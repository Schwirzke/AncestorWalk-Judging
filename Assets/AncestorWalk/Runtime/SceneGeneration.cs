using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace AncestorWalk
{
    public sealed class GenerationErrorNotice
    {public string Tree,Person,Message;public int Year;}
    [Serializable] public sealed class SceneServiceConnection { public string url,token; }
    [Serializable] public sealed class SceneSettingBrief
    {
        public int version;
        public string place,date,model,text,createdUtc,citationMode;
        public GuideSource[] sources;
        public static SceneSettingBrief Copy(SceneSettingBrief b)=>b==null?null:new SceneSettingBrief{version=b.version,place=b.place,date=b.date,model=b.model,text=b.text,createdUtc=b.createdUtc,citationMode=b.citationMode,sources=b.sources?.Select(s=>s==null?null:new GuideSource{title=s.title,url=s.url}).ToArray()};
    }
    [Serializable] public sealed class SceneJob
    {
        public string requestId,treeId,personKey,place,date,status,stage,error,model,prompt,sha256,createdUtc,briefCache,failureCode,transport;
        public string details;
        public bool retryable;
        public double receivedUnix,elapsedSeconds,researchSeconds,imageSeconds;
        public SceneSettingBrief settingBrief;
        public SceneReference reference,basePanorama;public bool referenceResolved,basePanoramaResolved;public string generationMode;
        public int year,policyRetryLevel;
        public float horizonLift=12,retryAfter;
        public bool synthetic=true;
    }
    public sealed class SceneGeneration : MonoBehaviour
    {
        public const int MaximumConcurrent=8;
        public static bool ReferencePanoramasEnabled=>false; // Temporarily generate each year independently.
        public event Action Changed;
        public event Action WaitingCleared;
        public bool QueuePaused=>queue.automaticPaused;
        public event Action<string> SceneSaved;
        public event Action<string,string,int> SceneCompleted;
        public event Action<string> TargetChanged;
        public string ActivePerson=>workers.Count==0?null:workers.Keys.First().personId;
        public bool Busy=>workers.Count>0;
        public bool Ready {get;private set;}
        public bool Running {get;private set;}
        public int WaitingCount=>queue.waiting.Count+queue.automatic.Count;
        public int ActiveCount=>workers.Count;
        public int CompletedCount {get;private set;}
        public int FailedCount {get;private set;}
        public int SkippedCount {get;private set;}
        public int Revision {get;private set;}
        public GenerationErrorNotice LatestError {get;private set;}
        public static bool MatchesHelperResult(SceneJob result,SceneJob requested)
            =>result!=null&&requested!=null&&result.requestId==requested.requestId&&result.treeId==requested.treeId&&result.personKey==requested.personKey&&result.year==requested.year&&result.policyRetryLevel==requested.policyRetryLevel&&ResidenceTimeline.Same(result.place,requested.place)&&SceneReference.Same(result.reference,requested.reference)&&SceneReference.Same(result.basePanorama,requested.basePanorama)&&(result.details??"")==(requested.details??"");
        void ReportError(string tree,string person,int year,string message)
        {LatestError=new GenerationErrorNotice{Tree=tree,Person=person,Year=year,Message=message};}
        public PendingScene Active=>queue.active.FirstOrDefault();
        public IReadOnlyList<PendingScene> ActiveJobs=>queue.active;
        readonly List<string> recent=new();public IReadOnlyList<string> Recent=>recent;
        readonly List<PendingScene> waitingView=new();public IReadOnlyList<PendingScene> Waiting=>waitingView;
        public string QueueMessage {get;private set;}="";
        readonly Dictionary<string,string> messages=new();
        sealed class Worker
        {
            public PendingScene Pending;public UnityWebRequest Request;public string ProgressLabel;
            public Coroutine Coroutine;public IEnumerator Operation;public Task Commit;
            public PendingScene Retry;
            public bool Keep=true,Requeue,Failed,Saved,Skipped,RecordTiming;public string Message="Request interrupted. Resume checks the same request.";
        }
        readonly Dictionary<PendingScene,Worker> workers=new();readonly HashSet<string> checkedRequests=new();
        readonly Dictionary<string,object> manualReplacements=new();
        int manualPauseVersion;
        static string ManualTarget(string tree,string person,int year)=>tree+"/"+PanoramaCache.PersonKey(person)+"/"+year;
        readonly Dictionary<PendingScene,PendingScene> checkpointCopies=new();
        sealed class ReferenceUpload {public bool Done,Ready;}
        [Serializable] sealed class ReferenceAck {public string sha256;}
        [Serializable] sealed class PhotoInsertHealth {public int photoInsertVersion;}
        static void CheckReferenceAck(UnityWebRequest request,SceneReference reference)
        {if(JsonUtility.FromJson<ReferenceAck>(request.downloadHandler.text)?.sha256!=reference.sha256)throw new IOException("Reference acknowledgement mismatch. No image requested.");}
        readonly Dictionary<string,ReferenceUpload> referenceUploads=new();
        SceneServiceConnection connection;SceneQueueState queue=new();
        string storage,openTree;float dispatchAfter;
        GenerationTimingHistory timings=new();
        Task persistence=Task.CompletedTask,cancellation;
        public bool CancellationNeedsSaving=>cancellation!=null&&cancellation.Status!=TaskStatus.RanToCompletion;
#if UNITY_EDITOR
        internal string TestStorage;internal SceneServiceConnection TestConnection;internal Func<double> TestClock;
#endif
        double Now
        {
            get{
#if UNITY_EDITOR
                if(TestClock!=null)return TestClock();
#endif
                return SceneRetryPolicy.UnixNow;
            }
        }
        string queuePath=>Path.Combine(storage,"scene-generation-queue.json");
        public string Message(string id)=>messages.TryGetValue(id,out var value)?value:"";
        void Awake()
        {
#if UNITY_EDITOR
            storage=TestStorage??PanoramaCache.StorageRoot;
#else
            storage=PanoramaCache.StorageRoot;
#endif
            StartCoroutine(LoadQueue());
        }
        IEnumerator LoadQueue()
        {
            string path=queuePath,legacy=Path.Combine(storage,"pending-scene.json");
            var timingLoad=Task.Run(()=>
            {
                try{string file=Path.Combine(storage,"generation-timings.json");var history=File.Exists(file)&&new FileInfo(file).Length<=32768?JsonUtility.FromJson<GenerationTimingHistory>(File.ReadAllText(file)):new GenerationTimingHistory();history??=new();history.Normalize();return history;}
                catch{return new GenerationTimingHistory();}
            });
            var load=Task.Run(()=>
            {
                var state=File.Exists(path)?JsonUtility.FromJson<SceneQueueState>(ReadSmall(path)):new SceneQueueState();state.Validate();
                if(File.Exists(legacy))
                {
                    var pending=JsonUtility.FromJson<PendingScene>(ReadSmall(legacy));SceneQueueState.Validate(pending,true);
                    var duplicate=state.waiting.Concat(state.automatic).FirstOrDefault(x=>x.job.requestId==pending.job.requestId);
                    if(duplicate?.phase==1)pending.phase=1; // A durable rejection/unsent checkpoint wins.
                    state.waiting.RemoveAll(x=>x.job.requestId==pending.job.requestId);state.automatic.RemoveAll(x=>x.job.requestId==pending.job.requestId);
                    if(!state.active.Any(x=>x.job.requestId==pending.job.requestId))state.active.Add(pending);
                }
                foreach(var pending in state.active.Where(x=>x.phase==1).ToArray())
                {state.active.Remove(pending);(pending.isAutomatic?state.automatic:state.waiting).Insert(0,pending);}
                state.version=2;state.Validate();Directory.CreateDirectory(storage);
                TreeStore.AtomicText(path,JsonUtility.ToJson(state)); // Commit migration before retiring legacy state.
                if(File.Exists(legacy))File.Delete(legacy);return state;
            });
            while(!load.IsCompleted)yield return null;
            if(load.IsFaulted){_=load.Exception;QueueMessage="Queue could not be read. Existing files were preserved.";Signal();yield break;}
            while(!timingLoad.IsCompleted)yield return null;timings=timingLoad.Result;
            queue=load.Result;Ready=true;
            Running=openTree!=null&&!queue.automaticPaused;
            QueueMessage=queue.automaticPaused?"Queue paused.":WaitingCount>0?"Open a tree to continue its waiting scenes.":"";Signal();
        }
        // Bound both the endpoint backlog and the capped stopped-request identity history.
        static string ReadSmall(string path){if(new FileInfo(path).Length>16*1024*1024)throw new IOException("Queue file too large.");return File.ReadAllText(path);}
        Task Persist()
        {
            // Request payloads are immutable. Freeze mutable phases before worker JSON/IO;
            // reuse those copies until a phase changes, even with a 4,000-item backlog.
            PendingScene Freeze(PendingScene p)
            {
                if(!checkpointCopies.TryGetValue(p,out var copy)||copy.phase!=p.phase||!ReferenceEquals(copy.job,p.job))
                    checkpointCopies[p]=copy=new PendingScene{treeId=p.treeId,personId=p.personId,job=p.job,phase=p.phase,isAutomatic=p.isAutomatic,isBulkRegeneration=p.isBulkRegeneration,retryCount=p.retryCount,notBeforeUnix=p.notBeforeUnix,startedUnix=p.startedUnix,expectedSeconds=p.expectedSeconds,queuePriority=p.queuePriority,baselineSet=p.baselineSet,baselineSha=p.baselineSha};
                return copy;
            }
            var snapshot=new SceneQueueState{version=2,automaticPaused=queue.automaticPaused,waiting=queue.waiting.Select(Freeze).ToList(),automatic=queue.automatic.Select(Freeze).ToList(),active=queue.active.Select(Freeze).ToList(),cancelled=queue.cancelled.Select(Freeze).ToList(),automaticSeen=new List<string>(queue.automaticSeen)};
            string path=queuePath;var before=persistence;
            return persistence=Task.Run(async()=>{await before.ConfigureAwait(false);Directory.CreateDirectory(storage);TreeStore.AtomicText(path,JsonUtility.ToJson(snapshot));});
        }
        void Signal(){waitingView.Clear();waitingView.AddRange(queue.waiting);waitingView.AddRange(queue.automatic);Revision++;Changed?.Invoke();}
        void Notify(string person,string message){messages[person]=message;Signal();TargetChanged?.Invoke(person);}
        public IEnumerable<int> GeneratingYears(string tree,string person)
        {foreach(var pair in workers)if(pair.Key.treeId==tree&&pair.Key.personId==person&&pair.Key.phase==2)yield return pair.Key.job.year;}
        public IEnumerable<int> QueuedYears(string tree,string person)
        {foreach(var p in queue.waiting)if(p.treeId==tree&&p.personId==person)yield return p.job.year;foreach(var p in queue.automatic)if(p.treeId==tree&&p.personId==person)yield return p.job.year;}
        public PendingScene QueuedTarget(string tree,string person,int year)=>queue.Queued(tree,person,year);
        public bool MoveToFront(string tree,string person,int year)
        {
            if(!Ready)return false;var pending=QueuedTarget(tree,person,year);
            if(pending==null||!queue.Promote(pending.job.requestId))return false;
            checkpointCopies.Remove(pending);Persist();QueueMessage=year+" moved to the front of the waiting queue.";Notify(person,QueueMessage);return true;
        }
        void RefreshWaitingTargets(IEnumerable<PendingScene> targets)
        {foreach(var id in targets.Select(p=>p.personId).Distinct())TargetChanged?.Invoke(id);}
        public bool TryGetProgress(string tree,string person,int year,out string label)
        {foreach(var pair in workers)if(pair.Key.treeId==tree&&pair.Key.personId==person&&pair.Key.job.year==year&&pair.Key.phase==2){label=pair.Value.ProgressLabel;return true;}var pending=queue.waiting.Concat(queue.automatic).FirstOrDefault(p=>p.treeId==tree&&p.personId==person&&p.job.year==year&&p.retryCount>0);label=pending==null?null:RetryLabel(pending);return pending!=null;}
        public bool TryGetGeneratingLocation(string tree,string person,int year,out string place)
        {
            foreach(var pair in workers)if(pair.Key.treeId==tree&&pair.Key.personId==person&&pair.Key.job.year==year&&!pair.Value.Failed&&pair.Value.Retry==null)
            {place=pair.Key.job.place;return true;}
            place=null;return false;
        }
        void BeginProgress(PendingScene p)
        {
            if(p.startedUnix>0&&p.startedUnix<=Now&&GenerationTimingHistory.Valid(p.expectedSeconds))return;
            p.startedUnix=Now;p.expectedSeconds=timings.Average(p.job.reference!=null);checkpointCopies.Remove(p);
        }
        public string PercentText(PendingScene p)
        {
            if(p==null||p.phase!=2||!workers.TryGetValue(p,out var worker))return "";
            return GenerationTimingHistory.Percent(p.startedUnix>0?Now-p.startedUnix:0,p.expectedSeconds,worker.Saved)+"%";
        }
        public string PercentText(string tree,string person,int year)
        {foreach(var p in workers.Keys)if(p.treeId==tree&&p.personId==person&&p.job.year==year)return PercentText(p);return "";}
        string RetryLabel(PendingScene p)=>"Retry "+p.retryCount+"/"+SceneRetryPolicy.MaximumRetries+(p.job.policyRetryLevel>0?" · simpler scene":"")+(queue.automaticPaused?" paused":" · "+Math.Max(0,(int)Math.Ceiling(p.notBeforeUnix-Now))+"s");
        public bool Scheduled(string tree,string person,int year,string place)
        {bool Match(PendingScene p)=>p.treeId==tree&&p.personId==person&&p.job.year==year&&(p.isAutomatic||ResidenceTimeline.Same(p.job.place,place));return queue.active.Any(Match)||queue.waiting.Any(Match)||queue.automatic.Any(Match);}
        public string TargetLabel(string tree,string person,int year,string place)
        {
            var active=queue.active.FirstOrDefault(p=>p.treeId==tree&&p.personId==person&&p.job.year==year&&(p.isAutomatic||ResidenceTimeline.Same(p.job.place,place)));
            if(active!=null)return (workers.ContainsKey(active)?"GENERATING ":"CHECK ")+year;
            var retry=queue.waiting.Concat(queue.automatic).FirstOrDefault(p=>p.treeId==tree&&p.personId==person&&p.job.year==year&&p.retryCount>0&&(p.isAutomatic||ResidenceTimeline.Same(p.job.place,place)));
            if(retry!=null)return RetryLabel(retry)+" · "+year;
            return Scheduled(tree,person,year,place)?"QUEUED "+year:null;
        }
        PendingScene Item(string tree,Person person,bool synthetic,int year,string place,bool automatic=false)
            =>new PendingScene{treeId=tree,personId=person.Id,phase=1,isAutomatic=automatic,job=new SceneJob{requestId=Guid.NewGuid().ToString("N"),treeId=tree,personKey=PanoramaCache.PersonKey(person.Id),year=year,date=year.ToString(),place=ResidenceTimeline.Normalize(place),details=SceneYearDetails.Load(storage,tree,person.Id,year),synthetic=synthetic}};
        public void Generate(string treeId,Person person,bool synthetic,int year=0,string place=null,SceneReference photo=null)
        {
            try
            {
                if(!Ready)throw new InvalidOperationException("Scene queue is loading. Try again shortly.");LoadConnection();
                if(year==0&&LifeEndpointScenes.TrySceneRange(person,out int birth,out _))year=birth;
                photo??=ScenePhotoChoices.Load(storage,treeId,person.Id,year,place??person.BirthPlace);
                if(!LifeEndpointScenes.TrySceneRange(person,out int start,out int end)||year<start||year>end)throw new InvalidOperationException("Year is outside the known dates.");
                var pending=Item(treeId,person,synthetic,year,place??person.BirthPlace);
                if(photo!=null){SceneReference.Validate(photo,pending.job.place);if(!photo.IsPhoto||photo.year!=year)throw new InvalidOperationException("Photo belongs to another year.");pending.job.reference=SceneReference.Copy(photo);pending.job.referenceResolved=true;}
                SceneQueueState.Validate(pending);string key=ManualTarget(treeId,person.Id,year);
                bool Match(PendingScene p)=>p.treeId==treeId&&p.personId==person.Id&&p.job.year==year;
                if(manualReplacements.ContainsKey(key)||queue.active.Concat(queue.waiting).Concat(queue.automatic).Concat(workers.Keys).Any(Match))
                {
                    var saved=CancelSceneJobs(treeId,person.Id,year);var token=new object();manualReplacements[key]=token;
                    Notify(person.Id,"Replacing previous "+year+" job…");StartCoroutine(FinishSceneCancellation(saved,treeId,person.Id,year,pending,token,manualPauseVersion));
                }
                else QueueManual(pending);
            }
            catch(Exception e){string message=e is IOException?"Cannot read/write local scene settings.":e.Message;ReportError(treeId,person.Id,year,message);Notify(person.Id,message);}
        }
        public int Enqueue(string tree,Person person,bool synthetic,IEnumerable<(int Year,string Place)> targets,SceneReference photo=null)
        {
            if(!Ready)throw new InvalidOperationException("Scene queue is loading. Try again shortly.");LoadConnection();
            if(!LifeEndpointScenes.TrySceneRange(person,out int birth,out int end))throw new InvalidOperationException("A known birth or death year is required.");
            var requests=new List<PendingScene>();foreach(var target in targets)
            {if(target.Year<birth||target.Year>end)throw new InvalidOperationException("Year is outside the known dates.");var item=Item(tree,person,synthetic,target.Year,target.Place);if(photo!=null){SceneReference.Validate(photo,target.Place);if(!photo.IsPhoto||photo.year!=target.Year)throw new InvalidOperationException("Photo belongs to a different scene request.");item.job.reference=SceneReference.Copy(photo);item.job.referenceResolved=true;}requests.Add(item);}
            int added=queue.Append(requests,null);if(added==0){Notify(person.Id,"Those exact years are already queued.");return 0;}
            openTree=tree;Running=true;Persist();Notify(person.Id,added+" scenes queued.");return added;
        }
        public int RegenerateAll(string tree,IEnumerable<(Person Person,int Year,string Place)> targets,bool synthetic=false)
        {
            if(!Ready)throw new InvalidOperationException("Scene queue is loading. Try again shortly.");LoadConnection();
            var requests=new List<PendingScene>();
            foreach(var target in targets)
            {
                if(!LifeEndpointScenes.TrySceneRange(target.Person,out int birth,out int end)||target.Year<birth||target.Year>end)throw new InvalidOperationException("Year is outside the known dates.");
                var item=Item(tree,target.Person,synthetic||tree==PanoramaCache.DemoTreeId,target.Year,target.Place);item.isBulkRegeneration=true;requests.Add(item);
            }
            int added=queue.AppendRegeneration(requests);openTree=tree;
            if(added>0){Running=true;Persist();}QueueMessage=added+" cached scenes queued for regeneration.";Signal();if(added>0)RefreshWaitingTargets(requests);return added;
        }
        static void ValidatePendingTarget(SceneJob job,int year,string place)
        {if(job==null||job.year!=year||!ResidenceTimeline.Same(job.place,place))throw new InvalidOperationException("A different year/location is pending. Resume its original request first.");}
        public int ScheduleEndpoints(string tree,IEnumerable<Person> people,bool synthetic,Func<Person,ResidenceTimeline> locations,Func<Person,int[]> cached)
        {
            if(!Ready)return 0;openTree=tree;var requests=new List<PendingScene>();
            foreach(var person in people.OrderBy(p=>p.Id,StringComparer.Ordinal))
                foreach(var target in LifeEndpointScenes.Targets(person,locations(person)))
                    if(Array.IndexOf(cached(person),target.Year)<0)requests.Add(Item(tree,person,synthetic,target.Year,target.Place,true));
            int added=queue.AppendAutomatic(requests,null);Persist();
            if(!queue.automaticPaused)Running=true;
            QueueMessage=added+" missing birth/death scenes added"+(queue.automaticPaused?" · queue paused.":".");Signal();if(added>0)RefreshWaitingTargets(requests);return added;
        }
        public void Resume(string treeId){openTree=treeId;if(Ready)Running=!queue.automaticPaused;}
        public void ResumeQueue()
        {
            if(!Ready)return;
            try{LoadConnection();if(persistence.IsFaulted){_=persistence.Exception;persistence=Task.CompletedTask;}openTree??=queue.active.Concat(queue.waiting).Concat(queue.automatic).FirstOrDefault()?.treeId;queue.automaticPaused=false;checkedRequests.Clear();Running=true;Persist();Signal();}
            catch(Exception e){QueueMessage=e.Message;Signal();}
        }
        public void PauseQueue(){if(!Ready)return;manualPauseVersion++;Running=false;queue.automaticPaused=true;Persist();QueueMessage=Busy?"Pausing after active scenes finish.":"Queue paused.";Signal();}
        public void ClearWaiting(){if(!Ready)return;manualPauseVersion++;manualReplacements.Clear();var affected=queue.waiting.Concat(queue.automatic).ToArray();foreach(var p in queue.waiting)checkpointCopies.Remove(p);foreach(var p in queue.automatic)checkpointCopies.Remove(p);queue.waiting.Clear();queue.automatic.Clear();Running=false;queue.automaticPaused=true;Persist();QueueMessage="Waiting scenes removed. Active scenes will finish.";Signal();RefreshWaitingTargets(affected);WaitingCleared?.Invoke();}
        public int ActiveForTree(string tree)=>queue.active.Concat(workers.Keys).Where(p=>p.treeId==tree).Select(p=>p.job.requestId).Distinct().Count();
        public int WaitingForTree(string tree)=>queue.waiting.Count(p=>p.treeId==tree)+queue.automatic.Count(p=>p.treeId==tree);
        public Task CancelTreeJobs(string tree)
            =>CancelJobs(tree,null,0);
        public Task CancelSceneJobs(string tree,string person,int year)
        {if(string.IsNullOrEmpty(person)||year<1||year>9999)throw new InvalidOperationException("Invalid scene cancellation target.");return CancelJobs(tree,person,year);}
        public void CancelScene(string tree,string person,int year)
        {
            try{StartCoroutine(FinishSceneCancellation(CancelSceneJobs(tree,person,year),tree,person,year,null,null));}
            catch(Exception e){ReportError(tree,person,year,e.Message);Notify(person,e.Message);}
        }
        void QueueManual(PendingScene pending,bool resume=true)
        {
            queue.Append(new[]{pending},null);openTree=pending.treeId;if(resume)Running=true;Persist();Notify(pending.personId,"1 scene queued.");
        }
        IEnumerator FinishSceneCancellation(Task saved,string tree,string person,int year,PendingScene replacement,object token,int pauseStamp=0)
        {
            while(!saved.IsCompleted)yield return null;
            if(saved.IsFaulted){_=saved.Exception;string message="Could not save the cancelled-job state. Retry Generate or Cancel.";ReportError(tree,person,year,message);Notify(person,message);yield break;}
            if(replacement==null)yield break;string key=ManualTarget(tree,person,year);
            if(!manualReplacements.TryGetValue(key,out var current)||!ReferenceEquals(current,token))yield break;
            manualReplacements.Remove(key);
            try{QueueManual(replacement,pauseStamp==manualPauseVersion);}catch(Exception e){ReportError(tree,person,year,e.Message);Notify(person,e.Message);}
        }
        Task CancelJobs(string tree,string person,int year)
        {
            if(!Ready)throw new InvalidOperationException("Wait for the scene queue to load.");
            if(persistence.IsFaulted){_=persistence.Exception;persistence=Task.CompletedTask;}
            bool Match(PendingScene p)=>p.treeId==tree&&(person==null||p.personId==person&&p.job.year==year);
            var stopped=workers.Values.Where(w=>Match(w.Pending)).ToArray();
            var removed=person==null?queue.CancelTree(tree,stopped.Select(w=>w.Pending)):queue.CancelScene(tree,person,year,stopped.Select(w=>w.Pending));
            if(person==null){Running=false;foreach(var key in manualReplacements.Keys.Where(k=>k.StartsWith(tree+"/",StringComparison.Ordinal)).ToArray())manualReplacements.Remove(key);}
            else manualReplacements.Remove(ManualTarget(tree,person,year));
            foreach(var worker in stopped)
            {
                var request=worker.Request;worker.Request=null;
                try{request?.Abort();}catch(ObjectDisposedException){}
                if(worker.Coroutine!=null)StopCoroutine(worker.Coroutine);
                (worker.Operation as IDisposable)?.Dispose();worker.Operation=null;
                workers.Remove(worker.Pending);
            }
            // An atomic cache commit already in flight may finish; keep it and await it
            // before switching. Queued/remote work never gets another local save/retry.
            var commits=stopped.Where(w=>w.Commit!=null).Select(w=>w.Commit).ToArray();
            if(commits.Length>0)persistence=Task.WhenAll(new[]{persistence}.Concat(commits));
            foreach(var pending in removed){messages[pending.personId]="Scene job cancelled.";if(pending.phase==1)checkpointCopies.Remove(pending);}
            QueueMessage=removed.Length+" scene job(s) cancelled. Saved scenes kept.";
            var saved=Persist();cancellation=saved;Signal();RefreshWaitingTargets(removed);
            return saved;
        }
        void Update()
        {
            if(!Ready)return;var owner=GetComponent<AncestorGallery>();
#if UNITY_EDITOR
            if(owner!=null&&UnityEditor.SessionState.GetBool("AncestorWalk.EndpointChecks",false))return;
#endif
            if(owner!=null&&!owner.CacheIndexReady)return;
            string tree=owner!=null?owner.TreeId:openTree;if(tree==null)return;
            if(workers.Count==0&&queue.active.Count==MaximumConcurrent&&!queue.active.Any(p=>p.treeId==tree)&&Running)
            {Running=false;QueueMessage="Pending requests in another tree need checking there before new scenes can start.";Signal();return;}
            // Each possibly submitted request gets GET recovery, never another POST.
            for(int i=0;i<queue.active.Count;i++)
            {var p=queue.active[i];if(p.treeId==tree&&!workers.ContainsKey(p)&&checkedRequests.Add(p.job.requestId))Launch(p);}
            // Let submitted work finish, but leave a stable window for browsing/switching trees.
            // This is temporary UI capture: never change the user's persisted Pause preference.
            if(owner!=null&&((owner.Library?.IsOpen??false)||(owner.AISetup?.IsOpen??false)))return;
#if UNITY_EDITOR
            bool directReady=MetaApi.KeyReady||TestConnection!=null||DevelopmentAI.TryConnection(out _);
#else
            bool directReady=MetaApi.KeyReady||DevelopmentAI.TryConnection(out _);
#endif
            if(!directReady){if(QueueMessage!="Open AI Setup and enter your Meta API key."){QueueMessage="Open AI Setup and enter your Meta API key.";Signal();}return;}
            if(!Running||Time.realtimeSinceStartup<dispatchAfter)return;
            while(workers.Count<MaximumConcurrent&&queue.active.Count<MaximumConcurrent)
            {
                var next=queue.Next(tree,Now);if(next==null)break;
                queue.waiting.Remove(next);queue.automatic.Remove(next);queue.active.Add(next);checkedRequests.Add(next.job.requestId);Launch(next);
            }
            if(workers.Count==0&&!queue.active.Any(p=>p.treeId==tree)&&!queue.waiting.Any(p=>p.treeId==tree)&&(!queue.automatic.Any(p=>p.treeId==tree)||queue.automaticPaused))
            {Running=false;QueueMessage="Queue finished · "+CompletedCount+" saved · "+FailedCount+" failed · "+SkippedCount+" already cached.";Signal();}
        }
        void Launch(PendingScene pending){var worker=new Worker{Pending=pending,ProgressLabel="Checking "+pending.job.year+"…"};workers.Add(pending,worker);Notify(pending.personId,worker.ProgressLabel);worker.Coroutine=StartCoroutine(SafeRun(worker));Signal();}
        IEnumerator SafeRun(Worker worker)
        {
            var routine=MetaApi.Flatten(Run(worker));worker.Operation=routine;
            while(true)
            {
                bool more=false;Exception failure=null;try{more=routine.MoveNext();}catch(Exception e){failure=e;}
                if(failure!=null){Running=false;worker.Message=worker.Pending.phase==1?"Scene/reference preparation failed. Check the helper and retry Generate; no image requested.":"Request interrupted. Resume checks the same request without generating again.";break;}
                if(!more)break;yield return routine.Current;
            }
            (routine as IDisposable)?.Dispose();worker.Operation=null;var p=worker.Pending;
            if(worker.Retry!=null){queue.active.Remove(p);(p.isAutomatic?queue.automatic:queue.waiting).Add(worker.Retry);checkedRequests.Remove(p.job.requestId);}
            else if(worker.Requeue){queue.active.Remove(p);p.phase=1;p.startedUnix=0;p.expectedSeconds=0;checkpointCopies.Remove(p);(p.isAutomatic?queue.automatic:queue.waiting).Insert(0,p);checkedRequests.Remove(p.job.requestId);}
            else if(!worker.Keep)queue.active.Remove(p);
            else Running=false;
            if(worker.Saved)
            {
                CompletedCount++;
                if(worker.RecordTiming&&p.startedUnix>0)
                {
                    timings.Record(p.job.reference!=null,Now-p.startedUnix);
                    string timingJson=JsonUtility.ToJson(timings),timingPath=Path.Combine(storage,"generation-timings.json");var before=persistence;
                    persistence=Task.Run(async()=>{await before.ConfigureAwait(false);TreeStore.AtomicText(timingPath,timingJson);});
                }
            }if(worker.Failed)FailedCount++;if(worker.Skipped)SkippedCount++;
            recent.Insert(0,p.job.year+" · "+worker.Message);if(recent.Count>20)recent.RemoveAt(recent.Count-1);
            var save=Persist();while(!save.IsCompleted)yield return null;
            if(save.IsFaulted){_=save.Exception;Running=false;QueueMessage="Queue storage needs attention. Saved request IDs were preserved.";}
            if(!worker.Keep&&!worker.Requeue)checkpointCopies.Remove(p);
            workers.Remove(p);Notify(p.personId,worker.Message);
            if(!worker.Saved&&!worker.Skipped&&!worker.Requeue&&(worker.Failed||worker.Retry!=null||worker.Keep))ReportError(p.treeId,p.personId,p.job.year,worker.Message);
            if(worker.Saved&&(GetComponent<AncestorGallery>()==null||GetComponent<AncestorGallery>().TreeId==p.treeId))
            {
                // Publish exact completion identity before the existing asynchronous index refresh.
                SceneCompleted?.Invoke(p.treeId,p.personId,p.job.year);SceneSaved?.Invoke(p.personId);
            }
        }
        bool CurrentTarget(PendingScene p)
        {
            var owner=GetComponent<AncestorGallery>();if(owner==null)return true;
            if(owner.TreeId!=p.treeId||!owner.Tree.People.TryGetValue(p.personId,out var person))return false;
            if(p.isAutomatic)return LifeEndpointScenes.Targets(person,owner.Locations(person.Id)).Any(x=>x.Year==p.job.year&&ResidenceTimeline.Same(x.Place,p.job.place));
            return LifeEndpointScenes.TrySceneRange(person,out int first,out int last)&&p.job.year>=first&&p.job.year<=last&&ResidenceTimeline.Same(owner.PlaceAt(person.Id,p.job.year),p.job.place);
        }
        IEnumerator PrepareReference(Worker worker,string directory)
        {
            var p=worker.Pending;
            if(p.job.reference?.IsPhoto==true&&!p.job.basePanoramaResolved)
            {
                var owner=GetComponent<AncestorGallery>();while(owner!=null&&owner.ReferenceIndexUpdating(p.personId))yield return null;
                var source=Task.Run(()=>SceneReference.ExactSceneAt(directory,p.treeId,p.personId,p.job.year,p.job.place));
                while(!source.IsCompleted)yield return null;if(source.IsFaulted)throw source.Exception;
                var frozen=JsonUtility.FromJson<SceneJob>(JsonUtility.ToJson(p.job));frozen.basePanoramaResolved=true;
                if(source.Result!=null){frozen.basePanorama=SceneReference.FromPanorama(source.Result);frozen.settingBrief=SceneSettingBrief.Copy(source.Result.settingBrief);frozen.generationMode="photo-insert";}
                p.job=frozen;
            }
            if(!ReferencePanoramasEnabled&&p.phase==1&&p.job.reference?.IsPhoto!=true)
            {
                // Clear even a previously prepared queued anchor; submitted requests bypass this path.
                var independent=JsonUtility.FromJson<SceneJob>(JsonUtility.ToJson(p.job));
                independent.reference=null;independent.referenceResolved=true;independent.generationMode="generate";p.job=independent;
                yield break;
            }
            if(!p.job.referenceResolved)
            {
                var owner=GetComponent<AncestorGallery>();
                // A previous completion may still be refreshing this person's index.
                // Include it before freezing the nearest source; never stall on others.
                while(owner!=null&&owner.ReferenceIndexUpdating(p.personId))yield return null;
                var years=owner!=null?owner.ReferenceYears(p.personId,p.job.place):null;
                var select=Task.Run(()=>SceneReference.NearestAt(directory,p.treeId,p.personId,p.job.year,p.job.place,years));
                while(!select.IsCompleted)yield return null;if(select.IsFaulted)throw select.Exception;
                // Publish a new immutable payload so checkpoint serialization never races mutation.
                var job=JsonUtility.FromJson<SceneJob>(JsonUtility.ToJson(p.job));job.reference=select.Result;job.referenceResolved=true;p.job=job;
            }
            if(p.job.transport==DirectMetaScene.Transport)yield break;
            if(p.job.basePanorama!=null)
            {
                LoadConnection();using(var health=Request(worker,"/health"))
                try{yield return health.SendWebRequest();if(health.result!=UnityWebRequest.Result.Success||(JsonUtility.FromJson<PhotoInsertHealth>(health.downloadHandler.text)?.photoInsertVersion??0)<1)throw new IOException("Update the scene helper for two-image photo edits; no image requested.");}
                finally{worker.Request=null;}
            }
            foreach(var reference in new[]{p.job.basePanorama,p.job.reference})
            {
                if(reference==null)continue;var operation=UploadReference(worker,reference,directory);
                try{while(operation.MoveNext())yield return operation.Current;}finally{(operation as IDisposable)?.Dispose();}
            }
        }
        IEnumerator UploadReference(Worker worker,SceneReference reference,string directory)
        {
            var p=worker.Pending;
            LoadConnection();string path=reference.Route(p.treeId,p.job.personKey);
            while(referenceUploads.TryGetValue(path,out var shared))
            {if(shared.Ready)yield break;if(shared.Done){referenceUploads.Remove(path);break;}yield return null;}
            var upload=new ReferenceUpload();referenceUploads[path]=upload;
            worker.ProgressLabel=reference.IsPhoto?"Preparing selected photo…":"Preparing reference from "+reference.year+"…";Notify(p.personId,worker.ProgressLabel);
            try
            {
                using(var request=Request(worker,path))
                try
                {
                    yield return request.SendWebRequest();
                    if(request.result==UnityWebRequest.Result.Success){CheckReferenceAck(request,reference);upload.Ready=true;yield break;}
                    if(request.responseCode!=404)throw new IOException("Could not check the reference panorama. No image requested.");
                }
                finally{worker.Request=null;}
                string image=reference.ImagePath(directory,p.treeId,p.personId);
                if(!File.Exists(image))throw new IOException("Reference image is missing. Restore its cache before retrying.");
                using(var request=new UnityWebRequest(connection.url+path,"POST"))
                try
                {
                    worker.Request=request;request.uploadHandler=new UploadHandlerFile(image);request.downloadHandler=new DownloadHandlerBuffer();request.timeout=60;request.redirectLimit=0;
                    request.SetRequestHeader("Authorization","Bearer "+connection.token);request.SetRequestHeader("Content-Type",reference.ContentType);
                    yield return request.SendWebRequest();
                    if(request.result!=UnityWebRequest.Result.Success)throw new IOException("Could not prepare the reference panorama. Update/reconnect the helper and retry; no image requested.");
                    CheckReferenceAck(request,reference);upload.Ready=true;
                }
                finally{worker.Request=null;}
            }
            finally{upload.Done=true;if(!upload.Ready)referenceUploads.Remove(path);}
        }
        IEnumerator Run(Worker worker)
        {
            var p=worker.Pending;var job=p.job;string directory=Path.Combine(storage,"Panoramas");
            if(p.phase!=1)BeginProgress(p);
            if(p.phase!=1&&job.transport==DirectMetaScene.Transport)
            {yield return MetaApi.Guard(RunDirect(worker,directory,false),()=>{worker.Keep=false;worker.Failed=true;worker.Message="Direct request interrupted; no automatic replay. Check Meta usage before retrying.";});yield break;}
            if(p.phase==1)
            {
                // Preserve every valid year image, including images with an edited/stale location.
                if(p.isAutomatic||!p.baselineSet||p.retryCount>0)
                {
                    var exists=Task.Run(()=>PanoramaCache.ImageShaAt(directory,p.treeId,p.personId,job.year));while(!exists.IsCompleted)yield return null;
                    if(exists.IsFaulted)throw exists.Exception;
                    if(exists.Result.Length>0&&(p.isAutomatic||p.retryCount>0&&exists.Result!=(p.baselineSha??""))){worker.Keep=false;worker.Skipped=true;worker.Message="Existing image kept for "+job.year+".";yield break;}
                    if(!p.baselineSet){p.baselineSet=true;p.baselineSha=exists.Result;}
                }
                if(!CurrentTarget(p)){worker.Keep=false;worker.Message="Year/location changed; queued snapshot skipped.";if(!p.isAutomatic){Running=false;QueueMessage=worker.Message;}yield break;}
                if(!Running){worker.Requeue=true;worker.Message="Unsubmitted request paused.";yield break;}
                LoadConnection();
                string transport=connection==null?DirectMetaScene.Transport:DevelopmentAI.Transport;
                if(p.job.transport!=transport){var selected=JsonUtility.FromJson<SceneJob>(JsonUtility.ToJson(p.job));selected.transport=transport;p.job=selected;}
                var prepare=PrepareReference(worker,directory);
                try{while(prepare.MoveNext())yield return prepare.Current;}finally{(prepare as IDisposable)?.Dispose();}
                job=p.job;
                if(!Running){worker.Requeue=true;worker.Message="Unsubmitted request paused.";yield break;}
                LoadConnection();p.phase=2;worker.RecordTiming=true;BeginProgress(p);var checkpoint=Persist();while(!checkpoint.IsCompleted)yield return null;
                if(checkpoint.IsFaulted)throw checkpoint.Exception;
                if(!Running){worker.Requeue=true;worker.Message="Unsubmitted request paused.";yield break;}
                if(job.transport==DirectMetaScene.Transport)
                {yield return MetaApi.Guard(RunDirect(worker,directory,true),()=>{worker.Keep=false;worker.Failed=true;worker.Message="Direct request interrupted; no automatic replay. Check Meta usage before retrying.";});yield break;}
                worker.ProgressLabel="Preparing "+job.year+"…";Notify(p.personId,worker.ProgressLabel+" · "+job.place);
                using(var request=Request(worker,"/jobs",JsonUtility.ToJson(job)))
                try
                {
                    yield return request.SendWebRequest();
                    if(request.result!=UnityWebRequest.Result.Success)
                    {
                        if(request.responseCode==429||request.responseCode==409)
                        {worker.Requeue=true;float.TryParse(request.GetResponseHeader("Retry-After"),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out float delay);dispatchAfter=Mathf.Max(dispatchAfter,Time.realtimeSinceStartup+Mathf.Max(1,delay));worker.Message="Waiting for helper capacity/rate limit.";yield break;}
                        if(request.responseCode==401){worker.Requeue=true;Running=false;worker.Message="Reconnect to the Mac helper, then resume.";yield break;}
                        if(request.responseCode==400){if(job.reference!=null)referenceUploads.Remove(job.reference.Route(p.treeId,job.personKey));worker.Keep=false;worker.Failed=true;worker.Message="Invalid scene request.";yield break;}
                        Running=false;worker.Message="Submission uncertain. Resume checks this request without another generation.";yield break;
                    }
                }
                finally{worker.Request=null;}
            }
            LoadHelperConnection();float deadline=Time.realtimeSinceStartup+480;
            while(Time.realtimeSinceStartup<deadline)
            {
                SceneJob result=null;
                using(var request=Request(worker,"/jobs/"+job.requestId))
                try
                {
                    yield return request.SendWebRequest();
                    if(request.result!=UnityWebRequest.Result.Success)
                    {worker.Message=request.responseCode==404?"Request not found; retry manually.":"Connection lost; resume to check.";worker.Keep=request.responseCode!=404;worker.Failed=!worker.Keep;yield break;}
                    result=JsonUtility.FromJson<SceneJob>(request.downloadHandler.text);
                }
                finally{worker.Request=null;}
                if(!MatchesHelperResult(result,job))
                {worker.Message="Scene identity mismatch; resume to check.";yield break;}
                if(result.status=="rate_limited")
                {worker.Requeue=true;dispatchAfter=Mathf.Max(dispatchAfter,Time.realtimeSinceStartup+Mathf.Max(1,result.retryAfter));worker.Message="Meta rate limit; waiting before resubmitting the rejected request.";yield break;}
                if(result.status=="failed")
                {
                    worker.Retry=SceneRetryPolicy.Next(p,result,Now);worker.Keep=false;worker.Failed=worker.Retry==null;
                    worker.Message=worker.Retry!=null?"Retry "+worker.Retry.retryCount+"/"+SceneRetryPolicy.MaximumRetries+" for "+job.year+" in "+SceneRetryPolicy.DelaySeconds(worker.Retry.retryCount)+"s · "+result.error:"Generation failed for "+job.year+". "+result.error;
                    yield break;
                }
                if(result.status=="generating")
                {
                    string label=(result.stage=="researching"?"Researching ":result.stage=="waiting_research"?"Waiting for shared research ":result.stage=="preparing"?"Preparing ":result.stage=="editing"?"Editing ":"Generating ")+job.year+(result.stage=="editing"&&job.reference!=null?" from "+job.reference.year:"")+"…";
                    if(job.policyRetryLevel>0)label="Simpler scene retry "+p.retryCount+"/"+SceneRetryPolicy.MaximumRetries+" · "+label;
                    if(worker.ProgressLabel!=label){worker.ProgressLabel=label;Notify(p.personId,label+" · "+job.place);}
                }
                if(result.status=="complete")
                {
                    worker.ProgressLabel="Saving "+job.year+"…";Notify(p.personId,worker.ProgressLabel);
                    using(var request=Request(worker,"/jobs/"+job.requestId+"/image",image:true))
                    try
                    {
                        yield return request.SendWebRequest();if(request.result!=UnityWebRequest.Result.Success){worker.Message="Download failed; resume retries the download only.";yield break;}
                        while(!PanoramaFrameBudget.TryTake())yield return null;
                        Texture2D decoded=null;byte[] bytes=null;int width=0,height=0;
                        try{decoded=DownloadHandlerTexture.GetContent(request);bytes=request.downloadHandler.data;width=decoded.width;height=decoded.height;}
                        finally{if(decoded!=null)Destroy(decoded);}
                        var save=Task.Run(()=>p.isAutomatic?PanoramaCache.SaveDecodedIfMissingAt(directory,p.treeId,p.personId,job.year,bytes,result,width,height):SaveManual(directory,p,bytes,result,width,height));worker.Commit=save;
                        while(!save.IsCompleted)yield return null;if(save.IsFaulted)throw save.Exception;
                        worker.Keep=false;worker.Saved=save.Result;worker.Skipped=!save.Result;worker.Message=save.Result?"Scene saved for "+job.year+".":"Existing image kept for "+job.year+".";yield break;
                    }
                    finally{worker.Request=null;}
                }
                yield return new WaitForSecondsRealtime(2);
            }
            worker.Message="Still pending. Resume checks the existing request.";
        }
        IEnumerator RunDirect(Worker worker,string directory,bool submit)
        {
            var p=worker.Pending;DirectSceneResult result=null;
            if(submit)
            {
                result=new DirectSceneResult();yield return DirectMetaScene.GenerateFor(p.personId,storage,directory,p.job,result,label=>{worker.ProgressLabel=label;Notify(p.personId,label+" · "+p.job.place);},request=>worker.Request=request);
                if(result.Job?.status=="failed")
                {
                    worker.Retry=SceneRetryPolicy.Next(p,result.Job,Now);worker.Keep=false;worker.Failed=worker.Retry==null;worker.Message=result.Job.error;
                    if(result.Job.failureCode=="image_http_429")dispatchAfter=Mathf.Max(dispatchAfter,Time.realtimeSinceStartup+Mathf.Max(1,result.Job.retryAfter));yield break;
                }
            }
            else
            {
                var recover=Task.Run(()=>DirectMetaScene.Recover(storage,p.job));while(!recover.IsCompleted)yield return null;
                if(recover.IsFaulted){_=recover.Exception;worker.Keep=false;worker.Failed=true;worker.Message="Saved direct result is invalid. No request was resubmitted.";yield break;}result=recover.Result;
            }
            if(result?.Png==null)
            {
                var exists=Task.Run(()=>PanoramaCache.ImageShaAt(directory,p.treeId,p.personId,p.job.year));while(!exists.IsCompleted)yield return null;
                if(exists.IsFaulted)_=exists.Exception;
                if(exists.Status==TaskStatus.RanToCompletion&&exists.Result.Length>0&&(p.isAutomatic||p.baselineSet&&exists.Result!=p.baselineSha)){worker.Keep=false;worker.Skipped=true;worker.Message="Previously saved image kept for "+p.job.year+".";yield break;}
                worker.Keep=false;worker.Failed=true;worker.Message="Direct request outcome is unknown. No automatic replay; check Meta usage before generating again.";yield break;
            }
            worker.ProgressLabel="Saving "+p.job.year+"…";Notify(p.personId,worker.ProgressLabel);while(!PanoramaFrameBudget.TryTake())yield return null;
            var save=Task.Run(()=>p.isAutomatic?PanoramaCache.SaveDecodedIfMissingAt(directory,p.treeId,p.personId,p.job.year,result.Png,result.Job,result.Width,result.Height):SaveManual(directory,p,result.Png,result.Job,result.Width,result.Height));worker.Commit=save;
            while(!save.IsCompleted)yield return null;if(save.IsFaulted)throw save.Exception;
            worker.Keep=false;worker.Saved=save.Result;worker.Skipped=!save.Result;worker.Message=save.Result?"Scene saved for "+p.job.year+".":"Existing image kept for "+p.job.year+".";
            var cleanup=Task.Run(()=>DirectMetaScene.Cleanup(storage,p.job.requestId));while(!cleanup.IsCompleted)yield return null;if(cleanup.IsFaulted)_=cleanup.Exception;
        }
        static bool SaveManual(string directory,PendingScene p,byte[] png,SceneJob result,int width,int height)
        {if(p.baselineSet)return PanoramaCache.SaveDecodedIfUnchangedAt(directory,p.treeId,p.personId,p.job.year,p.baselineSha,png,result,width,height);PanoramaCache.SaveDecodedAt(directory,p.treeId,p.personId,p.job.year,png,result,width,height);return true;}
        void LoadConnection()
        {
#if UNITY_EDITOR
            if(TestConnection!=null){connection=TestConnection;return;}
#endif
            connection=DevelopmentAI.Resolve();
        }
        void LoadHelperConnection(){
#if UNITY_EDITOR
            connection=TestConnection??ReadConnection();
#else
            connection=ReadConnection();
#endif
        }
        public static SceneServiceConnection ReadConnection()
        {
            string path=Path.Combine(PanoramaCache.StorageRoot,"scene-service.json");
            if(!File.Exists(path))throw new InvalidOperationException("Start the Mac scene helper first.");
            var connection=JsonUtility.FromJson<SceneServiceConnection>(File.ReadAllText(path));
            return ValidateConnection(connection);
        }
        public static SceneServiceConnection ValidateConnection(SceneServiceConnection connection)
        {
            if(connection==null||string.IsNullOrWhiteSpace(connection.token)||connection.token.Length!=64||!Uri.TryCreate(connection.url,UriKind.Absolute,out var uri))
                throw new InvalidOperationException("Scene helper connection needs setup.");
            if(uri.Scheme!="http"||!IPAddress.TryParse(uri.Host,out var address)||uri.Port!=8787||uri.AbsolutePath!="/"||uri.UserInfo!=""||uri.Query!=""||uri.Fragment!="")
                throw new InvalidOperationException("Only the local development helper is supported.");
            var bytes=address.GetAddressBytes();
            if(bytes.Length!=4 || !(bytes[0]==127||bytes[0]==10||(bytes[0]==192&&bytes[1]==168)||(bytes[0]==172&&bytes[1]>=16&&bytes[1]<=31)))
                throw new InvalidOperationException("Helper must be on the local network.");
            if(!Application.isEditor&&!Debug.isDebugBuild)throw new InvalidOperationException("Scene generation requires a development build.");
            connection.url=connection.url.TrimEnd('/');
            return connection;
        }
        UnityWebRequest Request(Worker worker,string path,string json=null,bool image=false)
        {
            var request=new UnityWebRequest(connection.url+path,json==null?"GET":"POST");
            request.downloadHandler=image?(DownloadHandler)new DownloadHandlerTexture(false):new DownloadHandlerBuffer();request.timeout=15;request.redirectLimit=0;
            request.SetRequestHeader("Authorization","Bearer "+connection.token);
            if(json!=null){request.uploadHandler=new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));request.SetRequestHeader("Content-Type","application/json");}
            worker.Request=request;return request;
        }
        void OnDestroy()
        {
            foreach(var worker in workers.Values)
            {
                // Completed request scopes clear this reference before disposing their native handle.
                var request=worker.Request;worker.Request=null;
                try{request?.Abort();}catch(ObjectDisposedException){}
                finally{(worker.Operation as IDisposable)?.Dispose();worker.Operation=null;request?.Dispose();}
            }
        }
    }
}
