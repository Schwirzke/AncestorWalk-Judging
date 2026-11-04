using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace AncestorWalk.Editor
{
    public static class SceneReferenceChecks
    {
        const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        const string Tree="aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const string PersonId="reference-fixture";
        const string Report="Artifacts/scene-reference-checks.txt";
        static Person Person()=>new Person{Id=PersonId,BirthDate="1800",BirthPlace="York",DeathDate="2000",DeathPlace="Paris"};
        static byte[] Image(Color color)
        {var t=new Texture2D(256,128);t.SetPixels(Enumerable.Repeat(color,256*128).ToArray());t.Apply();var bytes=t.EncodeToPNG();UnityEngine.Object.DestroyImmediate(t);return bytes;}
        static void Save(string cache,int year,byte[] png,string place="York",string person=PersonId,string tree=Tree,SceneReference reference=null)
            =>PanoramaCache.SaveDecodedAt(cache,tree,person,year,png,new SceneJob{sha256=PanoramaCache.Hash(png),place=place,prompt="fixture",model="fixture",createdUtc="2000-01-01T00:00:00Z",horizonLift=7,reference=reference,generationMode=reference==null?"generate":"edit"},256,128);
        public static string Data()
        {
            int n=0;void Check(bool ok,string why){if(!ok)throw new Exception(why);n++;}
            string cache=Path.GetFullPath("Library/SceneReferenceChecks/"+Guid.NewGuid().ToString("N"));var png=Image(Color.blue);
            Save(cache,1900,png);Save(cache,1920,png);Save(cache,1910,png,"Paris");Save(cache,1911,png,person:"other");Save(cache,1911,png,tree:new string('b',64));
            SceneReference Pick(int year,int[] years=null)=>SceneReference.NearestAt(cache,Tree,PersonId,year,"York",years);
            Check(Pick(1910).year==1900,"Nearest tie favors earlier year; other places/people/trees excluded");
            Check(Pick(1919).year==1920&&Pick(1920).year==1920,"Nearest and exact-year regeneration reference");
            Check(Pick(1919,new[]{1900}).year==1900&&Pick(1919,Array.Empty<int>())==null,"Residence compatibility constrains source years");
            Save(cache,1918,png,"");Check(Pick(1918).year==1920,"Legacy blank location is not guessed without gallery validation");
            Check(Pick(1918,new[]{1918}).year==1918,"Gallery-approved legacy birthplace reference accepted");
            long reads=PanoramaCache.ImageReadCount,validations=PanoramaCache.ImageValidationCount;for(int i=0;i<10;i++)Pick(1919);
            Check(reads==PanoramaCache.ImageReadCount&&validations==PanoramaCache.ImageValidationCount,"Warm selection does not read/hash PNGs again");
            var reference=Pick(1920);File.WriteAllBytes(reference.ImagePath(cache,Tree,PersonId),new byte[]{1,2,3});
            Check(Pick(1919).year==1900,"Corrupt cached reference skipped");
            var job=new SceneJob{requestId=new string('c',32),treeId=Tree,personKey=PanoramaCache.PersonKey(PersonId),year=1930,date="1930",place="York",reference=Pick(1900),referenceResolved=true};
            var pending=new PendingScene{treeId=Tree,personId=PersonId,phase=2,job=job};var retry=SceneRetryPolicy.Next(pending,new SceneJob{status="failed",retryable=true},1000);
            Check(retry.job.referenceResolved&&SceneReference.Same(retry.job.reference,job.reference)&&!ReferenceEquals(retry.job.reference,job.reference),"Confirmed-failure retry freezes an independent copy of the anchor");
            var restored=JsonUtility.FromJson<PendingScene>(JsonUtility.ToJson(retry));SceneQueueState.Validate(restored);
            Check(SceneReference.Same(restored.job.reference,job.reference),"Checkpoint preserves source identity");
            var empty=new PendingScene{treeId=Tree,personId=PersonId,phase=1,job=new SceneJob{requestId=new string('e',32),treeId=Tree,personKey=PanoramaCache.PersonKey(PersonId),year=1900,date="1900",place="York"}};
            empty=JsonUtility.FromJson<PendingScene>(JsonUtility.ToJson(empty));SceneQueueState.Validate(empty);Check(empty.job.reference==null,"Old/null references survive Unity inline serialization and checkpoint validation");
            Save(cache,1930,png,reference:job.reference);var record=PanoramaCache.YearRecordsAt(cache,Tree,PersonId).Single(r=>r.year==1930);
            Check(record.generationMode=="edit"&&SceneReference.Same(record.reference,job.reference),"Cache stores edit provenance");record.reference.year=1;
            Check(PanoramaCache.YearRecordsAt(cache,Tree,PersonId).Single(r=>r.year==1930).reference.year==1900,"Cached provenance copies cannot mutate the index");
            bool rejected=false;try{SceneReference.Validate(new SceneReference{year=1900,sha256=new string('d',64),place="Paris"},"York");}catch(InvalidDataException){rejected=true;}Check(rejected,"Cross-location anchor rejected");
            return n+" scene reference data checks passed.";
        }
        public static string Play()
        {
            if(!Application.isPlaying)throw new Exception("Play mode required");Directory.CreateDirectory("Artifacts");File.WriteAllText(Report,"RUNNING");
            UnityEngine.Object.FindAnyObjectByType<AncestorGallery>().StartCoroutine(Runner(Checks(),Report));return "Running isolated image reference checks; no Meta calls.";
        }
        static IEnumerator Runner(IEnumerator first,string report)
        {
            var stack=new Stack<IEnumerator>();stack.Push(first);
            while(stack.Count>0)
            {
                var current=stack.Peek();bool more=false;Exception failure=null;try{more=current.MoveNext();}catch(Exception e){failure=e;}
                if(failure!=null){while(stack.Count>0)(stack.Pop() as IDisposable)?.Dispose();File.WriteAllText(report,"FAILED: "+failure);Debug.LogException(failure);yield break;}
                if(!more){(stack.Pop() as IDisposable)?.Dispose();continue;}if(current.Current is IEnumerator nested){stack.Push(nested);continue;}yield return current.Current;
            }
        }
        static IEnumerator Checks()
        {
            int n=0;void Check(bool ok,string why){if(!ok)throw new Exception(why);n++;}
            IEnumerator Until(Func<bool> done,string why){float end=Time.realtimeSinceStartup+25;while(!done()&&Time.realtimeSinceStartup<end)yield return null;Check(done(),why);}
            bool guard=SessionState.GetBool("AncestorWalk.EndpointChecks",false);SessionState.SetBool("AncestorWalk.EndpointChecks",true);
            string root=Path.GetFullPath("Library/SceneReferenceChecks/"+Guid.NewGuid().ToString("N"));var png=Image(Color.green);var original=Image(Color.blue);GameObject go=null;
            SceneGeneration Create(string path,SceneQueueChecks.FakeHelper helper)
            {go=new GameObject("Isolated image edit checks");go.SetActive(false);var g=go.AddComponent<SceneGeneration>();typeof(SceneGeneration).GetField("TestStorage",Hidden).SetValue(g,path);typeof(SceneGeneration).GetField("TestConnection",Hidden).SetValue(g,new SceneServiceConnection{url=helper.Url,token=new string('a',64)});go.SetActive(true);return g;}
            void Dispose(){if(go!=null)UnityEngine.Object.DestroyImmediate(go);go=null;}
            try
            {
                using(var helper=new SceneQueueChecks.FakeHelper(png){Hold=true,ReadyDelayMilliseconds=0})
                {
                    string path=Path.Combine(root,"concurrent"),cache=Path.Combine(path,"Panoramas");Save(cache,1900,original);
                    var before=Directory.GetFiles(cache,"*",SearchOption.AllDirectories).ToDictionary(p=>p,p=>PanoramaCache.Hash(File.ReadAllBytes(p)));
                    var gen=Create(path,helper);yield return Until(()=>gen.Ready,"Queue ready");gen.Enqueue(Tree,Person(),true,new[]{(1910,"York"),(1915,"York"),(1920,"York"),(1925,"York")});
                    yield return Until(()=>helper.Posts==4,"Four edits submitted");Check(helper.ReferenceUploads==1&&helper.Requests.All(j=>j.reference?.year==1900&&j.referenceResolved),"Four jobs share one upload and immutable reference");
                    helper.Hold=false;yield return Until(()=>gen.CompletedCount==4&&!gen.Busy,"Four edit results saved");
                    Check(before.All(p=>PanoramaCache.Hash(File.ReadAllBytes(p.Key))==p.Value),"Source panorama and manifest unchanged");
                    Check(PanoramaCache.YearRecordsAt(cache,Tree,PersonId).Where(r=>r.year!=1900).All(r=>r.reference.year==1900&&r.generationMode=="edit"&&r.horizonLift==7),"All outputs retain reference provenance and horizon");
                    gen.Generate(Tree,Person(),true,1922,"York");yield return Until(()=>gen.CompletedCount==5&&!gen.Busy,"Later year saved");Check(helper.Requests.Last().reference.year==1920&&helper.ReferenceUploads==2,"Next request selects nearest new cached year");
                    gen.Generate(Tree,Person(),true,1950,"Paris");yield return Until(()=>gen.CompletedCount==6&&!gen.Busy,"Different location generated");Check(helper.Requests.Last().reference==null&&helper.Requests.Last().generationMode=="generate","No compatible reference preserves text generation");Dispose();
                }
                using(var helper=new SceneQueueChecks.FakeHelper(png){AmbiguousOnce=true,ReadyDelayMilliseconds=0})
                {
                    string path=Path.Combine(root,"recovery"),cache=Path.Combine(path,"Panoramas");Save(cache,1900,original);var gen=Create(path,helper);yield return Until(()=>gen.Ready,"Recovery queue ready");gen.Generate(Tree,Person(),true,1910,"York");
                    yield return Until(()=>!gen.Busy&&!gen.Running&&gen.Active!=null,"Uncertain response retained");string sha=gen.Active.job.reference.sha256;Save(cache,1900,png);Dispose();
                    gen=Create(path,helper);yield return Until(()=>gen.Ready,"Reference checkpoint restored");gen.Resume(Tree);gen.ResumeQueue();yield return Until(()=>gen.CompletedCount==1&&!gen.Busy,"Original edit recovered");
                    Check(helper.Posts==1&&helper.Requests.Single().reference.sha256==sha,"Restart polls original ID with frozen source despite source regeneration");Dispose();
                }
                using(var helper=new SceneQueueChecks.FakeHelper(png){ReferenceDelayMilliseconds=700,ReadyDelayMilliseconds=0})
                {
                    string path=Path.Combine(root,"cancel-upload"),cache=Path.Combine(path,"Panoramas");Save(cache,1900,original);var gen=Create(path,helper);yield return Until(()=>gen.Ready,"Cancellation queue ready");gen.Generate(Tree,Person(),true,1910,"York");
                    yield return Until(()=>helper.ReferenceUploads==1&&helper.Posts==0,"Reference uploading before model submission");var cancel=gen.CancelTreeJobs(Tree);yield return Until(()=>cancel.IsCompleted,"Upload cancellation checkpoint saved");Check(!cancel.IsFaulted&&!gen.Busy&&helper.Posts==0,"Cancellation during upload never submits image job");
                    helper.ReferenceDelayMilliseconds=0;gen.Generate(Tree,Person(),true,1910,"York");yield return Until(()=>gen.CompletedCount==1&&!gen.Busy,"Explicit generation after cancelled upload works");Check(helper.Posts==1,"Cancelled upload gate released");Dispose();
                }
                File.WriteAllText(Report,n+" reference integration checks passed: nearest source, coalesced upload, four concurrent edits, provenance, original preservation, cross-location fallback, immutable recovery and cancellation. No Meta calls.");
            }
            finally{Dispose();SessionState.SetBool("AncestorWalk.EndpointChecks",guard);}
        }
        public static string Live(string run="")
        {
            if(!Application.isPlaying)throw new Exception("Play mode required");
            if(run!=""&&run!="geometry")throw new Exception("Unknown live check");string report="Artifacts/scene-reference-live"+(run==""?"":"-"+run)+".txt";Directory.CreateDirectory("Artifacts");
            if(File.Exists(report))throw new Exception("Live report already exists. Inspect it before explicitly starting another paid image request.");
            File.WriteAllText(report,"STARTING");UnityEngine.Object.FindAnyObjectByType<AncestorGallery>().StartCoroutine(Runner(LiveCheck(report,run),report));return "Running one isolated Muse edit: fictional York 1870 → 1875.";
        }
        static IEnumerator LiveCheck(string report,string run)
        {
            string path=Path.GetFullPath("Library/SceneReferenceLive/"+Guid.NewGuid().ToString("N")),cache=Path.Combine(path,"Panoramas");
            string tree=PanoramaCache.Hash(System.Text.Encoding.UTF8.GetBytes(path));var png=File.ReadAllBytes("Assets/AncestorWalk/Resources/ClaraYork1870Panorama.png");string sha=PanoramaCache.Hash(png);
            int Width(int offset)=>(png[offset]<<24)|(png[offset+1]<<16)|(png[offset+2]<<8)|png[offset+3];
            PanoramaCache.SaveDecodedAt(cache,tree,PersonId,1870,png,new SceneJob{sha256=sha,place="York, England",model="bundled-fictional-reference",prompt="Fictional panorama test",createdUtc="2026-10-26T00:00:00Z",horizonLift=12},Width(16),Width(20));
            var go=new GameObject("Isolated live Muse edit");go.SetActive(false);var gen=go.AddComponent<SceneGeneration>();
            typeof(SceneGeneration).GetField("TestStorage",Hidden).SetValue(gen,path);typeof(SceneGeneration).GetField("TestConnection",Hidden).SetValue(gen,SceneGeneration.ReadConnection());
            // A failed live test is reported once, without automatic paid retries.
            typeof(SceneGeneration).GetField("TestClock",Hidden).SetValue(gen,(Func<double>)(()=>0));go.SetActive(true);
            while(!gen.Ready)yield return null;
            var person=new Person{Id=PersonId,BirthDate="1860",DeathDate="1900",BirthPlace="York, England",DeathPlace="York, England"};gen.Generate(tree,person,true,1875,"York, England");
            double start=Time.realtimeSinceStartupAsDouble;string previous="";
            yield return null;yield return null;
            while(Time.realtimeSinceStartupAsDouble-start<600)
            {
                string state=string.Join("; ",gen.ActiveJobs.Select(p=>p.job.requestId+" "+gen.Message(PersonId)));
                if(state!=previous){File.WriteAllText(report,"RUNNING\nStorage: "+path+"\n"+state);previous=state;}
                if(!gen.Busy&&(gen.CompletedCount>0||gen.FailedCount>0||gen.WaitingCount>0||!gen.Running))break;
                yield return null;
            }
            if(gen.CompletedCount==1)
            {
                var record=PanoramaCache.YearRecordsAt(cache,tree,PersonId).Single(r=>r.year==1875);
                if(record.generationMode!="edit"||record.reference?.sha256!=sha||record.reference.year!=1870||PanoramaCache.ImageShaAt(cache,tree,PersonId,1870)!=sha)throw new Exception("Live edit provenance or source protection failed");
                string output=Path.GetFullPath("Artifacts/SceneReferenceLive"+(run==""?"":"/"+run));Directory.CreateDirectory(output);File.WriteAllBytes(Path.Combine(output,"source-1870.png"),png);
                File.Copy(Path.Combine(cache,"people",tree,PanoramaCache.PersonKey(PersonId),"years","1875",record.file),Path.Combine(output,"edit-1875.png"));
                File.WriteAllText(report,"PASS: live Muse Image edit 1870 → 1875, same fictional York location.\nSeconds: "+(Time.realtimeSinceStartupAsDouble-start).ToString("F1")+"\nMode: "+record.generationMode+"\nSource preserved: "+sha+"\nResult: "+record.sha256+"\nDimensions: "+record.width+"×"+record.height+"\nHorizon preserved: "+record.horizonLift+"\nStorage: "+path+"\nOutput: "+output);UnityEngine.Object.Destroy(go);
            }
            else File.WriteAllText(report,"NOT COMPLETE; no new image request will be started automatically by this check.\nStorage: "+path+"\n"+gen.Message(PersonId)+"\n"+gen.QueueMessage+"\n"+string.Join("; ",gen.ActiveJobs.Select(p=>p.job.requestId)));
        }
    }
}
