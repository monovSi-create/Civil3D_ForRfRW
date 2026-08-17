using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.DatabaseServices;
using Civil3D_commands.Shared;

namespace Civil3D_commands.AssociativeBreaks
{
    /// <summary>
    /// Четвёрка объектов, вокруг которой крутится весь модуль: профиль, его вид,
    /// ось и коридор.
    ///
    /// Интерфейс нужен потому, что носителей этой четвёрки теперь два:
    /// <see cref="BreakLink"/> (связь целиком) и <see cref="StationMarker"/>
    /// (каждый разрыв несёт её же копию, чтобы пережить потерю записи связи).
    /// Всё, что умеет работать «по четвёрке» — <see cref="BreakSession.GetBaseline"/>,
    /// пределы разрыва, — принимает интерфейс и не выбирает между ними.
    /// </summary>
    public interface IBreakTarget
    {
        Handle ProfileHandle { get; }
        Handle ProfileViewHandle { get; }
        Handle AlignmentHandle { get; }
        Handle CorridorHandle { get; }
    }

    /// <summary>
    /// Связь «профиль ↔ вид профиля ↔ ось ↔ коридор» как самостоятельный объект.
    ///
    /// Раньше связь в чертеже была ровно одна и хранилась четырьмя хэндлами
    /// в <see cref="StationMarker"/>. Теперь их может быть сколько угодно, в том
    /// числе несколько на ОДНОМ виде профиля: в таком виде рядом лежат несколько
    /// профилей, и каждый служит базовой линией своему коридору.
    ///
    /// Чтобы связь можно было выбрать мышью, у неё есть контроллер — вставка
    /// служебного блока рядом с видом профиля с подписью «коридор-профиль»
    /// (см. <see cref="BreakController"/>). Он же и есть «объект-контроллер»,
    /// как в модуле облицовки: единственная точка входа в данные связи.
    ///
    /// Оформление (заливки участков, надписи, линии границ) — производное:
    /// хэндлы лежат в <see cref="OverlayHandles"/> и перестраиваются целиком.
    /// </summary>
    public class BreakLink : IBreakTarget
    {
        // Тег и версия — по образцу StationMarker: чужую или битую запись надо
        // отличать от своей, иначе она молча исчезает при следующей записи.
        private const string Tag = "RWLINK";
        private const int FormatVersion = 1;

        /// <summary>Постоянный идентификатор связи. Он же лежит в XData контроллера.</summary>
        public Guid Id { get; set; } = Guid.NewGuid();

        public Handle ProfileHandle { get; set; }
        public Handle ProfileViewHandle { get; set; }
        public Handle AlignmentHandle { get; set; }
        public Handle CorridorHandle { get; set; }

        /// <summary>Вставка блока-контроллера. Пусто — контроллер ещё не создан.</summary>
        public Handle ControllerHandle { get; set; }

        // Имена кэшируются: подпись контроллера должна читаться и тогда, когда
        // коридор с профилем открыть не удалось (их удалили, чертёж чинят).
        public string CorridorName { get; set; } = string.Empty;
        public string ProfileName { get; set; } = string.Empty;

        /// <summary>
        /// Цвет связи (индекс ACI). Назначается случайно при создании и дальше
        /// не меняется: по нему пользователь отличает контроллеры друг от друга,
        /// и «случайный каждый раз» означал бы, что запомнить его нельзя.
        /// Ноль — цвет ещё не назначен.
        /// </summary>
        public short ColorIndex { get; set; }

        /// <summary>
        /// Объекты оформления: линии концевых границ, заливки участков, надписи.
        /// Целиком производные — стираются и создаются заново при каждой
        /// перестройке (см. <see cref="BreakOverlay"/>).
        /// </summary>
        public List<Handle> OverlayHandles { get; private set; } = new List<Handle>();

        /// <summary>Подпись контроллера: «имя коридора-имя профиля».</summary>
        public string Label
        {
            get
            {
                string c = string.IsNullOrEmpty(CorridorName) ? "коридор" : CorridorName;
                string p = string.IsNullOrEmpty(ProfileName) ? "профиль" : ProfileName;
                return c + "-" + p;
            }
        }

        /// <summary>
        /// Шаблон нового разрыва: разрыв несёт ту же четвёрку хэндлов, что и связь.
        /// Дублирование намеренное — по нему связь восстанавливается, если её
        /// собственная запись потерялась.
        /// </summary>
        public StationMarker NewMarker()
        {
            return new StationMarker
            {
                ProfileHandle     = ProfileHandle,
                ProfileViewHandle = ProfileViewHandle,
                AlignmentHandle   = AlignmentHandle,
                CorridorHandle    = CorridorHandle
            };
        }

        /// <summary>Связь описывает те же объекты, что и этот маркер?</summary>
        public bool Covers(IBreakTarget other)
        {
            return other != null
                && other.ProfileHandle == ProfileHandle
                && other.CorridorHandle == CorridorHandle;
        }

        /// <summary>Обновить кэш имён из чертежа. Не открылось — прежние остаются.</summary>
        public void RefreshNames(Transaction tr, Database db)
        {
            var corridor = RwHandles.Open<Corridor>(tr, db, CorridorHandle, OpenMode.ForRead);
            if (corridor != null) CorridorName = corridor.Name;

            var profile = RwHandles.Open<Profile>(tr, db, ProfileHandle, OpenMode.ForRead);
            if (profile != null) ProfileName = profile.Name;
        }

        // ------------------------------------------------------------------
        //  СЕРИАЛИЗАЦИЯ
        // ------------------------------------------------------------------

        public ResultBuffer ToResultBuffer()
        {
            var rb = new ResultBuffer(
                new TypedValue((int)DxfCode.Text, Tag),
                new TypedValue((int)DxfCode.Int32, FormatVersion),
                new TypedValue((int)DxfCode.Text, Id.ToString("N")),
                new TypedValue((int)DxfCode.Text, RwHandles.ToText(ProfileHandle)),
                new TypedValue((int)DxfCode.Text, RwHandles.ToText(ProfileViewHandle)),
                new TypedValue((int)DxfCode.Text, RwHandles.ToText(AlignmentHandle)),
                new TypedValue((int)DxfCode.Text, RwHandles.ToText(CorridorHandle)),
                new TypedValue((int)DxfCode.Text, RwHandles.ToText(ControllerHandle)),
                new TypedValue((int)DxfCode.Text, CorridorName ?? string.Empty),
                new TypedValue((int)DxfCode.Text, ProfileName ?? string.Empty),
                new TypedValue((int)DxfCode.Int16, ColorIndex));

            // Оформление — списком переменной длины в хвосте: числа объектов
            // заранее не знает никто, а хвост читается «до конца буфера».
            foreach (Handle h in OverlayHandles)
                rb.Add(new TypedValue((int)DxfCode.Text, RwHandles.ToText(h)));

            return rb;
        }

        /// <summary>
        /// Бросает, если запись не наша или новее этой сборки: принять её
        /// за пустую значит потерять связь при следующем сохранении.
        /// </summary>
        public static BreakLink FromResultBuffer(ResultBuffer rb)
        {
            TypedValue[] v = rb.AsArray();
            if (v.Length < 11)
                throw new InvalidOperationException("Короткая запись связи.");

            if (v[0].TypeCode != (int)DxfCode.Text ||
                !string.Equals(v[0].Value.ToString(), Tag, StringComparison.Ordinal))
                throw new InvalidOperationException("Чужая запись в словаре связей.");

            int format = Convert.ToInt32(v[1].Value);
            if (format < 1 || format > FormatVersion)
                throw new InvalidOperationException(
                    "Запись связи сделана более новой версией плагина (формат " + format + ").");

            int i = 2;
            var link = new BreakLink
            {
                Id                = Guid.ParseExact(v[i++].Value.ToString(), "N"),
                ProfileHandle     = RwHandles.Parse(v[i++].Value.ToString()),
                ProfileViewHandle = RwHandles.Parse(v[i++].Value.ToString()),
                AlignmentHandle   = RwHandles.Parse(v[i++].Value.ToString()),
                CorridorHandle    = RwHandles.Parse(v[i++].Value.ToString()),
                ControllerHandle  = RwHandles.Parse(v[i++].Value.ToString()),
                CorridorName      = v[i++].Value.ToString(),
                ProfileName       = v[i++].Value.ToString(),
                ColorIndex        = Convert.ToInt16(v[i++].Value)
            };

            for (; i < v.Length; i++)
            {
                if (v[i].TypeCode != (int)DxfCode.Text) continue;
                Handle h = RwHandles.Parse(v[i].Value.ToString());
                if (h.Value != 0) link.OverlayHandles.Add(h);
            }

            return link;
        }

        /// <summary>Связь годна, только если живы все четыре объекта и каждый нужного типа.</summary>
        public bool IsAlive(Transaction tr, Database db)
        {
            return Alive<Profile>(tr, db, ProfileHandle)
                && Alive<ProfileView>(tr, db, ProfileViewHandle)
                && Alive<Alignment>(tr, db, AlignmentHandle)
                && Alive<Corridor>(tr, db, CorridorHandle);
        }

        // DBObject, как и Entity, есть в обоих пространствах имён — квалифицируем явно.
        private static bool Alive<T>(Transaction tr, Database db, Handle h)
            where T : Autodesk.AutoCAD.DatabaseServices.DBObject
        {
            return RwHandles.Open<T>(tr, db, h, OpenMode.ForRead) != null;
        }
    }

    /// <summary>
    /// Связи в словаре чертежа: подсловарь RW_AssocBreaks\Links, ключ — Guid связи.
    ///
    /// Ключ именно Guid, а не позиция: позиционные ключи означали бы другую связь
    /// после каждого удаления, и правка одной переписывала бы соседнюю. Ту же
    /// ошибку уже проходили в <see cref="MarkerStore"/>.
    /// </summary>
    public static class BreakLinkStore
    {
        private const string AppDict = "Links";
        private const string KeyPrefix = "LK_";

        private static string KeyOf(Guid id) => KeyPrefix + id.ToString("N");

        public static void SaveAll(Database db, IEnumerable<BreakLink> links)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                ObjectId dictId = MarkerStore.GetDictionaryId(tr, db, AppDict, true);
                var dict = (DBDictionary)tr.GetObject(dictId, OpenMode.ForWrite);

                var live = new HashSet<string>();

                foreach (BreakLink link in links)
                {
                    string key = KeyOf(link.Id);
                    live.Add(key);

                    ResultBuffer data = link.ToResultBuffer();

                    if (dict.Contains(key))
                    {
                        var existing = (Xrecord)tr.GetObject(dict.GetAt(key), OpenMode.ForWrite);
                        existing.Data = data;
                    }
                    else
                    {
                        var xrec = new Xrecord { XlateReferences = true, Data = data };
                        dict.SetAt(key, xrec);
                        tr.AddNewlyCreatedDBObject(xrec, true);
                    }
                }

                // Список стираемого собирается заранее: стирать объекты во время
                // обхода словаря нельзя.
                var stale = new List<ObjectId>();
                foreach (DBDictionaryEntry e in dict)
                    if (!live.Contains(e.Key)) stale.Add(e.Value);

                foreach (ObjectId id in stale)
                    tr.GetObject(id, OpenMode.ForWrite).Erase();

                tr.Commit();
            }
        }

        /// <summary>
        /// Все связи чертежа. Связи с мёртвыми объектами отбрасываются, но
        /// из чертежа не стираются: чинить чертёж — дело пользователя, а не
        /// побочный эффект открытия.
        /// </summary>
        public static List<BreakLink> LoadAll(Database db)
        {
            var result = new List<BreakLink>();

            using (var tr = db.TransactionManager.StartTransaction())
            {
                ObjectId dictId = MarkerStore.GetDictionaryId(tr, db, AppDict, false);
                if (dictId.IsNull) { tr.Commit(); return result; }

                var dict = (DBDictionary)tr.GetObject(dictId, OpenMode.ForRead);

                foreach (DBDictionaryEntry e in dict)
                {
                    var xrec = tr.GetObject(e.Value, OpenMode.ForRead) as Xrecord;
                    if (xrec?.Data == null) continue;

                    try
                    {
                        BreakLink link = BreakLink.FromResultBuffer(xrec.Data);
                        if (link.IsAlive(tr, db)) result.Add(link);
                    }
                    catch (System.Exception)
                    {
                        // Битую запись пропускаем: сообщать о ней есть кому — RW_BREAKDIAG.
                    }
                }

                tr.Commit();
            }

            return result.OrderBy(l => l.Label, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
    }
}
