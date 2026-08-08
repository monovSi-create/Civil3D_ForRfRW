using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.DatabaseServices;
using Civil3D_commands.Shared;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
// Entity и ObjectId есть и в AutoCAD-, и в Civil-пространстве имён — снимаем неоднозначность.
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;
using ObjectId = Autodesk.AutoCAD.DatabaseServices.ObjectId;

namespace Civil3D_commands.FaceArr
{
    /// <summary>
    /// Интерактивное редактирование рядов на виде профиля.
    ///
    /// Носитель ручек — служебный отрезок ряда (см. FacingWallProjection):
    /// от StartStation до EndStation на середине высоты ряда.
    ///
    /// ВАЖНО: GetGripPoints здесь НЕ переопределяется. Ручки на концах и в
    /// середине отрезка AutoCAD рисует сам, штатно. Предыдущая схема выдавала
    /// собственные GripData на вставке-контроллере: код вызывался, отдавал все
    /// 14 грипс без ошибок, а AutoCAD их не показывал. Здесь этот механизм из
    /// схемы убран целиком — перехватывается только перемещение.
    ///
    /// Левый конец меняет StartStation, правый — EndStation, средняя ручка
    /// двигает ряд целиком, не меняя его длину. Остальные ряды не трогаются.
    /// </summary>
    public class FacingWallGrips : GripOverrule
    {
        private static FacingWallGrips _instance;

        /// <summary>Защита от повторного входа во время перестроения.</summary>
        private static bool _busy;

        // --- Счётчики для диагностики. ---
        public static int IsApplicableCalls;
        public static int GripDataMoveCalls;
        public static int LegacyMoveCalls;
        public static int RowMoves;
        public static string LastError;
        public static string LastAction = "(ничего)";

        public static bool IsRegistered { get { return _instance != null; } }

        public static void Enable()
        {
            if (_instance == null)
            {
                _instance = new FacingWallGrips();
                Overrule.AddOverrule(RXObject.GetClass(typeof(Line)), _instance, false);
            }

            Overrule.Overruling = true;
        }

        public static void Disable()
        {
            if (_instance == null) return;

            Overrule.RemoveOverrule(RXObject.GetClass(typeof(Line)), _instance);
            _instance.Dispose();
            _instance = null;
        }

        public override bool IsApplicable(RXObject overruledSubject)
        {
            IsApplicableCalls++;

            try
            {
                ObjectId controllerId;
                int rowIndex;
                return FacingWallProjection.TryReadGripTag(
                    overruledSubject as Entity, out controllerId, out rowIndex);
            }
            catch (System.Exception)
            {
                return false;
            }
        }

        // =================================================================
        //  ПЕРЕХВАТ ПЕРЕМЕЩЕНИЯ
        //
        //  Переопределены обе перегрузки: IsApplicable AutoCAD не спрашивает,
        //  и на Line здесь же висит оверрул из ProfCorrLink — поэтому чужие
        //  отрезки обязаны уходить в base, иначе сломается соседний модуль.
        // =================================================================

        public override void MoveGripPointsAt(
            Entity entity, GripDataCollection grips,
            Vector3d offset, MoveGripPointsFlags bitFlags)
        {
            GripDataMoveCalls++;

            var line = entity as Line;
            ObjectId controllerId;
            int rowIndex;

            if (line == null ||
                !FacingWallProjection.TryReadGripTag(line, out controllerId, out rowIndex))
            {
                base.MoveGripPointsAt(entity, grips, offset, bitFlags);
                return;
            }

            var kinds = new List<GripKind>();
            foreach (GripData g in grips)
                kinds.Add(Classify(line, g.GripPoint));

            ApplyMove(line, controllerId, rowIndex, kinds, offset);
        }

        public override void MoveGripPointsAt(
            Entity entity, IntegerCollection indices, Vector3d offset)
        {
            LegacyMoveCalls++;

            var line = entity as Line;
            ObjectId controllerId;
            int rowIndex;

            if (line == null ||
                !FacingWallProjection.TryReadGripTag(line, out controllerId, out rowIndex))
            {
                base.MoveGripPointsAt(entity, indices, offset);
                return;
            }

            // Штатные точки грипс отрезка — берём их же и опознаём по координате.
            var kinds = new List<GripKind>();
            using (var pts = new Point3dCollection())
            {
                try
                {
                    base.GetGripPoints(entity, pts, new IntegerCollection(), new IntegerCollection());
                }
                catch (System.Exception)
                {
                    // не смогли получить — считаем по индексам отрезка: 0 начало, 1 конец
                }

                foreach (int i in indices)
                {
                    if (pts.Count > 0)
                        kinds.Add(i >= 0 && i < pts.Count ? Classify(line, pts[i]) : GripKind.None);
                    else
                        kinds.Add(i == 0 ? GripKind.Start : (i == 1 ? GripKind.End : GripKind.Middle));
                }
            }

            ApplyMove(line, controllerId, rowIndex, kinds, offset);
        }

        // =================================================================

        private enum GripKind { None, Start, Middle, End }

        /// <summary>Какой из трёх штатных ручек отрезка соответствует точка.</summary>
        private static GripKind Classify(Line line, Point3d p)
        {
            Point3d mid = line.StartPoint + (line.EndPoint - line.StartPoint) / 2.0;

            double dStart = p.DistanceTo(line.StartPoint);
            double dEnd = p.DistanceTo(line.EndPoint);
            double dMid = p.DistanceTo(mid);

            if (dStart <= dEnd && dStart <= dMid) return GripKind.Start;
            if (dEnd <= dStart && dEnd <= dMid) return GripKind.End;
            return GripKind.Middle;
        }

        /// <summary>
        /// Общая обвязка: блокировка документа, транзакция, запись и правка
        /// геометрии отрезка после коммита. Что именно двигать — решает метка:
        /// отрицательный номер ряда означает плановую ручку всего массива.
        /// </summary>
        private static void ApplyMove(
            Line line, ObjectId controllerId, int rowIndex,
            List<GripKind> kinds, Vector3d offset)
        {
            if (_busy || line == null || kinds == null || kinds.Count == 0) return;

            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            bool moved = false;
            Point3d newStart = line.StartPoint;
            Point3d newEnd = line.EndPoint;

            _busy = true;
            try
            {
                using (doc.LockDocument())
                using (Transaction tr = line.Database.TransactionManager.StartTransaction())
                {
                    FacingWallDefinition def = FacingWallController.Load(controllerId, tr);
                    if (def == null) { tr.Commit(); return; }

                    if (!OwnsThisLine(def, line, rowIndex)) { tr.Commit(); return; }

                    moved = FacingWallGripTag.IsLayout(rowIndex)
                        ? MoveLayoutMarker(
                            tr, line, controllerId, def, rowIndex, kinds, offset, ref newStart, ref newEnd)
                        : MoveSingleRow(
                            tr, line, controllerId, def, rowIndex, kinds, offset, ref newStart, ref newEnd);

                    if (moved)
                    {
                        FacingWallController.Save(controllerId, def, tr);
                        RowMoves++;
                    }

                    tr.Commit();
                }

                // Отрезок сейчас открыт на запись грип-системой: внутри транзакции
                // его не трогают, геометрию правим здесь.
                if (moved)
                {
                    line.StartPoint = newStart;
                    line.EndPoint = newEnd;
                }
            }
            catch (System.Exception ex)
            {
                // молча: исключение отсюда убивает сеанс редактирования.
                // Текст оседает в LastError, его печатает FACINGWALLDIAG.
                LastError = ex.Message;
            }
            finally
            {
                _busy = false;
            }
        }

        /// <summary>
        /// Этот ли отрезок записан в контроллере за своим номером.
        ///
        /// XData копируется вместе с отрезком, поэтому копия ручки указывает на
        /// ЧУЖОЙ контроллер и тянула бы чужой ряд. Проверка идёт по связи из
        /// модели: копия там не числится.
        ///
        /// Пустой слот — не отказ, а усыновление: отрезок мог быть создан, а
        /// запись контроллера не сохраниться. Занятый живым отрезком слот —
        /// отказ, это и есть признак копии.
        /// </summary>
        private static bool OwnsThisLine(FacingWallDefinition def, Line line, int rowIndex)
        {
            if (def.IsDetachedCopy)
            {
                LastError = "Это копия контроллера: связи с объектами сброшены. " +
                            "Сделайте FACINGWALLREBUILDALL, чтобы массив стал самостоятельным.";
                LastAction = "отказ: копия контроллера";
                return false;
            }

            ObjectId registered = def.GetGripLineId(rowIndex);

            if (registered == line.ObjectId) return true;

            if (!registered.IsNull && registered.IsValid && !registered.IsErased)
            {
                LastError = "Отрезок-ручка не числится в контроллере (копия?) — " +
                            "перемещение отклонено.";
                LastAction = "отказ: чужой отрезок-ручка";
                return false;
            }

            def.SetGripLineId(rowIndex, line.ObjectId);
            return true;
        }

        /// <summary>
        /// Профильная ручка: правит ровно свой ряд, остальные не трогает.
        /// </summary>
        private static bool MoveSingleRow(
            Transaction tr, Line line, ObjectId controllerId, FacingWallDefinition def,
            int rowIndex, List<GripKind> kinds, Vector3d offset,
            ref Point3d newStart, ref Point3d newEnd)
        {
            FacingWallRowDefinition row = def.GetRow(rowIndex);
            if (row == null) return false;

            var pv = tr.GetObject(def.ProfileViewId, OpenMode.ForRead) as ProfileView;
            var alignment = tr.GetObject(def.AlignmentId, OpenMode.ForRead) as Alignment;
            if (pv == null || alignment == null) return false;

            bool moved = false;

            foreach (GripKind kind in kinds)
            {
                if (kind == GripKind.None) continue;

                double station, elevation;
                if (!TryStationAt(pv, GripPoint(line, kind) + offset, out station, out elevation))
                    continue;

                // Ряды ходят по сетке в полблока — иначе половинку к краю не приставить.
                station = SnapToHalfStep(def, station);

                if (kind == GripKind.Start)
                {
                    row.StartStation = station;
                }
                else if (kind == GripKind.End)
                {
                    row.EndStation = station;
                }
                else
                {
                    // средняя ручка — сдвиг ряда целиком, длина сохраняется
                    double oldMid = (row.StartStation + row.EndStation) / 2.0;
                    double delta = SnapToHalfStep(def, station) - SnapToHalfStep(def, oldMid);
                    row.StartStation += delta;
                    row.EndStation += delta;
                }

                moved = true;
            }

            if (!moved) return false;

            ClampRow(def, row, alignment);

            FacingWallController.RebuildRow(tr, line.Database, controllerId, def, rowIndex);

            double elev = FacingWallBuilder.RowElevation(def, row) + def.BlockHeight / 2.0;

            // Не удалось посчитать — отрезок останется на месте, данные всё равно
            // сохраняются: картинку поправит FACINGWALLREBUILDALL.
            Point3d[] pts = RwGeometry.ProfileSegment(
                pv, row.StartStation, elev, row.EndStation, elev);

            if (pts != null)
            {
                newStart = pts[0];
                newEnd = pts[1];
            }

            LastAction = string.Format(
                "ряд {0}: {1:F3}..{2:F3}", rowIndex, row.StartStation, row.EndStation);

            return true;
        }

        /// <summary>
        /// Маркер границы раскладки — в плане или в виде профиля, безразлично.
        ///
        /// Виды равноправны: границы всего две, а маркеров четыре, и правятся
        /// они одним и тем же кодом. Разница только в том, как из точки мыши
        /// получить пикет: в плане — снос на трассу, в профиле — обратный
        /// пересчёт координат вида. Второй маркер той же границы подтянется
        /// сам, потому что BuildAll заново расставляет все четыре.
        ///
        /// Граница раскладки главнее профильных уточнений, поэтому своя сторона
        /// переписывается у всех рядов без разговоров. Противоположная сторона
        /// уточнения сохраняет — её только зажимает в новую область.
        ///
        /// Это единственное место, где массив перестраивается целиком без явной
        /// команды пользователя, — иначе и нельзя: тронуты все ряды сразу.
        /// </summary>
        private static bool MoveLayoutMarker(
            Transaction tr, Line line, ObjectId controllerId, FacingWallDefinition def,
            int tagRowIndex, List<GripKind> kinds, Vector3d offset,
            ref Point3d newStart, ref Point3d newEnd)
        {
            if (def.Rows == null || def.Rows.Count == 0) return false;

            var alignment = tr.GetObject(def.AlignmentId, OpenMode.ForRead) as Alignment;
            if (alignment == null) return false;

            bool inPlan = FacingWallGripTag.IsInPlan(tagRowIndex);

            var pv = def.ProfileViewId.IsNull
                ? null
                : tr.GetObject(def.ProfileViewId, OpenMode.ForRead) as ProfileView;

            if (!inPlan && pv == null) return false;

            // Маркер переезжает целиком, поэтому важна не ручка, а сама точка.
            GripKind kind = GripKind.None;
            foreach (GripKind k in kinds)
                if (k != GripKind.None) { kind = k; break; }

            if (kind == GripKind.None) return false;

            Point3d target = GripPoint(line, kind) + offset;

            double station;
            if (inPlan)
            {
                if (!TryStationOnAlignment(alignment, target, out station)) return false;
            }
            else
            {
                double elevation;
                if (!TryStationAt(pv, target, out station, out elevation)) return false;
            }

            station = Clamp(station, alignment.StartingStation, alignment.EndingStation);

            // Границы НЕ упорядочиваются: якорь можно увести за дальнюю границу,
            // и тогда раскладка пойдёт в обратную сторону. Это осознанно.
            bool isAnchor = FacingWallGripTag.IsAnchor(tagRowIndex);

            // Якорь ходит свободно — он и задаёт начало сетки. Но сетка вместе
            // с ним уезжает, поэтому дальнюю границу приходится пересадить на
            // неё заново, иначе остаток перестанет быть кратным полублоку.
            if (isAnchor)
            {
                def.LayoutStartStation = station;
                def.LayoutEndStation = SnapToHalfStep(def, def.LayoutEndStation);
            }
            else
            {
                station = SnapToHalfStep(def, station);
                def.LayoutEndStation = station;
            }

            foreach (FacingWallRowDefinition row in def.Rows)
            {
                if (row == null) continue;

                if (isAnchor) row.StartStation = def.LayoutStartStation;
                else row.EndStation = def.LayoutEndStation;
            }

            foreach (FacingWallRowDefinition row in def.Rows)
            {
                if (row == null) continue;
                ClampRow(def, row, alignment);
            }

            FacingWallController.BuildAll(tr, line.Database, controllerId, def);

            // Если точки посчитать не удалось, маркер останется на месте:
            // данные всё равно сохраняем, картинку поправит FACINGWALLREBUILDALL.
            Point3d[] points = inPlan
                ? FacingWallLayoutGrip.PlanPoints(alignment, def, station)
                : FacingWallLayoutGrip.ProfilePoints(pv, def, station);

            if (points != null)
            {
                newStart = points[0];
                newEnd = points[1];
            }

            LastAction = string.Format(
                "{0} {1}: пикет {2:F3} (раскладка {3:F3} -> {4:F3})",
                inPlan ? "план" : "профиль",
                isAnchor ? "якорь" : "граница", station,
                def.LayoutStartStation, def.LayoutEndStation);

            return true;
        }

        /// <summary>Точка, из которой тянут: конец отрезка или его середина.</summary>
        private static Point3d GripPoint(Line line, GripKind kind)
        {
            if (kind == GripKind.Start) return line.StartPoint;
            if (kind == GripKind.End) return line.EndPoint;

            return line.StartPoint + (line.EndPoint - line.StartPoint) / 2.0;
        }

        /// <summary>
        /// Точка плана -> пикет трассы. false, если точку не спроецировать:
        /// за пределами оси StationOffset возвращает NaN.
        /// </summary>
        private static bool TryStationOnAlignment(
            Alignment alignment, Point3d p, out double station)
        {
            return RwGeometry.TryStationOnAlignment(alignment, p, out station);
        }

        /// <summary>Точка вида профиля -> пикет. false, если точка вне вида.</summary>
        private static bool TryStationAt(
            ProfileView pv, Point3d p, out double station, out double elevation)
        {
            return RwGeometry.TryStationInProfileView(pv, p, out station, out elevation);
        }

        /// <summary>
        /// Ряд обязан лежать внутри плановой области, идти в ту же сторону, что
        /// и она, и быть не короче одного блока: план главнее профиля.
        ///
        /// Если область уже одного блока, ряд останется короче — блоков в нём
        /// просто не будет, и это честнее, чем вылезти за заданные границы.
        /// </summary>
        private static void ClampRow(
            FacingWallDefinition def, FacingWallRowDefinition row, Alignment alignment)
        {
            double lo = Math.Max(def.LayoutLowStation(), alignment.StartingStation);
            double hi = Math.Min(def.LayoutHighStation(), alignment.EndingStation);
            if (hi < lo) hi = lo;

            bool forward = def.LayoutEndStation >= def.LayoutStartStation;

            double s = Clamp(row.StartStation, lo, hi);
            double e = Clamp(row.EndStation, lo, hi);

            // Ряд не может идти против раскладки: направление задаётся в плане.
            if (forward != (e >= s))
            {
                double swap = s;
                s = e;
                e = swap;
            }

            double minLength = def.BlockWidth > 0.0 ? def.BlockWidth : 0.0;

            if (Math.Abs(e - s) < minLength)
            {
                // Двигаем дальнюю границу: якорь задан пользователем и важнее.
                double target = forward ? s + minLength : s - minLength;

                if (target >= lo && target <= hi)
                {
                    e = target;
                }
                else if (forward)
                {
                    e = hi;
                    s = Math.Max(lo, hi - minLength);
                }
                else
                {
                    e = lo;
                    s = Math.Min(hi, lo + minLength);
                }
            }

            row.StartStation = s;
            row.EndStation = e;
        }

        private static double Clamp(double value, double min, double max)
        {
            return RwGeometry.Clamp(value, min, max);
        }

        /// <summary>
        /// Притянуть пикет к сетке в полблока, отсчитанной ОТ ЯКОРЯ раскладки.
        ///
        /// Полшага, а не шаг: только так к краю ряда можно добавить половинчатый
        /// блок. Сам якорь не притягивается — он и задаёт начало сетки.
        /// </summary>
        private static double SnapToHalfStep(FacingWallDefinition def, double station)
        {
            double step = def.HalfStep();
            if (step <= 1e-9) return station;

            double k = Math.Round((station - def.LayoutStartStation) / step);
            return def.LayoutStartStation + k * step;
        }
    }
}
