using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Exception = Autodesk.AutoCAD.Runtime.Exception;
using Autodesk.Aec.PropertyData.DatabaseServices;

//[assembly: CommandClass(typeof(Civil3D_commands.AddGeoPropsProperty))]

namespace Civil3D_commands
{
    public class AddGeoPropsProperty
    {
        // Подгружаем ARX-функцию. Имя библиотеки — имя твоего ARX-файла.
        [DllImport("GeomProps2021x64.arx", CallingConvention = CallingConvention.Cdecl, EntryPoint = "GeomPropsGetArea")]
        public static extern double GeomPropsGetArea(ObjectId objectId);

        [DllImport("GeomProps2021x64.arx", CallingConvention = CallingConvention.Cdecl, EntryPoint = "GeomPropsGetVolume")]
        public static extern double GeomPropsGetVolume(ObjectId objectId);

        [CommandMethod("RW_AddGeoPropsToPropertySet")]
        public static void AddGeoPropsToSetCommand()
        {
            // Получаем текущий документ, базу данных и редактор
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            // Начинаем транзакцию
            using (var tr = db.TransactionManager.StartTransaction())
            {
                // Получаем все объекты типа Body
                var bodies = GetAllBodies(tr, db);
                var solids = GetAllSolids(tr, db);
                // Фильтруем тела по наличию поля "Площадь" в наборе характеристик
                var filteredBodies = FilterBodiesWithAreaProperty(tr, bodies);
                var filteredSolids = FilterSolidsWithVolumeProperty(tr, solids);
                // Обрабатываем отфильтрованные тела
                foreach (var bodyId in filteredBodies)
                {
                    try
                    {
                        // Получаем площадь через geomprops
                        double area = GeomPropsGetArea(bodyId);
                        // Обновляем характеристику "Площадь"
                        UpdateAreaProperty(tr, bodyId, area);
                    }
                    catch (Exception ex) { }
                }
                foreach (var  solidId in filteredSolids)
                {
                    try
                    {
                        // Получаем площадь через geomprops
                        double volume = GeomPropsGetVolume(solidId);
                        // Обновляем характеристику "Площадь"
                        UpdateVolumeProperty(tr, solidId, volume);
                    }
                    catch (Exception ex) { }
                }

                // Фиксируем изменения
                tr.Commit();
            }
        }

        /// <summary>
        /// Получает все объекты типа Solid из пространства модели.
        /// </summary>
        private static List<ObjectId> GetAllBodies(Transaction tr, Database db)
        {
            var bodies = new List<ObjectId>();
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

            foreach (ObjectId id in btr)
            {
                if (id.ObjectClass.DxfName == "BODY")
                {
                    bodies.Add(id);
                }
            }

            return bodies;
        }
        private static List<ObjectId> GetAllSolids(Transaction tr, Database db)
        {
            var solids = new List<ObjectId>();
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

            foreach (ObjectId id in btr)
            {
                if (id.ObjectClass.DxfName == "3DSOLID")
                {
                    solids.Add(id);
                }
            }

            return solids;
        }
        /// <summary>
        /// Фильтрует тела по наличию поля "Площадь" в наборе характеристик.
        /// </summary>
        private static List<ObjectId> FilterBodiesWithAreaProperty(Transaction tr, List<ObjectId> bodies)
        {
            var filtered = new List<ObjectId>();

            foreach (var bodyId in bodies)
            {
                var obj = tr.GetObject(bodyId, OpenMode.ForRead);
                if (obj != null)
                {
                    var propertySets = PropertyDataServices.GetPropertySets(obj);
                    foreach (ObjectId psId in propertySets)
                    {
                        var ps = tr.GetObject(psId, OpenMode.ForRead) as PropertySet;
                        if (ps != null && ps.PropertySetDefinitionName == "Информация_о_модели_армогрунта") // Замените на имя вашего набора
                        {
                        var propId = ps.PropertyNameToId("Площадь");

                            if (propId != null)
                            {
                                filtered.Add(bodyId);
                                break;
                            }
                            
                        }
                    }
                }
            }

            return filtered;
        }
        private static List<ObjectId> FilterSolidsWithVolumeProperty(Transaction tr, List<ObjectId> solids)
        {
            var filtered = new List<ObjectId>();

            foreach (var solidId in solids)
            {
                var obj = tr.GetObject(solidId, OpenMode.ForRead);
                if (obj != null)
                {
                    var propertySets = PropertyDataServices.GetPropertySets(obj);
                    foreach (ObjectId psId in propertySets)
                    {
                        var ps = tr.GetObject(psId, OpenMode.ForRead) as PropertySet;
                        if (ps != null && ps.PropertySetDefinitionName == "Информация_о_модели_армогрунта") // Замените на имя вашего набора
                        {
                            var propId = ps.PropertyNameToId("Объем");

                            if (propId != null)
                            {
                                filtered.Add(solidId);
                                break;
                            }

                        }
                    }
                }
            }

            return filtered;
        }
        /// <summary>
        /// Обновляет характеристику "Площадь" для указанного тела.
        /// </summary>
        private static void UpdateAreaProperty(Transaction tr, ObjectId bodyId, double area)
        {
            var obj = tr.GetObject(bodyId, OpenMode.ForRead);
            if (obj != null)
            {
                var propertySets = PropertyDataServices.GetPropertySets(obj);
                foreach (ObjectId psId in propertySets)
                {
                    var ps = tr.GetObject(psId, OpenMode.ForWrite) as PropertySet;
                    if (ps != null && ps.PropertySetDefinitionName == "Информация_о_модели_армогрунта") // Замените на имя вашего набора
                    {
                        var propId = ps.PropertyNameToId("Площадь");
                        if (propId != null)
                        {
                            ps.SetAt(propId, area);
                        }
                    }
                }
            }
        }
        private static void UpdateVolumeProperty(Transaction tr, ObjectId solidId, double volume)
        {
            var obj = tr.GetObject(solidId, OpenMode.ForRead);
            if (obj != null)
            {
                var propertySets = PropertyDataServices.GetPropertySets(obj);
                foreach (ObjectId psId in propertySets)
                {
                    var ps = tr.GetObject(psId, OpenMode.ForWrite) as PropertySet;
                    if (ps != null && ps.PropertySetDefinitionName == "Информация_о_модели_армогрунта") // Замените на имя вашего набора
                    {
                        var propId = ps.PropertyNameToId("Объем");
                        if (propId != null)
                        {
                            ps.SetAt(propId, volume);
                        }
                    }
                }
            }
        }

    }

}

