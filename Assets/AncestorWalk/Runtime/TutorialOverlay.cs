using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR;

namespace AncestorWalk
{
    // One persistent overlay across the tree and historical scenes; no scene/cache mutations.
    public sealed class TutorialOverlay : MonoBehaviour
    {
        public const string SeenKey="AncestorWalk.TutorialSeen.v1";
        struct Page
        {
            public string Title,Summary,Quest,Desktop,Hint;
            public Page(string title,string summary,string quest,string desktop,string hint)
            {Title=title;Summary=summary;Quest=quest;Desktop=desktop;Hint=hint;}
        }
        static readonly Page[] pages={
            new("Your family through time",
                "Explore the family tree, choose a year, and step into a person's world.",
                "POINT with your right controller\nSELECT with the right trigger\nNext / Previous move through this guide.",
                "POINT with your mouse\nSELECT with a left click\nNext / Previous move through this guide.",
                "Skip at any time. Open ? Help to replay this guide from the tree or a scene."),
            new("Move around the tree",
                "Choose Fly, Slide or Two-stick flight in the flight controls.",
                "LEFT STICK: left / right and up / down\nRIGHT STICK: forward / back and smooth turning\nX: nearest sphere   |   Y: previous   |   A: next",
                "W / S: fly where you look   |   A / D: strafe\nUP / DOWN or E / Q: height   |   SHIFT: faster\nRIGHT MOUSE: look   |   F: nearest   |   [ / ]: nodes",
                "The flight icon opens holdable movement buttons. Previous / Next also move between people."),
            new("Turn and resize the tree",
                "Rotate the whole family or change its size while you stay in place.",
                "Flight icon → ROTATE: hold Turn or Tilt\nRIGHT GRIP + right stick up / down: resize\nRESET ROTATION: straighten without moving",
                "J / K: turn   |   I / M: tilt\nSHIFT + UP / DOWN: resize the tree\nFlight icon → ROTATE: buttons and angle readout",
                "Tree Home frames the first child. Reset View restores original framing. Both reset tree size and rotation."),
            new("Point, jump, enter",
                "Every sphere is a person. Jump to one, then step into their scene.",
                "POINT at a sphere + TRIGGER: jump to its front\nKeep pointing nearby: its portal appears\nPOINT at the portal + TRIGGER: enter the scene",
                "CLICK a sphere: jump to its front\nKeep pointing nearby: its portal appears\nCLICK the portal: enter the scene",
                "Portals fade away when you point elsewhere or move out of range."),
            new("Open, import and manage trees",
                "Trees holds your saved families, GEDCOM imports and photos.",
                "TREES: open a saved tree or NEW TREE\nIMPORT GED / ZIP: family records and photos\nEdit current tree: people and relationships",
                "TREES or T: open, create or import a tree\nIMPORT GED / ZIP accepts linked family photos\nDELETE asks before removing a local tree",
                "Photos open a browser with portrait and subject cutout tools. Switching trees asks to cancel any unfinished jobs."),
            new("Build and edit your family",
                "New trees start with one translucent Add first person sphere.",
                "POINT + TRIGGER on a ghost: create a person\nEnter details, then add missing ghost parents\nEDIT TREE toggles ghosts on existing trees",
                "CLICK a ghost: create a person and edit details\nAdd dates, locations, photos and relationships\nEDIT TREE toggles missing-parent ghosts",
                "Closing details keeps the new person. Known living uses the current year; no death date and age under 110 also implies living."),
            new("Simplify sibling branches",
                "Keep one person's line visible while folding their siblings away.",
                "Use the sibling control on a person's panel\nThat person stays; sibling branches hide\nEXPAND SIBLINGS restores every folded branch",
                "Use the sibling control on a person's panel\nFull and half-sibling descendant branches fold\nEXPAND SIBLINGS restores every folded branch",
                "Folding changes the view, not family records or images. Blue connectors mark male parents; pink marks female parents."),
            new("Travel through the years",
                "The panel above each nearby sphere contains that person's lifespan slider.",
                "POINT at the slider, HOLD TRIGGER, and drag\nThe year follows the slider thumb\nInside a scene, select the CLOCK icon for its timeline",
                "CLICK and DRAG the year slider\nThe year follows the slider thumb\nInside a scene, click the CLOCK icon for its timeline",
                "Panoramas and location names blend together. An uncached year uses nearby saved imagery; All Time appears only when none is available."),
            new("Read the timeline markers",
                "Markers and thumb colors describe each exact year; there is no year rounding.",
                "GOLD: a generated scene for that year\nPURPLE: a saved location awaiting generation\nTEAL thumb: no exact scene or saved move",
                "GOLD: a generated scene for that year\nPURPLE: a saved location awaiting generation\nHover a marker for its year, place and state",
                "Blinking blue is queued; green is generating. Red marks a missing endpoint location. Dragging only browses; it does not generate."),
            new("Add a place and scene details",
                "Choose a year, then Edit Location on the scene timeline.",
                "LOCATION: town, street or named venue\nSCENE DETAILS: people, clothing and activity\nGENERATE saves the entry and starts an image",
                "LOCATION: town, street or named venue\nSCENE DETAILS: people, clothing and activity\nCtrl / Cmd + Enter or GENERATE starts the image",
                "Only the place and year guide research. Example: Location: Monterey, California. Details: A family beside a 1930s touring car."),
            new("Speak instead of typing",
                "Location and Scene details each have their own microphone.",
                "Select the mic next to the field you want\nTap that mic again to finish recording\nReview the transcription, then GENERATE",
                "Click the mic beside Location or Scene details\nClick it again to finish; review before Generate\nDouble-click words; use normal copy / paste",
                "The listening wave shows recording. Only one mic works at a time; both pause while processing. Close cancels dictation."),
            new("Bring a photo into the scene",
                "Select Include photo in Edit Location, then Choose Photo.",
                "The person's photos appear first in the picker\nChoose the person or object to include\nUse Scene details for outfit and placement",
                "The person's photos appear first in the picker\nChoose a photo, then describe how to use it\nGENERATE sends the selected photo to Meta",
                "A matching cached year edits its panorama in place; otherwise a new setting is made. Clothing adapts to the era unless you specify otherwise."),
            new("Generate and regenerate",
                "Images save automatically for the person and exact selected year.",
                "Timeline GENERATE: make the selected year\nCircular-arrow icon: regenerate a cached year\nThe sphere panel also offers regeneration",
                "Timeline GENERATE: make the selected year\nCircular-arrow icon: regenerate a cached year\nThe sphere panel also offers regeneration",
                "The old image stays until its replacement is ready. Valid birth/death places generate missing images on tree open, without replacing saved ones."),
            new("Follow and manage image jobs",
                "Keep browsing while up to eight images generate at once.",
                "Progress shows the active year and percent\nQueued scene: MOVE TO FRONT or CANCEL\nActive or retry panel: CANCEL stops that job",
                "F8: open the ACTIVE JOBS panel\nPause new jobs, clear waiting jobs or retry\nIn-scene progress and queue panels offer CANCEL",
                "Regenerate replaces any job for that same person/year. Errors share one dismissible alert. Other times and people keep their jobs."),
            new("Ask about the time and place",
                "ASK opens the historical guide for the scene you are viewing.",
                "Type a question and select ASK, or use the mic\nTap the mic again to finish and send\nUse the arrows to read longer answers",
                "Type a question and click ASK, or use the mic\nTap the mic again to finish and send\nClose the panel to cancel the question",
                "The guide uses the selected person, year and location. Changing time or leaving the scene cancels the current question."),
            new("Make the view your own",
                "Settings has Navigation, Tree Layout and Scenes tabs.",
                "NAVIGATION: speed, brightness, text and spin\nTREE LAYOUT: spacing, rise and panel distances\nSCENES: AI SETUP and REGENERATE ALL",
                "SETTINGS or P: change your saved preferences\nAdjust fading, side lock and distant panel freeze\nSCENES: AI SETUP and REGENERATE ALL",
                "Scenes includes AI Setup and passthrough scene opacity (25% by default). Regenerate All asks before replacing cached images."),
            new("Hide overlays and return home",
                "The scene timeline opens on entry and has its own independent toggle.",
                "Left MENU / HUD: hide or show overlays\nCLOCK: timeline   |   ROOM / VR: passthrough\nB / BACK: return   |   Tree HOME: first child",
                "TAB / HUD: hide or show overlays   |   H: Help\nCLOCK: timeline   |   arrows: look around a scene\nESC / BACK: return   |   Tree HOME: first child",
                "Quest starts in Room mode. Scenes overlay the room at your opacity setting. Back preserves the tree view; Home appears only in the tree.")
        };
        AncestorGallery gallery;Canvas canvas;RectTransform root,card,shield,help,back,next,skip;
        Text progress,title,summary,instructions,hint,nextLabel;Image[] dots;
        bool configured,wasVr;int page;TreeControlMode lastMode;
        readonly OverlayComfort comfort=new();
        public bool IsOpen=>card!=null&&UIFade.Visible(card.gameObject);
        public int CurrentPage=>page;
        public int PageCount=>pages.Length;
        public void Build(AncestorGallery owner)
        {
            gallery=owner;root=(RectTransform)transform;canvas=gameObject.AddComponent<Canvas>();canvas.sortingOrder=150;
            gameObject.AddComponent<GraphicRaycaster>();
            var scaler=gameObject.AddComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution=new Vector2(960,540);scaler.matchWidthOrHeight=.5f;
            shield=Rect("Tutorial input shield",root,Vector2.zero,new Vector2(4000,4000));Image(shield,Color.clear).raycastTarget=true;
            help=Button("Tutorial help",root,"? Help",new Vector2(-409,234),new Vector2(112,38));
            card=Rect("Getting started",root,Vector2.zero,new Vector2(660,396));
            Image(card,new Color(.025f,.065f,.08f,.97f));
            progress=Label(card,"",new Vector2(0,164),new Vector2(590,24),14,new Color(.95f,.72f,.3f));
            title=Label(card,"",new Vector2(0,126),new Vector2(610,40),28,Color.white);
            summary=Label(card,"",new Vector2(0,72),new Vector2(600,60),19,new Color(.82f,.91f,.92f));
            instructions=Label(card,"",new Vector2(0,-10),new Vector2(600,100),20,Color.white);
            hint=Label(card,"",new Vector2(0,-97),new Vector2(594,54),16,new Color(.82f,.91f,.92f));
            back=Button("Tutorial previous",card,"Previous",new Vector2(-221,-159),new Vector2(160,42));
            skip=Button("Tutorial skip",card,"Skip tutorial",new Vector2(-34,-159),new Vector2(166,42));
            next=Button("Tutorial next",card,"Next",new Vector2(187,-159),new Vector2(230,42));nextLabel=next.GetComponentInChildren<Text>();
            dots=new Image[pages.Length];for(int i=0;i<dots.Length;i++)dots[i]=Image(Rect("Progress "+i,card,new Vector2((i-(dots.Length-1)*.5f)*18,190),new Vector2(12,3)),Color.white);
            owner.Hud?.Register(help,"Help and tutorial · H",()=>!IsOpen);
            Configure(XRSettings.isDeviceActive);card.gameObject.SetActive(false);UIFade.Set(shield.gameObject,false);
            if(PlayerPrefs.GetInt(SeenKey,0)==0)Open();
        }
        void Update()
        {
            Configure(XRSettings.isDeviceActive);
            if(IsOpen&&lastMode!=TreeControlTemplates.Current)Refresh();
            UIFade.Set(canvas,(gallery.HudVisible&&(IsOpen||!NavigationPreferences.PanelOpen&&!(gallery.Overlay?.PanelOpen??false)&&!(gallery.Library?.IsOpen??false)&&!(gallery.AISetup?.IsOpen??false)&&!(gallery.Batch?.IsOpen??false))&&(gallery.LocationEditor==null||!gallery.LocationEditor.IsOpen))&&!gallery.IsTimelineDragging);
        }
        void Configure(bool vr)
        {
            if(configured&&vr==wasVr)return;configured=true;wasVr=vr;
            canvas.renderMode=vr?RenderMode.WorldSpace:RenderMode.ScreenSpaceOverlay;canvas.worldCamera=Camera.main;
            if(vr){root.SetParent(Camera.main.transform,false);root.sizeDelta=new Vector2(960,540);root.localPosition=new Vector3(0,0,1.6f);root.localRotation=Quaternion.identity;root.localScale=Vector3.one*.0018f;}
            else{root.SetParent(gallery.transform,false);root.localScale=Vector3.one;}
            OverlayComfort.TextDensity(canvas,vr);comfort.Set(help,vr,new Vector2(-320,210));
            if(IsOpen)Refresh();
        }
        public void SetHudVisible(bool visible){UIFade.Set(canvas,(visible&&(gallery.LocationEditor==null||!gallery.LocationEditor.IsOpen))&&!gallery.IsTimelineDragging);}
        public void Open(){if(gallery.AISetup?.IsOpen??false)return;gallery.Hud?.SetVisible(true);page=0;UIFade.Set(card.gameObject,true);UIFade.Set(shield.gameObject,true);Refresh();}
        public void Dismiss(){UIFade.Set(card.gameObject,false);UIFade.Set(shield.gameObject,false);PlayerPrefs.SetInt(SeenKey,1);PlayerPrefs.Save();}
        public void Advance(){if(page==pages.Length-1)Dismiss();else{page++;Refresh();}}
        public void Previous(){page=Mathf.Max(0,page-1);Refresh();}
        void Refresh()
        {
            var p=pages[page];progress.text="GETTING STARTED  /  "+(page+1)+" OF "+pages.Length;
            title.text=p.Title;summary.text=p.Summary;instructions.text=wasVr?p.Quest:p.Desktop;hint.text=p.Hint;
            lastMode=TreeControlTemplates.Current;
            if(page==1)
            {
                summary.text=!wasVr?"W/S fly forward/back where you look, including up and down.":lastMode==TreeControlMode.Slide?"Slide around the tree at a steady height; turn to face another direction.":"Fly between generations. Looking around does not steer Quest movement.";
                if(wasVr&&lastMode==TreeControlMode.Slide)instructions.text="LEFT STICK: slide sideways / forward / back\nRIGHT STICK: smooth turning\nX: nearest sphere   |   Y: previous   |   A: next";
                if(wasVr&&lastMode==TreeControlMode.Fly)
                {
                    summary.text="Fly where you look, including up and down between generations.";
                    if(wasVr)instructions.text="LEFT STICK: fly forward / back and sideways\nLook up or down to climb or descend\nRIGHT STICK: smooth turning   |   X: nearest sphere";
                }
                hint.text="The flight icon opens holdable movement buttons. Y / A or [ / ] jump to the previous / next person. Right stick turns smoothly.";
            }
            UIFade.Set(back.gameObject,page>0);nextLabel.text=page==pages.Length-1?"Start exploring":"Next";
            for(int i=0;i<dots.Length;i++)dots[i].color=i==page?new Color(.95f,.72f,.3f):new Color(.23f,.37f,.39f);
        }
        static bool Contains(RectTransform rect,Ray ray,bool vr)
        {
            if(!rect.gameObject.activeInHierarchy)return false;
            if(!UIFade.Interactive(rect))return false;if(!vr)return RectTransformUtility.RectangleContainsScreenPoint(rect,Mouse.current?.position.ReadValue()??Vector2.zero);
            var plane=new Plane(rect.forward,rect.position);
            return plane.Raycast(ray,out float distance)&&rect.rect.Contains(rect.InverseTransformPoint(ray.GetPoint(distance)));
        }
        public bool HandleInput(Ray ray,bool vr,bool select,bool close)
        {
            if(!gallery.HudVisible||!canvas.enabled||(gallery.LocationEditor!=null&&gallery.LocationEditor.IsOpen))return false;
            bool key=!vr&&(Keyboard.current?.hKey.wasPressedThisFrame??false);
            if(IsOpen)
            {
                if(close||key)Dismiss();
                else if(select){if(Contains(next,ray,vr))Advance();else if(Contains(back,ray,vr))Previous();else if(Contains(skip,ray,vr)||Contains(help,ray,vr))Dismiss();}
                return true; // Modal even outside the card: no flight, hidden clicks or scene returns.
            }
            if(key){Open();return true;}
            if(!Contains(help,ray,vr))return false;
            if(select)Open();return true;
        }
        RectTransform Button(string name,Transform parent,string text,Vector2 position,Vector2 size)
        {var r=Rect(name,parent,position,size);Image(r,new Color(.08f,.3f,.29f,.96f));Label(r,text,Vector2.zero,size,18,Color.white);return r;}
        Text Label(Transform parent,string text,Vector2 pos,Vector2 size,int fontSize,Color color)
        {var t=Rect("Tutorial text",parent,pos,size).gameObject.AddComponent<Text>();t.font=gallery.font;t.text=text;t.fontSize=fontSize;t.color=color;t.alignment=TextAnchor.MiddleCenter;t.raycastTarget=false;return t;}
        static Image Image(RectTransform rect,Color color){var image=rect.gameObject.AddComponent<Image>();image.color=color;image.raycastTarget=false;return image;}
        static RectTransform Rect(string name,Transform parent,Vector2 pos,Vector2 size)
        {var r=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();r.SetParent(parent,false);r.anchorMin=r.anchorMax=new Vector2(.5f,.5f);r.anchoredPosition=pos;r.sizeDelta=size;return r;}
    }
}
