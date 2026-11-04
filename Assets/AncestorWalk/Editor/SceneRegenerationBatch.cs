using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace AncestorWalk.Editor
{
    // Explicit Editor operation: feeds the normal bounded queue; never bypasses it.
    [InitializeOnLoad]
    public static class SceneRegenerationBatch
    {
        [Serializable] public sealed class Target
        {
            public string personId,place,beforeHash,requestId,state="pending",error,briefCache;
            public int year;public double queuedUnix,completedUnix,receivedUnix,elapsedSeconds,researchSeconds,imageSeconds;
        }
        [Serializable] public sealed class Plan
        {
            public string id,treeId,output,state="running",message;
            public double startedUnix,finishedUnix;public List<Target> targets=new();
        }
        [Serializable] public sealed class Report
        {
            public string batch,state,message,output;public int total,saved,failed,held,cancelled,pending,queued;
            public double wallSeconds,averageRequestSeconds,medianRequestSeconds,p90RequestSeconds,fastestSeconds,slowestSeconds;
            public double averageResearchSeconds,averageImageSeconds,averageQueueWaitSeconds,completedPerMinute;
            public double averageClientSeconds,averageDownloadSaveSeconds;
            public int briefCacheHits,briefShared,briefMisses;
        }
        static readonly string Checkpoint=Path.GetFullPath("Library/SceneRegenerationBatch.json");
        static Plan plan;static double next,reportAt;static SceneGeneration watched;
        static double Now=>DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()/1000d;
        static SceneRegenerationBatch(){EditorApplication.update+=Update;}
        static void Save(){TreeStore.AtomicText(Checkpoint,JsonUtility.ToJson(plan,true));WriteReport();}
        public static string Start(string skipPerson,int skipYear)
        {
            Load();if(plan?.state=="running")throw new InvalidOperationException("A regeneration batch is already running. Inspect Status first.");
            var g=UnityEngine.Object.FindAnyObjectByType<AncestorGallery>();
            if(!Application.isPlaying||g?.Tree==null||!g.CacheIndexReady||!g.Generation.Ready||g.Generation.Busy||g.Generation.WaitingCount!=0)
                throw new InvalidOperationException("Ready, idle Play mode is required.");
            var created=new Plan{id=Guid.NewGuid().ToString("N"),treeId=g.TreeId,startedUnix=Now};
            created.output=Path.GetFullPath("Artifacts/RegenerationBatch/"+created.id);Directory.CreateDirectory(created.output);
            foreach(var person in g.Tree.People.Values.OrderBy(p=>p.Id,StringComparer.Ordinal))
                foreach(var record in PanoramaCache.YearRecords(g.TreeId,person.Id).OrderBy(r=>r.year))
                {
                    if(person.Id==skipPerson&&record.year==skipYear)continue;
                    if(!LifeEndpointScenes.TrySceneRange(person,out int birth,out int end)||record.year<birth||record.year>end)continue;
                    string place=g.PlaceAt(person.Id,record.year);if(string.IsNullOrWhiteSpace(place))continue;
                    created.targets.Add(new Target{personId=person.Id,year=record.year,place=place,beforeHash=record.sha256});
                    string backup=Path.Combine(created.output,"before",PanoramaCache.PersonKey(person.Id),record.year+".json");Directory.CreateDirectory(Path.GetDirectoryName(backup));
                    File.Copy(Path.Combine(PanoramaCache.DirectoryPath,"people",g.TreeId,PanoramaCache.PersonKey(person.Id),"years",record.year.ToString(),"scene.json"),backup);
                }
            plan=created;Save();g.Generation.ResumeQueue();return Status();
        }
        static void Load()
        {if(plan==null&&File.Exists(Checkpoint))plan=JsonUtility.FromJson<Plan>(File.ReadAllText(Checkpoint));}
        static bool Match(PendingScene p,Target t)=>p.personId==t.personId&&p.job.year==t.year&&ResidenceTimeline.Same(p.job.place,t.place);
        static void Clear()
        {
            if(plan?.state!="running")return;
            foreach(var t in plan.targets.Where(t=>t.state=="pending"||t.state=="dispatching"||t.state=="queued"&&!watched.ActiveJobs.Any(p=>Match(p,t))))t.state="cancelled";
            plan.message="Waiting regeneration work cleared. Active requests may finish.";Save();
        }
        static void Update()
        {
            if(EditorApplication.timeSinceStartup<next||EditorApplication.isCompiling)return;next=EditorApplication.timeSinceStartup+1;
            try
            {
                Load();if(plan?.state!="running")return;
                if(!Application.isPlaying){plan.message="Waiting for Play mode.";if(EditorApplication.timeSinceStartup>reportAt){WriteReport();reportAt=EditorApplication.timeSinceStartup+5;}return;}
                var g=UnityEngine.Object.FindAnyObjectByType<AncestorGallery>();if(g?.TreeId!=plan.treeId||!g.CacheIndexReady||g.Generation?.Ready!=true)return;
                var gen=g.Generation;if(watched!=gen){if(watched!=null)watched.WaitingCleared-=Clear;watched=gen;gen.WaitingCleared+=Clear;}
                bool changed=false;
                foreach(var t in plan.targets.Where(t=>t.state=="dispatching"||t.state=="queued"))
                {
                    var queued=gen.ActiveJobs.Concat(gen.Waiting).FirstOrDefault(p=>Match(p,t));
                    if(string.IsNullOrEmpty(t.requestId)&&queued!=null){t.requestId=queued.job.requestId;t.state="queued";changed=true;}
                    SceneJob result=null;
                    if(!string.IsNullOrEmpty(t.requestId))
                    {
                        string file=Path.Combine("Artifacts/SceneService/jobs",t.requestId+".json");
                        if(File.Exists(file))result=JsonUtility.FromJson<SceneJob>(File.ReadAllText(file));
                    }
                    if(result!=null&&(result.treeId!=plan.treeId||result.personKey!=PanoramaCache.PersonKey(t.personId)||result.year!=t.year||!ResidenceTimeline.Same(result.place,t.place)))
                        throw new InvalidDataException("Regeneration result identity mismatch.");
                    if(result?.status=="failed")
                    {t.state="failed";t.error=result.error;t.completedUnix=Now;Capture(t,result);changed=true;}
                    else if(result?.status=="complete")
                    {
                        var record=PanoramaCache.YearRecords(plan.treeId,t.personId).FirstOrDefault(r=>r.year==t.year);
                        if(record!=null&&record.sha256==result.sha256&&record.createdUtc==result.createdUtc)
                        {t.state="saved";t.completedUnix=Now;Capture(t,result);changed=true;}
                    }
                    if(queued==null&&t.state!="saved"&&t.state!="failed"&&Now-t.queuedUnix>5)
                    {t.state="held";t.error=gen.Message(t.personId);t.completedUnix=Now;changed=true;}
                }
                if(!plan.targets.Any(t=>t.state=="pending"||t.state=="dispatching"||t.state=="queued"))
                {plan.state=plan.targets.Any(t=>t.state=="failed"||t.state=="held")?"finished_with_errors":"complete";plan.finishedUnix=Now;plan.message="Batch finished. Failed/uncertain targets were not retried.";Save();return;}
                if(gen.QueuePaused)plan.message="Paused through the scene queue.";
                else if(!gen.Running&&gen.ActiveJobs.Count>0)plan.message="Queue needs attention; pending work preserved.";
                else
                {
                    int capacity=SceneQueueState.MaximumPending-gen.Waiting.Count-gen.ActiveJobs.Count;
                    foreach(var t in plan.targets.Where(t=>t.state=="pending").Take(Math.Min(4,Math.Max(0,capacity))).ToArray())
                    {
                        if(!g.Tree.People.TryGetValue(t.personId,out var person)||!ResidenceTimeline.Same(g.PlaceAt(t.personId,t.year),t.place))
                        {t.state="held";t.error="Location/person changed before dispatch.";changed=true;continue;}
                        t.state="dispatching";t.queuedUnix=Now;Save(); // Ambiguous crash windows are held, never blindly resubmitted.
                        gen.Enqueue(plan.treeId,person,plan.treeId==PanoramaCache.DemoTreeId,new[]{(t.year,t.place)});
                        var item=gen.Waiting.Concat(gen.ActiveJobs).First(p=>Match(p,t));t.requestId=item.job.requestId;t.state="queued";changed=true;Save();
                    }
                    plan.message="Feeding the normal 20-target queue; four concurrent requests.";
                }
                if(changed)Save();else if(EditorApplication.timeSinceStartup>reportAt){WriteReport();reportAt=EditorApplication.timeSinceStartup+5;}
            }
            catch(Exception e){if(plan!=null){plan.message="Batch needs attention: "+e.Message;Save();}Debug.LogWarning("Scene regeneration batch: "+e.Message);}
        }
        static void Capture(Target t,SceneJob job)
        {t.receivedUnix=job.receivedUnix;t.elapsedSeconds=job.elapsedSeconds;t.researchSeconds=job.researchSeconds;t.imageSeconds=job.imageSeconds;t.briefCache=job.briefCache;}
        public static Report Summary(Plan p,double now)
        {
            if(p.finishedUnix>0)now=p.finishedUnix;
            var saved=p.targets.Where(t=>t.state=="saved").ToArray();var measured=p.targets.Where(t=>(t.state=="saved"||t.state=="failed")&&t.elapsedSeconds>0).ToArray();var seconds=measured.Select(t=>t.elapsedSeconds).OrderBy(x=>x).ToArray();
            double Mean(IEnumerable<double> values){var a=values.ToArray();return a.Length==0?0:a.Average();}
            double Percentile(double f)=>seconds.Length==0?0:seconds[(int)Math.Ceiling((seconds.Length-1)*f)];
            return new Report{batch=p.id,state=p.state,message=p.message,output=p.output,total=p.targets.Count,saved=saved.Length,failed=p.targets.Count(t=>t.state=="failed"),held=p.targets.Count(t=>t.state=="held"),cancelled=p.targets.Count(t=>t.state=="cancelled"),pending=p.targets.Count(t=>t.state=="pending"),queued=p.targets.Count(t=>t.state=="queued"||t.state=="dispatching"),wallSeconds=Math.Max(0,now-p.startedUnix),averageRequestSeconds=Mean(seconds),medianRequestSeconds=seconds.Length==0?0:seconds.Length%2==1?seconds[seconds.Length/2]:(seconds[seconds.Length/2-1]+seconds[seconds.Length/2])*.5,p90RequestSeconds=Percentile(.9),fastestSeconds=seconds.FirstOrDefault(),slowestSeconds=seconds.LastOrDefault(),averageResearchSeconds=Mean(measured.Select(t=>t.researchSeconds)),averageImageSeconds=Mean(measured.Where(t=>t.imageSeconds>0).Select(t=>t.imageSeconds)),averageQueueWaitSeconds=Mean(saved.Where(t=>t.receivedUnix>0).Select(t=>Math.Max(0,t.receivedUnix-t.queuedUnix))),averageClientSeconds=Mean(saved.Select(t=>t.completedUnix-t.queuedUnix)),averageDownloadSaveSeconds=Mean(saved.Where(t=>t.receivedUnix>0).Select(t=>Math.Max(0,t.completedUnix-t.receivedUnix-t.elapsedSeconds))),completedPerMinute=saved.Length*60/Math.Max(1,now-p.startedUnix),briefCacheHits=measured.Count(t=>t.briefCache=="hit"),briefShared=measured.Count(t=>t.briefCache=="shared"),briefMisses=measured.Count(t=>t.briefCache=="miss")};
        }
        static void WriteReport()
        {
            if(plan==null)return;var report=Summary(plan,Now);Directory.CreateDirectory(plan.output);TreeStore.AtomicText(Path.Combine(plan.output,"stats.json"),JsonUtility.ToJson(report,true));
            string Quote(string s)=>"\""+(s??"").Replace("\"","\"\"")+"\"";string Number(double n)=>n.ToString("0.000",CultureInfo.InvariantCulture);
            var csv=new StringBuilder("person_id,year,place,request_id,status,request_seconds,research_seconds,image_seconds,queue_wait_seconds,brief_cache,error\n");
            foreach(var t in plan.targets)csv.AppendLine(string.Join(",",Quote(t.personId),t.year.ToString(),Quote(t.place),Quote(t.requestId),Quote(t.state),Number(t.elapsedSeconds),Number(t.researchSeconds),Number(t.imageSeconds),Number(t.receivedUnix>0?Math.Max(0,t.receivedUnix-t.queuedUnix):0),Quote(t.briefCache),Quote(t.error)));
            TreeStore.AtomicText(Path.Combine(plan.output,"requests.csv"),csv.ToString());
        }
        public static string Status(){Load();return plan==null?"No batch.":JsonUtility.ToJson(Summary(plan,Now),true);}
    }
}
