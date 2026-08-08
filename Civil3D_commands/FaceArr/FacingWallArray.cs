using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.DatabaseServices;
using Autodesk.Aec.DatabaseServices;   // MultiViewBlockReference / DictionaryMultiViewBlockDefinition
// Entity и ObjectId есть и в AutoCAD-, и в Civil-пространстве имён — снимаем неоднозначность.
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;
using ObjectId = Autodesk.AutoCAD.DatabaseServices.ObjectId;

namespace Civil3D_commands.FaceArr
{
    /// <summary>
    /// Что рисовать на виде профиля. Хранится в контроллере, у каждого массива своё.
    /// </summary>
    public enum FacingWallProjectionMode
    {
        /// <summary>Контур блока: прямоугольник BlockWidth x BlockHeight в координатах вида.</summary>
        Outline = 0,

        /// <summary>Сам MultiViewBlock, вставленный в плоскость вида профиля.</summary>
        MvBlock = 1,

        /// <summary>
        /// Обычный 2D-блок AutoCAD из состава многовидового.
        /// Легче MVBlock и переживает экспорт в чистый AutoCAD без разбиения:
        /// одно определение блока на весь массив, а не по объекту на вставку.
        /// </summary>
        Block2d = 2
    }

    /// <summary>Что за блок стоит в этой позиции ряда.</summary>
    public enum FacingWallBlockKind
    {
        /// <summary>Целый блок шириной BlockWidth.</summary>
        Whole = 0,

        /// <summary>Половинчатый блок шириной BlockWidth/2.</summary>
        Half = 1,

        /// <summary>Блок, подставленный пользователем взамен штатного.</summary>
        Custom = 2
    }

    /// <summary>
    /// Одно место в ряду: где стоит блок, какой он ширины и какое определение
    /// в него вставлять. Раскладка перестала быть равномерной, поэтому одного
    /// пикета уже недостаточно.
    /// </summary>
    public class FacingWallBlockPlacement
    {
        /// <summary>Меньший пикет блока — независимо от направления раскладки.</summary>
        public double Station;

        /// <summary>Фактическая ширина именно этого блока.</summary>
        public double Width;

        public FacingWallBlockKind Kind;

        /// <summary>Имя определения MVBlock для этого места.</summary>
        public string MvBlockDefName;
    }

    /// <summary>
    /// Замена штатного блока на другой — команда FACINGWALLREPLACE.
    ///
    /// Привязана К МЕСТУ НА ТРАССЕ, а не к номеру блока в ряду: особый блок
    /// обычно стоит у чего-то реального (водопропуск, столб), и при сдвиге
    /// границ раскладки должен оставаться там же. Если граница ушла за него,
    /// замена не применяется, но и не теряется.
    /// </summary>
    public class FacingWallBlockOverride
    {
        /// <summary>Пикет, по которому замена находит своё место.</summary>
        public double Station;

        /// <summary>Ширина блока-заменителя.</summary>
        public double Width;

        /// <summary>Имя определения MVBlock.</summary>
        public string MvBlockDefName;
    }

    /// <summary>
    /// Параметрическое определение стены.
    /// Источник истины для геометрии массива.
    /// </summary>
    public class FacingWallDefinition
    {
        /// <summary>
        /// Ось Civil 3D, вдоль которой строится массив.
        /// Это основная геометрическая привязка стены.
        /// </summary>
        public ObjectId AlignmentId { get; set; }

        /// <summary>
        /// ProfileView, выбранный пользователем при создании.
        /// Используется для профильного представления и редактирования.
        /// </summary>
        public ObjectId ProfileViewId { get; set; }

        /// <summary>
        /// Имя определения MultiViewBlock.
        /// </summary>
        public string MvBlockDefName { get; set; }

        /// <summary>
        /// Смещение массива относительно Alignment.
        /// </summary>
        public double FaceOffset { get; set; }

        /// <summary>
        /// Абсолютная отметка низа стены.
        /// </summary>
        public double BaseElevation { get; set; }

        /// <summary>
        /// Количество рядов.
        /// Фактически определяется количеством элементов Rows.
        /// </summary>
        public int RowCount
        {
            get { return Rows != null ? Rows.Count : 0; }
        }

        /// <summary>
        /// Ширина одного MVBlock вдоль Alignment.
        /// </summary>
        public double BlockWidth { get; set; }

        /// <summary>
        /// Высота одного MVBlock.
        /// </summary>
        public double BlockHeight { get; set; }

        /// <summary>
        /// Масштаб MVBlock.
        /// </summary>
        public double Scale { get; set; } = 1.0;

        /// <summary>
        /// Что рисовать на виде профиля. Меняется командой FACINGWALLMODE.
        /// </summary>
        public FacingWallProjectionMode ProjectionMode { get; set; }
            = FacingWallProjectionMode.Outline;

        /// <summary>
        /// Поправка к повороту блока, радианы. Прибавляется к направлению хорды.
        ///
        /// Нужна, потому что ориентация геометрии внутри определения блока —
        /// не наше дело: она зависит от того, как блок нарисован. Развернуть
        /// лицевую грань на другую сторону — это ровно +Pi здесь.
        /// Меняется командой FACINGWALLROTATE.
        /// </summary>
        public double BlockRotationOffset { get; set; }

        /// <summary>
        /// Определение MultiViewBlock половинчатого блока. Пусто — половинки
        /// не ставятся вовсе, и в перевязке остаётся пустое место, как раньше.
        /// </summary>
        public string HalfMvBlockDefName { get; set; }

        /// <summary>2D-блок половинки для режима Block2d. Пусто — берётся вид спереди.</summary>
        public string HalfViewBlockName { get; set; }

        /// <summary>
        /// Шаг ручек и сетка раскладки: половина ширины блока. Именно поэтому
        /// по краям ряда может появиться половинка.
        /// </summary>
        public double HalfStep()
        {
            return BlockWidth / 2.0;
        }

        /// <summary>
        /// Имя обычного 2D-блока AutoCAD для режима Block2d.
        /// Многовидовой блок собран из таких блоков — по одному на направление
        /// вида; для профиля осмыслен тот, что отвечает за вид спереди (фасад).
        /// В остальных режимах не используется.
        /// </summary>
        public string ViewBlockName { get; set; }

        /// <summary>
        /// Индивидуальные параметры каждого ряда.
        /// </summary>
        public List<FacingWallRowDefinition> Rows { get; set; }
            = new List<FacingWallRowDefinition>();

        /// <summary>
        /// Пикет-ЯКОРЬ раскладки, задаётся в плане. Именно отсюда начинают
        /// укладываться блоки, в сторону LayoutEndStation.
        /// </summary>
        public double LayoutStartStation { get; set; }

        /// <summary>
        /// Дальняя граница раскладки, задаётся в плане. Может быть МЕНЬШЕ якоря:
        /// тогда блоки укладываются в обратную сторону. Последним ложится тот,
        /// что целиком помещается до этой границы.
        /// </summary>
        public double LayoutEndStation { get; set; }

        // Границы всего две, а носителей ручек четыре — по паре на вид.
        // Виды равноправны: двинули в одном, второй подтянулся.

        /// <summary>Перпендикулярный трассе маркер-ручка якоря (план).</summary>
        public ObjectId PlanStartGripId { get; set; }

        /// <summary>Перпендикулярный трассе маркер-ручка дальней границы (план).</summary>
        public ObjectId PlanEndGripId { get; set; }

        /// <summary>Вертикальный маркер-ручка якоря (вид профиля).</summary>
        public ObjectId ProfileStartGripId { get; set; }

        /// <summary>Вертикальный маркер-ручка дальней границы (вид профиля).</summary>
        public ObjectId ProfileEndGripId { get; set; }

        /// <summary>
        /// Проекции, созданные штатным инструментом Civil 3D (режим MvBlock).
        /// Хранятся отдельно от ProjectionIds рядов: те мы рисуем сами и знаем
        /// поштучно, а эти создаёт Civil, и опознаются они только по разнице
        /// состава чертежа до и после команды.
        /// </summary>
        public List<ObjectId> CivilProjectionIds { get; set; } = new List<ObjectId>();

        /// <summary>
        /// Хэндл вставки-контроллера, записанный в саму запись (версия 10).
        ///
        /// Нужен, чтобы отличить контроллер от его копии. Расширенный словарь
        /// копируется вместе со вставкой, поэтому копия несёт хэндлы объектов
        /// ОРИГИНАЛА: до этой проверки FACINGWALLDELETE на копии стирал блоки
        /// оригинала, а ручки копии двигали чужой массив.
        /// </summary>
        public string SelfHandle { get; set; }

        /// <summary>
        /// Запись прочитана из копии контроллера (SelfHandle не совпал с фактическим).
        /// Связи с объектами при этом сброшены: копия ничем не владеет, пока её
        /// не перестроят командой FACINGWALLREBUILDALL.
        /// </summary>
        public bool IsDetachedCopy { get; set; }

        /// <summary>
        /// Забыть все связи с объектами чертежа, сохранив параметры массива.
        /// </summary>
        public void ClearObjectLinks()
        {
            PlanStartGripId = ObjectId.Null;
            PlanEndGripId = ObjectId.Null;
            ProfileStartGripId = ObjectId.Null;
            ProfileEndGripId = ObjectId.Null;
            CivilProjectionIds = new List<ObjectId>();

            if (Rows == null) return;

            foreach (FacingWallRowDefinition row in Rows)
            {
                if (row == null) continue;
                row.BlockIds = new List<ObjectId>();
                row.ProjectionIds = new List<ObjectId>();
                row.GripLineId = ObjectId.Null;
            }
        }

        /// <summary>
        /// Отрезок-ручка, записанный за этим номером: ряд (>= 0) или один из
        /// четырёх маркеров границ (см. FacingWallGripTag).
        /// </summary>
        public ObjectId GetGripLineId(int rowIndex)
        {
            switch (rowIndex)
            {
                case FacingWallGripTag.PlanStartRowIndex: return PlanStartGripId;
                case FacingWallGripTag.PlanEndRowIndex: return PlanEndGripId;
                case FacingWallGripTag.ProfileStartRowIndex: return ProfileStartGripId;
                case FacingWallGripTag.ProfileEndRowIndex: return ProfileEndGripId;
            }

            FacingWallRowDefinition row = GetRow(rowIndex);
            return row == null ? ObjectId.Null : row.GripLineId;
        }

        /// <summary>Записать носителя ручек за этим номером.</summary>
        public void SetGripLineId(int rowIndex, ObjectId id)
        {
            switch (rowIndex)
            {
                case FacingWallGripTag.PlanStartRowIndex: PlanStartGripId = id; return;
                case FacingWallGripTag.PlanEndRowIndex: PlanEndGripId = id; return;
                case FacingWallGripTag.ProfileStartRowIndex: ProfileStartGripId = id; return;
                case FacingWallGripTag.ProfileEndRowIndex: ProfileEndGripId = id; return;
            }

            FacingWallRowDefinition row = GetRow(rowIndex);
            if (row != null) row.GripLineId = id;
        }

        /// <summary>Меньший из двух плановых пикетов — независимо от направления.</summary>
        public double LayoutLowStation()
        {
            return Math.Min(LayoutStartStation, LayoutEndStation);
        }

        /// <summary>Больший из двух плановых пикетов — независимо от направления.</summary>
        public double LayoutHighStation()
        {
            return Math.Max(LayoutStartStation, LayoutEndStation);
        }

        /// <summary>Ряд по индексу или null.</summary>
        public FacingWallRowDefinition GetRow(int rowIndex)
        {
            if (Rows == null) return null;
            return Rows.Find(r => r != null && r.RowIndex == rowIndex);
        }

        /// <summary>
        /// Разложить все ряды по плановым границам. Вызывается при создании и
        /// при перетаскивании плановых ручек: план главнее профиля.
        /// </summary>
        public void ResetRowsToLayout()
        {
            if (Rows == null) return;

            foreach (FacingWallRowDefinition row in Rows)
            {
                if (row == null) continue;
                row.StartStation = LayoutStartStation;
                row.EndStation = LayoutEndStation;
            }
        }
    }


    /// <summary>
    /// Геометрическое определение отдельного ряда стены.
    /// Здесь же — связи с порождёнными объектами: только так перестроение
    /// одного ряда может стереть ровно свои блоки и свои проекции.
    /// </summary>
    public class FacingWallRowDefinition
    {
        /// <summary>
        /// Индекс ряда, начиная с нуля.
        /// </summary>
        public int RowIndex { get; set; }

        /// <summary>
        /// Начальная станция ряда.
        /// </summary>
        public double StartStation { get; set; }

        /// <summary>
        /// Конечная станция ряда.
        /// </summary>
        public double EndStation { get; set; }

        /// <summary>MVBlock-вставки этого ряда в плане.</summary>
        public List<ObjectId> BlockIds { get; set; } = new List<ObjectId>();

        /// <summary>Объекты этого ряда в виде профиля.</summary>
        public List<ObjectId> ProjectionIds { get; set; } = new List<ObjectId>();

        /// <summary>
        /// Замены блоков в этом ряду, по местам на трассе.
        /// </summary>
        public List<FacingWallBlockOverride> Overrides { get; set; }
            = new List<FacingWallBlockOverride>();

        /// <summary>
        /// Служебный отрезок-ручка ряда в виде профиля.
        /// Именно за него тянут мышью: ручки на его концах рисует сам AutoCAD.
        /// Хранится отдельно от ProjectionIds, потому что при перестроении ряда
        /// он не пересоздаётся, а сдвигается — нельзя стирать объект,
        /// который в этот момент редактируют.
        /// </summary>
        public ObjectId GripLineId { get; set; }
    }


    /// <summary>
    /// Результат генерации стены.
    /// Позволяет управлять MVBlock отдельно по каждому ряду.
    /// </summary>
    public class FacingWallBuildResult
    {
        public List<ObjectId> AllBlocks { get; }
            = new List<ObjectId>();

        public Dictionary<int, List<ObjectId>> RowBlocks { get; }
            = new Dictionary<int, List<ObjectId>>();

        public void AddBlock(int rowIndex, ObjectId blockId)
        {
            AllBlocks.Add(blockId);

            List<ObjectId> rowCollection;

            if (!RowBlocks.TryGetValue(rowIndex, out rowCollection))
            {
                rowCollection = new List<ObjectId>();
                RowBlocks.Add(rowIndex, rowCollection);
            }

            rowCollection.Add(blockId);
        }
    }

    // =====================================================================
    //  Движок раскладки: из определения порождает реальные MVBlock-ссылки.
    //  Только геометрия. Никакого пользовательского ввода, хранения и грипс.
    // =====================================================================
    public static class FacingWallBuilder
    {
        private const double Eps = 1e-6;

        /// <summary>
        /// Единственный источник истины по раскладке: начальные станции блоков ряда.
        /// Этим же перечислением пользуется профильное отображение, поэтому план
        /// и профиль не могут разъехаться.
        ///
        /// Ряд НАПРАВЛЕН: StartStation — якорь, от него кладутся блоки, EndStation —
        /// дальняя граница. Якорь может быть правее границы: тогда раскладка идёт
        /// в обратную сторону. Последним ложится блок, целиком помещающийся до
        /// границы; остаток до неё не заполняется.
        ///
        /// Перевязка: нечётные ряды сдвинуты на BlockWidth/2 ОТ ЯКОРЯ, то есть
        /// в ту же сторону, куда идёт раскладка.
        ///
        /// Возвращается всегда МЕНЬШИЙ пикет блока, независимо от направления, —
        /// остальному коду направление знать не нужно.
        /// </summary>
        public static IEnumerable<FacingWallBlockPlacement> EnumerateBlocks(
            FacingWallDefinition def,
            FacingWallRowDefinition row)
        {
            if (def == null || row == null) yield break;

            double whole = def.BlockWidth;
            if (whole <= Eps) yield break;

            double span = Math.Abs(row.EndStation - row.StartStation);
            if (span <= Eps) yield break;

            bool forward = def.LayoutEndStation >= def.LayoutStartStation;
            double half = whole / 2.0;
            bool hasHalf = !string.IsNullOrEmpty(def.HalfMvBlockDefName);

            List<FacingWallBlockOverride> overrides = SortedOverrides(row, forward);
            int used = 0;

            // t — пройденное расстояние от НАЧАЛА РЯДА вдоль направления раскладки
            double t = 0.0;

            // Перевязка отсчитывается ОТ ЯКОРЯ, а не от начала ряда. Иначе
            // подрезанный ряд уезжает швами относительно соседей: у него своя
            // точка отсчёта. Нечётные ряды сдвинуты на полблока.
            double phase = (row.RowIndex % 2 != 0) ? half : 0.0;

            double fromAnchor = forward
                ? row.StartStation - def.LayoutStartStation
                : def.LayoutStartStation - row.StartStation;

            // Ряд может начинаться в середине целой ячейки сетки — её остаток
            // закрывается половинкой ровно так же, как остаток на дальнем конце.
            double lead = LeadingGap(fromAnchor, phase, whole);

            if (lead > Eps)
            {
                if (hasHalf && Math.Abs(lead - half) < Eps && span >= half - Eps)
                    yield return Place(row, forward, t, half, FacingWallBlockKind.Half,
                        def.HalfMvBlockDefName);

                // Даже если половинку поставить нечем, шаг делаем: попасть
                // в сетку важнее, чем закрыть остаток.
                t = Math.Min(lead, span);
            }

            while (span - t > Eps)
            {
                double remaining = span - t;
                double edge = forward ? row.StartStation + t : row.StartStation - t;

                double width;
                FacingWallBlockKind kind;
                string name;

                FacingWallBlockOverride ov = TakeOverride(overrides, ref used, edge, whole, forward);

                if (ov != null)
                {
                    width = ov.Width;
                    kind = FacingWallBlockKind.Custom;
                    name = ov.MvBlockDefName;
                }
                else if (remaining >= whole - Eps)
                {
                    width = whole;
                    kind = FacingWallBlockKind.Whole;
                    name = def.MvBlockDefName;
                }
                else if (hasHalf && remaining >= half - Eps)
                {
                    width = half;
                    kind = FacingWallBlockKind.Half;
                    name = def.HalfMvBlockDefName;
                }
                else
                {
                    break;   // остаток меньше половинки — не заполняем
                }

                if (width <= Eps || width > remaining + Eps) break;

                yield return Place(row, forward, t, width, kind, name);
                t += width;
            }
        }

        /// <summary>
        /// Сколько осталось до ближайшего шва сетки перевязки, если ряд начался
        /// в середине целой ячейки. Ноль — ряд начинается ровно на шве.
        /// </summary>
        private static double LeadingGap(double fromAnchor, double phase, double whole)
        {
            double rel = fromAnchor - phase;
            double frac = rel - Math.Floor(rel / whole) * whole;   // в [0, whole)

            if (frac <= Eps) return 0.0;
            if (whole - frac <= Eps) return 0.0;

            return whole - frac;
        }

        /// <summary>Замены по ходу раскладки: сначала ближние к якорю.</summary>
        private static List<FacingWallBlockOverride> SortedOverrides(
            FacingWallRowDefinition row, bool forward)
        {
            var list = new List<FacingWallBlockOverride>();
            if (row.Overrides == null) return list;

            foreach (FacingWallBlockOverride ov in row.Overrides)
                if (ov != null && ov.Width > Eps) list.Add(ov);

            list.Sort((a, b) => forward
                ? a.Station.CompareTo(b.Station)
                : b.Station.CompareTo(a.Station));

            return list;
        }

        /// <summary>
        /// Замена, попадающая в текущее место ряда. Пройденные молча
        /// пропускаются: раскладка могла уехать, и их пикеты остались позади.
        /// </summary>
        private static FacingWallBlockOverride TakeOverride(
            List<FacingWallBlockOverride> overrides, ref int used,
            double lead, double whole, bool forward)
        {
            while (used < overrides.Count)
            {
                FacingWallBlockOverride ov = overrides[used];
                double ahead = forward ? ov.Station - lead : lead - ov.Station;

                if (ahead < -Eps) { used++; continue; }     // уже позади
                if (ahead >= whole - Eps) return null;      // ещё далеко

                used++;
                return ov;
            }

            return null;
        }

        private static FacingWallBlockPlacement Place(
            FacingWallRowDefinition row, bool forward,
            double t, double width, FacingWallBlockKind kind, string name)
        {
            double lead = forward ? row.StartStation + t : row.StartStation - t;

            return new FacingWallBlockPlacement
            {
                Station = forward ? lead : lead - width,
                Width = width,
                Kind = kind,
                MvBlockDefName = name
            };
        }

        /// <summary>Абсолютная отметка низа ряда.</summary>
        public static double RowElevation(
            FacingWallDefinition def,
            FacingWallRowDefinition row)
        {
            return def.BaseElevation + row.RowIndex * def.BlockHeight;
        }

        /// <summary>
        /// Построить ОДИН ряд. Возвращает созданные MVBlock-вставки.
        /// </summary>
        public static List<ObjectId> GenerateRow(
            FacingWallDefinition def,
            FacingWallRowDefinition row,
            Alignment alignment,
            Database db,
            BlockTableRecord btr,
            Transaction tr)
        {
            var created = new List<ObjectId>();

            if (def == null || row == null || alignment == null) return created;

            double elevation = RowElevation(def, row);
            double alignStart = alignment.StartingStation;
            double alignEnd = alignment.EndingStation;

            // Определения блоков разрешаем по одному разу на имя, а не на вставку.
            var defCache = new Dictionary<string, ObjectId>();

            foreach (FacingWallBlockPlacement block in EnumerateBlocks(def, row))
            {
                // блок целиком должен лежать на оси, иначе PointLocation бросит
                if (block.Station < alignStart - Eps) continue;
                if (block.Station + block.Width > alignEnd + Eps) continue;

                Point3d insertion;
                double angle;
                if (!TryPlaceOnAlignment(alignment, def, block, elevation, out insertion, out angle))
                    continue;

                ObjectId id = ResolveCached(db, tr, defCache, block.MvBlockDefName);
                if (id.IsNull) continue;

                ObjectId blockId = CreateMvBlockRef(id, insertion, angle, def.Scale, btr, tr);
                if (!blockId.IsNull) created.Add(blockId);
            }

            return created;
        }

        /// <summary>
        /// Положение и поворот блока на трассе.
        ///
        /// Блок ЦЕНТРИРУЕТСЯ на хорде своего пролёта. Раньше он вставлялся в
        /// начало хорды, а поворачивался по её направлению — то есть по
        /// касательной в СЕРЕДИНЕ пролёта. Из-за этого каждый блок был развёрнут
        /// относительно оси в точке своего начала на половину собственного угла
        /// поворота, BlockWidth/(2R): при R=50 м это 0.23°, systematically в одну
        /// сторону. Центрирование делит эту ошибку пополам и симметрично.
        ///
        /// Остаётся не исправленным другое: пикеты отмеряются по оси, а блоки
        /// стоят на смещённой кривой, которая длиннее в отношении (R+o)/R.
        /// При FaceOffset != 0 на кривых копится щель BlockWidth*FaceOffset/R
        /// на каждом шве. При FaceOffset = 0 (умолчание) этого нет вовсе.
        /// </summary>
        private static bool TryPlaceOnAlignment(
            Alignment alignment,
            FacingWallDefinition def,
            FacingWallBlockPlacement block,
            double elevation,
            out Point3d insertion,
            out double angle)
        {
            insertion = Point3d.Origin;
            angle = 0.0;

            double x1 = 0.0, y1 = 0.0, x2 = 0.0, y2 = 0.0;

            try
            {
                alignment.PointLocation(block.Station, def.FaceOffset, ref x1, ref y1);
                alignment.PointLocation(block.Station + block.Width, def.FaceOffset, ref x2, ref y2);
            }
            catch (System.Exception)
            {
                return false;
            }

            double dx = x2 - x1;
            double dy = y2 - y1;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-9) return false;

            angle = Math.Atan2(dy, dx) + def.BlockRotationOffset;

            // Отступаем от середины хорды вдоль СОБСТВЕННОЙ оси блока — с учётом
            // поправки. Иначе при развороте на 180° блок уехал бы на свою длину
            // вместо того, чтобы перевернуться на месте.
            double ux = Math.Cos(angle);
            double uy = Math.Sin(angle);

            double cx = (x1 + x2) / 2.0;
            double cy = (y1 + y2) / 2.0;

            insertion = new Point3d(
                cx - ux * block.Width / 2.0,
                cy - uy * block.Width / 2.0,
                elevation);

            return true;
        }

        /// <summary>
        /// Направление оси трассы на пикете — численно, по двум близким точкам.
        /// Своего метода у Alignment нет: он отдаёт только координаты точки.
        /// Нужно диагностике поворота (FACINGWALLANGLE).
        /// </summary>
        public static bool TryTangentAngle(
            Alignment alignment, double station, out double angle)
        {
            angle = 0.0;
            if (alignment == null) return false;

            const double d = 0.01;

            double s1 = Math.Max(alignment.StartingStation, station - d);
            double s2 = Math.Min(alignment.EndingStation, station + d);
            if (s2 - s1 < 1e-9) return false;

            try
            {
                double x1 = 0.0, y1 = 0.0, x2 = 0.0, y2 = 0.0;
                alignment.PointLocation(s1, 0.0, ref x1, ref y1);
                alignment.PointLocation(s2, 0.0, ref x2, ref y2);

                double dx = x2 - x1, dy = y2 - y1;
                if (Math.Sqrt(dx * dx + dy * dy) < 1e-9) return false;

                angle = Math.Atan2(dy, dx);
                return true;
            }
            catch (System.Exception)
            {
                return false;
            }
        }

        private static ObjectId ResolveCached(
            Database db, Transaction tr, Dictionary<string, ObjectId> cache, string name)
        {
            if (string.IsNullOrEmpty(name)) return ObjectId.Null;

            ObjectId id;
            if (cache.TryGetValue(name, out id)) return id;

            try
            {
                id = ResolveMvBlockDef(db, tr, name);
            }
            catch (System.Exception)
            {
                id = ObjectId.Null;   // определение пропало — это место пропускаем
            }

            cache[name] = id;
            return id;
        }

        /// <summary>
        /// Построить все ряды массива.
        /// </summary>
        public static FacingWallBuildResult GenerateRows(
            FacingWallDefinition def,
            Transaction tr,
            Database db)
        {
            if (def == null)
                throw new ArgumentNullException(nameof(def));

            if (def.Rows == null || def.Rows.Count == 0)
                throw new InvalidOperationException(
                    "Определение стены не содержит рядов.");

            if (def.AlignmentId.IsNull)
                throw new InvalidOperationException(
                    "Не задан Alignment.");

            var result = new FacingWallBuildResult();

            Alignment alignment =
                tr.GetObject(def.AlignmentId, OpenMode.ForRead) as Alignment;

            if (alignment == null)
                throw new InvalidOperationException(
                    "Не удалось получить Alignment.");

            var btr = (BlockTableRecord)tr.GetObject(
                SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);

            foreach (FacingWallRowDefinition row in def.Rows)
            {
                if (row == null) continue;

                foreach (ObjectId id in GenerateRow(def, row, alignment, db, btr, tr))
                    result.AddBlock(row.RowIndex, id);
            }

            return result;
        }

        /// <summary>Стереть перечисленные объекты, молча пропуская уже стёртые.</summary>
        public static void EraseAll(Transaction tr, IEnumerable<ObjectId> ids)
        {
            if (ids == null) return;

            foreach (ObjectId id in ids)
            {
                if (id.IsNull || !id.IsValid || id.IsErased) continue;
                try
                {
                    var ent = tr.GetObject(id, OpenMode.ForWrite, false) as Entity;
                    if (ent != null) ent.Erase();
                }
                catch (Autodesk.AutoCAD.Runtime.Exception)
                {
                    // объект уже удалён из чертежа — связь просто устарела
                }
            }
        }

        // -----------------------------------------------------------------
        //  ИЗОЛИРОВАННАЯ часть AEC-API.
        // -----------------------------------------------------------------
        public static ObjectId CreateMvBlockRef(
            ObjectId mvDefId, Point3d pos, double rotation, double scale,
            BlockTableRecord btr, Transaction tr)
        {
            var mv = new MultiViewBlockReference();
            mv.SetDatabaseDefaults();

            mv.BlockDefId = mvDefId;
            mv.Location = pos;
            mv.Normal = Vector3d.ZAxis;   // вертикальный Z
            mv.Rotation = rotation;       // касательно к оси в плане
            mv.Scale = new Scale3d(scale);

            ObjectId id = btr.AppendEntity(mv);
            tr.AddNewlyCreatedDBObject(mv, true);
            return id;
        }

        public static ObjectId ResolveMvBlockDef(Database db, Transaction tr, string name)
        {
            var dic = new DictionaryMultiViewBlockDefinition(db);
            if (!string.IsNullOrEmpty(name) && dic.Has(name, tr))
                return dic.GetAt(name);

            throw new Autodesk.AutoCAD.Runtime.Exception(
                Autodesk.AutoCAD.Runtime.ErrorStatus.KeyNotFound,
                "Определение MVBlock '" + name + "' не найдено");
        }

        /// <summary>
        /// Обычные 2D-блоки AutoCAD, из которых собран многовидовой блок.
        /// Пустой список, если определение не найдено или не содержит блоков.
        /// </summary>
        public static List<string> GetViewBlockNames(
            Database db, Transaction tr, string mvDefName)
        {
            var names = new List<string>();

            MultiViewBlockDefinition mvDef = TryGetMvBlockDef(db, tr, mvDefName);
            if (mvDef == null) return names;

            foreach (ObjectId id in mvDef.GetAllBlocksReferenced())
            {
                if (id.IsNull || id.IsErased) continue;

                var btr = tr.GetObject(id, OpenMode.ForRead) as BlockTableRecord;
                if (btr == null || btr.IsLayout) continue;
                if (!names.Contains(btr.Name)) names.Add(btr.Name);
            }

            names.Sort();
            return names;
        }

        /// <summary>
        /// 2D-блок, отвечающий за вид спереди (фасад) — разумное умолчание для
        /// вида профиля. null, если такого назначения в определении нет.
        /// </summary>
        public static string GetFrontViewBlockName(
            Database db, Transaction tr, string mvDefName)
        {
            MultiViewBlockDefinition mvDef = TryGetMvBlockDef(db, tr, mvDefName);
            if (mvDef == null) return null;

            foreach (MultiViewBlockDisplayRepresentationDefinition dispRep
                     in mvDef.DisplayRepresentationDefinitions)
            {
                if (dispRep == null) continue;

                foreach (MultiViewBlockViewDefinition viewDef in dispRep.ViewDefinitions)
                {
                    if (viewDef == null) continue;
                    if (!viewDef.IsViewOn(ViewDirection.Front)) continue;

                    ObjectId id = viewDef.BlockId;
                    if (id.IsNull || id.IsErased) continue;

                    var btr = tr.GetObject(id, OpenMode.ForRead) as BlockTableRecord;
                    if (btr != null) return btr.Name;
                }
            }

            return null;
        }

        /// <summary>Определение MVBlock по имени или null (в отличие от ResolveMvBlockDef).</summary>
        private static MultiViewBlockDefinition TryGetMvBlockDef(
            Database db, Transaction tr, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            try
            {
                var dic = new DictionaryMultiViewBlockDefinition(db);
                if (!dic.Has(name, tr)) return null;

                return tr.GetObject(dic.GetAt(name), OpenMode.ForRead)
                       as MultiViewBlockDefinition;
            }
            catch (System.Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Обычный блок AutoCAD по имени. Бросает, если его нет: молча рисовать
        /// не то, что просили, хуже внятной ошибки.
        /// </summary>
        public static ObjectId ResolveBlock2d(Database db, Transaction tr, string name)
        {
            if (!string.IsNullOrEmpty(name))
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                if (bt.Has(name)) return bt[name];
            }

            throw new Autodesk.AutoCAD.Runtime.Exception(
                Autodesk.AutoCAD.Runtime.ErrorStatus.KeyNotFound,
                "Блок '" + name + "' не найден в чертеже");
        }

        /// <summary>Имена всех MVBlock-определений чертежа (для выбора пользователем).</summary>
        public static List<string> GetMvBlockNames(Database db, Transaction tr)
        {
            var names = new List<string>();
            var mvDic = new DictionaryMultiViewBlockDefinition(db);

            foreach (ObjectId id in mvDic.Records)
            {
                var def = tr.GetObject(id, OpenMode.ForRead) as MultiViewBlockDefinition;
                if (def != null) names.Add(def.Name);
            }

            names.Sort();
            return names;
        }
    }
}
