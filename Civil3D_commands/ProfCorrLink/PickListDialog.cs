using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using AcAp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace Civil3D_commands.AssociativeBreaks
{
    /// <summary>
    /// Окно выбора одного имени из списка.
    ///
    /// Заведено ради конструкций: их в чертеже бывает два десятка, имена
    /// длинные и похожие, и набирать номер в командной строке, сверяясь
    /// с напечатанным списком, дольше и ошибочнее, чем щёлкнуть строку.
    /// Остальные запросы модуля остаются в командной строке — окно посреди
    /// цепочки запросов сбивает ввод, и оправдано оно только там, где
    /// выбирают из длинного перечня.
    ///
    /// Показывается через <c>AcAp.ShowModalDialog</c>, а не <c>Form.ShowDialog</c>:
    /// иначе окно не принадлежит главному окну AutoCAD — теряется поверх него,
    /// не блокирует его как положено модальному и остаётся висеть при
    /// переключении документов.
    /// </summary>
    internal static class PickListDialog
    {
        /// <summary>
        /// Показать список. Возвращает индекс выбранного элемента
        /// или −1, если пользователь отказался.
        /// </summary>
        /// <param name="title">Заголовок окна.</param>
        /// <param name="header">Строка над списком: что и для чего выбирается.</param>
        /// <param name="items">Имена в том же порядке, что и объекты у вызывающего.</param>
        /// <param name="preselected">Что подсветить сразу; −1 — ничего.</param>
        public static int Show(string title, string header, IList<string> items, int preselected)
        {
            if (items == null || items.Count == 0) return -1;

            using (var form = new Form())
            using (var list = new ListBox())
            using (var caption = new Label())
            using (var buttons = new Panel())
            using (var ok = new Button())
            using (var cancel = new Button())
            {
                form.Text = title;
                form.ClientSize = new Size(420, 340);
                form.MinimumSize = new Size(320, 240);
                form.FormBorderStyle = FormBorderStyle.Sizable;
                form.MaximizeBox = false;
                form.MinimizeBox = false;
                form.ShowInTaskbar = false;
                form.StartPosition = FormStartPosition.CenterParent;

                caption.Text = header;
                caption.Dock = DockStyle.Top;
                caption.Height = 34;
                caption.Padding = new Padding(8, 8, 8, 0);
                caption.AutoEllipsis = true;

                list.Dock = DockStyle.Fill;
                list.IntegralHeight = false;   // иначе список обрезается по целым строкам
                list.SelectionMode = SelectionMode.One;
                foreach (string name in items) list.Items.Add(name ?? string.Empty);

                // Подсветить текущее значение: чаще всего его и подтверждают,
                // а если меняют — видно, от чего пляшут.
                //
                // Нынешнего нет — не подсвечиваем НИЧЕГО и держим OK серым,
                // пока пользователь не выберет сам. Иначе привычное «Enter»
                // назначило бы участку первую попавшуюся конструкцию, а отказ
                // здесь — обычный и частый ответ.
                list.SelectedIndex = preselected >= 0 && preselected < items.Count
                    ? preselected
                    : -1;

                ok.Text = "OK";
                ok.DialogResult = DialogResult.OK;
                ok.Dock = DockStyle.Right;
                ok.Width = 90;

                cancel.Text = "Отмена";
                cancel.DialogResult = DialogResult.Cancel;
                cancel.Dock = DockStyle.Right;
                cancel.Width = 90;

                buttons.Dock = DockStyle.Bottom;
                buttons.Height = 40;
                buttons.Padding = new Padding(8, 6, 8, 6);

                // Порядок добавления в Dock=Right обратный показанному:
                // добавленный первым окажется правее.
                buttons.Controls.Add(cancel);
                buttons.Controls.Add(ok);

                ok.Enabled = list.SelectedIndex >= 0;
                list.SelectedIndexChanged += (s, e) => ok.Enabled = list.SelectedIndex >= 0;

                // Двойной щелчок — самый быстрый путь: выбрал и закрыл.
                list.DoubleClick += (s, e) =>
                {
                    if (list.SelectedIndex >= 0) form.DialogResult = DialogResult.OK;
                };

                form.Controls.Add(list);
                form.Controls.Add(caption);
                form.Controls.Add(buttons);
                form.AcceptButton = ok;
                form.CancelButton = cancel;

                DialogResult result;
                try
                {
                    result = AcAp.ShowModalDialog(form);
                }
                catch (System.Exception)
                {
                    // Нет главного окна (команда из скрипта, иной хост) —
                    // показываем как обычное окно: выбор важнее принадлежности.
                    result = form.ShowDialog();
                }

                return result == DialogResult.OK ? list.SelectedIndex : -1;
            }
        }
    }
}
