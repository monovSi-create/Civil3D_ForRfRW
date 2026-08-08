using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;

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
        /// regionStart/regionEnd — пределы базовой линии коридора.
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
            return (prev + buffer, next - buffer);
        }

        // ----------------------------------------------------------------------
        //  ПЕРСИСТЕНТНОСТЬ
        // ----------------------------------------------------------------------

        public void SaveToDatabase(Database db)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                ObjectId dictId = GetDictionaryId(tr, db, true);
                var dict = (DBDictionary)tr.GetObject(dictId, OpenMode.ForWrite);

                // Полная перезапись: чистим старые записи.
                foreach (DBDictionaryEntry e in dict)
                    tr.GetObject(e.Value, OpenMode.ForWrite).Erase();

                int i = 0;
                foreach (var m in _byId.Values)
                {
                    var xrec = new Xrecord { XlateReferences = true, Data = m.ToResultBuffer() };
                    dict.SetAt(XrecPrefix + i, xrec);
                    tr.AddNewlyCreatedDBObject(xrec, true);
                    i++;
                }
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
                foreach (DBDictionaryEntry e in dict)
                {
                    var xrec = tr.GetObject(e.Value, OpenMode.ForRead) as Xrecord;
                    if (xrec?.Data == null) continue;
                    try
                    {
                        var m = StationMarker.FromResultBuffer(xrec.Data);
                        // Фильтруем маркеры, чьи прокси уже стёрты.
                        if (ProxyExists(db, m.ProfileProxyHandle) || ProxyExists(db, m.PlanProxyHandle))
                            Add(m);
                    }
                    catch (System.Exception) { /* битая запись — пропускаем */ }
                }
                tr.Commit();
            }
        }

        private static bool ProxyExists(Database db, Handle h)
        {
            if (h.Value == 0) return false;
            return db.TryGetObjectId(h, out ObjectId id) && id.IsValid && !id.IsErased;
        }

        private static ObjectId GetDictionaryId(Transaction tr, Database db, bool createIfMissing)
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

            if (!company.Contains(AppDict))
            {
                if (!createIfMissing) return ObjectId.Null;
                var app = new DBDictionary();
                ObjectId appId = company.SetAt(AppDict, app);
                tr.AddNewlyCreatedDBObject(app, true);
                return appId;
            }
            return company.GetAt(AppDict);
        }
    }
}
