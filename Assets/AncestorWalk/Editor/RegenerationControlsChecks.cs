using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace AncestorWalk.Editor
{
    public static class RegenerationControlsChecks
    {
        const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        const string Report="Artifacts/regeneration-controls-checks.txt";
        static readonly string Tree=PanoramaCache.Hash(System.Text.Encoding.UTF8.GetBytes("regeneration-controls-fictional"));
        static PendingScene Item(int year,bool bulk=true)=>new PendingScene{treeId=Tree,personId="fictional",phase=1,isBulkRegeneration=bulk,job=new SceneJob{treeId=Tree,personKey=PanoramaCache.PersonKey("fictional"),requestId=Guid.NewGuid().ToString("N"),year=year,date=year.ToString(),place="York",synthetic=true}};
        public static string Data()
        {
            int n=0;void Check(bool value,string why){if(!value)throw new Exception(why);n++;}
            void Reject(Action action,string why){bool failed=false;try{action();}catch(InvalidOperationException){failed=true;}Check(failed,why);}
            var queue=new SceneQueueState();var bulk=Enumerable.Range(1900,62).Select(y=>Item(y)).ToArray();
            Check(queue.AppendRegeneration(bulk)==62,"Bulk regeneration exceeds the manual twenty-scene cap");
            Check(queue.AppendRegeneration(bulk)==0,"Repeated batch coalesces exact person/year jobs");
            queue.Append(new[]{Item(1980,false)},null);Check(queue.waiting.Count==63,"An individual request fits behind a full bulk backlog");
            var restored=JsonUtility.FromJson<SceneQueueState>(JsonUtility.ToJson(queue));restored.Validate();
            Check(restored.waiting.Count(p=>p.isBulkRegeneration)==62&&restored.waiting[0].job.requestId==bulk[0].job.requestId,"Bulk flag and request identities survive checkpoints");
            var retry=SceneRetryPolicy.Next(bulk[0],new SceneJob{status="failed",retryable=true},100);
            Check(retry.isBulkRegeneration&&!retry.isAutomatic&&retry.job.year==1900,"Retry remains explicit regeneration rather than automatic missing-only work");
            var full=new SceneQueueState();full.Append(Enumerable.Range(1800,20).Select(y=>Item(y,false)),null);
            Reject(()=>full.Append(new[]{Item(1821,false)},null),"Individual queue limit remains twenty");
            Check(full.AppendRegeneration(new[]{Item(1934)})==1,"Bulk has independent bounded capacity");
            var automatic=Item(1934,false);automatic.isAutomatic=true;var mixed=new SceneQueueState();mixed.AppendAutomatic(new[]{automatic},null);
            Check(mixed.AppendRegeneration(new[]{Item(1934)})==0,"Already scheduled automatic year is not replaced or duplicated");
            var invalid=Item(1935);invalid.isAutomatic=true;Reject(()=>queue.AppendRegeneration(new[]{invalid}),"Bulk requests cannot claim automatic image protection semantics");
            var capacity=new SceneQueueState();capacity.waiting.AddRange(Enumerable.Range(1,4000).Select(y=>Item(y)));
            Reject(()=>capacity.AppendRegeneration(new[]{Item(4001)}),"Bulk overflow is rejected atomically");Check(capacity.waiting.Count==4000,"Rejected overflow preserves all queued identities");
            Check(queue.CancelTree(Tree).Length==63&&queue.waiting.Count==0,"Existing cancel-tree flow includes bulk regeneration");
            var legacy=JsonUtility.FromJson<SceneQueueState>("{\"version\":2,\"waiting\":[],\"active\":[],\"automatic\":[]}");legacy.Validate();Check(legacy.waiting.Count==0,"Existing queue files need no migration");
            return n+" bulk regeneration data checks passed.";
        }
        public static string Run()
        {
            var g=UnityEngine.Object.FindAnyObjectByType<AncestorGallery>();
            if(!Application.isPlaying||g==null||!g.CacheIndexReady||g.Generation.Busy)throw new Exception("Ready, idle Play mode required.");
            File.WriteAllText(Report,"RUNNING");g.StartCoroutine(Safe(Checks(g)));return "Regeneration control fixtures running; no AI calls or family-cache writes.";
        }
        static IEnumerator Safe(IEnumerator checks,string report=Report)
        {
            var stack=new Stack<IEnumerator>();stack.Push(checks);
            while(stack.Count>0)
            {
                var top=stack.Peek();bool next=false;Exception error=null;try{next=top.MoveNext();}catch(Exception e){error=e;}
                if(error!=null){while(stack.Count>0)(stack.Pop() as IDisposable)?.Dispose();File.WriteAllText(report,"FAILED: "+error);Debug.LogException(error);yield break;}
                if(!next){(stack.Pop() as IDisposable)?.Dispose();continue;}if(top.Current is IEnumerator nested)stack.Push(nested);else yield return top.Current;
            }
        }
        public static string BulkPlay()
        {
            var g=UnityEngine.Object.FindAnyObjectByType<AncestorGallery>();if(!Application.isPlaying||g==null)throw new Exception("Play mode required.");
            string report="Artifacts/regeneration-bulk-play.txt";File.WriteAllText(report,"RUNNING");g.StartCoroutine(Safe(BulkChecks(),report));return "Bulk scheduler checks running against an isolated fake helper.";
        }
        static IEnumerator BulkChecks()
        {
            int n=0;void Check(bool value,string why){if(!value)throw new Exception(why);n++;}
            byte[] Png(Color color){var image=new Texture2D(256,128,TextureFormat.RGBA32,false);image.SetPixels(Enumerable.Repeat(color,256*128).ToArray());image.Apply();var bytes=image.EncodeToPNG();UnityEngine.Object.Destroy(image);return bytes;}
            var original=Png(Color.blue);var replacement=Png(Color.green);string root=Path.GetFullPath("Artifacts/RegenerationBulk/"+Guid.NewGuid().ToString("N"));string directory=Path.Combine(root,"Panoramas");
            var person=new Person{Id="fictional",BirthDate="1900",DeathDate="1980",BirthPlace="York"};
            foreach(int year in Enumerable.Range(1900,62))PanoramaCache.SaveDecodedAt(directory,Tree,person.Id,year,original,new SceneJob{year=year,place="York",sha256=PanoramaCache.Hash(original),model="fixture",prompt="original fixture",createdUtc=DateTime.UtcNow.ToString("o")},256,128);
            using var helper=new SceneQueueChecks.FakeHelper(replacement){Hold=true};var objects=new List<GameObject>();
            SceneGeneration Create()
            {
                var go=new GameObject("Isolated bulk scheduler");go.SetActive(false);objects.Add(go);var gen=go.AddComponent<SceneGeneration>();
                typeof(SceneGeneration).GetField("TestStorage",Hidden).SetValue(gen,root);typeof(SceneGeneration).GetField("TestConnection",Hidden).SetValue(gen,new SceneServiceConnection{url=helper.Url,token=new string('a',64)});go.SetActive(true);return gen;
            }
            try
            {
                var gen=Create();float until=Time.realtimeSinceStartup+10;while(!gen.Ready&&Time.realtimeSinceStartup<until)yield return null;Check(gen.Ready,"Bulk test queue loads");
                var completions=new List<(string Tree,string Person,int Year)>();int savedSignals=0;bool signalOrder=true;
                gen.SceneCompleted+=(tree,id,year)=>completions.Add((tree,id,year));gen.SceneSaved+=id=>{savedSignals++;signalOrder&=completions.Count==savedSignals;};
                Check(gen.RegenerateAll(Tree,Enumerable.Range(1900,62).Select(y=>(person,y,"York")))==62,"Whole cached tree enters a persisted bulk backlog");
                gen.Enqueue(Tree,person,true,new[]{(1975,"York")});until=Time.realtimeSinceStartup+15;
                while((gen.ActiveJobs.Count<4||helper.Posts<4)&&Time.realtimeSinceStartup<until)yield return null;
                Check(gen.ActiveJobs.Count==4&&helper.Posts==4,"Four requests run concurrently with more than twenty waiting");
                Check(gen.ActiveJobs[0].job.year==1975&&!gen.ActiveJobs[0].isBulkRegeneration,"Individual scene dispatch has priority over earlier bulk jobs");
                Check(gen.ActiveJobs.Skip(1).All(p=>p.isBulkRegeneration),"Remaining slots are filled by explicit bulk replacements");
                gen.PauseQueue();helper.Hold=false;until=Time.realtimeSinceStartup+20;
                while((gen.Busy||gen.ActiveJobs.Count>0)&&Time.realtimeSinceStartup<until)yield return null;
                Check(!gen.Busy&&gen.CompletedCount==4&&helper.Posts==4,"Pause finishes active jobs without dispatching the backlog");
                Check(gen.WaitingCount==59&&gen.QueuePaused,"Remaining bulk jobs remain paused");
                Check(completions.Count==4&&signalOrder&&completions.All(c=>c.Tree==Tree&&c.Person==person.Id)&&completions.Select(c=>c.Year).OrderBy(y=>y).SequenceEqual(new[]{1900,1901,1902,1975}),"Successful saves publish exact tree/person/year before index-refresh signals");
                var saved=PanoramaCache.YearRecordsAt(directory,Tree,person.Id);Check(saved.Single(r=>r.year==1900).sha256==PanoramaCache.Hash(replacement),"Explicit bulk regeneration replaces an existing fixture image");
                Check(saved.Single(r=>r.year==1903).sha256==PanoramaCache.Hash(original),"Waiting years retain their original image");
                var pending=gen.Waiting.Select(p=>p.job.requestId).ToArray();UnityEngine.Object.Destroy(gen.gameObject);yield return null;
                var recovered=Create();until=Time.realtimeSinceStartup+10;while(!recovered.Ready&&Time.realtimeSinceStartup<until)yield return null;
                Check(recovered.Ready&&recovered.QueuePaused&&recovered.WaitingCount==59,"Paused bulk backlog reopens from its checkpoint");
                Check(recovered.Waiting.All(p=>p.isBulkRegeneration)&&recovered.Waiting.Select(p=>p.job.requestId).SequenceEqual(pending),"Recovery retains explicit replacement intent and every original request identity");
                Check(helper.Posts==4&&helper.MaximumActive==4,"Recovery never replays completed requests or exceeds concurrency");
                recovered.ClearWaiting();Check(recovered.WaitingCount==0,"Existing Clear waiting removes the bulk backlog");
                recovered.SceneCompleted+=(tree,id,year)=>completions.Add((tree,id,year));helper.FailYear=1980;
                recovered.Enqueue(Tree,person,true,new[]{(1980,"York")});recovered.ScheduleEndpoints(Tree,new[]{person},true,p=>new ResidenceTimeline(),p=>Array.Empty<int>());recovered.ResumeQueue();
                until=Time.realtimeSinceStartup+20;while((recovered.Busy||recovered.WaitingCount>0||recovered.ActiveJobs.Count>0)&&Time.realtimeSinceStartup<until)yield return null;
                Check(recovered.FailedCount==1&&recovered.SkippedCount==1&&completions.Count==4,"Failed requests and automatic cache skips publish no completion feedback");
                File.WriteAllText("Artifacts/regeneration-bulk-play.txt",n+" bulk scheduler checks passed: 62 cached scenes, manual priority, four active requests, existing-image replacement, waiting-image protection, pause, durable recovery and Clear. Isolated fake helper only.");
            }
            finally{foreach(var go in objects)if(go!=null)UnityEngine.Object.Destroy(go);}
        }
        static IEnumerator Checks(AncestorGallery g)
        {
            int n=0;void Check(bool value,string why){if(!value)throw new Exception(why);n++;}
            string data=Data();var document=g.Document;var generation=g.Generation;var selected=g.SelectedId;
            string lastTree=PlayerPrefs.GetString("AncestorWalk.LastTree","");bool hud=g.HudVisible,tutorial=g.Tutorial.IsOpen;
            var nav=UnityEngine.Object.FindAnyObjectByType<GalleryNavigation>();var prefs=nav.GetComponent<NavigationPreferences>();bool prefOpen=prefs.IsOpen;
            bool navEnabled=nav.enabled;var position=nav.transform.position;var rotation=nav.transform.rotation;var head=nav.view.transform.localRotation;
            bool guard=SessionState.GetBool("AncestorWalk.EndpointChecks",false);SessionState.SetBool("AncestorWalk.EndpointChecks",true);
            var holder=new GameObject("Isolated regeneration queue");holder.SetActive(false);var fixture=holder.AddComponent<SceneGeneration>();fixture.enabled=false;
            string storage=Path.GetFullPath("Artifacts/RegenerationControls/"+Guid.NewGuid().ToString("N"));
            typeof(SceneGeneration).GetField("TestStorage",Hidden).SetValue(fixture,storage);
            typeof(SceneGeneration).GetField("TestConnection",Hidden).SetValue(fixture,new SceneServiceConnection{url="http://127.0.0.1:9",token="fixture"});holder.SetActive(true);
            var person=new Person{Id="fictional",Name="Regeneration fixture",BirthDate="1900",DeathDate="1980",BirthPlace="York",DeathPlace="York"};
            try
            {
                g.Library.Close();g.Batch.Close();g.AISetup.Close();g.LocationEditor.Close();g.Tutorial.Dismiss();prefs.SetOpen(false);g.Hud.SetVisible(true);nav.enabled=false;
                float deadline=Time.realtimeSinceStartup+10;while(!fixture.Ready&&Time.realtimeSinceStartup<deadline)yield return null;Check(fixture.Ready,"Isolated queue is ready");
                typeof(AncestorGallery).GetProperty("Generation").SetValue(g,fixture);
                var tree=new FamilyTree();tree.People.Add(person.Id,person);g.OpenDocument(TreeDocument.FromTree(tree,Tree,"Regeneration fixture",true));
                deadline=Time.realtimeSinceStartup+10;while(!g.CacheIndexReady&&Time.realtimeSinceStartup<deadline)yield return null;Check(g.CacheIndexReady,"Fictional tree index is ready");
                var cached=(Dictionary<string,int[]>)typeof(AncestorGallery).GetField("cachedYears",Hidden).GetValue(g);cached[person.Id]=new[]{1936,1975};
                g.Locations(person.Id).entries.Add(new Residence{year=1934,place="Monterey"});g.Locations(person.Id).entries.Add(new Residence{year=1975,place="York"});g.SetYear(person.Id,1936);yield return null;
                var panel=g.GetComponentsInChildren<TimelinePanel>(true).Single();var control=panel.GetComponentInParent<CubeFacingControls>();control.SetInteractionRange(12);
                var action=panel.GetComponentsInChildren<GalleryTarget>(true).Single(t=>t.action==GalleryAction.GenerateScene);
                var label=panel.GetComponentsInChildren<TMP_Text>(true).Single(t=>t.name=="Regenerate year label");
                var advance=typeof(CubeFacingControls).GetMethod("AdvanceFacing",Hidden);
                nav.transform.position+=control.transform.TransformPoint(Vector3.back*10+Vector3.up*3)-nav.view.transform.position;advance.Invoke(control,new object[]{.4f});Physics.SyncTransforms();
                Check(label.text=="REGENERATE 1936"&&action.enabled,"Tree action labels the exact generated year");
                var endLabel=panel.GetComponentsInChildren<TMP_Text>(true).Single(t=>t.name=="End year");
                Check(label.transform.localPosition.y+label.GetComponent<PanelTextSizing>().Area.y/2<endLabel.transform.localPosition.y-endLabel.GetComponent<PanelTextSizing>().Area.y/2,"Regeneration label has a separate padded row below slider endpoints");
                var hit=action.GetComponent<Collider>();var ray=new Ray(hit.bounds.center-control.Panel.forward*.3f,control.Panel.forward);
                Check(Physics.Raycast(ray,out var picked,.6f)&&picked.collider==hit,"Tree action sits ahead of the panel blocker");
                g.SetYear(person.Id,1945);Check(label.text=="GENERATE 1945","Scrubbing updates tree action to selected year between cached scenes");
                g.GenerateScene(person.Id);Check(fixture.Waiting.Single().job.year==1945&&fixture.Waiting.Single().job.place=="Monterey","Tree action queues the exact selected year/location without entering scene");
                g.GenerateScene(person.Id);Check(fixture.WaitingCount==1,"Repeated request cannot duplicate a queued year");
                yield return null;Check(label.text=="QUEUED 1945"&&!action.enabled,"Tree button reflects queued status and disables duplicate clicks");fixture.ClearWaiting();
                nav.transform.position+=control.transform.position+Vector3.back*(control.InteractionRange+3)-nav.view.transform.position;advance.Invoke(control,new object[]{.4f});Check(!hit.enabled,"Hidden tree action cannot intercept rays");
                g.SetYear(person.Id,1936);g.Select(person.Id);yield return null;var overlay=g.Overlay;
                typeof(SceneOverlay).GetField("actionsOpen",Hidden).SetValue(overlay,false);typeof(SceneOverlay).GetMethod("Apply",Hidden).Invoke(overlay,new object[]{false});
                var quick=overlay.GetComponentsInChildren<RectTransform>(true).Single(r=>r.name=="Regenerate selected year");
                Check(quick.gameObject.activeInHierarchy&&quick.anchoredPosition==new Vector2(370,0),"Desktop regeneration is at middle right with panels collapsed");
                Check(quick.GetComponentInChildren<Text>().text=="REGENERATE 1936","Scene quick action matches selected year");
                typeof(SceneOverlay).GetField("actionsOpen",Hidden).SetValue(overlay,true);typeof(SceneOverlay).GetMethod("Apply",Hidden).Invoke(overlay,new object[]{false});
                var actions=overlay.GetComponentsInChildren<RectTransform>(true).Single(r=>r.name=="Scene actions");
                Check(quick.gameObject.activeInHierarchy&&actions.anchoredPosition.y-actions.rect.height/2>quick.anchoredPosition.y+quick.rect.height/2,"Expanded scene actions do not hide or overlap middle-right regeneration");
                typeof(SceneOverlay).GetMethod("ConfigureForMode",Hidden).Invoke(overlay,new object[]{true});Canvas.ForceUpdateCanvases();
                Check(quick.anchoredPosition==new Vector2(285,0)&&quick.rect.width>=180,"Quest action is inset without shrinking its target");
                g.SetYear(person.Id,1950);typeof(SceneOverlay).GetMethod("Refresh",Hidden).Invoke(overlay,null);
                Check(overlay.HandleInput(new Ray(quick.position-quick.forward,quick.forward),true,true,false),"Quest-style ray captures the quick action");
                Check(fixture.Waiting.Single().job.year==1950&&fixture.Waiting.Single().job.place=="Monterey","Scene quick action queues the slider year rather than nearest cached panorama");fixture.ClearWaiting();
                g.Hud.SetVisible(false);Check(!overlay.HandleInput(new Ray(quick.position-quick.forward,quick.forward),true,true,false)&&fixture.WaitingCount==0,"Hidden HUD cannot trigger regeneration");g.Hud.SetVisible(true);
                var regenButton=prefs.GetComponentsInChildren<Button>(true).Single(b=>b.name=="Regenerate All");prefs.SetOpen(true);prefs.GetComponentsInChildren<Button>(true).Single(b=>b.name=="Scenes").onClick.Invoke();Check(regenButton.gameObject.activeInHierarchy,"Scene settings exposes Regenerate All");regenButton.onClick.Invoke();
                Check(g.Batch.IsOpen&&g.Batch.Review.Contains("Are you sure?")&&g.Batch.Review.Contains("2 cached scenes"),"Settings opens an explicit current-tree count confirmation");
                Check(fixture.WaitingCount==0&&!prefs.IsOpen,"Opening the alert enqueues nothing and closes preferences");var cancel=g.Batch.GetComponentsInChildren<RectTransform>(true).First(r=>r.name=="Cancel"&&r.GetComponent<Image>()!=null);g.Batch.HandleInput(new Ray(cancel.position-cancel.forward,cancel.forward),true,true,false);Check(!g.Batch.IsOpen&&fixture.WaitingCount==0,"Cancel ray closes the alert and leaves the queue unchanged");
                g.Batch.OpenRegenerateAll();g.Batch.ConfirmRegenerateAll();
                Check(fixture.WaitingCount==2&&fixture.Waiting.All(p=>p.isBulkRegeneration&&!p.isAutomatic),"Confirm queues explicit replacements for all cached years only");
                Check(fixture.Waiting.Select(p=>p.job.year).OrderBy(y=>y).SequenceEqual(new[]{1936,1975}),"Bulk action does not generate unrequested intermediate years");
                g.Batch.Close();g.Batch.OpenRegenerateAll();Check(g.Batch.Review.Contains("0 cached scenes"),"Second confirmation excludes already scheduled years");g.Batch.ConfirmRegenerateAll();Check(fixture.WaitingCount==2,"Repeated bulk confirmation never duplicates targets");g.Batch.Close();
                Check(fixture.ActiveCount==0&&fixture.CompletedCount==0,"Fixture never dispatches an AI request or saves an image");
                fixture.ClearWaiting();
                string input=PanelInputChecks.Run();
                File.WriteAllText(Report,n+" regeneration UI checks passed; "+data+" "+input+" No AI calls or family-cache writes.");
            }
            finally
            {
                g.Batch.Close();typeof(AncestorGallery).GetProperty("Generation").SetValue(g,generation);g.OpenDocument(document);if(selected!=null)g.Select(selected);
                nav.enabled=navEnabled;nav.transform.SetPositionAndRotation(position,rotation);nav.view.transform.localRotation=head;
                g.Hud.SetVisible(hud);prefs.SetOpen(prefOpen);if(tutorial)g.Tutorial.Open();SessionState.SetBool("AncestorWalk.EndpointChecks",guard);
                if(string.IsNullOrEmpty(lastTree))PlayerPrefs.DeleteKey("AncestorWalk.LastTree");else PlayerPrefs.SetString("AncestorWalk.LastTree",lastTree);
                PlayerPrefs.DeleteKey("year."+Tree+"."+person.Id);foreach(string name in new[]{"timeline","info","actions"})PlayerPrefs.DeleteKey("overlay."+Tree+"."+PanoramaCache.PersonKey(person.Id)+"."+name);
                UnityEngine.Object.Destroy(holder);
            }
        }
    }
}
