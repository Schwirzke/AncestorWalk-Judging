using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
namespace AncestorWalk.Editor
{
    public static class ExactYearChecks
    {
        const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        public static string Run()
        {
            if(!Application.isPlaying)throw new Exception("Play mode required");
            int n=0;void Check(bool ok,string why){if(!ok)throw new Exception(why);n++;}
            bool Same(Color a,Color b)=>Mathf.Abs(a.r-b.r)<.001f&&Mathf.Abs(a.g-b.g)<.001f&&Mathf.Abs(a.b-b.b)<.001f;
            var g=UnityEngine.Object.FindAnyObjectByType<AncestorGallery>();var nav=UnityEngine.Object.FindAnyObjectByType<GalleryNavigation>();
            var p=new Person{Id="exact-year-"+Guid.NewGuid().ToString("N"),Name="Exact year fixture",BirthDate="1896",DeathDate="1975",BirthPlace="York",DeathPlace="York"};
            var locations=(Dictionary<string,ResidenceTimeline>)typeof(AncestorGallery).GetField("locations",Private).GetValue(g);
            string selected=g.SelectedId;var position=nav.transform.position;var rotation=nav.transform.rotation;var look=nav.view.transform.localRotation;
            bool seenHad=PlayerPrefs.HasKey(TutorialOverlay.SeenKey);int seen=PlayerPrefs.GetInt(TutorialOverlay.SeenKey);
            string cache=Path.Combine(PanoramaCache.DirectoryPath,"people",g.TreeId,PanoramaCache.PersonKey(p.Id));
            string locationFile=Path.Combine(PanoramaCache.StorageRoot,"Locations",g.TreeId,PanoramaCache.PersonKey(p.Id)+".json");
            var sceneRefresh=typeof(SceneOverlay).GetMethod("Refresh",Private);var mode=typeof(SceneOverlay).GetMethod("ConfigureForMode",Private);
            Color World(Renderer r){var block=new MaterialPropertyBlock();r.GetPropertyBlock(block);return block.GetColor("_Color");}
            try
            {
                g.Tutorial.Dismiss();nav.GetComponent<NavigationPreferences>().SetOpen(false);g.Tree.People.Add(p.Id,p);locations[p.Id]=ResidenceTimeline.Load(g.TreeId,p);
                locations[p.Id].Save(p,1934,"Monterey");
                for(int year=1896;year<=1975;year++)Check(locations[p.Id].GenerationYear(p,year)==year,"Every integer year stays exact");
                Check(typeof(LifeTimeline).GetMethod("Bucket")==null,"Five-year rounding helper removed");
                var retry=typeof(SceneGeneration).GetMethod("ValidatePendingTarget",BindingFlags.Static|BindingFlags.NonPublic);
                var job=new SceneJob{year=1936,place="Monterey"};retry.Invoke(null,new object[]{job,1936," Monterey "});Check(true,"Matching pending target can resume");
                foreach(var target in new[]{new SceneJob{year=1934,place="Monterey"},new SceneJob{year=1936,place="Paris"}})
                {bool rejected=false;try{retry.Invoke(null,new object[]{job,target.year,target.place});}catch(TargetInvocationException error){rejected=error.InnerException is InvalidOperationException;}Check(rejected,"Mismatched pending year/location rejected without submitting or retargeting");}
                var image=new Texture2D(256,128,TextureFormat.RGBA32,false);var pixels=new Color32[256*128];for(int i=0;i<pixels.Length;i++)pixels[i]=new Color32(70,120,160,255);image.SetPixels32(pixels);image.Apply();byte[] bytes=image.EncodeToPNG();UnityEngine.Object.Destroy(image);
                var old=PanoramaCache.SaveYear(g.TreeId,p.Id,1936,bytes,"fixture","fixture",place:"Monterey");g.RefreshPerson(p.Id);
                var otherYears=g.AvailableYears("@I3@");
                g.ShowTree();nav.FrameNode(p.Id);
                var controls=g.GetComponentsInChildren<CubeFacingControls>().Single(c=>c.GetComponent<CubePanoramaPreview>().PersonId==p.Id);
                var thumb=controls.Panel.GetComponentsInChildren<Renderer>(true).Single(r=>r.name=="Selected year");
                var advance=typeof(CubeFacingControls).GetMethod("AdvanceFacing",Private);
                int pool=controls.PoolBuilds,objects=controls.GetComponentsInChildren<Transform>(true).Length;
                foreach(int year in new[]{1934,1935,1936})
                {
                    g.SetYear(p.Id,year);advance.Invoke(controls,new object[]{.3f});
                    Color expected=year==1934?TimelineMarker.PendingColor:year==1935?g.teal.color:g.gold.color;
                    Check(Same(World(thumb),expected),"Sphere thumb state at "+year);Check(g.GenerationYear(p.Id)==year,"Generation target exact at "+year);
                }
                nav.transform.position+=Vector3.right*3;advance.Invoke(controls,new object[]{.04f});g.SetYear(p.Id,1934);
                var visibleThumbs=controls.GetComponentsInChildren<MeshRenderer>().Where(r=>r.sortingOrder==30).ToArray();Check(visibleThumbs.Length>=2,"Crossfade thumb copies visible");
                foreach(var copy in visibleThumbs)Check(Same(World(copy),TimelineMarker.PendingColor),"Purple synchronized across fading faces");
                Check(pool==controls.PoolBuilds&&objects==controls.GetComponentsInChildren<Transform>(true).Length,"Three-state scrubbing creates no panels");
                g.Select(p.Id);var ui=g.Overlay;if(!ui.TimelineOpen)ui.ToggleTimeline();
                void CheckScene(int year,Color color,string action)
                {
                    g.SetYear(p.Id,year);sceneRefresh.Invoke(ui,null);
                    var t=ui.GetComponentsInChildren<RectTransform>(true).Single(r=>r.name=="Selected year").GetComponent<Image>();
                    Check(Same(t.color,color),"Scene thumb at "+year);Check(ui.GetComponentsInChildren<Text>(true).Any(t=>t.text==action+" "+year),"Exact action label at "+year);
                }
                foreach(bool vr in new[]{false,true})
                {
                    mode.Invoke(ui,new object[]{vr});
                    CheckScene(1934,TimelineMarker.PendingColor,"GENERATE");Check(!g.IsAllTime,"Pending move retains nearby cached scene");
                    CheckScene(1935,g.teal.color,"GENERATE");
                    g.SetYear(p.Id,1936);sceneRefresh.Invoke(ui,null);var ticks=(List<Image>)typeof(SceneOverlay).GetField("tickImages",Private).GetValue(ui);
                    CheckScene(1936,ticks.Last(i=>i.gameObject.activeSelf).color,"REGENERATE");
                }
                PanoramaCache.SaveYear(g.TreeId,p.Id,1934,bytes,"exact fixture","fixture",place:"Monterey");g.RefreshPerson(p.Id);
                Check(g.AvailableYears(p.Id).SequenceEqual(new[]{1934,1936}),"Exact generation adds year without replacing neighbor");
                PanoramaCache.ReadYear(g.TreeId,p.Id,1936,out var retained);Check(retained.sha256==old.sha256&&retained.createdUtc==old.createdUtc,"Existing neighboring panorama preserved");
                var generatedTicks=(List<Image>)typeof(SceneOverlay).GetField("tickImages",Private).GetValue(ui);sceneRefresh.Invoke(ui,null);
                CheckScene(1934,generatedTicks[0].color,"REGENERATE");Check(g.Markers(p.Id).Single(m=>m.Year==1934).Generated,"Move marker changes purple to gold");
                Check(ReferenceEquals(otherYears,g.AvailableYears("@I3@")),"Refreshing fixture does not refresh Rose's cache");
                g.SaveLocation(p.Id,1934,"Paris");CheckScene(1934,TimelineMarker.PendingColor,"GENERATE");Check(File.Exists(Path.Combine(cache,"years","1936","scene.json")),"Incompatible cache files preserved");
                g.SaveLocation(p.Id,1934,"Monterey");sceneRefresh.Invoke(ui,null);CheckScene(1934,generatedTicks[0].color,"REGENERATE");
                g.SaveLocation(p.Id,1935,"Berlin");CheckScene(1935,TimelineMarker.PendingColor,"GENERATE");Check(!g.IsAllTime,"New pending move still uses nearest cache");
                g.SaveLocation(p.Id,1935,"",true);CheckScene(1935,g.teal.color,"GENERATE");
                foreach(int year in new[]{1896,1975})Check(g.Locations(p.Id).GenerationYear(p,year)==year,"Exact lifespan endpoint");
                var result=n+" exact-year checks passed; three-state thumbs, exact actions, isolated generation/cache compatibility, pooled fades, desktop/VR; no paid requests or Quest deployment.";
                File.WriteAllText("Artifacts/exact-year-checks.txt",result);return result;
            }
            finally
            {
                g.LocationEditor.Close();g.Tree.People.Remove(p.Id);locations.Remove(p.Id);g.ShowTree();if(selected!=null)g.Select(selected);
                nav.transform.SetPositionAndRotation(position,rotation);nav.view.transform.localRotation=look;
                PlayerPrefs.DeleteKey("year."+g.TreeId+"."+p.Id);string prefix="overlay."+g.TreeId+"."+PanoramaCache.PersonKey(p.Id)+".";
                foreach(string key in new[]{"timeline","info","actions"})PlayerPrefs.DeleteKey(prefix+key);
                if(seenHad)PlayerPrefs.SetInt(TutorialOverlay.SeenKey,seen);else PlayerPrefs.DeleteKey(TutorialOverlay.SeenKey);PlayerPrefs.Save();
                if(Directory.Exists(cache))Directory.Delete(cache,true);if(File.Exists(locationFile))File.Delete(locationFile);
            }
        }
    }
}
