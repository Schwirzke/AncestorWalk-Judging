using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
namespace AncestorWalk.Editor
{
    public static class AllTimeChecks
    {
        const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        static Dictionary<string,ResidenceTimeline> Locations(AncestorGallery g)=>(Dictionary<string,ResidenceTimeline>)typeof(AncestorGallery).GetField("locations",Private).GetValue(g);
        static void Refresh(AncestorGallery g)=>typeof(AncestorGallery).GetMethod("RefreshCards",Private).Invoke(g,null);
        static void Notify(AncestorGallery g,string id,string message)=>typeof(SceneGeneration).GetMethod("Notify",Private).Invoke(g.Generation,new object[]{id,message});
        static Person Add(AncestorGallery g,string id,string birth="1900")
        {
            var p=new Person{Id=id,Name="New ancestor · preview",BirthDate=birth,DeathDate="1980",BirthPlace="York"};
            g.Tree.People.Add(id,p);Locations(g)[id]=ResidenceTimeline.Load(g.TreeId,p);Refresh(g);return p;
        }
        public static string Run()
        {
            var g=UnityEngine.Object.FindAnyObjectByType<AncestorGallery>();
            if(!Application.isPlaying||g==null)throw new Exception("Play mode required");
            g.StartCoroutine(Checks(g));return "All Time checks running";
        }
        static IEnumerator Checks(AncestorGallery g)
        {
            int n=0;void Check(bool ok,string msg){if(!ok)throw new Exception(msg);n++;}
            string id="alltime-check-"+Guid.NewGuid().ToString("N");var p=Add(g,id);
            string otherId=id+"-other";Add(g,otherId);
            string cache=Path.Combine(PanoramaCache.DirectoryPath,"people",g.TreeId,PanoramaCache.PersonKey(id));
            string location=Path.Combine(PanoramaCache.StorageRoot,"Locations",g.TreeId,PanoramaCache.PersonKey(id)+".json");
            int actions=PlayerPrefs.GetInt("overlay.actions",0);var shared=Resources.Load<Texture2D>("AllTimePanorama");
            string overlayPrefix="overlay."+g.TreeId+"."+PanoramaCache.PersonKey(id)+".";
            try
            {
                Check(shared!=null&&shared.width==2*shared.height,"Bundled equirectangular texture");
                g.ShowTree();g.ResetTreePan();g.PanTree(new Vector2(1,-1));var pan=g.TreePan;
                yield return null; // Destroyed prior tree roots leave the hierarchy at frame end.
                foreach(var target in g.GetComponentsInChildren<GalleryTarget>(true))Check(target.action==GalleryAction.EnterScene||target.action==GalleryAction.GenerateScene,"Tree exposes Enter and exact-year generation");
                Check(g.GetComponentsInChildren<GalleryTarget>(true).Count(t=>t.action==GalleryAction.EnterScene)==g.Tree.People.Count,"Every cube has Enter, including distance-hidden portals");
                g.EditLocation(id);Check(!g.Generation.Busy&&!g.LocationEditor.IsOpen,"Location editing remains gated to scene");
                PlayerPrefs.SetInt("overlay.actions",0);g.SetYear(id,1936);g.Select(id);yield return null;
                Check(g.IsAllTime&&g.SelectedId==id&&g.Year(id)==1936,"Uncached entry keeps year");
                Check(RenderSettings.skybox.mainTexture==shared,"Default panorama applied");
                Check(g.Overlay.TimelineOpen&&!g.Overlay.GetComponentsInChildren<RectTransform>().Any(t=>t.name=="Scene actions"||t.name=="Person information"),"Timeline opens on entry while other panels remain collapsed");
                Check(!PlayerPrefs.HasKey(overlayPrefix+"timeline"),"Initial open state does not write preferences");
                g.Overlay.ToggleTimeline();Check(!g.Overlay.TimelineOpen,"Timeline can still collapse explicitly");
                g.Select(id);yield return null;Check(g.Overlay.TimelineOpen,"Scene re-entry opens a previously collapsed timeline");
                g.Select(otherId);yield return null;
                Check(g.Overlay.TimelineOpen,"Another first-visit person also opens its timeline");
                g.Select(id);yield return null;Check(g.Overlay.TimelineOpen,"Timeline opens on each scene entry");g.Overlay.ToggleTimeline();
                Check(!g.HasScene(id)&&!Directory.Exists(cache),"Default is not a cached scene");
                Check(!g.GetComponentInChildren<HistoricalEnvironment>().GetComponentsInChildren<Renderer>().Any(r=>r.enabled),"No courtyard objects");
                Notify(g,id,"Generation failed. Fixture response.");yield return null;
                Check(g.IsAllTime&&g.Overlay.GetComponentsInChildren<Text>(true).Any(t=>t.text.Contains("Fixture response")),"Failure preserves default and reports error");
                var bytes=File.ReadAllBytes("Assets/AncestorWalk/Resources/ClaraYork1870Panorama.png");
                PanoramaCache.SaveYear(g.TreeId,id,1935,bytes,"test fixture","fixture",0,null,"York");
                Notify(g,id,"Scene saved for 1935.");
                ((Action<string>)typeof(SceneGeneration).GetField("SceneSaved",Private).GetValue(g.Generation)).Invoke(id);
                Check(!g.IsAllTime&&RenderSettings.skybox.GetFloat("_Transition")==0,"Success starts dissolve from default");
                yield return new WaitForSecondsRealtime(.5f);
                Check(RenderSettings.skybox.GetFloat("_Transition")==1&&shared!=null,"Transition finishes without destroying shared asset");
                PanoramaCache.SaveYear(g.TreeId,id,1955,bytes,"test fixture","fixture",0,null,"York");Refresh(g);g.SetYear(id,1945);
                Check(Mathf.Abs(RenderSettings.skybox.GetFloat("_Blend")-.5f)<.001f,"Cached year blending preserved");
                g.EditLocation(id);Check(g.LocationEditor.IsOpen,"In-scene editing opens");g.LocationEditor.Close();
                g.SaveLocation(id,1900,"Monterey");Check(g.IsAllTime&&Directory.Exists(cache),"Invalidated scenes restore default, preserve files");
                g.SaveLocation(id,1900,"York");Check(!g.IsAllTime,"Restoring location reuses cache");
                g.ShowTree();Check(g.TreePan==pan,"Pan restored");g.Select(id);yield return null;
                Check(!g.IsAllTime&&!g.Overlay.GetComponentsInChildren<RectTransform>().Any(t=>t.name=="Scene actions"),"Cached entry keeps collapsed preference");
                p.BirthDate=p.DeathDate="";g.Select(id);yield return null;
                Check(g.IsAllTime&&g.Overlay.GetComponentsInChildren<Text>(true).Any(t=>t.text=="YEAR NEEDED"),"Unknown lifespan enters default with explanation");
                g.EditLocation(id);g.GenerateScene(id);Check(!g.LocationEditor.IsOpen&&!g.Generation.Busy,"Unknown lifespan actions disabled");
                g.ShowTree();yield return null;Check(g.GetComponentsInChildren<GalleryTarget>(true).Count(t=>t.action==GalleryAction.EnterScene)==g.Tree.People.Count,"Unknown lifespan still has Enter");
                yield return null;Check(shared!=null,"Shared default survives scene exit");
                Directory.CreateDirectory("Artifacts");File.WriteAllText("Artifacts/all-time-checks.txt",n+" All Time checks passed; simulated generation completion/failure, no paid API requests.");
            }
            finally
            {
                g.ShowTree();g.Tree.People.Remove(id);Locations(g).Remove(id);g.Tree.People.Remove(otherId);Locations(g).Remove(otherId);Refresh(g);g.ShowTree();PlayerPrefs.SetInt("overlay.actions",actions);
                if(Directory.Exists(cache))Directory.Delete(cache,true);if(File.Exists(location))File.Delete(location);
                PlayerPrefs.DeleteKey("year."+g.TreeId+"."+id);
                foreach(string panel in new[]{"timeline","info","actions"})PlayerPrefs.DeleteKey(overlayPrefix+panel);
                foreach(string panel in new[]{"timeline","info","actions"})PlayerPrefs.DeleteKey("overlay."+g.TreeId+"."+PanoramaCache.PersonKey(otherId)+"."+panel);
                PlayerPrefs.DeleteKey("year."+g.TreeId+"."+otherId);
            }
        }
        public static string ShowDemo()
        {
            var g=UnityEngine.Object.FindAnyObjectByType<AncestorGallery>();const string id="all-time-preview";
            if(!g.Tree.People.ContainsKey(id))Add(g,id);
            g.SetYear(id,1936);g.Select(id);return "Temporary uncached ancestor shown; no family data or cache files changed.";
        }
    }
}
