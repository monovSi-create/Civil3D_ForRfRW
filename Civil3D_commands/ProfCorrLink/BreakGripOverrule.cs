using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.DatabaseServices;
using Civil3D_commands.Shared;

namespace Civil3D_commands.AssociativeBreaks
{
    /// <summary>
    /// Кастомная грипса: одна точка на прокси-линию.
    /// - в виде профиля двигается по горизонтали (меняется только пикет);
    /// - в плане двигается вдоль оси (через проекцию пикета);
    /// - перемещение зажимается между соседними разрывами с буфером (0.1 м).
    /// Грипсы выдаются только в режиме редактирования.
    /// Тяжёлый ресинк (профиль/области/коридор) выполняется НЕ здесь, а в реакторе по
    /// окончании команды растягивания — здесь только дешёвое перемещение линии.
    /// </summary>
    public class BreakGripOverrule : GripOverrule
    {
        public static double Buffer = 0.1; // буфер, чтобы область не схлопнулась

        public override bool IsApplicable(RXObject overruledSubject)
        {
            // Защита от краша: Civil-объекты (Profile и др.) могут попасть сюда
            // через внутренние примитивы — перехватываем любое исключение.
            try
            {
                if (!(overruledSubject is Line ln)) return false;
                return BreakProxyFactory.GetMarkerGuid(ln).HasValue;
            }
            catch { return false; }
        }

        /// <summary>
        /// Настоящий прокси разрыва (не копия): владелец определяется по хэндлу
        /// в модели, а не по Guid из XData — у копии он тот же.
        /// </summary>
        private static bool IsOwnedProxy(Autodesk.AutoCAD.DatabaseServices.Entity entity)
        {
            try
            {
                var session = BreakSession.Current;
                if (session == null || entity.ObjectId.IsNull) return false;
                if (BreakProxyFactory.GetMarkerGuid(entity) == null) return false;

                var m = session.Store.GetByProxy(entity.ObjectId.Handle);
                return m != null && m.OwnsProxy(entity.ObjectId.Handle);
            }
            catch { return false; }
        }

        // У настоящих прокси РУЧЕК НЕТ ВООБЩЕ: границу двигают щелчком по стрелке
        // (BreakArrowOverrule рисует их, BreakArrowMouse ловит щелчок). Ручки
        // не вызываются — ни стандартные (концы и середина линии), ни свои.
        // Копии прокси остаются обычными отрезками со стандартными ручками.
        public override void GetGripPoints(Autodesk.AutoCAD.DatabaseServices.Entity entity, GripDataCollection grips,
            double curViewUnitSize, int gripSize, Vector3d curViewDir, GetGripPointsFlags bitFlags)
        {
            try
            {
                if (IsOwnedProxy(entity)) return;
                base.GetGripPoints(entity, grips, curViewUnitSize, gripSize, curViewDir, bitFlags);
            }
            catch { /* не роняем AutoCAD из-за ошибки в оверруле */ }
        }

        public override void GetGripPoints(Autodesk.AutoCAD.DatabaseServices.Entity entity,
            Point3dCollection gripPoints, IntegerCollection snapModes, IntegerCollection geometryIds)
        {
            try
            {
                if (IsOwnedProxy(entity)) return;
                base.GetGripPoints(entity, gripPoints, snapModes, geometryIds);
            }
            catch { /* не роняем AutoCAD из-за ошибки в оверруле */ }
        }

        public override void MoveGripPointsAt(Autodesk.AutoCAD.DatabaseServices.Entity entity, GripDataCollection grips,
            Vector3d offset, MoveGripPointsFlags bitFlags)
        {
            // Ресинк профиля / областей / второго прокси выполнит реактор
            // в CommandEnded; здесь только сама картинка перетаскивания.
            // В плане перемещение отдаётся AutoCAD как есть. Попытка собирать
            // линию заново прямо здесь (перпендикуляр на новом пикете) кончилась
            // тем, что при перетаскивании линия пропадала с экрана и правка
            // до модели не доходила: своя геометрия, поставленная в обход base,
            // видимо, оставалась на временной копии. Не повторять — для точной
            // постановки границы есть ветка «Перенести» в RW_EDITMODE с настоящим jig.
            try { base.MoveGripPointsAt(entity, grips, Constrain(entity, offset), bitFlags); }
            catch { }
        }

        /// <summary>
        /// Разрыв живёт на пикете, а пикет в виде профиля — это координата X.
        /// Поэтому вертикальная составляющая перемещения отбрасывается: линия
        /// ходит строго горизонтально и остаётся во всю высоту вида.
        ///
        /// Горизонталь дополнительно зажимается пределами вида профиля и
        /// участка коридора. Модель клампится ещё раз в оркестраторе (там же
        /// учитываются соседние разрывы), но без этого линию было видно
        /// уехавшей за край вида прямо во время перетаскивания.
        /// </summary>
        private Vector3d Constrain(Autodesk.AutoCAD.DatabaseServices.Entity entity, Vector3d offset)
        {
            var session = BreakSession.Current;
            if (session == null || entity.ObjectId.IsNull) return offset;

            var m = session.Store.GetByProxy(entity.ObjectId.Handle);
            if (m == null) return offset;

            // В плане ось может идти как угодно, «горизонтально» там смысла
            // не имеет — ограничение только для вида профиля.
            if (entity.ObjectId.Handle != m.ProfileProxyHandle) return offset;

            var flat = new Vector3d(offset.X, 0.0, 0.0);

            var doc = Autodesk.AutoCAD.ApplicationServices.Application
                        .DocumentManager.MdiActiveDocument;
            if (doc == null) return flat;

            try
            {
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    var pv = RwHandles.Open<ProfileView>(
                        tr, doc.Database, m.ProfileViewHandle, OpenMode.ForRead);

                    if (pv == null) { tr.Commit(); return flat; }

                    var line = entity as Line;
                    if (line == null) { tr.Commit(); return flat; }

                    Point3d mid = RwGeometry.Midpoint(line);
                    Vector3d result;

                    double station;
                    if (!RwGeometry.TryStationInProfileView(pv, mid + flat, out station))
                    {
                        // Утащили за пределы вида — дальше не пускаем.
                        result = new Vector3d(0.0, 0.0, 0.0);
                    }
                    else
                    {
                        double lo = pv.StationStart;
                        double hi = pv.StationEnd;

                        Baseline bl = session.GetBaseline(tr, m);
                        if (bl != null)
                        {
                            lo = Math.Max(lo, bl.StartStation);
                            // У смены отметки правая часть коридора — другая базовая
                            // линия: двигать границу можно до конца ЕЁ профиля.
                            hi = Math.Min(hi, session.SplitEnd(tr, m, bl.EndStation));
                        }

                        // Смена отметки (в том числе без коридора): граница ходит
                        // от начала левого профиля до конца правого.
                        if (m.IsSplit)
                        {
                            var leftProfile = RwHandles.Open<Profile>(
                                tr, doc.Database, m.ProfileHandle, OpenMode.ForRead);
                            if (leftProfile != null) lo = Math.Max(lo, leftProfile.StartingStation);
                            hi = Math.Min(hi, session.SplitEnd(tr, m, hi));
                        }

                        double clamped = RwGeometry.Clamp(station, lo, hi);

                        if (Math.Abs(clamped - station) < 1e-9)
                        {
                            result = flat;
                        }
                        else
                        {
                            // Пикет зажат — пересчитываем смещение под него.
                            Point3d edge;
                            result = RwGeometry.TryPointInProfileView(
                                         pv, clamped, pv.ElevationMin, out edge)
                                ? new Vector3d(edge.X - mid.X, 0.0, 0.0)
                                : new Vector3d(0.0, 0.0, 0.0);
                        }
                    }

                    tr.Commit();
                    return result;
                }
            }
            catch (System.Exception)
            {
                return flat;
            }
        }
    }

    /// <summary>
    /// Ручка прокси. Своей отрисовки у неё нет: Civil 3D 2024 не вызывает
    /// ни <c>ViewportDraw</c>, ни <c>WorldDraw</c> у собственных <see cref="GripData"/>
    /// (замерено по журналу: ни одного вызова), поэтому ручка рисуется стандартно,
    /// а стрелки показывает <see cref="BreakArrowOverrule"/>.
    /// </summary>
    public class BreakGrip : GripData
    {
        private readonly Guid _markerId;

        public BreakGrip(Guid markerId, Point3d point)
        {
            _markerId = markerId;
            GripPoint = point;
        }

        public override bool ViewportDraw(ViewportDraw worldDraw, ObjectId entityId,
            DrawType type, Point3d? imageGripPoint, int gripSizeInPixels)
        {
            return false; // используем стандартную отрисовку грипсы
        }
    }

    /// <summary>
    /// Стрелки ◄ ► по обе стороны от середины прокси — подсказка, куда его можно
    /// двигать (в виде профиля — горизонтально, в плане — вдоль оси).
    ///
    /// Это не ручка, а часть отрисовки самой линии: собственные ручки Civil 3D
    /// не рисует. Брать стрелку мышью не нужно — тянется стандартная ручка в той
    /// же точке. Рисуются только в режиме правки, опознаются, как и остальное
    /// в модуле, по XData и хэндлу в модели; чужие линии и копии не затрагиваются.
    /// </summary>
    public class BreakArrowOverrule : DrawableOverrule
    {
        // Размеры стрелки в долях от размера ручки (в пикселях).
        public const double ArrowPixels = 14.0;
        private const double Gap = 0.6;
        private const double Length = 1.6;
        private const double Half = 0.8;

        public override bool IsApplicable(RXObject overruledSubject)
        {
            try
            {
                return overruledSubject is Line ln && BreakProxyFactory.GetMarkerGuid(ln).HasValue;
            }
            catch { return false; }
        }

        public override bool WorldDraw(Drawable drawable, WorldDraw wd)
        {
            // Сначала сама линия — стрелки только добавляются.
            bool result = base.WorldDraw(drawable, wd);

            try
            {
                var ln = drawable as Line;
                if (ln == null || ln.ObjectId.IsNull) return result;

                var session = BreakSession.Current;
                if (session == null) return result;

                var m = session.Store.GetByProxy(ln.ObjectId.Handle);
                if (m == null || !m.OwnsProxy(ln.ObjectId.Handle)) return result;
                if (!session.IsEditMode(m.ProfileHandle)) return result;

                Vector3d along = ln.EndPoint - ln.StartPoint;
                Vector3d dir = new Vector3d(-along.Y, along.X, 0.0);
                if (dir.Length < 1e-9) return result;
                dir = dir.GetNormal();
                Vector3d perp = Vector3d.ZAxis.CrossProduct(dir).GetNormal();

                double unit = WorldPerPixel() * ArrowPixels;
                if (unit <= 0.0) return result;

                Point3d p = RwGeometry.Midpoint(ln);

                wd.SubEntityTraits.Color = 5;
                wd.SubEntityTraits.FillType = FillType.FillAlways;

                foreach (double side in new[] { 1.0, -1.0 })
                {
                    Point3d baseCentre = p + dir * (side * Gap * unit);
                    Point3d tip = p + dir * (side * (Gap + Length) * unit);

                    wd.Geometry.Polygon(new Point3dCollection
                    {
                        tip,
                        baseCentre + perp * (Half * unit),
                        baseCentre - perp * (Half * unit)
                    });
                }
            }
            catch (System.Exception)
            {
                // Стрелки — подсказка: ошибка рисования не должна ломать линию.
            }

            return result;
        }

        /// <summary>
        /// Единиц чертежа в одном пикселе текущего вида: так стрелки остаются
        /// одного размера на экране при любом увеличении.
        /// </summary>
        /// <summary>
        /// Центры обеих стрелок прокси по его концам. Общая арифметика для рисования
        /// и для проверки щелчка: стрелка и зона нажатия не должны расходиться.
        /// <paramref name="unit"/> — размер стрелки в единицах чертежа.
        /// </summary>
        public static bool TryArrowCentres(Point3d start, Point3d end, double unit,
                                           out Point3d first, out Point3d second)
        {
            first = Point3d.Origin;
            second = Point3d.Origin;

            Vector3d along = end - start;
            Vector3d dir = new Vector3d(-along.Y, along.X, 0.0);
            if (dir.Length < 1e-9 || unit <= 0.0) return false;
            dir = dir.GetNormal();

            Point3d mid = new Point3d((start.X + end.X) / 2.0, (start.Y + end.Y) / 2.0,
                                      (start.Z + end.Z) / 2.0);
            double r = (Gap + Length / 2.0) * unit;

            first = mid + dir * r;
            second = mid - dir * r;
            return true;
        }

        /// <summary>
        /// Курсор в координатах чертежа, посчитанный по системным переменным вида
        /// (VIEWCTR, VIEWSIZE, SCREENSIZE), а не через Editor.PointToWorld — тот в чертеже
        /// пользователя возвращал не те абсолютные координаты (разница в сотни тысяч единиц).
        /// <paramref name="x"/>, <paramref name="y"/> — клиентские пиксели окна чертежа
        /// (начало слева сверху). Поворот вида (VIEWTWIST) не учитывается; пересчёт UCS→WCS
        /// делается по текущей ПСК.
        /// </summary>
        public static bool TryCursorWorld(int x, int y, out Point3d world, out double wpp)
        {
            world = Point3d.Origin;
            wpp = 0.0;

            try
            {
                Func<string, object> app = Autodesk.AutoCAD.ApplicationServices.Application.GetSystemVariable;
                double viewSize = Convert.ToDouble(app("VIEWSIZE"));

                object scr = app("SCREENSIZE");
                double w, h;
                if (scr is Point2d p2) { w = p2.X; h = p2.Y; }
                else if (scr is double[] arr && arr.Length > 1) { w = arr[0]; h = arr[1]; }
                else return false;
                if (h < 1.0) return false;

                object ctr = app("VIEWCTR");
                double cx, cy;
                if (ctr is Point3d p3) { cx = p3.X; cy = p3.Y; }
                else if (ctr is Point2d c2) { cx = c2.X; cy = c2.Y; }
                else if (ctr is double[] ca && ca.Length > 1) { cx = ca[0]; cy = ca[1]; }
                else return false;

                wpp = viewSize / h;

                // VIEWCTR — в координатах текущей ПСК; переводим в мировые.
                var ucsPoint = new Point3d(cx + (x - w / 2.0) * wpp, cy - (y - h / 2.0) * wpp, 0.0);

                var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                world = doc != null
                    ? ucsPoint.TransformBy(doc.Editor.CurrentUserCoordinateSystem)
                    : ucsPoint;
                return true;
            }
            catch (System.Exception)
            {
                return false;
            }
        }

        public static double WorldPerPixel()
        {
            try
            {
                double viewSize = Convert.ToDouble(
                    Autodesk.AutoCAD.ApplicationServices.Application.GetSystemVariable("VIEWSIZE"));

                object screen = Autodesk.AutoCAD.ApplicationServices.Application.GetSystemVariable("SCREENSIZE");
                double heightPx = screen is Point2d p2 ? p2.Y
                                : screen is double[] arr && arr.Length > 1 ? arr[1]
                                : 0.0;

                return heightPx > 1.0 ? viewSize / heightPx : 0.0;
            }
            catch (System.Exception)
            {
                return 0.0;
            }
        }
    }
}
