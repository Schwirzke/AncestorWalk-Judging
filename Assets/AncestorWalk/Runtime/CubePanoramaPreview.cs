using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace AncestorWalk
{
    // Materials lease shared thumbnails; the gallery cache owns their lifetime.
    public sealed class CubePanoramaPreview : MonoBehaviour
    {
        public const string SpinSpeedKey="AncestorWalk.SphereSpinSpeed";
        public const float MaximumSpinSpeed=30;
        public const float DetailMipBias=-.75f;
        public const float CompletionPulseAmount=.06f,CompletionPulseSeconds=.65f;
        public static float ValidSpinSpeed(float value)=>float.IsNaN(value)||float.IsInfinity(value)?PreferenceDefaults.SphereSpinSpeed:Mathf.Clamp(value,0,MaximumSpinSpeed);
        public static float SpinSpeed=>ValidSpinSpeed(PlayerPrefs.GetFloat(SpinSpeedKey,PreferenceDefaults.SphereSpinSpeed));
        AncestorGallery gallery;Renderer surface;Material material;CubeThumbnailCache cache;
        readonly List<CubeThumbnailCache.Entry> current=new(2),previous=new(2),pending=new(2);
        string requestedKey,currentKey;int requestedA,requestedB;float requestedBlend,fadeStart;
        CubeThumbnailCache.Entry pendingFirst,pendingSecond;bool loading,resident,fading;TimelinePanel[] labels;
        public string PersonId {get;private set;}
        public bool IsResident=>resident;public bool IsLoading=>loading;
        public int FirstYear {get;private set;}public int SecondYear {get;private set;}
        public Material PreviewMaterial=>material;public Bounds Bounds=>surface.bounds;
        float visibility=1;bool transparent;
        public float Visibility=>visibility;
        public void SetVisibility(float value)
        {
            value=Mathf.Clamp01(value);if(material==null||Mathf.Approximately(value,visibility))return;
            visibility=value;material.SetFloat("_Opacity",value);
            bool fade=value<.999f;
            if(transparent!=fade)
            {
                transparent=fade;
                material.SetFloat("_SrcBlend",(float)(fade?BlendMode.SrcAlpha:BlendMode.One));
                material.SetFloat("_DstBlend",(float)(fade?BlendMode.OneMinusSrcAlpha:BlendMode.Zero));
                material.SetFloat("_ZWrite",fade?0:1);
                material.renderQueue=(int)(fade?RenderQueue.Transparent:RenderQueue.Geometry);
            }
            // Keep this component and its texture leases alive, including at zero opacity.
            if(surface.forceRenderingOff!=(value<=0))surface.forceRenderingOff=value<=0;
        }
        public void Build(AncestorGallery owner,Person person,Renderer renderer)
        {
            gallery=owner;cache=owner.Thumbnails;PersonId=person.Id;surface=renderer;
            material=new Material(Resources.Load<Shader>("Shaders/CubePanorama")){name="Year preview · "+person.Id};surface.sharedMaterial=material;
            material.SetFloat("_SpinStartTime",Time.timeSinceLevelLoad);material.SetFloat("_SpinSpeed",SpinSpeed);
            material.SetFloat("_MipBias",DetailMipBias);
            material.SetFloat("_CompletionStart",-100);material.SetFloat("_CompletionAmount",CompletionPulseAmount);material.SetFloat("_CompletionDuration",CompletionPulseSeconds);
            var bounds=surface.localBounds;bounds.Expand(bounds.size*CompletionPulseAmount);surface.localBounds=bounds;
            Loading();
        }
        public void PulseCompletion()
        {if(material!=null)material.SetFloat("_CompletionStart",Time.timeSinceLevelLoad);}
        public void SetSpinSpeed(float speed)
        {
            if(material==null)return;
            float now=Time.timeSinceLevelLoad;
            float angle=material.GetFloat("_Rotation")+(now-material.GetFloat("_SpinStartTime"))*material.GetFloat("_SpinSpeed");
            material.SetFloat("_Rotation",Mathf.Repeat(angle,360));material.SetFloat("_SpinStartTime",now);material.SetFloat("_SpinSpeed",ValidSpinSpeed(speed));
        }
        void RefreshLabels(){if(labels==null||labels.Length==0)labels=GetComponentsInChildren<TimelinePanel>(true);foreach(var panel in labels)if(panel!=null)panel.Refresh();}
        static Texture2D Fallback=>Resources.Load<Texture2D>("AllTimePanorama");
        void Release(List<CubeThumbnailCache.Entry> list){foreach(var e in list)if(cache!=null)cache.Release(e);list.Clear();}
        void FinishTransition(){fading=false;Release(previous);if(material!=null)material.SetFloat("_Transition",1);}
        void CancelPending(){loading=false;Release(pending);pendingFirst=pendingSecond=null;}
        public void SetResident(bool value)
        {
            if(resident==value)return;resident=value;
            if(value){requestedKey=null;Refresh();}else{CancelPending();FinishTransition();Release(current);Loading();requestedKey=null;}
        }
        public void Refresh(bool force=false)
        {
            if(!resident)return;
            int a=0,b=0;float blend=0;
            if(LifeEndpointScenes.TrySceneRange(gallery.Tree.People[PersonId],out _,out _))LifeTimeline.Pair(gallery.SceneYears(PersonId),gallery.Year(PersonId),out a,out b,out blend);
            if(a==0){CancelPending();requestedKey="all-time";if(currentKey!="all-time")Default(true);return;}
            string key=a+":"+b+":"+gallery.SceneRevision(PersonId,a)+":"+gallery.SceneRevision(PersonId,b);
            requestedBlend=blend;
            if(key==requestedKey&&!force){if(currentKey==key)material.SetFloat("_Blend",blend);return;}
            if(key==currentKey&&!force){requestedKey=key;CancelPending();material.SetFloat("_Blend",blend);return;}
            FinishTransition();CancelPending();requestedA=a;requestedB=b;requestedKey=key;loading=true;
            pendingFirst=cache.Acquire(gallery.TreeId,PersonId,a,gallery.SceneRevision(PersonId,a));pending.Add(pendingFirst);
            pendingSecond=a==b?pendingFirst:cache.Acquire(gallery.TreeId,PersonId,b,gallery.SceneRevision(PersonId,b));if(pendingSecond!=pendingFirst)pending.Add(pendingSecond);
        }
        // Poll completed shared jobs only. No file access, decode, upload or GPU readback here.
        public bool LoadNext()
        {
            if(!resident||!loading||!pendingFirst.Ready||!pendingSecond.Ready)return false;
            if(pendingFirst.Failed||pendingSecond.Failed){CancelPending();requestedKey=null;if(current.Count==0)Default(true);return true;}
            Commit(pendingFirst.Texture,pendingSecond.Texture,pendingFirst.Lift,pendingSecond.Lift,requestedBlend,requestedKey,true);
            FirstYear=requestedA;SecondYear=requestedB;loading=false;pendingFirst=pendingSecond=null;RefreshLabels();return true;
        }
        void Commit(Texture first,Texture second,float liftA,float liftB,float blend,string key,bool fade)
        {
            FinishTransition();
            material.SetTexture("_PreviousA",material.GetTexture("_MainTex"));material.SetTexture("_PreviousB",material.GetTexture("_SecondTex"));
            material.SetFloat("_PreviousBlend",material.GetFloat("_Blend"));material.SetFloat("_PreviousLiftA",material.GetFloat("_HorizonLift"));material.SetFloat("_PreviousLiftB",material.GetFloat("_SecondLift"));
            previous.AddRange(current);current.Clear();current.AddRange(pending);pending.Clear();
            material.SetTexture("_MainTex",first);material.SetTexture("_SecondTex",second);material.SetFloat("_HorizonLift",liftA);material.SetFloat("_SecondLift",liftB);material.SetFloat("_Blend",blend);
            currentKey=key;fading=fade;fadeStart=Time.unscaledTime;material.SetFloat("_Transition",fade?0:1);if(!fade)Release(previous);
        }
        void Default(bool fade){Commit(Fallback,Fallback,0,0,0,"all-time",fade);FirstYear=SecondYear=0;RefreshLabels();}
        void Loading()
        {Commit(Texture2D.grayTexture,Texture2D.grayTexture,0,0,0,"loading",false);FirstYear=SecondYear=0;}
        public void Tick(){if(!fading)return;float t=Mathf.Clamp01((Time.unscaledTime-fadeStart)/.35f);material.SetFloat("_Transition",t);if(t>=1)FinishTransition();}
        void OnEnable(){if(material!=null)SetResident(true);}
        void OnDisable(){if(material!=null)SetResident(false);}
        void OnDestroy(){if(material!=null)Destroy(material);}
    }
}
