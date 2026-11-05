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
                Call("SetKeyVisible",true);Check(input.textComponent.text.Contains("fixture"),"Eye toggle reveals entered characters");Call("SetKeyVisible",false);Check(!input.textComponent.text.Contains("fixture"),"Eye toggle masks characters again");
                input.SetTextWithoutNotify("fixture");input.selectionAnchorPosition=0;input.selectionFocusPosition=input.text.Length;NativeTextInput.Insert(input,"x");Check(input.text=="x","Native editing replaces selection");
                Value("key","");p.BeginTest(true);Check(!p.Busy&&p.Status.Contains("key"),"Empty key handled locally");
                foreach(string key in new[]{"", "short", "private key", "x\r\nheader", new string('x',4096)}){bool rejected=false;try{MetaApi.ValidateKey(key);}catch(InvalidOperationException){rejected=true;}Check(rejected,"Invalid key rejected without echo");}
                Check(MetaApi.BaseUrl=="https://api.meta.ai/v1/","Only official HTTPS base URL");
                Check(!p.GetComponentsInChildren<Transform>(true).Any(t=>t.name=="Helper address"||t.name=="Helper connection token"||t.name=="Start Mac helper"),"No helper controls remain");
                Check(!p.GetComponentsInChildren<Text>(true).Any(t=>t.text.IndexOf("helper",StringComparison.OrdinalIgnoreCase)>=0||t.text.IndexOf("Mac",StringComparison.OrdinalIgnoreCase)>=0),"Judge-facing panel has no development helper wording");
                var attached=(RectTransform)type.GetField("attachedKeyboard",Hidden).GetValue(p);
                Check(!attached.gameObject.activeSelf,"Desktop retains its normal input without the VR keypad");
                var card=(RectTransform)type.GetField("card",Hidden).GetValue(p);foreach(var label in card.GetComponentsInChildren<Text>()){var local=card.InverseTransformPoint(label.transform.position);var rect=label.rectTransform.rect;Check(Mathf.Abs(local.x)+rect.width*.5f<=card.rect.width*.5f+.1f&&Mathf.Abs(local.y)+rect.height*.5f<=card.rect.height*.5f+.1f,"Labels inside panel");}
                var nav=UnityEngine.Object.FindAnyObjectByType<GalleryNavigation>();Vector2 before=g.TreeRotation;nav.RotateTree(Vector2.one,1);Check(g.TreeRotation==before,"Modal blocks rotation");
                g.Library.Open();Check(!g.Library.IsOpen,"Modal blocks library");g.Batch.Open();Check(!g.Batch.IsOpen,"Modal blocks jobs");g.Tutorial.Open();Check(!g.Tutorial.IsOpen,"Modal blocks tutorial");
                Check(p.HandleInput(new Ray(Vector3.zero,Vector3.forward),false,false,false),"Empty panel area consumes clicks");
                Call("Configure",true);Check(attached.parent==card&&attached.gameObject.activeSelf,"VR keyboard is attached to the same panel");
                Check(input.shouldHideSoftKeyboard,"VR API entry suppresses the separate system overlay");
                Check(Mathf.Abs(p.transform.localPosition.z-1.2f)<.001f,"VR panel and keyboard sit closer at 1.2 metres");
                foreach(var label in card.GetComponentsInChildren<Text>()){var local=card.InverseTransformPoint(label.transform.position);var rect=label.rectTransform.rect;Check(Mathf.Abs(local.x)+rect.width*.5f<=card.rect.width*.5f+.1f&&Mathf.Abs(local.y)+rect.height*.5f<=card.rect.height*.5f+.1f,"VR keyboard and form labels stay inside the shared panel");}
                var lower=(string[])type.GetField("LowerRows",StaticHidden).GetValue(null);var upper=(string[])type.GetField("UpperRows",StaticHidden).GetValue(null);
                Check(string.Concat(lower).Concat(string.Concat(upper)).Distinct().OrderBy(c=>c).SequenceEqual(Enumerable.Range(33,94).Select(c=>(char)c)),"Keyboard covers every printable API-key character");
                void Press(string title){var target=attached.GetComponentsInChildren<RectTransform>().Single(r=>r.name=="API key · "+title);var origin=Camera.main.transform.position;Check(p.HandleInput(new Ray(origin,(target.position-origin).normalized),true,true,false),"Controller ray reaches "+title);}
                Call("SetValue","");input.selectionStringAnchorPosition=input.selectionStringFocusPosition=0;
                Press("a");Press("Shift");Press("b");Press("_");Check(input.text=="aB_","Controller keys type lowercase, uppercase and symbols into masked field");
                Press("1");Check(input.text=="aB_1"&&attached.GetComponentsInChildren<RectTransform>().Single(r=>r.name=="API key · 1").GetComponentInChildren<Text>().text=="1","Number row stays numeric with Shift on");Press("Backspace");
                Press("Backspace");Press("Left");Press("Shift");Press("1");Check(input.text=="a1B","Controller backspace and caret navigation edit the key");
                Press("Clear");Check(input.text==""&&!p.HasKeyInput,"Clear removes all entered characters");
                var hoverTarget=attached.GetComponentsInChildren<RectTransform>().Single(r=>r.name=="API key · q");var hoverImage=hoverTarget.GetComponent<Image>();var hoverOrigin=Camera.main.transform.position;var hoverRay=new Ray(hoverOrigin,(hoverTarget.position-hoverOrigin).normalized);var idleColor=hoverImage.color;
                p.HandleInput(hoverRay,true,false,false);var hoverColor=hoverImage.color;Check(hoverColor!=idleColor,"Pointing at a key highlights it");p.HandleInput(hoverRay,true,true,false);Check(hoverImage.color!=hoverColor,"Tapping a key flashes a distinct color");
                Call("SetValue",new string('x',4095));input.selectionStringAnchorPosition=input.selectionStringFocusPosition=4095;Press("a");Check(input.text.Length==4095,"Attached typing respects the key length limit");
                Call("Configure",false);
                Value("key","fixture-secret-DO-NOT-PERSIST");p.Close();Check(!p.IsOpen&&!p.HasKeyInput,"Close clears entry field");p.Open();Check(!p.HasKeyInput,"Reopen does not expose active key");Check(!JsonUtility.ToJson(p).Contains("fixture"),"No serialized credentials");
                Check(!File.Exists(path)||File.GetLastWriteTimeUtc(path)==stamp,"No helper configuration touched");
                string report=count+" direct AI Setup UI checks passed.";Directory.CreateDirectory("Artifacts");File.WriteAllText("Artifacts/ai-setup-checks.txt",report);return report;
            }
            finally{Call("Configure",UnityEngine.XR.XRSettings.isDeviceActive);p.Close();api.GetField("sessionKey",StaticHidden).SetValue(null,oldKey);api.GetField("keyRejected",StaticHidden).SetValue(null,oldRejected);api.GetField("keyRevision",StaticHidden).SetValue(null,oldRevision);}
        }
        public static string RunKeyValidationRetention()
        {
            var g=UnityEngine.Object.FindAnyObjectByType<AncestorGallery>();if(!Application.isPlaying||g?.AISetup==null||g.Generation.Busy||g.Generation.WaitingCount!=0)throw new Exception("Idle Play mode with no queued work required.");
            g.StartCoroutine(MetaApi.Guard(CheckKeyValidationRetention(g),()=>File.WriteAllText("Artifacts/ai-key-retention-checks.txt","FAILED: key retention check interrupted.")));return "Checking failed/successful key submission with local responses only.";
        }
        static System.Collections.IEnumerator CheckKeyValidationRetention(AncestorGallery g)
        {
            var p=g.AISetup;var type=typeof(AISetupPanel);var oldReply=MetaApi.TestReply;const string fixture="fixture-case-AbC123";string report="Artifacts/ai-key-retention-checks.txt";
            const BindingFlags hidden=BindingFlags.Static|BindingFlags.NonPublic;var keyField=typeof(MetaApi).GetField("sessionKey",hidden);var rejectedField=typeof(MetaApi).GetField("keyRejected",hidden);var revisionField=typeof(MetaApi).GetField("keyRevision",hidden);
            object oldKey=keyField.GetValue(null),oldRejected=rejectedField.GetValue(null),oldRevision=revisionField.GetValue(null);
            File.WriteAllText(report,"RUNNING");p.Close();p.Open();
            try
            {
                type.GetMethod("SetValue",Hidden).Invoke(p,new object[]{fixture});
                MetaApi.TestReply=(path,body)=>new MetaReply{Http=401,Error="Fixture rejected key"};p.BeginTest(true);
                if(!p.HasKeyInput)throw new Exception("Submit cleared the entry before validation");
                while(p.Busy)yield return null;
                var input=(TMPro.TMP_InputField)type.GetField("keyInput",Hidden).GetValue(p);
                if(input.text!=fixture||!input.interactable)throw new Exception("Failed submission lost the editable key");
                MetaApi.TestReply=(path,body)=>new MetaReply{Http=200,Json=new Newtonsoft.Json.Linq.JObject{["data"]=new Newtonsoft.Json.Linq.JArray(new Newtonsoft.Json.Linq.JObject{["id"]="muse-image-1.0"})}};p.BeginTest(true);
                while(p.Busy)yield return null;
                if(p.HasKeyInput||input.text.Length!=0||!MetaApi.KeyReady)throw new Exception("Successful submission did not clear and activate the key");
                File.WriteAllText(report,"PASSED: submit retains text while pending; 401 retains editable mixed-case text; success activates and clears entry. No external requests.");
            }
            finally{MetaApi.TestReply=oldReply;keyField.SetValue(null,oldKey);rejectedField.SetValue(null,oldRejected);revisionField.SetValue(null,oldRevision);p.Close();}
        }
    }
}
