using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace AncestorWalk.Editor
{
    public static class DirectMetaChecks
    {
        public static string RunLive()
        {
            MetaApi.RequireKey();var g=UnityEngine.Object.FindAnyObjectByType<AncestorGallery>();string report="Artifacts/DirectMetaLive/status.txt";File.WriteAllText(report,"RUNNING: direct York image edit and voice guide.");
            g.StartCoroutine(MetaApi.Guard(Live(report),()=>File.WriteAllText(report,"FAILED: direct live check interrupted. No automatic replay.")));return "Direct Meta live test started with fictional York context.";
        }
        static IEnumerator Live(string report)
        {
            string root=Path.GetFullPath("Artifacts/DirectMetaLive/Storage"),panoramas=Path.Combine(root,"Panoramas"),tree=new string('d',64),person="fictional-york-fixture";var original=File.ReadAllBytes("Assets/AncestorWalk/Resources/ClaraYork1870Panorama.png");PhotoHeader.Read(original,out int w,out int h,out _);
            var source=Job(tree,person,1870);source.model=MetaApi.Image;source.sha256=PanoramaCache.Hash(original);source.createdUtc=DateTime.UtcNow.ToString("o");source.prompt="Bundled fictional York reference for direct API verification.";
            var save=System.Threading.Tasks.Task.Run(()=>PanoramaCache.SaveDecodedAt(panoramas,tree,person,1870,original,source,w,h));while(!save.IsCompleted)yield return null;if(save.IsFaulted)throw save.Exception;
            var job=Job(tree,person,1875);job.reference=new SceneReference{sha256=source.sha256,place="York, England",year=1870,horizonLift=12};var image=new DirectSceneResult();
            yield return DirectMetaScene.GenerateFor(person,root,panoramas,job,image,label=>File.WriteAllText(report,"RUNNING: "+label),null);
            if(image.Png==null){File.WriteAllText(report,"FAILED: "+image.Job?.error);yield break;}
            File.WriteAllBytes("Artifacts/DirectMetaLive/york-1875.png",image.Png);File.WriteAllText(report,"RUNNING: direct image edit passed; testing voice guide.");
            var go=new GameObject("Live direct fictional guide");var guide=go.AddComponent<HistoricalGuide>();
            try
            {
                var question=new GuideQuestion{requestId=Guid.NewGuid().ToString("N"),treeId=tree,personKey=PanoramaCache.PersonKey(person),name="Fictional visitor",place="York, England",year=1875,synthetic=true,audio=Convert.ToBase64String(File.ReadAllBytes("Artifacts/DirectMetaLive/question.wav"))};
                yield return (IEnumerator)typeof(HistoricalGuide).GetMethod("AskDirect",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(guide,new object[]{question,0});
                if(guide.Answer==null){File.WriteAllText(report,"FAILED GUIDE: "+guide.Status+"; image edit passed.");yield break;}
                File.WriteAllText("Artifacts/DirectMetaLive/guide.json",JsonUtility.ToJson(guide.Answer,true));
                File.WriteAllText(report,"PASSED: direct HTTPS researched York 1875 image edit ("+image.Width+"×"+image.Height+", "+image.Job.elapsedSeconds.ToString("F2")+"s), multipart voice transcription and sourced guide ("+guide.Answer.sources.Length+" sources). Fictional context only; no helper HTTP requests or family/cache replacements.");
            }
            finally{UnityEngine.Object.Destroy(go);}
        }
        const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        const string Report="Artifacts/direct-meta-checks.txt";
        static int checks;static string last;
        static void Check(bool value,string why){last=why;if(!value)throw new Exception(why);checks++;}
        public static string Run()
        {
            var g=UnityEngine.Object.FindAnyObjectByType<AncestorGallery>();if(!Application.isPlaying||g==null||g.Generation.Busy)throw new Exception("Idle Play mode required.");
            File.WriteAllText(Report,"RUNNING");g.StartCoroutine(Runner(Checks(g)));return "Direct API fixture checks running; no external calls or family writes.";
        }
        public static string RunPolicyRetries()
        {
            var g=UnityEngine.Object.FindAnyObjectByType<AncestorGallery>();if(!Application.isPlaying||g==null||g.Generation.Busy)throw new Exception("Idle Play mode required.");
            File.WriteAllText(Report,"RUNNING");g.StartCoroutine(Runner(PolicyRetries()));return "Direct prompt-retry fixtures running; no Meta calls.";
        }
        static IEnumerator PolicyRetries()
        {
            checks=0;last="prompt retries";string root=Path.GetFullPath("Artifacts/DirectPolicyChecks/"+Guid.NewGuid().ToString("N")),tree=new string('a',64),person="policy-fixture";
            string oldKey=(string)typeof(MetaApi).GetField("sessionKey",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);var oldReply=MetaApi.TestReply;
            byte[] original=File.ReadAllBytes("Assets/AncestorWalk/Resources/ClaraYork1870Panorama.png");PhotoHeader.Read(original,out int width,out int height,out _);
            int images=0,research=0;var lengths=new List<int>();
            var pending=new PendingScene{treeId=tree,personId=person,phase=2,job=Job(tree,person,1900)};pending.job.details="OPTIONAL_PRIVATE_DIRECTIONS";
            try
            {
                MetaApi.UseKey("fixture-valid-key");
                var baseline=Job(tree,person,1900);baseline.sha256=PanoramaCache.Hash(original);baseline.model="fixture";
                PanoramaCache.SaveDecodedAt(Path.Combine(root,"Panoramas"),tree,person,1900,original,baseline,width,height);
                MetaApi.TestReply=(path,body)=>
                {
                    var value=JObject.Parse(Encoding.UTF8.GetString(body));
                    if(path=="responses")
                    {
                        research++;Check(!value["input"].ToString().Contains("OPTIONAL_PRIVATE_DIRECTIONS"),"Retry research never receives saved subject directions");
                        return new MetaReply{Http=200,Json=Research(string.Join(" ",Enumerable.Range(0,research==1?200:research==2?100:50).Select(i=>"ordinary"+i)))};
                    }
                    Check(path=="images/generations","Direct attempts retain generation endpoint");string prompt=(string)value["prompt"];lengths.Add(prompt.Length);images++;
                    if(images>1)Check(!prompt.Contains("OPTIONAL_PRIVATE_DIRECTIONS")&&prompt.Contains("York, England in 1900"),"Direct retries omit optional text and preserve place/year");
                    return images<4?new MetaReply{Http=400,Error="Meta HTTP 400. code: content_policy_violation. param: prompt."}:new MetaReply{Http=200,Json=new JObject{["data"]=new JArray(new JObject{["b64_json"]=Convert.ToBase64String(original)})}};
                };
                for(int attempt=0;attempt<4;attempt++)
                {
                    var result=new DirectSceneResult();yield return DirectMetaScene.GenerateFor(person,root,Path.Combine(root,"Panoramas"),pending.job,result,_=>{},null);
                    Check(PanoramaCache.ImageShaAt(Path.Combine(root,"Panoramas"),tree,person,1900)==PanoramaCache.Hash(original),"Transport never removes existing saved image");
                    if(attempt<3)
                    {Check(ScenePromptRetry.IsPromptRefusal(result.Job),"Direct refusal is explicitly classified");pending=SceneRetryPolicy.Next(pending,result.Job,1000);Check(pending!=null&&pending.job.policyRetryLevel==attempt+1,"Direct refusal advances bounded queue retry");}
                    else
                    {
                        Check(result.Png!=null&&result.Job.policyRetryLevel==3,"Minimal direct attempt succeeds");
                        var saved=PanoramaCache.SaveDecodedAt(Path.Combine(root,"Panoramas"),tree,person,1900,result.Png,result.Job,result.Width,result.Height);
                        Check(saved.policyRetryLevel==3&&saved.details=="OPTIONAL_PRIVATE_DIRECTIONS"&&!saved.prompt.Contains(saved.details),"Provenance records actual simplification while retaining editable original details");
                    }
                }
                Check(images==4&&research==3,"Exactly four image attempts; final minimal prompt skips fresh research");
                Check(lengths.Zip(lengths.Skip(1),(a,b)=>a>b).All(x=>x),"Actual direct image payloads get progressively shorter");
                File.WriteAllText(Report,checks+" direct prompt-retry checks passed: progressive payloads, sourced rewrites, minimal fallback, exact target, cache preservation and provenance. No Meta calls.");
            }
            finally{MetaApi.TestReply=oldReply;typeof(MetaApi).GetField("sessionKey",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,oldKey);if(Directory.Exists(root))Directory.Delete(root,true);}
        }
        static IEnumerator Runner(IEnumerator routine)
        {
            var stack=new Stack<IEnumerator>();stack.Push(routine);
            while(stack.Count>0)
            {
                var top=stack.Peek();bool more=false;Exception error=null;try{more=top.MoveNext();}catch(Exception e){error=e;}
                if(error!=null){while(stack.Count>0)(stack.Pop() as IDisposable)?.Dispose();File.WriteAllText(Report,"FAILED after "+checks+" checks ("+last+"): "+error);Debug.LogException(error);yield break;}
                if(!more){(stack.Pop() as IDisposable)?.Dispose();continue;}if(top.Current is IEnumerator nested)stack.Push(nested);else yield return top.Current;
            }
        }
        static JObject Research(string text="A dense urban square in York.")=>new JObject{["status"]="completed",["output"]=new JArray(new JObject{["type"]="web_search_call",["status"]="completed",["results"]=new JArray(new JObject{["url"]="https://example.org/history",["title"]="Archive fixture"})},new JObject{["type"]="message",["role"]="assistant",["content"]=new JArray(new JObject{["type"]="output_text",["text"]=text,["annotations"]=new JArray(new JObject{["type"]="url_citation",["url"]="https://example.org/history",["title"]="Archive fixture",["start_index"]=0,["end_index"]=text.Length})})})};
        static SceneJob Job(string tree,string person,int year)=>new SceneJob{transport=DirectMetaScene.Transport,requestId=Guid.NewGuid().ToString("N"),treeId=tree,personKey=PanoramaCache.PersonKey(person),place="York, England",date=year.ToString(),year=year,referenceResolved=true};
        static SceneGeneration Queue(string root)
        {
            var go=new GameObject("Isolated direct API queue");go.SetActive(false);var gen=go.AddComponent<SceneGeneration>();typeof(SceneGeneration).GetField("TestStorage",Hidden).SetValue(gen,root);go.SetActive(true);return gen;
        }
        static IEnumerator Wait(Func<bool> ready,float seconds=12)
        {float end=Time.realtimeSinceStartup+seconds;while(!ready()&&Time.realtimeSinceStartup<end)yield return null;Check(ready(),"Async operation finishes within deadline");}
        static IEnumerator Checks(AncestorGallery g)
        {
            checks=0;last="start";bool guard=SessionState.GetBool("AncestorWalk.EndpointChecks",false);SessionState.SetBool("AncestorWalk.EndpointChecks",true);
            string oldKey=(string)typeof(MetaApi).GetField("sessionKey",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
            var oldReply=MetaApi.TestReply;var objects=new List<GameObject>();string root=Path.GetFullPath("Artifacts/DirectMetaChecks/"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            string connection=Path.Combine(PanoramaCache.StorageRoot,"scene-service.json");var stamp=File.Exists(connection)?File.GetLastWriteTimeUtc(connection):default;
            byte[] png=null;int images=0,edits=0,research=0,speech=0,sam=0,models=0;bool rateOnce=false,uncertain=false,authFail=false;JObject lastEdit=null;
            var masks=JArray.Parse(SamMaskFixtures.Json);
            try
            {
                g.AISetup.Close();Check(AISetupChecks.Run().Contains("passed"),"Direct setup UI checks pass");MetaApi.UseKey("fixture-direct-session-key");
                foreach(JObject row in masks)
                {
                    int w=(int)row["width"],h=(int)row["height"];var alpha=SamMask.Decode((string)row["payload"],w,h);string expected=(string)row["alpha"];
                    Check(BitConverter.ToString(alpha).Replace("-","").ToLowerInvariant()==expected,"SAM codec matches official synthetic fixture");
                }
                foreach(string payload in new[]{"!", "!invalid", "~invalid", (string)masks[2]["payload"]+"!"})
                {bool failed=false;try{SamMask.Decode(payload,3,4);}catch{failed=true;}Check(failed,"Invalid SAM payload rejected");}
                Check(MetaApi.SourcedText(Research(),false).Sources.Length==1,"Sourced research parsed");
                bool rejected=false;try{MetaApi.SourcedText(new JObject{["status"]="completed",["output"]=new JArray()},true);}catch{rejected=true;}Check(rejected,"Unsourced research rejected");
                var texture=new Texture2D(256,128,TextureFormat.RGBA32,false);var pixels=Enumerable.Repeat(new Color32(30,70,100,255),256*128).ToArray();texture.SetPixels32(pixels);texture.Apply();png=texture.EncodeToPNG();UnityEngine.Object.Destroy(texture);
                MetaApi.ValidatePngComplete(png);Check(true,"Complete PNG accepted");
                var corrupt=(byte[])png.Clone();corrupt[30]^=1;bool badPng=false;try{MetaApi.ValidatePngComplete(corrupt);}catch{badPng=true;}Check(badPng,"PNG CRC failure rejected");
                badPng=false;try{MetaApi.ValidatePngComplete(png.Take(png.Length-5).ToArray());}catch{badPng=true;}Check(badPng,"Truncated PNG rejected");
                MetaApi.TestReply=(path,body)=>
                {
                    JObject request=body==null?null:path=="asr/transcribe"?null:JObject.Parse(Encoding.UTF8.GetString(body));
                    if(path=="models"){models++;return authFail?new MetaReply{Http=401,Error=MetaApi.Error(401)}:new MetaReply{Http=200,Json=new JObject{["data"]=new JArray(new JObject{["id"]="muse-image-1.0"},new JObject{["id"]="muse-spark-1.3"})}};}
                    if(path=="asr/transcribe"){speech++;Check(Encoding.UTF8.GetString(body,0,Math.Min(500,body.Length)).Contains("PUSH_TO_TALK"),"Voice multipart schema");return new MetaReply{Http=200,Json=new JObject{["transcript"]="York, England"}};}
                    if(path=="responses"&&(string)request["model"]=="sam-3.1")
                    {
                        sam++;Check((string)request["metadata"]["mask_encoding"]=="one_bit"&&!(bool)request["stream"],"SAM uses bounded nonstreaming binary masks");
                        string payload=(string)masks.Last["payload"],text="<0f>0<|box;x1=0;y1=0;x2=31;y2=23;w=32;h=24|><|mask;x=0;y=0;data=24,32,"+payload+"|>";
                        return new MetaReply{Http=200,Json=new JObject{["status"]="completed",["output"]=new JArray(new JObject{["type"]="message",["role"]="assistant",["content"]=new JArray(new JObject{["type"]="output_text",["text"]=text})})}};
                    }
                    if(path=="responses")
                    {research++;if(JObject.Parse(request["input"].ToString())["question"]==null)Check(request["instructions"].ToString().Contains("appearance of a place AS OF the requested year")&&request["instructions"].ToString().Contains("not just the page publication date"),"Direct scene research grounds appearance in target year");Check(!(bool)request["store"]&&request["tools"][0]["type"].ToString()=="web_search","Research requests are sourced and unstored");return new MetaReply{Http=200,Json=Research()};}
                    if(path=="images/generations"||path=="images/edits")
                    {
                        Check(request["prompt"].ToString().Contains("AS OF the target year, not its founding era")&&!request["prompt"].ToString().Contains("selected historical year"),"Generate and edit share date-appropriate appearance policy");
                        images++;Check((int)request["n"]==1&&(string)request["output_format"]=="png"&&(string)request["response_format"]=="b64_json","Image schema preserved");
                        if(path=="images/edits"){edits++;lastEdit=request;}
                        if(rateOnce){rateOnce=false;return new MetaReply{Http=429,Error=MetaApi.Error(429),RetryAfter=2};}
                        if(uncertain){uncertain=false;return new MetaReply{Http=0,Error=MetaApi.Error(0)};}
                        return new MetaReply{Http=200,Json=new JObject{["data"]=new JArray(new JObject{["b64_json"]=Convert.ToBase64String(png)})}};
                    }
                    throw new Exception("Unexpected endpoint");
                };
                g.AISetup.Open();typeof(AISetupPanel).GetField("key",Hidden).SetValue(g.AISetup,"fixture-valid-new-key");g.AISetup.BeginTest(true);yield return Wait(()=>!g.AISetup.Busy);
                Check(models==1&&MetaApi.Configured&&!g.AISetup.HasKeyInput&&g.AISetup.Status.Contains("Meta connected"),"Panel directly validates and activates session key");
                authFail=true;typeof(AISetupPanel).GetField("key",Hidden).SetValue(g.AISetup,"fixture-rejected-key");g.AISetup.BeginTest(true);yield return Wait(()=>!g.AISetup.Busy);
                Check((string)typeof(MetaApi).GetField("sessionKey",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null)=="fixture-valid-new-key","Rejected key preserves active key");authFail=false;g.AISetup.Close();
                var person=new Person{Id="fixture-person",Name="Fictional fixture",BirthDate="1870",DeathDate="1900",BirthPlace="York, England",DeathPlace="York, England"};string tree=new string('a',64);
                string oldBriefKey=PanoramaCache.Hash(Encoding.UTF8.GetBytes("1\nYORK, ENGLAND\n1870"));
                string legacyBriefPath=Path.Combine(root,"SettingBriefs",oldBriefKey+".json");Directory.CreateDirectory(Path.GetDirectoryName(legacyBriefPath));
                var legacyBrief=new SceneSettingBrief{version=1,place="York, England",date="1870",model=MetaApi.Spark,text="Undated old rustic fixture.",createdUtc=DateTime.UtcNow.ToString("o"),sources=new[]{new GuideSource{url="https://example.org/history",title="Archive fixture"}}};
                string legacyJson=JsonUtility.ToJson(legacyBrief);File.WriteAllText(legacyBriefPath,legacyJson);
                Check(DirectMetaScene.BriefCacheKey("York, England","1870")!=oldBriefKey,"Research policy bypasses old brief cache without changing schema");
                var gen=Queue(root);objects.Add(gen.gameObject);yield return Wait(()=>gen.Ready);
                gen.Enqueue(tree,person,true,new[]{(1870,"York, England"),(1880,"York, England"),(1890,"York, England"),(1900,"York, England")});
                int max=0;float end=Time.realtimeSinceStartup+15;
                while((gen.Busy||gen.WaitingCount>0||gen.ActiveJobs.Count>0)&&Time.realtimeSinceStartup<end){max=Math.Max(max,gen.ActiveCount);yield return null;}
                Check(research==4&&File.ReadAllText(legacyBriefPath)==legacyJson,"Old brief is preserved and fresh research is requested per target year");
                foreach(string file in Directory.EnumerateFiles(Path.Combine(root,"SettingBriefs"),"*.json"))Check(JsonUtility.FromJson<SceneSettingBrief>(File.ReadAllText(file)).version==1,"New and legacy research retain saved provenance schema v1");
                Check(gen.CompletedCount==4&&max==4,"Four direct jobs complete concurrently");Check(images==4&&edits==0,"First generations have no reference sentinel");
                Check(!Directory.EnumerateFiles(root,"*",SearchOption.AllDirectories).Any(f=>File.ReadAllText(f).Contains("fixture-valid-new-key")),"Direct job/checkpoint files contain no key");
                int before=images;var first=File.ReadAllBytes(Path.Combine(root,"Panoramas/people",tree,PanoramaCache.PersonKey(person.Id),"years/1870",PanoramaCache.Hash(png)+".png"));
                gen.Enqueue(tree,person,true,new[]{(1885,"York, England")});yield return Wait(()=>gen.CompletedCount==5);
                Check(edits==1&&lastEdit["images"] is JArray&&lastEdit["images"][0]["image_url"].ToString().EndsWith(Convert.ToBase64String(png)),"Nearest cached PNG uploaded directly as edit reference");
                Check(lastEdit["prompt"].ToString().Contains("from 1880 to 1885"),"Equal-distance reference chooses earlier year");
                Check(lastEdit["prompt"].ToString().Contains("not building age, surface condition or accuracy")&&lastEdit["prompt"].ToString().Contains("allow documented renovations"),"Edit corrects age/materials while keeping viewpoint");
                gen.ScheduleEndpoints(tree,new[]{person},true,p=>new ResidenceTimeline(),p=>Array.Empty<int>());yield return Wait(()=>!gen.Busy&&gen.WaitingCount==0&&gen.ActiveJobs.Count==0);
                Check(images==before+1&&gen.SkippedCount==2,"Automatic endpoints preserve existing images without requests");
                var retryPerson=new Person{Id="rate-fixture",BirthDate="1870",DeathDate="1900",BirthPlace="York, England"};double now=SceneRetryPolicy.UnixNow;typeof(SceneGeneration).GetField("TestClock",Hidden).SetValue(gen,(Func<double>)(()=>now));rateOnce=true;
                gen.Enqueue(tree,retryPerson,true,new[]{(1871,"York, England")});yield return Wait(()=>gen.Waiting.Any(p=>p.retryCount==1));
                Check(gen.Waiting.First().job.transport==DirectMetaScene.Transport,"Retry preserves direct transport");now+=120;typeof(SceneGeneration).GetField("dispatchAfter",Hidden).SetValue(gen,0f);yield return Wait(()=>gen.CompletedCount==6);
                uncertain=true;int posted=images;gen.Enqueue(tree,retryPerson,true,new[]{(1872,"York, England")});yield return Wait(()=>gen.FailedCount==1);
                gen.ResumeQueue();for(int i=0;i<5;i++)yield return null;Check(images==posted+1&&gen.WaitingCount==0,"Uncertain image POST is not replayed");
                var unknown=Job(tree,"recovery-fixture",1873);string recoverRoot=Path.Combine(root,"recovery");Directory.CreateDirectory(recoverRoot);var state=new SceneQueueState();state.active.Add(new PendingScene{treeId=tree,personId="recovery-fixture",job=unknown,phase=2,baselineSet=true,baselineSha=""});TreeStore.AtomicText(Path.Combine(recoverRoot,"scene-generation-queue.json"),JsonUtility.ToJson(state));
                posted=images;var recover=Queue(recoverRoot);objects.Add(recover.gameObject);recover.Resume(tree);yield return Wait(()=>recover.Ready&&recover.FailedCount==1);Check(images==posted,"Restart never resubmits a sent direct request");
                string stagedRoot=Path.Combine(root,"staged-recovery");var stagedJob=Job(tree,"staged-fixture",1874);var staged=new DirectSceneResult();yield return DirectMetaScene.GenerateFor("staged-fixture",stagedRoot,Path.Combine(stagedRoot,"Panoramas"),stagedJob,staged,s=>{},r=>{});Check(staged.Png!=null,"Direct result staged before publication");
                var stagedState=new SceneQueueState();stagedState.active.Add(new PendingScene{treeId=tree,personId="staged-fixture",job=stagedJob,phase=2,baselineSet=true,baselineSha=""});TreeStore.AtomicText(Path.Combine(stagedRoot,"scene-generation-queue.json"),JsonUtility.ToJson(stagedState));posted=images;
                var stagedQueue=Queue(stagedRoot);objects.Add(stagedQueue.gameObject);stagedQueue.Resume(tree);yield return Wait(()=>stagedQueue.CompletedCount==1);Check(images==posted,"Staged direct image recovers without another POST");
                byte[] wav=HistoricalGuide.EncodeWav(Enumerable.Repeat(.1f,24000).ToArray(),1,24000);string transcript=null,failure=null;yield return MetaApi.Transcribe(wav,(t,e)=>{transcript=t;failure=e;},null);Check(transcript=="York, England"&&failure==null,"Direct multipart transcription completes");
                var voiceObject=new GameObject("Direct guide fixture");objects.Add(voiceObject);var guide=voiceObject.AddComponent<HistoricalGuide>();var question=new GuideQuestion{requestId=Guid.NewGuid().ToString("N"),treeId=tree,personKey=PanoramaCache.PersonKey(person.Id),name="Fictional fixture",place="York, England",year=1870,audio=Convert.ToBase64String(wav),synthetic=true};
                yield return (IEnumerator)typeof(HistoricalGuide).GetMethod("AskDirect",Hidden).Invoke(guide,new object[]{question,0});Check(guide.Answer!=null&&guide.Answer.sources.Length==1&&speech==2,"Voice guide directly transcribes and researches");
                var location=voiceObject.AddComponent<LocationVoiceInput>();string recognized=null;location.Recognized+=(purpose,value)=>recognized=value;
                yield return (IEnumerator)typeof(LocationVoiceInput).GetMethod("TranscribeDirect",Hidden).Invoke(location,new object[]{new LocationSpeechRequest{requestId=Guid.NewGuid().ToString("N"),purpose="place",audio=Convert.ToBase64String(wav)},0});Check(recognized=="York, England"&&speech==3,"Location dictation uses direct Meta call");
                var small=new Texture2D(32,24,TextureFormat.RGBA32,false);small.SetPixels32(Enumerable.Repeat(new Color32(120,70,40,255),32*24).ToArray());small.Apply();byte[] photoPng=small.EncodeToPNG();UnityEngine.Object.Destroy(small);string photoId=PanoramaCache.Hash(photoPng);Directory.CreateDirectory(Path.Combine(root,"Photos"));File.WriteAllBytes(Path.Combine(root,"Photos",photoId+".png"),photoPng);
                var photo=new PhotoRecord{Id=photoId,File=photoId+".png",Width=32,Height=24,Orientation=1,Title="Synthetic fixture"};CutoutResult cutout=null;
                yield return DirectMetaCutout.Run(root,photo,"person",r=>cutout=r,s=>last=s,r=>{});
                Check(cutout!=null&&cutout.photos.Count==1&&sam==1,"Direct SAM produces cached alpha cutout without Python");Check(CutoutCache.Read(root,photo,"person")?.photos.Count==1,"SAM result reopens from existing cache format");Check(File.ReadAllBytes(Path.Combine(root,"Photos",photo.File)).SequenceEqual(photoPng),"Source photo unchanged");
                Check(!File.Exists(connection)||File.GetLastWriteTimeUtc(connection)==stamp,"All direct workflows leave helper configuration untouched");
                Check(research>0&&images>0&&models==2,"All direct request categories exercised");
                File.WriteAllText(Report,checks+" direct Meta checks passed: BYOK panel, four concurrent jobs, references, exact-year caches, preserved images, rate limits, uncertain recovery, sourced guide, transcription, 25 official SAM mask fixtures and direct photo cutouts. No external calls or family writes.");
            }
            finally
            {
                MetaApi.TestReply=oldReply;typeof(MetaApi).GetField("sessionKey",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,oldKey);foreach(var o in objects)if(o!=null)UnityEngine.Object.Destroy(o);g.AISetup.Close();SessionState.SetBool("AncestorWalk.EndpointChecks",guard);
            }
        }
    }
}
