using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.DatabaseServices;

namespace Civil3D_commands.AssociativeBreaks
{
    /// <summary>
    /// Операции над геометрией: ступени продольного профиля (PVI) и границы областей коридора.
    ///
    /// Разрыв на пикете S раздвигает соседние области на полузазор в каждую сторону:
    /// левая кончается на S-0.0005, правая начинается на S+0.0005. Участки перестают быть
    /// непрерывными — каждый строится сам по себе. Ступень профиля ставится ровно в этот
    /// зазор: PVI на S-0.0005 (низ) и S+0.0005 (верх), поэтому вертикаль профиля и стык
    /// областей — это одно и то же место.
    ///
    /// Границы двигаются ПРЯМО (BaselineRegion.StartStation/EndStation — сеттеры есть).
    /// Слияния всех областей с последующим разрезанием больше нет: оно стирало конструкции,
    /// цели и частоты у всех участков разом.
    /// </summary>
    public static class ProfileGeometryOps
    {
        /// <summary>
        /// Зазор по умолчанию — то самое значение, что раньше было константой.
        /// Достаётся новым разрывам и записям прежнего формата.
        /// </summary>
        public const double DefaultGap = 0.001;

        /// <summary>
        /// Минимальный зазор. Ниже него пара PVI ступени становится неразличимой
        /// для поиска по допуску, а участки — вырожденно близкими.
        /// </summary>
        public const double MinGap = 0.0002;

        /// <summary>Максимальный зазор: дальше это уже не микроразрыв, а дыра в коридоре.</summary>
        public const double MaxGap = 10.0;

        /// <summary>Допуск поиска PVI. Заметно меньше зазора, чтобы не спутать пару.</summary>
        private const double PviTol = 1e-4;

        /// <summary>
        /// Привести зазор к допустимому. Значение приходит из записи в чертеже
        /// и из ввода пользователя, поэтому проверяется в одном месте.
        /// </summary>
        public static double SanitizeGap(double gap)
        {
            if (double.IsNaN(gap) || double.IsInfinity(gap)) return DefaultGap;
            if (gap < MinGap) return MinGap;
            if (gap > MaxGap) return MaxGap;
            return gap;
        }

        /// <summary>
        /// Допуск восстановления привязки к областям по геометрии. Крупный намеренно:
        /// в чертежах, созданных прежней версией, граница стоит ровно на пикете разрыва,
        /// без зазора. Маркеры расходятся минимум на буфер клампа (0.1 м), так что
        /// перепутать соседей нельзя.
        /// </summary>
        private const double RebindTol = 0.01;

        // ----------------------------------------------------------------------
        //  ПРОФИЛЬ
        // ----------------------------------------------------------------------

        /// <summary>Отметка профиля в точке.</summary>
        public static double ElevationAt(Profile profile, double station) =>
            profile.ElevationAt(Clamp(profile, station));

        /// <summary>
        /// Отметка ровного участка ПЕРЕД разрывом. Берётся на S − полузазор, а не на S:
        /// в самой точке S профиль в этот момент вертикален, и что вернёт ElevationAt —
        /// низ или верх ступени — не определено.
        /// </summary>
        public static double BaseElevationAt(Profile profile, double station, double halfGap) =>
            profile.ElevationAt(Clamp(profile, station - halfGap));

        private static double Clamp(Profile profile, double station)
        {
            if (station < profile.StartingStation) return profile.StartingStation;
            if (station > profile.EndingStation) return profile.EndingStation;
            return station;
        }

        /// <summary>
        /// Вставить ступень на пикете station: поднять/опустить весь профиль правее
        /// на stepHeight и поставить пару PVI (station-HalfGap / station+HalfGap).
        /// stepHeight со знаком: + вверх по ходу пикетажа.
        /// </summary>
        public static void InsertStep(Profile profile, double station, double stepHeight, double halfGap)
        {
            double baseElev = BaseElevationAt(profile, station, halfGap);

            // Сдвигаем по высоте всё, что строго правее разрыва. Пары PVI ранее
            // поставленных ступеней уезжают целиком — обе точки строго правее.
            foreach (ProfilePVI pvi in profile.PVIs.Cast<ProfilePVI>()
                                              .Where(p => p.Station > station).ToList())
                pvi.Elevation += stepHeight;

            profile.PVIs.AddPVI(station - halfGap, baseElev);
            profile.PVIs.AddPVI(station + halfGap, baseElev + stepHeight);
        }

        /// <summary>
        /// Удалить ступень в station (обратная к InsertStep).
        /// halfGap должен быть ТОТ ЖЕ, с которым ступень ставилась, иначе пара
        /// PVI не найдётся и в профиле останется вертикаль.
        /// </summary>
        public static void RemoveStep(Profile profile, double station, double stepHeight, double halfGap)
        {
            RemovePviNear(profile, station - halfGap, halfGap);
            RemovePviNear(profile, station + halfGap, halfGap);

            foreach (ProfilePVI pvi in profile.PVIs.Cast<ProfilePVI>()
                                              .Where(p => p.Station > station).ToList())
                pvi.Elevation -= stepHeight;
        }

        /// <summary>Перенести ступень с oldStation на newStation, сохранив stepHeight.</summary>
        public static void MoveStep(Profile profile, double oldStation, double newStation,
                                    double stepHeight, double halfGap)
        {
            // Снять и поставить заново: устойчивее, чем править две точки на месте.
            RemoveStep(profile, oldStation, stepHeight, halfGap);
            InsertStep(profile, newStation, stepHeight, halfGap);
        }

        /// <summary>
        /// Допуск поиска сужается вместе с зазором: при узком стыке две точки
        /// пары ближе друг к другу, чем PviTol, и по нему нашлась бы не та.
        /// </summary>
        private static void RemovePviNear(Profile profile, double station, double halfGap)
        {
            double tol = Math.Min(PviTol, halfGap / 2.0);

            var pvi = profile.PVIs.Cast<ProfilePVI>()
                             .FirstOrDefault(p => Math.Abs(p.Station - station) <= tol);
            if (pvi != null) profile.PVIs.Remove(pvi);
        }

        /// <summary>
        /// Станции всех "скачков" профиля (середина каждой почти-вертикали).
        /// maxGap — какой зазор ещё считать ступенью; у разрывов он теперь свой,
        /// поэтому берётся наибольший из интересующих.
        /// </summary>
        public static List<double> GetStepStations(Profile profile, double maxGap = DefaultGap)
        {
            var stations = new List<double>();
            var pvis = profile.PVIs.Cast<ProfilePVI>().OrderBy(p => p.Station).ToArray();
            for (int i = 1; i < pvis.Length; i++)
                if (Math.Abs(pvis[i].Elevation - pvis[i - 1].Elevation) > 1e-6 &&
                    Math.Abs(pvis[i].Station - pvis[i - 1].Station) <= maxGap + PviTol)
                    stations.Add((pvis[i].Station + pvis[i - 1].Station) / 2.0);
            return stations;
        }

        // ----------------------------------------------------------------------
        //  ОБЛАСТИ КОРИДОРА
        // ----------------------------------------------------------------------

        /// <summary>
        /// Привести границы областей к пикету маркера. Разрыва ещё нет — область режется,
        /// есть — границы просто переставляются. Конструкции, цели и частоты остальных
        /// участков не трогаются вообще.
        ///
        /// oldStation нужен, только если привязка к областям потеряна (чертёж от прежней
        /// версии, ручная правка коридора) — по нему пара областей ищется в геометрии.
        /// </summary>
        public static bool ApplyBreak(Baseline baseline, StationMarker m, double oldStation)
        {
            var regions = baseline.BaselineRegions;

            BaselineRegion left = FindByGuid(regions, m.LeftRegionId);
            BaselineRegion right = FindByGuid(regions, m.RightRegionId);

            if (left == null || right == null)
                Rebind(regions, oldStation, m.HalfGap, ref left, ref right);

            if (left != null && right != null)
            {
                MoveBoundary(left, right, m.Station, m.HalfGap);
                m.LeftRegionId = left.RegionGUID;
                m.RightRegionId = right.RegionGUID;
                return true;
            }

            // Разрыва в коридоре ещё нет: режем область, внутри которой стоит маркер.
            BaselineRegion host = FindRegionAt(regions, m.Station, m.HalfGap);
            if (host == null) return false;

            BaselineRegion created = host.Split(m.Station);
            created.Name = "Участок " + regions.Count;
            MoveBoundary(host, created, m.Station, m.HalfGap);

            m.LeftRegionId = host.RegionGUID;
            m.RightRegionId = created.RegionGUID;
            return true;
        }

        /// <summary>
        /// Убрать разрыв: правая область исчезает, левая растягивается на её место
        /// (то есть выживают настройки левого участка). removed/surviving нужны
        /// вызывающему, чтобы перепривязать соседний маркер справа.
        /// </summary>
        public static bool RemoveBreak(Baseline baseline, StationMarker m,
                                       out Guid removed, out Guid surviving)
        {
            removed = Guid.Empty;
            surviving = Guid.Empty;

            var regions = baseline.BaselineRegions;
            BaselineRegion left = FindByGuid(regions, m.LeftRegionId);
            BaselineRegion right = FindByGuid(regions, m.RightRegionId);
            if (left == null || right == null)
                Rebind(regions, m.Station, m.HalfGap, ref left, ref right);
            if (left == null || right == null) return false;

            removed = right.RegionGUID;
            surviving = left.RegionGUID;

            // Сначала убрать, потом растянуть: иначе на промежуточном шаге области
            // перекроются и Civil отвергнет присваивание.
            double end = right.EndStation;
            regions.Remove(right);
            left.EndStation = end;
            return true;
        }

        /// <summary>
        /// Переставить общую границу пары областей на пикет station.
        /// Один участок при этом удлиняется, соседний ровно на столько же
        /// сжимается — между ними остаётся зазор шириной 2·halfGap.
        /// </summary>
        private static void MoveBoundary(BaselineRegion left, BaselineRegion right,
                                         double station, double halfGap)
        {
            double leftEnd = station - halfGap;
            double rightStart = station + halfGap;

            // Порядок обязателен: сначала ужимаем ту область, что отдаёт кусок,
            // потом расширяем принимающую. Наоборот — промежуточное перекрытие.
            if (leftEnd >= left.EndStation)
            {
                right.StartStation = rightStart;
                left.EndStation = leftEnd;
            }
            else
            {
                left.EndStation = leftEnd;
                right.StartStation = rightStart;
            }
        }

        /// <summary>Восстановить пару областей разрыва по его прежнему пикету.</summary>
        private static void Rebind(BaselineRegionCollection regions, double station, double halfGap,
                                   ref BaselineRegion left, ref BaselineRegion right)
        {
            foreach (BaselineRegion r in regions)
            {
                if (left == null && Math.Abs(r.EndStation - station) <= halfGap + RebindTol)
                    left = r;
                if (right == null && Math.Abs(r.StartStation - station) <= halfGap + RebindTol)
                    right = r;
            }
            // Одна область не может быть обеими сторонами одного разрыва.
            if (left != null && right != null && left.RegionGUID == right.RegionGUID)
            {
                left = null;
                right = null;
            }
        }

        private static BaselineRegion FindByGuid(BaselineRegionCollection regions, Guid id)
        {
            if (id == Guid.Empty) return null;
            foreach (BaselineRegion r in regions)
                if (r.RegionGUID == id) return r;
            return null;
        }

        /// <summary>Область, внутрь которой попадает пикет (с запасом на будущий зазор).</summary>
        private static BaselineRegion FindRegionAt(BaselineRegionCollection regions,
                                                   double station, double halfGap)
        {
            foreach (BaselineRegion r in regions)
                if (station > r.StartStation + halfGap && station < r.EndStation - halfGap)
                    return r;
            return null;
        }
    }
}
