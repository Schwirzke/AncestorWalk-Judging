using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace AncestorWalk.Editor
{
    public static class AsyncPanoramaHudChecks
    {
        const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        const string Report="Artifacts/Performance/async-panorama-hud-checks.txt";
        public static string Run()
        {
            var g=UnityEngine.Object.FindAnyObjectByType<AncestorGallery>();
            if(g==null||!Application.isPlaying||!g.CacheIndexReady)throw new Exception("Wait for Play mode cache indexing");
            Directory.CreateDirectory("Artifacts/Performance");File.WriteAllText(Report,"RUNNING");g.StartCoroutine(Runner(Checks(g)));return "Async panorama / HUD checks running";
        }
        static IEnumerator Runner(IEnumerator tests)
        {
            while(true)
            {
                object next=null;bool more=false;Exception error=null;
                try{more=tests.MoveNext();if(more)next=tests.Current;}catch(Exception e){error=e;}
                if(error!=null){File.WriteAllText(Report,"FAILED: "+error);Debug.LogException(error);yield break;}
                if(!more)yield break;yield return next;
            }
        }
        static byte[] Png(Color color)
        {var t=new Texture2D(256,128,TextureFormat.RGBA32,false);t.SetPixels(Enumerable.Repeat(color,256*128).ToArray());t.Apply();var png=t.EncodeToPNG();UnityEngine.Object.Destroy(t);return png;}
        static IEnumerator Checks(AncestorGallery g)
        {
            int n=0;void Check(bool ok,string why){if(!ok)throw new Exception(why);n++;}
            var nav=UnityEngine.Object.FindAnyObjectByType<GalleryNavigation>();bool navEnabled=nav.enabled,hudVisible=g.HudVisible;
            var locations=(Dictionary<string,ResidenceTimeline>)typeof(AncestorGallery).GetField("locations",Private).GetValue(g);
            var p=new Person{Id="async-hud-"+Guid.NewGuid().ToString("N"),Name="Async fixture",BirthDate="1900",DeathDate="1980",BirthPlace="York"};
            string directory=PanoramaCache.DirectoryPath,tree=g.TreeId,root=Path.Combine(directory,"people",tree,PanoramaCache.PersonKey(p.Id));
            var cache=g.FullPanoramas;var leases=new List<ScenePanoramaCache.Entry>();
            int mainThread=Thread.CurrentThread.ManagedThreadId,workerThread=mainThread;
            try
            {
                g.Tutorial.Dismiss();g.LocationEditor.Close();g.ShowTree();nav.enabled=false;g.Hud.SetVisible(true);
                g.Tree.People.Add(p.Id,p);locations[p.Id]=ResidenceTimeline.Load(tree,p);
                var png=Png(Color.blue);var job=new SceneJob{sha256=PanoramaCache.Hash(png),place="York",prompt="fixture",model="fixture",createdUtc=DateTime.UtcNow.ToString("O")};
                var save=Task.Run(()=>{workerThread=Thread.CurrentThread.ManagedThreadId;return PanoramaCache.SaveDecodedAt(directory,tree,p.Id,1930,png,job,256,128);});
                while(!save.IsCompleted)yield return null;
                Check(!save.IsFaulted&&workerThread!=mainThread,"Generated PNG validation and atomic save run on worker");
                var second=Png(Color.red);var secondJob=new SceneJob{sha256=PanoramaCache.Hash(second),place="York",model="fixture",prompt="fixture"};
                var save2=Task.Run(()=>PanoramaCache.SaveDecodedAt(directory,tree,p.Id,1950,second,secondJob,256,128));while(!save2.IsCompleted)yield return null;
                Check(!save2.IsFaulted,"Second exact-year worker save succeeds");
                string manifest=Path.Combine(root,"years","1930","scene.json"),original=File.ReadAllText(manifest);
                var invalid=Task.Run(()=>PanoramaCache.SaveDecodedAt(directory,tree,p.Id,1930,second,job,256,128));while(!invalid.IsCompleted)yield return null;
                Check(invalid.IsFaulted&&File.ReadAllText(manifest)==original,"Invalid checksum preserves previous atomic cache");_ = invalid.Exception;
                long reads=PanoramaCache.ImageReadCount,hashes=PanoramaCache.ImageValidationCount;
                g.RefreshPersonAsync(p.Id);while(g.PendingCacheRefreshes>0)yield return null;
                Check(g.CachedYears(p.Id).SequenceEqual(new[]{1930,1950}),"Worker index returns exact compatible years");
                Check(PanoramaCache.ImageReadCount==reads&&PanoramaCache.ImageValidationCount==hashes,"Async indexing reads and hashes zero PNGs on main thread");
                var timer=System.Diagnostics.Stopwatch.StartNew();g.SaveLocation(p.Id,1940,"York");timer.Stop();double saveMs=timer.Elapsed.TotalMilliseconds;
                Check(PanoramaCache.ImageReadCount==reads&&PanoramaCache.ImageValidationCount==hashes,"Location edit reuses validated records without PNG reads or hashes");
                var a=cache.Acquire(tree,p.Id,1930,g.SceneRevision(p.Id,1930));var b=cache.Acquire(tree,p.Id,1950,g.SceneRevision(p.Id,1950));leases.Add(a);leases.Add(b);
                var frames=new HashSet<int>();int commits=cache.Commits;float deadline=Time.realtimeSinceStartup+20;
                while((!a.Ready||!b.Ready)&&Time.realtimeSinceStartup<deadline){if(cache.Commits!=commits){Check(cache.Commits==commits+1&&frames.Add(cache.LastCommitFrame),"Full texture commits occur in distinct frames");commits=cache.Commits;}yield return null;}
                Check(a.Ready&&b.Ready&&!a.Failed&&!b.Failed,"Native worker decode validates both local PNGs");
                Check(a.Texture.width==(PanoramaUpscaler.Enabled?512:256)&&a.Texture.height==(PanoramaUpscaler.Enabled?256:128)&&(a.Texture is RenderTexture||a.Texture is Texture2D native&&!native.isReadable),"Scene panorama follows resolution policy without retained CPU pixels");
                Check(cache.WorkerThread!=mainThread,"Manifest and checksum validation use background worker");
                g.SetYear(p.Id,1940);g.Select(p.Id);Check(g.IsSceneLoading,"Scene texture pair installs asynchronously");
                deadline=Time.realtimeSinceStartup+10;while(g.IsSceneLoading&&Time.realtimeSinceStartup<deadline)yield return null;
                Check(!g.IsSceneLoading&&!g.IsAllTime&&RenderSettings.skybox.mainTexture==a.Texture,"Scene consumes shared full-resolution lease");
                Check(RenderSettings.skybox.GetTexture("_SecondTex")==b.Texture&&Mathf.Abs(RenderSettings.skybox.GetFloat("_Blend")-.5f)<.001f,"Scene pair and blend match exact selected year");
                int warmReads=cache.Reads;g.SetYear(p.Id,1942);yield return null;
                Check(!g.IsSceneLoading&&cache.Reads==warmReads,"Warm same-pair scrub does not reread or decode PNGs");
                g.ShowTree();yield return null;
                Check(a.References==1&&b.References==1,"Leaving scene releases its leases while explicit test leases remain");
                var cancelled=cache.Acquire(tree,p.Id,1970,"cancel");cache.Release(cancelled);yield return null;
                Check(cache.Reads==warmReads,"Unreferenced queued panorama is cancelled before disk read");
                var interrupted=cache.Acquire(tree,p.Id,1930,"cancel-active");yield return null;cache.Release(interrupted);
                deadline=Time.realtimeSinceStartup+10;while(!interrupted.Ready&&Time.realtimeSinceStartup<deadline)yield return null;
                var retry=cache.Acquire(tree,p.Id,1930,"cancel-active");leases.Add(retry);deadline=Time.realtimeSinceStartup+10;while(!retry.Ready&&Time.realtimeSinceStartup<deadline)yield return null;
                Check(retry.Ready&&!retry.Failed,"Returning after an interrupted load retries successfully");
                var missing=cache.Acquire(tree,p.Id,1971,"missing");leases.Add(missing);deadline=Time.realtimeSinceStartup+10;while(!missing.Ready&&Time.realtimeSinceStartup<deadline)yield return null;
                Check(missing.Ready&&missing.Failed&&missing.Texture==null,"Unavailable scene fails cleanly");
                var controls=g.GetComponentsInChildren<CubeFacingControls>();int count=controls.Length;
                g.Hud.SetVisible(false);yield return null;
                Check(!g.HudVisible&&PlayerPrefs.GetInt(HudOverlay.PreferenceKey)==0,"HUD hidden setting persists");
                Check(!g.TreeControls.GetComponent<Canvas>().enabled&&!g.Tutorial.GetComponent<Canvas>().enabled,"Tree and Help overlays hide");
                Check(controls.All(c=>c!=null&&c.gameObject.activeInHierarchy)&&g.GetComponentsInChildren<CubeFacingControls>().Length==count,"Sphere panels and portals retain their independent state");
                Check(g.Hud.GetComponent<Canvas>().enabled,"Restore HUD button remains visible");
                Check(g.Hud.HandleInput(default,false,false,false,true)&&g.HudVisible,"HUD keyboard/controller shortcut restores overlays");
                g.TreeControls.SetExpanded(false);yield return null;Canvas.ForceUpdateCanvases();
                var target=(RectTransform)g.TreeControls.transform.Find("Toggle flight controls");var ray=new Ray(target.position-target.forward*10,target.forward);
                g.Hud.Observe(ray,true,false);yield return new WaitForSecondsRealtime(.45f);g.Hud.Observe(ray,true,false);
                Check(g.Hud.TooltipVisible&&g.Hud.TooltipText=="Open flight controls","Collapsed tree icon tooltip appears after pointer dwell");
                g.Hud.Observe(ray,true,true);Check(!g.Hud.TooltipVisible,"Tooltip hides during active pointer input");
                g.Hud.SetVisible(false);g.Select(p.Id);yield return null;
                Check(!g.HudVisible&&!g.Overlay.GetComponent<Canvas>().enabled,"Hidden HUD stays hidden when entering scene");
                Check(!g.Overlay.HandleInput(default,true,true,true),"Hidden scene overlay does not consume pointer input");
                g.Hud.SetVisible(true);yield return null;Canvas.ForceUpdateCanvases();
                var clock=(RectTransform)g.Overlay.transform.Find("Toggle timeline");if(g.Overlay.TimelineOpen)g.Overlay.ToggleTimeline();
                ray=new Ray(clock.position-clock.forward*10,clock.forward);g.Hud.Observe(ray,true,false);yield return new WaitForSecondsRealtime(.45f);g.Hud.Observe(ray,true,false);
                Check(g.Hud.TooltipVisible&&g.Hud.TooltipText=="Open timeline","Collapsed scene timeline has pointer tooltip");
                g.Hud.SetVisible(false);g.ShowTree();yield return null;
                Check(!g.HudVisible&&!g.TreeControls.GetComponent<Canvas>().enabled,"Hidden HUD survives return to tree");
                File.WriteAllText(Report,n+" async panorama/HUD checks passed. Worker thread "+workerThread+", main "+mainThread+". Location edit "+saveMs.ToString("F2")+" ms; zero main-thread PNG reads/hashes. Native decode, frame pacing, shared leases, cancellation, missing files, exact-year blending, overlay-only hiding, persisted visibility, restore shortcut, pointer tooltips. Isolated fixtures; no API calls.");
            }
            finally
            {
                foreach(var e in leases)cache.Release(e);
                g.Tree.People.Remove(p.Id);locations.Remove(p.Id);g.ShowTree();g.Hud.SetVisible(hudVisible);nav.enabled=navEnabled;
                PlayerPrefs.DeleteKey("year."+tree+"."+p.Id);
                if(Directory.Exists(root))Directory.Delete(root,true);
                string location=Path.Combine(PanoramaCache.StorageRoot,"Locations",tree,PanoramaCache.PersonKey(p.Id)+".json");if(File.Exists(location))File.Delete(location);
            }
        }
    }
}
