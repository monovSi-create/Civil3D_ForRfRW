using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AcAp = Autodesk.AutoCAD.ApplicationServices.Application;
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;
using ObjectId = Autodesk.AutoCAD.DatabaseServices.ObjectId;

[assembly: CommandClass(typeof(Civil3D_commands.ExportCleanup.ExportCleanupCommands))]

namespace Civil3D_commands.ExportCleanup
{
    /// <summary>
    /// Очистка чертежа после «Экспорта в AutoCAD»: убирает слои с элементами
    /// типовых сечений, которые разворачиваются в чертёж вместе с коридором.
    ///
    /// Какие слои лишние, зависит от набора конструкций, поэтому список не
    /// зашит: пользователь отмечает слои один раз, список запоминается
    /// в файле пользователя, и дальше очистка идёт одной командой.
    /// Запоминаются имена слоёв, а не объекты, — так список годится
    /// для любого следующего экспорта.
    ///
    /// Работает в текущем чертеже — запускать в экспортированном DWG.
    /// Всё одной командой, так что UNDO откатывает очистку целиком.
    /// </summary>
    public class ExportCleanupCommands
    {
        private static string ListPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "RW", "CleanExportLayers.txt");
            }
        }

        [CommandMethod("RW_CLEANEXPORT")]
        public void CleanExport()
        {
            Document doc = AcAp.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            HashSet<string> saved = LoadList();

            // Есть сохранённый список — по умолчанию применяем его без вопросов к слоям.
            bool edit = saved.Count == 0;
            if (!edit)
            {
                var kw = new PromptKeywordOptions(string.Format(
                    "\nСохранённый список: {0} слоёв. Что сделать?", saved.Count));
                kw.Keywords.Add("Apply", "Удалить", "Удалить");
                kw.Keywords.Add("Edit", "Изменить", "Изменить");
                kw.Keywords.Default = "Apply";
                PromptResult kr = ed.GetKeywords(kw);
                if (kr.Status != PromptStatus.OK) return;
                edit = kr.StringResult == "Edit";
            }

            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Dictionary<string, List<ObjectId>> byLayer = Scan(tr, db);

                if (byLayer.Count == 0)
                {
                    ed.WriteMessage("\nВ пространстве модели нет объектов.");
                    return;
                }

                HashSet<string> chosen;
                if (edit)
                {
                    chosen = ChooseLayers(byLayer, saved);
                    if (chosen == null) return;

                    // Имена слоёв, которых в этом чертеже нет, остаются в списке:
                    // они понадобятся в другом экспорте.
                    var keep = new HashSet<string>(
                        saved.Where(n => !byLayer.ContainsKey(n)), StringComparer.OrdinalIgnoreCase);
                    keep.UnionWith(chosen);
                    SaveList(keep);
                }
                else
                {
                    chosen = new HashSet<string>(
                        saved.Where(n => byLayer.ContainsKey(n)), StringComparer.OrdinalIgnoreCase);
                }

                int erased = 0, layersGone = 0;
                var emptied = new List<string>();

                foreach (string layer in chosen)
                {
                    List<ObjectId> ids;
                    if (!byLayer.TryGetValue(layer, out ids)) continue;

                    foreach (ObjectId id in ids)
                    {
                        // true: слой может быть заблокирован, а чистить надо и его.
                        var e = tr.GetObject(id, OpenMode.ForWrite, false, true) as Entity;
                        if (e == null || e.IsErased) continue;
                        e.Erase();
                        erased++;
                    }

                    emptied.Add(layer);
                }

                // Опустевшие слои убираем тоже; не вышло (текущий, нулевой, занят
                // в блоке) — не страшно, слой просто останется пустым.
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                foreach (string name in emptied)
                {
                    if (!lt.Has(name) || name == "0" || name == "Defpoints") continue;
                    if (lt[name] == db.Clayer) continue;

                    try
                    {
                        var rec = (LayerTableRecord)tr.GetObject(lt[name], OpenMode.ForWrite);
                        rec.Erase();
                        layersGone++;
                    }
                    catch (Autodesk.AutoCAD.Runtime.Exception)
                    {
                    }
                }

                tr.Commit();

                ed.WriteMessage(string.Format(
                    "\nУдалено объектов: {0}; слоёв: {1}. Откатывается командой UNDO.",
                    erased, layersGone));
            }
        }

        /// <summary>Объекты пространства модели по слоям.</summary>
        private static Dictionary<string, List<ObjectId>> Scan(Transaction tr, Database db)
        {
            var map = new Dictionary<string, List<ObjectId>>(StringComparer.OrdinalIgnoreCase);
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

            foreach (ObjectId id in ms)
            {
                var e = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (e == null) continue;

                List<ObjectId> list;
                if (!map.TryGetValue(e.Layer, out list))
                {
                    list = new List<ObjectId>();
                    map[e.Layer] = list;
                }

                list.Add(id);
            }

            return map;
        }

        /// <summary>Окно с флажками. null — отказ.</summary>
        private static HashSet<string> ChooseLayers(
            Dictionary<string, List<ObjectId>> byLayer, HashSet<string> saved)
        {
            List<string> names = byLayer.Keys.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();

            using (var form = new Form())
            using (var caption = new Label())
            using (var list = new CheckedListBox())
            using (var buttons = new Panel())
            using (var ok = new Button())
            using (var cancel = new Button())
            using (var all = new Button())
            using (var none = new Button())
            {
                form.Text = "Очистка после экспорта";
                form.ClientSize = new Size(480, 420);
                form.MinimumSize = new Size(360, 260);
                form.FormBorderStyle = FormBorderStyle.Sizable;
                form.MaximizeBox = false;
                form.MinimizeBox = false;
                form.ShowInTaskbar = false;
                form.StartPosition = FormStartPosition.CenterParent;

                caption.Text = "Отметьте слои, объекты которых нужно удалить. " +
                               "Список запомнится для следующих экспортов.";
                caption.Dock = DockStyle.Top;
                caption.Height = 44;
                caption.Padding = new Padding(8, 8, 8, 0);

                list.Dock = DockStyle.Fill;
                list.IntegralHeight = false;
                list.CheckOnClick = true;
                foreach (string n in names)
                    list.Items.Add(string.Format("{0}   ({1})", n, byLayer[n].Count), saved.Contains(n));

                ok.Text = "Удалить";
                ok.DialogResult = DialogResult.OK;
                ok.Dock = DockStyle.Right;
                ok.Width = 90;

                cancel.Text = "Отмена";
                cancel.DialogResult = DialogResult.Cancel;
                cancel.Dock = DockStyle.Right;
                cancel.Width = 90;

                all.Text = "Все";
                all.Dock = DockStyle.Left;
                all.Width = 70;
                all.Click += (s, e) => { for (int i = 0; i < list.Items.Count; i++) list.SetItemChecked(i, true); };

                none.Text = "Снять";
                none.Dock = DockStyle.Left;
                none.Width = 70;
                none.Click += (s, e) => { for (int i = 0; i < list.Items.Count; i++) list.SetItemChecked(i, false); };

                buttons.Dock = DockStyle.Bottom;
                buttons.Height = 40;
                buttons.Padding = new Padding(8, 6, 8, 6);
                buttons.Controls.Add(cancel);
                buttons.Controls.Add(ok);
                buttons.Controls.Add(none);
                buttons.Controls.Add(all);

                form.Controls.Add(list);
                form.Controls.Add(caption);
                form.Controls.Add(buttons);
                form.AcceptButton = ok;
                form.CancelButton = cancel;

                DialogResult result;
                try { result = AcAp.ShowModalDialog(form); }
                catch (System.Exception) { result = form.ShowDialog(); }

                if (result != DialogResult.OK) return null;

                var chosen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < names.Count; i++)
                    if (list.GetItemChecked(i)) chosen.Add(names[i]);
                return chosen;
            }
        }

        private static HashSet<string> LoadList()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(ListPath))
                    foreach (string line in File.ReadAllLines(ListPath))
                        if (line.Trim().Length > 0) set.Add(line.Trim());
            }
            catch (System.Exception)
            {
                // нет доступа к файлу — работаем с пустым списком
            }

            return set;
        }

        private static void SaveList(HashSet<string> names)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ListPath));
                File.WriteAllLines(ListPath, names.OrderBy(n => n));
            }
            catch (System.Exception)
            {
                // не записалось — очистка всё равно пройдёт, список просто не запомнится
            }
        }
    }
}
