using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
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
        //  СМЕНА ОТМЕТКИ: профиль и коридор режутся на две части
        //
        //  Меню: «Создать» / «Изменить» (→ «Высота» / «Положение») / «Удалить» —
        //  по образцу RW_CREATEBREAK и ветки «Разрыв» в RW_EDITMODE.
        //
        //  Вид и профиль не спрашиваются: профиль, служащий базовой линией коридора,
        //  известен из связи (RW_LINKPROFILECORRIDOR). После первой смены отметки у
        //  коридора несколько связей — по одной на каждую базовую линию; пикет
        //  определяет, на какой из них делить, так что операцию можно повторять
        //  внутри новых базовых линий.
        // ------------------------------------------------------------------
        [CommandMethod("RW_BOTTOMSTEP")]
        public void BottomStep()
        {
            var doc = AcAp.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            var session = BreakSession.Current;

            if (session == null) { ed.WriteMessage("\nСессия модуля не создана."); return; }
            BottomStepMenu(doc, session);
        }

        /// <summary>
        /// Меню «Создать / Изменить / Удалить» смены отметки. Вызывается и командой
        /// RW_BOTTOMSTEP, и веткой «Отметка» редактора связи (RW_EDITMODE) — это
        /// один и тот же функционал.
        /// </summary>
        internal static void BottomStepMenu(Document doc, BreakSession session)
        {
            var ed = doc.Editor;

            if (session.Links.Count == 0)
            {
                ed.WriteMessage("\nСначала RW_LINKPROFILECORRIDOR — нужна связь коридора с видом профиля.");
                return;
            }

            // Ключевые слова латиницей (глобальное имя) с русскими подписями; первые
            // буквы у слов разные — иначе AutoCAD не разберёт, что набрано.
            var kw = new PromptKeywordOptions("\nСмена отметки коридора");
            kw.Keywords.Add("Create", "Создать", "Создать");
            kw.Keywords.Add("Edit", "Изменить", "Изменить");
            kw.Keywords.Add("Delete", "Удалить", "Удалить");
            kw.Keywords.Default = "Create";   // глобальное имя, не подпись
            kw.AppendKeywordsToMessage = true;

            PromptResult res = ed.GetKeywords(kw);
            if (res.Status != PromptStatus.OK) return;

            switch (res.StringResult)
            {
                case "Create": BottomStepCreate(doc, session); break;
                case "Edit": BottomStepEdit(doc, session); break;
                case "Delete": BottomStepDelete(doc, session); break;
            }
        }

        // ------------------------------------------------------------------
        //  СОЗДАТЬ
        // ------------------------------------------------------------------
        private static void BottomStepCreate(Document doc, BreakSession session)
        {
            var ed = doc.Editor;
            var db = doc.Database;

            BreakLink active = session.ActiveLink;
            if (active == null)
            {
                ed.WriteMessage("\nСвязей в чертеже несколько — выберите нужную: RW_EDITMODE");
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

                // Связь этого же коридора, чей профиль накрывает пикет. Именно по ней
                // и делим: после смены отметки у коридора несколько базовых линий.
                target = LinkAtStation(tr, db, session, active, station);

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

            // 2) Высота. Знак: + поднимает, − опускает отметку после пикета.
            var hOpt = new PromptDoubleOptions(
                "\nНа какую высоту сместить отметку после пикета (+ поднять, - опустить)")
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
            // предложит то же число.
            _lastBottomStep = hRes.Value;
            _hasLastBottomStep = true;

            // 3) Деление профиля и коридора.
            var notes = new List<string>();
            ObjectId rightId;
            string corridorInfo;
            try
            {
                rightId = session.Manager.SplitProfileOnly(
                    Resolve(db, target.ProfileHandle), Resolve(db, target.ProfileViewHandle),
                    station, hRes.Value, notes, out corridorInfo);
            }
            catch (System.Exception ex)
            {
                // Транзакция откатилась: профиль и коридор не тронуты.
                ed.WriteMessage($"\nСмену отметки создать не удалось: {ex.Message}" +
                                $"\nЖурнал операции: {BreakTrace.Path}");
                return;
            }

            string rightName = "?";
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var right = tr.GetObject(rightId, OpenMode.ForRead) as Profile;
                if (right != null) rightName = right.Name;
                tr.Commit();
            }

            ed.WriteMessage(
                $"\nПрофиль «{target.ProfileName}» заканчивается на пикете {station:F3}." +
                $"\nНовый профиль «{rightName}» начинается на нём же, сдвиг {hRes.Value:+0.###;-0.###}." +
                "\nНа пикете стоит прокси со стрелками: щёлкните по стрелке и ведите границу мышью." +
                " Изменить и удалить — снова RW_BOTTOMSTEP.");

            ed.WriteMessage(corridorInfo != null ? "\n" + corridorInfo : "\nКоридор не затронут.");

            foreach (string note in notes)
                ed.WriteMessage("\nВнимание: " + note);

            ed.Regen();
        }

        // ------------------------------------------------------------------
        //  ИЗМЕНИТЬ
        // ------------------------------------------------------------------
        private static void BottomStepEdit(Document doc, BreakSession session)
        {
            var ed = doc.Editor;

            // Прокси лежат на заблокированном слое: на время выбора защита снимается.
            using (new BoundaryUnlock(doc, session))
            {
                StationMarker m = PickSplitMarker(doc, session);
                if (m == null) return;

                var kw = new PromptKeywordOptions(
                    $"\nСмена отметки на пикете {m.Station:F3}, сдвиг {m.StepHeight:+0.###;-0.###}. Что изменить?");
                kw.Keywords.Add("Height", "Высоту", "Высоту");
                kw.Keywords.Add("Position", "Положение", "Положение");
                kw.Keywords.Default = "Position";
                kw.AppendKeywordsToMessage = true;

                PromptResult res = ed.GetKeywords(kw);
                if (res.Status != PromptStatus.OK) return;

                if (res.StringResult == "Position")
                {
                    // Тот же jig, что и по щелчку по стрелке.
                    MoveMarkerCore(doc, session, m);
                    return;
                }

                var hOpt = new PromptDoubleOptions(
                    "\nНовая высота смены отметки (+ вверх, - вниз)")
                {
                    AllowNegative = true,
                    AllowZero = false,
                    DefaultValue = m.StepHeight,
                    UseDefaultValue = true
                };
                var hRes = ed.GetDouble(hOpt);
                if (hRes.Status != PromptStatus.OK) return;

                _lastBottomStep = hRes.Value;
                _hasLastBottomStep = true;

                var notes = new List<string>();
                try
                {
                    session.Manager.ChangeSplitHeight(m, hRes.Value, notes);
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage($"\nВысоту изменить не удалось: {ex.Message}" +
                                    $"\nЖурнал операции: {BreakTrace.Path}");
                    return;
                }

                ed.WriteMessage(
                    $"\nВысота смены отметки на пикете {m.Station:F3} теперь {hRes.Value:+0.###;-0.###}." +
                    " Правый профиль и всё правее него сдвинуты, коридор перестроен.");
                foreach (string note in notes) ed.WriteMessage("\nВнимание: " + note);
                ed.Regen();
            }
        }

        // ------------------------------------------------------------------
        //  УДАЛИТЬ
        // ------------------------------------------------------------------
        private static void BottomStepDelete(Document doc, BreakSession session)
        {
            var ed = doc.Editor;

            using (new BoundaryUnlock(doc, session))
            {
                StationMarker m = PickSplitMarker(doc, session);
                if (m == null) return;

                DeleteSplitInteractive(doc, session, m);
            }
        }

        /// <summary>
        /// Правка смены отметки в ветке «Разрыв» редактора связи — то же, что у
        /// обычного разрыва: свойства (вместо ступени — высота) и конструкции
        /// участков слева и справа. Участки лежат в разных базовых линиях, поэтому
        /// каждый правится через свою связь.
        /// </summary>
        internal static void EditSplitCore(Document doc, BreakSession session, StationMarker m)
        {
            var ed = doc.Editor;
            var db = doc.Database;

            ed.WriteMessage(
                $"\nСмена отметки на пикете {m.Station:F3}: сдвиг {m.StepHeight:+0.###;-0.###;0}.");

            // 1) Высота. Enter оставляет прежнюю.
            var hOpt = new PromptDoubleOptions(
                "\nВысота смены отметки (+ вверх, - вниз)")
            {
                AllowNegative = true,
                AllowZero = false,
                DefaultValue = m.StepHeight,
                UseDefaultValue = true
            };
            var hRes = ed.GetDouble(hOpt);
            if (hRes.Status != PromptStatus.OK) return;
            double newHeight = hRes.Value;

            // 2) Конструкции участков по обе стороны границы.
            string leftName = "—", rightName = "—", leftAsmName = null, rightAsmName = null;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var corridor = RwHandles.Open<Corridor>(tr, db, m.CorridorHandle, OpenMode.ForRead);
                Baseline bl = session.GetBaselineByProfile(tr, corridor, m.ProfileHandle);
                Baseline br = session.GetBaselineByProfile(tr, corridor, m.NextProfileHandle);

                if (bl != null)
                {
                    foreach (BaselineRegion r in bl.BaselineRegions)
                        if (r.RegionGUID == m.LeftRegionId) { leftName = r.Name; break; }
                    leftAsmName = AssemblyNameOf(tr, bl, m.LeftRegionId);
                }

                if (br != null)
                {
                    foreach (BaselineRegion r in br.BaselineRegions)
                        if (r.RegionGUID == m.RightRegionId) { rightName = r.Name; break; }
                    rightAsmName = AssemblyNameOf(tr, br, m.RightRegionId);
                }

                tr.Commit();
            }

            bool hasCorridor = m.CorridorHandle.Value != 0;
            ObjectId leftAsm = ObjectId.Null, rightAsm = ObjectId.Null;

            if (hasCorridor)
            {
                leftAsm = AskAssembly(doc, $"предыдущего участка «{leftName}»", leftAsmName);
                rightAsm = AskAssembly(doc, $"следующего участка «{rightName}»", rightAsmName);
            }

            // Применяется по очереди; каждый шаг своей транзакцией, как у остальных веток.
            try
            {
                if (Math.Abs(newHeight - m.StepHeight) > 1e-9)
                    session.Manager.ChangeSplitHeight(m, newHeight, new List<string>());

                if (!leftAsm.IsNull)
                    session.Manager.SetRegionAssembly(
                        session.FindLinkByProfile(m.ProfileHandle, m.CorridorHandle),
                        m.LeftRegionId, leftAsm);

                if (!rightAsm.IsNull)
                    session.Manager.SetRegionAssembly(
                        session.FindLinkByProfile(m.NextProfileHandle, m.CorridorHandle),
                        m.RightRegionId, rightAsm);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nИзменить смену отметки не удалось: {ex.Message}" +
                                $"\nЖурнал операции: {BreakTrace.Path}");
                return;
            }

            ed.WriteMessage("\nСвойства смены отметки изменены, коридор перестроен.");
            ed.Regen();
        }

        /// <summary>
        /// Связь того же коридора и той же оси, чей профиль накрывает пикет.
        /// После смены отметки у коридора несколько базовых линий — по связи на
        /// каждую, и разрыв надо создавать именно на той, где лежит пикет.
        /// Общая для RW_BOTTOMSTEP и RW_CREATEBREAK. null — пикет вне всех профилей.
        /// </summary>
        internal static BreakLink LinkAtStation(Transaction tr, Database db, BreakSession session,
                                                BreakLink active, double station)
        {
            foreach (BreakLink l in session.Links.Where(x =>
                         x.CorridorHandle == active.CorridorHandle &&
                         x.AlignmentHandle == active.AlignmentHandle))
            {
                var p = RwHandles.Open<Profile>(tr, db, l.ProfileHandle, OpenMode.ForRead);
                if (p == null) continue;
                if (station < p.StartingStation || station > p.EndingStation) continue;

                return l;
            }

            return null;
        }

        /// <summary>
        /// Подтвердить и удалить смену отметки. Общая для пункта «Удалить» команды
        /// RW_BOTTOMSTEP и для RW_DELETEBREAK, наведённой на её прокси: прокси
        /// выполняют одну и ту же роль, как бы их ни вызвали.
        /// </summary>
        internal static void DeleteSplitInteractive(Document doc, BreakSession session, StationMarker m)
        {
            var ed = doc.Editor;

            var kw = new PromptKeywordOptions(
                $"\nУдалить смену отметки на пикете {m.Station:F3}? Профили и базовые линии коридора " +
                "будут склеены обратно, правый профиль и его связь стёрты.");
            kw.Keywords.Add("Yes", "Да", "Да");
            kw.Keywords.Add("No", "Нет", "Нет");
            kw.Keywords.Default = "No";   // необратимо, поэтому «по Enter» — отказ
            PromptResult res = ed.GetKeywords(kw);
            if (res.Status != PromptStatus.OK || res.StringResult != "Yes") return;

            var notes = new List<string>();
            try
            {
                session.Manager.DeleteSplit(m, notes);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nУдалить смену отметки не удалось: {ex.Message}" +
                                $"\nЖурнал операции: {BreakTrace.Path}");
                return;
            }

            ed.WriteMessage("\nСмена отметки удалена: профили и базовые линии склеены.");
            foreach (string note in notes) ed.WriteMessage("\nВнимание: " + note);
            ed.Regen();
        }

        /// <summary>
        /// Выбрать прокси смены отметки. Обычный разрыв и концевая граница не
        /// подходят: у них свои команды.
        /// </summary>
        private static StationMarker PickSplitMarker(Document doc, BreakSession session)
        {
            var ed = doc.Editor;

            var opt = new PromptEntityOptions("\nВыберите прокси смены отметки");
            opt.SetRejectMessage("\nНужен прокси смены отметки");
            opt.AddAllowedClass(typeof(Line), false);
            var res = ed.GetEntity(opt);
            if (res.Status != PromptStatus.OK) return null;

            StationMarker m = session.Store.GetByProxy(res.ObjectId.Handle);
            if (m == null)
            {
                ed.WriteMessage("\nЭта линия в модели не числится — не прокси или его копия.");
                return null;
            }

            if (!m.IsSplit)
            {
                ed.WriteMessage("\nЭто прокси обычного разрыва или конца коридора — для них RW_EDITMODE и RW_DELETEBREAK.");
                return null;
            }

            return m;
        }
    }
}
