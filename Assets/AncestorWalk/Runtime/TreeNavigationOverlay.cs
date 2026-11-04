using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR;

namespace AncestorWalk
{
    public sealed class TreeNavigationOverlay : MonoBehaviour
    {
        sealed class Control { public RectTransform Rect; public Vector3 Direction; public Vector2 Rotation;public int Tab=-1;public bool Rotates,Reset; public System.Action Click; }
        readonly List<Control> controls = new();
        AncestorGallery gallery; Canvas canvas; RectTransform root, panel, toggle,modeButton,collapse;Text toggleLabel,helpLabel,modeLabel;bool wasVr;TreeControlMode lastMode;
        const string ExpandedKey="AncestorWalk.TreeFlightControlsExpanded";
        public const string RotationTabKey="AncestorWalk.TreeRotationTab";
        RectTransform moveTab,rotateTab,buildToggle,expandSiblings;Text angleLabel,buildLabel;GalleryNavigation navigation;
        Vector2Int displayedAngles=new(int.MinValue,int.MinValue);int displayedScale=-1;
        public bool RotationTab {get;private set;}
        public bool IsExpanded {get;private set;}
        readonly OverlayComfort comfort=new();
        public void SetExpanded(bool expanded)
        {
            IsExpanded=expanded;panel.gameObject.SetActive(expanded);toggleLabel.text=expanded?"−":"↗";
            PlayerPrefs.SetInt(ExpandedKey,expanded?1:0);PlayerPrefs.Save();
        }
        public void Build(AncestorGallery owner)
        {
            gallery=owner;root=(RectTransform)transform;
            canvas=gameObject.AddComponent<Canvas>();canvas.sortingOrder=90;
            var scaler=gameObject.AddComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution=new Vector2(960,540);scaler.matchWidthOrHeight=.5f;
            panel=Rect("Tree navigation",root,new Vector2(0,-201),new Vector2(920,126));
            panel.gameObject.AddComponent<Image>().color=new Color(.025f,.065f,.08f,.85f);
            Add("Left",-390,Vector3.left);Add("Right",-270,Vector3.right);
            Add("Forward",-150,Vector3.forward);Add("Back",-30,Vector3.back);
            Add("Up",90,Vector3.up);Add("Down",210,Vector3.down);Add("Reset View",330,Vector3.zero,true);
            var nav=navigation=Object.FindAnyObjectByType<GalleryNavigation>();
            AddNode("Nearest · F / X",-250,()=>nav.FocusNearestNode());
            AddNode("Previous · [ / Y",0,()=>nav.StepNode(-1));
            AddNode("Next · ] / A",250,()=>nav.StepNode(1));
            var label=Rect("Pan help",panel,new Vector2(-25,44),new Vector2(330,28)).gameObject.AddComponent<Text>();
            label.font=owner.font;label.fontSize=15;label.alignment=TextAnchor.MiddleCenter;label.color=Color.white;label.raycastTarget=false;
            helpLabel=label;
            modeButton=Rect("Control template",panel,new Vector2(290,44),new Vector2(194,30));
            modeButton.gameObject.AddComponent<Image>().color=new Color(.12f,.36f,.34f);
            modeLabel=Rect("Mode label",modeButton,Vector2.zero,modeButton.sizeDelta).gameObject.AddComponent<Text>();
            modeLabel.font=owner.font;modeLabel.fontSize=16;modeLabel.alignment=TextAnchor.MiddleCenter;modeLabel.color=Color.white;modeLabel.raycastTarget=false;
            controls.Add(new Control{Rect=modeButton,Tab=0,Click=()=>{TreeControlTemplates.Select(TreeControlTemplates.Current==TreeControlMode.Fly?TreeControlMode.Slide:TreeControlTemplates.Current==TreeControlMode.Slide?TreeControlMode.TwoStickFlight:TreeControlMode.Fly);RefreshMode();}});
            moveTab=Tab("Move",-365,()=>SetRotationTab(false));rotateTab=Tab("Rotate",-255,()=>SetRotationTab(true));
            angleLabel=Rect("Tree angles",panel,new Vector2(290,44),new Vector2(194,30)).gameObject.AddComponent<Text>();
            angleLabel.font=owner.font;angleLabel.fontSize=16;angleLabel.alignment=TextAnchor.MiddleCenter;angleLabel.color=Color.white;angleLabel.raycastTarget=false;
            AddRotation("Turn Left",-340,Vector2.left);AddRotation("Turn Right",-170,Vector2.right);
            AddRotation("Tilt Up",0,Vector2.down);AddRotation("Tilt Down",170,Vector2.up);AddRotation("Reset Rotation",340,Vector2.zero,true);
            SetRotationTab(PlayerPrefs.GetInt(RotationTabKey,0)!=0);
            RefreshMode();
            collapse=Rect("Collapse flight controls",panel,new Vector2(438,43),new Vector2(36,36));
            collapse.gameObject.AddComponent<Image>().color=new Color(.08f,.3f,.29f,.95f);
            var collapseLabel=Rect("Collapse icon",collapse,Vector2.zero,collapse.sizeDelta).gameObject.AddComponent<Text>();
            collapseLabel.font=owner.font;collapseLabel.text="−";collapseLabel.fontSize=26;collapseLabel.alignment=TextAnchor.MiddleCenter;collapseLabel.color=Color.white;collapseLabel.raycastTarget=false;
            owner.Hud?.Register(collapse,"Collapse flight controls");
            toggle=Rect("Toggle flight controls",root,new Vector2(440,-235),new Vector2(36,36));
            toggle.gameObject.AddComponent<Image>().color=new Color(.08f,.3f,.29f,.85f);
            toggleLabel=Rect("Flight icon",toggle,Vector2.zero,toggle.sizeDelta).gameObject.AddComponent<Text>();
            toggleLabel.font=owner.font;toggleLabel.fontSize=26;toggleLabel.alignment=TextAnchor.MiddleCenter;toggleLabel.color=Color.white;toggleLabel.raycastTarget=false;
            buildToggle=Rect("Build Tree toggle",root,new Vector2(-159,234),new Vector2(132,38));buildToggle.gameObject.AddComponent<Image>().color=new Color(.08f,.3f,.29f,.95f);
            buildLabel=Rect("Build Tree label",buildToggle,Vector2.zero,buildToggle.sizeDelta).gameObject.AddComponent<Text>();buildLabel.font=owner.font;buildLabel.fontSize=16;buildLabel.alignment=TextAnchor.MiddleCenter;buildLabel.color=Color.white;buildLabel.raycastTarget=false;
            UIFade.Set(buildToggle.gameObject,!ProductDemoAuthoring.ReadOnly(owner.Document));RefreshBuildMode();owner.Hud?.Register(buildToggle,"Edit Tree · toggle missing-parent ghost spheres");
            expandSiblings=Rect("Expand all siblings",root,new Vector2(-250,185),new Vector2(160,36));expandSiblings.gameObject.AddComponent<Image>().color=new Color(.08f,.3f,.29f,.95f);
            var expandLabel=Rect("Expand siblings label",expandSiblings,Vector2.zero,expandSiblings.sizeDelta).gameObject.AddComponent<Text>();expandLabel.font=owner.font;expandLabel.fontSize=16;expandLabel.text="Expand siblings";expandLabel.alignment=TextAnchor.MiddleCenter;expandLabel.color=Color.white;expandLabel.raycastTarget=false;
            UIFade.Set(expandSiblings.gameObject,owner.HasCollapsedSiblings);owner.Hud?.Register(expandSiblings,"Expand every collapsed sibling branch");
            SetExpanded(PlayerPrefs.GetInt(ExpandedKey,0)!=0);
            owner.Hud?.Register(toggle,"Open flight controls",()=>!IsExpanded);
            wasVr=!XRSettings.isDeviceActive;Configure();SetHudVisible(owner.HudVisible);
        }
        void Add(string name,float x,Vector3 direction,bool reset=false)
        {
            var rect=Rect(name,panel,new Vector2(x,-34),new Vector2(110,36));
            rect.gameObject.AddComponent<Image>().color=new Color(.08f,.3f,.29f,.95f);
            var text=Rect("Label",rect,Vector2.zero,rect.sizeDelta).gameObject.AddComponent<Text>();
            text.font=gallery.font;text.text=name;text.fontSize=17;text.alignment=TextAnchor.MiddleCenter;text.color=Color.white;text.raycastTarget=false;
            controls.Add(new Control{Rect=rect,Direction=direction,Reset=reset,Tab=0});
        }
        RectTransform Tab(string name,float x,System.Action click)
        {
            var r=Rect(name+" tab",panel,new Vector2(x,44),new Vector2(100,30));r.gameObject.AddComponent<Image>();
            var text=Rect("Tab label",r,Vector2.zero,r.sizeDelta).gameObject.AddComponent<Text>();text.text=name;text.font=gallery.font;text.fontSize=16;text.alignment=TextAnchor.MiddleCenter;text.color=Color.white;text.raycastTarget=false;
            controls.Add(new Control{Rect=r,Click=click});return r;
        }
        void AddRotation(string name,float x,Vector2 rotation,bool reset=false)
        {
            var r=Rect(name,panel,new Vector2(x,-34),new Vector2(160,36));r.gameObject.AddComponent<Image>().color=new Color(.08f,.3f,.29f,.95f);
            var text=Rect("Rotation label",r,Vector2.zero,r.sizeDelta).gameObject.AddComponent<Text>();text.text=name;text.font=gallery.font;text.fontSize=16;text.alignment=TextAnchor.MiddleCenter;text.color=Color.white;text.raycastTarget=false;
            controls.Add(new Control{Rect=r,Tab=1,Rotation=rotation,Rotates=true,Click=reset?()=>navigation.ResetTreeRotation():null});
            if(gallery.Tree.People.Count==0){r.GetComponent<Image>().color=new Color(.08f,.3f,.29f,.3f);text.color=new Color(1,1,1,.35f);}
        }
        public void SetRotationTab(bool value)
        {
            RotationTab=value;PlayerPrefs.SetInt(RotationTabKey,value?1:0);PlayerPrefs.Save();
            foreach(var control in controls)if(control.Tab>=0)control.Rect.gameObject.SetActive(control.Tab==(value?1:0));
            moveTab.GetComponent<Image>().color=value?new Color(.06f,.16f,.17f):new Color(.18f,.46f,.42f);
            rotateTab.GetComponent<Image>().color=value?new Color(.18f,.46f,.42f):new Color(.06f,.16f,.17f);
            angleLabel.gameObject.SetActive(value);RefreshMode();RefreshAngles();
        }
        void RefreshAngles()
        {
            var next=new Vector2Int(Mathf.RoundToInt(gallery.TreeRotation.x)%360,Mathf.RoundToInt(gallery.TreeRotation.y));
            if(next==displayedAngles)return;displayedAngles=next;angleLabel.text=$"Turn {next.x}° · Tilt {next.y}°";
        }
        void AddNode(string name,float x,System.Action action)
        {
            var rect=Rect(name,panel,new Vector2(x,8),new Vector2(230,34));
            rect.gameObject.AddComponent<Image>().color=new Color(.08f,.3f,.29f,.95f);
            var text=Rect("Label",rect,Vector2.zero,rect.sizeDelta).gameObject.AddComponent<Text>();
            text.font=gallery.font;text.text=name;text.fontSize=17;text.alignment=TextAnchor.MiddleCenter;text.color=Color.white;text.raycastTarget=false;
            controls.Add(new Control{Rect=rect,Click=action});
        }
        void Configure()=>ConfigureForMode(XRSettings.isDeviceActive);
        void ConfigureForMode(bool vr)
        {
            if(wasVr==vr)return;wasVr=vr;
            canvas.renderMode=vr?RenderMode.WorldSpace:RenderMode.ScreenSpaceOverlay;canvas.worldCamera=Camera.main;
            if(vr){root.SetParent(Camera.main.transform,false);root.sizeDelta=new Vector2(960,540);root.localPosition=new Vector3(0,0,1.6f);root.localRotation=Quaternion.identity;root.localScale=Vector3.one*.0018f;}
            else{root.SetParent(gallery.transform,false);root.localScale=Vector3.one;}
            OverlayComfort.TextDensity(canvas,vr);comfort.Set(buildToggle,vr,new Vector2(-159,210),new Vector2(132,38));comfort.Set(expandSiblings,vr,new Vector2(-250,160));comfort.Set(toggle,vr,new Vector2(350,-215),new Vector2(44,44));
            comfort.Set(panel,vr,new Vector2(0,-95),new Vector2(760,180));
            comfort.Set(collapse,vr,new Vector2(354,55),new Vector2(44,44));
            int movement=0,node=0,rotation=0;foreach(var c in controls)
            {
                if(c.Rect==modeButton||c.Rect==moveTab||c.Rect==rotateTab)continue;
                if(c.Rotates)comfort.Set(c.Rect,vr,new Vector2((rotation++-2)*144,-45),new Vector2(136,44));
                else if(c.Click!=null)comfort.Set(c.Rect,vr,new Vector2((node++-1)*232,5),new Vector2(210,38));
                else comfort.Set(c.Rect,vr,new Vector2((movement++-3)*102,-45),new Vector2(96,44));
            }
            comfort.Set(moveTab,vr,new Vector2(-305,55),new Vector2(92,38));comfort.Set(rotateTab,vr,new Vector2(-205,55),new Vector2(92,38));
            comfort.Set((RectTransform)panel.Find("Pan help"),vr,new Vector2(-20,55),new Vector2(240,44));
            comfort.Set(modeButton,vr,new Vector2(220,55),new Vector2(190,38));
            comfort.Set(modeLabel.rectTransform,vr,size:new Vector2(190,38));
            comfort.Set(angleLabel.rectTransform,vr,new Vector2(220,55),new Vector2(190,38));
            RefreshMode();
        }
        void RefreshMode(){lastMode=TreeControlTemplates.Current;displayedScale=Mathf.RoundToInt(gallery.TreeScale*100);modeLabel.text=TreeControlTemplates.Name+" · "+displayedScale+"%";helpLabel.text=RotationTab?(wasVr?"Hold to turn or tilt":"J/K turn · I/M tilt"):(wasVr?"Right grip + stick ↑↓: size":"Shift + ↑↓: tree size");}
        void RefreshBuildMode(){string text=gallery.BuildMode?"Edit Tree · ON":"Edit Tree";if(buildLabel.text!=text)buildLabel.text=text;}
        void Update(){Configure();RefreshBuildMode();bool modal=NavigationPreferences.PanelOpen||(gallery.Overlay?.PanelOpen??false)||(gallery.Library?.IsOpen??false)||(gallery.AISetup?.IsOpen??false)||(gallery.Batch?.IsOpen??false)||(gallery.Tutorial?.IsOpen??false);UIFade.Set(buildToggle.gameObject,!ProductDemoAuthoring.ReadOnly(gallery.Document)&&!modal);UIFade.Set(expandSiblings.gameObject,gallery.HasCollapsedSiblings&&!modal);if(lastMode!=TreeControlTemplates.Current||displayedScale!=Mathf.RoundToInt(gallery.TreeScale*100))RefreshMode();if(RotationTab)RefreshAngles();UIFade.Set(canvas,(!modal&&gallery.HudVisible&&gallery.SelectedId==null&&(gallery.LocationEditor==null||!gallery.LocationEditor.IsOpen))&&!gallery.IsTimelineDragging);}
        public void SetHudVisible(bool visible){UIFade.Set(canvas,(visible&&gallery.IsTree)&&!gallery.IsTimelineDragging);}
        bool Contains(RectTransform rect,Ray ray,bool vr)
        {
            Vector2 p;
            if(!UIFade.Interactive(rect))return false;if(!vr)return RectTransformUtility.RectangleContainsScreenPoint(rect,Mouse.current?.position.ReadValue()??Vector2.zero);
            var plane=new Plane(rect.forward,rect.position);
            if(!plane.Raycast(ray,out float distance))return false;
            p=rect.InverseTransformPoint(ray.GetPoint(distance));return rect.rect.Contains(p);
        }
        public bool HandleInput(Ray ray,bool vr,bool pressed,bool held,float speed)
        {
            if(!gallery.HudVisible||!canvas.enabled)return false;
            if(buildToggle.gameObject.activeInHierarchy&&Contains(buildToggle,ray,vr)){if(pressed)gallery.SetBuildMode(!gallery.BuildMode);return true;}
            if(expandSiblings.gameObject.activeInHierarchy&&Contains(expandSiblings,ray,vr)){if(pressed)gallery.ExpandAllSiblings();return true;}
            if(Contains(toggle,ray,vr)){if(pressed)SetExpanded(!IsExpanded);return true;}
            if(!IsExpanded||!Contains(panel,ray,vr))return false;
            if(Contains(collapse,ray,vr)){if(pressed)SetExpanded(false);return true;}
            foreach(var control in controls)if(control.Rect.gameObject.activeInHierarchy&&Contains(control.Rect,ray,vr))
            {if(control.Rotates&&gallery.Tree.People.Count==0)break;if(control.Click!=null){if(pressed)control.Click();}else if(control.Rotates){if(held)navigation.RotateTree(control.Rotation,Time.unscaledDeltaTime);}else if(control.Reset){if(pressed)navigation.ResetTreeView();}else if(held)navigation.Fly(control.Direction,Time.unscaledDeltaTime);break;}
            return true;
        }
        static RectTransform Rect(string name,Transform parent,Vector2 pos,Vector2 size)
        {var r=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();r.SetParent(parent,false);r.anchorMin=r.anchorMax=new Vector2(.5f,.5f);r.anchoredPosition=pos;r.sizeDelta=size;return r;}
    }
}
