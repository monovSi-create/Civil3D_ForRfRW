using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.DatabaseServices;
using Civil3D_commands.Shared;
using AcColor = Autodesk.AutoCAD.Colors.Color;
using ColorMethod = Autodesk.AutoCAD.Colors.ColorMethod;
using Transparency = Autodesk.AutoCAD.Colors.Transparency;
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;
using ObjectId = Autodesk.AutoCAD.DatabaseServices.ObjectId;
using Polyline = Autodesk.AutoCAD.DatabaseServices.Polyline;

namespace Civil3D_commands.AssociativeBreaks
{
    /// <summary>
    /// Наглядное представление связи в виде профиля: границы участков, заливка
    /// каждого участка своим цветом и надписи вдоль границ.
    ///
    /// Всё здесь — **производное от модели**: заливки строятся по фактическим
    /// областям коридора (<see cref="BaselineRegion"/>), а не по маркерам, поэтому
    /// показывают именно то, где какая конструкция реально применена, вместе
    /// с микроразрывами между участками. Перестраивается целиком: хранить
    /// «что изменилось» дороже, чем нарисовать заново.
    ///
    /// Видимостью и защитой от случайной правки заведуют СЛОИ:
    ///
    /// | Слой | Что на нём | Вне режима редактирования |
    /// |------|-----------|---------------------------|
    /// | <see cref="BoundaryLayer"/> | прокси разрывов + концевые границы коридора | виден, **заблокирован** |
    /// | <see cref="AssemblyLayer"/> | имена конструкций участков | виден, заблокирован |
    /// | <see cref="FillLayer"/> | заливки участков | выключен |
    /// | <see cref="InfoLayer"/> | «Пикет …» и имя коридора | выключен |
    /// | <see cref="BreakController.LayerName"/> | контроллеры связей | виден, НЕ заблокирован |
    ///
    /// Блокировка, а не заморозка: заморозить значит спрятать, а границы должны
    /// остаться видны — просто неподцепляемыми, чтобы их нельзя было случайно
    /// сдвинуть мышью. Контроллер не блокируется никогда, иначе связь не выбрать.
    /// </summary>
    public static class BreakOverlay
    {
        public const string BoundaryLayer = "RW_BREAK_BOUNDARY";
        public const string FillLayer     = "RW_BREAK_FILL";
        public const string InfoLayer     = "RW_BREAK_INFO";
        public const string AssemblyLayer = "RW_BREAK_ASSEMBLY";

        /// <summary>Тип линии границ участков — штриховой.</summary>
        public const string BoundaryLinetype = "DASHED";

        /// <summary>Прозрачность заливок, проценты. 50 % — участок читается, а профиль сквозь него виден.</summary>
        public const int FillTransparencyPercent = 50;

        /// <summary>
        /// Запасная высота текста — только если её не задаёт ни стиль, ни
        /// <c>TEXTSIZE</c>. Своей высоты у надписей нет: всё берётся из чертежа.
        /// </summary>
        private const double FallbackTextHeight = 2.5;

        /// <summary>Отступ надписи от границы, в долях высоты текста.</summary>
        private const double LabelGap = 0.4;

        /// <summary>Отступ надписи от края вида по вертикали, в долях высоты текста.</summary>
        private const double LabelMargin = 1.0;

        /// <summary>XData-метка объектов оформления: приложение и Guid связи.</summary>
        public const string XAppName = "RW_BREAKOVL";

        // ------------------------------------------------------------------
        //  ПЕРЕСТРОЕНИЕ
        // ------------------------------------------------------------------

        /// <summary>
        /// Перестроить оформление одной связи. Слои при этом разблокируются
        /// и в конце получают состояние, положенное текущему режиму.
        ///
        /// Ошибки гасятся: оформление — удобство, а не механика разрывов.
        /// Отвалившаяся заливка не должна мешать двигать границу.
        /// </summary>
        public static void Rebuild(Transaction tr, Database db, BreakSession session, BreakLink link)
        {
            if (link == null || session == null) return;

            try
            {
                Unlock(tr, db);
                Erase(tr, db, link);
                Build(tr, db, session, link);
            }
            catch (System.Exception)
            {
                // Недостроенное оформление лучше сорванной команды.
            }
            finally
            {
                try { ApplyState(tr, db, session.AnyEditMode); }
                catch (System.Exception) { }
            }
        }

        /// <summary>Перестроить оформление всех связей чертежа.</summary>
        public static void RebuildAll(Transaction tr, Database db, BreakSession session)
        {
            if (session == null) return;
            foreach (BreakLink link in session.Links.ToList())
                Rebuild(tr, db, session, link);
        }

        /// <summary>Стереть объекты оформления связи. Слои должны быть уже разблокированы.</summary>
        public static void Erase(Transaction tr, Database db, BreakLink link)
        {
            if (link == null) return;

            foreach (Handle h in link.OverlayHandles)
            {
                ObjectId id = RwHandles.Resolve(db, h);
                if (id.IsNull) continue;

                try
                {
                    var ent = tr.GetObject(id, OpenMode.ForWrite) as Entity;
                    if (ent != null) ent.Erase();
                }
                catch (System.Exception)
                {
                    // Объект мог быть стёрт пользователем — это не ошибка.
                }
            }

            link.OverlayHandles.Clear();
        }

        // ------------------------------------------------------------------

        private static void Build(Transaction tr, Database db, BreakSession session, BreakLink link)
        {
            var pv = RwHandles.Open<ProfileView>(tr, db, link.ProfileViewHandle, OpenMode.ForRead);
            if (pv == null) return;

            double yLo, yHi;
            if (!ViewBox(pv, out yLo, out yHi)) return;

            Baseline baseline = session.GetBaseline(tr, link);
            if (baseline == null) return;

            var space = (BlockTableRecord)tr.GetObject(
                SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);

            double height = TextHeight(tr, db);
            short linkColor = BreakController.ColorOf(link);

            EnsureLayer(tr, db, BoundaryLayer, 7);
            EnsureLayer(tr, db, FillLayer, 8);
            EnsureLayer(tr, db, InfoLayer, 7);
            EnsureLayer(tr, db, AssemblyLayer, 7);

            // --- Участки: заливка + запоминание конструкции на каждой границе ---
            var regions = CollectRegions(tr, baseline);

            foreach (RegionInfo r in regions)
                AddFill(tr, db, space, link, pv, r, yLo, yHi);

            // --- Границы ---
            // Все они, включая начало и конец коридора, — прокси маркеров:
            // концевые границы двигаются так же, как разрывы, и рисовать их
            // отдельными линиями значило бы завести вторую, неподвижную копию.
            // Прокси уже существуют — им достаточно назначить слой и тип линии.
            var boundaries = new List<double>();

            foreach (StationMarker m in session.Store.ForProfile(link.ProfileHandle))
            {
                AdoptProxy(tr, db, m.ProfileProxyHandle);
                AdoptProxy(tr, db, m.PlanProxyHandle);
                boundaries.Add(m.Station);
            }

            // --- Надписи вдоль каждой границы ---
            foreach (double station in boundaries.OrderBy(s => s))
                AddBoundaryLabels(tr, db, space, link, pv, regions,
                                  station, yLo, yHi, height, linkColor);
        }

        /// <summary>
        /// Прямоугольники заливок в координатах чертежа — для RW_BREAKDIAG.
        ///
        /// Если полосы выглядят не полосами, этот список разделяет две
        /// оставшиеся гипотезы: числа верные — врёт построение штриховки;
        /// числа кривые (совпали X, вылез NaN) — врёт пересчёт пикета в точку
        /// вида, и штриховка тут ни при чём.
        /// </summary>
        public static List<string> DescribeFills(Transaction tr, Database db,
                                                 BreakSession session, BreakLink link)
        {
            var lines = new List<string>();
            if (link == null || session == null) return lines;

            var pv = RwHandles.Open<ProfileView>(tr, db, link.ProfileViewHandle, OpenMode.ForRead);
            if (pv == null) { lines.Add("вид профиля не открылся"); return lines; }

            double yLo, yHi;
            if (!ViewBox(pv, out yLo, out yHi))
            {
                lines.Add("габариты вида не получены — заливок не будет");
                return lines;
            }

            lines.Add($"вид: Y {yLo:F3}..{yHi:F3} (высота {yHi - yLo:F3})");

            Baseline baseline = session.GetBaseline(tr, link);
            if (baseline == null) { lines.Add("базовая линия не найдена"); return lines; }

            foreach (RegionInfo r in CollectRegions(tr, baseline))
            {
                Point3d a, b;
                bool okA = RwGeometry.TryPointInProfileView(pv, r.Start, MidElevation(pv), out a);
                bool okB = RwGeometry.TryPointInProfileView(pv, r.End, MidElevation(pv), out b);

                if (!okA || !okB)
                {
                    lines.Add($"участок {r.Start:F3}..{r.End:F3} «{r.AssemblyName}»: " +
                              $"точка вида не получена ({(okA ? "конец" : "начало")})");
                    continue;
                }

                double x1 = Math.Min(a.X, b.X);
                double x2 = Math.Max(a.X, b.X);

                lines.Add($"участок {r.Start:F3}..{r.End:F3} «{r.AssemblyName}»: " +
                          $"X {x1:F3}..{x2:F3} (ширина {x2 - x1:F3})");
            }

            return lines;
        }

        /// <summary>Габариты вида по вертикали. false — вид ещё не отрисован.</summary>
        private static bool ViewBox(ProfileView pv, out double yLo, out double yHi)
        {
            yLo = 0.0;
            yHi = 0.0;

            try
            {
                Extents3d ext = pv.GeometricExtents;
                yLo = Math.Min(ext.MinPoint.Y, ext.MaxPoint.Y);
                yHi = Math.Max(ext.MinPoint.Y, ext.MaxPoint.Y);
                return yHi - yLo > 1e-6;
            }
            catch (System.Exception)
            {
                return false;
            }
        }

        // ------------------------------------------------------------------
        //  УЧАСТКИ
        // ------------------------------------------------------------------

        /// <summary>Участок коридора в том виде, в каком он нужен оформлению.</summary>
        private class RegionInfo
        {
            public Guid Id;
            public double Start;
            public double End;
            public string AssemblyName;
        }

        private static List<RegionInfo> CollectRegions(Transaction tr, Baseline baseline)
        {
            var list = new List<RegionInfo>();

            foreach (BaselineRegion r in baseline.BaselineRegions)
            {
                string name = "—";
                try
                {
                    var asm = tr.GetObject(r.AssemblyId, OpenMode.ForRead)
                              as Autodesk.Civil.DatabaseServices.Assembly;
                    if (asm != null) name = asm.Name;
                }
                catch (System.Exception)
                {
                    // Конструкция не назначена или удалена — так и подпишем.
                }

                list.Add(new RegionInfo
                {
                    Id = r.RegionGUID,
                    Start = r.StartStation,
                    End = r.EndStation,
                    AssemblyName = name
                });
            }

            return list.OrderBy(r => r.Start).ToList();
        }

        /// <summary>
        /// Заливка участка: полоса во всю высоту вида от начала до конца области.
        /// Цвет выводится из GUID области, а не бросается заново при каждой
        /// перестройке: <c>Guid.GetHashCode</c> считается по байтам, поэтому
        /// участок сохраняет свой цвет и после перезапуска Civil 3D.
        /// </summary>
        private static void AddFill(Transaction tr, Database db, BlockTableRecord space,
                                    BreakLink link, ProfileView pv, RegionInfo region,
                                    double yLo, double yHi)
        {
            Point3d a, b;
            if (!RwGeometry.TryPointInProfileView(pv, region.Start, MidElevation(pv), out a)) return;
            if (!RwGeometry.TryPointInProfileView(pv, region.End, MidElevation(pv), out b)) return;

            double x1 = Math.Min(a.X, b.X);
            double x2 = Math.Max(a.X, b.X);
            if (x2 - x1 < 1e-9) return;

            try
            {
                // ------------------------------------------------------------------
                //  Контур — НАСТОЯЩАЯ замкнутая полилиния, петля берётся ПО ОБЪЕКТУ.
                //
                //  Перегрузка `AppendLoop(тип, вершины, вздутия)` здесь уже дважды
                //  дала треугольники вместо полос — сначала с флагом `Outermost`,
                //  потом с `Polyline | Outermost`. Разбираться, как именно она
                //  толкует список вершин, без чертежа нечем, а угадывать дальше
                //  дорого: каждая попытка стоит перезапуска Civil 3D.
                //
                //  Вариант с ObjectIdCollection этого класса ошибок лишён вовсе:
                //  форму задаёт готовая полилиния, и толковать в ней нечего.
                // ------------------------------------------------------------------
                var boundary = new Polyline(4);
                boundary.SetDatabaseDefaults();
                boundary.AddVertexAt(0, new Point2d(x1, yLo), 0.0, 0.0, 0.0);
                boundary.AddVertexAt(1, new Point2d(x2, yLo), 0.0, 0.0, 0.0);
                boundary.AddVertexAt(2, new Point2d(x2, yHi), 0.0, 0.0, 0.0);
                boundary.AddVertexAt(3, new Point2d(x1, yHi), 0.0, 0.0, 0.0);
                boundary.Closed = true;

                space.AppendEntity(boundary);
                tr.AddNewlyCreatedDBObject(boundary, true);

                var hatch = new Hatch();

                // Штриховка требует базы данных до настройки контура — сначала
                // в чертёж, потом узор и петля.
                space.AppendEntity(hatch);
                tr.AddNewlyCreatedDBObject(hatch, true);

                hatch.SetDatabaseDefaults();
                hatch.SetHatchPattern(HatchPatternType.PreDefined, "SOLID");
                hatch.Associative = false;

                using (var ids = new ObjectIdCollection())
                {
                    ids.Add(boundary.ObjectId);
                    hatch.AppendLoop(HatchLoopTypes.Outermost, ids);
                }

                hatch.EvaluateHatch(true);

                // Контур не стираем, а ПРЯЧЕМ. Стереть источник петли обычно
                // можно — заливка неассоциативная, — но проверить это в чертеже
                // сейчас нечем, а невидимый контур гарантированно оставляет
                // петлю валидной и при этом не обводит полосу лишней рамкой.
                // Живёт он на слое заливок, значит гасится и блокируется с ними,
                // а при следующей перестройке стирается как всё оформление.
                boundary.Visible = false;
                boundary.LayerId = EnsureLayer(tr, db, FillLayer, 8);
                Tag(tr, db, boundary, link);
                link.OverlayHandles.Add(boundary.Handle);

                hatch.LayerId = EnsureLayer(tr, db, FillLayer, 8);
                hatch.Color = AcColor.FromColorIndex(ColorMethod.ByAci, RandomColor(region.Id));
                hatch.Transparency = new Transparency(
                    (byte)(255 * (100 - FillTransparencyPercent) / 100));

                Tag(tr, db, hatch, link);
                link.OverlayHandles.Add(hatch.Handle);

                // В самый низ: иначе заливка перекроет сетку и линию профиля.
                SendToBack(tr, db, hatch.ObjectId);
            }
            catch (System.Exception)
            {
                // Один непостроившийся участок не повод бросать остальные.
            }
        }

        private static double MidElevation(ProfileView pv) =>
            (pv.ElevationMin + pv.ElevationMax) / 2.0;

        // ------------------------------------------------------------------
        //  ГРАНИЦЫ
        // ------------------------------------------------------------------

        /// <summary>
        /// Перевести прокси разрыва на слой границ и дать ему штриховой тип линии.
        /// Прокси создаются оркестратором и оформлению не принадлежат — стирать
        /// их вместе с оформлением нельзя, только переодеть.
        /// </summary>
        public static void AdoptProxy(Transaction tr, Database db, Handle handle)
        {
            ObjectId id = RwHandles.Resolve(db, handle);
            if (id.IsNull) return;

            try
            {
                var line = tr.GetObject(id, OpenMode.ForWrite) as Line;
                if (line == null) return;

                ObjectId layerId = EnsureLayer(tr, db, BoundaryLayer, 7);
                if (line.LayerId != layerId) line.LayerId = layerId;

                ApplyBoundaryLinetype(tr, db, line);
            }
            catch (System.Exception)
            {
                // Прокси может быть открыт грип-системой — тогда просто пропускаем.
            }
        }

        // ------------------------------------------------------------------
        //  НАДПИСИ
        // ------------------------------------------------------------------

        /// <summary>
        /// Четыре надписи у одной границы, все повёрнуты на 90° против часовой
        /// стрелки (текст читается снизу вверх):
        ///
        /// <code>
        ///   верх вида │ конструкция    │ конструкция
        ///             │ предыдущего    │ следующего
        ///             │ участка        │ участка
        ///             │                │
        ///             │      ГРАНИЦА   │
        ///             │                │
        ///   низ вида  │ Пикет …        │ имя коридора
        ///             слева            справа
        /// </code>
        ///
        /// Верхние надписи прижаты к верху вида (правое выравнивание при повороте
        /// на 90° уводит текст вниз от точки), нижние — к низу.
        /// </summary>
        private static void AddBoundaryLabels(Transaction tr, Database db, BlockTableRecord space,
                                              BreakLink link, ProfileView pv, List<RegionInfo> regions,
                                              double station, double yLo, double yHi,
                                              double height, short color)
        {
            Point3d anchor;
            if (!RwGeometry.TryPointInProfileView(pv, station, MidElevation(pv), out anchor)) return;

            double gap = height * LabelGap;
            double margin = height * LabelMargin;

            // Слева текст стоит СВОЕЙ ПРАВОЙ стороной к границе, справа — левой.
            // При повороте на 90° тело букв уходит от базовой линии в −X,
            // поэтому правая колонка отодвигается ещё на высоту текста.
            double xLeft = anchor.X - gap;
            double xRight = anchor.X + gap + height;

            string prevAssembly = RegionEndingAt(regions, station);
            string nextAssembly = RegionStartingAt(regions, station);

            // Низ: пикет и коридор — справочное, прячется вместе с режимом.
            AddLabel(tr, db, space, link, "Пикет " + FormatStation(station),
                     new Point3d(xLeft, yLo + margin, 0.0), false, height, InfoLayer, color);

            AddLabel(tr, db, space, link, link.CorridorName,
                     new Point3d(xRight, yLo + margin, 0.0), false, height, InfoLayer, color);

            // Верх: конструкции участков — остаются видны и без режима редактирования.
            AddLabel(tr, db, space, link, prevAssembly,
                     new Point3d(xLeft, yHi - margin, 0.0), true, height, AssemblyLayer, color);

            AddLabel(tr, db, space, link, nextAssembly,
                     new Point3d(xRight, yHi - margin, 0.0), true, height, AssemblyLayer, color);
        }

        /// <summary>
        /// Конструкция участка, который кончается на этом пикете (пусто — такого нет).
        ///
        /// Участок разведён с соседом микроразрывом, поэтому кончается он не на
        /// самом пикете границы, а на <c>S − полузазор</c>. Отсюда условие
        /// «не правее границы и не дальше допуска», а из подходящих берётся
        /// БЛИЖАЙШИЙ: искать «самый правый в пределах допуска» нельзя — при
        /// коротком участке туда попадал бы и следующий за границей.
        /// </summary>
        private static string RegionEndingAt(List<RegionInfo> regions, double station)
        {
            return Nearest(regions, r => station - r.End);
        }

        /// <summary>Конструкция участка, который начинается на этом пикете (или пусто).</summary>
        private static string RegionStartingAt(List<RegionInfo> regions, double station)
        {
            return Nearest(regions, r => r.Start - station);
        }

        /// <summary>
        /// Ближайший участок с нужной стороны. <paramref name="distance"/> обязана
        /// быть неотрицательной у правильной стороны границы и отрицательной
        /// у неправильной.
        /// </summary>
        private static string Nearest(List<RegionInfo> regions, Func<RegionInfo, double> distance)
        {
            RegionInfo best = null;
            double bestDistance = double.MaxValue;

            foreach (RegionInfo candidate in regions)
            {
                double d = distance(candidate);
                if (d < -RegionEpsilon || d > RegionTol) continue;
                if (d >= bestDistance) continue;

                bestDistance = d;
                best = candidate;
            }

            return best == null ? string.Empty : best.AssemblyName;
        }

        /// <summary>
        /// Допуск сопоставления «граница ↔ участок»: наибольший возможный
        /// полузазор (<see cref="ProfileGeometryOps.MaxGap"/> — предел
        /// микроразрыва, а расходятся стороны на половину каждая).
        /// </summary>
        private const double RegionTol = ProfileGeometryOps.MaxGap / 2.0 + 1e-6;

        /// <summary>Запас на округление: граница коридора стоит ровно на пикете участка.</summary>
        private const double RegionEpsilon = 1e-6;

        private static void AddLabel(Transaction tr, Database db, BlockTableRecord space,
                                     BreakLink link, string text, Point3d point, bool topAnchored,
                                     double height, string layer, short color)
        {
            if (string.IsNullOrEmpty(text)) return;

            try
            {
                var label = new DBText();
                label.SetDatabaseDefaults();

                label.TextString = text;
                label.Rotation = Math.PI / 2.0;   // 90° против часовой стрелки

                space.AppendEntity(label);
                tr.AddNewlyCreatedDBObject(label, true);

                // Все свойства текста — из стиля, заданного в чертеже.
                ApplyCurrentTextStyle(tr, db, label, height);

                if (topAnchored)
                {
                    // Выравнивание задаётся ДО точки: AlignmentPoint учитывается
                    // только при не-левом выравнивании, и порядок здесь важен.
                    label.HorizontalMode = TextHorizontalMode.TextRight;
                    label.VerticalMode = TextVerticalMode.TextBase;
                    label.AlignmentPoint = point;
                }
                else
                {
                    label.Position = point;
                }

                label.LayerId = EnsureLayer(tr, db, layer, 7);
                label.Color = AcColor.FromColorIndex(ColorMethod.ByAci, color);

                Tag(tr, db, label, link);
                link.OverlayHandles.Add(label.Handle);
            }
            catch (System.Exception) { }
        }

        // ------------------------------------------------------------------
        //  СЛОИ: ВИДИМОСТЬ И ЗАЩИТА
        // ------------------------------------------------------------------

        /// <summary>
        /// Привести слои к состоянию, положенному режиму. Вызывается в конце
        /// каждой операции, которая могла их разблокировать.
        ///
        /// Режим здесь один на чертёж, хотя включается он у профиля: связей
        /// может быть несколько, а слой общий, и «заблокировать наполовину»
        /// нельзя. Поэтому границы разблокированы, пока режим включён хотя бы
        /// у одной связи (<see cref="BreakSession.AnyEditMode"/>).
        /// </summary>
        public static void ApplyState(Transaction tr, Database db, bool editMode)
        {
            SetLayerState(tr, db, BoundaryLayer, 7, true, !editMode);
            SetLayerState(tr, db, AssemblyLayer, 7, true, true);
            SetLayerState(tr, db, FillLayer, 8, editMode, true);
            SetLayerState(tr, db, InfoLayer, 7, editMode, true);
            SetLayerState(tr, db, BreakController.LayerName, 7, true, false);
        }

        /// <summary>
        /// Снять блокировку со всех своих слоёв. Без этого нельзя ни стереть
        /// старое оформление, ни подвинуть прокси: на заблокированном слое
        /// открытие объекта на запись бросает eOnLockedLayer.
        /// </summary>
        public static void Unlock(Transaction tr, Database db)
        {
            foreach (string name in AllLayers)
                SetLayerLock(tr, db, name, false);
        }

        private static IEnumerable<string> AllLayers
        {
            get
            {
                yield return BoundaryLayer;
                yield return FillLayer;
                yield return InfoLayer;
                yield return AssemblyLayer;
                yield return BreakController.LayerName;
            }
        }

        private static void SetLayerLock(Transaction tr, Database db, string name, bool locked)
        {
            try
            {
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                if (!lt.Has(name)) return;

                var ltr = (LayerTableRecord)tr.GetObject(lt[name], OpenMode.ForWrite);
                if (ltr.IsLocked != locked) ltr.IsLocked = locked;
            }
            catch (System.Exception) { }
        }

        private static void SetLayerState(Transaction tr, Database db, string name,
                                          short colorIndex, bool visible, bool locked)
        {
            try
            {
                ObjectId id = EnsureLayer(tr, db, name, colorIndex);
                if (id.IsNull) return;

                var ltr = (LayerTableRecord)tr.GetObject(id, OpenMode.ForWrite);

                // Выключение, а не заморозка: замороженный слой нельзя выключить
                // для текущего вида без регенерации, а разница для пользователя
                // здесь нулевая.
                if (ltr.IsOff != !visible) ltr.IsOff = !visible;
                if (ltr.IsLocked != locked) ltr.IsLocked = locked;
            }
            catch (System.Exception) { }
        }

        public static ObjectId EnsureLayer(Transaction tr, Database db, string name, short colorIndex)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(name)) return lt[name];

            lt.UpgradeOpen();
            var ltr = new LayerTableRecord
            {
                Name = name,
                Color = AcColor.FromColorIndex(ColorMethod.ByAci, colorIndex)
            };

            ObjectId id = lt.Add(ltr);
            tr.AddNewlyCreatedDBObject(ltr, true);
            return id;
        }

        // ------------------------------------------------------------------
        //  ОБЩЕЕ
        // ------------------------------------------------------------------

        /// <summary>
        /// Применить к надписи текстовый стиль, заданный в чертеже для нового
        /// текста, — целиком: высоту, ширину знаков, наклон и аннотативность.
        ///
        /// Своей высоты у надписей нет и быть не должно: подписи в виде профиля
        /// обязаны выглядеть так же, как остальной текст чертежа. Аннотативность
        /// тоже берётся у стиля, а не назначается: аннотативная надпись в
        /// неаннотативном чертеже пропала бы с экрана, стоило переключить масштаб.
        /// </summary>
        private static void ApplyCurrentTextStyle(Transaction tr, Database db,
                                                  DBText label, double height)
        {
            try
            {
                if (db.Textstyle.IsNull) { label.Height = height; return; }

                label.TextStyleId = db.Textstyle;

                var style = tr.GetObject(db.Textstyle, OpenMode.ForRead) as TextStyleTableRecord;
                if (style == null) { label.Height = height; return; }

                // У стиля с фиксированной высотой она своя и переписать её нельзя;
                // у стиля с нулевой («переменной») высоту задаёт чертёж — TEXTSIZE.
                label.Height = height;
                label.WidthFactor = style.XScale > 1e-9 ? style.XScale : 1.0;
                label.Oblique = style.ObliquingAngle;

                // Аннотативность назначается только после добавления объекта
                // в чертёж: ему нужен менеджер контекстов базы.
                if (style.Annotative == AnnotativeStates.True)
                    label.Annotative = AnnotativeStates.True;
            }
            catch (System.Exception)
            {
                // Стиль недоступен — надпись всё равно должна появиться.
                try { label.Height = height; } catch (System.Exception) { }
            }
        }

        /// <summary>
        /// Цвет по Guid. Не <c>Random</c>: цвет обязан пережить перестроение
        /// и перезапуск, иначе участок каждый раз менял бы вид и запомнить его
        /// было бы нельзя. <c>Guid.GetHashCode</c> считается по байтам значения,
        /// поэтому одинаков в любом сеансе.
        /// </summary>
        public static short RandomColor(Guid seed)
        {
            int hash = seed.GetHashCode() & 0x7FFFFFFF;

            // 1..240: дальше идут служебные оттенки серого, а 7 — цвет фона.
            short color = (short)(1 + hash % 240);
            return color == 7 ? (short)8 : color;
        }

        /// <summary>
        /// Высота текста, заданная в чертеже: сначала текущий стиль, затем
        /// <c>TEXTSIZE</c> (у стиля с «переменной» высотой она нулевая, и высоту
        /// задаёт именно эта переменная), и лишь напоследок запасное значение.
        ///
        /// Число нужно не только самой надписи, но и раскладке: от него считаются
        /// отступы от границы и от краёв вида, — поэтому оно вычисляется отдельно,
        /// а не только присваивается объекту.
        /// </summary>
        public static double TextHeight(Transaction tr, Database db)
        {
            double height = 0.0;

            try
            {
                var style = tr.GetObject(db.Textstyle, OpenMode.ForRead) as TextStyleTableRecord;
                if (style != null) height = style.TextSize;
            }
            catch (System.Exception) { }

            if (height <= 1e-9) height = db.Textsize;
            if (height <= 1e-9) height = FallbackTextHeight;

            return height;
        }

        /// <summary>
        /// Пикет в привычном виде: <c>ПК 1+23.46</c> — сотня метров плюс остаток.
        ///
        /// Остаток дополняется нулём слева (<c>ПК 0+05.50</c>): без этого
        /// «5.50» и «55.00» в столбце подписей читаются одинаково быстро,
        /// а означают разное.
        ///
        /// Округление идёт ДО деления на сотни. Иначе пикет 99.999 дал бы
        /// «ПК 0+100.00»: остаток округлялся бы до сотни уже после того,
        /// как номер пикета посчитан.
        ///
        /// Отрицательные пикеты (ось начинается до нуля) получаются сами:
        /// <c>Math.Floor</c> уводит номер в −1, и остаток остаётся
        /// положительным — «ПК -1+94.50», как и принято.
        ///
        /// **Уравнения пикетажа не учитываются.** Модуль везде работает
        /// с сырым пикетом (см. «Не сделано», п. 5), и подпись здесь не
        /// исключение: на оси с уравнениями она разойдётся со штатными
        /// метками Civil.
        /// </summary>
        public static string FormatStation(double station)
        {
            if (double.IsNaN(station) || double.IsInfinity(station)) return "—";

            double rounded = Math.Round(station, 2, MidpointRounding.AwayFromZero);

            double pk = Math.Floor(rounded / 100.0);
            double rest = rounded - pk * 100.0;

            // Остаток мог дотянуться до сотни на самом округлении.
            if (rest >= 100.0 - 1e-9) { pk += 1.0; rest = 0.0; }

            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "ПК {0}+{1:00.00}", (long)pk, rest);
        }

        /// <summary>
        /// Штриховой тип линии. Нет в чертеже — подгружается из стандартного
        /// файла; не вышло — линия остаётся сплошной, падать из-за этого нельзя.
        /// </summary>
        public static void ApplyBoundaryLinetype(Transaction tr, Database db, Entity entity)
        {
            try
            {
                var ltt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);

                if (!ltt.Has(BoundaryLinetype))
                {
                    foreach (string file in new[] { "acadiso.lin", "acad.lin" })
                    {
                        try
                        {
                            db.LoadLineTypeFile(BoundaryLinetype, file);
                            break;
                        }
                        catch (System.Exception) { }
                    }

                    ltt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
                    if (!ltt.Has(BoundaryLinetype)) return;
                }

                ObjectId id = ltt[BoundaryLinetype];
                if (entity.LinetypeId != id) entity.LinetypeId = id;
            }
            catch (System.Exception) { }
        }

        /// <summary>
        /// Пометить объект оформления Guid его связи. Стирание идёт по хэндлам
        /// из записи, а метка нужна диагностике: по ней видно осиротевшие
        /// объекты, оставшиеся от прежних перестроений или от копирования.
        /// </summary>
        private static void Tag(Transaction tr, Database db, Entity entity, BreakLink link)
        {
            try
            {
                var rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
                if (!rat.Has(XAppName))
                {
                    rat.UpgradeOpen();
                    var rec = new RegAppTableRecord { Name = XAppName };
                    rat.Add(rec);
                    tr.AddNewlyCreatedDBObject(rec, true);
                }

                entity.XData = new ResultBuffer(
                    new TypedValue((int)DxfCode.ExtendedDataRegAppName, XAppName),
                    new TypedValue((int)DxfCode.ExtendedDataAsciiString, link.Id.ToString("N")));
            }
            catch (System.Exception) { }
        }

        /// <summary>Guid связи из XData объекта оформления. null — объект не наш.</summary>
        public static Guid? GetOverlayLinkId(Entity entity)
        {
            if (entity == null) return null;

            try
            {
                ResultBuffer rb = entity.GetXDataForApplication(XAppName);
                if (rb == null) return null;

                foreach (TypedValue tv in rb)
                    if (tv.TypeCode == (int)DxfCode.ExtendedDataAsciiString &&
                        Guid.TryParseExact(tv.Value.ToString(), "N", out Guid g))
                        return g;
            }
            catch (System.Exception) { }

            return null;
        }

        private static void SendToBack(Transaction tr, Database db, ObjectId entityId)
        {
            if (entityId.IsNull) return;

            try
            {
                var ent = tr.GetObject(entityId, OpenMode.ForRead) as Entity;
                if (ent == null) return;

                var owner = tr.GetObject(ent.BlockId, OpenMode.ForRead) as BlockTableRecord;
                if (owner == null) return;

                var order = tr.GetObject(owner.DrawOrderTableId, OpenMode.ForWrite) as DrawOrderTable;
                if (order == null) return;

                using (var ids = new ObjectIdCollection())
                {
                    ids.Add(entityId);
                    order.MoveToBottom(ids);
                }
            }
            catch (System.Exception)
            {
                // Порядок отрисовки — удобство, а не механика.
            }
        }
    }
}
