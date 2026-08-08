using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Aec.PropertyData;
using Autodesk.Aec.PropertyData.DatabaseServices;

namespace Civil3D_commands.AssociativeBreaks
{
    /// <summary>
    /// Наборы характеристик (Property Sets):
    ///   - на профиле: "RW_Редактирование" с булевым "Включить" — переключатель режима;
    ///   - на прокси:  "RW_Разрыв" с полями Пикет / Ступень / ВысотаСтупени.
    /// Построено на том же PropertyDataServices, что и твой PropertySetHelper.
    /// </summary>
    public static class PropertySetSupport
    {
        public const string EditPsd = "RW_Редактирование";
        public const string EditProp = "Включить";

        public const string MarkerPsd = "RW_Разрыв";
        public const string PropStation = "Пикет";
        public const string PropIsStep = "Ступень";
        public const string PropStepH = "ВысотаСтупени";

        public static ObjectId EnsureEditPsd(Database db)
        {
            return EnsurePsd(db, EditPsd, (d, def) =>
            {
                AddBool(d, def, EditProp, false);
            });
        }

        public static ObjectId EnsureMarkerPsd(Database db)
        {
            return EnsurePsd(db, MarkerPsd, (d, def) =>
            {
                AddReal(d, def, PropStation, 0);
                AddBool(d, def, PropIsStep, false);
                AddReal(d, def, PropStepH, 0);
            });
        }

        private static ObjectId EnsurePsd(Database db, string name, Action<Database, PropertySetDefinition> fill)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var dict = new DictionaryPropertySetDefinitions(db);
                if (dict.Has(name, tr))
                {
                    var id = dict.GetAt(name);
                    tr.Commit();
                    return id;
                }
                var def = new PropertySetDefinition();
                def.SetToStandard(db);
                def.SubSetDatabaseDefaults(db);
                // Применимость: для надёжного PropertyDataServices.AddPropertySet набор должен
                // допускать целевой класс. В вашем PropertySetHelper это уже отработано —
                // при ошибке "not applicable" задайте SetAppliesToFilter с именами классов
                // (AECC_PROFILE для профиля, AcDbLine для прокси).
                var filter = new System.Collections.Specialized.StringCollection();
                def.SetAppliesToFilter(filter, true); // true = применять ко всем типам
                fill(db, def);
                ObjectId defId = dict.AddNewRecord(name, def);
                tr.AddNewlyCreatedDBObject(def, true);
                tr.Commit();
                return defId;
            }
        }

        private static void AddBool(Database db, PropertySetDefinition def, string name, bool dflt)
        {
            var pd = new PropertyDefinition();
            pd.SetToStandard(db);
            pd.SubSetDatabaseDefaults(db);
            pd.Name = name;
            pd.DataType = DataType.TrueFalse;
            pd.DefaultData = dflt;
            def.Definitions.Add(pd);
        }

        private static void AddReal(Database db, PropertySetDefinition def, string name, double dflt)
        {
            var pd = new PropertyDefinition();
            pd.SetToStandard(db);
            pd.SubSetDatabaseDefaults(db);
            pd.Name = name;
            pd.DataType = DataType.Real;
            pd.DefaultData = dflt;
            def.Definitions.Add(pd);
        }

        /// <summary>Прикрепить набор к объекту (если ещё не прикреплён).</summary>
        public static void Attach(Transaction tr, ObjectId entId, ObjectId psdId)
        {
            var obj = tr.GetObject(entId, OpenMode.ForWrite);
            var sets = PropertyDataServices.GetPropertySets(obj);
            foreach (ObjectId psId in sets)
            {
                var ps = (PropertySet)tr.GetObject(psId, OpenMode.ForRead);
                if (ps.PropertySetDefinition == psdId) return; // уже есть
            }
            PropertyDataServices.AddPropertySet(obj, psdId);
        }

        /// <summary>Записать свойства маркера в набор на прокси.</summary>
        public static void WriteMarkerProps(Transaction tr, ObjectId entId, StationMarker m)
        {
            var obj = tr.GetObject(entId, OpenMode.ForWrite);
            foreach (ObjectId psId in PropertyDataServices.GetPropertySets(obj))
            {
                var ps = (PropertySet)tr.GetObject(psId, OpenMode.ForWrite);
                if (ps.PropertySetDefinitionName != MarkerPsd) continue;
                SetIf(ps, PropStation, m.Station);
                SetIf(ps, PropIsStep, m.IsStep);
                SetIf(ps, PropStepH, m.StepHeight);
            }
        }

        /// <summary>Прочитать значение булевого свойства "Включить" с профиля.</summary>
        public static bool ReadEditFlag(Transaction tr, ObjectId profileId)
        {
            var obj = tr.GetObject(profileId, OpenMode.ForRead);
            foreach (ObjectId psId in PropertyDataServices.GetPropertySets(obj))
            {
                var ps = (PropertySet)tr.GetObject(psId, OpenMode.ForRead);
                if (ps.PropertySetDefinitionName != EditPsd) continue;
                int pid = ps.PropertyNameToId(EditProp);
                if (pid >= 0) return Convert.ToBoolean(ps.GetAt(pid));
            }
            return false;
        }

        private static void SetIf(PropertySet ps, string prop, object value)
        {
            int pid = ps.PropertyNameToId(prop);
            if (pid >= 0) ps.SetAt(pid, value);
        }
    }
}
