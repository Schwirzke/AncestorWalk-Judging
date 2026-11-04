using System.Collections.Generic;
using System.Linq;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;

namespace AncestorWalk
{
    public sealed partial class AncestorGallery : MonoBehaviour
    {
        public TextAsset familyData;
        public Material ink, stone, gold, teal, paper;
        public Font font;
        Material depthText;
        Font worldFont, uiFont;
        public Font WorldFont => worldFont!=null?worldFont:(worldFont=Resources.Load<Font>("Fonts/Lato-Regular"));
        public Material WorldTextMaterial
        {
            get
            {
                if(depthText==null)depthText=new Material(Resources.Load<Shader>("Shaders/DepthText"));
                depthText.mainTexture=WorldFont.material.mainTexture;return depthText;
            }
        }
        void FontRebuilt(Font rebuilt){if(rebuilt==worldFont&&depthText!=null)depthText.mainTexture=WorldFont.material.mainTexture;}
        public FamilyTree Tree { get; private set; }
        public TreeDocument DemoDocument {get;private set;}
        public TreeDocument LegacyDemoDocument {get;private set;}
        public string SelectedId { get; private set; }
        public string Status { get; private set; }
        Transform content;
        Transform treeRoot;
        readonly List<Transform> treeNodes = new();
        public TreeLayoutResult Layout { get; private set; }
        public TreeNavigationOverlay TreeControls { get; private set; }
        public Vector2 TreePan { get; private set; }
        public Vector2 TreeRotation {get;private set;}
        public float TreeScale {get;private set;}=1;
        public void SetTreeScale(float value){value=TreeScaling.Clamp(value);if(value==TreeScale)return;TreeScale=value;ApplyTreePan();}
        public Quaternion TreeOrientation=>treeRoot!=null?treeRoot.rotation:TreeRotationPose.Orientation(TreeRotation);
        public Bounds WorldTreeBounds {get;private set;}
        bool treeCollidersDirty;
        GalleryNavigation treeNavigation;
        public bool IsTimelineDragging{get{if(treeNavigation==null)treeNavigation=FindAnyObjectByType<GalleryNavigation>();return (Overlay?.IsDraggingYear??false)||(treeNavigation!=null&&treeNavigation.IsDraggingYear);}}
        public bool IsTree => SelectedId == null && treeRoot != null && !IsBuildingTree;
        public bool IsBuildingTree {get;private set;}
        Coroutine treeBuild;
        public Vector3 TreeNodePosition(string id)=>treeRoot.TransformPoint(Layout.Positions[id]);
        public void PanTree(Vector2 delta) { if(IsTree){TreePan=Layout.ClampPan(TreePan+delta);ApplyTreePan();} }
        public void ResetTreePan() { TreePan=Vector2.zero;ApplyTreePan(); }
        public void SetTreeRotation(Vector2 angles)
        {angles=TreeRotationPose.Normalize(angles);if(angles==TreeRotation)return;TreeRotation=angles;ApplyTreePan();}
        public void RotateTree(Vector2 degrees){if(IsTree&&Tree.People.Count>0)SetTreeRotation(TreeRotation+degrees);}
        public void ResetTreeRotation()=>SetTreeRotation(Vector2.zero);
        public void SyncTreeColliders(){if(!treeCollidersDirty)return;Physics.SyncTransforms();treeCollidersDirty=false;}
        void ApplyTreePan()
        {
            if(treeRoot==null||Layout==null)return;
            var orientation=TreeRotationPose.Orientation(TreeRotation);
            treeRoot.localScale=Vector3.one*TreeScale;
            treeRoot.SetLocalPositionAndRotation(TreeRotationPose.Position(Layout.Bounds.center,TreePan,orientation,TreeScale),orientation);
            WorldTreeBounds=TreeRotationPose.WorldBounds(Layout.Bounds,treeRoot.localToWorldMatrix);treeCollidersDirty=true;
            if(treeNavigation==null)treeNavigation=FindAnyObjectByType<GalleryNavigation>();
            treeNavigation?.RefreshTreeClip();
        }
        public string TreeId { get; private set; }
        public SceneGeneration Generation { get; private set; }
        public TreeDocument Document {get;private set;}
        public TreeLibraryOverlay Library {get;private set;}
        public AISetupPanel AISetup {get;private set;}
        public SceneBatchOverlay Batch {get;private set;}
        PhotoTextureCache photos;
        public PhotoTextureCache Photos=>photos!=null?photos:(photos=gameObject.AddComponent<PhotoTextureCache>());
        public PhotoRecord Portrait(string id)
        {if(Document==null||!Tree.People.TryGetValue(id,out var p))return null;string photo=string.IsNullOrEmpty(p.PrimaryPhotoId)?p.PhotoIds.FirstOrDefault():p.PrimaryPhotoId;return Document.Photos.FirstOrDefault(x=>x.Id==photo);}
        int treeEpoch;
        readonly Dictionary<string,int> selectedYears=new();
        readonly Dictionary<string,int> completedYears=new();
        readonly Dictionary<string,int[]> cachedYears=new();
        readonly Dictionary<string,TimelineMarker[]> timelineMarkers=new();
        public TimelineMarker[] Markers(string id)=>timelineMarkers.TryGetValue(id,out var markers)?markers:System.Array.Empty<TimelineMarker>();
        readonly Dictionary<string,Dictionary<int,string>> sceneRevisions=new();
        CubeThumbnailCache thumbnails;Material panelFade;
        ScenePanoramaCache fullPanoramas;
        public ScenePanoramaCache FullPanoramas=>fullPanoramas!=null?fullPanoramas:(fullPanoramas=gameObject.AddComponent<ScenePanoramaCache>());
        readonly Dictionary<string,PanoramaRecord[]> validatedRecords=new();
        readonly Dictionary<string,int> refreshVersions=new();
        readonly Dictionary<string,int> activeRefreshes=new();
        public int PendingCacheRefreshes {get;private set;}
        public bool CacheIndexReady {get;private set;}
        public CubeThumbnailCache Thumbnails=>thumbnails!=null?thumbnails:(thumbnails=gameObject.AddComponent<CubeThumbnailCache>());
        public Material PanelFadeMaterial=>panelFade!=null?panelFade:(panelFade=new Material(Resources.Load<Shader>("Shaders/CubeControlFade")));
        public string SceneRevision(string id,int year)
        {
            if(sceneRevisions.TryGetValue(id,out var years)&&years.TryGetValue(year,out var revision))return revision;
            var old=validatedRecords.TryGetValue(id,out var records)?records.FirstOrDefault(r=>r.year==year):null;
            return old==null?"missing":old.sha256+":"+old.horizonLift.ToString(System.Globalization.CultureInfo.InvariantCulture)+":"+old.place;
        }
        HistoricalEnvironment activeEnvironment;
        public bool IsAllTime => activeEnvironment!=null && activeEnvironment.IsAllTime;
        public bool IsSceneLoading=>activeEnvironment!=null&&activeEnvironment.IsLoading;
        public void ScenePlaceWeights(Dictionary<string,float> weights)
        {
            weights.Clear();
            if(IsAllTime && SelectedId!=null){weights[Place(SelectedId)]=1;return;}
            if(activeEnvironment!=null)activeEnvironment.PlaceWeights(weights);
        }
        public SceneOverlay Overlay {get;private set;}
        readonly Dictionary<string,ResidenceTimeline> locations=new();
        public LocationEditor LocationEditor {get;private set;}
        public TutorialOverlay Tutorial {get;private set;}
        public HudOverlay Hud {get;private set;}
        public PassthroughMode Passthrough {get;private set;}
        public bool HudVisible=>Hud==null||Hud.Visible;
        public ResidenceTimeline Locations(string id)=>locations[id];
        public string Place(string id)=>locations[id].At(Tree.People[id],Year(id));
        public int GenerationYear(string id)=>Year(id);
        public string PlaceAt(string id,int year)=>locations[id].At(Tree.People[id],year);
        // Validate each panorama at its own year, then blend across move dates too.
        public int[] AvailableYears(string id)=>CachedYears(id);
        public bool ReferenceIndexUpdating(string id)=>activeRefreshes.ContainsKey(id);
        public int[] ReferenceYears(string id,string place)
        {
            if(!validatedRecords.TryGetValue(id,out var records))return System.Array.Empty<int>();
            var person=Tree.People[id];var moves=Locations(id);
            return records.Where(r=>ResidenceTimeline.Same(string.IsNullOrWhiteSpace(r.place)?person.BirthPlace:r.place,place)&&ResidenceTimeline.Same(string.IsNullOrWhiteSpace(r.place)?person.BirthPlace:r.place,moves.At(person,r.year))).Select(r=>r.year).ToArray();
        }
        // Missing residence images use the nearest cached year pair and normal blending.
        // Pending move markers remain distinct from actual generated-year markers.
        readonly Dictionary<string,(int Year,int[] Years)> heldSceneYears=new();
        public int[] SceneYears(string id)
        {
            var years=AvailableYears(id);var person=Tree.People[id];
            if(!LifeEndpointScenes.TrySceneRange(person,out _,out _))return System.Array.Empty<int>();
            // A location correction changes compatibility, not the old image's availability.
            // Keep its exact selected-year picture until a validated replacement is ready.
            int selected=Year(id);
            if(heldSceneYears.TryGetValue(id,out var held)&&held.Year==selected)
            {
                if(System.Array.IndexOf(years,selected)>=0)heldSceneYears.Remove(id);
                else return held.Years;
            }
            if(System.Array.IndexOf(years,selected)<0&&validatedRecords.TryGetValue(id,out var records)&&records.Any(r=>r.year==selected))
                return years.Append(selected).OrderBy(y=>y).ToArray();
            return years;
        }
        public void SaveLocation(string id,int year,string place,bool remove=false)
        {
            if(remove)heldSceneYears.Remove(id);
            if(!remove)
            {
                LifeTimeline.Pair(SceneYears(id),year,out int oldA,out int oldB,out _);
                if(oldA>0)heldSceneYears[id]=(year,oldA==oldB?new[]{oldA}:new[]{oldA,oldB});
            }
            locations[id].Save(Tree.People[id],year,place,remove);if(validatedRecords.TryGetValue(id,out var records)){ApplyRecords(id,records);RefreshPersonUI(id);}else RefreshPersonAsync(id);StartCoroutine(QueueEndpointsWhenReady(treeEpoch,id));}
        readonly Dictionary<Renderer,bool> modalRenderers=new();
        public void SetLocationModal(bool visible)
        {
            if(visible&&content!=null){foreach(var renderer in content.GetComponentsInChildren<Renderer>()){if(!modalRenderers.ContainsKey(renderer))modalRenderers[renderer]=renderer.enabled;renderer.enabled=false;}}
            else {foreach(var entry in modalRenderers)if(entry.Key!=null)entry.Key.enabled=entry.Value;modalRenderers.Clear();}
        }
        public void EditLocation(string id){if(SelectedId==id && LifeEndpointScenes.TrySceneRange(Tree.People[id],out _,out _))LocationEditor.Open(this,Tree.People[id],Year(id));}

        public int[] CachedYears(string id)=>cachedYears.TryGetValue(id,out var years)?years:System.Array.Empty<int>();
        public int Year(string id)
        {
            if(selectedYears.TryGetValue(id,out int year))return year;
            if(!LifeEndpointScenes.TrySceneRange(Tree.People[id],out int birth,out int end))return 0;
            year=Mathf.Clamp(PlayerPrefs.GetInt("year."+TreeId+"."+id,birth),birth,end);selectedYears[id]=year;return year;
        }
        public void SetYear(string id,int year)
        {
            if(!LifeEndpointScenes.TrySceneRange(Tree.People[id],out int birth,out int end))return;
            selectedYears[id]=Mathf.Clamp(year,birth,end);PlayerPrefs.SetInt("year."+TreeId+"."+id,selectedYears[id]);
            RefreshPersonUI(id);
        }
        public bool HasScene(string id)=>AvailableYears(id).Length>0;
        public string SceneActionLabel(string id)
        {
            if(!Tree.People.TryGetValue(id,out var person)||!LifeEndpointScenes.TrySceneRange(person,out _,out _))return "YEAR NEEDED";
            int year=Year(id);string place=Place(id);
            if(string.IsNullOrWhiteSpace(place))return "LOCATION NEEDED";
            bool restart=Generation.TargetLabel(TreeId,id,year,place)!=null||System.Array.IndexOf(AvailableYears(id),year)>=0;
            return (restart?"REGENERATE ":"GENERATE ")+year;
        }
        public bool CanGenerateScene(string id)
        {
            if(!(IsTree||SelectedId==id)||!CacheIndexReady||!Generation.Ready||!Tree.People.TryGetValue(id,out var person)||!LifeEndpointScenes.TrySceneRange(person,out _,out _)||string.IsNullOrWhiteSpace(Place(id)))return false;
            return true;
        }
        public void GenerateScene(string id)
        {
            if(CanGenerateScene(id))Generation.Generate(TreeId,Tree.People[id],Document.IsDemo,GenerationYear(id),Place(id));
        }
        public List<(Person Person,int Year,string Place)> RegenerationTargets()
        {
            if(!CacheIndexReady)throw new System.InvalidOperationException("Checking cached scenes. Please wait…");
            var result=new List<(Person,int,string)>();
            foreach(var person in Tree.People.Values.OrderBy(p=>p.Id,System.StringComparer.Ordinal))
            {
                if(!LifeEndpointScenes.TrySceneRange(person,out int birth,out int end))continue;
                foreach(int year in AvailableYears(person.Id))
                {
                    string place=PlaceAt(person.Id,year);
                    if(year>=birth&&year<=end&&!string.IsNullOrWhiteSpace(place)&&!Generation.Scheduled(TreeId,person.Id,year,place))result.Add((person,year,place));
                }
            }
            return result;
        }
        void RefreshCards()
        {
            foreach(var person in Tree.People.Values)RefreshPersonCache(person.Id);
            RefreshPersonUI(null);
        }
        public void RefreshPerson(string id){RefreshPersonCache(id);RefreshPersonUI(id);}
        void SceneCompleted(string tree,string id,int year)
        {
            if(TreeId==tree&&Tree.People.ContainsKey(id))completedYears[id]=year;
        }
        public void RefreshPersonAsync(string id){if(Tree.People.ContainsKey(id))StartCoroutine(RefreshAsync(id));}
        IEnumerator RefreshAsync(string id)
        {
            refreshVersions.TryGetValue(id,out int version);refreshVersions[id]=++version;
            string directory=PanoramaCache.DirectoryPath,tree=TreeId;int epoch=treeEpoch;activeRefreshes[id]=version;PendingCacheRefreshes++;
            var task=Task.Run(()=>PanoramaCache.YearRecordsAt(directory,tree,id));
            try
            {
                while(!task.IsCompleted)yield return null;
                if(task.IsFaulted||task.IsCanceled){_ = task.Exception;yield break;}
                if(treeEpoch!=epoch||TreeId!=tree||!Tree.People.ContainsKey(id)||!refreshVersions.TryGetValue(id,out int current)||current!=version)yield break;
                validatedRecords[id]=task.Result;ApplyRecords(id,task.Result);
                bool completed=completedYears.Remove(id,out int year)&&System.Array.IndexOf(CachedYears(id),year)>=0;
                if(completed)
                {
                    SetYear(id,year);
                    foreach(var node in treeNodes)if(node!=null){var preview=node.GetComponent<CubePanoramaPreview>();if(preview!=null&&preview.PersonId==id){preview.PulseCompletion();break;}}
                }
                else RefreshPersonUI(id);
            }
            finally{PendingCacheRefreshes--;if(treeEpoch==epoch&&activeRefreshes.TryGetValue(id,out int active)&&active==version)activeRefreshes.Remove(id);}
        }
        IEnumerator RefreshSubsetAsync(string[] ids)
        {int epoch=treeEpoch;foreach(string id in ids){if(epoch!=treeEpoch)yield break;yield return RefreshAsync(id);yield return null;}if(epoch==treeEpoch){CacheIndexReady=true;yield return QueueEndpointsWhenReady(epoch);}}
        IEnumerator RefreshAllAsync()
        {
            // Capture Unity-owned values before starting the pure IO migration worker.
            string directory=PanoramaCache.DirectoryPath,tree=TreeId;bool demo=TreeId==PanoramaCache.DemoTreeId;int epoch=treeEpoch;
            var people=Tree.People.Values.ToArray();
            bool bundledFailed=false;
            if(Document.SourceFingerprint==PanoramaCache.ProductDemoTreeId)
                foreach(var omission in ProductDemoData.Current.omittedScenes)
                    if(Tree.People.ContainsKey(omission.personId))bundledOmittedYears[omission.personId]=ProductDemoData.Current.omittedScenes.Where(o=>o.personId==omission.personId).Select(o=>o.year).ToArray();
            if(tree==PanoramaCache.ProductDemoTreeId)
            {
                string streaming=Application.streamingAssetsPath;
                var photoCache=Photos;photoCache.BeginBundledMedia();Task<BundledDemoScenes.Result> seed;
                try{seed=Task.Run(()=>BundledDemoScenes.SeedAt(streaming,directory,tree,people));while(!seed.IsCompleted)yield return null;}
                finally{if(photoCache!=null)photoCache.EndBundledMedia();}
                if(seed.IsFaulted){_=seed.Exception;bundledFailed=true;Status="Bundled demo images could not be prepared. Existing images were kept.";}
                if(epoch!=treeEpoch)yield break;
                if(!seed.IsFaulted&&!seed.IsCanceled)foreach(var omission in seed.Result.OmittedScenes)
                    bundledOmittedYears[omission.personId]=seed.Result.OmittedScenes.Where(o=>o.personId==omission.personId).Select(o=>o.year).ToArray();
            }
            var migration=Task.Run(()=>PanoramaCache.MigrateAt(directory,tree,people,demo));
            while(!migration.IsCompleted)yield return null;
            if(migration.IsFaulted)_=migration.Exception;
            foreach(var person in people){if(epoch!=treeEpoch)yield break;yield return RefreshAsync(person.Id);yield return null;}
            if(epoch==treeEpoch){CacheIndexReady=true;if(!bundledFailed)yield return QueueEndpointsWhenReady(epoch);}
        }
        IEnumerator QueueEndpointsWhenReady(int epoch,string id=null)
        {
#if UNITY_EDITOR
            // Disposable fake-helper checks must not start the live gallery's paid jobs.
            if(UnityEditor.SessionState.GetBool("AncestorWalk.EndpointChecks",false))yield break;
#endif
            while(epoch==treeEpoch&&(!CacheIndexReady||!Generation.Ready))yield return null;
            if(epoch!=treeEpoch)yield break;
            if(id!=null&&!Tree.People.ContainsKey(id))yield break;
            Generation.ScheduleEndpoints(TreeId,id==null?Tree.People.Values:new[]{Tree.People[id]},Document.IsDemo,p=>Locations(p.Id),p=>AutomaticCachedYears(p.Id));
        }
        readonly Dictionary<string,int[]> bundledOmittedYears=new();
        int[] AutomaticCachedYears(string id)
        {
            var cached=validatedRecords.TryGetValue(id,out var existing)?System.Array.ConvertAll(existing,r=>r.year):System.Array.Empty<int>();
            return bundledOmittedYears.TryGetValue(id,out var omitted)?cached.Concat(omitted).Distinct().ToArray():cached;
        }
        void RefreshPersonCache(string id)
        {
            var records=PanoramaCache.YearRecords(TreeId,id);validatedRecords[id]=records;ApplyRecords(id,records);
        }
        void ApplyRecords(string id,PanoramaRecord[] all)
        {
            var person=Tree.People[id];var records=all.Where(r=>ResidenceTimeline.Same(string.IsNullOrWhiteSpace(r.place)?person.BirthPlace:r.place,locations[id].At(person,r.year))).ToArray();
            cachedYears[id]=System.Array.ConvertAll(records,record=>record.year);
            timelineMarkers[id]=TimelineMarker.Build(Tree.People[id],locations[id],cachedYears[id],Generation?.GeneratingYears(TreeId,id),Generation?.QueuedYears(TreeId,id));
            if(!sceneRevisions.TryGetValue(id,out var revisions))sceneRevisions[id]=revisions=new Dictionary<int,string>();
            revisions.Clear();
            foreach(var record in records)
            {
                revisions[record.year]=record.sha256+":"+record.horizonLift.ToString(System.Globalization.CultureInfo.InvariantCulture)+":"+record.place;
            }
        }
        void RefreshGenerationMarkers(string id)
        {
            if(Tree==null||!Tree.People.TryGetValue(id,out var person)||!locations.TryGetValue(id,out var moves))return;
            var next=TimelineMarker.Build(person,moves,CachedYears(id),Generation.GeneratingYears(TreeId,id),Generation.QueuedYears(TreeId,id));
            if(timelineMarkers.TryGetValue(id,out var old)&&TimelineMarker.Same(old,next))return;
            timelineMarkers[id]=next;RefreshPersonUI(id);
        }
        void RefreshPersonUI(string id)
        {
            if(Hud!=null&&(id==null||id==SelectedId))Hud.RefreshSceneCaption();
            if(content!=null)foreach(var panel in content.GetComponentsInChildren<TimelinePanel>())if(id==null||panel.PersonId==id)panel.Refresh();
            if(content!=null)foreach(var preview in content.GetComponentsInChildren<CubePanoramaPreview>())if(id==null||preview.PersonId==id)preview.Refresh();
            if(LocationEditor!=null&&LocationEditor.IsOpen)SetLocationModal(true);
            if(!string.IsNullOrEmpty(SelectedId)&&(id==null||SelectedId==id)&&Tree.People.ContainsKey(SelectedId))ApplyYear();
        }
        void ApplyYear()
        {
            var years=SceneYears(SelectedId);LifeTimeline.Pair(years,Year(SelectedId),out int a,out int b,out float blend);
            if(activeEnvironment==null)return;
            if(!LifeEndpointScenes.TrySceneRange(Tree.People[SelectedId],out _,out _) || a==0 || !activeEnvironment.UseYears(TreeId,SelectedId,a,b,blend,Tree.People[SelectedId].BirthPlace,SceneRevision(SelectedId,a),SceneRevision(SelectedId,b)))
                activeEnvironment.UseAllTime();
        }
        void Timeline(Person p,Vector3 position,bool inside)
        {var go=new GameObject("Life timeline");go.transform.SetParent(content);go.AddComponent<TimelinePanel>().Build(this,p,position,inside);}
        void OnDestroy() { if(Generation!=null){Generation.SceneSaved-=RefreshPersonAsync;Generation.SceneCompleted-=SceneCompleted;Generation.TargetChanged-=RefreshGenerationMarkers;}Font.textureRebuilt-=FontRebuilt;if(depthText!=null)Destroy(depthText);if(panelFade!=null)Destroy(panelFade);if(ghostMaterial!=null)Destroy(ghostMaterial);if(uiFont!=null)Destroy(uiFont);if(TreeControls!=null)Destroy(TreeControls.gameObject);if(Tutorial!=null)Destroy(Tutorial.gameObject);if(Hud!=null)Destroy(Hud.gameObject);if(Library!=null)Destroy(Library.gameObject);if(Batch!=null)Destroy(Batch.gameObject);if(AISetup!=null)Destroy(AISetup.gameObject); }
        public void Start()
        {
            uiFont=Font.CreateDynamicFontFromOSFont(new[]{"Arial","Roboto","Noto Sans","Droid Sans"},18);font=uiFont;
            Font.textureRebuilt+=FontRebuilt;
            try {
                Tree = GedcomImporter.Parse(familyData.text); TreeId=PanoramaCache.TreeId(familyData.text);
                DemoDocument=TreeDocument.FromTree(Tree,TreeId,familyData.name=="ProductDemoFamily"?"Hartwell Through Time · fictional":"Fictional demo",true);
                DemoDocument.SourceGedcom=familyData.text;DemoDocument.SourceFingerprint=TreeId;Document=DemoDocument;
                ProductDemoData.Attach(DemoDocument);
                DemoDocument=ProductDemoAuthoring.Load(DemoDocument);Document=DemoDocument;Tree=Document.ToTree();
                var legacy=Resources.Load<TextAsset>("DemoFamily");LegacyDemoDocument=legacy!=null?TreeDocument.FromTree(GedcomImporter.Parse(legacy.text),PanoramaCache.DemoTreeId,"Classic Hartwell demo · fictional",true):null;
                Generation=gameObject.AddComponent<SceneGeneration>();Generation.SceneSaved+=RefreshPersonAsync;Generation.SceneCompleted+=SceneCompleted;Generation.TargetChanged+=RefreshGenerationMarkers;
                foreach(var person in Tree.People.Values)locations[person.Id]=ResidenceTimeline.Load(TreeId,person);
                LocationEditor=gameObject.AddComponent<LocationEditor>();
                foreach(var person in Tree.People.Values)ApplyRecords(person.Id,System.Array.Empty<PanoramaRecord>());
                Passthrough=Camera.main.GetComponent<PassthroughMode>()??Camera.main.gameObject.AddComponent<PassthroughMode>();
                var hud=new GameObject("HUD controls",typeof(RectTransform));hud.transform.SetParent(transform,false);Hud=hud.AddComponent<HudOverlay>();Hud.Build(this);
                ShowTree();StartCoroutine(RefreshAllAsync());Generation.Resume(TreeId);
                var tutorial=new GameObject("Basics tutorial",typeof(RectTransform));tutorial.transform.SetParent(transform,false);
                Tutorial=tutorial.AddComponent<TutorialOverlay>();Tutorial.Build(this);
                var library=new GameObject("Tree library",typeof(RectTransform));library.transform.SetParent(transform,false);Library=library.AddComponent<TreeLibraryOverlay>();Library.Build(this);
                var batch=new GameObject("Scene generation queue",typeof(RectTransform));batch.transform.SetParent(transform,false);Batch=batch.AddComponent<SceneBatchOverlay>();Batch.Build(this);
                var setup=new GameObject("AI setup",typeof(RectTransform));setup.transform.SetParent(transform,false);AISetup=setup.AddComponent<AISetupPanel>();AISetup.Build(this);
            }
            catch (System.Exception e) { Status = e.Message; Debug.LogError("Family import failed: " + e.Message); }
        }
        public void ShowTree()
        {
            if(treeBuild!=null){StopCoroutine(treeBuild);treeBuild=null;}IsBuildingTree=false;
            bool returning=SelectedId!=null;
            LocationEditor?.Close();Clear(); SelectedId = null; Status = "Family gallery";
            content.gameObject.AddComponent<TreeEnvironment>().Build();
            PrepareSiblingView();
            if(visibleTree.People.Count>64){IsBuildingTree=true;Layout=null;treeBuild=StartCoroutine(BuildLargeTree(returning,TreeLayoutSettings.Load()));}
            else {Layout=BuildMode?TreeAuthoring.Layout(TreeId,visibleTree,TreeLayoutSettings.Load(),Tree):TreeLayout.Build(visibleTree);var previews=BeginTreeSpace();foreach(var pair in Layout.Positions)BuildPerson(pair,previews);FinishTree(returning);}
        }
        CubePreviewManager BeginTreeSpace()
        {
            if(Camera.main!=null)Camera.main.farClipPlane=Mathf.Max(80,Layout.Bounds.max.z+20);
            treeRoot=new GameObject("Family tree space").transform;treeRoot.SetParent(content,false);treeNodes.Clear();
            return content.gameObject.AddComponent<CubePreviewManager>();
        }
        void BuildPerson(KeyValuePair<string,Vector3> pair,CubePreviewManager previews)
        {
                var p=Tree.People[pair.Key];var pos=pair.Value;
                var node=new GameObject("Person sphere "+p.Id).transform;node.SetParent(treeRoot,false);
                // Build in world coordinates, then group all face controls with this node.
                var previous=content;content=node;
                // Author face contents in the original design units, then scale the whole node.
                const float faceDesignSize=3.8f;
                Primitive(p.Name,pos,Vector3.one*faceDesignSize,stone,PrimitiveType.Sphere);
                var body=node.GetComponentsInChildren<MeshRenderer>().First();
                var preview=node.gameObject.AddComponent<CubePanoramaPreview>();preview.Build(this,p,body);previews.Register(preview);
                var panel=new GameObject("Person information panel").transform;panel.SetParent(node,false);
                var panelCenter=pos+Vector3.up*3f;panel.position=panelCenter;content=panel;
                var backing=Box("Panel backing",panelCenter+Vector3.forward*.08f,new Vector3(3.7f,2.45f,.05f),ink);
                if(SiblingCount(p.Id)>0)SiblingButton(p,panelCenter);
                else {Box("Top rule",panelCenter+new Vector3(0,.99f,-.04f),new Vector3(3.4f,.02f,.03f),gold);var rule=panel.Find("Top rule");rule.GetComponent<Collider>().enabled=false;Destroy(rule.GetComponent<Collider>());}
                var portrait=Portrait(p.Id);
                if(portrait!=null)
                {
                    var quad=Primitive("Person portrait",panelCenter+PanelPortraitSizing.Position,new Vector3(PanelPortraitSizing.NearSize,PanelPortraitSizing.NearSize,1),paper,PrimitiveType.Quad);Destroy(quad.GetComponent<Collider>());var surface=quad.GetComponent<Renderer>();PanelPortraitSizing.Attach(surface);node.gameObject.AddComponent<PhotoPortrait>().Build(this,portrait,surface);
                }
                PanelTextSizing.Attach(Label(p.Name,panelCenter+new Vector3(portrait!=null?PanelPortraitSizing.LabelX:0,.70f,-.06f),.075f,paper),new Vector2(portrait!=null?PanelPortraitSizing.LabelWidth:3.4f,.34f));
                string lifespanEnd=LifeTimeline.IsLiving(p)?System.DateTime.Now.Year+" · Living":p.DeathDate;
                PanelTextSizing.Attach(Label(p.BirthDate+"  —  "+lifespanEnd,panelCenter+new Vector3(portrait!=null?PanelPortraitSizing.LabelX:0,.40f,-.06f),.047f,gold),new Vector2(portrait!=null?PanelPortraitSizing.LabelWidth:3.4f,.22f));
                Timeline(p,panelCenter,false);
                panel.localScale=Vector3.one*.85f;
                var portal=new GameObject("Scene portal").transform;portal.SetParent(node,false);
                portal.position=pos+Vector3.back*2.08f;content=portal;
                portal.gameObject.AddComponent<ScenePortal>().Build(p.Id);
                Label("ENTER SCENE",portal.position+Vector3.back*.16f,.045f,paper);
                content=previous;
                // Keep geometry offsets relative to the person center for visibility checks.
                var children=new List<Transform>();foreach(Transform child in node)children.Add(child);
                node.position=pos;foreach(var child in children)child.position-=pos;
                node.localScale=Vector3.one*(TreeLayout.Size/faceDesignSize);
                var facing=node.gameObject.AddComponent<CubeFacingControls>();facing.Build(body);
                var cubeTarget=body.gameObject.AddComponent<TreeNodeTarget>();cubeTarget.PersonId=p.Id;cubeTarget.Controls=facing;
                treeNodes.Add(node);
            if(Library!=null&&Library.IsOpen)foreach(var renderer in node.GetComponentsInChildren<Renderer>()){if(!modalRenderers.ContainsKey(renderer))modalRenderers[renderer]=renderer.enabled;renderer.enabled=false;}
        }
        IEnumerator BuildLargeTree(bool returning,TreeLayoutSettings settings)
        {
            var loading=Label("Preparing family tree…",new Vector3(0,2.1f,6.5f),.07f,paper);var source=visibleTree;var canonical=Tree;bool authoring=BuildMode;string layoutTree=TreeId;
            var task=Task.Run(()=>authoring?TreeAuthoring.Layout(layoutTree,source,settings,canonical):TreeLayout.Build(source,settings));while(!task.IsCompleted)yield return null;
            if(task.IsFaulted){_=task.Exception;Status="Could not arrange this tree.";loading.text=Status;IsBuildingTree=false;treeBuild=null;yield break;}
            Layout=task.Result;var previews=BeginTreeSpace();int built=0;double start=Time.realtimeSinceStartupAsDouble;
            foreach(var pair in Layout.Positions)
            {
                BuildPerson(pair,previews);built++;
                if(built%4==0||Time.realtimeSinceStartupAsDouble-start>.004){loading.text="Opening family tree · "+built+" / "+Layout.Positions.Count;yield return null;start=Time.realtimeSinceStartupAsDouble;}
            }
            foreach(var slot in Layout.Ghosts)
            {
                BuildGhostSphere(slot);built++;
                if(built%4==0||Time.realtimeSinceStartupAsDouble-start>.004){yield return null;start=Time.realtimeSinceStartupAsDouble;}
            }
            Destroy(loading.gameObject);IsBuildingTree=false;treeBuild=null;FinishTree(returning,true);
        }
        void FinishTree(bool returning,bool ghostsBuilt=false)
        {
            if(!ghostsBuilt)BuildGhostSpheres();else if(Library!=null&&Library.IsOpen)SetLocationModal(true);
            var lineColors=new MaterialPropertyBlock();
            foreach(var route in Layout.Connectors)
            {
                if(route.Points.All(p=>p==route.Points[0]))continue;
                var go=new GameObject("Family relationship");go.transform.SetParent(treeRoot,false);
                var line=go.AddComponent<LineRenderer>();line.sharedMaterial=paper;line.startWidth=line.endWidth=TreeLayout.ConnectorWidth;
                Tree.People.TryGetValue(route.PersonId??"",out var person);var color=RelationshipColors.For(person,gold.color);
                lineColors.SetColor("_BaseColor",color);lineColors.SetColor("_Color",color);line.SetPropertyBlock(lineColors);
                line.useWorldSpace=false;line.positionCount=route.Points.Length;line.SetPositions(route.Points);
            }
            Status="Family tree · "+Tree.People.Count+" people";
            if(Layout.Warnings.Count>0){Status+=" · "+string.Join("; ",Layout.Warnings.Distinct());Label(Status,new Vector3(0,4,4),.035f,gold);}
            var controls=new GameObject("Tree pan controls",typeof(RectTransform));controls.transform.SetParent(transform,false);
            TreeControls=controls.AddComponent<TreeNavigationOverlay>();TreeControls.Build(this);
            if(Tree.People.Count==0&&!BuildMode)Label("Turn on Build Tree to add your first person",new Vector3(0,2.1f,6.5f),.08f,paper);
            TreePan=Tree.People.Count>0?Layout.ClampPan(TreePan):Vector2.zero;ApplyTreePan();
            if(returning)FindAnyObjectByType<GalleryNavigation>()?.RestoreTreeView();
        }

        public void OpenDocument(TreeDocument document)
        {
            if(Generation.Busy)throw new System.InvalidOperationException("Wait for scene generation before switching trees.");
            if(Generation.CancellationNeedsSaving)throw new System.InvalidOperationException("Save the stopped-job state before switching trees.");
            document.Validate();bool sameTree=TreeId==document.Id;var oldPan=TreePan;
            if(!sameTree)bundledOmittedYears.Clear();
            var retained=sameTree?validatedRecords.Where(p=>!activeRefreshes.ContainsKey(p.Key)).ToDictionary(p=>p.Key,p=>p.Value):new Dictionary<string,PanoramaRecord[]>();
            var nextLocations=new Dictionary<string,ResidenceTimeline>();foreach(var p in document.People)nextLocations[p.Id]=sameTree&&locations.TryGetValue(p.Id,out var existing)?existing:ResidenceTimeline.Load(document.Id,p);
            LocationEditor?.Close();Clear();SelectedId=null;treeEpoch++;
            heldSceneYears.Clear();Document=document;if(document.Id==DemoDocument?.Id)DemoDocument=document;Tree=document.ToTree();TreeId=document.Id;selectedYears.Clear();completedYears.Clear();cachedYears.Clear();timelineMarkers.Clear();sceneRevisions.Clear();validatedRecords.Clear();refreshVersions.Clear();activeRefreshes.Clear();locations.Clear();CacheIndexReady=false;
            foreach(var p in Tree.People.Values){locations[p.Id]=nextLocations[p.Id];if(retained.TryGetValue(p.Id,out var records))validatedRecords[p.Id]=records;ApplyRecords(p.Id,records??System.Array.Empty<PanoramaRecord>());}
            TreePan=sameTree?oldPan:Vector2.zero;if(!sameTree){TreeRotation=Vector2.zero;TreeScale=1;}ShowTree();if(!sameTree)FindAnyObjectByType<GalleryNavigation>()?.ResetTreeView();Hud?.RefreshSceneCaption();
            if(sameTree)StartCoroutine(RefreshSubsetAsync(Tree.People.Keys.Where(id=>!retained.ContainsKey(id)).ToArray()));else StartCoroutine(RefreshAllAsync());
            PlayerPrefs.SetString("AncestorWalk.LastTree",TreeId);PlayerPrefs.Save();
        }
        public void OpenDemo()=>OpenDocument(DemoDocument);
        public void ApplyPhotoDocument(TreeDocument document)
        {
            document.Validate();if(document.Id!=TreeId)throw new System.InvalidOperationException("Photo edit belongs to another tree.");
            Document=document;if(document.Id==DemoDocument?.Id)DemoDocument=document;foreach(var p in document.People)if(Tree.People.TryGetValue(p.Id,out var current)){current.PhotoIds=p.PhotoIds;current.PrimaryPhotoId=p.PrimaryPhotoId;}
        }

        public void Select(string id)
        {
            if (IsBuildingTree||!Tree.People.TryGetValue(id, out var p)) return;
            ShowScene(p,null,0);
        }
        public void PreviewScene(string id,byte[] png,float horizonLift=12)
        {
            if(Tree.People.TryGetValue(id,out var person))ShowScene(person,PanoramaCache.Decode(png),horizonLift);
        }
        void ShowScene(Person p,Texture2D texture,float horizonLift)
        {
            if(IsTree)FindAnyObjectByType<GalleryNavigation>()?.EnterHistoricalScene();
            Clear();if(Camera.main!=null)Camera.main.farClipPlane=80;SelectedId=p.Id;Status="Exploring "+p.Name;
            var backdrop=Resources.Load<Material>("ClaraYork1870Panorama");
            var environment=content.gameObject.AddComponent<HistoricalEnvironment>();activeEnvironment=environment;
            environment.Build(backdrop,stone);
            if(texture!=null)environment.UsePanorama(texture,horizonLift);else {environment.UseAllTime();ApplyYear();}
            var overlayObject=new GameObject("Scene overlays",typeof(RectTransform));
            overlayObject.transform.SetParent(content,false);Overlay=overlayObject.AddComponent<SceneOverlay>();Overlay.Build(this,p);
        }
        void Clear()
        {
            if(treeBuild!=null){StopCoroutine(treeBuild);treeBuild=null;}IsBuildingTree=false;
            if(Overlay!=null){Overlay.gameObject.SetActive(false);Destroy(Overlay.gameObject);Overlay=null;}
            if(TreeControls!=null){TreeControls.gameObject.SetActive(false);Destroy(TreeControls.gameObject);TreeControls=null;}
            treeRoot=null;treeNodes.Clear();activeEnvironment=null;
            if (content != null) { content.gameObject.SetActive(false); Destroy(content.gameObject); }
            content = new GameObject("Gallery content").transform; content.SetParent(transform);
        }
        GameObject Box(string title, Vector3 pos, Vector3 size, Material material)
            => Primitive(title,pos,size,material,PrimitiveType.Cube);
        GameObject Primitive(string title, Vector3 pos, Vector3 size, Material material, PrimitiveType shape)
        {
            var go = GameObject.CreatePrimitive(shape); go.name = title;
            go.transform.SetParent(content); go.transform.position = pos; go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = material; return go;
        }
        TMPro.TMP_Text Label(string text, Vector3 pos, float size, Material color)
        {
            var go = new GameObject("Label"); go.transform.SetParent(content); go.transform.position = pos;
            return WorldText.Create(go,text,size*.4f,color.color);
        }
    }
}
