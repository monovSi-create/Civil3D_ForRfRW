using System;
using Autodesk.AutoCAD.DatabaseServices;
using Civil3D_commands.Shared;

namespace Civil3D_commands.AssociativeBreaks
{
    /// <summary>
    /// Логический маркер "разрыва" коридора.
    /// Это ЕДИНСТВЕННЫЙ источник истины. Геометрия профиля, области коридора и
    /// два прокси-объекта (в виде профиля и в плане) — лишь представления этого маркера.
    ///
    /// Разрыв коридора может быть:
    ///   - "ступенью" (IsStep = true)  -> двигает и границу области, и пару PVI профиля;
    ///   - простой рубкой (IsStep = false) на ровном участке -> двигает только границу области.
    /// </summary>
    public class StationMarker
    {
        /// <summary>Что лежит в записи: сам маркер или активная связь.</summary>
        public enum RecordKind { Marker = 0, Link = 1 }

        // Тег и версия формата записи. До их появления запись начиналась сразу
        // с Guid, и отличить свою запись от чужой было нельзя вообще: чтение
        // падало на первом же поле, а исключение гасилось вызывающим кодом —
        // маркер молча исчезал. Прежние записи читаются позиционным путём.
        private const string Tag = "RWBREAK";
        private const int FormatVersion = 1;

        /// <summary>
        /// Вид записи. LinkStore хранит связь этим же классом (заполнены только
        /// четыре хэндла), и по содержимому одно от другого не отличается —
        /// поэтому вид пишется в запись явно.
        /// </summary>
        public RecordKind Kind { get; set; } = RecordKind.Marker;

        /// <summary>Постоянный идентификатор маркера (переживает save/open).</summary>
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>Пикет (станция вдоль оси/профиля), на котором стоит разрыв.</summary>
        public double Station { get; set; }

        /// <summary>true — это ступень профиля; false — рубка на ровном участке.</summary>
        public bool IsStep { get; set; }

        /// <summary>
        /// Высота ступени со знаком. Знак определяет направление скачка ПО ХОДУ пикетажа:
        /// положительное — вверх, отрицательное — вниз. Для не-ступени = 0.
        /// </summary>
        public double StepHeight { get; set; }

        /// <summary>Базовая отметка ровного участка перед ступенью (для размещения прокси по вертикали).</summary>
        public double BaseElevation { get; set; }

        /// <summary>Слой прокси-объектов.</summary>
        public string Layer { get; set; } = "0";

        // --- Связи с объектами чертежа. Храним Handle, а не ObjectId: Handle стабилен между сессиями. ---
        public Handle ProfileHandle { get; set; }      // Profile (продольный профиль-основание)
        public Handle ProfileViewHandle { get; set; }  // ProfileView
        public Handle AlignmentHandle { get; set; }    // Alignment (ось — для плана)
        public Handle CorridorHandle { get; set; }     // Corridor
        public Handle ProfileProxyHandle { get; set; } // Line в виде профиля
        public Handle PlanProxyHandle { get; set; }    // Line в плане (ортогональ оси)

        // --- Пара областей коридора, которые разводит этот разрыв. ---
        // RegionGUID стабилен: переживает и перестановку границ, и Split соседей.
        // Пусто — привязка ещё не установлена (новый маркер, чертёж от прежней версии);
        // тогда ProfileGeometryOps восстанавливает её по геометрии.
        public Guid LeftRegionId { get; set; } = Guid.Empty;
        public Guid RightRegionId { get; set; } = Guid.Empty;

        /// <summary>Сериализация в ResultBuffer для хранения в Xrecord словаря чертежа.</summary>
        public ResultBuffer ToResultBuffer()
        {
            return ToResultBuffer(Kind);
        }

        /// <summary>То же, но вид записи задаётся явно (нужно LinkStore).</summary>
        public ResultBuffer ToResultBuffer(RecordKind kind)
        {
            return new ResultBuffer(
                new TypedValue((int)DxfCode.Text, Tag),
                new TypedValue((int)DxfCode.Int32, FormatVersion),
                new TypedValue((int)DxfCode.Int16, (short)kind),
                new TypedValue((int)DxfCode.Text, Id.ToString("N")),
                new TypedValue((int)DxfCode.Real, Station),
                new TypedValue((int)DxfCode.Int16, IsStep ? (short)1 : (short)0),
                new TypedValue((int)DxfCode.Real, StepHeight),
                new TypedValue((int)DxfCode.Real, BaseElevation),
                new TypedValue((int)DxfCode.Text, Layer ?? "0"),
                new TypedValue((int)DxfCode.Text, RwHandles.ToText(ProfileHandle)),
                new TypedValue((int)DxfCode.Text, RwHandles.ToText(ProfileViewHandle)),
                new TypedValue((int)DxfCode.Text, RwHandles.ToText(AlignmentHandle)),
                new TypedValue((int)DxfCode.Text, RwHandles.ToText(CorridorHandle)),
                new TypedValue((int)DxfCode.Text, RwHandles.ToText(ProfileProxyHandle)),
                new TypedValue((int)DxfCode.Text, RwHandles.ToText(PlanProxyHandle)),
                new TypedValue((int)DxfCode.Text, LeftRegionId.ToString("N")),
                new TypedValue((int)DxfCode.Text, RightRegionId.ToString("N")));
        }

        /// <summary>
        /// Чтение терпимо и к формату, и к длине: записи без тега (прежний формат)
        /// читаются позиционно, записи без привязки к областям — тоже, привязка
        /// остаётся пустой и восстанавливается по геометрии.
        ///
        /// Бросает, если запись не наша или сделана более новой версией плагина:
        /// молча принять такую за пустую — значит потерять её при следующей записи.
        /// </summary>
        public static StationMarker FromResultBuffer(ResultBuffer rb)
        {
            TypedValue[] v = rb.AsArray();
            if (v.Length == 0)
                throw new InvalidOperationException("Пустая запись разрыва.");

            var m = new StationMarker();
            int i = 0;

            bool tagged = v[0].TypeCode == (int)DxfCode.Text &&
                          string.Equals(v[0].Value.ToString(), Tag, StringComparison.Ordinal);

            if (tagged)
            {
                i = 1;
                int format = Convert.ToInt32(v[i++].Value);
                if (format < 1 || format > FormatVersion)
                    throw new InvalidOperationException(
                        "Запись разрыва сделана более новой версией плагина (формат " + format + ").");

                m.Kind = Convert.ToInt16(v[i++].Value) == (short)RecordKind.Link
                    ? RecordKind.Link
                    : RecordKind.Marker;
            }
            else
            {
                // Прежний формат начинался с Guid маркера. Всё остальное — чужая запись.
                Guid probe;
                if (v[0].TypeCode != (int)DxfCode.Text ||
                    !Guid.TryParseExact(v[0].Value.ToString(), "N", out probe))
                    throw new InvalidOperationException("Чужая запись в словаре разрывов.");
            }

            m.Id = Guid.ParseExact(v[i++].Value.ToString(), "N");
            m.Station = Convert.ToDouble(v[i++].Value);
            m.IsStep = Convert.ToInt16(v[i++].Value) != 0;
            m.StepHeight = Convert.ToDouble(v[i++].Value);
            m.BaseElevation = Convert.ToDouble(v[i++].Value);
            m.Layer = v[i++].Value.ToString();
            m.ProfileHandle = RwHandles.Parse(v[i++].Value.ToString());
            m.ProfileViewHandle = RwHandles.Parse(v[i++].Value.ToString());
            m.AlignmentHandle = RwHandles.Parse(v[i++].Value.ToString());
            m.CorridorHandle = RwHandles.Parse(v[i++].Value.ToString());
            m.ProfileProxyHandle = RwHandles.Parse(v[i++].Value.ToString());
            m.PlanProxyHandle = RwHandles.Parse(v[i++].Value.ToString());

            if (v.Length > i + 1)
            {
                m.LeftRegionId = ParseGuid(v[i++].Value.ToString());
                m.RightRegionId = ParseGuid(v[i++].Value.ToString());
            }

            return m;
        }

        private static Guid ParseGuid(string s) =>
            Guid.TryParseExact(s, "N", out Guid g) ? g : Guid.Empty;

        /// <summary>Помогает реактору отличить, какой из двух прокси был задет.</summary>
        public bool OwnsProxy(Handle h) => h == ProfileProxyHandle || h == PlanProxyHandle;
    }
}
