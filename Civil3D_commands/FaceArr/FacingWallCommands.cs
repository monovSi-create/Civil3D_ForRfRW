using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.DatabaseServices;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
// Entity и ObjectId есть и в AutoCAD-, и в Civil-пространстве имён — снимаем неоднозначность.
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;
using ObjectId = Autodesk.AutoCAD.DatabaseServices.ObjectId;

[assembly: CommandClass(typeof(Civil3D_commands.FaceArr.FacingWallCommands))]

namespace Civil3D_commands.FaceArr
{
    /// <summary>
    /// Только команды AutoCAD и диалог с пользователем.
    /// Генерация массива, MVBlock, грипсы и хранение — не здесь.
    /// </summary>
    public class FacingWallCommands
    {
        // =================================================================
        //  СОЗДАНИЕ
        // =================================================================
        [CommandMethod("FACINGWALLCREATE")]
        public static void CreateFacingWall()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            Editor ed = doc.Editor;

            FacingWallGrips.Enable();

            // 1. Вид профиля. Трасса определяется через него, отдельно не спрашиваем.
            var pvOpts = new PromptEntityOptions("\nВыберите вид профиля: ");
            pvOpts.SetRejectMessage("\nНужен вид профиля (ProfileView).");
            pvOpts.AddAllowedClass(typeof(ProfileView), true);

            PromptEntityResult pvRes = ed.GetEntity(pvOpts);
            if (pvRes.Status != PromptStatus.OK) return;

            ObjectId profileViewId = pvRes.ObjectId;
            ObjectId alignmentId;
            double stationStart, stationEnd;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var pv = (ProfileView)tr.GetObject(profileViewId, OpenMode.ForRead);
                alignmentId = pv.AlignmentId;

                var alignment = tr.GetObject(alignmentId, OpenMode.ForRead) as Alignment;
                if (alignment == null)
                {
                    ed.WriteMessage("\nУ вида профиля не определена трасса.");
                    return;
                }

                stationStart = Math.Max(pv.StationStart, alignment.StartingStation);
                stationEnd = Math.Min(pv.StationEnd, alignment.EndingStation);

                tr.Commit();
            }

            if (stationEnd - stationStart <= 0.0)
            {
                ed.WriteMessage("\nДиапазон вида профиля не пересекается с трассой.");
                return;
            }

            // 2. Отметка низа стены: вручную или снятая с профиля.
            //    Связь с профилем после этого не сохраняется — только число.
            double baseElevation;
            if (!AskBaseElevation(ed, db, stationStart, stationEnd, out baseElevation)) return;

            // 3. Многовидовой блок.
            List<string> mvNames;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                mvNames = FacingWallBuilder.GetMvBlockNames(db, tr);
                tr.Commit();
            }

            if (mvNames.Count == 0)
            {
                ed.WriteMessage("\nВ чертеже нет определений многовидовых блоков.");
                return;
            }

            string mvName = SelectMvBlockUI(mvNames);
            if (mvName == null) { ed.WriteMessage("\nОтменено."); return; }

            // 3a. Половинчатый блок. Необязателен: без него перевязка нечётных
            //     рядов остаётся пустой, как было раньше.
            string halfName = SelectFromListUI(
                "Половинчатый блок (Отмена — без половинок)", mvNames, null);

            if (halfName == null)
                ed.WriteMessage("\nПоловинчатый блок не задан — края рядов будут ступенчатыми.");

            // 4. Параметры массива.
            double blockWidth = AskDouble(ed, "Ширина блока вдоль трассы", 0.405);
            double blockHeight = AskDouble(ed, "Высота блока (шаг ряда)", 0.2);
            int rowCount = AskInt(ed, "Количество рядов", 5);
            double faceOffset = AskDouble(ed, "Смещение от оси к грани", 0.0, true);
            double scale = AskDouble(ed, "Масштаб блока", 1.0);

            if (blockWidth <= 0.0 || blockHeight <= 0.0 || rowCount <= 0)
            {
                ed.WriteMessage("\nНедопустимые параметры массива.");
                return;
            }

            var def = new FacingWallDefinition
            {
                AlignmentId = alignmentId,
                ProfileViewId = profileViewId,
                MvBlockDefName = mvName,
                HalfMvBlockDefName = halfName,
                FaceOffset = faceOffset,
                BaseElevation = baseElevation,
                BlockWidth = blockWidth,
                BlockHeight = blockHeight,
                Scale = scale,

                // Область раскладки: якорь и дальняя граница. Дальше их двигают
                // перпендикулярными маркерами в плане.
                LayoutStartStation = stationStart,
                LayoutEndStation = stationEnd
            };

            for (int i = 0; i < rowCount; i++)
                def.Rows.Add(new FacingWallRowDefinition { RowIndex = i });

            // Изначально все ряды на всю область; дальше их уточняют в профиле.
            def.ResetRowsToLayout();

            try
            {
                using (doc.LockDocument())
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    Point3d position = ControllerPosition(tr, profileViewId);

                    ObjectId controllerId =
                        FacingWallController.CreateArray(db, tr, def, position);

                    tr.Commit();

                    int total = 0;
                    foreach (FacingWallRowDefinition row in def.Rows)
                        total += row.BlockIds.Count;

                    ed.WriteMessage(
                        "\nМассив создан. Рядов: {0}, блоков: {1}. Контроллер: {2}",
                        def.RowCount, total, controllerId.Handle);
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nОшибка построения: " + ex.Message);
                return;
            }

            ed.Regen();
        }

        // =================================================================
        //  ПЕРЕСТРОЕНИЕ ОДНОГО РЯДА
        // =================================================================
        [CommandMethod("FACINGWALLREBUILD")]
        public static void RebuildRow()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Editor ed = doc.Editor;
            FacingWallGrips.Enable();

            ObjectId controllerId = SelectController(doc);
            if (controllerId.IsNull) return;

            int rowCount;
            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                FacingWallDefinition def = FacingWallController.Load(controllerId, tr);
                rowCount = def != null ? def.RowCount : 0;
                tr.Commit();
            }

            if (rowCount == 0)
            {
                ed.WriteMessage("\nВ контроллере нет рядов.");
                return;
            }

            var opts = new PromptIntegerOptions(
                "\nНомер ряда (0.." + (rowCount - 1) + "): ")
            {
                LowerLimit = 0,
                UpperLimit = rowCount - 1,
                DefaultValue = 0,
                AllowNone = false
            };

            PromptIntegerResult res = ed.GetInteger(opts);
            if (res.Status != PromptStatus.OK) return;

            try
            {
                FacingWallController.RebuildRow(controllerId, res.Value);
                ed.WriteMessage("\nРяд {0} перестроен.", res.Value);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nОшибка перестроения: " + ex.Message);
            }

            ed.Regen();
        }

        // =================================================================
        //  ПОЛНОЕ ПЕРЕСТРОЕНИЕ
        // =================================================================
        [CommandMethod("FACINGWALLREBUILDALL")]
        public static void RebuildAll()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Editor ed = doc.Editor;
            FacingWallGrips.Enable();

            ObjectId controllerId = SelectController(doc);
            if (controllerId.IsNull) return;

            try
            {
                FacingWallController.RebuildAll(controllerId);
                ed.WriteMessage("\nМассив перестроен полностью.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nОшибка перестроения: " + ex.Message);
            }

            ed.Regen();
        }

        // =================================================================
        //  УДАЛЕНИЕ
        // =================================================================
        [CommandMethod("FACINGWALLDELETE")]
        public static void DeleteArray()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Editor ed = doc.Editor;
            FacingWallGrips.Enable();

            ObjectId controllerId = SelectController(doc);
            if (controllerId.IsNull) return;

            try
            {
                FacingWallController.Delete(controllerId);
                ed.WriteMessage("\nМассив удалён.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nОшибка удаления: " + ex.Message);
            }

            ed.Regen();
        }

        // =================================================================
        //  РЕДАКТИРОВАНИЕ МАССИВА
        //
        //  Одно место для всего, что меняют уже после создания: определения
        //  блоков (целый, половинчатый, заменители), их длины, количество
        //  рядов и отметка низа. Прежняя FACINGWALLBLOCKS умела только первое
        //  и потому удалена — здесь она вся целиком.
        //
        //  Правки копятся в памяти и применяются ОДНИМ перестроением на
        //  выходе: BuildAll стирает проекции Civil и пересоздаёт все блоки,
        //  делать это после каждого ответа незачем. Побочный полезный
        //  эффект — выход по Esc не оставляет массив на полпути.
        // =================================================================
        [CommandMethod("FACINGWALLEDIT")]
        public static void EditArray()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            Editor ed = doc.Editor;

            FacingWallGrips.Enable();

            ObjectId controllerId = SelectController(doc);
            if (controllerId.IsNull) return;

            FacingWallDefinition def;
            List<string> mvNames;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                def = FacingWallController.Load(controllerId, tr);
                mvNames = FacingWallBuilder.GetMvBlockNames(db, tr);
                tr.Commit();
            }

            if (def == null)
            {
                ed.WriteMessage("\nНе удалось прочитать контроллер.");
                return;
            }

            // Ряды, снятые сверху. Их блоки живут в чертеже до самого
            // применения: вернуть количество обратно — обычное дело, и тогда
            // ряд возвращается со своими объектами, а не строится заново.
            var removed = new List<FacingWallRowDefinition>();
            bool changed = false;

            while (true)
            {
                PrintArrayState(ed, def);

                // Глобальные имена латиницей, подписи русские — иначе ключевое
                // слово не вводится при английской раскладке. Default задаётся
                // ГЛОБАЛЬНЫМ именем: русское бросает eInvalidInput до показа.
                var opts = new PromptKeywordOptions("\nЧто изменить");
                opts.Keywords.Add("Whole", "Целый", "Целый");
                opts.Keywords.Add("Half", "Половинчатый", "Половинчатый");
                opts.Keywords.Add("Custom", "Индивидуальный", "Индивидуальный");
                opts.Keywords.Add("Rows", "Ряды", "Ряды");
                opts.Keywords.Add("Elevation", "Отметка", "Отметка");
                opts.Keywords.Add("Apply", "Применить", "Применить");
                opts.Keywords.Default = "Apply";
                opts.AllowNone = true;

                PromptResult res = ed.GetKeywords(opts);
                if (res.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\nОтменено, массив не изменён.");
                    return;
                }

                if (res.StringResult == "Apply") break;

                switch (res.StringResult)
                {
                    case "Whole":
                        if (ChangeMvBlock(ed, db, def, mvNames, true)) changed = true;
                        break;

                    case "Half":
                        if (ChangeMvBlock(ed, db, def, mvNames, false)) changed = true;
                        break;

                    case "Custom":
                        if (ChangeCustomBlock(ed, db, def, mvNames)) changed = true;
                        break;

                    case "Rows":
                        if (ChangeRowCount(ed, def, removed)) changed = true;
                        break;

                    case "Elevation":
                        if (ChangeBaseElevation(ed, db, def)) changed = true;
                        break;
                }
            }

            if (!changed)
            {
                ed.WriteMessage("\nНичего не изменено.");
                return;
            }

            try
            {
                using (doc.LockDocument())
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    // Снятые ряды: их блоки, проекции и отрезок-ручку стирает
                    // только эта команда — BuildAll о них уже не знает.
                    foreach (FacingWallRowDefinition row in removed)
                    {
                        if (row == null) continue;

                        FacingWallBuilder.EraseAll(tr, row.BlockIds);
                        FacingWallBuilder.EraseAll(tr, row.ProjectionIds);
                        FacingWallProjection.EraseGripLine(tr, row);
                    }

                    FacingWallController.BuildAll(tr, db, controllerId, def);
                    FacingWallController.Save(controllerId, def, tr);
                    tr.Commit();
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nОшибка перестроения: " + ex.Message);
                return;
            }

            int total = 0;
            foreach (FacingWallRowDefinition row in def.Rows)
                if (row != null && row.BlockIds != null) total += row.BlockIds.Count;

            ed.WriteMessage("\nМассив перестроен. Рядов: {0}, блоков: {1}.",
                def.RowCount, total);

            // Проекции Civil ассоциативны, а блоки только что созданы заново.
            if (def.ProjectionMode == FacingWallProjectionMode.MvBlock)
                ed.WriteMessage(
                    "\nРежим «многовидовой»: проекции стёрты вместе со старыми " +
                    "блоками, восстановите их командой FACINGWALLPROJECT.");

            ed.Regen();
        }

        /// <summary>Что сейчас в массиве — печатается перед каждым вопросом.</summary>
        private static void PrintArrayState(Editor ed, FacingWallDefinition def)
        {
            bool flat = def.ProjectionMode == FacingWallProjectionMode.Block2d;

            ed.WriteMessage("\n\n--- Массив облицовки ---");
            ed.WriteMessage("\nЦелый блок .................. {0}, длина {1:F3}{2}",
                def.MvBlockDefName ?? "не задан", def.BlockWidth,
                flat ? ", плоский «" + (def.ViewBlockName ?? "авто") + "»" : "");
            ed.WriteMessage("\nПоловинчатый блок ........... {0}, длина {1:F3}{2}{3}",
                def.HalfMvBlockDefName ?? "не задан", def.HalfWidth(),
                def.HalfBlockWidth <= 0.0 ? " (половина ячейки)" : "",
                flat ? ", плоский «" + (def.HalfViewBlockName ?? "авто") + "»" : "");

            foreach (string custom in def.CustomMvBlockNames())
                ed.WriteMessage("\nЗаменитель .................. {0}, длина {1:F3}{2}",
                    custom, CustomWidth(def, custom),
                    flat ? ", плоский «" + (def.GetCustomViewBlock(custom) ?? "авто") + "»" : "");

            ed.WriteMessage("\nРядов ....................... {0}", def.RowCount);
            ed.WriteMessage("\nОтметка низа / верха ........ {0:F3} / {1:F3}",
                def.BaseElevation, def.BaseElevation + def.RowCount * def.BlockHeight);
            ed.WriteMessage("\nОтображение на профиле ...... {0}",
                ModeLabel(def.ProjectionMode));
        }

        /// <summary>
        /// Длина, с которой стоят замены этого определения. Их может быть
        /// несколько с разной длиной — тогда берём первую попавшуюся: в
        /// редакторе она служит только значением по умолчанию.
        /// </summary>
        private static double CustomWidth(FacingWallDefinition def, string mvName)
        {
            foreach (FacingWallRowDefinition row in def.Rows)
            {
                if (row == null || row.Overrides == null) continue;

                foreach (FacingWallBlockOverride ov in row.Overrides)
                    if (ov != null && string.Equals(
                            ov.MvBlockDefName, mvName, StringComparison.OrdinalIgnoreCase))
                        return ov.Width;
            }

            return def.BlockWidth;
        }

        /// <summary>
        /// Сменить определение целого или половинчатого блока. Возвращает,
        /// изменилось ли что-нибудь на самом деле: перестраивать массив
        /// из-за повторного выбора того же блока незачем.
        /// </summary>
        private static bool ChangeMvBlock(
            Editor ed, Database db, FacingWallDefinition def,
            List<string> mvNames, bool whole)
        {
            if (mvNames == null || mvNames.Count == 0)
            {
                ed.WriteMessage("\nВ чертеже нет определений многовидовых блоков.");
                return false;
            }

            string currentMv = whole ? def.MvBlockDefName : def.HalfMvBlockDefName;
            string currentView = whole ? def.ViewBlockName : def.HalfViewBlockName;

            string picked = SelectFromListUI(
                whole ? "Целый блок" : "Половинчатый блок", mvNames, currentMv);

            if (picked == null) { ed.WriteMessage("\nОтменено."); return false; }

            bool sameMv = string.Equals(picked, currentMv, StringComparison.OrdinalIgnoreCase);

            string viewBlock = null;
            if (def.ProjectionMode == FacingWallProjectionMode.Block2d)
                viewBlock = AskViewBlock(
                    ed, db,
                    whole ? "Плоский блок для вида профиля — целый"
                          : "Плоский блок для вида профиля — половинчатый",
                    picked, sameMv ? currentView : null);

            // Определение не менялось и плоский блок не выбран — оставляем как было.
            // Сменилось определение — прежний плоский блок принадлежал другому
            // MVBlock и должен уйти, даже если нового не выбрали.
            string newView = (sameMv && viewBlock == null) ? currentView : viewBlock;

            // Длина: у нового определения она своя, у прежнего может остаться
            // прежней. Предлагаем текущую — Enter оставляет как было.
            double currentWidth = whole ? def.BlockWidth : def.HalfWidth();
            double newWidth = AskDouble(ed,
                whole ? "Длина целого блока" : "Длина половинчатого блока", currentWidth);

            if (newWidth <= 0.0)
            {
                ed.WriteMessage("\nДлина должна быть положительной, оставлена прежняя.");
                newWidth = currentWidth;
            }

            if (!whole && newWidth > def.HalfStep() + 1e-9)
            {
                // Ячейка сетки — BlockWidth/2, и половинка шире неё налезла бы
                // на соседний целый блок. Саму сетку не трогаем: на ней держатся
                // ручки, перевязка и привязка замен к пикетам.
                ed.WriteMessage(
                    "\nПоловинка не может быть длиннее половины ячейки ({0:F3}) — " +
                    "укорочена до неё.", def.HalfStep());
                newWidth = def.HalfStep();
            }

            bool result =
                !sameMv ||
                !string.Equals(newView, currentView, StringComparison.OrdinalIgnoreCase) ||
                Math.Abs(newWidth - currentWidth) > 1e-9;

            if (whole)
            {
                def.MvBlockDefName = picked;
                def.ViewBlockName = newView;
                def.BlockWidth = newWidth;
            }
            else
            {
                def.HalfMvBlockDefName = picked;
                def.HalfViewBlockName = newView;

                // Ровно половина ячейки хранится как «не задано»: так массив
                // продолжает следовать за шириной целого блока, если её меняют.
                def.HalfBlockWidth =
                    Math.Abs(newWidth - def.HalfStep()) < 1e-9 ? 0.0 : newWidth;
            }

            if (result)
                ed.WriteMessage("\n{0} блок: «{1}», длина {2:F3}{3}.",
                    whole ? "Целый" : "Половинчатый", picked, newWidth,
                    newView == null ? "" : ", плоский «" + newView + "»");
            else
                ed.WriteMessage("\nБлок не изменён.");

            return result;
        }

        /// <summary>
        /// Заменители (FACINGWALLREPLACE): определение, его длина и плоское
        /// представление. Правка идёт ПО ОПРЕДЕЛЕНИЮ и разом по всем местам,
        /// где оно стоит: замена привязана к месту на трассе, и перебирать
        /// их поштучно в командной строке было бы мучением.
        /// </summary>
        private static bool ChangeCustomBlock(
            Editor ed, Database db, FacingWallDefinition def, List<string> mvNames)
        {
            List<string> customNames = def.CustomMvBlockNames();

            if (customNames.Count == 0)
            {
                ed.WriteMessage(
                    "\nВ массиве нет заменённых блоков. Замены ставятся командой " +
                    "FACINGWALLREPLACE.");
                return false;
            }

            string target = customNames.Count == 1
                ? customNames[0]
                : SelectFromListUI("Какой заменитель править", customNames, null);

            if (target == null) { ed.WriteMessage("\nОтменено."); return false; }

            double currentWidth = CustomWidth(def, target);
            string currentView = def.GetCustomViewBlock(target);

            string picked = SelectFromListUI(
                "Определение вместо «" + target + "»", mvNames, target);

            if (picked == null) { ed.WriteMessage("\nОтменено."); return false; }

            bool sameMv = string.Equals(picked, target, StringComparison.OrdinalIgnoreCase);

            double newWidth = AskDouble(ed, "Длина блока-заменителя", currentWidth);
            if (newWidth <= 0.0)
            {
                ed.WriteMessage("\nДлина должна быть положительной, оставлена прежняя.");
                newWidth = currentWidth;
            }

            string viewBlock = null;
            if (def.ProjectionMode == FacingWallProjectionMode.Block2d)
                viewBlock = AskViewBlock(
                    ed, db, "Плоский блок для вида профиля — заменитель «" + picked + "»",
                    picked, sameMv ? currentView : null);

            string newView = (sameMv && viewBlock == null) ? currentView : viewBlock;

            bool result =
                !sameMv ||
                Math.Abs(newWidth - currentWidth) > 1e-9 ||
                !string.Equals(newView, currentView, StringComparison.OrdinalIgnoreCase);

            if (!result)
            {
                ed.WriteMessage("\nЗаменитель не изменён.");
                return false;
            }

            int touched = 0;

            foreach (FacingWallRowDefinition row in def.Rows)
            {
                if (row == null || row.Overrides == null) continue;

                foreach (FacingWallBlockOverride ov in row.Overrides)
                {
                    if (ov == null) continue;
                    if (!string.Equals(ov.MvBlockDefName, target,
                                       StringComparison.OrdinalIgnoreCase)) continue;

                    ov.MvBlockDefName = picked;
                    ov.Width = newWidth;
                    touched++;
                }
            }

            // Прежнее определение больше нигде не стоит — его плоский блок
            // в записи не нужен, иначе карта копила бы мусор.
            if (!sameMv) def.SetCustomViewBlock(target, null);
            def.SetCustomViewBlock(picked, newView);

            ed.WriteMessage("\nЗаменитель: «{0}», длина {1:F3}{2}. Мест: {3}.",
                picked, newWidth,
                newView == null ? "" : ", плоский «" + newView + "»", touched);

            return true;
        }

        /// <summary>
        /// Изменить количество рядов. Ряды снимаются и добавляются СВЕРХУ:
        /// номер ряда задаёт его отметку (BaseElevation + RowIndex*BlockHeight)
        /// и фазу перевязки, поэтому нумерация обязана оставаться сплошной
        /// от нуля.
        /// </summary>
        private static bool ChangeRowCount(
            Editor ed, FacingWallDefinition def, List<FacingWallRowDefinition> removed)
        {
            int current = def.RowCount;
            int wanted = AskInt(ed, "Количество рядов", current);

            if (wanted <= 0)
            {
                ed.WriteMessage("\nРядов должно быть не меньше одного.");
                return false;
            }

            if (wanted == current) return false;

            while (def.RowCount > wanted)
            {
                FacingWallRowDefinition top = TopRow(def);
                if (top == null) break;

                def.Rows.Remove(top);
                removed.Add(top);
            }

            while (def.RowCount < wanted)
            {
                FacingWallRowDefinition top = TopRow(def);
                int index = top == null ? 0 : top.RowIndex + 1;

                // Ряд этого номера могли снять в этом же сеансе — возвращаем его
                // вместе со связями, иначе его блоки остались бы в чертеже ничьими.
                FacingWallRowDefinition back =
                    removed.Find(r => r != null && r.RowIndex == index);

                if (back != null)
                {
                    removed.Remove(back);
                }
                else
                {
                    // Новый ряд занимает всю область раскладки; подрезать его
                    // отдельно — дело профильных ручек.
                    back = new FacingWallRowDefinition
                    {
                        RowIndex = index,
                        StartStation = def.LayoutStartStation,
                        EndStation = def.LayoutEndStation
                    };
                }

                def.Rows.Add(back);
            }

            ed.WriteMessage("\nРядов: {0} -> {1}, верх стены {2:F3}.",
                current, def.RowCount,
                def.BaseElevation + def.RowCount * def.BlockHeight);

            return true;
        }

        /// <summary>Самый верхний ряд — у него наибольший номер.</summary>
        private static FacingWallRowDefinition TopRow(FacingWallDefinition def)
        {
            FacingWallRowDefinition top = null;

            foreach (FacingWallRowDefinition row in def.Rows)
            {
                if (row == null) continue;
                if (top == null || row.RowIndex > top.RowIndex) top = row;
            }

            return top;
        }

        /// <summary>
        /// Сменить отметку низа. Спрашивается тем же диалогом, что и при
        /// создании: числом или снятием минимума с профиля в границах раскладки.
        /// </summary>
        private static bool ChangeBaseElevation(
            Editor ed, Database db, FacingWallDefinition def)
        {
            double current = def.BaseElevation;
            double elevation;

            if (!AskBaseElevation(
                    ed, db, def.LayoutLowStation(), def.LayoutHighStation(), out elevation))
            {
                ed.WriteMessage("\nОтметка не изменена.");
                return false;
            }

            if (Math.Abs(elevation - current) < 1e-9)
            {
                ed.WriteMessage("\nОтметка не изменена.");
                return false;
            }

            def.BaseElevation = elevation;

            ed.WriteMessage("\nОтметка низа: {0:F3} -> {1:F3}.", current, elevation);
            return true;
        }

        /// <summary>
        /// Каким 2D-блоком показывать это определение на виде профиля.
        /// null — «подобрать самому»: вид спереди, а если такого назначения
        /// в определении нет, первый блок состава.
        /// </summary>
        private static string AskViewBlock(
            Editor ed, Database db, string title, string mvName, string current)
        {
            List<string> names;
            string preferred;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                names = FacingWallBuilder.GetViewBlockNames(db, tr, mvName);
                preferred = FacingWallBuilder.GetProfileViewBlockName(db, tr, mvName);
                tr.Commit();
            }

            if (names.Count == 0)
            {
                ed.WriteMessage(
                    "\nВ определении «{0}» нет обычных блоков — " +
                    "на виде профиля показать его нечем.", mvName);
                return null;
            }

            return SelectFromListUI(title, names, current ?? preferred);
        }

        // =================================================================
        //  ПОДРЕЗКА РЯДОВ ПОД ПРОФИЛЬ
        //
        //  Стена выстраивается ступенями между своей отметкой низа и выбранным
        //  профилем: ряд обрывается на последнем блоке, который целиком помещается
        //  ПОД профилем, не коснувшись его контуром.
        //
        //  Ряды считаются от границ раскладки, а не от нынешней своей длины.
        //  Иначе команда умела бы только укорачивать: второй запуск давал бы
        //  не тот же результат, что первый, а ряд, обнулённый низким профилем,
        //  было бы нечем вернуть. Плата за это — профильные уточнения рядов,
        //  сделанные ручками, командой сбрасываются.
        //
        //  Связь с профилем НЕ сохраняется: профиль изменили — команду надо
        //  выполнить заново. Ассоциативность здесь означала бы реактор,
        //  как в ProfCorrLink, и это отдельная задача.
        // =================================================================
        [CommandMethod("FACINGBYPROFILE")]
        public static void FitRowsToProfile()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            Editor ed = doc.Editor;

            FacingWallGrips.Enable();

            ObjectId controllerId = SelectController(doc);
            if (controllerId.IsNull) return;

            var pOpts = new PromptEntityOptions(
                "\nВыберите профиль, ниже которого должна остаться стена: ");
            pOpts.SetRejectMessage("\nНужен профиль (Profile).");
            pOpts.AddAllowedClass(typeof(Profile), true);

            PromptEntityResult pRes = ed.GetEntity(pOpts);
            if (pRes.Status != PromptStatus.OK) return;

            int shortened = 0;
            int emptied = 0;
            int total = 0;

            try
            {
                using (doc.LockDocument())
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    FacingWallDefinition def = FacingWallController.Load(controllerId, tr);
                    if (def == null) { tr.Commit(); return; }

                    var profile = tr.GetObject(pRes.ObjectId, OpenMode.ForRead) as Profile;
                    if (profile == null)
                    {
                        ed.WriteMessage("\nНе удалось открыть профиль.");
                        tr.Commit();
                        return;
                    }

                    def.ResetRowsToLayout();

                    foreach (FacingWallRowDefinition row in def.Rows)
                    {
                        if (row == null) continue;

                        double full = row.EndStation;
                        row.EndStation = FitRowUnderProfile(def, row, profile);

                        if (Math.Abs(row.EndStation - row.StartStation) < 1e-9) emptied++;
                        else if (Math.Abs(row.EndStation - full) > 1e-9) shortened++;

                        total++;
                    }

                    FacingWallController.BuildAll(tr, db, controllerId, def);
                    FacingWallController.Save(controllerId, def, tr);
                    tr.Commit();
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nОшибка подрезки: " + ex.Message);
                return;
            }

            ed.WriteMessage(
                "\nРядов: {0}, подрезано: {1}, пустых: {2}.",
                total, shortened, emptied);

            if (emptied == total && total > 0)
                ed.WriteMessage(
                    "\nПод профилем не поместился ни один блок — профиль ниже " +
                    "отметки низа стены или слишком близко к ней.");
            else if (emptied > 0)
                ed.WriteMessage(
                    "\nПустые ряды не удалены: команда считает от границ раскладки, " +
                    "и с другим профилем они вернутся. Убрать совсем — FACINGWALLEDIT.");

            ed.WriteMessage(
                "\nСвязь с профилем не сохранена: изменили профиль — выполните команду заново.");

            ed.Regen();
        }

        /// <summary>
        /// Докуда ряд помещается под профилем.
        ///
        /// Блоки перебираются в порядке укладки, от якоря наружу, и ряд
        /// обрывается на ПЕРВОМ, который профиля касается: ряд сплошной, и
        /// оставить блоки за провалом профиля всё равно нельзя. Возвращается
        /// шов сетки за последним поместившимся блоком (NextStation), а не его
        /// край: у половинки со своей шириной это разные точки, и обрезка по
        /// краю выбросила бы сам этот блок из укоротившегося ряда.
        /// </summary>
        private static double FitRowUnderProfile(
            FacingWallDefinition def, FacingWallRowDefinition row, Profile profile)
        {
            double top = FacingWallBuilder.RowElevation(def, row) + def.BlockHeight;
            double edge = row.StartStation;   // ничего не поместилось — ряд пуст

            foreach (FacingWallBlockPlacement block in FacingWallBuilder.EnumerateBlocks(def, row))
            {
                if (!IsUnderProfile(profile, block, top)) break;
                edge = block.NextStation;
            }

            return edge;
        }

        /// <summary>
        /// Весь контур блока строго ниже профиля?
        ///
        /// Профиль между двумя пикетами может провисать (вертикальная кривая),
        /// поэтому проверяются не только края блока. Шаг опроса держим около
        /// 0.1 м: у облицовочного блока это те же 4-5 точек, что и раньше, а
        /// у длинного блока-заменителя провал профиля больше не проскочит
        /// между краями. Пикет, до которого профиль не достаёт, считается
        /// непроходимым: гарантировать, что блок под ним, там нечем.
        /// </summary>
        private static bool IsUnderProfile(
            Profile profile, FacingWallBlockPlacement block, double top)
        {
            const double eps = 1e-6;

            int samples = (int)Math.Ceiling(block.Width / 0.1);
            if (samples < 4) samples = 4;
            if (samples > 200) samples = 200;

            for (int k = 0; k <= samples; k++)
            {
                double station = block.Station + block.Width * k / samples;

                double elevation;
                try
                {
                    elevation = profile.ElevationAt(station);
                }
                catch (System.Exception)
                {
                    return false;   // профиль сюда не дотягивается
                }

                if (double.IsNaN(elevation)) return false;
                if (elevation <= top + eps) return false;
            }

            return true;
        }

        // =================================================================
        //  ПОВОРОТ БЛОКОВ
        //
        //  Ориентация геометрии внутри определения блока — не наше дело, она
        //  зависит от того, как блок нарисован. Поэтому поправка задаётся
        //  пользователем, а не угадывается.
        // =================================================================
        [CommandMethod("FACINGWALLROTATE")]
        public static void RotateBlocks()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            Editor ed = doc.Editor;
            FacingWallGrips.Enable();

            ObjectId controllerId = SelectController(doc);
            if (controllerId.IsNull) return;

            double current;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                FacingWallDefinition loaded = FacingWallController.Load(controllerId, tr);
                if (loaded == null)
                {
                    ed.WriteMessage("\nНе удалось прочитать контроллер.");
                    tr.Commit();
                    return;
                }

                current = loaded.BlockRotationOffset;
                tr.Commit();
            }

            ed.WriteMessage("\nТекущая поправка: {0:F2}°.", current * 180.0 / Math.PI);

            var opts = new PromptKeywordOptions("\nПовернуть блоки");
            opts.Keywords.Add("Flip", "Развернуть180", "Развернуть180");
            opts.Keywords.Add("Left", "Влево90", "Влево90");
            opts.Keywords.Add("Right", "Вправо90", "Вправо90");
            opts.Keywords.Add("Angle", "Угол", "Угол");
            opts.Keywords.Add("Reset", "Сбросить", "Сбросить");
            opts.Keywords.Default = "Flip";
            opts.AllowNone = true;

            PromptResult res = ed.GetKeywords(opts);
            if (res.Status != PromptStatus.OK) return;

            double offset;
            switch (res.StringResult)
            {
                case "Left": offset = current + Math.PI / 2.0; break;
                case "Right": offset = current - Math.PI / 2.0; break;
                case "Reset": offset = 0.0; break;

                case "Angle":
                    double deg = AskDouble(ed, "Поправка, градусы", 0.0, true);
                    offset = deg * Math.PI / 180.0;
                    break;

                default: offset = current + Math.PI; break;
            }

            // держим в пределах круга, чтобы поправка не накапливалась без конца
            offset = offset - Math.Floor(offset / (2.0 * Math.PI)) * 2.0 * Math.PI;

            try
            {
                using (doc.LockDocument())
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    FacingWallDefinition def = FacingWallController.Load(controllerId, tr);
                    if (def == null) { tr.Commit(); return; }

                    def.BlockRotationOffset = offset;

                    FacingWallController.BuildAll(tr, db, controllerId, def);
                    FacingWallController.Save(controllerId, def, tr);
                    tr.Commit();
                }

                ed.WriteMessage("\nПоправка: {0:F2}°. Массив перестроен.",
                    offset * 180.0 / Math.PI);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nОшибка поворота: " + ex.Message);
            }

            ed.Regen();
        }

        // =================================================================
        //  ДИАГНОСТИКА ПОВОРОТА
        //
        //  Печатает фактический угол блока, направление оси и разницу. По ней
        //  сразу видно, постоянная это ошибка (лечится поправкой) или зависит
        //  от кривизны (значит, дело в способе построения).
        // =================================================================
        [CommandMethod("FACINGWALLANGLE")]
        public static void DiagnoseAngle()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            Editor ed = doc.Editor;

            var opts = new PromptEntityOptions("\nВыберите блок массива в плане: ");
            PromptEntityResult res = ed.GetEntity(opts);
            if (res.Status != PromptStatus.OK) return;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                FacingWallDefinition def = null;
                FacingWallRowDefinition row = null;
                double station = 0.0;

                foreach (ObjectId id in FacingWallController.FindAll(db, tr))
                {
                    FacingWallDefinition candidate = FacingWallController.Load(id, tr);
                    if (candidate == null) continue;

                    if (TryLocateBlock(tr, candidate, res.ObjectId, out row, out station))
                    {
                        def = candidate;
                        break;
                    }
                }

                if (def == null)
                {
                    ed.WriteMessage("\nЭто не блок массива облицовки.");
                    tr.Commit();
                    return;
                }

                var alignment = tr.GetObject(def.AlignmentId, OpenMode.ForRead) as Alignment;
                var ent = tr.GetObject(res.ObjectId, OpenMode.ForRead);

                double actual = RotationOf(ent);
                double tangent;
                bool haveTangent = FacingWallBuilder.TryTangentAngle(alignment, station, out tangent);

                ed.WriteMessage("\n=== FACINGWALL: угол блока ===");
                ed.WriteMessage("\nРяд ......................... {0}", row.RowIndex);
                ed.WriteMessage("\nПикет центра блока .......... {0:F3}", station);
                ed.WriteMessage("\nПоворот блока ............... {0:F3}°", Deg(actual));

                if (haveTangent)
                {
                    ed.WriteMessage("\nНаправление оси ............. {0:F3}°", Deg(tangent));
                    ed.WriteMessage("\nРазница блок минус ось ...... {0:F3}°",
                        Deg(Normalize(actual - tangent)));
                }
                else
                {
                    ed.WriteMessage("\nНаправление оси ............. не определено");
                }

                ed.WriteMessage("\nЗаданная поправка ........... {0:F3}°",
                    Deg(def.BlockRotationOffset));
                ed.WriteMessage(
                    "\n\nЕсли разница одинакова у блоков на прямой и на кривой — " +
                    "это постоянная ошибка, лечится FACINGWALLROTATE. Если на " +
                    "кривой она другая — дело в способе построения, сообщите оба числа.");

                tr.Commit();
            }
        }

        // DBObject, как и Entity, есть в обоих пространствах имён — уточняем.
        private static double RotationOf(Autodesk.AutoCAD.DatabaseServices.DBObject obj)
        {
            var mv = obj as Autodesk.Aec.DatabaseServices.MultiViewBlockReference;
            if (mv != null) return mv.Rotation;

            var br = obj as BlockReference;
            if (br != null) return br.Rotation;

            return 0.0;
        }

        private static double Deg(double radians)
        {
            return radians * 180.0 / Math.PI;
        }

        /// <summary>Угол в (-Pi, Pi] — чтобы разница читалась как отклонение.</summary>
        private static double Normalize(double radians)
        {
            double a = radians - Math.Floor(radians / (2.0 * Math.PI)) * 2.0 * Math.PI;
            if (a > Math.PI) a -= 2.0 * Math.PI;
            return a;
        }

        // =================================================================
        //  ЗАМЕНА ОТДЕЛЬНЫХ БЛОКОВ
        //
        //  Выбранные блоки заменяются другим определением заданной длины;
        //  всё, что стоит после них в ряду, пересобирается под новую длину.
        //  Замена привязана к месту на трассе (см. FacingWallBlockOverride).
        // =================================================================
        [CommandMethod("FACINGWALLREPLACE")]
        public static void ReplaceBlocks()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            Editor ed = doc.Editor;
            FacingWallGrips.Enable();

            ObjectId controllerId = SelectController(doc);
            if (controllerId.IsNull) return;

            var pick = new PromptSelectionOptions
            {
                MessageForAdding = "\nВыберите блоки для замены (в плане или на профиле)"
            };

            PromptSelectionResult sel = ed.GetSelection(pick);
            if (sel.Status != PromptStatus.OK) return;

            List<string> mvNames;
            FacingWallDefinition loaded;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                loaded = FacingWallController.Load(controllerId, tr);
                mvNames = FacingWallBuilder.GetMvBlockNames(db, tr);
                tr.Commit();
            }

            if (loaded == null)
            {
                ed.WriteMessage("\nНе удалось прочитать контроллер.");
                return;
            }

            if (mvNames.Count == 0)
            {
                ed.WriteMessage("\nВ чертеже нет определений многовидовых блоков.");
                return;
            }

            string picked = SelectFromListUI("Блок-заменитель", mvNames, null);
            if (picked == null) { ed.WriteMessage("\nОтменено."); return; }

            double width = AskDouble(ed, "Длина блока-заменителя", loaded.BlockWidth);
            if (width <= 0.0)
            {
                ed.WriteMessage("\nДлина должна быть положительной.");
                return;
            }

            // Плоское представление спрашивается ПО ОПРЕДЕЛЕНИЮ: заменителей
            // в массиве может быть сколько угодно разных, и вид спереди назначен
            // далеко не у каждого — без этого их места на профиле оставались
            // пустыми ровно так же, как раньше половинки.
            string customView = null;
            if (loaded.ProjectionMode == FacingWallProjectionMode.Block2d)
                customView = AskViewBlock(
                    ed, db, "Плоский блок для вида профиля — заменитель «" + picked + "»",
                    picked, loaded.GetCustomViewBlock(picked));

            int added = 0;

            try
            {
                using (doc.LockDocument())
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    FacingWallDefinition def = FacingWallController.Load(controllerId, tr);
                    if (def == null) { tr.Commit(); return; }

                    foreach (ObjectId id in sel.Value.GetObjectIds())
                    {
                        FacingWallRowDefinition row;
                        double station;

                        if (!TryLocateBlock(tr, def, id, out row, out station)) continue;

                        row.Overrides.RemoveAll(o =>
                            o != null && Math.Abs(o.Station - station) < def.BlockWidth / 4.0);

                        row.Overrides.Add(new FacingWallBlockOverride
                        {
                            Station = station,
                            Width = width,
                            MvBlockDefName = picked
                        });

                        added++;
                    }

                    if (added > 0)
                    {
                        def.SetCustomViewBlock(picked, customView);

                        FacingWallController.BuildAll(tr, db, controllerId, def);
                        FacingWallController.Save(controllerId, def, tr);
                    }

                    tr.Commit();
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nОшибка замены: " + ex.Message);
                return;
            }

            if (added == 0)
                ed.WriteMessage(
                    "\nСреди выбранного нет блоков этого массива. " +
                    "В режиме «Многовидовой» проекции создаёт Civil, и опознать " +
                    "их нельзя — выбирайте блоки в плане.");
            else
                ed.WriteMessage("\nЗаменено мест: {0}. Ряды пересобраны.", added);

            ed.Regen();
        }

        /// <summary>
        /// Какому ряду и какому пикету принадлежит выбранный объект.
        ///
        /// Ряд ищется по спискам связей контроллера — так работает и для блоков
        /// в плане, и для наших объектов на профиле. Пикет берётся из геометрии:
        /// в плане сносом на трассу, на профиле — обратным пересчётом координат
        /// вида. Проекции Civil сюда не попадают: они не наши.
        /// </summary>
        private static bool TryLocateBlock(
            Transaction tr, FacingWallDefinition def, ObjectId id,
            out FacingWallRowDefinition row, out double station)
        {
            row = null;
            station = 0.0;

            bool inPlan = false;

            foreach (FacingWallRowDefinition candidate in def.Rows)
            {
                if (candidate == null) continue;

                if (candidate.BlockIds != null && candidate.BlockIds.Contains(id))
                {
                    row = candidate;
                    inPlan = true;
                    break;
                }

                if (candidate.ProjectionIds != null && candidate.ProjectionIds.Contains(id))
                {
                    row = candidate;
                    break;
                }
            }

            if (row == null) return false;

            var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
            if (ent == null) return false;

            // Берём ЦЕНТР габаритов, а не угол: блок в плане повёрнут, и его
            // угол по пикету может уехать к соседнему месту. Центр же остаётся
            // внутри своего блока при любом повороте.
            Point3d p;
            try
            {
                Extents3d ext = ent.GeometricExtents;
                p = new Point3d(
                    (ext.MinPoint.X + ext.MaxPoint.X) / 2.0,
                    (ext.MinPoint.Y + ext.MaxPoint.Y) / 2.0,
                    0.0);
            }
            catch (System.Exception)
            {
                return false;
            }

            try
            {
                if (inPlan)
                {
                    var alignment = tr.GetObject(def.AlignmentId, OpenMode.ForRead) as Alignment;
                    if (alignment == null) return false;

                    double s = 0.0, o = 0.0;
                    alignment.StationOffset(p.X, p.Y, ref s, ref o);
                    if (double.IsNaN(s)) return false;

                    station = s;
                }
                else
                {
                    var pv = tr.GetObject(def.ProfileViewId, OpenMode.ForRead) as ProfileView;
                    if (pv == null) return false;

                    double s = 0.0, e = 0.0;
                    pv.FindStationAndElevationAtXY(p.X, p.Y, ref s, ref e);
                    if (double.IsNaN(s)) return false;

                    station = s;
                }
            }
            catch (System.Exception)
            {
                return false;
            }

            return true;
        }

        // =================================================================
        //  РЕЖИМ ОТОБРАЖЕНИЯ НА ВИДЕ ПРОФИЛЯ
        //
        //  Меняет только профильную часть: блоки в плане не трогаются.
        // =================================================================
        [CommandMethod("FACINGWALLMODE")]
        public static void SetProjectionMode()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            Editor ed = doc.Editor;

            FacingWallGrips.Enable();

            ObjectId controllerId = SelectController(doc);
            if (controllerId.IsNull) return;

            // 1. Текущее состояние и состав многовидовых блоков — целого и
            //    половинчатого. Состав у них РАЗНЫЙ, поэтому и списки разные:
            //    один вопрос на оба блока показывал бы для половинки чужие имена.
            FacingWallProjectionMode current;
            string mvName, halfMvName;
            string currentView, currentHalfView;
            List<string> viewBlocks, halfViewBlocks;
            string frontBlock, halfFrontBlock;
            List<string> customMvNames;
            FacingWallDefinition loaded;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                loaded = FacingWallController.Load(controllerId, tr);
                if (loaded == null)
                {
                    ed.WriteMessage("\nНе удалось прочитать контроллер.");
                    tr.Commit();
                    return;
                }

                current = loaded.ProjectionMode;
                mvName = loaded.MvBlockDefName;
                halfMvName = loaded.HalfMvBlockDefName;
                currentView = loaded.ViewBlockName;
                currentHalfView = loaded.HalfViewBlockName;
                customMvNames = loaded.CustomMvBlockNames();

                viewBlocks = FacingWallBuilder.GetViewBlockNames(db, tr, mvName);
                frontBlock = FacingWallBuilder.GetFrontViewBlockName(db, tr, mvName);

                halfViewBlocks = FacingWallBuilder.GetViewBlockNames(db, tr, halfMvName);
                halfFrontBlock = FacingWallBuilder.GetFrontViewBlockName(db, tr, halfMvName);

                ed.WriteMessage("\nТекущий режим: {0}", ModeLabel(current));
                if (current == FacingWallProjectionMode.Block2d)
                    ed.WriteMessage("  (целый «{0}», половинчатый «{1}»)",
                        currentView ?? "авто", currentHalfView ?? "авто");

                tr.Commit();
            }

            // 2. Новый режим.
            var opts = new PromptKeywordOptions("\nОтображение на виде профиля");
            opts.Keywords.Add("Outline", "Контур", "Контур");
            opts.Keywords.Add("MvBlock", "Многовидовой", "Многовидовой");
            opts.Keywords.Add("Block2d", "Плоский", "Плоский");
            opts.Keywords.Default = current.ToString();
            opts.AllowNone = true;

            PromptResult res = ed.GetKeywords(opts);
            if (res.Status != PromptStatus.OK) return;

            FacingWallProjectionMode mode;
            switch (res.StringResult)
            {
                case "MvBlock": mode = FacingWallProjectionMode.MvBlock; break;
                case "Block2d": mode = FacingWallProjectionMode.Block2d; break;
                default: mode = FacingWallProjectionMode.Outline; break;
            }

            // 3. Для плоского режима — какими именно 2D-блоками показывать целый
            //    блок и половинку. Половинку спрашиваем отдельно: раньше её не
            //    спрашивали вовсе, брали вид спереди её MVBlock, а если такого
            //    назначения не было — половинки на профиле не появлялись совсем.
            string viewBlockName = null;
            string halfViewBlockName = null;
            bool askedHalf = false;
            var customViews = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (mode == FacingWallProjectionMode.Block2d)
            {
                if (viewBlocks.Count == 0)
                {
                    ed.WriteMessage(
                        "\nВ определении '{0}' не нашлось обычных блоков. " +
                        "Режим не изменён.", mvName);
                    return;
                }

                viewBlockName = SelectFromListUI(
                    "Плоский блок для вида профиля — целый",
                    viewBlocks, currentView ?? frontBlock);

                if (viewBlockName == null) { ed.WriteMessage("\nОтменено."); return; }

                if (frontBlock != null && viewBlockName == frontBlock)
                    ed.WriteMessage("\nВыбран блок вида спереди — то, что нужно для фасада.");

                if (string.IsNullOrEmpty(halfMvName))
                {
                    ed.WriteMessage(
                        "\nПоловинчатый блок массиву не задан — половинки не ставятся " +
                        "(задаётся командой FACINGWALLEDIT).");
                }
                else if (halfViewBlocks.Count == 0)
                {
                    ed.WriteMessage(
                        "\nВ определении половинки '{0}' не нашлось обычных блоков — " +
                        "на виде профиля показать её нечем.", halfMvName);
                }
                else
                {
                    halfViewBlockName = SelectFromListUI(
                        "Плоский блок для вида профиля — половинчатый",
                        halfViewBlocks, currentHalfView ?? halfFrontBlock);

                    if (halfViewBlockName == null) { ed.WriteMessage("\nОтменено."); return; }

                    askedHalf = true;
                }

                // Заменители: у каждого определения своё плоское представление,
                // поэтому вопрос задаётся ПО КАЖДОМУ, а не один на всех.
                foreach (string customMv in customMvNames)
                {
                    string answer = AskViewBlock(
                        ed, db,
                        "Плоский блок для вида профиля — заменитель «" + customMv + "»",
                        customMv, loaded.GetCustomViewBlock(customMv));

                    if (answer != null) customViews[customMv] = answer;
                }
            }

            // 4. Записать и пересобрать только проекции.
            FacingWallDefinition saved = null;

            try
            {
                using (doc.LockDocument())
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    FacingWallDefinition def = FacingWallController.Load(controllerId, tr);
                    if (def == null) { tr.Commit(); return; }

                    def.ProjectionMode = mode;
                    if (mode == FacingWallProjectionMode.Block2d)
                    {
                        def.ViewBlockName = viewBlockName;

                        // Если половинку спросить было негде (не задана или в её
                        // определении нет обычных блоков), прежний выбор не
                        // затираем: массив мог быть настроен раньше.
                        if (askedHalf) def.HalfViewBlockName = halfViewBlockName;

                        foreach (KeyValuePair<string, string> pair in customViews)
                            def.SetCustomViewBlock(pair.Key, pair.Value);
                    }

                    // Проекции Civil принадлежат только режиму MvBlock: уходя из
                    // него — стираем, входя — стираем прежние, чтобы не копились.
                    FacingWallCivilProjection.Erase(tr, def);

                    FacingWallProjection.ProjectAll(tr, db, def);
                    FacingWallController.Save(controllerId, def, tr);

                    saved = def;
                    tr.Commit();
                }

                ed.WriteMessage("\nРежим: {0}. Проекции перестроены.", ModeLabel(mode));
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nОшибка смены режима: " + ex.Message);
                return;
            }

            ed.Regen();

            // Штатный инструмент запускаем последним: он интерактивен и уйдёт
            // в очередь команд, то есть начнётся уже после нашей.
            if (mode == FacingWallProjectionMode.MvBlock && saved != null)
                FacingWallCivilProjection.Launch(doc, controllerId, saved);
        }

        // =================================================================
        //  ПОВТОРНОЕ ПРОЕЦИРОВАНИЕ ШТАТНЫМ ИНСТРУМЕНТОМ
        //
        //  Нужна после каждого перестроения массива: проекции Civil
        //  ассоциативны, а блоки при перестроении создаются заново.
        // =================================================================
        [CommandMethod("FACINGWALLPROJECT")]
        public static void ProjectByCivil()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Editor ed = doc.Editor;
            FacingWallGrips.Enable();

            ObjectId controllerId = SelectController(doc);
            if (controllerId.IsNull) return;

            FacingWallDefinition def;
            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                def = FacingWallController.Load(controllerId, tr);
                tr.Commit();
            }

            if (def == null)
            {
                ed.WriteMessage("\nНе удалось прочитать контроллер.");
                return;
            }

            if (def.ProjectionMode != FacingWallProjectionMode.MvBlock)
                ed.WriteMessage(
                    "\nВнимание: режим массива — «{0}». Проекции лягут поверх него.",
                    ModeLabel(def.ProjectionMode));

            FacingWallCivilProjection.Launch(doc, controllerId, def);
        }

        private static string ModeLabel(FacingWallProjectionMode mode)
        {
            switch (mode)
            {
                case FacingWallProjectionMode.MvBlock: return "многовидовой блок";
                case FacingWallProjectionMode.Block2d: return "плоский блок";
                default: return "контур";
            }
        }

        // =================================================================
        //  ДИАГНОСТИКА: почему нет грипс.
        //  Печатает состояние оверрула и разбирает выбранный объект по шагам —
        //  видно, на каком именно из них обрывается цепочка.
        // =================================================================
        [CommandMethod("FACINGWALLDIAG")]
        public static void Diagnose()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            Editor ed = doc.Editor;

            ed.WriteMessage("\n=== FACINGWALL: диагностика ===");
            ed.WriteMessage("\nOverrule.Overruling ......... {0}", Overrule.Overruling);
            ed.WriteMessage("\nОверрул зарегистрирован ..... {0}", FacingWallGrips.IsRegistered);
            ed.WriteMessage("\nВызовов IsApplicable ........ {0}", FacingWallGrips.IsApplicableCalls);
            ed.WriteMessage("\nMoveGripPointsAt (GripData) . {0}", FacingWallGrips.GripDataMoveCalls);
            ed.WriteMessage("\nMoveGripPointsAt (Point3d) .. {0}", FacingWallGrips.LegacyMoveCalls);
            ed.WriteMessage("\nРядов сдвинуто .............. {0}", FacingWallGrips.RowMoves);
            ed.WriteMessage("\nПоследнее действие .......... {0}", FacingWallGrips.LastAction);
            ed.WriteMessage("\nПоследняя ошибка ............ {0}",
                FacingWallGrips.LastError ?? "(нет)");

            foreach (string sv in new[] { "GRIPS", "GRIPOBJLIMIT", "GRIPSIZE" })
            {
                object v = null;
                try { v = AcApp.GetSystemVariable(sv); }
                catch (System.Exception) { }
                ed.WriteMessage("\n{0,-27} {1}", sv + " " + new string('.', Math.Max(1, 24 - sv.Length)),
                    v == null ? "?" : v.ToString());
            }

            var opts = new PromptEntityOptions(
                "\n\nВыберите контроллер или отрезок-ручку ряда: ");
            PromptEntityResult res = ed.GetEntity(opts);
            if (res.Status != PromptStatus.OK) return;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var ent = tr.GetObject(res.ObjectId, OpenMode.ForRead) as Entity;

                // Отрезок-ручка: показываем, на какой контроллер и ряд он указывает.
                ObjectId taggedController;
                int taggedRow;
                if (FacingWallProjection.TryReadGripTag(ent, out taggedController, out taggedRow))
                {
                    ed.WriteMessage("\n\nЭто отрезок-ручка.");
                    ed.WriteMessage("\nРяд ......................... {0}", taggedRow);
                    ed.WriteMessage("\nКонтроллер .................. {0}", taggedController.Handle);
                    ed.WriteMessage("\nОверрул считает его нашим ... {0}",
                        FacingWallGrips.IsRegistered);

                    // Числится ли отрезок в контроллере. Не числится — это копия:
                    // XData у неё та же, а модель о ней не знает, и ручки молчат.
                    FacingWallDefinition taggedDef =
                        FacingWallController.Load(taggedController, tr);
                    ed.WriteMessage("\nЧислится в контроллере ...... {0}",
                        taggedDef != null && taggedDef.GetGripLineId(taggedRow) == res.ObjectId
                            ? "да"
                            : "НЕТ (копия?)");
                    ed.WriteMessage(
                        "\n\nТяните за концы отрезка — ручки рисует сам AutoCAD.");
                    tr.Commit();
                    return;
                }

                var br = ent as BlockReference;
                if (br == null)
                {
                    ed.WriteMessage("\nЭто не контроллер и не отрезок-ручка.");
                    tr.Commit();
                    return;
                }

                var blockDef = tr.GetObject(br.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
                ed.WriteMessage("\n\nИмя блока ................... {0}",
                    blockDef != null ? blockDef.Name : "?");

                bool hasXData = FacingWallController.IsController(br);
                ed.WriteMessage("\nXData '{0}' ......... {1}",
                    FacingWallController.XAppName, hasXData);

                bool hasDict = !br.ExtensionDictionary.IsNull;
                ed.WriteMessage("\nРасширенный словарь ......... {0}", hasDict);

                bool hasXrec = false;
                if (hasDict)
                {
                    var dict = tr.GetObject(br.ExtensionDictionary, OpenMode.ForRead) as DBDictionary;
                    hasXrec = dict != null && dict.Contains(FacingWallController.XRecordKey);
                }
                ed.WriteMessage("\nXrecord '{0}' ....... {1}",
                    FacingWallController.XRecordKey, hasXrec);

                FacingWallDefinition def;
                FacingWallRecordStatus status = FacingWallController.Inspect(br, tr, out def);
                ed.WriteMessage("\nСостояние записи ............ {0}", status);
                ed.WriteMessage("\nОпределение прочитано ....... {0}", def != null);

                if (def == null)
                {
                    ed.WriteMessage("\n>>> Обрыв здесь: данные не читаются.");
                    tr.Commit();
                    return;
                }

                ed.WriteMessage("\nСвой хэндл в записи ......... {0} (фактический {1})",
                    string.IsNullOrEmpty(def.SelfHandle) ? "нет (запись до версии 10)" : def.SelfHandle,
                    br.Handle);

                if (def.IsDetachedCopy)
                    ed.WriteMessage(
                        "\n>>> Это КОПИЯ контроллера: связи сброшены, ручки отключены. " +
                        "Лечится FACINGWALLREBUILDALL.");

                ed.WriteMessage("\nРядов ....................... {0}", def.RowCount);
                ed.WriteMessage("\nBlockWidth / BlockHeight .... {0} / {1}",
                    def.BlockWidth, def.BlockHeight);
                ed.WriteMessage("\nBaseElevation ............... {0}", def.BaseElevation);

                ed.WriteMessage("\nAlignmentId валиден ......... {0}",
                    !def.AlignmentId.IsNull && def.AlignmentId.IsValid);
                ed.WriteMessage("\nProfileViewId валиден ....... {0}",
                    !def.ProfileViewId.IsNull && def.ProfileViewId.IsValid);

                if (def.ProfileViewId.IsNull)
                {
                    ed.WriteMessage("\n>>> Обрыв здесь: не сохранён вид профиля.");
                    tr.Commit();
                    return;
                }

                var pv = tr.GetObject(def.ProfileViewId, OpenMode.ForRead) as ProfileView;
                ed.WriteMessage("\nВид профиля открылся ........ {0}", pv != null);

                if (pv == null)
                {
                    ed.WriteMessage("\n>>> Обрыв здесь: вид профиля недоступен.");
                    tr.Commit();
                    return;
                }

                ed.WriteMessage("\n\nОтрезки-ручки по рядам:");

                int missing = 0;
                foreach (FacingWallRowDefinition row in def.Rows)
                {
                    if (row == null) continue;

                    bool ok = !row.GripLineId.IsNull &&
                              row.GripLineId.IsValid &&
                              !row.GripLineId.IsErased;

                    if (!ok) missing++;

                    ed.WriteMessage(
                        "\n  ряд {0}: {1:F3}..{2:F3}  блоков {3}  отрезок {4}",
                        row.RowIndex, row.StartStation, row.EndStation,
                        row.BlockIds.Count,
                        ok ? row.GripLineId.Handle.ToString() : "НЕТ");
                }

                bool planOk = IsAlive(def.PlanStartGripId) && IsAlive(def.PlanEndGripId) &&
                              IsAlive(def.ProfileStartGripId) && IsAlive(def.ProfileEndGripId);

                ed.WriteMessage(
                    "\n\nМаркеры якоря  план/профиль . {0} / {1}",
                    IsAlive(def.PlanStartGripId) ? def.PlanStartGripId.Handle.ToString() : "НЕТ",
                    IsAlive(def.ProfileStartGripId) ? def.ProfileStartGripId.Handle.ToString() : "НЕТ");
                ed.WriteMessage(
                    "\nМаркеры границы план/профиль  {0} / {1}",
                    IsAlive(def.PlanEndGripId) ? def.PlanEndGripId.Handle.ToString() : "НЕТ",
                    IsAlive(def.ProfileEndGripId) ? def.ProfileEndGripId.Handle.ToString() : "НЕТ");
                ed.WriteMessage(
                    "\nРаскладка ................... {0:F3} -> {1:F3} ({2})",
                    def.LayoutStartStation, def.LayoutEndStation,
                    def.LayoutEndStation >= def.LayoutStartStation
                        ? "по возрастанию пикета" : "в обратную сторону");

                if (missing > 0 || !planOk)
                    ed.WriteMessage(
                        "\n\n>>> Не хватает объектов-ручек (рядов без ручки: {0}{1}). " +
                        "Массив создан старой версией — выполните FACINGWALLREBUILDALL.",
                        missing, planOk ? "" : ", плановых маркеров тоже нет");
                else
                    ed.WriteMessage(
                        "\n\nСлои {0} и {1} — границы раскладки (равноправны, " +
                        "тянутся в любом виде), слой {2} — уточнение рядов в профиле.",
                        FacingWallLayoutGrip.PlanLayerName,
                        FacingWallLayoutGrip.ProfileLayerName,
                        FacingWallProjection.GripLayerName);

                tr.Commit();
            }
        }

        // =================================================================
        //  ДИАЛОГ С ПОЛЬЗОВАТЕЛЕМ
        // =================================================================

        private static bool AskBaseElevation(
            Editor ed, Database db, double stationStart, double stationEnd, out double elevation)
        {
            elevation = 0.0;

            // Глобальные имена ключевых слов держим латиницей: русские нельзя
            // ввести в командной строке при английской раскладке.
            var opts = new PromptKeywordOptions("\nОтметка низа стены");
            opts.Keywords.Add("Input", "Ввод", "Ввод");
            opts.Keywords.Add("Profile", "Профиль", "Профиль");
            opts.Keywords.Default = "Input";
            opts.AllowNone = true;

            PromptResult res = ed.GetKeywords(opts);
            if (res.Status != PromptStatus.OK) return false;

            if (res.StringResult == "Input")
            {
                var dOpts = new PromptDoubleOptions("\nОтметка низа стены: ")
                {
                    AllowNone = false
                };

                PromptDoubleResult dRes = ed.GetDouble(dOpts);
                if (dRes.Status != PromptStatus.OK) return false;

                elevation = dRes.Value;
                return true;
            }

            // Профиль нужен только чтобы взять число — связь с ним не сохраняется.
            var pOpts = new PromptEntityOptions("\nВыберите профиль низа стены: ");
            pOpts.SetRejectMessage("\nНужен профиль (Profile).");
            pOpts.AddAllowedClass(typeof(Profile), true);

            PromptEntityResult pRes = ed.GetEntity(pOpts);
            if (pRes.Status != PromptStatus.OK) return false;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var profile = (Profile)tr.GetObject(pRes.ObjectId, OpenMode.ForRead);
                bool found = TryMinElevation(profile, stationStart, stationEnd, out elevation);
                tr.Commit();

                if (!found)
                {
                    ed.WriteMessage("\nНе удалось снять отметку с профиля в этом диапазоне.");
                    return false;
                }
            }

            ed.WriteMessage("\nОтметка низа: {0:F3}", elevation);
            return true;
        }

        /// <summary>
        /// Минимальная отметка профиля в диапазоне. Профиль может не покрывать
        /// весь диапазон, поэтому опрос точечный и с защитой.
        /// </summary>
        private static bool TryMinElevation(
            Profile profile, double stationStart, double stationEnd, out double elevation)
        {
            const int samples = 200;

            elevation = double.MaxValue;
            bool any = false;

            double step = (stationEnd - stationStart) / samples;
            if (step <= 0.0) step = 1.0;

            for (double s = stationStart; s <= stationEnd + 1e-6; s += step)
            {
                try
                {
                    double e = profile.ElevationAt(s);
                    if (double.IsNaN(e)) continue;

                    if (e < elevation) elevation = e;
                    any = true;
                }
                catch (System.Exception)
                {
                    // профиль не покрывает этот пикет
                }
            }

            if (!any) elevation = 0.0;
            return any;
        }

        private static bool IsAlive(ObjectId id)
        {
            return !id.IsNull && id.IsValid && !id.IsErased;
        }

        private static ObjectId SelectController(Document doc)
        {
            Editor ed = doc.Editor;

            var opts = new PromptEntityOptions("\nВыберите контроллер массива: ");
            opts.SetRejectMessage("\nНужна вставка-контроллер массива облицовки.");
            opts.AddAllowedClass(typeof(BlockReference), true);

            PromptEntityResult res = ed.GetEntity(opts);
            if (res.Status != PromptStatus.OK) return ObjectId.Null;

            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                var br = tr.GetObject(res.ObjectId, OpenMode.ForRead) as BlockReference;
                bool ok = FacingWallController.IsController(br);

                FacingWallDefinition def = null;
                FacingWallRecordStatus status = FacingWallRecordStatus.NotController;
                if (ok) status = FacingWallController.Inspect(br, tr, out def);

                tr.Commit();

                if (!ok)
                {
                    ed.WriteMessage("\nЭто не контроллер массива облицовки.");
                    return ObjectId.Null;
                }

                switch (status)
                {
                    case FacingWallRecordStatus.NewerVersion:
                        // Работать нельзя: записать эту сборку поверх — значит
                        // потерять то, чего она не умеет читать.
                        ed.WriteMessage(
                            "\nЗапись сделана более новой версией плагина. " +
                            "Обновите сборку — эта данные не поймёт и перезапишет их с потерями.");
                        return ObjectId.Null;

                    case FacingWallRecordStatus.Corrupt:
                        ed.WriteMessage("\nЗапись контроллера повреждена и не читается.");
                        return ObjectId.Null;

                    case FacingWallRecordStatus.NoRecord:
                        ed.WriteMessage("\nУ этого контроллера нет записи с данными массива.");
                        return ObjectId.Null;

                    case FacingWallRecordStatus.DetachedCopy:
                        // Копия работать может, но своими объектами ещё не обзавелась.
                        ed.WriteMessage(
                            "\nЭто копия контроллера ({0} рядов): связи с объектами оригинала сброшены." +
                            "\nFACINGWALLREBUILDALL построит ей собственные объекты; блоки, " +
                            "скопированные вместе с ней, останутся ничьими — сотрите их вручную.",
                            def == null ? 0 : def.RowCount);
                        break;
                }
            }

            return res.ObjectId;
        }

        /// <summary>Контроллер ставим у левого верхнего угла вида профиля.</summary>
        private static Point3d ControllerPosition(Transaction tr, ObjectId profileViewId)
        {
            var pv = (ProfileView)tr.GetObject(profileViewId, OpenMode.ForRead);

            double x = 0.0, y = 0.0;
            pv.FindXYAtStationAndElevation(pv.StationStart, pv.ElevationMax, ref x, ref y);

            return new Point3d(x, y, 0.0);
        }

        private static double AskDouble(Editor ed, string msg, double defValue, bool allowNegative = false)
        {
            var opts = new PromptDoubleOptions("\n" + msg + ": ")
            {
                DefaultValue = defValue,
                AllowNone = true,
                AllowNegative = allowNegative
            };

            PromptDoubleResult res = ed.GetDouble(opts);
            return res.Status == PromptStatus.OK ? res.Value : defValue;
        }

        private static int AskInt(Editor ed, string msg, int defValue)
        {
            var opts = new PromptIntegerOptions("\n" + msg + ": ")
            {
                DefaultValue = defValue,
                AllowNone = true,
                AllowNegative = false,
                AllowZero = false
            };

            PromptIntegerResult res = ed.GetInteger(opts);
            return res.Status == PromptStatus.OK ? res.Value : defValue;
        }

        private static string SelectMvBlockUI(List<string> names)
        {
            return SelectFromListUI("Выберите многовидовой блок", names, null);
        }

        /// <summary>
        /// Выбор имени из списка. preselect — что подсветить изначально;
        /// если его нет в списке, встаём на первую строку.
        /// </summary>
        private static string SelectFromListUI(
            string title, List<string> names, string preselect)
        {
            if (names == null || names.Count == 0) return null;

            using (var form = new System.Windows.Forms.Form())
            {
                form.Text = title;
                form.Size = new System.Drawing.Size(320, 260);
                form.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;

                var listBox = new System.Windows.Forms.ListBox
                {
                    Dock = System.Windows.Forms.DockStyle.Fill
                };
                listBox.Items.AddRange(names.ToArray());

                int index = preselect != null ? names.IndexOf(preselect) : -1;
                listBox.SelectedIndex = index >= 0 ? index : 0;

                var btnOk = new System.Windows.Forms.Button
                {
                    Text = "OK",
                    Dock = System.Windows.Forms.DockStyle.Bottom,
                    DialogResult = System.Windows.Forms.DialogResult.OK
                };

                form.Controls.Add(listBox);
                form.Controls.Add(btnOk);
                form.AcceptButton = btnOk;

                if (form.ShowDialog() == System.Windows.Forms.DialogResult.OK &&
                    listBox.SelectedItem != null)
                    return listBox.SelectedItem.ToString();

                return null;
            }
        }
    }
}
