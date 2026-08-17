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
                // не смертельно: любая команда FACINGWALL* включает их сама
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
            BreakSession session = BreakSession.Attach(doc);
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                PropertySetSupport.EnsureEditPsd(doc.Database);
                PropertySetSupport.EnsureMarkerPsd(doc.Database);

                // Слои оформления приводятся к режиму сразу при открытии: иначе
                // чертёж, сохранённый с включённым режимом, открывался бы
                // с незаблокированными границами.
                BreakOverlay.ApplyState(tr, doc.Database, session != null && session.AnyEditMode);

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

            // 2) Новый коридор или существующий?
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

            // 3) Имя нового коридора ИЛИ выбор существующего
            string corrName = null;
            ObjectId existingCorrId = ObjectId.Null;

            // Профиль существующей базовой линии. Не пуст — мастер ничего
            // не создаёт и об отметке не спрашивает.
            ObjectId existingProfileId = ObjectId.Null;
            string existingBaselineName = null;

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

                // У существующего коридора базовая линия уже построена по своему
                // профилю — берём его, вместо того чтобы городить второй.
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    if (!TryPickBaselineProfile(tr, ed, existingCorrId, alignmentId,
                                                out existingProfileId, out existingBaselineName))
                        existingProfileId = ObjectId.Null;
                    tr.Commit();
                }

                if (existingProfileId.IsNull)
                    ed.WriteMessage(
                        "\nУ этого коридора нет базовой линии по оси выбранного вида профиля." +
                        "\nБудет создан плоский профиль-основание и добавлена новая базовая линия.");
            }

            // 4) Отметка ПЛОСКОГО ПРОФИЛЯ-ОСНОВАНИЯ — только если его вообще
            //    нужно создавать. Коридор строится по нему, а разрывы потом
            //    режут в нём ступени, поэтому отметка задаёт исходный уровень
            //    всей стены. По умолчанию — середина вида, чтобы профиль
            //    заведомо оказался в видимой части.
            double baseElevation = Math.Round((elevLow + elevHigh) / 2.0, 3);

            if (existingProfileId.IsNull)
            {
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
            }

            // 5) Конструкция (Assembly) — только при создании нового коридора
            ObjectId assemblyId = ObjectId.Null;
            if (createNew)
            {
                var asmKw = new PromptKeywordOptions(
                    "\nЗадать конструкцию (Assembly) коридора?");
                asmKw.Keywords.Add("Yes", "Да", "Да");
                asmKw.Keywords.Add("No", "Нет", "Нет");
                // Default — ГЛОБАЛЬНОЕ имя. Русское здесь бросает eInvalidInput
                // («значение не попадает в ожидаемый диапазон») ещё до показа
                // запроса, и команда обрывается молча.
                asmKw.Keywords.Default = "No";
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
                ObjectId profId;
                Handle profHandle;

                if (!existingProfileId.IsNull)
                {
                    // --- Профиль уже есть: это профиль базовой линии коридора ---
                    profId = existingProfileId;
                    var existing = (Profile)tr.GetObject(profId, OpenMode.ForRead);
                    profHandle = existing.Handle;

                    ed.WriteMessage(
                        $"\nСвязь с существующей базовой линией «{existingBaselineName}»," +
                        $" профиль «{existing.Name}». Ничего не создавалось.");
                }
                else
                {
                    // --- Стиль профиля и набор меток ---
                    // Набор меток берётся пустой: иначе у каждого перелома профиля
                    // вырастает группа меток, а переломов здесь ровно столько,
                    // сколько разрывов, и подписывать их незачем.
                    ObjectId styleId = civDoc.Styles.ProfileStyles[0];
                    ObjectId labelSetId = FindEmptyLabelSet(civDoc, tr);

                    // --- Создаём плоский профиль ---
                    string profName = "Профиль-основание_" + DateTime.Now.Ticks;

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
                    profHandle = profile.Handle;
                }

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
                    corrId = existingCorrId;

                    // Базовую линию добавляем, только если своей подходящей
                    // у коридора не нашлось.
                    if (existingProfileId.IsNull)
                    {
                        var corridor = (Corridor)tr.GetObject(corrId, OpenMode.ForWrite);
                        corridor.Baselines.Add("Базовая линия", alignmentId, profId);
                        ed.WriteMessage("\nБазовая линия добавлена в существующий коридор.");
                    }
                }

                // --- Запоминаем связь ---
                // Связей в чертеже может быть несколько, в том числе на одном
                // виде профиля, поэтому новая ДОБАВЛЯЕТСЯ к существующим,
                // а не заменяет их.
                var link = new BreakLink
                {
                    ProfileHandle     = profHandle,
                    ProfileViewHandle = pvHandle,
                    AlignmentHandle   = alHandle,
                    CorridorHandle    = corrId.Handle
                };
                link.ColorIndex = BreakOverlay.RandomColor(link.Id);
                link.RefreshNames(tr, db);

                // Контроллер — объект, которым связь потом выбирают мышью.
                // Место под него считается по числу связей на этом же виде:
                // иначе второй лёг бы поверх первого.
                int slot = session.Links.Count(l => l.ProfileViewHandle == pvHandle);
                BreakController.Ensure(tr, db, link, slot);

                session.AddLink(link);
                session.ActiveLink = link;

                tr.Commit();
            }

            // Связи — в чертёж: после перезапуска Civil 3D они восстановятся сами,
            // и разрывы можно будет добавлять к ним, а не создавать всё заново.
            session.SaveLinks();

            // Концы коридора — такие же перемещаемые границы, как разрывы.
            session.Manager.EnsureTerminals(session.ActiveLink);
            session.RefreshOverlay();

            ed.WriteMessage(
                $"\nГотово, связь «{session.ActiveLink.Label}» создана." +
                "\nВыбрать связь и включить редактирование: RW_EDITLINKS");
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
            if (link == null)
            {
                doc.Editor.WriteMessage(session != null && session.Links.Count > 1
                    ? "\nСвязей в чертеже несколько — выберите нужную: RW_EDITLINKS"
                    : "\nСначала RW_LINKPROFILECORRIDOR");
                return;
            }

            bool now = !session.IsEditMode(link.ProfileHandle);
            session.SetEditMode(link.ProfileHandle, now);
            doc.Editor.WriteMessage(
                $"\nСвязь «{link.Label}», режим редактирования: {(now ? "ВКЛ" : "ВЫКЛ")}" +
                " (сохранён в чертеже, переживёт перезапуск)");
            doc.Editor.Regen();
        }

        // ------------------------------------------------------------------
        //  РЕДАКТИРОВАНИЕ СВЯЗЕЙ: ВЫБОР КОНТРОЛЛЕРА
        //
        //  Связей в чертеже может быть много, и командной строкой их не
        //  различить — «профиль такой-то» пользователю ничего не говорит.
        //  Поэтому у каждой связи есть контроллер: вставка рядом с видом
        //  профиля с подписью «коридор-профиль» и своим цветом. Выбрали
        //  контроллер — эта связь стала активной и открылась для правки:
        //  границы участков разблокировались, появились заливки и надписи.
        // ------------------------------------------------------------------
        [CommandMethod("RW_EDITLINKS")]
        public void EditLinks()
        {
            var doc = AcAp.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            var db = doc.Database;
            var session = BreakSession.Current;

            if (session == null) { ed.WriteMessage("\nСессии нет."); return; }
            if (session.Links.Count == 0)
            {
                ed.WriteMessage("\nСвязей в чертеже нет — начните с RW_LINKPROFILECORRIDOR.");
                return;
            }

            // Контроллеров может не быть: связь могла прийти из чертежа прежней
            // версии или быть восстановленной по самим разрывам. Выбирать
            // нечего — значит, сначала создаём. Заодно заводим концевые границы:
            // в чертежах прежних версий их тоже нет.
            EnsureControllers(doc, session);

            int terminals = 0;
            foreach (BreakLink l in session.Links.ToList())
                terminals += session.Manager.EnsureTerminals(l);

            if (terminals > 0)
                ed.WriteMessage($"\nДобавлено концевых границ коридора: {terminals}.");

            ed.WriteMessage("\nСвязи чертежа:");
            foreach (BreakLink l in session.Links)
                ed.WriteMessage(
                    $"\n  {l.Label}{(session.IsEditMode(l.ProfileHandle) ? "  [правится]" : string.Empty)}");

            var opt = new PromptEntityOptions(
                "\nВыберите контроллер связи (вставка с подписью «коридор-профиль»)");
            opt.SetRejectMessage("\nНужен контроллер связи");
            opt.AddAllowedClass(typeof(BlockReference), false);
            // Латиницей только глобальное имя: второй и третий параметры обязаны
            // быть тем самым русским словом, которое показано пользователю.
            opt.Keywords.Add("Off", "Выключить", "Выключить");
            opt.AppendKeywordsToMessage = true;

            var res = ed.GetEntity(opt);

            if (res.Status == PromptStatus.Keyword && res.StringResult == "Off")
            {
                TurnOffAll(session);
                ed.WriteMessage(
                    "\nРедактирование выключено у всех связей." +
                    "\nВидны границы участков и имена конструкций; границы заблокированы.");
                ed.Regen();
                return;
            }

            if (res.Status != PromptStatus.OK) return;

            BreakLink link = FindLinkByController(session, db, res.ObjectId);
            if (link == null)
            {
                ed.WriteMessage(
                    "\nЭта вставка в модели не числится контроллером связи." +
                    "\nЕсли это копия контроллера — удалите её: копия ничем не управляет.");
                return;
            }

            // Правится всегда одна связь: слои общие на чертёж, и держать
            // разблокированными границы сразу нескольких коридоров значит
            // потерять саму защиту от случайного смещения.
            foreach (BreakLink other in session.Links.ToList())
                if (other != link && session.IsEditMode(other.ProfileHandle))
                    session.SetEditMode(other.ProfileHandle, false);

            session.ActiveLink = link;
            session.SetEditMode(link.ProfileHandle, true);

            ed.WriteMessage(
                $"\nСвязь «{link.Label}» открыта для правки: границы разблокированы," +
                " показаны участки и подписи." +
                "\nВыключить: RW_EDITMODE или RW_EDITLINKS → Выключить.");
            ed.Regen();
        }

        /// <summary>
        /// Создать контроллеры связям, у которых их ещё нет, и обновить подписи
        /// у остальных: имена коридора и профиля пользователь мог поменять.
        /// </summary>
        private static void EnsureControllers(Document doc, BreakSession session)
        {
            try
            {
                using (doc.LockDocument())
                using (session.Suspend())
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    var db = doc.Database;
                    BreakOverlay.Unlock(tr, db);

                    var slots = new System.Collections.Generic.Dictionary<long, int>();

                    foreach (BreakLink link in session.Links)
                    {
                        long key = link.ProfileViewHandle.Value;
                        int slot;
                        slots.TryGetValue(key, out slot);
                        slots[key] = slot + 1;

                        link.RefreshNames(tr, db);
                        if (link.ColorIndex <= 0) link.ColorIndex = BreakOverlay.RandomColor(link.Id);

                        BreakController.Ensure(tr, db, link, slot);
                    }

                    BreakOverlay.ApplyState(tr, db, session.AnyEditMode);
                    tr.Commit();
                }

                session.SaveLinks();
            }
            catch (System.Exception)
            {
                // Без контроллера связь всё равно работает — выбрать её нельзя,
                // но команды с активной связью действуют по-прежнему.
            }
        }

        private static void TurnOffAll(BreakSession session)
        {
            foreach (BreakLink link in session.Links.ToList())
                if (session.IsEditMode(link.ProfileHandle))
                    session.SetEditMode(link.ProfileHandle, false);

            // Ни одна связь не правится — оформление всё равно перестраиваем:
            // надписи конструкций остаются видны и обязаны быть свежими.
            session.RefreshOverlay();
        }

        /// <summary>
        /// Связь по выбранной вставке.
        ///
        /// Guid из XData — только предварительный фильтр: у КОПИИ контроллера
        /// он ровно тот же, и по нему копия управляла бы настоящей связью.
        /// Владение проверяется по хэндлу, записанному в самой связи, — тем же
        /// правилом, что и у прокси-линий.
        /// </summary>
        private static BreakLink FindLinkByController(BreakSession session, Database db, ObjectId id)
        {
            if (id.IsNull) return null;

            Guid? tagged = null;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Autodesk.AutoCAD.DatabaseServices.Entity;
                if (ent != null) tagged = BreakController.GetLinkId(ent);
                tr.Commit();
            }

            if (tagged == null) return null;

            BreakLink link = session.FindLink(tagged.Value);
            return link != null && link.ControllerHandle == id.Handle ? link : null;
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
            if (link == null)
            {
                // Активной связи нет по двум разным причинам, и советы у них разные.
                ed.WriteMessage(session != null && session.Links.Count > 1
                    ? "\nСвязей в чертеже несколько — выберите нужную: RW_EDITLINKS"
                    : "\nСначала RW_LINKPROFILECORRIDOR");
                return;
            }

            ed.WriteMessage($"\nСвязь: «{link.Label}».");

            // Точка в виде профиля ИЛИ в плане -> пикет.
            var ptRes = ed.GetPoint("\nУкажите положение разрыва (в виде профиля или в плане)");
            if (ptRes.Status != PromptStatus.OK) return;

            double station;
            string source;

            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var db = doc.Database;
                var pv = RwHandles.Open<ProfileView>(tr, db, link.ProfileViewHandle, OpenMode.ForRead);
                var al = RwHandles.Open<Alignment>(tr, db, link.AlignmentHandle, OpenMode.ForRead);

                if (RwGeometry.TryStationInProfileView(pv, ptRes.Value, out station))
                    source = "вид профиля";
                else if (RwGeometry.TryStationOnAlignment(al, ptRes.Value, out station))
                    source = "план";
                else
                    source = null;

                tr.Commit();
            }

            if (source == null)
            {
                // Раньше пикет в этом случае получался NaN и доходил до AddPVI,
                // где Civil бросал «значение не попадает в ожидаемый диапазон».
                ed.WriteMessage(
                    "\nТочка не попала ни в вид профиля, ни на трассу — разрыв не создан." +
                    "\nЩёлкайте внутри сетки вида профиля либо рядом с осью в плане.");
                return;
            }

            ed.WriteMessage($"\nПикет {station:F3} (источник: {source}).");

            // Ступень?
            var stepKw = new PromptKeywordOptions("\nЭто ступень профиля?");
            stepKw.Keywords.Add("Yes", "Да", "Да");
            stepKw.Keywords.Add("No", "Нет", "Нет");
            stepKw.Keywords.Default = "Yes";   // глобальное имя, не подпись
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

            // Разрыв обязан попасть внутрь и профиля, и участка коридора: ступень
            // ставится парой PVI, и за пределами профиля Civil их не принимает.
            // Профиль существующего коридора бывает короче вида — тогда часть
            // вида для разрывов недоступна, и сказать об этом надо до, а не после.
            double lo, hi;
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                bool ok = TryBreakRange(tr, doc.Database, session, marker,
                                        marker.HalfGap + BreakGripOverrule.Buffer, out lo, out hi);
                tr.Commit();

                if (!ok)
                {
                    ed.WriteMessage("\nНе удалось определить пределы профиля и коридора — разрыв не создан.");
                    return;
                }
            }

            if (station < lo || station > hi)
            {
                ed.WriteMessage(
                    $"\nПикет {station:F3} вне допустимого диапазона {lo:F3}..{hi:F3}" +
                    " (пересечение профиля и участка коридора) — разрыв не создан.");
                return;
            }

            try
            {
                // Набор характеристик на прокси пишет сам оркестратор — и при
                // создании, и после каждой правки, иначе палитра врёт.
                session.Manager.CreateBreak(marker);
            }
            catch (System.Exception ex)
            {
                // Лучше внятная строка, чем окно «необработанное исключение»:
                // оно к тому же оставляет сеанс в непонятном состоянии.
                ed.WriteMessage($"\nРазрыв создать не удалось: {ex.Message}");
                return;
            }

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
            var session = BreakSession.Current;
            if (session == null) return;

            using (new BoundaryUnlock(doc, session)) DeleteBreakCore(doc, session);
        }

        private static void DeleteBreakCore(Document doc, BreakSession session)
        {
            var ed = doc.Editor;

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

            // Конец коридора удалять нечего: он не разделяет пару участков,
            // а держит внешний край крайнего. Убрать его значит остаться
            // без ручки, которой этот край двигают.
            StationMarker target = session.Store.Get(id.Value);
            if (target != null && target.IsTerminal)
            {
                ed.WriteMessage(
                    "\nЭто концевая граница коридора, а не разрыв — удалить нельзя." +
                    "\nЕё можно только перетащить: она задаёт край крайнего участка.");
                return;
            }

            session.Manager.DeleteBreak(id.Value);
            ed.WriteMessage("\nРазрыв удалён.");
        }

        // ------------------------------------------------------------------
        //  ПЕРЕНЕСТИ ГРАНИЦУ УЧАСТКОВ (jig)
        //
        //  Ручка в плане двигает линию как умеет AutoCAD, а положение границы
        //  видно только после отпускания. Здесь наоборот: пока не щёлкнули,
        //  показывается будущая граница — отрезок от курсора до его проекции
        //  на ось. Проекция идёт по нормали, поэтому отрезок перпендикулярен
        //  оси сам собой, а его конец скользит по ней.
        // ------------------------------------------------------------------
        [CommandMethod("RW_MOVEBREAK")]
        public void MoveBreak()
        {
            var doc = AcAp.DocumentManager.MdiActiveDocument;
            var session = BreakSession.Current;
            if (session == null) return;

            using (new BoundaryUnlock(doc, session)) MoveBreakCore(doc, session);
        }

        private static void MoveBreakCore(Document doc, BreakSession session)
        {
            var ed = doc.Editor;

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

            double station;
            bool accepted = false;

            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var al = RwHandles.Open<Alignment>(
                    tr, doc.Database, m.AlignmentHandle, OpenMode.ForRead);
                var pv = RwHandles.Open<ProfileView>(
                    tr, doc.Database, m.ProfileViewHandle, OpenMode.ForRead);

                double lo, hi;
                // Конец коридора доходит до самого края — отступа у него нет.
                double margin = m.IsTerminal ? 0.0 : m.HalfGap + BreakGripOverrule.Buffer;

                if (al == null || !TryBreakRange(tr, doc.Database, session, m, margin, out lo, out hi))
                {
                    ed.WriteMessage("\nНе удалось определить ось или пределы участка.");
                    tr.Commit();
                    return;
                }

                if (pv == null)
                    ed.WriteMessage("\nВид профиля не найден — вести можно только в плане.");

                // Соседние разрывы тоже держат границу — те же пределы, что
                // и при перетаскивании ручки.
                var bounds = session.Store.GetMoveBounds(m, BreakGripOverrule.Buffer, lo, hi);
                lo = Math.Max(lo, bounds.min);
                hi = Math.Min(hi, bounds.max);

                // Виды равноправны: куда увели курсор, в том и ведём.
                var jig = new BreakBoundaryJig(al, pv, lo, hi, m.Station);
                PromptResult jigRes = ed.Drag(jig);

                accepted = jigRes.Status == PromptStatus.OK && jig.HasStation;
                station = jig.Station;

                tr.Commit();
            }

            if (!accepted)
            {
                ed.WriteMessage("\nПеренос отменён.");
                return;
            }

            session.Manager.ApplyStationChange(m.Id, station);
            ed.WriteMessage($"\nГраница участков перенесена на пикет {station:F3}.");
        }

        /// <summary>
        /// Временное представление будущей границы: отрезок «курсор → его
        /// проекция на ось». Точка, за которую держится пользователь, идёт
        /// за курсором; вторая скользит по оси, оставаясь основанием
        /// перпендикуляра, — проекция на трассу и есть нормаль.
        /// </summary>
        /// <summary>
        /// Виды равноправны: тянуть границу можно и в плане, и в виде профиля,
        /// независимо от того, какой прокси выбран. Режим определяется на каждом
        /// движении мыши — по тому, попал курсор в габариты вида профиля или нет,
        /// — а показываются сразу оба представления будущей границы.
        /// </summary>
        private class BreakBoundaryJig : DrawJig
        {
            private readonly Alignment _alignment;
            private readonly ProfileView _view;

            private readonly double _lo;
            private readonly double _hi;

            // Габариты вида: по ним и распознаётся «курсор внутри вида»,
            // и рисуется вертикаль во всю его высоту.
            private readonly bool _hasBox;
            private readonly double _xLo, _xHi, _yLo, _yHi;
            private readonly double _midElevation;

            private Point3d _cursor;
            private bool _valid;
            private bool _inView;

            // Что рисуем: представление в каждом виде плюс «резинка» к курсору.
            private Point3d _profA, _profB;
            private Point3d _planA, _planB;
            private Point3d _rubberA, _rubberB;
            private bool _hasProf, _hasPlan, _hasRubber;

            public double Station { get; private set; }
            public bool HasStation { get { return _valid; } }

            public BreakBoundaryJig(Alignment alignment, ProfileView view,
                                    double lo, double hi, double startStation)
            {
                _alignment = alignment;
                _view = view;
                _lo = lo;
                _hi = hi;
                Station = startStation;

                if (view == null) return;

                _midElevation = (view.ElevationMin + view.ElevationMax) / 2.0;

                try
                {
                    Extents3d ext = view.GeometricExtents;
                    _xLo = Math.Min(ext.MinPoint.X, ext.MaxPoint.X);
                    _xHi = Math.Max(ext.MinPoint.X, ext.MaxPoint.X);
                    _yLo = Math.Min(ext.MinPoint.Y, ext.MaxPoint.Y);
                    _yHi = Math.Max(ext.MinPoint.Y, ext.MaxPoint.Y);
                    _hasBox = _xHi - _xLo > 1e-6 && _yHi - _yLo > 1e-6;
                }
                catch (System.Exception)
                {
                    _hasBox = false;
                }
            }

            protected override SamplerStatus Sampler(JigPrompts prompts)
            {
                var opt = new JigPromptPointOptions(
                    "\nНовое положение границы участков — ведите в плане или в виде профиля" +
                    " (щелчок — подтвердить)");
                opt.UserInputControls =
                    UserInputControls.Accept3dCoordinates |
                    UserInputControls.NoZeroResponseAccepted;

                PromptPointResult res = prompts.AcquirePoint(opt);
                if (res.Status != PromptStatus.OK) return SamplerStatus.Cancel;

                // Без этого jig крутится вхолостую на каждом движении мыши.
                if (res.Value.DistanceTo(_cursor) < 1e-8) return SamplerStatus.NoChange;

                _cursor = res.Value;
                _inView = InsideView(_cursor);

                double station;
                bool ok = _inView
                    ? RwGeometry.TryStationInProfileView(_view, _cursor, out station)
                    : RwGeometry.TryStationOnAlignment(_alignment, _cursor, out station);

                if (ok)
                {
                    Station = RwGeometry.Clamp(station, _lo, _hi);
                    BuildPreview();
                }

                _valid = ok && (_hasProf || _hasPlan);
                return SamplerStatus.OK;
            }

            /// <summary>Курсор в габаритах вида профиля — значит ведём по профилю.</summary>
            private bool InsideView(Point3d p)
            {
                if (!_hasBox) return false;
                return p.X >= _xLo && p.X <= _xHi && p.Y >= _yLo && p.Y <= _yHi;
            }

            /// <summary>
            /// Оба представления строятся из одного пикета, поэтому разъехаться
            /// не могут: подвели курсор в плане — вертикаль в профиле уже там же.
            /// </summary>
            private void BuildPreview()
            {
                _hasProf = false;
                _hasPlan = false;
                _hasRubber = false;

                if (_view != null && _hasBox)
                {
                    Point3d anchor;
                    if (RwGeometry.TryPointInProfileView(_view, Station, _midElevation, out anchor))
                    {
                        _profA = new Point3d(anchor.X, _yLo, 0.0);
                        _profB = new Point3d(anchor.X, _yHi, 0.0);
                        _hasProf = true;
                    }
                }

                if (_alignment != null)
                {
                    Point3d[] pts = BreakProxyFactory.PlanPoints(_alignment, Station);
                    if (pts != null)
                    {
                        _planA = pts[0];
                        _planB = pts[1];
                        _hasPlan = true;

                        // В плане «резинка» от оси к курсору показывает, что
                        // именно сносится на ось. В виде профиля она не нужна:
                        // там курсор и так стоит на будущей границе.
                        if (!_inView)
                        {
                            _rubberA = pts[0];
                            _rubberB = _cursor;
                            _hasRubber = true;
                        }
                    }
                }
            }

            protected override bool WorldDraw(Autodesk.AutoCAD.GraphicsInterface.WorldDraw draw)
            {
                // Курсор увели туда, где пикет не определяется — показывать
                // нечего, но jig продолжает работать: вернётся, и картинка появится.
                if (!_valid) return true;

                if (_hasProf) draw.Geometry.WorldLine(_profA, _profB);
                if (_hasPlan) draw.Geometry.WorldLine(_planA, _planB);
                if (_hasRubber) draw.Geometry.WorldLine(_rubberA, _rubberB);

                return true;
            }
        }

        // ------------------------------------------------------------------
        //  ИЗМЕНИТЬ СВОЙСТВА РАЗРЫВА
        //  До этого свойства задавались только при создании и потом не менялись.
        // ------------------------------------------------------------------
        [CommandMethod("RW_EDITBREAK")]
        public void EditBreak()
        {
            var doc = AcAp.DocumentManager.MdiActiveDocument;
            var session = BreakSession.Current;
            if (session == null) return;

            using (new BoundaryUnlock(doc, session)) EditBreakCore(doc, session);
        }

        private static void EditBreakCore(Document doc, BreakSession session)
        {
            var ed = doc.Editor;

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

            // У конца коридора нет ни ступени, ни микроразрыва — спрашивать
            // о них нечего, а участок рядом только один.
            if (m.IsTerminal) { EditTerminalCore(doc, session, m); return; }

            ed.WriteMessage(
                $"\nРазрыв на пикете {m.Station:F3}: ступень={(m.IsStep ? m.StepHeight.ToString("F3") : "нет")}, " +
                $"микроразрыв={m.Gap:F4}");

            // Ключевые слова латиницей с русскими подписями: кириллицу
            // не ввести при английской раскладке.
            var stepKw = new PromptKeywordOptions("\nСтупень профиля?");
            stepKw.Keywords.Add("Yes", "Да", "Да");
            stepKw.Keywords.Add("No", "Нет", "Нет");
            stepKw.Keywords.Default = m.IsStep ? "Yes" : "No";   // глобальное имя
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

        /// <summary>
        /// Правка концевой границы коридора. Менять здесь можно ровно одно —
        /// конструкцию того единственного участка, который к ней примыкает.
        /// Пикет правится ручкой или RW_MOVEBREAK, как и у разрыва.
        /// </summary>
        private static void EditTerminalCore(Document doc, BreakSession session, StationMarker m)
        {
            var ed = doc.Editor;
            bool isStart = m.Role == StationMarker.MarkerRole.Start;

            string regionName;
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                Baseline bl = session.GetBaseline(tr, m);
                string left, right;
                ProfileGeometryOps.DescribeRegions(bl, m, out left, out right);
                regionName = isStart ? right : left;
                tr.Commit();
            }

            ed.WriteMessage(
                $"\n{(isStart ? "Начало" : "Конец")} коридора на пикете {m.Station:F3}" +
                $" ({BreakOverlay.FormatStation(m.Station)}), участок «{regionName}»." +
                "\nСтупени и микроразрыва у конца коридора нет; пикет правится ручкой или RW_MOVEBREAK.");

            ObjectId assembly = AskAssembly(ed, $"участка «{regionName}»");
            if (assembly.IsNull) { ed.WriteMessage("\nНичего не изменено."); return; }

            // Конструкция назначается той стороне, с которой участок и лежит.
            session.Manager.ApplyProperties(
                m.Id, false, 0.0, m.Gap,
                isStart ? ObjectId.Null : assembly,
                isStart ? assembly : ObjectId.Null);

            ed.WriteMessage("\nКонструкция участка изменена, коридор перестроен.");
        }

        // ------------------------------------------------------------------
        //  ПЕРЕСТРОИТЬ ПРОКСИ ВСЕХ РАЗРЫВОВ
        //
        //  Нужна для чертежей, где прокси уже созданы прежней версией: там
        //  профильная линия могла остаться короткой или вовсе вырожденной.
        //  Модель не трогается — только геометрия линий.
        // ------------------------------------------------------------------
        [CommandMethod("RW_REFRESHBREAKS")]
        public void RefreshBreaks()
        {
            var doc = AcAp.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            var session = BreakSession.Current;
            if (session == null) { ed.WriteMessage("\nСессии нет."); return; }

            // Чертежи прежних версий концевых границ не содержат — заводим.
            int terminals = 0;
            foreach (BreakLink l in session.Links.ToList())
                terminals += session.Manager.EnsureTerminals(l);

            int done = 0, failed = 0;

            using (doc.LockDocument())
            using (session.Suspend())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var db = doc.Database;
                BreakOverlay.Unlock(tr, db);

                foreach (var m in session.Store.All)
                {
                    var pv = RwHandles.Open<ProfileView>(tr, db, m.ProfileViewHandle, OpenMode.ForRead);
                    var al = RwHandles.Open<Alignment>(tr, db, m.AlignmentHandle, OpenMode.ForRead);

                    if (pv == null && al == null) { failed++; continue; }

                    try
                    {
                        BreakProxyFactory.UpdateProxyGeometry(tr, m, pv, al);

                        // Прокси из чертежей прежней версии лежат на слое «0»
                        // со штрихпунктиром — переводим их на слой границ.
                        BreakOverlay.AdoptProxy(tr, db, m.ProfileProxyHandle);
                        BreakOverlay.AdoptProxy(tr, db, m.PlanProxyHandle);

                        BreakProxyFactory.BringToFront(
                            tr, db, RwHandles.Resolve(db, m.ProfileProxyHandle));
                        BreakProxyFactory.BringToFront(
                            tr, db, RwHandles.Resolve(db, m.PlanProxyHandle));

                        done++;
                    }
                    catch (System.Exception)
                    {
                        failed++;
                    }
                }

                // Оформление тоже производное — перестраиваем заодно.
                BreakOverlay.RebuildAll(tr, db, session);

                tr.Commit();
            }

            session.SaveLinks();

            ed.WriteMessage(
                $"\nПрокси перестроены: {done}, не удалось: {failed}" +
                $"{(terminals > 0 ? $", концевых границ добавлено: {terminals}" : string.Empty)}.");
            ed.Regen();
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

            if (session.Links.Count == 0)
                ed.WriteMessage("\nСвязей нет — нужен RW_LINKPROFILECORRIDOR");

            foreach (BreakLink l in session.Links)
            {
                bool active = ReferenceEquals(l, link);
                ObjectId ctrl = RwHandles.Resolve(doc.Database, l.ControllerHandle);

                ed.WriteMessage(
                    $"\nсвязь «{l.Label}»{(active ? "  [активная]" : string.Empty)}" +
                    $"\n    профиль {l.ProfileHandle}, вид {l.ProfileViewHandle}, " +
                    $"ось {l.AlignmentHandle}, коридор {l.CorridorHandle}" +
                    $"\n    контроллер: {(ctrl.IsNull ? "НЕТ (RW_EDITLINKS создаст)" : ctrl.Handle.ToString())}" +
                    $", цвет {BreakController.ColorOf(l)}, оформление: {l.OverlayHandles.Count} об." +
                    $"\n    режим редактирования: {(session.IsEditMode(l.ProfileHandle) ? "ВКЛ" : "ВЫКЛ")}");
            }

            ed.WriteMessage(
                $"\nслои: границы {(session.AnyEditMode ? "разблокированы" : "заблокированы")}, " +
                $"заливки и подписи пикетов {(session.AnyEditMode ? "видны" : "выключены")}");

            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                // Высота вида двумя мерками: по диапазону отметок и по фактическим
                // габаритам. Расхождение и объясняет, почему вертикаль, построенная
                // по отметкам, выглядит короче самого вида.
                if (link != null)
                {
                    var pv = RwHandles.Open<ProfileView>(
                        tr, doc.Database, link.ProfileViewHandle, OpenMode.ForRead);

                    if (pv != null)
                    {
                        string extents;
                        try
                        {
                            Extents3d e = pv.GeometricExtents;
                            extents = $"{Math.Abs(e.MaxPoint.Y - e.MinPoint.Y):F3}";
                        }
                        catch (System.Exception) { extents = "нет"; }

                        ed.WriteMessage(
                            $"\nвид: отметки {pv.ElevationMin:F3}..{pv.ElevationMax:F3}" +
                            $" (диапазон {pv.ElevationMax - pv.ElevationMin:F3}), габарит по Y {extents}");
                    }
                }

                foreach (var m in session.Store.All.OrderBy(x => x.Station))
                {
                    ed.WriteMessage(
                        $"\nразрыв {m.Station:F3}  ступень={(m.IsStep ? m.StepHeight.ToString("F3") : "нет")}" +
                        $"  зазор={m.Gap:F4}  области {Short(m.LeftRegionId)}/{Short(m.RightRegionId)}");

                    // Длины прокси: ноль означает вырожденную линию — её не видно
                    // и не выбрать, хотя объект в чертеже есть.
                    ed.WriteMessage(
                        $"\n    прокси: профиль {LineInfo(tr, doc.Database, m.ProfileProxyHandle)}," +
                        $" план {LineInfo(tr, doc.Database, m.PlanProxyHandle)}");
                }

                // Путь «правка в палитре → перестроение» в чертеже не проверялся.
                // Счётчики показывают, на каком шаге он обрывается: ноль событий —
                // палитра правит набор не через ObjectModified; события есть, а
                // опознанных нет — не сходится цепочка владения от набора к прокси.
                ed.WriteMessage(
                    $"\nпалитра (разрыв): событий {BreakReactor.PropEvents}, " +
                    $"опознано {BreakReactor.PropMatched}, применено {BreakReactor.PropApplied}" +
                    $"\nпалитра (режим): опознано {BreakReactor.PropEditMatched}, " +
                    $"применено {BreakReactor.PropEditApplied}");

                // Прямоугольники заливок числами: если полосы выглядят не полосами,
                // отсюда видно, врёт построение штриховки или пересчёт пикета
                // в координаты вида.
                if (link != null)
                    foreach (string line in BreakOverlay.DescribeFills(tr, doc.Database, session, link))
                        ed.WriteMessage("\nзаливка: " + line);

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

        /// <summary>Есть ли прокси и какой он длины — вырожденный виден сразу.</summary>
        private static string LineInfo(Transaction tr, Database db, Handle h)
        {
            var ln = RwHandles.Open<Line>(tr, db, h, OpenMode.ForRead);
            if (ln == null) return "НЕТ";

            double len = ln.StartPoint.DistanceTo(ln.EndPoint);
            return len < 1e-9
                ? $"{h} ВЫРОЖДЕН (длина 0)"
                : $"{h} длина {len:F3}";
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
            kw.Keywords.Default = "No";   // глобальное имя

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
        /// Снять защиту с границ на время команды и вернуть её в конце.
        ///
        /// Вне режима редактирования слой границ заблокирован — в этом и смысл
        /// защиты. Но заблокированный объект нельзя и ВЫБРАТЬ, а команды правки
        /// разрыва начинаются именно с выбора прокси: без этого стража они
        /// молча отказывались бы работать всюду, кроме режима редактирования.
        ///
        /// Случайного смещения это не открывает: команду надо ввести осознанно,
        /// а защита возвращается даже при отмене по Esc — за это отвечает
        /// <c>using</c> у вызывающего.
        /// </summary>
        private sealed class BoundaryUnlock : IDisposable
        {
            private readonly Document _doc;
            private readonly BreakSession _session;

            public BoundaryUnlock(Document doc, BreakSession session)
            {
                _doc = doc;
                _session = session;
                Apply(true);
            }

            public void Dispose() => Apply(false);

            private void Apply(bool unlock)
            {
                try
                {
                    using (_doc.LockDocument())
                    using (_session.Suspend())
                    using (var tr = _doc.Database.TransactionManager.StartTransaction())
                    {
                        if (unlock) BreakOverlay.Unlock(tr, _doc.Database);
                        else BreakOverlay.ApplyState(tr, _doc.Database, _session.AnyEditMode);

                        tr.Commit();
                    }
                }
                catch (System.Exception)
                {
                    // Не вышло — команда всё равно должна попробовать отработать.
                }
            }
        }

        /// <summary>
        /// Пикеты, в которых граница вообще возможна: пересечение профиля
        /// и участка коридора, ужатое на <paramref name="margin"/> с каждой стороны.
        ///
        /// У разрыва отступ есть — пара PVI ступени должна поместиться внутрь
        /// профиля, — а у концевой границы он нулевой: ей положено доходить
        /// ровно до края коридора, там ступени нет и упираться не во что.
        /// </summary>
        private static bool TryBreakRange(Transaction tr, Database db, BreakSession session,
                                          IBreakTarget target, double margin,
                                          out double lo, out double hi)
        {
            lo = 0.0;
            hi = 0.0;

            var profile = RwHandles.Open<Profile>(tr, db, target.ProfileHandle, OpenMode.ForRead);
            if (profile == null) return false;

            lo = profile.StartingStation;
            hi = profile.EndingStation;

            Baseline bl = session.GetBaseline(tr, target);
            if (bl != null)
            {
                lo = Math.Max(lo, bl.StartStation);
                hi = Math.Min(hi, bl.EndStation);
            }

            lo += margin;
            hi -= margin;

            return hi > lo;
        }

        /// <summary>
        /// Профиль базовой линии существующего коридора, построенной по той же
        /// оси, что и выбранный вид профиля.
        ///
        /// Именно он и есть «профиль коридора»: создавать рядом второй, плоский,
        /// незачем — коридор уже стоит на этом. Если подходящих базовых линий
        /// несколько, спрашиваем номер: выбрать не ту значит резать не тот
        /// коридор.
        /// </summary>
        private static bool TryPickBaselineProfile(
            Transaction tr, Editor ed, ObjectId corridorId, ObjectId alignmentId,
            out ObjectId profileId, out string baselineName)
        {
            profileId = ObjectId.Null;
            baselineName = null;

            var corridor = tr.GetObject(corridorId, OpenMode.ForRead) as Corridor;
            if (corridor == null) return false;

            var names = new System.Collections.Generic.List<string>();
            var profiles = new System.Collections.Generic.List<ObjectId>();

            foreach (Baseline bl in corridor.Baselines)
            {
                if (bl.AlignmentId != alignmentId) continue;
                if (bl.ProfileId.IsNull) continue;   // базовая линия по отметкам, без профиля

                names.Add(bl.Name);
                profiles.Add(bl.ProfileId);
            }

            if (profiles.Count == 0) return false;

            int index = 0;

            if (profiles.Count > 1)
            {
                ed.WriteMessage("\nБазовые линии коридора по этой оси:");
                for (int i = 0; i < names.Count; i++)
                    ed.WriteMessage($"\n  {i + 1}. {names[i]}");

                var opt = new PromptIntegerOptions("\nНомер базовой линии")
                {
                    DefaultValue = 1,
                    UseDefaultValue = true,
                    LowerLimit = 1,
                    UpperLimit = profiles.Count
                };

                var res = ed.GetInteger(opt);
                if (res.Status != PromptStatus.OK) return false;
                index = res.Value - 1;
            }

            profileId = profiles[index];
            baselineName = names[index];
            return true;
        }

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
