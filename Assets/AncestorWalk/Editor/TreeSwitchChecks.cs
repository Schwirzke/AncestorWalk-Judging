using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace AncestorWalk.Editor
{
    public static class TreeSwitchChecks
    {
        const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        const string A="aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const string B="bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        const string Report="Artifacts/tree-switch-checks.txt";
        static PendingScene Item(string tree,int year,int phase=1)=>new PendingScene{treeId=tree,personId="switch-fixture",phase=phase,job=new SceneJob{requestId=Guid.NewGuid().ToString("N"),treeId=tree,personKey=PanoramaCache.PersonKey("switch-fixture"),year=year,date=year.ToString(),place="York"}};
        public static string Data()
        {
            int n=0;void Check(bool ok,string why){if(!ok)throw new Exception(why);n++;}
            foreach(bool paused in new[]{false,true})
            {
                var q=new SceneQueueState{automaticPaused=paused};var sent=Item(A,1900,2);var unknown=Item(A,1901,0);var other=Item(B,1910,2);
                q.active.AddRange(new[]{sent,unknown,other});q.waiting.AddRange(new[]{Item(A,1902),Item(B,1911)});
                var endpoint=Item(A,1903);endpoint.isAutomatic=true;q.AppendAutomatic(new[]{endpoint},null);
                var removed=q.CancelTree(A);Check(removed.Length==4,"Cancellation includes active, uncertain, manual and automatic jobs for one tree");
                Check(q.active.Single()==other&&q.waiting.Single().treeId==B&&q.automatic.Count==0,"Other tree jobs are preserved");
                Check(q.cancelled.Select(p=>p.job.requestId).OrderBy(x=>x).SequenceEqual(new[]{sent.job.requestId,unknown.job.requestId}.OrderBy(x=>x)),"Submitted/uncertain identities are retained, unsent jobs are discarded");
                Check(q.automaticPaused==paused,"Saved Pause preference is unchanged");q.Validate();
                q=JsonUtility.FromJson<SceneQueueState>(JsonUtility.ToJson(q));q.Validate();Check(q.cancelled.Count==2&&q.active.Single().treeId==B,"Stopped request history survives restart outside active slots");
                Check(q.AppendAutomatic(removed.Select(p=>Item(A,p.job.year)),null)==0,"Reopening cannot automatically regenerate cancelled years");
                Check(q.Append(new[]{Item(A,1900)},null)==1,"An explicit manual request remains possible");
                Check(q.CancelTree(A).Length==1&&q.CancelTree(A).Length==0,"Repeated cancellation is idempotent");
            }
            var legacy=JsonUtility.FromJson<SceneQueueState>("{\"version\":2,\"waiting\":[],\"active\":[],\"automatic\":[],\"automaticSeen\":[]}");legacy.Validate();Check(legacy.cancelled.Count==0,"Existing version-two checkpoints remain compatible");
            return n+" tree-switch queue policy checks passed.";
        }
        public static string Play()
        {
            if(!Application.isPlaying)throw new Exception("Play mode required");
            Directory.CreateDirectory("Artifacts");File.WriteAllText(Report,"RUNNING");
            UnityEngine.Object.FindAnyObjectByType<AncestorGallery>().StartCoroutine(Runner(Checks()));return "Running isolated tree-switch confirmation/cancellation checks; no Meta requests.";
        }
        public static string ErrorAlerts()
        {
            if(!Application.isPlaying)throw new Exception("Play mode required");
            File.WriteAllText("Artifacts/error-navigation-checks.txt","RUNNING");
            UnityEngine.Object.FindAnyObjectByType<AncestorGallery>().StartCoroutine(Runner(ErrorChecks(),"Artifacts/error-navigation-checks.txt"));
            return "Checking error navigation with isolated fictional storage; no Meta requests.";
        }
        static IEnumerator Runner(IEnumerator first,string report=Report)
        {
            var stack=new Stack<IEnumerator>();stack.Push(first);
            while(stack.Count>0)
            {
                var current=stack.Peek();bool more=false;Exception failure=null;try{more=current.MoveNext();}catch(Exception e){failure=e;}
                if(failure!=null){while(stack.Count>0)(stack.Pop() as IDisposable)?.Dispose();File.WriteAllText(report,"FAILED: "+failure);Debug.LogException(failure);yield break;}
                if(!more){(stack.Pop() as IDisposable)?.Dispose();continue;}if(current.Current is IEnumerator nested){stack.Push(nested);continue;}yield return current.Current;
            }
        }
        static IEnumerator ErrorChecks()
        {
            int n=0;void Check(bool ok,string why){if(!ok)throw new Exception(why);n++;}
            IEnumerator Until(Func<bool> done){float end=Time.realtimeSinceStartup+15;while(!done()&&Time.realtimeSinceStartup<end)yield return null;var owner=UnityEngine.Object.FindAnyObjectByType<AncestorGallery>();Check(done(),"Async error navigation completed: "+owner.Library.Message+"; selected="+owner.SelectedId+"; tree="+owner.TreeId+"; busy="+owner.Library.Busy);}
            var g=UnityEngine.Object.FindAnyObjectByType<AncestorGallery>();var lib=g.Library;var nav=UnityEngine.Object.FindAnyObjectByType<GalleryNavigation>();
            var original=g.Generation;var document=g.Document;var originalStore=lib.Store;
            if(original.ActiveCount!=0||original.WaitingCount!=0||lib.Busy)throw new Exception("Preserve existing jobs; wait before checks");
            var error=UnityEngine.Object.FindAnyObjectByType<GenerationErrorOverlay>();var update=typeof(GenerationErrorOverlay).GetMethod("Update",Hidden);
            var card=(RectTransform)typeof(GenerationErrorOverlay).GetField("card",Hidden).GetValue(error);
            var button=(RectTransform)typeof(GenerationErrorOverlay).GetField("visit",Hidden).GetValue(error);
            var close=(RectTransform)typeof(GenerationErrorOverlay).GetField("close",Hidden).GetValue(error);
            var seen=typeof(GenerationErrorOverlay).GetField("seen",Hidden).GetValue(error);bool wasVisible=UIFade.Visible(card.gameObject),hud=g.HudVisible;
            var text=error.GetComponentsInChildren<Text>(true).ToDictionary(t=>t,t=>t.text);
            var library=(List<TreeDocument>)typeof(TreeLibraryOverlay).GetField("library",Hidden).GetValue(lib);
            var oldLibrary=library.ToList();var root=Path.GetFullPath("Library/ErrorNavigationChecks/"+Guid.NewGuid().ToString("N"));
            var store=new TreeStore(root);var fixture=new GameObject("Isolated error navigation");fixture.SetActive(false);var gen=fixture.AddComponent<SceneGeneration>();
            typeof(SceneGeneration).GetField("storage",Hidden).SetValue(gen,root);typeof(SceneGeneration).GetProperty("Ready").SetValue(gen,true);
            var mouse=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Mouse>();
            var target=new TreeDocument{Id=B,Name="Error destination fixture",People=new List<Person>{new Person{Id="error-person",Name="Fictional person",BirthDate="1900",DeathDate="1980"}}};
            var current=document.People.First(p=>LifeEndpointScenes.TrySceneRange(p,out _,out _));LifeEndpointScenes.TrySceneRange(current,out int first,out int last);
            void Notice(string tree,string person,int year)
            {typeof(SceneGeneration).GetProperty("LatestError").SetValue(gen,new GenerationErrorNotice{Tree=tree,Person=person,Year=year,Message="Synthetic failure"});update.Invoke(error,null);Canvas.ForceUpdateCanvases();}
            void SelectAlert(RectTransform rect,bool desktop=false)
            {
                Canvas.ForceUpdateCanvases();
                if(desktop){UnityEngine.InputSystem.InputSystem.QueueStateEvent(mouse,new UnityEngine.InputSystem.LowLevel.MouseState{position=RectTransformUtility.WorldToScreenPoint(null,rect.position)});UnityEngine.InputSystem.InputSystem.Update();}
                Check(error.HandleInput(new Ray(rect.position-rect.forward,rect.forward),!desktop,true),"Alert captures selection");
            }
            void SelectLibrary(string title)
            {
                var hits=(IEnumerable)typeof(TreeLibraryOverlay).GetField("hits",Hidden).GetValue(lib);
                foreach(var hit in hits){var rect=(RectTransform)hit.GetType().GetField("Rect").GetValue(hit);if(rect.name!=title)continue;Canvas.ForceUpdateCanvases();lib.HandleInput(new Ray(rect.position-rect.forward,rect.forward),true,true,false);return;}
                throw new Exception("Missing switch control "+title);
            }
            try
            {
                g.Tutorial.Dismiss();lib.Close();g.LocationEditor.Close();g.Batch.Close();g.AISetup.Close();UnityEngine.Object.FindAnyObjectByType<NavigationPreferences>().SetOpen(false);
                typeof(AncestorGallery).GetProperty("Generation").SetValue(g,gen);typeof(TreeLibraryOverlay).GetField("store",Hidden).SetValue(lib,store);
                g.ShowTree();Notice(g.TreeId,current.Id,first);SelectAlert(button);
                Check(g.SelectedId==current.Id&&g.Year(current.Id)==first&&g.Overlay.TimelineOpen&&!UIFade.Visible(card.gameObject),"Tree alert opens exact scene/year and timeline, then dismisses");
                var position=new Vector3(.2f,0,.4f);var rotation=Quaternion.Euler(0,37,0);nav.transform.SetPositionAndRotation(position,rotation);
                g.Overlay.ToggleTimeline();Notice(g.TreeId,current.Id,last);SelectAlert(button,true);
                Check(g.Year(current.Id)==last&&g.Overlay.TimelineOpen&&nav.transform.position==position&&nav.transform.rotation==rotation,"Desktop same-person visit preserves view and reopens timeline");
                g.EditLocation(current.Id);Notice(g.TreeId,current.Id,first);SelectAlert(button);
                Check(g.LocationEditor.IsOpen&&g.Year(current.Id)==last&&UIFade.Visible(card.gameObject),"Open editor prevents navigation without losing text");g.LocationEditor.Close();
                Notice(g.TreeId,"missing-person",first);SelectAlert(button);Check(g.SelectedId==current.Id&&UIFade.Visible(card.gameObject),"Missing person keeps alert and view");
                Notice(g.TreeId,current.Id,0);SelectAlert(button);Check(g.Year(current.Id)==last,"Unavailable year never clamps to another year");SelectAlert(close,true);Check(!UIFade.Visible(card.gameObject),"Close still works on desktop");
                store.Save(target);library.Add(target);Check(lib.CanVisitScene(B,"error-person",1917,out _),"Saved other-tree destination resolves");
                var queue=(SceneQueueState)typeof(SceneGeneration).GetField("queue",Hidden).GetValue(gen);queue.waiting.Add(Item(g.TreeId,first));
                Notice(B,"error-person",1917);SelectAlert(button);Check(lib.IsOpen&&g.TreeId==document.Id,"Cross-tree visit waits for existing switch confirmation");
                SelectLibrary("Stay here");Check(queue.waiting.Count==1&&UIFade.Visible(card.gameObject),"Stay preserves queue and error");lib.Close();
                Notice(B,"error-person",1917);SelectAlert(button);SelectLibrary("Cancel jobs & switch");Notice(B,"error-person",1918);
                yield return Until(()=>!lib.Busy&&g.TreeId==B&&g.SelectedId=="error-person");
                Check(g.Year("error-person")==1917&&g.Overlay.TimelineOpen&&UIFade.Visible(card.gameObject),"Confirmed switch reaches exact scene without dismissing a newer error");
                update.Invoke(error,null);SelectAlert(button);Check(g.Year("error-person")==1918&&!UIFade.Visible(card.gameObject),"Newest error navigates to its own year and dismisses");
                Check(queue.waiting.Count==0&&gen.ActiveCount==0,"Only confirmed cancellation clears source backlog; navigation submits no jobs");
                var missing=target.Copy();missing.Id=A;library.Add(missing);Notice(A,"error-person",1917);SelectAlert(button);
                yield return Until(()=>!lib.Busy);
                Check(g.TreeId==B&&UIFade.Visible(card.gameObject)&&!(bool)typeof(GenerationErrorOverlay).GetField("navigating",Hidden).GetValue(error),"Failed tree load preserves alert, view and retryable navigation state");
                Check(UnityEngine.Object.FindObjectsByType<GenerationErrorOverlay>(FindObjectsSortMode.None).Length==1,"Errors keep one reusable alert");
                File.WriteAllText("Artifacts/error-navigation-checks.txt",n+" error-navigation checks passed; desktop/controller selection, exact year, view preservation, editor guard, missing targets, switch confirmation, Stay, load failure; no Meta requests.");
            }
            finally
            {
                lib.Close();g.OpenDocument(document);typeof(AncestorGallery).GetProperty("Generation").SetValue(g,original);typeof(TreeLibraryOverlay).GetField("store",Hidden).SetValue(lib,originalStore);
                library.Clear();library.AddRange(oldLibrary);foreach(var entry in text)if(entry.Key!=null)entry.Key.text=entry.Value;
                typeof(GenerationErrorOverlay).GetField("seen",Hidden).SetValue(error,seen);typeof(GenerationErrorOverlay).GetField("navigating",Hidden).SetValue(error,false);UIFade.Set(card.gameObject,wasVisible);
                g.Hud.SetVisible(hud);UnityEngine.InputSystem.InputSystem.RemoveDevice(mouse);UnityEngine.Object.DestroyImmediate(fixture);
            }
        }
        static IEnumerator Checks()
        {
            int n=0;void Check(bool ok,string why){if(!ok)throw new Exception(why);n++;}
            IEnumerator Until(Func<bool> done,string why){float end=Time.realtimeSinceStartup+20;while(!done()&&Time.realtimeSinceStartup<end)yield return null;Check(done(),why);}
            var owner=UnityEngine.Object.FindAnyObjectByType<AncestorGallery>();var lib=owner.Library;var original=owner.Generation;bool hud=owner.HudVisible;
            if(original.Busy||lib.Busy)throw new Exception("Wait for existing jobs before synthetic tree-switch checks");
            owner.Tutorial.Dismiss();lib.Close();string root=Path.GetFullPath("Library/TreeSwitchChecks/"+Guid.NewGuid().ToString("N"));GameObject go=null;
            SceneGeneration Create(string path,SceneQueueChecks.FakeHelper helper,bool active=true)
            {
                go=new GameObject("Isolated tree-switch fixture");go.SetActive(false);var gen=go.AddComponent<SceneGeneration>();
                typeof(SceneGeneration).GetField("TestStorage",Hidden).SetValue(gen,path);typeof(SceneGeneration).GetField("storage",Hidden).SetValue(gen,path);
                typeof(SceneGeneration).GetField("TestConnection",Hidden).SetValue(gen,new SceneServiceConnection{url=helper?.Url??"http://127.0.0.1:1",token=new string('a',64)});
                if(active)go.SetActive(true);else typeof(SceneGeneration).GetProperty("Ready").SetValue(gen,true);return gen;
            }
            void Dispose(){if(go!=null)UnityEngine.Object.DestroyImmediate(go);go=null;}
            void Click(string title)
            {
                var hits=(IEnumerable)typeof(TreeLibraryOverlay).GetField("hits",Hidden).GetValue(lib);RectTransform rect=null;
                foreach(var hit in hits){var candidate=(RectTransform)hit.GetType().GetField("Rect").GetValue(hit);if(candidate.name==title){rect=candidate;break;}}
                if(rect==null)throw new Exception("Missing confirmation button "+title);Canvas.ForceUpdateCanvases();lib.HandleInput(new Ray(rect.position-rect.forward,rect.forward),true,true,false);
            }
            void Request(Action next)=>typeof(TreeLibraryOverlay).GetMethod("RequestTreeChange",Hidden).Invoke(lib,new object[]{owner.TreeId==A?B:A,next});
            try
            {
                var gen=Create(Path.Combine(root,"ui"),null,false);typeof(AncestorGallery).GetProperty("Generation").SetValue(owner,gen);
                var queue=(SceneQueueState)typeof(SceneGeneration).GetField("queue",Hidden).GetValue(gen);var job=Item(owner.TreeId,1900,2);queue.active.Add(job);queue.waiting.Add(Item(owner.TreeId,1901));
                var workers=(IDictionary)typeof(SceneGeneration).GetField("workers",Hidden).GetValue(gen);var worker=Activator.CreateInstance(typeof(SceneGeneration).GetNestedType("Worker",BindingFlags.NonPublic),true);worker.GetType().GetField("Pending").SetValue(worker,job);workers.Add(job,worker);
                bool switched=false;lib.Open();Request(()=>switched=true);Check(!switched&&lib.IsOpen,"Selecting a different tree does not cancel or switch before confirmation");
                Check(lib.GetComponentsInChildren<Text>().Any(t=>t.text.Contains("1 active and 1 waiting")&&t.text.Contains("Meta")),"Warning gives scoped counts and already-submitted limitation");
                Click("Stay here");Check(!switched&&gen.Busy&&queue.waiting.Count==1,"Stay keeps all jobs and the current tree");
                Request(()=>switched=true);lib.HandleInput(new Ray(),true,false,true);Check(!switched&&gen.Busy,"Quest Back dismisses confirmation without cancellation");
                Request(()=>switched=true);Click("Cancel jobs & switch");yield return Until(()=>!lib.Busy,"Cancellation checkpoint saved before continuing");
                Check(switched&&!gen.Busy&&gen.WaitingForTree(owner.TreeId)==0&&queue.cancelled.Single().job.requestId==job.job.requestId,"Confirmation stops the selected tree jobs and continues exactly once");
                Check(JsonUtility.FromJson<SceneQueueState>(File.ReadAllText(Path.Combine(root,"ui/scene-generation-queue.json"))).cancelled.Count==1,"Switch continuation follows durable cancelled-request identity");
                var saving=Item(owner.TreeId,1902,2);var commit=new TaskCompletionSource<bool>();var saver=Activator.CreateInstance(worker.GetType(),true);saver.GetType().GetField("Pending").SetValue(saver,saving);saver.GetType().GetField("Commit").SetValue(saver,commit.Task);queue.active.Add(saving);workers.Add(saving,saver);
                var stopping=gen.CancelTreeJobs(owner.TreeId);Check(!gen.Busy&&!stopping.IsCompleted,"An in-flight atomic save finishes without blocking the main thread or exposing a switch early");commit.SetResult(true);yield return Until(()=>stopping.IsCompleted,"Cancellation completes after existing atomic save");if(stopping.IsFaulted)throw stopping.Exception;
                var failing=Item(owner.TreeId,1903,2);var failedCommit=new TaskCompletionSource<bool>();var failedWorker=Activator.CreateInstance(worker.GetType(),true);failedWorker.GetType().GetField("Pending").SetValue(failedWorker,failing);failedWorker.GetType().GetField("Commit").SetValue(failedWorker,failedCommit.Task);queue.active.Add(failing);workers.Add(failing,failedWorker);
                switched=false;Request(()=>switched=true);Click("Cancel jobs & switch");failedCommit.SetException(new IOException("Synthetic storage unavailable"));yield return Until(()=>!lib.Busy,"Failed cancellation returns feedback");
                Check(!switched&&lib.Message.Contains("Synthetic storage unavailable")&&gen.CancellationNeedsSaving,"A failed checkpoint/save cannot switch trees");
                bool blocked=false;try{owner.OpenDocument(owner.Document);}catch(InvalidOperationException){blocked=true;}Check(blocked,"Direct tree opens cannot bypass failed cancellation persistence");
                Click("Cancel jobs & switch");yield return Until(()=>!lib.Busy,"Explicit retry persists stopped-job state");Check(switched&&!gen.CancellationNeedsSaving,"Retry can finish cancellation without restarting jobs");
                queue.waiting.Add(Item(owner.TreeId,1904));switched=false;Request(()=>switched=true);Check(!switched&&lib.GetComponentsInChildren<Text>().Any(t=>t.text.Contains("0 active and 1 waiting")),"Waiting-only jobs also require confirmation");Click("Stay here");Check(queue.waiting.Count==1,"Declining waiting-only cancellation keeps the backlog");
                lib.Close();typeof(AncestorGallery).GetProperty("Generation").SetValue(owner,original);Dispose();

                var pixels=new Texture2D(256,128);pixels.SetPixels(Enumerable.Repeat(Color.blue,256*128).ToArray());pixels.Apply();var png=pixels.EncodeToPNG();UnityEngine.Object.Destroy(pixels);
                using(var helper=new SceneQueueChecks.FakeHelper(png){Hold=true})
                {
                    string path=Path.Combine(root,"network");gen=Create(path,helper);yield return Until(()=>gen.Ready,"Isolated queue ready");
                    var person=new Person{Id="switch-fixture",BirthDate="1900",BirthPlace="York",DeathDate="1905",DeathPlace="York"};
                    var cache=Path.Combine(path,"Panoramas");PanoramaCache.SaveDecodedAt(cache,A,person.Id,1900,png,new SceneJob{sha256=PanoramaCache.Hash(png),place="York",model="fixture",prompt="fixture",createdUtc="2000-01-01T00:00:00Z"},256,128);
                    var before=Directory.GetFiles(cache,"*",SearchOption.AllDirectories).ToDictionary(p=>p,p=>PanoramaCache.Hash(File.ReadAllBytes(p)));
                    gen.Enqueue(A,person,false,Enumerable.Range(1900,6).Select(y=>(y,"York")));yield return Until(()=>helper.Posts==4&&gen.ActiveCount==4,"Four submitted jobs plus two waiting");
                    var ids=gen.ActiveJobs.Select(p=>p.job.requestId).ToArray();var cancel=gen.CancelTreeJobs(A);Check(!gen.Busy&&gen.WaitingCount==0,"Cancellation frees local slots immediately");yield return Until(()=>cancel.IsCompleted,"Cancellation persisted");if(cancel.IsFaulted)throw cancel.Exception;
                    int posts=helper.Posts;yield return new WaitForSecondsRealtime(.2f);int gets=helper.Gets;yield return new WaitForSecondsRealtime(2.2f);
                    Check(helper.Posts==posts&&helper.Gets==gets&&helper.ImageGets==0,"Cancelled coroutines neither retry nor poll/download later");
                    Check(before.All(p=>File.Exists(p.Key)&&PanoramaCache.Hash(File.ReadAllBytes(p.Key))==p.Value),"Existing cached scenes are unchanged");Dispose();
                    gen=Create(path,helper);yield return Until(()=>gen.Ready,"Cancelled queue reloaded");gen.Resume(A);gen.ScheduleEndpoints(A,new[]{person},false,_=>new ResidenceTimeline(),_=>Array.Empty<int>());yield return null;yield return null;
                    Check(gen.ActiveJobs.Count==0&&gen.WaitingCount==0&&helper.Posts==4,"Restart/reopen cannot silently regenerate cancelled endpoints");
                    var restored=(SceneQueueState)typeof(SceneGeneration).GetField("queue",Hidden).GetValue(gen);Check(restored.cancelled.Select(p=>p.job.requestId).OrderBy(x=>x).SequenceEqual(ids.OrderBy(x=>x)),"All four submitted IDs are retained after restart");
                    gen.Generate(B,person,false,1902,"York");yield return Until(()=>helper.Posts==5,"Another tree can submit after cancellation");Check(gen.ActiveJobs.All(p=>p.treeId==B),"Old tree uses no local active slots");
                    var cleanup=gen.CancelTreeJobs(B);yield return Until(()=>cleanup.IsCompleted,"New fixture cleaned up");Dispose();
                }
                string result=n+" tree-switch Play checks passed: VR confirmation/Stay/Back, durable cancellation, four active plus waiting jobs, no late polling/retries, cached image preservation, restart and another tree. Local fake helper only.";File.WriteAllText(Report,result);
            }
            finally{typeof(AncestorGallery).GetProperty("Generation").SetValue(owner,original);lib.Close();owner.Hud.SetVisible(hud);Dispose();}
        }
    }
}
