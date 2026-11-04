using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;

namespace AncestorWalk.Editor
{
    public static class GenerationMarkerChecks
    {
        const BindingFlags Hidden=BindingFlags.NonPublic|BindingFlags.Instance;
        public static string Data()
        {
            int n=0;void Check(bool value,string why){if(!value)throw new Exception(why);n++;}
            var p=new Person{BirthDate="1900",DeathDate="1980",BirthPlace="York",DeathPlace="York"};var moves=new ResidenceTimeline();moves.entries.Add(new Residence{year=1940,place="York"});
            var markers=TimelineMarker.Build(p,moves,new[]{1900},new[]{1900,1910,1940,1910,1999});
            Check(markers.Select(x=>x.Year).SequenceEqual(new[]{1900,1910,1940}),"Active exact years union/deduplicate with cache/moves and stay inside range");
            Check(markers.All(x=>x.Generating),"Cached regeneration and new years both carry active state");
            Check(TimelineMarker.SelectionState(1900,markers)==TimelineSelectionState.Generated,"Regenerating cached year keeps gold thumb");
            Check(TimelineMarker.SelectionState(1910,markers)==TimelineSelectionState.Unmarked,"New generating year keeps teal thumb");
            Check(TimelineMarker.SelectionState(1940,markers)==TimelineSelectionState.PendingMove,"Generating saved move keeps purple thumb");
            Check(!TimelineMarker.Build(p,moves,new[]{1900}).Any(x=>x.Generating),"Completion/failure removes active state");
            Check(TimelineMarker.BlinkAlpha(.1f)==1&&Mathf.Approximately(TimelineMarker.BlinkAlpha(.7f),.2f),"Blink alternates bright/dim once per second");
            Check(TimelineMarker.BlinkAlpha(.1f)==TimelineMarker.BlinkAlpha(1.1f),"Shared blink phase repeats consistently");
            p.DeathDate="";Check(TimelineMarker.Build(p,new ResidenceTimeline(),Array.Empty<int>(),new[]{1900}).Single().Generating,"Single known year supports a generating marker without inferred lifespan");
            Check(!TimelineMarker.Same(markers,TimelineMarker.Build(new Person{BirthDate="1900",DeathDate="1980"},moves,new[]{1900})),"Marker state comparison notices generation transitions");
            var queued=TimelineMarker.Build(new Person{BirthDate="1900",DeathDate="1980",BirthPlace="York",DeathPlace="York"},moves,new[]{1900},new[]{1910},new[]{1900,1910,1940,1950});
            Check(queued.Single(m=>m.Year==1950).Queued&&queued.Single(m=>m.Year==1950).Blinking&&queued.Single(m=>m.Year==1950).DisplayColor(Color.yellow)==TimelineMarker.QueuedColor,"Queued exact years blink blue");
            Check(queued.Single(m=>m.Year==1910).Generating&&!queued.Single(m=>m.Year==1910).Queued,"Active green state takes precedence over queued blue");
            Check(TimelineMarker.SelectionState(1900,queued)==TimelineSelectionState.Generated&&TimelineMarker.SelectionState(1940,queued)==TimelineSelectionState.PendingMove&&TimelineMarker.SelectionState(1950,queued)==TimelineSelectionState.Unmarked,"Queued state preserves generated/move/unmarked thumb colors");
            Check(new TimelineMarker(1934,true).Tooltip("Monterey")=="1934\nMonterey","Generated marker tooltip includes exact year and location");
            Check(new TimelineMarker(1934,false,true).Tooltip("Monterey").Contains("Saved location · not generated"),"Saved move tooltip distinguishes missing generation");
            Check(new TimelineMarker(1934,true,false,true).Tooltip("Monterey").Contains("Regenerating"),"Active replacement tooltip preserves generated state");
            Check(new TimelineMarker(1934,false,false,false,false,true).Tooltip("Monterey").Contains("Queued"),"Waiting tooltip reports queued state");
            Check(new TimelineMarker(1934,false,false,false,true).Tooltip(null).Contains("Missing location\nLocation not set"),"Blank endpoint tooltip explains missing location");
            var overlapping=new[]{new TimelineMarker(1934,true),new TimelineMarker(1975,false,true)};
            Check(TimelineMarker.CoveredMarker(1934,overlapping,1900,2000,720,10,5)==0,"Thumb inherits exact-year marker tooltip");
            Check(TimelineMarker.CoveredMarker(1935,overlapping,1900,2000,720,10,5)==0,"Thumb inherits a visually covered nearby marker");
            Check(TimelineMarker.CoveredMarker(1950,overlapping,1900,2000,720,10,5)==-1,"Unmarked thumb does not invent a marker tooltip");
            var timing=new GenerationTimingHistory();
            Check(timing.Average(false)==60,"Empty history has a bounded initial duration");
            timing.Record(false,20);timing.Record(false,40);timing.Record(false,60);timing.Record(true,100);
            Check(timing.Average(false)==40&&timing.Average(true)==55,"Matching request type uses its mean when enough samples exist");
            Check(GenerationTimingHistory.Percent(30,60)==50&&GenerationTimingHistory.Percent(300,60)==95&&GenerationTimingHistory.Percent(60,60,true)==100,"Text percentage tracks average and only completes after saving");
            Check(GenerationTimingHistory.Percent(-5,60)==0&&GenerationTimingHistory.Percent(5,0)==0,"Invalid or backwards time never produces an invalid percentage");
            timing.Record(false,double.NaN);timing.Record(false,-10);Check(timing.samples.Count==4,"Invalid timings ignored");
            for(int i=0;i<40;i++)timing.Record(false,i+1);
            timing=JsonUtility.FromJson<GenerationTimingHistory>(JsonUtility.ToJson(timing));timing.Normalize();
            Check(timing.samples.Count==30&&timing.samples.First().seconds==11&&timing.samples.Last().seconds==40,"Last thirty samples survive persistence");
            string result=n+" generation-marker data checks passed.";File.WriteAllText("Artifacts/generation-marker-data.txt",result);return result;
        }
        public static string Play()
        {
            var g=UnityEngine.Object.FindAnyObjectByType<AncestorGallery>();if(!Application.isPlaying||g?.Tree==null||g.Generation.Busy)throw new Exception("Idle Play mode required");
            File.WriteAllText("Artifacts/generation-marker-play.txt","RUNNING");g.StartCoroutine(Runner(Checks(g)));return "Running marker/progress UI against an isolated fake helper; no Meta calls.";
        }
        static IEnumerator Runner(IEnumerator routine)
        {
            var stack=new Stack<IEnumerator>();stack.Push(routine);
            while(stack.Count>0)
            {
                var current=stack.Peek();bool more=false;Exception error=null;try{more=current.MoveNext();}catch(Exception e){error=e;}
                if(error!=null){while(stack.Count>0)(stack.Pop() as IDisposable)?.Dispose();File.WriteAllText("Artifacts/generation-marker-play.txt","FAILED: "+error);Debug.LogException(error);yield break;}
                if(!more){(stack.Pop() as IDisposable)?.Dispose();continue;}if(current.Current is IEnumerator nested){stack.Push(nested);continue;}yield return current.Current;
            }
        }
        static IEnumerator Checks(AncestorGallery g)
        {
            int n=0;void Check(bool value,string why){if(!value)throw new Exception(why);n++;}
            IEnumerator Until(Func<bool> value,string why){float end=Time.realtimeSinceStartup+20;while(!value()&&Time.realtimeSinceStartup<end)yield return null;Check(value(),why);}
            var original=g.Document;string selected=g.SelectedId;var originalGen=g.Generation;bool hud=g.HudVisible;
            var doc=new TreeDocument{Id=TreeStore.NewId(),Name="Generation UI fixture"};
            var p=new Person{Id="marker",Name="Marker fixture",BirthDate="1900",DeathDate="1980",BirthPlace="York",DeathPlace="York"};
            var single=new Person{Id="single",Name="Single year fixture",BirthDate="1920",BirthPlace="York"};doc.People.Add(p);doc.People.Add(single);
            var image=new Texture2D(256,128,TextureFormat.RGBA32,false);image.SetPixels(Enumerable.Repeat(Color.blue,256*128).ToArray());image.Apply();byte[] png=image.EncodeToPNG();UnityEngine.Object.Destroy(image);
            PanoramaCache.SaveDecodedAt(PanoramaCache.DirectoryPath,doc.Id,p.Id,1900,png,new SceneJob{sha256=PanoramaCache.Hash(png),place="York",prompt="fixture",model="fixture"},256,128);
            GameObject go=null;SceneGeneration gen=null;Action<string> refresh=null;var release=new ManualResetEvent(false);SceneJob held=null;
            using var helper=new SceneQueueChecks.FakeHelper(png){Hold=true,Stage="researching",SettingBrief=new SceneSettingBrief{version=1,text="Synthetic researched street",sources=new[]{new GuideSource{title="Synthetic archive",url="https://example.org/archive"}}}};
            void HudRefresh()=>typeof(HudOverlay).GetMethod("UpdateGenerationProgress",Hidden).Invoke(g.Hud,new object[]{false});
            RectTransform Progress()=>g.Hud.GetComponentsInChildren<RectTransform>(true).Single(r=>r.name=="Scene generation progress");
            try
            {
                g.Library.Close();g.Batch.Close();g.Tutorial.Dismiss();g.OpenDocument(doc);yield return Until(()=>g.CacheIndexReady,"Fixture cache indexed");g.Locations(p.Id).entries.Add(new Residence{year=1940,place="York"});g.RefreshPerson(p.Id);
                go=new GameObject("Isolated generation UI queue");go.SetActive(false);gen=go.AddComponent<SceneGeneration>();
                typeof(SceneGeneration).GetField("TestStorage",Hidden).SetValue(gen,Path.GetFullPath("Library/GenerationMarkerChecks/"+Guid.NewGuid().ToString("N")));
                typeof(SceneGeneration).GetField("TestConnection",Hidden).SetValue(gen,new SceneServiceConnection{url=helper.Url,token=new string('a',64)});go.SetActive(true);yield return Until(()=>gen.Ready,"Fixture queue ready");
                typeof(AncestorGallery).GetProperty("Generation").SetValue(g,gen);
                refresh=(Action<string>)Delegate.CreateDelegate(typeof(Action<string>),g,typeof(AncestorGallery).GetMethod("RefreshGenerationMarkers",Hidden));gen.TargetChanged+=refresh;
                gen.Enqueue(doc.Id,p,false,new[]{(1900,"York"),(1910,"York"),(1940,"York"),(1921,"York"),(1922,"York"),(1923,"York"),(1924,"York")});gen.Enqueue(doc.Id,single,false,new[]{(1920,"York")});gen.Enqueue(doc.Id,p,false,new[]{(1950,"York")});
                yield return Until(()=>helper.Requests.Count==SceneGeneration.MaximumConcurrent,"Eight fixture years actively generating");
                Check(g.Markers(p.Id).Count(m=>m.Generating)==7&&g.Markers(single.Id).Single().Generating,"Only exact active years become green markers, including single-year record");
                Check(g.Markers(p.Id).Single(m=>m.Year==1950).Queued&&!g.Markers(p.Id).Single(m=>m.Year==1950).Generating,"Queued year is blue instead of claiming active generation");
                var control=g.GetComponentsInChildren<TreeNodeTarget>().Single(x=>x.PersonId==p.Id).Controls;control.SetInteractionRange(20);
                var nav=UnityEngine.Object.FindAnyObjectByType<GalleryNavigation>();nav.FrameNode(p.Id);
                var advance=(Action<float>)Delegate.CreateDelegate(typeof(Action<float>),control,typeof(CubeFacingControls).GetMethod("AdvanceFacing",Hidden));advance(.4f);
                var block=new MaterialPropertyBlock();var ticks=control.Panel.GetComponentsInChildren<Renderer>(true).Where(r=>r.name=="Cached year").ToArray();
                foreach(var tick in ticks){tick.GetPropertyBlock(block);Check(block.GetFloat("_Blink")==1&&tick.sortingOrder>30,"World green markers blink in shader above thumb");Check(tick.GetComponent<Collider>()==null||!tick.GetComponent<Collider>().enabled,"Generation ticks do not intercept slider input");}
                int pool=control.PoolBuilds;var center=control.transform.position;nav.transform.position+=center+Vector3.right*2-nav.view.transform.position;advance(.05f);
                var ghost=control.transform.Find("Pooled panel face 0");Check(ghost.gameObject.activeSelf,"Outgoing panel face is fading");
                foreach(var mesh in ghost.GetComponentsInChildren<Renderer>())if(mesh.sortingOrder==35){mesh.GetPropertyBlock(block);Check(block.GetFloat("_Blink")==1,"Outgoing face retains synchronized shader blink");}
                advance(.4f);long before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<1000;i++)advance(.016f);Check(GC.GetAllocatedBytesForCurrentThread()==before&&control.PoolBuilds==pool,"Blinking/facing creates no pooled objects or recurring managed allocations");
                g.SetYear(p.Id,1910);g.Select(p.Id);HudRefresh();Check(Progress().gameObject.activeSelf,"Selected active scene shows progress even with panels collapsed");
                Check(Progress().GetComponentInChildren<Text>().text.Contains("1910"),"Progress label identifies exact selected year");
                var activeProgress=gen.ActiveJobs.Single(x=>x.personId==p.Id&&x.job.year==1910);activeProgress.startedUnix=SceneRetryPolicy.UnixNow-30;activeProgress.expectedSeconds=60;HudRefresh();
                Check(Progress().GetComponentInChildren<Text>().text.EndsWith("50%")&&!Progress().GetComponentInChildren<Text>().text.Contains("Estimated"),"Scene progress shows only numeric percentage beneath stage text");
                yield return Until(()=>gen.TryGetProgress(doc.Id,p.Id,1910,out var text)&&text=="Researching 1910…","Researching stage reaches exact-year HUD");
                helper.Stage="generating";yield return Until(()=>gen.TryGetProgress(doc.Id,p.Id,1910,out var text)&&text=="Generating 1910…","Image stage replaces research label");
                g.Hud.SetVisible(false);HudRefresh();Check(Progress().gameObject.activeSelf,"Persistent scene status remains visible with HUD overlays hidden");g.Hud.SetVisible(true);
                foreach(bool vr in new[]{false,true})
                {
                    typeof(HudOverlay).GetMethod("Configure",Hidden).Invoke(g.Hud,new object[]{vr});HudRefresh();
                    var toggle=g.Hud.GetComponentsInChildren<RectTransform>(true).Single(r=>r.name=="Toggle HUD");
                    Check(Progress().anchoredPosition.y+Progress().rect.height*.5f<toggle.anchoredPosition.y-toggle.rect.height*.5f,"Progress bar stays below HUD toggle without overlap");
                }
                typeof(HudOverlay).GetMethod("Configure",Hidden).Invoke(g.Hud,new object[]{UnityEngine.XR.XRSettings.isDeviceActive});
                yield return null;yield return null;Canvas.ForceUpdateCanvases();HudRefresh();
                g.Overlay.ToggleTimeline();typeof(SceneOverlay).GetMethod("Refresh",Hidden).Invoke(g.Overlay,null);
                var activeTicks=(List<Image>)typeof(SceneOverlay).GetField("blinkingTicks",Hidden).GetValue(g.Overlay);Check(activeTicks.Count==8,"Scene timeline includes active and queued year markers");
                var knob=g.Overlay.GetComponentsInChildren<RectTransform>(true).Single(r=>r.name=="Selected year");Check(activeTicks.All(t=>t.transform.GetSiblingIndex()>knob.GetSiblingIndex()),"Green tick remains visible at the selected thumb");
                yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot("Artifacts/generation-progress-scene.png");yield return null;
                g.SetYear(p.Id,1950);HudRefresh();Check(!Progress().gameObject.activeSelf,"Queued selected year has no active progress bar");
                var queuedCard=g.Hud.GetComponentsInChildren<RectTransform>(true).Single(r=>r.name=="Queued scene status");
                Check(queuedCard.gameObject.activeSelf&&queuedCard.GetComponentsInChildren<Text>().Any(t=>t.text.Contains("1950 is in queue")),"Selected queued year shows its scene status card");
                var waiting=gen.QueuedTarget(doc.Id,p.Id,1950);string waitingId=waiting.job.requestId;
                foreach(bool vr in new[]{false,true})
                {
                    typeof(HudOverlay).GetMethod("Configure",Hidden).Invoke(g.Hud,new object[]{vr});Canvas.ForceUpdateCanvases();HudRefresh();
                    var hudToggle=g.Hud.GetComponentsInChildren<RectTransform>(true).Single(r=>r.name=="Toggle HUD");
                    Check(queuedCard.anchoredPosition.y+queuedCard.rect.height*.5f<hudToggle.anchoredPosition.y-hudToggle.rect.height*.5f,"Desktop and Quest queue cards stay below the HUD toggle");
                }
                var moveFirst=queuedCard.GetComponentsInChildren<RectTransform>().Single(r=>r.name=="Move queued scene to front");
                Check(g.Hud.HandleInput(new Ray(moveFirst.position-moveFirst.forward*2,moveFirst.forward),true,true,true,false)&&gen.QueuedTarget(doc.Id,p.Id,1950).job.requestId==waitingId&&waiting.queuePriority>0&&gen.ActiveCount==8,"Queue-card pointer click prioritizes the existing request without preempting workers");
                typeof(HudOverlay).GetMethod("Configure",Hidden).Invoke(g.Hud,new object[]{UnityEngine.XR.XRSettings.isDeviceActive});
                HudRefresh();Check(queuedCard.GetComponentsInChildren<Text>().Any(t=>t.text.Contains("Prioritized")),"Queue card confirms priority");
                var prioritySave=(System.Threading.Tasks.Task)typeof(SceneGeneration).GetField("persistence",Hidden).GetValue(gen);yield return Until(()=>prioritySave.IsCompleted,"Priority checkpoint finishes");
                var priorityCheckpoint=JsonUtility.FromJson<SceneQueueState>(File.ReadAllText(Path.Combine((string)typeof(SceneGeneration).GetField("TestStorage",Hidden).GetValue(gen),"scene-generation-queue.json")));
                Check(priorityCheckpoint.waiting.Single(x=>x.job.requestId==waitingId).queuePriority==waiting.queuePriority,"Real queue checkpoint preserves promoted order and request identity");
                g.Select(single.Id);HudRefresh();Check(Progress().gameObject.activeSelf&&g.Year(single.Id)==1920,"Single known-year scene shows its progress");
                helper.BeforeImage=job=>{Volatile.Write(ref held,job);release.WaitOne(5000);};helper.Hold=false;
                yield return Until(()=>Volatile.Read(ref held)!=null,"Fixture enters saving stage");var saving=Volatile.Read(ref held);var target=doc.People.Single(x=>PanoramaCache.PersonKey(x.Id)==saving.personKey);
                g.Select(target.Id);g.SetYear(target.Id,saving.year);HudRefresh();Check(Progress().GetComponentInChildren<Text>().text.StartsWith("Saving"),"Progress switches from generation to saving");release.Set();
                yield return Until(()=>gen.CompletedCount==9&&!gen.Busy,"All fixture jobs finish");HudRefresh();Check(!Progress().gameObject.activeSelf,"Progress hides after completion");
                string fixtureStorage=(string)typeof(SceneGeneration).GetField("TestStorage",Hidden).GetValue(gen);var savedRecords=PanoramaCache.YearRecordsAt(Path.Combine(fixtureStorage,"Panoramas"),doc.Id,p.Id);
                Check(savedRecords.Length==8&&savedRecords.All(r=>r.settingBrief?.text=="Synthetic researched street"&&r.settingBrief.sources.Single().url=="https://example.org/archive"),"HTTP result research provenance reaches app image manifests");
                Check(!g.Markers(p.Id).Any(x=>x.Generating)&&g.Markers(p.Id).Single(x=>x.Year==1900).Generated,"Active marker clears and original cached gold state returns");
                helper.Hold=true;helper.FailYear=1900;gen.Enqueue(doc.Id,p,false,new[]{(1900,"York")});
                yield return Until(()=>helper.Requests.Count==10,"Regeneration failure fixture starts");g.Select(p.Id);g.SetYear(p.Id,1900);HudRefresh();
                Check(Progress().gameObject.activeSelf,"Regenerating an existing selected year shows progress");helper.Hold=false;
                yield return Until(()=>gen.FailedCount==1&&!gen.Busy,"Model failure clears its active state");HudRefresh();
                Check(!Progress().gameObject.activeSelf&&!g.Markers(p.Id).Any(x=>x.Generating)&&g.Markers(p.Id).Single(x=>x.Year==1900).Generated,"Failure removes blink/progress while keeping original gold cache marker");
                gen.PauseQueue();gen.Enqueue(doc.Id,p,false,new[]{(1960,"York")});gen.PauseQueue();g.SetYear(p.Id,1960);HudRefresh();
                Check(queuedCard.gameObject.activeSelf&&g.Markers(p.Id).Single(m=>m.Year==1960).Queued,"Paused waiting request still shows its blue marker and queue card");
                gen.ClearWaiting();HudRefresh();Check(!queuedCard.gameObject.activeSelf&&!g.Markers(p.Id).Any(m=>m.Queued),"Clearing waiting jobs removes queued markers and status without PNG reads");
                File.WriteAllText("Artifacts/generation-marker-play.txt",n+" generation marker/progress checks passed: exact-year activity, queued exclusion, GPU blink and fading copies, thumb visibility, single-year scenes, HUD/Quest layout, saving/completion and pooled performance. No Meta calls.");
            }
            finally
            {
                release.Set();if(gen!=null&&refresh!=null)gen.TargetChanged-=refresh;
                typeof(AncestorGallery).GetProperty("Generation").SetValue(g,originalGen);if(go!=null)UnityEngine.Object.DestroyImmediate(go);
                g.OpenDocument(original);if(selected!=null)g.Select(selected);g.Hud.SetVisible(hud);
                string path=Path.Combine(PanoramaCache.DirectoryPath,"people",doc.Id);if(Directory.Exists(path))Directory.Delete(path,true);
            }
        }
    }
}
