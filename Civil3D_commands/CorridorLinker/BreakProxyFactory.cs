using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.DatabaseServices;

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

        public static Guid? GetMarkerGuid(Entity ent)
        {
            ResultBuffer rb = ent.GetXDataForApplication(XAppName);
            if (rb == null) return null;
            foreach (TypedValue tv in rb)
                if (tv.TypeCode == (int)DxfCode.ExtendedDataAsciiString &&
                    Guid.TryParseExact(tv.Value.ToString(), "N", out Guid g))
                    return g;
            return null;
        }

        private static void SetMarkerGuid(Entity ent, Guid id)
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
                double xBot = 0, yBot = 0, xTop = 0, yTop = 0;
                // 2024: ProfileView.FindXYAtStationAndElevation(station, elevation, ref x, ref y)
                profileView.FindXYAtStationAndElevation(m.Station, m.BaseElevation, ref xBot, ref yBot);
                profileView.FindXYAtStationAndElevation(
                    m.Station, m.BaseElevation + ProfileProxyHalfHeight, ref xTop, ref yTop);

                var ln = (Line)tr.GetObject(profProxyId, OpenMode.ForWrite);
                ln.StartPoint = new Point3d(xBot, yBot, 0);
                ln.EndPoint = new Point3d(xTop, yTop, 0);
            }

            // --- План: ортогональ к оси в пикете m.Station ---
            ObjectId planProxyId = ResolveId(m.PlanProxyHandle);
            if (!planProxyId.IsNull && alignment != null)
            {
                GetPlanOrthoSegment(alignment, m.Station, PlanProxyHalfWidth,
                                    out Point3d p1, out Point3d p2);
                var ln = (Line)tr.GetObject(planProxyId, OpenMode.ForWrite);
                ln.StartPoint = p1;
                ln.EndPoint = p2;
            }
        }

        /// <summary>Точки ортогонального отрезка к оси в заданном пикете.</summary>
        public static void GetPlanOrthoSegment(Alignment alignment, double station, double half,
                                               out Point3d left, out Point3d right)
        {
            double e0 = 0, n0 = 0, eA = 0, nA = 0;
            // 2024: Alignment.PointLocation(station, offset, ref easting(X), ref northing(Y))
            alignment.PointLocation(station, 0.0, ref e0, ref n0);
            double ahead = Math.Min(station + 0.5, alignment.EndingStation);
            alignment.PointLocation(ahead, 0.0, ref eA, ref nA);

            var tangent = new Vector3d(eA - e0, nA - n0, 0);
            if (tangent.IsZeroLength()) tangent = Vector3d.XAxis;
            tangent = tangent.GetNormal();
            var ortho = tangent.RotateBy(Math.PI / 2.0, Vector3d.ZAxis);

            var center = new Point3d(e0, n0, 0);
            left = center - ortho * half;
            right = center + ortho * half;
        }

        /// <summary>Обратное преобразование: положение прокси в плане -> пикет на оси.</summary>
        public static double PlanPointToStation(Alignment alignment, Point3d planPoint)
        {
            double station = 0, offset = 0;
            try
            {
                // 2024: Alignment.StationOffset(easting(X), northing(Y), ref station, ref offset)
                alignment.StationOffset(planPoint.X, planPoint.Y, ref station, ref offset);
            }
            catch (System.Exception)
            {
                // Точка вне оси (увели прокси слишком вбок) — оставляем пикет как был.
                return double.NaN;
            }
            return station;
        }

        /// <summary>Положение прокси в виде профиля -> пикет.</summary>
        public static double ProfilePointToStation(ProfileView pv, Point3d viewPoint)
        {
            double station = 0, elevation = 0;
            pv.FindStationAndElevationAtXY(viewPoint.X, viewPoint.Y, ref station, ref elevation);
            return station;
        }

        private static ObjectId ResolveId(Handle h)
        {
            var db = Autodesk.AutoCAD.ApplicationServices.Application
                        .DocumentManager.MdiActiveDocument.Database;
            return (h.Value != 0 && db.TryGetObjectId(h, out ObjectId id)) ? id : ObjectId.Null;
        }
    }
}
