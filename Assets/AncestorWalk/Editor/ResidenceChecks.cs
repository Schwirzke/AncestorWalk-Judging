using System;
using System.IO;
using System.Linq;
using UnityEngine;
namespace AncestorWalk.Editor
{
    public static class ResidenceChecks
    {
        public static string Run()
        {
            int checks=0;void Check(bool value,string why){if(!value)throw new Exception(why);checks++;}
            var person=new Person{Id="fixture",BirthDate="1870",DeathDate="1941",BirthPlace="York, England"};
            string tree=PanoramaCache.Hash(System.Text.Encoding.UTF8.GetBytes(Guid.NewGuid().ToString()));
            string cacheRoot=Path.Combine(PanoramaCache.DirectoryPath,"people",tree);
            string root=Path.Combine(PanoramaCache.StorageRoot,"Locations",tree);
            try
            {
                var locations=ResidenceTimeline.Load(tree,person);
                Check(locations.At(person,1890)==person.BirthPlace,"Birthplace fallback");
                locations.Save(person,1892," London,  England ");locations.Save(person,1910,"Paris, France");
                var fixture=File.ReadAllBytes("Assets/AncestorWalk/Resources/ClaraYork1870Panorama.png");
                PanoramaCache.SaveYear(tree,person.Id,1890,fixture,"fixture","test",place:"York, England");
                PanoramaCache.SaveYear(tree,person.Id,1900,fixture,"fixture","test",place:"York, England");
                Check(locations.CompatibleYears(tree,person).SequenceEqual(new[]{1890}),"Stale location cache excluded");
                PanoramaCache.SaveYear(tree,person.Id,1900,fixture,"fixture","test",place:"London, England");
                Check(locations.CompatibleYears(tree,person).SequenceEqual(new[]{1890,1900}),"Matching location cache included");
                PanoramaCache.ReadYear(tree,person.Id,1900,out var saved);Check(saved.place=="London, England","Location provenance persists");
                Check(locations.At(person,1891)=="York, England","Before move");
                Check(locations.At(person,1892)=="London, England","Move boundary");
                Check(locations.At(person,1909)=="London, England","Carry forward");
                Check(locations.At(person,1910)=="Paris, France","Next move boundary");
                locations.Segment(person,1900,out int start,out int end);Check(start==1892&&end==1909,"Residence segment");
                Check(locations.GenerationYear(person,1892)==1892,"Generate exact move year");
                Check(locations.GenerationYear(person,1909)==1909,"Generate exact year before next move");
                var loaded=ResidenceTimeline.Load(tree,person);Check(loaded.entries.Count==2&&loaded.At(person,1900)=="London, England","Restart persistence");
                loaded.Save(person,1892,"Manchester, England");Check(loaded.entries.Count==2&&loaded.At(person,1900)=="Manchester, England","Edit same year");
                loaded.Save(person,1892,"",true);Check(loaded.At(person,1900)==person.BirthPlace&&loaded.At(person,1911)=="Paris, France","Remove restores previous place");
                try{loaded.Save(person,1800,"Invalid");throw new Exception("Outside lifespan accepted");}catch(ArgumentException){}
                Check(loaded.entries.Count==1,"Invalid write preserves entries");
                try{loaded.Save(person,1900," ");throw new Exception("Empty place accepted");}catch(ArgumentException){}
                Check(ResidenceTimeline.Load(tree,person).entries.Count==1,"Invalid input preserves disk");
                var other=new Person{Id="other",BirthDate="1870",DeathDate="1941",BirthPlace="Rome"};
                Check(ResidenceTimeline.Load(tree,other).entries.Count==0,"Person isolation");
                Check(ResidenceTimeline.Same(" York,  England ","york, England"),"Normalized location comparison");
                Check(!ResidenceTimeline.Same("York, England","London, England"),"Changed location invalidates prior scene");
            }
            finally{if(Directory.Exists(root))Directory.Delete(root,true);if(Directory.Exists(cacheRoot))Directory.Delete(cacheRoot,true);}
            var g=UnityEngine.Object.FindAnyObjectByType<AncestorGallery>();g.EditLocation("@I1@");
            Check(g.LocationEditor.IsOpen,"Location editor opens");Physics.SyncTransforms();
            foreach(var key in UnityEngine.Object.FindObjectsByType<LocationKey>(FindObjectsSortMode.None))
            {var origin=Camera.main.transform.position;Check(Physics.Raycast(origin,(key.transform.position-origin).normalized,out var hit,15)&&hit.collider.gameObject==key.gameObject,"Keyboard key occluded: "+key.value);}
            g.LocationEditor.Activate("clear");g.LocationEditor.Activate("L");g.LocationEditor.Activate("space");g.LocationEditor.Activate("back");g.LocationEditor.Activate("cancel");
            Check(!g.LocationEditor.IsOpen,"Cancel closes editor");
            string result=checks+" residence checks passed; fixture locations removed; no paid requests.";File.WriteAllText("Artifacts/Locations/checks.txt",result);return result;
        }
    }
}
