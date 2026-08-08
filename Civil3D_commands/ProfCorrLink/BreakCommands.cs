using System;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Civil3D_commands.Shared;
using AcAp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: ExtensionApplication(typeof(Civil3D_commands.AssociativeBreaks.BreakCommands))]
[assembly: CommandClass(typeof(Civil3D_commands.AssociativeBreaks.BreakCommands))]

namespace Civil3D_commands.AssociativeBreaks
{
    public class BreakCommands : IExtensionApplication
    {
        private static BreakGripOverrule _gripOverrule;

        // ------------------------------------------------------------------
        //  ЖИЗНЕННЫЙ ЦИКЛ
        // ------------------------------------------------------------------
        public void Initialize()
        {
            _gripOverrule = new BreakGripOverrule();
            Overrule.AddOverrule(RXObject.GetClass(typeof(Line)), _gripOverrule, false);
            Overrule.Overruling = true;

            // Модуль облицовки вешает на Line свой оверрул ручек. Своего
            // IExtensionApplication у него быть не может — в сборке разрешён
            // один, поэтому включаем отсюда. Чужие отрезки оба оверрула
            // пропускают в base, так что соседство безопасно.
            // try/catch: падение здесь оставило бы без реактора и разрывы.
            try
            {
                FaceArr.FacingWallGrips.Enable();
            }
            catch (System.Exception)
            {
                // не смертельно: ручки облицовки включаются командой FACINGWALLGRIPS
            }

            AcAp.DocumentManager.DocumentActivated    += (s, e) => AttachTo(e.Document);
            AcAp.DocumentManager.DocumentDestroyed    += (s, e) => BreakSession.DetachByFileName(e.FileName);
            var doc = AcAp.DocumentManager.MdiActiveDocument;
            if (doc != null) AttachTo(doc);
        }

        public void Terminate()
        {
            if (_gripOverrule != null)
            {
                Overrule.RemoveOverrule(RXObject.GetClass(typeof(Line)), _gripOverrule);
                _gripOverrule.Dispose();
                _gripOverrule = null;
            }
        }

        private static void AttachTo(Document doc)
        {
            if (doc == null) return;
            // НЕ детачим существующую сессию — иначе теряем IsEditMode и ActiveLink
            // при каждом переключении окна. Attach сам проверяет наличие сессии.
            BreakSession.Attach(doc);
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                PropertySetSupport.EnsureEditPsd(doc.Database);
                PropertySetSupport.EnsureMarkerPsd(doc.Database);
                tr.Commit();
            }
            BreakProxyFactory.EnsureRegApp(doc.Database);
        }

        // ------------------------------------------------------------------
        //  МАСТЕР: создать профиль + коридор и связать их
        // ------------------------------------------------------------------
        [CommandMethod("RW_LINKPROFILECORRIDOR")]
        public void LinkProfileCorridor()
        {
            var doc = AcAp.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            var db = doc.Database;
            var session = BreakSession.Current;
            var civDoc = CivilApplication.ActiveDocument;

            // 1) Вид профиля
            var pvOpt = new PromptEntityOptions("\nВыберите вид продольного профиля");
            pvOpt.SetRejectMessage("\nНужен ProfileView");
            pvOpt.AddAllowedClass(typeof(ProfileView), true);
            var pvRes = ed.GetEntity(pvOpt);
            if (pvRes.Status != PromptStatus.OK) return;

            // ------------------------------------------------------------------
            // Данные вида профиля нужны раньше вопросов: из них берётся
            // и подсказка по отметке, и пределы оси.
            // ------------------------------------------------------------------
            ObjectId alignmentId;
            Handle pvHandle, alHandle;
            double alStart, alEnd, elevLow, elevHigh;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var pv = (ProfileView)tr.GetObject(pvRes.ObjectId, OpenMode.ForRead);
                alignmentId = pv.AlignmentId;
                pvHandle = pv.Handle;
                elevLow = pv.ElevationMin;
                elevHigh = pv.ElevationMax;

                var al = (Alignment)tr.GetObject(alignmentId, OpenMode.ForRead);
                alHandle = al.Handle;
                alStart = al.StartingStation;
                alEnd = al.EndingStation;
                tr.Commit();
            }

            // 2) Отметка ПЛОСКОГО ПРОФИЛЯ-ОСНОВАНИЯ, который создаёт мастер.
            //    Коридор строится по нему, а разрывы потом режут в нём ступени,
            //    поэтому отметка задаёт исходный уровень всей стены. Значение
            //    по умолчанию — середина вида, чтобы профиль заведомо оказался
            //    в видимой части.
            double baseElevation = Math.Round((elevLow + elevHigh) / 2.0, 3);

            var elOpt = new PromptDoubleOptions(
                "\nОтметка плоского профиля-основания (по нему построится коридор)")
            {
                DefaultValue = baseElevation,
                UseDefaultValue = true,
                AllowNegative = true,
                AllowZero = true
            };
            elOpt.Keywords.Add("Pick", "Указать", "Указать");
            elOpt.AppendKeywordsToMessage = true;

            var elRes = ed.GetDouble(elOpt);

            if (elRes.Status == PromptStatus.Keyword && elRes.StringResult == "Pick")
            {
                var ptRes = ed.GetPoint("\nУкажите точку в виде профиля на нужной отметке");
                if (ptRes.Status != PromptStatus.OK) return;

                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var pv = (ProfileView)tr.GetObject(pvRes.ObjectId, OpenMode.ForRead);
                    double st, el;
                    if (RwGeometry.TryStationInProfileView(pv, ptRes.Value, out st, out el))
                        baseElevation = el;
                    else
                        ed.WriteMessage("\nТочка вне вида профиля — оставлена отметка по умолчанию.");
                    tr.Commit();
                }
            }
            else if (elRes.Status == PromptStatus.OK)
            {
                baseElevation = elRes.Value;
            }
            else return;

            // 3) Новый коридор или существующий?
            var corrKw = new PromptKeywordOptions(
                "\nКоридор: создать новый или выбрать существующий?");
            // Первый параметр — глобальное имя (латиницей, оно же возвращается
            // в StringResult), второй и третий — то, что показано и вводится.
            // Латиница во втором параметре ломает ввод: AutoCAD отвергает
            // показанное русское слово как неправильное ключевое.
            corrKw.Keywords.Add("Create", "Создать", "Создать");
            corrKw.Keywords.Add("Select", "Выбрать", "Выбрать");
            corrKw.Keywords.Default = "Create";
            var corrKwRes = ed.GetKeywords(corrKw);
            if (corrKwRes.Status != PromptStatus.OK) return;
            bool createNew = corrKwRes.StringResult == "Create";

            // 4) Имя нового коридора ИЛИ выбор существующего
            string corrName = null;
            ObjectId existingCorrId = ObjectId.Null;

            if (createNew)
            {
                var nameRes = ed.GetString("\nИмя нового коридора");
                if (nameRes.Status != PromptStatus.OK) return;
                corrName = nameRes.StringResult;
            }
            else
            {
                var corrOpt = new PromptEntityOptions("\nВыберите существующий коридор");
                corrOpt.SetRejectMessage("\nНужен коридор");
                corrOpt.AddAllowedClass(typeof(Corridor), true);
                var corrRes = ed.GetEntity(corrOpt);
                if (corrRes.Status != PromptStatus.OK) return;
                existingCorrId = corrRes.ObjectId;
            }

            // 5) Конструкция (Assembly) — только при создании нового коридора
            ObjectId assemblyId = ObjectId.Null;
            if (createNew)
            {
                var asmKw = new PromptKeywordOptions(
                    "\nЗадать конструкцию (Assembly) коридора?");
                asmKw.Keywords.Add("Yes", "Да", "Да");
                asmKw.Keywords.Add("No", "Нет", "Нет");
                asmKw.Keywords.Default = "Нет";
                var asmKwRes = ed.GetKeywords(asmKw);
                if (asmKwRes.Status != PromptStatus.OK) return;

                if (asmKwRes.StringResult == "Yes")
                {
                    var asmOpt = new PromptEntityOptions("\nВыберите конструкцию (Assembly)");
                    asmOpt.SetRejectMessage("\nНужна Assembly");
                    asmOpt.AddAllowedClass(typeof(Assembly), true);
                    var asmRes = ed.GetEntity(asmOpt);
                    if (asmRes.Status != PromptStatus.OK) return;
                    assemblyId = asmRes.ObjectId;
                }
            }

            // ------------------------------------------------------------------
            // Создаём профиль, коридор (если нужно), базовую линию
            // ------------------------------------------------------------------
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                // --- Стиль профиля и набор меток ---
                // Набор меток берётся пустой: иначе у каждого перелома профиля
                // вырастает группа меток, а переломов здесь ровно столько,
                // сколько разрывов, и подписывать их незачем.
                ObjectId styleId = civDoc.Styles.ProfileStyles[0];
                ObjectId labelSetId = FindEmptyLabelSet(civDoc, tr);

                // --- Создаём плоский профиль ---
                string profName = "Профиль-основание_" + DateTime.Now.Ticks;
                ObjectId profId;

                try
                {
                    profId = Profile.CreateByLayout(
                        profName, alignmentId, db.LayerZero, styleId, labelSetId);
                }
                catch (System.Exception)
                {
                    // Пустой набор меток не принят — берём первый из списка.
                    // Лишние метки лучше, чем несозданный профиль.
                    profId = Profile.CreateByLayout(
                        profName, alignmentId, db.LayerZero, styleId,
                        civDoc.Styles.LabelSetStyles.ProfileLabelSetStyles[0]);
                    ed.WriteMessage(
                        "\nПустой набор меток профиля не применился — метки придётся снять вручную.");
                }

                var profile = (Profile)tr.GetObject(profId, OpenMode.ForWrite);
                profile.PVIs.AddPVI(alStart, baseElevation);
                profile.PVIs.AddPVI(alEnd,   baseElevation);

                // --- Свойство режима редактирования ---
                PropertySetSupport.Attach(tr, profId,
                    PropertySetSupport.EnsureEditPsd(db));

                // --- Коридор ---
                ObjectId corrId;
                if (createNew)
                {
                    if (assemblyId.IsNull)
                    {
                        // Без конструкции
                        corrId = civDoc.CorridorCollection.Add(
                            corrName,
                            "Базовая линия",
                            alignmentId,
                            profId);
                    }
                    else
                    {
                        // С конструкцией
                        corrId = civDoc.CorridorCollection.Add(
                            corrName,
                            "Базовая линия",
                            alignmentId,
                            profId,
                            "Участок 1",
                            assemblyId);
                    }

                    var corridor = (Corridor)tr.GetObject(corrId, OpenMode.ForWrite);
                    corridor.Rebuild();
                    ed.WriteMessage($"\nКоридор «{corrName}» создан.");
                }
                else
                {
                    // Существующий коридор: добавляем базовую линию
                    corrId = existingCorrId;
                    var corridor = (Corridor)tr.GetObject(corrId, OpenMode.ForWrite);
                    corridor.Baselines.Add("Базовая линия", alignmentId, profId);
                    ed.WriteMessage("\nБазовая линия добавлена в существующий коридор.");
                }

                // --- Запоминаем активную связь ---
                session.ActiveLink = new StationMarker
                {
                    ProfileHandle     = profile.Handle,
                    ProfileViewHandle = pvHandle,
                    AlignmentHandle   = alHandle,
                    CorridorHandle    = corrId.Handle
                };

                tr.Commit();
            }

            // Связь — в чертёж: после перезапуска Civil 3D она восстановится сама,
            // и разрывы можно будет добавлять к ней, а не создавать всё заново.
            LinkStore.Save(db, session.ActiveLink);

            ed.WriteMessage("\nГотово. Включите режим редактирования: RW_EDITMODE");
        }

        // ------------------------------------------------------------------
        //  РЕЖИМ РЕДАКТИРОВАНИЯ (если переключатель в палитре не читается реактором)
        // ------------------------------------------------------------------
        [CommandMethod("RW_EDITMODE")]
        public void ToggleEditMode()
        {
            var doc = AcAp.DocumentManager.MdiActiveDocument;
            var session = BreakSession.Current;
            var link = session?.ActiveLink;
            if (link == null) { doc.Editor.WriteMessage("\nСначала RW_LINKPROFILECORRIDOR"); return; }

            bool now = !session.IsEditMode(link.ProfileHandle);
            session.SetEditMode(link.ProfileHandle, now);
            doc.Editor.WriteMessage($"\nРежим редактирования: {(now ? "ВКЛ" : "ВЫКЛ")}");
            doc.Editor.Regen();
        }

        // ------------------------------------------------------------------
        //  СОЗДАТЬ РАЗРЫВ
        // ------------------------------------------------------------------
        [CommandMethod("RW_CREATEBREAK")]
        public void CreateBreak()
        {
            var doc = AcAp.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            var session = BreakSession.Current;
            var link = session?.ActiveLink;
            if (link == null) { ed.WriteMessage("\nСначала RW_LINKPROFILECORRIDOR"); return; }

            // Точка в виде профиля -> пикет.
            var ptRes = ed.GetPoint("\nУкажите положение разрыва в виде профиля");
            if (ptRes.Status != PromptStatus.OK) return;

            double station;
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var pv = (ProfileView)tr.GetObject(Resolve(doc.Database, link.ProfileViewHandle), OpenMode.ForRead);
                station = BreakProxyFactory.ProfilePointToStation(pv, ptRes.Value);
                tr.Commit();
            }

            // Ступень?
            var stepKw = new PromptKeywordOptions("\nЭто ступень профиля?");
            stepKw.Keywords.Add("Yes", "Да", "Да");
            stepKw.Keywords.Add("No", "Нет", "Нет");
            stepKw.Keywords.Default = "Да";
            var stepRes = ed.GetKeywords(stepKw);
            if (stepRes.Status != PromptStatus.OK) return;
            bool isStep = stepRes.StringResult == "Yes";

            double stepH = 0;
            if (isStep)
            {
                var hRes = ed.GetDouble("\nВысота ступени (знак: + вверх, - вниз по ходу пикета)");
                if (hRes.Status != PromptStatus.OK) return;
                stepH = hRes.Value;
            }

            // Микроразрыв: по умолчанию тот же, что был константой.
            var gapOpt = new PromptDoubleOptions("\nМикроразрыв между участками")
            {
                DefaultValue = ProfileGeometryOps.DefaultGap,
                UseDefaultValue = true,
                AllowNegative = false,
                AllowZero = false
            };
            var gapRes = ed.GetDouble(gapOpt);
            if (gapRes.Status != PromptStatus.OK) return;

            var marker = new StationMarker
            {
                Station = station,
                IsStep = isStep,
                StepHeight = stepH,
                Gap = ProfileGeometryOps.SanitizeGap(gapRes.Value),
                Layer = "0",                       // АДАПТ: слой по умолчанию
                ProfileHandle = link.ProfileHandle,
                ProfileViewHandle = link.ProfileViewHandle,
                AlignmentHandle = link.AlignmentHandle,
                CorridorHandle = link.CorridorHandle
            };

            // Набор характеристик на прокси пишет сам оркестратор — и при
            // создании, и после каждой правки, иначе палитра врёт.
            session.Manager.CreateBreak(marker);

            ed.WriteMessage(
                $"\nРазрыв создан на пикете {station:F3}, микроразрыв {marker.Gap:F4}." +
                "\nИзменить свойства: RW_EDITBREAK.");
        }

        // ------------------------------------------------------------------
        //  УДАЛИТЬ РАЗРЫВ
        // ------------------------------------------------------------------
        [CommandMethod("RW_DELETEBREAK")]
        public void DeleteBreak()
        {
            var doc = AcAp.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            var session = BreakSession.Current;
            if (session == null) return;

            var opt = new PromptEntityOptions("\nВыберите прокси разрыва для удаления");
            opt.SetRejectMessage("\nНужен прокси разрыва");
            opt.AddAllowedClass(typeof(Line), false);
            var res = ed.GetEntity(opt);
            if (res.Status != PromptStatus.OK) return;

            Guid? id = null;
            bool tagged = false;
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var ln = (Line)tr.GetObject(res.ObjectId, OpenMode.ForRead);
                tagged = BreakProxyFactory.GetMarkerGuid(ln) != null;

                // По Guid из XData разрыв не ищем: у копии прокси он тот же, и
                // удаление копии сносило бы настоящий разрыв. Владение проверяется
                // по хэндлу, записанному в модели.
                id = session.Store.GetByProxy(res.ObjectId.Handle)?.Id;
                tr.Commit();
            }

            if (id == null)
            {
                ed.WriteMessage(tagged
                    ? "\nЭта линия помечена как прокси, но в модели не числится — " +
                      "похоже, это копия. Удалите её обычным стиранием."
                    : "\nЭто не прокси разрыва");
                return;
            }

            session.Manager.DeleteBreak(id.Value);
            ed.WriteMessage("\nРазрыв удалён.");
        }

        // ------------------------------------------------------------------
        //  ИЗМЕНИТЬ СВОЙСТВА РАЗРЫВА
        //  До этого свойства задавались только при создании и потом не менялись.
        // ------------------------------------------------------------------
        [CommandMethod("RW_EDITBREAK")]
        public void EditBreak()
        {
            var doc = AcAp.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            var session = BreakSession.Current;
            if (session == null) return;

            var opt = new PromptEntityOptions("\nВыберите прокси разрыва");
            opt.SetRejectMessage("\nНужен прокси разрыва");
            opt.AddAllowedClass(typeof(Line), false);
            var res = ed.GetEntity(opt);
            if (res.Status != PromptStatus.OK) return;

            StationMarker m = session.Store.GetByProxy(res.ObjectId.Handle);
            if (m == null)
            {
                ed.WriteMessage("\nЭта линия в модели не числится — не прокси разрыва или его копия.");
                return;
            }

            ed.WriteMessage(
                $"\nРазрыв на пикете {m.Station:F3}: ступень={(m.IsStep ? m.StepHeight.ToString("F3") : "нет")}, " +
                $"микроразрыв={m.Gap:F4}");

            // Ключевые слова латиницей с русскими подписями: кириллицу
            // не ввести при английской раскладке.
            var stepKw = new PromptKeywordOptions("\nСтупень профиля?");
            stepKw.Keywords.Add("Yes", "Да", "Да");
            stepKw.Keywords.Add("No", "Нет", "Нет");
            stepKw.Keywords.Default = m.IsStep ? "Да" : "Нет";
            var stepRes = ed.GetKeywords(stepKw);
            if (stepRes.Status != PromptStatus.OK) return;
            bool isStep = stepRes.StringResult == "Yes";

            double stepH = m.StepHeight;
            if (isStep)
            {
                var hOpt = new PromptDoubleOptions(
                    "\nВысота ступени (знак: + вверх, - вниз по ходу пикета)")
                {
                    DefaultValue = m.StepHeight == 0.0 ? 1.0 : m.StepHeight,
                    UseDefaultValue = true,
                    AllowNegative = true,
                    AllowZero = true
                };
                var hRes = ed.GetDouble(hOpt);
                if (hRes.Status != PromptStatus.OK) return;
                stepH = hRes.Value;
            }

            var gapOpt = new PromptDoubleOptions(
                $"\nМикроразрыв между участками ({ProfileGeometryOps.MinGap}..{ProfileGeometryOps.MaxGap})")
            {
                DefaultValue = m.Gap,
                UseDefaultValue = true,
                AllowNegative = false,
                AllowZero = false
            };
            var gapRes = ed.GetDouble(gapOpt);
            if (gapRes.Status != PromptStatus.OK) return;

            double gap = ProfileGeometryOps.SanitizeGap(gapRes.Value);
            if (Math.Abs(gap - gapRes.Value) > 1e-12)
                ed.WriteMessage($"\nМикроразрыв приведён к допустимому: {gap:F4}");

            // Конструкции соседних участков. «Предыдущий» и «следующий» —
            // по ходу пикетажа, то есть те же левый и правый, что у границы.
            string leftName, rightName;
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                Baseline bl = session.GetBaseline(tr, m);
                ProfileGeometryOps.DescribeRegions(bl, m, out leftName, out rightName);
                tr.Commit();
            }

            ObjectId leftAsm = AskAssembly(ed, $"предыдущего участка «{leftName}»");
            ObjectId rightAsm = AskAssembly(ed, $"следующего участка «{rightName}»");

            session.Manager.ApplyProperties(m.Id, isStep, stepH, gap, leftAsm, rightAsm);
            ed.WriteMessage("\nСвойства разрыва изменены, коридор перестроен.");
        }

        [CommandMethod("RW_SAVEBREAKS")]
        public void SaveBreaks() =>
            BreakSession.Current?.Store.SaveToDatabase(AcAp.DocumentManager.MdiActiveDocument.Database);

        // ------------------------------------------------------------------
        //  ДИАГНОСТИКА
        //  Печатает состояние связи, маркеров и фактические границы областей.
        //  Иначе разбирать поведение можно только перезапусками Civil 3D. Не удалять.
        // ------------------------------------------------------------------
        [CommandMethod("RW_BREAKDIAG")]
        public void Diagnose()
        {
            var doc = AcAp.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            var session = BreakSession.Current;
            if (session == null) { ed.WriteMessage("\n[RW_Break] Сессии нет."); return; }

            var link = session.ActiveLink;
            ed.WriteMessage("\n--- RW_Break ---");
            if (link == null)
                ed.WriteMessage("\nСвязь: НЕТ — нужен RW_LINKPROFILECORRIDOR");
            else
                ed.WriteMessage(
                    $"\nСвязь: профиль {link.ProfileHandle}, вид {link.ProfileViewHandle}, " +
                    $"ось {link.AlignmentHandle}, коридор {link.CorridorHandle}" +
                    $"\nРежим редактирования: {(session.IsEditMode(link.ProfileHandle) ? "ВКЛ" : "ВЫКЛ")}");

            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                foreach (var m in session.Store.All.OrderBy(x => x.Station))
                    ed.WriteMessage(
                        $"\nразрыв {m.Station:F3}  ступень={(m.IsStep ? m.StepHeight.ToString("F3") : "нет")}" +
                        $"  зазор={m.Gap:F4}  области {Short(m.LeftRegionId)}/{Short(m.RightRegionId)}");

                // Путь «правка в палитре → перестроение» в чертеже не проверялся.
                // Счётчики показывают, на каком шаге он обрывается: ноль событий —
                // палитра правит набор не через ObjectModified; события есть, а
                // опознанных нет — не сходится цепочка владения от набора к прокси.
                ed.WriteMessage(
                    $"\nпалитра: событий {BreakReactor.PropEvents}, " +
                    $"опознано {BreakReactor.PropMatched}, применено {BreakReactor.PropApplied}");

                if (link != null)
                {
                    Baseline bl = session.GetBaseline(tr, link);
                    if (bl == null)
                        ed.WriteMessage("\nБазовая линия коридора не найдена.");
                    else
                        foreach (BaselineRegion r in bl.BaselineRegions)
                            ed.WriteMessage($"\nобласть {Short(r.RegionGUID)} «{r.Name}» " +
                                            $"{r.StartStation:F4} .. {r.EndStation:F4}");
                }

                ReportOrphanProxies(tr, doc.Database, session, ed);
                tr.Commit();
            }
        }

        /// <summary>
        /// Линии с нашей XData, которые ни одному маркеру не принадлежат.
        /// Это копии прокси: они выглядят как разрывы, но модель о них не знает.
        /// Раньше такая копия работала как настоящая (удаление копии сносило
        /// разрыв), теперь — просто отрезок, и найти их можно только здесь.
        /// </summary>
        private static void ReportOrphanProxies(Transaction tr, Database db,
                                                BreakSession session, Editor ed)
        {
            var btr = (BlockTableRecord)tr.GetObject(
                SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);

            int orphans = 0;
            foreach (ObjectId id in btr)
            {
                if (id.IsErased) continue;

                var ln = tr.GetObject(id, OpenMode.ForRead) as Line;
                if (ln == null) continue;
                if (BreakProxyFactory.GetMarkerGuid(ln) == null) continue;
                if (session.Store.GetByProxy(id.Handle) != null) continue;

                orphans++;
                if (orphans <= 10)
                    ed.WriteMessage($"\nничей прокси: линия {id.Handle} (копия?)");
            }

            ed.WriteMessage(orphans == 0
                ? "\nЧужих прокси-линий нет."
                : $"\nВсего ничьих прокси-линий: {orphans}. Их можно просто стереть.");
        }

        private static string Short(Guid g) =>
            g == Guid.Empty ? "—" : g.ToString("N").Substring(0, 8);

        /// <summary>
        /// Спросить конструкцию участка. «Нет» означает «оставить как есть» —
        /// в 99 случаях из 100 менять надо одну сторону, а не обе.
        /// </summary>
        private static ObjectId AskAssembly(Editor ed, string what)
        {
            var kw = new PromptKeywordOptions($"\nМенять конструкцию {what}?");
            kw.Keywords.Add("Yes", "Да", "Да");
            kw.Keywords.Add("No", "Нет", "Нет");
            kw.Keywords.Default = "Нет";

            var res = ed.GetKeywords(kw);
            if (res.Status != PromptStatus.OK || res.StringResult != "Yes") return ObjectId.Null;

            var opt = new PromptEntityOptions("\nВыберите конструкцию (Assembly)");
            opt.SetRejectMessage("\nНужна Assembly");
            opt.AddAllowedClass(typeof(Assembly), true);

            var entRes = ed.GetEntity(opt);
            return entRes.Status == PromptStatus.OK ? entRes.ObjectId : ObjectId.Null;
        }

        private static ObjectId Resolve(Database db, Handle h) => RwHandles.Resolve(db, h);

        /// <summary>
        /// Набор меток профиля, который ничего не подписывает.
        ///
        /// В шаблонах Civil он обычно называется «_No Labels» / «Нет меток».
        /// Не нашли — отдаём ObjectId.Null: CreateByLayout принимает его как
        /// «без меток». Если и это не пройдёт, вызывающий откатится на первый
        /// набор из списка — метки лучше, чем несозданный профиль.
        /// </summary>
        private static ObjectId FindEmptyLabelSet(CivilDocument civDoc, Transaction tr)
        {
            try
            {
                foreach (ObjectId id in civDoc.Styles.LabelSetStyles.ProfileLabelSetStyles)
                {
                    var style = tr.GetObject(id, OpenMode.ForRead)
                                as Autodesk.Civil.DatabaseServices.Styles.StyleBase;
                    if (style == null) continue;

                    string name = (style.Name ?? string.Empty).ToLowerInvariant();
                    if (name.Contains("no label") || name.Contains("_no")
                        || name.Contains("нет мет") || name.Contains("без мет"))
                        return id;
                }
            }
            catch (System.Exception)
            {
                // Стилями заведует чертёж, а не мы: не нашли — не беда.
            }

            return ObjectId.Null;
        }
    }
}
