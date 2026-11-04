using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
namespace AncestorWalk.Editor
{
    public static class SceneCancelChecks
    {
        const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        const string Tree="aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const string Report="Artifacts/scene-cancel-checks.txt";
        static int count;
        static void Check(bool ok,string why){if(!ok)throw new Exception(why);count++;}
        static PendingScene Item(string id,int year,int phase=1)=>new PendingScene{treeId=Tree,personId=id,phase=phase,job=new SceneJob{requestId=Guid.NewGuid().ToString("N"),treeId=Tree,personKey=PanoramaCache.PersonKey(id),year=year,date=year.ToString(),place="York, England"}};
        public static string Run()
        {
            var g=UnityEngine.Object.FindAnyObjectByType<AncestorGallery>();if(!Application.isPlaying||g==null||g.Generation.ActiveCount>0)throw new Exception("Idle Play required");
            count=0;File.WriteAllText(Report,"RUNNING");g.StartCoroutine(Safe(Checks(g)));return "Isolated scene cancellation checks started; no API calls";
        }
        static IEnumerator Safe(IEnumerator first)
        {
            var routine=MetaApi.Flatten(first);try{while(true){bool next;try{next=routine.MoveNext();}catch(Exception e){File.WriteAllText(Report,"FAILED: "+e);yield break;}if(!next)break;yield return routine.Current;}}
            finally{(routine as IDisposable)?.Dispose();}
        }
        static IEnumerator Checks(AncestorGallery g)
        {
            var state=new SceneQueueState{automaticPaused=true};var sent=Item("one",1900,2);var queued=Item("one",1900);var retry=Item("one",1900);retry.retryCount=1;retry.notBeforeUnix=SceneRetryPolicy.UnixNow+30;
            var otherYear=Item("one",1901);var otherPerson=Item("two",1900);state.active.Add(sent);state.waiting.AddRange(new[]{queued,retry,otherYear,otherPerson});
            var removed=state.CancelScene(Tree,"one",1900);Check(removed.Length==3,"Cancel includes active, queued and delayed retry");Check(state.waiting.SequenceEqual(new[]{otherYear,otherPerson}),"Other years and people survive");Check(state.cancelled.Single()==sent&&state.automaticPaused,"Submitted identity and Pause retained");
            var restored=JsonUtility.FromJson<SceneQueueState>(JsonUtility.ToJson(state));restored.Validate();Check(restored.cancelled.Single().job.requestId==sent.job.requestId,"Cancelled identity survives restart");Check(restored.CancelScene(Tree,"one",1900).Length==0,"Repeated cancel is harmless");
            string root=Path.GetFullPath("Artifacts/SceneCancelChecks/"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            var obj=new GameObject("Isolated scoped cancellation");obj.SetActive(false);var gen=obj.AddComponent<SceneGeneration>();typeof(SceneGeneration).GetField("TestStorage",Private).SetValue(gen,root);
            string key=(string)typeof(MetaApi).GetField("sessionKey",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);var reply=MetaApi.TestReply;bool guard=SessionState.GetBool("AncestorWalk.EndpointChecks",false);TaskCompletionSource<bool> commit=null;
            try
            {
                SessionState.SetBool("AncestorWalk.EndpointChecks",true);MetaApi.UseKey("fixture-cancel-key");MetaApi.TestReply=(p,b)=>throw new Exception("No request should dispatch while paused");obj.SetActive(true);
                float deadline=Time.realtimeSinceStartup+15;while(!gen.Ready&&Time.realtimeSinceStartup<deadline)yield return null;Check(gen.Ready,"Isolated queue loads");
                var q=(SceneQueueState)typeof(SceneGeneration).GetField("queue",Private).GetValue(gen);var workers=(IDictionary)typeof(SceneGeneration).GetField("workers",Private).GetValue(gen);var wt=typeof(SceneGeneration).GetNestedType("Worker",BindingFlags.NonPublic);
                object AddWorker(PendingScene p,Task task=null){q.active.Add(p);var w=Activator.CreateInstance(wt,true);wt.GetField("Pending").SetValue(w,p);wt.GetField("Commit").SetValue(w,task);workers.Add(p,w);return w;}
                var person=new Person{Id="one",BirthDate="1900",DeathDate="1905"};var old=Item("one",1900,2);commit=new TaskCompletionSource<bool>();AddWorker(old,commit.Task);var unrelated=Item("two",1901,2);AddWorker(unrelated);
                SceneYearDetails.Save(root,Tree,person.Id,1900,"First directions");gen.Generate(Tree,person,true,1900,"York, England");
                SceneYearDetails.Save(root,Tree,person.Id,1900,"Latest directions");gen.Generate(Tree,person,true,1900,"York, England");gen.PauseQueue();
                Check(workers.Contains(unrelated)&&!workers.Contains(old),"Scoped replacement leaves unrelated worker running");Check(!q.waiting.Any(p=>p.personId=="one"),"Replacement waits for in-flight atomic save");
                commit.SetResult(true);while(!q.waiting.Any(p=>p.personId=="one")&&Time.realtimeSinceStartup<deadline)yield return null;
                var replacement=q.waiting.Single(p=>p.personId=="one");Check(replacement.job.details=="Latest directions"&&replacement.job.requestId!=old.job.requestId,"Latest click wins with a new immutable snapshot");Check(!gen.Running&&gen.QueuePaused,"Pause during replacement is respected");Check(q.cancelled.Any(p=>p.job.requestId==old.job.requestId),"Old request cannot recover into a save");
                yield return Wait(gen.CancelSceneJobs(Tree,"one",1900));Check(!q.waiting.Any(p=>p.personId=="one")&&workers.Contains(unrelated),"Cancel stops replacement and preserves other work");
                var oldAgain=Item("one",1902,2);commit=new TaskCompletionSource<bool>();AddWorker(oldAgain,commit.Task);gen.Generate(Tree,person,true,1902,"York, England");var cancelled=gen.CancelSceneJobs(Tree,"one",1902);commit.SetResult(true);yield return Wait(cancelled);yield return null;
                Check(!q.waiting.Any(p=>p.personId=="one"&&p.job.year==1902),"Cancel invalidates a replacement awaiting commit");
                string selected=g.SelectedId;g.Select(selected??g.Tree.People.Keys.First());string id=g.SelectedId;int year=g.Year(id);var generationProperty=typeof(AncestorGallery).GetField("<Generation>k__BackingField",Private);var real=g.Generation;
                var target=Item(id,year);target.treeId=target.job.treeId=g.TreeId;target.retryCount=1;target.notBeforeUnix=SceneRetryPolicy.UnixNow+30;q.waiting.Add(target);
                try
                {
                    generationProperty.SetValue(g,gen);typeof(SceneGeneration).GetMethod("Signal",Private).Invoke(gen,null);typeof(HudOverlay).GetMethod("UpdateGenerationProgress",Private).Invoke(g.Hud,new object[]{false});
                    var cancel=(RectTransform)typeof(HudOverlay).GetField("cancelQueued",Private).GetValue(g.Hud);var panel=(RectTransform)typeof(HudOverlay).GetField("queuedProgress",Private).GetValue(g.Hud);Check(UIFade.Visible(panel.gameObject),"Retry countdown panel opens");
                    Check(g.Hud.HandleInput(new Ray(cancel.position-cancel.forward,cancel.forward),true,true,false,false),"Cancel button accepts controller ray");Check(!q.waiting.Contains(target),"Countdown Cancel removes delayed retry");Check(workers.Contains(unrelated),"Countdown Cancel does not affect other workers");
                    var active=Item(id,year,2);active.treeId=active.job.treeId=g.TreeId;var activeWorker=AddWorker(active);wt.GetField("ProgressLabel").SetValue(activeWorker,"Generating fixture scene…");
                    typeof(SceneGeneration).GetMethod("Signal",Private).Invoke(gen,null);typeof(HudOverlay).GetMethod("UpdateGenerationProgress",Private).Invoke(g.Hud,new object[]{false});
                    var activePanel=(RectTransform)typeof(HudOverlay).GetField("generationProgress",Private).GetValue(g.Hud);var stop=(RectTransform)typeof(HudOverlay).GetField("cancelActive",Private).GetValue(g.Hud);
                    Check(UIFade.Visible(activePanel.gameObject),"Active progress panel opens");var corners=new Vector3[4];stop.GetWorldCorners(corners);foreach(var corner in corners)Check(activePanel.rect.Contains(activePanel.InverseTransformPoint(corner)),"Active Cancel is inside progress panel");
                    Check(g.Hud.HandleInput(new Ray(stop.position-stop.forward,stop.forward),true,true,false,false),"Active Cancel accepts controller ray");
                    Check(!workers.Contains(active)&&!q.active.Contains(active)&&!q.waiting.Any(p=>p.job.requestId==active.job.requestId),"Active Cancel stops without regenerating");Check(workers.Contains(unrelated),"Active Cancel preserves unrelated job");
                }
                finally{generationProperty.SetValue(g,real);typeof(HudOverlay).GetMethod("UpdateGenerationProgress",Private).Invoke(g.Hud,new object[]{false});if(selected==null)g.ShowTree();else g.Select(selected);}
                yield return Wait(gen.CancelTreeJobs(Tree));Check(gen.ActiveCount==0,"Fixture cleanup finishes");Check(TreeSwitchChecks.Data().Contains("passed"),"Existing tree cancellation policy preserved");
                File.WriteAllText(Report,count+" scoped cancellation checks passed; active/queued/retry targets, unrelated jobs, immutable restart, repeated clicks, delayed commit, Pause, restart history and countdown Cancel. No external calls or family writes.");
            }
            finally{commit?.TrySetResult(true);MetaApi.TestReply=reply;if(string.IsNullOrEmpty(key))MetaApi.ForgetKey();else MetaApi.UseKey(key);SessionState.SetBool("AncestorWalk.EndpointChecks",guard);UnityEngine.Object.Destroy(obj);}
        }
        static IEnumerator Wait(Task task){while(!task.IsCompleted)yield return null;if(task.IsFaulted)throw task.Exception.GetBaseException();}
    }
}
