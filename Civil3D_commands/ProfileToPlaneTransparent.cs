// =============================================================================
//  ProfileToPlaneTransparent.cs  —  v4 (настоящая прозрачная команда)
//
//  'PTP набирается ИЗНУТРИ любой команды AutoCAD в ответ на запрос точки.
//  Один раз выбирается вид профиля и способ передачи (3D/2D), дальше на каждый
//  следующий запрос точки родительской команды режим включается сам:
//
//      щелчок на виде профиля  → пикет и отметка
//      щелчок в плане          → смещение по нормали к оси на этом пикете
//      точка уходит в родительскую команду строкой координат
//
//  Так рисуется целая полилиния, не выходя из PLINE.
//
//  Режим НЕ привязан к жизни родительской команды: он переживает её конец и
//  перехватывает запрос точки следующей. Иначе `POINT`, который ставит одну
//  точку и заканчивается, сбрасывал бы режим после каждой точки.
//  Выход только явный: Esc внутри PTP, повторный 'PTP, команда PTPOFF.
//
//  --- Как режим удерживается между точками --------------------------------
//  Вернуть точку в ожидающий запрос из .NET нечем: API для этого нет.
//  Единственный рабочий путь — послать координату строкой
//  (`SendStringToExecute`), она попадает в тот самый ожидающий запрос.
//  Строка ОБЯЗАНА заканчиваться "\n": без завершителя текст только набирается
//  в командной строке и не отправляется — на этом молча стояла v3.
//
//  Перевооружение идёт по паре событий, а не по счётчику пропусков:
//      PromptedForPoint  — родительский запрос ЗАКРЫТ нашей координатой,
//                          значит можно вклиниваться в следующий  → _armed = true
//      PromptingForPoint — родитель просит следующую точку, _armed снимается
//                          и подаётся 'PTP
//  Счётчик в v3 угадывал, поднимет ли AutoCAD PromptingForPoint повторно для
//  уже висящего запроса; при неверной догадке перехват работал через раз.
//  Пара событий верна при любом ответе на этот вопрос.
//
//  Собственные запросы PTP (выбор точки, жиг) поднимают те же события —
//  на время работы команды они глушатся флагом PtpSession.Inside.
//
//  Зависимости: acdbmgd.dll, acmgd.dll, AeccDbMgd.dll
// =============================================================================

using System;
using System.Globalization;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Civil3D_commands.Shared;
using CivilAlignment   = Autodesk.Civil.DatabaseServices.Alignment;
using CivilProfileView = Autodesk.Civil.DatabaseServices.ProfileView;

[assembly: CommandClass(typeof(ProfileToPlane.ProfileToPlaneTransparent))]

namespace ProfileToPlane
{
    // =========================================================================
    //  Жиг смещения — точка едет по нормали к оси на зафиксированном пикете
    //
    //  Перекрестье AutoCAD физически к линии не привязать, поэтому нормаль
    //  рисуется рельсом во всю ширину экрана, а маркер точки скользит строго
    //  по ней: курсор ходит свободно, ставится всегда проекция на нормаль.
    // =========================================================================
    internal class OffsetJig : DrawJig
    {
        private readonly Point3d  _base;
        private readonly Vector3d _normal;   // единичная, +вправо от направления оси
        private readonly double   _rail;     // половина длины рельса, ед. чертежа

        private Point3d _preview;

        public double Offset { get; private set; }

        public OffsetJig(Point3d basePoint, Vector3d normal, double rail)
        {
            _base    = basePoint;
            _normal  = normal;
            _rail    = rail;
            _preview = basePoint;
        }

        protected override SamplerStatus Sampler(JigPrompts prompts)
        {
            JigPromptPointOptions opt = new JigPromptPointOptions(
                "\n[PTP] Смещение от оси (+вправо/-влево)");
            opt.BasePoint    = _base;
            opt.UseBasePoint = true;
            opt.UserInputControls =
                UserInputControls.Accept3dCoordinates |
                UserInputControls.GovernedByOrthoMode;

            // Латиницей только глобальное имя — оно приходит в StringResult.
            opt.Keywords.Add("Number", "Ввод", "Ввод");
            opt.AppendKeywordsToMessage = true;

            PromptPointResult res = prompts.AcquirePoint(opt);

            if (res.Status == PromptStatus.Keyword) return SamplerStatus.OK;
            if (res.Status == PromptStatus.Cancel)  return SamplerStatus.Cancel;
            if (res.Status != PromptStatus.OK)      return SamplerStatus.NoChange;

            double offset = (res.Value - _base).DotProduct(_normal);
            if (Math.Abs(offset - Offset) < 1e-9) return SamplerStatus.NoChange;

            Offset   = offset;
            _preview = _base + _normal * offset;
            return SamplerStatus.OK;
        }

        protected override bool WorldDraw(Autodesk.AutoCAD.GraphicsInterface.WorldDraw draw)
        {
            // Рельс — сама нормаль к оси на этом пикете
            draw.Geometry.WorldLine(_base - _normal * _rail, _base + _normal * _rail);

            // Отложенное смещение
            draw.Geometry.WorldLine(_base, _preview);

            // Крестик на текущей точке
            double t = _rail * 0.02;
            draw.Geometry.WorldLine(_preview + new Vector3d(-t, 0, 0), _preview + new Vector3d(t, 0, 0));
            draw.Geometry.WorldLine(_preview + new Vector3d(0, -t, 0), _preview + new Vector3d(0, t, 0));

            return true;
        }
    }

    // =========================================================================
    //  Состояние режима
    // =========================================================================
    internal static class PtpSession
    {
        public static bool     IsActive      { get; private set; }
        public static ObjectId ProfileViewId { get; private set; }
        public static ObjectId AlignmentId   { get; private set; }
        public static bool     Send3d        { get; private set; }

        /// <summary>PTP выполняется прямо сейчас — собственные запросы точки не перехватывать.</summary>
        public static bool Inside;

        private static Document _doc;
        private static bool     _armed;
        private static bool     _selfInvoked;

        // Диагностика (PTPDIAG). Дешевле одного лишнего сеанса Civil 3D.
        public static int    PromptingCount;
        public static int    PromptedCount;
        public static int    InvokeCount;
        public static int    DeliverCount;
        public static string StartContext = "";

        public static bool Armed { get { return _armed; } }

        public static void Start(Document doc, ObjectId pv, ObjectId al, bool send3d, bool armNow)
        {
            _doc          = doc;
            ProfileViewId = pv;
            AlignmentId   = al;
            Send3d        = send3d;
            IsActive      = true;
            _selfInvoked  = false;

            // Если PTP набрали внутри уже висящего запроса точки, этот запрос мы
            // закрываем сами — вклиниваться в него второй раз не нужно.
            _armed = armNow;

            // На CommandEnded/Cancelled/Failed не подписываемся сознательно:
            // конец родительской команды режим не гасит, см. шапку файла.
            doc.Editor.PromptingForPoint += OnPromptingForPoint;
            doc.Editor.PromptedForPoint  += OnPromptedForPoint;
        }

        public static void Stop(string reason)
        {
            if (!IsActive) return;
            IsActive = false;
            _armed   = false;

            if (_doc != null)
            {
                _doc.Editor.PromptingForPoint -= OnPromptingForPoint;
                _doc.Editor.PromptedForPoint  -= OnPromptedForPoint;

                if (!string.IsNullOrEmpty(reason))
                    _doc.Editor.WriteMessage("\n[PTP] Режим выключен: " + reason);
            }
            _doc = null;
        }

        /// <summary>true, если этот запуск PTP подан сессией, а не набран пользователем.</summary>
        public static bool ConsumeSelfInvoked()
        {
            bool v = _selfInvoked;
            _selfInvoked = false;
            return v;
        }

        /// <summary>Координата уходит в ожидающий запрос родительской команды.</summary>
        public static void Deliver(Document doc, Point3d p)
        {
            string coord = Send3d
                ? Fmt(p.X) + "," + Fmt(p.Y) + "," + Fmt(p.Z)
                : Fmt(p.X) + "," + Fmt(p.Y);

            DeliverCount++;

            // "\n" обязателен: без него строка только набирается, но не вводится.
            doc.SendStringToExecute(coord + "\n", false, false, false);
        }

        private static string Fmt(double v)
        {
            return v.ToString("F6", CultureInfo.InvariantCulture);
        }

        // --- события -------------------------------------------------------

        private static void OnPromptedForPoint(object sender, PromptPointResultEventArgs e)
        {
            if (!IsActive || Inside) return;
            PromptedCount++;

            // Запрос закрыт — со следующего можно вклиниваться. Чей это был
            // запрос, неважно: режим живёт и после конца родительской команды.
            _armed = true;
        }

        private static void OnPromptingForPoint(object sender, PromptPointOptionsEventArgs e)
        {
            if (!IsActive || Inside || _doc == null) return;

            // Чертёж переключили — режим относился к прежнему.
            if (!ReferenceEquals(Application.DocumentManager.MdiActiveDocument, _doc))
            {
                Stop("переключение чертежа");
                return;
            }

            PromptingCount++;

            if (!_armed) return;
            _armed = false;

            _selfInvoked = true;
            _doc.SendStringToExecute("'PTP\n", false, false, false);
        }
    }

    // =========================================================================
    //  Команда
    // =========================================================================
    public class ProfileToPlaneTransparent
    {
        [CommandMethod("PTP", CommandFlags.Transparent | CommandFlags.Redraw)]
        public static void ProfileToPlaneCmd()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Editor ed = doc.Editor;
            PtpSession.InvokeCount++;

            bool self = PtpSession.ConsumeSelfInvoked();

            // Подан сессией, но родитель успел закончиться — тихо выходим.
            if (self && !PtpSession.IsActive) return;

            // Набран пользователем при работающем режиме — это выключатель.
            if (!self && PtpSession.IsActive)
            {
                PtpSession.Stop("повторный вызов PTP");
                return;
            }

            PtpSession.Inside = true;
            try
            {
                if (!PtpSession.IsActive && !Activate(doc, ed)) return;
                if (!PtpSession.IsActive) return;   // режим только вооружён, точки ещё не ждут

                Point3d? pt = RunCycle(doc, ed);
                if (!pt.HasValue)
                {
                    PtpSession.Stop("отменено пользователем");
                    return;
                }

                PtpSession.Deliver(doc, pt.Value);
                ed.WriteMessage(PtpSession.Send3d
                    ? string.Format(CultureInfo.InvariantCulture,
                        "\n[PTP] Передана точка 3D ({0:F3}; {1:F3}; {2:F3})", pt.Value.X, pt.Value.Y, pt.Value.Z)
                    : string.Format(CultureInfo.InvariantCulture,
                        "\n[PTP] Передана точка 2D ({0:F3}; {1:F3})", pt.Value.X, pt.Value.Y));
            }
            finally
            {
                PtpSession.Inside = false;
            }
        }

        // -------------------------------------------------------------------
        //  Включение: вид профиля + способ передачи
        // -------------------------------------------------------------------
        private static bool Activate(Document doc, Editor ed)
        {
            // Вложены ли мы в чужую команду, чей запрос точки надо закрыть.
            //
            // `CommandInProgress` для этого НЕ ГОДИТСЯ: при вызове 'PTP из-под
            // PLINE он возвращает не «PLINE», а собственное имя команды, и
            // проверка «есть родитель» давала false. Первый цикл тогда не
            // запускался, PLINE спрашивал начальную точку сам, и первая вершина
            // ложилась туда, куда пользователь щёлкал на виде профиля.
            //
            // CMDACTIVE — битовая маска: 1 = идёт обычная команда,
            // 2 = поверх неё идёт прозрачная. Нужен именно второй бит.
            int cmdActive = 0;
            try { cmdActive = Convert.ToInt32(Application.GetSystemVariable("CMDACTIVE")); }
            catch (System.Exception) { }
            bool nested = (cmdActive & 2) != 0;

            string ctx = "CMDACTIVE=" + cmdActive +
                         ", CommandInProgress=" + (doc.CommandInProgress ?? "").Trim();

            PromptEntityOptions pvOpt = new PromptEntityOptions(
                "\n[PTP] Выберите вид профиля: ");
            pvOpt.SetRejectMessage("\nЭто не вид профиля.");
            pvOpt.AddAllowedClass(typeof(CivilProfileView), false);

            PromptEntityResult pvRes = ed.GetEntity(pvOpt);
            if (pvRes.Status != PromptStatus.OK)
            {
                ed.WriteMessage("\n[PTP] Отменено.");
                return false;
            }

            PromptKeywordOptions kw = new PromptKeywordOptions(
                "\n[PTP] Что передавать в команду");
            kw.Keywords.Add("3D", "3D", "3D");
            kw.Keywords.Add("2D", "2D", "2D");
            kw.Keywords.Default = "3D";          // ГЛОБАЛЬНОЕ имя, иначе eInvalidInput
            kw.AllowNone = true;

            PromptResult kwRes = ed.GetKeywords(kw);
            if (kwRes.Status != PromptStatus.OK && kwRes.Status != PromptStatus.None)
            {
                ed.WriteMessage("\n[PTP] Отменено.");
                return false;
            }
            bool send3d = kwRes.Status == PromptStatus.None || kwRes.StringResult == "3D";

            ObjectId alignmentId;
            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                CivilProfileView pv = tr.GetObject(pvRes.ObjectId, OpenMode.ForRead) as CivilProfileView;
                if (pv == null) { ed.WriteMessage("\n[PTP] Вид профиля не читается."); return false; }

                alignmentId = pv.AlignmentId;
                if (alignmentId.IsNull || alignmentId.IsErased)
                {
                    ed.WriteMessage("\n[PTP] У вида профиля нет трассы.");
                    return false;
                }
                tr.Commit();
            }

            PtpSession.StartContext = ctx;
            PtpSession.Start(doc, pvRes.ObjectId, alignmentId, send3d, armNow: !nested);

            ed.WriteMessage("\n[PTP] Режим включён, передача " + (send3d ? "3D" : "2D") +
                            ". Работает до сброса: Esc, повторный PTP или PTPOFF.");

            if (!nested)
            {
                // Вызвано из пустой командной строки: точку девать некуда,
                // поэтому просто ждём первого запроса точки от любой команды.
                ed.WriteMessage("\n[PTP] Ожидаю команду, которая запросит точку.");
                return false;
            }
            return true;
        }

        // -------------------------------------------------------------------
        //  Один цикл: профиль → план → точка
        //
        //  Транзакции короткие и снаружи от щелчков: держать вид профиля и
        //  трассу открытыми, пока пользователь целится, незачем — тем более
        //  внутри чужой команды, которая в это время правит чертёж.
        // -------------------------------------------------------------------
        private static Point3d? RunCycle(Document doc, Editor ed)
        {
            double station, elevation;
            if (!PickStationElevation(doc, ed, out station, out elevation)) return null;

            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n[PTP] Пикет {0:F3}, отметка {1:F3}", station, elevation));

            Point3d basePoint;
            Vector3d normal;
            if (!PlanFrame(doc, station, out basePoint, out normal))
            {
                ed.WriteMessage("\n[PTP] Пикет вне трассы — точку в плане не построить.");
                return null;
            }

            double rail;
            using (ViewTableRecord view = ed.GetCurrentView())
                rail = Math.Max(view.Height, view.Width) * 0.75;

            OffsetJig jig = new OffsetJig(basePoint, normal, rail);
            PromptResult jigRes = ed.Drag(jig);

            double offset;
            if (jigRes.Status == PromptStatus.OK)
            {
                offset = jig.Offset;
            }
            else if (jigRes.Status == PromptStatus.Keyword)
            {
                PromptDoubleOptions dOpt = new PromptDoubleOptions(
                    "\n[PTP] Смещение (+вправо/-влево), м: ");
                dOpt.AllowNone = false;
                PromptDoubleResult dRes = ed.GetDouble(dOpt);
                if (dRes.Status != PromptStatus.OK) return null;
                offset = dRes.Value;
            }
            else return null;

            Point3d plan;
            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                CivilAlignment al = tr.GetObject(PtpSession.AlignmentId, OpenMode.ForRead) as CivilAlignment;
                bool ok = RwGeometry.TryPointOnAlignment(al, station, offset, out plan);
                tr.Commit();
                if (!ok)
                {
                    ed.WriteMessage("\n[PTP] Точку с таким смещением на трассе не построить.");
                    return null;
                }
            }

            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n[PTP] Смещение {0:F3}", offset));

            return new Point3d(plan.X, plan.Y, elevation);
        }

        // -------------------------------------------------------------------
        //  Щелчок на виде профиля → пикет и отметка (или ввод с клавиатуры)
        // -------------------------------------------------------------------
        private static bool PickStationElevation(
            Document doc, Editor ed, out double station, out double elevation)
        {
            station = 0.0;
            elevation = 0.0;

            while (true)
            {
                PromptPointOptions opt = new PromptPointOptions(
                    "\n[PTP] Точка на виде профиля");
                opt.Keywords.Add("Number", "Ввод", "Ввод");
                opt.AppendKeywordsToMessage = true;

                PromptPointResult res = ed.GetPoint(opt);

                if (res.Status == PromptStatus.Keyword)
                {
                    PromptDoubleResult s = ed.GetDouble(
                        new PromptDoubleOptions("\n[PTP] Пикет, м: "));
                    if (s.Status != PromptStatus.OK) return false;

                    PromptDoubleResult e = ed.GetDouble(
                        new PromptDoubleOptions("\n[PTP] Отметка, м: "));
                    if (e.Status != PromptStatus.OK) return false;

                    station   = s.Value;
                    elevation = e.Value;
                    return true;
                }

                if (res.Status != PromptStatus.OK) return false;

                bool ok;
                using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                {
                    CivilProfileView pv =
                        tr.GetObject(PtpSession.ProfileViewId, OpenMode.ForRead) as CivilProfileView;
                    ok = RwGeometry.TryStationInProfileView(pv, res.Value, out station, out elevation);
                    tr.Commit();
                }

                if (ok) return true;

                // Мимо вида — RwGeometry вернула false вместо исключения,
                // поэтому просто просим ткнуть ещё раз.
                ed.WriteMessage("\n[PTP] Точка вне выбранного вида профиля, повторите.");
            }
        }

        // -------------------------------------------------------------------
        //  Точка на оси и единичная нормаль к ней на заданном пикете
        //
        //  Нормаль берётся из PointLocation(пикет, смещение) — там смещение и
        //  отмеряется по нормали. Поворот касательной (как было в v3) врёт на
        //  малых радиусах и в конце оси, см. Shared/RwGeometry.
        // -------------------------------------------------------------------
        private static bool PlanFrame(
            Document doc, double station, out Point3d basePoint, out Vector3d normal)
        {
            basePoint = Point3d.Origin;
            normal    = Vector3d.XAxis;

            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                CivilAlignment al =
                    tr.GetObject(PtpSession.AlignmentId, OpenMode.ForRead) as CivilAlignment;

                Point3d p0 = Point3d.Origin, p1 = Point3d.Origin;
                bool ok = RwGeometry.TryPointOnAlignment(al, station, 0.0, out p0) &&
                          RwGeometry.TryPointOnAlignment(al, station, 1.0, out p1);
                tr.Commit();

                if (!ok) return false;

                Vector3d v = p1 - p0;
                if (v.Length < Tolerance.Global.EqualPoint) return false;

                basePoint = p0;
                normal    = v.GetNormal();
                return true;
            }
        }

        // -------------------------------------------------------------------
        //  Явный выключатель. Нужен потому, что режим больше не гаснет сам:
        //  если перехват оказался некстати, а до запроса точки не добраться,
        //  выключить его иначе нечем.
        // -------------------------------------------------------------------
        [CommandMethod("PTPOFF", CommandFlags.Transparent)]
        public static void PtpOffCmd()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            if (!PtpSession.IsActive)
            {
                doc.Editor.WriteMessage("\n[PTP] Режим и так выключен.");
                return;
            }
            PtpSession.Stop("команда PTPOFF");
        }

        // -------------------------------------------------------------------
        //  Диагностика: что режим видит на самом деле
        // -------------------------------------------------------------------
        [CommandMethod("PTPDIAG")]
        public static void PtpDiagCmd()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Editor ed = doc.Editor;
            ed.WriteMessage("\n=== PTPDIAG ===");
            ed.WriteMessage("\n  режим активен:        " + PtpSession.IsActive);
            ed.WriteMessage("\n  вооружён:             " + PtpSession.Armed);
            ed.WriteMessage("\n  внутри PTP:           " + PtpSession.Inside);
            ed.WriteMessage("\n  передача:             " + (PtpSession.Send3d ? "3D" : "2D"));
            ed.WriteMessage("\n  запусков PTP:         " + PtpSession.InvokeCount);
            ed.WriteMessage("\n  PromptingForPoint:    " + PtpSession.PromptingCount);
            ed.WriteMessage("\n  PromptedForPoint:     " + PtpSession.PromptedCount);
            ed.WriteMessage("\n  передано координат:   " + PtpSession.DeliverCount);
            ed.WriteMessage("\n  контекст включения:   " + PtpSession.StartContext);
            ed.WriteMessage("\n===============");
        }
    }
}
