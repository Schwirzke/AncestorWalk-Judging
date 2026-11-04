using System.Collections.Generic;
using UnityEngine;

namespace AncestorWalk
{
    // A monoscopic spherical panorama supplies distant detail; geometry supplies near stereo depth.
    // This is deliberately a courtyard diorama, not a claimed navigable 3D reconstruction.
    public sealed class HistoricalEnvironment : MonoBehaviour
    {
        readonly List<Material> ownedMaterials = new();
        Renderer floor;
        Collider floorCollider;
        bool originalFloorVisible;
        public bool showCourtyardObjects = false;
        Material originalFloor;
        Material originalSkybox;
        Camera view;
        CameraClearFlags originalClearFlags;
        bool panoramaApplied,panoramaAligned;
        readonly List<Texture2D> ownedTextures = new();
        public void Build(Material backdrop, Material baseMaterial)
        {
            baseMaterial = Resources.Load<Material>("HistoricalSurface") ?? baseMaterial;
            var paving = Tint(baseMaterial,new Color(.31f,.28f,.23f));
            var brick = Tint(baseMaterial,new Color(.38f,.22f,.14f));
            var wood = Tint(baseMaterial,new Color(.16f,.12f,.085f));
            var leaves = Tint(baseMaterial,new Color(.23f,.3f,.13f));
            floor = GameObject.Find("Teleport floor")?.GetComponent<Renderer>();
            if(floor!=null){originalFloor=floor.sharedMaterial;originalFloorVisible=floor.enabled;floor.sharedMaterial=paving;floorCollider=floor.GetComponent<Collider>();if(floorCollider!=null)floorCollider.enabled=false;}
            originalSkybox = RenderSettings.skybox;
            view = Camera.main;
            if (view != null)
            {
                originalClearFlags = PassthroughMode.BackgroundFlags(view);
                PassthroughMode.SetBackground(view,CameraClearFlags.Skybox);
            }
            panoramaMaterial=new Material(backdrop);ownedMaterials.Add(panoramaMaterial);
            RenderSettings.skybox = panoramaMaterial;
            PassthroughMode.SetSceneBackground(view,true);
            panoramaApplied = true;
            // Near courtyard geometry has parallax; the infinitely distant skybox rotates with head view only.
            for (int side=-1;side<=1;side+=2)
            {
                Cube("Garden wall",new Vector3(side*5.5f,.65f,4),new Vector3(.5f,1.3f,14),brick);
                Cube("Stone coping",new Vector3(side*5.5f,1.34f,4),new Vector3(.65f,.1f,14),paving);
                for(int j=0;j<4;j++)
                {
                    float z=-1+j*3.5f;
                    Cube("Brick pier",new Vector3(side*5.5f,.9f,z),new Vector3(.8f,1.8f,.8f),brick);
                    Cube("Pier cap",new Vector3(side*5.5f,1.85f,z),new Vector3(.95f,.1f,.95f),paving);
                }
            }
            Cube("Bench seat",new Vector3(3.7f,.55f,3.5f),new Vector3(.65f,.14f,2.2f),wood);
            Cube("Bench back",new Vector3(4,.95f,3.5f),new Vector3(.12f,.7f,2.2f),wood);
            for(int i=-1;i<=1;i+=2) Cube("Bench leg",new Vector3(3.7f,.25f,3.5f+i*.8f),new Vector3(.5f,.5f,.12f),wood);
            for(int i=0;i<4;i++)
            {
                float x=i%2==0?-4.5f:4.5f, z=7+i/2*3;
                Cube("Terracotta planter",new Vector3(x,.3f,z),new Vector3(.8f,.6f,.8f),brick);
                var shrub=GameObject.CreatePrimitive(PrimitiveType.Sphere); shrub.name="Garden shrub"; shrub.transform.SetParent(transform,false);
                shrub.transform.position=new Vector3(x,.9f,z); shrub.transform.localScale=new Vector3(1,1.1f,1); shrub.GetComponent<Renderer>().sharedMaterial=leaves;
            }
            // Paving is visual only; navigation uses an analytical ground plane.
            for(int z=-2;z<=10;z++)
                Cube("Paving joint",new Vector3(0,.002f,z),new Vector3(10,.003f,.018f),wood,false);
            for(int x=-5;x<=5;x++)
                Cube("Paving joint",new Vector3(x,.003f,4),new Vector3(.018f,.003f,12),wood,false);
            SetCourtyardVisible(showCourtyardObjects);
        }
        public void SetCourtyardVisible(bool visible)
        {
            showCourtyardObjects=visible;
            foreach(var renderer in GetComponentsInChildren<MeshRenderer>())renderer.enabled=visible;
            foreach(var collider in GetComponentsInChildren<Collider>())collider.enabled=visible;
            if(floor!=null)floor.enabled=visible;
            if(floorCollider!=null)floorCollider.enabled=false;
        }
        Material Tint(Material source,Color color) { var m=new Material(source){color=color}; ownedMaterials.Add(m); return m; }
        Material panoramaMaterial;
        string pairKey,requestedKey,placeA,placeB,previousPlaceA,previousPlaceB;
        ScenePanoramaCache cache;ScenePanoramaCache.Entry pendingA,pendingB;
        readonly List<ScenePanoramaCache.Entry> currentEntries=new(),previousEntries=new();
        float requestedBlend;string fallback;
        public bool IsLoading=>pendingA!=null;
        public string LoadError {get;private set;}
        void Release(List<ScenePanoramaCache.Entry> list){foreach(var e in list)if(cache!=null)cache.Release(e);list.Clear();}
        void CancelPending(){if(pendingA!=null&&cache!=null)cache.Release(pendingA);if(pendingB!=null&&pendingB!=pendingA&&cache!=null)cache.Release(pendingB);pendingA=pendingB=null;}
        public void PlaceWeights(Dictionary<string,float> weights)
        {
            weights.Clear();if(pairKey==null||panoramaMaterial==null)return;
            float t=panoramaMaterial.GetFloat("_Transition"),mix=panoramaMaterial.GetFloat("_Blend"),oldMix=panoramaMaterial.GetFloat("_PreviousBlend");
            void Add(string place,float weight){if(string.IsNullOrWhiteSpace(place)||weight<=0)return;weights.TryGetValue(place,out float existing);weights[place]=existing+weight;}
            Add(placeA,t*(1-mix));Add(placeB,t*mix);Add(previousPlaceA,(1-t)*(1-oldMix));Add(previousPlaceB,(1-t)*oldMix);
        }
        readonly List<Texture2D> previousTextures=new();
        Coroutine transition;
        void ClearPrevious()
        {if(transition!=null){StopCoroutine(transition);transition=null;}Release(previousEntries);foreach(var t in previousTextures)Destroy(t);previousTextures.Clear();if(panoramaMaterial!=null)panoramaMaterial.SetFloat("_Transition",1);}
        System.Collections.IEnumerator FadeReplacement()
        {float start=Time.unscaledTime;while(Time.unscaledTime-start<.35f){panoramaMaterial.SetFloat("_Transition",(Time.unscaledTime-start)/.35f);yield return null;}transition=null;ClearPrevious();}

        public bool IsAllTime {get;private set;}
        void AlignPanoramaToViewer()
        {
            if(panoramaAligned||view==null||panoramaMaterial==null)return;
            // AlignedPanorama maps its texture midpoint to +X and rotates opposite Unity yaw.
            // Point that midpoint forward, leaving the longitude wrap directly behind the viewer.
            panoramaMaterial.SetFloat("_Rotation",Mathf.Repeat(90-view.transform.eulerAngles.y,360));
            panoramaAligned=true;
        }
        public void UsePanorama(Texture2D texture,float horizonLift=12,bool ownTexture=true,bool align=true)
        {
            if(panoramaMaterial==null){panoramaMaterial=new Material(Resources.Load<Material>("ClaraYork1870Panorama"));ownedMaterials.Add(panoramaMaterial);}
            if(align)AlignPanoramaToViewer();
            CancelPending();requestedKey=null;Release(currentEntries);ClearPrevious();
            foreach(var old in ownedTextures)Destroy(old);ownedTextures.Clear();if(ownTexture)ownedTextures.Add(texture);
            panoramaMaterial.SetTexture("_MainTex",texture);panoramaMaterial.SetTexture("_SecondTex",texture);
            panoramaMaterial.SetFloat("_Blend",0);panoramaMaterial.SetFloat("_HorizonLift",horizonLift);panoramaMaterial.SetFloat("_SecondLift",horizonLift);
            RenderSettings.skybox=panoramaMaterial;pairKey=null;IsAllTime=false;PassthroughMode.SetBackground(view,CameraClearFlags.Skybox);
        }
        public void UseAllTime()
        {
            if(IsAllTime)return;
            UsePanorama(Resources.Load<Texture2D>("AllTimePanorama"),0,false,false);
            IsAllTime=true;pairKey="all-time";placeA=placeB="All Time · symbolic environment";
        }
        public bool UseYears(string tree,string person,int a,int b,float blend,string fallbackPlace=null,string revisionA=null,string revisionB=null)
        {
            if(cache==null)cache=GetComponentInParent<AncestorGallery>().FullPanoramas;
            string key=tree+"/"+person+"/"+a+":"+b+":"+revisionA+":"+revisionB;
            requestedBlend=blend;fallback=fallbackPlace;
            if(key==requestedKey){if(key==pairKey)panoramaMaterial.SetFloat("_Blend",blend);return true;}
            CancelPending();LoadError=null;requestedKey=key;
            if(key==pairKey){panoramaMaterial.SetFloat("_Blend",blend);return true;}
            pendingA=cache.Acquire(tree,person,a,revisionA);pendingB=a==b?pendingA:cache.Acquire(tree,person,b,revisionB);
            // Retain the previous scene while loading; initial loads use neutral gray.
            if(IsAllTime){panoramaMaterial.SetTexture("_MainTex",Texture2D.grayTexture);panoramaMaterial.SetTexture("_SecondTex",Texture2D.grayTexture);panoramaMaterial.SetFloat("_Blend",blend);panoramaMaterial.SetFloat("_Transition",1);placeA=placeB=fallbackPlace;IsAllTime=false;}
            return true;
        }
        void Update()
        {
            if(pendingA==null||!pendingA.Ready||!pendingB.Ready)return;
            if(pendingA.Failed||pendingB.Failed)
            {LoadError="Cached scene unavailable. Previous scene kept.";CancelPending();if(currentEntries.Count==0&&(panoramaMaterial.GetTexture("_MainTex")==null||panoramaMaterial.GetTexture("_MainTex")==Texture2D.grayTexture))UseAllTime();return;}
            if(!PanoramaFrameBudget.TryTake())return;
            AlignPanoramaToViewer();
            ClearPrevious();previousPlaceA=placeA;previousPlaceB=placeB;
            panoramaMaterial.SetTexture("_PreviousA",panoramaMaterial.GetTexture("_MainTex"));panoramaMaterial.SetTexture("_PreviousB",panoramaMaterial.GetTexture("_SecondTex"));
            panoramaMaterial.SetFloat("_PreviousBlend",panoramaMaterial.GetFloat("_Blend"));panoramaMaterial.SetFloat("_PreviousLiftA",panoramaMaterial.GetFloat("_HorizonLift"));panoramaMaterial.SetFloat("_PreviousLiftB",panoramaMaterial.GetFloat("_SecondLift"));
            previousEntries.AddRange(currentEntries);currentEntries.Clear();previousTextures.AddRange(ownedTextures);ownedTextures.Clear();
            currentEntries.Add(pendingA);if(pendingB!=pendingA)currentEntries.Add(pendingB);
            panoramaMaterial.SetTexture("_MainTex",pendingA.Texture);panoramaMaterial.SetTexture("_SecondTex",pendingB.Texture);
            panoramaMaterial.SetFloat("_HorizonLift",pendingA.Record.horizonLift);panoramaMaterial.SetFloat("_SecondLift",pendingB.Record.horizonLift);panoramaMaterial.SetFloat("_Blend",requestedBlend);
            placeA=string.IsNullOrWhiteSpace(pendingA.Record.place)?fallback:pendingA.Record.place;placeB=string.IsNullOrWhiteSpace(pendingB.Record.place)?fallback:pendingB.Record.place;
            pairKey=requestedKey;pendingA=pendingB=null;IsAllTime=false;RenderSettings.skybox=panoramaMaterial;
            panoramaMaterial.SetFloat("_Transition",0);transition=StartCoroutine(FadeReplacement());
        }
        void Cube(string name,Vector3 position,Vector3 scale,Material material,bool collider=true)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube); go.name=name; go.transform.SetParent(transform,false);
            go.transform.position=position; go.transform.localScale=scale; go.GetComponent<Renderer>().sharedMaterial=material;
            if(!collider) Destroy(go.GetComponent<Collider>());
        }
        void OnDisable()
        {
            CancelPending();Release(currentEntries);ClearPrevious();
            if(floor!=null) {floor.sharedMaterial=originalFloor;floor.enabled=originalFloorVisible;}
            if(floorCollider!=null)floorCollider.enabled=false;
            if (panoramaApplied)
            {
                RenderSettings.skybox=originalSkybox;
                PassthroughMode.SetSceneBackground(view,false);
                PassthroughMode.SetBackground(view,originalClearFlags);
                panoramaApplied=false;
            }
        }
        void OnDestroy() { foreach(var m in ownedMaterials) Destroy(m); foreach(var t in ownedTextures) Destroy(t); foreach(var t in previousTextures)Destroy(t); }
    }
}
