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

        /// <summary>
        /// Правый профиль: создаётся двумя вершинами (начало и КОНЕЦ — так же, как
        /// строит плоский профиль мастер связи), потом внутрь добавляются остальные.
        /// Вставка вершин за пределами уже построенного профиля не используется.
        /// </summary>
        private static ObjectId CreateRightProfile(Transaction tr, Database db, CivilDocument civDoc,
                                            Profile source, Alignment alignment, string name,
                                            double start, double startElev,
                                            double end, double endElev,
                                            List<Pt> pvis, double delta, List<string> notes)
        {
            // Стиль профиля прочитать нельзя (свойство только для записи), поэтому
            // берётся тот же, что у мастера. Слой — как у исходного профиля.
            // У Profile свойство StyleId только для записи, а у базового класса
            // Entity оно читается — через него берём стиль исходного профиля.
            // Первый стиль из списка мог оказаться стилем без отображения:
            // профиль при этом есть в модели, но в виде его не видно.
            ObjectId styleId = ObjectId.Null;
            try { styleId = ((Autodesk.Civil.DatabaseServices.Entity)source).StyleId; }
            catch (System.Exception ex) { BreakTrace.Step("  стиль исходного профиля не прочитан: " + ex.Message); }

            if (styleId.IsNull)
            {
                styleId = civDoc.Styles.ProfileStyles[0];
                notes.Add("Стиль исходного профиля прочитать не удалось — у нового взят первый в чертеже; смените его вручную.");
            }

            ObjectId labelSetId = BreakCommands.FindEmptyLabelSet(civDoc, tr);
            ObjectId layerId = source.LayerId;

            try
            {
                BreakTrace.Step("  стиль " + ((Autodesk.Civil.DatabaseServices.Entity)source).StyleName +
                                ", слой " + source.Layer);
            }
            catch (System.Exception) { }

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

            // Профиль строится КАК В МАСТЕРЕ СВЯЗИ: первая вершина в начале оси,
            // вторая — в конце. Первая вершина посреди трассы отвергалась
            // («значение не попадает в ожидаемый диапазон»). Нужное начало
            // получается потом обрезкой — вставкой вершины ВНУТРЬ и снятием
            // всего левее, тем же приёмом, что и у левого профиля.
            // Профиль исходной связи может выходить за ось (ось потом укоротили):
            // в журнале конец профиля 251.315 при конце оси 251.113, и вершина
            // за осью отвергается. Правый профиль ограничивается осью.
            double alStart = alignment.StartingStation;
            double alEnd = alignment.EndingStation;
            BreakTrace.Step(string.Format(CultureInfo.InvariantCulture,
                "ось: {0:F3}..{1:F3}, конец профиля {2:F3}", alStart, alEnd, end));

            if (end > alEnd)
            {
                end = alEnd;
                endElev = source.ElevationAt(Math.Min(end, source.EndingStation)) + delta;
                notes.Add("Профиль выходил за конец оси — правый профиль кончается на конце оси (" +
                          alEnd.ToString("F3", CultureInfo.InvariantCulture) + ").");
            }

            bool needTrim = start - alStart > SplitTol;
            double first = needTrim ? alStart : start;

            BreakTrace.Step("правый профиль: вершина в начале " + first.ToString("F3", CultureInfo.InvariantCulture));
            right.PVIs.AddPVI(first, startElev);

            BreakTrace.Step("правый профиль: вершина в конце " + end.ToString("F3", CultureInfo.InvariantCulture));
            right.PVIs.AddPVI(end, endElev);

            // Остальные вершины прежнего профиля правее пикета — те же, но на delta.
            BreakTrace.Step("правый профиль: внутренние вершины");
            foreach (Pt p in pvis.Where(p => p.Station > start + SplitTol && p.Station < end - SplitTol))
                right.PVIs.AddPVI(p.Station, p.Elevation + delta);

            if (needTrim)
            {
                BreakTrace.Step("правый профиль: обрезка начала на " + start.ToString("F3", CultureInfo.InvariantCulture));
                CutProfileStart(right, start, startElev);
            }

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

                CopyRegionSettings(plan, created, notes);

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
        /// Перенести настройки участка в его продолжение. Старый участок ещё жив
        /// (он стирается или укорачивается позже), поэтому копировать можно с него.
        ///
        /// Основной путь — <c>BaselineRegion.Match(источник, All)</c>: Civil сам копирует
        /// конструкцию, цели и частоты. Если он не принят, цели и частоты копируются
        /// по отдельности. Станции после <c>Match</c> возвращаются на место, а
        /// дополнительные станции переносятся те, что попали в диапазон нового участка.
        /// Всё, что не удалось, попадает в <paramref name="notes"/>, а не теряется молча.
        /// </summary>
        private static void CopyRegionSettings(RegionPlan plan, BaselineRegion created, List<string> notes)
        {
            bool matched = false;

            try
            {
                BreakTrace.Step("  участок «" + plan.Name + "»: Match(All)");
                created.Match(plan.Old, RegionMatchType.All);
                matched = true;
            }
            catch (System.Exception ex)
            {
                BreakTrace.Step("  Match не принят (" + ex.Message + ") — копирую по частям");
            }

            // Match мог тронуть станции — возвращаем.
            try
            {
                if (Math.Abs(created.StartStation - plan.Start) > SplitTol) created.StartStation = plan.Start;
                if (Math.Abs(created.EndStation - plan.End) > SplitTol) created.EndStation = plan.End;
            }
            catch (System.Exception ex)
            {
                BreakTrace.Step("  станции участка после Match не восстановлены: " + ex.Message);
            }

            if (!matched)
            {
                created.AssemblyId = plan.AssemblyId;

                if (plan.Targets != null)
                {
                    try { created.SetTargets(plan.Targets); }
                    catch (System.Exception)
                    {
                        notes.Add("Цели участка «" + plan.Name + "» не перенесены — задайте их заново.");
                    }
                }

                try
                {
                    // Частоты и прочие параметры расстановки — свойство за свойством.
                    var from = plan.Old.AppliedAssemblySetting;
                    var to = created.AppliedAssemblySetting;
                    foreach (var prop in typeof(AppliedAssemblySetting).GetProperties())
                    {
                        if (!prop.CanRead || !prop.CanWrite) continue;
                        try { prop.SetValue(to, prop.GetValue(from, null), null); }
                        catch (System.Exception) { }
                    }
                }
                catch (System.Exception)
                {
                    notes.Add("Частоты участка «" + plan.Name + "» не перенесены — проверьте их.");
                }
            }

            // Дополнительные станции участка.
            try
            {
                foreach (double st in plan.Old.AdditionalStations())
                    if (st > plan.Start + SplitTol && st < plan.End - SplitTol)
                        created.AddStation(st, string.Empty);
            }
            catch (System.Exception)
            {
                notes.Add("Дополнительные станции участка «" + plan.Name + "» не перенесены.");
            }
        }

        /// <summary>
        /// Настройки самой базовой линии (цели и переходы) — в правую базовую линию.
        /// Участки при этом переносятся отдельно, <see cref="TransferRegions"/>.
        /// </summary>
        private static void CopyBaselineSettings(Baseline from, Baseline to, List<string> notes)
        {
            try { to.SetTargets(from.GetTargets()); }
            catch (System.Exception)
            {
                notes.Add("Цели базовой линии не перенесены — проверьте их в правой базовой линии.");
            }

            try
            {
                var transitions = from.getTransitions();
                if (transitions != null && transitions.Count > 0) to.SetTransitions(transitions);
            }
            catch (System.Exception)
            {
                notes.Add("Переходы базовой линии не перенесены — проверьте их.");
            }
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
        //  ТОЛЬКО ПРОФИЛЬ (первый этап RW_BOTTOMSTEP: без коридора и связей)
        // ======================================================================

        /// <summary>
        /// Разделить профиль на «до» и «после» на пикете station: левый кончается
        /// ровно на пикете, правый — новый профиль — начинается на нём же на delta
        /// выше/ниже и дальше повторяет прежний, сдвинутый на delta. Имя нового —
        /// имя прежнего + отметка начала. Коридора, связей и маркеров не касается.
        /// Возвращает ObjectId нового профиля.
        /// </summary>
        public ObjectId SplitProfileOnly(ObjectId profileId, ObjectId viewId,
                                         double station, double delta, List<string> notes,
                                         out string corridorInfo)
        {
            var db = Doc.Database;
            var civDoc = CivilApplication.ActiveDocument;

            try
            {
                return SplitProfileOnlyCore(db, civDoc, profileId, viewId, station, delta, notes,
                                            out corridorInfo);
            }
            catch (System.Exception ex)
            {
                // Исключение должно попасть в журнал с текстом: в командной строке
                // его легко не заметить, а по журналу видно и шаг, и причину.
                BreakTrace.Step("ОШИБКА: " + ex.GetType().Name + ": " + ex.Message);
                throw;
            }
        }

        private ObjectId SplitProfileOnlyCore(Database db, CivilDocument civDoc,
                                              ObjectId profileId, ObjectId viewId,
                                              double station, double delta, List<string> notes,
                                              out string corridorInfo)
        {
            ObjectId resultId;
            Handle leftHandle, rightHandle;
            BreakLink rightLinkOut = null;
            corridorInfo = null;

            using (Doc.LockDocument())
            using (_session.Suspend())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                // Прокси ложатся на слой границ, который вне режима правки заблокирован.
                BreakOverlay.Unlock(tr, db);

                var profile = (Profile)tr.GetObject(profileId, OpenMode.ForWrite);
                var pv = (ProfileView)tr.GetObject(viewId, OpenMode.ForRead);
                var alignment = (Alignment)tr.GetObject(pv.AlignmentId, OpenMode.ForRead);

                BreakTrace.Step(string.Format(CultureInfo.InvariantCulture,
                    "=== РАЗДЕЛИТЬ ПРОФИЛЬ «{0}», пикет {1:F3}, сдвиг {2:F3}, тип {3}, пикеты {4:F3}..{5:F3}, PVI {6}",
                    profile.Name, station, delta, profile.ProfileType,
                    profile.StartingStation, profile.EndingStation, profile.PVIs.Count));

                if (profile.ProfileType != ProfileType.FG)
                    throw new InvalidOperationException(
                        "Профиль «" + profile.Name + "» имеет тип " + profile.ProfileType +
                        ", а делить можно только проектный профиль (FG).");

                const double edge = 0.1;
                if (!(station > profile.StartingStation + edge) || !(station < profile.EndingStation - edge))
                    throw new InvalidOperationException(
                        "Пикет " + station.ToString("F3") + " вне профиля (" +
                        profile.StartingStation.ToString("F3") + ".." + profile.EndingStation.ToString("F3") +
                        ") или ближе " + edge.ToString("0.#") + " м к его краю.");

                if (profile.PVIs.Cast<ProfilePVI>().Any(p => Math.Abs(p.Station - station) < 1e-4))
                    throw new InvalidOperationException(
                        "Ровно на этом пикете в профиле уже стоит вершина — сдвиньте пикет.");

                // --- Связанный коридор: у профиля есть связь (RW_LINKPROFILECORRIDOR) ---
                // Тогда профиль служит базовой линией коридора, и делить надо и её.
                // Всё, что можно проверить заранее, проверяется ДО первой правки.
                BreakLink link = _session.Links.FirstOrDefault(
                                     l => l.ProfileHandle == profile.Handle && l.ProfileViewHandle == pv.Handle)
                                 ?? _session.Links.FirstOrDefault(l => l.ProfileHandle == profile.Handle);

                Corridor corridor = null;
                Baseline leftBaseline = null;

                if (link != null)
                {
                    corridor = RwHandles.Open<Corridor>(tr, db, link.CorridorHandle, OpenMode.ForWrite);
                    leftBaseline = _session.GetBaselineByProfile(tr, corridor, profile.Handle);

                    if (corridor == null || leftBaseline == null)
                    {
                        // Связь есть, а базовой линии на этом профиле уже нет — делим только профиль.
                        notes.Add("Связь профиля есть, но базовой линии на нём не найдено — разделён только профиль.");
                        link = null;
                        corridor = null;
                        leftBaseline = null;
                    }
                    else if (ProfileGeometryOps.FindRegionAt(leftBaseline.BaselineRegions, station, 0.0) == null)
                    {
                        throw new InvalidOperationException(
                            "Пикет " + station.ToString("F3") + " не попадает ни в один участок коридора «" +
                            corridor.Name + "».");
                    }
                    else
                    {
                        BreakTrace.Snapshot(tr, db, _session, link, "КОРИДОР «" + corridor.Name + "»: перед делением");
                    }
                }

                double eLeft = profile.ElevationAt(station);
                double eRight = eLeft + delta;
                double end = profile.EndingStation;

                var pvis = profile.PVIs.Cast<ProfilePVI>()
                                  .OrderBy(p => p.Station)
                                  .Select(p => new Pt(p.Station, p.Elevation))
                                  .ToList();
                double endElev = pvis[pvis.Count - 1].Elevation + delta;

                string name = UniqueProfileName(tr, alignment,
                    profile.Name + " " + eRight.ToString("0.###", CultureInfo.InvariantCulture));

                BreakTrace.Step("новый профиль «" + name + "»");
                ObjectId rightId = CreateRightProfile(
                    tr, db, civDoc, profile, alignment, name,
                    station, eRight, end, endElev, pvis, delta, notes);

                // --- Коридор: правая базовая линия и перенос участков ---
                // До обрезки левого профиля: участки левой базовой линии надо ужать
                // раньше, чем профиль под ними станет короче.
                Baseline rightBaseline = null;
                Guid hostId = Guid.Empty, firstRightId = Guid.Empty;
                Dictionary<Guid, Guid> regionMap = null;

                if (link != null)
                {
                    string blName = UniqueBaselineName(corridor, name);
                    BreakTrace.Step("коридор: базовая линия «" + blName + "»");
                    rightBaseline = corridor.Baselines.Add(blName, alignment.ObjectId, rightId);

                    BreakTrace.Step("коридор: настройки базовой линии");
                    CopyBaselineSettings(leftBaseline, rightBaseline, notes);

                    BreakTrace.Step("коридор: перенос участков");
                    regionMap = TransferRegions(leftBaseline, rightBaseline, station, station, notes,
                                                out hostId, out firstRightId);
                }

                BreakTrace.Step("левый профиль: обрезка конца");
                CutProfileEnd(profile, station, eLeft);

                var right = (Profile)tr.GetObject(rightId, OpenMode.ForWrite);
                BreakCommands.EnsureProfileVisible(tr, db, Doc.Editor, viewId, right, eRight);

                // Диагностика видимости: что именно получилось у нового профиля.
                try
                {
                    BreakTrace.Step(string.Format(CultureInfo.InvariantCulture,
                        "новый профиль: стиль «{0}», слой «{1}», пикеты {2:F3}..{3:F3}, отметки {4:F3}..{5:F3}, PVI {6}; " +
                        "вид: отметки {7:F3}..{8:F3}, режим {9}",
                        ((Autodesk.Civil.DatabaseServices.Entity)right).StyleName, right.Layer,
                        right.StartingStation, right.EndingStation,
                        right.ElevationMin, right.ElevationMax, right.PVIs.Count,
                        pv.ElevationMin, pv.ElevationMax, pv.ElevationRangeMode));
                }
                catch (System.Exception ex) { BreakTrace.Step("диагностика видимости не удалась: " + ex.Message); }

                // --- Маркер и прокси: к ним привязаны концы обоих профилей ---
                // Маркер — обычный StationMarker с ролью Split, поэтому ручки,
                // реактор и перенос работают теми же путями, что у разрывов.
                // Без связанного коридора хэндл коридора пуст, и маркер держит только профили.
                var marker = new StationMarker
                {
                    Role = StationMarker.MarkerRole.Split,
                    Station = station,
                    StepHeight = delta,
                    IsStep = false,
                    Gap = 0.0,
                    BaseElevation = eLeft,
                    ProfileHandle = profile.Handle,
                    NextProfileHandle = right.Handle,
                    ProfileViewHandle = pv.Handle,
                    AlignmentHandle = alignment.Handle
                };

                if (link != null)
                {
                    marker.CorridorHandle = link.CorridorHandle;
                    marker.LeftRegionId = hostId;
                    marker.RightRegionId = firstRightId;

                    // Правая часть — отдельная связь со своим контроллером; маркеры правее
                    // пикета (и конец коридора) переходят в неё вместе с участками.
                    var rightLink = new BreakLink
                    {
                        ProfileHandle     = right.Handle,
                        ProfileViewHandle = link.ProfileViewHandle,
                        AlignmentHandle   = link.AlignmentHandle,
                        CorridorHandle    = link.CorridorHandle
                    };
                    rightLink.ColorIndex = BreakOverlay.RandomColor(rightLink.Id);
                    rightLink.RefreshNames(tr, db);

                    foreach (StationMarker mk in _session.Store.ForProfile(link.ProfileHandle)
                                                         .Where(x => x.Station > station).ToList())
                    {
                        mk.ProfileHandle = right.Handle;
                        mk.BaseElevation += delta;

                        Guid g;
                        if (regionMap.TryGetValue(mk.LeftRegionId, out g)) mk.LeftRegionId = g;
                        if (regionMap.TryGetValue(mk.RightRegionId, out g)) mk.RightRegionId = g;
                    }

                    int slot = _session.Links.Count(l => l.ProfileViewHandle == link.ProfileViewHandle);
                    BreakController.Ensure(tr, db, rightLink, slot);
                    _session.AddLink(rightLink);
                    rightLinkOut = rightLink;

                    corridorInfo = "Коридор «" + corridor.Name + "»: новая базовая линия «" +
                                   rightBaseline.Name + "», связь «" + rightLink.Label + "».";
                }

                BreakTrace.Step("прокси границы");
                BreakProxyFactory.CreateProxies(tr, db, marker, pv, alignment);
                _session.Store.Add(marker);
                WriteProps(tr, marker);

                if (corridor != null)
                {
                    BreakTrace.Step("коридор: Rebuild");
                    corridor.Rebuild();

                    BreakTrace.Step("оформление");
                    RefreshOverlays(tr, link, rightLinkOut);
                }

                leftHandle = profile.Handle;
                rightHandle = right.Handle;
                resultId = rightId;

                BreakTrace.Step("коммит транзакции");
                tr.Commit();
                BreakTrace.Step("коммит прошёл");
            }

            _session.Store.SaveToDatabase(db);
            if (rightLinkOut != null) _session.SaveLinks();

            // Ручки у прокси есть только в режиме правки профиля: включаем его
            // для обоих профилей (после него слой границ разблокирован).
            _session.SetEditMode(leftHandle, true);
            _session.SetEditMode(rightHandle, true);

            return resultId;
        }

        /// <summary>
        /// Обрезать конец профиля на cut: лишние вершины снимаются, последняя
        /// переносится в пикет. Каждый шаг пишется в журнал.
        /// </summary>
        private static void CutProfileEnd(Profile p, double cut, double elev)
        {
            // Первую и последнюю вершину Civil снять не даёт («Cound not remove the
            // first or the last PVI»), поэтому крайняя ПЕРЕНОСИТСЯ в пикет. Лишние
            // снимаются с конца к началу: после удаления индексы вершин правее
            // сдвигаются, и прямой порядок рано или поздно попадал на последнюю.
            var tail = p.PVIs.Cast<ProfilePVI>()
                        .Where(x => x.Station > cut + SplitTol)
                        .OrderBy(x => x.Station).ToList();
            if (tail.Count == 0) throw new InvalidOperationException("Правее пикета нет вершин профиля.");

            BreakTrace.Step("  конец: внутренних вершин правее пикета " + (tail.Count - 1));
            for (int i = tail.Count - 2; i >= 0; i--)
            {
                BreakTrace.Step("  конец: снимаю вершину " + tail[i].Station.ToString("F3", CultureInfo.InvariantCulture));
                p.PVIs.Remove(tail[i]);
            }

            // Объект последней вершины, взятый ДО удалений, мог устареть вместе
            // с индексами — берём её заново из текущего списка.
            BreakTrace.Step("  конец: сдвигаю последнюю вершину в пикет");
            ProfilePVI last = p.PVIs.Cast<ProfilePVI>().OrderBy(x => x.Station).Last();
            BreakTrace.Step("  конец: последняя вершина сейчас на " +
                            last.Station.ToString("F3", CultureInfo.InvariantCulture));
            last.Elevation = elev;
            BreakTrace.Step("  конец: отметка задана");
            last.Station = cut;
            BreakTrace.Step("  конец: станция задана");
            BreakTrace.Step("  конец: готово");
        }

        /// <summary>Зеркально <see cref="CutProfileEnd"/>: обрезать начало профиля на cut.</summary>
        private static void CutProfileStart(Profile p, double cut, double elev)
        {
            var head = p.PVIs.Cast<ProfilePVI>()
                        .Where(x => x.Station < cut - SplitTol)
                        .OrderBy(x => x.Station).ToList();
            if (head.Count == 0) throw new InvalidOperationException("Левее пикета нет вершин профиля.");

            // Все, кроме первой, — с конца к началу (индексы нижних не плывут).
            BreakTrace.Step("  начало: внутренних вершин левее пикета " + (head.Count - 1));
            for (int i = head.Count - 1; i >= 1; i--)
            {
                BreakTrace.Step("  начало: снимаю вершину " + head[i].Station.ToString("F3", CultureInfo.InvariantCulture));
                p.PVIs.Remove(head[i]);
            }

            BreakTrace.Step("  начало: сдвигаю первую вершину в пикет");
            ProfilePVI first = p.PVIs.Cast<ProfilePVI>().OrderBy(x => x.Station).First();
            first.Station = cut;
            first.Elevation = elev;
            BreakTrace.Step("  начало: готово");
        }

        // ======================================================================
        //  ИЗМЕНЕНИЕ ВЫСОТЫ И УДАЛЕНИЕ
        // ======================================================================

        /// <summary>
        /// Цепочка профилей правее смены отметки: правый профиль и все, что идёт
        /// за ним через следующие смены. Сдвиг высоты первой смены сдвигает их все
        /// разом — иначе ступени следующих смен изменились бы.
        /// </summary>
        private List<Handle> ChainRightOf(StationMarker m)
        {
            var chain = new List<Handle>();
            var seen = new HashSet<long>();

            Handle cur = m.NextProfileHandle;
            while (cur.Value != 0 && seen.Add(cur.Value))
            {
                chain.Add(cur);
                StationMarker next = _session.Store.SplitEndingProfile(cur);
                cur = next != null ? next.NextProfileHandle : new Handle(0);
            }

            return chain;
        }

        /// <summary>
        /// Изменить высоту смены отметки: правый профиль и всё, что правее него по
        /// цепочке, поднимается или опускается на разницу. Форма профилей и ступени
        /// следующих смен сохраняются. Имена профилей и базовых линий, оканчивающиеся
        /// прежней отметкой, переименовываются на новую.
        /// </summary>
        public void ChangeSplitHeight(StationMarker m, double newDelta, List<string> notes)
        {
            if (m == null || !m.IsSplit) throw new InvalidOperationException("Это не смена отметки.");
            if (double.IsNaN(newDelta) || double.IsInfinity(newDelta) || Math.Abs(newDelta) < 1e-9)
                throw new InvalidOperationException("Высота смены отметки не может быть нулевой.");

            double d = newDelta - m.StepHeight;
            if (Math.Abs(d) < 1e-9) return;

            var db = Doc.Database;
            double oldDelta = m.StepHeight;

            try
            {
                using (Doc.LockDocument())
                using (_session.Suspend())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    BreakOverlay.Unlock(tr, db);

                    var pv = RwHandles.Open<ProfileView>(tr, db, m.ProfileViewHandle, OpenMode.ForRead);
                    var alignment = pv != null
                        ? tr.GetObject(pv.AlignmentId, OpenMode.ForRead) as Alignment
                        : null;
                    var corridor = RwHandles.Open<Corridor>(tr, db, m.CorridorHandle, OpenMode.ForWrite);

                    List<Handle> chain = ChainRightOf(m);
                    BreakTrace.Step(string.Format(CultureInfo.InvariantCulture,
                        "=== ВЫСОТА смены отметки на {0:F3}: {1:F3} -> {2:F3}, профилей в цепочке {3}",
                        m.Station, oldDelta, newDelta, chain.Count));

                    var touchedLinks = new List<BreakLink>();
                    BreakLink leftLink = _session.FindLinkByProfile(m.ProfileHandle, m.CorridorHandle);
                    if (leftLink != null) touchedLinks.Add(leftLink);

                    Profile firstRight = null;
                    double firstRightElev = 0.0;

                    foreach (Handle h in chain)
                    {
                        var p = RwHandles.Open<Profile>(tr, db, h, OpenMode.ForWrite);
                        if (p == null) continue;

                        double oldStartElev = p.PVIs.Cast<ProfilePVI>()
                                               .OrderBy(x => x.Station).First().Elevation;

                        BreakTrace.Step("  профиль «" + p.Name + "»: сдвиг на " +
                                        d.ToString("F3", CultureInfo.InvariantCulture));
                        foreach (ProfilePVI pvi in p.PVIs.Cast<ProfilePVI>().ToList())
                            pvi.Elevation += d;

                        if (firstRight == null)
                        {
                            firstRight = p;
                            firstRightElev = oldStartElev + d;
                        }

                        // Имя и базовая линия, названная так же, — на новую отметку.
                        Baseline bl = _session.GetBaselineByProfile(tr, corridor, h);
                        if (alignment != null)
                            RenameForElevation(tr, alignment, corridor, p, bl, oldStartElev, oldStartElev + d);

                        BreakLink link = _session.FindLinkByProfile(h, m.CorridorHandle);
                        if (link != null)
                        {
                            link.RefreshNames(tr, db);
                            BreakController.Update(tr, db, link);
                            touchedLinks.Add(link);
                        }
                    }

                    // Маркеры цепочки стоят на сдвинутых профилях.
                    var inChain = new HashSet<long>(chain.Select(c => c.Value));
                    foreach (StationMarker mk in _session.Store.All)
                        if (inChain.Contains(mk.ProfileHandle.Value) && mk.Id != m.Id)
                            mk.BaseElevation += d;

                    m.StepHeight = newDelta;
                    WriteProps(tr, m);

                    if (corridor != null)
                    {
                        BreakTrace.Step("коридор: Rebuild");
                        corridor.Rebuild();
                    }

                    if (firstRight != null && pv != null)
                        BreakCommands.EnsureProfileVisible(tr, db, Doc.Editor, pv.ObjectId, firstRight, firstRightElev);

                    RefreshOverlays(tr, touchedLinks.ToArray());

                    BreakTrace.Step("коммит транзакции");
                    tr.Commit();
                    BreakTrace.Step("коммит прошёл");
                }
            }
            catch (System.Exception ex)
            {
                BreakTrace.Step("ОШИБКА: " + ex.GetType().Name + ": " + ex.Message);
                throw;
            }

            _session.Store.SaveToDatabase(db);
            _session.SaveLinks();
        }

        /// <summary>
        /// Имя профиля вида «… 36.5» — на новую отметку. Если имя не оканчивается
        /// прежней отметкой (пользователь переименовал сам) — не трогаем.
        /// Базовая линия переименовывается, только если называлась так же, как профиль.
        /// </summary>
        private static void RenameForElevation(Transaction tr, Alignment alignment, Corridor corridor,
                                               Profile p, Baseline bl, double oldElev, double newElev)
        {
            string suffix = " " + oldElev.ToString("0.###", CultureInfo.InvariantCulture);
            if (!p.Name.EndsWith(suffix, StringComparison.Ordinal)) return;

            string oldName = p.Name;
            string wanted = oldName.Substring(0, oldName.Length - suffix.Length) +
                            " " + newElev.ToString("0.###", CultureInfo.InvariantCulture);

            p.Name = UniqueProfileName(tr, alignment, wanted);

            if (bl != null && corridor != null && string.Equals(bl.Name, oldName, StringComparison.Ordinal))
                bl.Name = UniqueBaselineName(corridor, p.Name);
        }

        /// <summary>
        /// Удалить смену отметки: два профиля снова становятся одним, а коридор —
        /// одной базовой линией. Обратная к созданию:
        ///
        ///  • левый профиль продлевается до конца правого, формой правого, сдвинутого
        ///    назад на высоту смены; правый профиль стирается;
        ///  • участки правой базовой линии возвращаются в левую: первый из них, стоявший
        ///    у разреза, поглощается разрезанным (выживают настройки левого участка,
        ///    как при удалении обычного разрыва), остальные создаются заново с настройками
        ///    прежних; правая базовая линия удаляется;
        ///  • маркеры правого профиля переходят в левый, правая связь стирается вместе
        ///    с контроллером и оформлением, прокси смены стираются.
        /// </summary>
        public void DeleteSplit(StationMarker m, List<string> notes)
        {
            if (m == null || !m.IsSplit) throw new InvalidOperationException("Это не смена отметки.");

            var db = Doc.Database;
            Handle leftHandle = m.ProfileHandle, rightHandle = m.NextProfileHandle;

            // Режим правки у правого профиля снимается ДО его стирания: запись
            // флага открывает сам профиль.
            if (_session.IsEditMode(rightHandle)) _session.SetEditMode(rightHandle, false);

            BreakLink rightLink = _session.FindLinkByProfile(rightHandle, m.CorridorHandle);

            try
            {
                using (Doc.LockDocument())
                using (_session.Suspend())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    BreakOverlay.Unlock(tr, db);

                    var left = RwHandles.Open<Profile>(tr, db, leftHandle, OpenMode.ForWrite);
                    var right = RwHandles.Open<Profile>(tr, db, rightHandle, OpenMode.ForWrite);
                    var corridor = RwHandles.Open<Corridor>(tr, db, m.CorridorHandle, OpenMode.ForWrite);

                    BreakTrace.Step(string.Format(CultureInfo.InvariantCulture,
                        "=== УДАЛИТЬ смену отметки на {0:F3}, сдвиг {1:F3}", m.Station, m.StepHeight));

                    if (left == null || right == null)
                    {
                        // Одного из профилей уже нет: склеивать нечего — снимается только управление.
                        BreakTrace.Step("профиль слева или справа не открылся — снимаю только управление");
                        notes.Add("Один из профилей уже отсутствует: удалены только прокси и маркер.");
                    }
                    else
                    {
                        double delta = m.StepHeight;

                        // --- 1) Профиль: левый продлевается формой правого, сдвинутого назад ---
                        var shape = right.PVIs.Cast<ProfilePVI>()
                                         .Where(p => p.Station > m.Station + SplitTol)
                                         .OrderBy(p => p.Station)
                                         .Select(p => new Pt(p.Station, p.Elevation - delta))
                                         .ToList();
                        if (shape.Count == 0)
                            throw new InvalidOperationException("У правого профиля нет вершин правее пикета.");

                        Pt last = shape[shape.Count - 1];
                        shape.RemoveAt(shape.Count - 1);

                        BreakTrace.Step("профиль: левый продлевается до " +
                                        last.Station.ToString("F3", CultureInfo.InvariantCulture));
                        ExtendProfileEnd(left, last.Station, last.Elevation, shape, 0.0);

                        // --- 2) Коридор: участки обратно в левую базовую линию ---
                        var map = new Dictionary<Guid, Guid>();
                        if (corridor != null)
                        {
                            Baseline bl = _session.GetBaselineByProfile(tr, corridor, leftHandle);
                            Baseline br = _session.GetBaselineByProfile(tr, corridor, rightHandle);

                            if (bl != null && br != null)
                            {
                                BreakTrace.Step("коридор: участки обратно в левую базовую линию");
                                MergeRegionsBack(bl, br, m, map, notes);

                                BreakTrace.Step("коридор: удаление правой базовой линии");
                                corridor.Baselines.Remove(br);
                            }
                            else notes.Add("Не найдена базовая линия одной из частей — участки коридора не склеивались.");
                        }

                        // --- 3) Маркеры правого профиля — в левый ---
                        foreach (StationMarker mk in _session.Store.ForProfile(rightHandle).ToList())
                        {
                            if (mk.Id == m.Id) continue;

                            mk.ProfileHandle = leftHandle;
                            mk.BaseElevation -= delta;

                            Guid g;
                            if (map.TryGetValue(mk.LeftRegionId, out g)) mk.LeftRegionId = g;
                            if (map.TryGetValue(mk.RightRegionId, out g)) mk.RightRegionId = g;
                        }

                        // --- 4) Правая связь и правый профиль ---
                        if (rightLink != null)
                        {
                            BreakOverlay.Erase(tr, db, rightLink);
                            BreakController.Erase(tr, db, rightLink);
                            _session.RemoveLink(rightLink);
                        }

                        BreakTrace.Step("профиль: удаление правого");
                        try { right.Erase(); }
                        catch (System.Exception ex)
                        {
                            BreakTrace.Step("  правый профиль не стёрт: " + ex.Message);
                            notes.Add("Правый профиль стереть не удалось — удалите его вручную.");
                        }
                    }

                    // --- 5) Сам маркер и прокси ---
                    EraseIfValid(tr, m.ProfileProxyHandle);
                    EraseIfValid(tr, m.PlanProxyHandle);
                    _session.Store.Remove(m.Id);

                    if (corridor != null)
                    {
                        BreakTrace.Step("коридор: Rebuild");
                        corridor.Rebuild();
                    }

                    RefreshOverlays(tr, _session.FindLinkByProfile(leftHandle, m.CorridorHandle));

                    BreakTrace.Step("коммит транзакции");
                    tr.Commit();
                    BreakTrace.Step("коммит прошёл");
                }
            }
            catch (System.Exception ex)
            {
                BreakTrace.Step("ОШИБКА: " + ex.GetType().Name + ": " + ex.Message);
                // Связь выводилась из сессии внутри транзакции, а та откатилась.
                if (rightLink != null) _session.AddLink(rightLink);
                throw;
            }

            _session.Store.SaveToDatabase(db);
            _session.SaveLinks();
        }

        /// <summary>
        /// Вернуть участки правой базовой линии в левую. Первый (у разреза) поглощается
        /// разрезанным участком слева — тот просто растягивается; остальные создаются
        /// заново, настройки копируются с прежних.
        /// </summary>
        private static void MergeRegionsBack(Baseline left, Baseline right, StationMarker m,
                                             Dictionary<Guid, Guid> map, List<string> notes)
        {
            var rights = new List<BaselineRegion>();
            foreach (BaselineRegion r in right.BaselineRegions) rights.Add(r);
            rights = rights.OrderBy(r => r.StartStation).ToList();
            if (rights.Count == 0) return;

            BaselineRegion host = ProfileGeometryOps.FindByGuid(left.BaselineRegions, m.LeftRegionId)
                                  ?? RegionWithMaxEnd(left);
            BaselineRegion first = rights[0];

            int from = 0;
            if (host != null)
            {
                // Левый профиль уже продлён — участок можно растянуть.
                host.EndStation = first.EndStation;
                map[first.RegionGUID] = host.RegionGUID;
                from = 1;
            }

            for (int i = from; i < rights.Count; i++)
            {
                BaselineRegion r = rights[i];
                var plan = new RegionPlan
                {
                    Old = r,
                    OldId = r.RegionGUID,
                    Name = r.Name,
                    AssemblyId = r.AssemblyId,
                    Start = r.StartStation,
                    End = r.EndStation
                };

                try { plan.Targets = r.GetTargets(); }
                catch (System.Exception) { }

                BaselineRegion created = left.BaselineRegions.Add(
                    plan.Name, plan.AssemblyId, plan.Start, plan.End);

                CopyRegionSettings(plan, created, notes);
                map[plan.OldId] = created.RegionGUID;
            }
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
                    // Коридор необязателен: на первом этапе смена отметки делит только
                    // профили, и тогда участков и базовых линий нет.
                    var corridor = RwHandles.Open<Corridor>(tr, db, m.CorridorHandle, OpenMode.ForWrite);

                    if (left == null || right == null)
                        throw new InvalidOperationException(
                            "У смены отметки не открылся один из профилей — она осталась на прежнем месте.");

                    Baseline bl = _session.GetBaselineByProfile(tr, corridor, m.ProfileHandle);
                    Baseline br = _session.GetBaselineByProfile(tr, corridor, m.NextProfileHandle);

                    var bounds = _session.Store.GetMoveBounds(
                        m, BreakGripOverrule.Buffer, left.StartingStation, right.EndingStation);
                    double s = Math.Min(Math.Max(newStation, bounds.min), bounds.max);

                    if (Math.Abs(s - old) > 1e-6)
                    {
                        m.Station = s;

                        BreakTrace.Snapshot(tr, db, _session, m,
                            string.Format(CultureInfo.InvariantCulture,
                                "ПЕРЕНОС смены отметки {0:F3} -> {1:F3}", old, s));

                        MoveSplit(m, old, s, left, right, bl, br);

                        if (corridor != null)
                        {
                            BreakTrace.Step("коридор: Rebuild");
                            corridor.Rebuild();
                        }
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

            // Участки есть, только если смена отметки связана с коридором.
            BaselineRegion lr = null, rr = null;
            if (bl != null && br != null)
            {
                lr = ProfileGeometryOps.FindByGuid(bl.BaselineRegions, m.LeftRegionId)
                     ?? RegionWithMaxEnd(bl);
                rr = ProfileGeometryOps.FindByGuid(br.BaselineRegions, m.RightRegionId)
                     ?? RegionWithMinStart(br);
            }

            // Профили правятся только тем, что Civil принимает: перенос КРАЙНЕЙ
            // вершины, снятие внутренних (с конца к началу) и вставка внутрь.
            if (s > old)
            {
                // Левый удлиняется, правый укорачивается.
                BreakTrace.Step("профиль: левый удлиняется до " + lNew.ToString("F3", CultureInfo.InvariantCulture));
                ExtendProfileEnd(left, lNew, fL, shape.Where(p => p.Station > lOld + SplitTol && p.Station < lNew - SplitTol), 0.0);
                if (lr != null) lr.EndStation = lNew;

                BreakTrace.Step("профиль: правый укорачивается до " + rNew.ToString("F3", CultureInfo.InvariantCulture));
                if (rr != null) rr.StartStation = rNew;
                CutProfileStart(right, rNew, fR + delta);
            }
            else
            {
                // Левый укорачивается, правый удлиняется.
                BreakTrace.Step("профиль: левый укорачивается до " + lNew.ToString("F3", CultureInfo.InvariantCulture));
                if (lr != null) lr.EndStation = lNew;
                CutProfileEnd(left, lNew, fL);

                BreakTrace.Step("профиль: правый удлиняется до " + rNew.ToString("F3", CultureInfo.InvariantCulture));
                ExtendProfileStart(right, rNew, fR + delta,
                                   shape.Where(p => p.Station > rNew + SplitTol && p.Station < rOld - SplitTol), delta);
                if (rr != null) rr.StartStation = rNew;
            }

            if (lr != null) m.LeftRegionId = lr.RegionGUID;
            if (rr != null) m.RightRegionId = rr.RegionGUID;
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
