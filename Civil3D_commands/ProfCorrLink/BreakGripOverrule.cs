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

                var ln = (Line)entity;
                grips.Add(new BreakGrip(m.Id,
                    (ln.StartPoint + ln.EndPoint.GetAsVector()) / 2.0));
            }
            catch { /* не роняем AutoCAD из-за ошибки в оверруле */ }
        }

        public override void MoveGripPointsAt(Autodesk.AutoCAD.DatabaseServices.Entity entity, GripDataCollection grips,
            Vector3d offset, MoveGripPointsFlags bitFlags)
        {
            // Отдаём перемещение AutoCAD — grip-система двигает линию стандартно.
            // Ресинк профиля / областей / второго прокси выполнит реактор в CommandEnded.
            try { base.MoveGripPointsAt(entity, grips, offset, bitFlags); }
            catch { }
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
