using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Civil3D_commands.Shared;
using AcAp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace Civil3D_commands.AssociativeBreaks
{
    /// <summary>
    /// Контейнер состояния на документ: модель, реактор, оркестратор, кэш режима редактирования.
    /// Доступ через BreakSession.Current.
    /// </summary>
    public class BreakSession
    {
        // Словарь по документу — пережирает DocumentActivated без потери состояния.
        private static readonly Dictionary<Document, BreakSession> _sessions =
            new Dictionary<Document, BreakSession>();

        public static BreakSession Current
        {
            get
            {
                var doc = AcAp.DocumentManager.MdiActiveDocument;
                return doc != null && _sessions.TryGetValue(doc, out var s) ? s : null;
            }
        }

        public MarkerStore Store { get; } = new MarkerStore();
        public AssociativeBreakManager Manager { get; }
        public BreakReactor Reactor { get; }

        /// <summary>Флаг подавления реактора во время собственных правок оркестратора.</summary>
        public bool Suspended { get; private set; }

        private readonly Document _doc;
        // "Режим редактирования вкл" по Handle профиля. Живёт в чертеже
        // (EditModeStore), в сеансе только зеркалится.
        private HashSet<long> _editProfiles = new HashSet<long>();

        private BreakSession(Document doc)
        {
            _doc = doc;
            Manager = new AssociativeBreakManager(this);
            Reactor = new BreakReactor(this);
        }

        public static BreakSession Attach(Document doc)
        {
            // Если сессия для этого документа уже есть — НЕ пересоздаём,
            // чтобы не терять IsEditMode и ActiveLink при DocumentActivated.
            if (_sessions.TryGetValue(doc, out var existing))
                return existing;

            var session = new BreakSession(doc);
            _sessions[doc] = session;
            session.Reactor.Subscribe(doc);
            session.Store.LoadFromDatabase(doc.Database);
            session.ActiveLink = LinkStore.Load(doc.Database)
                                 ?? RestoreLinkFromMarkers(doc.Database, session.Store);

            // Режим редактирования переживает перезапуск: иначе после открытия
            // чертежа ручек нет, и догадаться про RW_EDITMODE невозможно.
            session._editProfiles = EditModeStore.Load(doc.Database);

            return session;
        }

        /// <summary>
        /// Запасной путь, если явной записи связи в чертеже нет: любой маркер несёт
        /// те же четыре хэндла. Нужен для чертежей, сделанных до появления LinkStore,
        /// и на случай, если запись связи потерялась, а разрывы остались.
        /// В чертёж ничего не пишем: восстановление не должно помечать его изменённым.
        /// </summary>
        private static StationMarker RestoreLinkFromMarkers(Database db, MarkerStore store)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                StationMarker link = null;
                foreach (var m in store.All)
                {
                    if (!LinkStore.IsAlive(tr, db, m)) continue;
                    link = new StationMarker
                    {
                        ProfileHandle     = m.ProfileHandle,
                        ProfileViewHandle = m.ProfileViewHandle,
                        AlignmentHandle   = m.AlignmentHandle,
                        CorridorHandle    = m.CorridorHandle
                    };
                    break;
                }
                tr.Commit();
                return link;
            }
        }

        public void Detach()
        {
            var doc = _doc;
            Reactor.Unsubscribe(doc);
            _sessions.Remove(doc);
        }

        /// <summary>Очистить сессию закрытого документа по имени файла.</summary>
        public static void DetachByFileName(string fileName)
        {
            foreach (var kv in new System.Collections.Generic.List<Document>(_sessions.Keys))
            {
                try
                {
                    if (string.Equals(kv.Name, fileName, StringComparison.OrdinalIgnoreCase))
                    {
                        _sessions[kv].Reactor.Unsubscribe(kv);
                        _sessions.Remove(kv);
                        return;
                    }
                }
                catch { /* документ уже закрыт — просто удаляем */ _sessions.Remove(kv); }
            }
        }

        public IDisposable Suspend() => new Guard(this);

        public bool IsEditMode(Handle profileHandle) => _editProfiles.Contains(profileHandle.Value);

        /// <summary>
        /// Текущая связь "профиль-вид-ось-коридор", заданная мастером RW_LINKPROFILECORRIDOR.
        /// Хранится в чертеже (<see cref="LinkStore"/>) и восстанавливается при открытии,
        /// поэтому переживает перезапуск Civil 3D. Связь в чертеже одна: если их несколько,
        /// восстановится последняя созданная.
        /// </summary>
        public StationMarker ActiveLink { get; set; }

        /// <summary>
        /// Переключить режим и запомнить это в чертеже.
        ///
        /// Флаг пишется и в набор характеристик профиля, чтобы галочка
        /// в палитре свойств показывала правду: править режим можно с обеих
        /// сторон, и расходиться им нельзя.
        /// </summary>
        public void SetEditMode(Handle profileHandle, bool on)
        {
            if (on) _editProfiles.Add(profileHandle.Value);
            else _editProfiles.Remove(profileHandle.Value);

            try
            {
                // Под подавлением: иначе реактор увидит нашу же запись в набор
                // и попробует применить её обратно.
                using (_doc.LockDocument())
                using (Suspend())
                {
                    EditModeStore.Save(_doc.Database, _editProfiles);

                    ObjectId profId = RwHandles.Resolve(_doc.Database, profileHandle);
                    if (profId.IsNull) return;

                    using (var tr = _doc.Database.TransactionManager.StartTransaction())
                    {
                        PropertySetSupport.Attach(
                            tr, profId, PropertySetSupport.EnsureEditPsd(_doc.Database));
                        PropertySetSupport.WriteEditFlag(tr, profId, on);
                        tr.Commit();
                    }
                }
            }
            catch (System.Exception)
            {
                // Не сохранилось — режим всё равно действует в этом сеансе.
            }
        }

        /// <summary>Базовая линия коридора, построенная на профиле маркера.</summary>
        public Baseline GetBaseline(Transaction tr, StationMarker m)
        {
            var db = _doc.Database;

            var corridor = RwHandles.Open<Corridor>(tr, db, m.CorridorHandle, OpenMode.ForWrite);
            if (corridor == null) return null;

            // Сначала ищем по профилю (точное совпадение).
            ObjectId profId = RwHandles.Resolve(db, m.ProfileHandle);
            if (!profId.IsNull)
                foreach (Baseline bl in corridor.Baselines)
                    if (bl.ProfileId == profId) return bl;

            // Фоллбэк: ищем по оси — надёжнее если профиль ещё не сохранён в BL.
            ObjectId alId = RwHandles.Resolve(db, m.AlignmentHandle);
            if (!alId.IsNull)
                foreach (Baseline bl in corridor.Baselines)
                    if (bl.AlignmentId == alId) return bl;

            return null;
        }

        public Corridor GetCorridor(Transaction tr, StationMarker m)
        {
            return RwHandles.Open<Corridor>(tr, _doc.Database, m.CorridorHandle, OpenMode.ForWrite);
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
