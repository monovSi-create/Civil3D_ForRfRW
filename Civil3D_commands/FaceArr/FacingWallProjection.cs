using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.DatabaseServices;
using Civil3D_commands.Shared;
// Entity и ObjectId есть и в AutoCAD-, и в Civil-пространстве имён — снимаем неоднозначность.
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;
using ObjectId = Autodesk.AutoCAD.DatabaseServices.ObjectId;

namespace Civil3D_commands.FaceArr
{
    /// <summary>
    /// Изолированный модуль профильного отображения.
    /// Вся логика Profile View находится только здесь.
    ///
    /// Проекции строятся вручную (создаём/стираем свои объекты), а не штатной
    /// проекцией Civil 3D: перестроение по ТЗ идёт поряднo, то есть мы обязаны
    /// удалять и создавать ровно объекты одного ряда.
    ///
    /// Пикет/отметка переводятся в координаты вида через
    /// ProfileView.FindXYAtStationAndElevation, поэтому вертикальное
    /// преувеличение вида учитывается автоматически.
    /// </summary>
    public static class FacingWallProjection
    {
        /// <summary>
        /// Режим по умолчанию для вновь создаваемых массивов.
        ///
        /// Outline, потому что вид профиля лежит в плоскости XY чертежа: MVBlock,
        /// вставленный в него, показывает свой ВИД СВЕРХУ (BlockWidth x глубина
        /// блока), а не фасад. Контур же геометрически верен всегда.
        /// У существующего массива режим меняется командой FACINGWALLMODE и
        /// хранится в его контроллере.
        /// </summary>
        public const FacingWallProjectionMode DefaultMode = FacingWallProjectionMode.Outline;

        /// <summary>Слой профильных объектов. Создаётся при необходимости.</summary>
        public const string LayerName = "RW_FACINGWALL_PROFILE";

        /// <summary>Слой отрезков-ручек. Отдельный, чтобы их можно было погасить.</summary>
        public const string GripLayerName = "RW_FACINGWALL_GRIP";

        /// <summary>
        /// Построить проекции ОДНОГО ряда. Возвращает созданные объекты.
        /// </summary>
        public static List<ObjectId> ProjectRow(
            Transaction tr,
            Database db,
            FacingWallDefinition def,
            FacingWallRowDefinition row)
        {
            var created = new List<ObjectId>();

            if (def == null || row == null) return created;
            if (def.ProfileViewId.IsNull) return created;

            var pv = tr.GetObject(def.ProfileViewId, OpenMode.ForRead) as ProfileView;
            if (pv == null) return created;

            // В режиме MvBlock профиль наполняет не этот модуль, а штатный
            // инструмент Civil 3D — своих объектов мы там не создаём вовсе.
            // См. FacingWallCivilProjection.
            if (def.ProjectionMode == FacingWallProjectionMode.MvBlock) return created;

            double elevBottom = FacingWallBuilder.RowElevation(def, row);
            double elevTop = elevBottom + def.BlockHeight;

            // за пределами вида проекция бессмысленна
            double stMin = pv.StationStart;
            double stMax = pv.StationEnd;

            var btr = (BlockTableRecord)tr.GetObject(
                SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);

            ObjectId layerId = EnsureLayer(tr, db);

            // Плоские блоки разрешаем по одному разу на имя, а не на вставку:
            // в ряду теперь встречаются целые, половинки и заменённые.
            var block2dCache = new Dictionary<string, ObjectId>();

            foreach (FacingWallBlockPlacement block in FacingWallBuilder.EnumerateBlocks(def, row))
            {
                double stationEnd = block.Station + block.Width;

                if (stationEnd < stMin || block.Station > stMax) continue;

                // Диагональ габарита блока в координатах вида. null — пикет вне
                // вида профиля: такой блок пропускаем, а не рисуем по NaN.
                Point3d[] box = RwGeometry.ProfileSegment(
                    pv, block.Station, elevBottom, stationEnd, elevTop);
                if (box == null) continue;

                Point3d lowerLeft = box[0];
                double x2 = box[1].X, y2 = box[1].Y;
                double x1 = lowerLeft.X, y1 = lowerLeft.Y;

                ObjectId id;
                if (def.ProjectionMode == FacingWallProjectionMode.Block2d)
                {
                    ObjectId blockDefId = ResolveBlock2dFor(db, tr, block2dCache, def, block);
                    if (blockDefId.IsNull) continue;

                    id = CreateBlock2dAt(
                        tr, btr, blockDefId, block.Width, def, lowerLeft, x2 - x1, y2 - y1);
                }
                else
                {
                    id = CreateOutline(tr, btr, lowerLeft, new Point3d(x2, y2, 0.0));
                }

                if (id.IsNull) continue;

                var ent = (Entity)tr.GetObject(id, OpenMode.ForWrite);
                ent.LayerId = layerId;

                created.Add(id);
            }

            return created;
        }

        /// <summary>
        /// Построить проекции всех рядов и записать связи в определение.
        /// </summary>
        public static void ProjectAll(
            Transaction tr,
            Database db,
            FacingWallDefinition def)
        {
            if (def == null || def.Rows == null) return;

            foreach (FacingWallRowDefinition row in def.Rows)
            {
                if (row == null) continue;

                FacingWallBuilder.EraseAll(tr, row.ProjectionIds);
                row.ProjectionIds = ProjectRow(tr, db, def, row);
            }
        }

        /// <summary>
        /// Пересоздать проекции одного ряда: старые удаляются, новые записываются в ряд.
        /// </summary>
        public static void ReprojectRow(
            Transaction tr,
            Database db,
            FacingWallDefinition def,
            FacingWallRowDefinition row)
        {
            if (row == null) return;

            FacingWallBuilder.EraseAll(tr, row.ProjectionIds);
            row.ProjectionIds = ProjectRow(tr, db, def, row);
        }

        /// <summary>Удалить проекции ряда, не создавая новых.</summary>
        public static void EraseRowProjections(
            Transaction tr,
            FacingWallRowDefinition row)
        {
            if (row == null) return;

            FacingWallBuilder.EraseAll(tr, row.ProjectionIds);
            row.ProjectionIds = new List<ObjectId>();
        }

        // -----------------------------------------------------------------

        private static ObjectId CreateOutline(
            Transaction tr, BlockTableRecord btr, Point3d lowerLeft, Point3d upperRight)
        {
            var pl = new Polyline(4);
            pl.AddVertexAt(0, new Point2d(lowerLeft.X, lowerLeft.Y), 0, 0, 0);
            pl.AddVertexAt(1, new Point2d(upperRight.X, lowerLeft.Y), 0, 0, 0);
            pl.AddVertexAt(2, new Point2d(upperRight.X, upperRight.Y), 0, 0, 0);
            pl.AddVertexAt(3, new Point2d(lowerLeft.X, upperRight.Y), 0, 0, 0);
            pl.Closed = true;
            pl.SetDatabaseDefaults();

            ObjectId id = btr.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);
            return id;
        }

        /// <summary>
        /// Вставка обычного 2D-блока AutoCAD.
        ///
        /// В отличие от MVBlock это не AEC-объект: на весь массив приходится одно
        /// определение блока и N лёгких вставок. Такой чертёж переживает экспорт
        /// в чистый AutoCAD без разбиения каждой вставки в отдельный блок.
        /// </summary>
        /// <summary>
        /// Плоский блок для конкретного места ряда: целый, половинка или
        /// заменённый. Явно заданное имя важнее (для целого — ViewBlockName,
        /// для половинки — HalfViewBlockName), иначе блок подбирается из состава
        /// соответствующего многовидового: вид спереди, а при его отсутствии
        /// первый блок состава.
        ///
        /// Именно здесь пропадали половинки: раньше запасным вариантом был
        /// только вид спереди, и половинчатый MVBlock без такого назначения
        /// давал ObjectId.Null, а место молча пропускалось.
        /// </summary>
        private static ObjectId ResolveBlock2dFor(
            Database db, Transaction tr, Dictionary<string, ObjectId> cache,
            FacingWallDefinition def, FacingWallBlockPlacement block)
        {
            string explicitName = null;

            if (block.Kind == FacingWallBlockKind.Whole) explicitName = def.ViewBlockName;
            else if (block.Kind == FacingWallBlockKind.Half) explicitName = def.HalfViewBlockName;
            else if (block.Kind == FacingWallBlockKind.Custom)
                explicitName = def.GetCustomViewBlock(block.MvBlockDefName);

            string key = explicitName ?? ("mv:" + (block.MvBlockDefName ?? string.Empty));

            ObjectId id;
            if (cache.TryGetValue(key, out id)) return id;

            try
            {
                string name = explicitName;

                if (string.IsNullOrEmpty(name))
                    name = FacingWallBuilder.GetProfileViewBlockName(db, tr, block.MvBlockDefName);

                id = string.IsNullOrEmpty(name)
                    ? ObjectId.Null
                    : FacingWallBuilder.ResolveBlock2d(db, tr, name);
            }
            catch (System.Exception)
            {
                id = ObjectId.Null;   // блок пропал — это место пропускаем
            }

            cache[key] = id;
            return id;
        }

        /// <summary>
        /// Вставка плоского блока в габарит места.
        ///
        /// По горизонтали блок ЦЕНТРИРУЕТСЯ: базовая точка облицовочного блока
        /// лежит в его середине — ровно так же, как у многовидового в плане
        /// (см. TryPlaceOnAlignment). Раньше вставка шла в левый нижний угол,
        /// и на профиле блоки стояли на полблока левее своих мест.
        ///
        /// По вертикали базой остаётся НИЗ ряда: там базовая точка и есть,
        /// это подтверждено тем, что ряды по отметкам всегда стояли верно.
        /// </summary>
        private static ObjectId CreateBlock2dAt(
            Transaction tr, BlockTableRecord btr, ObjectId blockDefId, double blockWidth,
            FacingWallDefinition def, Point3d lowerLeft, double dx, double dy)
        {
            double sx, sy;
            ViewScales(def, blockWidth, dx, dy, out sx, out sy);

            var insertion = new Point3d(
                lowerLeft.X + dx / 2.0, lowerLeft.Y, lowerLeft.Z);

            var br = new BlockReference(insertion, blockDefId);
            br.SetDatabaseDefaults();

            br.Normal = Vector3d.ZAxis;
            br.Rotation = 0.0;
            br.ScaleFactors = new Scale3d(def.Scale * sx, def.Scale * sy, def.Scale);

            ObjectId id = btr.AppendEntity(br);
            tr.AddNewlyCreatedDBObject(br, true);
            return id;
        }

        /// <summary>
        /// Масштабы вида: сколько единиц чертежа приходится на блок по X и по Y.
        /// Так вставка растягивается вместе с вертикальным преувеличением вида.
        /// Ширина берётся фактическая — у половинки и заменённого блока она своя.
        /// Предполагается, что геометрия блока занимает ровно эту ширину x BlockHeight.
        /// </summary>
        private static void ViewScales(
            FacingWallDefinition def, double blockWidth,
            double dx, double dy, out double sx, out double sy)
        {
            sx = blockWidth > 1e-9 ? dx / blockWidth : def.Scale;
            sy = def.BlockHeight > 1e-9 ? dy / def.BlockHeight : def.Scale;
        }

        private static ObjectId EnsureLayer(Transaction tr, Database db)
        {
            return EnsureLayer(tr, db, LayerName, 30);
        }

        private static ObjectId EnsureLayer(
            Transaction tr, Database db, string name, short colorIndex)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);

            if (lt.Has(name)) return lt[name];

            lt.UpgradeOpen();
            var ltr = new LayerTableRecord
            {
                Name = name,
                Color = Color.FromColorIndex(ColorMethod.ByAci, colorIndex)
            };

            ObjectId id = lt.Add(ltr);
            tr.AddNewlyCreatedDBObject(ltr, true);
            return id;
        }

        // =================================================================
        //  ОТРЕЗОК-РУЧКА РЯДА
        //
        //  Носитель интерактивного редактирования. Ручки на его концах и в
        //  середине рисует сам AutoCAD — мы не выдаём собственных GripData,
        //  а только перехватываем перемещение (см. FacingWallGrips).
        // =================================================================

        /// <summary>
        /// Создать отрезок-ручку ряда либо подвинуть существующий.
        /// Именно подвинуть: стирать объект, который сейчас тянут мышью, нельзя.
        /// </summary>
        public static void EnsureGripLine(
            Transaction tr,
            Database db,
            ObjectId controllerId,
            FacingWallDefinition def,
            FacingWallRowDefinition row)
        {
            if (def == null || row == null || def.ProfileViewId.IsNull) return;

            var pv = tr.GetObject(def.ProfileViewId, OpenMode.ForRead) as ProfileView;
            if (pv == null) return;

            double elevation =
                FacingWallBuilder.RowElevation(def, row) + def.BlockHeight / 2.0;

            // Горизонтальный отрезок на середине высоты ряда.
            Point3d[] pts = RwGeometry.ProfileSegment(
                pv, row.StartStation, elevation, row.EndStation, elevation);
            if (pts == null) return;

            Point3d start = pts[0];
            Point3d end = pts[1];

            // Существующий — двигаем. Выходим в любом случае: создавать второй
            // отрезок на тот же ряд нельзя, даже если открыть этот не удалось.
            if (!row.GripLineId.IsNull && row.GripLineId.IsValid && !row.GripLineId.IsErased)
            {
                try
                {
                    var existing = tr.GetObject(row.GripLineId, OpenMode.ForWrite, false) as Line;
                    if (existing != null)
                    {
                        existing.StartPoint = start;
                        existing.EndPoint = end;
                    }
                }
                catch (Autodesk.AutoCAD.Runtime.Exception)
                {
                    // Объект уже открыт на запись грип-системой — трогать его отсюда
                    // нельзя. Геометрию поправит FacingWallGrips после транзакции.
                }
                return;
            }

            var btr = (BlockTableRecord)tr.GetObject(
                SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);

            var line = new Line(start, end);
            line.SetDatabaseDefaults();
            line.LayerId = EnsureLayer(tr, db, GripLayerName, 2);

            row.GripLineId = btr.AppendEntity(line);
            tr.AddNewlyCreatedDBObject(line, true);

            FacingWallGripTag.Set(tr, db, line, controllerId, row.RowIndex);
        }

        /// <summary>Удалить отрезок-ручку ряда.</summary>
        public static void EraseGripLine(Transaction tr, FacingWallRowDefinition row)
        {
            if (row == null || row.GripLineId.IsNull) return;

            FacingWallBuilder.EraseAll(tr, new List<ObjectId> { row.GripLineId });
            row.GripLineId = ObjectId.Null;
        }

        /// <summary>
        /// Прочитать метку отрезка-ручки. false, если это не наш объект.
        /// Оставлено обёрткой: метки теперь общие для плана и профиля.
        /// </summary>
        public static bool TryReadGripTag(
            Entity ent, out ObjectId controllerId, out int rowIndex)
        {
            return FacingWallGripTag.TryRead(ent, out controllerId, out rowIndex);
        }
    }
}
