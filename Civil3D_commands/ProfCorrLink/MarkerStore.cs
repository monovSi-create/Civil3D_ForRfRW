using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.Civil.DatabaseServices;
using Civil3D_commands.Shared;

namespace Civil3D_commands.AssociativeBreaks
{
    /// <summary>
    /// Хранилище маркеров: индексы в памяти + сохранение в словарь чертежа (NOD) через Xrecord.
    /// Один экземпляр на документ (см. BreakSession).
    /// Паттерн взят из твоего LinkedObjectManager.SaveToDatabase/LoadFromDatabase.
    /// </summary>
    public class MarkerStore
    {
        private const string CompanyDict = "RW_AssocBreaks";
        private const string AppDict = "Markers";
        private const string XrecPrefix = "MK";

        private readonly Dictionary<Guid, StationMarker> _byId = new Dictionary<Guid, StationMarker>();
        // Оба прокси (план + профиль) указывают на один и тот же маркер.
        private readonly Dictionary<Handle, Guid> _byProxy = new Dictionary<Handle, Guid>();

        public IEnumerable<StationMarker> All => _byId.Values;

        public StationMarker Get(Guid id) => _byId.TryGetValue(id, out var m) ? m : null;

        public StationMarker GetByProxy(Handle h) =>
            _byProxy.TryGetValue(h, out var id) ? Get(id) : null;

        public void Add(StationMarker m)
        {
            _byId[m.Id] = m;
            ReindexProxies(m);
        }

        public void Remove(Guid id)
        {
            if (!_byId.TryGetValue(id, out var m)) return;
            _byProxy.Remove(m.ProfileProxyHandle);
            _byProxy.Remove(m.PlanProxyHandle);
            _byId.Remove(id);
        }

        /// <summary>Перестроить индекс прокси для маркера (после смены хэндлов прокси).</summary>
        public void ReindexProxies(StationMarker m)
        {
            if (m.ProfileProxyHandle.Value != 0) _byProxy[m.ProfileProxyHandle] = m.Id;
            if (m.PlanProxyHandle.Value != 0) _byProxy[m.PlanProxyHandle] = m.Id;
        }

        /// <summary>Все маркеры на заданном профиле, отсортированные по пикету.</summary>
        public List<StationMarker> ForProfile(Handle profileHandle) =>
            _byId.Values.Where(m => m.ProfileHandle == profileHandle)
                        .OrderBy(m => m.Station).ToList();

        /// <summary>
        /// Соседние пикеты для маркера (для клампа). Возвращает границы допустимого
        /// перемещения с учётом буфера: (минимально допустимый, максимально допустимый).
        /// baselineStart/baselineEnd — пределы базовой линии коридора.
        ///
        /// Буфер отделяет маркер от СОСЕДА, чтобы область не схлопнулась. С внешней
        /// стороны у концевых границ соседа нет, и буфер там не нужен: иначе
        /// начало коридора невозможно было бы поставить на самое начало базовой
        /// линии — оно упиралось бы в невидимый отступ шириной в буфер.
        /// </summary>
        public (double min, double max) GetMoveBounds(StationMarker m, double buffer,
                                                      double baselineStart, double baselineEnd)
        {
            var siblings = ForProfile(m.ProfileHandle).Where(x => x.Id != m.Id)
                                                      .Select(x => x.Station).ToList();
            double prev = baselineStart;
            double next = baselineEnd;
            foreach (var s in siblings)
            {
                if (s < m.Station && s > prev) prev = s;
                if (s > m.Station && s < next) next = s;
            }

            double lower = m.Role == StationMarker.MarkerRole.Start ? 0.0 : buffer;
            double upper = m.Role == StationMarker.MarkerRole.End ? 0.0 : buffer;

            return (prev + lower, next - upper);
        }

        // ----------------------------------------------------------------------
        //  ПЕРСИСТЕНТНОСТЬ
        // ----------------------------------------------------------------------

        /// <summary>
        /// Ключ записи — Guid маркера, поэтому запись правится на месте.
        /// Прежние ключи MK0..MKn были позиционными: после любой правки они
        /// означали уже другие маркеры, и каждое сохранение (а оно происходит
        /// на каждом отпускании грипсы) стирало и пересоздавало всю таблицу.
        /// </summary>
        private static string KeyOf(Guid id) => XrecPrefix + "_" + id.ToString("N");

        public void SaveToDatabase(Database db)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                ObjectId dictId = GetDictionaryId(tr, db, true);
                var dict = (DBDictionary)tr.GetObject(dictId, OpenMode.ForWrite);

                var live = new HashSet<string>();

                foreach (var m in _byId.Values)
                {
                    string key = KeyOf(m.Id);
                    live.Add(key);

                    // XlateReferences касается только ссылок-указателей (SoftPointerId
                    // и подобных) при копировании базы. Здесь их нет — все ссылки
                    // лежат текстовыми хэндлами, — так что флаг ни на что не влияет
                    // и оставлен на случай перехода на настоящие указатели.
                    ResultBuffer data = m.ToResultBuffer();

                    if (dict.Contains(key))
                    {
                        var existing = (Xrecord)tr.GetObject(dict.GetAt(key), OpenMode.ForWrite);
                        existing.Data = data;
                    }
                    else
                    {
                        var xrec = new Xrecord { XlateReferences = true, Data = data };
                        dict.SetAt(key, xrec);
                        tr.AddNewlyCreatedDBObject(xrec, true);
                    }
                }

                // Всё, чего в модели больше нет, — в том числе записи прежнего формата
                // MK0..MKn, только что переписанные под своими Guid. Собираем список
                // заранее: стирать объекты во время обхода словаря нельзя.
                var stale = new List<ObjectId>();
                foreach (DBDictionaryEntry e in dict)
                    if (!live.Contains(e.Key)) stale.Add(e.Value);

                foreach (ObjectId id in stale)
                    tr.GetObject(id, OpenMode.ForWrite).Erase();

                tr.Commit();
            }
        }

        public void LoadFromDatabase(Database db)
        {
            _byId.Clear();
            _byProxy.Clear();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                ObjectId dictId = GetDictionaryId(tr, db, false);
                if (dictId.IsNull) { tr.Commit(); return; }

                var dict = (DBDictionary)tr.GetObject(dictId, OpenMode.ForRead);
                int broken = 0;

                foreach (DBDictionaryEntry e in dict)
                {
                    var xrec = tr.GetObject(e.Value, OpenMode.ForRead) as Xrecord;
                    if (xrec?.Data == null) continue;
                    try
                    {
                        var m = StationMarker.FromResultBuffer(xrec.Data);

                        // Запись связи в словаре маркеров делать нечего, но если
                        // она туда попала — это не маркер, и разрывом её считать нельзя.
                        if (m.Kind != StationMarker.RecordKind.Marker) continue;

                        // Фильтруем маркеры, чьи прокси уже стёрты.
                        if (ProxyExists(db, m.ProfileProxyHandle) || ProxyExists(db, m.PlanProxyHandle))
                            Add(m);
                    }
                    catch (System.Exception)
                    {
                        // Пропускаем, но не молча: следующее сохранение такую запись
                        // сотрёт, и разрыв исчезнет из чертежа без единого слова.
                        broken++;
                    }
                }

                if (broken > 0) Warn(broken);
                tr.Commit();
            }
        }

        /// <summary>Сообщить о непрочитанных записях. Падать из-за этого нельзя.</summary>
        private static void Warn(int broken)
        {
            try
            {
                Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(
                    $"\n[RW_Break] Записей разрывов не прочитано: {broken}. " +
                    "Подробности — RW_BREAKDIAG.");
            }
            catch (System.Exception) { }
        }

        private static bool ProxyExists(Database db, Handle h)
        {
            return RwHandles.Exists(db, h);
        }

        private static ObjectId GetDictionaryId(Transaction tr, Database db, bool createIfMissing) =>
            GetDictionaryId(tr, db, AppDict, createIfMissing);

        /// <summary>Подсловарь чертежа RW_AssocBreaks\{appDict}. Общий для маркеров и связи.</summary>
        internal static ObjectId GetDictionaryId(Transaction tr, Database db, string appDict, bool createIfMissing)
        {
            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            DBDictionary company;
            if (!nod.Contains(CompanyDict))
            {
                if (!createIfMissing) return ObjectId.Null;
                nod.UpgradeOpen();
                company = new DBDictionary();
                nod.SetAt(CompanyDict, company);
                tr.AddNewlyCreatedDBObject(company, true);
            }
            else company = (DBDictionary)tr.GetObject(nod.GetAt(CompanyDict), OpenMode.ForWrite);

            if (!company.Contains(appDict))
            {
                if (!createIfMissing) return ObjectId.Null;
                var app = new DBDictionary();
                ObjectId appId = company.SetAt(appDict, app);
                tr.AddNewlyCreatedDBObject(app, true);
                return appId;
            }
            return company.GetAt(appDict);
        }
    }

    /// <summary>
    /// Режим редактирования в словаре чертежа: список хэндлов профилей,
    /// у которых он включён.
    ///
    /// Раньше режим жил только в сеансе: после перезапуска Civil 3D ручки
    /// пропадали, и связать это с `RW_EDITMODE` было невозможно.
    /// </summary>
    public static class EditModeStore
    {
        private const string AppDict = "EditMode";
        private const string XrecKey = "Profiles";

        public static HashSet<long> Load(Database db)
        {
            var result = new HashSet<long>();

            using (var tr = db.TransactionManager.StartTransaction())
            {
                ObjectId dictId = MarkerStore.GetDictionaryId(tr, db, AppDict, false);
                if (dictId.IsNull) { tr.Commit(); return result; }

                var dict = (DBDictionary)tr.GetObject(dictId, OpenMode.ForRead);
                if (!dict.Contains(XrecKey)) { tr.Commit(); return result; }

                var xrec = tr.GetObject(dict.GetAt(XrecKey), OpenMode.ForRead) as Xrecord;
                if (xrec?.Data != null)
                {
                    foreach (TypedValue tv in xrec.Data)
                    {
                        if (tv.TypeCode != (int)DxfCode.Text) continue;

                        Handle h = RwHandles.Parse(tv.Value.ToString());
                        // Профиль мог быть удалён — тогда и режим ни к чему.
                        if (RwHandles.Exists(db, h)) result.Add(h.Value);
                    }
                }

                tr.Commit();
            }

            return result;
        }

        public static void Save(Database db, IEnumerable<long> profileHandles)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                ObjectId dictId = MarkerStore.GetDictionaryId(tr, db, AppDict, true);
                var dict = (DBDictionary)tr.GetObject(dictId, OpenMode.ForWrite);

                var rb = new ResultBuffer();
                foreach (long value in profileHandles)
                    rb.Add(new TypedValue((int)DxfCode.Text, RwHandles.ToText(new Handle(value))));

                if (dict.Contains(XrecKey))
                {
                    var existing = (Xrecord)tr.GetObject(dict.GetAt(XrecKey), OpenMode.ForWrite);
                    existing.Data = rb;
                }
                else
                {
                    var xrec = new Xrecord { Data = rb };
                    dict.SetAt(XrecKey, xrec);
                    tr.AddNewlyCreatedDBObject(xrec, true);
                }

                tr.Commit();
            }
        }
    }

    /// <summary>
    /// Активная связь «профиль ↔ вид профиля ↔ ось ↔ коридор» в словаре чертежа.
    /// Лежит в ОТДЕЛЬНОМ подсловаре: MarkerStore при сохранении полностью переписывает
    /// свой, так что положить связь туда — значит терять её при каждой правке разрыва.
    /// Связь описывается тем же StationMarker: заполнены только четыре хэндла.
    /// </summary>
    public static class LinkStore
    {
        private const string AppDict = "Link";
        private const string XrecKey = "Active";

        public static void Save(Database db, StationMarker link)
        {
            if (link == null) return;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                ObjectId dictId = MarkerStore.GetDictionaryId(tr, db, AppDict, true);
                var dict = (DBDictionary)tr.GetObject(dictId, OpenMode.ForWrite);

                // Вид записи пишем явно: по содержимому связь от маркера не отличить.
                ResultBuffer data = link.ToResultBuffer(StationMarker.RecordKind.Link);

                if (dict.Contains(XrecKey))
                {
                    var existing = (Xrecord)tr.GetObject(dict.GetAt(XrecKey), OpenMode.ForWrite);
                    existing.Data = data;
                }
                else
                {
                    var xrec = new Xrecord { XlateReferences = true, Data = data };
                    dict.SetAt(XrecKey, xrec);
                    tr.AddNewlyCreatedDBObject(xrec, true);
                }
                tr.Commit();
            }
        }

        /// <summary>Связь из чертежа. null, если её нет или объекты не пережили правку чертежа.</summary>
        public static StationMarker Load(Database db)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                ObjectId dictId = MarkerStore.GetDictionaryId(tr, db, AppDict, false);
                if (dictId.IsNull) { tr.Commit(); return null; }

                var dict = (DBDictionary)tr.GetObject(dictId, OpenMode.ForRead);
                if (!dict.Contains(XrecKey)) { tr.Commit(); return null; }

                StationMarker link = null;
                var xrec = tr.GetObject(dict.GetAt(XrecKey), OpenMode.ForRead) as Xrecord;
                if (xrec?.Data != null)
                {
                    try { link = StationMarker.FromResultBuffer(xrec.Data); }
                    catch (System.Exception) { link = null; }
                }
                if (link != null && !IsAlive(tr, db, link)) link = null;

                tr.Commit();
                return link;
            }
        }

        /// <summary>Связь годна, только если живы все четыре объекта и каждый нужного типа.</summary>
        public static bool IsAlive(Transaction tr, Database db, StationMarker link)
        {
            return link != null
                && Alive<Profile>(tr, db, link.ProfileHandle)
                && Alive<ProfileView>(tr, db, link.ProfileViewHandle)
                && Alive<Alignment>(tr, db, link.AlignmentHandle)
                && Alive<Corridor>(tr, db, link.CorridorHandle);
        }

        // DBObject, как и Entity, есть в обоих пространствах имён — квалифицируем явно.
        private static bool Alive<T>(Transaction tr, Database db, Handle h)
            where T : Autodesk.AutoCAD.DatabaseServices.DBObject
        {
            return RwHandles.Open<T>(tr, db, h, OpenMode.ForRead) != null;
        }
    }
}
