using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Civil3D_commands.AssociativeBreaks;
using Civil3D_commands.Shared;
using AcAp = Autodesk.AutoCAD.ApplicationServices.Application;
using Alignment = Autodesk.Civil.DatabaseServices.Alignment;
using ObjectId = Autodesk.AutoCAD.DatabaseServices.ObjectId;
using ProfileView = Autodesk.Civil.DatabaseServices.ProfileView;
using SampleLine = Autodesk.Civil.DatabaseServices.SampleLine;
using SampleLineGroup = Autodesk.Civil.DatabaseServices.SampleLineGroup;
using Site = Autodesk.Civil.DatabaseServices.Site;

[assembly: CommandClass(typeof(Civil3D_commands.SectionMarks.SectionMarkCommands))]

namespace Civil3D_commands.SectionMarks
{
    /// <summary>
    /// Отметки разрезов на продольном профиле. Всё сводится к двум командам:
    /// RW_SECTIONMARKS — вынести сечения на вид, RW_SECTIONMARKEDIT — править
    /// их отображение. Подробности — README.md модуля.
    /// </summary>
    public class SectionMarkCommands
    {
        /// <summary>Включить оверрул и реактор. Идемпотентно, зовётся из каждой команды.</summary>
        internal static SectionMarkReactor Activate(Document doc)
        {
            SectionMarkGrips.Enable();
            return SectionMarkReactor.For(doc);
        }

        // =================================================================
        //  RW_SECTIONMARKS — вынести поперечные сечения на вид профиля
        // =================================================================
        [CommandMethod("RW_SECTIONMARKS")]
        public void CreateMarks()
        {
            Document doc = AcAp.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            Database db = doc.Database;
            SectionMarkReactor reactor = Activate(doc);

            var opt = new PromptEntityOptions("\nВыберите вид профиля, на который нужно вынести сечения");
            opt.SetRejectMessage("\nНужен ProfileView");
            opt.AddAllowedClass(typeof(ProfileView), true);
            PromptEntityResult res = ed.GetEntity(opt);
            if (res.Status != PromptStatus.OK) return;
            ObjectId pvId = res.ObjectId;

            // Откуда брать сечения. Указание в плане — основной путь: так выбирают
            // сечения соседней трассы; «Группа» — когда их много и щёлкать лень.
            var kw = new PromptKeywordOptions("\nСечения: указать в плане / группа линий выборки / своей трассы?");
            kw.Keywords.Add("Pick", "Указать", "Указать");
            kw.Keywords.Add("Group", "Группа", "Группа");
            kw.Keywords.Add("Own", "Своей", "Своей");
            kw.Keywords.Default = "Pick";   // глобальное имя, не подпись
            PromptResult kr = ed.GetKeywords(kw);
            if (kr.Status != PromptStatus.OK) return;

            List<ObjectId> lineIds;
            switch (kr.StringResult)
            {
                case "Group": lineIds = PickGroups(doc, pvId, false); break;
                case "Own": lineIds = PickGroups(doc, pvId, true); break;
                default: lineIds = PickInPlan(ed, db); break;
            }

            if (lineIds == null || lineIds.Count == 0)
            {
                ed.WriteMessage("\nЛиний выборки не выбрано.");
                return;
            }

            int created = 0, updated = 0;
            var missed = new List<string>();

            using (reactor.Suspend())
            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var pv = (ProfileView)tr.GetObject(pvId, OpenMode.ForRead);
                List<SectionMark> marks = SectionMarkStore.LoadAll(tr, db);
                string blockName = SectionMarkStore.GetDefaultBlock(tr, db);

                foreach (ObjectId slId in lineIds)
                {
                    var sl = tr.GetObject(slId, OpenMode.ForRead) as SampleLine;
                    if (sl == null) continue;

                    SectionMark m = marks.Find(x =>
                        x.SampleLineHandle == sl.Handle && x.ProfileViewHandle == pv.Handle);

                    bool isNew = m == null;
                    if (isNew)
                    {
                        m = new SectionMark
                        {
                            ProfileViewHandle = pv.Handle,
                            SampleLineHandle = sl.Handle,
                            BlockName = blockName
                        };
                    }

                    if (!SectionMarkBuilder.Sync(tr, db, m)) continue;

                    // Ничего не нарисовано: линия не пересекает трассу вида или
                    // пересечение за краем вида. Новую запись не заводим.
                    if (m.BlockHandle.Value == 0 && isNew)
                    {
                        missed.Add(sl.Name);
                        continue;
                    }

                    if (isNew) marks.Add(m);
                    SectionMarkStore.Save(tr, db, m);
                    if (isNew) created++; else updated++;
                }

                tr.Commit();
                reactor.Reindex(marks);
            }

            ed.WriteMessage(string.Format(
                "\nСечений вынесено: {0}, обновлено: {1}.", created, updated));
            if (missed.Count > 0)
                ed.WriteMessage(string.Format(
                    "\nНе пересекают трассу вида или лежат за его краем ({0}): {1}",
                    missed.Count, string.Join(", ", missed)));
            ed.WriteMessage("\nБлок, направление, удаление: RW_SECTIONMARKEDIT.");
        }

        /// <summary>Линии выборки, указанные мышью в плане.</summary>
        private static List<ObjectId> PickInPlan(Editor ed, Database db)
        {
            var so = new PromptSelectionOptions
            {
                MessageForAdding = "\nВыберите линии выборки (сечения) в плане"
            };
            PromptSelectionResult sr = ed.GetSelection(so);
            if (sr.Status != PromptStatus.OK) return null;

            var result = new List<ObjectId>();
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in sr.Value.GetObjectIds())
                    if (tr.GetObject(id, OpenMode.ForRead) is SampleLine) result.Add(id);

                tr.Commit();
            }

            return result;
        }

        /// <summary>
        /// Группы линий выборки в окне с флажками. ownOnly — только трассы вида.
        /// </summary>
        private static List<ObjectId> PickGroups(Document doc, ObjectId pvId, bool ownOnly)
        {
            Database db = doc.Database;

            var labels = new List<string>();
            var groups = new List<ObjectId>();

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var pv = (ProfileView)tr.GetObject(pvId, OpenMode.ForRead);

                foreach (ObjectId alId in AllAlignmentIds(tr))
                {
                    if (ownOnly && alId != pv.AlignmentId) continue;

                    var al = tr.GetObject(alId, OpenMode.ForRead) as Alignment;
                    if (al == null) continue;

                    foreach (ObjectId gId in al.GetSampleLineGroupIds())
                    {
                        var g = tr.GetObject(gId, OpenMode.ForRead) as SampleLineGroup;
                        if (g == null) continue;

                        labels.Add(string.Format("Трасса «{0}» — группа «{1}» ({2} лин.)",
                                                 al.Name, g.Name, g.GetSampleLineIds().Count));
                        groups.Add(gId);
                    }
                }

                tr.Commit();
            }

            if (groups.Count == 0)
            {
                doc.Editor.WriteMessage("\nГрупп линий выборки не найдено.");
                return null;
            }

            List<int> chosen = CheckListDialog.Show(
                "Группы линий выборки",
                "Отметьте группы, сечения которых нужно вынести на вид профиля.",
                labels, null);
            if (chosen == null) return null;

            var result = new List<ObjectId>();
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (int i in chosen)
                {
                    var g = (SampleLineGroup)tr.GetObject(groups[i], OpenMode.ForRead);
                    foreach (ObjectId id in g.GetSampleLineIds()) result.Add(id);
                }

                tr.Commit();
            }

            return result;
        }

        /// <summary>Все трассы чертежа, в том числе лежащие в площадках.</summary>
        private static List<ObjectId> AllAlignmentIds(Transaction tr)
        {
            var seen = new HashSet<ObjectId>();
            var result = new List<ObjectId>();
            CivilDocument civ = CivilApplication.ActiveDocument;

            foreach (ObjectId id in civ.GetAlignmentIds())
                if (seen.Add(id)) result.Add(id);

            foreach (ObjectId id in civ.GetSitelessAlignmentIds())
                if (seen.Add(id)) result.Add(id);

            foreach (ObjectId siteId in civ.GetSiteIds())
            {
                var site = tr.GetObject(siteId, OpenMode.ForRead) as Site;
                if (site == null) continue;

                foreach (ObjectId id in site.GetAlignmentIds())
                    if (seen.Add(id)) result.Add(id);
            }

            return result;
        }

        // =================================================================
        //  RW_SECTIONMARKEDIT — редактировать отображение сечений
        // =================================================================
        [CommandMethod("RW_SECTIONMARKEDIT")]
        public void EditMarks()
        {
            Document doc = AcAp.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            SectionMarkReactor reactor = Activate(doc);

            while (true)
            {
                var kw = new PromptKeywordOptions(
                    "\nОтображение сечений: блок / развернуть стрелки / удалить / обновить (Enter — выйти)");
                kw.AllowNone = true;
                kw.Keywords.Add("Block", "Блок", "Блок");
                kw.Keywords.Add("Flip", "Развернуть", "Развернуть");
                kw.Keywords.Add("Delete", "Удалить", "Удалить");
                kw.Keywords.Add("Refresh", "Обновить", "Обновить");

                PromptResult res = ed.GetKeywords(kw);
                if (res.Status != PromptStatus.Keyword) return;

                switch (res.StringResult)
                {
                    case "Block": ChooseBlockCore(doc, reactor); break;
                    case "Flip": FlipCore(doc, reactor); break;
                    case "Delete": DeleteCore(doc, reactor); break;
                    case "Refresh": RefreshCore(doc, reactor); break;
                }
            }
        }

        /// <summary>Блок отметки из блоков чертежа: ко всем отметкам и по умолчанию.</summary>
        private static void ChooseBlockCore(Document doc, SectionMarkReactor reactor)
        {
            Editor ed = doc.Editor;
            Database db = doc.Database;

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

        /// <summary>Развернуть стрелки отметки, указанной по любой её части.</summary>
        private static void FlipCore(Document doc, SectionMarkReactor reactor)
        {
            Editor ed = doc.Editor;
            Database db = doc.Database;

            PromptEntityResult res = ed.GetEntity("\nВыберите блок, чёрточку или стрелку отметки");
            if (res.Status != PromptStatus.OK) return;

            bool done = false;
            using (reactor.Suspend())
            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                List<SectionMark> marks = SectionMarkStore.LoadAll(tr, db);
                SectionMark m = FindByDrawnPart(marks, res.ObjectId.Handle);

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

        /// <summary>
        /// Удалить отметку, указанную по любой её части, или все отметки вида профиля.
        /// Линия выборки при этом остаётся: удаляется только вынос на профиль.
        /// </summary>
        private static void DeleteCore(Document doc, SectionMarkReactor reactor)
        {
            Editor ed = doc.Editor;
            Database db = doc.Database;

            var opt = new PromptEntityOptions("\nВыберите блок или чёрточку отметки [Все отметки вида профиля]");
            opt.Keywords.Add("View", "Вид", "Вид");
            opt.AllowNone = false;
            PromptEntityResult res = ed.GetEntity(opt);

            Handle pvHandle = new Handle(0);
            Handle picked = new Handle(0);

            if (res.Status == PromptStatus.Keyword)
            {
                var po = new PromptEntityOptions("\nВыберите вид профиля");
                po.SetRejectMessage("\nНужен ProfileView");
                po.AddAllowedClass(typeof(ProfileView), true);
                PromptEntityResult pr = ed.GetEntity(po);
                if (pr.Status != PromptStatus.OK) return;
                pvHandle = pr.ObjectId.Handle;
            }
            else if (res.Status == PromptStatus.OK)
            {
                picked = res.ObjectId.Handle;
            }
            else
            {
                return;
            }

            int removed = 0;
            using (reactor.Suspend())
            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                List<SectionMark> marks = SectionMarkStore.LoadAll(tr, db);

                List<SectionMark> victims = pvHandle.Value != 0
                    ? marks.Where(x => x.ProfileViewHandle == pvHandle).ToList()
                    : new List<SectionMark>();

                if (picked.Value != 0)
                {
                    SectionMark one = FindByDrawnPart(marks, picked);
                    if (one != null) victims.Add(one);
                }

                foreach (SectionMark m in victims)
                {
                    SectionMarkBuilder.EraseDrawn(tr, db, m);
                    SectionMarkStore.Remove(tr, db, m);
                    marks.Remove(m);
                    removed++;
                }

                tr.Commit();
                reactor.Reindex(marks);
            }

            ed.WriteMessage(removed > 0
                ? string.Format("\nУдалено отметок: {0}.", removed)
                : "\nЭто не часть отметки разреза.");
        }

        /// <summary>Перерисовать все отметки: вернуть стёртое, убрать осиротевшие.</summary>
        private static void RefreshCore(Document doc, SectionMarkReactor reactor)
        {
            Editor ed = doc.Editor;
            Database db = doc.Database;

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
                "\nОтметок перерисовано: {0}, удалено осиротевших: {1}.", ok, removed));
        }

        /// <summary>Отметка, чьей нарисованной частью (блок, чёрточка, стрелка) является объект.</summary>
        private static SectionMark FindByDrawnPart(List<SectionMark> marks, Handle picked)
        {
            return marks.Find(x =>
            {
                foreach (var link in x.Links())
                    if (link.Key == picked && link.Value <= MarkPart.BotArrow) return true;
                return false;
            });
        }

        // =================================================================
        //  RW_SECTIONMARKDIAG — состояние модуля
        // =================================================================
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
