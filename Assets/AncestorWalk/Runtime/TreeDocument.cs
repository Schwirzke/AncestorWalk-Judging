using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace AncestorWalk
{
    [Serializable] public sealed class FamilyRecord
    {public string Id;public List<string> Parents=new(),Children=new();}
    [Serializable] public sealed class PhotoRecord
    {
        public string Id,File,Title,Date,Description,OriginalPath,SourcePhotoId,CutoutMode,CutoutModel;public int Width,Height,Orientation=1;
    }
    [Serializable] public sealed class TreeDocument
    {
        public int Version=1;public string Id,Name,SourceFingerprint,SourceGedcom,UpdatedUtc;public bool IsDemo;
        public List<Person> People=new();public List<FamilyRecord> Families=new();public List<PhotoRecord> Photos=new();public List<string> Warnings=new();
        public static TreeDocument FromTree(FamilyTree tree,string id,string name,bool demo=false)=>new TreeDocument
        {Id=id,Name=name,IsDemo=demo,People=tree.People.Values.ToList(),Families=tree.Families.Select(f=>new FamilyRecord{Id=f.Id,Parents=f.Parents.ToList(),Children=f.Children.ToList()}).ToList(),Warnings=tree.Warnings.ToList()};
        public FamilyTree ToTree()
        {
            var tree=new FamilyTree();foreach(var p in People)tree.People.Add(p.Id,p);
            foreach(var r in Families){var f=new Family{Id=r.Id};f.Parents.AddRange(r.Parents);f.Children.AddRange(r.Children);tree.Families.Add(f);}tree.Warnings.AddRange(Warnings);return tree;
        }
        public TreeDocument Copy()=>JsonUtility.FromJson<TreeDocument>(JsonUtility.ToJson(this));
        public void Validate(bool editing=false)
        {
            TreeStore.CheckId(Id);if(Version!=1)throw new InvalidDataException("Unsupported tree version.");
            if(string.IsNullOrWhiteSpace(Name)||Name.Length>100)throw new InvalidDataException("Enter a tree name up to 100 characters.");
            if(People==null||Families==null||Photos==null||Warnings==null||People.Count>2000||Families.Count>4000||Photos.Count>2000||Warnings.Count>10000)throw new InvalidDataException("Tree exceeds the supported size (2,000 people/photos, 4,000 families).");
            RestoreRecordedSex();
            var ids=new HashSet<string>();foreach(var p in People)
            {
                if(p==null||string.IsNullOrWhiteSpace(p.Id)||p.Id.Length>200||!ids.Add(p.Id))throw new InvalidDataException("Missing or duplicate person ID.");
                if(new[]{p.Name,p.BirthDate,p.DeathDate,p.BirthPlace,p.DeathPlace,p.Sex}.Any(s=>s==null||s.Length>200||s.Any(char.IsControl)))throw new InvalidDataException("Person fields must be at most 200 characters.");
                if(editing&&LifeTimeline.Year(p.BirthDate,out int birth)&&LifeTimeline.Year(p.DeathDate,out int death)&&birth>death)throw new InvalidDataException("Death cannot precede birth.");
                p.PhotoIds??=new List<string>();
            }
            var photos=new HashSet<string>();foreach(var photo in Photos)
            {if(photo==null)throw new InvalidDataException("Missing photo record.");TreeStore.CheckId(photo.Id);if(!photos.Add(photo.Id)||photo.File!=photo.Id+".png"&&photo.File!=photo.Id+".jpg"||photo.Width<1||photo.Height<1||photo.Orientation<1||photo.Orientation>8||(photo.Title?.Length??0)>200||(photo.Date?.Length??0)>200||(photo.Description?.Length??0)>2000)throw new InvalidDataException("Invalid photo record.");}
            foreach(var p in People){p.PhotoIds=p.PhotoIds.Distinct().ToList();if(p.PhotoIds.Any(id=>!photos.Contains(id))||!string.IsNullOrEmpty(p.PrimaryPhotoId)&&!p.PhotoIds.Contains(p.PrimaryPhotoId))throw new InvalidDataException("Photo reference is missing.");}
            var families=new HashSet<string>();var children=ids.ToDictionary(id=>id,_=>new HashSet<string>());
            foreach(var f in Families)
            {
                if(f==null||string.IsNullOrEmpty(f.Id)||f.Id.Length>200||!families.Add(f.Id)||f.Parents==null||f.Children==null)throw new InvalidDataException("Missing or duplicate family record.");
                if(editing&&(f.Parents.Concat(f.Children).Any(id=>!ids.Contains(id))||f.Parents.Intersect(f.Children).Any()))throw new InvalidDataException("A relationship references a missing person or makes someone their own parent.");
                foreach(var parent in f.Parents.Where(ids.Contains))foreach(var child in f.Children.Where(ids.Contains))children[parent].Add(child);
            }
            if(editing)
            {
                var indegree=ids.ToDictionary(id=>id,_=>0);foreach(var set in children.Values)foreach(var id in set)indegree[id]++;
                var ready=new Queue<string>(ids.Where(id=>indegree[id]==0));int seen=0;
                while(ready.Count>0){var id=ready.Dequeue();seen++;foreach(var child in children[id])if(--indegree[child]==0)ready.Enqueue(child);}
                if(seen!=ids.Count)throw new InvalidDataException("This relationship would create a cycle in the tree.");
            }
        }
        void RestoreRecordedSex()
        {
            if(!People.Any(p=>p!=null&&p.Sex==null))return;
            FamilyTree source=null;
            if(!string.IsNullOrWhiteSpace(SourceGedcom))try{source=GedcomImporter.Parse(SourceGedcom);}catch(FormatException){}
            foreach(var p in People.Where(p=>p!=null&&p.Sex==null))p.Sex=source!=null&&source.People.TryGetValue(p.Id,out var original)?original.Sex:"";
        }
    }
    public sealed class TreeStore
    {
        public readonly string Root,MediaRoot,ThumbnailRoot;
        readonly string storage;
        public TreeStore(string storage){this.storage=Path.GetFullPath(storage);Root=Path.Combine(this.storage,"Trees");MediaRoot=Path.Combine(this.storage,"Photos");ThumbnailRoot=Path.Combine(this.storage,"PhotoThumbnails");}
        public static string NewId()=>PanoramaCache.Hash(Encoding.UTF8.GetBytes(Guid.NewGuid().ToString("N")));
        public static void CheckId(string id){if(id==null||id.Length!=64||id.Any(c=>!Uri.IsHexDigit(c)))throw new InvalidDataException("Invalid tree/media ID.");}
        public string DocumentDirectory(string id){CheckId(id);return Path.Combine(Root,id);}
        public TreeDocument Load(string id)
        {
            string path=Path.Combine(DocumentDirectory(id),"tree.json");if(new FileInfo(path).Length>16*1024*1024)throw new InvalidDataException("Tree document is too large.");
            var doc=JsonUtility.FromJson<TreeDocument>(File.ReadAllText(path));if(doc==null||doc.Id!=id)throw new InvalidDataException("Tree identity mismatch.");doc.Validate();return doc;
        }
        public List<TreeDocument> List()
        {
            var list=new List<TreeDocument>();if(!Directory.Exists(Root))return list;
            foreach(var dir in Directory.GetDirectories(Root))try{list.Add(Load(Path.GetFileName(dir)));}catch{}
            return list.OrderByDescending(d=>d.UpdatedUtc,StringComparer.Ordinal).ToList();
        }
        public void Save(TreeDocument document,bool editing=true)
        {
            document.Validate(editing);if(ProductDemoAuthoring.ReadOnly(document))throw new InvalidOperationException("Copy the demo to a new tree before editing.");
            string dir=DocumentDirectory(document.Id);Directory.CreateDirectory(dir);document.UpdatedUtc=DateTime.UtcNow.ToString("O");
            AtomicText(Path.Combine(dir,"tree.json"),Serialize(document));
            if(!string.IsNullOrEmpty(document.SourceGedcom)&&!File.Exists(Path.Combine(dir,"source.ged")))AtomicText(Path.Combine(dir,"source.ged"),document.SourceGedcom);
            ProductDemoAuthoring.Changed(storage,document.Id);
        }
        // Caller must stop this tree's jobs and await their checkpoint/commits first.
        // Shared content-addressed photos and request history deliberately remain.
        public string Delete(string id)
        {
            var document=Load(id);if(document.IsDemo)throw new InvalidOperationException("The built-in demo cannot be deleted.");
            string directory=DocumentDirectory(id),removed=Path.Combine(Root,".deleted-"+id+"-"+Guid.NewGuid().ToString("N"));
            var owned=new[]{directory,Path.Combine(storage,"Locations",id),Path.Combine(storage,"SceneDetails",id),Path.Combine(storage,"ScenePhotoChoices",id),Path.Combine(storage,"Panoramas","people",id)};
            foreach(string path in owned)CheckLocalPath(path);
            // Atomic removal from the library precedes slow cache cleanup. A failed rename
            // leaves the saved tree untouched; incomplete cleanup is reported explicitly.
            Directory.Move(directory,removed);bool incomplete=false;
            foreach(string path in new[]{owned[1],owned[2],removed})
                try{if(Directory.Exists(path))Directory.Delete(path,true);}catch(IOException){incomplete=true;}catch(UnauthorizedAccessException){incomplete=true;}
            return incomplete?"Tree deleted. Some local cache files could not be removed.":"Tree deleted.";
        }
        void CheckLocalPath(string path)
        {
            for(string current=Path.GetFullPath(path);current!=null;current=Path.GetDirectoryName(current))
            {
                if((File.Exists(current)||Directory.Exists(current))&&(File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new IOException("Cannot delete a tree through a linked storage folder.");
                if(current==storage)return;
            }
            throw new IOException("Tree data is outside app storage.");
        }
        public static string Serialize(TreeDocument document)
        {string json=JsonUtility.ToJson(document,true);if(Encoding.UTF8.GetByteCount(json)>16*1024*1024)throw new InvalidDataException("Tree document exceeds 16 MB.");return json;}
        public static void AtomicText(string path,string text)
        {
            string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{File.WriteAllText(temp,text,new UTF8Encoding(false));if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}
            finally{if(File.Exists(temp))File.Delete(temp);}
        }
    }
}
