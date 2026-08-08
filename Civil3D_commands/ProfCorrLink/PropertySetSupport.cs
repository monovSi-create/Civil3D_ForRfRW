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
        public const string PropGap = "Микроразрыв";

        /// <summary>Описание одного свойства набора: имя, тип, значение по умолчанию.</summary>
        private struct PropSpec
        {
            public readonly string Name;
            public readonly Autodesk.Aec.PropertyData.DataType Type;
            public readonly object Default;

            public PropSpec(string name, Autodesk.Aec.PropertyData.DataType type, object dflt)
            {
                Name = name;
                Type = type;
                Default = dflt;
            }
        }

        private static PropSpec[] EditSpecs()
        {
            return new[]
            {
                new PropSpec(EditProp, Autodesk.Aec.PropertyData.DataType.TrueFalse, false)
            };
        }

        private static PropSpec[] MarkerSpecs()
        {
            return new[]
            {
                new PropSpec(PropStation, Autodesk.Aec.PropertyData.DataType.Real, 0.0),
                new PropSpec(PropIsStep, Autodesk.Aec.PropertyData.DataType.TrueFalse, false),
                new PropSpec(PropStepH, Autodesk.Aec.PropertyData.DataType.Real, 0.0),
                new PropSpec(PropGap, Autodesk.Aec.PropertyData.DataType.Real,
                             ProfileGeometryOps.DefaultGap)
            };
        }

        public static ObjectId EnsureEditPsd(Database db)
        {
            return EnsurePsd(db, EditPsd, EditSpecs());
        }

        public static ObjectId EnsureMarkerPsd(Database db)
        {
            return EnsurePsd(db, MarkerPsd, MarkerSpecs());
        }

        private static ObjectId EnsurePsd(Database db, string name, PropSpec[] specs)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var dict = new DictionaryPropertySetDefinitions(db);
                if (dict.Has(name, tr))
                {
                    var id = dict.GetAt(name);
                    Upgrade(db, tr, id, specs);
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

                foreach (PropSpec spec in specs) AddProperty(db, def, spec);

                dict.AddNewRecord(name, def);
                tr.AddNewlyCreatedDBObject(def, true);
                ObjectId defId = def.ObjectId;
                tr.Commit();
                return defId;
            }
        }

        /// <summary>
        /// Дополнить уже существующий набор свойствами, которых в нём нет.
        ///
        /// Набор создаётся один раз и остаётся в чертеже навсегда, поэтому
        /// в чертежах прежних версий нет «Микроразрыва»: без этого он бы там
        /// никогда и не появился. Добавляется только недостающее по имени —
        /// то, что пользователь настроил сам, не трогается.
        ///
        /// Определения строятся здесь заново: PropertyDefinition, уже добавленный
        /// в один набор, чужому набору отдавать нельзя.
        /// </summary>
        private static void Upgrade(Database db, Transaction tr, ObjectId psdId, PropSpec[] specs)
        {
            try
            {
                var existing = tr.GetObject(psdId, OpenMode.ForRead) as PropertySetDefinition;
                if (existing == null) return;

                var have = new System.Collections.Generic.HashSet<string>();
                foreach (PropertyDefinition pd in existing.Definitions) have.Add(pd.Name);

                var missing = new System.Collections.Generic.List<PropSpec>();
                foreach (PropSpec spec in specs)
                    if (!have.Contains(spec.Name)) missing.Add(spec);

                if (missing.Count == 0) return;

                existing.UpgradeOpen();
                foreach (PropSpec spec in missing) AddProperty(db, existing, spec);
            }
            catch (System.Exception)
            {
                // Не смогли дополнить — набор просто останется прежним,
                // а недостающее свойство молча не запишется (см. SetIf).
            }
        }

        private static void AddProperty(Database db, PropertySetDefinition def, PropSpec spec)
        {
            var pd = new PropertyDefinition();
            pd.SetToStandard(db);
            pd.SubSetDatabaseDefaults(db);
            pd.Name = spec.Name;
            pd.DataType = spec.Type;
            pd.DefaultData = spec.Default;
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
                SetIf(ps, PropGap, m.Gap);
            }
        }

        /// <summary>
        /// Прочитать свойства разрыва из набора на прокси. false, если набора
        /// нет. Значения, которых в наборе не оказалось, остаются как были
        /// в маркере — так правка старого чертежа не обнуляет то, чего в его
        /// наборе не было.
        /// </summary>
        public static bool ReadMarkerProps(Transaction tr, ObjectId entId, StationMarker m,
                                           out double station, out bool isStep,
                                           out double stepHeight, out double gap)
        {
            station = m.Station;
            isStep = m.IsStep;
            stepHeight = m.StepHeight;
            gap = m.Gap;

            var obj = tr.GetObject(entId, OpenMode.ForRead);

            foreach (ObjectId psId in PropertyDataServices.GetPropertySets(obj))
            {
                var ps = (PropertySet)tr.GetObject(psId, OpenMode.ForRead);
                if (ps.PropertySetDefinitionName != MarkerPsd) continue;

                station = GetIf(ps, PropStation, station);
                isStep = GetIf(ps, PropIsStep, isStep);
                stepHeight = GetIf(ps, PropStepH, stepHeight);
                gap = GetIf(ps, PropGap, gap);
                return true;
            }

            return false;
        }

        private static double GetIf(PropertySet ps, string prop, double fallback)
        {
            try
            {
                int pid = ps.PropertyNameToId(prop);
                if (pid < 0) return fallback;

                object value = ps.GetAt(pid);
                return value == null ? fallback : Convert.ToDouble(value);
            }
            catch (System.Exception)
            {
                return fallback;
            }
        }

        private static bool GetIf(PropertySet ps, string prop, bool fallback)
        {
            try
            {
                int pid = ps.PropertyNameToId(prop);
                if (pid < 0) return fallback;

                object value = ps.GetAt(pid);
                return value == null ? fallback : Convert.ToBoolean(value);
            }
            catch (System.Exception)
            {
                return fallback;
            }
        }

        /// <summary>
        /// Записать флаг «Включить» на профиль, чтобы галочка в палитре
        /// показывала то же, что действует на самом деле.
        /// </summary>
        public static void WriteEditFlag(Transaction tr, ObjectId profileId, bool on)
        {
            var obj = tr.GetObject(profileId, OpenMode.ForWrite);

            foreach (ObjectId psId in PropertyDataServices.GetPropertySets(obj))
            {
                var ps = (PropertySet)tr.GetObject(psId, OpenMode.ForWrite);
                if (ps.PropertySetDefinitionName != EditPsd) continue;
                SetIf(ps, EditProp, on);
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
