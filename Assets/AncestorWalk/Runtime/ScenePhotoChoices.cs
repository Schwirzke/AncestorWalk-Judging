using System;
using System.IO;
using UnityEngine;

namespace AncestorWalk
{
    // Explicit photo choices survive failed requests and ordinary Regenerate clicks.
    public static class ScenePhotoChoices
    {
        [Serializable] sealed class Choice {public SceneReference photo;}
        static string PathFor(string storage,string tree,string person,int year)
        {
            TreeStore.CheckId(tree);if(year<1||year>9999)throw new InvalidDataException("Invalid photo selection year.");
            return Path.Combine(storage,"ScenePhotoChoices",tree,PanoramaCache.PersonKey(person),year+".json");
        }
        public static void Save(string storage,string tree,string person,int year,SceneReference photo)
        {
            photo=SceneReference.Optional(photo);
            if(photo!=null){SceneReference.Validate(photo,photo.place);if(!photo.IsPhoto||photo.year!=year)throw new InvalidDataException("Invalid selected scene photo.");}
            string path=PathFor(storage,tree,person,year);Directory.CreateDirectory(Path.GetDirectoryName(path));TreeStore.AtomicText(path,JsonUtility.ToJson(new Choice{photo=SceneReference.Copy(photo)}));ProductDemoAuthoring.Changed(storage,tree);
        }
        public static SceneReference Load(string storage,string tree,string person,int year,string place)
        {
            string path=PathFor(storage,tree,person,year);if(!File.Exists(path)){var bundled=SceneReference.Copy(ProductDemoAuthoring.Setting(tree,person,year)?.photo);if(bundled!=null)bundled.place=ResidenceTimeline.Normalize(place);return bundled;}
            if(new FileInfo(path).Length>4096)throw new InvalidDataException("Saved scene photo selection is invalid. Choose a photo again.");
            var photo=SceneReference.Optional(JsonUtility.FromJson<Choice>(File.ReadAllText(path))?.photo);if(photo==null)return null;
            SceneReference.Validate(photo,photo.place);if(!photo.IsPhoto||photo.year!=year)throw new InvalidDataException("Saved scene photo selection is invalid.");
            photo.place=ResidenceTimeline.Normalize(place);SceneReference.Validate(photo,place);return photo;
        }
    }
}
