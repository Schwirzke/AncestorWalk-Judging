using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AncestorWalk
{
    // Only the built-in fictional demo is authorable in the Editor. Player copies stay private.
    public static class ProductDemoAuthoring
    {
        [Serializable] public sealed class Snapshot
        {public int version=1;public TreeDocument document;public ResidenceTimeline[] locations=Array.Empty<ResidenceTimeline>();public YearSetting[] settings=Array.Empty<YearSetting>();}
        [Serializable] public sealed class YearSetting {public string personKey,details;public int year;public SceneReference photo;}
        public const string ResourceName="ProductDemoAuthoring";
        static string loadedJson;static Snapshot loaded;
        public static Snapshot Current
        {
            get
            {
                var asset=Resources.Load<TextAsset>(ResourceName);string json=asset!=null?asset.text:null;
                if(json==null)return null;
                if(json!=loadedJson){var next=JsonUtility.FromJson<Snapshot>(json);Validate(next,PanoramaCache.ProductDemoTreeId);loaded=next;loadedJson=json;}
                return loaded;
            }
        }
        public static void Validate(Snapshot snapshot,string tree)
        {
            if(snapshot==null||snapshot.version!=1||snapshot.document==null||!snapshot.document.IsDemo||snapshot.document.Id!=tree||snapshot.locations==null)throw new System.IO.InvalidDataException("Invalid authored demo identity.");
            snapshot.document.Validate(true);
            var settings=new HashSet<string>();
            foreach(var setting in snapshot.settings??Array.Empty<YearSetting>())
            {
                if(setting==null||!snapshot.document.People.Any(p=>PanoramaCache.PersonKey(p.Id)==setting.personKey)||setting.year<1||setting.year>9999||!settings.Add(setting.personKey+"/"+setting.year))throw new System.IO.InvalidDataException("Invalid demo year settings.");
                SceneYearDetails.Normalize(setting.details);var photo=SceneReference.Optional(setting.photo);
                if(photo!=null){SceneReference.Validate(photo,photo.place);if(!photo.IsPhoto||photo.year!=setting.year||!snapshot.document.Photos.Any(p=>p.Id==photo.sha256&&p.File==photo.file))throw new System.IO.InvalidDataException("Unattached demo photo choice.");}
            }
            var people=snapshot.document.People.Select(p=>PanoramaCache.PersonKey(p.Id)).ToHashSet();var seen=new HashSet<string>();
            foreach(var location in snapshot.locations)
                if(location==null||location.treeId!=tree||!people.Contains(location.personKey)||!seen.Add(location.personKey)||location.entries==null||location.entries.Count>300||location.entries.Any(e=>e==null||e.year<1||e.year>9999||string.IsNullOrWhiteSpace(e.place)||e.place.Length>ResidenceTimeline.MaximumLocationLength)||location.entries.Select(e=>e.year).Distinct().Count()!=location.entries.Count)throw new System.IO.InvalidDataException("Invalid authored demo locations.");
        }
#if UNITY_EDITOR
        public static string EditorTreeId;
        public static Action<string,string> EditorChanged;
        public static Func<TreeDocument,TreeDocument> EditorLoad;
#endif
        public static bool CanEdit(TreeDocument document)
        {
#if UNITY_EDITOR
            return document!=null&&document.IsDemo&&document.Id==EditorTreeId;
#else
            return false;
#endif
        }
        public static bool ReadOnly(TreeDocument document)=>document!=null&&document.IsDemo&&!CanEdit(document);
        public static void Changed(string storage,string tree)
        {
#if UNITY_EDITOR
            EditorChanged?.Invoke(storage,tree);
#endif
        }
        public static TreeDocument Load(TreeDocument fallback)
        {
            if(fallback.Id!=PanoramaCache.ProductDemoTreeId)return fallback;
            var document=Current?.document.Copy()??fallback;
#if UNITY_EDITOR
            document=EditorLoad?.Invoke(document)??document;
#endif
            return document;
        }
        public static YearSetting Setting(string tree,string person,int year)
            =>tree==PanoramaCache.ProductDemoTreeId?Current?.settings?.FirstOrDefault(e=>e.personKey==PanoramaCache.PersonKey(person)&&e.year==year):null;
        public static List<Residence> Moves(string tree,string person)
        {
            if(tree!=PanoramaCache.ProductDemoTreeId)return null;
            var entries=Current?.locations.FirstOrDefault(e=>e.personKey==PanoramaCache.PersonKey(person))?.entries;
            return entries?.Select(e=>new Residence{year=e.year,place=e.place}).ToList();
        }
    }
}
