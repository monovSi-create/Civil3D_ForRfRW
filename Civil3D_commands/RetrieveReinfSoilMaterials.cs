using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.VisualBasic;

using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using static Autodesk.AutoCAD.LayerManager.LayerFilter;
using System.Collections.Specialized;
using System.Xml.Linq;
using Autodesk.Aec.ApplicationServices;
using Autodesk.AutoCAD.Geometry;
using System.Text.RegularExpressions;
using System.Diagnostics.Eventing.Reader;
using Surface = Autodesk.Civil.DatabaseServices.Surface;
using System.Web;
using System.Drawing;
using System.Security.Cryptography.X509Certificates;

namespace Civil3D_commands
{
    public class ExtractPolylines
    {
        [CommandMethod("RW_WallPolylines")]
        public static void ExtractWallPolylines()
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

        }
        public static List<Polyline> RetrieveWallPolylines(Subassembly subassembly, Baseline baseline, BaselineRegion region)
        {
            return null;
        }
    }
    public class RetrieveReinfSoilMaterials
    {
        [CommandMethod("RW_MATERIALS")]
        public static void RetriveWallMaterials()
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            string[] materials =
            {
                "gravel",
                "sand",
                "geotextile",
                "RE520",
                "RE540",
                "RE560",
                "RE570",
                "RE580",
                "Bodkins",
                "blue-connectors",
                "pipe",
                "geomembrane",
                "face"
            };

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                CivilDocument civDoc = CivilApplication.ActiveDocument;
                CorridorCollection corridors = civDoc.CorridorCollection;
                Corridor corridor = CorridorSurfaceCreator.SelectCorridor(tr, corridors);
                if (corridor == null) return;
                AssemblyCollection assemblyCollect = civDoc.AssemblyCollection;
                CorridorSurfaceCollection corridorSurfaces = corridor.CorridorSurfaces;
                BaselineCollection baselines = corridor.Baselines;
                //создаем таблицу для вычисленных значений
                foreach (Baseline baseline in baselines)
                {
                    //счетчик для каждой области
                    int regCount = 1;
                    BaselineRegionCollection baselineRegions = baseline.BaselineRegions;
                    foreach (BaselineRegion region in baselineRegions)
                    {
                        //объявляем общую коллекцию материалов
                        List<WallMaterialData> totalRegData = new List<WallMaterialData>();
                        //имя области
                        string regName = region.Name;
                        List<Solid3d> solid3Ds = new List<Solid3d>();
                        //запрос 3д-тел рассматриваемого участка у пользователя
                        try
                        {
                            TypedValue[] tvs = new TypedValue[]
                            {
                                new TypedValue((int)DxfCode.Start, "3DSOLID")
                            };
                            SelectionFilter filter = new SelectionFilter(tvs);
                            //просим пользователя выделить область
                            PromptSelectionOptions promptOpt = new PromptSelectionOptions();
                            promptOpt.MessageForAdding = $"\nВыберите 3D тела на Участке {regCount} : ";

                            PromptSelectionResult promptRes = ed.GetSelection(promptOpt, filter);
                            if (promptRes.Status == PromptStatus.OK)
                            {
                                //return;
                                //получаем ID выбраных объектов
                                ObjectId[] solidIds = promptRes.Value.GetObjectIds();
                                ed.WriteMessage($"\nВыбрано 3D тел: {solidIds.Length}");
                                foreach (ObjectId solidId in solidIds)
                                {
                                    try
                                    {
                                        Solid3d solid = tr.GetObject(solidId, OpenMode.ForRead) as Solid3d;
                                        solid3Ds.Add(solid);
                                    }
                                    catch { }
                                }
                            }
                        }
                        catch 
                        {  
                            ed.WriteMessage("\nВыбор отменен или 3D тело не найдено");    
                        }
                        //получаем коллекцию примененных элементов конструкции в области
                        List<Subassembly> currentSubassemblies = SubassemblyRetrieve(tr, region);
                        //извлечение материалов
                        foreach (Subassembly currentSubassembly in currentSubassemblies)
                        {
                            if (currentSubassembly.Name == "SubBaseElement")
                            {
                                var squareList = RetrieveSquareMaterials(corridor, region, tr, regCount);
                                var volumeList = RetrieveVolumeMaterials(solid3Ds, ed, tr, regCount);
                                totalRegData.AddRange(squareList);
                                totalRegData.AddRange(volumeList);
                            }
                            else if (currentSubassembly.Name == "FoundationElement")
                            {
                                var squareList = RetrieveSquareMaterials(corridor, region, tr, regCount);
                                var volumeList = RetrieveVolumeMaterials(solid3Ds, ed, tr, regCount);
                                totalRegData.AddRange(squareList);
                                totalRegData.AddRange(volumeList);
                            }
                            else if (currentSubassembly.Name == "WallFromLowerToUpperProfile")
                            {
                                var lineList = RetrieveGeogridMaterials(currentSubassembly, baseline, region, regCount);
                                var squareList = RetrieveSquareMaterials(corridor, region, tr, regCount);
                                var volumeList = RetrieveVolumeMaterials(solid3Ds, ed, tr, regCount);
                                totalRegData.AddRange(squareList);
                                totalRegData.AddRange(volumeList);
                                totalRegData.AddRange(lineList);
                            }
                            else if (currentSubassembly.Name == "FacingElementMB")
                            {
                                var squareList = RetrieveSquareMaterials(corridor, region, tr, regCount);
                                var volumeList = RetrieveVolumeMaterials(solid3Ds, ed, tr, regCount);
                                totalRegData.AddRange(squareList);
                                totalRegData.AddRange(volumeList);
                            }
                            else if (currentSubassembly.Name == "TopElement")
                            {
                                var volumeList = RetrieveVolumeMaterials(solid3Ds, ed, tr, regCount);
                                totalRegData.AddRange(volumeList);
                            }
                        }
                        //вызываем агрегатор
                        totalRegData = totalRegData.Where(m => m.Quantity != 0).ToList();
                        //создание таблицы
                        Autodesk.AutoCAD.DatabaseServices.Table table = TableMaterials(Aggregate(totalRegData), db);
                        table.Cells[0,0].TextString = "Участок" + regCount.ToString();
                        //применяем предпросмотр таблицы
                        TableJig jig = new TableJig(table);
                        PromptResult res = ed.Drag(jig);
                        if (res.Status == PromptStatus.OK)
                        {
                            BlockTable bt = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                            BlockTableRecord btr = tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite) as BlockTableRecord;
                            btr.AppendEntity(table);
                            tr.AddNewlyCreatedDBObject(table, true);
                        }
                        regCount++;
                    }
                    //извлекаем объемные тела
                    /*
                                List<CalculatedShape> crushedstoneShapeColl = new List<CalculatedShape>();
                                List<CalculatedShape> sandShapeColl = new List<CalculatedShape>();
                        foreach(AppliedAssembly appliedAssembly in region.AppliedAssemblies)
                        {

                            foreach(AppliedSubassembly appSub in appliedAssembly.GetAppliedSubassemblies())
                            {
                                List<LoftProfile> loftProfs = new List<LoftProfile>();
                                foreach (Autodesk.Civil.DatabaseServices.CalculatedShape calcShape in appSub.Shapes)
                                {
                                    string[] soilMaterials =
                                    {
                                       "sand",
                                       "gravel"
                                    };
                                    if (FindMaterial(calcShape.CorridorCodes.ToString(), soilMaterials) == "sand")
                                    {
                                        string layer = FindLayer(calcShape.CorridorCodes.ToString(), regName);
                                        int layer1 = int.Parse(layer);
                                        sandShapeColl.add

                                    }
                                    else if (FindMaterial(calcShape.CorridorCodes.ToString(), soilMaterials) == "gravel")
                                    {
                                        string layer = FindLayer(calcShape.CorridorCodes.ToString(), regName);
                                        int layer1 = int.Parse(layer);

                                    }
                                        List<Polyline3d> polyList = new List<Polyline3d>();
                                    Point3dCollection point3DCollection = new Point3dCollection();
                                    Autodesk.AutoCAD.DatabaseServices.Shape shape= new Autodesk.AutoCAD.DatabaseServices.Shape();
                                    CalculatedLinkCollection clinkCollection = calcShape.CalculatedLinks;
                                    
                                    foreach(CalculatedLink clink in clinkCollection)
                                    {
                                        CalculatedPointCollection cpointColl = clink.CalculatedPoints;
                                        foreach (CalculatedPoint cpoint in cpointColl)
                                        {
                                            Point3d linkP = cpoint.XYZ;
                                            point3DCollection.Add(linkP);
                                        }
                                        Polyline3d polyForSolid = new Polyline3d(Poly3dType.SimplePoly,point3DCollection,true);
                                        polyList.Add(polyForSolid);
                                    }
                                    foreach(Polyline3d poly in polyList)
                                    {
                                        LoftProfile lProfile = new LoftProfile(poly);

                                        ObjectId profileId = poly.ObjectId;
                                        Autodesk.AutoCAD.DatabaseServices.Entity polyEnt = tr.GetObject(profileId,OpenMode.ForWrite) as Autodesk.AutoCAD.DatabaseServices.Entity;
                                        
                                        loftProfs.Add(lProfile);
                                        
                                    }
                                }                                

                                
                            }
                        }
                                ObjectIdCollection loftProfiles = new ObjectIdCollection();
                                Solid3d solid = new Solid3d();
                                LoftOptions options = new LoftOptions();
                                solid.CreateLoftedSolid(loftProfs.ToArray(), null, null, options);
                        */
                }
                tr.Commit();
            }
        }

   
        public static double TotalLength(ObjectIdCollection collection)
        {
            double totalLength = 0;
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId item in collection)
                {
                    Polyline3d pl = tr.GetObject(item, OpenMode.ForRead) as Polyline3d;
                    totalLength += pl.Length;
                }
                tr.Commit();
            }
            return totalLength;
        }
        public static double TotalSquare(List<double> squares)
        {
            double totalSquare = 0;
            foreach(double square in squares)
            {
                totalSquare += square;
            }
            return totalSquare;
        }
        public static List<Subassembly> SubassemblyRetrieve(Transaction tr, BaselineRegion region)
        {
            List<Subassembly> subassemblies = new List<Subassembly>();
            ObjectId appliedAssemblyId = region.AssemblyId;
            Assembly assembly = tr.GetObject(appliedAssemblyId, OpenMode.ForWrite) as Assembly;
            AssemblyGroupCollection assemblyGroups = assembly.Groups;

            foreach (AssemblyGroup assemblyGroup in assemblyGroups)
            {
                ObjectIdCollection subassebmlyIds = assemblyGroup.GetSubassemblyIds();
                foreach (ObjectId subassemblyId in subassebmlyIds)
                {
                    Subassembly subassembly = tr.GetObject(subassemblyId, OpenMode.ForWrite) as Subassembly; 
                    ParamStringCollection paramStringColl = subassembly.ParamsString;
                    foreach (ParamString paramString in paramStringColl)
                    {
                        if (paramString.DisplayName == "Имя участка")
                        {
                            subassemblies.Add(subassembly);
                        }
                    }
                }
            }
            return subassemblies;
        }
        public static bool IsRegoinConsistFTL(string inputFTLName, string regionName)
        {
            if (Regex.IsMatch(inputFTLName, Regex.Escape(regionName)))
            {
                return true;
            }
            return false;
        }
        public static string FindLayer(string inputCodeName, string inputRegionName)
        {
            string searchPattern = $"(?<={Regex.Escape(inputRegionName)}_)" + @"\d+(?=_)";
            Match match = Regex.Match(inputCodeName, searchPattern);
            return match.Success ? match.Value : null;
        }
        public static string FindMaterial(string input, string[] array)
        {
            foreach (string str in array)
            {
                if (Regex.IsMatch(input, Regex.Escape(str)))
                {
                    return str;
                }
            }
            return null;
        }
        public static List<WallMaterialData> RetrieveGeogridMaterials(Subassembly subassembly, Baseline baseline, BaselineRegion region, int regionCount)
        {
            ObjectIdCollection RE520 = new ObjectIdCollection();
            ObjectIdCollection RE540 = new ObjectIdCollection();
            ObjectIdCollection RE560 = new ObjectIdCollection();
            ObjectIdCollection RE570 = new ObjectIdCollection();
            ObjectIdCollection RE580 = new ObjectIdCollection();
            ParamDoubleCollection paramDColl = subassembly.ParamsDouble;
            double gridLength = 0;
            foreach (ParamDouble paramD in paramDColl)
            {
                if (paramD.DisplayName == "Длина георешеток")
                {
                    gridLength = paramD.Value;
                }
            }
            FeatureLineCollectionMap ftLCollMap = baseline.MainBaselineFeatureLines.FeatureLineCollectionMap;
            foreach (FeatureLineCollection ftLColl in ftLCollMap)
            {
                //перебераем все характерные линии, отбираем по необходимому условию и передаем в соответствующий столбец таблицы
                foreach (CorridorFeatureLine corrftL in ftLColl)
                {
                    if (IsRegoinConsistFTL(corrftL.CodeName, region.Name))
                    {

                        if (Regex.IsMatch(corrftL.CodeName, Regex.Escape("RE5201")))
                        {
                            RE520.Add(corrftL.ExportAsPolyline3dCollection()[0]);
                        }
                        else if (Regex.IsMatch(corrftL.CodeName, Regex.Escape("RE5401")))
                        {
                            RE540.Add(corrftL.ExportAsPolyline3dCollection()[0]);
                        }
                        else if (Regex.IsMatch(corrftL.CodeName, Regex.Escape("RE5601")))
                        {
                            RE560.Add(corrftL.ExportAsPolyline3dCollection()[0]);
                        }
                        else if (Regex.IsMatch(corrftL.CodeName, Regex.Escape("RE5701")))
                        {
                            RE570.Add(corrftL.ExportAsPolyline3dCollection()[0]);
                        }
                        else if (Regex.IsMatch(corrftL.CodeName, Regex.Escape("RE5801")))
                        {
                            RE580.Add(corrftL.ExportAsPolyline3dCollection()[0]);
                        }
                    }
                }
            }
            //вычисляем площадь по каждому типу решетки

            double RE520square = Math.Round(TotalLength(RE520) * gridLength, 1);
            double RE540square = Math.Round(TotalLength(RE540) * gridLength, 1);
            double RE560square = Math.Round(TotalLength(RE560) * gridLength, 1);
            double RE570square = Math.Round(TotalLength(RE570) * gridLength, 1);
            double RE580square = Math.Round(TotalLength(RE580) * gridLength, 1);

            double blueConnector = Math.Round((TotalLength(RE580) + TotalLength(RE570) + TotalLength(RE560) + TotalLength(RE540) + TotalLength(RE520)) * 5, 0);

            double bodkins = Math.Ceiling((RE580square + RE570square + RE560square + RE540square) / 65 + RE520square / 97.5);

            var materials = new List<WallMaterialData>();
            materials.Add(new WallMaterialData("Георешетка RE520", "описание", "м2", RE520square, regionCount));
            materials.Add(new WallMaterialData("Георешетка RE540", "описание", "м2", RE540square, regionCount));
            materials.Add(new WallMaterialData("Георешетка RE560", "описание", "м2", RE560square, regionCount));
            materials.Add(new WallMaterialData("Георешетка RE570", "описание", "м2", RE570square, regionCount));
            materials.Add(new WallMaterialData("Георешетка RE580", "описание", "м2", RE580square, regionCount));
            materials.Add(new WallMaterialData("Bodkin", "описание", "шт", bodkins, regionCount));
            materials.Add(new WallMaterialData("Blue-connector", "описание", "шт", blueConnector, regionCount));

            return materials;
        }
        public static List<WallMaterialData> RetrieveSquareMaterials(Corridor corridor, BaselineRegion region, Transaction transaction, int regionCount)
        {
            CorridorSurfaceCollection corridorSurfaces = corridor.CorridorSurfaces;
            //извлекаем кол-во площадных материалов на участке
            List<double> geotextileList = new List<double>();
            List<double> triaxList = new List<double>();
            List<double> gidroizolList = new List<double>();
            foreach (CorridorSurface corSurf in corridorSurfaces)
            {
                if (Regex.IsMatch(corSurf.Name, Regex.Escape("geotextile")) && IsRegoinConsistFTL(corSurf.Name, region.Name))
                {
                    TinSurface surf = transaction.GetObject(corSurf.SurfaceId, OpenMode.ForRead) as TinSurface;
                    if (surf != null)
                    {
                        try
                        {
                            TerrainSurfaceProperties props = surf.GetTerrainProperties();
                            geotextileList.Add(props.SurfaceArea3D);
                        }
                        catch { }
                    }
                }
                else if (Regex.IsMatch(corSurf.Name, Regex.Escape("триакс"), RegexOptions.IgnoreCase) && IsRegoinConsistFTL(corSurf.Name, region.Name))
                {
                    TinSurface triax = transaction.GetObject(corSurf.SurfaceId, OpenMode.ForRead) as TinSurface;
                    if (triax != null)
                    {
                        try
                        {
                            TerrainSurfaceProperties props = triax.GetTerrainProperties();
                            triaxList.Add(props.SurfaceArea3D);
                        }
                        catch { }
                    }
                }
                else if (Regex.IsMatch(corSurf.Name, Regex.Escape("gidroizol"), RegexOptions.IgnoreCase) && IsRegoinConsistFTL(corSurf.Name, region.Name))
                {
                    TinSurface gidroizol = transaction.GetObject(corSurf.SurfaceId, OpenMode.ForRead) as TinSurface;
                    if (gidroizol != null)
                    {
                        try
                        {
                            TerrainSurfaceProperties props = gidroizol.GetTerrainProperties();
                            gidroizolList.Add(props.SurfaceArea3D);
                        }
                        catch { }
                    }
                }
            }
            double geotextileAtRegion = Math.Round(TotalSquare(geotextileList), 1);
            double triaxAtRegion = Math.Round(TotalSquare(triaxList), 1);
            double gidroizolAtRegion = Math.Round(TotalSquare(gidroizolList), 1);
            var materials = new List<WallMaterialData>();

            materials.Add(new WallMaterialData("Нетканый геотекстиль","описание","м2",geotextileAtRegion, regionCount));
            materials.Add(new WallMaterialData("Триакс", "описание", "м2", triaxAtRegion, regionCount));
            materials.Add(new WallMaterialData("Обмазочная гидроизоляция фундамента", "описание", "м2", gidroizolAtRegion, regionCount));

            return materials;
        }
        public static List<WallMaterialData> RetrieveVolumeMaterials(List<Solid3d> solids, Editor editor, Transaction tr, int regionCount)
        {
            //выбираем 3д тела коридора
            //создание фильтра для создания только 3д солидов
            double solidSoil = 0;
            double solidDrenage = 0;
            double solidSubbase = 0;
            double solidFoundation = 0;
            double solidConcLeveling = 0;
            double solidConcBlock = 0;
            double solidConcAboveBlock = 0;
            double solidConcTop = 0;
            //перебираем все 3дСолиды выбраного участка
            foreach (Solid3d solid in solids)
            {
                //получаем объем 3д солида
                double volume = solid.MassProperties.Volume;
                string solidLayerName = solid.Layer;
                //в зависимости от имени слоя на котором лежит 3д тело добавляем объем к общему кол-ву материала
                if (Regex.IsMatch(solidLayerName, "Дренирующий грунт", RegexOptions.IgnoreCase))
                {
                    solidSoil += volume;
                }
                else if (Regex.IsMatch(solidLayerName, "Щебень дренажной призмы", RegexOptions.IgnoreCase))
                {
                    solidDrenage += volume;
                }
                else if (Regex.IsMatch(solidLayerName, "Щебень основания", RegexOptions.IgnoreCase))
                {
                    solidSubbase += volume;
                }
                else if (Regex.IsMatch(solidLayerName, "Фундамент", RegexOptions.IgnoreCase))
                {
                    solidFoundation += volume;
                }
                else if (Regex.IsMatch(solidLayerName, "Цементная подготовка", RegexOptions.IgnoreCase))
                {
                    solidConcLeveling += volume;
                }
                else if (Regex.IsMatch(solidLayerName, "Облицовочный блок", RegexOptions.IgnoreCase))
                {
                    solidConcBlock += volume;
                }
                else if (Regex.IsMatch(solidLayerName, "Выравнивающая лента", RegexOptions.IgnoreCase))
                {
                    solidConcAboveBlock += volume;
                }
                else if (Regex.IsMatch(solidLayerName, "Шапочный блок", RegexOptions.IgnoreCase))
                {
                    solidConcTop += volume;
                }
            }
            var materials = new List<WallMaterialData>();
            materials.Add(new WallMaterialData("Дренирующий грунт", "описание", "м3", solidSoil, regionCount));
            materials.Add(new WallMaterialData("Щебень дренажной призмы", "описание", "м3", solidDrenage, regionCount));
            materials.Add(new WallMaterialData("Щебень основания", "описание", "м3", solidSubbase, regionCount));
            materials.Add(new WallMaterialData("Фундамент", "описание", "м3", solidFoundation, regionCount));
            materials.Add(new WallMaterialData("Цементная подготовка", "описание", "м3", solidConcLeveling, regionCount));
            materials.Add(new WallMaterialData("Облицовочный блок", "описание", "м3", solidConcBlock, regionCount));
            materials.Add(new WallMaterialData("Выравнивающая лента", "описание", "м3", solidConcAboveBlock, regionCount));
            materials.Add(new WallMaterialData("Шапочный блок", "описание", "м3", solidConcTop, regionCount));
            return materials;
        }
        public static List<WallMaterialData> Aggregate(List<WallMaterialData> wallMaterials)
        {
            var aggregated = wallMaterials.GroupBy(m => m.MaterialName).Select(g => new WallMaterialData(g.Key, g.First().MaterialDescription, g.First().Unit, g.Sum(m => m.Quantity), g.First().Region)).ToList();
            return aggregated;
        }
        public static Autodesk.AutoCAD.DatabaseServices.Table TableMaterials(List<WallMaterialData> materials, Database db)
        {
            //создаем таблицу
            Autodesk.AutoCAD.DatabaseServices.Table table = new Autodesk.AutoCAD.DatabaseServices.Table();
            table.TableStyle = db.Tablestyle;
            table.SetSize(2, 6);
            table.Position = new Point3d(0, 0, 0);
            //задаем кол-во столбцов
            table.Cells[1, 0].TextString = "Номер";
            table.Cells[1, 1].TextString = "Обозначение";
            table.Cells[1, 2].TextString = "Наименование";
            table.Cells[1, 3].TextString = "Ед.изм";
            table.Cells[1, 4].TextString = "Кол-во";
            table.Cells[1, 5].TextString = "Примечние";
            table.SetColumnWidth(3);
            //перебираем материалы и записываем в таблицу
            for (int i = 0; i < materials.Count; i++)
            {
                int row = i+2;
                table.InsertRows(row, 0.5, 1);
                table.SetValue(row, 0, i + 1);
                table.SetValue(row, 1, materials[i].MaterialDescription);
                table.SetValue(row, 2, materials[i].MaterialName);
                table.SetValue(row, 3, materials[i].Unit);
                table.SetValue(row, 4, materials[i].Quantity);

            }
            return table;           
        }
    }
    //Класс хранения информации о материале
    public class WallMaterialData
    {
        //Название материала, например "Щебень"
        public string MaterialName {  get; set; }
        //Описание материала, например, ГОСТ или какие-то физические свойства
        public string MaterialDescription { get; set; }
        //Единицы измерения (м3, кг и т.п.)
        public string Unit { get; set; }
        //Количество материала
        public double Quantity { get; set; }
        public int Region { get; }
        //Конструктор для инициализации всех параметров
        public WallMaterialData(string materialName, string materialDescription, string unit, double quantity, int regionCount)
        {
            MaterialName = materialName;
            MaterialDescription = materialDescription;
            Unit = unit;
            Quantity = quantity;
            Region = regionCount;
        }
    }
   
    public class TableJig : EntityJig
    {
        private Autodesk.AutoCAD.DatabaseServices.Table _table;
        private Point3d _currentPosition;
        public TableJig(Autodesk.AutoCAD.DatabaseServices.Table table) : base(table)
        {
            _table = table;
            _currentPosition = _table.Position;
        }
        protected override SamplerStatus Sampler(JigPrompts prompts)
        {
            JigPromptPointOptions jigOpts = new JigPromptPointOptions("\nУкажите точку для размещения таблицы");
            PromptPointResult res = prompts.AcquirePoint(jigOpts);
            if (res.Status==PromptStatus.OK)
            {
                if (_currentPosition.IsEqualTo(res.Value))
                {
                    return SamplerStatus.NoChange;
                }
                else
                {
                    _currentPosition = res.Value;
                    return SamplerStatus.OK;
                }
            }
            return SamplerStatus.Cancel;

        }
        protected override bool Update()
        {
            _table.Position = _currentPosition;
            return true;
        }

    }
}