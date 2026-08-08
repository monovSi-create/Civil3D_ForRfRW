using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.DatabaseServices;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
// Entity и ObjectId есть и в AutoCAD-, и в Civil-пространстве имён — снимаем неоднозначность.
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;
using ObjectId = Autodesk.AutoCAD.DatabaseServices.ObjectId;

namespace Civil3D_commands.FaceArr
{
    /// <summary>
    /// Проецирование блоков массива на вид профиля ШТАТНЫМ инструментом Civil 3D.
    ///
    /// Почему так криво. В Civil 3D 2024 создание проекций не выведено ни в один
    /// API — проверено рефлексией по библиотекам:
    ///   * у ProfileView нет ни одного метода про проекцию;
    ///   * у ProfileProjection нет ни конструктора, ни фабрики — только чтение;
    ///   * ProjectionUtil умеет лишь IsProfileProjectionObject (проверку);
    ///   * в COM-интерфейсе Autodesk.AECC.Interop.Roadway проекций нет вовсе.
    /// Остаётся команда PROJECTOBJECTSTOPROF, а она интерактивна и открывает
    /// модальное окно. Поэтому: предвыбираем блоки, отдаём команду пользователю
    /// и ждём её завершения.
    ///
    /// Готовая проекция не рассказывает, из чего сделана: у ProfileProjection нет
    /// свойства с исходным объектом. Узнать «наши» проекции можно только одним
    /// способом — запомнить состав чертежа до команды и вычесть его из состава
    /// после. Этим здесь и занимаемся.
    ///
    /// ВАЖНО: проекции ассоциативны, а массив при каждом перестроении создаёт
    /// блоки заново. Поэтому после любого перестроения проекции протухают, их
    /// стирают, и команду нужно повторить. Обойти это нельзя.
    /// </summary>
    public static class FacingWallCivilProjection
    {
        /// <summary>
        /// Команда Civil 3D «Спроецировать объекты на вид профиля».
        /// Имя не проверялось в живом сеансе — если Civil ответит «неизвестная
        /// команда», подставьте фактическое: всё остальное от него не зависит.
        /// </summary>
        public const string CommandName = "PROJECTOBJECTSTOPROF";

        // Состояние одной незавершённой попытки проецирования.
        private static ObjectId _pendingController;
        private static HashSet<string> _before;
        private static bool _hooked;

        /// <summary>Есть незавершённое ожидание команды проецирования?</summary>
        public static bool IsPending { get { return !_pendingController.IsNull; } }

        /// <summary>
        /// Предвыбрать блоки массива и запустить штатную команду.
        /// Команда уйдёт в очередь и начнётся после текущей — так и задумано.
        /// </summary>
        public static bool Launch(Document doc, ObjectId controllerId, FacingWallDefinition def)
        {
            if (doc == null || def == null) return false;

            List<ObjectId> blocks = CollectBlocks(def);
            if (blocks.Count == 0)
            {
                doc.Editor.WriteMessage("\nВ массиве нет блоков для проецирования.");
                return false;
            }

            _before = CollectProjections(doc.Database);
            _pendingController = controllerId;

            if (!_hooked)
            {
                doc.CommandEnded += OnCommandFinished;
                doc.CommandCancelled += OnCommandFinished;
                doc.CommandFailed += OnCommandFinished;
                _hooked = true;
            }

            try
            {
                // Команда берёт объекты из предвыбора, поэтому PICKFIRST нужен.
                AcApp.SetSystemVariable("PICKFIRST", 1);
                doc.Editor.SetImpliedSelection(blocks.ToArray());
            }
            catch (System.Exception)
            {
                // не смертельно: пользователь выберет блоки сам
            }

            doc.Editor.WriteMessage(
                "\nВыбрано блоков: {0}. Запускаю {1} — укажите вид профиля и " +
                "настройте стиль в окне.", blocks.Count, CommandName);

            doc.SendStringToExecute("_." + CommandName + " ", true, false, true);
            return true;
        }

        /// <summary>Стереть ранее созданные проекции этого массива.</summary>
        public static void Erase(Transaction tr, FacingWallDefinition def)
        {
            if (def == null || def.CivilProjectionIds == null) return;

            FacingWallBuilder.EraseAll(tr, def.CivilProjectionIds);
            def.CivilProjectionIds = new List<ObjectId>();
        }

        // =================================================================

        /// <summary>
        /// Команда завершилась — вычитаем состав до из состава после.
        ///
        /// Ловим ЛЮБУЮ команду, а не только свою: её глобальное имя не проверено,
        /// и промахнуться мимо фильтра хуже, чем лишний раз посчитать разницу.
        /// Пустая разница означает «ещё не она» — ждём дальше, но снимаемся,
        /// если завершилась команда с похожим именем (значит, отменили).
        /// </summary>
        private static void OnCommandFinished(object sender, CommandEventArgs e)
        {
            if (_pendingController.IsNull) return;

            var doc = sender as Document;
            if (doc == null) return;

            List<ObjectId> created;

            try
            {
                created = Diff(doc.Database);
            }
            catch (System.Exception)
            {
                Unhook(doc);
                return;
            }

            bool looksLikeOurCommand =
                e.GlobalCommandName != null &&
                e.GlobalCommandName.IndexOf("PROJECT", StringComparison.OrdinalIgnoreCase) >= 0;

            if (created.Count == 0)
            {
                if (looksLikeOurCommand)
                {
                    doc.Editor.WriteMessage("\nПроекции не созданы.");
                    Unhook(doc);
                }
                return;   // не наша команда — ждём дальше
            }

            ObjectId controllerId = _pendingController;
            Unhook(doc);

            try
            {
                using (doc.LockDocument())
                using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                {
                    FacingWallDefinition def = FacingWallController.Load(controllerId, tr);
                    if (def != null)
                    {
                        def.CivilProjectionIds.AddRange(created);
                        FacingWallController.Save(controllerId, def, tr);
                    }

                    tr.Commit();
                }

                doc.Editor.WriteMessage(
                    "\nПроекций создано: {0}. Они привязаны к массиву и будут " +
                    "стёрты при смене режима.", created.Count);
            }
            catch (System.Exception ex)
            {
                doc.Editor.WriteMessage("\nНе удалось запомнить проекции: " + ex.Message);
            }
        }

        private static void Unhook(Document doc)
        {
            _pendingController = ObjectId.Null;
            _before = null;

            if (!_hooked || doc == null) return;

            doc.CommandEnded -= OnCommandFinished;
            doc.CommandCancelled -= OnCommandFinished;
            doc.CommandFailed -= OnCommandFinished;
            _hooked = false;
        }

        private static List<ObjectId> Diff(Database db)
        {
            var created = new List<ObjectId>();
            if (_before == null) return created;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in EnumerateProjections(tr, db))
                    if (!_before.Contains(id.Handle.ToString())) created.Add(id);

                tr.Commit();
            }

            return created;
        }

        private static HashSet<string> CollectProjections(Database db)
        {
            var handles = new HashSet<string>();

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in EnumerateProjections(tr, db))
                    handles.Add(id.Handle.ToString());

                tr.Commit();
            }

            return handles;
        }

        /// <summary>
        /// Все проекции чертежа. Перебор всего пространства модели — дорого, но
        /// это одноразовая операция на команду пользователя, а дешевле способа
        /// в API нет: по проекции не спросить, чья она.
        /// </summary>
        private static IEnumerable<ObjectId> EnumerateProjections(Transaction tr, Database db)
        {
            var btr = (BlockTableRecord)tr.GetObject(
                SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);

            foreach (ObjectId id in btr)
            {
                if (id.IsErased) continue;

                ProfileProjection projection = null;
                try
                {
                    projection = tr.GetObject(id, OpenMode.ForRead) as ProfileProjection;
                }
                catch (System.Exception)
                {
                    continue;
                }

                if (projection != null) yield return id;
            }
        }

        private static List<ObjectId> CollectBlocks(FacingWallDefinition def)
        {
            var blocks = new List<ObjectId>();
            if (def.Rows == null) return blocks;

            foreach (FacingWallRowDefinition row in def.Rows)
            {
                if (row == null || row.BlockIds == null) continue;

                foreach (ObjectId id in row.BlockIds)
                    if (!id.IsNull && id.IsValid && !id.IsErased) blocks.Add(id);
            }

            return blocks;
        }
    }
}
