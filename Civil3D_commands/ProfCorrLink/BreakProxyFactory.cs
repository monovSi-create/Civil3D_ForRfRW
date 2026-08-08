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
        }

        /// <summary>Пересчитать координаты обеих прокси-линий из пикета маркера.</summary>
        public static void UpdateProxyGeometry(Transaction tr, StationMarker m,
                                               ProfileView profileView, Alignment alignment)
        {
            // --- Вид профиля: вертикаль во всю высоту вида ---
            // Не «отметка низа плюс два метра»: разрыв делит вид целиком, и
            // короткая чёрточка у основания заставляла целиться в неё мышью.
            ObjectId profProxyId = ResolveId(m.ProfileProxyHandle);
            if (!profProxyId.IsNull && profileView != null)
            {
                Point3d[] pts = RwGeometry.ProfileSegment(
                    profileView,
                    m.Station, profileView.ElevationMin,
                    m.Station, profileView.ElevationMax);

                if (pts != null)
                {
                    var ln = (Line)tr.GetObject(profProxyId, OpenMode.ForWrite);
                    ln.StartPoint = pts[0];
                    ln.EndPoint = pts[1];
                    ApplyProxyLinetype(tr, ln);
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
