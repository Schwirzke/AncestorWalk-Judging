using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR;

namespace AncestorWalk
{
    public sealed class SceneBatchOverlay : MonoBehaviour
    {
        public static bool MultiYearPanelEnabled=>false;
        sealed class Hit{public RectTransform Rect;public Action Action;}
        readonly List<Hit> hits=new();readonly List<GameObject> items=new();
        AncestorGallery gallery;Canvas canvas;RectTransform root,launcher,card;Text launcherText,field,review,summary,submit,queueText,queueHint;
        Person person;string years="",error="";bool queuePage,regeneratePage,configured,wasVr,dirty,selectAll;Image selection;string regenerationTree;List<(Person Person,int Year,string Place)> regenerationTargets;Keyboard keyboard;
        bool lastIndexReady;int[] lastCachedYears;
        public bool IsOpen=>card!=null&&UIFade.Visible(card.gameObject);
        public string Years=>years;
        public string Review=>review!=null?review.text:"";
        public void Build(AncestorGallery owner)
        {
            gallery=owner;root=(RectTransform)transform;canvas=gameObject.AddComponent<Canvas>();canvas.sortingOrder=140;
            var scaler=gameObject.AddComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(960,540);scaler.matchWidthOrHeight=.5f;
            launcher=Rect("Generation queue",root,new Vector2(135,234),new Vector2(110,34));Background(launcher);launcherText=Label(launcher,"Active Jobs",Vector2.zero,launcher.sizeDelta,16);UIFade.Set(launcher.gameObject,false);
            card=Rect("Generate years",root,new Vector2(0,-20),new Vector2(740,420));Background(card,new Color(.018f,.04f,.055f,.98f));card.gameObject.SetActive(false);
            gallery.Hud?.Register(launcher,"Active Jobs");gallery.Generation.Changed+=Changed;Configure(XRSettings.isDeviceActive);
        }
        void Changed(){dirty=true;}
        void Update()
        {
            if(IsOpen&&!queuePage&&!regeneratePage&&!MultiYearPanelEnabled)Close();
            if((Keyboard.current?.f8Key.wasPressedThisFrame??false)&&!(gallery.AISetup?.IsOpen??false)&&!(gallery.Tutorial?.IsOpen??false)&&!(gallery.LocationEditor?.IsOpen??false)&&!(gallery.Library?.IsOpen??false))
            {if(IsOpen&&queuePage)Close();else Open();}
            Configure(XRSettings.isDeviceActive);launcher.anchoredPosition=new Vector2(135,gallery.SelectedId!=null?(wasVr?174:210):(wasVr?210:234));
            // Hide the HUD launcher; Active Jobs remains available with F8.
            UIFade.Set(launcher.gameObject,false);
            int count=gallery.Generation.WaitingCount+gallery.Generation.ActiveJobs.Count;launcherText.text=count>0?"Active Jobs · "+count:"Active Jobs";
            if(IsOpen&&person!=null){var cached=gallery.AvailableYears(person.Id);if(lastIndexReady!=gallery.CacheIndexReady||lastCachedYears!=cached){lastIndexReady=gallery.CacheIndexReady;lastCachedYears=cached;dirty=true;}}
            if(IsOpen&&regeneratePage&&lastIndexReady!=gallery.CacheIndexReady){lastIndexReady=gallery.CacheIndexReady;dirty=true;}
            if(IsOpen&&queuePage&&Time.unscaledTime>=nextProgressRefresh)dirty=true;
            if(dirty&&IsOpen){dirty=false;if(queuePage)RefreshQueue();else if(regeneratePage)RefreshRegenerationReview();else RefreshReview();}
        }
        void Configure(bool vr)
        {
            if(configured&&wasVr==vr)return;configured=true;wasVr=vr;canvas.renderMode=vr?RenderMode.WorldSpace:RenderMode.ScreenSpaceOverlay;canvas.worldCamera=Camera.main;
            if(vr){root.SetParent(Camera.main.transform,false);root.sizeDelta=new Vector2(960,540);root.localPosition=new Vector3(0,0,1.6f);root.localScale=Vector3.one*.0018f;root.localRotation=Quaternion.identity;}
            else{root.SetParent(gallery.transform,false);root.localScale=Vector3.one;}OverlayComfort.TextDensity(canvas,vr);
        }
        public void Open(Person target=null){if(target!=null&&!MultiYearPanelEnabled)return;OpenPage(target,false);}
        public void OpenRegenerateAll()=>OpenPage(null,true);
        void OpenPage(Person target,bool regenerate)
        {
            if(gallery.AISetup?.IsOpen??false)return;
            if((gallery.Tutorial?.IsOpen??false)||(gallery.LocationEditor?.IsOpen??false)||(gallery.Library?.IsOpen??false))return;
            person=target;regeneratePage=regenerate;regenerationTree=gallery.TreeId;queuePage=target==null&&!regenerate;error="";years=target==null?"":MoveYears();selectAll=years.Length>0;UIFade.Set(card.gameObject,true);gallery.Hud.SetVisible(true);gallery.Overlay?.CancelInteraction();
            UnityEngine.Object.FindAnyObjectByType<NavigationPreferences>()?.SetOpen(false);gallery.SetLocationModal(true);if(keyboard!=null)keyboard.onTextInput-=Type;keyboard=Keyboard.current;if(keyboard!=null)keyboard.onTextInput-=Type;Draw();
        }
        string MoveYears()
        {
            if(person==null||!LifeEndpointScenes.TrySceneRange(person,out int birth,out int end))return "";
            return string.Join(", ",new[]{birth}.Concat(gallery.Locations(person.Id).entries.Select(e=>e.year)).Where(y=>y>=birth&&y<=end).Distinct().OrderBy(y=>y));
        }
        TMPro.TMP_InputField nativeInput;
        public void SetYears(string text){if((text?.Length??0)>500){error="Keep the year list under 500 characters.";RefreshReview();return;}years=text??"";selectAll=years.Length>0;error="";RefreshReview();}
        public void Close(){if(nativeInput!=null)nativeInput.DeactivateInputField();if(keyboard!=null)keyboard.onTextInput-=Type;keyboard=null;UIFade.Set(card.gameObject,false);gallery.SetLocationModal(false);}
        void Type(char c){}
        void Append(string text){NativeTextInput.Insert(nativeInput,text);}
        void Delete(){NativeTextInput.Delete(nativeInput);}
        static bool Allowed(char c)=>(c>='0'&&c<='9')||c==','||c=='-'||c=='/'||c==' ';
        List<(int Year,string Place)> Targets(out int cached)
        {
            cached=0;var list=new List<(int,string)>();if(!gallery.CacheIndexReady)throw new InvalidOperationException("Checking cached scenes. Please wait…");if(person==null||!LifeEndpointScenes.TrySceneRange(person,out int birth,out int end))throw new InvalidOperationException("A usable lifespan is required.");
            foreach(int year in SceneQueueState.ParseYears(years,birth,end))
            {if(Array.IndexOf(gallery.AvailableYears(person.Id),year)>=0){cached++;continue;}list.Add((year,gallery.Locations(person.Id).At(person,year)));}return list;
        }
        void RefreshReview()
        {
            if(field==null||person==null)return;if(nativeInput!=null&&nativeInput.text!=years)nativeInput.SetTextWithoutNotify(years);try
            {
                var targets=Targets(out int cached);review.text=string.Join("\n",targets.Take(4).Select(t=>t.Year+" · "+t.Place))+(targets.Count>4?"\n…and "+(targets.Count-4)+" more exact years":"");
                summary.text=string.IsNullOrEmpty(error)?targets.Count+" new scene"+(targets.Count==1?"":"s")+" · "+cached+" cached year"+(cached==1?"":"s")+" skipped":error;submit.text="QUEUE "+targets.Count+" SCENES";
            }
            catch(Exception e){review.text="";summary.text=e.Message;submit.text="CHECK YEARS";}
        }
        public void Submit()
        {
            try{var targets=Targets(out _);if(targets.Count==0){error="No missing years selected.";RefreshReview();return;}
                gallery.Generation.Enqueue(gallery.TreeId,person,gallery.Document.IsDemo,targets);queuePage=true;Draw();}
            catch(Exception e){error=e.Message;RefreshReview();}
        }
        void RefreshRegenerationReview()
        {
            if(review==null)return;
            try
            {
                if(gallery.TreeId!=regenerationTree)throw new InvalidOperationException("The open tree changed. Cancel and reopen this confirmation.");
                var targets=gallery.RegenerationTargets();regenerationTargets=targets;
                review.text="Are you sure?\n\nRegenerate "+targets.Count+" cached scenes for "+targets.Select(t=>t.Person.Id).Distinct().Count()+" people in the current tree.\n\nExisting images will be replaced only when new results are saved. This uses new AI requests. Already queued years are skipped.";
                summary.text=error;submit.text=targets.Count>0?"REGENERATE "+targets.Count+" SCENES":"NO SCENES TO REGENERATE";
            }
            catch(Exception e){review.text=e.Message;summary.text=error;submit.text="NOT READY";}
        }
        public void ConfirmRegenerateAll()
        {
            if(!IsOpen||!regeneratePage)return;
            try
            {
                if(gallery.TreeId!=regenerationTree)throw new InvalidOperationException("The open tree changed. Cancel and reopen this confirmation.");
                var current=gallery.RegenerationTargets();var targets=current.Where(t=>regenerationTargets!=null&&regenerationTargets.Any(shown=>shown.Person.Id==t.Person.Id&&shown.Year==t.Year&&ResidenceTimeline.Same(shown.Place,t.Place))).ToList();if(targets.Count==0){error="No cached scenes need regeneration.";RefreshRegenerationReview();return;}
                gallery.Generation.RegenerateAll(regenerationTree,targets,gallery.Document.IsDemo);regeneratePage=false;queuePage=true;Draw();
            }
            catch(Exception e){error=e.Message;RefreshRegenerationReview();}
        }
        void Draw()
        {
            hits.Clear();nativeInput=null;field=review=summary=submit=queueText=queueHint=null;foreach(var item in items){item.SetActive(false);Destroy(item);}items.Clear();
            Text(regeneratePage?"Regenerate all cached scenes?":queuePage?"Active Jobs":person.Name+" · Generate years",new Vector2(0,178),new Vector2(580,32),21);
            Button("×",new Vector2(325,178),new Vector2(40,32),Close);
            if(regeneratePage)
            {
                review=Text("",new Vector2(0,35),new Vector2(650,220),19);
                summary=Text("",new Vector2(0,-105),new Vector2(650,40),14);summary.color=new Color(1,.8f,.45f);
                Button("Cancel",new Vector2(-170,-170),new Vector2(310,40),Close);
                submit=Button("REGENERATE ALL",new Vector2(170,-170),new Vector2(310,40),ConfirmRegenerateAll);
                RefreshRegenerationReview();return;
            }
            if(queuePage)
            {
                queueText=Text("",new Vector2(0,38),new Vector2(650,214),14);
                queueHint=Text(HudOverlay.GenerationBrowseHint,new Vector2(0,-95),new Vector2(650,30),12);RefreshQueue();
                Button("Pause after active",new Vector2(-170,-134),new Vector2(310,34),()=>{gallery.Generation.PauseQueue();RefreshQueue();});
                Button("Resume jobs",new Vector2(170,-134),new Vector2(310,34),()=>{gallery.Generation.ResumeQueue();RefreshQueue();});
                Button("Clear waiting",new Vector2(-170,-177),new Vector2(310,30),()=>{gallery.Generation.ClearWaiting();RefreshQueue();});
                Button("Keep exploring",new Vector2(170,-177),new Vector2(310,30),Close);return;
            }
            var input=Rect("Year list",card,new Vector2(0,137),new Vector2(650,36));items.Add(input.gameObject);Background(input,new Color(.1f,.17f,.19f,1));field=Label(input,years,Vector2.zero,input.sizeDelta,18);nativeInput=NativeTextInput.Bind(field,v=>{years=v;error="";RefreshReview();},500);nativeInput.onValidateInput=(text,index,c)=>Allowed(c)?c:'\0';hits.Add(new Hit{Rect=(RectTransform)nativeInput.transform,Action=()=>NativeTextInput.Focus(nativeInput)});NativeTextInput.Focus(nativeInput,true);
            Text("Years: 1934, 1936   ·   Range: 1900-1940/10   ·   Exact years, no rounding",new Vector2(0,105),new Vector2(680,22),12);
            Button("Saved move years",new Vector2(-170,76),new Vector2(310,30),()=>SetYears(MoveYears()));
            Button("Selected year",new Vector2(170,76),new Vector2(310,30),()=>SetYears(gallery.Year(person.Id).ToString()));
            review=Text("",new Vector2(0,12),new Vector2(650,90),14);
            string digits="1234567890";for(int i=0;i<digits.Length;i++){string key=digits[i].ToString();Button(key,new Vector2((i-4.5f)*55,-57),new Vector2(50,30),()=>Append(key));}
            foreach(var pair in new[]{(",",-275f),("-",-220f),("/",-165f)}){string value=pair.Item1;Button(value,new Vector2(pair.Item2,-93),new Vector2(50,30),()=>Append(value));}
            Button("Space",new Vector2(-60,-93),new Vector2(140,30),()=>Append(" "));Button("Delete",new Vector2(90,-93),new Vector2(140,30),Delete);Button("Clear",new Vector2(240,-93),new Vector2(140,30),()=>SetYears(""));
            summary=Text("",new Vector2(0,-126),new Vector2(680,28),13);summary.color=new Color(1,.8f,.45f);
            submit=Button("QUEUE SCENES",new Vector2(-170,-170),new Vector2(310,36),Submit);
            Button("Active Jobs",new Vector2(170,-170),new Vector2(310,36),()=>{queuePage=true;Draw();});RefreshReview();
        }
        float nextProgressRefresh;
        void RefreshQueue()
        {
            nextProgressRefresh=Time.unscaledTime+1;
            if(queueText==null)return;var generation=gallery.Generation;var active=generation.ActiveJobs;
            if(queueHint!=null)queueHint.gameObject.SetActive(generation.ActiveCount+generation.WaitingCount>0);
            string Short(string value,int length)=>value.Length>length?value.Substring(0,length-1)+"…":value;
            string Describe(PendingScene x)=>Short(gallery.TreeId==x.treeId&&gallery.Tree.People.TryGetValue(x.personId,out var p)?p.Name:x.personId,24)+" · "+x.job.year+" · "+Short(x.job.place,30)+(x.phase==2?" · "+generation.PercentText(x):"")+(x.retryCount>0?" · retry "+x.retryCount+"/"+SceneRetryPolicy.MaximumRetries:"");
            string details=active.Count>4?"":"\n\n"+string.Join("\n",generation.Waiting.Take(2).Select(Describe))+(generation.WaitingCount>2?"\n…and "+(generation.WaitingCount-2)+" more":"")+"\n"+string.Join("\n",generation.Recent.Take(1));
            queueText.text="Generating "+generation.ActiveCount+"/"+SceneGeneration.MaximumConcurrent+"\n"+string.Join("\n",active.Select(Describe))+"\nWaiting "+generation.WaitingCount+" · Saved "+generation.CompletedCount+" · Failed "+generation.FailedCount+details+"\n\n"+generation.QueueMessage;
        }
        static bool Contains(RectTransform rect,Ray ray,bool vr)
        {if(!UIFade.Interactive(rect))return false;if(!vr)return RectTransformUtility.RectangleContainsScreenPoint(rect,Mouse.current?.position.ReadValue()??Vector2.zero);var plane=new Plane(rect.forward,rect.position);return plane.Raycast(ray,out float d)&&rect.rect.Contains(rect.InverseTransformPoint(ray.GetPoint(d)));}
        public bool HandleInput(Ray ray,bool vr,bool pressed,bool back)
        {
            if(!IsOpen){if(pressed&&launcher.gameObject.activeInHierarchy&&Contains(launcher,ray,vr)){Open();return true;}return false;}
            if(back||(!vr&&(Keyboard.current?.escapeKey.wasPressedThisFrame??false))){Close();return true;}

            if(pressed)for(int i=hits.Count-1;i>=0;i--)if(hits[i].Rect!=null&&UIFade.Interactive(hits[i].Rect)&&Contains(hits[i].Rect,ray,vr)){if(vr&&nativeInput!=null&&hits[i].Rect==nativeInput.transform)NativeTextInput.Point(nativeInput,ray);else hits[i].Action();break;}return true;
        }
        static RectTransform Rect(string name,Transform parent,Vector2 at,Vector2 size){var r=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();r.SetParent(parent,false);r.anchorMin=r.anchorMax=new Vector2(.5f,.5f);r.anchoredPosition=at;r.sizeDelta=size;return r;}
        static void Background(RectTransform r,Color? c=null){var image=r.gameObject.AddComponent<Image>();image.color=c??new Color(.06f,.23f,.25f,.95f);image.raycastTarget=false;}
        Text Label(Transform parent,string value,Vector2 at,Vector2 size,int font){var t=Rect(value,parent,at,size).gameObject.AddComponent<Text>();t.font=gallery.font;t.text=value;t.fontSize=font;t.color=Color.white;t.alignment=TextAnchor.MiddleCenter;t.supportRichText=false;t.raycastTarget=false;t.resizeTextForBestFit=true;t.resizeTextMinSize=11;t.resizeTextMaxSize=font;return t;}
        Text Text(string value,Vector2 at,Vector2 size,int font){var label=Label(card,value,at,size,font);items.Add(label.gameObject);return label;}
        Text Button(string title,Vector2 at,Vector2 size,Action action){var r=Rect(title,card,at,size);items.Add(r.gameObject);Background(r);hits.Add(new Hit{Rect=r,Action=action});return Label(r,title,Vector2.zero,size,16);}
        void OnDestroy(){if(gallery!=null&&gallery.Generation!=null)gallery.Generation.Changed-=Changed;if(keyboard!=null)keyboard.onTextInput-=Type;}
    }
}
