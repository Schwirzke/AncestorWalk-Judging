using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR;

namespace AncestorWalk
{
    // One modal editor shared by desktop and controller rays. Only Input System supplies input.
    public sealed class TreeLibraryOverlay : MonoBehaviour
    {
        sealed class Hit{public RectTransform Rect;public Action Action;}
        readonly List<Hit> hits=new();readonly List<GameObject> pageObjects=new();
        AncestorGallery gallery;TreeStore store;Canvas canvas;RectTransform root,launcher,card;Text heading,status;
        bool configured,wasVr;string page="home",message="",editingId,photoPerson,pickRole,pickFamily,previousPage;int offset,photoIndex;bool choosePhoto;string authoringPerson;
        List<TreeDocument> library=new();TreeDocument draft;FamilyRecord family;TreeImportDraft importDraft;
        Task pending;Action completion;CancellationTokenSource cancel;volatile string progress;
        PhotoTextureCache.Entry photoEntry;RawImage photoView;Text photoLoading;
        PhotoSegmentation segmentation;PhotoRecord cutoutSource;string cutoutTree;
        Action<PhotoRecord> scenePhotoPicked;Action scenePhotoCancelled;string scenePhotoPerson;
        string textValue,textTitle;Action<string> textDone;Keyboard keyboard;Text textInput;TMPro.TMP_InputField nativeInput;Image textSelection;bool lowerKeys,selectAll;
        Action switchAction;string switchFrom,switchPage;Text switchSummary;int switchRevision=-1;
        Action<bool> sceneVisitCompletion;
        TreeDocument deleting;Text deleteSummary;int deleteRevision=-1;
        public bool IsOpen=>card!=null&&UIFade.Visible(card.gameObject);public bool Busy=>pending!=null;
        public string Message=>message;public TreeStore Store=>store;
        public void Build(AncestorGallery owner)
        {
            gallery=owner;store=new TreeStore(PanoramaCache.StorageRoot);root=(RectTransform)transform;
            segmentation=owner.gameObject.GetComponent<PhotoSegmentation>()??owner.gameObject.AddComponent<PhotoSegmentation>();segmentation.Changed+=CutoutsChanged;
            canvas=gameObject.AddComponent<Canvas>();canvas.sortingOrder=130;var scaler=gameObject.AddComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(960,540);scaler.matchWidthOrHeight=.5f;
            launcher=Rect("Open tree library",root,new Vector2(-289,234),new Vector2(110,38));Background(launcher,new Color(.06f,.22f,.23f,.85f));Label(launcher,"Trees",Vector2.zero,launcher.sizeDelta,17);
            card=Rect("Tree library editor",root,new Vector2(0,-20),new Vector2(880,420));Background(card,new Color(.018f,.04f,.055f,.98f));card.gameObject.SetActive(false);
            owner.Hud?.Register(launcher,"Trees · create, import and edit · T");Configure(XRSettings.isDeviceActive);
            var demo=owner.DemoDocument;var legacy=owner.LegacyDemoDocument;
            Work(()=>{var items=store.List();foreach(var builtIn in new[]{demo,legacy})if(builtIn!=null&&items.All(d=>d.Id!=builtIn.Id))items.Add(builtIn);return items;},items=>{library=items;string last=PlayerPrefs.GetString("AncestorWalk.LastTree","");if(last==PanoramaCache.PreviousProductDemoTreeId){last=PanoramaCache.ProductDemoTreeId;PlayerPrefs.SetString("AncestorWalk.LastTree",last);PlayerPrefs.Save();}var doc=items.FirstOrDefault(x=>x.Id==last);if(doc!=null&&doc.Id!=gallery.TreeId)gallery.OpenDocument(doc);});
        }
        void Work<T>(Func<T> action,Action<T> finished)
        {
            if(Busy)throw new InvalidOperationException("Wait for the current tree operation.");
            var task=Task.Run(action);pending=task;completion=()=>finished(task.Result);message="";progress="Working…";
            if(IsOpen)Draw();
        }
        void Update()
        {
            Configure(XRSettings.isDeviceActive);launcher.anchoredPosition=gallery.IsTree?new Vector2(wasVr?-199:-289,wasVr?210:234):new Vector2(-135,wasVr?174:210);
            UIFade.Set(launcher.gameObject,!(gallery.AISetup?.IsOpen??false)&&!NavigationPreferences.PanelOpen&&!(gallery.Overlay?.PanelOpen??false)&&gallery.IsTree&&!IsOpen&&gallery.HudVisible&&!gallery.IsTimelineDragging&&!(gallery.Tutorial?.IsOpen??false)&&!(gallery.LocationEditor?.IsOpen??false)&&!(gallery.Batch?.IsOpen??false));
            if(pending!=null&&pending.IsCompleted)
            {
                var done=pending;var callback=completion;pending=null;completion=null;cancel?.Dispose();cancel=null;
                try{if(done.IsFaulted){message=done.Exception.GetBaseException().Message;_=done.Exception;if(page=="import")page="home";FinishSceneVisit(false);}else if(done.IsCanceled){message="Import cancelled.";page="home";FinishSceneVisit(false);}else callback();}
                catch(Exception e){message=e.Message;FinishSceneVisit(false);}
                if(IsOpen){gallery.SetLocationModal(true);Draw();}
            }
            if(status!=null)status.text=Busy?progress:!string.IsNullOrEmpty(message)?message:
                gallery.Generation.Busy?"Finishing "+gallery.Generation.ActiveCount+" scene(s). You can switch trees when they finish.":"";
            if(IsOpen&&page=="switch"&&switchSummary!=null)UpdateSwitchSummary();
            if(IsOpen&&page=="delete"&&deleteSummary!=null)UpdateDeleteSummary();
            if(photoEntry!=null&&photoView!=null&&photoEntry.Ready)
            {if(photoEntry.Failed)photoLoading.text="Photo unavailable";else if(photoView.texture!=photoEntry.Texture&&PanoramaFrameBudget.TryTake()){photoView.texture=photoEntry.Texture;photoLoading.text="";}}
        }
        void Configure(bool vr)
        {
            if(configured&&wasVr==vr)return;configured=true;wasVr=vr;canvas.renderMode=vr?RenderMode.WorldSpace:RenderMode.ScreenSpaceOverlay;canvas.worldCamera=Camera.main;
            if(vr){root.SetParent(Camera.main.transform,false);root.sizeDelta=new Vector2(960,540);root.localPosition=new Vector3(0,0,1.6f);root.localScale=Vector3.one*.0018f;root.localRotation=Quaternion.identity;}
            else{root.SetParent(gallery.transform,false);root.localScale=Vector3.one;}
            card.sizeDelta=new Vector2(vr?760:880,420);OverlayComfort.TextDensity(canvas,vr);
        }
        public void Open()
        {
            if(gallery.AISetup?.IsOpen??false)return;
            if(gallery.Batch?.IsOpen??false)return;
            if(gallery.LocationEditor?.IsOpen??false)return;if(gallery.Tutorial?.IsOpen??false)return;
            authoringPerson=null;gallery.Hud?.SetVisible(true);gallery.Overlay?.CancelInteraction();UnityEngine.Object.FindAnyObjectByType<NavigationPreferences>()?.SetOpen(false);
            UIFade.Set(card.gameObject,true);gallery.SetLocationModal(true);page="home";offset=0;message="";Subscribe();Draw();
        }
        public void Close()
        {
            if(Busy){cancel?.Cancel();return;}authoringPerson=null;UIFade.Set(card.gameObject,false);gallery.SetLocationModal(false);Unsubscribe();ReleasePhoto();
            FinishSceneVisit(false);
            var cancelled=scenePhotoCancelled;scenePhotoPicked=null;scenePhotoCancelled=null;scenePhotoPerson=null;cancelled?.Invoke();
            switchAction=null;switchFrom=switchPage=null;deleting=null;
            if(importDraft!=null){var abandoned=importDraft;importDraft=null;_ = Task.Run(()=>TreeArchiveImport.Cleanup(store,abandoned));}
        }
        void Subscribe(){Unsubscribe();keyboard=Keyboard.current;if(keyboard!=null)keyboard.onTextInput-=Type;}
        void Unsubscribe(){if(nativeInput!=null)nativeInput.DeactivateInputField();if(keyboard!=null)keyboard.onTextInput-=Type;keyboard=null;}
        void RefreshText(){if(nativeInput!=null&&nativeInput.text!=textValue)nativeInput.SetTextWithoutNotify(textValue);}
        void AppendText(string text){NativeTextInput.Insert(nativeInput,text);}
        void DeleteText(){NativeTextInput.Delete(nativeInput);}
        void Type(char c){} // Editing is owned by InputField.
        void EditText(string title,string value,Action<string> done)
        {previousPage=page;textTitle=title;textValue=value??"";selectAll=textValue.Length>0;textDone=done;page="text";Draw();}
        void EndText(){var action=textDone;string value=textValue.Trim();page=previousPage;action(value);Draw();}
        public void OpenPhotos(string personId)
        {Open();if(!IsOpen)return;photoPerson=personId;photoIndex=0;choosePhoto=false;page="photos";Draw();}
        public void PickScenePhoto(string personId,Action<PhotoRecord> selected,Action cancelled)
        {
            if(Busy)throw new InvalidOperationException("Wait for the current tree operation.");
            scenePhotoPicked=selected;scenePhotoCancelled=cancelled;scenePhotoPerson=personId;photoPerson=null;photoIndex=0;choosePhoto=false;page="photos";message="";
            UIFade.Set(card.gameObject,true);gallery.SetLocationModal(true);Subscribe();Draw();
        }
        void CompleteScenePhoto(PhotoRecord photo)
        {var selected=scenePhotoPicked;scenePhotoPicked=null;scenePhotoCancelled=null;Close();selected?.Invoke(photo);}
        public void OpenPersonEditor(string id)
        {Open();if(IsOpen)EditPerson(id);}
        public bool CanVisitScene(string tree,string person,int year,out string reason)
        {
            reason="That person or year is no longer available.";
            if(Busy||gallery.IsBuildingTree){reason="Wait for the tree to finish opening.";return false;}
            if(string.IsNullOrEmpty(tree)||string.IsNullOrEmpty(person)||year<=0)return false;
            Person target=null;
            if(tree==gallery.TreeId)gallery.Tree.People.TryGetValue(person,out target);
            else
            {
                var document=library.Find(d=>d.Id==tree);
                document??=gallery.DemoDocument?.Id==tree?gallery.DemoDocument:gallery.LegacyDemoDocument?.Id==tree?gallery.LegacyDemoDocument:null;
                target=document?.People.Find(p=>p.Id==person);
            }
            if(target==null||!LifeEndpointScenes.TrySceneRange(target,out int start,out int end)||year<start||year>end)return false;
            reason="";return true;
        }
        public void VisitScene(string tree,string person,int year,Action<bool> finished)
        {
            if(!CanVisitScene(tree,person,year,out _)){finished?.Invoke(false);return;}
            if(tree==gallery.TreeId){finished?.Invoke(FocusScene(person,year));return;}
            Open();if(!IsOpen){finished?.Invoke(false);return;}sceneVisitCompletion=finished;
            var demoDocument=library.Find(d=>d.Id==tree)??(gallery.DemoDocument?.Id==tree?gallery.DemoDocument:gallery.LegacyDemoDocument?.Id==tree?gallery.LegacyDemoDocument:null);bool isDemo=demoDocument?.IsDemo==true;
            try
            {
                RequestTreeChange(tree,()=>Work(()=>isDemo?demoDocument:store.Load(tree),document=>
                {
                    var target=document.People.Find(p=>p.Id==person);
                    if(target==null||!LifeEndpointScenes.TrySceneRange(target,out int start,out int end)||year<start||year>end)
                        throw new InvalidOperationException("That person or year is no longer available.");
                    gallery.OpenDocument(document);
                    var completed=sceneVisitCompletion;sceneVisitCompletion=null;Close();
                    StartCoroutine(FinishSceneVisitWhenReady(tree,person,year,completed));
                }));
            }
            catch(Exception e){message=e.Message;FinishSceneVisit(false);Draw();}
        }
        System.Collections.IEnumerator FinishSceneVisitWhenReady(string tree,string person,int year,Action<bool> finished)
        {
            while(gallery.TreeId==tree&&gallery.IsBuildingTree)yield return null;
            finished?.Invoke(gallery.TreeId==tree&&FocusScene(person,year));
        }
        bool FocusScene(string person,int year)
        {
            if(!CanVisitScene(gallery.TreeId,person,year,out _))return false;
            gallery.SetYear(person,year);if(gallery.SelectedId!=person)gallery.Select(person);
            if(gallery.SelectedId!=person||gallery.Year(person)!=year||gallery.Overlay==null)return false;
            if(!gallery.Overlay.TimelineOpen)gallery.Overlay.ToggleTimeline();return true;
        }
        void FinishSceneVisit(bool success){var done=sceneVisitCompletion;sceneVisitCompletion=null;done?.Invoke(success);}
        public void CreateFromGhost(GhostSlot slot)
        {
            if(Busy||!gallery.IsTree||!gallery.BuildMode)return;
            try
            {
                var copy=TreeAuthoring.AddPerson(gallery.Document,slot,out string id);gallery.ValidateAuthoringDocument(copy);
                Open();if(!IsOpen)return;
                Work(()=>{store.Save(copy);return copy;},saved=>
                {
                    gallery.ApplyAuthoringDocument(saved);gallery.SetLocationModal(true);Remember(saved);EditPerson(id);authoringPerson=id;
                });
            }
            catch(Exception e){Open();if(IsOpen){message=e.Message;Draw();}}
        }
        void Remember(TreeDocument saved){library.RemoveAll(d=>d.Id==saved.Id);library.Insert(0,saved);}
        void SaveAuthoringPerson()
        {
            string id=authoringPerson;var copy=draft.Copy();gallery.ValidateAuthoringDocument(copy,id);
            Work(()=>{store.Save(copy);return copy;},saved=>{gallery.ApplyAuthoringDocument(saved,id);Remember(saved);Close();});
        }
        public void ImportPath(string path,string choice=null)
        {
            if(!IsOpen)Open();if(!IsOpen)return;page="import";cancel=new CancellationTokenSource();var token=cancel.Token;
            Work(()=>TreeArchiveImport.Stage(store,path,choice,token,p=>progress=p),result=>{importDraft=result;page=result.Document==null?"gedchoose":"preview";offset=0;});
        }
        [UnityEngine.Scripting.Preserve]
        public void OnFilePicked(string path)
        {
            if(string.IsNullOrEmpty(path)){choosePhoto=false;message="File selection cancelled.";Draw();return;}
            if(path.StartsWith("ERROR:")){message=path.Substring(6);Draw();return;}
            if(scenePhotoPicked!=null){Work(()=>TreeArchiveImport.AddPhoto(store,path),photo=>
            {
                if(ProductDemoAuthoring.CanEdit(gallery.Document))
                {
                    var copy=gallery.Document.Copy();if(copy.Photos.All(p=>p.Id!=photo.Id))copy.Photos.Add(photo);
                    Save(copy,()=>CompleteScenePhoto(photo));
                }
                else CompleteScenePhoto(photo);
            });return;}
            if(choosePhoto)
            {
                string target=photoPerson;Work(()=>TreeArchiveImport.AddPhoto(store,path),photo=>{var copy=gallery.Document.Copy();if(ProductDemoAuthoring.ReadOnly(copy))throw new InvalidOperationException("Copy the demo before adding photos.");if(copy.Photos.All(p=>p.Id!=photo.Id))copy.Photos.Add(photo);var person=copy.People.Single(p=>p.Id==target);if(!person.PhotoIds.Contains(photo.Id))person.PhotoIds.Add(photo.Id);if(string.IsNullOrEmpty(person.PrimaryPhotoId))person.PrimaryPhotoId=photo.Id;Save(copy,()=>{page="photos";choosePhoto=false;});});
            }
            else ImportPath(path);
        }
        void PickFile(bool photo)
        {
            choosePhoto=photo;
#if UNITY_EDITOR
            string path=UnityEditor.EditorUtility.OpenFilePanel(photo?"Choose JPEG or PNG":"Choose GEDCOM, ZIP or GEDZIP","","");OnFilePicked(path);
#elif UNITY_ANDROID
            try{using var player=new AndroidJavaClass("com.unity3d.player.UnityPlayer");using var activity=player.GetStatic<AndroidJavaObject>("currentActivity");using var intent=new AndroidJavaObject("android.content.Intent");intent.Call<AndroidJavaObject>("setClassName",activity.Call<string>("getPackageName"),"com.ancestorwalk.importer.FilePickerActivity").Dispose();intent.Call<AndroidJavaObject>("putExtra","callback",gameObject.name).Dispose();intent.Call<AndroidJavaObject>("putExtra","photos",photo).Dispose();activity.Call("startActivity",intent);}
            catch(Exception){message="File picker unavailable. Put a file in the app's ImportInbox folder and use Import inbox.";Draw();}
#else
            EditText("Local file path","",path=>OnFilePicked(path));
#endif
        }
        void Save(TreeDocument document,Action done=null)
        {
            if(document.Id!=gallery.TreeId){RequestTreeChange(document.Id,()=>SaveIdle(document,done));return;}
            SaveIdle(document,done);
        }
        void SaveIdle(TreeDocument document,Action done)
        {
            RequireIdleGeneration();
            bool keepScene=(page=="photos"||page=="cutouts")&&gallery.SelectedId!=null;var copy=document.Copy();Work(()=>{store.Save(copy);return copy;},saved=>{if(keepScene)gallery.ApplyPhotoDocument(saved);else gallery.OpenDocument(saved);gallery.SetLocationModal(true);library.RemoveAll(d=>d.Id==saved.Id);library.Insert(0,saved);done?.Invoke();});
        }
        void RequireIdleGeneration()
        {
            if(gallery.Generation.CancellationNeedsSaving)throw new InvalidOperationException("Retry Cancel jobs & switch to save the stopped-job state.");
            if(gallery.Generation.Busy)throw new InvalidOperationException("Finishing active scenes. Try again when they finish; new jobs wait while Trees is open.");
        }
        void RequestTreeChange(string target,Action action)
        {
            if(target!=gallery.TreeId&&(gallery.Generation.ActiveForTree(gallery.TreeId)>0||gallery.Generation.WaitingForTree(gallery.TreeId)>0||gallery.Generation.CancellationNeedsSaving))
            {switchAction=action;switchFrom=gallery.TreeId;switchPage=page;page="switch";message="";Draw();return;}
            RequireIdleGeneration();action();
        }
        void UpdateSwitchSummary()
        {
            if(switchRevision==gallery.Generation.Revision)return;switchRevision=gallery.Generation.Revision;
            switchSummary.text=(gallery.Generation.CancellationNeedsSaving?"Retry saving the stopped-job state before switching?":"Cancel "+gallery.Generation.ActiveForTree(switchFrom)+" active and "+gallery.Generation.WaitingForTree(switchFrom)+" waiting jobs for this tree?")+"\n\nSaved scenes will be kept.\nRequests already sent may still finish at Meta.";
        }
        void SwitchConfirmation()
        {
            heading.text="Switch trees?";
            switchRevision=-1;switchSummary=Text("",new Vector2(0,30),new Vector2(650,210),19);UpdateSwitchSummary();
            Button("Stay here",new Vector2(-170,-130),new Vector2(310,42),CancelSwitch);
            Button("Cancel jobs & switch",new Vector2(170,-130),new Vector2(310,42),()=>
            {
                if(gallery.TreeId!=switchFrom||switchAction==null)throw new InvalidOperationException("Choose the tree again.");
                var next=switchAction;var stopped=gallery.Generation.CancelTreeJobs(switchFrom);
                Work(()=>{stopped.GetAwaiter().GetResult();return true;},_=>{switchAction=null;switchFrom=null;page=switchPage;switchPage=null;next();});progress="Cancelling jobs…";
            });
        }
        void CancelSwitch(){page=switchPage??"home";switchAction=null;switchFrom=switchPage=null;message="";FinishSceneVisit(false);Draw();}
        void RequestDelete(TreeDocument document)
        {
            if(document.IsDemo)throw new InvalidOperationException("The built-in demo cannot be deleted.");
            deleting=document;page="delete";message="";Draw();
        }
        void UpdateDeleteSummary()
        {
            if(deleteRevision==gallery.Generation.Revision)return;deleteRevision=gallery.Generation.Revision;
            int active=gallery.Generation.ActiveForTree(deleting.Id),waiting=gallery.Generation.WaitingForTree(deleting.Id);
            deleteSummary.text="Delete “"+deleting.Name+"”?\n\n"+deleting.People.Count+" people, saved locations and generated scenes will be removed from this device. This cannot be undone.\nShared photo files and the original import ZIP are kept."+
                (active+waiting>0?"\n\nAlso cancel "+active+" active and "+waiting+" waiting jobs. Requests already sent may still finish at Meta.":"");
        }
        void DeleteConfirmation()
        {
            heading.text="Delete tree?";deleteRevision=-1;deleteSummary=Text("",new Vector2(0,30),new Vector2(660,235),18);UpdateDeleteSummary();
            Button("Keep tree",new Vector2(-170,-140),new Vector2(310,40),CancelDelete);
            Button("Delete tree",new Vector2(170,-140),new Vector2(310,40),DeleteConfirmed);
        }
        void CancelDelete(){deleting=null;page="home";message="";Draw();}
        void DeleteConfirmed()
        {
            if(deleting==null||deleting.IsDemo)throw new InvalidOperationException("Choose a saved tree to delete.");
            if(!gallery.Generation.Ready)throw new InvalidOperationException("Wait for the scene queue to load.");
            bool current=deleting.Id==gallery.TreeId;
            if(current&&(!gallery.CacheIndexReady||gallery.PendingCacheRefreshes>0))throw new InvalidOperationException("Wait for this tree's cached scenes to finish loading.");
            if(current&&segmentation.Busy)throw new InvalidOperationException("Wait for photo extraction to finish before deleting this tree.");
            string id=deleting.Id;bool resume=gallery.Generation.Running&&!gallery.Generation.QueuePaused;
            bool stopping=gallery.Generation.ActiveForTree(id)+gallery.Generation.WaitingForTree(id)>0||gallery.Generation.CancellationNeedsSaving;
            Task stopped=stopping?gallery.Generation.CancelTreeJobs(id):Task.CompletedTask;
            Work(()=>{stopped.GetAwaiter().GetResult();return store.Delete(id);},result=>
            {
                library.RemoveAll(d=>d.Id==id);PlayerPrefs.DeleteKey(TreeAuthoring.PreferenceKey(id));PlayerPrefs.DeleteKey(SiblingVisibility.PreferenceKey(id));
                if(PlayerPrefs.GetString("AncestorWalk.LastTree","")==id)PlayerPrefs.DeleteKey("AncestorWalk.LastTree");
                if(current){gallery.OpenDemo();gallery.SetLocationModal(true);}
                else if(stopping&&resume)gallery.Generation.Resume(gallery.TreeId);
                PlayerPrefs.Save();deleting=null;page="home";offset=0;message=result;
            });progress="Deleting tree…";
        }
        void CutoutsChanged(){if(IsOpen&&page=="cutouts"&&!Busy){message=segmentation.Message;Draw();}}
        public void ExtractPhoto(PhotoRecord source,string mode)
        {
            if(ProductDemoAuthoring.ReadOnly(gallery.Document))throw new InvalidOperationException("Copy the demo before choosing a portrait.");
            if(segmentation.Busy){message="Another photo is extracting. You can keep exploring.";return;}
            cutoutSource=source;cutoutTree=gallery.TreeId;photoIndex=0;page="cutouts";segmentation.Extract(source,mode);Draw();
        }
        void UseCutout(PhotoRecord photo)
        {
            if(cutoutTree!=gallery.TreeId||cutoutSource==null)throw new InvalidOperationException("Open the source tree to attach this cutout.");
            var copy=gallery.Document.Copy();if(copy.Photos.All(p=>p.Id!=photo.Id))copy.Photos.Add(photo);
            if(photoPerson!=null){var p=copy.People.Single(p=>p.Id==photoPerson);if(!p.PhotoIds.Contains(photo.Id))p.PhotoIds.Add(photo.Id);p.PrimaryPhotoId=photo.Id;}
            Save(copy,()=>{page="photos";choosePhoto=false;photoIndex=SelectedPhotos().FindIndex(p=>p.Id==photo.Id);message="Cutout saved; original photo retained.";});
        }
        void ReleasePhoto(){if(photoEntry!=null){gallery.Photos.Release(photoEntry);photoEntry=null;}photoView=null;photoLoading=null;}
        void Draw()
        {
            ReleasePhoto();nativeInput=null;textInput=null;textSelection=null;switchSummary=null;deleteSummary=null;hits.Clear();foreach(var item in pageObjects){item.SetActive(false);Destroy(item);}pageObjects.Clear();
            heading=Text("Tree library",new Vector2(0,178),new Vector2(620,32),22);Button("×",new Vector2(342,178),new Vector2(40,32),Close);
            status=Text(message,new Vector2(0,-197),new Vector2(690,24),12);status.color=new Color(1,.8f,.45f);
            if(Busy){Text(progress,new Vector2(0,15),new Vector2(680,50),19);if(page=="import")Button("Cancel import",new Vector2(0,-80),new Vector2(200,36),()=>cancel?.Cancel());return;}
            switch(page)
            {
                case "home":Home();break;case "create":Create();break;case "people":People(false);break;case "pickperson":People(true);break;
                case "person":PersonForm();break;case "families":Families();break;case "family":FamilyForm();break;case "photos":Photos();break;case "cutouts":Cutouts();break;
                case "preview":Preview();break;case "gedchoose":GedChoices();break;case "text":TextEditor();break;case "warnings":Warnings();break;case "switch":SwitchConfirmation();break;case "delete":DeleteConfirmation();break;
            }
        }
        void Home()
        {
            heading.text="Your trees";
            Button("Edit current tree: "+gallery.Document.Name,new Vector2(0,137),new Vector2(680,32),()=>{page="people";offset=0;Draw();});
            var items=library.Skip(offset).Take(4).ToArray();for(int i=0;i<items.Length;i++){var item=items[i];Button(item.Name+" · "+item.People.Count+" people · "+item.Photos.Count+" photos",new Vector2(-61,90-i*38),new Vector2(558,32),()=>RequestTreeChange(item.Id,()=>Work(()=>item.IsDemo?item:store.Load(item.Id),doc=>{gallery.OpenDocument(doc);Close();})));if(!item.IsDemo)Button("Delete",new Vector2(285,90-i*38),new Vector2(110,32),()=>RequestDelete(item));}
            Pager(library.Count,4,-77);
            Button("New Tree",new Vector2(-225,-126),new Vector2(210,36),()=>{draft=new TreeDocument{Id=TreeStore.NewId(),Name="My family"};page="create";Draw();});
            Button("Import GED / ZIP",new Vector2(0,-126),new Vector2(210,36),()=>PickFile(false));
            Button("Fictional demo",new Vector2(225,-126),new Vector2(210,36),()=>RequestTreeChange(gallery.DemoDocument.Id,()=>{gallery.OpenDemo();Close();}));
            Button("Import inbox",new Vector2(-175,-170),new Vector2(320,30),Inbox);
            Button("Photos / unassigned",new Vector2(175,-170),new Vector2(320,30),()=>{photoPerson=null;choosePhoto=false;photoIndex=0;page="photos";Draw();});
            if(gallery.Document.Warnings.Count>0)Button("Import notes",new Vector2(-280,178),new Vector2(120,30),()=>{previousPage="home";page="warnings";offset=0;Draw();});
        }
        void Inbox()
        {
            string inbox=Path.Combine(PanoramaCache.StorageRoot,"ImportInbox");Work(()=>{Directory.CreateDirectory(inbox);return Directory.GetFiles(inbox).Where(f=>new[]{".ged",".zip",".gdz"}.Contains(Path.GetExtension(f).ToLowerInvariant())).ToArray();},paths=>{if(paths.Length==0){message="ImportInbox is empty. Copy a .ged, .zip or .gdz file there.";return;}inboxPaths=paths;page="gedchoose";importDraft=null;offset=0;});
        }
        string[] inboxPaths;
        void Create()
        {
            heading.text="Create a tree";Button("Name: "+draft.Name,new Vector2(0,90),new Vector2(680,44),()=>EditText("Tree name",draft.Name,value=>draft.Name=value));
            Text("Start with an empty tree. Click its ghost sphere to add your first person.",new Vector2(0,25),new Vector2(680,60),17);
            Button("Create tree",new Vector2(0,-100),new Vector2(250,40),()=>Save(draft,()=>{PlayerPrefs.SetInt(TreeAuthoring.PreferenceKey(draft.Id),1);PlayerPrefs.Save();gallery.ShowTree();Close();}));Back();
        }
        void People(bool picking)
        {
            heading.text=picking?"Choose a "+(pickRole=="parent"?"parent":"child"):gallery.Document.Name+" · People";
            var list=gallery.Document.People.Where(p=>!picking||!family.Parents.Contains(p.Id)&&!family.Children.Contains(p.Id)).OrderBy(p=>p.Name,StringComparer.OrdinalIgnoreCase).ToArray();
            for(int i=0;i<Math.Min(6,list.Length-offset);i++){var p=list[offset+i];string end=LifeTimeline.IsLiving(p)?DateTime.Now.Year+" · Living":p.DeathDate;Button(p.Name+" · "+p.BirthDate+" — "+end,new Vector2(0,116-i*36),new Vector2(680,31),()=>{if(picking){(pickRole=="parent"?family.Parents:family.Children).Add(p.Id);page="family";Draw();}else EditPerson(p.Id);});}
            if(list.Length==0)Text(picking?"No other people available. Add them in People first.":"This tree is empty. Add your first person.",new Vector2(0,40),new Vector2(680,70),18);
            Pager(list.Length,6,-112);
            if(!picking)Button(ProductDemoAuthoring.ReadOnly(gallery.Document)?"Copy demo to edit":"Add person",new Vector2(0,-157),new Vector2(280,36),()=>{if(ProductDemoAuthoring.ReadOnly(gallery.Document))CopyDemo();else EditPerson(null);});
            Back(picking?"family":"home");
        }
        void EditPerson(string id)
        {
            if(ProductDemoAuthoring.ReadOnly(gallery.Document)){message="The demo is read-only. Use Copy demo to edit.";return;}
            editingId=id;draft=gallery.Document.Copy();var p=id==null?new Person{Id="person-"+Guid.NewGuid().ToString("N"),Name=""}:draft.People.Single(x=>x.Id==id);if(id==null){draft.People.Add(p);editingId=p.Id;}page="person";Draw();
        }
        void CopyDemo()
        {
            if(!gallery.CacheIndexReady){message="Demo images are still loading. Try copying when they are ready.";return;}
            var copy=gallery.Document.Copy();string original=copy.Id;copy.Id=TreeStore.NewId();copy.IsDemo=false;copy.Name="My demo copy";
            string storage=PanoramaCache.StorageRoot,panoramas=PanoramaCache.DirectoryPath;
            var moves=copy.People.Select(person=>new ResidenceTimeline{treeId=copy.Id,personKey=PanoramaCache.PersonKey(person.Id),entries=gallery.Locations(person.Id).entries.Select(e=>new Residence{year=e.year,place=e.place}).ToList()}).ToArray();
            RequestTreeChange(copy.Id,()=>
            {
                RequireIdleGeneration();Work(()=>
                {
                    // Publish the saved document only after its local scenes and moves exist.
                    foreach(var person in copy.People)foreach(var record in PanoramaCache.YearRecordsAt(panoramas,original,person.Id))
                    {
                        string source=Path.Combine(panoramas,"people",original,record.personKey,"years",record.year.ToString(),record.file);
                        record.treeId=copy.Id;PanoramaCache.InstallBundledAt(panoramas,copy.Id,person.Id,record,()=>File.OpenRead(source));
                    }
                    string locations=Path.Combine(storage,"Locations",copy.Id);Directory.CreateDirectory(locations);
                    foreach(var timeline in moves)TreeStore.AtomicText(Path.Combine(locations,timeline.personKey+".json"),JsonUtility.ToJson(timeline));
                    store.Save(copy);return copy;
                },saved=>{gallery.OpenDocument(saved);gallery.SetLocationModal(true);library.Insert(0,saved);page="people";offset=0;});
            });
        }
        void PersonForm()
        {
            heading.text="Edit person";var p=draft.People.Single(x=>x.Id==editingId);
            Field("Name",p.Name,118,v=>p.Name=v);Field("Birth date",p.BirthDate,80,v=>p.BirthDate=v);Field("Birth location",p.BirthPlace,42,v=>p.BirthPlace=v);Field("Death date",p.DeathDate,4,v=>p.DeathDate=v);Field("Death location",p.DeathPlace,-34,v=>p.DeathPlace=v);
            Button((p.KnownLiving?"☑":"☐")+" Known living",new Vector2(-180,-79),new Vector2(310,32),()=>{p.KnownLiving=!p.KnownLiving;if(p.KnownLiving){p.DeathDate="";p.DeathPlace="";}Draw();});
            Button("Relationships",new Vector2(175,-79),new Vector2(310,32),()=>{if(gallery.Document.People.All(x=>x.Id!=editingId)){message="Save this person before adding relationships.";return;}page="families";offset=0;Draw();});
            Button("Photos",new Vector2(175,-121),new Vector2(310,32),()=>{if(gallery.Document.People.All(x=>x.Id!=editingId)){message="Save this person before adding photos.";return;}photoPerson=editingId;photoIndex=0;choosePhoto=false;page="photos";Draw();});
            Button("Sex: "+RelationshipColors.Label(p.Sex),new Vector2(-180,-121),new Vector2(310,32),()=>{p.Sex=RelationshipColors.Next(p.Sex);Draw();});
            Button("Save person",new Vector2(0,-166),new Vector2(290,36),()=>{if(string.IsNullOrWhiteSpace(p.Name)){message="Enter a name.";return;}if(p.KnownLiving&&!string.IsNullOrWhiteSpace(p.DeathDate)){message="Remove the death date or uncheck living.";return;}if(authoringPerson==editingId)SaveAuthoringPerson();else Save(draft,()=>{page="people";offset=0;});});if(authoringPerson==editingId)Button("‹ Back",new Vector2(-295,178),new Vector2(100,30),Close);else Back("people");
        }
        void Field(string label,string value,float y,Action<string> write)=>Button(label+": "+value,new Vector2(0,y),new Vector2(680,32),()=>EditText(label,value,write));
        void Families()
        {
            heading.text="Family groups";var list=gallery.Document.Families.Where(f=>f.Parents.Contains(editingId)||f.Children.Contains(editingId)).ToArray();
            for(int i=0;i<Math.Min(5,list.Length-offset);i++){var f=list[offset+i];Button(string.Join(" + ",f.Parents.Select(PersonName))+" → "+f.Children.Count+" children",new Vector2(0,110-i*40),new Vector2(680,34),()=>{draft=gallery.Document.Copy();family=draft.Families.Single(x=>x.Id==f.Id);pickRole="parent";page="family";offset=0;Draw();});}
            Pager(list.Length,5,-110);Button("New family group",new Vector2(0,-160),new Vector2(280,36),()=>{draft=gallery.Document.Copy();family=new FamilyRecord{Id="family-"+Guid.NewGuid().ToString("N")};family.Parents.Add(editingId);draft.Families.Add(family);pickRole="parent";page="family";offset=0;Draw();});Back("person");
        }
        string PersonName(string id)=>gallery.Tree.People.TryGetValue(id,out var p)?p.Name:"Missing person: "+id;
        void FamilyForm()
        {
            heading.text="Edit family group";Button("Parents",new Vector2(-175,125),new Vector2(320,32),()=>{pickRole="parent";offset=0;Draw();});Button("Children",new Vector2(175,125),new Vector2(320,32),()=>{pickRole="child";offset=0;Draw();});
            var list=pickRole=="parent"?family.Parents:family.Children;for(int i=0;i<Math.Min(4,list.Count-offset);i++){string id=list[offset+i];Button("Remove "+PersonName(id),new Vector2(0,80-i*38),new Vector2(680,32),()=>{list.Remove(id);offset=0;Draw();});}
            Pager(list.Count,4,-78);Button("Add "+pickRole,new Vector2(0,-117),new Vector2(280,32),()=>{page="pickperson";offset=0;Draw();});
            Button("Save family",new Vector2(0,-164),new Vector2(280,36),()=>Save(draft,()=>{page="families";offset=0;}));Back("families");
        }
        static List<PhotoRecord> ScenePhotos(TreeDocument document,string personId)
        {
            var person=document.People.FirstOrDefault(p=>p.Id==personId);
            var own=new HashSet<string>(person?.PhotoIds??new List<string>());
            return document.Photos.OrderBy(photo=>own.Contains(photo.Id)?photo.Id==person.PrimaryPhotoId?0:1:2).ToList();
        }
        List<PhotoRecord> SelectedPhotos()=>scenePhotoPicked!=null?ScenePhotos(gallery.Document,scenePhotoPerson):photoPerson==null||choosePhoto?gallery.Document.Photos:gallery.Document.Photos.Where(p=>gallery.Tree.People[photoPerson].PhotoIds.Contains(p.Id)).ToList();
        void Photos()
        {
            bool picking=scenePhotoPicked!=null;
            heading.text=picking?"Choose photo to include in scene":photoPerson==null?"Tree photos":PersonName(photoPerson)+" · Photos";var list=SelectedPhotos();photoIndex=Mathf.Clamp(photoIndex,0,Math.Max(0,list.Count-1));
            if(list.Count>0)
            {
                var photo=list[photoIndex];photoView=Rect("Photo preview",card,new Vector2(-135,10),new Vector2(250,250)).gameObject.AddComponent<RawImage>();pageObjects.Add(photoView.gameObject);photoView.raycastTarget=false;photoEntry=gallery.Photos.Acquire(photo,1024);
                photoLoading=Text("Loading photo…",new Vector2(-135,10),new Vector2(250,35),15);
                Text(photo.Title+"\n"+photo.Date+"\n\n"+(photo.Description??""),new Vector2(180,68),new Vector2(280,120),15);
                if(picking)Button("Use this photo",new Vector2(180,-22),new Vector2(280,38),()=>CompleteScenePhoto(photo));
                else if(photoPerson!=null&&!ProductDemoAuthoring.ReadOnly(gallery.Document))
                    Button(choosePhoto?"Attach to person":"Use as portrait",new Vector2(180,-22),new Vector2(280,32),()=>{var copy=gallery.Document.Copy();var p=copy.People.Single(x=>x.Id==photoPerson);if(!p.PhotoIds.Contains(photo.Id))p.PhotoIds.Add(photo.Id);p.PrimaryPhotoId=photo.Id;Save(copy,()=>{page="photos";choosePhoto=false;});});
                if(!picking&&!ProductDemoAuthoring.ReadOnly(gallery.Document))
                {
                    Text("Extract with Meta SAM 3.1",new Vector2(180,-56),new Vector2(280,23),13);
                    foreach(var choice in new[]{("Head","head",85f),("Face","face",180f),("Person","person",275f)}){var mode=choice.Item2;Button(choice.Item1,new Vector2(choice.Item3,-86),new Vector2(88,30),()=>ExtractPhoto(photo,mode));}
                    Text("Sends this photo to Meta. Results cached.",new Vector2(180,-113),new Vector2(300,20),11);
                }
            }
            else Text("No photos attached yet.",new Vector2(0,20),new Vector2(650,70),19);
            Button("‹",new Vector2(-290,-139),new Vector2(60,32),()=>{photoIndex=Math.Max(0,photoIndex-1);Draw();});Text(list.Count==0?"0 photos":(photoIndex+1)+" / "+list.Count,new Vector2(0,-139),new Vector2(260,30),15);Button("›",new Vector2(290,-139),new Vector2(60,32),()=>{photoIndex=Math.Min(list.Count-1,photoIndex+1);Draw();});
            if(photoPerson!=null&&!ProductDemoAuthoring.ReadOnly(gallery.Document)){Button("Add photo file",new Vector2(-175,-177),new Vector2(320,30),()=>PickFile(true));Button("Choose existing photo",new Vector2(175,-177),new Vector2(320,30),()=>{choosePhoto=true;photoIndex=0;Draw();});}
            if(picking){Button("Choose photo file",new Vector2(140,-177),new Vector2(280,32),()=>PickFile(true));Button("Cancel",new Vector2(-170,-177),new Vector2(250,32),Close);}
            else Back("home");
        }
        void Cutouts()
        {
            heading.text="Meta SAM 3.1 · Photo cutouts";
            var result=segmentation.Result;var list=result!=null&&result.sourcePhotoId==cutoutSource?.Id?result.photos:new List<PhotoRecord>();photoIndex=Mathf.Clamp(photoIndex,0,Math.Max(0,list.Count-1));
            if(segmentation.Busy)Text(segmentation.Message+"\n\nYou can close this window and keep exploring.",new Vector2(0,10),new Vector2(650,100),18);
            else if(list.Count==0)Text(segmentation.Message,new Vector2(0,10),new Vector2(650,100),18);
            else
            {
                var photo=list[photoIndex];var backdrop=Rect("Cutout backdrop",card,new Vector2(-135,10),new Vector2(250,250));pageObjects.Add(backdrop.gameObject);Background(backdrop,new Color(.23f,.29f,.34f));
                photoView=Rect("Transparent cutout preview",backdrop,Vector2.zero,new Vector2(250,250)).gameObject.AddComponent<RawImage>();photoView.raycastTarget=false;photoEntry=gallery.Photos.Acquire(photo,1024);photoLoading=Text("Loading cutout…",new Vector2(-135,10),new Vector2(250,35),15);
                Text("Choose the correct person.\n\nTransparent "+result.mode+" cutout\nOriginal photo preserved.",new Vector2(180,55),new Vector2(280,130),16);
                Button(photoPerson==null?"Save cutout":"Use as portrait",new Vector2(180,-55),new Vector2(280,38),()=>UseCutout(photo));
                Button("‹",new Vector2(-290,-139),new Vector2(60,32),()=>{photoIndex=Math.Max(0,photoIndex-1);Draw();});Text((photoIndex+1)+" / "+list.Count+" cutouts",new Vector2(0,-139),new Vector2(260,30),15);Button("›",new Vector2(290,-139),new Vector2(60,32),()=>{photoIndex=Math.Min(list.Count-1,photoIndex+1);Draw();});
            }
            Button("Keep exploring",new Vector2(0,-177),new Vector2(300,30),Close);Back("photos");
        }
        void Preview()
        {
            heading.text="Review import";var doc=importDraft.Document;
            Text(doc.Name+"\n"+doc.People.Count+" people · "+doc.Families.Count+" families · "+doc.Photos.Count+" unique photos\n"+doc.People.Count(p=>p.PhotoIds.Count>0)+" people with attached photos",new Vector2(0,75),new Vector2(680,100),19);
            string warnings=doc.Warnings.Count==0?"All referenced supported photos matched.":string.Join("\n",doc.Warnings.Take(4))+ (doc.Warnings.Count>4?"\n…and "+(doc.Warnings.Count-4)+" more (saved with the tree).":"");
            if(library.Any(d=>d.SourceFingerprint==doc.SourceFingerprint))warnings="Previously imported. This creates a separate tree.\n"+warnings;
            Text(warnings,new Vector2(0,-28),new Vector2(680,100),14);
            if(doc.Warnings.Count>0)Button("All import notes",new Vector2(0,-78),new Vector2(220,26),()=>{previousPage="preview";page="warnings";offset=0;Draw();});
            Button("Rename",new Vector2(-175,-116),new Vector2(300,32),()=>EditText("Imported tree name",doc.Name,v=>doc.Name=v));
            Button("Import tree",new Vector2(175,-116),new Vector2(300,36),()=>RequestTreeChange(importDraft.Document.Id,()=>Work(()=>TreeArchiveImport.Commit(store,importDraft),saved=>{importDraft=null;library.Insert(0,saved);gallery.OpenDocument(saved);Close();})));
            Button("Cancel",new Vector2(0,-166),new Vector2(240,32),()=>{var abandoned=importDraft;importDraft=null;Work(()=>{TreeArchiveImport.Cleanup(store,abandoned);return true;},_=>page="home");});
        }
        void Warnings()
        {
            heading.text="Import notes";var notes=previousPage=="preview"?importDraft.Document.Warnings:gallery.Document.Warnings;
            for(int i=0;i<Math.Min(3,notes.Count-offset);i++)Text(notes[offset+i],new Vector2(0,105-i*77),new Vector2(680,72),14);
            Pager(notes.Count,3,-151);Back(previousPage);
        }
        void GedChoices()
        {
            heading.text="Choose a GEDCOM file";var paths=importDraft?.GedcomChoices?.ToArray()??inboxPaths??Array.Empty<string>();
            for(int i=0;i<Math.Min(6,paths.Length-offset);i++){string path=paths[offset+i];Button(path,new Vector2(0,115-i*38),new Vector2(680,32),()=>{if(importDraft!=null)ImportPath(importDraft.SourcePath,path);else ImportPath(path);});}Pager(paths.Length,6,-143);Back();
        }
        void TextEditor()
        {
            heading.text=textTitle;textInput=Text(textValue,new Vector2(0,115),new Vector2(680,62),19);nativeInput=NativeTextInput.Bind(textInput,v=>textValue=v,200);pageObjects.Remove(textInput.gameObject);pageObjects.Add(nativeInput.gameObject);RefreshText();NativeTextInput.Focus(nativeInput,true);
            string[] rows={"QWERTYUIOP","ASDFGHJKL","ZXCVBNM,.-","0123456789/"};
            for(int row=0;row<rows.Length;row++)for(int col=0;col<rows[row].Length;col++){string key=(lowerKeys?char.ToLowerInvariant(rows[row][col]):rows[row][col]).ToString();Button(key,new Vector2((col-(rows[row].Length-1)*.5f)*51,50-row*36),new Vector2(46,31),()=>AppendText(key));}
            Button("Space",new Vector2(-225,-103),new Vector2(200,32),()=>AppendText(" "));Button("Backspace",new Vector2(0,-103),new Vector2(200,32),DeleteText);Button("Clear",new Vector2(172,-103),new Vector2(94,32),()=>{textValue="";selectAll=false;RefreshText();});Button("a / A",new Vector2(279,-103),new Vector2(94,32),()=>{lowerKeys=!lowerKeys;Draw();});
            hits.Add(new Hit{Rect=(RectTransform)nativeInput.transform,Action=()=>NativeTextInput.Focus(nativeInput)});
            Button("Done",new Vector2(170,-159),new Vector2(300,36),EndText);Button("Cancel",new Vector2(-170,-159),new Vector2(300,36),()=>{page=previousPage;Draw();});
        }
        void Pager(int total,int count,float y)
        {Button("‹",new Vector2(-295,y),new Vector2(70,28),()=>{offset=Math.Max(0,offset-count);Draw();});Text(total==0?"0":(offset+1)+"–"+Math.Min(total,offset+count)+" / "+total,new Vector2(0,y),new Vector2(300,28),14);Button("›",new Vector2(295,y),new Vector2(70,28),()=>{if(offset+count<total)offset+=count;Draw();});}
        void Back(string target="home")=>Button("‹ Back",new Vector2(-295,178),new Vector2(100,30),()=>{page=target;offset=0;if(target=="person"&&gallery.Document.People.Any(x=>x.Id==editingId))draft=gallery.Document.Copy();Draw();});
        static bool Contains(RectTransform rect,Ray ray,bool vr)
        {if(!UIFade.Interactive(rect))return false;if(!vr)return RectTransformUtility.RectangleContainsScreenPoint(rect,Mouse.current?.position.ReadValue()??Vector2.zero);var plane=new Plane(rect.forward,rect.position);return plane.Raycast(ray,out float d)&&rect.rect.Contains(rect.InverseTransformPoint(ray.GetPoint(d)));}
        public bool HandleInput(Ray ray,bool vr,bool pressed,bool back)
        {
            if(!IsOpen)
            {
                bool shortcut=!vr&&(Keyboard.current?.tKey.wasPressedThisFrame??false);if((shortcut||pressed&&launcher.gameObject.activeInHierarchy&&Contains(launcher,ray,vr))&&!(gallery.LocationEditor?.IsOpen??false)&&!(gallery.Tutorial?.IsOpen??false)){Open();return true;}return false;
            }
            if(back||(!vr&&(Keyboard.current?.escapeKey.wasPressedThisFrame??false))){if(page=="switch"&&!Busy)CancelSwitch();else if(page=="delete"&&!Busy)CancelDelete();else if(page=="text"){page=previousPage;Draw();}else Close();return true;}
            if(page=="text"&&!Busy){if(Keyboard.current?.enterKey.wasPressedThisFrame??false){EndText();return true;}}
            if(pressed)for(int i=hits.Count-1;i>=0;i--)if(hits[i].Rect!=null&&UIFade.Interactive(hits[i].Rect)&&Contains(hits[i].Rect,ray,vr)){try{if(vr&&nativeInput!=null&&hits[i].Rect==nativeInput.transform)NativeTextInput.Point(nativeInput,ray);else hits[i].Action();}catch(Exception e){message=e.Message;Draw();}break;}return true;
        }
        static RectTransform Rect(string name,Transform parent,Vector2 at,Vector2 size){var r=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();r.SetParent(parent,false);r.anchorMin=r.anchorMax=new Vector2(.5f,.5f);r.anchoredPosition=at;r.sizeDelta=size;return r;}
        static void Background(RectTransform r,Color c){var image=r.gameObject.AddComponent<Image>();image.color=c;image.raycastTarget=false;}
        Text Label(Transform parent,string value,Vector2 at,Vector2 size,int font){var t=Rect(value,parent,at,size).gameObject.AddComponent<Text>();t.font=gallery.font;t.text=value;t.fontSize=font;t.color=Color.white;t.alignment=TextAnchor.MiddleCenter;t.supportRichText=false;t.raycastTarget=false;t.resizeTextForBestFit=true;t.resizeTextMinSize=11;t.resizeTextMaxSize=font;return t;}
        Text Text(string text,Vector2 at,Vector2 size,int font){var label=Label(card,text,at,size,font);pageObjects.Add(label.gameObject);return label;}
        void Button(string title,Vector2 at,Vector2 size,Action action){var r=Rect(title,card,at,size);pageObjects.Add(r.gameObject);Background(r,new Color(.07f,.27f,.28f,.95f));Label(r,title,Vector2.zero,size,16);hits.Add(new Hit{Rect=r,Action=action});}
        void OnDestroy(){if(segmentation!=null)segmentation.Changed-=CutoutsChanged;Unsubscribe();cancel?.Cancel();ReleasePhoto();if(importDraft!=null){var abandoned=importDraft;_ = Task.Run(()=>TreeArchiveImport.Cleanup(store,abandoned));}}
    }
}
