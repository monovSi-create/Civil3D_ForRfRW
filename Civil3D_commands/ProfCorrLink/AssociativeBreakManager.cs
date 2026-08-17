using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.DatabaseServices;
using Civil3D_commands.Shared;

namespace Civil3D_commands.AssociativeBreaks
{
    /// <summary>
    /// Оркестратор. ВСЕ изменения (грипса, палитра, команды) проходят только через него,
    /// под флагом BreakSession.Suspend(), чтобы реактор не реагировал на собственные правки.
    /// Порядок: правка модели -> профиль (если ступень) -> области -> прокси -> один Rebuild.
    /// </summary>
    public class AssociativeBreakManager
    {
        private readonly BreakSession _session;
        public AssociativeBreakManager(BreakSession session) { _session = session; }

        private static Document Doc =>
            Application.DocumentManager.MdiActiveDocument;

        /// <summary>Создать новый разрыв на профиле.</summary>
        public Guid CreateBreak(StationMarker template)
        {
            using (Doc.LockDocument())            // 2024: правки из Idle/реактора требуют блокировки
            using (_session.Suspend())
            using (var tr = Doc.Database.TransactionManager.StartTransaction())
            {
                // Слой границ заблокирован вне режима редактирования, а прокси
                // лежат на нём: без этого создание и правка линий бросают
                // eOnLockedLayer. Состояние слоёв возвращает RefreshOverlay.
                BreakOverlay.Unlock(tr, Doc.Database);

                var profile = (Profile)tr.GetObject(Resolve(template.ProfileHandle), OpenMode.ForWrite);
                var pv = (ProfileView)tr.GetObject(Resolve(template.ProfileViewHandle), OpenMode.ForRead);
                var alignment = (Alignment)tr.GetObject(Resolve(template.AlignmentHandle), OpenMode.ForRead);

                // Кламп ещё на этапе создания.
                Baseline bl = _session.GetBaseline(tr, template);
                ClampToNeighbors(template, bl);

                template.BaseElevation =
                    ProfileGeometryOps.BaseElevationAt(profile, template.Station, template.HalfGap);

                if (template.IsStep)
                    ProfileGeometryOps.InsertStep(
                        profile, template.Station, template.StepHeight, template.HalfGap);

                BreakProxyFactory.CreateProxies(tr, Doc.Database, template, pv, alignment);
                _session.Store.Add(template);

                // Режет ровно одну область — ту, внутрь которой попал разрыв.
                if (bl != null) ProfileGeometryOps.ApplyBreak(bl, template, template.Station);
                WriteProps(tr, template);
                RebuildCorridor(tr, template);
                RefreshOverlay(tr, template);

                tr.Commit();
            }
            _session.Store.SaveToDatabase(Doc.Database);
            _session.SaveLinks();
            return template.Id;
        }

        /// <summary>
        /// Завести концевые границы связи, если их ещё нет.
        ///
        /// Начало и конец коридора двигаются так же, как разрывы: те же прокси,
        /// те же ручки, тот же реактор. Разница только в том, что они не пара
        /// областей, а единственный край крайней, и ступенью быть не могут.
        ///
        /// Поэтому они — обычные <see cref="StationMarker"/> с ролью, а не особый
        /// вид объектов: заведи их отдельным классом, и пришлось бы дублировать
        /// и оверрул ручек, и реактор, и кламп по соседям.
        ///
        /// Идемпотентна: зовётся из мастера, из RW_EDITLINKS и из RW_REFRESHBREAKS,
        /// потому что чертежи прежних версий концевых границ не содержат.
        /// </summary>
        public int EnsureTerminals(BreakLink link)
        {
            if (link == null) return 0;

            int created = 0;

            using (Doc.LockDocument())
            using (_session.Suspend())
            using (var tr = Doc.Database.TransactionManager.StartTransaction())
            {
                var db = Doc.Database;
                BreakOverlay.Unlock(tr, db);

                Baseline bl = _session.GetBaseline(tr, link);
                if (bl == null) { tr.Commit(); return 0; }

                var pv = RwHandles.Open<ProfileView>(tr, db, link.ProfileViewHandle, OpenMode.ForRead);
                var alignment = RwHandles.Open<Alignment>(tr, db, link.AlignmentHandle, OpenMode.ForRead);
                var profile = RwHandles.Open<Profile>(tr, db, link.ProfileHandle, OpenMode.ForRead);

                double start, end;
                RegionSpan(bl, out start, out end);

                if (EnsureTerminal(tr, db, link, StationMarker.MarkerRole.Start, start,
                                   pv, alignment, profile)) created++;
                if (EnsureTerminal(tr, db, link, StationMarker.MarkerRole.End, end,
                                   pv, alignment, profile)) created++;

                BreakOverlay.ApplyState(tr, db, _session.AnyEditMode);
                tr.Commit();
            }

            if (created > 0) _session.Store.SaveToDatabase(Doc.Database);
            return created;
        }

        private bool EnsureTerminal(Transaction tr, Database db, BreakLink link,
                                    StationMarker.MarkerRole role, double station,
                                    ProfileView pv, Alignment alignment, Profile profile)
        {
            foreach (StationMarker existing in _session.Store.ForProfile(link.ProfileHandle))
                if (existing.Role == role) return false;

            StationMarker m = link.NewMarker();
            m.Role = role;
            m.Station = station;
            m.IsStep = false;
            m.StepHeight = 0.0;

            // Полузазора у конца нет — отметка берётся ровно на его пикете.
            if (profile != null)
                m.BaseElevation = ProfileGeometryOps.BaseElevationAt(profile, station, 0.0);

            BreakProxyFactory.CreateProxies(tr, db, m, pv, alignment);
            _session.Store.Add(m);
            WriteProps(tr, m);
            return true;
        }

        /// <summary>
        /// Пикеты, которые занимают области коридора. Не то же, что пределы
        /// базовой линии: области могут не покрывать её целиком, а концевая
        /// граница управляет именно краем области.
        /// </summary>
        private static void RegionSpan(Baseline bl, out double start, out double end)
        {
            start = bl.StartStation;
            end = bl.EndStation;

            bool first = true;
            foreach (BaselineRegion r in bl.BaselineRegions)
            {
                if (first)
                {
                    start = r.StartStation;
                    end = r.EndStation;
                    first = false;
                    continue;
                }

                if (r.StartStation < start) start = r.StartStation;
                if (r.EndStation > end) end = r.EndStation;
            }
        }

        /// <summary>Удалить разрыв.</summary>
        public void DeleteBreak(Guid id)
        {
            var m = _session.Store.Get(id);
            if (m == null) return;

            using (Doc.LockDocument())
            using (_session.Suspend())
            using (var tr = Doc.Database.TransactionManager.StartTransaction())
            {
                BreakOverlay.Unlock(tr, Doc.Database);

                var profile = (Profile)tr.GetObject(Resolve(m.ProfileHandle), OpenMode.ForWrite);

                if (m.IsStep)
                    ProfileGeometryOps.RemoveStep(profile, m.Station, m.StepHeight, m.HalfGap);

                EraseIfValid(tr, m.ProfileProxyHandle);
                EraseIfValid(tr, m.PlanProxyHandle);

                // Правая область поглощается левой; соседний справа маркер ссылался
                // на исчезнувшую — переводим его на выжившую.
                //
                // У концевой границы поглощать нечего: она не разделяет пару
                // областей, а держит внешний край крайней. Стирается только сам
                // маркер с прокси — край коридора остаётся там, где стоял,
                // а RW_EDITLINKS заведёт границу заново.
                Baseline bl = _session.GetBaseline(tr, m);
                if (!m.IsTerminal && bl != null &&
                    ProfileGeometryOps.RemoveBreak(bl, m, out Guid removed, out Guid surviving))
                    RebindNeighbours(removed, surviving);

                _session.Store.Remove(id);

                RebuildCorridor(tr, m);
                RefreshOverlay(tr, m);
                tr.Commit();
            }
            _session.Store.SaveToDatabase(Doc.Database);
            _session.SaveLinks();
        }

        /// <summary>
        /// Применить новый пикет к маркеру (вызывается реактором после грипса-перемещения
        /// или при правке поля "Пикет" в палитре). Делает кламп, двигает ступень и области.
        /// </summary>
        public void ApplyStationChange(Guid id, double newStation)
        {
            var m = _session.Store.Get(id);
            if (m == null) return;

            using (Doc.LockDocument())
            using (_session.Suspend())
            using (var tr = Doc.Database.TransactionManager.StartTransaction())
            {
                BreakOverlay.Unlock(tr, Doc.Database);

                var profile = (Profile)tr.GetObject(Resolve(m.ProfileHandle), OpenMode.ForWrite);
                var pv = (ProfileView)tr.GetObject(Resolve(m.ProfileViewHandle), OpenMode.ForRead);
                var alignment = (Alignment)tr.GetObject(Resolve(m.AlignmentHandle), OpenMode.ForRead);

                Baseline bl = _session.GetBaseline(tr, m);

                double old = m.Station;
                m.Station = newStation;
                ClampToNeighbors(m, bl);

                if (Math.Abs(m.Station - old) > 1e-6)
                {
                    if (m.IsStep)
                    {
                        ProfileGeometryOps.MoveStep(profile, old, m.Station, m.StepHeight, m.HalfGap);
                        m.BaseElevation =
                            ProfileGeometryOps.BaseElevationAt(profile, m.Station, m.HalfGap);
                    }

                    if (bl != null) ProfileGeometryOps.ApplyBreak(bl, m, old);
                    RebuildCorridor(tr, m);
                }

                // Прокси пересчитываем всегда, в том числе когда кламп съел всё
                // перемещение: пикет не изменился, но линию пользователь уже утащил,
                // и без этого она осталась бы стоять мимо модели.
                BreakProxyFactory.UpdateProxyGeometry(tr, m, pv, alignment);
                WriteProps(tr, m);
                RefreshOverlay(tr, m);
                tr.Commit();
            }
            _session.Store.SaveToDatabase(Doc.Database);
            _session.SaveLinks();
        }

        /// <summary>
        /// Изменить свойства разрыва: ступень, её высоту и микроразрыв.
        /// Пикет при этом не меняется — для него есть ApplyStationChange.
        ///
        /// Порядок важен: старая ступень снимается по СТАРЫМ значениям (пара PVI
        /// стоит на прежнем полузазоре и по новому не найдётся), и только потом
        /// в маркер пишутся новые.
        /// </summary>
        public void ApplyProperties(Guid id, bool isStep, double stepHeight, double gap)
        {
            ApplyProperties(id, isStep, stepHeight, gap, ObjectId.Null, ObjectId.Null);
        }

        /// <summary>
        /// То же плюс конструкции участков слева и справа от разрыва
        /// (по ходу пикетажа: предыдущий и следующий).
        /// ObjectId.Null означает «не менять».
        /// </summary>
        public void ApplyProperties(Guid id, bool isStep, double stepHeight, double gap,
                                    ObjectId leftAssembly, ObjectId rightAssembly)
        {
            var m = _session.Store.Get(id);
            if (m == null) return;

            using (Doc.LockDocument())
            using (_session.Suspend())
            using (var tr = Doc.Database.TransactionManager.StartTransaction())
            {
                var db = Doc.Database;
                BreakOverlay.Unlock(tr, db);

                var profile = RwHandles.Open<Profile>(tr, db, m.ProfileHandle, OpenMode.ForWrite);
                var pv = RwHandles.Open<ProfileView>(tr, db, m.ProfileViewHandle, OpenMode.ForRead);
                var alignment = RwHandles.Open<Alignment>(tr, db, m.AlignmentHandle, OpenMode.ForRead);

                if (m.IsStep && profile != null)
                    ProfileGeometryOps.RemoveStep(profile, m.Station, m.StepHeight, m.HalfGap);

                // Ступень у концевой границы невозможна: поднимать «весь профиль
                // правее» за пределами коридора не над чем. Гасим здесь, а не
                // только в командах: сюда же приходит и правка из палитры свойств.
                m.IsStep = isStep && !m.IsTerminal;
                m.StepHeight = m.IsStep ? stepHeight : 0.0;
                m.Gap = ProfileGeometryOps.SanitizeGap(gap);

                if (profile != null)
                {
                    if (m.IsStep)
                        ProfileGeometryOps.InsertStep(profile, m.Station, m.StepHeight, m.HalfGap);

                    m.BaseElevation =
                        ProfileGeometryOps.BaseElevationAt(profile, m.Station, m.HalfGap);
                }

                // Зазор мог измениться — границы областей переставляются под него.
                Baseline bl = _session.GetBaseline(tr, m);
                if (bl != null)
                {
                    ProfileGeometryOps.ApplyBreak(bl, m, m.Station);
                    ProfileGeometryOps.SetAssemblies(bl, m, leftAssembly, rightAssembly);
                }

                BreakProxyFactory.UpdateProxyGeometry(tr, m, pv, alignment);
                WriteProps(tr, m);
                RebuildCorridor(tr, m);
                RefreshOverlay(tr, m);

                tr.Commit();
            }
            _session.Store.SaveToDatabase(Doc.Database);
            _session.SaveLinks();
        }

        // ----------------------------------------------------------------------

        /// <summary>
        /// Перестроить оформление связи, к которой относится разрыв, и вернуть
        /// слоям состояние, положенное текущему режиму.
        ///
        /// Заливки участков строятся по фактическим областям коридора, а области
        /// только что переставлены — значит, картинка устарела ровно сейчас.
        /// Ошибки гасятся: оформление не механика, и падать из-за него нельзя.
        /// </summary>
        private void RefreshOverlay(Transaction tr, IBreakTarget target)
        {
            try
            {
                BreakLink link = _session.LinkFor(target);

                if (link != null)
                    BreakOverlay.Rebuild(tr, Doc.Database, _session, link);
                else
                    BreakOverlay.ApplyState(tr, Doc.Database, _session.AnyEditMode);
            }
            catch (System.Exception) { }
        }

        /// <summary>
        /// Обновить набор характеристик на обоих прокси. Вызывается после КАЖДОЙ
        /// правки: прежде набор писался только при создании и после первого же
        /// перетаскивания показывал неправду.
        /// </summary>
        private void WriteProps(Transaction tr, StationMarker m)
        {
            try
            {
                ObjectId psd = PropertySetSupport.EnsureMarkerPsd(Doc.Database);
                if (psd.IsNull) return;

                foreach (Handle h in new[] { m.ProfileProxyHandle, m.PlanProxyHandle })
                {
                    ObjectId id = Resolve(h);
                    if (id.IsNull) continue;

                    PropertySetSupport.Attach(tr, id, psd);
                    PropertySetSupport.WriteMarkerProps(tr, id, m);
                }
            }
            catch (System.Exception)
            {
                // Набор характеристик — удобство, а не механика: если он
                // не применился к Line, разрывы обязаны работать по-прежнему.
            }
        }

        private void ClampToNeighbors(StationMarker m, Baseline bl)
        {
            double bStart = bl?.StartStation ?? 0;
            double bEnd = bl?.EndStation ?? m.Station;
            var (min, max) = _session.Store.GetMoveBounds(m, BreakGripOverrule.Buffer, bStart, bEnd);
            m.Station = Math.Min(Math.Max(m.Station, min), max);
        }

        private void RebindNeighbours(Guid removed, Guid surviving)
        {
            if (removed == Guid.Empty) return;
            foreach (var other in _session.Store.All)
            {
                if (other.LeftRegionId == removed) other.LeftRegionId = surviving;
                if (other.RightRegionId == removed) other.RightRegionId = surviving;
            }
        }

        private void RebuildCorridor(Transaction tr, StationMarker m)
        {
            var corridor = _session.GetCorridor(tr, m);
            corridor?.Rebuild(); // один Rebuild на всю операцию
        }

        private static ObjectId Resolve(Handle h)
        {
            return RwHandles.Resolve(Doc.Database, h);
        }

        private static void EraseIfValid(Transaction tr, Handle h)
        {
            ObjectId id = Resolve(h);
            if (!id.IsNull && !id.IsErased)
                tr.GetObject(id, OpenMode.ForWrite).Erase();
        }
    }
}
