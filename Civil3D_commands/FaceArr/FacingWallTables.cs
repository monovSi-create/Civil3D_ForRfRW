using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.DatabaseServices;
using Civil3D_commands.Shared;
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;
using ObjectId = Autodesk.AutoCAD.DatabaseServices.ObjectId;
using Table = Autodesk.AutoCAD.DatabaseServices.Table;

namespace Civil3D_commands.FaceArr
{
    /// <summary>
    /// Две таблички слева от вида профиля: сколько блоков в каждом ряду
    /// и сколько всего по каждому наименованию.
    ///
    /// Считаются по <see cref="FacingWallBuilder.EnumerateBlocks"/> —
    /// единственному источнику истины по раскладке. Поэтому в них само собой
    /// учтено всё: половинки, заменители и разрывы рядов. Считать «длину ряда
    /// делить на ширину блока» было бы проще ровно до первого заменителя.
    ///
    /// Таблички производные: перестраиваются целиком, как блоки и проекции.
    /// </summary>
    public static class FacingWallTables
    {
        /// <summary>Отступ табличек от левого края вида профиля, в долях их ширины.</summary>
        private const double GapFactor = 0.15;

        /// <summary>Ширина столбца с числом относительно столбца с именем.</summary>
        private const double NarrowColumnFactor = 0.45;

        /// <summary>Запасная ширина столбца, если из чертежа её взять неоткуда.</summary>
        private const double FallbackColumnWidth = 20.0;

        // =================================================================
        //  ПОСТРОЕНИЕ
        // =================================================================

        /// <summary>
        /// Перестроить обе таблички. Возвращает false, если строить не по чему
        /// (нет вида профиля или он ещё не отрисован).
        /// </summary>
        public static bool Rebuild(Transaction tr, Database db, FacingWallDefinition def)
        {
            if (def == null) return false;

            Erase(tr, def);

            var pv = tr.GetObject(def.ProfileViewId, OpenMode.ForRead) as ProfileView;
            if (pv == null) return false;

            var alignment = tr.GetObject(def.AlignmentId, OpenMode.ForRead) as Alignment;

            double left, top;
            if (!Anchor(pv, out left, out top)) return false;

            var counts = CountBlocks(def, alignment);

            var space = (BlockTableRecord)tr.GetObject(
                SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);

            double width = ColumnWidth(tr, db);
            double gap = width * GapFactor;

            Table rows = BuildRowTable(tr, db, space, def, pv, counts, width);
            if (rows != null)
            {
                // Правый край таблички — у левого края вида, с зазором.
                rows.Position = new Point3d(left - rows.Width - gap, RowTableTop(rows, top), 0.0);
                def.RowTableId = rows.ObjectId;
            }

            Table totals = BuildTotalsTable(tr, db, space, def, counts, width);
            if (totals != null)
            {
                double totalsTop = rows != null
                    ? rows.Position.Y - rows.Height - gap
                    : top;

                totals.Position = new Point3d(left - totals.Width - gap, totalsTop, 0.0);
                def.TotalsTableId = totals.ObjectId;
            }

            return rows != null || totals != null;
        }

        /// <summary>Стереть обе таблички и забыть о них.</summary>
        public static void Erase(Transaction tr, FacingWallDefinition def)
        {
            if (def == null) return;

            EraseOne(tr, def.RowTableId);
            EraseOne(tr, def.TotalsTableId);

            def.RowTableId = ObjectId.Null;
            def.TotalsTableId = ObjectId.Null;
        }

        private static void EraseOne(Transaction tr, ObjectId id)
        {
            if (id.IsNull || !id.IsValid || id.IsErased) return;

            try
            {
                var ent = tr.GetObject(id, OpenMode.ForWrite, false) as Entity;
                if (ent != null) ent.Erase();
            }
            catch (System.Exception)
            {
                // Табличку мог стереть пользователь — связь просто устарела.
            }
        }

        // =================================================================
        //  ПОДСЧЁТ
        // =================================================================

        /// <summary>Сколько блоков в ряду и сколько всего по каждому имени.</summary>
        public class BlockCounts
        {
            /// <summary>Номер ряда → сколько в нём блоков.</summary>
            public Dictionary<int, int> ByRow { get; }
                = new Dictionary<int, int>();

            /// <summary>Имя определения MVBlock → сколько таких блоков во всей стене.</summary>
            public Dictionary<string, int> ByName { get; }
                = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            public int Total { get; set; }

            public int RowCount(int rowIndex)
            {
                int n;
                return ByRow.TryGetValue(rowIndex, out n) ? n : 0;
            }
        }

        /// <summary>
        /// Пересчитать блоки по раскладке.
        ///
        /// Блоки, не помещающиеся на трассу, НЕ считаются: их не строит и
        /// <see cref="FacingWallBuilder.GenerateRow"/>, а табличка обязана
        /// показывать то, что в чертеже, а не то, что задумано.
        /// </summary>
        public static BlockCounts CountBlocks(FacingWallDefinition def, Alignment alignment)
        {
            var counts = new BlockCounts();
            if (def == null || def.Rows == null) return counts;

            double alignStart = alignment != null ? alignment.StartingStation : double.MinValue;
            double alignEnd = alignment != null ? alignment.EndingStation : double.MaxValue;
            const double eps = 1e-6;

            foreach (FacingWallRowDefinition row in def.Rows)
            {
                if (row == null) continue;

                int inRow = 0;

                foreach (FacingWallBlockPlacement block in
                         FacingWallBuilder.EnumerateBlocks(def, row))
                {
                    if (block.Station < alignStart - eps) continue;
                    if (block.Station + block.Width > alignEnd + eps) continue;

                    inRow++;
                    counts.Total++;

                    string name = string.IsNullOrEmpty(block.MvBlockDefName)
                        ? "(без имени)"
                        : block.MvBlockDefName;

                    int already;
                    counts.ByName[name] = counts.ByName.TryGetValue(name, out already)
                        ? already + 1
                        : 1;
                }

                counts.ByRow[row.RowIndex] = inRow;
            }

            return counts;
        }

        // =================================================================
        //  ТАБЛИЧКА «БЛОКОВ В РЯДУ»
        // =================================================================

        /// <summary>
        /// Строка на каждый ряд, сверху вниз — как ряды и лежат в стене.
        /// Высота строки равна высоте ряда В КООРДИНАТАХ ВИДА, а не в отметках:
        /// у вида профиля своё вертикальное преувеличение, и строка, посчитанная
        /// по BlockHeight, не совпала бы со своим рядом ни на одном чертеже,
        /// где оно отлично от единицы.
        /// </summary>
        private static Table BuildRowTable(
            Transaction tr, Database db, BlockTableRecord space,
            FacingWallDefinition def, ProfileView pv, BlockCounts counts, double width)
        {
            var ordered = OrderedRowsTopDown(def);
            if (ordered.Count == 0) return null;

            double station = MidStation(def);

            var table = new Table();
            table.TableStyle = db.Tablestyle;
            table.SetSize(2 + ordered.Count, 2);

            table.Cells[0, 0].TextString = "Блоков в рядах";
            table.Cells[1, 0].TextString = "Ряд";
            table.Cells[1, 1].TextString = "Блоков";

            table.Columns[0].Width = width;
            table.Columns[1].Width = width * NarrowColumnFactor;

            for (int k = 0; k < ordered.Count; k++)
            {
                FacingWallRowDefinition row = ordered[k];
                int line = 2 + k;

                table.Cells[line, 0].TextString = row.RowIndex.ToString();
                table.Cells[line, 1].TextString = counts.RowCount(row.RowIndex).ToString();

                double height = RowHeightInView(pv, def, row, station);
                if (height > 1e-6) table.Rows[line].Height = height;
            }

            space.AppendEntity(table);
            tr.AddNewlyCreatedDBObject(table, true);

            return table;
        }

        /// <summary>
        /// Ряды сверху вниз: в стене нулевой ряд самый нижний, а в табличке
        /// первая строка — верхняя. Без разворота табличка читалась бы
        /// вверх ногами относительно того, что нарисовано рядом.
        /// </summary>
        private static List<FacingWallRowDefinition> OrderedRowsTopDown(FacingWallDefinition def)
        {
            var ordered = new List<FacingWallRowDefinition>();
            if (def.Rows == null) return ordered;

            foreach (FacingWallRowDefinition row in def.Rows)
                if (row != null) ordered.Add(row);

            ordered.Sort((a, b) => b.RowIndex.CompareTo(a.RowIndex));
            return ordered;
        }

        /// <summary>Высота ряда в координатах вида профиля. Ноль — пересчёт не удался.</summary>
        private static double RowHeightInView(
            ProfileView pv, FacingWallDefinition def, FacingWallRowDefinition row, double station)
        {
            double bottom = FacingWallBuilder.RowElevation(def, row);
            double top = bottom + def.BlockHeight;

            Point3d a, b;
            if (!RwGeometry.TryPointInProfileView(pv, station, bottom, out a)) return 0.0;
            if (!RwGeometry.TryPointInProfileView(pv, station, top, out b)) return 0.0;

            return Math.Abs(b.Y - a.Y);
        }

        /// <summary>
        /// Где должна оказаться верхняя кромка таблички, чтобы её строки
        /// совпали с рядами: заголовок и шапка стоят ВЫШЕ верхнего ряда.
        ///
        /// Их высоты читаются у самой таблички, а не назначаются: они заданы
        /// стилем таблиц чертежа, и переписать их значило бы навязать чертежу
        /// своё оформление.
        /// </summary>
        private static double RowTableTop(Table table, double topOfWall)
        {
            double header = 0.0;

            try
            {
                header = table.Rows[0].Height + table.Rows[1].Height;
            }
            catch (System.Exception)
            {
                // Не прочиталось — табличка просто встанет верхом на уровень стены.
            }

            return topOfWall + header;
        }

        // =================================================================
        //  ТАБЛИЧКА «ВСЕГО БЛОКОВ»
        // =================================================================

        private static Table BuildTotalsTable(
            Transaction tr, Database db, BlockTableRecord space,
            FacingWallDefinition def, BlockCounts counts, double width)
        {
            var names = new List<string>(counts.ByName.Keys);
            if (names.Count == 0) return null;

            names.Sort(StringComparer.CurrentCultureIgnoreCase);

            var table = new Table();
            table.TableStyle = db.Tablestyle;

            // Заголовок, шапка, строки по именам и строка «Всего».
            table.SetSize(3 + names.Count, 2);

            table.Cells[0, 0].TextString = "Блоки облицовки";
            table.Cells[1, 0].TextString = "Наименование";
            table.Cells[1, 1].TextString = "Количество";

            table.Columns[0].Width = width;
            table.Columns[1].Width = width * NarrowColumnFactor;

            for (int k = 0; k < names.Count; k++)
            {
                table.Cells[2 + k, 0].TextString = names[k];
                table.Cells[2 + k, 1].TextString = counts.ByName[names[k]].ToString();
            }

            int last = 2 + names.Count;
            table.Cells[last, 0].TextString = "Всего";
            table.Cells[last, 1].TextString = counts.Total.ToString();

            space.AppendEntity(table);
            tr.AddNewlyCreatedDBObject(table, true);

            return table;
        }

        // =================================================================
        //  ПРИВЯЗКА К ВИДУ
        // =================================================================

        /// <summary>
        /// Левый край вида профиля и верх стены — от них считается место
        /// табличек. false, если вид ещё не отрисован и габаритов у него нет.
        /// </summary>
        private static bool Anchor(ProfileView pv, out double left, out double top)
        {
            left = 0.0;
            top = 0.0;

            try
            {
                Extents3d ext = pv.GeometricExtents;
                left = Math.Min(ext.MinPoint.X, ext.MaxPoint.X);
                top = Math.Max(ext.MinPoint.Y, ext.MaxPoint.Y);
                return true;
            }
            catch (System.Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Пикет, на котором меряется высота ряда в координатах вида.
        /// Берётся середина раскладки: у краёв вида пересчёт может не пройти,
        /// а вертикальное преувеличение по всему виду одно и то же.
        /// </summary>
        private static double MidStation(FacingWallDefinition def)
        {
            return (def.LayoutLowStation() + def.LayoutHighStation()) / 2.0;
        }

        /// <summary>
        /// Ширина основного столбца. Берётся от высоты текста чертежа, чтобы
        /// табличка была соразмерна остальным надписям: в метровом чертеже
        /// фиксированная ширина оказалась бы то огромной, то нечитаемой.
        /// </summary>
        private static double ColumnWidth(Transaction tr, Database db)
        {
            double height = 0.0;

            try
            {
                var style = tr.GetObject(db.Textstyle, OpenMode.ForRead) as TextStyleTableRecord;
                if (style != null) height = style.TextSize;
            }
            catch (System.Exception) { }

            if (height <= 1e-9) height = db.Textsize;
            if (height <= 1e-9) return FallbackColumnWidth;

            return height * 8.0;
        }
    }
}
