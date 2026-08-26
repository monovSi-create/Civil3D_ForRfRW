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
    /// Метка объекта-ручки: чей контроллер и что именно он двигает.
    ///
    /// RowIndex >= 0 — отрезок ряда в виде профиля (правит один ряд).
    /// RowIndex  < 0 — маркер границы раскладки (правит весь массив).
    /// Отрицательные индексы выбраны сознательно: формат XData от их появления
    /// не изменился, отрезки в старых чертежах читаются как были.
    /// </summary>
    public static class FacingWallGripTag
    {
        /// <summary>Маркер якоря раскладки в плане.</summary>
        public const int PlanStartRowIndex = -1;

        /// <summary>Маркер дальней границы раскладки в плане.</summary>
        public const int PlanEndRowIndex = -2;

        /// <summary>Маркер якоря раскладки в виде профиля.</summary>
        public const int ProfileStartRowIndex = -3;

        /// <summary>Маркер дальней границы раскладки в виде профиля.</summary>
        public const int ProfileEndRowIndex = -4;

        /// <summary>Метка относится к границе раскладки, а не к ряду?</summary>
        public static bool IsLayout(int rowIndex)
        {
            return rowIndex < 0;
        }

        /// <summary>Маркер якоря (в любом из двух видов)?</summary>
        public static bool IsAnchor(int rowIndex)
        {
            return rowIndex == PlanStartRowIndex || rowIndex == ProfileStartRowIndex;
        }

        /// <summary>Маркер лежит в плане (иначе — в виде профиля)?</summary>
        public static bool IsInPlan(int rowIndex)
        {
            return rowIndex == PlanStartRowIndex || rowIndex == PlanEndRowIndex;
        }

        /// <summary>Метка не относится к разрыву: обычная ручка ряда или раскладки.</summary>
        public const int NoGap = -1;

        /// <summary>Пометить объект: чей контроллер и какой ряд.</summary>
        public static void Set(
            Transaction tr, Database db, Entity ent, ObjectId controllerId, int rowIndex)
        {
            Set(tr, db, ent, controllerId, rowIndex, NoGap);
        }

        /// <summary>
        /// То же плюс номер разрыва в ряду — им помечены отрезки-ручки разрывов.
        ///
        /// Номер идёт ВТОРЫМ целым, и это существенно: метки прежних версий
        /// несут только одно целое, и читаются они по-прежнему — как ручка
        /// ряда без разрыва. Порядок чисел в XData сохраняется, поэтому
        /// «первое — ряд, второе — разрыв» разбирается однозначно.
        /// </summary>
        public static void Set(
            Transaction tr, Database db, Entity ent,
            ObjectId controllerId, int rowIndex, int gapIndex)
        {
            var rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (!rat.Has(FacingWallController.XAppName))
            {
                rat.UpgradeOpen();
                var rec = new RegAppTableRecord { Name = FacingWallController.XAppName };
                rat.Add(rec);
                tr.AddNewlyCreatedDBObject(rec, true);
            }

            var values = new List<TypedValue>
            {
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, FacingWallController.XAppName),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, controllerId.Handle.ToString()),
                new TypedValue((int)DxfCode.ExtendedDataInteger32, rowIndex)
            };

            if (gapIndex >= 0)
                values.Add(new TypedValue((int)DxfCode.ExtendedDataInteger32, gapIndex));

            ent.XData = new ResultBuffer(values.ToArray());
        }

        /// <summary>
        /// Прочитать метку. false, если это не наш объект.
        /// </summary>
        public static bool TryRead(Entity ent, out ObjectId controllerId, out int rowIndex)
        {
            int gapIndex;
            return TryRead(ent, out controllerId, out rowIndex, out gapIndex);
        }

        /// <summary>
        /// То же плюс номер разрыва. <see cref="NoGap"/> означает, что отрезок
        /// принадлежит ряду или раскладке, а не разрыву.
        /// </summary>
        public static bool TryRead(
            Entity ent, out ObjectId controllerId, out int rowIndex, out int gapIndex)
        {
            controllerId = ObjectId.Null;
            rowIndex = 0;
            gapIndex = NoGap;

            if (ent == null) return false;

            ResultBuffer rb = null;
            try
            {
                rb = ent.GetXDataForApplication(FacingWallController.XAppName);
                if (rb == null) return false;

                string handleText = null;
                int seen = 0;

                foreach (TypedValue tv in rb)
                {
                    if (tv.TypeCode == (int)DxfCode.ExtendedDataAsciiString)
                    {
                        handleText = tv.Value.ToString();
                    }
                    else if (tv.TypeCode == (int)DxfCode.ExtendedDataInteger32)
                    {
                        // Первое целое — ряд, второе — разрыв. Прежде здесь
                        // брали ПОСЛЕДНЕЕ, и второе число подменило бы номер ряда.
                        if (seen == 0) rowIndex = Convert.ToInt32(tv.Value);
                        else if (seen == 1) gapIndex = Convert.ToInt32(tv.Value);

                        seen++;
                    }
                }

                if (string.IsNullOrEmpty(handleText) || seen == 0) return false;

                controllerId = RwHandles.Resolve(ent.Database, handleText);
                return !controllerId.IsNull;
            }
            catch (System.Exception)
            {
                return false;
            }
            finally
            {
                if (rb != null) rb.Dispose();
            }
        }
    }

    /// <summary>
    /// Границы раскладки и их маркеры-ручки — сразу в обоих видах.
    ///
    /// Границ ровно две (LayoutStartStation — якорь, LayoutEndStation — дальняя
    /// граница), а носителей ручек четыре: по паре в плане и в виде профиля.
    /// Виды РАВНОПРАВНЫ: подвинули маркер в любом из них — второй подтягивается
    /// на тот же пикет, метаться между видами не нужно.
    ///
    /// В плане маркер перпендикулярен трассе: начало на оси, конец в
    /// PlanMarkerLength от неё. Перпендикулярность обеспечивает сам
    /// Alignment.PointLocation — offset у него отмеряется по нормали к оси.
    /// В виде профиля маркер вертикален и перекрывает стену по высоте с запасом.
    ///
    /// Оба вида здесь намеренно вместе, хотя профильная графика вообще-то живёт
    /// в FacingWallProjection: это ОДНА пара чисел, и разносить её по модулям
    /// значило бы гарантированно однажды их рассинхронизировать.
    ///
    /// Носитель — обычный Line: ручки на концах и в середине рисует сам AutoCAD.
    /// Собственные GripData на вставке в Civil 3D 2024 не отображаются (см.
    /// README). За какую бы из трёх ручек ни тянули, маркер целиком встаёт на
    /// новый пикет и строится заново — так он остаётся перпендикулярным
    /// (в плане) и вертикальным (в профиле) при любом драге.
    /// </summary>
    public static class FacingWallLayoutGrip
    {
        /// <summary>Слой маркеров в плане.</summary>
        public const string PlanLayerName = "RW_FACINGWALL_GRIP_PLAN";

        /// <summary>Слой маркеров в виде профиля. Отдельный от слоя ручек рядов.</summary>
        public const string ProfileLayerName = "RW_FACINGWALL_GRIP_LAYOUT";

        /// <summary>Длина планового маркера от оси трассы.</summary>
        public const double PlanMarkerLength = 3.0;

        /// <summary>
        /// Создать все четыре маркера либо подвинуть существующие.
        /// Именно подвинуть: стирать объект, который сейчас тянут мышью, нельзя.
        /// </summary>
        public static void EnsureMarkers(
            Transaction tr,
            Database db,
            ObjectId controllerId,
            FacingWallDefinition def)
        {
            if (def == null) return;

            var alignment = tr.GetObject(def.AlignmentId, OpenMode.ForRead) as Alignment;
            var pv = def.ProfileViewId.IsNull
                ? null
                : tr.GetObject(def.ProfileViewId, OpenMode.ForRead) as ProfileView;

            ObjectId planStart = def.PlanStartGripId;
            ObjectId planEnd = def.PlanEndGripId;
            ObjectId profStart = def.ProfileStartGripId;
            ObjectId profEnd = def.ProfileEndGripId;

            if (alignment != null)
            {
                EnsureMarker(tr, db, controllerId, PlanLayerName, 4,
                    PlanPoints(alignment, def, def.LayoutStartStation),
                    FacingWallGripTag.PlanStartRowIndex, ref planStart);

                EnsureMarker(tr, db, controllerId, PlanLayerName, 4,
                    PlanPoints(alignment, def, def.LayoutEndStation),
                    FacingWallGripTag.PlanEndRowIndex, ref planEnd);
            }

            if (pv != null)
            {
                EnsureMarker(tr, db, controllerId, ProfileLayerName, 5,
                    ProfilePoints(pv, def, def.LayoutStartStation),
                    FacingWallGripTag.ProfileStartRowIndex, ref profStart);

                EnsureMarker(tr, db, controllerId, ProfileLayerName, 5,
                    ProfilePoints(pv, def, def.LayoutEndStation),
                    FacingWallGripTag.ProfileEndRowIndex, ref profEnd);
            }

            def.PlanStartGripId = planStart;
            def.PlanEndGripId = planEnd;
            def.ProfileStartGripId = profStart;
            def.ProfileEndGripId = profEnd;
        }

        /// <summary>Удалить все четыре маркера.</summary>
        public static void EraseMarkers(Transaction tr, FacingWallDefinition def)
        {
            if (def == null) return;

            FacingWallBuilder.EraseAll(tr, new List<ObjectId>
            {
                def.PlanStartGripId, def.PlanEndGripId,
                def.ProfileStartGripId, def.ProfileEndGripId
            });

            def.PlanStartGripId = ObjectId.Null;
            def.PlanEndGripId = ObjectId.Null;
            def.ProfileStartGripId = ObjectId.Null;
            def.ProfileEndGripId = ObjectId.Null;
        }

        // =================================================================
        //  ГЕОМЕТРИЯ МАРКЕРОВ
        // =================================================================

        /// <summary>
        /// Плановый маркер на пикете: точка на оси и точка в стороне от неё.
        /// null, если пикет вне трассы и точку не построить.
        /// </summary>
        public static Point3d[] PlanPoints(
            Alignment alignment, FacingWallDefinition def, double station)
        {
            // Один пикет, два смещения — отрезок получается перпендикулярным оси.
            return RwGeometry.PlanSegment(
                alignment,
                station, 0.0,
                station, PlanMarkerOffset(def));
        }

        /// <summary>
        /// Профильный маркер на пикете: вертикальный отрезок по высоте стены
        /// с запасом в один ряд сверху и снизу — чтобы концы с ручками торчали
        /// за блоками и по ним можно было попасть.
        /// null, если пикет вне вида профиля.
        /// </summary>
        public static Point3d[] ProfilePoints(
            ProfileView pv, FacingWallDefinition def, double station)
        {
            if (pv == null) return null;

            double margin = def.BlockHeight > 0.0 ? def.BlockHeight : 1.0;
            double bottom = def.BaseElevation - margin;
            double top = def.BaseElevation + def.RowCount * def.BlockHeight + margin;

            // Один пикет, две отметки — отрезок вертикален в координатах вида.
            return RwGeometry.ProfileSegment(pv, station, bottom, station, top);
        }

        /// <summary>
        /// Куда торчит плановый маркер: в сторону стены, чтобы не уходить в
        /// противоположную от построенных блоков.
        /// </summary>
        private static double PlanMarkerOffset(FacingWallDefinition def)
        {
            return def.FaceOffset < 0.0 ? -PlanMarkerLength : PlanMarkerLength;
        }

        // =================================================================

        private static void EnsureMarker(
            Transaction tr,
            Database db,
            ObjectId controllerId,
            string layerName,
            short colorIndex,
            Point3d[] points,
            int tagRowIndex,
            ref ObjectId markerId)
        {
            if (points == null) return;

            // Существующий — двигаем. Выходим в любом случае: второй маркер
            // на ту же границу создавать нельзя, даже если открыть этот не удалось.
            if (!markerId.IsNull && markerId.IsValid && !markerId.IsErased)
            {
                try
                {
                    var existing = tr.GetObject(markerId, OpenMode.ForWrite, false) as Line;
                    if (existing != null)
                    {
                        existing.StartPoint = points[0];
                        existing.EndPoint = points[1];
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

            var line = new Line(points[0], points[1]);
            line.SetDatabaseDefaults();
            line.LayerId = EnsureLayer(tr, db, layerName, colorIndex);

            markerId = btr.AppendEntity(line);
            tr.AddNewlyCreatedDBObject(line, true);

            FacingWallGripTag.Set(tr, db, line, controllerId, tagRowIndex);
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
    }
}
