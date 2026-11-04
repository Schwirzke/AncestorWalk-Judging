using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace AncestorWalk
{
    [Serializable] public sealed class PendingScene
    {
        public string treeId,personId;public SceneJob job;public int phase;public bool isAutomatic,isBulkRegeneration;
        public int retryCount;public double notBeforeUnix;
        public double startedUnix,expectedSeconds;public long queuePriority;public bool baselineSet;public string baselineSha;
    } // 0 legacy/unknown, 1 not submitted, 2 may have been submitted
    public static class SceneRetryPolicy
    {
        public const int MaximumRetries=3;
        public static double UnixNow=>(DateTime.UtcNow-new DateTime(1970,1,1)).TotalSeconds;
        public static int DelaySeconds(int retry)=>15*(1<<Math.Max(0,Math.Min(MaximumRetries,retry)-1));
        public static PendingScene Next(PendingScene p,SceneJob failure,double now)
        {
            bool policy=ScenePromptRetry.IsPromptRefusal(failure);
            if(failure?.status!="failed"||(!failure.retryable&&!policy)||p.retryCount>=MaximumRetries||failure.failureCode=="image_uncertain")return null;
            int retry=p.retryCount+1;
            return new PendingScene{treeId=p.treeId,personId=p.personId,isAutomatic=p.isAutomatic,isBulkRegeneration=p.isBulkRegeneration,phase=1,retryCount=retry,queuePriority=p.queuePriority,notBeforeUnix=now+DelaySeconds(retry),baselineSet=p.baselineSet,baselineSha=p.baselineSha,
                job=new SceneJob{requestId=Guid.NewGuid().ToString("N"),treeId=p.job.treeId,personKey=p.job.personKey,year=p.job.year,date=p.job.date,place=p.job.place,details=p.job.details,synthetic=p.job.synthetic,horizonLift=p.job.horizonLift,transport=p.job.transport,policyRetryLevel=policy?Math.Min(ScenePromptRetry.MaximumLevel,p.job.policyRetryLevel+1):p.job.policyRetryLevel,referenceResolved=p.job.referenceResolved,reference=SceneReference.Copy(p.job.reference),basePanorama=SceneReference.Copy(p.job.basePanorama),basePanoramaResolved=p.job.basePanoramaResolved,settingBrief=SceneSettingBrief.Copy(policy?failure.settingBrief??p.job.settingBrief:p.job.settingBrief??failure.settingBrief),generationMode=p.job.generationMode}};
        }
    }
    [Serializable] public sealed class SceneQueueState
    {
        public int version=2;public List<PendingScene> waiting=new();
        public List<PendingScene> active=new();
        public List<PendingScene> cancelled=new();
        public List<PendingScene> automatic=new();
        public List<string> automaticSeen=new();
        public bool automaticPaused;
        public const int MaximumPending=20;
        public const int MaximumAutomatic=4000;
        public const int MaximumCancelled=4000;
        public static string TargetKey(PendingScene item)=>PanoramaCache.Hash(System.Text.Encoding.UTF8.GetBytes(item.treeId+"\n"+item.personId+"\n"+item.job.year+"\n"+ResidenceTimeline.Normalize(item.job.place).ToUpperInvariant()));
        public static bool SameYear(PendingScene a,PendingScene b)=>a.treeId==b.treeId&&a.personId==b.personId&&a.job.year==b.job.year;
        public static bool SameTarget(PendingScene a,PendingScene b)=>SameYear(a,b)&&(a.isAutomatic||b.isAutomatic||ResidenceTimeline.Same(a.job.place,b.job.place));
        public static void Validate(PendingScene item,bool legacy=false)
        {
            if(item?.job==null||string.IsNullOrWhiteSpace(item.personId)||item.personId.Length>200||item.phase<0||item.phase>2)throw new InvalidOperationException("Invalid queued scene.");
            if(item.queuePriority<0||item.retryCount<0||item.retryCount>SceneRetryPolicy.MaximumRetries||double.IsNaN(item.notBeforeUnix)||double.IsInfinity(item.notBeforeUnix)||item.notBeforeUnix<0||item.notBeforeUnix>253402300799||!string.IsNullOrEmpty(item.baselineSha)&&(!item.baselineSet||!Regex.IsMatch(item.baselineSha,@"\A[a-f0-9]{64}\z")))throw new InvalidOperationException("Invalid scene retry checkpoint.");
            if(item.isAutomatic&&item.isBulkRegeneration)throw new InvalidOperationException("Regeneration cannot be automatic.");
            TreeStore.CheckId(item.treeId);TreeStore.CheckId(item.job.personKey);
            if((item.job.details?.Length??0)>SceneYearDetails.MaximumLength)throw new InvalidOperationException("Invalid scene details.");
            if(item.job.policyRetryLevel<0||item.job.policyRetryLevel>ScenePromptRetry.MaximumLevel||item.job.policyRetryLevel>item.retryCount)throw new InvalidOperationException("Invalid prompt retry checkpoint.");
            item.job.reference=SceneReference.Optional(item.job.reference);SceneReference.Validate(item.job.reference,item.job.place);
            item.job.basePanorama=SceneReference.Optional(item.job.basePanorama);SceneReference.Validate(item.job.basePanorama,item.job.place);
            if(item.job.basePanorama!=null&&(item.job.basePanorama.IsPhoto||item.job.reference?.IsPhoto!=true||!item.job.basePanoramaResolved||item.job.basePanorama.year!=item.job.year||item.job.reference.year!=item.job.year))throw new InvalidOperationException("Invalid photo-edit base panorama.");
            if(item.job.reference!=null&&!item.job.referenceResolved)throw new InvalidOperationException("Reference panorama was not resolved.");
            if(item.job.treeId!=item.treeId||item.job.personKey!=PanoramaCache.PersonKey(item.personId)||!Regex.IsMatch(item.job.requestId??"",@"\A[a-f0-9]{32}\z")||(!legacy||item.job.year!=0)&&(item.job.year<1||item.job.year>9999||item.job.date!=item.job.year.ToString())||string.IsNullOrWhiteSpace(item.job.place)||item.job.place.Length>ResidenceTimeline.MaximumLocationLength)throw new InvalidOperationException("Invalid queued scene identity or year.");
        }
        public void Validate()
        {
            automatic??=new();automaticSeen??=new();active??=new();cancelled??=new();
            if((version!=1&&version!=2)||waiting==null||waiting.Count>MaximumPending+MaximumAutomatic||waiting.Count(p=>!p.isBulkRegeneration)>MaximumPending||waiting.Count(p=>p.isBulkRegeneration)>MaximumAutomatic||active.Count>SceneGeneration.MaximumConcurrent)throw new InvalidOperationException("Invalid scene queue.");
            if(automatic.Count>MaximumAutomatic||automaticSeen.Count>MaximumAutomatic*2)throw new InvalidOperationException("Automatic endpoint queue is too large.");
            if(cancelled.Count>MaximumCancelled)throw new InvalidOperationException("Cancelled scene history is full.");
            foreach(string key in automaticSeen)TreeStore.CheckId(key);
            var requests=new HashSet<string>();foreach(var item in waiting.Concat(automatic).Concat(active).Concat(cancelled)){Validate(item,active.Contains(item)||cancelled.Contains(item));if(!requests.Add(item.job.requestId))throw new InvalidOperationException("Duplicate queued request ID.");}
        }
        public PendingScene Queued(string tree,string person,int year)=>waiting.Concat(automatic).FirstOrDefault(p=>p.treeId==tree&&p.personId==person&&p.job.year==year);
        public bool Promote(string request)
        {
            var target=waiting.Concat(automatic).FirstOrDefault(p=>p.job.requestId==request);if(target==null)return false;
            long maximum=waiting.Concat(automatic).Concat(active).Select(p=>p.queuePriority).DefaultIfEmpty(0).Max();
            if(maximum==long.MaxValue)return false;target.queuePriority=maximum+1;return true;
        }
        public PendingScene Next(string tree,double now)
        {
            bool Eligible(PendingScene p)=>p.treeId==tree&&p.notBeforeUnix<=now&&!active.Any(a=>SameYear(a,p));
            var priority=waiting.Concat(automaticPaused?Enumerable.Empty<PendingScene>():automatic).Where(p=>p.queuePriority>0&&Eligible(p)).OrderByDescending(p=>p.queuePriority).FirstOrDefault();
            return priority??waiting.FirstOrDefault(p=>!p.isBulkRegeneration&&Eligible(p))??waiting.FirstOrDefault(p=>p.isBulkRegeneration&&Eligible(p))??(automaticPaused?null:automatic.FirstOrDefault(Eligible));
        }
        public PendingScene[] CancelTree(string tree,IEnumerable<PendingScene> workers=null)
        {
            TreeStore.CheckId(tree);
            return CancelWhere(p=>p.treeId==tree,workers);
        }
        public PendingScene[] CancelScene(string tree,string person,int year,IEnumerable<PendingScene> workers=null)
        {
            TreeStore.CheckId(tree);if(string.IsNullOrEmpty(person)||year<1||year>9999)throw new InvalidOperationException("Invalid scene cancellation target.");
            return CancelWhere(p=>p.treeId==tree&&p.personId==person&&p.job.year==year,workers);
        }
        PendingScene[] CancelWhere(Predicate<PendingScene> match,IEnumerable<PendingScene> workers)
        {
            var removed=waiting.Concat(automatic).Concat(active).Concat(workers??Array.Empty<PendingScene>()).Where(p=>match(p)).GroupBy(p=>p.job.requestId).Select(g=>g.First()).ToArray();
            var history=cancelled.Concat(removed.Where(p=>p.phase!=1)).GroupBy(p=>p.job.requestId).Select(g=>g.First()).ToList();
            var seen=automaticSeen.Concat(removed.Select(TargetKey)).Distinct().ToList();
            if(history.Count>MaximumCancelled||seen.Count>MaximumAutomatic*2)throw new InvalidOperationException("Scene history is full; jobs were left unchanged.");
            waiting.RemoveAll(match);automatic.RemoveAll(match);active.RemoveAll(match);
            cancelled=history;automaticSeen=seen;return removed;
        }
        public int Append(IEnumerable<PendingScene> requests,PendingScene active)
        {
            var additions=new List<PendingScene>();foreach(var request in requests)
            {Validate(request);if(active!=null&&SameTarget(active,request)||waiting.Concat(automatic).Concat(this.active).Any(x=>SameTarget(x,request))||additions.Any(x=>SameTarget(x,request)))continue;additions.Add(request);}
            if(waiting.Count(x=>!x.isBulkRegeneration)+additions.Count+this.active.Count(x=>!x.isAutomatic&&!x.isBulkRegeneration)+(active==null||this.active.Contains(active)?0:1)>MaximumPending)throw new InvalidOperationException("Queue at most 20 scenes at a time.");
            waiting.AddRange(additions);return additions.Count;
        }
        public int AppendRegeneration(IEnumerable<PendingScene> requests)
        {
            var additions=new List<PendingScene>();
            foreach(var request in requests)
            {
                Validate(request);if(request.isAutomatic||!request.isBulkRegeneration)throw new InvalidOperationException("Invalid regeneration request.");
                if(waiting.Concat(automatic).Concat(active).Concat(additions).Any(x=>SameYear(x,request)))continue;
                additions.Add(request);
            }
            if(waiting.Count(p=>p.isBulkRegeneration)+active.Count(p=>p.isBulkRegeneration)+additions.Count>MaximumAutomatic)throw new InvalidOperationException("Regenerate at most 4,000 cached scenes at a time.");
            waiting.AddRange(additions);return additions.Count;
        }
        public int AppendAutomatic(IEnumerable<PendingScene> requests,PendingScene active)
        {
            var seen=new HashSet<string>(automaticSeen);var additions=new List<PendingScene>();
            foreach(var request in requests)
            {
                Validate(request);if(!seen.Add(TargetKey(request)))continue;
                if(active!=null&&SameTarget(active,request)||waiting.Concat(automatic).Concat(this.active).Concat(additions).Any(x=>SameTarget(x,request)))continue;
                additions.Add(request);
            }
            if(automatic.Count+additions.Count>MaximumAutomatic||seen.Count>MaximumAutomatic*2)throw new InvalidOperationException("Automatic endpoint queue is full.");
            automaticSeen=seen.ToList();automatic.AddRange(additions);return additions.Count;
        }
        public static int[] ParseYears(string text,int birth,int end)
        {
            var result=new SortedSet<int>();if(string.IsNullOrWhiteSpace(text))return Array.Empty<int>();
            if(text.Length>500)throw new InvalidOperationException("Year list is too long.");
            foreach(string token in text.Split(','))
            {
                var match=Regex.Match(token.Trim(),@"\A(\d{1,4})(?:\s*-\s*(\d{1,4})(?:\s*/\s*(\d{1,4}))?)?\z");
                if(!match.Success)throw new InvalidOperationException("Use years such as 1934, 1936 or 1900-1940/10.");
                int first=int.Parse(match.Groups[1].Value),last=match.Groups[2].Success?int.Parse(match.Groups[2].Value):first,step=match.Groups[3].Success?int.Parse(match.Groups[3].Value):1;
                if(first<birth||last>end||first>last||step<1)throw new InvalidOperationException("Choose years within "+birth+"–"+end+" and a positive interval.");
                for(int year=first;year<=last;year+=step){result.Add(year);if(result.Count>MaximumPending)throw new InvalidOperationException("Choose at most 20 exact years per batch.");}
            }
            return result.ToArray();
        }
    }
}
