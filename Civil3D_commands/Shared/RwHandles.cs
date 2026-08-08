using System;
using Autodesk.AutoCAD.DatabaseServices;
// ObjectId есть и в AutoCAD-, и в Civil-пространстве имён — снимаем неоднозначность.
using ObjectId = Autodesk.AutoCAD.DatabaseServices.ObjectId;

namespace Civil3D_commands.Shared
{
    /// <summary>
    /// Ссылки на объекты чертежа: Handle ↔ текст ↔ ObjectId.
    ///
    /// Оба модуля хранят связи не в ObjectId, а в Handle — он стабилен между
    /// сеансами. Перевод туда-обратно был написан восемь раз с расходящейся
    /// строгостью: где-то стёртый объект отсеивался, где-то нет; признаком
    /// «пусто» служила то строка "0", то Handle.Value == 0.
    ///
    /// Здесь правило одно: **стёртое равно пустому**. Resolve на стёртый или
    /// несуществующий объект возвращает ObjectId.Null, и вызывающему достаточно
    /// проверить IsNull, не думая, откуда взялась ссылка.
    /// </summary>
    public static class RwHandles
    {
        /// <summary>Текстовый признак «ссылки нет». Пишется в записи вместо хэндла.</summary>
        public const string NullText = "0";

        /// <summary>Хэндл объекта строкой. Пустая ссылка — "0".</summary>
        public static string ToText(ObjectId id)
        {
            if (id.IsNull || !id.IsValid) return NullText;
            return id.Handle.ToString();
        }

        /// <summary>То же для готового хэндла.</summary>
        public static string ToText(Handle handle)
        {
            return handle.Value == 0 ? NullText : handle.ToString();
        }

        /// <summary>
        /// Разобрать хэндл из шестнадцатеричной строки. Пустая строка, "0" и
        /// любой мусор дают Handle(0) — «ссылки нет». Записи в чертеже правит
        /// не только плагин, поэтому падать здесь нельзя.
        /// </summary>
        public static Handle Parse(string text)
        {
            if (string.IsNullOrEmpty(text) || text == NullText) return new Handle(0);

            try
            {
                return new Handle(Convert.ToInt64(text, 16));
            }
            catch (System.Exception)
            {
                return new Handle(0);
            }
        }

        /// <summary>Объект по хэндлу. Нет объекта или он стёрт — ObjectId.Null.</summary>
        public static ObjectId Resolve(Database db, Handle handle)
        {
            if (db == null || handle.Value == 0) return ObjectId.Null;

            ObjectId id;
            if (!db.TryGetObjectId(handle, out id)) return ObjectId.Null;

            return IsAlive(id) ? id : ObjectId.Null;
        }

        /// <summary>То же от текстового хэндла.</summary>
        public static ObjectId Resolve(Database db, string handleText)
        {
            return Resolve(db, Parse(handleText));
        }

        /// <summary>Ссылка указывает на живой объект?</summary>
        public static bool IsAlive(ObjectId id)
        {
            return !id.IsNull && id.IsValid && !id.IsErased;
        }

        /// <summary>Объект с таким хэндлом есть в чертеже и не стёрт?</summary>
        public static bool Exists(Database db, Handle handle)
        {
            return !Resolve(db, handle).IsNull;
        }

        /// <summary>
        /// Открыть объект по хэндлу нужным типом. null, если объекта нет,
        /// он стёрт, оказался другого типа или не открылся.
        /// </summary>
        public static T Open<T>(Transaction tr, Database db, Handle handle, OpenMode mode)
            where T : Autodesk.AutoCAD.DatabaseServices.DBObject
        {
            if (tr == null) return null;

            ObjectId id = Resolve(db, handle);
            if (id.IsNull) return null;

            try
            {
                return tr.GetObject(id, mode) as T;
            }
            catch (System.Exception)
            {
                return null;
            }
        }
    }
}
