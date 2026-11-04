using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace AncestorWalk.Editor
{
    public static class SceneQueueChecks
    {
        static readonly string Tree=PanoramaCache.Hash(Encoding.UTF8.GetBytes("queue-fixture"));
        static PendingScene Item(int year,string place="York",string person="fixture")=>new PendingScene{treeId=Tree,personId=person,job=new SceneJob{requestId=Guid.NewGuid().ToString("N"),treeId=Tree,personKey=PanoramaCache.PersonKey(person),year=year,date=year.ToString(),place=place,synthetic=true}};
        public static string Data()
        {
            int n=0;void Check(bool ok,string why){if(!ok)throw new Exception(why);n++;}void Reject(Action action,string why){bool rejected=false;try{action();}catch{rejected=true;}Check(rejected,why);}
            Check(SceneQueueState.ParseYears("1934,1936,1934",1900,1980).SequenceEqual(new[]{1934,1936}),"Exact years deduplicate without bucketing");
            Check(SceneQueueState.ParseYears("1900-1940/10,1934",1900,1980).SequenceEqual(new[]{1900,1910,1920,1930,1934,1940}),"Explicit intervals expand exact integer years");
            Check(SceneQueueState.ParseYears("1900,1980",1900,1980).Length==2,"Endpoints accepted");
            foreach(string bad in new[]{"1899","1981","1940-1900","1900-1940/0","abc","1900,","1900-1980"})Reject(()=>SceneQueueState.ParseYears(bad,1900,1980),"Invalid/outsize batch rejected");
            var q=new SceneQueueState();var a=Item(1934);Check(q.Append(new[]{a,Item(1934),Item(1936)},null)==2,"Duplicate queued target coalesced");Check(q.waiting[0].job.requestId==a.job.requestId,"Request ID stays stable");
            var active=q.waiting[0];q.waiting.RemoveAt(0);Check(q.Append(new[]{Item(1934)},active)==0,"Active target is not submitted twice");Check(q.Append(new[]{Item(1934,"Paris")},active)==1,"Distinct location snapshot remains distinct");
            var restored=JsonUtility.FromJson<SceneQueueState>(JsonUtility.ToJson(q));restored.Validate();Check(restored.waiting[0].job.requestId==q.waiting[0].job.requestId,"Durable queue keeps original IDs");
            var full=new SceneQueueState();full.Append(Enumerable.Range(1900,20).Select(y=>Item(y)),null);Reject(()=>full.Append(new[]{Item(1950)},null),"Pending limit enforced atomically");Check(full.waiting.Count==20,"Overflow preserves existing queue");
            var concurrent=new SceneQueueState();
            for(int i=0;i<SceneGeneration.MaximumConcurrent;i++){var request=Item(1900+i);request.phase=2;concurrent.active.Add(request);}
            concurrent.Validate();Check(concurrent.active.Count==8,"Eight independent active request checkpoints are accepted");
            var ninth=Item(1950);ninth.phase=2;concurrent.active.Add(ninth);Reject(concurrent.Validate,"Ninth active request is rejected");
            var priority=new SceneQueueState();var manual=Item(1900);var bulk=Item(1910);bulk.isBulkRegeneration=true;var automatic=Item(1920);automatic.isAutomatic=true;
            priority.waiting.Add(manual);priority.waiting.Add(bulk);priority.automatic.Add(automatic);
            string originalId=automatic.job.requestId;Check(priority.Next(Tree,1000)==manual,"Manual requests retain normal priority");
            Check(priority.Promote(originalId)&&priority.Next(Tree,1000)==automatic&&automatic.isAutomatic&&automatic.job.requestId==originalId,"Promotion reorders an existing automatic request without changing its identity or cache protection");
            var persisted=JsonUtility.FromJson<SceneQueueState>(JsonUtility.ToJson(priority));persisted.Validate();Check(persisted.Next(Tree,1000).job.requestId==originalId,"Promoted order survives restart");
            automatic.notBeforeUnix=1015;Check(priority.Next(Tree,1000)==manual,"Priority cannot bypass retry backoff");automatic.notBeforeUnix=0;
            priority.automaticPaused=true;Check(priority.Next(Tree,1000)==manual,"Priority cannot bypass paused automatic work");priority.automaticPaused=false;
            priority.active.Add(Item(1920));Check(priority.Next(Tree,1000)==manual&&!priority.Promote(priority.active[0].job.requestId),"Active same-year work and active request identities cannot be preempted");priority.active.Clear();
            Check(priority.Promote(bulk.job.requestId)&&priority.Next(Tree,1000)==bulk,"Most recent front request takes precedence across queue types");
            var mismatch=Item(1900);mismatch.job.year=1901;Reject(()=>SceneQueueState.Validate(mismatch),"Year/date mismatch rejected");
            mismatch=Item(1900);mismatch.job.personKey=new string('f',64);Reject(()=>SceneQueueState.Validate(mismatch),"Person identity mismatch rejected");
            var imported=Item(1900);imported.job.synthetic=false;SceneQueueState.Validate(imported);Check(true,"Imported-family scene accepted");
            var importedQueue=new SceneQueueState();importedQueue.Append(new[]{imported},null);var importedRestored=JsonUtility.FromJson<SceneQueueState>(JsonUtility.ToJson(importedQueue));importedRestored.Validate();Check(!importedRestored.waiting[0].job.synthetic,"Imported provenance survives queue persistence");
            var legacy=Item(1900);legacy.job.year=0;legacy.job.date="ABT 1870";SceneQueueState.Validate(legacy,true);Check(true,"Legacy pending requests remain recoverable");
            var person=new Person{Id="endpoint",BirthDate="1900",BirthPlace="York",DeathDate="1980",DeathPlace="Paris"};var places=new ResidenceTimeline();places.entries.Add(new Residence{year=1950,place="Berlin"});
            Check(LifeEndpointScenes.Targets(person,places).SequenceEqual(new[]{(1900,"York"),(1980,"Paris")}),"Birth/death endpoints use recorded death place instead of previous move");
            Check(places.At(person,1979)=="Berlin"&&places.At(person,1980)=="Paris","Death place changes only at death year");
            places.entries.Add(new Residence{year=1980,place="Rome"});Check(LifeEndpointScenes.Targets(person,places).Last().Place=="Rome","Explicit death-year edit wins over GEDCOM");
            person.DeathDate="";person.KnownLiving=true;Check(LifeEndpointScenes.Targets(person,places).Select(x=>x.Year).SequenceEqual(new[]{1900}),"Living person gets birth only");
            person.KnownLiving=false;Check(LifeEndpointScenes.Targets(person,places).Select(x=>x.Year).SequenceEqual(new[]{1900}),"A known birth queues independently without inferring living");
            person.DeathDate="1900";Check(LifeEndpointScenes.Targets(person,places).Count()==1,"Same-year endpoints coalesce");
            person.BirthPlace=person.DeathPlace="";places.entries.Clear();Check(!LifeEndpointScenes.Targets(person,places).Any(),"Unknown places are not invented");
            var auto=new SceneQueueState();var endpoints=Enumerable.Range(1800,62).Select(y=>Item(y)).ToArray();Check(auto.AppendAutomatic(endpoints,null)==62,"All sphere endpoints fit beyond the manual 20-job limit");
            Check(auto.AppendAutomatic(endpoints,null)==0,"Automatic endpoints never duplicate on rebuild");
            auto.automatic.Clear();Check(auto.AppendAutomatic(endpoints,null)==0,"Cleared/failed/completed endpoint identities do not retry automatically");
            auto=JsonUtility.FromJson<SceneQueueState>(JsonUtility.ToJson(auto));auto.Validate();Check(auto.AppendAutomatic(endpoints,null)==0,"Automatic seen ledger survives restart");
            Check(auto.AppendAutomatic(new[]{Item(1800,"Kyoto")},null)==1,"Changed endpoint location can schedule a new target");
            string result=n+" scene queue data checks passed: exact years, ranges, immutable identity, deduplication, persistence, bounds and legacy recovery.";Directory.CreateDirectory("Artifacts");File.WriteAllText("Artifacts/scene-queue-data-checks.txt",result);return result;
        }
        internal sealed class FakeHelper:IDisposable
        {
            readonly HttpListener listener=new();readonly Thread thread;readonly byte[] png;readonly string hash;readonly object gate=new();
            readonly Dictionary<string,SceneJob> jobs=new();readonly Dictionary<string,DateTime> readyAt=new();
            public readonly List<SceneJob> Requests=new();public readonly List<string> PostIds=new();
            public readonly Dictionary<string,byte[]> References=new();public int ReferenceUploads,ReferenceDelayMilliseconds;
            public volatile bool Hold;public bool RateLimitOnce;public Action<SceneJob> BeforeImage;public string Stage;public SceneSettingBrief SettingBrief;
            public int Posts,Gets,ImageGets,MaximumActive;int outstanding;public int FailYear=-1,TransientFailures,PolicyFailures,ReadyDelayMilliseconds=250;public bool RejectOnce,AmbiguousOnce,ImageFailureOnce;public readonly string Url;
            public FakeHelper(byte[] image)
            {
                png=image;hash=PanoramaCache.Hash(image);var reserve=new TcpListener(IPAddress.Loopback,0);reserve.Start();int port=((IPEndPoint)reserve.LocalEndpoint).Port;reserve.Stop();Url="http://127.0.0.1:"+port;
                listener.Prefixes.Add(Url+"/");listener.Start();thread=new Thread(Serve){IsBackground=true};thread.Start();
            }
            public void Seed(SceneJob job){lock(gate){jobs[job.requestId]=job;readyAt[job.requestId]=DateTime.UtcNow;}}
            void Serve()
            {while(listener.IsListening){try{var context=listener.GetContext();Handle(context);}catch(HttpListenerException){break;}catch(ObjectDisposedException){break;}}}
            void Json(HttpListenerContext c,int status,object body){var bytes=Encoding.UTF8.GetBytes(JsonUtility.ToJson(body));c.Response.StatusCode=status;c.Response.ContentType="application/json";c.Response.ContentLength64=bytes.Length;c.Response.OutputStream.Write(bytes,0,bytes.Length);c.Response.Close();}
            [Serializable] sealed class Error {public string error="Fixture failure";}
            void Handle(HttpListenerContext c)
            {
                try
                {
                    if(c.Request.Url.AbsolutePath.StartsWith("/references/",StringComparison.Ordinal))
                    {
                        string path=c.Request.Url.AbsolutePath;string digest=path.Split('/').Last();
                        if(c.Request.HttpMethod=="POST")
                        {
                            using var bytes=new MemoryStream();c.Request.InputStream.CopyTo(bytes);var image=bytes.ToArray();
                            if(PanoramaCache.Hash(image)!=digest){Json(c,400,new Error());return;}
                            lock(gate){References[path]=image;ReferenceUploads++;}if(ReferenceDelayMilliseconds>0)Thread.Sleep(ReferenceDelayMilliseconds);Json(c,200,new ReferenceAck{sha256=digest});return;
                        }
                        lock(gate){Json(c,References.ContainsKey(path)?200:404,new ReferenceAck{sha256=digest});}return;
                    }
                    if(c.Request.HttpMethod=="POST")
                    {
                        var job=JsonUtility.FromJson<SceneJob>(new StreamReader(c.Request.InputStream).ReadToEnd());job.reference=SceneReference.Optional(job.reference);job.generationMode=job.reference!=null?"edit":"generate";job.horizonLift=job.reference?.horizonLift??12;lock(gate){Posts++;PostIds.Add(job.requestId);}
                        if(RejectOnce){RejectOnce=false;Json(c,409,new Error());return;}
                        if(RateLimitOnce){RateLimitOnce=false;c.Response.Headers["Retry-After"]="1";Json(c,429,new Error());return;}
                        lock(gate){if(!jobs.ContainsKey(job.requestId)){jobs[job.requestId]=job;readyAt[job.requestId]=DateTime.UtcNow.AddMilliseconds(ReadyDelayMilliseconds);Requests.Add(job);outstanding++;MaximumActive=Math.Max(MaximumActive,outstanding);}}
                        if(AmbiguousOnce){AmbiguousOnce=false;Json(c,503,new Error());return;}Json(c,202,job);return;
                    }
                    string[] parts=c.Request.Url.AbsolutePath.Trim('/').Split('/');SceneJob value;lock(gate){Gets++;jobs.TryGetValue(parts.Length>1?parts[1]:"",out value);}
                    if(value==null){Json(c,404,new Error());return;}
                    if(parts.Length==3)
                    {
                        ImageGets++;BeforeImage?.Invoke(value);if(ImageFailureOnce){ImageFailureOnce=false;Json(c,503,new Error());return;}
                        c.Response.StatusCode=200;c.Response.ContentType="image/png";c.Response.ContentLength64=png.Length;c.Response.OutputStream.Write(png,0,png.Length);c.Response.Close();return;
                    }
                    lock(gate)
                    {
                        if(!Hold&&DateTime.UtcNow>=readyAt[value.requestId])
                        {
                            if(value.status!="complete"&&value.status!="failed")
                            {
                                outstanding=Math.Max(0,outstanding-1);bool transient=TransientFailures>0;if(transient)TransientFailures--;bool policy=PolicyFailures>0;if(policy)PolicyFailures--;
                                value.status=policy||transient||value.year==FailYear?"failed":"complete";value.retryable=transient;value.failureCode=policy?"image_http_400":transient?"research_transient":"generation_permanent";value.error=policy?"Meta HTTP 400. code: content_policy_violation. param: prompt.":transient?"Temporary fixture failure":value.year==FailYear?"Fixture model failure":null;
                                value.sha256=hash;value.model="fixture";value.prompt="fixture";value.createdUtc=DateTime.UtcNow.ToString("O");value.settingBrief=SettingBrief;
                            }
                        }
                        else {value.status="generating";value.stage=Stage;}
                    }
                    Json(c,200,value);
                }
                catch(Exception){try{c.Response.Abort();}catch{}}
            }
            [Serializable] sealed class ReferenceAck {public string sha256;}
            public void Dispose(){listener.Close();thread.Join(1000);}
        }
        public static string Play()=>EndpointQueueChecks.Play();
    }
}
