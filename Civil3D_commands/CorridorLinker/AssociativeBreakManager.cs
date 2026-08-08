using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.DatabaseServices;

namespace Civil3D_commands.AssociativeBreaks
{
    /// <summary>
    /// Оркестратор. ВСЕ изменения (грипса, палитра, команды) проходят только через него,
    /// под флагом BreakSession.Suspend(), чтобы реактор не реагировал на собственные правки.
    /// Порядок: правка модели -> профиль (если ступень) -> области -> прокси -> один Rebuild.
    /// </summary>
    public class AssociativeBreakManager
    {
        private readonly BreakSession _session;
        public AssociativeBreakManager(BreakSession session) { _session = session; }

        private static Document Doc =>
            Application.DocumentManager.MdiActiveDocument;

        /// <summary>Создать новый разрыв на профиле.</summary>
        public Guid CreateBreak(StationMarker template)
        {
            using (Doc.LockDocument())            // 2024: правки из Idle/реактора требуют блокировки
            using (_session.Suspend())
            using (var tr = Doc.Database.TransactionManager.StartTransaction())
            {
                var profile = (Profile)tr.GetObject(Resolve(template.ProfileHandle), OpenMode.ForWrite);
                var pv = (ProfileView)tr.GetObject(Resolve(template.ProfileViewHandle), OpenMode.ForRead);
                var alignment = (Alignment)tr.GetObject(Resolve(template.AlignmentHandle), OpenMode.ForRead);

                // Кламп ещё на этапе создания.
                Baseline bl = _session.GetBaseline(tr, template);
                ClampToNeighbors(template, bl);

                template.BaseElevation = ProfileGeometryOps.ElevationAt(profile, template.Station);

                if (template.IsStep)
                    ProfileGeometryOps.InsertStep(profile, template.Station, template.StepHeight);

                BreakProxyFactory.CreateProxies(tr, Doc.Database, template, pv, alignment);
                _session.Store.Add(template);

                ResyncRegions(tr, template);
                RebuildCorridor(tr, template);

                tr.Commit();
            }
            _session.Store.SaveToDatabase(Doc.Database);
            return template.Id;
        }

        /// <summary>Удалить разрыв.</summary>
        public void DeleteBreak(Guid id)
        {
            var m = _session.Store.Get(id);
            if (m == null) return;

            using (Doc.LockDocument())
            using (_session.Suspend())
            using (var tr = Doc.Database.TransactionManager.StartTransaction())
            {
                var profile = (Profile)tr.GetObject(Resolve(m.ProfileHandle), OpenMode.ForWrite);

                if (m.IsStep)
                    ProfileGeometryOps.RemoveStep(profile, m.Station, m.StepHeight);

                EraseIfValid(tr, m.ProfileProxyHandle);
                EraseIfValid(tr, m.PlanProxyHandle);
                _session.Store.Remove(id);

                ResyncRegions(tr, m);
                RebuildCorridor(tr, m);
                tr.Commit();
            }
            _session.Store.SaveToDatabase(Doc.Database);
        }

        /// <summary>
        /// Применить новый пикет к маркеру (вызывается реактором после грипса-перемещения
        /// или при правке поля "Пикет" в палитре). Делает кламп, двигает ступень и области.
        /// </summary>
        public void ApplyStationChange(Guid id, double newStation)
        {
            var m = _session.Store.Get(id);
            if (m == null) return;

            using (Doc.LockDocument())
            using (_session.Suspend())
            using (var tr = Doc.Database.TransactionManager.StartTransaction())
            {
                var profile = (Profile)tr.GetObject(Resolve(m.ProfileHandle), OpenMode.ForWrite);
                var pv = (ProfileView)tr.GetObject(Resolve(m.ProfileViewHandle), OpenMode.ForRead);
                var alignment = (Alignment)tr.GetObject(Resolve(m.AlignmentHandle), OpenMode.ForRead);

                Baseline bl = _session.GetBaseline(tr, m);

                double old = m.Station;
                m.Station = newStation;
                ClampToNeighbors(m, bl);
                if (Math.Abs(m.Station - old) < 1e-6) { tr.Commit(); return; }

                if (m.IsStep)
                {
                    ProfileGeometryOps.MoveStep(profile, old, m.Station, m.StepHeight);
                    m.BaseElevation = ProfileGeometryOps.ElevationAt(profile, m.Station);
                }

                BreakProxyFactory.UpdateProxyGeometry(tr, m, pv, alignment);
                ResyncRegions(tr, m);
                RebuildCorridor(tr, m);
                tr.Commit();
            }
            _session.Store.SaveToDatabase(Doc.Database);
        }

        // ----------------------------------------------------------------------

        private void ClampToNeighbors(StationMarker m, Baseline bl)
        {
            double bStart = bl?.StartStation ?? 0;
            double bEnd = bl?.EndStation ?? m.Station;
            var (min, max) = _session.Store.GetMoveBounds(m, BreakGripOverrule.Buffer, bStart, bEnd);
            m.Station = Math.Min(Math.Max(m.Station, min), max);
        }

        private void ResyncRegions(Transaction tr, StationMarker m)
        {
            Baseline bl = _session.GetBaseline(tr, m);
            if (bl == null) return;

            var stations = _session.Store.ForProfile(m.ProfileHandle)
                                   .Select(x => x.Station).ToList();
            var asmByStart = new Dictionary<double, ObjectId>();
            ProfileGeometryOps.ResyncRegions(bl, stations, asmByStart);
        }

        private void RebuildCorridor(Transaction tr, StationMarker m)
        {
            var corridor = _session.GetCorridor(tr, m);
            corridor?.Rebuild(); // один Rebuild на всю операцию
        }

        private static ObjectId Resolve(Handle h)
        {
            var db = Doc.Database;
            return (h.Value != 0 && db.TryGetObjectId(h, out ObjectId id)) ? id : ObjectId.Null;
        }

        private static void EraseIfValid(Transaction tr, Handle h)
        {
            ObjectId id = Resolve(h);
            if (!id.IsNull && !id.IsErased)
                tr.GetObject(id, OpenMode.ForWrite).Erase();
        }
    }
}
