using System;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.DatabaseServices;
// Line есть только в AutoCAD-пространстве, но алиас ставим по общему правилу файла.
using Line = Autodesk.AutoCAD.DatabaseServices.Line;

namespace Civil3D_commands.Shared
{
    /// <summary>
    /// Перевод «пикет ↔ точка» в плане и в виде продольного профиля.
    ///
    /// Оба модуля делают это постоянно и делали по-разному. Существенные
    /// расхождения, ради которых код и сведён:
    ///
    /// 1. **Перпендикуляр к оси.** Здесь он получается сам собой: у
    ///    <see cref="Alignment.PointLocation(double,double,ref double,ref double)"/>
    ///    смещение отмеряется по нормали к оси. Прежний численный способ в
    ///    ProfCorrLink (точка на полметра вперёд, поворот касательной на 90°)
    ///    грубее на кривых малого радиуса, а в конце оси давал нулевой вектор
    ///    и подставлял ось X — то есть перпендикуляр смотрел куда попало.
    ///
    /// 2. **Выход за пределы объекта.** `Alignment.StationOffset` за пределами
    ///    оси бросает, `ProfileView.Find*` возвращает NaN. Проверяется и то,
    ///    и другое, в каждом методе: половина прежних реализаций ловила лишь
    ///    что-то одно.
    ///
    /// Ошибка здесь — это всегда false или null, никогда исключение: почти все
    /// вызовы идут из обработчиков грипс, где исключение убивает сеанс
    /// редактирования.
    ///
    /// Сигнатуры Civil 3D 2024: `PointLocation(station, offset, ref x, ref y)`,
    /// `StationOffset(x, y, ref station, ref offset)`,
    /// `FindXYAtStationAndElevation(station, elevation, ref x, ref y)`,
    /// `FindStationAndElevationAtXY(x, y, ref station, ref elevation)` — везде
    /// `ref`, не `out`.
    /// </summary>
    public static class RwGeometry
    {
        // =================================================================
        //  ПЛАН
        // =================================================================

        /// <summary>
        /// Точка на трассе: пикет + смещение по нормали к оси.
        /// false, если пикет вне трассы.
        /// </summary>
        public static bool TryPointOnAlignment(
            Alignment alignment, double station, double offset, out Point3d point)
        {
            point = Point3d.Origin;
            if (alignment == null) return false;

            try
            {
                double x = 0.0, y = 0.0;
                alignment.PointLocation(station, offset, ref x, ref y);

                if (double.IsNaN(x) || double.IsNaN(y)) return false;

                point = new Point3d(x, y, 0.0);
                return true;
            }
            catch (System.Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Отрезок в плане по двум парам «пикет + смещение». null, если хотя бы
        /// один конец не построить.
        ///
        /// Одинаковые пикеты дают перпендикуляр к оси (маркер границы, прокси
        /// разрыва), одинаковые смещения — хорду вдоль оси (место блока).
        /// </summary>
        public static Point3d[] PlanSegment(
            Alignment alignment,
            double station1, double offset1,
            double station2, double offset2)
        {
            Point3d p1, p2;
            if (!TryPointOnAlignment(alignment, station1, offset1, out p1)) return null;
            if (!TryPointOnAlignment(alignment, station2, offset2, out p2)) return null;

            return new[] { p1, p2 };
        }

        /// <summary>Точка плана → пикет. false, если точку не спроецировать на ось.</summary>
        public static bool TryStationOnAlignment(
            Alignment alignment, Point3d point, out double station)
        {
            double offset;
            return TryStationOffsetOnAlignment(alignment, point, out station, out offset);
        }

        /// <summary>
        /// Точка плана → пикет и смещение. За пределами оси `StationOffset`
        /// возвращает NaN или бросает — и то и другое означает false.
        /// </summary>
        public static bool TryStationOffsetOnAlignment(
            Alignment alignment, Point3d point, out double station, out double offset)
        {
            station = 0.0;
            offset = 0.0;
            if (alignment == null) return false;

            try
            {
                double s = 0.0, o = 0.0;
                alignment.StationOffset(point.X, point.Y, ref s, ref o);

                if (double.IsNaN(s)) return false;

                station = s;
                offset = o;
                return true;
            }
            catch (System.Exception)
            {
                return false;
            }
        }

        // =================================================================
        //  ВИД ПРОФИЛЯ
        // =================================================================

        /// <summary>
        /// Пикет и отметка → точка в координатах вида. Вертикальное
        /// преувеличение вида учитывается самим Civil.
        /// </summary>
        public static bool TryPointInProfileView(
            ProfileView view, double station, double elevation, out Point3d point)
        {
            point = Point3d.Origin;
            if (view == null) return false;

            try
            {
                double x = 0.0, y = 0.0;
                view.FindXYAtStationAndElevation(station, elevation, ref x, ref y);

                if (double.IsNaN(x) || double.IsNaN(y)) return false;

                point = new Point3d(x, y, 0.0);
                return true;
            }
            catch (System.Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Отрезок в виде профиля по двум парам «пикет + отметка». null, если
        /// хотя бы один конец не построить.
        ///
        /// Одинаковые пикеты дают вертикаль (маркер границы, прокси разрыва),
        /// одинаковые отметки — горизонталь (отрезок-ручка ряда), разные и то
        /// и другое — диагональ габарита (контур блока).
        /// </summary>
        public static Point3d[] ProfileSegment(
            ProfileView view,
            double station1, double elevation1,
            double station2, double elevation2)
        {
            Point3d p1, p2;
            if (!TryPointInProfileView(view, station1, elevation1, out p1)) return null;
            if (!TryPointInProfileView(view, station2, elevation2, out p2)) return null;

            return new[] { p1, p2 };
        }

        /// <summary>Точка вида → пикет. false, если точка вне вида.</summary>
        public static bool TryStationInProfileView(
            ProfileView view, Point3d point, out double station)
        {
            double elevation;
            return TryStationInProfileView(view, point, out station, out elevation);
        }

        /// <summary>Точка вида → пикет и отметка. За пределами вида возвращается NaN.</summary>
        public static bool TryStationInProfileView(
            ProfileView view, Point3d point, out double station, out double elevation)
        {
            station = 0.0;
            elevation = 0.0;
            if (view == null) return false;

            try
            {
                double s = 0.0, e = 0.0;
                view.FindStationAndElevationAtXY(point.X, point.Y, ref s, ref e);

                if (double.IsNaN(s) || double.IsNaN(e)) return false;

                station = s;
                elevation = e;
                return true;
            }
            catch (System.Exception)
            {
                return false;
            }
        }

        // =================================================================
        //  ОБЩЕЕ
        // =================================================================

        /// <summary>Зажать значение в отрезок. Границы не переставляются местами.</summary>
        public static double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        /// <summary>
        /// Середина отрезка. Носитель ручек — служебный Line, и «где он стоит»
        /// в обоих модулях означает именно его середину.
        /// </summary>
        public static Point3d Midpoint(Line line)
        {
            if (line == null) return Point3d.Origin;
            return line.StartPoint + (line.EndPoint - line.StartPoint) / 2.0;
        }
    }
}
