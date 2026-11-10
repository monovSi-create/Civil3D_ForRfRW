using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Civil3D_commands.Shared;
using ObjectId = Autodesk.AutoCAD.DatabaseServices.ObjectId;
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;
using Line = Autodesk.AutoCAD.DatabaseServices.Line;
using Polyline = Autodesk.AutoCAD.DatabaseServices.Polyline;
using Alignment = Autodesk.Civil.DatabaseServices.Alignment;
using ProfileView = Autodesk.Civil.DatabaseServices.ProfileView;
using SectionView = Autodesk.Civil.DatabaseServices.SectionView;
using SampleLine = Autodesk.Civil.DatabaseServices.SampleLine;

namespace Civil3D_commands.SectionMarks
{
    /// <summary>Какую роль играет объект чертежа в отметке разреза.</summary>
    internal enum MarkPart
    {
        Block, TopTick, BotTick, TopArrow, BotArrow,
        SampleLine, SectionView, ProfileView, Alignment
    }

    /// <summary>
    /// Отметка разреза на виде профиля: блок с названием сечения плюс две
    /// вертикальные чёрточки (вверху и внизу вида) со стрелками взгляда.
    ///
    /// Источник истины — эта запись, а не нарисованное: пикет и название берутся
    /// из линии выборки при каждой синхронизации, от пользователя в модели
    /// остаются только высоты чёрточек, направление и имя блока.
    ///
    /// Пикет — это пересечение линии выборки с трассой ВИДА ПРОФИЛЯ, а не
    /// пикет самой линии: сечение может принадлежать соседней трассе.
    /// </summary>
    internal class SectionMark
    {
        public Guid Id = Guid.NewGuid();

        public Handle ProfileViewHandle;
        public Handle SampleLineHandle;

        /// <summary>
        /// Вид сечения. Нужен только записям первой версии, где отметка
        /// привязывалась к нему; у новых пусто — отметке достаточно линии выборки.
        /// </summary>
        public Handle SectionViewHandle;

        /// <summary>Трасса вида профиля: её изменение сдвигает пересечение.</summary>
        public Handle AlignmentHandle;

        public string Name = "";
        public string BlockName = SectionMarkBuilder.DefaultBlockName;

        /// <summary>Стрелки смотрят против пикетажа. По умолчанию — как у вида сечения.</summary>
        public bool Reverse;

        /// <summary>Отметки чёрточек заданы (иначе при синхронизации берутся по умолчанию).</summary>
        public bool TicksSet;
        public double TopLow, TopHigh, BotLow, BotHigh;

        public Handle BlockHandle, TopTickHandle, BotTickHandle, TopArrowHandle, BotArrowHandle;

        public double Station;

        public IEnumerable<KeyValuePair<Handle, MarkPart>> Links()
        {
            yield return new KeyValuePair<Handle, MarkPart>(ProfileViewHandle, MarkPart.ProfileView);
            yield return new KeyValuePair<Handle, MarkPart>(SampleLineHandle, MarkPart.SampleLine);
            yield return new KeyValuePair<Handle, MarkPart>(SectionViewHandle, MarkPart.SectionView);
            yield return new KeyValuePair<Handle, MarkPart>(AlignmentHandle, MarkPart.Alignment);
            yield return new KeyValuePair<Handle, MarkPart>(BlockHandle, MarkPart.Block);
            yield return new KeyValuePair<Handle, MarkPart>(TopTickHandle, MarkPart.TopTick);
            yield return new KeyValuePair<Handle, MarkPart>(BotTickHandle, MarkPart.BotTick);
            yield return new KeyValuePair<Handle, MarkPart>(TopArrowHandle, MarkPart.TopArrow);
            yield return new KeyValuePair<Handle, MarkPart>(BotArrowHandle, MarkPart.BotArrow);
        }
    }

    /// <summary>Метка в XData чёрточки: чья она и верхняя или нижняя.</summary>
    internal static class SectionMarkTag
    {
        public const string App = "RW_SECTIONMARK";

        public static void EnsureRegApp(Transaction tr, Database db)
        {
            var table = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (table.Has(App)) return;

            table.UpgradeOpen();
            var rec = new RegAppTableRecord { Name = App };
            table.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
        }

        public static void Write(Entity e, Guid id, bool top)
        {
            e.XData = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, App),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, id.ToString("N")),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, top ? "T" : "B"));
        }

        public static bool TryRead(Entity e, out Guid id, out bool top)
        {
            id = Guid.Empty;
            top = false;
            if (e == null) return false;

            try
            {
                using (ResultBuffer rb = e.GetXDataForApplication(App))
                {
                    if (rb == null) return false;

                    TypedValue[] v = rb.AsArray();
                    if (v.Length < 3) return false;
                    if (!Guid.TryParse(Convert.ToString(v[1].Value), out id)) return false;

                    top = Convert.ToString(v[2].Value) == "T";
                    return true;
                }
            }
            catch (System.Exception)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Хранилище отметок: словарь RW_SECTIONMARKS в NOD, по Xrecord на отметку.
    /// Ссылки — Handle, как и в остальных модулях.
    /// </summary>
    internal static class SectionMarkStore
    {
        private const string DictName = "RW_SECTIONMARKS";
        private const string KeyPrefix = "SM_";
        private const string DefaultBlockKey = "DEFAULT_BLOCK";

        public static List<SectionMark> LoadAll(Transaction tr, Database db)
        {
            var result = new List<SectionMark>();
            DBDictionary dict = OpenDict(tr, db, false);
            if (dict == null) return result;

            foreach (DBDictionaryEntry entry in dict)
            {
                if (!entry.Key.StartsWith(KeyPrefix)) continue;

                var xr = tr.GetObject(entry.Value, OpenMode.ForRead) as Xrecord;
                SectionMark m = xr == null ? null : Decode(entry.Key, xr);
                if (m != null) result.Add(m);
            }

            return result;
        }

        public static void Save(Transaction tr, Database db, SectionMark m)
        {
            DBDictionary dict = OpenDict(tr, db, true);
            string key = KeyPrefix + m.Id.ToString("N");

            Xrecord xr;
            if (dict.Contains(key))
            {
                xr = (Xrecord)tr.GetObject(dict.GetAt(key), OpenMode.ForWrite);
            }
            else
            {
                xr = new Xrecord();
                dict.SetAt(key, xr);
                tr.AddNewlyCreatedDBObject(xr, true);
            }

            xr.Data = Encode(m);
        }

        public static void Remove(Transaction tr, Database db, SectionMark m)
        {
            DBDictionary dict = OpenDict(tr, db, true);
            string key = KeyPrefix + m.Id.ToString("N");
            if (!dict.Contains(key)) return;

            var xr = tr.GetObject(dict.GetAt(key), OpenMode.ForWrite);
            xr.Erase();
        }

        public static string GetDefaultBlock(Transaction tr, Database db)
        {
            DBDictionary dict = OpenDict(tr, db, false);
            if (dict == null || !dict.Contains(DefaultBlockKey)) return SectionMarkBuilder.DefaultBlockName;

            var xr = tr.GetObject(dict.GetAt(DefaultBlockKey), OpenMode.ForRead) as Xrecord;
            if (xr == null || xr.Data == null) return SectionMarkBuilder.DefaultBlockName;

            TypedValue[] v = xr.Data.AsArray();
            string name = v.Length > 0 ? Convert.ToString(v[0].Value) : null;
            return string.IsNullOrEmpty(name) ? SectionMarkBuilder.DefaultBlockName : name;
        }

        public static void SetDefaultBlock(Transaction tr, Database db, string name)
        {
            DBDictionary dict = OpenDict(tr, db, true);

            Xrecord xr;
            if (dict.Contains(DefaultBlockKey))
            {
                xr = (Xrecord)tr.GetObject(dict.GetAt(DefaultBlockKey), OpenMode.ForWrite);
            }
            else
            {
                xr = new Xrecord();
                dict.SetAt(DefaultBlockKey, xr);
                tr.AddNewlyCreatedDBObject(xr, true);
            }

            xr.Data = new ResultBuffer(new TypedValue((int)DxfCode.Text, name));
        }

        private static DBDictionary OpenDict(Transaction tr, Database db, bool create)
        {
            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);

            if (nod.Contains(DictName))
                return (DBDictionary)tr.GetObject(
                    nod.GetAt(DictName), create ? OpenMode.ForWrite : OpenMode.ForRead);

            if (!create) return null;

            nod.UpgradeOpen();
            var dict = new DBDictionary();
            nod.SetAt(DictName, dict);
            tr.AddNewlyCreatedDBObject(dict, true);
            return dict;
        }

        private static ResultBuffer Encode(SectionMark m)
        {
            return new ResultBuffer(
                new TypedValue((int)DxfCode.Text, RwHandles.ToText(m.ProfileViewHandle)),
                new TypedValue((int)DxfCode.Text, RwHandles.ToText(m.SampleLineHandle)),
                new TypedValue((int)DxfCode.Text, RwHandles.ToText(m.SectionViewHandle)),
                new TypedValue((int)DxfCode.Text, m.Name ?? ""),
                new TypedValue((int)DxfCode.Text, m.BlockName ?? ""),
                new TypedValue((int)DxfCode.Int16, (short)(m.Reverse ? 1 : 0)),
                new TypedValue((int)DxfCode.Int16, (short)(m.TicksSet ? 1 : 0)),
                new TypedValue((int)DxfCode.Real, m.TopLow),
                new TypedValue((int)DxfCode.Real, m.TopHigh),
                new TypedValue((int)DxfCode.Real, m.BotLow),
                new TypedValue((int)DxfCode.Real, m.BotHigh),
                new TypedValue((int)DxfCode.Text, RwHandles.ToText(m.BlockHandle)),
                new TypedValue((int)DxfCode.Text, RwHandles.ToText(m.TopTickHandle)),
                new TypedValue((int)DxfCode.Text, RwHandles.ToText(m.BotTickHandle)),
                new TypedValue((int)DxfCode.Text, RwHandles.ToText(m.TopArrowHandle)),
                new TypedValue((int)DxfCode.Text, RwHandles.ToText(m.BotArrowHandle)),
                new TypedValue((int)DxfCode.Real, m.Station),
                new TypedValue((int)DxfCode.Text, RwHandles.ToText(m.AlignmentHandle)));
        }

        private static SectionMark Decode(string key, Xrecord xr)
        {
            if (xr.Data == null) return null;

            TypedValue[] v = xr.Data.AsArray();
            if (v.Length < 17) return null;

            Guid id;
            if (!Guid.TryParse(key.Substring(KeyPrefix.Length), out id)) return null;

            return new SectionMark
            {
                Id = id,
                ProfileViewHandle = RwHandles.Parse(Convert.ToString(v[0].Value)),
                SampleLineHandle = RwHandles.Parse(Convert.ToString(v[1].Value)),
                SectionViewHandle = RwHandles.Parse(Convert.ToString(v[2].Value)),
                Name = Convert.ToString(v[3].Value),
                BlockName = Convert.ToString(v[4].Value),
                Reverse = Convert.ToInt32(v[5].Value) != 0,
                TicksSet = Convert.ToInt32(v[6].Value) != 0,
                TopLow = Convert.ToDouble(v[7].Value),
                TopHigh = Convert.ToDouble(v[8].Value),
                BotLow = Convert.ToDouble(v[9].Value),
                BotHigh = Convert.ToDouble(v[10].Value),
                BlockHandle = RwHandles.Parse(Convert.ToString(v[11].Value)),
                TopTickHandle = RwHandles.Parse(Convert.ToString(v[12].Value)),
                BotTickHandle = RwHandles.Parse(Convert.ToString(v[13].Value)),
                TopArrowHandle = RwHandles.Parse(Convert.ToString(v[14].Value)),
                BotArrowHandle = RwHandles.Parse(Convert.ToString(v[15].Value)),
                Station = Convert.ToDouble(v[16].Value),

                // Записи первой версии (17 значений) трассы не хранят — её даёт вид профиля.
                AlignmentHandle = v.Length > 17
                    ? RwHandles.Parse(Convert.ToString(v[17].Value))
                    : new Handle(0)
            };
        }
    }

    /// <summary>
    /// Рисование отметки по модели. Идемпотентно: существующие объекты
    /// переставляются, недостающие создаются заново, поэтому тот же вызов
    /// и строит отметку, и лечит её после правки пользователя.
    /// </summary>
    internal static class SectionMarkBuilder
    {
        public const string DefaultBlockName = "RW_sectionOnProfile";
        public const string LayerName = "RW_SECTION_MARK";

        // Теги атрибута блока, в который кладётся название сечения.
        private static readonly string[] NameTags =
            { "NAME", "НАЗВАНИЕ", "ИМЯ", "СЕЧЕНИЕ", "SECTION", "MARK", "ОТМЕТКА", "НОМЕР" };

        /// <summary>
        /// Привести отметку в соответствие с чертежом. false — то, к чему она
        /// привязана, исчезло, и отметку пора удалять.
        /// </summary>
        public static bool Sync(Transaction tr, Database db, SectionMark m)
        {
            var pv = RwHandles.Open<ProfileView>(tr, db, m.ProfileViewHandle, OpenMode.ForRead);
            var sl = RwHandles.Open<SampleLine>(tr, db, m.SampleLineHandle, OpenMode.ForRead);
            if (pv == null || sl == null) return false;

            // Запись первой версии привязана ещё и к виду сечения: стёрт он — стёрта и отметка.
            if (m.SectionViewHandle.Value != 0 &&
                RwHandles.Open<SectionView>(tr, db, m.SectionViewHandle, OpenMode.ForRead) == null)
                return false;

            var alignment = tr.GetObject(pv.AlignmentId, OpenMode.ForRead) as Alignment;
            if (alignment == null) return false;
            m.AlignmentHandle = alignment.Handle;

            m.Name = sl.Name ?? "";

            // Пикет — там, где линия выборки пересекает ТРАССУ ВИДА. Не пересекает
            // (сечение унесли или оно соседней трассы и не доходит) — рисовать
            // нечего, но запись живёт: вернут линию, и отметка встанет сама.
            double station;
            bool forward;
            if (!SectionCrossing.TryCross(tr, sl, alignment, out station, out forward))
            {
                EraseDrawn(tr, db, m);
                return true;
            }

            m.Station = station;
            bool reverse = !forward ^ m.Reverse;   // сторона по сечению, затем ручной разворот

            double eMin = pv.ElevationMin, eMax = pv.ElevationMax;
            double h = eMax - eMin;

            if (!m.TicksSet && h > 1e-9)
            {
                m.TopHigh = eMax - 0.02 * h;
                m.TopLow = eMax - 0.12 * h;
                m.BotLow = eMin + 0.02 * h;
                m.BotHigh = eMin + 0.12 * h;
                m.TicksSet = true;
            }

            // Пикет вне вида — тоже нечего рисовать, запись остаётся.
            if (m.Station < pv.StationStart || m.Station > pv.StationEnd)
            {
                EraseDrawn(tr, db, m);
                return true;
            }

            Point3d[] top = RwGeometry.ProfileSegment(pv, m.Station, m.TopLow, m.Station, m.TopHigh);
            Point3d[] bot = RwGeometry.ProfileSegment(pv, m.Station, m.BotLow, m.Station, m.BotHigh);
            Point3d tipPoint;
            if (top == null || bot == null ||
                !RwGeometry.TryPointInProfileView(pv, m.Station, eMax, out tipPoint)) return true;

            SectionMarkTag.EnsureRegApp(tr, db);
            string layer = EnsureLayer(tr, db);
            var space = (BlockTableRecord)tr.GetObject(pv.BlockId, OpenMode.ForWrite);

            m.TopTickHandle = PlaceTick(tr, db, space, layer, m, m.TopTickHandle, top, true);
            m.BotTickHandle = PlaceTick(tr, db, space, layer, m, m.BotTickHandle, bot, false);
            m.TopArrowHandle = PlaceArrow(tr, db, space, layer, m.TopArrowHandle, top, reverse);
            m.BotArrowHandle = PlaceArrow(tr, db, space, layer, m.BotArrowHandle, bot, reverse);
            m.BlockHandle = PlaceBlock(tr, db, space, layer, m, tipPoint);

            return true;
        }

        /// <summary>Подхватить высоты чёрточек, которые пользователь передвинул.</summary>
        public static void ReadTicks(Transaction tr, Database db, SectionMark m)
        {
            var pv = RwHandles.Open<ProfileView>(tr, db, m.ProfileViewHandle, OpenMode.ForRead);
            if (pv == null) return;

            double lo, hi;
            if (TryReadTick(tr, db, pv, m.TopTickHandle, out lo, out hi))
            {
                m.TopLow = lo;
                m.TopHigh = hi;
            }

            if (TryReadTick(tr, db, pv, m.BotTickHandle, out lo, out hi))
            {
                m.BotLow = lo;
                m.BotHigh = hi;
            }
        }

        /// <summary>
        /// Стереть всё нарисованное отметкой (сама запись остаётся). Хэндлы
        /// стёртого обнуляются: следующая синхронизация нарисует заново.
        /// </summary>
        public static void EraseDrawn(Transaction tr, Database db, SectionMark m)
        {
            foreach (var link in m.Links())
            {
                if (link.Value > MarkPart.BotArrow) continue;   // привязки к чертежу не рисуются

                var obj = RwHandles.Open<Entity>(tr, db, link.Key, OpenMode.ForWrite);
                if (obj != null && !obj.IsErased) obj.Erase();
            }

            var none = new Handle(0);
            m.BlockHandle = none;
            m.TopTickHandle = none;
            m.BotTickHandle = none;
            m.TopArrowHandle = none;
            m.BotArrowHandle = none;
        }

        // -----------------------------------------------------------------

        private static bool TryReadTick(
            Transaction tr, Database db, ProfileView pv, Handle h, out double lo, out double hi)
        {
            lo = hi = 0.0;
            var ln = RwHandles.Open<Line>(tr, db, h, OpenMode.ForRead);
            if (ln == null) return false;

            double s1, e1, s2, e2;
            if (!RwGeometry.TryStationInProfileView(pv, ln.StartPoint, out s1, out e1)) return false;
            if (!RwGeometry.TryStationInProfileView(pv, ln.EndPoint, out s2, out e2)) return false;

            lo = Math.Min(e1, e2);
            hi = Math.Max(e1, e2);
            return hi - lo > 1e-9;
        }

        private static Handle PlaceTick(
            Transaction tr, Database db, BlockTableRecord space, string layer,
            SectionMark m, Handle existing, Point3d[] pts, bool top)
        {
            var ln = RwHandles.Open<Line>(tr, db, existing, OpenMode.ForWrite);
            if (ln == null)
            {
                ln = new Line();
                space.AppendEntity(ln);
                tr.AddNewlyCreatedDBObject(ln, true);
            }

            ln.StartPoint = pts[0];
            ln.EndPoint = pts[1];
            ln.Layer = layer;
            SectionMarkTag.Write(ln, m.Id, top);
            return ln.Handle;
        }

        /// <summary>
        /// Стрелка приходит к середине чёрточки сбоку: хвост на пикетаже «до»,
        /// остриё в середине — по умолчанию взгляд идёт вперёд по пикетажу,
        /// как у вида сечения Civil 3D.
        /// </summary>
        private static Handle PlaceArrow(
            Transaction tr, Database db, BlockTableRecord space, string layer,
            Handle existing, Point3d[] tick, bool reverse)
        {
            double len = Math.Abs(tick[1].Y - tick[0].Y);
            double arrowLen = Math.Max(len * 0.8, 1e-6);
            double headLen = arrowLen * 0.35;
            double headWidth = arrowLen * 0.25;
            double dir = reverse ? -1.0 : 1.0;

            Point3d mid = tick[0] + (tick[1] - tick[0]) / 2.0;
            var tip = new Point2d(mid.X, mid.Y);
            var headBase = new Point2d(mid.X - dir * headLen, mid.Y);
            var tail = new Point2d(mid.X - dir * arrowLen, mid.Y);

            var pl = RwHandles.Open<Polyline>(tr, db, existing, OpenMode.ForWrite);
            if (pl == null)
            {
                pl = new Polyline();
                space.AppendEntity(pl);
                tr.AddNewlyCreatedDBObject(pl, true);
            }
            else
            {
                while (pl.NumberOfVertices > 0) pl.RemoveVertexAt(0);
            }

            // Древко — тонкий сегмент, наконечник — сегмент с шириной, сходящей на нет.
            pl.AddVertexAt(0, tail, 0.0, 0.0, 0.0);
            pl.AddVertexAt(1, headBase, 0.0, headWidth, 0.0);
            pl.AddVertexAt(2, tip, 0.0, 0.0, 0.0);
            pl.Closed = false;
            pl.Elevation = mid.Z;
            pl.Layer = layer;
            return pl.Handle;
        }

        private static Handle PlaceBlock(
            Transaction tr, Database db, BlockTableRecord space, string layer,
            SectionMark m, Point3d top)
        {
            ObjectId btrId = EnsureBlock(tr, db, m.BlockName);
            if (btrId.IsNull) btrId = EnsureBlock(tr, db, DefaultBlockName);

            var br = RwHandles.Open<BlockReference>(tr, db, m.BlockHandle, OpenMode.ForWrite);

            // Другой блок, чем в модели (пользователь выбрал иной): старый вставку заменяем.
            if (br != null && br.DynamicBlockTableRecord != btrId)
            {
                br.Erase();
                br = null;
            }

            if (br == null)
            {
                br = new BlockReference(top, btrId);
                br.Layer = layer;
                space.AppendEntity(br);
                tr.AddNewlyCreatedDBObject(br, true);
                AddAttributes(tr, br, btrId);
            }
            else
            {
                // По вертикали блок остаётся там, куда его поставили: он ходит
                // по высоте свободно, а по пикетажу привязан к сечению.
                br.Position = new Point3d(top.X, br.Position.Y, br.Position.Z);
            }

            SetName(tr, br, m.Name);
            return br.Handle;
        }

        private static void AddAttributes(Transaction tr, BlockReference br, ObjectId btrId)
        {
            var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
            foreach (ObjectId id in btr)
            {
                var def = tr.GetObject(id, OpenMode.ForRead) as AttributeDefinition;
                if (def == null || def.Constant) continue;

                var att = new AttributeReference();
                att.SetAttributeFromBlock(def, br.BlockTransform);
                br.AttributeCollection.AppendAttribute(att);
                tr.AddNewlyCreatedDBObject(att, true);
            }
        }

        private static void SetName(Transaction tr, BlockReference br, string name)
        {
            var atts = new List<AttributeReference>();
            foreach (ObjectId id in br.AttributeCollection)
            {
                var a = tr.GetObject(id, OpenMode.ForWrite) as AttributeReference;
                if (a != null) atts.Add(a);
            }

            if (atts.Count == 0) return;

            AttributeReference target = atts[0];
            foreach (string tag in NameTags)
            {
                AttributeReference hit = atts.Find(a =>
                    string.Equals(a.Tag, tag, StringComparison.OrdinalIgnoreCase));
                if (hit != null) { target = hit; break; }
            }

            target.TextString = name ?? "";
        }

        private static string EnsureLayer(Transaction tr, Database db)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(LayerName)) return LayerName;

            lt.UpgradeOpen();
            var rec = new LayerTableRecord
            {
                Name = LayerName,
                Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                    Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 1)
            };
            lt.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
            return LayerName;
        }

        /// <summary>Определение блока по имени. Свой блок по умолчанию создаётся, если его нет.</summary>
        public static ObjectId EnsureBlock(Transaction tr, Database db, string name)
        {
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            if (!string.IsNullOrEmpty(name) && bt.Has(name)) return bt[name];
            if (!string.Equals(name, DefaultBlockName, StringComparison.OrdinalIgnoreCase))
                return ObjectId.Null;

            // Заготовка: кружок с названием сечения над точкой вставки.
            bt.UpgradeOpen();
            var btr = new BlockTableRecord { Name = DefaultBlockName };
            ObjectId id = bt.Add(btr);
            tr.AddNewlyCreatedDBObject(btr, true);

            var circle = new Circle(new Point3d(0, 3, 0), Vector3d.ZAxis, 3.0);
            btr.AppendEntity(circle);
            tr.AddNewlyCreatedDBObject(circle, true);

            var def = new AttributeDefinition
            {
                Tag = "NAME",
                Prompt = "Название сечения",
                TextString = "A-A",
                Height = 2.5,
                Justify = AttachmentPoint.MiddleCenter,
                Position = new Point3d(0, 3, 0)
            };
            def.AlignmentPoint = new Point3d(0, 3, 0);
            btr.AppendEntity(def);
            tr.AddNewlyCreatedDBObject(def, true);

            return id;
        }

        /// <summary>Имена блоков, годных для вставки: без листов, внешних ссылок и анонимных.</summary>
        public static List<string> ListBlocks(Transaction tr, Database db)
        {
            var names = new List<string>();
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);

            foreach (ObjectId id in bt)
            {
                var btr = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
                if (btr.IsLayout || btr.IsAnonymous || btr.IsFromExternalReference ||
                    btr.IsFromOverlayReference || btr.Name.StartsWith("*")) continue;

                names.Add(btr.Name);
            }

            if (!names.Exists(n => string.Equals(n, DefaultBlockName, StringComparison.OrdinalIgnoreCase)))
                names.Add(DefaultBlockName);

            names.Sort(StringComparer.CurrentCultureIgnoreCase);
            return names;
        }
    }
}
