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
//using Autodesk.Aec.ApplicationServices;
using Autodesk.AutoCAD.Geometry;
using System.Text.RegularExpressions;
using System.Diagnostics.Eventing.Reader;
using System.Drawing;
using Autodesk.Civil.DatabaseServices.Styles;
using static System.Collections.Specialized.BitVector32;
//using Autodesk.Aec.Geometry;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.StartPanel;
using Civil3D_commands;
//using System.Reflection;

[assembly: CommandClass(typeof(BaselineCreator))]
[assembly: CommandClass(typeof(CorridorRegionSlicer))]


namespace Civil3D_commands
{
    public class CorridorSurfaceCreator
    {
        [CommandMethod("RW_CREATESURFACES")]
        public static void CreateSurfaceFromSelectedCorridor()
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                CivilDocument civDoc = CivilApplication.ActiveDocument;
                CorridorCollection corridors = civDoc.CorridorCollection;
                if (corridors.Count == 0)
                {
                    ed.WriteMessage("Нет доступных коридоров.");
                    return;
                }
                // Выбор коридора через диалоговое окно
                Corridor corridor = SelectCorridor(tr, corridors);
                if (corridor == null) return;

                CorridorSurfaceCollection corridorSurfaces = corridor.CorridorSurfaces;
                if (corridorSurfaces.Count != 0)
                {
                    List<string> surfToRemove = new List<string>();
                    int c = corridorSurfaces.Count;
                    for (int i = 0; i < c; i++)
                    {
                        surfToRemove.Add(corridorSurfaces[i].Name);
                    }
                    foreach (string name in surfToRemove)
                    {
                        corridorSurfaces.Remove(name);
                    }
                }
                // Выбор звена коридора
                string[] linkNames = SelectLinksCodes(corridor);
                if (linkNames == null || linkNames.Length == 0) return;

                //CalculatedLinkCollection allLinks = null;
                //часть кода для получения составных объектов в конкретных сечениях коридора
                /* 
                BaselineCollection baselines = corridor.Baselines;

                foreach (Baseline baseline in baselines)
                {
                    ObjectId algnId = baseline.AlignmentId;
                    BaselineRegionCollection baselineRegions = baseline.BaselineRegions;
                    foreach (BaselineRegion baselineRegion in baselineRegions)
                    {
                        AppliedAssemblyCollection appliedAssemblies = baselineRegion.AppliedAssemblies;
                        double[] appliedStations = appliedAssemblies.Stations();
                        List<double> stationList = appliedStations.ToList();
                        stationList.Sort();

                        foreach (double station in stationList)
                        {
                            AppliedAssembly appliedAssembly = appliedAssemblies.GetItemAt(station);
                            Assembly assembly = (Assembly)tr.GetObject(appliedAssembly.AssemblyId, OpenMode.ForRead);
                            allLinks = appliedAssembly.GetLinksByCode("1DgeotextileUp");

                            AppliedSubassemblyCollection appliedSubassemblies = appliedAssembly.GetAppliedSubassemblies();
                            foreach (AppliedSubassembly appliedSubassembly in appliedSubassemblies)
                            {
                                Subassembly subassembly = (Subassembly)tr.GetObject(appliedSubassembly.SubassemblyId, OpenMode.ForRead);
                                LinkCollection subLinks = subassembly.Links;
                                
                            }
                        }
                    }
                }
                */
                foreach (string linkName in linkNames)
                {
                    //Перебираем каждое проименованное звено коридора 
                    //Создаем имена поверхностям коридора соответствующее именам звеньев
                    string surfaceName = corridor.Name + " " + linkName;
                    //Создаем поверхности коридора с этими именами по выбраным звеньям 
                    CorridorSurface newCorSurf = corridorSurfaces.Add(surfaceName);
                    newCorSurf.AddLinkCode(linkName, false);
                    //добываем базовые линии коридора и создаем границы соответствующих поверхностей коридора
                    BaselineCollection baselines = corridor.Baselines;
                    CorridorSurfaceBoundaryCollection boundaryCollection = newCorSurf.Boundaries;
                    CorridorSurfaceBoundary newBound = boundaryCollection.Add(linkName + " boundary");
                    foreach (Baseline baseline in baselines)
                    {
                        BaselineRegionCollection baseLRColl = baseline.BaselineRegions;
                        if (baseLRColl.Count >= 2)
                        {
                            foreach (BaselineRegion region in baseLRColl)
                            {
                                string regionName = region.Name;
                                FeatureLineCollectionMap ftlCollMap = baseline.MainBaselineFeatureLines.FeatureLineCollectionMap;
                                bool reverseFTLFlug = false;
                                foreach (FeatureLineCollection ftlColl in ftlCollMap)
                                {
                                    foreach (CorridorFeatureLine corrFtl in ftlColl)
                                    {
                                        //
                                        if (CompareStrings(linkName, corrFtl.CodeName, regionName) & IsRegionConsistFTL(corrFtl.CodeName, regionName))
                                        {
                                            FeatureLineComponent ftlComp1 = newBound.FeatureLineComponents.Add(corrFtl);
                                            ftlComp1.IsReversed = reverseFTLFlug;
                                            reverseFTLFlug = true;
                                            //newCorSurf.OverhangCorrection = OverhangCorrectionType.TopLinks;
                                            try
                                            {
                                                newCorSurf.OverhangCorrection = SurfOverhanging(linkName);
                                            }
                                            catch
                                            {
                                                newCorSurf.OverhangCorrection = OverhangCorrectionType.TopLinks;
                                            }
                                            /*string[] arrayForSand = { "sand" };
                                            if (FindMaterial(linkName, arrayForSand) == "sand")
                                            {
                                                newBound.BoundaryType = CorridorSurfaceBoundaryType.InsideBoundary;
                                             }
                                            */
                                        }
                                    }
                                }
                            }

                        }
                        else
                        {
                            FeatureLineCollectionMap ftlCollMap = baseline.MainBaselineFeatureLines.FeatureLineCollectionMap;
                            bool reverseFTLFlug = false;
                            foreach (FeatureLineCollection ftlColl in ftlCollMap)
                            {
                                foreach (CorridorFeatureLine corrFtl in ftlColl)
                                {

                                    if (CompareStringsNoRegion(linkName, corrFtl.CodeName))
                                    {
                                        FeatureLineComponent ftlComp1 = newBound.FeatureLineComponents.Add(corrFtl);
                                        ftlComp1.IsReversed = reverseFTLFlug;
                                        reverseFTLFlug = true;
                                        //newCorSurf.OverhangCorrection = OverhangCorrectionType.TopLinks;
                                        try
                                        {
                                            newCorSurf.OverhangCorrection = SurfOverhanging(linkName);
                                        }
                                        catch
                                        {
                                            newCorSurf.OverhangCorrection = OverhangCorrectionType.TopLinks;
                                        }
                                    }
                                }
                            }

                        }
                    }
                    ed.WriteMessage($"Создана поверхность: {newCorSurf.Name}");
                }



                // Создание поверхности с именем звена
                //CorridorSurfaceCollection surfaces = corridor.CorridorSurfaces;
                //CorridorSurface surface = surfaces.Add(surfaceName); // Имя поверхности = имя звена
                //surface.AddLinkCode(linkName, true);

                tr.Commit();
            }
            // Применяем коррекцию свеса: корректируем Z для всех точек поверхности
            //ApplySlopeCorrection(surface, slopeCorrection);

        }

        private static bool CompareStrings(string linkName, string pointName, string regionName)
        {
            string[] materials =
            {   "gravel",
                "sand",
                "Drenagelayer",
                "geotextile",
                "RE",
                "Триакс",
                "gidroizol",
                "geomembrane"
            };

            string[] upDown =
            {
                "Up",
                "Down"
            };

            string linkMaterial = FindMaterial(linkName, materials);
            string linkElev = null;
            try
            {
                linkElev = FindElevation(linkName, upDown);
            }
            catch
            {

            }
            string linkLayer = FindLayer(linkName, regionName);

            string pointMaterial = FindMaterial(pointName, materials);
            string pointLayer = FindLayer(pointName, regionName);
            string lastPoint = FindPointInEnd(pointName);

            if (linkMaterial == pointMaterial & linkLayer == pointLayer)
            {
                //отбор конкретных точек(хар-ных линий) в зависимости от материала

                //для геотекстиля
                if (linkMaterial == "geotextile")
                {
                    if (linkElev == "Up")
                    {
                        string lastPoint1 = "2";
                        string lastPoint2 = "4";
                        if (lastPoint == lastPoint1 || lastPoint == lastPoint2)
                        {
                            return true;
                        }
                    }
                    else if (linkElev == "Down")
                    {
                        string lastPoint1 = "1";
                        string lastPoint2 = "2";
                        if (lastPoint == lastPoint1 || lastPoint == lastPoint2)
                        {
                            return true;
                        }
                    }
                }

                //для щебня
                if (linkMaterial == "gravel")
                {
                    string lastPoint1 = "1";
                    string lastPoint2 = "3";
                    if (lastPoint == lastPoint1 || lastPoint == lastPoint2)
                    {
                        return true;
                    }
                }
                //для песка
                if (linkMaterial == "sand")
                {
                    string lastPoint1 = "1";
                    string lastPoint2 = "3";
                    if (lastPoint == lastPoint1 || lastPoint == lastPoint2)
                    {
                        return true;
                    }
                }
                //для гидроизоляции
                else if(linkMaterial == "gidroizol")
                {
                    string lastPoint1 = "1";
                    string lastPoint2 = "2";
                    if (lastPoint == lastPoint1 || lastPoint == lastPoint2)
                    {
                        return true;
                    }
                }
                //для триакс
                else if(linkMaterial == "Триакс")
                {
                    string lastPoint1 = "1";
                    string lastPoint2 = "2";
                    if (lastPoint == lastPoint1 || lastPoint == lastPoint2)
                    {
                        return true;
                    }
                }
            }
            //для георешетки
            if (linkMaterial == "RE" && linkLayer == pointLayer)
            {
                string linkGridType = FindREType(linkName);
                string pointGridType = FindREType(pointName);
                if (linkGridType == pointGridType)
                {
                    string lastPoint1 = "1";
                    string lastPoint2 = "2";
                    if (lastPoint == lastPoint1 || lastPoint == lastPoint2)
                    {
                        return true;
                    }
                }
            }

            //для разделений слоев засыпки
            else if (linkMaterial == "Drenagelayer")
            {
                if (pointMaterial == "gravel" && linkLayer == pointLayer)
                {
                    string lastPoint1 = "1";
                    if (lastPoint == lastPoint1)
                    {
                        return true;
                    }
                }
                else if (pointMaterial == "sand" && linkLayer == pointLayer)
                {
                    string lastPoint2 = "3";
                    if (lastPoint == lastPoint2)
                    {
                        return true;
                    }
                }

            }
            return false;
        }
        private static bool CompareStringsNoRegion(string linkName, string pointName)
        {
            string[] materials =
            {   "gravel",
                "sand",
                "Drenagelayer",
                "geotextile",
                "RE"
            };

            string[] upDown =
            {
                "Up",
                "Down"
            };

            string linkMaterial = FindMaterial(linkName, materials);
            string linkElev = null;
            try
            {
                linkElev = FindElevation(linkName, upDown);
            }
            catch
            {

            }
            string linkLayer = FindLayerNoRegion(linkName);
            string pointMaterial = FindMaterial(pointName, materials);
            string pointLayer = FindLayerNoRegion(pointName);
            string lastPoint = FindPointInEnd(pointName);

            if (linkMaterial == pointMaterial & linkLayer == pointLayer)
            {
                //отбор конкретных точек(хар-ных линий) в зависимости от материала

                //для геотекстиля
                if (linkMaterial == "geotextile")
                {
                    if (linkElev == "Up")
                    {
                        string lastPoint1 = "2";
                        string lastPoint2 = "4";
                        if (lastPoint == lastPoint1 || lastPoint == lastPoint2)
                        {
                            return true;
                        }
                    }
                    else if (linkElev == "Down")
                    {
                        string lastPoint1 = "1";
                        string lastPoint2 = "2";
                        if (lastPoint == lastPoint1 || lastPoint == lastPoint2)
                        {
                            return true;
                        }
                    }
                }

                //для щебня
                if (linkMaterial == "gravel")
                {
                    string lastPoint1 = "1";
                    string lastPoint2 = "3";
                    if (lastPoint == lastPoint1 || lastPoint == lastPoint2)
                    {
                        return true;
                    }
                }
                //для песка
                if (linkMaterial == "sand")
                {
                    string lastPoint1 = "1";
                    string lastPoint2 = "3";
                    if (lastPoint == lastPoint1 || lastPoint == lastPoint2)
                    {
                        return true;
                    }
                }
            }
            //для георешетки
            if (linkMaterial == "RE" && linkLayer == pointLayer)
            {
                string linkGridType = FindREType(linkName);
                string pointGridType = FindREType(pointName);
                if (linkGridType == pointGridType)
                {
                    string lastPoint1 = "1";
                    string lastPoint2 = "2";
                    if (lastPoint == lastPoint1 || lastPoint == lastPoint2)
                    {
                        return true;
                    }
                }
            }

            //для разделений слоев засыпки
            else if (linkMaterial == "Drenagelayer")
            {
                if (pointMaterial == "gravel" && linkLayer == pointLayer)
                {
                    string lastPoint1 = "1";
                    if (lastPoint == lastPoint1)
                    {
                        return true;
                    }
                }
                else if (pointMaterial == "sand" && linkLayer == pointLayer)
                {
                    string lastPoint2 = "3";
                    if (lastPoint == lastPoint2)
                    {
                        return true;
                    }
                }

            }
            return false;
        }
        static string FindMaterial(string input, string[] array)
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
        static string FindElevation(string input, string[] array)
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
        static string FindLayer(string inputLinkName, string inputRegionName)
        {
            string searchPattern = $"(?<={Regex.Escape(inputRegionName)}_)" + @"\d+(?=_)";
            Match match = Regex.Match(inputLinkName, searchPattern);
            return match.Success ? match.Value : null;
        }
        static string FindLayerNoRegion(string inputLinkName)
        {
            string searchPattern = @"_(\d+)_";
            Match match = Regex.Match(inputLinkName, searchPattern);
            return match.Success ? match.Groups[1].Value : null;
        }
        static string FindPointInEnd(string input)
        {
            Match match = Regex.Match(input, @"\d{1}$");
            return match.Success ? match.Value : null;
        }
        static string FindREType(string input)
        {
            Match match = Regex.Match(input, @"RE(\d{3})");
            return match.Success ? match.Value : null;
        }
        static OverhangCorrectionType SurfOverhanging(string inputlinkName)
        {
            string[] upDown =
            {
                "Up",
                "Down"
            };
            if (FindElevation(inputlinkName, upDown) == "Up")
            {
                return OverhangCorrectionType.TopLinks;
            }
            else if (FindElevation(inputlinkName, upDown) == "Down")
            {
                return OverhangCorrectionType.BottomLinks;
            }
            else
            {
                return OverhangCorrectionType.None;
            }
        }
        static bool IsRegionConsistFTL(string inputFTLName, string regionName)
        {
            if (Regex.IsMatch(inputFTLName, Regex.Escape(regionName)))
            {
                return true;
            }
            return false;
        }
        // Выбор коридора через диалоговое окно
        public static Corridor SelectCorridor(Transaction tr, CorridorCollection corridors)
        {
            List<string> corridorNames = new List<string>();
            Dictionary<string, ObjectId> corridorDict = new Dictionary<string, ObjectId>();

            foreach (ObjectId corridorId in corridors)
            {
                Corridor corridor = tr.GetObject(corridorId, OpenMode.ForRead) as Corridor;
                if (corridor != null)
                {
                    corridorNames.Add(corridor.Name);
                    corridorDict[corridor.Name] = corridorId;
                }
            }

            string selectedName = ShowSelectionCorridorDialog("Выберите коридор", corridorNames);
            if (string.IsNullOrEmpty(selectedName))
                return null;

            return tr.GetObject(corridorDict[selectedName], OpenMode.ForRead) as Corridor;
        }

        // Выбор звена (Link Code) через диалоговое окно
        private static string[] SelectLinksCodes(Corridor corridor)
        {
            string[] linkCodes = corridor.GetLinkCodes();
            if (linkCodes.Length == 0)
            {
                Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage("Нет доступных звеньев.");
                return new string[] { };
            }

            List<string> codes = new List<string>();
            foreach (string code in linkCodes)
            {
                codes.Add(code);
            }

            string[] selectedLinks = ShowSelectionLinksDialog("Выберите звенья", codes);
            return selectedLinks;
        }
        private static string[] ShowSelectionLinksDialog(string title, List<string> options)
        {
            Form form = new Form()
            {
                Text = title,
                Size = new System.Drawing.Size(300, 200),
                StartPosition = FormStartPosition.CenterScreen
            };

            ListBox listBox = new ListBox()
            {
                Dock = DockStyle.Fill,
                SelectionMode = System.Windows.Forms.SelectionMode.MultiExtended
            };
            listBox.Items.AddRange(options.ToArray());

            Button buttonOK = new Button()
            {
                Text = "OK",
                Dock = DockStyle.Bottom
            };
            buttonOK.Click += (sender, e) => { form.DialogResult = System.Windows.Forms.DialogResult.OK; };

            form.Controls.Add(listBox);
            form.Controls.Add(buttonOK);

            if (form.ShowDialog() == System.Windows.Forms.DialogResult.OK && listBox.SelectedItems != null)
                return listBox.SelectedItems.Cast<string>().ToArray();
            return new string[] { };
        }
        private static string ShowSelectionCorridorDialog(string title, List<string> options)
        {
            Form form = new Form()
            {
                Text = title,
                Size = new System.Drawing.Size(300, 200),
                StartPosition = FormStartPosition.CenterScreen
            };

            ListBox listBox = new ListBox()
            {
                Dock = DockStyle.Fill
            };
            listBox.Items.AddRange(options.ToArray());

            Button buttonOK = new Button()
            {
                Text = "OK",
                Dock = DockStyle.Bottom
            };
            buttonOK.Click += (sender, e) => { form.DialogResult = System.Windows.Forms.DialogResult.OK; };

            form.Controls.Add(listBox);
            form.Controls.Add(buttonOK);

            if (form.ShowDialog() == System.Windows.Forms.DialogResult.OK && listBox.SelectedItem != null)
                return listBox.SelectedItem.ToString();

            return string.Empty;
        }
        //static string FindSubPropertiRegName()
    }
    public class BaselineRegionSubassemblyRenamer
    {
        [CommandMethod("RW_RENAMESUBS")]
        public static void RegionSubRenamer()
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {

                CivilDocument civDoc = CivilApplication.ActiveDocument;
                CorridorCollection corridors = civDoc.CorridorCollection;
                if (corridors.Count == 0)
                {
                    ed.WriteMessage("Нет доступных коридоров.");
                    return;
                }
                // Выбор коридора через диалоговое окно
                Corridor corridor = CorridorSurfaceCreator.SelectCorridor(tr, corridors);
                if (corridor == null) return;
                // Пробираемся к эл-там конструкции по пути сравнивая с ID конструкции внутри которой находится элемент, если в 
                // имени строкового параметра конструкции есть поле "Имя участка" - заменяем значение поля на имя области в которой эта конструкция определена 
                BaselineCollection baselines = corridor.Baselines;
                foreach (Baseline baseline in baselines)
                {
                    BaselineRegionCollection bRegCollection = baseline.BaselineRegions;
                    foreach (BaselineRegion region in bRegCollection)
                    {
                        ObjectId appliedAssemblyId = region.AssemblyId;
                        AssemblyCollection assemblyCollect = civDoc.AssemblyCollection;
                        foreach (ObjectId assemblyId in assemblyCollect)
                        {
                            if (assemblyId == appliedAssemblyId)
                            {
                                Assembly assembly = tr.GetObject(assemblyId, OpenMode.ForWrite) as Assembly;
                                //assembly.Name = region.Name;
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
                                                paramString.Value = region.Name;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        /*
                        AppliedAssemblyCollection assembl = region.AppliedAssemblies;
                        foreach (AppliedAssembly assembly in assembl)
                        {

                            AppliedSubassemblyCollection subassemblies = assembly.GetAppliedSubassemblies();
                            foreach (AppliedSubassembly subassembly in subassemblies)
                            {

                                /*
                                if (subassembly.Contains("Имя участка"))
                                {
                                    list1.Add(subassembly.GetParameter<string>("Имя участка").Value);
                                    string newSubName = region.Name;
                                    subassembly.GetParameter<string>("Имя участка").Value = newSubName;
                                    list2.Add(subassembly.GetParameter<string>("Имя участка").Value);
                                }
                            }
                        }
                        */
                    }
                }
                tr.Commit();
            }
        }
        /*
         public static SubassemblyCollection SelectionSubassemblyPropertyRename(Corridor corridor, string[] inputSubNames)
         {
             BaselineCollection baselines = corridor.Baselines;
             foreach (Baseline baseline in baselines)
             {
                 BaselineRegionCollection bRegCollection = baseline.BaselineRegions;
                 foreach (BaselineRegion region in bRegCollection)
                 {
                     ObjectId appliedAssemblyId = region.AssemblyId;
                     AssemblyCollection assemblyCollect = civDoc.AssemblyCollection;
                     foreach (ObjectId assemblyId in assemblyCollect)
                     {
                         if (assemblyId == appliedAssemblyId)
                         {
                             Assembly assembly = tr.GetObject(assemblyId, OpenMode.ForWrite) as Assembly;
                             //assembly.Name = region.Name;
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
                                             paramString.Value = region.Name;
                                         }
                                     }
                                 }
                             }
                         }
                     }
                 }
             }

             BaselineCollection baselines = corridor.Baselines;
             foreach (Baseline baseline in baselines)
             {
                 BaselineRegionCollection bRegCollection = baseline.BaselineRegions;
                 foreach (BaselineRegion region in bRegCollection)
                 {
                     AppliedAssemblyCollection assembl = region.AppliedAssemblies;
                     foreach (AppliedAssembly assembly in assembl)
                     {
                         AppliedSubassemblyCollection subassemblies = assembly.GetAppliedSubassemblies();
                         foreach (AppliedSubassembly subassembly in subassemblies)
                         {
                             string subAsDisplayName = string.Empty;
                             foreach (AppliedSubassemblyParam<string> subParam in subassembly.Parameters)
                             {
                                 string nameParam = subParam.DisplayName;
                                 //var stringParam = subParam as AppliedSubassemblyParam<string>;
                                 if (nameParam != null && !string.IsNullOrEmpty(nameParam))
                                 {
                                     subAsDisplayName = nameParam;
                                     break;
                                 }
                             }
                             foreach(string inputSubName in inputSubNames)
                             {
                                 if(subAsDisplayName.Equals(inputSubName, StringComparison.OrdinalIgnoreCase)) 
                                 {
                                     foreach (AppliedSubassemblyParam<string> subParam in subassembly.Parameters)
                                     {
                                         var targetParamName = subParam.KeyName;
                                         if(targetParamName != null && targetParamName.Equals("Имя участка", StringComparison.OrdinalIgnoreCase))
                                         { 
                                             string val = subParam.Value;
                                             val = region.Name;
                                             Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage(
                                                 $"\nПараметр 'Имя Участка'обновлен с '{subParam.Value}' на '{region.Name}' для элемента конструкции '{subAsDisplayName}'.");
                                             break;
                                         }
                                     }
                                 }
                             }                     
                         }
                     }
                 }
             }
             return null;
         }
        */
        private static string[] SelectCorridorSubassemblies(Corridor corridor)
        {
            List<string> subNames = new List<string>();
            List<ObjectId> subIds = new List<ObjectId>();
            BaselineCollection baselines = corridor.Baselines;
            foreach (Baseline baseline in baselines)
            {
                BaselineRegionCollection bRegCollection = baseline.BaselineRegions;
                foreach (BaselineRegion region in bRegCollection)
                {
                    AppliedAssemblyCollection assembl = region.AppliedAssemblies;
                    foreach (AppliedAssembly assembly in assembl)
                    {
                        AppliedSubassemblyCollection subassemblies = assembly.GetAppliedSubassemblies();
                        foreach (AppliedSubassembly subassembly in subassemblies)
                        {
                            ObjectId subId = subassembly.SubassemblyId;
                            subIds.Add(subId);
                            string nm = subassembly.GetParameter<string>("Имя участка").Value;
                            subNames.Add(nm);
                            //foreach (IAppliedSubassemblyParam subParam in subassembly.Parameters)
                            //{
                            //    //string nameParam = subParams.DisplayName;
                            //    //var stringParam = subParam as AppliedSubassemblyParam<string>;
                            //    if (subParam != null && !string.IsNullOrEmpty(subParam.ToString()))
                            //    {
                            //        string subName = subParam.DisplayName;
                            //        //subNames.Add(subName);
                            //    }
                            //}

                        }
                    }
                }
            }
            string[] selectedSubs = ShowSelectionSubassembliesDialog("Выберите конструкции", subNames);
            return selectedSubs;
        }
        private static string[] ShowSelectionSubassembliesDialog(string title, List<string> options)
        {
            Form form = new Form()
            {
                Text = title,
                Size = new System.Drawing.Size(300, 200),
                StartPosition = FormStartPosition.CenterScreen
            };

            ListBox listBox = new ListBox()
            {
                Dock = DockStyle.Fill,
                SelectionMode = System.Windows.Forms.SelectionMode.MultiExtended
            };
            listBox.Items.AddRange(options.ToArray());

            Button buttonOK = new Button()
            {
                Text = "OK",
                Dock = DockStyle.Bottom
            };
            buttonOK.Click += (sender, e) => { form.DialogResult = System.Windows.Forms.DialogResult.OK; };

            form.Controls.Add(listBox);
            form.Controls.Add(buttonOK);

            if (form.ShowDialog() == System.Windows.Forms.DialogResult.OK && listBox.SelectedItems != null)
                return listBox.SelectedItems.Cast<string>().ToArray();
            return new string[] { };
        }

    }
    public class CorridorSurfaceRenamer
    {

        public static void RenameCorridorSurfaceCmd()
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            CivilDocument civDoc = CivilApplication.ActiveDocument;
            Editor ed = doc.Editor;

            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                //получаем коллекцию корридоров
                CorridorCollection corridors = civDoc.CorridorCollection;

                if (corridors.Count == 0)
                {
                    ed.WriteMessage("Нет доступных коридоров.");
                    return;
                }
                Corridor corridor = SelectCorridor(tr, corridors);
                if (corridor == null) return;
                //Проходим по поверхностям коридора
                CorridorSurfaceCollection surfaces = corridor.CorridorSurfaces;
                foreach (CorridorSurface surf in surfaces)
                {
                    if (surf != null)
                    {
                        String[] linkCode = surf.LinkCodes();
                        if (linkCode != null && linkCode.Length > 0)
                        {
                            string newSurfName = corridor.Name + linkCode[0];
                            surf.Name = newSurfName;
                            ed.WriteMessage($"\nПереименована поверхность ({surf.Name}) в {newSurfName}");
                        }
                    }
                }
                //Предполагаем что в определении поверхности сохранены имена звеньяев
                //если такая колекция есть - то добавляем к имени поверхности имя звена
                tr.Commit();
            }

        }
        //Выбираем коридор из списка
        private static Corridor SelectCorridor(Transaction tr, CorridorCollection corridors)
        {
            List<string> corridorNames = new List<string>();
            Dictionary<string, ObjectId> corridorDict = new Dictionary<string, ObjectId>();

            foreach (ObjectId corridorId in corridors)
            {
                Corridor corridor = tr.GetObject(corridorId, OpenMode.ForRead) as Corridor;
                if (corridor != null)
                {
                    corridorNames.Add(corridor.Name);
                    corridorDict[corridor.Name] = corridorId;
                }
            }

            string selectedName = ShowSelectionDialog("Выберите коридор", corridorNames);
            if (string.IsNullOrEmpty(selectedName))
                return null;

            return tr.GetObject(corridorDict[selectedName], OpenMode.ForRead) as Corridor;
        }
        private static string ShowSelectionDialog(string title, List<string> options)
        {
            Form form = new Form()
            {
                Text = title,
                Size = new System.Drawing.Size(300, 200),
                StartPosition = FormStartPosition.CenterScreen
            };

            ListBox listBox = new ListBox()
            {
                Dock = DockStyle.Fill
            };
            listBox.Items.AddRange(options.ToArray());

            Button buttonOK = new Button()
            {
                Text = "OK",
                Dock = DockStyle.Bottom
            };
            buttonOK.Click += (sender, e) => { form.DialogResult = System.Windows.Forms.DialogResult.OK; };

            form.Controls.Add(listBox);
            form.Controls.Add(buttonOK);

            if (form.ShowDialog() == System.Windows.Forms.DialogResult.OK && listBox.SelectedItem != null)
                return listBox.SelectedItem.ToString();

            return string.Empty;
        }
    }
    public class LinkRegionManager
    {
        Dictionary<ObjectId, ObjectId> r_dict;
        //Dictionary<uint, Guid> p_dict;
        // constructor
        public LinkRegionManager()
        {
            r_dict = new Dictionary<ObjectId, ObjectId>();
            //p_dict = new Dictionary<uint, Guid>();
        }
        // create a bi-directional link between two objects

        public void LinkObjects(ObjectId profile, ObjectId corridor)
        {
            LinkRegion(profile, corridor);
            //LinkProfEnt(profileEntity, region);
        }
        // helper function to create one-way link between objects
        private void LinkRegion(ObjectId profileFrom, ObjectId corridorTo)
        {
            ObjectId existingCorr;
            if (r_dict.TryGetValue(profileFrom, out existingCorr))
            {
                if (existingCorr != corridorTo)
                {
                    r_dict.Remove(profileFrom);
                    r_dict.Add(profileFrom, existingCorr);
                }
            }
            else
            {
                r_dict.Add(profileFrom, corridorTo);
            }
        }
        // helper function to create one-way link between objects
        //private void LinkProfEnt(uint profEntFrom, Guid regionTo)
        //{
        //    Guid existingReg;
        //    if(p_dict.TryGetValue(profEntFrom, out existingReg))
        //    {
        //        if(existingReg != regionTo)
        //        {
        //            p_dict.Remove(profEntFrom);
        //            p_dict.Add(profEntFrom, existingReg);
        //        }
        //    }
        //    else
        //    {
        //        p_dict.Add(profEntFrom, regionTo);
        //    }
        //}

        //Remove bi-directional links from an object
        public void RemoveLinks(ObjectId profile)
        {
            ObjectId existingCorr;
            if (r_dict.TryGetValue(profile, out existingCorr))
            {
                r_dict.Remove(profile);
            }
        }
        // returns the list of objects linked to the one passed in
        public ObjectId GetLinkedObject(ObjectId obj)
        {
            ObjectId exisitngCorr;
            r_dict.TryGetValue(obj, out exisitngCorr);
            return exisitngCorr;
        }
        public bool Contains(ObjectId key)
        {
            return r_dict.ContainsKey(key);
        }
    }
    public class CorridorRegionSlicer
    {
        //private const string AppName = "CorridorProfileLink";
        LinkRegionManager m_linkManager;
        ObjectIdCollection m_regionsToUpdate;
        //ObjectIdCollection m_profileEntitiesToUpdate;

        public CorridorRegionSlicer()
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            db.ObjectModified += new ObjectEventHandler(OnObjectModified);
            db.ObjectErased += new ObjectErasedEventHandler(OnObjectErased);
            //db.BeginSave += new DatabaseIOEventHandler(OnBeginSave);
            doc.CommandEnded += new CommandEventHandler(OnCommandEnded);
            m_linkManager = new LinkRegionManager();
            m_regionsToUpdate = new ObjectIdCollection();
            //m_profileEntitiesToUpdate = new ObjectIdCollection();
        }
        ~CorridorRegionSlicer()
        {
            try
            {
                Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                Database db = doc.Database;
                db.ObjectModified -= new ObjectEventHandler(OnObjectModified);
                db.ObjectErased -= new ObjectErasedEventHandler(OnObjectErased);
                //db.BeginSave -= new DatabaseIOEventHandler(OnBeginSave);
                doc.CommandEnded -= new CommandEventHandler(OnCommandEnded);
            }
            catch (System.Exception)
            {
                //the document or database may no longer be available on unload
            }
        }
        private void OnObjectModified(object sender, ObjectEventArgs e)
        {
            ObjectId id = e.DBObject.ObjectId;
            if (m_linkManager.Contains(id) && !m_regionsToUpdate.Contains(id))
            {
                m_regionsToUpdate.Add(id);
            }
        }
        private void OnObjectErased(object sender, ObjectErasedEventArgs e)
        {
            if (e.Erased)
            {
                m_linkManager.RemoveLinks(e.DBObject.ObjectId);
            }
        }
        private void OnCommandEnded(object sender, CommandEventArgs e)
        {
            foreach (ObjectId id in m_regionsToUpdate)
            {
                //UpdateLinkEntities(id);
            }
            //m_regionsToUpdate.Clear();
        }
        private void UpdateLinkEntities(ObjectId from)
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;
            ObjectId linked = m_linkManager.GetLinkedObject(from);
            Transaction tr = db.TransactionManager.StartTransaction();
            CivilDocument civDoc = CivilApplication.ActiveDocument;
            CorridorCollection corridors = civDoc.CorridorCollection;
            using (tr)
            {
                try
                {
                    Profile prof = tr.GetObject(from, OpenMode.ForRead) as Profile;
                    Corridor corridor = tr.GetObject(linked, OpenMode.ForWrite) as Corridor;
                    BaselineCollection baselines = corridor.Baselines;
                    Baseline corrBase = null;
                    foreach (Baseline baseline in baselines)
                    {
                        if (baseline.ProfileId == prof.Id)
                        {
                            corrBase = baseline;
                            break;
                        }
                    }
                    BaselineRegionCollection blrcoll = corrBase.BaselineRegions;
                    //объединяем все области коридора
                    //BaselineRegion mReg = null;
                    UnionRegions(corrBase);
                    //разбиваем коридор на области
                    BaselineRegion BLRegion = blrcoll[0];
                    int regNameNumber = 1;
                    BLRegion.Name = "Участок" + " " + regNameNumber.ToString();
                    BaselineRegion newRegion = null;
                    ProfileEntityCollection profEntColl = prof.Entities;

                    foreach (ProfileEntity entity in profEntColl)
                    {
                        if (entity.EntityType == ProfileEntityType.Tangent && entity.StartElevation == entity.EndElevation)
                        {
                            double station = entity.EndStation;
                            regNameNumber++;
                            BaselineRegion curReg = null;
                            if (station < BLRegion.EndStation)
                            {
                                curReg = BLRegion;
                                newRegion = BLRegion.Split(station);
                                newRegion.Name = "Участок" + " " + regNameNumber.ToString();
                                //перестраиваем рассматриваемый участок в соответствии с entity
                                double start = entity.StartStation;
                                double end = entity.EndStation;
                                curReg.StartStation = start;
                                curReg.EndStation = end;
                            }
                            else if (station < newRegion.EndStation - 0.01)
                            {
                                curReg = newRegion;
                                newRegion = newRegion.Split(station);
                                newRegion.Name = "Участок" + " " + regNameNumber.ToString();
                                //перестраиваем рассматриваемый участок в соответствии с entity
                                double start = entity.StartStation;
                                double end = entity.EndStation;
                                curReg.StartStation = start;
                                curReg.EndStation = end;
                            }
                            else
                            {
                                //перестраиваем рассматриваемый участок в соответствии с entity
                                double start = entity.StartStation;
                                double end = entity.EndStation;
                                newRegion.StartStation = start;
                            }
                        }
                      //перестраиваем рассматриваемый участок в соответствии с entity
                      //Guid regGuid = newRegion.RegionGUID;
                      //uint ent = entity.EntityId;
                    }
                }
                catch (System.Exception ex)
                {
                    Autodesk.AutoCAD.Runtime.Exception ex2 = ex as Autodesk.AutoCAD.Runtime.Exception;
                    if (ex2 != null && ex2.ErrorStatus != ErrorStatus.WasOpenForUndo)
                    {
                        ed.WriteMessage("\nAutoCAD exception: {0}", ex2);
                    }
                    else if (ex2 == null)
                    {
                        ed.WriteMessage("\nAutoCAD exception: {0}", ex);
                    }
                }
                tr.Commit();
            }
        }
        //[CommandMethod("LinkCorridor")]
        public void LinkCorridorByProfile()
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;
            //выбор профиля
            PromptEntityOptions selProfPrompt = new PromptEntityOptions("\nВыберите профиль");
            selProfPrompt.SetRejectMessage("\nВыбранный объект не является профилем");
            selProfPrompt.AddAllowedClass(typeof(Profile), false);
            PromptEntityResult prRes = ed.GetEntity(selProfPrompt);
            //выбор коридора
            PromptEntityOptions selCorrPrompt = new PromptEntityOptions("\nВыберите коридор");
            selCorrPrompt.SetRejectMessage("\nВыбранный объект не является коридором");
            selCorrPrompt.AddAllowedClass(typeof(Corridor), false);
            PromptEntityResult prCorRes = ed.GetEntity(selCorrPrompt);
            if (prCorRes.Status != PromptStatus.OK && prRes.Status != PromptStatus.OK)
            {
                ed.WriteMessage("\nКоманда отменена");
                return;
            }
            ObjectId from = prRes.ObjectId;
            ObjectId to = prCorRes.ObjectId;
            m_linkManager.LinkObjects(from, to);
            m_regionsToUpdate.Add(from);
        }

        [CommandMethod("RW_SPLITCORRBYPROF")]
        public void SliceByProfile()
        {

            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                //выбор и обработка профиля
                PromptEntityOptions selProfPrompt = new PromptEntityOptions("\nВыберите профиль основания");
                selProfPrompt.SetRejectMessage("\nВыбранный объект не является профилем");
                selProfPrompt.AddAllowedClass(typeof(Profile), false);
                PromptEntityResult prRes = ed.GetEntity(selProfPrompt);
                if (prRes.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\nКоманда отменена");
                    return;
                }
                Profile prof = tr.GetObject(prRes.ObjectId, OpenMode.ForRead) as Profile;
                List<double> profileStations = new List<double>();
                ProfilePVICollection pviColl = prof.PVIs;
                ProfileEntityCollection profEntColl = prof.Entities;
                //выбор и обработка коридора
                CivilDocument civDoc = CivilApplication.ActiveDocument;
                CorridorCollection corridors = civDoc.CorridorCollection;
                if (corridors.Count == 0)
                {
                    ed.WriteMessage("Нет доступных коридоров.");
                    return;
                }
                // Выбор коридора через диалоговое окно
                Corridor corridor = CorridorSurfaceCreator.SelectCorridor(tr, corridors);
                if (corridor == null) return;
                BaselineCollection baselines = corridor.Baselines;
                Baseline profBase = null;
                foreach (Baseline baseline in baselines)
                {
                    if (baseline.ProfileId == prof.Id)
                    {
                        profBase = baseline;
                        break;
                    }
                }
                UnionRegions(profBase);

                //Переименовываем элементы конструкции участков
                BaselineRegionCollection blrcoll = profBase.BaselineRegions;
                BaselineRegion BLRegion = blrcoll[0];
                Alignment alignment = tr.GetObject(prof.AlignmentId, OpenMode.ForRead) as Alignment; 
                profileStations.Sort();
                int regNameNumber = 1;
                BLRegion.Name = "Участок" + " " + regNameNumber.ToString();
                BaselineRegion newRegion = null;
                ProfileEntity[] sortedEntities = profEntColl.Cast<ProfileEntity>().OrderBy(entity => entity.StartStation).ToArray();
                foreach (ProfileEntity entity in sortedEntities)
                {
                    if (entity.EntityType == ProfileEntityType.Tangent && entity.StartElevation == entity.EndElevation)
                    {
                        PromptEntityOptions prSubassemblyOpt = new PromptEntityOptions("\nВыберите конструкцию сечения на участке " + regNameNumber);
                        prSubassemblyOpt.SetRejectMessage("\nВыбраны могут быть только конструкции сечения");
                        prSubassemblyOpt.AddAllowedClass(typeof(Autodesk.Civil.DatabaseServices.Assembly), false);
                        PromptEntityResult resAs = ed.GetEntity(prSubassemblyOpt);
                        Assembly inputAssembly = null;
                        if (resAs.Status == PromptStatus.OK & resAs != null)
                        {
                            Assembly assembly = tr.GetObject(resAs.ObjectId, OpenMode.ForWrite) as Assembly;
                            //assembly.Name = region.Name;
                            inputAssembly = assembly;
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
                                            paramString.Value = "Участок" + " " + regNameNumber.ToString();
                                        }
                                    }
                                }
                            }
                        }
                        double station = entity.EndStation;
                        regNameNumber++;
                        BaselineRegion curReg = null;
                        if (station < BLRegion.EndStation)
                        {
                            curReg = BLRegion;
                            newRegion = BLRegion.Split(station);
                            newRegion.Name = "Участок" + " " + regNameNumber.ToString();
                            //перестраиваем рассматриваемый участок в соответствии с entity
                            double start = 0.0;
                            double end = 0.0;
                            if (entity.StartStation < alignment.StartingStation)
                            {
                                start = alignment.StartingStation;
                            }
                            if(entity.StartStation > alignment.StartingStation)
                            {
                                start = entity.StartStation;
                            }
                            if (entity.EndStation < alignment.EndingStation)
                            {
                                end = entity.EndStation;
                            }
                            else if (entity.EndStation > alignment.EndingStation)
                            {
                                end = alignment.EndingStation;
                            }
                            curReg.StartStation = start;
                            curReg.EndStation = end;
                            curReg.AssemblyId = inputAssembly.ObjectId;
                        }
                        else if (station < newRegion.EndStation - 0.01)
                        {
                            curReg = newRegion;
                            newRegion = newRegion.Split(station);
                            newRegion.Name = "Участок" + " " + regNameNumber.ToString();
                            //перестраиваем рассматриваемый участок в соответствии с entity
                            double start = entity.StartStation;
                            double end = entity.EndStation;
                            curReg.StartStation = start;
                            curReg.EndStation = end;
                            curReg.AssemblyId = inputAssembly.ObjectId;
                        }
                        else
                        {
                            //перестраиваем рассматриваемый участок в соответствии с entity
                            double start = entity.StartStation;
                            double end = 0.0;
                            if (entity.EndStation < alignment.EndingStation)
                            {
                                end = entity.EndStation;
                            }
                            else if (entity.EndStation > alignment.EndingStation)
                            {
                                end = alignment.EndingStation;
                            }
                            newRegion.StartStation = start;
                            newRegion.AssemblyId = inputAssembly.ObjectId;
                        }
                    }
                    //перестраиваем рассматриваемый участок в соответствии с entity
                    //Guid regGuid = newRegion.RegionGUID;
                    //uint ent = entity.EntityId;
                }
                //ObjectId from = prof.ObjectId;
                //ObjectId to = corridor.ObjectId;
                //m_linkManager.LinkObjects(from, to);
                //m_regionsToUpdate.Add(from);
                //Corridor corr = tr.GetObject(corridor.ObjectId, OpenMode.ForWrite) as Corridor;
                //сохраниение связи профиля и коридора в XData
                string myApp = "MyApp";
                //SetXData(prof, corridor.ObjectId, myApp);
                ed.WriteMessage($"\nСвязь профиля {prof.Name} с коридором {corridor.Name} установлена");
                //BaselineCollection bCollect = corridor.Baselines;
                tr.Commit();
            }

        }

        public void UnionRegions(Baseline baseline)
        {
            BaselineRegionCollection blrcoll = baseline.BaselineRegions;
            if (blrcoll.Count > 1)
            {
                BaselineRegion baselineRegion = blrcoll[0];
                baselineRegion.Merge(blrcoll[0], blrcoll[blrcoll.Count - 1]);
            }
        }
        /*
        [CommandMethod("UpdateRegions")]
        public static void UpdateCorridorRegions()
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                CivilDocument civDoc = CivilApplication.ActiveDocument;
                ObjectIdCollection profileIds = civDoc.
            }
        }
        private static List<double> GetRegionStations(Profile inputProfile)
        {
            List<double> stations = new List<double>();
            for (int i = 1; i < inputProfile.PVIs.Count; i ++)
            {
                if (inputProfile.PVIs[i-1].Elevation != inputProfile.PVIs[i].Elevation)
                {
                    stations.Add(inputProfile.PVIs[i].Station);
                }
            }
            return stations;
        }
        private static void SetXData(BaselineRegion region, ObjectId profileEntityId, string appName)
        {
            Database db = profile.Database;
            RegisterAppName(db, appName);
            using (Transaction tr = db.TransactionManager.StartTransaction()) 
            {
                Autodesk.AutoCAD.DatabaseServices.DBObject obj = tr.GetObject(profile.ObjectId, OpenMode.ForWrite);
                ResultBuffer rb = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, appName),
                new TypedValue((int)DxfCode.ExtendedDataHandle, corridorId.Handle.ToString()));
            
                obj.XData = rb;
                tr.Commit();                
            }
        }
        private static ObjectId GetCorridorFromXData(Profile profile, string appName)
        {
            ResultBuffer rb = profile.XData;
            if (rb == null) return ObjectId.Null;

            TypedValue[] values = rb.AsArray();
            if( values.Length <2 || values[0].Value.ToString() != appName) return ObjectId.Null;
            
            string handlerStr = values[1].Value.ToString();
            Handle handle = new Handle(Convert.ToInt64(handlerStr, 16));

            ObjectId corridorId;
            profile.Database.TryGetObjectId(handle, out corridorId);
            return corridorId;
        }
        private static void RegisterAppName(Database db, string appName)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                RegAppTable regAppTable = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
                if (!regAppTable.Has(appName))
                {
                    regAppTable.UpgradeOpen();
                    RegAppTableRecord regAppRecord = new RegAppTableRecord { Name = appName};
                    regAppTable.Add(regAppRecord);
                    tr.AddNewlyCreatedDBObject(regAppRecord, true);

                }
                tr.Commit();
            }
        }
        */
    }
    public class BaselineCreator
    {
        [CommandMethod("RW_ADDSTEPS")]
        public static void AddSteps()
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;
            CivilDocument civDoc = CivilApplication.ActiveDocument;
            PromptEntityOptions profOpt = new PromptEntityOptions("\nВыберите профиль");
            profOpt.SetRejectMessage("\nВыбраный объект не является профилем");
            profOpt.AddAllowedClass(typeof(Profile), false);
            PromptEntityResult prRes = ed.GetEntity(profOpt);
            if (prRes.Status != PromptStatus.OK)
            {
                ed.WriteMessage("\nВид профиля не выбран");
                return;
            }
            while (true)
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    PromptPointOptions prPointOpt = new PromptPointOptions("\nВыберите точку вставки ТВП на виде профиля");
                    prPointOpt.AllowNone = true;
                    PromptDoubleOptions prStepOp = new PromptDoubleOptions("\nВведите вертикальный шаг:");
                    prStepOp.AllowNone = true;
                    prStepOp.DefaultValue = 0.5;
                    PromptDoubleResult prStepRes = ed.GetDouble(prStepOp);
                    PromptPointResult prPointRes = ed.GetPoint(prPointOpt);
                    //запрос профиля
                    Profile profile = tr.GetObject(prRes.ObjectId, OpenMode.ForWrite) as Profile;
                    Alignment align = tr.GetObject(profile.AlignmentId, OpenMode.ForRead) as Alignment;
                    ProfileView profView = tr.GetObject(align.GetProfileViewIds()[0], OpenMode.ForRead) as ProfileView;
                    if (prPointRes.Status != PromptStatus.OK || prStepRes.Status != PromptStatus.OK)
                    {
                        break;
                    }
                    double station = 0.0;
                    double elevation = 0.0;
                    profView.FindStationAndElevationAtXY(prPointRes.Value.X, prPointRes.Value.Y, ref station, ref elevation);
                    rebuildProfile(station, prStepRes.Value, profile);

                    tr.Commit();
                }
            }
        }
        public static void rebuildProfile(double station, double step, Profile profile)
        {
            ProfilePVICollection pviCollect = profile.PVIs;
            ProfilePVI[] sortedPVIs = pviCollect.Cast<ProfilePVI>().OrderBy(pvi => pvi.Station).ToArray(); 
            ProfileEntityCollection entities = profile.Entities;
            ProfileEntity[] sortedEnities = entities.Cast<ProfileEntity>().OrderBy(entity => entity.StartStation).ToArray();
            double elevation = 0.0;
            bool rebuildFlug = false;
            for (int i = 0; i < sortedEnities.Length; i++)
            {
                double h = sortedEnities[i].StartElevation - sortedEnities[i].EndElevation;

                if (sortedEnities[i].EntityType == ProfileEntityType.Tangent && 
                    sortedEnities[i].StartStation < station &&
                    sortedEnities[i].EndStation > station &&
                    h == 0
                    )
                {
                    elevation = sortedEnities[i].StartElevation;
                    rebuildFlug = true;
                }
            }
            for (int i = 0; i < sortedPVIs.Length; i++)
            {
                //bool pviFlug = false;
                if (sortedPVIs[i].Station >= station)
                {
                    sortedPVIs[i].Elevation += step;
                }
            }
            profile.PVIs.AddPVI(station, elevation);
            profile.PVIs.AddPVI(station + 0.001, elevation + step);
            rebuildFlug = false;
        }
        public static void createPVIs()
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                CivilDocument civDoc = CivilApplication.ActiveDocument;
                //выбор вида профиля
                PromptEntityOptions profViewOpt = new PromptEntityOptions("\nВыберите вид профиля");
                profViewOpt.SetRejectMessage("\nВыбраный объект не является видом профиля");
                profViewOpt.AddAllowedClass(typeof(ProfileView), false);
                PromptEntityResult profViewRes = ed.GetEntity(profViewOpt);
                if (profViewRes.Status != PromptStatus.OK )
                {
                    ed.WriteMessage("\nВид профиля не выбран");
                    return;
                }
                ProfileView profView = tr.GetObject(profViewRes.ObjectId, OpenMode.ForRead) as ProfileView;
                if (profView == null)
                {
                    ed.WriteMessage("\nОшибка выбора вида профиля");
                    return;
                }
                //извлечение трассы по профилю
                Alignment alignment = tr.GetObject(profView.AlignmentId, OpenMode.ForRead) as Alignment;
                if (alignment == null)
                {
                    ed.WriteMessage("\nНе удалось получить трассу");
                    return;
                }
                //создание нового профиля
                ObjectId lableSetId = civDoc.Styles.LabelSetStyles.ProfileLabelSetStyles[0];
                ObjectId styleId = civDoc.Styles.ProfileStyles[0];
                ObjectId profileId = Profile.CreateByLayout("MyProlile", alignment.ObjectId, profView.LayerId, styleId, lableSetId);
                Profile profile = tr.GetObject(profileId, OpenMode.ForWrite) as Profile;
                if (profile == null)
                {
                    ed.WriteMessage("\nОшибка создания профиля");
                    return;
                }
                //запрос у пользователя точек и вертикального шага (если такой необходим)
                //double step;
                PromptPointOptions prPointOpt = new PromptPointOptions("\nВыберите точку вставки ТВП на виде профиля");
                prPointOpt.AllowNone = true;
                PromptDoubleOptions prStepOp = new PromptDoubleOptions("\nВведите вертикальный шаг:");
                //prStepOp.AllowNone = true;
                PromptDoubleResult prStepRes = ed.GetDouble(prStepOp);
                //if (prStepOp.AllowNone)
                //{
                //    step = prStepRes.Value;
                //}

                while (true)
                {
                    PromptPointResult prPointRes = ed.GetPoint(prPointOpt);
                    if (prPointRes.Status != PromptStatus.OK)
                    {
                        break;
                    }
                    //Point3d p1 = prPointRes.Value;
                    /*ProfilePolylineJig jigProf = new ProfilePolylineJig(p1);
                    while (true)
                    {
                        PromptResult res = ed.Drag(jigProf);
                        if (res.Status != PromptStatus.OK) break;
                        jigProf.AddVertex();
                    }
                    */
                    double station = 0.0;
                    double elevation = 0.0;   
                    profView.FindStationAndElevationAtXY(prPointRes.Value.X, prPointRes.Value.Y, ref station, ref elevation);
                    profile.PVIs.AddPVI(station, elevation);
                    profView.FindStationAndElevationAtXY(prPointRes.Value.X + 0.001, prPointRes.Value.Y + prStepRes.Value, ref station, ref elevation);
                    profile.PVIs.AddPVI(station, elevation);
                }

                tr.Commit();
            }
        }
    }
    public class DeleteAllSurfaces
    {
        [CommandMethod("RW_DELETESURF")]
        public void deleteSurfaces()
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;


            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                CivilDocument civDoc = CivilApplication.ActiveDocument;
                CorridorCollection corridors = civDoc.CorridorCollection;
                if (corridors.Count == 0)
                {
                    ed.WriteMessage("Нет доступных коридоров.");
                    return;
                }
                // Выбор коридора через диалоговое окно
                PromptEntityOptions prCorr = new PromptEntityOptions("\nВыберите коридор");
                prCorr.SetRejectMessage("\nвыбран не коридор");
                prCorr.AddAllowedClass(typeof(Corridor), true);
                PromptEntityResult res = ed.GetEntity(prCorr);
                Corridor corridor = tr.GetObject(res.ObjectId, OpenMode.ForWrite) as Corridor;
                if (corridor == null) return;

                CorridorSurfaceCollection corridorSurfaces = corridor.CorridorSurfaces;
                if (corridorSurfaces.Count != 0)
                {
                    List<string> surfToRemove = new List<string>();
                    int c = corridorSurfaces.Count;
                    for (int i = 0; i < c; i++)
                    {
                        surfToRemove.Add(corridorSurfaces[i].Name);
                    }
                    foreach (string name in surfToRemove)
                    {
                        corridorSurfaces.Remove(name);
                    }
                }
                tr.Commit();
            }
        }
    }
    
    /*public class ProfilePolylineJig: EntityJig
    {
        private Polyline _polyline;
        private Point3dCollection _points;
        private Point3d _currentPoint;
        public ProfilePolylineJig(Point3d startPoint) : base(new Polyline())
        {
            _polyline = (Polyline)Entity;
            _points = new Point3dCollection();
            _currentPoint = startPoint;
            _polyline.AddVertexAt(0, new Point2d(startPoint.X, startPoint.Y),0,0,0);
        }
        protected override bool Update()
        {
            //_polyline.AddVertexAt(_polyline.NumberOfVertices, new Point2d(_currentPoint.X, _currentPoint.Y),0,0,0);
            _polyline.SetPointAt(_polyline.NumberOfVertices-1, new Point2d(_currentPoint.X, _currentPoint.Y));
            return true;
        }
        protected override SamplerStatus Sampler(JigPrompts prompts)
        {
            JigPromptPointOptions jigOpts = new JigPromptPointOptions("\nУкажите следующую точку (Enter для завершения):");
            PromptPointResult jigRes = prompts.AcquirePoint(jigOpts);
            if(jigRes.Status == PromptStatus.OK)
            {
                if(jigRes.Value == _currentPoint)
                {
                    return SamplerStatus.NoChange;
                }
                _currentPoint = jigRes.Value;
                return SamplerStatus.OK;
            }
            return SamplerStatus.Cancel;
        }
        public void AddVertex()
        {
            _points.Add(_currentPoint);
            _polyline.AddVertexAt(_polyline.NumberOfVertices, new Point2d(_currentPoint.X, _currentPoint.Y),0,0,0);
        }
        public Polyline GetFinalPoly()
        {
            return _polyline;
        }
    }*/
}


    