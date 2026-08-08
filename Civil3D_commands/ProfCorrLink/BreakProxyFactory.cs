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
        // Полувысота линии в виде профиля и полудлина "чёрточки" в плане.
        public const double ProfileProxyHalfHeight = 2.0; // АДАПТ: подберите под масштаб вида
        public const double PlanProxyHalfWidth = 3.0;     // АДАПТ: полуширина ортогонали в плане

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
        }

        /// <summary>Пересчитать координаты обеих прокси-линий из пикета маркера.</summary>
        public static void UpdateProxyGeometry(Transaction tr, StationMarker m,
                                               ProfileView profileView, Alignment alignment)
        {
            // --- Вид профиля: вертикальная линия в точке (station, base..base+halfH) ---
            ObjectId profProxyId = ResolveId(m.ProfileProxyHandle);
            if (!profProxyId.IsNull)
            {
                Point3d[] pts = RwGeometry.ProfileSegment(
                    profileView,
                    m.Station, m.BaseElevation,
                    m.Station, m.BaseElevation + ProfileProxyHalfHeight);

                if (pts != null)
                {
                    var ln = (Line)tr.GetObject(profProxyId, OpenMode.ForWrite);
                    ln.StartPoint = pts[0];
                    ln.EndPoint = pts[1];
                }
            }

            // --- План: ортогональ к оси в пикете m.Station ---
            // Перпендикулярность обеспечивает сам PointLocation: смещение у него
            // отмеряется по нормали к оси. Прежний численный поворот касательной
            // врал на малых радиусах и в конце оси.
            ObjectId planProxyId = ResolveId(m.PlanProxyHandle);
            if (!planProxyId.IsNull && alignment != null)
            {
                Point3d[] pts = RwGeometry.PlanSegment(
                    alignment,
                    m.Station, -PlanProxyHalfWidth,
                    m.Station, PlanProxyHalfWidth);

                if (pts != null)
                {
                    var ln = (Line)tr.GetObject(planProxyId, OpenMode.ForWrite);
                    ln.StartPoint = pts[0];
                    ln.EndPoint = pts[1];
                }
            }
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

        private static ObjectId ResolveId(Handle h)
        {
            var db = Autodesk.AutoCAD.ApplicationServices.Application
                        .DocumentManager.MdiActiveDocument.Database;
            return RwHandles.Resolve(db, h);
        }
    }
}
