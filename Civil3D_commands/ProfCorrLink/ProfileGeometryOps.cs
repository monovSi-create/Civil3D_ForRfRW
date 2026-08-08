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
        /// <summary>Полузазор: на столько разъезжаются области и PVI по обе стороны от разрыва.</summary>
        public const double HalfGap = 0.0005;

        /// <summary>Полный зазор между областями (и между парой PVI ступени).</summary>
        public const double StepGap = 2.0 * HalfGap;

        /// <summary>Допуск поиска PVI. Заметно меньше зазора, чтобы не спутать пару.</summary>
        private const double PviTol = 1e-4;

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
        /// Отметка ровного участка ПЕРЕД разрывом. Берётся на S-0.0005, а не на S:
        /// в самой точке S профиль в этот момент вертикален, и что вернёт ElevationAt —
        /// низ или верх ступени — не определено.
        /// </summary>
        public static double BaseElevationAt(Profile profile, double station) =>
            profile.ElevationAt(Clamp(profile, station - HalfGap));

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
        public static void InsertStep(Profile profile, double station, double stepHeight)
        {
            double baseElev = BaseElevationAt(profile, station);

            // Сдвигаем по высоте всё, что строго правее разрыва. Пары PVI ранее
            // поставленных ступеней уезжают целиком — обе точки строго правее.
            foreach (ProfilePVI pvi in profile.PVIs.Cast<ProfilePVI>()
                                              .Where(p => p.Station > station).ToList())
                pvi.Elevation += stepHeight;

            profile.PVIs.AddPVI(station - HalfGap, baseElev);
            profile.PVIs.AddPVI(station + HalfGap, baseElev + stepHeight);
        }

        /// <summary>Удалить ступень в station (обратная к InsertStep).</summary>
        public static void RemoveStep(Profile profile, double station, double stepHeight)
        {
            RemovePviNear(profile, station - HalfGap);
            RemovePviNear(profile, station + HalfGap);

            foreach (ProfilePVI pvi in profile.PVIs.Cast<ProfilePVI>()
                                              .Where(p => p.Station > station).ToList())
                pvi.Elevation -= stepHeight;
        }

        /// <summary>Перенести ступень с oldStation на newStation, сохранив stepHeight.</summary>
        public static void MoveStep(Profile profile, double oldStation, double newStation, double stepHeight)
        {
            // Снять и поставить заново: устойчивее, чем править две точки на месте.
            RemoveStep(profile, oldStation, stepHeight);
            InsertStep(profile, newStation, stepHeight);
        }

        private static void RemovePviNear(Profile profile, double station)
        {
            var pvi = profile.PVIs.Cast<ProfilePVI>()
                             .FirstOrDefault(p => Math.Abs(p.Station - station) <= PviTol);
            if (pvi != null) profile.PVIs.Remove(pvi);
        }

        /// <summary>Станции всех "скачков" профиля (середина каждой почти-вертикали).</summary>
        public static List<double> GetStepStations(Profile profile)
        {
            var stations = new List<double>();
            var pvis = profile.PVIs.Cast<ProfilePVI>().OrderBy(p => p.Station).ToArray();
            for (int i = 1; i < pvis.Length; i++)
                if (Math.Abs(pvis[i].Elevation - pvis[i - 1].Elevation) > 1e-6 &&
                    Math.Abs(pvis[i].Station - pvis[i - 1].Station) <= StepGap + PviTol)
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
                Rebind(regions, oldStation, ref left, ref right);

            if (left != null && right != null)
            {
                MoveBoundary(left, right, m.Station);
                m.LeftRegionId = left.RegionGUID;
                m.RightRegionId = right.RegionGUID;
                return true;
            }

            // Разрыва в коридоре ещё нет: режем область, внутри которой стоит маркер.
            BaselineRegion host = FindRegionAt(regions, m.Station);
            if (host == null) return false;

            BaselineRegion created = host.Split(m.Station);
            created.Name = "Участок " + regions.Count;
            MoveBoundary(host, created, m.Station);

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
                Rebind(regions, m.Station, ref left, ref right);
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

        /// <summary>Переставить общую границу пары областей на пикет station.</summary>
        private static void MoveBoundary(BaselineRegion left, BaselineRegion right, double station)
        {
            double leftEnd = station - HalfGap;
            double rightStart = station + HalfGap;

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
        private static void Rebind(BaselineRegionCollection regions, double station,
                                   ref BaselineRegion left, ref BaselineRegion right)
        {
            foreach (BaselineRegion r in regions)
            {
                if (left == null && Math.Abs(r.EndStation - station) <= HalfGap + RebindTol)
                    left = r;
                if (right == null && Math.Abs(r.StartStation - station) <= HalfGap + RebindTol)
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
        private static BaselineRegion FindRegionAt(BaselineRegionCollection regions, double station)
        {
            foreach (BaselineRegion r in regions)
                if (station > r.StartStation + HalfGap && station < r.EndStation - HalfGap)
                    return r;
            return null;
        }
    }
}
