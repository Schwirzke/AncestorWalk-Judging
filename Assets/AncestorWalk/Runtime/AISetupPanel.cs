using InputField = TMPro.TMP_InputField;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Networking;
using UnityEngine.UI;
using UnityEngine.XR;

namespace AncestorWalk
{
    // Session-only BYOK. Unity calls api.meta.ai directly over HTTPS.
    public sealed class AISetupPanel : MonoBehaviour
    {
        sealed class Hit {public RectTransform Rect;public Action Action;}
        readonly List<Hit> hits=new();AncestorGallery gallery;Canvas canvas;RectTransform root,card;
        InputField keyInput;Text keyText,status,connectionText;Image keyImage,keySelection;string key="";bool selectAll=true,wasVr,configured;
        Keyboard keyboard;TouchScreenKeyboard native;UnityWebRequest request;Coroutine operation;IEnumerator testOperation;
        bool setupRequested;readonly Dictionary<Canvas,bool> suspendedCanvases=new();
        public bool IsOpen=>card!=null&&UIFade.Visible(card.gameObject);
        public bool Busy=>operation!=null;
        public string Status=>status!=null?status.text:"";
        public bool HasKeyInput=>key.Length>0;
        public void Build(AncestorGallery owner)
        {
            gallery=owner;root=(RectTransform)transform;canvas=gameObject.AddComponent<Canvas>();canvas.sortingOrder=160;
            var scaler=gameObject.AddComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(960,540);scaler.matchWidthOrHeight=.5f;
            card=Rect("AI Setup panel",root,new Vector2(0,-10),new Vector2(660,320));Background(card,new Color(.018f,.04f,.055f,.98f));
            Label(card,"AI SETUP",new Vector2(0,128),new Vector2(590,34),25);
            connectionText=Label(card,"Unity → Meta · direct HTTPS",new Vector2(0,93),new Vector2(590,26),16);
            Label(card,"Meta Model API key · this session",new Vector2(0,60),new Vector2(590,24),15).alignment=TextAnchor.MiddleLeft;
            var field=Rect("Meta API key",card,new Vector2(0,29),new Vector2(590,36));keyImage=Background(field);hits.Add(new Hit{Rect=field,Action=Focus});keyText=Label(field,"",Vector2.zero,new Vector2(570,34),16);keyText.alignment=TextAnchor.MiddleLeft;keyInput=NativeTextInput.Bind(keyText,v=>key=v,4095,true);hits.Add(new Hit{Rect=(RectTransform)keyInput.transform,Action=Focus});
            Button("Test & use key",new Vector2(-151,-19),new Vector2(286,34),()=>BeginTest(true));Button("Test current key",new Vector2(151,-19),new Vector2(286,34),()=>BeginTest(false));
            status=Label(card,"",new Vector2(0,-64),new Vector2(590,52),15);
            Label(card,"Your key stays in memory for this session.\nConnection tests check model access without generating a scene.",new Vector2(0,-108),new Vector2(590,34),12);
            Button("Paste",new Vector2(-200,-140),new Vector2(186,30),Paste);Button("Forget key",new Vector2(0,-140),new Vector2(186,30),Forget);Button("Close",new Vector2(200,-140),new Vector2(186,30),Close);
            card.gameObject.SetActive(false);Configure(XRSettings.isDeviceActive);
            MetaApi.SetupRequired+=RequestSetup;setupRequested=!MetaApi.KeyReady;
        }
        void RequestSetup(){if(!IsOpen)setupRequested=true;}
        public void Open()=>Show(false);
        void Show(bool required)
        {
            if(IsOpen||!required&&((gallery.Library?.IsOpen??false)||(gallery.Batch?.IsOpen??false)||(gallery.Tutorial?.IsOpen??false)||(gallery.LocationEditor?.IsOpen??false)))return;
            if(required)
            {
                // Keep the underlying form and its unsaved draft intact while entering a key.
                foreach(var owner in new MonoBehaviour[]{gallery.Library,gallery.Batch,gallery.Tutorial})
                    if(owner!=null)foreach(var other in owner.GetComponentsInChildren<Canvas>(true)){suspendedCanvases[other]=other.enabled;other.enabled=false;}
                gallery.LocationEditor?.SetSetupSuspended(true);
            }
            gallery.Hud?.SetVisible(true);gallery.Overlay?.CloseGuide();gallery.Overlay?.CancelInteraction();UnityEngine.Object.FindAnyObjectByType<NavigationPreferences>()?.SetOpen(false);
            key="";selectAll=true;UIFade.Set(card.gameObject,true);gallery.SetLocationModal(true);keyboard=Keyboard.current;if(keyboard!=null)keyboard.onTextInput-=Type;
            RefreshField();NativeTextInput.Focus(keyInput,true);SetStatus(MetaApi.KeyReady?"A Meta key is active for this session.":"Enter a valid Meta API key, then select Test & use key.\nYou can close this panel to explore saved scenes.");
        }
        public void Close()
        {
            if(keyInput!=null)keyInput.DeactivateInputField();
            if(keyboard!=null)keyboard.onTextInput-=Type;keyboard=null;key="";if(native!=null){native.active=false;native=null;}
            try{request?.Abort();}catch(ObjectDisposedException){}if(operation!=null){StopCoroutine(operation);operation=null;}var run=testOperation;testOperation=null;(run as IDisposable)?.Dispose();request=null;
            if(card!=null)UIFade.Set(card.gameObject,false);
            foreach(var other in suspendedCanvases)if(other.Key!=null)other.Key.enabled=other.Value;suspendedCanvases.Clear();
            if(gallery!=null){gallery.LocationEditor?.SetSetupSuspended(false);gallery.SetLocationModal((gallery.LocationEditor?.IsOpen??false)||(gallery.Library?.IsOpen??false)||(gallery.Batch?.IsOpen??false));}RefreshField();
        }
        void SetStatus(string value){if(status!=null)status.text=value;}
        void SetValue(string value){if(Busy)return;key=(value??"").Replace("\r","").Replace("\n","");if(key.Length>4095)key=key.Substring(0,4095);RefreshField();}
        void Type(char c){}
        public void Paste(){if(IsOpen&&!Busy)NativeTextInput.Insert(keyInput,GUIUtility.systemCopyBuffer.Trim());}
        void Focus(){if(!Busy)NativeTextInput.Focus(keyInput);}
        void RefreshField(){if(keyInput==null)return;if(keyInput.text!=key)keyInput.SetTextWithoutNotify(key);keyInput.interactable=!Busy;}
        bool ActiveWork()=>gallery.Generation.Busy||MetaApi.ActiveRequests>0||gallery.GetComponentsInChildren<PhotoSegmentation>(true).Any(s=>s.Busy)||gallery.GetComponentsInChildren<HistoricalGuide>(true).Any(g=>g.Busy)||gallery.GetComponentsInChildren<LocationVoiceInput>(true).Any(v=>v.Busy);
        public void Forget(){if(Busy)return;if(ActiveWork()){SetStatus("Wait for active AI work to finish before forgetting the key.");return;}MetaApi.ForgetKey();key="";RefreshField();SetStatus("Session key forgotten. Cached scenes remain available.");}
        public void BeginTest(bool useEnteredKey)
        {
            if(Busy)return;if(useEnteredKey&&ActiveWork()){SetStatus("Wait for active AI work to finish before changing the key.");return;}
            string candidate=null;try{if(useEnteredKey)candidate=MetaApi.ValidateKey(key);else MetaApi.RequireKey();}catch(InvalidOperationException error){SetStatus(error.Message);return;}
            key="";RefreshField();SetStatus("Connecting directly to Meta…");
            testOperation=MetaApi.Guard(Test(candidate),()=>{operation=null;request=null;SetStatus("Connection check interrupted. Your existing session key was kept.");});operation=StartCoroutine(testOperation);
        }
        IEnumerator Test(string candidate)
        {
            try
            {
                var reply=new MetaReply();yield return MetaApi.Send("models",null,reply,r=>request=r,candidate: candidate,timeout:22);
                if(!reply.Success){SetStatus(reply.Error);yield break;}
                if(reply.Json["data"] is not JArray data){SetStatus("Meta returned an unreadable model catalog.");yield break;}
                var models=data.OfType<JObject>().Select(m=>(string)m["id"]??"").ToArray();
                if(candidate!=null){MetaApi.UseKey(candidate);if(!gallery.Generation.QueuePaused)gallery.Generation.Resume(gallery.TreeId);}
                connectionText.text="Unity → Meta · connected over HTTPS";
                string Available(Func<string,bool> predicate)=>models.Any(predicate)?"available":"not listed";
                SetStatus("Meta connected. Image "+Available(m=>m.StartsWith("muse-image"))+" · Spark "+Available(m=>m.StartsWith("muse-spark"))+"\nVoice "+Available(m=>m.StartsWith("muse-voice"))+" · SAM "+Available(m=>m.StartsWith("sam-")));
            }
            finally{candidate=null;request=null;operation=null;}
        }
        void Update()
        {
            if(setupRequested&&!gallery.IsBuildingTree){setupRequested=false;if(!MetaApi.KeyReady)Show(true);}
            Configure(XRSettings.isDeviceActive);keyInput.interactable=!Busy;
            if(native!=null){if(native.status==TouchScreenKeyboard.Status.Canceled||native.status==TouchScreenKeyboard.Status.LostFocus){native.active=false;native=null;}else{if(native.text!=key)selectAll=false;SetValue(native.text);if(native.status==TouchScreenKeyboard.Status.Done){native.active=false;native=null;}}}
        }
        // Other panels update their fades each frame. Keep them hidden until setup closes.
        void LateUpdate(){foreach(var other in suspendedCanvases.Keys)if(other!=null)other.enabled=false;}
        void Configure(bool vr)
        {
            if(configured&&wasVr==vr)return;configured=true;wasVr=vr;canvas.renderMode=vr?RenderMode.WorldSpace:RenderMode.ScreenSpaceOverlay;canvas.worldCamera=Camera.main;
            if(vr){root.SetParent(Camera.main.transform,false);root.sizeDelta=new Vector2(960,540);root.localPosition=new Vector3(0,0,1.6f);root.localScale=Vector3.one*.0018f;root.localRotation=Quaternion.identity;}else{root.SetParent(gallery.transform,false);root.localScale=Vector3.one;}OverlayComfort.TextDensity(canvas,vr);
        }
        static bool Contains(RectTransform rect,Ray ray,bool vr){if(!UIFade.Interactive(rect))return false;if(!vr)return RectTransformUtility.RectangleContainsScreenPoint(rect,Mouse.current?.position.ReadValue()??Vector2.zero);var plane=new Plane(rect.forward,rect.position);return plane.Raycast(ray,out float distance)&&distance>=0&&rect.rect.Contains(rect.InverseTransformPoint(ray.GetPoint(distance)));}
        public bool HandleInput(Ray ray,bool vr,bool pressed,bool back)
        {
            if(!IsOpen)return false;
            var keys=Keyboard.current;if(back||(keys?.escapeKey.wasPressedThisFrame??false)){Close();return true;}
            if(!Busy&&native==null&&keys!=null){if(keys.enterKey.wasPressedThisFrame)BeginTest(key.Length>0);}
            if(pressed)for(int i=hits.Count-1;i>=0;i--)if(UIFade.Interactive(hits[i].Rect)&&Contains(hits[i].Rect,ray,vr)){if(vr&&hits[i].Rect==keyInput.transform)NativeTextInput.Point(keyInput,ray);else hits[i].Action();break;}return true;
        }
        void Button(string title,Vector2 at,Vector2 size,Action action){var rect=Rect(title,card,at,size);Background(rect);Label(rect,title,Vector2.zero,size,15);hits.Add(new Hit{Rect=rect,Action=action});}
        static RectTransform Rect(string name,Transform parent,Vector2 at,Vector2 size){var rect=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();rect.SetParent(parent,false);rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);rect.anchoredPosition=at;rect.sizeDelta=size;return rect;}
        static Image Background(RectTransform rect,Color? color=null){var image=rect.gameObject.AddComponent<Image>();image.color=color??new Color(.07f,.27f,.28f,.95f);image.raycastTarget=false;return image;}
        Text Label(Transform parent,string value,Vector2 at,Vector2 size,int fontSize){var label=Rect("AI setup label",parent,at,size).gameObject.AddComponent<Text>();label.font=gallery.font;label.text=value;label.fontSize=fontSize;label.color=Color.white;label.alignment=TextAnchor.MiddleCenter;label.supportRichText=false;label.raycastTarget=false;label.resizeTextForBestFit=true;label.resizeTextMinSize=10;label.resizeTextMaxSize=fontSize;return label;}
        void OnDestroy(){MetaApi.SetupRequired-=RequestSetup;Close();}
    }
}
