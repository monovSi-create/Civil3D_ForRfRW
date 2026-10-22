using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Civil3D_commands.AssociativeBreaks;
using Civil3D_commands.Shared;
using AcAp = Autodesk.AutoCAD.ApplicationServices.Application;
using Alignment = Autodesk.Civil.DatabaseServices.Alignment;
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;
using ObjectId = Autodesk.AutoCAD.DatabaseServices.ObjectId;
using ProfileView = Autodesk.Civil.DatabaseServices.ProfileView;
using SampleLine = Autodesk.Civil.DatabaseServices.SampleLine;
using SampleLineGroup = Autodesk.Civil.DatabaseServices.SampleLineGroup;
using SectionView = Autodesk.Civil.DatabaseServices.SectionView;

[assembly: CommandClass(typeof(Civil3D_commands.SectionMarks.SectionMarkCommands))]

namespace Civil3D_commands.SectionMarks
{
    /// <summary>
    /// Отметки разрезов на продольном профиле. Подробности — README.md модуля.
    /// </summary>
    public class SectionMarkCommands
    {
        /// <summary>Включить оверрул и реактор. Идемпотентно, зовётся из каждой команды.</summary>
        internal static SectionMarkReactor Activate(Document doc)
        {
            SectionMarkGrips.Enable();
            return SectionMarkReactor.For(doc);
        }

        // -----------------------------------------------------------------
        //  RW_SECTIONMARKS — создать/обновить отметки по видам сечений
        // -----------------------------------------------------------------
        [CommandMethod("RW_SECTIONMARKS")]
        public void CreateMarks()
        {
            Document doc = AcAp.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            Database db = doc.Database;
            SectionMarkReactor reactor = Activate(doc);

            var opt = new PromptEntityOptions("\nВыберите вид продольного профиля");
            opt.SetRejectMessage("\nНужен ProfileView");
            opt.AddAllowedClass(typeof(ProfileView), true);
            PromptEntityResult res = ed.GetEntity(opt);
            if (res.Status != PromptStatus.OK) return;

            int created = 0, updated = 0, noViews = 0;
            var names = new List<string>();

            using (reactor.Suspend())
            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var pv = (ProfileView)tr.GetObject(res.ObjectId, OpenMode.ForRead);
                var al = tr.GetObject(pv.AlignmentId, OpenMode.ForRead) as Alignment;
                if (al == null)
                {
                    ed.WriteMessage("\nУ вида профиля нет трассы.");
                    return;
                }

                List<SectionMark> marks = SectionMarkStore.LoadAll(tr, db);
                string blockName = SectionMarkStore.GetDefaultBlock(tr, db);

                foreach (ObjectId groupId in al.GetSampleLineGroupIds())
                {
                    var group = tr.GetObject(groupId, OpenMode.ForRead) as SampleLineGroup;
                    if (group == null) continue;

                    foreach (ObjectId slId in group.GetSampleLineIds())
                    {
                        var sl = tr.GetObject(slId, OpenMode.ForRead) as SampleLine;
                        if (sl == null) continue;
                        if (sl.Station < pv.StationStart || sl.Station > pv.StationEnd) continue;

                        ObjectId svId = ObjectId.Null;
                        foreach (ObjectId id in sl.GetSectionViewIds()) { svId = id; break; }

                        if (svId.IsNull)
                        {
                            noViews++;
                            continue;
                        }

                        SectionMark m = marks.Find(x =>
                            x.SectionViewHandle == svId.Handle &&
                            x.ProfileViewHandle == pv.Handle);

                        bool isNew = m == null;
                        if (isNew)
                        {
                            m = new SectionMark
                            {
                                ProfileViewHandle = pv.Handle,
                                SampleLineHandle = sl.Handle,
                                SectionViewHandle = svId.Handle,
                                BlockName = blockName
                            };
                            marks.Add(m);
                        }

                        if (!SectionMarkBuilder.Sync(tr, db, m))
                        {
                            marks.Remove(m);
                            continue;
                        }

                        SectionMarkStore.Save(tr, db, m);
                        if (isNew) created++; else updated++;
                        names.Add(m.Name);
                    }
                }

                tr.Commit();
                reactor.Reindex(marks);
            }

            ed.WriteMessage(string.Format(
                "\nОтметок разрезов: создано {0}, обновлено {1}. {2}",
                created, updated,
                noViews > 0
                    ? string.Format("Линий выборки без вида сечения: {0} (пропущены).", noViews)
                    : ""));
            if (created + updated == 0)
                ed.WriteMessage("\nНа этом профиле нет линий выборки с видами сечений.");
        }

        // -----------------------------------------------------------------
        //  RW_SECTIONMARKBLOCK — выбрать блок отметки из блоков чертежа
        // -----------------------------------------------------------------
        [CommandMethod("RW_SECTIONMARKBLOCK")]
        public void ChooseBlock()
        {
            Document doc = AcAp.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            Database db = doc.Database;
            SectionMarkReactor reactor = Activate(doc);

            List<string> blocks;
            string current;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                blocks = SectionMarkBuilder.ListBlocks(tr, db);
                current = SectionMarkStore.GetDefaultBlock(tr, db);
                tr.Commit();
            }

            int pre = blocks.FindIndex(n => string.Equals(n, current, StringComparison.OrdinalIgnoreCase));
            int pick = PickListDialog.Show(
                "Блок отметки разреза",
                "Блок берётся из чертежа. Название сечения пишется в его атрибут (NAME, а если такого нет — в первый).",
                blocks, pre);
            if (pick < 0) return;

            string chosen = blocks[pick];
            int changed = 0;

            using (reactor.Suspend())
            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                SectionMarkStore.SetDefaultBlock(tr, db, chosen);

                List<SectionMark> marks = SectionMarkStore.LoadAll(tr, db);
                foreach (SectionMark m in marks)
                {
                    m.BlockName = chosen;
                    if (SectionMarkBuilder.Sync(tr, db, m)) { SectionMarkStore.Save(tr, db, m); changed++; }
                }

                tr.Commit();
                reactor.Reindex(marks);
            }

            ed.WriteMessage(string.Format(
                "\nБлок «{0}» назначен по умолчанию; переставлено отметок: {1}.", chosen, changed));
        }

        // -----------------------------------------------------------------
        //  RW_SECTIONMARKFLIP — развернуть стрелки отметки
        // -----------------------------------------------------------------
        [CommandMethod("RW_SECTIONMARKFLIP")]
        public void Flip()
        {
            Document doc = AcAp.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            Database db = doc.Database;
            SectionMarkReactor reactor = Activate(doc);

            var opt = new PromptEntityOptions("\nВыберите блок, чёрточку или стрелку отметки разреза");
            PromptEntityResult res = ed.GetEntity(opt);
            if (res.Status != PromptStatus.OK) return;

            bool done = false;
            using (reactor.Suspend())
            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Handle picked = res.ObjectId.Handle;
                List<SectionMark> marks = SectionMarkStore.LoadAll(tr, db);

                SectionMark m = marks.Find(x =>
                {
                    foreach (var link in x.Links())
                        if (link.Key == picked && link.Value >= MarkPart.Block && link.Value <= MarkPart.BotArrow)
                            return true;
                    return false;
                });

                if (m != null)
                {
                    m.Reverse = !m.Reverse;
                    if (SectionMarkBuilder.Sync(tr, db, m)) { SectionMarkStore.Save(tr, db, m); done = true; }
                }

                tr.Commit();
                reactor.Reindex(marks);
            }

            ed.WriteMessage(done ? "\nНаправление взгляда изменено." : "\nЭто не часть отметки разреза.");
        }

        // -----------------------------------------------------------------
        //  RW_SECTIONMARKREFRESH — перерисовать все отметки
        // -----------------------------------------------------------------
        [CommandMethod("RW_SECTIONMARKREFRESH")]
        public void Refresh()
        {
            Document doc = AcAp.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            Database db = doc.Database;
            SectionMarkReactor reactor = Activate(doc);

            int ok = 0, removed = 0;
            using (reactor.Suspend())
            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                List<SectionMark> marks = SectionMarkStore.LoadAll(tr, db);
                foreach (SectionMark m in new List<SectionMark>(marks))
                {
                    SectionMarkBuilder.ReadTicks(tr, db, m);

                    if (SectionMarkBuilder.Sync(tr, db, m))
                    {
                        SectionMarkStore.Save(tr, db, m);
                        ok++;
                    }
                    else
                    {
                        SectionMarkBuilder.EraseDrawn(tr, db, m);
                        SectionMarkStore.Remove(tr, db, m);
                        marks.Remove(m);
                        removed++;
                    }
                }

                tr.Commit();
                reactor.Reindex(marks);
            }

            ed.WriteMessage(string.Format(
                "\nОтметок разрезов перерисовано: {0}, удалено осиротевших: {1}.", ok, removed));
        }

        // -----------------------------------------------------------------
        //  RW_SECTIONMARKDIAG — состояние модуля
        // -----------------------------------------------------------------
        [CommandMethod("RW_SECTIONMARKDIAG")]
        public void Diag()
        {
            Document doc = AcAp.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;

            // Намеренно без Activate: диагностика не должна сама включать то, о чём спрашивает.
            ed.WriteMessage(string.Format(
                "\nОверрул чёрточек включён: {0}" +
                "\nСобытий по отметкам: {1}, синхронизаций: {2}, удалений: {3}" +
                "\nПоследняя ошибка: {4}",
                SectionMarkGrips.IsRegistered,
                SectionMarkReactor.Events, SectionMarkReactor.Syncs, SectionMarkReactor.Deletes,
                SectionMarkReactor.LastError ?? "(нет)"));

            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                ed.WriteMessage("\nОтметок в чертеже: " +
                                SectionMarkStore.LoadAll(tr, doc.Database).Count);
                ed.WriteMessage("\nБлок по умолчанию: " +
                                SectionMarkStore.GetDefaultBlock(tr, doc.Database));
                tr.Commit();
            }
        }
    }
}
