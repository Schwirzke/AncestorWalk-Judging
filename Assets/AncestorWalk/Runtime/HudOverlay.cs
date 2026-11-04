using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR;
namespace AncestorWalk
{
    // Overlay-only visibility; world person controls and portals remain independent.
    public sealed class HudOverlay : MonoBehaviour
    {
        public const string GenerationBrowseHint="Feel free to browse to other times and generate more images, or look around.";
        public const string PreferenceKey="AncestorWalk.HudVisible";
        sealed class Tip {public RectTransform Target;public Canvas Canvas;public string Text;public Func<bool> Enabled;public Transform WorldTarget;public bool Marker;}
        readonly List<Tip> tips=new();
        public AncestorGallery Owner=>gallery;
        AncestorGallery gallery;Canvas canvas;RectTransform root,toggle,roomToggle,home,tooltip;Text toggleText,roomText,tooltipText;
        GenerationErrorOverlay errorOverlay;
        public bool HandleError(Ray ray,bool vr,bool pressed)=>errorOverlay!=null&&errorOverlay.HandleInput(ray,vr,pressed);
        bool lastRoomActive,lastRoomStarting;
        Image roomBackground;
        static readonly Color RoomIdleColor=new(.06f,.22f,.23f,.9f);
        static readonly Color RoomBlinkColor=new(.24f,.62f,.52f,1);
        string captionPerson;int captionYear=int.MinValue;
        RectTransform generationProgress,queuedProgress,moveQueuedFirst,cancelQueued,cancelActive;Text generationText,queuedText;string generationLabel,queuedLabel;
        string activeTree,activePerson;int activeYear;
        int queuedRevision=-1,queuedYear;string queuedTree,queuedPerson;PendingScene queuedTarget;
        bool wasVr,configured;Tip hovered;float hoveredAt;
        public bool Visible {get;private set;}=true;
        public bool TooltipVisible=>tooltip!=null&&UIFade.Visible(tooltip.gameObject);
        void OnDestroy(){if(errorOverlay!=null)Destroy(errorOverlay.gameObject);}
        public string TooltipText=>tooltipText!=null?tooltipText.text:"";
        public void Build(AncestorGallery owner)
        {
            gallery=owner;root=(RectTransform)transform;canvas=gameObject.AddComponent<Canvas>();canvas.sortingOrder=120;
            errorOverlay=new GameObject("Generation error overlay",typeof(RectTransform)).AddComponent<GenerationErrorOverlay>();errorOverlay.Build(owner);
            var scaler=gameObject.AddComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(960,540);scaler.matchWidthOrHeight=.5f;
            toggle=Rect("Toggle HUD",root,new Vector2(0,234),new Vector2(68,34));toggle.gameObject.AddComponent<Image>().color=new Color(.12f,.30f,.68f,1);
            toggleText=Label(toggle,"HUD",toggle.sizeDelta,14);
            roomToggle=Rect("Toggle passthrough",root,new Vector2(80,234),new Vector2(72,34));roomBackground=roomToggle.gameObject.AddComponent<Image>();roomBackground.color=RoomIdleColor;
            roomText=Label(roomToggle,"Room",roomToggle.sizeDelta,14);
            home=Rect("Home",root,new Vector2(80,234),new Vector2(72,34));home.gameObject.AddComponent<Image>().color=new Color(.96f,.70f,.22f,1);
            Label(home,"Home",home.sizeDelta,14).color=new Color(.12f,.09f,.035f,1);
            generationProgress=Rect("Scene generation progress",root,new Vector2(0,158),new Vector2(200,78));
            var backing=generationProgress.gameObject.AddComponent<Image>();backing.color=new Color(.025f,.065f,.04f,.9f);backing.raycastTarget=false;
            generationText=Label(generationProgress,"",new Vector2(190,42),13);generationText.rectTransform.anchoredPosition=new Vector2(0,12);
            cancelActive=Rect("Cancel active scene",generationProgress,new Vector2(0,-22),new Vector2(100,24));cancelActive.gameObject.AddComponent<Image>().color=new Color(.32f,.16f,.18f,.95f);Label(cancelActive,"Cancel",cancelActive.sizeDelta,13);
            Register(cancelActive,"Stop this year’s generation without regenerating");
            generationProgress.gameObject.SetActive(false);
            var browse=Rect("Generation exploration hint",generationProgress,new Vector2(0,-65),new Vector2(370,42));
            var browseBackground=browse.gameObject.AddComponent<Image>();browseBackground.color=new Color(.025f,.065f,.08f,.78f);browseBackground.raycastTarget=false;
            Label(browse,GenerationBrowseHint,new Vector2(356,36),13);
            queuedProgress=Rect("Queued scene status",root,new Vector2(0,150),new Vector2(280,70));
            var queuedBackground=queuedProgress.gameObject.AddComponent<Image>();queuedBackground.color=new Color(.025f,.065f,.10f,.9f);queuedBackground.raycastTarget=false;
            queuedText=Label(queuedProgress,"",new Vector2(270,32),13);queuedText.rectTransform.anchoredPosition=new Vector2(0,15);
            moveQueuedFirst=Rect("Move queued scene to front",queuedProgress,new Vector2(-47,-17),new Vector2(160,26));
            var moveImage=moveQueuedFirst.gameObject.AddComponent<Image>();moveImage.color=new Color(.08f,.25f,.36f,.95f);moveImage.raycastTarget=false;
            Label(moveQueuedFirst,"Move to front",moveQueuedFirst.sizeDelta,13);queuedProgress.gameObject.SetActive(false);
            cancelQueued=Rect("Cancel queued scene",queuedProgress,new Vector2(87,-17),new Vector2(88,26));cancelQueued.gameObject.AddComponent<Image>().color=new Color(.32f,.16f,.18f,.95f);Label(cancelQueued,"Cancel",cancelQueued.sizeDelta,13);
            Register(cancelQueued,"Cancel this year’s queued job or pending retry");
            Register(moveQueuedFirst,"Prioritize this waiting request. Active jobs keep running.");
            tooltip=Rect("HUD tooltip",root,Vector2.zero,new Vector2(200,28));tooltip.gameObject.AddComponent<Image>().color=new Color(.18f,.19f,.21f,1);tooltipText=Label(tooltip,"",new Vector2(188,24),12);tooltipText.color=Color.white;tooltip.gameObject.SetActive(false);
            Visible=PlayerPrefs.GetInt(PreferenceKey,1)!=0;RefreshToggle();Configure(XRSettings.isDeviceActive);
            Register(toggle,"Toggle HUD · Tab / left Menu");
            Register(roomToggle,"Toggle passthrough / immersive VR");
            Register(home,"Go in front of the first child · reset tree rotation and size");
            UnityEngine.Object.FindAnyObjectByType<NavigationPreferences>()?.RegisterHud(this);
        }
        public void Register(RectTransform target,string text,Func<bool> enabled=null)
        {if(target==null)return;var tip=tips.Find(t=>t.Target==target);if(tip==null){tip=new Tip{Target=target,Canvas=target.GetComponentInParent<Canvas>()};tips.Add(tip);}tip.Text=text;tip.Enabled=enabled;}
        public void RegisterMarker(RectTransform target,string text)
        {
            var tip=tips.Find(t=>t.Target==target);if(tip==null){Register(target,text);tip=tips[tips.Count-1];}tip.Text=text;tip.Marker=true;
        }
        public void RegisterMarker(Transform target,string text,Func<bool> enabled)
        {
            var tip=tips.Find(t=>t.WorldTarget==target);if(tip==null){tip=new Tip{WorldTarget=target,Marker=true,Enabled=enabled};tips.Add(tip);}tip.Text=text;
        }
        static bool ContainsWorld(Transform target,Ray ray,out float score)
        {
            score=float.PositiveInfinity;if(Vector3.Dot(ray.direction,target.forward)<=0)return false;
            var plane=new Plane(target.forward,target.position);if(!plane.Raycast(ray,out float d))return false;
            var p=target.InverseTransformPoint(ray.GetPoint(d));
            score=Mathf.Abs(p.x*target.lossyScale.x);return Mathf.Abs(p.x)<=Mathf.Max(.5f,.04f/target.localScale.x)&&Mathf.Abs(p.y)<=Mathf.Max(.5f,.10f/target.localScale.y);
        }
        public void SetVisible(bool visible)
        {
            Visible=visible;PlayerPrefs.SetInt(PreferenceKey,visible?1:0);PlayerPrefs.Save();HideTip();RefreshToggle();
            gallery.Overlay?.SetHudVisible(visible);gallery.TreeControls?.SetHudVisible(visible);gallery.Tutorial?.SetHudVisible(visible);UnityEngine.Object.FindAnyObjectByType<NavigationPreferences>()?.SetHudVisible(visible);
        }
        void RefreshToggle(){if(toggleText!=null)toggleText.text=Visible?"HUD −":"HUD +";}
        public void RefreshSceneCaption()
        {
            captionPerson=gallery.SelectedId;captionYear=captionPerson!=null?gallery.Year(captionPerson):int.MinValue;
            CaptionLayout();gallery.Overlay?.RefreshTitle();
        }
        void CaptionLayout()
        {
            float y=wasVr?210:234;
            toggle.anchoredPosition=new Vector2(0,y);roomToggle.anchoredPosition=new Vector2(80,y);home.anchoredPosition=new Vector2(wasVr?160:80,y);
            if(generationProgress!=null)generationProgress.anchoredPosition=new Vector2(0,wasVr?122:158);
            if(queuedProgress!=null)queuedProgress.anchoredPosition=new Vector2(0,wasVr?114:150);
        }
        void Update()
        {
            Configure(XRSettings.isDeviceActive);UIFade.Set(canvas,!(Visible&&gallery.IsTimelineDragging));
            if(captionPerson!=gallery.SelectedId||(captionPerson!=null&&captionYear!=gallery.Year(captionPerson)))RefreshSceneCaption();
            bool modal=NavigationPreferences.PanelOpen||(gallery.Overlay?.PanelOpen??false)||(gallery.AISetup?.IsOpen??false)||(gallery.LocationEditor!=null&&gallery.LocationEditor.IsOpen)||(gallery.Tutorial!=null&&gallery.Tutorial.IsOpen)||(gallery.Library!=null&&gallery.Library.IsOpen)||(gallery.Batch!=null&&gallery.Batch.IsOpen);
            UIFade.Set(toggle.gameObject,!modal&&!gallery.IsTimelineDragging);UIFade.Set(roomToggle.gameObject,wasVr&&!modal);UIFade.Set(home.gameObject,Visible&&gallery.IsTree&&!modal&&!gallery.IsTimelineDragging);if(modal)HideTip();
            var room=gallery.Passthrough;
            if(room!=null&&(lastRoomActive!=room.Active||lastRoomStarting!=room.Starting))
            {lastRoomActive=room.Active;lastRoomStarting=room.Starting;roomText.text=room.Starting?"…":room.Active?"VR":"Room";}
            bool blink=gallery.SelectedId!=null&&room!=null&&room.Active&&UIFade.Visible(roomToggle.gameObject);
            roomBackground.color=blink?Color.Lerp(RoomIdleColor,RoomBlinkColor,.5f+.5f*Mathf.Sin(Time.unscaledTime*Mathf.PI*2)):RoomIdleColor;
            UpdateGenerationProgress(modal||(gallery.Overlay?.PanelOpen??false));
        }
        void UpdateGenerationProgress(bool modal)
        {
            bool eligible=!modal&&gallery.SelectedId!=null&&gallery.Generation!=null;
            if(eligible&&(queuedRevision!=gallery.Generation.Revision||queuedTree!=gallery.TreeId||queuedPerson!=gallery.SelectedId||queuedYear!=gallery.Year(gallery.SelectedId)))
            {queuedRevision=gallery.Generation.Revision;queuedTree=gallery.TreeId;queuedPerson=gallery.SelectedId;queuedYear=gallery.Year(gallery.SelectedId);queuedTarget=gallery.Generation.QueuedTarget(queuedTree,queuedPerson,queuedYear);}
            bool waiting=eligible&&queuedTarget!=null;if(UIFade.Visible(queuedProgress.gameObject)!=waiting)UIFade.Set(queuedProgress.gameObject,waiting);
            if(waiting)
            {
                string text=queuedYear+" is in queue"+(gallery.Generation.QueuePaused?" · Paused":queuedTarget.queuePriority>0?" · Prioritized":"");
                double delay=queuedTarget.notBeforeUnix-SceneRetryPolicy.UnixNow;if(delay>0)text+="\nRetry in "+Math.Ceiling(delay)+"s";
                if(queuedLabel!=text){queuedLabel=text;queuedText.text=text;}
            }
            string label=null;bool show=!waiting&&!modal&&gallery.SelectedId!=null&&gallery.Generation!=null&&gallery.Generation.TryGetProgress(gallery.TreeId,gallery.SelectedId,gallery.Year(gallery.SelectedId),out label);
            if(UIFade.Visible(generationProgress.gameObject)!=show)UIFade.Set(generationProgress.gameObject,show);
            if(!show)return;
            activeTree=gallery.TreeId;activePerson=gallery.SelectedId;activeYear=gallery.Year(activePerson);
            string percent=gallery.Generation.PercentText(gallery.TreeId,gallery.SelectedId,gallery.Year(gallery.SelectedId));
            if(percent.Length>0)label+="\n"+percent;
            if(generationLabel!=label){generationLabel=label;generationText.text=label;}
        }
        void Configure(bool vr)
        {
            if(configured&&wasVr==vr)return;configured=true;wasVr=vr;
            canvas.renderMode=vr?RenderMode.WorldSpace:RenderMode.ScreenSpaceOverlay;canvas.worldCamera=Camera.main;
            if(vr){root.SetParent(Camera.main.transform,false);root.sizeDelta=new Vector2(960,540);root.localPosition=new Vector3(0,0,1.6f);root.localRotation=Quaternion.identity;root.localScale=Vector3.one*.0018f;}
            else{root.SetParent(gallery.transform,false);root.localScale=Vector3.one;}
            UIFade.Set(roomToggle.gameObject,vr);CaptionLayout();OverlayComfort.TextDensity(canvas,vr);HideTip();
        }
        static bool Contains(RectTransform rect,Ray ray,bool vr)
        {
            if(!UIFade.Interactive(rect))return false;if(!vr)return RectTransformUtility.RectangleContainsScreenPoint(rect,Mouse.current?.position.ReadValue()??Vector2.zero);
            var plane=new Plane(rect.forward,rect.position);return plane.Raycast(ray,out float d)&&rect.rect.Contains(rect.InverseTransformPoint(ray.GetPoint(d)));
        }
        public bool HandleInput(Ray ray,bool vr,bool pressed,bool held,bool shortcut)
        {
            if(gallery.IsTimelineDragging){HideTip();return false;}
            if(NavigationPreferences.PanelOpen||(gallery.Overlay?.PanelOpen??false)||(gallery.AISetup?.IsOpen??false)||(gallery.LocationEditor!=null&&gallery.LocationEditor.IsOpen)||(gallery.Tutorial!=null&&gallery.Tutorial.IsOpen)||(gallery.Library!=null&&gallery.Library.IsOpen)||(gallery.Batch!=null&&gallery.Batch.IsOpen)){HideTip();return false;}
            if(!vr&&!Visible&&((Keyboard.current?.pKey.wasPressedThisFrame??false)||(Keyboard.current?.hKey.wasPressedThisFrame??false)))SetVisible(true);
            if(shortcut){SetVisible(!Visible);return true;}
            if(gallery.IsTree&&Contains(home,ray,vr)){if(pressed)UnityEngine.Object.FindAnyObjectByType<GalleryNavigation>()?.GoHome();Observe(ray,vr,held);return true;}
            if(queuedProgress.gameObject.activeInHierarchy&&Contains(queuedProgress,ray,vr))
            {
                if(pressed&&Contains(cancelQueued,ray,vr)){gallery.Generation.CancelScene(queuedTree,queuedPerson,queuedYear);UpdateGenerationProgress(false);}
                else if(pressed&&Contains(moveQueuedFirst,ray,vr)){gallery.Generation.MoveToFront(gallery.TreeId,gallery.SelectedId,gallery.Year(gallery.SelectedId));UpdateGenerationProgress(false);}
                Observe(ray,vr,held);return pressed;
            }
            if(generationProgress.gameObject.activeInHierarchy&&Contains(generationProgress,ray,vr))
            {
                if(pressed&&Contains(cancelActive,ray,vr)){gallery.Generation.CancelScene(activeTree,activePerson,activeYear);UpdateGenerationProgress(false);}
                Observe(ray,vr,held);return pressed;
            }
            if(UIFade.Interactive(roomToggle)&&Contains(roomToggle,ray,vr)){if(pressed){gallery.Passthrough?.Toggle();return true;}Observe(ray,vr,held);return false;}
            if(UIFade.Interactive(toggle)&&Contains(toggle,ray,vr)){if(pressed){SetVisible(!Visible);return true;}Observe(ray,vr,held);return false;}
            Observe(ray,vr,held);return false;
        }
        public void Observe(Ray ray,bool vr,bool held)
        {
            if(held){HideTip();return;}
            Tip next=null;float closest=float.PositiveInfinity;
            for(int i=tips.Count-1;i>=0;i--)
            {
                var tip=tips[i];var anchor=tip.WorldTarget!=null?tip.WorldTarget:tip.Target;
                if(anchor==null){tips.RemoveAt(i);continue;}
                if(string.IsNullOrEmpty(tip.Text)||!anchor.gameObject.activeInHierarchy||(tip.Enabled!=null&&!tip.Enabled()))continue;
                float score=0;
                if(tip.WorldTarget!=null){if(!gallery.IsTree||!ContainsWorld(anchor,ray,out score))continue;}
                else
                {
                    if(tip.Canvas==null||!tip.Canvas.enabled||!UIFade.Interactive(anchor))continue;
                    bool contains=Contains(tip.Target,ray,vr);
                    if(tip.Marker)
                    {
                        Vector2 point;
                        if(vr){var plane=new Plane(tip.Target.forward,tip.Target.position);point=plane.Raycast(ray,out float d)?(Vector2)tip.Target.InverseTransformPoint(ray.GetPoint(d)):new Vector2(float.MaxValue,float.MaxValue);}
                        else RectTransformUtility.ScreenPointToLocalPointInRectangle(tip.Target,Mouse.current?.position.ReadValue()??Vector2.zero,null,out point);
                        score=Mathf.Abs(point.x)*Mathf.Abs(tip.Target.lossyScale.x);
                        var area=tip.Target.rect;area.xMin-=5;area.xMax+=5;area.yMin-=6;area.yMax+=6;contains=area.Contains(point);
                    }
                    if(!contains)continue;
                }
                if(tip.Marker&&score>=closest)continue;next=tip;closest=score;if(!tip.Marker)break;
            }
            if(next==null){HideTip();return;}
            if(hovered!=next){hovered=next;hoveredAt=Time.unscaledTime;UIFade.Set(tooltip.gameObject,false);}
            if(Time.unscaledTime-hoveredAt<.4f)return;
            tooltipText.text=next.Target==roomToggle?(gallery.Passthrough?.Status??next.Text):next.Text;
            float height=next.Marker?56:28;tooltip.sizeDelta=new Vector2(200,height);tooltipText.rectTransform.sizeDelta=new Vector2(188,height-4);tooltipText.resizeTextForBestFit=true;tooltipText.resizeTextMinSize=10;tooltipText.resizeTextMaxSize=12;
            var target=next.WorldTarget!=null?next.WorldTarget:next.Target;Vector2 local;
            if(next.WorldTarget!=null&&!vr)RectTransformUtility.ScreenPointToLocalPointInRectangle(root,Camera.main.WorldToScreenPoint(target.position),null,out local);
            else local=root.InverseTransformPoint(target.position);
            float half=next.Target!=null?next.Target.rect.height*Mathf.Abs(next.Target.lossyScale.y/root.lossyScale.y)*.5f:0;
            float offset=height*.5f+8;float y=local.y+half+offset;if(y>root.rect.height*.5f-height*.5f)y=local.y-half-offset;
            tooltip.anchoredPosition=new Vector2(Mathf.Clamp(local.x,-root.rect.width*.5f+108,root.rect.width*.5f-108),Mathf.Clamp(y,-root.rect.height*.5f+height*.5f,root.rect.height*.5f-height*.5f));UIFade.Set(tooltip.gameObject,true);tooltip.SetAsLastSibling();
        }
        void HideTip(){hovered=null;if(tooltip!=null)UIFade.Set(tooltip.gameObject,false);}
        static RectTransform Rect(string name,Transform parent,Vector2 at,Vector2 size){var r=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();r.SetParent(parent,false);r.anchorMin=r.anchorMax=new Vector2(.5f,.5f);r.anchoredPosition=at;r.sizeDelta=size;return r;}
        Text Label(Transform parent,string value,Vector2 size,int fontSize){var t=Rect("Label",parent,Vector2.zero,size).gameObject.AddComponent<Text>();t.font=gallery.font;t.text=value;t.fontSize=fontSize;t.color=Color.white;t.alignment=TextAnchor.MiddleCenter;t.raycastTarget=false;return t;}
    }
}
