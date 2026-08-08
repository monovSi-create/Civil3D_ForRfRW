using System;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
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

            // 2) Отметка
            var elRes = ed.GetDouble("\nОтметка продольного профиля");
            if (elRes.Status != PromptStatus.OK) return;

            // 3) Новый коридор или существующий?
            var corrKw = new PromptKeywordOptions(
                "\nКоридор: создать новый или выбрать существующий?");
            corrKw.Keywords.Add("Создать");
            corrKw.Keywords.Add("Выбрать");
            corrKw.Keywords.Default = "Создать";
            var corrKwRes = ed.GetKeywords(corrKw);
            if (corrKwRes.Status != PromptStatus.OK) return;
            bool createNew = corrKwRes.StringResult == "Создать";

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
                asmKw.Keywords.Add("Да");
                asmKw.Keywords.Add("Нет");
                asmKw.Keywords.Default = "Нет";
                var asmKwRes = ed.GetKeywords(asmKw);
                if (asmKwRes.Status != PromptStatus.OK) return;

                if (asmKwRes.StringResult == "Да")
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
            // Транзакция 1: читаем данные ProfileView / Alignment
            // ------------------------------------------------------------------
            ObjectId alignmentId;
            Handle pvHandle, alHandle;
            double alStart, alEnd;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var pv = (ProfileView)tr.GetObject(pvRes.ObjectId, OpenMode.ForRead);
                alignmentId = pv.AlignmentId;
                pvHandle = pv.Handle;
                var al = (Alignment)tr.GetObject(alignmentId, OpenMode.ForRead);
                alHandle = al.Handle;
                alStart = al.StartingStation;
                alEnd = al.EndingStation;
                tr.Commit();
            }

            // ------------------------------------------------------------------
            // Транзакция 2: создаём профиль, коридор (если нужно), базовую линию
            // ------------------------------------------------------------------
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                // --- Стиль профиля и набор меток ---
                ObjectId styleId    = civDoc.Styles.ProfileStyles[0];
                ObjectId labelSetId = civDoc.Styles.LabelSetStyles
                                            .ProfileLabelSetStyles[0];

                // --- Создаём плоский профиль ---
                ObjectId profId = Profile.CreateByLayout(
                    "Профиль-основание_" + DateTime.Now.Ticks,
                    alignmentId, db.LayerZero,
                    styleId, labelSetId);

                var profile = (Profile)tr.GetObject(profId, OpenMode.ForWrite);
                profile.PVIs.AddPVI(alStart, elRes.Value);
                profile.PVIs.AddPVI(alEnd,   elRes.Value);

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
            stepKw.Keywords.Add("Да"); stepKw.Keywords.Add("Нет");
            stepKw.Keywords.Default = "Да";
            var stepRes = ed.GetKeywords(stepKw);
            if (stepRes.Status != PromptStatus.OK) return;
            bool isStep = stepRes.StringResult == "Да";

            double stepH = 0;
            if (isStep)
            {
                var hRes = ed.GetDouble("\nВысота ступени (знак: + вверх, - вниз по ходу пикета)");
                if (hRes.Status != PromptStatus.OK) return;
                stepH = hRes.Value;
            }

            var marker = new StationMarker
            {
                Station = station,
                IsStep = isStep,
                StepHeight = stepH,
                Layer = "0",                       // АДАПТ: слой по умолчанию
                ProfileHandle = link.ProfileHandle,
                ProfileViewHandle = link.ProfileViewHandle,
                AlignmentHandle = link.AlignmentHandle,
                CorridorHandle = link.CorridorHandle
            };

            Guid id = session.Manager.CreateBreak(marker);

            // Записать свойства в набор прокси (для палитры).
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var m = session.Store.Get(id);
                ObjectId psd = PropertySetSupport.EnsureMarkerPsd(doc.Database);
                foreach (var h in new[] { m.ProfileProxyHandle, m.PlanProxyHandle })
                {
                    ObjectId pid = Resolve(doc.Database, h);
                    if (pid.IsNull) continue;
                    PropertySetSupport.Attach(tr, pid, psd);
                    PropertySetSupport.WriteMarkerProps(tr, pid, m);
                }
                tr.Commit();
            }
            ed.WriteMessage($"\nРазрыв создан на пикете {station:F3}.");
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
                        $"  области {Short(m.LeftRegionId)}/{Short(m.RightRegionId)}");

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

        private static ObjectId Resolve(Database db, Handle h) =>
            (h.Value != 0 && db.TryGetObjectId(h, out ObjectId id)) ? id : ObjectId.Null;
    }
}
