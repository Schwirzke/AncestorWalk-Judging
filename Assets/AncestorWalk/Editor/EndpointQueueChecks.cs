using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace AncestorWalk.Editor
{
    public static class EndpointQueueChecks
    {
        const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        static readonly string Tree=PanoramaCache.Hash(Encoding.UTF8.GetBytes("four-slot-endpoint-fixture"));
        static PendingScene Item(int year,string person="fixture",bool automatic=false)=>new PendingScene{treeId=Tree,personId=person,phase=1,isAutomatic=automatic,job=new SceneJob{treeId=Tree,personKey=PanoramaCache.PersonKey(person),requestId=Guid.NewGuid().ToString("N"),year=year,date=year.ToString(),place="York",synthetic=false}};
        static byte[] Image(Color color)
        {var image=new Texture2D(256,128,TextureFormat.RGBA32,false);image.SetPixels(Enumerable.Repeat(color,256*128).ToArray());image.Apply();var png=image.EncodeToPNG();UnityEngine.Object.Destroy(image);return png;}
        static SceneJob Result(byte[] png,int year,string place="York")=>new SceneJob{year=year,place=place,sha256=PanoramaCache.Hash(png),prompt="fixture",model="fixture",createdUtc="2000-01-01T00:00:00Z"};
        public static string Data()
        {
            int n=0;void Check(bool value,string why){if(!value)throw new Exception(why);n++;}
            var p=new Person{Id="fixture",BirthDate="1900",BirthPlace="York"};var moves=new ResidenceTimeline();
            Check(LifeEndpointScenes.Targets(p,moves).SequenceEqual(new[]{(1900,"York")}),"Birth independently qualifies without death or living inference");
            Check(!p.KnownLiving&&LifeEndpointScenes.TrySceneRange(p,out int start,out int end)&&start==1900&&end==1900,"Birth-only playback is one known year");
            p.BirthDate="";p.DeathDate="1980";p.DeathPlace="Paris";
            Check(LifeEndpointScenes.Targets(p,moves).SequenceEqual(new[]{(1980,"Paris")}),"Death independently qualifies without birth");
            Check(LifeEndpointScenes.TrySceneRange(p,out start,out end)&&start==1980&&end==1980,"Death-only playback is one known year");
            p.BirthDate="1900";p.DeathPlace="";moves.entries.Add(new Residence{year=1950,place="Berlin"});
            Check(LifeEndpointScenes.Targets(p,moves).SequenceEqual(new[]{(1900,"York")}),"Unknown death place does not inherit a birthplace or earlier move");
            moves.entries.Add(new Residence{year=1980,place="Rome"});Check(LifeEndpointScenes.Targets(p,moves).Last()==(1980,"Rome"),"Exact saved endpoint location takes precedence");
            p.DeathDate="1900";p.DeathPlace="Lima";Check(LifeEndpointScenes.Targets(p,moves).SequenceEqual(new[]{(1900,"Lima")}),"Same-year endpoints deduplicate with death-place precedence");
            p.DeathPlace="";Check(LifeEndpointScenes.Targets(p,moves).SequenceEqual(new[]{(1900,"York")}),"Empty same-year death place does not discard a valid birth event");
            p.KnownLiving=true;p.DeathDate="1980";Check(LifeEndpointScenes.Targets(p,moves).Count()==1,"Living never invents a death/current-year endpoint");p.KnownLiving=false;
            foreach(string bad in new[]{"","BEF 1900","BET 1890 AND 1900","FROM 1900 TO 1910","9999"})
            {p.BirthDate=bad;p.DeathDate="";Check(!LifeEndpointScenes.Targets(p,moves).Any(),"Missing/ambiguous/future birth skipped");}
            p.BirthDate="1980";p.DeathDate="1900";Check(!LifeEndpointScenes.Targets(p,moves).Any(),"Reversed endpoints rejected");
            var state=new SceneQueueState();var first=Item(1900,automatic:true);state.active.Add(first);
            var changed=Item(1900,automatic:true);changed.job.place="Paris";
            Check(state.AppendAutomatic(new[]{changed},null)==0,"Active automatic person/year deduplicates even when location changes");
            var json=JsonUtility.ToJson(state);var restored=JsonUtility.FromJson<SceneQueueState>(json);restored.Validate();Check(restored.active.Single().job.requestId==first.job.requestId,"Version-two active request identity persists");
            var png=Image(Color.blue);var replacement=Image(Color.green);string directory=Path.GetFullPath("Library/EndpointCacheChecks/"+Guid.NewGuid().ToString("N"));
            var record=PanoramaCache.SaveDecodedAt(directory,Tree,"fixture",1900,png,Result(png,1900,"Old location"),256,128);
            string root=Path.Combine(directory,"people",Tree,PanoramaCache.PersonKey("fixture"),"years","1900");
            var files=Directory.GetFiles(root).ToDictionary(f=>f,f=>PanoramaCache.Hash(File.ReadAllBytes(f)));
            long reads=PanoramaCache.ImageReadCount,hashes=PanoramaCache.ImageValidationCount;
            for(int i=0;i<20;i++)Check(PanoramaCache.HasImageAt(directory,Tree,"fixture",1900),"Indexed/stale-location image is present");
            Check(reads==PanoramaCache.ImageReadCount&&hashes==PanoramaCache.ImageValidationCount,"Unchanged cache probes never reread/hash PNGs");
            for(int i=0;i<4;i++)Check(PanoramaCache.YearRecordsAt(directory,Tree,"fixture").Single().year==1900,"Person refresh reuses validated record");
            Check(reads==PanoramaCache.ImageReadCount&&hashes==PanoramaCache.ImageValidationCount,"Concurrent result refreshes do not rehash unchanged person images");
            Check(!PanoramaCache.SaveDecodedIfMissingAt(directory,Tree,"fixture",1900,replacement,Result(replacement,1900,"Changed location"),256,128),"Automatic save never replaces an existing image");
            Check(files.All(f=>File.Exists(f.Key)&&PanoramaCache.Hash(File.ReadAllBytes(f.Key))==f.Value),"Manifest and PNG remain byte-for-byte unchanged");
            Check(Directory.GetFiles(root).Length==files.Count,"Rejected replacement creates no image files");
            File.WriteAllBytes(Path.Combine(root,record.file),new byte[]{1,2,3});
            Check(!PanoramaCache.HasImageAt(directory,Tree,"fixture",1900),"Corrupt cache is not treated as a valid image");
            Check(PanoramaCache.SaveDecodedIfMissingAt(directory,Tree,"fixture",1900,replacement,Result(replacement,1900),256,128),"Missing/corrupt cache can be populated");
            PanoramaCache.SaveDecodedAt(directory,Tree,"legacy-birth",0,png,Result(png,0),256,128);
            var legacy=new Person{Id="legacy-birth",BirthDate="1900",BirthPlace="York"};PanoramaCache.MigrateAt(directory,Tree,new[]{legacy},false);
            Check(PanoramaCache.HasImageAt(directory,Tree,legacy.Id,1900),"Legacy birth panorama migrates even with an unknown death");
            Check(PanoramaCache.YearRecordsAt(directory,Tree,legacy.Id).Single().sha256==PanoramaCache.Hash(png),"Legacy image is reused without a model request");
            var text=n+" endpoint/cache data checks passed: independent dates, event locations, deduplication, active identities, unchanged-image protection and metadata-only repeat probes.";
            File.WriteAllText("Artifacts/endpoint-data-checks.txt",text);return text;
        }
        public static string Play()
        {
            var g=UnityEngine.Object.FindAnyObjectByType<AncestorGallery>();if(!Application.isPlaying||g==null)throw new Exception("Play mode required");
            File.WriteAllText("Artifacts/scene-queue-play-checks.txt","RUNNING");g.StartCoroutine(Runner(Checks(g)));return "Running isolated four-slot queue checks; no Meta calls.";
        }
        static IEnumerator Runner(IEnumerator routine)
        {
            var stack=new Stack<IEnumerator>();stack.Push(routine);
            while(stack.Count>0)
            {
                var current=stack.Peek();bool more=false;Exception error=null;
                try{more=current.MoveNext();}catch(Exception e){error=e;}
                if(error!=null){while(stack.Count>0)(stack.Pop() as IDisposable)?.Dispose();File.WriteAllText("Artifacts/scene-queue-play-checks.txt","FAILED: "+error);Debug.LogException(error);yield break;}
                if(!more){(stack.Pop() as IDisposable)?.Dispose();continue;}if(current.Current is IEnumerator next){stack.Push(next);continue;}yield return current.Current;
            }
        }
        static IEnumerator Checks(AncestorGallery gallery)
        {
            int n=0;void Check(bool value,string why){if(!value)throw new Exception(why);n++;}
            var png=Image(Color.blue);var existing=Image(Color.green);var person=new Person{Id="fixture",Name="Fixture",BirthDate="1900",BirthPlace="York",DeathDate="1980",DeathPlace="Paris"};
            string root=Path.GetFullPath("Library/EndpointQueueChecks/"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);GameObject go=null;
            SceneGeneration Create(string path,SceneQueueChecks.FakeHelper helper)
            {
                go=new GameObject("Isolated four-slot queue");go.SetActive(false);var gen=go.AddComponent<SceneGeneration>();
                typeof(SceneGeneration).GetField("TestStorage",Hidden).SetValue(gen,path);typeof(SceneGeneration).GetField("TestConnection",Hidden).SetValue(gen,new SceneServiceConnection{url=helper.Url,token=new string('a',64)});go.SetActive(true);return gen;
            }
            IEnumerator Until(Func<bool> done,string why,float seconds=25)
            {float deadline=Time.realtimeSinceStartup+seconds;while(!done()&&Time.realtimeSinceStartup<deadline)yield return null;Check(done(),why);}
            void Dispose(){if(go!=null)UnityEngine.Object.DestroyImmediate(go);go=null;}
            try
            {
                using(var helper=new SceneQueueChecks.FakeHelper(png){Hold=true})
                {
                    string path=Path.Combine(root,"four");var gen=Create(path,helper);yield return Until(()=>gen.Ready,"Concurrent queue ready");
                    gen.Enqueue(Tree,person,false,Enumerable.Range(1900,6).Select(y=>(y,"York")));int frame=Time.frameCount;
                    yield return Until(()=>helper.Requests.Count==4,"Exactly four requests are dispatched");
                    Check(gen.ActiveCount==4&&gen.WaitingCount==2&&helper.MaximumActive==4,"Four global slots with remaining work queued");
                    gen.PauseQueue();helper.Hold=false;yield return Until(()=>!gen.Busy&&gen.CompletedCount==4,"Pause lets all active slots finish");
                    Check(gen.WaitingCount==2&&helper.Requests.Count==4&&Time.frameCount>frame,"Pause prevents new posts while frames progress");
                    gen.ResumeQueue();yield return Until(()=>gen.CompletedCount==6&&!gen.Running&&!gen.Busy,"Resume drains remaining work");
                    Check(helper.MaximumActive==4&&helper.Requests.All(x=>!x.synthetic),"Imported provenance and four-job cap hold");
                    helper.Hold=true;gen.Enqueue(Tree,person,false,Enumerable.Range(1920,6).Select(y=>(y,"York")));
                    yield return Until(()=>gen.ActiveCount==4&&helper.Requests.Count==10,"Clear fixture has four active and two waiting");
                    gen.ClearWaiting();Check(gen.WaitingCount==0&&gen.ActiveCount==4,"Clear removes waiting work without cancelling active slots");helper.Hold=false;
                    yield return Until(()=>gen.CompletedCount==10&&!gen.Busy,"Cleared queue finishes only its four active jobs");Check(helper.Requests.Count==10,"Cleared waiting targets were never submitted");
                    var saved=JsonUtility.FromJson<SceneQueueState>(File.ReadAllText(Path.Combine(path,"scene-generation-queue.json")));saved.Validate();Check(saved.version==2&&saved.active.Count==0,"All active checkpoints clear atomically");Dispose();
                }
                using(var helper=new SceneQueueChecks.FakeHelper(png){Hold=true})
                {
                    var gen=Create(Path.Combine(root,"priority"),helper);yield return Until(()=>gen.Ready,"Priority queue ready");
                    var people=Enumerable.Range(0,4).Select(i=>new Person{Id="endpoint-"+i,BirthDate="1900",BirthPlace="York",DeathDate="1980",DeathPlace="Paris"}).ToArray();
                    Check(gen.ScheduleEndpoints(Tree,people,false,_=>new ResidenceTimeline(),_=>Array.Empty<int>())==8,"Imported tree schedules all independent endpoints");
                    yield return Until(()=>helper.Requests.Count==4,"Automatic jobs occupy four slots");gen.Enqueue(Tree,person,false,new[]{(1934,"Monterey")});helper.Hold=false;
                    yield return Until(()=>gen.CompletedCount==9&&!gen.Running&&!gen.Busy,"Priority fixture finishes");Check(helper.Requests[4].year==1934,"Manual request takes the next available slot");
                    Check(gen.ScheduleEndpoints(Tree,people,false,_=>new ResidenceTimeline(),_=>Array.Empty<int>())==0,"Reopening does not resubmit attempted endpoints");Dispose();
                }
                using(var helper=new SceneQueueChecks.FakeHelper(png))
                {
                    string path=Path.Combine(root,"protect"),cache=Path.Combine(path,"Panoramas");
                    PanoramaCache.SaveDecodedAt(cache,Tree,person.Id,1900,existing,Result(existing,1900,"Previous location"),256,128);
                    var before=Directory.GetFiles(cache,"*",SearchOption.AllDirectories).ToDictionary(x=>x,x=>PanoramaCache.Hash(File.ReadAllBytes(x)));
                    helper.BeforeImage=job=>PanoramaCache.SaveDecodedAt(cache,Tree,person.Id,job.year,existing,Result(existing,job.year,"Concurrent result"),256,128);
                    var gen=Create(path,helper);yield return Until(()=>gen.Ready,"Cache guard fixture ready");
                    gen.ScheduleEndpoints(Tree,new[]{person},false,_=>new ResidenceTimeline(),_=>Array.Empty<int>());
                    yield return Until(()=>gen.SkippedCount==2&&!gen.Busy&&!gen.Running,"Preflight and commit both preserve existing images");
                    Check(helper.Requests.Count==1&&helper.Requests[0].year==1980,"Existing birth image never reaches generation even with stale location");
                    Check(before.All(f=>PanoramaCache.Hash(File.ReadAllBytes(f.Key))==f.Value),"Preexisting image and manifest bytes unchanged");
                    Check(PanoramaCache.YearRecordsAt(cache,Tree,person.Id).All(r=>r.sha256==PanoramaCache.Hash(existing)),"Concurrent generated result wins over automatic replacement");Dispose();
                }
                foreach(string failure in new[]{"ambiguous","download","busy","rate","model"})
                using(var helper=new SceneQueueChecks.FakeHelper(png){AmbiguousOnce=failure=="ambiguous",ImageFailureOnce=failure=="download",RejectOnce=failure=="busy",RateLimitOnce=failure=="rate",FailYear=failure=="model"?1900:-1})
                {
                    var gen=Create(Path.Combine(root,failure),helper);yield return Until(()=>gen.Ready,"Failure fixture ready");gen.Enqueue(Tree,person,false,new[]{(1900,"York")});
                    if(failure=="ambiguous"||failure=="download")
                    {
                        yield return Until(()=>!gen.Busy&&!gen.Running&&gen.Active!=null,"Uncertain result keeps original checkpoint");int posts=helper.Posts;string id=gen.Active.job.requestId;
                        gen.ResumeQueue();yield return Until(()=>gen.CompletedCount==1&&!gen.Busy,"Resume recovers without another POST");Check(helper.Posts==posts&&helper.PostIds[0]==id,"Original request identity is checked, not regenerated");
                    }
                    else if(failure=="model")
                    {yield return Until(()=>gen.FailedCount==1&&!gen.Busy,"Model failure is recorded");Check(helper.Posts==1&&gen.ActiveJobs.Count==0,"Failed model request is not automatically retried");}
                    else
                    {yield return Until(()=>gen.CompletedCount==1&&!gen.Busy,"Definitive capacity/rate rejection waits and retries");Check(helper.Posts==2&&helper.PostIds.Distinct().Count()==1,"Rejected submission retains the same request ID");}
                    Dispose();
                }
                foreach(bool submitted in new[]{false,true})
                using(var helper=new SceneQueueChecks.FakeHelper(png))
                {
                    string path=Path.Combine(root,submitted?"legacy-sent":"legacy-unsent");Directory.CreateDirectory(path);var pending=Item(1900);pending.phase=submitted?2:1;
                    var old=new SceneQueueState{version=1};if(!submitted)old.waiting.Add(pending);
                    File.WriteAllText(Path.Combine(path,"scene-generation-queue.json"),JsonUtility.ToJson(old));File.WriteAllText(Path.Combine(path,"pending-scene.json"),JsonUtility.ToJson(pending));if(submitted)helper.Seed(pending.job);
                    var gen=Create(path,helper);yield return Until(()=>gen.Ready,"Legacy checkpoint migrates");
                    Check(!File.Exists(Path.Combine(path,"pending-scene.json")),"Legacy checkpoint retired after version-two write");
                    gen.Resume(Tree);yield return Until(()=>gen.CompletedCount==1&&!gen.Busy,"Opened tree resumes legacy work safely");Check(helper.Posts==(submitted?0:1),"Submitted legacy request uses GET; unsent request posts once");Dispose();
                }
                using(var helper=new SceneQueueChecks.FakeHelper(png){Hold=true})
                {
                    string path=Path.Combine(root,"restart-four");var gen=Create(path,helper);yield return Until(()=>gen.Ready,"Restart fixture ready");gen.Enqueue(Tree,person,false,Enumerable.Range(1900,4).Select(y=>(y,"York")));
                    yield return Until(()=>helper.Requests.Count==4,"Four requests checkpointed before simulated restart");var ids=helper.PostIds.ToArray();Dispose();helper.Hold=false;
                    gen=Create(path,helper);yield return Until(()=>gen.Ready,"Four active checkpoints restored");gen.Resume(Tree);yield return Until(()=>gen.CompletedCount==4&&!gen.Busy,"All four submitted jobs recover by GET");
                    Check(helper.PostIds.SequenceEqual(ids),"Restart adds no generation POSTs");Dispose();
                }
                yield return Playback(gallery,Check);
                yield return Until(()=>gallery.CacheIndexReady,"Original family cache index restored after playback checks");
                File.WriteAllText("Artifacts/scene-queue-play-checks.txt",n+" concurrent endpoint queue checks passed: four slots, imported automatic endpoints, manual priority, cache preservation before POST/commit, pause/resume, rate/capacity limits, uncertain/download recovery, failures, legacy migration, four-job restart and birth/death-only scene playback. No Meta calls.");
            }
            finally{Dispose();}
        }
        static IEnumerator Playback(AncestorGallery g,Action<bool,string> check)
        {
            var document=g.Document;string selected=g.SelectedId;var nav=UnityEngine.Object.FindAnyObjectByType<GalleryNavigation>();
            var position=nav.transform.position;var rotation=nav.transform.rotation;var look=nav.view.transform.localRotation;var angles=g.TreeRotation;
            var doc=new TreeDocument{Id=TreeStore.NewId(),Name="Endpoint playback fixture"};
            doc.People.Add(new Person{Id="birth-only",Name="Birth fixture",BirthDate="1900",BirthPlace="York"});
            doc.People.Add(new Person{Id="death-only",Name="Death fixture",DeathDate="1980",DeathPlace="Paris"});
            var png=Image(Color.blue);string root=Path.Combine(PanoramaCache.DirectoryPath,"people",doc.Id);
            foreach(var p in doc.People)
            {int year=p.Id=="birth-only"?1900:1980;PanoramaCache.SaveDecodedAt(PanoramaCache.DirectoryPath,doc.Id,p.Id,year,png,Result(png,year,p.Id=="birth-only"?"York":"Paris"),256,128);}
            try
            {
                g.OpenDocument(doc);while(!g.CacheIndexReady)yield return null;
                foreach(var p in doc.People)
                {
                    int year=p.Id=="birth-only"?1900:1980;
                    check(g.Year(p.Id)==year&&g.SceneYears(p.Id).SequenceEqual(new[]{year}),"Partial record exposes its known cached endpoint");
                    var node=g.GetComponentsInChildren<TreeNodeTarget>().Single(n=>n.PersonId==p.Id);
                    check(node.Controls.Panel.GetComponentsInChildren<YearSlider>(true).Length==0,"Unknown lifespan has no invented lifespan slider");
                    g.Select(p.Id);float deadline=Time.realtimeSinceStartup+15;
                    while((g.IsSceneLoading||g.IsAllTime)&&Time.realtimeSinceStartup<deadline)yield return null;
                    check(!g.IsAllTime&&!g.IsSceneLoading,"Known endpoint panorama enters without a complete lifespan");
                    var rail=g.Overlay.GetComponentsInChildren<UnityEngine.RectTransform>(true).Single(r=>r.name=="Year slider");
                    check(!rail.gameObject.activeSelf,"One known year hides the in-scene lifespan slider");
                    var context=HistoricalGuide.Context(g,p);check(context.year==year&&!context.synthetic&&!p.KnownLiving,"Guide receives the exact endpoint without inferring living");
                    g.ShowTree();yield return null;
                }
            }
            finally
            {
                g.OpenDocument(document);g.SetTreeRotation(angles);if(selected!=null)g.Select(selected);
                nav.transform.SetPositionAndRotation(position,rotation);nav.view.transform.localRotation=look;
                typeof(GalleryNavigation).GetField("pitch",Hidden).SetValue(nav,Mathf.DeltaAngle(0,look.eulerAngles.x));
                if(Directory.Exists(root))Directory.Delete(root,true);
            }
        }
    }
}
