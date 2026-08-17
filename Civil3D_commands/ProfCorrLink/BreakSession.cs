using System;
using System.Collections.Generic;
using System.Linq;
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

        private readonly List<BreakLink> _links = new List<BreakLink>();

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
            session.LoadLinks(doc.Database);

            // Режим редактирования переживает перезапуск: иначе после открытия
            // чертежа ручек нет, и догадаться про RW_EDITMODE невозможно.
            session._editProfiles = EditModeStore.Load(doc.Database);

            return session;
        }

        // ------------------------------------------------------------------
        //  СВЯЗИ
        // ------------------------------------------------------------------

        /// <summary>
        /// Все связи чертежа. Их может быть несколько, в том числе несколько
        /// на одном виде профиля: в таком виде лежат несколько профилей, и
        /// каждый служит базовой линией своему коридору.
        /// </summary>
        public IReadOnlyList<BreakLink> Links => _links;

        /// <summary>
        /// Связь, к которой относятся команды без явного выбора контроллера.
        /// Задаётся мастером и командой RW_EDITLINKS; при единственной связи
        /// в чертеже подставляется сама, чтобы прежние сценарии работали как были.
        /// </summary>
        public BreakLink ActiveLink
        {
            get
            {
                if (_active != null && _links.Contains(_active)) return _active;
                return _links.Count == 1 ? _links[0] : _active;
            }
            set { _active = value; }
        }
        private BreakLink _active;

        public BreakLink FindLink(Guid id) => _links.FirstOrDefault(l => l.Id == id);

        /// <summary>Связь, которой принадлежит разрыв. Ищется по профилю и коридору.</summary>
        public BreakLink LinkFor(IBreakTarget target)
        {
            if (target == null) return null;
            return _links.FirstOrDefault(l => l.Covers(target));
        }

        public void AddLink(BreakLink link)
        {
            if (link == null) return;
            if (_links.Any(l => l.Id == link.Id)) return;
            _links.Add(link);
        }

        public void RemoveLink(BreakLink link)
        {
            if (link == null) return;
            _links.Remove(link);
            if (_active == link) _active = null;
        }

        public void SaveLinks() => BreakLinkStore.SaveAll(_doc.Database, _links);

        /// <summary>
        /// Прочитать связи чертежа. Записи прежнего формата (одна связь
        /// в подсловаре Link) подхватываются, чтобы существующие чертежи
        /// не пришлось размечать заново; если и её нет, связи восстанавливаются
        /// по самим разрывам — каждый несёт ту же четвёрку хэндлов.
        ///
        /// В чертёж при этом ничего не пишется: открытие не должно помечать
        /// его изменённым.
        /// </summary>
        private void LoadLinks(Database db)
        {
            _links.Clear();
            _links.AddRange(BreakLinkStore.LoadAll(db));

            if (_links.Count == 0)
            {
                BreakLink legacy = FromLegacyRecord(db);
                if (legacy != null) _links.Add(legacy);
            }

            foreach (BreakLink restored in RestoreLinksFromMarkers(db))
                if (!_links.Any(l => l.Covers(restored)))
                    _links.Add(restored);
        }

        /// <summary>Единственная связь прежнего формата, если она ещё жива.</summary>
        private static BreakLink FromLegacyRecord(Database db)
        {
            StationMarker legacy = LinkStore.Load(db);
            if (legacy == null) return null;

            return new BreakLink
            {
                ProfileHandle     = legacy.ProfileHandle,
                ProfileViewHandle = legacy.ProfileViewHandle,
                AlignmentHandle   = legacy.AlignmentHandle,
                CorridorHandle    = legacy.CorridorHandle
            };
        }

        /// <summary>
        /// Связи, выведенные из самих разрывов: по одной на каждую пару
        /// «профиль + коридор». Нужно для чертежей, сделанных до появления
        /// записи связи, и на случай, если запись потерялась, а разрывы остались.
        /// </summary>
        private List<BreakLink> RestoreLinksFromMarkers(Database db)
        {
            var result = new List<BreakLink>();

            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var m in Store.All)
                {
                    var candidate = new BreakLink
                    {
                        ProfileHandle     = m.ProfileHandle,
                        ProfileViewHandle = m.ProfileViewHandle,
                        AlignmentHandle   = m.AlignmentHandle,
                        CorridorHandle    = m.CorridorHandle
                    };

                    if (!candidate.IsAlive(tr, db)) continue;
                    if (result.Any(l => l.Covers(candidate))) continue;

                    candidate.RefreshNames(tr, db);
                    result.Add(candidate);
                }

                tr.Commit();
            }

            return result;
        }

        // ------------------------------------------------------------------

        public void Detach()
        {
            var doc = _doc;
            Reactor.Unsubscribe(doc);
            _sessions.Remove(doc);
        }

        /// <summary>Очистить сессию закрытого документа по имени файла.</summary>
        public static void DetachByFileName(string fileName)
        {
            foreach (var kv in new List<Document>(_sessions.Keys))
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
        /// Режим включён хотя бы у одной связи.
        ///
        /// Слои оформления общие на чертёж, а «заблокировать наполовину» нельзя —
        /// поэтому защита границ снимается, пока правится хоть что-нибудь.
        /// </summary>
        public bool AnyEditMode => _editProfiles.Count > 0;

        /// <summary>
        /// Переключить режим и запомнить это в чертеже.
        ///
        /// Флаг пишется и в набор характеристик профиля, чтобы галочка
        /// в палитре свойств показывала правду: править режим можно с обеих
        /// сторон, и расходиться им нельзя.
        ///
        /// Следом перестраивается оформление: включение режима должно САМО
        /// показать заливки участков и подписи, а выключение — убрать лишнее
        /// и заблокировать границы.
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

                    RefreshOverlay();

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

        /// <summary>
        /// Перестроить оформление всех связей и привести слои к текущему режиму.
        /// Отдельной транзакцией: вызывается и снаружи любой другой правки.
        /// </summary>
        public void RefreshOverlay()
        {
            try
            {
                using (_doc.LockDocument())
                using (Suspend())
                using (var tr = _doc.Database.TransactionManager.StartTransaction())
                {
                    BreakOverlay.RebuildAll(tr, _doc.Database, this);
                    tr.Commit();
                }

                SaveLinks();
            }
            catch (System.Exception)
            {
                // Оформление — удобство, а не механика разрывов.
            }
        }

        /// <summary>Базовая линия коридора, построенная на профиле цели.</summary>
        public Baseline GetBaseline(Transaction tr, IBreakTarget target)
        {
            if (target == null) return null;
            var db = _doc.Database;

            var corridor = RwHandles.Open<Corridor>(tr, db, target.CorridorHandle, OpenMode.ForWrite);
            if (corridor == null) return null;

            // Сначала ищем по профилю (точное совпадение).
            ObjectId profId = RwHandles.Resolve(db, target.ProfileHandle);
            if (!profId.IsNull)
                foreach (Baseline bl in corridor.Baselines)
                    if (bl.ProfileId == profId) return bl;

            // Фоллбэк: ищем по оси — надёжнее если профиль ещё не сохранён в BL.
            ObjectId alId = RwHandles.Resolve(db, target.AlignmentHandle);
            if (!alId.IsNull)
                foreach (Baseline bl in corridor.Baselines)
                    if (bl.AlignmentId == alId) return bl;

            return null;
        }

        public Corridor GetCorridor(Transaction tr, IBreakTarget target)
        {
            if (target == null) return null;
            return RwHandles.Open<Corridor>(tr, _doc.Database, target.CorridorHandle, OpenMode.ForWrite);
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
