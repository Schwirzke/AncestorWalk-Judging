using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace AncestorWalk.Editor
{
    public static class RequestLifetimeChecks
    {
        const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        public static string Run()
        {
            string report="Artifacts/request-lifetime-checks.txt";Directory.CreateDirectory("Artifacts");File.WriteAllText(report,"RUNNING");
            var go=new GameObject("Isolated request shutdown checks");go.SetActive(false);var generation=go.AddComponent<SceneGeneration>();
            var type=typeof(SceneGeneration);var workerType=type.GetNestedType("Worker",BindingFlags.NonPublic);
            var requestField=workerType.GetField("Request");var workers=(IDictionary)type.GetField("workers",Hidden).GetValue(generation);
            var shutdown=type.GetMethod("OnDestroy",Hidden);
            var helper=new SceneQueueChecks.FakeHelper(Array.Empty<byte>()){Hold=true};
            type.GetField("TestConnection",Hidden).SetValue(generation,new SceneServiceConnection{url=helper.Url,token=new string('a',64)});
            type.GetField("storage",Hidden).SetValue(generation,Path.GetFullPath("Library/RequestLifetimeChecks"));
            object worker=null;IEnumerator routine=null;UnityWebRequestAsyncOperation operation=null;int checks=0,phase=0;
            void Check(bool ok,string why){if(!ok)throw new Exception(why);checks++;}
            void Begin()
            {
                var job=new SceneJob{requestId=Guid.NewGuid().ToString("N"),treeId=new string('a',64),personKey="fixture",year=1900,place="Example"};helper.Seed(job);
                var pending=new PendingScene{phase=2,treeId=job.treeId,personId="fixture",job=job};
                worker=Activator.CreateInstance(workerType,true);workerType.GetField("Pending").SetValue(worker,pending);workers.Add(pending,worker);
                routine=(IEnumerator)type.GetMethod("Run",Hidden).Invoke(generation,new[]{worker});
                Check(routine.MoveNext(),"GET starts");operation=routine.Current as UnityWebRequestAsyncOperation;
                Check(operation!=null&&requestField.GetValue(worker)!=null,"Only in-flight GET is retained");
            }
            EditorApplication.CallbackFunction tick=null;double deadline=EditorApplication.timeSinceStartup+15;
            void Finish(string result)
            {
                EditorApplication.update-=tick;
                try{(routine as IDisposable)?.Dispose();shutdown.Invoke(generation,null);}
                finally{helper.Dispose();UnityEngine.Object.DestroyImmediate(go);File.WriteAllText(report,result);}
            }
            tick=()=>
            {
                try
                {
                    if(EditorApplication.timeSinceStartup>deadline)throw new Exception("Local helper timeout");
                    if(phase==0){Begin();phase=1;return;}
                    if(!operation.isDone)return;
                    Check(routine.MoveNext(),"Completed poll reaches wait between polls");
                    Check(routine.Current is WaitForSecondsRealtime,"Generating response waits without holding its request");
                    Check(requestField.GetValue(worker)==null,"Disposed GET reference cleared");
                    shutdown.Invoke(generation,null);Check(requestField.GetValue(worker)==null,"Shutdown after completed poll is safe");
                    (routine as IDisposable)?.Dispose();routine=null;workers.Clear();
                    Begin();shutdown.Invoke(generation,null);
                    Check(requestField.GetValue(worker)==null,"Shutdown detaches an active request");
                    shutdown.Invoke(generation,null);checks++;
                    (routine as IDisposable)?.Dispose();routine=null;Check(requestField.GetValue(worker)==null,"Iterator disposal after shutdown remains safe");
                    Finish(checks+" request lifetime checks passed: completed polls, active cancellation, repeated shutdown and iterator cleanup; no model calls.");
                }
                catch(Exception error){Finish("FAILED: "+error);Debug.LogException(error);}
            };
            EditorApplication.update+=tick;return "Running isolated Edit-mode request lifetime checks.";
        }
    }
}
