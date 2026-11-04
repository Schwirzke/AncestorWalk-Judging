using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using UnityEngine.XR;

namespace AncestorWalk
{
    // Desktop look preferences. Headset tracking and snap turns remain independent.
    public sealed class NavigationPreferences : MonoBehaviour
    {
        const string SensitivityKey = "AncestorWalk.MouseLookSensitivity";
        const string InteractionDistanceKey="AncestorWalk.InteractionGenerations";
        public static float InteractionGenerations
        {
            get {float value=PlayerPrefs.GetFloat(InteractionDistanceKey,PreferenceDefaults.InteractionGenerations);return float.IsNaN(value)||float.IsInfinity(value)?PreferenceDefaults.InteractionGenerations:Mathf.Clamp(value,.5f,6);}
        }
        public const float DefaultSensitivity = PreferenceDefaults.LookSensitivity;
        public float TreePanSpeed { get; private set; }
        Text panValueLabel,brightnessLabel,textBoostLabel,spinSpeedLabel;
        Slider brightnessSlider,textBoostSlider,spinSpeedSlider;
        Slider sceneOpacitySlider,controllerSlider;Text sceneOpacityLabel;Canvas canvas;Selectable[] controllerControls;bool configured,wasVr;
        public float LookSensitivity { get; private set; }
        public bool IsOpen => panel != null && UIFade.Visible(panel);
        static NavigationPreferences activeSettings;public static bool PanelOpen=>activeSettings!=null&&activeSettings.IsOpen;
        GameObject root, panel, ownedEventSystem;
        RectTransform toggleRect,collapseRect;
        Slider slider;
        TreeLayoutSettings treeSettings;
        Slider gapSlider,staggerSlider,riseSlider,distanceSlider,closeFadeSlider,sideLockSlider,distantLockSlider;
        Text gapLabel,staggerLabel,riseLabel,distanceLabel,closeFadeLabel,sideLockLabel,distantLockLabel;
        Text valueLabel;
        Font font;
        int lastToggleFrame = -1;
        bool pointerCaptured;AncestorGallery hudOwner;
        public bool CapturingPointer=>pointerCaptured;

        void Awake()
        {
            activeSettings=this;
            var saved = PlayerPrefs.GetFloat(SensitivityKey, DefaultSensitivity);
            LookSensitivity = float.IsNaN(saved) || float.IsInfinity(saved) ? DefaultSensitivity : Mathf.Clamp(saved, .05f, 1f);
            float panSaved=PlayerPrefs.GetFloat("AncestorWalk.TreePanSpeed",PreferenceDefaults.FlightSpeed);
            TreePanSpeed=float.IsNaN(panSaved)||float.IsInfinity(panSaved)?PreferenceDefaults.FlightSpeed:Mathf.Clamp(panSaved,.5f,8);
            font = Font.CreateDynamicFontFromOSFont(new[]{"Arial","Roboto","Noto Sans","Droid Sans"},18);
            root = new GameObject("Desktop settings", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.transform.SetParent(transform, false);
            canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 100;
            var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(960, 540); scaler.matchWidthOrHeight = .5f;
            if (EventSystem.current == null)
                ownedEventSystem = new GameObject("Settings input", typeof(EventSystem), typeof(InputSystemUIInputModule));
            toggleRect = Button(root.transform, "Settings", new Vector2(-112,-30), new Vector2(200,40), () => SetOpen(!IsOpen));
            toggleRect.anchorMin = toggleRect.anchorMax = new Vector2(1,1);
            var panelRect=Rect("Settings panel",root.transform,new Vector2(177,0),new Vector2(330,360));
            panelRect.anchorMin=panelRect.anchorMax=new Vector2(0,.5f);
            panel=panelRect.gameObject;
            panel.AddComponent<Image>().color = new Color(.035f,.075f,.09f,.94f);
            var navigation=Rect("Navigation settings",panel.transform,new Vector2(0,-15),new Vector2(450,430));
            var layout=Rect("Tree layout settings",panel.transform,new Vector2(0,-15),new Vector2(450,430));
            var scenes=Rect("Scene settings",panel.transform,new Vector2(0,-15),new Vector2(450,430));
            navigation.localScale=layout.localScale=scenes.localScale=Vector3.one*.72f;
            void Tab(RectTransform selected){navigation.gameObject.SetActive(selected==navigation);layout.gameObject.SetActive(selected==layout);scenes.gameObject.SetActive(selected==scenes);}
            Button(panel.transform,"Navigation",new Vector2(-108,151),new Vector2(100,30),()=>Tab(navigation));
            Button(panel.transform,"Tree Layout",new Vector2(-3,151),new Vector2(100,30),()=>Tab(layout));
            Button(panel.transform,"Scenes",new Vector2(85,151),new Vector2(68,30),()=>Tab(scenes));
            scenes.gameObject.SetActive(false);
            Label(scenes,"SCENE GENERATION",new Vector2(0,160),new Vector2(420,35),24);
            Label(scenes,"Regenerate all cached scenes in the current tree.\nYou will review and confirm before anything is queued.",new Vector2(0,95),new Vector2(400,70),18);
            Button(scenes,"Regenerate All",new Vector2(0,20),new Vector2(300,44),()=>hudOwner?.Batch?.OpenRegenerateAll());
            Button(scenes,"AI Setup",new Vector2(0,-55),new Vector2(300,44),()=>hudOwner?.AISetup?.Open());
            sceneOpacityLabel=Label(scenes,"",new Vector2(0,-109),new Vector2(420,26),19);
            sceneOpacitySlider=LayoutSlider(scenes,"Passthrough scene opacity",-140,0,1,PassthroughMode.SceneOpacity,SetSceneOpacity);
            Button(scenes,"Reset scene settings",new Vector2(0,-190),new Vector2(300,36),()=>SetSceneOpacity(PreferenceDefaults.PassthroughSceneOpacity));RefreshSceneOpacityLabel();
            collapseRect=Button(panel.transform,"−",new Vector2(139,151),new Vector2(36,36),()=>SetOpen(false));collapseRect.name="Collapse settings";
            layout.gameObject.SetActive(false);
            Label(navigation, "SETTINGS", new Vector2(0,177), new Vector2(420,35), 24);
            Label(navigation, "Mouse look sensitivity", new Vector2(0,132), new Vector2(420,26), 19);
            valueLabel = Label(navigation, "", new Vector2(0,110), new Vector2(420,24), 17);
            var track = Rect("Look sensitivity", navigation, new Vector2(0,83), new Vector2(380,24));
            track.gameObject.AddComponent<Image>().color = new Color(.17f,.25f,.28f);
            slider = track.gameObject.AddComponent<Slider>(); slider.minValue = .05f; slider.maxValue = 1f;
            var handle = Rect("Handle", track, Vector2.zero, new Vector2(22,8));
            var handleImage = handle.gameObject.AddComponent<Image>(); handleImage.color = new Color(.25f,.8f,.7f);
            slider.handleRect = handle; slider.targetGraphic = handleImage;
            slider.navigation = new Navigation { mode = Navigation.Mode.None };
            slider.SetValueWithoutNotify(LookSensitivity); slider.onValueChanged.AddListener(SetSensitivity);
            panValueLabel=Label(navigation,"",new Vector2(0,42),new Vector2(420,26),19);
            var panTrack=Rect("Tree flight speed",navigation,new Vector2(0,15),new Vector2(380,24));
            panTrack.gameObject.AddComponent<Image>().color=new Color(.17f,.25f,.28f);
            var panSlider=panTrack.gameObject.AddComponent<Slider>();panSlider.minValue=.5f;panSlider.maxValue=8;
            var panHandle=Rect("Handle",panTrack,Vector2.zero,new Vector2(22,8));
            var panImage=panHandle.gameObject.AddComponent<Image>();panImage.color=new Color(.25f,.8f,.7f);
            panSlider.handleRect=panHandle;panSlider.targetGraphic=panImage;panSlider.navigation=new Navigation{mode=Navigation.Mode.None};
            panSlider.SetValueWithoutNotify(TreePanSpeed);panSlider.onValueChanged.AddListener(SetTreePanSpeed);
            SetTreePanSpeed(TreePanSpeed);
            brightnessLabel=Label(navigation,"",new Vector2(0,-12),new Vector2(420,26),19);
            brightnessSlider=LayoutSlider(navigation,"Tree panorama brightness",-37,0,1.5f,TreeEnvironment.Brightness,SetTreeBrightness);
            RefreshBrightnessLabel();
            textBoostLabel=Label(navigation,"",new Vector2(0,-73),new Vector2(420,26),19);
            textBoostSlider=LayoutSlider(navigation,"Distant panel text boost",-98,0,PanelTextSizing.MaximumBoost,PanelTextSizing.Boost,SetTextBoost);RefreshTextBoostLabel();
            spinSpeedLabel=Label(navigation,"",new Vector2(0,-134),new Vector2(420,26),19);
            spinSpeedSlider=LayoutSlider(navigation,"Sphere spin speed",-159,0,CubePanoramaPreview.MaximumSpinSpeed,CubePanoramaPreview.SpinSpeed,SetSphereSpinSpeed);RefreshSpinSpeedLabel();
            Button(navigation, "Reset", new Vector2(-110,-201), new Vector2(180,36), () => { slider.value = DefaultSensitivity; panSlider.value=PreferenceDefaults.FlightSpeed;brightnessSlider.value=PreferenceDefaults.PanoramaBrightness;SetTextBoost(PreferenceDefaults.DistantPanelTextBoost);SetSphereSpinSpeed(PreferenceDefaults.SphereSpinSpeed); });
            Button(navigation, "Done", new Vector2(110,-201), new Vector2(180,36), () => SetOpen(false));
            treeSettings=TreeLayoutSettings.Load();
            Label(layout,"TREE LAYOUT",new Vector2(0,177),new Vector2(420,35),24);
            gapLabel=Label(layout,"",new Vector2(0,140),new Vector2(420,26),19);
            gapSlider=LayoutSlider(layout,"Sphere gap",115,.2f,4,treeSettings.Gap,v=>{treeSettings.Gap=v;ApplyTreeLayout();},24);
            staggerLabel=Label(layout,"",new Vector2(0,90),new Vector2(420,26),19);
            staggerSlider=LayoutSlider(layout,"Generation stagger",65,0,2,treeSettings.Stagger,v=>{treeSettings.Stagger=v;ApplyTreeLayout();},24);
            riseLabel=Label(layout,"",new Vector2(0,40),new Vector2(420,26),19);
            riseSlider=LayoutSlider(layout,"Generation rise",15,0,1,treeSettings.Rise,v=>{treeSettings.Rise=v;ApplyTreeLayout();},24);
            distanceLabel=Label(layout,"",new Vector2(0,-10),new Vector2(420,26),19);
            distanceSlider=LayoutSlider(layout,"Portal & panel distance",-35,.5f,6,InteractionGenerations,SetInteractionDistance,24);
            closeFadeLabel=Label(layout,"",new Vector2(0,-60),new Vector2(420,26),19);
            closeFadeSlider=LayoutSlider(layout,"Close fade distance",-85,0,NodeNearFade.MaximumDistance,NodeNearFade.Distance,SetCloseFadeDistance,24);
            RefreshCloseFadeLabel();
            sideLockLabel=Label(layout,"",new Vector2(0,-110),new Vector2(420,26),19);
            sideLockSlider=LayoutSlider(layout,"Panel side lock distance",-135,0,PanelSideLock.MaximumDistance,PanelSideLock.Distance,SetPanelSideLockDistance,24);RefreshSideLockLabel();
            distantLockLabel=Label(layout,"",new Vector2(0,-160),new Vector2(420,26),19);
            distantLockSlider=LayoutSlider(layout,"Distant panel freeze distance",-185,0,DistantPanelLock.MaximumDistance,DistantPanelLock.Distance,SetDistantPanelLockDistance,24);RefreshDistantLockLabel();
            Button(layout,"Reset layout",new Vector2(0,-212),new Vector2(240,30),()=>{SetTreeLayout(PreferenceDefaults.CubeGap,PreferenceDefaults.GenerationStagger,PreferenceDefaults.GenerationRise);SetInteractionDistance(PreferenceDefaults.InteractionGenerations);SetCloseFadeDistance(PreferenceDefaults.NodeNearFadeDistance);SetPanelSideLockDistance(PreferenceDefaults.PanelSideLockDistance);SetDistantPanelLockDistance(PreferenceDefaults.DistantPanelLockDistance);});
            RefreshLayoutLabels();RefreshValue();panel.SetActive(false); SetOpen(false);controllerControls=root.GetComponentsInChildren<Selectable>(true);Configure(XRSettings.isDeviceActive);
        }
        Slider LayoutSlider(Transform parent,string name,float y,float min,float max,float value,UnityEngine.Events.UnityAction<float> changed,float height=28)
        {
            var track=Rect(name,parent,new Vector2(0,y),new Vector2(380,height));track.gameObject.AddComponent<Image>().color=new Color(.17f,.25f,.28f);
            var control=track.gameObject.AddComponent<Slider>();control.minValue=min;control.maxValue=max;
            var handle=Rect("Handle",track,Vector2.zero,new Vector2(22,8));var image=handle.gameObject.AddComponent<Image>();image.color=new Color(.25f,.8f,.7f);
            control.handleRect=handle;control.targetGraphic=image;control.navigation=new Navigation{mode=Navigation.Mode.None};control.SetValueWithoutNotify(value);control.onValueChanged.AddListener(changed);return control;
        }
        public void SetTreeLayout(float gap,float stagger,float rise)
        {
            treeSettings=new TreeLayoutSettings{Gap=gap,Stagger=stagger,Rise=rise}.Validated();
            gapSlider.SetValueWithoutNotify(treeSettings.Gap);staggerSlider.SetValueWithoutNotify(treeSettings.Stagger);riseSlider.SetValueWithoutNotify(treeSettings.Rise);ApplyTreeLayout();
        }
        void RefreshLayoutLabels()
        {
            gapLabel.text=$"Gap between spheres · {treeSettings.Gap:0.00} m";
            staggerLabel.text=$"Sideways per generation · {treeSettings.Stagger:0.00} m";
            riseLabel.text=$"Height per generation · {treeSettings.Rise:0.00} m";
            RefreshDistanceLabel();
        }
        void RefreshDistanceLabel(){if(distanceLabel!=null)distanceLabel.text=$"Portal & panel · {InteractionGenerations:0.0} gen / {InteractionGenerations*(TreeLayout.Size+treeSettings.Gap):0.00} m";}
        public void SetCloseFadeDistance(float value)
        {
            value=NodeNearFade.ValidDistance(value);PlayerPrefs.SetFloat(NodeNearFade.PreferenceKey,value);PlayerPrefs.Save();
            if(closeFadeSlider!=null)closeFadeSlider.SetValueWithoutNotify(value);RefreshCloseFadeLabel();
            var gallery=Object.FindAnyObjectByType<AncestorGallery>();
            if(gallery!=null&&gallery.IsTree)foreach(var controls in gallery.GetComponentsInChildren<CubeFacingControls>())controls.SetCloseFadeDistance(value);
        }
        void RefreshCloseFadeLabel(){if(closeFadeLabel!=null)closeFadeLabel.text=NodeNearFade.Distance<=0?"Close fade · Off":"Close fade · "+NodeNearFade.Distance.ToString("0.00")+" m";}
        public void SetPanelSideLockDistance(float value)
        {
            value=PanelSideLock.ValidDistance(value);PlayerPrefs.SetFloat(PanelSideLock.PreferenceKey,value);PlayerPrefs.Save();
            if(sideLockSlider!=null)sideLockSlider.SetValueWithoutNotify(value);RefreshSideLockLabel();
            var gallery=Object.FindAnyObjectByType<AncestorGallery>();
            if(gallery!=null&&gallery.IsTree)foreach(var controls in gallery.GetComponentsInChildren<CubeFacingControls>())controls.SetSideLockDistance(value);
        }
        void RefreshSideLockLabel(){if(sideLockLabel!=null)sideLockLabel.text=PanelSideLock.Distance<=0?"Panel side lock · Off":$"Panel side lock · {PanelSideLock.Distance:0.0} m";}
        public void SetDistantPanelLockDistance(float value)
        {
            value=DistantPanelLock.ValidDistance(value);PlayerPrefs.SetFloat(DistantPanelLock.PreferenceKey,value);PlayerPrefs.Save();
            if(distantLockSlider!=null)distantLockSlider.SetValueWithoutNotify(value);RefreshDistantLockLabel();
            var gallery=Object.FindAnyObjectByType<AncestorGallery>();
            if(gallery!=null&&gallery.IsTree)foreach(var controls in gallery.GetComponentsInChildren<CubeFacingControls>())controls.SetDistantLockDistance(value);
        }
        void RefreshDistantLockLabel(){if(distantLockLabel!=null)distantLockLabel.text=DistantPanelLock.Distance<=0?"Freeze distant panels · Off":$"Freeze panels beyond · {DistantPanelLock.Distance:0.0} m";}
        public void SetInteractionDistance(float value)
        {
            value=float.IsNaN(value)||float.IsInfinity(value)?PreferenceDefaults.InteractionGenerations:Mathf.Clamp(value,.5f,6);
            PlayerPrefs.SetFloat(InteractionDistanceKey,value);
            if(distanceSlider!=null)distanceSlider.SetValueWithoutNotify(value);
            RefreshDistanceLabel();
            var gallery=Object.FindAnyObjectByType<AncestorGallery>();
            if(gallery!=null&&gallery.IsTree)foreach(var controls in gallery.GetComponentsInChildren<CubeFacingControls>())controls.SetInteractionRange(value);
            if(gallery!=null&&gallery.IsTree)foreach(var ghost in gallery.GetComponentsInChildren<GhostSphereTarget>())ghost.SetRange(value);
        }
        void ApplyTreeLayout()
        {
            treeSettings=treeSettings.Validated();treeSettings.Save();RefreshLayoutLabels();
            var gallery=Object.FindAnyObjectByType<AncestorGallery>();if(gallery!=null&&gallery.IsTree)gallery.ShowTree();
        }

        public void SetSensitivity(float value)
        {
            LookSensitivity = Mathf.Clamp(value,.05f,1f);
            PlayerPrefs.SetFloat(SensitivityKey,LookSensitivity); RefreshValue();
        }
        public void SetTreePanSpeed(float value)
        {TreePanSpeed=Mathf.Clamp(value,.5f,8);PlayerPrefs.SetFloat("AncestorWalk.TreePanSpeed",TreePanSpeed);if(panValueLabel!=null)panValueLabel.text=$"Tree flight speed · {TreePanSpeed:0.0} m/s";}
        public void SetTreeBrightness(float value)
        {
            TreeEnvironment.SetBrightness(value);brightnessSlider.SetValueWithoutNotify(TreeEnvironment.Brightness);RefreshBrightnessLabel();
        }
        void RefreshBrightnessLabel(){brightnessLabel.text=$"Tree panorama brightness · {TreeEnvironment.Brightness:0%}";}
        public void SetSceneOpacity(float value)
        {PassthroughMode.SetSceneOpacity(value);sceneOpacitySlider.SetValueWithoutNotify(PassthroughMode.SceneOpacity);RefreshSceneOpacityLabel();}
        void RefreshSceneOpacityLabel(){sceneOpacityLabel.text=$"Passthrough scene opacity · {PassthroughMode.SceneOpacity:0%}";}
        public void SetTextBoost(float value)
        {PanelTextSizing.SetBoost(value);textBoostSlider.SetValueWithoutNotify(PanelTextSizing.Boost);RefreshTextBoostLabel();}
        void RefreshTextBoostLabel(){textBoostLabel.text=$"Distant text & photo boost · {PanelTextSizing.Boost:0%}";}
        public void SetSphereSpinSpeed(float value)
        {
            value=CubePanoramaPreview.ValidSpinSpeed(value);PlayerPrefs.SetFloat(CubePanoramaPreview.SpinSpeedKey,value);
            if(spinSpeedSlider!=null)spinSpeedSlider.SetValueWithoutNotify(value);
            var gallery=Object.FindAnyObjectByType<AncestorGallery>();
            if(gallery!=null)foreach(var preview in gallery.GetComponentsInChildren<CubePanoramaPreview>(true))preview.SetSpinSpeed(value);
            RefreshSpinSpeedLabel();
        }
        void RefreshSpinSpeedLabel(){if(spinSpeedLabel!=null)spinSpeedLabel.text=CubePanoramaPreview.SpinSpeed==0?"Sphere spin speed · Stopped":$"Sphere spin speed · {CubePanoramaPreview.SpinSpeed:0.0}°/s";}
        void RefreshValue() { valueLabel.text = $"{LookSensitivity / .12f:0.0}× original speed · saved on this device"; }
        void Update()
        {
            Configure(XRSettings.isDeviceActive);UIFade.Set(root,hudOwner==null||hudOwner.HudVisible&&!hudOwner.IsTimelineDragging);if(!root.activeSelf)pointerCaptured=false;
            bool other=hudOwner!=null&&((hudOwner.AISetup?.IsOpen??false)||(hudOwner.Library?.IsOpen??false)||(hudOwner.Batch?.IsOpen??false)||(hudOwner.Tutorial?.IsOpen??false)||(hudOwner.LocationEditor?.IsOpen??false)||(hudOwner.Overlay?.PanelOpen??false));
            if(other&&IsOpen)SetOpen(false);UIFade.Set(toggleRect.gameObject,!other&&!IsOpen);
        }
        public void SetOpen(bool open)
        {
            if(open)hudOwner?.Overlay?.ClosePanels();
            UIFade.Set(panel,open);
            if(!open){pointerCaptured=false;controllerSlider=null;}
            lastToggleFrame = Time.frameCount;
            if (!open) PlayerPrefs.Save();
        }
        public void RegisterHud(HudOverlay hud){hudOwner=hud.Owner;hud.Register(toggleRect,"Settings",()=>!IsOpen);hud.Register(collapseRect,"Collapse settings");}
        public void CancelPointerCapture(){pointerCaptured=false;}
        public void SetHudVisible(bool visible){UIFade.Set(root,visible);if(!visible){pointerCaptured=false;controllerSlider=null;}}
        void Configure(bool vr)
        {
            if(configured&&wasVr==vr)return;configured=true;wasVr=vr;
            var rect=(RectTransform)root.transform;canvas.renderMode=vr?RenderMode.WorldSpace:RenderMode.ScreenSpaceOverlay;canvas.worldCamera=Camera.main;
            rect.SetParent(vr?Camera.main.transform:transform,false);
            if(vr){rect.sizeDelta=new Vector2(960,540);rect.localPosition=new Vector3(0,0,1.6f);rect.localRotation=Quaternion.identity;rect.localScale=Vector3.one*.0018f;}
            else rect.localScale=Vector3.one;
            var panelRect=(RectTransform)panel.transform;panelRect.anchorMin=panelRect.anchorMax=vr?new Vector2(.5f,.5f):new Vector2(0,.5f);panelRect.anchoredPosition=vr?new Vector2(-200,0):new Vector2(177,0);
            toggleRect.anchorMin=toggleRect.anchorMax=vr?new Vector2(.5f,.5f):Vector2.one;toggleRect.anchoredPosition=vr?new Vector2(285,210):new Vector2(-112,-30);toggleRect.sizeDelta=vr?new Vector2(142,38):new Vector2(200,40);
            OverlayComfort.TextDensity(canvas,vr);
        }
        static bool ControllerPoint(RectTransform rect,Ray ray,out Vector2 point)
        {
            point=default;if(!UIFade.Interactive(rect))return false;var plane=new Plane(rect.forward,rect.position);
            if(!plane.Raycast(ray,out float distance)||distance<0)return false;point=rect.InverseTransformPoint(ray.GetPoint(distance));return true;
        }
        static bool ControllerContains(RectTransform rect,Ray ray,float horizontalPadding=.25f)
        {
            if(!ControllerPoint(rect,ray,out var p))return false;var area=rect.rect;
            return p.x>=area.xMin-horizontalPadding&&p.x<=area.xMax+horizontalPadding&&p.y>=area.yMin-.25f&&p.y<=area.yMax+.25f;
        }
        static void DragControllerSlider(Slider control,Ray ray)
        {
            var rect=(RectTransform)control.transform;if(!ControllerPoint(rect,ray,out var p))return;
            float t=Mathf.InverseLerp(rect.rect.xMin,rect.rect.xMax,p.x);if(Mathf.Approximately(t,1))t=1;else if(Mathf.Approximately(t,0))t=0;
            control.value=Mathf.Lerp(control.minValue,control.maxValue,t);
        }
        public bool HandleControllerInput(Ray ray,bool pressed,bool held,bool back)
        {
            if(hudOwner==null||!hudOwner.HudVisible||!UIFade.Interactive(root.transform)||hudOwner.IsTimelineDragging)return false;
            if((hudOwner.AISetup?.IsOpen??false)||(hudOwner.Library?.IsOpen??false)||(hudOwner.Batch?.IsOpen??false)||(hudOwner.Tutorial?.IsOpen??false)||(hudOwner.LocationEditor?.IsOpen??false)||(hudOwner.Overlay?.PanelOpen??false))return false;
            if(back&&IsOpen){SetOpen(false);return true;}
            if(controllerSlider!=null)
            {if(held)DragControllerSlider(controllerSlider,ray);else{controllerSlider=null;pointerCaptured=false;}return true;}
            if(!held)pointerCaptured=false;
            if(ControllerContains(toggleRect,ray)){if(pressed){SetOpen(!IsOpen);pointerCaptured=held;}return true;}
            if(!IsOpen||!ControllerContains((RectTransform)panel.transform,ray))return pointerCaptured;
            if(pressed)
            {
                pointerCaptured=held;
                foreach(var control in controllerControls)
                {
                    float padding=control is Slider target&&target.handleRect!=null?target.handleRect.rect.width*.5f+.25f:.25f;
                    if(!control.IsInteractable()||!ControllerContains((RectTransform)control.transform,ray,padding))continue;
                    if(control is Slider slider){controllerSlider=slider;DragControllerSlider(slider,ray);}
                    else if(control is Button button)button.onClick.Invoke();break;
                }
            }
            return true;
        }
        public bool HandleDesktopInput()
        {
            if(hudOwner!=null&&!hudOwner.HudVisible){pointerCaptured=false;return false;}
            if (Time.frameCount == lastToggleFrame) return true;
            if (Keyboard.current?.pKey.wasPressedThisFrame ?? false) { SetOpen(!IsOpen); return true; }
            if (IsOpen && (Keyboard.current?.escapeKey.wasPressedThisFrame ?? false)) { SetOpen(false); return true; }
            var mouse=Mouse.current;if(mouse==null){pointerCaptured=false;return false;}
            bool over=(UIFade.Interactive(toggleRect)&&RectTransformUtility.RectangleContainsScreenPoint(toggleRect,mouse.position.ReadValue())) ||
                (IsOpen&&RectTransformUtility.RectangleContainsScreenPoint((RectTransform)panel.transform,mouse.position.ReadValue()));
            if(mouse.leftButton.wasPressedThisFrame&&over)pointerCaptured=true;
            if(!mouse.leftButton.isPressed)pointerCaptured=false;
            // Capture a slider drag even after it leaves the panel, while leaving the
            // open panel nonmodal for keyboard flight and looking elsewhere.
            return pointerCaptured||(over&&mouse.rightButton.isPressed);
        }
        void OnDisable() { PlayerPrefs.Save(); }
        void OnDestroy() { if (ownedEventSystem != null) Destroy(ownedEventSystem);if(font!=null)Destroy(font); }
        RectTransform Rect(string name, Transform parent, Vector2 position, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent,false); rect.anchorMin = rect.anchorMax = new Vector2(.5f,.5f);
            rect.sizeDelta = size; rect.anchoredPosition = position; return rect;
        }
        Text Label(Transform parent, string text, Vector2 position, Vector2 size, int fontSize)
        {
            var label = Rect(text,parent,position,size).gameObject.AddComponent<Text>();
            label.font = font; label.text = text; label.fontSize = fontSize; label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white; label.raycastTarget = false; return label;
        }
        RectTransform Button(Transform parent, string title, Vector2 position, Vector2 size, UnityEngine.Events.UnityAction action)
        {
            var rect = Rect(title,parent,position,size);
            var image = rect.gameObject.AddComponent<Image>(); image.color = new Color(.12f,.36f,.34f);
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None }; button.onClick.AddListener(action);
            Label(rect,title,Vector2.zero,size,18); return rect;
        }
    }
}
