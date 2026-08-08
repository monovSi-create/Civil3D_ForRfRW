using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using AcAp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: ExtensionApplication(typeof(Civil3D_commands.AssociativeBreaks.BreakCommands))]
[assembly: CommandClass(typeof(Civil3D_commands.AssociativeBreaks.BreakCommands))]

namespace Civil3D_commands.AssociativeBreaks
{
    public class BreakCommands : IExtensionApplication
    {
        private static BreakGripOverrule _gripOverrule;

        // ------------------------------------------------------------------
        //  ЖИЗНЕННЫЙ ЦИКЛ
        // ------------------------------------------------------------------
        public void Initialize()
        {
            _gripOverrule = new BreakGripOverrule();
            Overrule.AddOverrule(RXObject.GetClass(typeof(Line)), _gripOverrule, false);
            Overrule.Overruling = true;

            AcAp.DocumentManager.DocumentActivated += (s, e) => AttachTo(e.Document);
            var doc = AcAp.DocumentManager.MdiActiveDocument;
            if (doc != null) AttachTo(doc);
        }

        public void Terminate()
        {
            if (_gripOverrule != null)
            {
                Overrule.RemoveOverrule(RXObject.GetClass(typeof(Line)), _gripOverrule);
                _gripOverrule.Dispose();
                _gripOverrule = null;
            }
        }

        private static void AttachTo(Document doc)
        {
            if (doc == null) return;
            BreakSession.Current?.Detach();
            var session = BreakSession.Attach(doc);
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                PropertySetSupport.EnsureEditPsd(doc.Database);
                PropertySetSupport.EnsureMarkerPsd(doc.Database);
                tr.Commit();
            }
            BreakProxyFactory.EnsureRegApp(doc.Database);
        }

        // ------------------------------------------------------------------
        //  МАСТЕР: создать профиль и связать с коридором
        // ------------------------------------------------------------------
        [CommandMethod("RW_LINKPROFILECORRIDOR")]
        public void LinkProfileCorridor()
        {
            var doc = AcAp.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            var db = doc.Database;
            var session = BreakSession.Current;

            // 1) Вид профиля
            var pvOpt = new PromptEntityOptions("\nВыберите вид продольного профиля");
            pvOpt.SetRejectMessage("\nНужен вид профиля");
            pvOpt.AddAllowedClass(typeof(ProfileView), true);
            var pvRes = ed.GetEntity(pvOpt);
            if (pvRes.Status != PromptStatus.OK) return;

            // 2) Отметка
            var elRes = ed.GetDouble("\nОтметка для создания продольного профиля");
            if (elRes.Status != PromptStatus.OK) return;

            // 3) Коридор
            var corrOpt = new PromptEntityOptions("\nВыберите коридор для связи");
            corrOpt.SetRejectMessage("\nНужен коридор");
            corrOpt.AddAllowedClass(typeof(Corridor), true);
            var corrRes = ed.GetEntity(corrOpt);
            if (corrRes.Status != PromptStatus.OK) return;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var pv = (ProfileView)tr.GetObject(pvRes.ObjectId, OpenMode.ForRead);
                ObjectId alignmentId = pv.AlignmentId;
                var alignment = (Alignment)tr.GetObject(alignmentId, OpenMode.ForRead);

                // Создаём плоский профиль на заданной отметке.
                ObjectId profId = Profile.CreateByLayout(
                    "Профиль-основание_" + DateTime.Now.Ticks,
                    alignmentId, db.LayerZero,
                    ObjectId.Null, ObjectId.Null);            // АДАПТ: стиль/набор меток
                var profile = (Profile)tr.GetObject(profId, OpenMode.ForWrite);
                profile.PVIs.AddPVI(alignment.StartingStation, elRes.Value);
                profile.PVIs.AddPVI(alignment.EndingStation, elRes.Value);

                // Свойство-переключатель режима редактирования на профиль.
                PropertySetSupport.Attach(tr, profId, PropertySetSupport.EnsureEditPsd(db));

                // Запоминаем активную связь.
                session.ActiveLink = new StationMarker
                {
                    ProfileHandle = profile.Handle,
                    ProfileViewHandle = pv.Handle,
                    AlignmentHandle = alignment.Handle,
                    CorridorHandle = corrRes.ObjectId.Handle
                };
                tr.Commit();
                ed.WriteMessage("\nПрофиль создан и связан с коридором. " +
                                "Включите «Режим редактирования» в свойствах профиля и используйте RW_CREATEBREAK.");
            }
        }

        // ------------------------------------------------------------------
        //  РЕЖИМ РЕДАКТИРОВАНИЯ (если переключатель в палитре не читается реактором)
        // ------------------------------------------------------------------
        [CommandMethod("RW_EDITMODE")]
        public void ToggleEditMode()
        {
            var doc = AcAp.DocumentManager.MdiActiveDocument;
            var session = BreakSession.Current;
            var link = session?.ActiveLink;
            if (link == null) { doc.Editor.WriteMessage("\nСначала RW_LINKPROFILECORRIDOR"); return; }

            bool now = !session.IsEditMode(link.ProfileHandle);
            session.SetEditMode(link.ProfileHandle, now);
            doc.Editor.WriteMessage($"\nРежим редактирования: {(now ? "ВКЛ" : "ВЫКЛ")}");
            doc.Editor.Regen();
        }

        // ------------------------------------------------------------------
        //  СОЗДАТЬ РАЗРЫВ
        // ------------------------------------------------------------------
        [CommandMethod("RW_CREATEBREAK")]
        public void CreateBreak()
        {
            var doc = AcAp.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            var session = BreakSession.Current;
            var link = session?.ActiveLink;
            if (link == null) { ed.WriteMessage("\nСначала RW_LINKPROFILECORRIDOR"); return; }

            // Точка в виде профиля -> пикет.
            var ptRes = ed.GetPoint("\nУкажите положение разрыва в виде профиля");
            if (ptRes.Status != PromptStatus.OK) return;

            double station;
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var pv = (ProfileView)tr.GetObject(Resolve(doc.Database, link.ProfileViewHandle), OpenMode.ForRead);
                station = BreakProxyFactory.ProfilePointToStation(pv, ptRes.Value);
                tr.Commit();
            }

            // Ступень?
            var stepKw = new PromptKeywordOptions("\nЭто ступень профиля?");
            stepKw.Keywords.Add("Да"); stepKw.Keywords.Add("Нет");
            stepKw.Keywords.Default = "Да";
            var stepRes = ed.GetKeywords(stepKw);
            if (stepRes.Status != PromptStatus.OK) return;
            bool isStep = stepRes.StringResult == "Да";

            double stepH = 0;
            if (isStep)
            {
                var hRes = ed.GetDouble("\nВысота ступени (знак: + вверх, - вниз по ходу пикета)");
                if (hRes.Status != PromptStatus.OK) return;
                stepH = hRes.Value;
            }

            var marker = new StationMarker
            {
                Station = station,
                IsStep = isStep,
                StepHeight = stepH,
                Layer = "0",                       // АДАПТ: слой по умолчанию
                ProfileHandle = link.ProfileHandle,
                ProfileViewHandle = link.ProfileViewHandle,
                AlignmentHandle = link.AlignmentHandle,
                CorridorHandle = link.CorridorHandle
            };

            Guid id = session.Manager.CreateBreak(marker);

            // Записать свойства в набор прокси (для палитры).
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var m = session.Store.Get(id);
                ObjectId psd = PropertySetSupport.EnsureMarkerPsd(doc.Database);
                foreach (var h in new[] { m.ProfileProxyHandle, m.PlanProxyHandle })
                {
                    ObjectId pid = Resolve(doc.Database, h);
                    if (pid.IsNull) continue;
                    PropertySetSupport.Attach(tr, pid, psd);
                    PropertySetSupport.WriteMarkerProps(tr, pid, m);
                }
                tr.Commit();
            }
            ed.WriteMessage($"\nРазрыв создан на пикете {station:F3}.");
        }

        // ------------------------------------------------------------------
        //  УДАЛИТЬ РАЗРЫВ
        // ------------------------------------------------------------------
        [CommandMethod("RW_DELETEBREAK")]
        public void DeleteBreak()
        {
            var doc = AcAp.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            var session = BreakSession.Current;
            if (session == null) return;

            var opt = new PromptEntityOptions("\nВыберите прокси разрыва для удаления");
            opt.SetRejectMessage("\nНужен прокси разрыва");
            opt.AddAllowedClass(typeof(Line), false);
            var res = ed.GetEntity(opt);
            if (res.Status != PromptStatus.OK) return;

            Guid? id;
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var ln = (Line)tr.GetObject(res.ObjectId, OpenMode.ForRead);
                id = BreakProxyFactory.GetMarkerGuid(ln);
                tr.Commit();
            }
            if (id == null) { ed.WriteMessage("\nЭто не прокси разрыва"); return; }

            session.Manager.DeleteBreak(id.Value);
            ed.WriteMessage("\nРазрыв удалён.");
        }

        [CommandMethod("RW_SAVEBREAKS")]
        public void SaveBreaks() =>
            BreakSession.Current?.Store.SaveToDatabase(AcAp.DocumentManager.MdiActiveDocument.Database);

        private static ObjectId Resolve(Database db, Handle h) =>
            (h.Value != 0 && db.TryGetObjectId(h, out ObjectId id)) ? id : ObjectId.Null;
    }
}
