using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Civil3D_commands.Shared;

namespace Civil3D_commands.AssociativeBreaks
{
    /// <summary>
    /// Итог создания смены отметки — для сообщения команды.
    /// </summary>
    public sealed class SplitResult
    {
        public StationMarker Marker;
        public BreakLink LeftLink;
        public BreakLink RightLink;

        /// <summary>Чего не удалось перенести или что стоит знать пользователю.</summary>
        public List<string> Notes = new List<string>();
    }

    /// <summary>
    /// Смена отметки коридора (RW_BOTTOMSTEP).
    ///
    /// Ступень в профиле (обычный разрыв) ломает ОДИН профиль парой PVI, и коридор
    /// остаётся на одной базовой линии. Смена отметки делает иначе: профиль
    /// РЕЖЕТСЯ на два самостоятельных, и каждому достаётся своя базовая линия:
    ///
    ///   левый профиль   — кончается на S − полузазор;
    ///   правый профиль  — начинается на S + полузазор, на <c>delta</c> выше/ниже,
    ///                     дальше повторяет прежний профиль, сдвинутый на delta;
    ///   левая базовая   — прежняя, её участки обрезаются по пикету;
    ///   правая базовая  — новая, участки правее S переезжают в неё.
    ///
    /// Между ними стоит ОДИН маркер с ролью <see cref="StationMarker.MarkerRole.Split"/>
    /// и обычной парой прокси: его двигают и ручкой, и веткой «Перенести», как
    /// любой разрыв. Левая часть остаётся в прежней связи, для правой заводится
    /// своя (<see cref="BreakLink"/>), и маркеры правее S переходят в неё.
    ///
    /// Все правки профиля ограничены тем, что в этом проекте уже проверено в чертеже
    /// или заведомо допустимо: создание профиля двумя PVI, вставка PVI ВНУТРИ
    /// профиля, удаление PVI, правка станции и отметки КРАЙНИХ PVI. Вставка PVI
    /// за пределами уже построенного профиля не используется нигде.
    /// </summary>
    public partial class AssociativeBreakManager
    {
        /// <summary>Допуск при сравнении пикетов вершин профиля.</summary>
        private const double SplitTol = 1e-6;

        /// <summary>Насколько близко к ожидаемому пикету должна лежать вершина, чтобы считаться «той самой».</summary>
        private const double SplitFindTol = 1e-3;

        private sealed class Pt
        {
            public double Station;
            public double Elevation;
            public Pt(double s, double e) { Station = s; Elevation = e; }
        }

        private sealed class RegionPlan
        {
            public BaselineRegion Old;
            public Guid OldId;
            public string Name;
            public ObjectId AssemblyId;
            public double Start;
            public double End;
            public bool IsHost;
            public bool Skip;
            public SubassemblyTargetInfoCollection Targets;
        }

        // ======================================================================
        //  СОЗДАНИЕ
        // ======================================================================

        /// <summary>
        /// Разделить коридор связи <paramref name="link"/> на пикете
        /// <paramref name="station"/> и сместить правую часть на <paramref name="delta"/>
        /// (+ вверх, − вниз). Бросает <see cref="InvalidOperationException"/> с понятным
        /// текстом, если сделать этого нельзя: транзакция при этом откатывается.
        /// </summary>
        public SplitResult CreateSplit(BreakLink link, double station, double delta)
        {
            if (link == null) throw new ArgumentNullException(nameof(link));
            if (double.IsNaN(station) || double.IsInfinity(station))
                throw new InvalidOperationException("Пикет не определён.");
            if (double.IsNaN(delta) || double.IsInfinity(delta) || Math.Abs(delta) < 1e-9)
                throw new InvalidOperationException("Высота смены отметки не может быть нулевой.");

            var db = Doc.Database;
            var civDoc = CivilApplication.ActiveDocument;
            var result = new SplitResult { LeftLink = link };
            bool edit = _session.IsEditMode(link.ProfileHandle);
            Handle rightHandle;

            using (Doc.LockDocument())
            using (_session.Suspend())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                BreakOverlay.Unlock(tr, db);

                var profile = RwHandles.Open<Profile>(tr, db, link.ProfileHandle, OpenMode.ForWrite);
                var pv = RwHandles.Open<ProfileView>(tr, db, link.ProfileViewHandle, OpenMode.ForRead);
                var alignment = RwHandles.Open<Alignment>(tr, db, link.AlignmentHandle, OpenMode.ForRead);
                var corridor = RwHandles.Open<Corridor>(tr, db, link.CorridorHandle, OpenMode.ForWrite);

                if (profile == null || pv == null || alignment == null || corridor == null)
                    throw new InvalidOperationException(
                        "Связь потеряла профиль, вид, ось или коридор — открыть их не удалось.");

                Baseline left = _session.GetBaselineByProfile(tr, corridor, link.ProfileHandle);
                if (left == null)
                    throw new InvalidOperationException(
                        "У коридора нет базовой линии, построенной на профиле «" + profile.Name + "».");

                StationMarker marker = link.NewMarker();
                marker.Role = StationMarker.MarkerRole.Split;
                marker.Station = station;
                marker.StepHeight = delta;
                marker.IsStep = false;
                marker.Gap = 0.0;                 // стык ровно на пикете, HalfGap у Split = 0
                double half = marker.HalfGap;
                double cutLeft = station - half;
                double startRight = station + half;

                BreakTrace.Snapshot(tr, db, _session, marker,
                    string.Format(CultureInfo.InvariantCulture,
                        "СМЕНА ОТМЕТКИ, пикет {0:F3}, сдвиг {1:F3}, зазор {2}", station, delta, marker.Gap));

                PreflightSplit(profile, left, marker, half);
                BreakTrace.Step("проверка пройдена");

                // --- Что есть до правок: отметки на концах зазора и вершины профиля ---
                double eLeftEnd = profile.ElevationAt(cutLeft);
                double eRightStart = profile.ElevationAt(startRight) + delta;
                double profileEnd = profile.EndingStation;

                var pvis = profile.PVIs.Cast<ProfilePVI>()
                                  .OrderBy(p => p.Station)
                                  .Select(p => new Pt(p.Station, p.Elevation))
                                  .ToList();
                double endElev = pvis[pvis.Count - 1].Elevation + delta;

                // --- Правый профиль ---
                string baseName = left.Name;
                string profName = UniqueProfileName(tr, alignment,
                    baseName + " " + eRightStart.ToString("0.###", CultureInfo.InvariantCulture));

                BreakTrace.Step("профиль: создание правого «" + profName + "»");
                ObjectId rightId = CreateRightProfile(
                    tr, db, civDoc, profile, alignment, profName,
                    startRight, eRightStart, profileEnd, endElev, pvis, delta, result.Notes);

                var rightProfile = (Profile)tr.GetObject(rightId, OpenMode.ForWrite);
                rightHandle = rightProfile.Handle;

                PropertySetSupport.Attach(tr, rightId, PropertySetSupport.EnsureEditPsd(db));

                // --- Правая базовая линия и перенос участков ---
                string blName = UniqueBaselineName(corridor, profName);
                BreakTrace.Step("коридор: базовая линия «" + blName + "»");
                Baseline right = corridor.Baselines.Add(blName, alignment.ObjectId, rightId);

                BreakTrace.Step("коридор: перенос участков");
                Guid hostId, firstRightId;
                Dictionary<Guid, Guid> map = TransferRegions(
                    left, right, cutLeft, startRight, result.Notes, out hostId, out firstRightId);

                marker.LeftRegionId = hostId;
                marker.RightRegionId = firstRightId;

                // --- Левый профиль: обрезать по S − полузазор ---
                // Участки левой базовой линии уже ужаты (TransferRegions) — профиль
                // можно резать, не оставляя область за его концом.
                BreakTrace.Step("профиль: обрезка левого");
                TruncateProfileEnd(profile, cutLeft, eLeftEnd);

                // --- Маркеры правее S переезжают в правую связь ---
                BreakLink rightLink = new BreakLink
                {
                    ProfileHandle     = rightHandle,
                    ProfileViewHandle = link.ProfileViewHandle,
                    AlignmentHandle   = link.AlignmentHandle,
                    CorridorHandle    = link.CorridorHandle
                };
                rightLink.ColorIndex = BreakOverlay.RandomColor(rightLink.Id);
                rightLink.RefreshNames(tr, db);

                foreach (StationMarker mk in _session.Store.ForProfile(link.ProfileHandle)
                                                     .Where(x => x.Station > station).ToList())
                {
                    mk.ProfileHandle = rightHandle;
                    mk.BaseElevation += delta;

                    Guid g;
                    if (map.TryGetValue(mk.LeftRegionId, out g)) mk.LeftRegionId = g;
                    if (map.TryGetValue(mk.RightRegionId, out g)) mk.RightRegionId = g;
                }

                int slot = _session.Links.Count(l => l.ProfileViewHandle == link.ProfileViewHandle);
                BreakController.Ensure(tr, db, rightLink, slot);
                _session.AddLink(rightLink);

                // --- Сам маркер и его прокси ---
                marker.NextProfileHandle = rightHandle;
                marker.BaseElevation = eLeftEnd;

                BreakTrace.Step("прокси смены отметки");
                BreakProxyFactory.CreateProxies(tr, db, marker, pv, alignment);
                _session.Store.Add(marker);
                WriteProps(tr, marker);

                BreakTrace.Step("коридор: Rebuild");
                corridor.Rebuild();

                // Профиль должен быть ВИДЕН в виде: тот же заслон, что у мастера связи.
                BreakCommands.EnsureProfileVisible(
                    tr, db, Doc.Editor, pv.ObjectId, rightProfile, eRightStart);

                BreakTrace.Step("оформление");
                RefreshOverlays(tr, link, rightLink);

                BreakTrace.Step("коммит транзакции");
                tr.Commit();
                BreakTrace.Step("коммит прошёл");

                result.Marker = marker;
                result.RightLink = rightLink;
            }

            _session.Store.SaveToDatabase(db);
            _session.SaveLinks();

            // Режим правки переносится на правый профиль: иначе ручки у маркеров,
            // уехавших в него, пропали бы, хотя левая часть правится.
            if (edit) _session.SetEditMode(rightHandle, true);

            return result;
        }

        /// <summary>
        /// Проверки до первой правки. Профиль и коридор правятся по очереди, и
        /// отказ посередине оставил бы их в разных состояниях — а нативный вызов
        /// Civil, получивший негодные данные, иногда роняет процесс.
        /// </summary>
        private void PreflightSplit(Profile profile, Baseline left, StationMarker m, double half)
        {
            // Ломать (резать) можно только проектный профиль по трассе: у остальных
            // правка вершин не определена — то же правило, что у ступени.
            if (profile.ProfileType != ProfileType.FG)
                throw new InvalidOperationException(
                    "Профиль «" + profile.Name + "» имеет тип " + profile.ProfileType +
                    ", а смена отметки работает только с проектным профилем (FG).");

            double buf = BreakGripOverrule.Buffer;
            double s = m.Station;

            if (!(s - half > profile.StartingStation + buf) || !(s + half < profile.EndingStation - buf))
                throw new InvalidOperationException(
                    "Пикет " + s.ToString("F3") + " слишком близко к краю профиля (" +
                    profile.StartingStation.ToString("F3") + ".." + profile.EndingStation.ToString("F3") +
                    "): от края нужно не меньше " + buf.ToString("0.###") + " м.");

            var bounds = _session.Store.GetMoveBounds(m, buf, left.StartStation, left.EndStation);
            if (s < bounds.min - 1e-9 || s > bounds.max + 1e-9)
                throw new InvalidOperationException(
                    "Пикет " + s.ToString("F3") + " слишком близко к другой границе коридора. " +
                    "Допустимо " + bounds.min.ToString("F3") + ".." + bounds.max.ToString("F3") + ".");

            if (ProfileGeometryOps.FindRegionAt(left.BaselineRegions, s, half) == null)
                throw new InvalidOperationException(
                    "Пикет " + s.ToString("F3") + " не попадает ни в один участок коридора.");

            foreach (ProfilePVI p in profile.PVIs.Cast<ProfilePVI>())
                if (Math.Abs(p.Station - (s - half)) < SplitFindTol * 0.1 ||
                    Math.Abs(p.Station - (s + half)) < SplitFindTol * 0.1)
                    throw new InvalidOperationException(
                        "На этом пикете в профиле уже стоит вершина — сдвиньте пикет.");
        }

        /// <summary>
        /// Правый профиль: создаётся двумя вершинами (начало и КОНЕЦ — так же, как
        /// строит плоский профиль мастер связи), потом внутрь добавляются остальные.
        /// Вставка вершин за пределами уже построенного профиля не используется.
        /// </summary>
        private ObjectId CreateRightProfile(Transaction tr, Database db, CivilDocument civDoc,
                                            Profile source, Alignment alignment, string name,
                                            double start, double startElev,
                                            double end, double endElev,
                                            List<Pt> pvis, double delta, List<string> notes)
        {
            // Стиль профиля прочитать нельзя (свойство только для записи), поэтому
            // берётся тот же, что у мастера. Слой — как у исходного профиля.
            ObjectId styleId = civDoc.Styles.ProfileStyles[0];
            ObjectId labelSetId = BreakCommands.FindEmptyLabelSet(civDoc, tr);
            ObjectId layerId = source.LayerId;

            ObjectId rightId;
            try
            {
                rightId = Profile.CreateByLayout(name, alignment.ObjectId, layerId, styleId, labelSetId);
            }
            catch (System.Exception)
            {
                rightId = Profile.CreateByLayout(
                    name, alignment.ObjectId, layerId, styleId,
                    civDoc.Styles.LabelSetStyles.ProfileLabelSetStyles[0]);
                notes.Add("Пустой набор меток профиля не применился — метки правого профиля придётся снять вручную.");
            }

            var right = (Profile)tr.GetObject(rightId, OpenMode.ForWrite);
            right.PVIs.AddPVI(start, startElev);
            right.PVIs.AddPVI(end, endElev);

            // Остальные вершины прежнего профиля правее зазора — те же, но на delta.
            foreach (Pt p in pvis.Where(p => p.Station > start + SplitTol && p.Station < end - SplitTol))
                right.PVIs.AddPVI(p.Station, p.Elevation + delta);

            if (source.PVIs.Cast<ProfilePVI>().Any(p => p.PVIType == ProfileEntityType.Circular
                                                      || p.PVIType == ProfileEntityType.ParabolaSymmetric
                                                      || p.PVIType == ProfileEntityType.ParabolaAsymmetric))
                notes.Add("В исходном профиле есть вертикальные кривые — в правом профиле вершины " +
                          "перенесены без кривых (прямыми).");

            return rightId;
        }

        /// <summary>
        /// Перенести участки правее зазора из левой базовой линии в правую.
        /// Конструкция, имя и (по возможности) цели участка переезжают с ним.
        ///
        /// Участок, внутрь которого попал разрез («хозяин»), остаётся слева до
        /// <paramref name="cutLeft"/>, а справа получает продолжение с тем же
        /// именем и конструкцией. Целиком правые участки стираются слева и
        /// создаются справа заново — у них меняется Guid, поэтому возвращается
        /// карта «прежний Guid → новый» для маркеров, переходящих вместе с ними.
        /// </summary>
        private Dictionary<Guid, Guid> TransferRegions(Baseline left, Baseline right,
                                                       double cutLeft, double startRight,
                                                       List<string> notes,
                                                       out Guid hostId, out Guid firstRightId)
        {
            hostId = Guid.Empty;
            firstRightId = Guid.Empty;

            var plans = new List<RegionPlan>();
            foreach (BaselineRegion r in left.BaselineRegions)
            {
                // Целиком слева — не трогаем.
                if (r.EndStation <= cutLeft + SplitTol) continue;

                var plan = new RegionPlan
                {
                    Old = r,
                    OldId = r.RegionGUID,
                    Name = r.Name,
                    AssemblyId = r.AssemblyId,
                    End = r.EndStation
                };

                if (r.StartStation >= cutLeft - SplitTol)
                {
                    // Целиком правее разреза.
                    plan.Start = Math.Max(r.StartStation, startRight);
                }
                else
                {
                    plan.Start = startRight;
                    plan.IsHost = true;
                }

                plan.Skip = plan.End - plan.Start < SplitTol;

                try { plan.Targets = r.GetTargets(); }
                catch (System.Exception)
                {
                    notes.Add("Цели участка «" + r.Name + "» прочитать не удалось — задайте их заново.");
                }

                plans.Add(plan);
            }

            plans = plans.OrderBy(p => p.Start).ToList();

            if (!plans.Any(p => p.IsHost && !p.Skip))
                throw new InvalidOperationException(
                    "Не нашлось участка, который можно разрезать на этом пикете.");

            // --- Правая базовая линия ---
            // Новая базовая линия может прийти уже с участком по умолчанию — тогда
            // первый переносимый участок занимает его, а остальные создаются.
            var defaults = new List<BaselineRegion>();
            foreach (BaselineRegion r in right.BaselineRegions) defaults.Add(r);

            var map = new Dictionary<Guid, Guid>();
            int index = 0;

            foreach (RegionPlan plan in plans.Where(p => !p.Skip))
            {
                BaselineRegion created;

                if (index == 0 && defaults.Count > 0)
                {
                    created = defaults[0];
                    created.Name = plan.Name;
                    created.AssemblyId = plan.AssemblyId;
                    created.StartStation = plan.Start;
                    created.EndStation = plan.End;
                }
                else
                {
                    created = right.BaselineRegions.Add(plan.Name, plan.AssemblyId, plan.Start, plan.End);
                }

                index++;

                if (plan.Targets != null)
                {
                    try { created.SetTargets(plan.Targets); }
                    catch (System.Exception)
                    {
                        notes.Add("Цели участка «" + plan.Name + "» не перенесены — задайте их заново.");
                    }
                }

                map[plan.OldId] = created.RegionGUID;
                if (plan.IsHost) hostId = plan.OldId;
                if (firstRightId == Guid.Empty) firstRightId = created.RegionGUID;
            }

            // Лишние участки по умолчанию.
            for (int i = 1; i < defaults.Count; i++)
            {
                try { right.BaselineRegions.Remove(defaults[i]); }
                catch (System.Exception)
                {
                    notes.Add("В правой базовой линии остался лишний участок по умолчанию — удалите его вручную.");
                }
            }

            // --- Левая базовая линия ---
            foreach (RegionPlan plan in plans)
            {
                if (plan.IsHost) plan.Old.EndStation = cutLeft;
                else left.BaselineRegions.Remove(plan.Old);
            }

            return map;
        }

        /// <summary>
        /// Обрезать профиль на <paramref name="cut"/>: вершина ставится ВНУТРИ
        /// профиля (допустимо), всё правее неё снимается.
        /// </summary>
        private static void TruncateProfileEnd(Profile profile, double cut, double elevation)
        {
            profile.PVIs.AddPVI(cut, elevation);

            foreach (ProfilePVI p in profile.PVIs.Cast<ProfilePVI>()
                                            .Where(p => p.Station > cut + SplitTol).ToList())
                profile.PVIs.Remove(p);
        }

        // ======================================================================
        //  ПЕРЕНОС
        // ======================================================================

        /// <summary>
        /// Новый пикет смены отметки (вызывается реактором после ручки и веткой
        /// «Перенести»). Левый профиль удлиняется или укорачивается, правый — ровно
        /// наоборот, границы участков идут следом, затем один Rebuild.
        /// </summary>
        private void ApplySplitStationChange(StationMarker m, double newStation)
        {
            double old = m.Station;
            var db = Doc.Database;

            try
            {
                using (Doc.LockDocument())
                using (_session.Suspend())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    BreakOverlay.Unlock(tr, db);

                    var left = RwHandles.Open<Profile>(tr, db, m.ProfileHandle, OpenMode.ForWrite);
                    var right = RwHandles.Open<Profile>(tr, db, m.NextProfileHandle, OpenMode.ForWrite);
                    var pv = RwHandles.Open<ProfileView>(tr, db, m.ProfileViewHandle, OpenMode.ForRead);
                    var alignment = RwHandles.Open<Alignment>(tr, db, m.AlignmentHandle, OpenMode.ForRead);
                    var corridor = RwHandles.Open<Corridor>(tr, db, m.CorridorHandle, OpenMode.ForWrite);

                    if (left == null || right == null || corridor == null)
                        throw new InvalidOperationException(
                            "У смены отметки не открылся профиль или коридор — она осталась на прежнем месте.");

                    Baseline bl = _session.GetBaselineByProfile(tr, corridor, m.ProfileHandle);
                    Baseline br = _session.GetBaselineByProfile(tr, corridor, m.NextProfileHandle);
                    if (bl == null || br == null)
                        throw new InvalidOperationException(
                            "У смены отметки нет одной из базовых линий — она осталась на прежнем месте.");

                    var bounds = _session.Store.GetMoveBounds(
                        m, BreakGripOverrule.Buffer, bl.StartStation, br.EndStation);
                    double s = Math.Min(Math.Max(newStation, bounds.min), bounds.max);

                    if (Math.Abs(s - old) > 1e-6)
                    {
                        m.Station = s;

                        BreakTrace.Snapshot(tr, db, _session, m,
                            string.Format(CultureInfo.InvariantCulture,
                                "ПЕРЕНОС смены отметки {0:F3} -> {1:F3}", old, s));

                        MoveSplit(m, old, s, left, right, bl, br);

                        BreakTrace.Step("коридор: Rebuild");
                        corridor.Rebuild();
                    }

                    // Прокси пересчитываем всегда — и когда кламп съел всё перемещение.
                    BreakProxyFactory.UpdateProxyGeometry(tr, m, pv, alignment);
                    WriteProps(tr, m);

                    BreakLink l1 = _session.FindLinkByProfile(m.ProfileHandle, m.CorridorHandle);
                    BreakLink l2 = _session.FindLinkByProfile(m.NextProfileHandle, m.CorridorHandle);
                    RefreshOverlays(tr, l1, l2);

                    BreakTrace.Step("коммит транзакции");
                    tr.Commit();
                    BreakTrace.Step("коммит прошёл");
                }
            }
            catch (System.Exception)
            {
                // Транзакция откатилась — модель обязана остаться с прежним пикетом.
                m.Station = old;
                throw;
            }

            _session.Store.SaveToDatabase(db);
            _session.SaveLinks();
        }

        /// <summary>
        /// Переставить границу между двумя профилями. Прежняя форма профиля
        /// восстанавливается из обоих (правый сдвигается обратно на delta),
        /// после чего каждому достаётся своя часть по новому пикету.
        /// </summary>
        private static void MoveSplit(StationMarker m, double old, double s,
                                      Profile left, Profile right, Baseline bl, Baseline br)
        {
            double half = m.HalfGap;
            double delta = m.StepHeight;

            double lOld = old - half, rOld = old + half;
            double lNew = s - half, rNew = s + half;

            // Прежняя форма: вершины левого левее зазора и правого правее него.
            // Вершины на самих концах зазора искусственные — они и переставляются.
            var shape = new List<Pt>();
            foreach (ProfilePVI p in left.PVIs.Cast<ProfilePVI>())
                if (p.Station < lOld - SplitTol) shape.Add(new Pt(p.Station, p.Elevation));
            foreach (ProfilePVI p in right.PVIs.Cast<ProfilePVI>())
                if (p.Station > rOld + SplitTol) shape.Add(new Pt(p.Station, p.Elevation - delta));
            shape = shape.OrderBy(p => p.Station).ToList();

            // Отметки на новых концах считаются ДО правок, по тому профилю,
            // на чьей стороне пикет сейчас.
            double fL = old >= lNew ? ElevationClamped(left, lNew) : ElevationClamped(right, lNew) - delta;
            double fR = old >= rNew ? ElevationClamped(left, rNew) : ElevationClamped(right, rNew) - delta;

            BaselineRegion lr = ProfileGeometryOps.FindByGuid(bl.BaselineRegions, m.LeftRegionId)
                                ?? RegionWithMaxEnd(bl);
            BaselineRegion rr = ProfileGeometryOps.FindByGuid(br.BaselineRegions, m.RightRegionId)
                                ?? RegionWithMinStart(br);
            if (lr == null || rr == null)
                throw new InvalidOperationException("Не найдены участки по обе стороны смены отметки.");

            if (s > old)
            {
                // Левый удлиняется, правый укорачивается.
                BreakTrace.Step("профиль: левый удлиняется");
                ExtendProfileEnd(left, lNew, fL, shape.Where(p => p.Station > lOld + SplitTol && p.Station < lNew - SplitTol), 0.0);
                lr.EndStation = lNew;

                BreakTrace.Step("профиль: правый укорачивается");
                rr.StartStation = rNew;
                TruncateProfileStart(right, rNew, fR + delta);
            }
            else
            {
                // Левый укорачивается, правый удлиняется.
                BreakTrace.Step("профиль: левый укорачивается");
                lr.EndStation = lNew;
                TruncateProfileEnd(left, lNew, fL);

                BreakTrace.Step("профиль: правый удлиняется");
                ExtendProfileStart(right, rNew, fR + delta,
                                   shape.Where(p => p.Station > rNew + SplitTol && p.Station < rOld - SplitTol), delta);
                rr.StartStation = rNew;
            }

            m.LeftRegionId = lr.RegionGUID;
            m.RightRegionId = rr.RegionGUID;
            m.BaseElevation = fL;
        }

        /// <summary>
        /// Сдвинуть последнюю вершину профиля на новый пикет (правее прежнего),
        /// а промежуточные вершины формы добавить внутрь. За концы профиля
        /// вершина не вставляется — двигается существующая крайняя.
        /// </summary>
        private static void ExtendProfileEnd(Profile profile, double newEnd, double elevation,
                                             IEnumerable<Pt> interior, double shift)
        {
            ProfilePVI last = profile.PVIs.Cast<ProfilePVI>().OrderBy(p => p.Station).Last();
            last.Station = newEnd;
            last.Elevation = elevation;

            foreach (Pt p in interior.OrderBy(p => p.Station).ToList())
                profile.PVIs.AddPVI(p.Station, p.Elevation + shift);
        }

        /// <summary>Зеркально <see cref="ExtendProfileEnd"/>: сдвинуть первую вершину влево.</summary>
        private static void ExtendProfileStart(Profile profile, double newStart, double elevation,
                                               IEnumerable<Pt> interior, double shift)
        {
            ProfilePVI first = profile.PVIs.Cast<ProfilePVI>().OrderBy(p => p.Station).First();
            first.Station = newStart;
            first.Elevation = elevation;

            foreach (Pt p in interior.OrderBy(p => p.Station).ToList())
                profile.PVIs.AddPVI(p.Station, p.Elevation + shift);
        }

        /// <summary>Сдвинуть начало профиля вправо: вершина ставится внутрь, всё левее снимается.</summary>
        private static void TruncateProfileStart(Profile profile, double cut, double elevation)
        {
            profile.PVIs.AddPVI(cut, elevation);

            foreach (ProfilePVI p in profile.PVIs.Cast<ProfilePVI>()
                                            .Where(p => p.Station < cut - SplitTol).ToList())
                profile.PVIs.Remove(p);
        }

        private static double ElevationClamped(Profile profile, double station)
        {
            if (station < profile.StartingStation) station = profile.StartingStation;
            if (station > profile.EndingStation) station = profile.EndingStation;
            return profile.ElevationAt(station);
        }

        private static BaselineRegion RegionWithMaxEnd(Baseline bl)
        {
            BaselineRegion best = null;
            foreach (BaselineRegion r in bl.BaselineRegions)
                if (best == null || r.EndStation > best.EndStation) best = r;
            return best;
        }

        private static BaselineRegion RegionWithMinStart(Baseline bl)
        {
            BaselineRegion best = null;
            foreach (BaselineRegion r in bl.BaselineRegions)
                if (best == null || r.StartStation < best.StartStation) best = r;
            return best;
        }

        // ======================================================================
        //  СЛУЖЕБНОЕ
        // ======================================================================

        /// <summary>
        /// Стёрли прокси смены отметки или удалили её командой. Коридор остаётся
        /// разрезанным как был — склеивать два профиля и две базовые линии обратно
        /// модуль не умеет. Снимается только управление: маркер и оставшийся прокси.
        /// Концевые границы для обеих частей заведёт RW_EDITMODE или RW_REFRESHBREAKS.
        /// </summary>
        private void ForgetSplit(StationMarker m)
        {
            using (Doc.LockDocument())
            using (_session.Suspend())
            using (var tr = Doc.Database.TransactionManager.StartTransaction())
            {
                BreakOverlay.Unlock(tr, Doc.Database);

                BreakTrace.Step("=== смена отметки " + m.Station.ToString("F3", CultureInfo.InvariantCulture) +
                                ": прокси стёрт, управление снимается (коридор остаётся разрезанным)");

                EraseIfValid(tr, m.ProfileProxyHandle);
                EraseIfValid(tr, m.PlanProxyHandle);
                _session.Store.Remove(m.Id);

                RefreshOverlays(tr,
                    _session.FindLinkByProfile(m.ProfileHandle, m.CorridorHandle),
                    _session.FindLinkByProfile(m.NextProfileHandle, m.CorridorHandle));
                tr.Commit();
            }

            _session.Store.SaveToDatabase(Doc.Database);
            _session.SaveLinks();
        }

        /// <summary>Правка свойств смены отметки из палитры не принимается — палитра возвращается к модели.</summary>
        private void RestoreSplitProps(StationMarker m)
        {
            using (Doc.LockDocument())
            using (_session.Suspend())
            using (var tr = Doc.Database.TransactionManager.StartTransaction())
            {
                WriteProps(tr, m);
                tr.Commit();
            }
        }

        private void RefreshOverlays(Transaction tr, params BreakLink[] links)
        {
            foreach (BreakLink link in links)
            {
                if (link == null) continue;
                try { BreakOverlay.Rebuild(tr, Doc.Database, _session, link); }
                catch (System.Exception) { /* оформление — не механика */ }
            }
        }

        /// <summary>Имя профиля, которого ещё нет у этой оси: при совпадении — « (2)», « (3)»…</summary>
        private static string UniqueProfileName(Transaction tr, Alignment alignment, string wanted)
        {
            var taken = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
            foreach (ObjectId id in alignment.GetProfileIds())
            {
                var p = tr.GetObject(id, OpenMode.ForRead) as Profile;
                if (p != null) taken.Add(p.Name);
            }

            return Unique(taken, wanted);
        }

        private static string UniqueBaselineName(Corridor corridor, string wanted)
        {
            var taken = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
            foreach (Baseline b in corridor.Baselines) taken.Add(b.Name);
            return Unique(taken, wanted);
        }

        private static string Unique(HashSet<string> taken, string wanted)
        {
            if (!taken.Contains(wanted)) return wanted;
            for (int i = 2; ; i++)
            {
                string candidate = wanted + " (" + i + ")";
                if (!taken.Contains(candidate)) return candidate;
            }
        }
    }
}
