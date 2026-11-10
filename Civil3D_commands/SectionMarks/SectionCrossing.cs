using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Civil3D_commands.Shared;
using Alignment = Autodesk.Civil.DatabaseServices.Alignment;
using SampleLine = Autodesk.Civil.DatabaseServices.SampleLine;
using SampleLineGroup = Autodesk.Civil.DatabaseServices.SampleLineGroup;
using SampleLineVertex = Autodesk.Civil.DatabaseServices.SampleLineVertex;

namespace Civil3D_commands.SectionMarks
{
    /// <summary>
    /// Где линия выборки (сечение) пересекает трассу вида профиля и куда
    /// при этом смотрит сечение.
    ///
    /// Пересечение ищется не геометрией кривых, а по смещению: линия выборки
    /// режется на короткие отрезки, у каждой точки считается знаковое смещение
    /// от трассы (<see cref="RwGeometry"/>), и пересечение — там, где знак
    /// меняется; точка уточняется делением пополам. Так не нужен полилинейный
    /// двойник трассы (у дуг и спиралей он приближённый), и тот же код
    /// работает и для собственной трассы сечения, и для соседней.
    ///
    /// Направление взгляда сечения Civil 3D — вперёд по пикетажу ЕГО трассы.
    /// На чужой трассе оно переводится так: совпадает с ходом пикетажа
    /// трассы профиля (скалярное произведение касательных неотрицательно) —
    /// стрелка вперёд, иначе назад.
    /// </summary>
    internal static class SectionCrossing
    {
        // Число частей на отрезок линии выборки. Нужно, чтобы не пропустить
        // пересечение у длинной линии, оба конца которой лежат по одну сторону.
        private const int Steps = 24;

        private const int Bisections = 48;

        /// <summary>Касательная трассы в пикете; false — не вычислить.</summary>
        private static bool TryTangent(Alignment al, double station, out Vector2d dir)
        {
            dir = new Vector2d(1.0, 0.0);
            const double d = 0.05;

            Point3d a, b;
            if (RwGeometry.TryPointOnAlignment(al, station, 0.0, out a) &&
                RwGeometry.TryPointOnAlignment(al, station + d, 0.0, out b))
            {
                var v = new Vector2d(b.X - a.X, b.Y - a.Y);
                if (v.Length > 1e-9) { dir = v.GetNormal(); return true; }
            }

            // У конца оси «вперёд» нет — берём назад.
            if (RwGeometry.TryPointOnAlignment(al, station - d, 0.0, out a) &&
                RwGeometry.TryPointOnAlignment(al, station, 0.0, out b))
            {
                var v = new Vector2d(b.X - a.X, b.Y - a.Y);
                if (v.Length > 1e-9) { dir = v.GetNormal(); return true; }
            }

            return false;
        }

        private static bool TryOffset(Alignment al, Point3d p, out double station, out double offset)
        {
            return RwGeometry.TryStationOffsetOnAlignment(al, p, out station, out offset);
        }

        /// <summary>
        /// Пикет пересечения линии выборки с трассой и признак «сечение смотрит
        /// вперёд по пикетажу этой трассы». false — линия трассу не пересекает.
        /// </summary>
        public static bool TryCross(
            Transaction tr, SampleLine line, Alignment target,
            out double station, out bool forward)
        {
            station = 0.0;
            forward = true;
            if (line == null || target == null) return false;

            var pts = new List<Point3d>();
            foreach (SampleLineVertex v in line.Vertices) pts.Add(v.Location);
            if (pts.Count < 2) return false;

            if (!FindCrossing(target, pts, out station)) return false;

            forward = LooksForward(tr, line, target, station);
            return true;
        }

        private static bool FindCrossing(Alignment target, List<Point3d> pts, out double station)
        {
            station = 0.0;

            for (int i = 0; i + 1 < pts.Count; i++)
            {
                Point3d p0 = pts[i], p1 = pts[i + 1];

                Point3d a = p0;
                double sa, oa;
                bool haveA = TryOffset(target, a, out sa, out oa);

                for (int k = 1; k <= Steps; k++)
                {
                    Point3d b = p0 + (p1 - p0) * ((double)k / Steps);
                    double sb, ob;
                    bool haveB = TryOffset(target, b, out sb, out ob);

                    if (haveA && haveB && (Math.Abs(oa) < 1e-9 || oa * ob < 0.0))
                    {
                        Point3d at = a;

                        if (Math.Abs(oa) >= 1e-9)
                        {
                            Point3d lo = a, hi = b;
                            for (int n = 0; n < Bisections; n++)
                            {
                                Point3d mid = lo + (hi - lo) / 2.0;
                                double sm, om;
                                if (!TryOffset(target, mid, out sm, out om)) break;

                                if (om * oa > 0.0) lo = mid; else hi = mid;
                            }

                            at = lo + (hi - lo) / 2.0;
                        }

                        double s, o;
                        if (!TryOffset(target, at, out s, out o)) return false;

                        station = s;
                        return true;
                    }

                    a = b;
                    oa = ob;
                    haveA = haveB;
                }
            }

            return false;
        }

        /// <summary>
        /// Ход пикетажа собственной трассы сечения против хода пикетажа трассы
        /// профиля в точке пересечения. Не определить — считаем «вперёд»:
        /// так смотрят сечения собственной трассы.
        /// </summary>
        private static bool LooksForward(
            Transaction tr, SampleLine line, Alignment target, double targetStation)
        {
            try
            {
                var group = tr.GetObject(line.GroupId, OpenMode.ForRead) as SampleLineGroup;
                if (group == null) return true;

                var source = tr.GetObject(group.ParentAlignmentId, OpenMode.ForRead) as Alignment;
                if (source == null) return true;

                // Одна и та же трасса — взгляд ровно по пикетажу, считать нечего.
                if (source.ObjectId == target.ObjectId) return true;

                Vector2d view, along;
                if (!TryTangent(source, line.Station, out view)) return true;
                if (!TryTangent(target, targetStation, out along)) return true;

                return view.DotProduct(along) >= 0.0;
            }
            catch (System.Exception)
            {
                return true;
            }
        }
    }
}
