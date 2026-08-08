using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.DatabaseServices;

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
            return overruledSubject is Line ln && BreakProxyFactory.GetMarkerGuid(ln).HasValue;
        }

        public override void GetGripPoints(Entity entity, GripDataCollection grips,
            double curViewUnitSize, int gripSize, Vector3d curViewDir, GetGripPointsFlags bitFlags)
        {
            var session = BreakSession.Current;
            var guid = BreakProxyFactory.GetMarkerGuid(entity);
            if (session == null || guid == null) return;

            var m = session.Store.Get(guid.Value);
            if (m == null) return;
            if (!session.IsEditMode(m.ProfileHandle)) return; // вне режима — без грипс

            var ln = (Line)entity;
            grips.Add(new BreakGrip(m.Id, (ln.StartPoint + ln.EndPoint.GetAsVector()) / 2.0));
        }

        // Перемещение выполняем сами в BreakGrip.MoveGripPointsAt-эквиваленте ниже
        // через стандартный механизм: AutoCAD вызовет MoveGripPointsAt у overrule.
        public override void MoveGripPointsAt(Entity entity, GripDataCollection grips,
            Vector3d offset, MoveGripPointsFlags bitFlags)
        {
            var session = BreakSession.Current;
            var guid = BreakProxyFactory.GetMarkerGuid(entity);
            if (session == null || guid == null) { base.MoveGripPointsAt(entity, grips, offset, bitFlags); return; }

            var m = session.Store.Get(guid.Value);
            if (m == null) { base.MoveGripPointsAt(entity, grips, offset, bitFlags); return; }

            var ln = (Line)entity;
            Point3d gripPt = (ln.StartPoint + ln.EndPoint.GetAsVector()) / 2.0;
            Point3d desired = gripPt + offset;

            using (var tr = entity.Database.TransactionManager.StartTransaction())
            {
                ProfileView pv = Resolve<ProfileView>(tr, m.ProfileViewHandle);
                Alignment al = Resolve<Alignment>(tr, m.AlignmentHandle);

                // Желаемый пикет в зависимости от вида.
                double wantStation = (entity.Handle == m.PlanProxyHandle && al != null)
                    ? BreakProxyFactory.PlanPointToStation(al, desired)
                    : (pv != null ? BreakProxyFactory.ProfilePointToStation(pv, desired) : m.Station);
                if (double.IsNaN(wantStation)) wantStation = m.Station; // точка вне оси — не двигаем

                // Кламп между соседями.
                Baseline bl = session.GetBaseline(tr, m);
                double bStart = bl?.StartStation ?? 0;
                double bEnd = bl?.EndStation ?? wantStation;
                var (min, max) = session.Store.GetMoveBounds(m, Buffer, bStart, bEnd);
                double clamped = Math.Min(Math.Max(wantStation, min), max);

                // Дешёвое визуальное перемещение ОБЕИХ прокси на новый пикет.
                m.Station = clamped;
                BreakProxyFactory.UpdateProxyGeometry(tr, m, pv, al);
                tr.Commit();
            }
            // Тяжёлый ресинк выполнит реактор в CommandEnded (видит изменение прокси).
        }

        private static T Resolve<T>(Transaction tr, Handle h) where T : class
        {
            var db = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument.Database;
            if (h.Value == 0 || !db.TryGetObjectId(h, out ObjectId id) || id.IsNull) return null;
            return tr.GetObject(id, OpenMode.ForRead) as T;
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
