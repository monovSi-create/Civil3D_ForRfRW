using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.VisualBasic;

using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using static Autodesk.AutoCAD.LayerManager.LayerFilter;
using System.Collections.Specialized;
using System.Xml.Linq;
using Autodesk.Aec.ApplicationServices;
using Autodesk.AutoCAD.Geometry;
using System.Text.RegularExpressions;
using System.Diagnostics.Eventing.Reader;
using System.Drawing;
using Civil3D_commands;
using System.Runtime.InteropServices;
using Autodesk.Civil.DatabaseServices.Styles;

//[assembly: CommandClass(typeof(Civil3D_commands.LinkingCommands))]

namespace Civil3D_commands
{
    public class LinkedObjectManager
    {
        const string kCompanyDict = "AsdkLinks";
        const string kApplicationDict = "AsdkLinkedObjects";
        const string kXrecPrefix = "LINKXREC";

        Dictionary<ObjectId, ObjectIdCollection> m_dict;
        //constructor
        public LinkedObjectManager() 
        {
            m_dict = new Dictionary<ObjectId, ObjectIdCollection>();
        }
        //create a bi-directional link between two objects
        public void LinkObjects(ObjectId from, ObjectId to)
        {
            CreateLink(from, to);
            CreateLink(to, from);
        }
        //helper function to create one-way link between objects
        private void CreateLink(ObjectId from, ObjectId to)
        {
            ObjectIdCollection existingList;
            if (m_dict.TryGetValue(from, out existingList))
            {
                if (!existingList.Contains(to))
                {
                    existingList.Add(to);
                    m_dict.Remove(from);
                    m_dict.Add(from, existingList);
                }
            }
            else
            {
                ObjectIdCollection newList = new ObjectIdCollection();
                newList.Add(to);
                m_dict.Add(from, newList);
            }
        }
        //Remove bi-directional links from an object
        public void RemoveLinks(ObjectId from)
        {
            ObjectIdCollection existingList;
            if (m_dict.TryGetValue(from, out existingList))
            {
                m_dict.Remove(from);
                foreach (ObjectId id in existingList)
                {
                    RemoveFromList(id, from);
                }
            }
        }
        //helper function to remove an object reference from list
        private void RemoveFromList(ObjectId key, ObjectId toremove)
        {
            ObjectIdCollection existingList;
            if (m_dict.TryGetValue(key, out existingList))
            {
                if (existingList.Contains(toremove))
                {
                    existingList.Remove(toremove);
                    m_dict.Remove(key);
                    m_dict.Add(key, existingList);
                }
            }
        }
        //returns the list of objects linked to the one passed in
        public ObjectIdCollection GetLinkedObjects(ObjectId from)
        {
            ObjectIdCollection existingList;
            m_dict.TryGetValue(from, out existingList);
            return existingList;
        }
        //check whether the dictionary contains a particular key
        public bool Contains(ObjectId key)
        {
            return m_dict.ContainsKey(key);
        }
        //save the link information to a special dictionary in the database
        public void SaveToDatabase(Database db)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                ObjectId dictId = GetLinkDictionaryId(db, true);
                DBDictionary dict = (DBDictionary)tr.GetObject(dictId, OpenMode.ForWrite);
                int xrecCount = 0;
                foreach (KeyValuePair<ObjectId, ObjectIdCollection> kv in m_dict)
                {
                    //prepare the result buffer with our data
                    ResultBuffer rb = new ResultBuffer(new TypedValue((int)DxfCode.SoftPointerId, kv.Key));
                    int i = 1;
                    foreach(ObjectId id in kv.Value)
                    {
                        rb.Add(new TypedValue((int)(DxfCode.SoftPointerId + i), id));
                        i++;
                    }
                    //update or create an xrecord to store the data
                    Xrecord xrec;
                    bool newXrec = false;
                    if(dict.Contains(kXrecPrefix + xrecCount.ToString()))
                    {
                        //open the existing object
                        Autodesk.AutoCAD.DatabaseServices.DBObject obj = tr.GetObject(dict.GetAt(kXrecPrefix + xrecCount.ToString()), OpenMode.ForWrite);
                        //check whether its an xrecord
                        xrec = obj as Xrecord;
                        if (xrec == null)
                        {
                            //should never happen
                            //we only store records in this dict
                            obj.Erase();
                            xrec = new Xrecord();
                            newXrec = true;
                        }
                    }
                    //no object existed - create a new one
                    else
                    {
                        xrec = new Xrecord();
                        newXrec = true;
                    }
                    xrec.XlateReferences = true;
                    xrec.Data = (ResultBuffer)rb;
                    if (newXrec)
                    {
                        dict.SetAt(kXrecPrefix + xrecCount.ToString(), xrec);
                        tr.AddNewlyCreatedDBObject(xrec, true);
                    }
                    xrecCount++;
                }
                //now erase the left-over xrecords
                bool finished = false;
                do
                { 
                    if(dict.Contains(kXrecPrefix + xrecCount.ToString()))
                    {
                        Autodesk.AutoCAD.DatabaseServices.DBObject obj = tr.GetObject(dict.GetAt(kXrecPrefix + xrecCount.ToString()), OpenMode.ForWrite);
                        obj.Erase();
                    }
                    else
                    {
                        finished = true;
                    }
                    xrecCount++;
                } while(!finished);
                tr.Commit();
            }
        }
        //load the link information from a special dictionary in the database
        public void LoadFromDatabase(Database db)
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            using(Transaction tr = doc.TransactionManager.StartTransaction())
            {
                //try to find the link dictionary, but do not create it if one isnt there
                ObjectId dictId = GetLinkDictionaryId(db, false);
                if (dictId.IsNull)
                {
                    ed.WriteMessage("\nНе удалось найти словарь связей");
                    return;
                }
                //by this stage we can assume the dictionary exists
                DBDictionary dict = (DBDictionary)tr.GetObject(dictId, OpenMode.ForWrite);
                int xrecCount = 0;
                bool done = false;
                //loop, reading the xrecords one-by-one
                while (!done)
                {
                    if (dict.Contains(kXrecPrefix + xrecCount.ToString()))
                    {
                        ObjectId recId = dict.GetAt(kXrecPrefix+xrecCount.ToString());
                        Autodesk.AutoCAD.DatabaseServices.DBObject obj = tr.GetObject(recId, OpenMode.ForRead);
                        Xrecord xrec = obj as Xrecord;
                        if(xrec == null)
                        {
                            ed.WriteMessage("\nDictionary contains non-xrecord");
                            return;
                        }
                        int i = 0;
                        ObjectId from = new ObjectId();
                        ObjectIdCollection to = new ObjectIdCollection();
                        foreach(TypedValue val in xrec.Data)
                        {
                            if(i == 0)
                            {
                                from = (ObjectId)val.Value;
                            }
                            else
                            {
                                to.Add((ObjectId)val.Value);
                            }
                            i++;
                        }
                        //validate the link info and add it to our internal data structure
                        AddValidateLinks(db,from, to);
                        xrecCount++;
                    }
                    else
                    {
                        done = true;
                    }
                }
                tr.Commit();
            }
        }
        //helper function to validate links before adding them to the internal data structure
        private void AddValidateLinks(Database db, ObjectId from, ObjectIdCollection to)
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;
            using (Transaction tr = doc.TransactionManager.StartTransaction())
            {
                try
                {
                    ObjectIdCollection newList = new ObjectIdCollection();
                    //open the from object
                    Autodesk.AutoCAD.DatabaseServices.DBObject obj = tr.GetObject(from, OpenMode.ForRead, false);
                    if (obj != null)
                    {
                        //open each of the "to" objects
                        foreach (ObjectId id in to)
                        {
                            Autodesk.AutoCAD.DatabaseServices.DBObject obj2;
                            try
                            {
                                obj2 = tr.GetObject(id, OpenMode.ForRead, false);
                                //filter out the erased "to" objects
                                if(obj2 != null)
                                {
                                    newList.Add(id);
                                }
                            }
                            catch (System.Exception)
                            {
                                ed.WriteMessage("\nFiltered out link to an erased object");
                            }
                        }
                        //only if the "from" object and at least one "to" object exist (and are unerased) do we add an entry for them
                        if (newList.Count > 0)
                        {
                            m_dict.Add(from, newList);
                        }
                    }
                }
                catch(System.Exception)
                {
                    ed.WriteMessage("\nFiltered out link to an erased object");
                }
                tr.Commit();
            }
        }
        //helper function to get (optionally create) the nested dictionary for our xrecord objects
        private ObjectId GetLinkDictionaryId(Database db, bool createIfNotExisting)
        {
            ObjectId appDictId = ObjectId.Null;
            using(Transaction tr = db.TransactionManager.StartTransaction())
            {
                DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
                //Our outer level ("company") dictionary does not exist
                if (!nod.Contains(kCompanyDict))
                {
                    if (!createIfNotExisting)
                    {
                        return ObjectId.Null;
                    }
                    //create both the "company" dictionary..
                    DBDictionary compDict = new DBDictionary();
                    nod.UpgradeOpen();
                    nod.SetAt(kCompanyDict,compDict);
                    tr.AddNewlyCreatedDBObject(compDict, true);

                    //..and the inner "application" dictionary
                    DBDictionary appDict = new DBDictionary();
                    appDictId = compDict.SetAt(kApplicationDict, appDict);
                    tr.AddNewlyCreatedDBObject(appDict, true);
                }
                else
                {
                    //our "company" dictionary exist
                    DBDictionary compDict = (DBDictionary)tr.GetObject(nod.GetAt(kCompanyDict), OpenMode.ForRead);
                    //so chek for our "application" dictionary
                    if (!compDict.Contains(kApplicationDict))
                    {
                        if (!createIfNotExisting) { return ObjectId.Null; }
                        // create the "application" dictionary
                        DBDictionary appDict = new DBDictionary();
                        compDict.UpgradeOpen();
                        appDictId = compDict.SetAt(kApplicationDict, appDict);
                        tr.AddNewlyCreatedDBObject(appDict, true);
                    }
                    else
                    {
                        //both dictionaries already exist
                        appDictId = compDict.GetAt(kApplicationDict);
                    }
                }
                tr.Commit();
            }
            return appDictId;
        }
    }
    //this class defines our command and event callbacks
    public class LinkingCommands
    {
        LinkedObjectManager m_linkManager;
        ObjectIdCollection m_entitiesToUpdate;

        public LinkingCommands()
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            db.ObjectModified += new ObjectEventHandler(OnObjectModified);
            db.ObjectErased += new ObjectErasedEventHandler(OnObjectErased);
            db.BeginSave += new DatabaseIOEventHandler(OnBeginSave);
            doc.CommandEnded += new CommandEventHandler(OnCommandEnded);
            m_linkManager = new LinkedObjectManager();
            m_entitiesToUpdate = new ObjectIdCollection();
        }
        ~LinkingCommands()
        {
            try
            {
                Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                Database db = doc.Database;
                db.ObjectModified -= new ObjectEventHandler(OnObjectModified);
                db.ObjectErased -= new ObjectErasedEventHandler(OnObjectErased);
                db.BeginSave -= new DatabaseIOEventHandler(OnBeginSave);
                doc.CommandEnded -= new CommandEventHandler(OnCommandEnded);
            }
            catch (System.Exception)  
            {
                //the document or database may no longer be available on unload
            }
        }
        //define "myLink" command
        [CommandMethod("MYLINK")]
        public void LinkPVIs()
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;
            PromptEntityOptions opts = new PromptEntityOptions("\nВыберите линию");
            opts.AllowNone = true;
            opts.SetRejectMessage("\nВыбраны могут быть только линии");
            opts.AddAllowedClass(typeof(Line), false);

            PromptEntityResult res = ed.GetEntity(opts);
            if (res.Status == PromptStatus.OK)
            {
                ObjectId from = res.ObjectId;
                PromptSelectionOptions opts2 = new PromptSelectionOptions();
                opts2.MessageForAdding = "Выберите две ТВП по очереди";
                
                //opts2.AddAllowedClass(typeof(ProfilePVI), false);
                //opts.Message = "\nВыберите две ТВП по очереди";
                PromptSelectionResult res2 = ed.GetSelection(opts2);
                //res = ed.GetSelection(opts);
                if (res2.Status == PromptStatus.OK)
                {
                    SelectionSet ss = res2.Value;
                    foreach(ObjectId id in ss.GetObjectIds())
                    {
                        ObjectId to = id;
                        m_linkManager.LinkObjects(from, to);
                        m_entitiesToUpdate.Add(from);
                    }
                }
            }
        }

        // define "link" command
        [CommandMethod("LINK")]
        public void LinkEntities()
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;
            PromptEntityOptions opts = new PromptEntityOptions("\nВыберите первый круг");
            opts.AllowNone = true;
            opts.SetRejectMessage("\nВыбраны могут быть только круги");
            opts.AddAllowedClass(typeof(Circle), false);

            PromptEntityResult res = ed.GetEntity(opts);
            if (res.Status == PromptStatus.OK)
            {
                ObjectId from = res.ObjectId;
                opts.Message = "\nВыберите второй круг";
                res = ed.GetEntity(opts);
                if (res.Status == PromptStatus.OK)
                {
                    ObjectId to = res.ObjectId;
                    m_linkManager.LinkObjects(from, to);
                    m_entitiesToUpdate.Add(from);
                    
                }
            }
        }
        //define "loadlinks" command
        [CommandMethod("LOADLINKS")]
        public void LoadLinkSettings()
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            m_linkManager.LoadFromDatabase(db);
        }
        //define "savelinks" command
        [CommandMethod("SAVELINKS")]
        public void SaveLinkSettings()
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            m_linkManager.SaveToDatabase(db);
        }
        //define callback for Database.ObjectModified event
        private void OnObjectModified(object sender, ObjectEventArgs e)
        {
            ObjectId id = e.DBObject.ObjectId;
            if(m_linkManager.Contains(id) && !m_entitiesToUpdate.Contains(id)) 
            {
                m_entitiesToUpdate.Add(id);
            }
        }
        //define callback for Database.ObjectErased event
        private void OnObjectErased(object sender, ObjectErasedEventArgs e)
        {
            if (e.Erased)
            {
                m_linkManager.RemoveLinks(e.DBObject.ObjectId);
            }
        }
        //define callback for Database.BeginSave event
        void OnBeginSave(object sender, DatabaseIOEventArgs e)
        {
            Database db = sender as Database;
            if (db != null)
            {
                m_linkManager.SaveToDatabase(db);
            }
        }
        //define callback for Database.CommandEnded event
        private void OnCommandEnded(object sender, CommandEventArgs e)
        {
            foreach(ObjectId id in m_entitiesToUpdate)
            {
                UpdateLinkedEntities(id);
            }
            m_entitiesToUpdate.Clear();
        }
        private void UpdateLinkedEntities(ObjectId from)
        {
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;
            Editor ed = doc.Editor;
            ObjectIdCollection linked = m_linkManager.GetLinkedObjects(from);
            Transaction tr = db.TransactionManager.StartTransaction();
            using(tr)
            {
                try
                {
                    Point3d firstCenter;
                    Point3d secondCenter;
                    double firstRadius;
                    double secondRadius;

                    Autodesk.AutoCAD.DatabaseServices.Entity ent = (Autodesk.AutoCAD.DatabaseServices.Entity)tr.GetObject(from, OpenMode.ForRead);
                    if (GetCenterAndRadius(ent, out firstCenter, out firstRadius))
                    {
                        foreach(ObjectId to in linked)
                        {
                            Autodesk.AutoCAD.DatabaseServices.Entity ent2 = (Autodesk.AutoCAD.DatabaseServices.Entity)tr.GetObject(to, OpenMode.ForRead);
                            if (GetCenterAndRadius(ent2, out secondCenter, out secondRadius))
                            {
                                Vector3d vec = firstCenter - secondCenter;
                                if (!vec.IsZeroLength())
                                {
                                    double apart = vec.Length - (firstRadius + secondRadius);
                                    if (apart < 0.0)
                                    {
                                        apart = -apart;
                                    }
                                    if (apart > 0.00001)
                                    {
                                        ent2.UpgradeOpen();
                                        ent2.TransformBy(Matrix3d.Displacement(vec.GetNormal() * apart));
                                    }
                                }
                            }
                        }
                    }
                }
                catch(System.Exception ex) 
                { 
                    Autodesk.AutoCAD.Runtime.Exception ex2 = ex as Autodesk.AutoCAD.Runtime.Exception;
                    if (ex2 != null && ex2.ErrorStatus != ErrorStatus.WasOpenForUndo)
                    {
                        ed.WriteMessage("\nAutoCAD exception: {0}", ex2);
                    }
                    else if (ex2 == null)
                    {
                        ed.WriteMessage("\nAutoCAD exception: {0}", ex);
                    }
                }
                tr.Commit();
            }

        }
        private bool GetCenterAndRadius(Autodesk.AutoCAD.DatabaseServices.Entity ent, out Point3d center, out double radius)
        {
            Circle circle = ent as Circle;
            if (circle != null)
            {
                center = circle.Center;
                radius = circle.Radius;
                return true;
            }
            else
            {
                center = Point3d.Origin;
                radius = 0.0;
                return false;
            }
        }
    }
}
