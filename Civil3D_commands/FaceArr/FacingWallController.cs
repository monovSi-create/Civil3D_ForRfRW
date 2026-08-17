using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.DatabaseServices;
using Civil3D_commands.Shared;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
// Entity и ObjectId есть и в AutoCAD-, и в Civil-пространстве имён — снимаем неоднозначность.
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;
using ObjectId = Autodesk.AutoCAD.DatabaseServices.ObjectId;

namespace Civil3D_commands.FaceArr
{
    /// <summary>Что оказалось в расширенном словаре выбранного объекта.</summary>
    public enum FacingWallRecordStatus
    {
        /// <summary>Не наш объект вовсе.</summary>
        NotController,

        /// <summary>Объект наш, но записи в словаре нет.</summary>
        NoRecord,

        /// <summary>Запись есть, но не читается.</summary>
        Corrupt,

        /// <summary>Запись сделана более новой версией плагина.</summary>
        NewerVersion,

        /// <summary>Это копия контроллера: связи с объектами сброшены.</summary>
        DetachedCopy,

        /// <summary>Прочитано нормально.</summary>
        Ok
    }

    /// <summary>
    /// Запись новее, чем понимает эта сборка. Отдельный тип нужен, чтобы такой
    /// случай не смешивался с битой записью: поверх более новых данных нельзя
    /// ни писать, ни строить.
    /// </summary>
    public class FacingWallNewerVersionException : System.Exception
    {
        public FacingWallNewerVersionException(int found, int supported)
            : base(string.Format(
                "Запись сделана более новой версией плагина: версия данных {0}, " +
                "эта сборка понимает до {1}.", found, supported))
        {
            FoundVersion = found;
            SupportedVersion = supported;
        }

        public int FoundVersion { get; private set; }
        public int SupportedVersion { get; private set; }
    }

    /// <summary>
    /// Главный управляющий класс массива облицовки.
    ///
    /// Контроллер — единственный источник данных: параметры массива, параметры
    /// рядов и связи со всеми порождёнными объектами. Всё остальное (MVBlock в
    /// плане, объекты на виде профиля) — производное и может быть пересоздано.
    ///
    /// Физически контроллер — вставка служебного блока FACINGWALL_CONTROLLER,
    /// помеченная XData; данные лежат в Xrecord его расширенного словаря.
    /// </summary>
    public static class FacingWallController
    {
        public const string ControllerBlockName = "FACINGWALL_CONTROLLER";
        public const string XRecordKey = "FACINGWALL_DATA";
        public const string XAppName = "RW_FACINGWALL";

        // 2 — добавлена ссылка на отрезок-ручку ряда.
        // 3 — добавлены режим профильного отображения и имя 2D-блока.
        // 4 — ссылка на отрезок-ручку массива в плане (одна, вдоль трассы).
        // 5 — вместо неё два перпендикулярных маркера + границы раскладки.
        // 6 — те же границы продублированы маркерами в виде профиля.
        // 7 — проекции, созданные штатным инструментом Civil 3D.
        // 8 — половинчатый блок и замены блоков по местам на трассе.
        // 9 — поправка к повороту блока.
        // 10 — собственный хэндл контроллера: по нему опознаётся копия.
        // 11 — своя ширина половинчатого блока и плоские блоки заменителей.
        // 12 — отскок ряда, таблички слева от вида профиля, разрывы в рядах.
        // Старые записи читаются: недостающее берётся по умолчанию, а
        // недостающие отрезки-ручки создаст FACINGWALLREBUILDALL.
        private const int DataVersion = 12;

        // =================================================================
        //  ЖИЗНЕННЫЙ ЦИКЛ
        // =================================================================

        /// <summary>
        /// Создать контроллер и построить массив целиком (план + профиль).
        /// Возвращает ObjectId контроллера.
        /// </summary>
        public static ObjectId CreateArray(
            Database db,
            Transaction tr,
            FacingWallDefinition definition,
            Point3d position)
        {
            ObjectId controllerId = CreateController(db, tr, definition, position);

            BuildAll(tr, db, controllerId, definition);
            Save(controllerId, definition, tr);

            return controllerId;
        }

        /// <summary>
        /// Создать объект-контроллер и записать в него определение.
        /// Геометрия массива при этом не строится.
        /// </summary>
        public static ObjectId CreateController(
            Database db,
            Transaction tr,
            FacingWallDefinition definition,
            Point3d position)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));

            EnsureRegApp(db, tr);
            ObjectId blockDefId = EnsureControllerBlock(db, tr);

            var currentSpace = (BlockTableRecord)tr.GetObject(
                db.CurrentSpaceId, OpenMode.ForWrite);

            var controller = new BlockReference(position, blockDefId);
            controller.SetDatabaseDefaults();

            ObjectId controllerId = currentSpace.AppendEntity(controller);
            tr.AddNewlyCreatedDBObject(controller, true);

            controller.XData = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, XAppName),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, XRecordKey));

            SaveDefinition(tr, controller, definition);

            return controllerId;
        }

        /// <summary>
        /// Удалить массив целиком: все блоки, все проекции и сам контроллер.
        /// </summary>
        public static void Delete(ObjectId controllerId)
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            using (doc.LockDocument())
            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                FacingWallDefinition def = Load(controllerId, tr);

                if (def != null && def.Rows != null)
                {
                    foreach (FacingWallRowDefinition row in def.Rows)
                    {
                        if (row == null) continue;
                        FacingWallBuilder.EraseAll(tr, row.BlockIds);
                        FacingWallBuilder.EraseAll(tr, row.ProjectionIds);
                        FacingWallProjection.EraseGripLine(tr, row);
                    }
                }

                if (def != null)
                {
                    FacingWallLayoutGrip.EraseMarkers(tr, def);
                    FacingWallCivilProjection.Erase(tr, def);
                    FacingWallTables.Erase(tr, def);
                }

                if (!controllerId.IsNull && !controllerId.IsErased)
                {
                    var ent = tr.GetObject(controllerId, OpenMode.ForWrite, false) as Entity;
                    if (ent != null) ent.Erase();
                }

                tr.Commit();
            }
        }

        // =================================================================
        //  ПОСТРОЕНИЕ И ПЕРЕСТРОЕНИЕ
        // =================================================================

        /// <summary>
        /// Построить все ряды и их проекции. Связи записываются в определение.
        /// Существующие объекты рядов предварительно стираются.
        /// </summary>
        public static void BuildAll(
            Transaction tr,
            Database db,
            ObjectId controllerId,
            FacingWallDefinition def)
        {
            if (def == null || def.Rows == null) return;

            Alignment alignment = tr.GetObject(def.AlignmentId, OpenMode.ForRead) as Alignment;
            if (alignment == null)
                throw new InvalidOperationException("Не удалось получить Alignment.");

            // Проекции Civil ассоциативны, а блоки сейчас будут созданы заново —
            // старые повиснут на удалённых объектах. Стираем: восстановить их
            // может только пользователь командой FACINGWALLPROJECT.
            FacingWallCivilProjection.Erase(tr, def);

            var btr = (BlockTableRecord)tr.GetObject(
                SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);

            foreach (FacingWallRowDefinition row in def.Rows)
            {
                if (row == null) continue;

                FacingWallBuilder.EraseAll(tr, row.BlockIds);
                row.BlockIds = FacingWallBuilder.GenerateRow(def, row, alignment, db, btr, tr);

                FacingWallProjection.ReprojectRow(tr, db, def, row);
                FacingWallProjection.EnsureGripLine(tr, db, controllerId, def, row);
            }

            // Ручка массива в плане одна на все ряды — после них.
            FacingWallLayoutGrip.EnsureMarkers(tr, db, controllerId, def);

            // Таблички производные, как и всё остальное нарисованное: раскладка
            // только что изменилась, значит числа в них устарели ровно сейчас.
            // Сами по себе не заводятся — только обновляются, если пользователь
            // их уже построил командой FACINGWALLTABLE.
            if (!def.RowTableId.IsNull || !def.TotalsTableId.IsNull)
                FacingWallTables.Rebuild(tr, db, def);
        }

        /// <summary>
        /// Перестроить ОДИН ряд внутри уже открытой транзакции.
        /// Остальные ряды не затрагиваются.
        /// </summary>
        public static void RebuildRow(
            Transaction tr,
            Database db,
            ObjectId controllerId,
            FacingWallDefinition def,
            int rowIndex)
        {
            if (def == null) return;

            FacingWallRowDefinition row = def.GetRow(rowIndex);
            if (row == null) return;

            Alignment alignment = tr.GetObject(def.AlignmentId, OpenMode.ForRead) as Alignment;
            if (alignment == null) return;

            var btr = (BlockTableRecord)tr.GetObject(
                SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);

            // 0. проекции Civil висят на блоках, которые сейчас исчезнут
            FacingWallCivilProjection.Erase(tr, def);

            // 1. удаляются объекты только этого ряда
            FacingWallBuilder.EraseAll(tr, row.BlockIds);

            // 2. ряд строится заново
            row.BlockIds = FacingWallBuilder.GenerateRow(def, row, alignment, db, btr, tr);

            // 3. пересоздаются только его проекции
            FacingWallProjection.ReprojectRow(tr, db, def, row);

            // 4. отрезки-ручки не пересоздаются, а сдвигаются: их могут тянуть прямо сейчас
            FacingWallProjection.EnsureGripLine(tr, db, controllerId, def, row);

            // 5. границы массива могли измениться — подтягиваем плановую ручку
            FacingWallLayoutGrip.EnsureMarkers(tr, db, controllerId, def);
        }

        /// <summary>
        /// Перестроить один ряд массива. Открывает собственную транзакцию.
        /// </summary>
        public static void RebuildRow(ObjectId controllerId, int rowIndex)
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            using (doc.LockDocument())
            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                FacingWallDefinition def = Load(controllerId, tr);
                if (def == null) { tr.Commit(); return; }

                RebuildRow(tr, doc.Database, controllerId, def, rowIndex);

                // 5. обновляются сохранённые связи
                Save(controllerId, def, tr);
                tr.Commit();
            }
        }

        /// <summary>
        /// Полная перестройка массива. Выполняется только по явной команде.
        /// </summary>
        public static void RebuildAll(ObjectId controllerId)
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            using (doc.LockDocument())
            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                FacingWallDefinition def = Load(controllerId, tr);
                if (def == null) { tr.Commit(); return; }

                BuildAll(tr, doc.Database, controllerId, def);
                Save(controllerId, def, tr);
                tr.Commit();
            }
        }

        // =================================================================
        //  ХРАНЕНИЕ
        // =================================================================

        /// <summary>Записать определение в контроллер.</summary>
        public static void Save(ObjectId controllerId, FacingWallDefinition def, Transaction tr)
        {
            if (controllerId.IsNull || controllerId.IsErased || def == null) return;

            var controller = tr.GetObject(controllerId, OpenMode.ForWrite) as BlockReference;
            if (controller == null) return;

            SaveDefinition(tr, controller, def);
        }

        /// <summary>То же, но в уже открытую вставку (см. LoadFrom).</summary>
        public static void SaveTo(BlockReference controller, FacingWallDefinition def, Transaction tr)
        {
            if (controller == null || def == null) return;
            SaveDefinition(tr, controller, def);
        }

        /// <summary>Прочитать определение из контроллера. null, если это не контроллер.</summary>
        public static FacingWallDefinition Load(ObjectId controllerId, Transaction tr)
        {
            if (controllerId.IsNull || !controllerId.IsValid) return null;

            var controller = tr.GetObject(controllerId, OpenMode.ForRead) as BlockReference;
            return LoadFrom(controller, tr);
        }

        /// <summary>
        /// То же, но из уже открытой вставки. Нужно грипсам: там объект открыт
        /// самим AutoCAD, и повторно доставать его транзакцией нельзя.
        /// </summary>
        public static FacingWallDefinition LoadFrom(BlockReference controller, Transaction tr)
        {
            FacingWallDefinition def;
            Inspect(controller, tr, out def);
            return def;
        }

        /// <summary>
        /// То же чтение, но с внятным ответом «почему не прочиталось».
        ///
        /// Раньше все отказы выглядели одинаково — null, то есть «это не контроллер».
        /// Хуже всего это для записи из более новой версии плагина: пользователь
        /// со старой сборкой не отличал её от постороннего объекта и мог построить
        /// поверх новый массив.
        /// </summary>
        public static FacingWallRecordStatus Inspect(
            BlockReference controller, Transaction tr, out FacingWallDefinition def)
        {
            def = null;

            if (controller == null) return FacingWallRecordStatus.NotController;
            if (controller.ExtensionDictionary.IsNull) return FacingWallRecordStatus.NoRecord;

            var dict = tr.GetObject(controller.ExtensionDictionary, OpenMode.ForRead) as DBDictionary;
            if (dict == null || !dict.Contains(XRecordKey)) return FacingWallRecordStatus.NoRecord;

            var xrec = tr.GetObject(dict.GetAt(XRecordKey), OpenMode.ForRead) as Xrecord;
            if (xrec == null || xrec.Data == null) return FacingWallRecordStatus.NoRecord;

            try
            {
                def = Deserialize(controller.Database, xrec.Data, controller.Handle);
            }
            catch (FacingWallNewerVersionException)
            {
                return FacingWallRecordStatus.NewerVersion;
            }
            catch (System.Exception)
            {
                return FacingWallRecordStatus.Corrupt;
            }

            if (def == null) return FacingWallRecordStatus.Corrupt;

            return def.IsDetachedCopy
                ? FacingWallRecordStatus.DetachedCopy
                : FacingWallRecordStatus.Ok;
        }

        /// <summary>Это вставка-контроллер массива облицовки?</summary>
        public static bool IsController(Entity ent)
        {
            if (ent == null) return false;

            try
            {
                using (ResultBuffer rb = ent.GetXDataForApplication(XAppName))
                    return rb != null;
            }
            catch (System.Exception)
            {
                return false;
            }
        }

        /// <summary>Все контроллеры чертежа.</summary>
        public static List<ObjectId> FindAll(Database db, Transaction tr)
        {
            var found = new List<ObjectId>();

            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            if (!bt.Has(ControllerBlockName)) return found;

            var blockDef = (BlockTableRecord)tr.GetObject(bt[ControllerBlockName], OpenMode.ForRead);

            foreach (ObjectId id in blockDef.GetBlockReferenceIds(true, false))
            {
                if (id.IsErased) continue;
                var br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                if (IsController(br)) found.Add(id);
            }

            return found;
        }

        private static void SaveDefinition(
            Transaction tr, BlockReference controller, FacingWallDefinition def)
        {
            if (controller.ExtensionDictionary.IsNull)
            {
                if (!controller.IsWriteEnabled) controller.UpgradeOpen();
                controller.CreateExtensionDictionary();
            }

            var dict = (DBDictionary)tr.GetObject(
                controller.ExtensionDictionary, OpenMode.ForWrite);

            // Записываем собственный хэндл: по нему копия контроллера отличается
            // от оригинала. Заодно это делает копию самостоятельной сразу после
            // первой же перестройки — она сохранится уже под своим хэндлом.
            def.SelfHandle = controller.Handle.ToString();
            def.IsDetachedCopy = false;

            ResultBuffer data = Serialize(def);

            if (dict.Contains(XRecordKey))
            {
                var existing = (Xrecord)tr.GetObject(dict.GetAt(XRecordKey), OpenMode.ForWrite);
                existing.Data = data;
            }
            else
            {
                var xrec = new Xrecord { XlateReferences = true, Data = data };
                dict.SetAt(XRecordKey, xrec);
                tr.AddNewlyCreatedDBObject(xrec, true);
            }
        }

        // -----------------------------------------------------------------
        //  Сериализация. Плоский поток TypedValue, читается последовательно.
        //  Ссылки на объекты храним как Handle (стабилен между сессиями).
        // -----------------------------------------------------------------

        private static ResultBuffer Serialize(FacingWallDefinition def)
        {
            var rb = new ResultBuffer();

            rb.Add(new TypedValue((int)DxfCode.Text, "FACINGWALL"));
            rb.Add(new TypedValue((int)DxfCode.Int32, DataVersion));

            rb.Add(new TypedValue((int)DxfCode.Text, HandleOf(def.AlignmentId)));
            rb.Add(new TypedValue((int)DxfCode.Text, HandleOf(def.ProfileViewId)));
            rb.Add(new TypedValue((int)DxfCode.Text, def.MvBlockDefName ?? string.Empty));

            rb.Add(new TypedValue((int)DxfCode.Real, def.FaceOffset));
            rb.Add(new TypedValue((int)DxfCode.Real, def.BaseElevation));
            rb.Add(new TypedValue((int)DxfCode.Real, def.BlockWidth));
            rb.Add(new TypedValue((int)DxfCode.Real, def.BlockHeight));
            rb.Add(new TypedValue((int)DxfCode.Real, def.Scale));

            // версия 3
            rb.Add(new TypedValue((int)DxfCode.Int32, (int)def.ProjectionMode));
            rb.Add(new TypedValue((int)DxfCode.Text, def.ViewBlockName ?? string.Empty));

            // версия 5
            rb.Add(new TypedValue((int)DxfCode.Real, def.LayoutStartStation));
            rb.Add(new TypedValue((int)DxfCode.Real, def.LayoutEndStation));
            rb.Add(new TypedValue((int)DxfCode.Text, HandleOf(def.PlanStartGripId)));
            rb.Add(new TypedValue((int)DxfCode.Text, HandleOf(def.PlanEndGripId)));

            // версия 6
            rb.Add(new TypedValue((int)DxfCode.Text, HandleOf(def.ProfileStartGripId)));
            rb.Add(new TypedValue((int)DxfCode.Text, HandleOf(def.ProfileEndGripId)));

            // версия 7
            AddIdList(rb, def.CivilProjectionIds);

            // версия 8
            rb.Add(new TypedValue((int)DxfCode.Text, def.HalfMvBlockDefName ?? string.Empty));
            rb.Add(new TypedValue((int)DxfCode.Text, def.HalfViewBlockName ?? string.Empty));

            // версия 9
            rb.Add(new TypedValue((int)DxfCode.Real, def.BlockRotationOffset));

            // версия 10
            rb.Add(new TypedValue((int)DxfCode.Text, def.SelfHandle ?? "0"));

            // версия 11
            rb.Add(new TypedValue((int)DxfCode.Real, def.HalfBlockWidth));

            var customViews = def.CustomViewBlocks
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            rb.Add(new TypedValue((int)DxfCode.Int32, customViews.Count));

            foreach (KeyValuePair<string, string> pair in customViews)
            {
                rb.Add(new TypedValue((int)DxfCode.Text, pair.Key ?? string.Empty));
                rb.Add(new TypedValue((int)DxfCode.Text, pair.Value ?? string.Empty));
            }

            // версия 12
            rb.Add(new TypedValue((int)DxfCode.Real, def.RowSetback));
            rb.Add(new TypedValue((int)DxfCode.Text, HandleOf(def.RowTableId)));
            rb.Add(new TypedValue((int)DxfCode.Text, HandleOf(def.TotalsTableId)));

            var rows = def.Rows ?? new List<FacingWallRowDefinition>();
            rb.Add(new TypedValue((int)DxfCode.Int32, rows.Count));

            foreach (FacingWallRowDefinition row in rows)
            {
                rb.Add(new TypedValue((int)DxfCode.Int32, row.RowIndex));
                rb.Add(new TypedValue((int)DxfCode.Real, row.StartStation));
                rb.Add(new TypedValue((int)DxfCode.Real, row.EndStation));

                AddIdList(rb, row.BlockIds);
                AddIdList(rb, row.ProjectionIds);
                rb.Add(new TypedValue((int)DxfCode.Text, HandleOf(row.GripLineId)));

                // версия 8: замены блоков этого ряда
                var overrides = row.Overrides ?? new List<FacingWallBlockOverride>();
                rb.Add(new TypedValue((int)DxfCode.Int32, overrides.Count));

                foreach (FacingWallBlockOverride ov in overrides)
                {
                    rb.Add(new TypedValue((int)DxfCode.Real, ov.Station));
                    rb.Add(new TypedValue((int)DxfCode.Real, ov.Width));
                    rb.Add(new TypedValue((int)DxfCode.Text, ov.MvBlockDefName ?? string.Empty));
                }

                // версия 12: разрывы этого ряда
                var gaps = row.Gaps ?? new List<FacingWallRowGap>();
                rb.Add(new TypedValue((int)DxfCode.Int32, gaps.Count));

                foreach (FacingWallRowGap gap in gaps)
                {
                    rb.Add(new TypedValue((int)DxfCode.Real, gap.Start));
                    rb.Add(new TypedValue((int)DxfCode.Real, gap.End));
                }
            }

            return rb;
        }

        private static FacingWallDefinition Deserialize(Database db, ResultBuffer rb, Handle actual)
        {
            TypedValue[] v = rb.AsArray();
            int i = 0;

            string tag = v[i++].Value.ToString();
            if (tag != "FACINGWALL")
                throw new InvalidOperationException("Чужая запись в словаре контроллера.");

            int version = Convert.ToInt32(v[i++].Value);
            if (version < 1)
                throw new InvalidOperationException(
                    "Неподдерживаемая версия данных контроллера: " + version);

            if (version > DataVersion)
                throw new FacingWallNewerVersionException(version, DataVersion);

            var def = new FacingWallDefinition
            {
                AlignmentId = IdOf(db, v[i++].Value.ToString()),
                ProfileViewId = IdOf(db, v[i++].Value.ToString()),
                MvBlockDefName = v[i++].Value.ToString(),
                FaceOffset = Convert.ToDouble(v[i++].Value),
                BaseElevation = Convert.ToDouble(v[i++].Value),
                BlockWidth = Convert.ToDouble(v[i++].Value),
                BlockHeight = Convert.ToDouble(v[i++].Value),
                Scale = Convert.ToDouble(v[i++].Value)
            };

            if (version >= 3)
            {
                def.ProjectionMode = ToProjectionMode(Convert.ToInt32(v[i++].Value));
                def.ViewBlockName = v[i++].Value.ToString();
            }
            else
            {
                def.ProjectionMode = FacingWallProjection.DefaultMode;
                def.ViewBlockName = null;
            }

            // В версии 4 плановая ручка была одна и лежала вдоль трассы. Её
            // handle подбираем: EnsureMarkers переставит этот же отрезок
            // перпендикулярно, вместо того чтобы бросить его в чертеже сиротой.
            string legacyPlanHandle = null;

            if (version == 4)
            {
                legacyPlanHandle = v[i++].Value.ToString();
            }
            else if (version >= 5)
            {
                def.LayoutStartStation = Convert.ToDouble(v[i++].Value);
                def.LayoutEndStation = Convert.ToDouble(v[i++].Value);
                def.PlanStartGripId = IdOf(db, v[i++].Value.ToString());
                def.PlanEndGripId = IdOf(db, v[i++].Value.ToString());
            }

            if (version >= 6)
            {
                def.ProfileStartGripId = IdOf(db, v[i++].Value.ToString());
                def.ProfileEndGripId = IdOf(db, v[i++].Value.ToString());
            }

            if (version >= 7)
                def.CivilProjectionIds = ReadIdList(db, v, ref i);

            if (version >= 8)
            {
                def.HalfMvBlockDefName = NullIfEmpty(v[i++].Value.ToString());
                def.HalfViewBlockName = NullIfEmpty(v[i++].Value.ToString());
            }

            if (version >= 9)
                def.BlockRotationOffset = Convert.ToDouble(v[i++].Value);

            if (version >= 10)
                def.SelfHandle = NullIfEmpty(v[i++].Value.ToString());

            if (version >= 11)
            {
                def.HalfBlockWidth = Convert.ToDouble(v[i++].Value);

                int customCount = Convert.ToInt32(v[i++].Value);

                for (int k = 0; k < customCount; k++)
                {
                    string mvName = v[i++].Value.ToString();
                    string viewName = v[i++].Value.ToString();
                    def.SetCustomViewBlock(mvName, NullIfEmpty(viewName));
                }
            }

            if (version >= 12)
            {
                def.RowSetback = Convert.ToDouble(v[i++].Value);
                def.RowTableId = IdOf(db, v[i++].Value.ToString());
                def.TotalsTableId = IdOf(db, v[i++].Value.ToString());
            }

            int rowCount = Convert.ToInt32(v[i++].Value);

            for (int r = 0; r < rowCount; r++)
            {
                var row = new FacingWallRowDefinition
                {
                    RowIndex = Convert.ToInt32(v[i++].Value),
                    StartStation = Convert.ToDouble(v[i++].Value),
                    EndStation = Convert.ToDouble(v[i++].Value)
                };

                row.BlockIds = ReadIdList(db, v, ref i);
                row.ProjectionIds = ReadIdList(db, v, ref i);

                if (version >= 2)
                    row.GripLineId = IdOf(db, v[i++].Value.ToString());

                if (version >= 8)
                {
                    int overrideCount = Convert.ToInt32(v[i++].Value);

                    for (int k = 0; k < overrideCount; k++)
                    {
                        row.Overrides.Add(new FacingWallBlockOverride
                        {
                            Station = Convert.ToDouble(v[i++].Value),
                            Width = Convert.ToDouble(v[i++].Value),
                            MvBlockDefName = NullIfEmpty(v[i++].Value.ToString())
                        });
                    }
                }

                if (version >= 12)
                {
                    int gapCount = Convert.ToInt32(v[i++].Value);

                    for (int k = 0; k < gapCount; k++)
                    {
                        row.Gaps.Add(new FacingWallRowGap
                        {
                            Start = Convert.ToDouble(v[i++].Value),
                            End = Convert.ToDouble(v[i++].Value)
                        });
                    }
                }

                def.Rows.Add(row);
            }

            if (version < 5) RestoreLayoutFromRows(def, db, legacyPlanHandle);

            DetectCopy(def, actual);

            return def;
        }

        /// <summary>
        /// Копия контроллера несёт в записи хэндл оригинала — и, что важнее,
        /// хэндлы ЕГО блоков, проекций и отрезков-ручек. Работать по таким
        /// связям нельзя: удаление копии стёрло бы объекты оригинала.
        ///
        /// Связи сбрасываются, параметры остаются: FACINGWALLREBUILDALL строит
        /// копии собственные объекты и записывает её собственный хэндл, после
        /// чего это обычный самостоятельный массив. Блоки, скопированные вместе
        /// с ней, остаются ничьими — стирать их пользователю.
        ///
        /// Записи до версии 10 своего хэндла не содержат; там определить копию
        /// нечем, и они читаются как раньше.
        /// </summary>
        private static void DetectCopy(FacingWallDefinition def, Handle actual)
        {
            if (def == null) return;
            if (string.IsNullOrEmpty(def.SelfHandle) || def.SelfHandle == "0") return;

            if (string.Equals(def.SelfHandle, actual.ToString(),
                              StringComparison.OrdinalIgnoreCase))
                return;

            def.IsDetachedCopy = true;
            def.ClearObjectLinks();
        }

        /// <summary>
        /// До версии 5 границы раскладки отдельно не хранились — восстанавливаем
        /// их по рядам. Направление при этом теряется: старые массивы всегда
        /// раскладывались по возрастанию пикета, таким и остаётся.
        /// </summary>
        private static void RestoreLayoutFromRows(
            FacingWallDefinition def, Database db, string legacyPlanHandle)
        {
            double min = double.MaxValue;
            double max = double.MinValue;

            foreach (FacingWallRowDefinition row in def.Rows)
            {
                if (row == null) continue;
                if (row.StartStation < min) min = row.StartStation;
                if (row.EndStation > max) max = row.EndStation;
            }

            def.LayoutStartStation = min == double.MaxValue ? 0.0 : min;
            def.LayoutEndStation = max == double.MinValue ? 0.0 : max;

            if (legacyPlanHandle != null)
                def.PlanStartGripId = IdOf(db, legacyPlanHandle);
        }

        /// <summary>
        /// Число из записи -> режим. Неизвестное значение (запись из более новой
        /// версии плагина) откатывается к контуру, а не роняет чтение.
        /// </summary>
        private static FacingWallProjectionMode ToProjectionMode(int value)
        {
            if (Enum.IsDefined(typeof(FacingWallProjectionMode), value))
                return (FacingWallProjectionMode)value;

            return FacingWallProjection.DefaultMode;
        }

        private static void AddIdList(ResultBuffer rb, List<ObjectId> ids)
        {
            var list = ids ?? new List<ObjectId>();
            rb.Add(new TypedValue((int)DxfCode.Int32, list.Count));

            foreach (ObjectId id in list)
                rb.Add(new TypedValue((int)DxfCode.Text, HandleOf(id)));
        }

        private static List<ObjectId> ReadIdList(Database db, TypedValue[] v, ref int i)
        {
            int count = Convert.ToInt32(v[i++].Value);
            var list = new List<ObjectId>(count);

            for (int k = 0; k < count; k++)
            {
                ObjectId id = IdOf(db, v[i++].Value.ToString());
                if (!id.IsNull && !id.IsErased) list.Add(id);
            }

            return list;
        }

        /// <summary>Пустая строка в записи означает «не задано».</summary>
        private static string NullIfEmpty(string value)
        {
            return string.IsNullOrEmpty(value) ? null : value;
        }

        private static string HandleOf(ObjectId id)
        {
            return RwHandles.ToText(id);
        }

        /// <summary>
        /// Ссылка из записи. Стёртый объект теперь даёт ObjectId.Null, а не
        /// «живой» идентификатор стёртого: единое правило общего модуля.
        /// Для списков это ничего не меняет — ReadIdList и раньше отсеивал
        /// стёртых, — а одиночные ссылки (трасса, вид) корректнее проверять
        /// на IsNull, чем ловить исключение при открытии.
        /// </summary>
        private static ObjectId IdOf(Database db, string handleText)
        {
            return RwHandles.Resolve(db, handleText);
        }

        // =================================================================
        //  СЛУЖЕБНОЕ
        // =================================================================

        private static void EnsureRegApp(Database db, Transaction tr)
        {
            var rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (rat.Has(XAppName)) return;

            rat.UpgradeOpen();
            var rec = new RegAppTableRecord { Name = XAppName };
            rat.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
        }

        /// <summary>
        /// Определение служебного блока. Если его нет в чертеже — создаём,
        /// чтобы плагин не зависел от заранее подготовленного шаблона.
        /// </summary>
        private static ObjectId EnsureControllerBlock(Database db, Transaction tr)
        {
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            if (bt.Has(ControllerBlockName)) return bt[ControllerBlockName];

            bt.UpgradeOpen();

            var btr = new BlockTableRecord { Name = ControllerBlockName };
            ObjectId blockDefId = bt.Add(btr);
            tr.AddNewlyCreatedDBObject(btr, true);

            const double r = 0.5;

            var circle = new Circle(Point3d.Origin, Vector3d.ZAxis, r);
            btr.AppendEntity(circle);
            tr.AddNewlyCreatedDBObject(circle, true);

            var h = new Line(new Point3d(-r, 0.0, 0.0), new Point3d(r, 0.0, 0.0));
            btr.AppendEntity(h);
            tr.AddNewlyCreatedDBObject(h, true);

            var vLine = new Line(new Point3d(0.0, -r, 0.0), new Point3d(0.0, r, 0.0));
            btr.AppendEntity(vLine);
            tr.AddNewlyCreatedDBObject(vLine, true);

            return blockDefId;
        }
    }
}
