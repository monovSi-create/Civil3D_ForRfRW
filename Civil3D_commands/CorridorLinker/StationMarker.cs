using System;
using Autodesk.AutoCAD.DatabaseServices;

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

        /// <summary>Сериализация в ResultBuffer для хранения в Xrecord словаря чертежа.</summary>
        public ResultBuffer ToResultBuffer()
        {
            return new ResultBuffer(
                new TypedValue((int)DxfCode.Text, Id.ToString("N")),
                new TypedValue((int)DxfCode.Real, Station),
                new TypedValue((int)DxfCode.Int16, IsStep ? (short)1 : (short)0),
                new TypedValue((int)DxfCode.Real, StepHeight),
                new TypedValue((int)DxfCode.Real, BaseElevation),
                new TypedValue((int)DxfCode.Text, Layer ?? "0"),
                new TypedValue((int)DxfCode.Text, ProfileHandle.ToString()),
                new TypedValue((int)DxfCode.Text, ProfileViewHandle.ToString()),
                new TypedValue((int)DxfCode.Text, AlignmentHandle.ToString()),
                new TypedValue((int)DxfCode.Text, CorridorHandle.ToString()),
                new TypedValue((int)DxfCode.Text, ProfileProxyHandle.ToString()),
                new TypedValue((int)DxfCode.Text, PlanProxyHandle.ToString()));
        }

        public static StationMarker FromResultBuffer(ResultBuffer rb)
        {
            var v = rb.AsArray();
            var m = new StationMarker
            {
                Id = Guid.ParseExact(v[0].Value.ToString(), "N"),
                Station = (double)v[1].Value,
                IsStep = Convert.ToInt16(v[2].Value) != 0,
                StepHeight = (double)v[3].Value,
                BaseElevation = (double)v[4].Value,
                Layer = v[5].Value.ToString(),
                ProfileHandle = ParseHandle(v[6].Value.ToString()),
                ProfileViewHandle = ParseHandle(v[7].Value.ToString()),
                AlignmentHandle = ParseHandle(v[8].Value.ToString()),
                CorridorHandle = ParseHandle(v[9].Value.ToString()),
                ProfileProxyHandle = ParseHandle(v[10].Value.ToString()),
                PlanProxyHandle = ParseHandle(v[11].Value.ToString())
            };
            return m;
        }

        private static Handle ParseHandle(string s)
        {
            if (string.IsNullOrEmpty(s)) return new Handle(0);
            return new Handle(Convert.ToInt64(s, 16));
        }

        /// <summary>Помогает реактору отличить, какой из двух прокси был задет.</summary>
        public bool OwnsProxy(Handle h) => h == ProfileProxyHandle || h == PlanProxyHandle;
    }
}
