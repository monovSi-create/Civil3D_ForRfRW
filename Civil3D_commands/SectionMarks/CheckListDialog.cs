using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using AcAp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace Civil3D_commands.SectionMarks
{
    /// <summary>
    /// Окно со списком флажков: выбрать несколько строк. null — отказ.
    /// Показывается через <c>AcAp.ShowModalDialog</c> по той же причине,
    /// что и PickListDialog: окно обязано принадлежать главному окну AutoCAD.
    /// </summary>
    internal static class CheckListDialog
    {
        public static List<int> Show(
            string title, string header, IList<string> items, ICollection<int> preChecked)
        {
            if (items == null || items.Count == 0) return null;

            using (var form = new Form())
            using (var caption = new Label())
            using (var list = new CheckedListBox())
            using (var buttons = new Panel())
            using (var ok = new Button())
            using (var cancel = new Button())
            using (var all = new Button())
            using (var none = new Button())
            {
                form.Text = title;
                form.ClientSize = new Size(520, 400);
                form.MinimumSize = new Size(360, 260);
                form.FormBorderStyle = FormBorderStyle.Sizable;
                form.MaximizeBox = false;
                form.MinimizeBox = false;
                form.ShowInTaskbar = false;
                form.StartPosition = FormStartPosition.CenterParent;

                caption.Text = header;
                caption.Dock = DockStyle.Top;
                caption.Height = 44;
                caption.Padding = new Padding(8, 8, 8, 0);

                list.Dock = DockStyle.Fill;
                list.IntegralHeight = false;
                list.CheckOnClick = true;
                for (int i = 0; i < items.Count; i++)
                    list.Items.Add(items[i] ?? string.Empty,
                                   preChecked != null && preChecked.Contains(i));

                ok.Text = "OK";
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

                var chosen = new List<int>();
                for (int i = 0; i < items.Count; i++)
                    if (list.GetItemChecked(i)) chosen.Add(i);
                return chosen;
            }
        }
    }
}
