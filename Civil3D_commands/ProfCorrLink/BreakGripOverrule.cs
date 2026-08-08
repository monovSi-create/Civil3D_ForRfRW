using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.DatabaseServices;
using Civil3D_commands.Shared;

namespace Civil3D_commands.AssociativeBreaks
{
    /// <summary>
    /// Кастомная грипса: одна точка на прокси-линию.
    /// - в виде профиля двигается по горизонтали (меняется только пикет);
    /// - в плане двигается вдоль оси (через проекцию пикета);
    /// - перемещение зажимается между соседними разрывами с буфером (0.1 м).
    /// Грипсы выдаются только в режиме редактирования.
    /// Тяжёлый ресинк (профиль/области/коридор) выполняется НЕ здесь, а в реакторе по
    /// окончании команды растягивания — здесь только дешёвое перемещение линии.
    /// </summary>
    public class BreakGripOverrule : GripOverrule
    {
        public static double Buffer = 0.1; // буфер, чтобы область не схлопнулась

        public override bool IsApplicable(RXObject overruledSubject)
        {
            // Защита от краша: Civil-объекты (Profile и др.) могут попасть сюда
            // через внутренние примитивы — перехватываем любое исключение.
            try
            {
                if (!(overruledSubject is Line ln)) return false;
                return BreakProxyFactory.GetMarkerGuid(ln).HasValue;
            }
            catch { return false; }
        }

        public override void GetGripPoints(Autodesk.AutoCAD.DatabaseServices.Entity entity, GripDataCollection grips,
            double curViewUnitSize, int gripSize, Vector3d curViewDir, GetGripPointsFlags bitFlags)
        {
            try
            {
                var session = BreakSession.Current;
                if (session == null) return;

                Guid? guid = BreakProxyFactory.GetMarkerGuid(entity);
                if (guid == null) return;
                if (entity.ObjectId.IsNull) return;

                // Владельца ищем по хэндлу, а не по Guid из XData: у копии прокси
                // Guid тот же, и ручка на ней двигала бы чужой разрыв.
                var m = session.Store.GetByProxy(entity.ObjectId.Handle);
                if (m == null || !m.OwnsProxy(entity.ObjectId.Handle)) return;

                // Грипса видна только в режиме редактирования.
                if (!session.IsEditMode(m.ProfileHandle)) return;

                grips.Add(new BreakGrip(m.Id, RwGeometry.Midpoint((Line)entity)));
            }
            catch { /* не роняем AutoCAD из-за ошибки в оверруле */ }
        }

        public override void MoveGripPointsAt(Autodesk.AutoCAD.DatabaseServices.Entity entity, GripDataCollection grips,
            Vector3d offset, MoveGripPointsFlags bitFlags)
        {
            // Ресинк профиля / областей / второго прокси выполнит реактор
            // в CommandEnded; здесь только сама картинка перетаскивания.
            try
            {
                var session = BreakSession.Current;
                var line = entity as Line;

                StationMarker m = (session != null && line != null && !entity.ObjectId.IsNull)
                    ? session.Store.GetByProxy(entity.ObjectId.Handle)
                    : null;

                // В плане линия не едет за курсором жёстко, а пересобирается
                // на новом пикете — так она остаётся перпендикуляром к оси.
                if (m != null && entity.ObjectId.Handle == m.PlanProxyHandle &&
                    DragAlongAlignment(session, line, m, offset))
                    return;

                base.MoveGripPointsAt(entity, grips, Constrain(entity, offset), bitFlags);
            }
            catch { }
        }

        /// <summary>
        /// Перетаскивание в плане: точку под курсором сносим на ось, получаем
        /// пикет и строим перпендикуляр заново. Линия при этом всё время
        /// стоит началом на оси и перпендикулярна ей — на кривых тоже, потому
        /// что нормаль отмеряет сам Civil.
        ///
        /// Геометрия ставится прямо здесь, а не через base: именно её AutoCAD
        /// и рисует как образ перетаскивания, поэтому пользователь видит
        /// итоговое положение сразу, а не после отпускания кнопки.
        ///
        /// false — не смогли (нет оси, точка вне её): тогда работает обычное
        /// перемещение, а модель поправит реактор.
        /// </summary>
        private bool DragAlongAlignment(BreakSession session, Line line, StationMarker m, Vector3d offset)
        {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application
                        .DocumentManager.MdiActiveDocument;
            if (doc == null) return false;

            try
            {
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    var al = RwHandles.Open<Alignment>(
                        tr, doc.Database, m.AlignmentHandle, OpenMode.ForRead);

                    if (al == null) { tr.Commit(); return false; }

                    // Тянут за середину линии, а держится она началом на оси:
                    // сносим на ось именно ту точку, что оказалась под курсором.
                    Point3d target = RwGeometry.Midpoint(line) + offset;

                    double station;
                    bool ok = RwGeometry.TryStationOnAlignment(al, target, out station);
                    Point3d[] pts = null;

                    if (ok)
                    {
                        double lo = al.StartingStation;
                        double hi = al.EndingStation;

                        Baseline bl = session.GetBaseline(tr, m);
                        if (bl != null)
                        {
                            lo = Math.Max(lo, bl.StartStation);
                            hi = Math.Min(hi, bl.EndStation);
                        }

                        pts = BreakProxyFactory.PlanPoints(
                            al, RwGeometry.Clamp(station, lo, hi));
                    }

                    if (pts != null)
                    {
                        line.StartPoint = pts[0];
                        line.EndPoint = pts[1];
                    }

                    tr.Commit();
                    return pts != null;
                }
            }
            catch (System.Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Разрыв живёт на пикете, а пикет в виде профиля — это координата X.
        /// Поэтому вертикальная составляющая перемещения отбрасывается: линия
        /// ходит строго горизонтально и остаётся во всю высоту вида.
        ///
        /// Горизонталь дополнительно зажимается пределами вида профиля и
        /// участка коридора. Модель клампится ещё раз в оркестраторе (там же
        /// учитываются соседние разрывы), но без этого линию было видно
        /// уехавшей за край вида прямо во время перетаскивания.
        /// </summary>
        private Vector3d Constrain(Autodesk.AutoCAD.DatabaseServices.Entity entity, Vector3d offset)
        {
            var session = BreakSession.Current;
            if (session == null || entity.ObjectId.IsNull) return offset;

            var m = session.Store.GetByProxy(entity.ObjectId.Handle);
            if (m == null) return offset;

            // В плане ось может идти как угодно, «горизонтально» там смысла
            // не имеет — ограничение только для вида профиля.
            if (entity.ObjectId.Handle != m.ProfileProxyHandle) return offset;

            var flat = new Vector3d(offset.X, 0.0, 0.0);

            var doc = Autodesk.AutoCAD.ApplicationServices.Application
                        .DocumentManager.MdiActiveDocument;
            if (doc == null) return flat;

            try
            {
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    var pv = RwHandles.Open<ProfileView>(
                        tr, doc.Database, m.ProfileViewHandle, OpenMode.ForRead);

                    if (pv == null) { tr.Commit(); return flat; }

                    var line = entity as Line;
                    if (line == null) { tr.Commit(); return flat; }

                    Point3d mid = RwGeometry.Midpoint(line);
                    Vector3d result;

                    double station;
                    if (!RwGeometry.TryStationInProfileView(pv, mid + flat, out station))
                    {
                        // Утащили за пределы вида — дальше не пускаем.
                        result = new Vector3d(0.0, 0.0, 0.0);
                    }
                    else
                    {
                        double lo = pv.StationStart;
                        double hi = pv.StationEnd;

                        Baseline bl = session.GetBaseline(tr, m);
                        if (bl != null)
                        {
                            lo = Math.Max(lo, bl.StartStation);
                            hi = Math.Min(hi, bl.EndStation);
                        }

                        double clamped = RwGeometry.Clamp(station, lo, hi);

                        if (Math.Abs(clamped - station) < 1e-9)
                        {
                            result = flat;
                        }
                        else
                        {
                            // Пикет зажат — пересчитываем смещение под него.
                            Point3d edge;
                            result = RwGeometry.TryPointInProfileView(
                                         pv, clamped, pv.ElevationMin, out edge)
                                ? new Vector3d(edge.X - mid.X, 0.0, 0.0)
                                : new Vector3d(0.0, 0.0, 0.0);
                        }
                    }

                    tr.Commit();
                    return result;
                }
            }
            catch (System.Exception)
            {
                return flat;
            }
        }
    }

    /// <summary>Единственная грипса прокси.</summary>
    public class BreakGrip : GripData
    {
        private readonly Guid _markerId;
        public BreakGrip(Guid markerId, Point3d point)
        {
            _markerId = markerId;
            GripPoint = point;
        }

        public override bool ViewportDraw(ViewportDraw worldDraw, ObjectId entityId,
            DrawType type, Point3d? imageGripPoint, int gripSizeInPixels)
        {
            return false; // используем стандартную отрисовку грипсы
        }
    }
}
