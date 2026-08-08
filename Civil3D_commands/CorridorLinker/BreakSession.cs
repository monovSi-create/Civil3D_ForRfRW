using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;

namespace Civil3D_commands.AssociativeBreaks
{
    /// <summary>
    /// Контейнер состояния на документ: модель, реактор, оркестратор, кэш режима редактирования.
    /// Доступ через BreakSession.Current.
    /// </summary>
    public class BreakSession
    {
        [ThreadStatic] private static BreakSession _current;
        public static BreakSession Current => _current;

        public MarkerStore Store { get; } = new MarkerStore();
        public AssociativeBreakManager Manager { get; }
        public BreakReactor Reactor { get; }

        /// <summary>Флаг подавления реактора во время собственных правок оркестратора.</summary>
        public bool Suspended { get; private set; }

        private readonly Document _doc;
        // Кэш "режим редактирования вкл" по Handle профиля.
        private readonly HashSet<long> _editProfiles = new HashSet<long>();

        private BreakSession(Document doc)
        {
            _doc = doc;
            Manager = new AssociativeBreakManager(this);
            Reactor = new BreakReactor(this);
        }

        public static BreakSession Attach(Document doc)
        {
            _current = new BreakSession(doc);
            _current.Reactor.Subscribe(doc);
            _current.Store.LoadFromDatabase(doc.Database);
            return _current;
        }

        public void Detach()
        {
            Reactor.Unsubscribe(_doc);
            _current = null;
        }

        public IDisposable Suspend() => new Guard(this);

        public bool IsEditMode(Handle profileHandle) => _editProfiles.Contains(profileHandle.Value);

        /// <summary>
        /// Текущая связь "профиль-вид-ось-коридор", заданная мастером RW_LINKPROFILECORRIDOR.
        /// АДАПТ: для многодокументной/много-связной работы вынести в постоянное хранилище
        /// (Xrecord), как сделано для маркеров.
        /// </summary>
        public StationMarker ActiveLink { get; set; }

        public void SetEditMode(Handle profileHandle, bool on)
        {
            if (on) _editProfiles.Add(profileHandle.Value);
            else _editProfiles.Remove(profileHandle.Value);
        }

        /// <summary>Базовая линия коридора, построенная на профиле маркера.</summary>
        public Baseline GetBaseline(Transaction tr, StationMarker m)
        {
            var db = _doc.Database;
            if (!db.TryGetObjectId(m.CorridorHandle, out ObjectId corrId) || corrId.IsNull) return null;
            if (!db.TryGetObjectId(m.ProfileHandle, out ObjectId profId) || profId.IsNull) return null;

            var corridor = tr.GetObject(corrId, OpenMode.ForWrite) as Corridor;
            if (corridor == null) return null;
            foreach (Baseline bl in corridor.Baselines)
                if (bl.ProfileId == profId) return bl;
            return null;
        }

        public Corridor GetCorridor(Transaction tr, StationMarker m)
        {
            var db = _doc.Database;
            if (!db.TryGetObjectId(m.CorridorHandle, out ObjectId corrId) || corrId.IsNull) return null;
            return tr.GetObject(corrId, OpenMode.ForWrite) as Corridor;
        }

        private sealed class Guard : IDisposable
        {
            private readonly BreakSession _s;
            private readonly bool _prev;
            public Guard(BreakSession s) { _s = s; _prev = s.Suspended; s.Suspended = true; }
            public void Dispose() { _s.Suspended = _prev; }
        }
    }
}
