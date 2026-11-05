using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.XR;
namespace AncestorWalk
{
    public sealed partial class SceneOverlay : MonoBehaviour
    {
        sealed class Hit {public RectTransform rect;public Action action;public Func<bool> enabled;public bool slider;public Vector2 padding;}
        readonly List<Hit> hits=new();
        readonly OverlayComfort comfort=new();readonly List<RectTransform> dockButtons=new();
        AncestorGallery gallery;Person person;Canvas canvas;RectTransform root, timeline,info,actions,rail,knob,timelineToggle;
        Text sceneTitle,yearText,quickGenerateText,statusText,locationText,savedLocationText;Font font;
        Image knobImage;RectTransform quickGenerate;
        static readonly Color GeneratedYearColor=new Color(.95f,.72f,.3f);
        readonly Text[] placeLabels=new Text[4];
        readonly string[] previewPlaces=new string[4];
        readonly float[] locationWidths=new float[4];
        readonly TextGenerator locationMeasure=new();
        string pendingLocation;int pendingLocationYear;bool pendingLocationCommitted,pulseLocation;float pendingLocationUntil;
        Canvas returnCanvas;RectTransform returnRoot,returnButton;
        string savedPlace,titleName;int savedMoveYear=int.MinValue;int thumbTipYear=int.MinValue;TimelineMarker[] thumbTipData;
        readonly Dictionary<string,float> placeWeights=new(StringComparer.OrdinalIgnoreCase);
        TimelineMarker[] ticksData; readonly List<GameObject> ticks=new();
        readonly List<Image> tickImages=new();
        readonly List<Image> blinkingTicks=new();float lastBlink=-1;
        bool timelineOpen,infoOpen,dragging,wasVr,validRange;int birth,end;
        string locationNotice;int noticeYear;float noticeUntil;
        int actionYear=int.MinValue,actionRevision=-1;string actionPlace;int[] actionYears;
        float sliderGrabOffset;bool actionReady;
        string preferencePrefix;
        public void LocationSaved(int year,bool removed,bool generating=false)
        {
            noticeYear=year;noticeUntil=generating?0:Time.unscaledTime+8;
            locationNotice=(removed?"Entry removed. ":"Saved. ")+gallery.Place(person.Id)+" · "+year;
            Apply(false);Refresh();
        }
        public bool TimelineOpen=>timelineOpen;
        public bool IsDraggingYear=>dragging;
        public void Build(AncestorGallery owner,Person p)
        {
            gallery=owner;person=p;font=owner.font;validRange=LifeEndpointScenes.TrySceneRange(p,out birth,out end);
            owner.Generation.SceneCompleted+=LocationGenerationCompleted;
            canvas=gameObject.AddComponent<Canvas>();root=gameObject.GetComponent<RectTransform>();canvas.sortingOrder=90;
            var scaler=gameObject.AddComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(960,540);scaler.matchWidthOrHeight=.5f;
            preferencePrefix="overlay."+owner.TreeId+"."+PanoramaCache.PersonKey(p.Id)+".";
            timelineOpen=true;infoOpen=PlayerPrefs.GetInt(preferencePrefix+"info",0)!=0;
            returnRoot=new GameObject("Persistent scene return",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler)).GetComponent<RectTransform>();
            returnCanvas=returnRoot.GetComponent<Canvas>();returnCanvas.sortingOrder=250;
            var returnScaler=returnRoot.GetComponent<CanvasScaler>();returnScaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;returnScaler.referenceResolution=new Vector2(960,540);returnScaler.matchWidthOrHeight=.5f;
            returnButton=Rect("Return to tree",returnRoot,new Vector2(-440,-238),new Vector2(44,40));Image(returnButton,new Color(.08f,.3f,.29f,.9f));Label(returnButton,"←",Vector2.zero,returnButton.sizeDelta,22);owner.Hud?.Register(returnButton,"Return to family tree");
            Button(root,"i",new Vector2(-386,-238),new Vector2(44,40),()=>{infoOpen=!infoOpen;if(infoOpen)timelineOpen=false;Apply();});
            var clock=Button(root,"",new Vector2(386,-238),new Vector2(44,40),ToggleTimeline).transform.parent;
            clock.name="Toggle timeline";timelineToggle=(RectTransform)clock;
            for(int i=0;i<12;i++){float angle=i*Mathf.PI/6;var tick=Rect("Clock tick",clock,new Vector2(Mathf.Sin(angle)*12,Mathf.Cos(angle)*12),new Vector2(2,3));tick.localRotation=Quaternion.Euler(0,0,-i*30);Image(tick,Color.white);}
            Image(Rect("Clock hour hand",clock,new Vector2(3,0),new Vector2(8,2)),Color.white);
            Image(Rect("Clock minute hand",clock,new Vector2(0,4),new Vector2(2,10)),Color.white);
            quickGenerateText=Button(root,"Regenerate selected year",new Vector2(370,0),new Vector2(180,48),()=>gallery.GenerateScene(person.Id));
            quickGenerate=(RectTransform)quickGenerateText.transform.parent;quickGenerateText.fontSize=16;
            statusText=Label(quickGenerate,"",new Vector2(0,-51),new Vector2(180,48),12);
            owner.Hud?.Register(quickGenerate,"Generate or regenerate the selected year");
            timeline=Panel("Bottom timeline",new Vector2(0,-129),new Vector2(900,144));
            sceneTitle=Label(timeline,"",new Vector2(0,60),new Vector2(820,18),15);sceneTitle.name="Scene title";StretchWidth(sceneTitle.rectTransform,80);
            sceneTitle.supportRichText=false;sceneTitle.resizeTextForBestFit=true;sceneTitle.resizeTextMinSize=11;sceneTitle.resizeTextMaxSize=15;
            statusText.rectTransform.SetParent(root,false);statusText.rectTransform.anchoredPosition=new Vector2(370,-51);
            quickGenerate.SetParent(timeline,false);quickGenerate.anchorMin=quickGenerate.anchorMax=new Vector2(1,.5f);quickGenerate.anchoredPosition=new Vector2(-72,-7);quickGenerate.sizeDelta=new Vector2(28,24);quickGenerateText.rectTransform.sizeDelta=quickGenerate.sizeDelta;quickGenerateText.enabled=false;
            var regenerationIcon=Rect("Regeneration icon",quickGenerate,Vector2.zero,new Vector2(24,24)).gameObject.AddComponent<RegenerateIcon>();regenerationIcon.raycastTarget=false;regenerationIcon.color=Color.white;
            quickGenerate.name="Timeline generate selected year";var generateHit=hits.Find(h=>h.rect==quickGenerate);hits.Remove(generateHit);generateHit.padding=new Vector2(6,4);hits.Add(generateHit);
            savedLocationText=Label(timeline,"",new Vector2(-100,39),new Vector2(560,18),16);savedLocationText.name="Saved location";savedLocationText.gameObject.SetActive(false);StretchWidth(savedLocationText.rectTransform,280);
            yearText=Label(timeline,"",new Vector2(0,-5),new Vector2(50,18),18);
            for(int i=0;i<placeLabels.Length;i++)
            {
                var label=placeLabels[i]=Label(timeline,"",new Vector2(0,27),new Vector2(860,18),14);
                label.name="Blended location "+i;StretchWidth(label.rectTransform,40);label.supportRichText=false;
                label.resizeTextForBestFit=false;label.horizontalOverflow=HorizontalWrapMode.Overflow;label.verticalOverflow=VerticalWrapMode.Truncate;label.color=Color.clear;
            }
            rail=Rect("Year slider",timeline,new Vector2(-50,-31),new Vector2(720,16));StretchWidth(rail,180);Image(rail,new Color(.3f,.4f,.42f,.8f));
            var sliderHit=Rect("Year slider hit area",rail,Vector2.zero,new Vector2(864,44));StretchWidth(sliderHit,-24);hits.Add(new Hit{rect=sliderHit,slider=true});
            knob=Rect("Selected year",rail,Vector2.zero,new Vector2(10,20));Image(knob,owner.teal.color);knobImage=knob.GetComponent<Image>();
            var birthLabel=Label(timeline,validRange&&birth!=end?birth.ToString():"",new Vector2(40,-53),new Vector2(50,16),14).rectTransform;
            birthLabel.name="Birth year";birthLabel.anchorMin=birthLabel.anchorMax=new Vector2(0,.5f);
            var endLabel=Label(timeline,validRange&&birth!=end?end.ToString():"",new Vector2(-140,-53),new Vector2(50,16),14).rectTransform;
            endLabel.name="End year";endLabel.anchorMin=endLabel.anchorMax=new Vector2(1,.5f);
            var timelineCollapse=Collapse(timeline,new Vector2(-20,-16),()=>{timelineOpen=false;Apply();});
            timelineCollapse.anchorMin=timelineCollapse.anchorMax=Vector2.one;timelineCollapse.sizeDelta=new Vector2(28,24);var collapseText=timelineCollapse.GetComponentInChildren<Text>();collapseText.fontSize=22;collapseText.rectTransform.sizeDelta=timelineCollapse.sizeDelta;hits.Find(h=>h.rect==timelineCollapse).padding=new Vector2(4,4);
            info=Panel("Person information",new Vector2(-265,20),new Vector2(390,130));
            Label(info,p.Name,new Vector2(-18,35),new Vector2(310,28),22);
            string lifespanEnd=LifeTimeline.IsLiving(p)?DateTime.Now.Year+" · Living":"Died: "+p.DeathDate+(string.IsNullOrEmpty(p.DeathPlace)?"":" · "+p.DeathPlace);
            Label(info,"Born: "+p.BirthDate+" · "+p.BirthPlace+"\n"+lifespanEnd,new Vector2(0,-15),new Vector2(360,70),16);
            Collapse(info,new Vector2(169,40),()=>{infoOpen=false;Apply();});
            locationText=Button(timeline,"Edit Location",new Vector2(-62,-44),new Vector2(100,28),()=>{if(validRange)gallery.EditLocation(person.Id);});locationText.fontSize=12;
            actions=(RectTransform)locationText.transform.parent;actions.anchorMin=actions.anchorMax=new Vector2(1,.5f);hits.Find(h=>h.rect==actions).padding=new Vector2(4,6);owner.Hud?.Register(actions,"Edit location for the selected year");
            BuildGuide();
            Button(root,"PHOTOS",new Vector2(-210,-238),new Vector2(100,40),()=>gallery.Library?.OpenPhotos(person.Id));
            if(owner.Hud!=null)foreach(var hit in hits)
            {
                if(hit.rect.name=="Toggle timeline")owner.Hud.Register(hit.rect,"Show or hide timeline",()=>!GuideOpen);
                else if(hit.rect.name=="i")owner.Hud.Register(hit.rect,"Person information",()=>!infoOpen&&!GuideOpen);
                else if(hit.rect.name=="←")owner.Hud.Register(hit.rect,"Return to family tree");
                else if(hit.rect.name=="ASK")owner.Hud.Register(hit.rect,"Ask about this place",()=>!GuideOpen);
            }
            timeline.gameObject.SetActive(false);info.gameObject.SetActive(false);
            foreach(Transform child in root)if(child.gameObject.activeSelf)UIFade.Set(child.gameObject,true);
            wasVr=!XRSettings.isDeviceActive;Configure();Apply(false);Refresh();SetHudVisible(owner.HudVisible);
        }
        void Configure()=>ConfigureForMode(XRSettings.isDeviceActive);
        void ConfigureForMode(bool vr)
        {
            if(wasVr==vr)return;wasVr=vr;
            canvas.renderMode=vr?RenderMode.WorldSpace:RenderMode.ScreenSpaceOverlay;canvas.worldCamera=Camera.main;
            if(vr){root.SetParent(Camera.main.transform,false);root.sizeDelta=new Vector2(960,540);root.localPosition=new Vector3(0,0,1.6f);root.localRotation=Quaternion.identity;root.localScale=Vector3.one*.0018f;}
            else{root.SetParent(gallery.transform,false);root.localScale=Vector3.one;}
            returnCanvas.renderMode=canvas.renderMode;returnCanvas.worldCamera=Camera.main;
            returnRoot.SetParent(root.parent,false);returnRoot.localPosition=root.localPosition;returnRoot.localRotation=root.localRotation;returnRoot.localScale=root.localScale;returnRoot.sizeDelta=root.sizeDelta;
            returnButton.anchoredPosition=vr?new Vector2(-350,-215):new Vector2(-440,-238);OverlayComfort.TextDensity(returnCanvas,vr);
            OverlayComfort.TextDensity(canvas,vr);
            foreach(var button in dockButtons){var p=comfort.Original(button);comfort.Set(button,vr,new Vector2(p.x-Mathf.Sign(p.x)*90,-215));}
            comfort.Set(quickGenerate,vr,new Vector2(-72,-7),new Vector2(28,24));comfort.Set(timeline,vr,new Vector2(0,-106),new Vector2(800,144));comfort.Set(info,vr,new Vector2(-175,65));comfort.Set(actions,vr,new Vector2(-62,-44),new Vector2(100,28));
            ticksData=null;thumbTipData=null; // Reposition pooled markers for the resized rail on the next refresh.
            ConfigureGuideComfort(vr);
        }
        bool CanShow=>!NavigationPreferences.PanelOpen&&!(gallery.Library?.IsOpen??false)&&!(gallery.AISetup?.IsOpen??false)&&!(gallery.Batch?.IsOpen??false)&&!(gallery.Tutorial?.IsOpen??false)&&(gallery.LocationEditor==null||!gallery.LocationEditor.IsOpen);
        void Update(){Configure();UIFade.Set(canvas,CanShow);Refresh();RefreshGuideProcessing();}
        void LateUpdate()
        {
            gallery.ScenePlaceWeights(placeWeights);
            if(placeWeights.Count==0)placeWeights[string.IsNullOrWhiteSpace(savedPlace)?"Location unknown":savedPlace]=1;
            RefreshPendingLocation();
            UpdateLocationLabels();
        }
        void LocationGenerationCompleted(string tree,string id,int year)
        {if(tree==gallery.TreeId&&id==person.Id&&pendingLocation!=null&&pendingLocationYear==year){pendingLocationCommitted=true;pendingLocationUntil=Time.unscaledTime+15;}}
        void RefreshPendingLocation()
        {
            int year=gallery.Year(person.Id);
            if(gallery.Generation.TryGetGeneratingLocation(gallery.TreeId,person.Id,year,out string place))
            {
                if(pendingLocation!=place||pendingLocationYear!=year)pendingLocationCommitted=false;
                pendingLocation=place;pendingLocationYear=year;
            }
            else if(pendingLocation==null||pendingLocationYear!=year||!pendingLocationCommitted||Time.unscaledTime>=pendingLocationUntil||placeWeights.ContainsKey(pendingLocation))
            {pendingLocation=null;pendingLocationCommitted=false;}
            pulseLocation=pendingLocation!=null;
            if(pulseLocation){placeWeights.Clear();placeWeights[pendingLocation]=1;}
        }
        void UpdateLocationLabels()
        {
            // The environment combines identical places and includes both sides of replacement fades.
            int i=0,count=Mathf.Min(placeWeights.Count,placeLabels.Length);
            foreach(var entry in placeWeights)
            {
                if(i>=placeLabels.Length)break;var label=placeLabels[i];
                bool changed=previewPlaces[i]!=entry.Key;previewPlaces[i]=entry.Key;
                float weight=Mathf.Clamp01(entry.Value);
                var rect=label.rectTransform;bool single=count==1;float left=single?0:(i%2)*.5f;
                var min=new Vector2(left,.5f);var max=new Vector2(single?1:left+.5f,.5f);
                var size=new Vector2(single?-40:-30,18);var position=new Vector2(0,count>2?40-26*(i/2):27);
                if(rect.anchorMin!=min)rect.anchorMin=min;if(rect.anchorMax!=max)rect.anchorMax=max;
                if(rect.sizeDelta!=size)rect.sizeDelta=size;if(rect.anchoredPosition!=position)rect.anchoredPosition=position;
                float width=rect.rect.width;if(changed||Mathf.Abs(locationWidths[i]-width)>.5f){locationWidths[i]=width;label.text=FitLocation(label,entry.Key,width);}
                label.color=new Color(1,1,1,weight*(pulseLocation ? .8f+.05f*Mathf.Sin(Time.unscaledTime*3.5f) : 1));i++;
            }
            while(i<placeLabels.Length){if(previewPlaces[i]!=null){previewPlaces[i]=null;placeLabels[i].text="";}placeLabels[i++].color=Color.clear;}
        }
        string FitLocation(Text label,string text,float width)
        {
            text=(text??"").Replace('\n',' ').Replace('\r',' ');var settings=label.GetGenerationSettings(Vector2.zero);settings.horizontalOverflow=HorizontalWrapMode.Overflow;settings.verticalOverflow=VerticalWrapMode.Overflow;
            float Measure(string value)=>locationMeasure.GetPreferredWidth(value,settings)/label.pixelsPerUnit;
            if(Measure(text)<=width)return text;
            int low=0,high=text.Length;while(low<high){int mid=(low+high+1)/2;if(Measure(text.Substring(0,mid)+"…")<=width)low=mid;else high=mid-1;}
            if(low>0&&char.IsHighSurrogate(text[low-1]))low--;return text.Substring(0,low).TrimEnd()+"…";
        }
        public bool HandleReturn(Ray ray,bool vr,bool pressed)
        {
            if(returnButton==null||!Point(returnButton,ray,vr,out var point)||!returnButton.rect.Contains(point))return false;
            if(pressed&&!(gallery.Library?.Busy??false))
            {
                gallery.LocationEditor?.Close();gallery.Library?.Close();gallery.AISetup?.Close();gallery.Batch?.Close();gallery.Tutorial?.Dismiss();
                UnityEngine.Object.FindAnyObjectByType<NavigationPreferences>()?.SetOpen(false);CloseGuide();
                UnityEngine.Object.FindFirstObjectByType<GalleryNavigation>().Return();
            }
            return true;
        }
        public void ToggleTimeline(){timelineOpen=!timelineOpen;if(timelineOpen)infoOpen=false;Apply();}
        void Apply(bool persist=true){RestorePanelUI();UIFade.Set(timeline.gameObject,timelineOpen);UIFade.Set(info.gameObject,infoOpen);IsolatePanel();dragging=false;if(!persist)return;PlayerPrefs.SetInt(preferencePrefix+"timeline",timelineOpen?1:0);PlayerPrefs.SetInt(preferencePrefix+"info",infoOpen?1:0);PlayerPrefs.Save();}
        public void RefreshTitle()
        {
            if(sceneTitle==null||gallery==null||person==null||gallery.SelectedId!=person.Id)return;string name=person.Name;
            if(titleName==name)return;
            titleName=name;sceneTitle.text=string.IsNullOrWhiteSpace(name)?"Unnamed person":name;
        }
        void Refresh()
        {
            RefreshTitle();
            int year=gallery.Year(person.Id);yearText.text=validRange?year.ToString():"Lifespan unavailable";
            int moveYear=birth;foreach(var entry in gallery.Locations(person.Id).entries)if(entry.year<=year&&entry.year>moveYear)moveYear=entry.year;
            var place=gallery.Place(person.Id);if(savedPlace!=place||savedMoveYear!=moveYear){savedPlace=place;savedMoveYear=moveYear;savedLocationText.text=validRange?place+" · "+moveYear:place;}
            knob.gameObject.SetActive(validRange&&birth!=end);rail.gameObject.SetActive(validRange&&birth!=end);
            knob.anchoredPosition=new Vector2(Mathf.InverseLerp(birth,end,year)*rail.rect.width-rail.rect.width*.5f,0);
            yearText.rectTransform.anchoredPosition=new Vector2(validRange&&birth!=end?knob.anchoredPosition.x+rail.anchoredPosition.x:0,-5);
            yearText.rectTransform.sizeDelta=new Vector2(validRange?50:245,18);
            var years=gallery.AvailableYears(person.Id);
            var data=gallery.Markers(person.Id);
            var thumbColor=TimelineMarker.ThumbColor(year,data,GeneratedYearColor,gallery.teal.color);
            if(knobImage.color!=thumbColor)knobImage.color=thumbColor;
            if(ticksData!=data)
            {
                ticksData=data;
                blinkingTicks.Clear();lastBlink=-1;
                while(ticks.Count<data.Length){var tick=Rect("Cached year",rail,Vector2.zero,new Vector2(3,12));Image(tick,GeneratedYearColor);ticks.Add(tick.gameObject);tickImages.Add(tick.GetComponent<Image>());}
                for(int i=0;i<ticks.Count;i++){bool show=i<data.Length;ticks[i].SetActive(show);if(!show)continue;var tick=tickImages[i].rectTransform;var parent=birth==end?timeline:rail;if(tick.parent!=parent)tick.SetParent(parent,false);tick.anchoredPosition=birth==end?new Vector2(0,-31):new Vector2(Mathf.InverseLerp(birth,end,data[i].Year)*rail.rect.width-rail.rect.width*.5f,0);tick.sizeDelta=data[i].LocationMove||data[i].Blinking?new Vector2(5,16):new Vector2(3,9);tickImages[i].color=data[i].DisplayColor(GeneratedYearColor);gallery.Hud?.RegisterMarker(tick,data[i].Tooltip(data[i].MissingLocation&&!data[i].Generated?null:gallery.Locations(person.Id).At(person,data[i].Year)));if(data[i].Blinking)blinkingTicks.Add(tickImages[i]);}
                knob.SetAsLastSibling();
                foreach(var tick in blinkingTicks)tick.transform.SetAsLastSibling();
            }
            if(timeline.gameObject.activeInHierarchy&&canvas.enabled&&blinkingTicks.Count>0)
            {float alpha=TimelineMarker.BlinkAlpha(Time.time);if(lastBlink!=alpha){lastBlink=alpha;foreach(var tick in blinkingTicks){var color=tick.color;color.a=alpha;tick.color=color;}}}
            if(thumbTipYear!=year||thumbTipData!=data)
            {
                thumbTipYear=year;thumbTipData=data;int covered=TimelineMarker.CoveredMarker(year,data,birth,end,rail.rect.width,knob.rect.width,5);
                gallery.Hud?.RegisterMarker(knob,covered<0?null:data[covered].Tooltip(data[covered].MissingLocation&&!data[covered].Generated?null:gallery.Locations(person.Id).At(person,data[covered].Year)));
            }
            int target=validRange?gallery.GenerationYear(person.Id):0;
            if(actionReady!=gallery.CacheIndexReady||actionYear!=target||actionRevision!=gallery.Generation.Revision||actionPlace!=place||actionYears!=years)
            {actionReady=gallery.CacheIndexReady;actionYear=target;actionRevision=gallery.Generation.Revision;actionPlace=place;actionYears=years;quickGenerateText.text=gallery.SceneActionLabel(person.Id);gallery.Hud?.Register(quickGenerate,quickGenerateText.text);bool enabled=gallery.CanGenerateScene(person.Id);quickGenerate.GetComponent<Image>().color=enabled?new Color(.08f,.3f,.29f,.9f):new Color(.14f,.20f,.21f,.9f);}
            if(!validRange){quickGenerateText.text="YEAR NEEDED";quickGenerateText.color=Color.gray;locationText.color=Color.gray;}
            // Generation progress belongs to the HUD; errors use the single dismissible alert.
            statusText.text=gallery.IsSceneLoading?"Loading scene…":!validRange?"A usable birth/death range or living status is required for year actions.":"";
            if(validRange&&Time.unscaledTime<noticeUntil&&noticeYear==year&&!gallery.Generation.Busy)statusText.text=locationNotice+(gallery.IsAllTime?"\nGenerate to create this location’s scene.":"");
            else if(validRange&&gallery.IsAllTime&&!gallery.Generation.Busy&&string.IsNullOrEmpty(statusText.text))statusText.text="No scene for "+gallery.Place(person.Id)+" in this period. Generate this year’s scene.";
        }
        bool Point(RectTransform rect,Ray ray,bool vr,out Vector2 local)
        {
            if(!vr)return RectTransformUtility.ScreenPointToLocalPointInRectangle(rect,Mouse.current?.position.ReadValue()??Vector2.zero,null,out local);
            var plane=new Plane(rect.forward,rect.position);if(plane.Raycast(ray,out float distance)){local=rect.InverseTransformPoint(ray.GetPoint(distance));return true;}local=default;return false;
        }
        void Drag(Ray ray,bool vr){if(Point(rail,ray,vr,out var p))gallery.SetYear(person.Id,Mathf.RoundToInt(Mathf.Lerp(birth,end,Mathf.InverseLerp(rail.rect.xMin,rail.rect.xMax,p.x-sliderGrabOffset))));}
        void BeginYearDrag(Ray ray,bool vr)
        {
            sliderGrabOffset=0;
            if(Point(knob,ray,vr,out var atThumb)&&new Rect(knob.rect.xMin-12,knob.rect.yMin-8,knob.rect.width+24,knob.rect.height+16).Contains(atThumb)&&Point(rail,ray,vr,out var atRail))sliderGrabOffset=atRail.x-knob.anchoredPosition.x;
            dragging=true;IsolatePanel();Drag(ray,vr);
        }
        public void SetHudVisible(bool visible){if(!visible){infoOpen=false;CancelInteraction();}Apply(false);UIFade.Set(canvas,CanShow);}
        public void CancelInteraction(){dragging=false;IsolatePanel();if(GuideOpen)CloseGuide();}
        public bool HandleInput(Ray ray,bool vr,bool select,bool held)
        {
            if(!CanShow||!UIFade.Interactive(root))return false;
            if(GuideOpen)
            {
                dragging=false;
                for(int i=hits.Count-1;i>=0;i--){var h=hits[i];if(!h.rect.IsChildOf(guidePanel)||!UIFade.Interactive(h.rect)||!Point(h.rect,ray,vr,out var p)||!HitArea(h).Contains(p))continue;if(select&&(h.enabled==null||h.enabled())){if(vr&&h.rect==guideInput.transform)NativeTextInput.Point(guideInput,ray);else h.action?.Invoke();}break;}
                return true;
            }
            if(dragging){if(held)Drag(ray,vr);else {dragging=false;IsolatePanel();}return true;}
            for(int i=hits.Count-1;i>=0;i--){var h=hits[i];if(!UIFade.Interactive(h.rect)||!Point(h.rect,ray,vr,out var p)||!HitArea(h).Contains(p))continue;if(select){if(h.slider){if(validRange)BeginYearDrag(ray,vr);}else if(h.enabled==null||h.enabled())h.action?.Invoke();}return true;}return false;
        }
        void OnDestroy(){if(gallery!=null&&gallery.Generation!=null)gallery.Generation.SceneCompleted-=LocationGenerationCompleted;if(returnRoot!=null)Destroy(returnRoot.gameObject);PlayerPrefs.Save();}
        static Rect HitArea(Hit hit){var area=hit.rect.rect;area.xMin-=hit.padding.x;area.xMax+=hit.padding.x;area.yMin-=hit.padding.y;area.yMax+=hit.padding.y;return area;}
        RectTransform Rect(string name,Transform parent,Vector2 pos,Vector2 size){var r=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();r.SetParent(parent,false);r.anchorMin=r.anchorMax=new Vector2(.5f,.5f);r.sizeDelta=size;r.anchoredPosition=pos;return r;}
        void Image(RectTransform r,Color color){var image=r.gameObject.AddComponent<Image>();image.color=color;image.raycastTarget=false;}
        RectTransform Panel(string name,Vector2 pos,Vector2 size){var r=Rect(name,root,pos,size);Image(r,new Color(.025f,.065f,.08f,.68f));hits.Add(new Hit{rect=r});return r;}
        Text Label(Transform parent,string value,Vector2 pos,Vector2 size,int fontSize){var t=Rect("Overlay label",parent,pos,size).gameObject.AddComponent<Text>();t.font=font;t.text=value;t.fontSize=fontSize;t.alignment=TextAnchor.MiddleCenter;t.color=Color.white;t.raycastTarget=false;return t;}
        Text Button(Transform parent,string title,Vector2 pos,Vector2 size,Action action){var r=Rect(title,parent,pos,size);Image(r,new Color(.08f,.3f,.29f,.8f));hits.Add(new Hit{rect=r,action=action});if(parent==root&&pos.y<-220)dockButtons.Add(r);return Label(r,title,Vector2.zero,size,18);}
        static void StretchWidth(RectTransform rect,float inset)
        {
            rect.anchorMin=new Vector2(0,.5f);rect.anchorMax=new Vector2(1,.5f);var size=rect.sizeDelta;size.x=-inset;rect.sizeDelta=size;
        }
        RectTransform Collapse(RectTransform panel,Vector2 position,Action action)
        {
            var label=Button(panel,"Collapse "+panel.name,position,new Vector2(36,36),action);label.text="−";label.fontSize=26;
            gallery.Hud?.Register((RectTransform)label.transform.parent,"Collapse "+panel.name.ToLowerInvariant());
            return (RectTransform)label.transform.parent;
        }
    }
}
