using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.DatabaseServices;
using Civil3D_commands.Shared;

namespace Civil3D_commands.AssociativeBreaks
{
    /// <summary>
    /// Создание/обновление двух прокси-линий маркера и хранение Guid маркера в XData.
    /// Прокси — обычные Line: в виде профиля (вертикальная) и в плане (ортогональ оси).
    /// </summary>
    public static class BreakProxyFactory
    {
        public const string XAppName = "RW_BREAK";

        /// <summary>Полудлина "чёрточки" в плане.</summary>
        public const double PlanProxyHalfWidth = 3.0;     // АДАПТ: полуширина ортогонали в плане

        /// <summary>
        /// Тип линии прокси в виде профиля. Штрихпунктир отличает служебную
        /// линию от геометрии чертежа. Если в чертеже такого типа нет, он
        /// подгружается из стандартного файла; не вышло — остаётся сплошная.
        /// </summary>
        public const string ProxyLinetype = "DASHDOT";

        public static void EnsureRegApp(Database db)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForWrite);
                if (!rat.Has(XAppName))
                {
                    var rec = new RegAppTableRecord { Name = XAppName };
                    rat.Add(rec);
                    tr.AddNewlyCreatedDBObject(rec, true);
                }
                tr.Commit();
            }
        }

        public static Guid? GetMarkerGuid(Autodesk.AutoCAD.DatabaseServices.Entity ent)
        {
            ResultBuffer rb = ent.GetXDataForApplication(XAppName);
            if (rb == null) return null;
            foreach (TypedValue tv in rb)
                if (tv.TypeCode == (int)DxfCode.ExtendedDataAsciiString &&
                    Guid.TryParseExact(tv.Value.ToString(), "N", out Guid g))
                    return g;
            return null;
        }

        private static void SetMarkerGuid(Autodesk.AutoCAD.DatabaseServices.Entity ent, Guid id)
        {
            ent.XData = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, XAppName),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, id.ToString("N")));
        }

        /// <summary>Создать обе прокси-линии, записать их Handle в маркер.</summary>
        public static void CreateProxies(Transaction tr, Database db, StationMarker m,
                                         ProfileView profileView, Alignment alignment)
        {
            var btr = (BlockTableRecord)tr.GetObject(
                SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);

            var profLine = new Line { Layer = m.Layer };
            var planLine = new Line { Layer = m.Layer };
            btr.AppendEntity(profLine); tr.AddNewlyCreatedDBObject(profLine, true);
            btr.AppendEntity(planLine); tr.AddNewlyCreatedDBObject(planLine, true);

            SetMarkerGuid(profLine, m.Id);
            SetMarkerGuid(planLine, m.Id);
            m.ProfileProxyHandle = profLine.Handle;
            m.PlanProxyHandle = planLine.Handle;

            UpdateProxyGeometry(tr, m, profileView, alignment);

            // Наверх порядка отрисовки: профильная линия лежит внутри вида
            // профиля, и под ним её труднее и увидеть, и подцепить.
            BringToFront(tr, db, profLine.ObjectId);
            BringToFront(tr, db, planLine.ObjectId);
        }

        /// <summary>Пересчитать координаты обеих прокси-линий из пикета маркера.</summary>
        public static void UpdateProxyGeometry(Transaction tr, StationMarker m,
                                               ProfileView profileView, Alignment alignment)
        {
            // --- Вид профиля: вертикаль во всю высоту вида ---
            ObjectId profProxyId = ResolveId(m.ProfileProxyHandle);
            if (!profProxyId.IsNull && profileView != null)
            {
                Point3d[] pts = ProfilePoints(profileView, m);

                if (pts != null)
                {
                    var ln = (Line)tr.GetObject(profProxyId, OpenMode.ForWrite);
                    ln.StartPoint = pts[0];
                    ln.EndPoint = pts[1];
                    ApplyProxyLinetype(tr, ln);
                }
            }

            // --- План: перпендикуляр к оси в пикете m.Station ---
            ObjectId planProxyId = ResolveId(m.PlanProxyHandle);
            if (!planProxyId.IsNull && alignment != null)
            {
                Point3d[] pts = PlanPoints(alignment, m.Station);

                if (pts != null)
                {
                    var ln = (Line)tr.GetObject(planProxyId, OpenMode.ForWrite);
                    ln.StartPoint = pts[0];
                    ln.EndPoint = pts[1];
                }
            }
        }

        /// <summary>
        /// Геометрия профильного прокси: вертикаль **во всю высоту вида**.
        /// Не «отметка низа плюс два метра»: разрыв делит вид целиком, и
        /// короткая чёрточка у основания терялась среди геометрии.
        ///
        /// Отметки берутся с отступом внутрь диапазона вида. Ровно на границе
        /// `FindXYAtStationAndElevation` возвращает NaN, отрезок не строился
        /// вовсе, и линия оставалась нулевой длины — невидимой и невыбираемой.
        /// Именно так и выглядела «пропавшая» линия на профиле.
        ///
        /// Запасной вариант — от отметки низа вверх: лучше короткая линия,
        /// чем никакой.
        /// </summary>
        public static Point3d[] ProfilePoints(ProfileView view, StationMarker m)
        {
            if (view == null) return null;

            double lo = view.ElevationMin;
            double hi = view.ElevationMax;
            if (hi < lo) { double t = lo; lo = hi; hi = t; }

            // X берём по пикету, на заведомо внутренней отметке: ровно на границе
            // диапазона FindXYAtStationAndElevation возвращает NaN.
            Point3d anchor;
            if (!RwGeometry.TryPointInProfileView(view, m.Station, (lo + hi) / 2.0, out anchor))
                return null;

            // Y — по фактическим габаритам вида. ElevationMin/ElevationMax дают
            // диапазон ОТМЕТОК, и построенная по ним вертикаль оказывалась заметно
            // короче самого вида: сетка рисуется с запасом, а вертикальное
            // преувеличение к отметкам отношения не имеет.
            try
            {
                Extents3d ext = view.GeometricExtents;

                double yLo = Math.Min(ext.MinPoint.Y, ext.MaxPoint.Y);
                double yHi = Math.Max(ext.MinPoint.Y, ext.MaxPoint.Y);

                if (yHi - yLo > 1e-6)
                    return new[]
                    {
                        new Point3d(anchor.X, yLo, 0.0),
                        new Point3d(anchor.X, yHi, 0.0)
                    };
            }
            catch (System.Exception)
            {
                // Габаритов нет (вид только что создан и не отрисован) —
                // строим по диапазону отметок, как раньше.
            }

            double inset = Math.Max((hi - lo) * 1e-4, 1e-6);

            Point3d[] pts = RwGeometry.ProfileSegment(
                view, m.Station, lo + inset, m.Station, hi - inset);

            if (pts != null) return pts;

            return RwGeometry.ProfileSegment(
                view, m.Station, m.BaseElevation, m.Station, m.BaseElevation + FallbackHeight);
        }

        /// <summary>Высота запасной вертикали, если во всю высоту вида не вышло.</summary>
        private const double FallbackHeight = 2.0;

        /// <summary>
        /// Геометрия планового прокси: **начало на оси**, конец в стороне на
        /// PlanProxyHalfWidth. Перпендикулярность обеспечивает сам PointLocation —
        /// смещение у него отмеряется по нормали к оси, поэтому на кривых
        /// ничего не перекашивает. Прежний численный поворот касательной врал
        /// на малых радиусах и в конце оси.
        ///
        /// Начало именно на оси, а не посередине: за него линия и «держится»
        /// при перетаскивании (см. BreakGripOverrule).
        /// </summary>
        public static Point3d[] PlanPoints(Alignment alignment, double station)
        {
            return RwGeometry.PlanSegment(alignment, station, 0.0, station, PlanProxyHalfWidth);
        }

        /// <summary>
        /// Положение прокси в плане -> пикет на оси. NaN, если точка вне оси
        /// (прокси увели слишком вбок) — вызывающий оставляет пикет как был.
        /// </summary>
        public static double PlanPointToStation(Alignment alignment, Point3d planPoint)
        {
            double station;
            return RwGeometry.TryStationOnAlignment(alignment, planPoint, out station)
                ? station
                : double.NaN;
        }

        /// <summary>Положение прокси в виде профиля -> пикет. NaN, если точка вне вида.</summary>
        public static double ProfilePointToStation(ProfileView pv, Point3d viewPoint)
        {
            double station;
            return RwGeometry.TryStationInProfileView(pv, viewPoint, out station)
                ? station
                : double.NaN;
        }

        /// <summary>
        /// Поднять прокси на самый верх порядка отрисовки.
        ///
        /// Профильная линия лежит целиком внутри вида профиля, и если вид
        /// нарисован поверх неё, щелчок попадает в вид, а не в линию: выбрать
        /// прокси и взяться за его ручку становится невозможно. В плане такой
        /// беды нет — там поверх линии ничего не лежит.
        /// </summary>
        public static void BringToFront(Transaction tr, Database db, ObjectId lineId)
        {
            if (lineId.IsNull) return;

            try
            {
                var ent = tr.GetObject(lineId, OpenMode.ForRead) as Autodesk.AutoCAD.DatabaseServices.Entity;
                if (ent == null) return;

                var owner = tr.GetObject(ent.BlockId, OpenMode.ForRead) as BlockTableRecord;
                if (owner == null) return;

                var order = tr.GetObject(owner.DrawOrderTableId, OpenMode.ForWrite) as DrawOrderTable;
                if (order == null) return;

                using (var ids = new ObjectIdCollection())
                {
                    ids.Add(lineId);
                    order.MoveToTop(ids);
                }
            }
            catch (System.Exception)
            {
                // Порядок отрисовки — удобство выбора, а не механика разрывов.
            }
        }

        /// <summary>
        /// Назначить прокси штрихпунктир. Тип линии в чертеже может
        /// отсутствовать — тогда он подгружается из acad.lin/acadiso.lin.
        /// Ничего не вышло — линия остаётся сплошной, это не повод падать.
        /// </summary>
        private static void ApplyProxyLinetype(Transaction tr, Line line)
        {
            try
            {
                Database db = line.Database;
                if (db == null) return;

                var ltt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);

                if (!ltt.Has(ProxyLinetype))
                {
                    foreach (string file in new[] { "acadiso.lin", "acad.lin" })
                    {
                        try
                        {
                            db.LoadLineTypeFile(ProxyLinetype, file);
                            break;
                        }
                        catch (System.Exception) { }
                    }

                    ltt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
                    if (!ltt.Has(ProxyLinetype)) return;
                }

                ObjectId ltId = ltt[ProxyLinetype];
                if (line.LinetypeId != ltId) line.LinetypeId = ltId;
            }
            catch (System.Exception)
            {
                // Тип линии — оформление, а не механика разрывов.
            }
        }

        private static ObjectId ResolveId(Handle h)
        {
            var db = Autodesk.AutoCAD.ApplicationServices.Application
                        .DocumentManager.MdiActiveDocument.Database;
            return RwHandles.Resolve(db, h);
        }
    }
}
