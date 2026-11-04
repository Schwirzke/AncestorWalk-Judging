using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AncestorWalk
{
    // Small fictional catalog is loaded on the main thread, before panels/IO workers.
    public static class ProductDemoData
    {
        [Serializable] public sealed class Catalog
        {public int version,sceneYear;public Endpoint[] endpoints;public Move[] moves;public Portrait[] portraits;public BundledDemoScenes.Omission[] omittedScenes;}
        [Serializable] public sealed class Endpoint {public string personId,place;public int year;}
        [Serializable] public sealed class Move {public string personId,place;public int year;}
        [Serializable] public sealed class Portrait
        {public string personId,name,place,style,prompt;public int year;public PhotoRecord photo;}
        static Catalog current;static string loadedJson;
        public static Catalog Current
        {
            get{string json=Resources.Load<TextAsset>("ProductDemoCatalog").text;if(current==null||json!=loadedJson){current=JsonUtility.FromJson<Catalog>(json);loadedJson=json;}return current;}
        }
        public static void Attach(TreeDocument document)
        {
            if(document.Id!=PanoramaCache.ProductDemoTreeId)return;
            foreach(var entry in Current.portraits??Array.Empty<Portrait>())
            {
                if(entry.photo==null||string.IsNullOrEmpty(entry.photo.Id))continue;
                var person=document.People.Single(p=>p.Id==entry.personId);
                if(document.Photos.All(p=>p.Id!=entry.photo.Id))document.Photos.Add(entry.photo);
                if(!person.PhotoIds.Contains(entry.photo.Id))person.PhotoIds.Add(entry.photo.Id);
                person.PrimaryPhotoId=entry.photo.Id;
            }
            document.Validate();
        }
        public static List<Residence> DefaultMoves(string tree,string person)
        {
            if(tree!=PanoramaCache.ProductDemoTreeId)return new List<Residence>();
            var authored=ProductDemoAuthoring.Moves(tree,person);if(authored!=null)return authored;
            return Current.moves.Where(e=>e.personId==person).Select(e=>new Residence{year=e.year,place=e.place}).ToList();
        }
    }
}
