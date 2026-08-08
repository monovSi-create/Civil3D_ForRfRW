using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = Autodesk.AutoCAD.Runtime.Exception;

[assembly: CommandClass(typeof(Civil3D_commands.CorridorPolylineExtractor))]

namespace Civil3D_commands
{
    public class CorridorPolylineExtractor
    {
        // ─────────────────────────────────────────────────────────────────────
        //  КОНСТАНТЫ
        // ─────────────────────────────────────────────────────────────────────
        private const int    Z_ROUND_DIGITS   = 4;   // точность группировки по Z
        private const double MIN_CURVE_LENGTH = 0.01; // минимальная длина сегмента (м)

        // ─────────────────────────────────────────────────────────────────────
        //  ТОЧКА ВХОДА
        // ─────────────────────────────────────────────────────────────────────
        [CommandMethod("RW_ExtractCorridorPolylines")]
        public static void RunCommand()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            Editor   ed  = doc.Editor;
            Database db  = doc.Database;

            // 1. Выбор коридора — возвращаем ObjectId, не объект (объект живёт в транзакции)
            ObjectId corridorId = SelectCorridorId();
            if (corridorId.IsNull) return;

            // 2. Выбор кода точки — открываем коридор в отдельной короткой транзакции
            string selectedCode;
            using (Transaction trRead = db.TransactionManager.StartTransaction())
            {
                Corridor cor = trRead.GetObject(corridorId, OpenMode.ForRead) as Corridor;
                selectedCode = SelectCodeUI(cor);
                trRead.Commit();
            }
            if (string.IsNullOrEmpty(selectedCode)) return;

            // 3. Запрос допуска
            PromptDoubleOptions pdo = new PromptDoubleOptions("\nВведите допуск объединения (м): ")
            {
                DefaultValue  = 0.01,
                AllowNegative = false,
                AllowZero     = false
            };
            PromptDoubleResult pdr = ed.GetDouble(pdo);
            if (pdr.Status != PromptStatus.OK) return;
            double tolerance = pdr.Value;

            // 4. Основная транзакция
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                ObjectId msId = SymbolUtilityServices.GetBlockModelSpaceId(db);
                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(msId, OpenMode.ForWrite);

                ObjectId layerId = EnsureLayer(db, tr, selectedCode);

                // Извлекаем сегменты
                List<Curve> segments = ExtractSegments(tr, corridorId, selectedCode, ed);

                // Фильтруем слишком короткие
                segments = FilterShortCurves(segments);

                // Склеиваем в полилинии по уровню Z
                List<Polyline> polylines = JoinSegmentsByElevation(segments, tolerance);

                // Записываем в чертёж
                int count = 0;
                foreach (Polyline pl in polylines)
                {
                    pl.SetLayerId(layerId, false);
                    ms.AppendEntity(pl);
                    tr.AddNewlyCreatedDBObject(pl, true);
                    count++;
                }

                tr.Commit();
                ed.WriteMessage($"\nГотово: добавлено {count} полилиний на слой «{selectedCode}».");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  ИЗВЛЕЧЕНИЕ СЕГМЕНТОВ ИЗ КОРИДОРА
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Обходит feature lines коридора, экспортирует Polyline3d для выбранного кода,
        /// взрывает их и собирает горизонтальные сегменты (StartPoint.Z == EndPoint.Z).
        /// FIX: цикл по region убран — FeatureLineCollectionMap берётся один раз с baseline,
        ///      иначе сегменты дублировались бы по числу регионов.
        /// </summary>
        private static List<Curve> ExtractSegments(Transaction tr, ObjectId corridorId,
                                             string code, Editor ed)
        {
            var result = new List<Curve>();
            Corridor corridor = tr.GetObject(corridorId, OpenMode.ForRead) as Corridor;
            if (corridor == null) return result;

            foreach (Baseline baseline in corridor.Baselines)
            {
                FeatureLineCollectionMap ftMap =
                    baseline.MainBaselineFeatureLines.FeatureLineCollectionMap;

                foreach (FeatureLineCollection ftColl in ftMap)
                {
                    foreach (CorridorFeatureLine cfl in ftColl)
                    {
                        // FIX: точное сравнение вместо Regex.IsMatch (избегаем ложных совпадений)
                        if (cfl.CodeName != code) continue;

                        ObjectIdCollection poly3dIds = cfl.ExportAsPolyline3dCollection();
                        foreach (ObjectId pid in poly3dIds)
                        {
                            Polyline3d p3d = tr.GetObject(pid, OpenMode.ForWrite) as Polyline3d;
                            if (p3d == null) continue;

                            var exploded = new DBObjectCollection();
                            try
                            {
                                p3d.Explode(exploded);
                            }
                            catch (Exception ex)
                            {
                                ed.WriteMessage($"\n[Предупреждение] Не удалось взорвать Polyline3d: {ex.Message}");
                                continue;
                            }
                            finally
                            {
                                // FIX: Erase без ручного Dispose — транзакция сама освободит объект
                                p3d.Erase();
                            }

                            // Оставляем только горизонтальные сегменты
                            foreach (Autodesk.AutoCAD.DatabaseServices.DBObject obj in exploded)
                            {
                                Curve curve = obj as Curve;
                                if (curve == null) { obj.Dispose(); continue; }

                                double zStart = Math.Round(curve.StartPoint.Z, Z_ROUND_DIGITS);
                                double zEnd   = Math.Round(curve.EndPoint.Z,   Z_ROUND_DIGITS);

                                if (zStart == zEnd)
                                    result.Add(curve);
                                else
                                    curve.Dispose();
                            }
                        }
                    }
                }
            }

            return result;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  ФИЛЬТРАЦИЯ КОРОТКИХ КРИВЫХ
        // ─────────────────────────────────────────────────────────────────────
        private static List<Curve> FilterShortCurves(List<Curve> curves)
        {
            var result = new List<Curve>(curves.Count);
            foreach (Curve c in curves)
            {
                if (c.GetDistanceAtParameter(c.EndParam) > MIN_CURVE_LENGTH)
                    result.Add(c);
                else
                    c.Dispose();
            }
            return result;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  СКЛЕЙКА СЕГМЕНТОВ В ПОЛИЛИНИИ
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Группирует сегменты по Z, затем для каждой группы жадно собирает цепочки
        /// смежных кривых и конвертирует каждую цепочку в 2D Polyline.
        ///
        /// FIX: группировка и currentZ используют одно и то же число знаков (Z_ROUND_DIGITS).
        /// OPT: индекс по конечным точкам (Dictionary) снижает сложность с O(n²) до O(n).
        /// </summary>
        public static List<Polyline> JoinSegmentsByElevation(List<Curve> segments, double tolerance)
        {
            var result = new List<Polyline>();

            // Группируем по округлённому Z
            var byLevel = segments
                .GroupBy(s => Math.Round(s.StartPoint.Z, Z_ROUND_DIGITS))
                .Select(g => g.ToList())
                .ToList();

            foreach (List<Curve> pool in byLevel)
            {
                if (pool.Count == 0) continue;
                double currentZ = Math.Round(pool[0].StartPoint.Z, Z_ROUND_DIGITS); // FIX: те же знаки

                // Строим пространственный индекс: ключ = координата (x,y), округлённая до мм
                // Каждая кривая регистрируется по обеим конечным точкам
                var index = BuildSpatialIndex(pool);

                while (pool.Count > 0)
                {
                    LinkedList<Curve> chain = new LinkedList<Curve>();
                    Curve seed = pool[0];
                    pool.RemoveAt(0);
                    UnregisterCurve(index, seed);
                    chain.AddFirst(seed);

                    // Расширяем цепочку в обе стороны
                    ExpandChain(chain, pool, index, tolerance);

                    Polyline pl = ConvertChainToPolyline(chain, currentZ);
                    if (pl.NumberOfVertices > 1)
                        result.Add(pl);
                }
            }

            return result;
        }

        // ── Индекс по координатам ──────────────────────────────────────────

        private static (long x, long y) PointKey(Point3d p) =>
            ((long)Math.Round(p.X * 10000), (long)Math.Round(p.Y * 10000));

        private static Dictionary<(long, long), List<Curve>> BuildSpatialIndex(List<Curve> pool)
        {
            var idx = new Dictionary<(long, long), List<Curve>>();
            foreach (Curve c in pool)
            {
                Register(idx, PointKey(c.StartPoint), c);
                Register(idx, PointKey(c.EndPoint),   c);
            }
            return idx;
        }

        private static void Register(Dictionary<(long, long), List<Curve>> idx,
                                     (long, long) key, Curve c)
        {
            if (!idx.TryGetValue(key, out var list))
                idx[key] = list = new List<Curve>();
            if (!list.Contains(c)) list.Add(c);
        }

        private static void UnregisterCurve(Dictionary<(long, long), List<Curve>> idx, Curve c)
        {
            RemoveFromBucket(idx, PointKey(c.StartPoint), c);
            RemoveFromBucket(idx, PointKey(c.EndPoint),   c);
        }

        private static void RemoveFromBucket(Dictionary<(long, long), List<Curve>> idx,
                                              (long, long) key, Curve c)
        {
            if (idx.TryGetValue(key, out var list)) list.Remove(c);
        }

        // ── Жадное расширение цепочки ──────────────────────────────────────

        private static void ExpandChain(LinkedList<Curve> chain, List<Curve> pool,
                                         Dictionary<(long, long), List<Curve>> index,
                                         double tolerance)
        {
            bool found;
            do
            {
                found = false;
                found |= TryAttach(chain, pool, index, tolerance, atEnd: true);
                found |= TryAttach(chain, pool, index, tolerance, atEnd: false);
            } while (found);
        }

        private static bool TryAttach(LinkedList<Curve> chain, List<Curve> pool,
                               Dictionary<(long, long), List<Curve>> index,
                               double tolerance, bool atEnd)
        {
            Point3d anchor = atEnd ? chain.Last.Value.EndPoint : chain.First.Value.StartPoint;

            const double GRID_STEP = 1.0 / 10000.0;
            long radius = (long)Math.Ceiling(tolerance / GRID_STEP) + 1;
            (long cx, long cy) = PointKey(anchor);

            var seen = new HashSet<Curve>();
            var candidates = new List<Curve>();
            for (long dx = -radius; dx <= radius; dx++)
                for (long dy = -radius; dy <= radius; dy++)
                {
                    var bucketKey = (cx + dx, cy + dy);
                    if (!index.TryGetValue(bucketKey, out var bucket)) continue;
                    foreach (Curve c in bucket)
                        if (seen.Add(c)) candidates.Add(c);
                }

            if (candidates.Count == 0) return false;

                // Ищем кандидата в пределах допуска
            foreach (Curve seg in candidates)
            {
                bool startMatches = anchor.DistanceTo(seg.StartPoint) <= tolerance;
                bool endMatches   = anchor.DistanceTo(seg.EndPoint)   <= tolerance;

                if (!startMatches && !endMatches) continue;

                // Ориентируем сегмент нужным концом к цепочке
                if (atEnd)
                {
                    if (endMatches && !startMatches) seg.ReverseCurve();
                    chain.AddLast(seg);
                }
                else
                {
                    if (startMatches && !endMatches) seg.ReverseCurve();
                    chain.AddFirst(seg);
                }

                pool.Remove(seg);
                UnregisterCurve(index, seg);
                return true;
            }

            return false;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  КОНВЕРТАЦИЯ ЦЕПОЧКИ В POLYLINE
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Конвертирует связный список кривых (Line/Arc) в 2D Polyline с bulge для дуг.
        /// Elevation задаётся отдельно — координаты вершин плоские (X,Y).
        /// </summary>
        private static Polyline ConvertChainToPolyline(LinkedList<Curve> chain, double zLevel)
        {
            var poly = new Polyline();
            poly.SetDatabaseDefaults();
            poly.Elevation = zLevel;

            if (chain.Count == 0) return poly;

            // Первая вершина с bulge первого сегмента
            Curve first = chain.First.Value;
            poly.AddVertexAt(0,
                new Point2d(first.StartPoint.X, first.StartPoint.Y),
                GetBulge(first), 0, 0);

            int idx = 1;
            var node = chain.First;
            while (node != null)
            {
                Curve cur  = node.Value;
                Curve next = node.Next?.Value;

                // Bulge пишется в вершину входа следующего сегмента
                double nextBulge = next != null ? GetBulge(next) : 0;
                poly.AddVertexAt(idx,
                    new Point2d(cur.EndPoint.X, cur.EndPoint.Y),
                    nextBulge, 0, 0);

                idx++;
                node = node.Next;
            }

            return poly;
        }

        private static double GetBulge(Curve curve)
        {
            if (curve is Arc arc)
            {
                double delta = arc.EndAngle - arc.StartAngle;
                if (delta < 0) delta += 2 * Math.PI;
                double bulge = Math.Tan(delta / 4);
                if (arc.Normal.Z < 0) bulge = -bulge;
                return bulge;
            }
            return 0;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  СЛОЙ
        // ─────────────────────────────────────────────────────────────────────
        private static ObjectId EnsureLayer(Database db, Transaction tr, string name)
        {
            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(name)) return lt[name];

            lt.UpgradeOpen();
            LayerTableRecord ltr = new LayerTableRecord { Name = name };
            ObjectId id = lt.Add(ltr);
            tr.AddNewlyCreatedDBObject(ltr, true);
            return id;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  UI: ВЫБОР КОРИДОРА
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// FIX: возвращает ObjectId (а не Corridor), чтобы объект не жил вне транзакции.
        /// FIX: транзакция оборачивается в using — гарантированное закрытие.
        /// </summary>
        public static ObjectId SelectCorridorId()
        {
            CivilDocument cdoc = CivilApplication.ActiveDocument;
            Database db = Application.DocumentManager.MdiActiveDocument.Database;

            var names = new List<string>();
            var map   = new Dictionary<string, ObjectId>();

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId cid in cdoc.CorridorCollection)
                {
                    Corridor cor = tr.GetObject(cid, OpenMode.ForRead) as Corridor;
                    if (cor == null) continue;
                    names.Add(cor.Name);
                    map[cor.Name] = cid;
                }
                tr.Commit();
            }

            string selected = ShowListDialog("Выберите коридор", names);
            if (string.IsNullOrEmpty(selected)) return ObjectId.Null;

            return map[selected];
        }

        // ─────────────────────────────────────────────────────────────────────
        //  UI: ВЫБОР КОДА ТОЧКИ
        // ─────────────────────────────────────────────────────────────────────
        private static string SelectCodeUI(Corridor corridor)
        {
            var codes = corridor.GetPointCodes().OrderBy(c => c).ToList();
            return ShowListDialog("Выберите код точки", codes);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  UI: УНИВЕРСАЛЬНЫЙ ДИАЛОГ СПИСКА
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// FIX: using гарантирует dispose формы.
        /// FIX: AcceptButton — Enter подтверждает выбор.
        /// FIX: двойной клик также подтверждает выбор.
        /// </summary>
        private static string ShowListDialog(string title, List<string> items)
        {
            using (Form f = new Form
            {
                Text            = title,
                Width           = 320,
                Height          = 420,
                StartPosition   = FormStartPosition.CenterScreen,
                FormBorderStyle = FormBorderStyle.FixedToolWindow
            })
            using (ListBox lb = new ListBox { Dock = DockStyle.Fill, DataSource = items })
            using (Button btn = new Button
            {
                Text         = "OK",
                Dock         = DockStyle.Bottom,
                DialogResult = DialogResult.OK
            })
            {
                f.Controls.Add(lb);
                f.Controls.Add(btn);
                f.AcceptButton = btn; // FIX: Enter = подтвердить

                // FIX: двойной клик закрывает диалог
                lb.DoubleClick += (s, e) => f.DialogResult = DialogResult.OK;

                return f.ShowDialog() == DialogResult.OK && lb.SelectedItem != null
                    ? lb.SelectedItem.ToString()
                    : null;
            }
        }
    }
}
