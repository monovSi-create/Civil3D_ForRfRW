using System;
using System.Runtime.InteropServices;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.DatabaseServices;
using Exception = Autodesk.AutoCAD.Runtime.Exception;

//[assembly: CommandClass(typeof(Civil3D_commands.GetAreaCommand))]

namespace Civil3D_commands
{

        public class GetAreaCommand
        {
            // Подгружаем ARX-функцию. Имя библиотеки — имя твоего ARX-файла.
            [DllImport("GeomProps2021x64.arx", CallingConvention = CallingConvention.Cdecl, EntryPoint = "GeomPropsGetArea")]
            public static extern double GeomPropsGetArea(ObjectId objectId);

            [CommandMethod("GETBODYAREA")]
            public static void GetAreaFromArx()
            {
                Document doc = Application.DocumentManager.MdiActiveDocument;
                Editor ed = doc.Editor;
                Database db = doc.Database;

                try
                {
                    PromptEntityOptions peo = new PromptEntityOptions("\nВыберите 3D объект: ");
                    PromptEntityResult per = ed.GetEntity(peo);

                    if (per.Status != PromptStatus.OK)
                    {
                        ed.WriteMessage("\nОтмена.");
                        return;
                    }

                    ObjectId id = per.ObjectId;

                    // Проверим, что объект валидный
                    if (!id.IsValid)
                    {
                        ed.WriteMessage("\nНедопустимый объект.");
                        return;
                    }

                    // Вызов ARX-функции с ObjectId
                    double area = GeomPropsGetArea(id);

                    ed.WriteMessage($"\nПлощадь поверхности: {area:F2}");
                }
                catch (Exception ex)
                {
                    ed.WriteMessage($"\nОшибка: {ex.Message}");
                }
            }
        }
    }

