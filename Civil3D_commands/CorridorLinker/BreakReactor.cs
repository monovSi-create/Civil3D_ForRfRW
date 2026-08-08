using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.DatabaseServices;

namespace Civil3D_commands.AssociativeBreaks
{
    /// <summary>
    /// Реактор-детектор. Сам ничего не меняет: при правке прокси помечает маркер "грязным",
    /// а в конце команды (или в Idle) сводит изменения через оркестратор.
    /// Во время собственных правок оркестратора (Suspended) события игнорируются.
    /// </summary>
    public class BreakReactor
    {
        private readonly BreakSession _session;
        // guid -> хэндл именно того прокси, который двигали (план или профиль).
        private readonly Dictionary<Guid, Handle> _dirtyStation = new Dictionary<Guid, Handle>();
        private readonly HashSet<Guid> _pendingDelete = new HashSet<Guid>();
        private bool _idleHooked;

        public BreakReactor(BreakSession session) { _session = session; }

        public void Subscribe(Document doc)
        {
            doc.Database.ObjectModified += OnObjectModified;
            doc.Database.ObjectErased += OnObjectErased;
            doc.CommandEnded += OnCommandEnded;
        }

        public void Unsubscribe(Document doc)
        {
            doc.Database.ObjectModified -= OnObjectModified;
            doc.Database.ObjectErased -= OnObjectErased;
            doc.CommandEnded -= OnCommandEnded;
            if (_idleHooked) { Application.Idle -= OnIdle; _idleHooked = false; }
        }

        private void OnObjectModified(object sender, ObjectEventArgs e)
        {
            if (_session.Suspended) return;            // собственные правки — пропускаем
            if (!(e.DBObject is Line ln)) return;      // нас интересуют только прокси-линии
            var guid = BreakProxyFactory.GetMarkerGuid(ln);
            if (guid == null) return;
            _dirtyStation[guid.Value] = ln.Handle;   // запоминаем, какой именно прокси двигали
            HookIdle();
        }

        private void OnObjectErased(object sender, ObjectErasedEventArgs e)
        {
            if (_session.Suspended) return;
            if (!e.Erased) return;
            if (!(e.DBObject is Line ln)) return;
            var guid = BreakProxyFactory.GetMarkerGuid(ln);
            if (guid != null) { _pendingDelete.Add(guid.Value); HookIdle(); }
        }

        private void OnCommandEnded(object sender, CommandEventArgs e) => Drain();
        private void OnIdle(object sender, EventArgs e) => Drain();

        private void HookIdle()
        {
            if (_idleHooked) return;       // Idle как страховка, если CommandEnded не сработает (чистая грипса)
            Application.Idle += OnIdle;
            _idleHooked = true;
        }

        private void Drain()
        {
            if (_session.Suspended) return;
            if (_dirtyStation.Count == 0 && _pendingDelete.Count == 0)
            {
                if (_idleHooked) { Application.Idle -= OnIdle; _idleHooked = false; }
                return;
            }

            var toMove = new Dictionary<Guid, Handle>(_dirtyStation); _dirtyStation.Clear();
            var toDelete = new List<Guid>(_pendingDelete); _pendingDelete.Clear();

            foreach (var id in toDelete) { _session.Manager.DeleteBreak(id); toMove.Remove(id); }

            foreach (var kv in toMove)
            {
                double? station = ReadStationFromProxy(kv.Key, kv.Value);
                if (station.HasValue && !double.IsNaN(station.Value))
                    _session.Manager.ApplyStationChange(kv.Key, station.Value);
            }

            if (_idleHooked) { Application.Idle -= OnIdle; _idleHooked = false; }
        }

        /// <summary>Текущий пикет, вычисленный из геометрии именно того прокси, что двигали.</summary>
        private double? ReadStationFromProxy(Guid id, Handle movedProxy)
        {
            var m = _session.Store.Get(id);
            if (m == null) return null;
            var db = Application.DocumentManager.MdiActiveDocument.Database;

            bool isPlan = movedProxy == m.PlanProxyHandle;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                double? st = StationFromLine(tr, db, movedProxy, isPlan, m);
                tr.Commit();
                return st;
            }
        }

        private double? StationFromLine(Transaction tr, Database db, Handle h, bool isPlan, StationMarker m)
        {
            if (h.Value == 0 || !db.TryGetObjectId(h, out ObjectId id) || id.IsNull || id.IsErased) return null;
            var ln = tr.GetObject(id, OpenMode.ForRead) as Line;
            if (ln == null) return null;
            Point3d mid = (ln.StartPoint + ln.EndPoint.GetAsVector()) / 2.0;

            if (isPlan)
            {
                if (!db.TryGetObjectId(m.AlignmentHandle, out ObjectId aid) || aid.IsNull) return null;
                var al = tr.GetObject(aid, OpenMode.ForRead) as Alignment;
                return al == null ? (double?)null : BreakProxyFactory.PlanPointToStation(al, mid);
            }
            else
            {
                if (!db.TryGetObjectId(m.ProfileViewHandle, out ObjectId pid) || pid.IsNull) return null;
                var pv = tr.GetObject(pid, OpenMode.ForRead) as ProfileView;
                return pv == null ? (double?)null : BreakProxyFactory.ProfilePointToStation(pv, mid);
            }
        }
    }
}
