using System;
using System.IO;
using UnityEngine;
namespace AncestorWalk
{
    public static class SceneYearDetails
    {
        public const int MaximumLength=1000;
        [Serializable] sealed class Entry {public string details;}
        static string FileFor(string storage,string tree,string person,int year)
        {TreeStore.CheckId(tree);if(year<1||year>9999)throw new InvalidDataException("Invalid scene-details year.");return Path.Combine(storage,"SceneDetails",tree,PanoramaCache.PersonKey(person),year+".json");}
        public static string Normalize(string value)
        {value=(value??"").Trim();if(value.Length>MaximumLength)throw new InvalidDataException("Scene details must be at most 1,000 characters.");return value;}
        public static string Load(string storage,string tree,string person,int year)
        {string file=FileFor(storage,tree,person,year);if(!File.Exists(file))return ProductDemoAuthoring.Setting(tree,person,year)?.details??"";if(new FileInfo(file).Length>8192)throw new InvalidDataException("Invalid saved scene details.");return Normalize(JsonUtility.FromJson<Entry>(File.ReadAllText(file))?.details);}
        public static void Save(string storage,string tree,string person,int year,string details)
        {details=Normalize(details);string file=FileFor(storage,tree,person,year);Directory.CreateDirectory(Path.GetDirectoryName(file));TreeStore.AtomicText(file,JsonUtility.ToJson(new Entry{details=details}));ProductDemoAuthoring.Changed(storage,tree);}
    }
}
