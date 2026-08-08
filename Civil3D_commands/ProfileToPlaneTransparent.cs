// =============================================================================
//  ProfileToPlaneTransparent.cs  —  v3 (PointMonitor architecture)
//
//  Команда PTP включает/выключает РЕЖИМ:
//    - Один раз выбираете вид профиля
//    - Каждый запрос точки от любой команды (LINE, PLINE, CIRCLE…)
//      автоматически перехватывается — запускается наш jig
//    - Выход: повторный PTP или Esc внутри jig
//
//  Jig-логика (пикет → отметка → смещение) взята из рабочей v1 без изменений.
//
//  Зависимости: acdbmgd.dll, acmgd.dll, AeccDbMgd.dll, AeccLandMgd.dll
// =============================================================================

using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.DatabaseServices;

[assembly: CommandClass(typeof(ProfileToPlane.ProfileToPlaneTransparent))]

namespace ProfileToPlane
{
    // =========================================================================
    //  Jig #1 — ПИКЕТ  (горизонтальное движение, Y заморожен)
    // =========================================================================
    internal class StationJig : DrawJig
    {
        private readonly ProfileView _profView;
        private readonly double      _anchorY;
        private Point3d              _cursor;

        private double _xB, _yBottom, _xT, _yTop, _pvHeight;

        public double Station { get; private set; }
        public double FixedX  { get; private set; }

        public StationJig(ProfileView profView, double anchorY)
        {
            _profView = profView;
            _anchorY  = anchorY;

            double xT = 0, yT = 0, xR = 0, yR = 0;
            profView.FindXYAtStationAndElevation(
                profView.StationStart, profView.ElevationMin, ref _xB, ref _yBottom);
            profView.FindXYAtStationAndElevation(
                profView.StationStart, profView.ElevationMax, ref xT,  ref _yTop);
            _pvHeight = Math.Abs(_yTop - _yBottom);

            _cursor = new Point3d(_xB, _anchorY, 0);
        }

        protected override SamplerStatus Sampler(JigPrompts prompts)
        {
            JigPromptPointOptions opt = new JigPromptPointOptions(
                "\n[PTP] Пикет: щёлкните на виде профиля [Ввод=вручную]: ");
            opt.UserInputControls =
                UserInputControls.NullResponseAccepted |
                UserInputControls.AcceptOtherInputString;
            opt.Keywords.Add("Ввод");
            opt.AppendKeywordsToMessage = true;

            PromptPointResult res = prompts.AcquirePoint(opt);

            if (res.Status == PromptStatus.Cancel) return SamplerStatus.Cancel;
            if (res.Status == PromptStatus.Keyword) return SamplerStatus.OK;
            if (res.Status != PromptStatus.OK)      return SamplerStatus.NoChange;

            Point3d newPt = new Point3d(res.Value.X, _anchorY, 0);
            if (newPt.IsEqualTo(_cursor, Tolerance.Global))
                return SamplerStatus.NoChange;

            _cursor = newPt;

            double st = 0, el = 0;
            _profView.FindStationAndElevationAtXY(_cursor.X, _anchorY, ref st, ref el);
            Station = st;
            FixedX  = _cursor.X;

            return SamplerStatus.OK;
        }

        protected override bool WorldDraw(Autodesk.AutoCAD.GraphicsInterface.WorldDraw draw)
        {
            // Вертикальный визир — следует за курсором
            draw.Geometry.WorldLine(
                new Point3d(_cursor.X, _yBottom - _pvHeight * 0.05, 0),
                new Point3d(_cursor.X, _yTop    + _pvHeight * 0.05, 0));

            // Горизонтальная засечка у курсора
            double tick = _pvHeight * 0.03;
            draw.Geometry.WorldLine(
                new Point3d(_cursor.X - tick, _cursor.Y, 0),
                new Point3d(_cursor.X + tick, _cursor.Y, 0));

            return true;
        }
    }

    // =========================================================================
    //  Jig #2 — ОТМЕТКА  (вертикальное движение, X заморожен)
    // =========================================================================
    internal class ElevationJig : DrawJig
    {
        private readonly ProfileView _profView;
        private readonly double      _fixedX;
        private Point3d              _cursor;

        private double _xLeft, _yLeft, _xRight, _yRight, _pvWidth;
        private double _yBottom, _yTop, _pvHeight;

        public double Elevation { get; private set; }

        public ElevationJig(ProfileView profView, double fixedX)
        {
            _profView = profView;
            _fixedX   = fixedX;

            profView.FindXYAtStationAndElevation(
                profView.StationStart, profView.ElevationMin, ref _xLeft,  ref _yLeft);
            profView.FindXYAtStationAndElevation(
                profView.StationEnd,   profView.ElevationMin, ref _xRight, ref _yRight);
            _pvWidth = Math.Abs(_xRight - _xLeft);

            double xT = 0;
            profView.FindXYAtStationAndElevation(
                profView.StationStart, profView.ElevationMin, ref _xLeft,  ref _yBottom);
            profView.FindXYAtStationAndElevation(
                profView.StationStart, profView.ElevationMax, ref xT,      ref _yTop);
            _pvHeight = Math.Abs(_yTop - _yBottom);

            _cursor = new Point3d(_fixedX, (_yBottom + _yTop) / 2.0, 0);
        }

        protected override SamplerStatus Sampler(JigPrompts prompts)
        {
            JigPromptPointOptions opt = new JigPromptPointOptions(
                "\n[PTP] Отметка: двигайте вверх/вниз и щёлкните: ");
            opt.UserInputControls =
                UserInputControls.NullResponseAccepted |
                UserInputControls.AcceptOtherInputString;

            PromptPointResult res = prompts.AcquirePoint(opt);

            if (res.Status == PromptStatus.Cancel) return SamplerStatus.Cancel;
            if (res.Status != PromptStatus.OK)     return SamplerStatus.NoChange;

            Point3d newPt = new Point3d(_fixedX, res.Value.Y, 0);
            if (newPt.IsEqualTo(_cursor, Tolerance.Global))
                return SamplerStatus.NoChange;

            _cursor = newPt;

            double st = 0, el = 0;
            _profView.FindStationAndElevationAtXY(_fixedX, _cursor.Y, ref st, ref el);
            Elevation = el;

            return SamplerStatus.OK;
        }

        protected override bool WorldDraw(Autodesk.AutoCAD.GraphicsInterface.WorldDraw draw)
        {
            // Фиксированная вертикальная линия пикета
            draw.Geometry.WorldLine(
                new Point3d(_fixedX, _yBottom - _pvHeight * 0.05, 0),
                new Point3d(_fixedX, _yTop    + _pvHeight * 0.05, 0));

            // Горизонтальный визир — следует за курсором
            draw.Geometry.WorldLine(
                new Point3d(_xLeft  - _pvWidth * 0.05, _cursor.Y, 0),
                new Point3d(_xRight + _pvWidth * 0.05, _cursor.Y, 0));

            // Крестик на пересечении
            double tick = _pvHeight * 0.03;
            draw.Geometry.WorldLine(
                new Point3d(_fixedX - tick, _cursor.Y, 0),
                new Point3d(_fixedX + tick, _cursor.Y, 0));
            draw.Geometry.WorldLine(
                new Point3d(_fixedX, _cursor.Y - tick, 0),
                new Point3d(_fixedX, _cursor.Y + tick, 0));

            return true;
        }
    }

    // =========================================================================
    //  Jig #3 — СМЕЩЕНИЕ  (перпендикулярно трассе, вид сверху)
    // =========================================================================
    internal class OffsetJig : DrawJig
    {
        private readonly Alignment _alignment;
        private readonly double    _elevation;
        private readonly Point3d   _basePoint;
        private readonly Vector3d  _perpDir;

        private Point3d _cursor;
        private Point3d _resultPoint;

        public double  FinalOffset { get; private set; }
        public Point3d FinalPoint  => _resultPoint;

        public OffsetJig(Alignment alignment, double station, double elevation)
        {
            _alignment = alignment;
            _elevation = elevation;

            double bx = 0, by = 0;
            alignment.PointLocation(station, 0.0, ref bx, ref by);
            _basePoint = new Point3d(bx, by, 0);

            double fx = 0, fy = 0, bkx = 0, bky = 0;
            double ds   = 0.1;
            double stFw = Math.Min(station + ds, alignment.EndingStation);
            double stBk = Math.Max(station - ds, alignment.StartingStation);
            alignment.PointLocation(stFw, 0.0, ref fx,  ref fy);
            alignment.PointLocation(stBk, 0.0, ref bkx, ref bky);

            Vector3d tangent = new Vector3d(fx - bkx, fy - bky, 0).GetNormal();
            _perpDir = new Vector3d(tangent.Y, -tangent.X, 0);

            _cursor      = _basePoint;
            _resultPoint = _basePoint;
        }

        protected override SamplerStatus Sampler(JigPrompts prompts)
        {
            JigPromptPointOptions opt = new JigPromptPointOptions(
                "\n[PTP] Смещение ⊥ трассе (щёлкните или введите число): ");
            opt.BasePoint    = _basePoint;
            opt.UseBasePoint = true;
            opt.UserInputControls =
                UserInputControls.GovernedByOrthoMode |
                UserInputControls.NullResponseAccepted |
                UserInputControls.UseBasePointElevation;

            PromptPointResult res = prompts.AcquirePoint(opt);
            if (res.Status == PromptStatus.Cancel) return SamplerStatus.Cancel;
            if (res.Status != PromptStatus.OK)     return SamplerStatus.NoChange;

            if (res.Value.IsEqualTo(_cursor, Tolerance.Global))
                return SamplerStatus.NoChange;

            _cursor = res.Value;

            double offset = (_cursor - _basePoint).DotProduct(_perpDir);
            FinalOffset  = offset;
            _resultPoint = _basePoint + _perpDir * offset;
            _resultPoint = new Point3d(_resultPoint.X, _resultPoint.Y, _elevation);

            return SamplerStatus.OK;
        }

        protected override bool WorldDraw(Autodesk.AutoCAD.GraphicsInterface.WorldDraw draw)
        {
            Point3d flat = new Point3d(_resultPoint.X, _resultPoint.Y, 0);

            draw.Geometry.WorldLine(_basePoint, flat);

            double cs = 0.5;
            draw.Geometry.WorldLine(flat + new Vector3d(-cs, 0, 0), flat + new Vector3d(cs, 0, 0));
            draw.Geometry.WorldLine(flat + new Vector3d(0, -cs, 0), flat + new Vector3d(0, cs, 0));

            return true;
        }
    }

    // =========================================================================
    //  PtpSession
    // =========================================================================
    internal static class PtpSession
    {
        public static bool     IsActive      { get; private set; }
        public static ObjectId ProfileViewId { get; private set; }
        public static ObjectId AlignmentId   { get; private set; }
        public static Document Document      { get; private set; }

        // Сколько раз пропустить PromptingForPoint прежде чем подавать 'PTP.
        // = 1 после передачи coord — пропускаем запрос который получит coord.
        private static int _skipCount;

        public static void Start(Document doc, ObjectId profViewId, ObjectId alignmentId)
        {
            if (IsActive) { Stop(); return; }

            Document      = doc;
            ProfileViewId = profViewId;
            AlignmentId   = alignmentId;
            IsActive      = true;
            _skipCount    = 0;

            doc.Editor.PromptingForPoint += OnPromptingForPoint;
            doc.CommandEnded             += OnCommandEnded;
            doc.CommandCancelled         += OnParentCommandDone;
            doc.CommandFailed            += OnParentCommandDone;

            doc.Editor.WriteMessage(
                "[PTP] Режим активен. Esc внутри jig = выход. Повторный PTP = выход.");

            // Первый 'PTP — перехватывает текущий ожидающий запрос точки
            doc.SendStringToExecute("'PTP", false, false, false);
        }

        public static void Stop()
        {
            if (!IsActive) return;
            IsActive   = false;
            _skipCount = 0;

            if (Document != null)
            {
                Document.Editor.PromptingForPoint -= OnPromptingForPoint;
                Document.CommandEnded             -= OnCommandEnded;
                Document.CommandCancelled         -= OnParentCommandDone;
                Document.CommandFailed            -= OnParentCommandDone;
                Document.Editor.WriteMessage("[PTP] Режим отключён.");
            }
            Document = null;
        }

        // Вызывается из основной команды PTP после успешной передачи coord.
        // Говорим сессии: следующий PromptingForPoint — это родительская команда
        // получает coord, его пропускаем; после него подаём 'PTP.
        public static void OnPointDelivered()
        {
            _skipCount = 1;
            // Переподписываемся чтобы поймать следующий запрос
            if (Document != null)
            {
                Document.Editor.PromptingForPoint -= OnPromptingForPoint;
                Document.Editor.PromptingForPoint += OnPromptingForPoint;
            }
        }

        private static void OnPromptingForPoint(object sender, PromptPointOptionsEventArgs e)
        {
            if (!IsActive || Document == null) return;

            // Игнорируем запросы внутри самой PTP
            if ((Document.CommandInProgress ?? "").ToUpper() == "PTP") return;

            // Пропускаем запрос который поглотит coord (переданные координаты)
            if (_skipCount > 0)
            {
                _skipCount--;
                return;
            }

            // Отписываемся — чтобы не сработать повторно пока jig работает
            Document.Editor.PromptingForPoint -= OnPromptingForPoint;

            // Подаём 'PTP
            Document.SendStringToExecute("'PTP", false, false, false);
        }

        private static void OnCommandEnded(object sender, CommandEventArgs e)
        {
            if (!IsActive || Document == null) return;
            if ((e.GlobalCommandName ?? "").ToUpper() == "PTP") return;
            // Родительская команда завершена
            Stop();
        }

        private static void OnParentCommandDone(object sender, CommandEventArgs e)
        {
            if ((e.GlobalCommandName ?? "").ToUpper() == "PTP") return;
            Stop();
        }

        public static Point3d? RunJig(Editor ed, Database db)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                ProfileView profView = tr.GetObject(ProfileViewId, OpenMode.ForRead) as ProfileView;
                Alignment alignment  = tr.GetObject(AlignmentId,   OpenMode.ForRead) as Alignment;
                if (profView == null || alignment == null) return null;

                double anchorY = profView.Location.Y;

                // ── Пикет ──
                StationJig stJig = new StationJig(profView, anchorY);
                PromptResult stRes = ed.Drag(stJig);
                if (stRes.Status == PromptStatus.Cancel) return null;

                double station = 0, elevation = 0, fixedX = 0;

                if (stRes.Status == PromptStatus.Keyword)
                {
                    PromptDoubleResult pr = ed.GetDouble(
                        new PromptDoubleOptions("[PTP] Пикет (м): "));
                    if (pr.Status != PromptStatus.OK) return null;
                    station = pr.Value;

                    PromptDoubleResult er = ed.GetDouble(
                        new PromptDoubleOptions("[PTP] Отметка (м): "));
                    if (er.Status != PromptStatus.OK) return null;
                    elevation = er.Value;

                    double dummyY = 0;
                    profView.FindXYAtStationAndElevation(station, elevation, ref fixedX, ref dummyY);
                }
                else if (stRes.Status == PromptStatus.OK)
                {
                    station = Math.Max(alignment.StartingStation,
                              Math.Min(alignment.EndingStation, stJig.Station));
                    fixedX  = stJig.FixedX;

                    ed.WriteMessage($"[PTP] Пикет: {station:F3} м");

                    // ── Отметка ──
                    ElevationJig elJig = new ElevationJig(profView, fixedX);
                    PromptResult elRes = ed.Drag(elJig);
                    if (elRes.Status == PromptStatus.Cancel) return null;

                    if (elRes.Status == PromptStatus.OK)
                    {
                        elevation = elJig.Elevation;
                    }
                    else
                    {
                        PromptDoubleResult er = ed.GetDouble(
                            new PromptDoubleOptions("[PTP] Отметка (м): "));
                        if (er.Status != PromptStatus.OK) return null;
                        elevation = er.Value;
                    }
                }
                else return null;

                ed.WriteMessage($"[PTP] Отметка: {elevation:F3} м");

                // ── Смещение ──
                OffsetJig offJig = new OffsetJig(alignment, station, elevation);
                PromptResult offRes = ed.Drag(offJig);
                if (offRes.Status == PromptStatus.Cancel) return null;

                Point3d result;
                if (offRes.Status == PromptStatus.OK)
                {
                    result = offJig.FinalPoint;
                    ed.WriteMessage($"[PTP] Смещение: {offJig.FinalOffset:F3} м");
                }
                else
                {
                    PromptDoubleResult offMan = ed.GetDouble(
                        new PromptDoubleOptions("[PTP] Смещение (+вправо/-влево, м): "));
                    if (offMan.Status != PromptStatus.OK) return null;
                    double rx = 0, ry = 0;
                    alignment.PointLocation(station, offMan.Value, ref rx, ref ry);
                    result = new Point3d(rx, ry, elevation);
                }

                tr.Commit();
                return result;
            }
        }
    }
    // =========================================================================
    //  Основная команда PTP — включает/выключает режим
    // =========================================================================
    public class ProfileToPlaneTransparent
    {
        [CommandMethod("PTP",
            CommandFlags.Transparent |
            CommandFlags.UsePickSet  |
            CommandFlags.Redraw)]
        public static void ProfileToPlaneCmd()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Editor   ed = doc.Editor;
            Database db = doc.Database;

            // ── Если режим активен — PTP вызван автоматически сессией
            //    Запускаем jig и передаём точку родительской команде ──
            if (PtpSession.IsActive)
            {
                Point3d? result = PtpSession.RunJig(ed, db);

                if (result.HasValue)
                {
                    Application.SetSystemVariable("LASTPOINT", result.Value);

                    string coord =
                        result.Value.X.ToString("F6", System.Globalization.CultureInfo.InvariantCulture) + "," +
                        result.Value.Y.ToString("F6", System.Globalization.CultureInfo.InvariantCulture) + "," +
                        result.Value.Z.ToString("F6", System.Globalization.CultureInfo.InvariantCulture) + "";

                    doc.SendStringToExecute(coord, false, false, false);
                    ed.WriteMessage(
                        $"[PTP] Точка ({result.Value.X:F3}; {result.Value.Y:F3}; {result.Value.Z:F3}) передана.");
                    PtpSession.OnPointDelivered();
                }
                else
                {
                    // Пользователь нажал Esc — выключаем режим
                    PtpSession.Stop();
                }
                return;
            }

            // ── Первый вызов PTP — выбор вида профиля и включение режима ──
            PromptEntityOptions pvOpt = new PromptEntityOptions(
                "[PTP] Выберите вид профиля: ");
            pvOpt.SetRejectMessage("Выбранный объект не является видом профиля.");
            pvOpt.AddAllowedClass(typeof(ProfileView), false);

            PromptEntityResult pvRes = ed.GetEntity(pvOpt);
            if (pvRes.Status != PromptStatus.OK)
            {
                ed.WriteMessage("[PTP] Отменено.");
                return;
            }

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                ProfileView profView = tr.GetObject(pvRes.ObjectId, OpenMode.ForRead) as ProfileView;
                if (profView == null) { ed.WriteMessage("[PTP] Ошибка вида профиля."); return; }

                Alignment alignment = tr.GetObject(profView.AlignmentId, OpenMode.ForRead) as Alignment;
                if (alignment == null) { ed.WriteMessage("[PTP] Ошибка трассы."); return; }

                // Включаем режим
                PtpSession.Start(doc, profView.ObjectId, alignment.ObjectId);

                tr.Commit();
            }
        }
    }
}
