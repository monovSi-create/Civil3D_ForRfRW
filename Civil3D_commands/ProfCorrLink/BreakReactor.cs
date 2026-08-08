using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Aec.PropertyData.DatabaseServices;
using Autodesk.Civil.DatabaseServices;
using Civil3D_commands.Shared;

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
        // Наборы характеристик, тронутые в палитре свойств. Владелец ищется
        // не здесь, а в Drain: открывать транзакцию внутри ObjectModified не стоит.
        private readonly HashSet<ObjectId> _dirtyProps = new HashSet<ObjectId>();

        // --- Счётчики для RW_BREAKDIAG. Путь через палитру в чертеже не проверен,
        // --- и без них непонятно, на каком шаге он обрывается.
        public static int PropEvents;       // сколько правок набора вообще замечено
        public static int PropMatched;      // из них опознано как набор нашего прокси
        public static int PropApplied;      // из них привело к перестроению
        public static int PropEditMatched;  // правок набора «Режим редактирования» на профиле
        public static int PropEditApplied;  // из них изменивших режим
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

            // Правка поля в палитре свойств меняет не прокси, а его набор
            // характеристик — отдельный объект, и приходит он сюда же.
            if (e.DBObject is PropertySet ps)
            {
                PropEvents++;
                if (!ps.ObjectId.IsNull) { _dirtyProps.Add(ps.ObjectId); HookIdle(); }
                return;
            }

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
            if (_dirtyStation.Count == 0 && _pendingDelete.Count == 0 && _dirtyProps.Count == 0)
            {
                if (_idleHooked) { Application.Idle -= OnIdle; _idleHooked = false; }
                return;
            }

            var toMove = new Dictionary<Guid, Handle>(_dirtyStation); _dirtyStation.Clear();
            var toDelete = new List<Guid>(_pendingDelete); _pendingDelete.Clear();
            var toRead = new List<ObjectId>(_dirtyProps); _dirtyProps.Clear();

            foreach (var id in toDelete) { _session.Manager.DeleteBreak(id); toMove.Remove(id); }

            foreach (ObjectId psId in toRead) ApplyPropertySet(psId);

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

        /// <summary>
        /// Правка в палитре свойств: прочитать набор и применить его к маркеру.
        ///
        /// Владелец набора ищется подъёмом по цепочке владения: набор лежит
        /// в словаре AEC_PROPERTY_SETS, тот — в расширенном словаре объекта,
        /// и только его владелец есть сам прокси. Сколько именно звеньев —
        /// в живом сеансе не проверялось, поэтому идём вверх до пяти,
        /// проверяя каждое на принадлежность маркеру.
        /// </summary>
        private void ApplyPropertySet(ObjectId psId)
        {
            if (psId.IsNull || psId.IsErased) return;

            Guid markerId = Guid.Empty;
            double station = 0.0, stepHeight = 0.0, gap = 0.0;
            bool isStep = false, changedProps = false, changedStation = false;

            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            try
            {
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    var ps = tr.GetObject(psId, OpenMode.ForRead) as PropertySet;
                    if (ps == null)
                    {
                        tr.Commit();
                        return;
                    }

                    // Набор «Режим редактирования» висит на профиле, а не на прокси,
                    // и обрабатывается отдельно.
                    if (ps.PropertySetDefinitionName == PropertySetSupport.EditPsd)
                    {
                        ApplyEditFlag(tr, doc, ps);
                        tr.Commit();
                        return;
                    }

                    if (ps.PropertySetDefinitionName != PropertySetSupport.MarkerPsd)
                    {
                        tr.Commit();
                        return;
                    }

                    StationMarker m = null;
                    ObjectId ownerId = ps.OwnerId;

                    for (int i = 0; i < 5 && !ownerId.IsNull; i++)
                    {
                        m = _session.Store.GetByProxy(ownerId.Handle);
                        if (m != null) break;

                        var owner = tr.GetObject(ownerId, OpenMode.ForRead);
                        if (owner == null) break;
                        ownerId = owner.OwnerId;
                    }

                    if (m == null) { tr.Commit(); return; }

                    PropMatched++;
                    markerId = m.Id;

                    PropertySetSupport.ReadMarkerProps(
                        tr, ownerId, m, out station, out isStep, out stepHeight, out gap);

                    changedStation = Math.Abs(station - m.Station) > 1e-6;
                    changedProps = isStep != m.IsStep
                                   || Math.Abs(stepHeight - m.StepHeight) > 1e-9
                                   || Math.Abs(gap - m.Gap) > 1e-9;

                    tr.Commit();
                }
            }
            catch (System.Exception)
            {
                return;
            }

            if (markerId == Guid.Empty) return;

            // Пикет и остальное — разные операции оркестратора; пикет первым,
            // чтобы ступень встала уже на новом месте.
            if (changedStation) _session.Manager.ApplyStationChange(markerId, station);
            if (changedProps) _session.Manager.ApplyProperties(markerId, isStep, stepHeight, gap);

            if (changedStation || changedProps) PropApplied++;
        }

        /// <summary>
        /// Галочка «Режим редактирования» в палитре свойств профиля.
        ///
        /// Владелец набора ищется тем же подъёмом по цепочке владения, что и для
        /// прокси, только опознаётся не линия, а Profile. Раньше этот набор
        /// создавался и вешался на профиль, но не читался никем — галочка
        /// не делала ничего.
        /// </summary>
        private void ApplyEditFlag(Transaction tr, Document doc, PropertySet ps)
        {
            ObjectId ownerId = ps.OwnerId;
            Profile profile = null;

            for (int i = 0; i < 5 && !ownerId.IsNull; i++)
            {
                var owner = tr.GetObject(ownerId, OpenMode.ForRead);
                if (owner == null) break;

                profile = owner as Profile;
                if (profile != null) break;

                ownerId = owner.OwnerId;
            }

            if (profile == null) return;

            PropEditMatched++;

            bool wanted = PropertySetSupport.ReadEditFlag(tr, profile.ObjectId);
            Handle h = profile.Handle;

            if (_session.IsEditMode(h) == wanted) return;

            _session.SetEditMode(h, wanted);
            PropEditApplied++;

            try { doc.Editor.Regen(); } catch (System.Exception) { }
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
            var ln = RwHandles.Open<Line>(tr, db, h, OpenMode.ForRead);
            if (ln == null) return null;

            if (isPlan)
            {
                var al = RwHandles.Open<Alignment>(tr, db, m.AlignmentHandle, OpenMode.ForRead);

                // Плановая линия держится НАЧАЛОМ на оси — по нему пикет и
                // считается. Середина дала бы почти то же, но на кривых
                // проекция точки со смещением точна не так очевидно.
                return al == null
                    ? (double?)null
                    : BreakProxyFactory.PlanPointToStation(al, ln.StartPoint);
            }
            else
            {
                var pv = RwHandles.Open<ProfileView>(tr, db, m.ProfileViewHandle, OpenMode.ForRead);
                return pv == null
                    ? (double?)null
                    : BreakProxyFactory.ProfilePointToStation(pv, RwGeometry.Midpoint(ln));
            }
        }
    }
}
