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
        /// Тип линии и слой прокси задаёт <see cref="BreakOverlay"/>: прокси —
        /// это и есть границы участков, а они обязаны выглядеть одинаково
        /// с концевыми границами коридора и блокироваться вместе с ними.
        /// Прежде здесь был свой штрихпунктир на слое «0».
        /// </summary>
        public const string ProxyLinetype = BreakOverlay.BoundaryLinetype;

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

            var profLine = new Line();
            var planLine = new Line();
            btr.AppendEntity(profLine); tr.AddNewlyCreatedDBObject(profLine, true);
            btr.AppendEntity(planLine); tr.AddNewlyCreatedDBObject(planLine, true);

            // Слой границ, а не m.Layer: на нём же лежат концевые границы
            // коридора, и он же блокируется вне режима редактирования.
            ObjectId boundaryLayer = BreakOverlay.EnsureLayer(tr, db, BreakOverlay.BoundaryLayer, 7);
            profLine.LayerId = boundaryLayer;
            planLine.LayerId = boundaryLayer;

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
                    BreakOverlay.ApplyBoundaryLinetype(tr, ln.Database, ln);
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
                    BreakOverlay.ApplyBoundaryLinetype(tr, ln.Database, ln);
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

        private static ObjectId ResolveId(Handle h)
        {
            var db = Autodesk.AutoCAD.ApplicationServices.Application
                        .DocumentManager.MdiActiveDocument.Database;
            return RwHandles.Resolve(db, h);
        }
    }
}
