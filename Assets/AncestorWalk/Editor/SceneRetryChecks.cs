using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace AncestorWalk.Editor
{
    public static class SceneRetryChecks
    {
        const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        const string Report="Artifacts/scene-retry-checks.txt";
        static readonly string Tree=new string('a',64);
        static Person Person()=>new Person{Id="retry-fixture",BirthDate="1900",BirthPlace="York",DeathDate="1980",DeathPlace="Paris"};
        static PendingScene Pending()=>new PendingScene{treeId=Tree,personId="retry-fixture",phase=2,job=new SceneJob{requestId=new string('b',32),treeId=Tree,personKey=PanoramaCache.PersonKey("retry-fixture"),year=1900,date="1900",place="York"}};
        public static string Data()
        {
            int n=0;void Check(bool ok,string why){if(!ok)throw new Exception(why);n++;}
            var p=Pending();var failure=new SceneJob{status="failed",retryable=true};string id=p.job.requestId;
            foreach(int delay in new[]{15,30,60})
            {
                var next=SceneRetryPolicy.Next(p,failure,1000);SceneQueueState.Validate(next);
                Check(next.notBeforeUnix==1000+delay,"Increasing retry delay");
                Check(next.retryCount==p.retryCount+1&&next.phase==1,"New attempt has persisted count and unsent phase");
                Check(next.job.requestId!=id&&next.job.requestId!=p.job.requestId,"Only a confirmed failed request gets a new identity");
                Check(SceneQueueState.SameYear(p,next)&&ResidenceTimeline.Same(p.job.place,next.job.place),"Exact person/year/place preserved");p=next;
            }
            Check(SceneRetryPolicy.Next(p,failure,1000)==null,"Three retries is the limit");
            var policy=new SceneJob{status="failed",failureCode="image_http_400",error="Meta HTTP 400. code: content_policy_violation. param: prompt.",settingBrief=new SceneSettingBrief{text="Ordinary setting"}};
            var candidate=Pending();candidate.baselineSet=true;candidate.baselineSha=new string('c',64);candidate.job.details="Original saved creative directions";
            int previousLength=int.MaxValue;
            for(int level=1;level<=3;level++)
            {
                candidate=SceneRetryPolicy.Next(candidate,policy,1000);SceneQueueState.Validate(candidate);
                Check(candidate.job.policyRetryLevel==level&&candidate.retryCount==level,"Policy retries increment the durable simplification level");
                Check(candidate.job.details=="Original saved creative directions"&&candidate.baselineSha==new string('c',64),"Saved details and replacement baseline remain intact");
                var brief=new SceneSettingBrief{text=string.Join(" ",Enumerable.Range(0,150).Select(i=>"ordinary"+i))};
                string prompt=DirectMetaScene.Prompt(candidate.job,brief);
                Check(!prompt.Contains(candidate.job.details)&&prompt.Contains("York in 1900")&&prompt.Contains("exact 2:1"),"Simpler requests omit optional directions while preserving target and VR projection");
                Check(prompt.Length<previousLength,"Each refusal produces less text");previousLength=prompt.Length;
                var checkpoint=JsonUtility.FromJson<PendingScene>(JsonUtility.ToJson(candidate));Check(checkpoint.job.policyRetryLevel==level,"Restart retains simplification level");
            }
            Check(SceneRetryPolicy.Next(candidate,policy,1000)==null,"Policy refusals stop after three retries");
            Check(SceneRetryPolicy.Next(Pending(),new SceneJob{status="failed",failureCode="image_http_400",error="Meta HTTP 400. code: content_policy_violation. param: images."},1000)==null,"Image-input refusals are not rewritten as prompt retries");
            Check(SceneRetryPolicy.Next(Pending(),new SceneJob{status="failed",failureCode="image_uncertain",retryable=true},1000)==null,"Even a misclassified uncertain image cannot get a new identity");
            var retained=SceneRetryPolicy.Next(new PendingScene{treeId=Tree,personId="retry-fixture",phase=2,retryCount=1,job=new SceneJob{policyRetryLevel=1,requestId=new string('d',32),treeId=Tree,personKey=PanoramaCache.PersonKey("retry-fixture"),place="York",year=1900,date="1900"}},failure,1000);
            Check(retained.job.policyRetryLevel==1&&retained.retryCount==2,"Transport retries retain simplification and share the total budget");
            var frozen=Pending();frozen.job.policyRetryLevel=1;frozen.retryCount=1;frozen.job.settingBrief=new SceneSettingBrief{text="Frozen refused context"};
            var temporary=SceneRetryPolicy.Next(frozen,new SceneJob{status="failed",retryable=true,settingBrief=new SceneSettingBrief{text="Already revised research"}},1000);
            Check(temporary.job.settingBrief.text=="Frozen refused context","Temporary failures reuse the same simplified research cache identity");
            foreach(string status in new[]{"generating","complete","rate_limited",null})Check(SceneRetryPolicy.Next(Pending(),new SceneJob{status=status,retryable=true},1000)==null,"Uncertain/success/rejected requests never get a new generation identity");
            Check(SceneRetryPolicy.Next(Pending(),new SceneJob{status="failed"},1000)==null,"Permanent or legacy unclassified failures remain manual");
            var restored=JsonUtility.FromJson<PendingScene>(JsonUtility.ToJson(p));SceneQueueState.Validate(restored);
            Check(restored.retryCount==3&&restored.notBeforeUnix==1060&&restored.job.requestId==p.job.requestId,"Restart preserves budget, delay and request identity");
            foreach(double invalid in new[]{-1d,double.NaN,double.PositiveInfinity}){var bad=Pending();bad.notBeforeUnix=invalid;bool rejected=false;try{SceneQueueState.Validate(bad);}catch(InvalidOperationException){rejected=true;}Check(rejected,"Invalid retry timestamps rejected");}
            return n+" scene retry policy checks passed.";
        }
        public static string Play()
        {
            if(!Application.isPlaying)throw new Exception("Play mode required");
            var owner=UnityEngine.Object.FindAnyObjectByType<AncestorGallery>();Directory.CreateDirectory("Artifacts");File.WriteAllText(Report,"RUNNING");owner.StartCoroutine(Runner(Checks()));return "Running isolated retry queue checks; no Meta calls.";
        }
        static IEnumerator Runner(IEnumerator first)
        {
            var stack=new Stack<IEnumerator>();stack.Push(first);
            while(stack.Count>0)
            {
                var current=stack.Peek();bool more=false;Exception failure=null;try{more=current.MoveNext();}catch(Exception e){failure=e;}
                if(failure!=null){while(stack.Count>0)(stack.Pop() as IDisposable)?.Dispose();File.WriteAllText(Report,"FAILED: "+failure);Debug.LogException(failure);yield break;}
                if(!more){(stack.Pop() as IDisposable)?.Dispose();continue;}if(current.Current is IEnumerator nested){stack.Push(nested);continue;}yield return current.Current;
            }
        }
        static IEnumerator Checks()
        {
            int n=0;void Check(bool ok,string why){if(!ok)throw new Exception(why);n++;}
            byte[] Image(Color color){var t=new Texture2D(256,128);t.SetPixels(Enumerable.Repeat(color,256*128).ToArray());t.Apply();var bytes=t.EncodeToPNG();UnityEngine.Object.Destroy(t);return bytes;}
            var png=Image(Color.green);var old=Image(Color.blue);string root=Path.GetFullPath("Library/SceneRetryChecks/"+Guid.NewGuid().ToString("N"));double clock=2000000000;GameObject go=null;
            SceneGeneration Create(string path,SceneQueueChecks.FakeHelper helper)
            {
                go=new GameObject("Isolated retry queue");go.SetActive(false);var g=go.AddComponent<SceneGeneration>();
                typeof(SceneGeneration).GetField("TestStorage",Hidden).SetValue(g,path);typeof(SceneGeneration).GetField("TestConnection",Hidden).SetValue(g,new SceneServiceConnection{url=helper.Url,token=new string('a',64)});typeof(SceneGeneration).GetField("TestClock",Hidden).SetValue(g,(Func<double>)(()=>clock));go.SetActive(true);return g;
            }
            IEnumerator Until(Func<bool> done,string why){float deadline=Time.realtimeSinceStartup+20;while(!done()&&Time.realtimeSinceStartup<deadline)yield return null;Check(done(),why);}
            void Dispose(){if(go!=null)UnityEngine.Object.DestroyImmediate(go);go=null;}
            void Save(string cache,byte[] image)=>PanoramaCache.SaveDecodedAt(cache,Tree,"retry-fixture",1900,image,new SceneJob{sha256=PanoramaCache.Hash(image),place="York",prompt="fixture",model="fixture",createdUtc="2000-01-01T00:00:00Z"},256,128);
            try
            {
                using(var helper=new SceneQueueChecks.FakeHelper(png){TransientFailures=1,ReadyDelayMilliseconds=0})
                {
                    string path=Path.Combine(root,"manual-batch");var gen=Create(path,helper);yield return Until(()=>gen.Ready,"Manual fixture ready");
                    gen.Generate(Tree,Person(),false,1900,"York");gen.Enqueue(Tree,Person(),false,new[]{(1901,"York")});
                    yield return Until(()=>gen.CompletedCount==1&&gen.WaitingCount==1&&!gen.Busy,"Manual failure waits while another batch year completes");
                    var retry=gen.Waiting.Single();string id=retry.job.requestId;Check(retry.retryCount==1&&retry.notBeforeUnix==clock+15&&helper.Posts==2&&gen.FailedCount==0,"Retry frees slot and is not a terminal failure");
                    Check(gen.QueuedYears(Tree,"retry-fixture").Contains(retry.job.year)&&gen.TryGetProgress(Tree,"retry-fixture",retry.job.year,out var label)&&label.Contains("Retry 1/3"),"Delayed retry remains visible in timeline/progress");
                    clock+=14;yield return null;yield return null;Check(helper.Posts==2,"No submission before backoff expires");
                    gen.PauseQueue();clock+=100;yield return null;yield return null;Check(helper.Posts==2,"Pause holds eligible retries");
                    yield return Until(()=>JsonUtility.FromJson<SceneQueueState>(File.ReadAllText(Path.Combine(path,"scene-generation-queue.json"))).automaticPaused,"Pause and retry checkpoint persisted");
                    Dispose();gen=Create(path,helper);yield return Until(()=>gen.Ready,"Retry checkpoint restored");gen.Resume(Tree);yield return null;
                    Check(gen.Waiting.Single().job.requestId==id&&gen.Waiting.Single().retryCount==1&&gen.QueuePaused&&helper.Posts==2,"Restart preserves count, identity and explicit pause");
                    gen.ResumeQueue();yield return Until(()=>gen.CompletedCount==1&&!gen.Busy&&gen.WaitingCount==0,"Resume retries the failed manual year successfully");
                    Check(helper.Posts==3&&helper.PostIds.Distinct().Count()==3&&gen.FailedCount==0,"Only failed year gets a new request");Dispose();
                }
                foreach(bool automatic in new[]{false,true})
                using(var helper=new SceneQueueChecks.FakeHelper(png){TransientFailures=99,ReadyDelayMilliseconds=0})
                {
                    var gen=Create(Path.Combine(root,automatic?"automatic-limit":"manual-limit"),helper);yield return Until(()=>gen.Ready,"Budget fixture ready");var person=Person();person.DeathDate="";
                    if(automatic)gen.ScheduleEndpoints(Tree,new[]{person},false,_=>new ResidenceTimeline(),_=>Array.Empty<int>());else gen.Generate(Tree,person,false,1900,"York");
                    for(int retry=1;retry<=3;retry++){int count=retry;yield return Until(()=>gen.WaitingCount==1&&!gen.Busy&&gen.Waiting[0].retryCount==count,"Backoff "+retry+" queued");clock+=100;}
                    yield return Until(()=>gen.FailedCount==1&&!gen.Busy&&gen.WaitingCount==0,"Retry budget terminates");
                    Check(helper.Posts==4&&helper.PostIds.Distinct().Count()==4,"Initial attempt plus exactly three retries");Dispose();
                }
                foreach(bool exhaust in new[]{false,true})
                using(var helper=new SceneQueueChecks.FakeHelper(png){PolicyFailures=exhaust?99:1,ReadyDelayMilliseconds=0})
                {
                    string path=Path.Combine(root,exhaust?"policy-limit":"policy-success"),cache=Path.Combine(path,"Panoramas");Save(cache,old);
                    var gen=Create(path,helper);yield return Until(()=>gen.Ready,"Policy fixture ready");gen.Generate(Tree,Person(),false,1900,"York");
                    int last=exhaust?3:1;
                    for(int level=1;level<=last;level++)
                    {
                        int expected=level;yield return Until(()=>gen.WaitingCount==1&&!gen.Busy&&gen.Waiting[0].job.policyRetryLevel==expected,"Simpler prompt level "+level+" waits");
                        Check(gen.TryGetProgress(Tree,"retry-fixture",1900,out string label)&&label.Contains("simpler scene"),"UI explains simplified description and retains cancel countdown");
                        Check(PanoramaCache.ImageShaAt(cache,Tree,"retry-fixture",1900)==PanoramaCache.Hash(old),"Old panorama stays saved throughout refused attempts");clock+=100;
                    }
                    yield return Until(()=>!gen.Busy&&gen.WaitingCount==0&&(gen.CompletedCount==1||gen.FailedCount==1),"Policy retry chain completes");
                    Check(helper.Posts==(exhaust?4:2)&&helper.PostIds.Distinct().Count()==helper.Posts,"Policy chain has one initial attempt and a bounded number of fresh identities");
                    Check(helper.Requests.Select(j=>j.policyRetryLevel).SequenceEqual(Enumerable.Range(0,exhaust?4:2)),"Helper receives progressive simplification levels");
                    Check(PanoramaCache.ImageShaAt(cache,Tree,"retry-fixture",1900)==PanoramaCache.Hash(exhaust?old:png),"Success replaces old image; exhausted refusals preserve it");Dispose();
                }
                using(var helper=new SceneQueueChecks.FakeHelper(png){TransientFailures=4,ReadyDelayMilliseconds=0})
                {
                    var gen=Create(Path.Combine(root,"four-retries"),helper);yield return Until(()=>gen.Ready,"Concurrent retry fixture ready");gen.Enqueue(Tree,Person(),false,Enumerable.Range(1900,4).Select(y=>(y,"York")));
                    yield return Until(()=>gen.WaitingCount==4&&!gen.Busy&&gen.Waiting.All(p=>p.retryCount==1),"Four failed attempts wait outside active slots");clock+=100;
                    yield return Until(()=>gen.CompletedCount==4&&!gen.Busy&&gen.WaitingCount==0,"Four retries complete");Check(helper.Posts==8&&helper.MaximumActive<=4,"Retries obey global four-slot cap");Dispose();
                }
                foreach(bool download in new[]{false,true})
                using(var helper=new SceneQueueChecks.FakeHelper(png){AmbiguousOnce=!download,ImageFailureOnce=download,ReadyDelayMilliseconds=0})
                {
                    var gen=Create(Path.Combine(root,download?"download":"uncertain"),helper);yield return Until(()=>gen.Ready,"Recovery fixture ready");gen.Generate(Tree,Person(),false,1900,"York");
                    yield return Until(()=>!gen.Busy&&!gen.Running&&gen.Active!=null,"Interrupted outcome retains submitted checkpoint");string id=gen.Active.job.requestId;
                    Check(gen.Active.retryCount==0&&gen.WaitingCount==0&&helper.Posts==1,"Interrupted requests do not schedule new generation");gen.ResumeQueue();
                    yield return Until(()=>gen.CompletedCount==1&&!gen.Busy,"Recovery checks/downloads original request");Check(helper.Posts==1&&helper.PostIds.Single()==id,"No duplicate image generation on recovery");Dispose();
                }
                using(var helper=new SceneQueueChecks.FakeHelper(png){TransientFailures=1,ReadyDelayMilliseconds=0})
                {
                    var gen=Create(Path.Combine(root,"clear"),helper);yield return Until(()=>gen.Ready,"Clear fixture ready");gen.Generate(Tree,Person(),false,1900,"York");yield return Until(()=>gen.WaitingCount==1&&!gen.Busy&&gen.Waiting[0].retryCount==1,"Retry waiting before clear");
                    gen.ClearWaiting();clock+=100;yield return null;yield return null;Check(gen.WaitingCount==0&&helper.Posts==1,"Clear waiting removes delayed retries");Dispose();
                }
                foreach(string mode in new[]{"arrived-before","arrived-commit","regenerate"})
                using(var helper=new SceneQueueChecks.FakeHelper(png){TransientFailures=1,ReadyDelayMilliseconds=0})
                {
                    string path=Path.Combine(root,mode),cache=Path.Combine(path,"Panoramas");if(mode=="regenerate")Save(cache,old);
                    var gen=Create(path,helper);yield return Until(()=>gen.Ready,"Cache fixture ready");gen.Generate(Tree,Person(),false,1900,"York");yield return Until(()=>gen.WaitingCount==1&&!gen.Busy&&gen.Waiting[0].retryCount==1,"Cache fixture retry queued");
                    if(mode=="arrived-before")Save(cache,old);if(mode=="arrived-commit")helper.BeforeImage=job=>Save(cache,old);
                    clock+=100;yield return Until(()=>!gen.Busy&&gen.WaitingCount==0&&(gen.CompletedCount==1||gen.SkippedCount==1),"Cache fixture finishes");
                    Check(PanoramaCache.ImageShaAt(cache,Tree,"retry-fixture",1900)==PanoramaCache.Hash(mode=="regenerate"?png:old),"New successful images preserved; explicit regeneration may replace its original");
                    Check(helper.Posts==(mode=="arrived-before"?1:2),"Preflight avoids redundant generation");Dispose();
                }
                using(var helper=new SceneQueueChecks.FakeHelper(png){FailYear=1900,ReadyDelayMilliseconds=0})
                {
                    var gen=Create(Path.Combine(root,"permanent"),helper);yield return Until(()=>gen.Ready,"Permanent fixture ready");gen.Generate(Tree,Person(),false,1900,"York");yield return Until(()=>gen.FailedCount==1&&!gen.Busy,"Permanent failure finishes");Check(helper.Posts==1&&gen.WaitingCount==0,"Permanent failures do not retry");Dispose();
                }
                File.WriteAllText(Report,n+" retry integration checks passed: manual/batch/automatic paths, backoff, pause/restart, visible waiting state, budget, cache preflight/commit protection, explicit regeneration and permanent failures. No Meta calls.");
            }
            finally{Dispose();}
        }
    }
}
