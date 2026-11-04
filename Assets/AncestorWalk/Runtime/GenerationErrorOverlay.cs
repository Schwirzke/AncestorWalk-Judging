using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR;

namespace AncestorWalk
{
    // One reusable alert for the latest error; dismissal survives view/year changes.
    public sealed class GenerationErrorOverlay : MonoBehaviour
    {
        AncestorGallery gallery;Canvas canvas;RectTransform root,card,close,visit;Text title,body,visitLabel,visitHint;Image visitImage;
        GenerationErrorNotice seen;bool configured,wasVr,navigating,visitEnabled;string visitReason;
        public void Build(AncestorGallery owner)
        {
            gallery=owner;root=(RectTransform)transform;canvas=gameObject.AddComponent<Canvas>();canvas.sortingOrder=260;
            var scaler=gameObject.AddComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(960,540);scaler.matchWidthOrHeight=.5f;
            card=Rect("Latest generation error",root,Vector2.zero,new Vector2(310,164));card.gameObject.AddComponent<Image>().color=new Color(.12f,.10f,.11f,.98f);
            title=Label(card,new Vector2(-8,64),new Vector2(260,20),13);title.alignment=TextAnchor.MiddleLeft;
            body=Label(card,new Vector2(0,18),new Vector2(282,60),12);body.alignment=TextAnchor.UpperLeft;body.resizeTextForBestFit=true;body.resizeTextMinSize=10;body.resizeTextMaxSize=12;
            close=Rect("Close generation error",card,new Vector2(136,64),new Vector2(26,26));close.gameObject.AddComponent<Image>().color=new Color(.28f,.22f,.24f,1);Label(close,Vector2.zero,close.sizeDelta,20).text="×";
            visit=Rect("Take me there",card,new Vector2(0,-38),new Vector2(150,30));visitImage=visit.gameObject.AddComponent<Image>();visitLabel=Label(visit,Vector2.zero,visit.sizeDelta,13);visitLabel.text="Take me there";
            visitHint=Label(card,new Vector2(0,-66),new Vector2(282,24),10);visitHint.color=new Color(.7f,.72f,.73f,1);
            card.gameObject.SetActive(false);Configure();
        }
        void Configure()
        {
            bool vr=XRSettings.isDeviceActive;if(configured&&wasVr==vr)return;configured=true;wasVr=vr;
            canvas.renderMode=vr?RenderMode.WorldSpace:RenderMode.ScreenSpaceOverlay;canvas.worldCamera=Camera.main;
            root.SetParent(vr?Camera.main.transform:gallery.transform,false);
            if(vr){root.sizeDelta=new Vector2(960,540);root.localPosition=new Vector3(0,0,1.55f);root.localScale=Vector3.one*.0018f;}
            else root.localScale=Vector3.one;
            card.anchoredPosition=vr?new Vector2(220,108):new Vector2(300,108);OverlayComfort.TextDensity(canvas,vr);
        }
        void Update()
        {
            if(gallery==null||gallery.Generation==null)return;Configure();var latest=gallery.Generation.LatestError;
            if(latest!=null&&!ReferenceEquals(latest,seen))
            {seen=latest;title.text="Generation error"+(latest.Year>0?" · "+latest.Year:"");body.text=latest.Message;UIFade.Set(card.gameObject,true);}
            RefreshVisit();
        }
        bool CanVisit(out string reason)
        {
            reason="Wait for the scene to open.";if(navigating)return false;
            reason="Close the open panel or finish editing first.";
            if(NavigationPreferences.PanelOpen||NativeTextInput.Editing||gallery.IsTimelineDragging||gallery.LocationEditor?.IsOpen==true||gallery.Library?.IsOpen==true||gallery.AISetup?.IsOpen==true||gallery.Batch?.IsOpen==true||gallery.Tutorial?.IsOpen==true||gallery.Overlay?.PanelOpen==true)return false;
            reason="That person or year is no longer available.";
            return seen!=null&&gallery.Library!=null&&gallery.Library.CanVisitScene(seen.Tree,seen.Person,seen.Year,out reason);
        }
        void RefreshVisit()
        {
            bool enabled=CanVisit(out string reason);
            if(enabled!=visitEnabled||visitReason!=reason)
            {
                visitEnabled=enabled;visitReason=reason;visitImage.color=enabled?new Color(.08f,.3f,.29f,1):new Color(.16f,.18f,.19f,1);visitLabel.color=enabled?Color.white:new Color(.6f,.62f,.63f,1);
                visitHint.text=enabled?"":reason;
            }
            string label=navigating?"Opening…":"Take me there";if(visitLabel.text!=label)visitLabel.text=label;
        }
        public bool HandleInput(Ray ray,bool vr,bool pressed)
        {
            if(!UIFade.Interactive(card)||!Inside(card,ray,vr))return false;
            if(pressed&&Inside(close,ray,vr))UIFade.Set(card.gameObject,false);
            else if(pressed&&Inside(visit,ray,vr)&&CanVisit(out _))
            {
                var target=seen;navigating=true;RefreshVisit();
                gallery.Library.VisitScene(target.Tree,target.Person,target.Year,success=>
                {
                    if(this==null)return;navigating=false;
                    if(success&&ReferenceEquals(seen,target))UIFade.Set(card.gameObject,false);RefreshVisit();
                });
            }
            return true;
        }
        static bool Inside(RectTransform rect,Ray ray,bool vr)
        {
            if(!vr)return RectTransformUtility.RectangleContainsScreenPoint(rect,Mouse.current?.position.ReadValue()??Vector2.zero);
            var plane=new Plane(rect.forward,rect.position);return plane.Raycast(ray,out float d)&&rect.rect.Contains(rect.InverseTransformPoint(ray.GetPoint(d)));
        }
        static RectTransform Rect(string name,Transform parent,Vector2 at,Vector2 size)
        {var rect=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();rect.SetParent(parent,false);rect.anchoredPosition=at;rect.sizeDelta=size;return rect;}
        Text Label(Transform parent,Vector2 at,Vector2 size,int fontSize)
        {var rect=Rect("Alert text",parent,at,size);var text=rect.gameObject.AddComponent<Text>();text.font=gallery.font;text.fontSize=fontSize;text.color=Color.white;text.supportRichText=false;text.alignment=TextAnchor.MiddleCenter;text.raycastTarget=false;return text;}
    }
}
