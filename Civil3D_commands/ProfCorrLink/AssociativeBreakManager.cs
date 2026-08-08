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

                tr.Commit();
            }
            _session.Store.SaveToDatabase(Doc.Database);
            return template.Id;
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
                var profile = (Profile)tr.GetObject(Resolve(m.ProfileHandle), OpenMode.ForWrite);

                if (m.IsStep)
                    ProfileGeometryOps.RemoveStep(profile, m.Station, m.StepHeight, m.HalfGap);

                EraseIfValid(tr, m.ProfileProxyHandle);
                EraseIfValid(tr, m.PlanProxyHandle);

                // Правая область поглощается левой; соседний справа маркер ссылался
                // на исчезнувшую — переводим его на выжившую.
                Baseline bl = _session.GetBaseline(tr, m);
                if (bl != null &&
                    ProfileGeometryOps.RemoveBreak(bl, m, out Guid removed, out Guid surviving))
                    RebindNeighbours(removed, surviving);

                _session.Store.Remove(id);

                RebuildCorridor(tr, m);
                tr.Commit();
            }
            _session.Store.SaveToDatabase(Doc.Database);
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
                tr.Commit();
            }
            _session.Store.SaveToDatabase(Doc.Database);
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
            var m = _session.Store.Get(id);
            if (m == null) return;

            using (Doc.LockDocument())
            using (_session.Suspend())
            using (var tr = Doc.Database.TransactionManager.StartTransaction())
            {
                var db = Doc.Database;
                var profile = RwHandles.Open<Profile>(tr, db, m.ProfileHandle, OpenMode.ForWrite);
                var pv = RwHandles.Open<ProfileView>(tr, db, m.ProfileViewHandle, OpenMode.ForRead);
                var alignment = RwHandles.Open<Alignment>(tr, db, m.AlignmentHandle, OpenMode.ForRead);

                if (m.IsStep && profile != null)
                    ProfileGeometryOps.RemoveStep(profile, m.Station, m.StepHeight, m.HalfGap);

                m.IsStep = isStep;
                m.StepHeight = isStep ? stepHeight : 0.0;
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
                if (bl != null) ProfileGeometryOps.ApplyBreak(bl, m, m.Station);

                BreakProxyFactory.UpdateProxyGeometry(tr, m, pv, alignment);
                WriteProps(tr, m);
                RebuildCorridor(tr, m);

                tr.Commit();
            }
            _session.Store.SaveToDatabase(Doc.Database);
        }

        // ----------------------------------------------------------------------

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
