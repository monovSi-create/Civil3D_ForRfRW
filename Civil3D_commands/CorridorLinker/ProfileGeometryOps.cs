using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.DatabaseServices;

namespace Civil3D_commands.AssociativeBreaks
{
    /// <summary>
    /// Операции над геометрией: ступени продольного профиля (PVI) и области коридора.
    /// Логика "лесенки" соответствует твоему rebuildProfile: ступень = пара PVI
    /// на station и station + StepGap, а все последующие PVI смещены по высоте.
    /// </summary>
    public static class ProfileGeometryOps
    {
        /// <summary>Зазор "почти вертикали" (~1 мм), как в твоём rebuildProfile (+0.001).</summary>
        public const double StepGap = 0.001;

        private const double Tol = 0.0005; // допуск сравнения станций

        /// <summary>Отметка профиля в точке (ровный участок).</summary>
        public static double ElevationAt(Profile profile, double station) =>
            profile.ElevationAt(station);

        // ----------------------------------------------------------------------
        //  СТУПЕНИ ПРОФИЛЯ
        // ----------------------------------------------------------------------

        /// <summary>
        /// Вставить ступень: поднять/опустить весь профиль после station на stepHeight
        /// и поставить пару PVI (station / station+StepGap). stepHeight со знаком.
        /// </summary>
        public static void InsertStep(Profile profile, double station, double stepHeight)
        {
            double baseElev = ElevationAt(profile, station);

            // Сдвигаем по высоте все PVI, начиная со station (вместе с правой полкой).
            foreach (ProfilePVI pvi in profile.PVIs.Cast<ProfilePVI>()
                                              .Where(p => p.Station >= station - Tol).ToList())
                pvi.Elevation += stepHeight;

            profile.PVIs.AddPVI(station, baseElev);
            profile.PVIs.AddPVI(station + StepGap, baseElev + stepHeight);
        }

        /// <summary>Удалить ступень в station (обратная к InsertStep).</summary>
        public static void RemoveStep(Profile profile, double station, double stepHeight)
        {
            RemovePviNear(profile, station);
            RemovePviNear(profile, station + StepGap);

            foreach (ProfilePVI pvi in profile.PVIs.Cast<ProfilePVI>()
                                              .Where(p => p.Station > station + StepGap + Tol).ToList())
                pvi.Elevation -= stepHeight;
        }

        /// <summary>Перенести ступень с oldStation на newStation, сохранив stepHeight.</summary>
        public static void MoveStep(Profile profile, double oldStation, double newStation, double stepHeight)
        {
            // Простой и устойчивый путь: снять и поставить заново.
            RemoveStep(profile, oldStation, stepHeight);
            InsertStep(profile, newStation, stepHeight);
        }

        private static void RemovePviNear(Profile profile, double station)
        {
            var pvi = profile.PVIs.Cast<ProfilePVI>()
                             .FirstOrDefault(p => Math.Abs(p.Station - station) <= Tol);
            if (pvi != null) profile.PVIs.RemovePVI(pvi.Station);
        }

        /// <summary>Станции всех "скачков" профиля (нижняя точка каждой почти-вертикали).</summary>
        public static List<double> GetStepStations(Profile profile)
        {
            var stations = new List<double>();
            var pvis = profile.PVIs.Cast<ProfilePVI>().OrderBy(p => p.Station).ToArray();
            for (int i = 1; i < pvis.Length; i++)
                if (Math.Abs(pvis[i].Elevation - pvis[i - 1].Elevation) > 1e-6 &&
                    Math.Abs(pvis[i].Station - pvis[i - 1].Station) <= StepGap + Tol)
                    stations.Add(pvis[i - 1].Station);
            return stations;
        }

        // ----------------------------------------------------------------------
        //  ОБЛАСТИ КОРИДОРА
        // ----------------------------------------------------------------------

        /// <summary>
        /// Привести области базовой линии к набору пикетов разрыва.
        /// Все области сливаются в одну и заново режутся в каждом breakStation.
        /// Привязка конструкции (AssemblyId) восстанавливается из assemblyByStart,
        /// ключ — стартовый пикет области (округлённый), чтобы пережить респлит.
        /// </summary>
        public static void ResyncRegions(Baseline baseline, IList<double> breakStations,
                                          IDictionary<double, ObjectId> assemblyByStart)
        {
            var regions = baseline.BaselineRegions;

            // 1) Запоминаем текущее назначение конструкций по стартовому пикету.
            if (assemblyByStart != null)
                foreach (BaselineRegion r in regions)
                    assemblyByStart[Round(r.StartStation)] = r.AssemblyId;

            // 2) Сливаем всё в одну область.
            UnionRegions(baseline);

            // 3) Режем по каждому пикету разрыва (по возрастанию).
            BaselineRegion current = regions[0];
            int n = 1;
            current.Name = "Участок " + n;

            foreach (double st in breakStations.OrderBy(x => x))
            {
                if (st <= current.StartStation + 1e-6 || st >= current.EndStation - 1e-6)
                    continue;
                BaselineRegion next = current.Split(st);
                n++;
                next.Name = "Участок " + n;
                current = next;
            }

            // 4) Возвращаем конструкции по стартовому пикету (если знаем).
            if (assemblyByStart != null)
                foreach (BaselineRegion r in regions)
                    if (assemblyByStart.TryGetValue(Round(r.StartStation), out ObjectId asm) && !asm.IsNull)
                        r.AssemblyId = asm;
        }

        public static void UnionRegions(Baseline baseline)
        {
            var rc = baseline.BaselineRegions;
            if (rc.Count > 1)
                rc[0].Merge(rc[0], rc[rc.Count - 1]);
        }

        private static double Round(double s) => Math.Round(s, 3);
    }
}
