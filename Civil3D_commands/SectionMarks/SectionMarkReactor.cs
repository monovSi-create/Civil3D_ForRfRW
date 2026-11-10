using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;
using Line = Autodesk.AutoCAD.DatabaseServices.Line;
using AcAp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace Civil3D_commands.SectionMarks
{
    /// <summary>
    /// Следит за линиями выборки, видами сечений, видом профиля и самими
    /// отметками. Сам ничего не правит: помечает отметки «грязными», а на
    /// CommandEnded/Idle сводит их под блокировкой документа — та же схема,
    /// что у BreakReactor, и по той же причине (нельзя открывать объекты
    /// на запись из события об их изменении).
    ///
    /// Что происходит:
    ///   линия выборки сдвинута или переименована → отметка встаёт на новый пикет;
    ///   линия выборки или вид сечения стёрты, либо стёрт блок отметки → отметка удаляется целиком;
    ///   чёрточка сдвинута → её новая высота запоминается, остальное перестраивается;
    ///   стёрта чёрточка или стрелка → рисуется заново.
    /// </summary>
    internal sealed class SectionMarkReactor
    {
        private static readonly Dictionary<Database, SectionMarkReactor> All =
            new Dictionary<Database, SectionMarkReactor>();

        public static string LastError;
        public static int Events, Syncs, Deletes;

        private readonly Document _doc;
        private readonly Database _db;

        private struct Link
        {
            public Guid Id;
            public MarkPart Part;
        }

        private Dictionary<Handle, List<Link>> _watch = new Dictionary<Handle, List<Link>>();
        private int _markCount;

        private readonly HashSet<Guid> _sync = new HashSet<Guid>();
        private readonly HashSet<Guid> _readTicks = new HashSet<Guid>();
        private readonly HashSet<Guid> _delete = new HashSet<Guid>();

        private bool _suspended;
        private bool _idleHooked;
        private int _commandDepth;

        private SectionMarkReactor(Document doc)
        {
            _doc = doc;
            _db = doc.Database;
        }

        public int MarkCount { get { return _markCount; } }
        public int WatchedCount { get { return _watch.Count; } }

        public static SectionMarkReactor For(Document doc)
        {
            if (doc == null) return null;

            SectionMarkReactor r;
            if (All.TryGetValue(doc.Database, out r)) return r;

            r = new SectionMarkReactor(doc);
            All[doc.Database] = r;
            r.Subscribe();

            try { r.Reload(); } catch (System.Exception ex) { LastError = ex.Message; }
            return r;
        }

        private void Subscribe()
        {
            _db.ObjectModified += OnModified;
            _db.ObjectErased += OnErased;
            _doc.CommandWillStart += OnCommandWillStart;
            _doc.CommandEnded += OnCommandEnded;
            _doc.CommandCancelled += OnCommandCancelled;
            _doc.CommandFailed += OnCommandFailed;
            _doc.BeginDocumentClose += OnClose;
        }

        private void OnClose(object sender, DocumentBeginCloseEventArgs e)
        {
            _db.ObjectModified -= OnModified;
            _db.ObjectErased -= OnErased;
            _doc.CommandWillStart -= OnCommandWillStart;
            _doc.CommandEnded -= OnCommandEnded;
            _doc.CommandCancelled -= OnCommandCancelled;
            _doc.CommandFailed -= OnCommandFailed;
            _doc.BeginDocumentClose -= OnClose;
            if (_idleHooked) { AcAp.Idle -= OnIdle; _idleHooked = false; }
            All.Remove(_db);
        }

        /// <summary>Пока объект не освобождён, события отметок игнорируются: так команды правят свои же объекты.</summary>
        public IDisposable Suspend()
        {
            bool before = _suspended;
            _suspended = true;
            return new Resume(this, before);
        }

        private sealed class Resume : IDisposable
        {
            private readonly SectionMarkReactor _r;
            private readonly bool _before;
            public Resume(SectionMarkReactor r, bool before) { _r = r; _before = before; }
            public void Dispose() { _r._suspended = _before; }
        }

        /// <summary>Перечитать отметки из чертежа и пересобрать таблицу наблюдения.</summary>
        public void Reload()
        {
            using (Transaction tr = _db.TransactionManager.StartTransaction())
            {
                Reindex(SectionMarkStore.LoadAll(tr, _db));
                tr.Commit();
            }
        }

        public void Reindex(List<SectionMark> marks)
        {
            var map = new Dictionary<Handle, List<Link>>();

            foreach (SectionMark m in marks)
            {
                foreach (var kv in m.Links())
                {
                    if (kv.Key.Value == 0) continue;

                    List<Link> list;
                    if (!map.TryGetValue(kv.Key, out list))
                    {
                        list = new List<Link>();
                        map[kv.Key] = list;
                    }

                    list.Add(new Link { Id = m.Id, Part = kv.Value });
                }
            }

            _watch = map;
            _markCount = marks.Count;
        }

        // =================================================================
        //  СОБЫТИЯ
        // =================================================================

        private void OnModified(object sender, ObjectEventArgs e)
        {
            if (_suspended || _watch.Count == 0) return;

            List<Link> links;
            if (!_watch.TryGetValue(e.DBObject.Handle, out links)) return;

            Events++;
            foreach (Link l in links)
            {
                _sync.Add(l.Id);
                if (l.Part == MarkPart.TopTick || l.Part == MarkPart.BotTick) _readTicks.Add(l.Id);
            }

            HookIdle();
        }

        private void OnErased(object sender, ObjectErasedEventArgs e)
        {
            if (_suspended || _watch.Count == 0) return;

            List<Link> links;
            if (!_watch.TryGetValue(e.DBObject.Handle, out links)) return;

            Events++;
            foreach (Link l in links)
            {
                bool primary = l.Part == MarkPart.SampleLine || l.Part == MarkPart.SectionView ||
                               l.Part == MarkPart.ProfileView || l.Part == MarkPart.Alignment ||
                               l.Part == MarkPart.Block;

                // Стёрли основу — отметка уходит. Вернули (UNDO) или стёрли только
                // чёрточку со стрелкой — отметку перерисовываем.
                if (e.Erased && primary) _delete.Add(l.Id);
                else _sync.Add(l.Id);
            }

            HookIdle();
        }

        private void OnCommandWillStart(object sender, CommandEventArgs e)
        {
            _commandDepth++;
        }

        private void OnCommandCancelled(object sender, CommandEventArgs e)
        {
            if (_commandDepth > 0) _commandDepth--;
            Drain();
        }

        private void OnCommandFailed(object sender, CommandEventArgs e)
        {
            if (_commandDepth > 0) _commandDepth--;
        }

        private void OnCommandEnded(object sender, CommandEventArgs e)
        {
            if (_commandDepth > 0) _commandDepth--;

            // После UNDO/REDO записи в чертеже откатились, а наша таблица — нет.
            string cmd = (e.GlobalCommandName ?? "").ToUpperInvariant();
            if (cmd == "UNDO" || cmd == "U" || cmd == "REDO" || cmd == "MREDO")
            {
                try { Reload(); } catch (System.Exception ex) { LastError = ex.Message; }
            }

            Drain();
        }

        private void OnIdle(object sender, EventArgs e)
        {
            if (_commandDepth > 0) return;
            Drain();
        }

        private void HookIdle()
        {
            if (_idleHooked) return;
            AcAp.Idle += OnIdle;
            _idleHooked = true;
        }

        // =================================================================

        private void Drain()
        {
            if (_suspended) return;

            if (_sync.Count == 0 && _delete.Count == 0)
            {
                if (_idleHooked) { AcAp.Idle -= OnIdle; _idleHooked = false; }
                return;
            }

            var toSync = new List<Guid>(_sync);
            var toRead = new HashSet<Guid>(_readTicks);
            var toDelete = new List<Guid>(_delete);
            _sync.Clear();
            _readTicks.Clear();
            _delete.Clear();

            if (_idleHooked) { AcAp.Idle -= OnIdle; _idleHooked = false; }

            _suspended = true;
            try
            {
                using (_doc.LockDocument())
                using (Transaction tr = _db.TransactionManager.StartTransaction())
                {
                    List<SectionMark> marks = SectionMarkStore.LoadAll(tr, _db);

                    foreach (Guid id in toDelete)
                    {
                        SectionMark m = marks.Find(x => x.Id == id);
                        if (m == null) continue;

                        Remove(tr, marks, m);
                    }

                    foreach (Guid id in toSync)
                    {
                        if (toDelete.Contains(id)) continue;

                        SectionMark m = marks.Find(x => x.Id == id);
                        if (m == null) continue;

                        if (toRead.Contains(id)) SectionMarkBuilder.ReadTicks(tr, _db, m);

                        if (SectionMarkBuilder.Sync(tr, _db, m))
                        {
                            SectionMarkStore.Save(tr, _db, m);
                            Syncs++;
                        }
                        else
                        {
                            Remove(tr, marks, m);
                        }
                    }

                    tr.Commit();
                    Reindex(marks);
                }
            }
            catch (System.Exception ex)
            {
                // молча: исключение отсюда не должно ронять сеанс
                LastError = ex.Message;
            }
            finally
            {
                _suspended = false;
            }
        }

        private void Remove(Transaction tr, List<SectionMark> marks, SectionMark m)
        {
            SectionMarkBuilder.EraseDrawn(tr, _db, m);
            SectionMarkStore.Remove(tr, _db, m);
            marks.Remove(m);
            Deletes++;
        }
    }

    /// <summary>
    /// Чёрточки отметки двигаются только по вертикали: горизонтальная
    /// составляющая смещения отбрасывается до base. Тот же приём, что у
    /// вертикальных границ в ProfCorrLink; все чужие отрезки уходят в base как есть.
    /// </summary>
    internal sealed class SectionMarkGrips : GripOverrule
    {
        private static SectionMarkGrips _instance;

        public static bool IsRegistered { get { return _instance != null; } }

        public static void Enable()
        {
            if (_instance == null)
            {
                _instance = new SectionMarkGrips();
                Overrule.AddOverrule(RXObject.GetClass(typeof(Line)), _instance, false);
            }

            Overrule.Overruling = true;
        }

        private static bool IsTick(Entity e)
        {
            Guid id;
            bool top;
            return SectionMarkTag.TryRead(e, out id, out top);
        }

        public override bool IsApplicable(RXObject overruledSubject)
        {
            return IsTick(overruledSubject as Entity);
        }

        public override void MoveGripPointsAt(
            Entity entity, GripDataCollection grips,
            Vector3d offset, MoveGripPointsFlags bitFlags)
        {
            if (IsTick(entity)) offset = new Vector3d(0.0, offset.Y, 0.0);
            base.MoveGripPointsAt(entity, grips, offset, bitFlags);
        }

        public override void MoveGripPointsAt(
            Entity entity, IntegerCollection indices, Vector3d offset)
        {
            if (IsTick(entity)) offset = new Vector3d(0.0, offset.Y, 0.0);
            base.MoveGripPointsAt(entity, indices, offset);
        }
    }
}
