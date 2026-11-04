using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
namespace AncestorWalk
{
    [Serializable] public sealed class Residence {public int year;public string place;}
    [Serializable] public sealed class ResidenceTimeline
    {
        public string treeId,personKey;
        public const int MaximumLocationLength=1000;
        public List<Residence> entries=new();
        public static string Normalize(string place)=>string.Join(" ",(place??"").Split((char[])null,StringSplitOptions.RemoveEmptyEntries));
        public static bool Same(string a,string b)=>string.Equals(Normalize(a),Normalize(b),StringComparison.OrdinalIgnoreCase);
        public string At(Person p,int year)
        {
            // A saved edit at death wins; otherwise GEDCOM's death place is the final endpoint.
            if(LifeTimeline.Year(p.DeathDate,out int death)&&year>=death&&!string.IsNullOrWhiteSpace(p.DeathPlace)&&!entries.Any(e=>e.year==death))return p.DeathPlace;
            return entries.Where(e=>e.year<=year).OrderBy(e=>e.year).LastOrDefault()?.place??p.BirthPlace;
        }
        public void Segment(Person p,int year,out int start,out int end)
        {
            if(!LifeEndpointScenes.TrySceneRange(p,out start,out end))throw new InvalidOperationException("Usable lifespan required.");
            foreach(var e in entries.OrderBy(e=>e.year)) {if(e.year<=year)start=e.year;else {end=e.year-1;break;}}
        }
        public PanoramaRecord[] CompatibleRecords(string tree,Person p)=>PanoramaCache.YearRecords(tree,p.Id)
            .Where(record=>Same(string.IsNullOrWhiteSpace(record.place)?p.BirthPlace:record.place,At(p,record.year))).ToArray();
        public int[] CompatibleYears(string tree,Person p)=>CompatibleRecords(tree,p).Select(record=>record.year).ToArray();
        public int GenerationYear(Person p,int year)
        {if(!LifeEndpointScenes.TrySceneRange(p,out int birth,out int end))throw new InvalidOperationException("Usable lifespan required.");return Mathf.Clamp(year,birth,end);}
        static string PathFor(string tree,string person)
        {
            if(tree==null||tree.Length!=64||tree.Any(c=>!Uri.IsHexDigit(c)))throw new ArgumentException("Invalid tree");
            return Path.Combine(PanoramaCache.StorageRoot,"Locations",tree,PanoramaCache.PersonKey(person)+".json");
        }
        public static ResidenceTimeline Load(string tree,Person p)
        {
            var path=PathFor(tree,p.Id);if(!File.Exists(path))return new ResidenceTimeline{treeId=tree,personKey=PanoramaCache.PersonKey(p.Id),entries=ProductDemoData.DefaultMoves(tree,p.Id)};
            if(new FileInfo(path).Length>2*1024*1024)throw new InvalidDataException("Location file too large.");
            var data=JsonUtility.FromJson<ResidenceTimeline>(File.ReadAllText(path));
            if(data==null||data.treeId!=tree||data.personKey!=PanoramaCache.PersonKey(p.Id)||data.entries==null)throw new InvalidDataException("Invalid location file.");
            if(data.entries.Count>300||data.entries.Any(e=>e==null||e.year<1||e.year>9999||string.IsNullOrWhiteSpace(e.place)||e.place.Length>MaximumLocationLength)||data.entries.Select(e=>e.year).Distinct().Count()!=data.entries.Count)throw new InvalidDataException("Invalid location entries.");
            return data;
        }
        public void Save(Person p,int year,string place,bool remove=false)
        {
            if(!LifeEndpointScenes.TrySceneRange(p,out int birth,out int end)||year<birth||year>end)throw new ArgumentException("Year is outside this lifespan.");
            place=Normalize(place);if(!remove&&(place.Length==0||place.Length>MaximumLocationLength||place.Any(char.IsControl)))throw new ArgumentException("Enter a location description of 1–1,000 characters.");
            var updated=entries.Where(e=>e.year!=year).ToList();if(!remove)updated.Add(new Residence{year=year,place=place});
            if(updated.Count>300)throw new ArgumentException("Location entry limit reached.");
            var copy=new ResidenceTimeline{treeId=treeId,personKey=personKey,entries=updated.OrderBy(e=>e.year).ToList()};
            var path=PathFor(treeId,p.Id);Directory.CreateDirectory(Path.GetDirectoryName(path));var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            File.WriteAllText(temp,JsonUtility.ToJson(copy,true));if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);
            entries=copy.entries;ProductDemoAuthoring.Changed(PanoramaCache.StorageRoot,treeId);
        }
    }
}
