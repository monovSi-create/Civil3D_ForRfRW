using System;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.DatabaseServices;
using Civil3D_commands.Shared;
using AcAp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace Civil3D_commands.AssociativeBreaks
{
    public partial class BreakCommands
    {
        // Прежний ввод высоты. Живёт, пока открыт Civil 3D: смены отметки обычно
        // идут подряд с одним и тем же шагом, и набирать его каждый раз незачем.
        private static double _lastBottomStep;
        private static bool _hasLastBottomStep;

        // ------------------------------------------------------------------
        //  СМЕНА ОТМЕТКИ: коридор режется на два профиля и две базовые линии
        //
        //  Ступень (RW_CREATEBREAK) ломает один профиль парой PVI. Здесь
        //  профиль РЕЖЕТСЯ: левый кончается на пикете, правый начинается на нём
        //  же, на выбранную высоту выше или ниже, и продолжается до конца
        //  коридора. Коридор получает вторую базовую линию, а в месте разреза
        //  встаёт прокси — тот же, что у разрыва, он держит обе части.
        //  Подробности и ограничения — раздел «Смена отметки» в README модуля.
        // ------------------------------------------------------------------
        [CommandMethod("RW_BOTTOMSTEP")]
        public void BottomStep()
        {
            var doc = AcAp.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            var db = doc.Database;
            var session = BreakSession.Current;
            var active = session?.ActiveLink;

            if (active == null)
            {
                ed.WriteMessage(session != null && session.Links.Count > 1
                    ? "\nСвязей в чертеже несколько — выберите нужную: RW_EDITMODE"
                    : "\nСначала RW_LINKPROFILECORRIDOR");
                return;
            }

            ed.WriteMessage($"\nСвязь: «{active.Label}».");

            // 1) Пикет: щелчок в виде профиля или в плане — как у RW_CREATEBREAK.
            var ptRes = ed.GetPoint("\nУкажите пикет смены отметки (в виде профиля или в плане)");
            if (ptRes.Status != PromptStatus.OK) return;

            double station = double.NaN;
            BreakLink target = null;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var pv = RwHandles.Open<ProfileView>(tr, db, active.ProfileViewHandle, OpenMode.ForRead);
                var al = RwHandles.Open<Alignment>(tr, db, active.AlignmentHandle, OpenMode.ForRead);

                bool ok = (pv != null && RwGeometry.TryStationInProfileView(pv, ptRes.Value, out station))
                       || (al != null && RwGeometry.TryStationOnAlignment(al, ptRes.Value, out station));

                if (!ok)
                {
                    tr.Commit();
                    ed.WriteMessage(
                        "\nТочка не попала ни в вид профиля, ни на трассу — смена отметки не создана." +
                        "\nЩёлкайте внутри сетки вида профиля либо рядом с осью в плане.");
                    return;
                }

                // Пикет мог попасть на правую часть уже разделённого коридора:
                // у неё свой профиль и своя связь. Берётся та связь этого же
                // коридора, чей профиль накрывает пикет.
                foreach (BreakLink l in session.Links.Where(x =>
                             x.CorridorHandle == active.CorridorHandle &&
                             x.AlignmentHandle == active.AlignmentHandle))
                {
                    var p = RwHandles.Open<Profile>(tr, db, l.ProfileHandle, OpenMode.ForRead);
                    if (p == null) continue;
                    if (station < p.StartingStation || station > p.EndingStation) continue;

                    target = l;
                    break;
                }

                tr.Commit();
            }

            if (target == null)
            {
                ed.WriteMessage(
                    $"\nПикет {station:F3} не попадает ни в один профиль этого коридора — смена отметки не создана.");
                return;
            }

            ed.WriteMessage(target == active
                ? $"\nПикет {station:F3}."
                : $"\nПикет {station:F3} лежит на связи «{target.Label}».");

            // 2) Высота. Знак: + поднимает, − опускает отметку правой части.
            //    Прежнее значение предлагается по умолчанию.
            var hOpt = new PromptDoubleOptions(
                "\nНа какую высоту сместить отметку правее пикета (+ поднять, - опустить)")
            {
                AllowNegative = true,
                AllowZero = false
            };

            if (_hasLastBottomStep)
            {
                hOpt.DefaultValue = _lastBottomStep;
                hOpt.UseDefaultValue = true;
            }

            var hRes = ed.GetDouble(hOpt);
            if (hRes.Status != PromptStatus.OK) return;

            // Запоминаем сразу: если построение откажет, повторный запуск
            // предложит то же число, а не заставит набирать его заново.
            _lastBottomStep = hRes.Value;
            _hasLastBottomStep = true;

            // 3) Само разделение.
            SplitResult result;
            try
            {
                result = session.Manager.CreateSplit(target, station, hRes.Value);
            }
            catch (System.Exception ex)
            {
                // Строка в командной строке лучше окна «необработанное исключение»;
                // транзакция откатилась, профиль и коридор не тронуты.
                ed.WriteMessage($"\nСмену отметки создать не удалось: {ex.Message}" +
                                $"\nЖурнал операции: {BreakTrace.Path}");
                return;
            }

            ed.WriteMessage(
                $"\nСмена отметки на пикете {station:F3}, сдвиг {hRes.Value:+0.###;-0.###}." +
                $"\nЛевая часть: «{result.LeftLink.Label}». Правая часть: «{result.RightLink.Label}»." +
                "\nПеренести границу: ручка прокси или RW_EDITMODE, ветка «Перенести».");

            foreach (string note in result.Notes)
                ed.WriteMessage("\nВнимание: " + note);

            ed.Regen();
        }
    }
}
