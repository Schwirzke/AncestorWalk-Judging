using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
namespace AncestorWalk.Editor
{
    public static class LocationSaveChecks
    {
        public static string Run()
        {
            var g=UnityEngine.Object.FindAnyObjectByType<AncestorGallery>();
            if(!Application.isPlaying||g==null)throw new Exception("Play mode required");
            if(g.Generation.Busy)throw new Exception("Wait for the current generation before running fixtures");
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            int checks=0;void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;}
            string id="location-save-"+Guid.NewGuid().ToString("N");
            var p=new Person{Id=id,Name="Location save fixture",BirthDate="1900",DeathDate="1980",BirthPlace="York"};
            var locations=(Dictionary<string,ResidenceTimeline>)typeof(AncestorGallery).GetField("locations",flags).GetValue(g);
            string cache=Path.Combine(PanoramaCache.DirectoryPath,"people",g.TreeId,PanoramaCache.PersonKey(id));
            string file=Path.Combine(PanoramaCache.StorageRoot,"Locations",g.TreeId,PanoramaCache.PersonKey(id)+".json");
            Action refresh=()=>typeof(AncestorGallery).GetMethod("RefreshCards",flags).Invoke(g,null);
            int actions=PlayerPrefs.GetInt("overlay.actions",0);
            var originalGeneration=g.Generation;var fixture=new GameObject("Inactive location-generation fixture");fixture.SetActive(false);
            var generation=fixture.AddComponent<SceneGeneration>();
            typeof(SceneGeneration).GetField("storage",flags).SetValue(generation,Path.GetFullPath("Library/LocationSaveChecks/"+Guid.NewGuid().ToString("N")));
            typeof(SceneGeneration).GetField("TestConnection",flags).SetValue(generation,new SceneServiceConnection{url="http://127.0.0.1:1",token=new string('a',64)});
            typeof(SceneGeneration).GetProperty("Ready").SetValue(generation,true);
            typeof(AncestorGallery).GetProperty("Generation").SetValue(g,generation);
            try
            {
                g.Tree.People.Add(id,p);locations[id]=ResidenceTimeline.Load(g.TreeId,p);
                var bytes=File.ReadAllBytes("Assets/AncestorWalk/Resources/ClaraYork1870Panorama.png");
                PanoramaCache.SaveYear(g.TreeId,id,1916,bytes,"fixture","fixture",place:"York");
                locations[id].Save(p,1975,"Paris");
                PanoramaCache.SaveYear(g.TreeId,id,1975,bytes,"fixture","fixture",place:"Paris");refresh();
                g.SetYear(id,1924);g.Select(id);Check(!g.IsAllTime,"Prior cached panorama reproduces starting state");
                g.EditLocation(id);g.LocationEditor.Activate("clear");
                foreach(char c in "Watsonville")g.LocationEditor.Activate(c.ToString());
                Physics.SyncTransforms();var save=g.LocationEditor.GetComponentsInChildren<LocationKey>().First(k=>k.value=="save");
                var eye=Camera.main.transform.position;
                Check(Physics.Raycast(eye,(save.transform.position-eye).normalized,out var hit,15)&&hit.collider==save.GetComponent<Collider>(),"Save button reachable");
                Check(g.LocationEditor.GetComponentsInChildren<TextMesh>().Any(t=>t.text=="GENERATE"),"Location editor labels the action Generate");
                // The inactive isolated queue records the real entry point without dispatching HTTP requests.
                g.LocationEditor.Activate("save");
                Check(g.Generation.Scheduled(g.TreeId,id,1924,"Watsonville"),"Generate action queues the edited exact-year/location snapshot");
                Check(!g.LocationEditor.IsOpen,"Save closes editor");
                Check(g.Year(id)==1924&&g.Place(id)=="Watsonville","Saved location selected immediately");
                Check(ResidenceTimeline.Load(g.TreeId,p).At(p,1924)=="Watsonville","Save persists to disk");
                Check(locations[id].At(p,1975)=="Paris","Later move preserved");
                Check(!g.IsAllTime&&g.SceneYears(id).SequenceEqual(new[]{1916,1975}),"Uncached residence retains nearest cached scenes");
                Check(g.AvailableYears(id).SequenceEqual(new[]{1916,1975}),"Old cache files retained");
                var weights=new Dictionary<string,float>();g.ScenePlaceWeights(weights);
                Check(weights.Count>0&&g.Place(id)=="Watsonville","Fallback keeps saved residence and panorama-derived location labels");
                typeof(SceneOverlay).GetMethod("Refresh",flags).Invoke(g.Overlay,null);
                typeof(SceneOverlay).GetMethod("LateUpdate",flags).Invoke(g.Overlay,null);
                Check(g.Overlay.GetComponentsInChildren<Text>(true).Any(t=>t.name=="Saved location"&&t.text=="Watsonville · 1924"),"Saved place/date remains visible beside fallback imagery");
                Check(g.Overlay.GetComponentsInChildren<Text>().Any(t=>t.text.Contains("queued")),"Generation feedback is visible after overlay refresh");
                g.EditLocation(id);
                Check((string)typeof(LocationEditor).GetField("value",flags).GetValue(g.LocationEditor)=="Watsonville","Reopen uses saved value");
                g.LocationEditor.Close();
                g.SetYear(id,1910);Check(!g.IsAllTime,"Previous residence still available");
                g.SetYear(id,1924);PanoramaCache.SaveYear(g.TreeId,id,1925,bytes,"fixture","fixture",place:"Watsonville");refresh();
                Check(!g.IsAllTime&&g.SceneYears(id).Length==3,"New residence scene joins the cached blend timeline");
                // Browsing an earlier saved entry must also move the scene to its edited year on Save.
                g.SetYear(id,1975);g.EditLocation(id);g.LocationEditor.Activate("entry");g.LocationEditor.Activate("save");
                Check(g.Year(id)==1924&&g.Place(id)=="Watsonville","Saving browsed entry follows edited year");
                string result=checks+" location-save regression checks passed; fixtures only, no paid requests.";
                File.WriteAllText("Artifacts/Locations/save-checks.txt",result);return result;
            }
            finally
            {
                typeof(AncestorGallery).GetProperty("Generation").SetValue(g,originalGeneration);UnityEngine.Object.DestroyImmediate(fixture);
                g.LocationEditor.Close();g.Tree.People.Remove(id);locations.Remove(id);g.ShowTree();refresh();
                PlayerPrefs.SetInt("overlay.actions",actions);PlayerPrefs.DeleteKey("year."+g.TreeId+"."+id);
                if(Directory.Exists(cache))Directory.Delete(cache,true);if(File.Exists(file))File.Delete(file);
            }
        }
    }
}
