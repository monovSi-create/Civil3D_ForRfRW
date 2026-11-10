using System;
using System.IO;
using System.Text;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.DatabaseServices;
using Civil3D_commands.Shared;

namespace Civil3D_commands.AssociativeBreaks
{
    /// <summary>
    /// Пошаговый журнал рискованных операций над коридором и профилем.
    ///
    /// Заведён ради вылетов AutoCAD: когда падает сам процесс, исключения нет,
    /// сообщения нет, и по чертежу не понять, на каком вызове Civil API это
    /// случилось. Каждая строка дописывается и закрывается сразу, поэтому
    /// переживает падение: ПОСЛЕДНЯЯ строка файла — это шаг, на котором упало.
    ///
    /// Файл: %APPDATA%\RW\BreakTrace.log. Растёт только на операциях с разрывами,
    /// поэтому обрезается при превышении размера.
    /// </summary>
    internal static class BreakTrace
    {
        private const long MaxBytes = 512 * 1024;

        public static string Path
        {
            get
            {
                return System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "RW", "BreakTrace.log");
            }
        }

        public static void Step(string text)
        {
            try
            {
                string path = Path;
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));

                var info = new FileInfo(path);
                if (info.Exists && info.Length > MaxBytes) File.Delete(path);

                File.AppendAllText(
                    path, DateTime.Now.ToString("HH:mm:ss.fff") + "  " + text + "\r\n", Encoding.UTF8);
            }
            catch (System.Exception)
            {
                // журнал — вспомогательное; не мешать самой операции
            }
        }

        /// <summary>
        /// Снимок состояния перед операцией: что за коридор, базовая линия,
        /// участки и профиль. Именно здесь видно, чем «проблемный» коридор
        /// отличается от нормального.
        /// </summary>
        public static void Snapshot(
            Transaction tr, Database db, BreakSession session, IBreakTarget target, string what)
        {
            try
            {
                Step("=== " + what);

                var corridor = RwHandles.Open<Corridor>(tr, db, target.CorridorHandle, OpenMode.ForRead);
                var profile = RwHandles.Open<Profile>(tr, db, target.ProfileHandle, OpenMode.ForRead);

                if (corridor == null) Step("коридор: не открылся");
                else Step("коридор: " + corridor.Name + ", базовых линий " + corridor.Baselines.Count);

                if (profile == null)
                {
                    Step("профиль: не открылся");
                }
                else
                {
                    Step(string.Format(
                        "профиль: {0}, тип {1}, режим {2}, пикеты {3:F3}..{4:F3}, PVI {5}",
                        profile.Name, profile.ProfileType, profile.UpdateMode,
                        profile.StartingStation, profile.EndingStation, profile.PVIs.Count));
                }

                Baseline bl = session.GetBaseline(tr, target);
                if (bl == null)
                {
                    Step("базовая линия: не найдена");
                    return;
                }

                Step(string.Format(
                    "базовая линия: {0}, тип {1}, пикеты {2:F3}..{3:F3}, обработана {4}, ждёт обработки {5}",
                    bl.Name, bl.BaselineType, bl.StartStation, bl.EndStation,
                    bl.IsProcessed, bl.NeedsProcessing));

                foreach (BaselineRegion r in bl.BaselineRegions)
                    Step(string.Format(
                        "  участок «{0}» {1:F4}..{2:F4}, {3}, ждёт обработки {4}",
                        r.Name, r.StartStation, r.EndStation, r.RegionGUID, r.NeedsProcessing));
            }
            catch (System.Exception ex)
            {
                Step("снимок не удался: " + ex.Message);
            }
        }
    }
}
