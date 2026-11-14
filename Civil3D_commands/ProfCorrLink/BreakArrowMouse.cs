using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Civil3D_commands.Shared;
using AcAp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace Civil3D_commands.AssociativeBreaks
{
    /// <summary>
    /// Щелчок по стрелке прокси — начало его переноса.
    ///
    /// У прокси нет ручек (см. <see cref="BreakGripOverrule"/>), а свои ручки
    /// Civil 3D не рисует вовсе. Поэтому щелчок перехватывается до AutoCAD
    /// (<c>PreTranslateMessage</c>): если в этот момент нет ни одной команды, режим
    /// правки включён и курсор стоит на стрелке, щелчок «съедается» и запускается
    /// <c>RW_MOVEPROXY</c> — тот же jig, что в ветке «Перенести»: двигаете мышь,
    /// граница едет в обоих видах, щелчок подтверждает, Esc отменяет.
    ///
    /// Всё обёрнуто в try/catch: сбой здесь не должен ронять ни AutoCAD, ни остальной
    /// модуль. Первые щелчки пишутся в BreakTrace.log — без них нельзя понять,
    /// не попадает ли зона нажатия мимо стрелки.
    /// </summary>
    public static class BreakArrowMouse
    {
        private const int WmLButtonDown = 0x0201;

        /// <summary>Радиус зоны нажатия вокруг центра стрелки, в пикселях.</summary>
        private const double HitPixels = 16.0;

        /// <summary>Маркер, перенос которого ждёт запуска команды.</summary>
        public static Guid PendingMarker = Guid.Empty;

        private static bool _enabled;
        private static int _logged;

        public static void Enable()
        {
            if (_enabled) return;
            AcAp.PreTranslateMessage += OnMessage;
            _enabled = true;
        }

        public static void Disable()
        {
            if (!_enabled) return;
            AcAp.PreTranslateMessage -= OnMessage;
            _enabled = false;
        }

        private static void OnMessage(object sender, PreTranslateMessageEventArgs e)
        {
            try
            {
                if (e.Message.message != WmLButtonDown) return;

                var session = BreakSession.Current;
                if (session == null || !session.AnyEditMode) return;

                var doc = AcAp.DocumentManager.MdiActiveDocument;
                if (doc == null) return;

                // Идёт команда (в том числе наш jig) — щелчок принадлежит ей.
                if (!doc.Editor.IsQuiescent) return;

                long lp = e.Message.lParam.ToInt64();
                int x = (short)(lp & 0xFFFF);
                int y = (short)((lp >> 16) & 0xFFFF);

                // Курсор считается ДВУМЯ способами: по системным переменным вида
                // и через Editor.PointToWorld. Первый — основной; второй оставлен ради
                // журнала: по расхождению видно, который из них врёт.
                Point3d cursor;
                double wpp;
                if (!BreakArrowOverrule.TryCursorWorld(x, y, out cursor, out wpp) || wpp <= 0.0)
                    return;

                Point3d cursorPtw = doc.Editor.PointToWorld(new System.Windows.Point(x, y));

                double unit = wpp * BreakArrowOverrule.ArrowPixels;
                double hit = wpp * HitPixels;

                Guid found = Guid.Empty;
                double best = double.MaxValue, bestPtw = double.MaxValue;
                string nearest = "—";

                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    foreach (StationMarker m in session.Store.All)
                    {
                        if (!session.IsEditMode(m.ProfileHandle)) continue;

                        foreach (Handle h in new[] { m.ProfileProxyHandle, m.PlanProxyHandle })
                        {
                            var ln = RwHandles.Open<Line>(tr, doc.Database, h, OpenMode.ForRead);
                            if (ln == null) continue;

                            Point3d a, b;
                            if (!BreakArrowOverrule.TryArrowCentres(ln.StartPoint, ln.EndPoint, unit, out a, out b))
                                continue;

                            double d = Math.Min(Dist2d(cursor, a), Dist2d(cursor, b));
                            bestPtw = Math.Min(bestPtw, Math.Min(Dist2d(cursorPtw, a), Dist2d(cursorPtw, b)));

                            if (d < best)
                            {
                                best = d;
                                found = d <= hit ? m.Id : Guid.Empty;
                                nearest = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                                    "{0} прокси ({1:F3},{2:F3})-({3:F3},{4:F3})",
                                    h == m.ProfileProxyHandle ? "профильный" : "плановый",
                                    ln.StartPoint.X, ln.StartPoint.Y, ln.EndPoint.X, ln.EndPoint.Y);
                            }
                        }
                    }
                    tr.Commit();
                }

                if (_logged < 12)
                {
                    _logged++;
                    BreakTrace.Step(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "стрелки: щелчок ({0},{1}); курсор по VIEWCTR ({2:F3},{3:F3}), по PointToWorld ({4:F3},{5:F3}); " +
                        "px={6:F5}; до ближайшей стрелки: по VIEWCTR {7:F1} px, по PointToWorld {8:F1} px, зона {9:F0} px, {10}; ближайший: {11}",
                        x, y, cursor.X, cursor.Y, cursorPtw.X, cursorPtw.Y, wpp,
                        best == double.MaxValue ? -1.0 : best / wpp,
                        bestPtw == double.MaxValue ? -1.0 : bestPtw / wpp,
                        HitPixels, found == Guid.Empty ? "мимо" : "ПОПАДАНИЕ", nearest));
                }

                if (found == Guid.Empty) return;

                PendingMarker = found;
                e.Handled = true;

                // Строка уходит в очередь и выполняется, когда AutoCAD снова свободен.
                doc.SendStringToExecute("RW_MOVEPROXY\n", true, false, false);
            }
            catch (System.Exception ex)
            {
                BreakTrace.Step("стрелки: ОШИБКА " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static double Dist2d(Point3d a, Point3d b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }

    public partial class BreakCommands
    {
        /// <summary>
        /// Служебная команда: её запускает щелчок по стрелке прокси. Вводить
        /// вручную незачем (для выбора линии есть ветка «Перенести» в RW_EDITMODE).
        /// </summary>
        [CommandMethod("RW_MOVEPROXY")]
        public void MovePendingProxy()
        {
            var doc = AcAp.DocumentManager.MdiActiveDocument;
            var session = BreakSession.Current;
            if (doc == null || session == null) return;

            Guid id = BreakArrowMouse.PendingMarker;
            BreakArrowMouse.PendingMarker = Guid.Empty;
            if (id == Guid.Empty)
            {
                doc.Editor.WriteMessage("\nНе выбрана граница: щёлкните по стрелке прокси в режиме правки.");
                return;
            }

            StationMarker m = session.Store.Get(id);
            if (m == null) return;

            // Слой границ вне режима правки заблокирован — на время переноса снимаем.
            using (new BoundaryUnlock(doc, session)) MoveMarkerCore(doc, session, m);
        }
    }
}
