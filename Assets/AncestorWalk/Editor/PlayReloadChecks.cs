using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace AncestorWalk.Editor
{
    [InitializeOnLoad]
    public static class PlayReloadChecks
    {
        const string Key="AncestorWalk.PlayReloadCheck";
        [Serializable] sealed class Expected {public string tree,person;public int people;public Vector3 position;public Quaternion rotation,look;public Vector2 treeRotation,treePan;public bool paused;public int exceptions;public double deadline;}
        static double settled;
        static PlayReloadChecks(){Application.logMessageReceived+=Log;EditorApplication.update+=Tick;}
        static Expected Read(){string json=SessionState.GetString(Key,"");return string.IsNullOrEmpty(json)?null:JsonUtility.FromJson<Expected>(json);}
        static void Save(Expected e)=>SessionState.SetString(Key,JsonUtility.ToJson(e));
        static void Log(string message,string stack,LogType type)
        {if(type!=LogType.Exception||!stack.Contains("AncestorWalk"))return;var e=Read();if(e!=null){e.exceptions++;Save(e);}}
        public static string Arm(bool scene=false,bool paused=false)
        {
            var g=UnityEngine.Object.FindAnyObjectByType<AncestorGallery>();var nav=UnityEngine.Object.FindAnyObjectByType<GalleryNavigation>();
            if(!Application.isPlaying||g?.Tree==null||g.Library.Busy||!g.CacheIndexReady||g.Generation.Busy)throw new Exception("Wait for a ready, idle example in Play mode.");
            g.Library.Close();g.Tutorial.Dismiss();g.Batch.Close();if(scene)g.Select(g.Tree.People.Keys.OrderBy(x=>x).First());else g.ShowTree();
            nav.transform.SetPositionAndRotation(new Vector3(.3f,scene?0:1.2f,-.7f),Quaternion.Euler(0,12,0));nav.view.transform.localRotation=Quaternion.Euler(8,0,0);
            typeof(GalleryNavigation).GetField("pitch",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(nav,8f);
            Save(new Expected{tree=g.TreeId,person=g.SelectedId,people=g.Tree.People.Count,position=nav.transform.position,rotation=nav.transform.rotation,look=nav.view.transform.localRotation,treeRotation=g.TreeRotation,treePan=g.TreePan,paused=paused,deadline=EditorApplication.timeSinceStartup+75});
            settled=0;Directory.CreateDirectory("Artifacts");File.WriteAllText("Artifacts/play-reload-checks.txt","ARMED · "+(scene?"scene":"tree")+" · paused="+paused);EditorApplication.isPaused=paused;
            return "Armed actual script-reload validation. Now compile scripts while Play mode is running.";
        }
        static void Tick()
        {
            var e=Read();if(e==null)return;
            if(EditorApplication.timeSinceStartup>e.deadline){Finish("FAILED: reload recovery timed out");return;}
            if(EditorApplication.isCompiling||EditorApplication.isUpdating||!EditorApplication.isPlaying||PlayReloadRecovery.IsRecovering){settled=0;return;}
            // The request stays armed before compilation; a fresh runtime starts at a small frame count.
            var g=UnityEngine.Object.FindAnyObjectByType<AncestorGallery>();if(g?.Tree==null||g.Library==null||g.Library.Busy||!g.CacheIndexReady)return;
            string report=File.ReadAllText("Artifacts/play-reload-checks.txt");
            if(report.StartsWith("ARMED")&&!SessionState.GetBool(Key+".reloaded",false))return;
            if(settled==0)settled=EditorApplication.timeSinceStartup;if(EditorApplication.timeSinceStartup-settled<1.5)return;
            try
            {
                if(e.exceptions!=0)throw new Exception(e.exceptions+" runtime exceptions during reload");
                if(g.TreeId!=e.tree||g.Tree.People.Count!=e.people||!string.Equals(g.SelectedId??"",e.person??""))throw new Exception("Tree or selected scene was not restored");
                if(Vector2.Distance(g.TreeRotation,e.treeRotation)>.001f||Vector2.Distance(g.TreePan,e.treePan)>.001f)throw new Exception("Tree rotation or pan was not restored");
                var nav=UnityEngine.Object.FindAnyObjectByType<GalleryNavigation>();
                if(Vector3.Distance(nav.transform.position,e.position)>.001f||Quaternion.Angle(nav.transform.rotation,e.rotation)>.01f||Quaternion.Angle(nav.view.transform.localRotation,e.look)>.01f)throw new Exception("View pose was not restored");
                var field=typeof(CubeFacingControls).GetField("ghosts",BindingFlags.NonPublic|BindingFlags.Instance);
                foreach(var controls in UnityEngine.Object.FindObjectsByType<CubeFacingControls>(FindObjectsSortMode.None))
                    if(((Array)field.GetValue(controls)).Cast<object>().Any(x=>x==null))throw new Exception("A pooled face is missing");
                if(EditorApplication.isPaused!=e.paused)throw new Exception("Pause state was not restored");
                Finish("PASSED: actual Play-mode script reload; tree/scene, rotation, pan, view, pause state and pooled faces restored; zero runtime exceptions.");
            }
            catch(Exception error){Finish("FAILED: "+error.Message);}
        }
        [UnityEditor.Callbacks.DidReloadScripts] static void Reloaded(){if(Read()!=null)SessionState.SetBool(Key+".reloaded",true);}
        static void Finish(string report){File.WriteAllText("Artifacts/play-reload-checks.txt",report);SessionState.EraseString(Key);SessionState.EraseBool(Key+".reloaded");}
    }
}
