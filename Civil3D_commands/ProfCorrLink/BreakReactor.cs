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
        private int _commandDepth; // счётчик вложенных команд; Idle не дренирует пока > 0

        public BreakReactor(BreakSession session) { _session = session; }

        public void Subscribe(Document doc)
        {
            doc.Database.ObjectModified += OnObjectModified;
            doc.Database.ObjectErased += OnObjectErased;
            doc.CommandWillStart += OnCommandBegan;
            doc.CommandEnded += OnCommandEnded;
            doc.CommandCancelled += OnCommandCancelled;
            doc.CommandFailed += OnCommandFailed;
        }

        public void Unsubscribe(Document doc)
        {
            doc.Database.ObjectModified -= OnObjectModified;
            doc.Database.ObjectErased -= OnObjectErased;
            doc.CommandWillStart -= OnCommandBegan;
            doc.CommandEnded -= OnCommandEnded;
            doc.CommandCancelled -= OnCommandCancelled;
            doc.CommandFailed -= OnCommandFailed;
            if (_idleHooked) { Application.Idle -= OnIdle; _idleHooked = false; }
        }

        private void OnCommandBegan(object sender, CommandEventArgs e)   => _commandDepth++;
        private void OnCommandCancelled(object sender, CommandEventArgs e) { if (_commandDepth > 0) _commandDepth--; Drain(); }
        private void OnCommandFailed(object sender, CommandEventArgs e)    { if (_commandDepth > 0) _commandDepth--; }

        private void OnObjectModified(object sender, ObjectEventArgs e)
        {
            if (_session.Suspended) return;            // собственные правки — пропускаем
            if (!(e.DBObject is Line ln)) return;      // нас интересуют только прокси-линии
            var m = OwnerOf(ln);
            if (m == null) return;
            _dirtyStation[m.Id] = ln.Handle;         // запоминаем, какой именно прокси двигали
            HookIdle();
        }

        private void OnObjectErased(object sender, ObjectErasedEventArgs e)
        {
            if (_session.Suspended) return;
            if (!e.Erased) return;
            if (!(e.DBObject is Line ln)) return;
            var m = OwnerOf(ln);
            if (m != null) { _pendingDelete.Add(m.Id); HookIdle(); }
        }

        /// <summary>
        /// Маркер, которому принадлежит эта линия, или null.
        ///
        /// Guid в XData — только предварительный фильтр: у КОПИИ прокси он ровно
        /// тот же самый, и раньше удаление копии удаляло настоящий разрыв вместе
        /// с перестроением коридора. Владелец определяется по хэндлу, записанному
        /// в самой модели: копия там не числится и остаётся обычным отрезком.
        /// </summary>
        private StationMarker OwnerOf(Line ln)
        {
            if (BreakProxyFactory.GetMarkerGuid(ln) == null) return null;
            if (ln.ObjectId.IsNull) return null;

            var m = _session.Store.GetByProxy(ln.ObjectId.Handle);
            return m != null && m.OwnsProxy(ln.ObjectId.Handle) ? m : null;
        }

        private void OnCommandEnded(object sender, CommandEventArgs e)
        {
            if (_commandDepth > 0) _commandDepth--;
            Drain();
        }

        private void OnIdle(object sender, EventArgs e)
        {
            // Idle — только страховка (например UNDO снаружи команды).
            // Пока активна хоть одна команда — не дренируем.
            if (_commandDepth > 0) return;
            Drain();
        }

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
                if (!station.HasValue || double.IsNaN(station.Value)) continue;
                try
                {
                    _session.Manager.ApplyStationChange(kv.Key, station.Value);
                }
                catch (System.Exception ex)
                {
                    // Выводим ошибку в командную строку — не роняем AutoCAD.
                    try
                    {
                        Application.DocumentManager.MdiActiveDocument?
                            .Editor.WriteMessage(
                                $"\n[RW_Break] Ошибка обновления: {ex.Message}\n{ex.StackTrace}");
                    }
                    catch { }
                }
            }

            if (_idleHooked) { Application.Idle -= OnIdle; _idleHooked = false; }
        }

        /// <summary>Текущий пикет, вычисленный из геометрии именно того прокси, что двигали.</summary>
        private double? ReadStationFromProxy(Guid id, Handle movedProxy)
        {
            var m = _session.Store.Get(id);
            if (m == null) return null;

            // Линия обязана быть одним из двух зарегистрированных прокси: иначе
            // ни одна из веток ниже не подходит и пикет получится из координат
            // не того вида (план прочитали бы через ProfileView).
            if (!m.OwnsProxy(movedProxy)) return null;

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
