using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace AncestorWalk.Editor
{
    // Runtime trees, pooled faces and texture leases cannot survive Unity's script hot reload.
    // Restart this project's Play session instead of allowing partially restored objects to update.
    [InitializeOnLoad]
    public static class PlayReloadRecovery
    {
        const string Key="AncestorWalk.PlayReloadRecovery";
        [Serializable] sealed class Snapshot
        {
            public int phase=1;public string tree,person;public bool demo,paused,tutorial,hud;
            public bool hasView;public Vector3 position;public Quaternion rotation,look;public float pitch;
            public Vector2 treeRotation,treePan;public float treeScale=1;
        }
        static bool stopping;
        static double deadline;
        static bool restoring;
        static PlayReloadRecovery()
        {
            AssemblyReloadEvents.beforeAssemblyReload+=Capture;
            EditorApplication.playModeStateChanged+=StateChanged;
            EditorApplication.update+=Drive;
        }
        static Snapshot Read()
        {string json=SessionState.GetString(Key,"");return string.IsNullOrEmpty(json)?null:JsonUtility.FromJson<Snapshot>(json);}
        static void Save(Snapshot state)=>SessionState.SetString(Key,JsonUtility.ToJson(state));
        static void Capture()
        {
            if(stopping||!EditorApplication.isPlaying||Read()!=null)return;
            var gallery=UnityEngine.Object.FindAnyObjectByType<AncestorGallery>();if(gallery?.Document==null)return;
            var state=new Snapshot{tree=gallery.Document.Id,demo=gallery.Document.IsDemo,person=gallery.SelectedId,paused=EditorApplication.isPaused,tutorial=gallery.Tutorial?.IsOpen??false,hud=gallery.HudVisible,treeRotation=gallery.TreeRotation,treePan=gallery.TreePan,treeScale=gallery.TreeScale};
            var navigation=UnityEngine.Object.FindAnyObjectByType<GalleryNavigation>();
            if(navigation?.view!=null)
            {
                state.hasView=true;state.position=navigation.transform.position;state.rotation=navigation.transform.rotation;state.look=navigation.view.transform.localRotation;
                state.pitch=(float)typeof(GalleryNavigation).GetField("pitch",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(navigation);
            }
            Save(state);
            // OnEnable runs during deserialization, before DidReloadScripts. Release/disable the
            // runtime hierarchy now, while its dictionaries and texture leases still exist.
            gallery.gameObject.SetActive(false);
        }
        [DidReloadScripts]
        static void Reloaded()
        {
            var state=Read();if(state==null||state.phase!=1)return;
            // Pause immediately, before any restored Update/LateUpdate can read missing pools or dictionaries.
            if(EditorApplication.isPlaying)EditorApplication.isPaused=true;
        }
        static void Drive()
        {
            var state=Read();if(state==null||EditorApplication.isCompiling||EditorApplication.isUpdating)return;
            if(state.phase==1)
            {
                if(EditorApplication.isPlaying){EditorApplication.isPaused=true;EditorApplication.isPlaying=false;}
                else if(!EditorApplication.isPlayingOrWillChangePlaymode)Restart();
            }
            else if(state.phase==2&&EditorApplication.isPlaying&&!restoring)
            {restoring=true;deadline=EditorApplication.timeSinceStartup+30;EditorApplication.update-=Restore;EditorApplication.update+=Restore;}
        }
        static void Restart()
        {
            var state=Read();if(state==null||state.phase!=1)return;
            state.phase=2;Save(state);stopping=false;EditorApplication.isPaused=false;EditorApplication.isPlaying=true;
        }
        static void StateChanged(PlayModeStateChange change)
        {
            if(change==PlayModeStateChange.EnteredPlayMode)stopping=false;
            if(change==PlayModeStateChange.ExitingPlayMode)
            {
                stopping=true;
                // An explicit stop during recovery cancels the pending restart.
                if(Read()?.phase==2)SessionState.EraseString(Key);
            }
        }
        static void Restore()
        {
            var state=Read();if(state==null||!EditorApplication.isPlaying){EditorApplication.update-=Restore;return;}
            if(EditorApplication.timeSinceStartup>deadline){Finish("Script reload restarted Play mode; reopen the saved tree if needed.",false);return;}
            var gallery=UnityEngine.Object.FindAnyObjectByType<AncestorGallery>();
            if(gallery?.Tree==null||gallery.Library==null||gallery.Library.Busy||!gallery.CacheIndexReady||gallery.IsBuildingTree)return;
            try
            {
                if(gallery.TreeId!=state.tree)
                {
                    if(state.demo)gallery.OpenDemo();
                    else {var store=new TreeStore(PanoramaCache.StorageRoot);gallery.OpenDocument(store.Load(state.tree));}
                    return;
                }
                if(!state.tutorial)gallery.Tutorial?.Dismiss();else gallery.Tutorial?.Open();
                gallery.SetTreeScale(state.treeScale);gallery.SetTreeRotation(state.treeRotation);gallery.PanTree(state.treePan-gallery.TreePan);
                if(!string.IsNullOrEmpty(state.person)&&gallery.Tree.People.ContainsKey(state.person))gallery.Select(state.person);
                var navigation=UnityEngine.Object.FindAnyObjectByType<GalleryNavigation>();
                if(state.hasView&&navigation?.view!=null)
                {
                    navigation.transform.SetPositionAndRotation(state.position,state.rotation);navigation.view.transform.localRotation=state.look;
                    typeof(GalleryNavigation).GetField("pitch",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(navigation,state.pitch);
                }
                gallery.Hud?.SetVisible(state.hud);Finish("Ancestor Walk restarted Play mode after script reload and restored the current tree/view.",state.paused);
            }
            catch(Exception){Finish("Play mode restarted after script reload. Select the saved tree from Trees to continue.",state.paused);}
        }
        static void Finish(string message,bool paused)
        {EditorApplication.update-=Restore;restoring=false;SessionState.EraseString(Key);EditorApplication.isPaused=paused;Debug.Log(message);}
        public static bool IsRecovering=>Read()!=null;
    }
}
