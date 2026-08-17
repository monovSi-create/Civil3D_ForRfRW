using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Civil3D_commands.Shared;
using AcColor = Autodesk.AutoCAD.Colors.Color;
using ColorMethod = Autodesk.AutoCAD.Colors.ColorMethod;
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;
using ObjectId = Autodesk.AutoCAD.DatabaseServices.ObjectId;

namespace Civil3D_commands.AssociativeBreaks
{
    /// <summary>
    /// Объект-контроллер связи: вставка служебного блока рядом с видом профиля
    /// с подписью «имя коридора-имя профиля».
    ///
    /// Роль та же, что у контроллера облицовки (<c>FacingWallController</c>):
    /// единственный объект, который пользователь выбирает мышью, чтобы попасть
    /// в данные. Отличие в том, что данные лежат не в его расширенном словаре,
    /// а в общем словаре чертежа (<see cref="BreakLinkStore"/>): связей может
    /// быть много, и часть из них живёт в чертежах, где контроллера ещё нет —
    /// такие связи обязаны читаться и без него.
    ///
    /// Контроллер несёт в XData Guid своей связи. Как и у прокси, **владение
    /// проверяется по хэндлу из модели, а не по Guid**: у копии вставки XData
    /// та же самая, и без этой проверки копия управляла бы чужой связью
    /// (ровно эта ошибка уже была у прокси-линий).
    ///
    /// Подпись — атрибут блока, а не текст в определении: определение одно
    /// на все связи, а подписи у них разные.
    /// </summary>
    public static class BreakController
    {
        public const string BlockName = "RW_BREAK_CONTROLLER";
        public const string XAppName = "RW_BREAKLINK";
        public const string AttributeTag = "LABEL";

        /// <summary>Слой контроллеров. Он единственный никогда не блокируется — иначе связь не выбрать.</summary>
        public const string LayerName = "RW_BREAK_CONTROLLER";

        /// <summary>
        /// Геометрия определения блока построена в единичном размере, а нужный
        /// размер задаётся масштабом вставки. Так один и тот же блок годится
        /// и для чертежа в метрах, и для чертежа в миллиметрах.
        /// </summary>
        private const double UnitRadius = 0.6;

        // ------------------------------------------------------------------
        //  СОЗДАНИЕ И ОБНОВЛЕНИЕ
        // ------------------------------------------------------------------

        /// <summary>
        /// Создать контроллер для связи и записать его хэндл в неё же.
        /// Если контроллер уже жив, только обновляет подпись и цвет.
        /// </summary>
        public static ObjectId Ensure(Transaction tr, Database db, BreakLink link, int slot)
        {
            if (link == null) return ObjectId.Null;

            ObjectId existing = RwHandles.Resolve(db, link.ControllerHandle);
            if (!existing.IsNull)
            {
                Update(tr, db, link);
                return existing;
            }

            EnsureRegApp(tr, db);
            ObjectId blockDefId = EnsureBlockDefinition(tr, db);
            if (blockDefId.IsNull) return ObjectId.Null;

            // Размер задаётся масштабом вставки и берётся из чертежа — той же
            // высотой текста, что и у надписей. Аннотативным контроллер
            // намеренно не делается: аннотативный объект пропадает с экрана,
            // если текущего масштаба нет в его списке, а это единственный
            // объект, который обязан выбираться всегда.
            double height = BreakOverlay.TextHeight(tr, db);
            Point3d position = DefaultPosition(tr, db, link, slot, height);

            var space = (BlockTableRecord)tr.GetObject(
                SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);

            var controller = new BlockReference(position, blockDefId);
            controller.SetDatabaseDefaults();
            controller.ScaleFactors = new Scale3d(height);
            controller.LayerId = BreakOverlay.EnsureLayer(tr, db, LayerName, 7);

            ObjectId controllerId = space.AppendEntity(controller);
            tr.AddNewlyCreatedDBObject(controller, true);

            controller.Color = AcColor.FromColorIndex(ColorMethod.ByAci, ColorOf(link));

            controller.XData = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, XAppName),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, link.Id.ToString("N")));

            // Атрибуты добавляются ПОСЛЕ вставки в чертёж: SetAttributeFromBlock
            // берёт BlockTransform, а он до этого момента не определён.
            AppendAttributes(tr, db, controller, blockDefId, link.Label);

            link.ControllerHandle = controller.Handle;
            return controllerId;
        }

        /// <summary>Подпись и цвет контроллера — из связи. Нет контроллера — ничего не делает.</summary>
        public static void Update(Transaction tr, Database db, BreakLink link)
        {
            if (link == null) return;

            ObjectId id = RwHandles.Resolve(db, link.ControllerHandle);
            if (id.IsNull) return;

            try
            {
                var controller = tr.GetObject(id, OpenMode.ForWrite) as BlockReference;
                if (controller == null) return;

                controller.Color = AcColor.FromColorIndex(ColorMethod.ByAci, ColorOf(link));

                bool written = false;
                foreach (ObjectId attId in controller.AttributeCollection)
                {
                    var att = tr.GetObject(attId, OpenMode.ForWrite) as AttributeReference;
                    if (att == null) continue;
                    if (!string.Equals(att.Tag, AttributeTag, StringComparison.OrdinalIgnoreCase)) continue;

                    att.TextString = link.Label;
                    written = true;
                }

                // Вставка сделана прежней версией — атрибута у неё нет.
                if (!written)
                    AppendAttributes(tr, db, controller, controller.BlockTableRecord, link.Label);
            }
            catch (System.Exception)
            {
                // Подпись — оформление. Механика связи от неё не зависит.
            }
        }

        /// <summary>Стереть контроллер связи.</summary>
        public static void Erase(Transaction tr, Database db, BreakLink link)
        {
            if (link == null) return;

            ObjectId id = RwHandles.Resolve(db, link.ControllerHandle);
            if (id.IsNull) return;

            try
            {
                var ent = tr.GetObject(id, OpenMode.ForWrite) as Entity;
                if (ent != null) ent.Erase();
            }
            catch (System.Exception) { }

            link.ControllerHandle = new Handle(0);
        }

        // ------------------------------------------------------------------
        //  ОПОЗНАНИЕ
        // ------------------------------------------------------------------

        /// <summary>Guid связи из XData вставки. null — это не контроллер.</summary>
        public static Guid? GetLinkId(Entity ent)
        {
            if (ent == null) return null;

            try
            {
                ResultBuffer rb = ent.GetXDataForApplication(XAppName);
                if (rb == null) return null;

                foreach (TypedValue tv in rb)
                    if (tv.TypeCode == (int)DxfCode.ExtendedDataAsciiString &&
                        Guid.TryParseExact(tv.Value.ToString(), "N", out Guid g))
                        return g;
            }
            catch (System.Exception) { }

            return null;
        }

        /// <summary>
        /// Цвет связи. Ноль в записи означает «ещё не назначен» — тогда цвет
        /// берётся из Guid связи. Guid.GetHashCode считается по байтам, поэтому
        /// один и тот же Guid даёт один и тот же цвет и после перезапуска.
        /// </summary>
        public static short ColorOf(BreakLink link)
        {
            if (link == null) return 7;
            if (link.ColorIndex > 0) return link.ColorIndex;
            return BreakOverlay.RandomColor(link.Id);
        }

        // ------------------------------------------------------------------
        //  СЛУЖЕБНОЕ
        // ------------------------------------------------------------------

        private static void AppendAttributes(Transaction tr, Database db,
                                             BlockReference controller,
                                             ObjectId blockDefId, string label)
        {
            try
            {
                var btr = tr.GetObject(blockDefId, OpenMode.ForRead) as BlockTableRecord;
                if (btr == null) return;

                foreach (ObjectId id in btr)
                {
                    var def = tr.GetObject(id, OpenMode.ForRead) as AttributeDefinition;
                    if (def == null || def.Constant) continue;

                    var att = new AttributeReference();
                    att.SetAttributeFromBlock(def, controller.BlockTransform);
                    att.TextString = label;

                    controller.AttributeCollection.AppendAttribute(att);
                    tr.AddNewlyCreatedDBObject(att, true);
                }
            }
            catch (System.Exception)
            {
                // Без подписи контроллер всё равно выбирается и работает.
            }
        }

        private static void EnsureRegApp(Transaction tr, Database db)
        {
            var rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (rat.Has(XAppName)) return;

            rat.UpgradeOpen();
            var rec = new RegAppTableRecord { Name = XAppName };
            rat.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
        }

        /// <summary>
        /// Определение блока: кружок с крестом (как у контроллера облицовки —
        /// он узнаётся с первого взгляда) и атрибут подписи справа от него.
        /// Всё в единичном размере, см. <see cref="UnitRadius"/>.
        /// </summary>
        private static ObjectId EnsureBlockDefinition(Transaction tr, Database db)
        {
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            if (bt.Has(BlockName)) return bt[BlockName];

            bt.UpgradeOpen();

            var btr = new BlockTableRecord { Name = BlockName };
            ObjectId blockDefId = bt.Add(btr);
            tr.AddNewlyCreatedDBObject(btr, true);

            const double r = UnitRadius;

            var circle = new Circle(Point3d.Origin, Vector3d.ZAxis, r);
            circle.ColorIndex = 0;                       // ByBlock: цвет задаёт вставка
            btr.AppendEntity(circle);
            tr.AddNewlyCreatedDBObject(circle, true);

            var h = new Line(new Point3d(-r, 0.0, 0.0), new Point3d(r, 0.0, 0.0));
            h.ColorIndex = 0;
            btr.AppendEntity(h);
            tr.AddNewlyCreatedDBObject(h, true);

            var v = new Line(new Point3d(0.0, -r, 0.0), new Point3d(0.0, r, 0.0));
            v.ColorIndex = 0;
            btr.AppendEntity(v);
            tr.AddNewlyCreatedDBObject(v, true);

            var att = new AttributeDefinition
            {
                Tag = AttributeTag,
                Prompt = "Связь",
                TextString = "коридор-профиль",
                Height = 1.0,
                Position = new Point3d(r * 2.0, -0.5, 0.0),
                Justify = AttachmentPoint.BaseLeft,
                ColorIndex = 0,
                Verifiable = false,
                LockPositionInBlock = true
            };

            // Стиль текста — тот, который выбран в чертеже для нового текста.
            if (!db.Textstyle.IsNull) att.TextStyleId = db.Textstyle;

            btr.AppendEntity(att);
            tr.AddNewlyCreatedDBObject(att, true);

            return blockDefId;
        }

        /// <summary>
        /// Место контроллера: над левым верхним углом вида профиля, столбиком.
        /// Связей на одном виде может быть несколько, поэтому каждая следующая
        /// поднимается ещё на строку — иначе они легли бы одна на другую.
        ///
        /// Вставку потом можно двигать как обычный блок: положение нигде
        /// не пересчитывается и ни на что не влияет.
        /// </summary>
        private static Point3d DefaultPosition(Transaction tr, Database db,
                                               BreakLink link, int slot, double height)
        {
            double step = height * 2.0;

            try
            {
                var pv = RwHandles.Open<Autodesk.Civil.DatabaseServices.ProfileView>(
                    tr, db, link.ProfileViewHandle, OpenMode.ForRead);

                if (pv != null)
                {
                    Extents3d ext = pv.GeometricExtents;
                    double x = Math.Min(ext.MinPoint.X, ext.MaxPoint.X);
                    double y = Math.Max(ext.MinPoint.Y, ext.MaxPoint.Y);
                    return new Point3d(x, y + step * (slot + 1), 0.0);
                }
            }
            catch (System.Exception)
            {
                // Габаритов нет (вид только что создан и ещё не отрисован).
            }

            return new Point3d(0.0, step * (slot + 1), 0.0);
        }
    }
}
