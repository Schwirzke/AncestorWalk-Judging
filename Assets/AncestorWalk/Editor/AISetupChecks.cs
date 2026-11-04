using System;
using System.IO;
using System.Reflection;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace AncestorWalk.Editor
{
    public static class AISetupChecks
    {
        const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        public static string Run()
        {
            var g=UnityEngine.Object.FindAnyObjectByType<AncestorGallery>();if(!Application.isPlaying||g?.AISetup==null)throw new Exception("Play mode required.");
            int count=0;void Check(bool ok,string message){if(!ok)throw new Exception(message);count++;}
            var p=g.AISetup;g.Tutorial.Dismiss();if(g.Library.IsOpen)g.Library.Close();if(g.Batch.IsOpen)g.Batch.Close();p.Close();
            var type=typeof(AISetupPanel);void Value(string field,object value)=>type.GetField(field,Hidden).SetValue(p,value);void Call(string method,params object[] args)=>type.GetMethod(method,Hidden).Invoke(p,args);
            string path=Path.Combine(PanoramaCache.StorageRoot,"scene-service.json");DateTime stamp=File.Exists(path)?File.GetLastWriteTimeUtc(path):default;
            var api=typeof(MetaApi);const BindingFlags StaticHidden=BindingFlags.Static|BindingFlags.NonPublic;
            object oldKey=api.GetField("sessionKey",StaticHidden).GetValue(null),oldRejected=api.GetField("keyRejected",StaticHidden).GetValue(null),oldRevision=api.GetField("keyRevision",StaticHidden).GetValue(null);
            try
            {
                MetaApi.ForgetKey();Value("setupRequested",true);Call("Update");Check(p.IsOpen,"Startup with no key opens setup");
                g.Tutorial.GetComponent<Canvas>().enabled=true;Call("LateUpdate");Check(!g.Tutorial.GetComponent<Canvas>().enabled,"Setup stays above underlying panel fade updates");
                p.Close();Call("Update");Check(!p.IsOpen,"Closing startup setup allows cached browsing without reopening");
                try{MetaApi.RequireKey();}catch(InvalidOperationException){}Call("Update");Check(p.IsOpen,"An AI attempt without a key opens setup again");p.Close();
                var person=g.Tree.People.Values.First(x=>LifeEndpointScenes.TrySceneRange(x,out _,out _));g.LocationEditor.Open(g,person,g.Year(person.Id));
                var locationType=typeof(LocationEditor);locationType.GetField("value",Hidden).SetValue(g.LocationEditor,"Unsaved location fixture");
                try{MetaApi.RequireKey();}catch(InvalidOperationException){}Call("Update");
                var locationPanel=(GameObject)locationType.GetField("panel",Hidden).GetValue(g.LocationEditor);
                Check(p.IsOpen&&g.LocationEditor.IsOpen&&!locationPanel.activeSelf,"Setup suspends the location form");p.Close();
                Check(locationPanel.activeSelf&&(string)locationType.GetField("value",Hidden).GetValue(g.LocationEditor)=="Unsaved location fixture","Closing setup restores the unsaved draft");g.LocationEditor.Close();
                MetaApi.UseKey("fixture-valid-key");int revision=(int)api.GetField("keyRevision",StaticHidden).GetValue(null);
                var auth=api.GetMethod("CheckAuthentication",StaticHidden);auth.Invoke(null,new object[]{401L,false,revision});Check(MetaApi.KeyReady,"Rejected candidate does not invalidate the active key");
                auth.Invoke(null,new object[]{401L,true,revision-1});Check(MetaApi.KeyReady,"An old request cannot invalidate a replacement key");
                auth.Invoke(null,new object[]{403L,true,revision});Call("Update");Check(!MetaApi.KeyReady&&p.IsOpen,"Rejected session access blocks AI and opens setup");p.Close();
                MetaApi.UseKey("fixture-replacement-key");Check(MetaApi.KeyReady,"A validated replacement clears rejected state");MetaApi.ForgetKey();
                bool hadRoom=PlayerPrefs.HasKey(PassthroughMode.PreferenceKey);int room=PlayerPrefs.GetInt(PassthroughMode.PreferenceKey,0);
                try{PlayerPrefs.SetInt(PassthroughMode.PreferenceKey,1);typeof(PassthroughMode).GetMethod("Start",Hidden).Invoke(g.Passthrough,null);Check(!g.Passthrough.Active&&!g.Passthrough.Starting,"Startup uses full VR even after an earlier room session");}
                finally{if(hadRoom)PlayerPrefs.SetInt(PassthroughMode.PreferenceKey,room);else PlayerPrefs.DeleteKey(PassthroughMode.PreferenceKey);PlayerPrefs.Save();}
                var prefs=UnityEngine.Object.FindAnyObjectByType<NavigationPreferences>();
                Check(prefs.GetComponentsInChildren<Text>(true).Any(t=>t.text=="Settings")&&prefs.GetComponentsInChildren<Text>(true).Any(t=>t.text=="SETTINGS"),"Settings Panel replaces the visible Preferences label");
                prefs.SetOpen(true);prefs.GetComponentsInChildren<Button>(true).Single(b=>b.name=="Scenes").onClick.Invoke();
                var setup=prefs.GetComponentsInChildren<Button>(true).Single(b=>b.name=="AI Setup");Check(setup.gameObject.activeInHierarchy,"AI Setup button is inside the open Scenes settings tab");
                setup.onClick.Invoke();Check(p.IsOpen&&!prefs.IsOpen,"Settings button opens AI Setup and closes Settings Panel");p.Close();
                Call("Update");Check(!p.GetComponentsInChildren<RectTransform>(true).Any(r=>r.name=="Open AI setup"),"Standalone AI Setup launcher is removed on all platforms");
                p.Open();Check(p.IsOpen&&!p.HasKeyInput,"Open blank masked field");Value("key","fixture-secret-DO-NOT-PERSIST");Call("RefreshField");
                var input=(TMPro.TMP_InputField)type.GetField("keyInput",Hidden).GetValue(p);input.ForceLabelUpdate();Check(input.contentType==TMPro.TMP_InputField.ContentType.Password&&!input.textComponent.text.Contains("fixture")&&input.textComponent.text.Length>0,"Key is masked");
                input.SetTextWithoutNotify("fixture");input.selectionAnchorPosition=0;input.selectionFocusPosition=input.text.Length;NativeTextInput.Insert(input,"x");Check(input.text=="x","Native editing replaces selection");
                Value("key","");p.BeginTest(true);Check(!p.Busy&&p.Status.Contains("key"),"Empty key handled locally");
                foreach(string key in new[]{"", "short", "private key", "x\r\nheader", new string('x',4096)}){bool rejected=false;try{MetaApi.ValidateKey(key);}catch(InvalidOperationException){rejected=true;}Check(rejected,"Invalid key rejected without echo");}
                Check(MetaApi.BaseUrl=="https://api.meta.ai/v1/","Only official HTTPS base URL");
                Check(!p.GetComponentsInChildren<Transform>(true).Any(t=>t.name=="Helper address"||t.name=="Helper connection token"||t.name=="Start Mac helper"),"No helper controls remain");
                Check(!p.GetComponentsInChildren<Text>(true).Any(t=>t.text.IndexOf("helper",StringComparison.OrdinalIgnoreCase)>=0||t.text.IndexOf("Mac",StringComparison.OrdinalIgnoreCase)>=0),"Judge-facing panel has no development helper wording");
                Check(!(bool)typeof(TMPro.TMP_InputField).GetField("m_HideSoftKeyboard",Hidden).GetValue(input),"Mobile software keyboard configuration is enabled");
                var card=(RectTransform)type.GetField("card",Hidden).GetValue(p);foreach(var label in card.GetComponentsInChildren<Text>()){var local=card.InverseTransformPoint(label.transform.position);var rect=label.rectTransform.rect;Check(Mathf.Abs(local.x)+rect.width*.5f<=card.rect.width*.5f+.1f&&Mathf.Abs(local.y)+rect.height*.5f<=card.rect.height*.5f+.1f,"Labels inside panel");}
                var nav=UnityEngine.Object.FindAnyObjectByType<GalleryNavigation>();Vector2 before=g.TreeRotation;nav.RotateTree(Vector2.one,1);Check(g.TreeRotation==before,"Modal blocks rotation");
                g.Library.Open();Check(!g.Library.IsOpen,"Modal blocks library");g.Batch.Open();Check(!g.Batch.IsOpen,"Modal blocks jobs");g.Tutorial.Open();Check(!g.Tutorial.IsOpen,"Modal blocks tutorial");
                Check(p.HandleInput(new Ray(Vector3.zero,Vector3.forward),false,false,false),"Empty panel area consumes clicks");
                Value("key","fixture-secret-DO-NOT-PERSIST");p.Close();Check(!p.IsOpen&&!p.HasKeyInput,"Close clears entry field");p.Open();Check(!p.HasKeyInput,"Reopen does not expose active key");Check(!JsonUtility.ToJson(p).Contains("fixture"),"No serialized credentials");
                Check(!File.Exists(path)||File.GetLastWriteTimeUtc(path)==stamp,"No helper configuration touched");
                string report=count+" direct AI Setup UI checks passed.";Directory.CreateDirectory("Artifacts");File.WriteAllText("Artifacts/ai-setup-checks.txt",report);return report;
            }
            finally{p.Close();api.GetField("sessionKey",StaticHidden).SetValue(null,oldKey);api.GetField("keyRejected",StaticHidden).SetValue(null,oldRejected);api.GetField("keyRevision",StaticHidden).SetValue(null,oldRevision);}
        }
    }
}
