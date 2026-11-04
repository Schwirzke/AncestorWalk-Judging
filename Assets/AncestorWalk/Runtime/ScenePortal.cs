using UnityEngine;
namespace AncestorWalk
{
    // Procedural portal artwork and bounded local sparks; the root owns the hit target.
    public sealed class ScenePortal : MonoBehaviour
    {
        public CubeFacingControls Controls {get;set;}
        public string PersonId {get;private set;}
        Material portalMaterial,sparkMaterial;ParticleSystem sparks;MeshRenderer surface;
        Camera view;float nextVisibilityCheck;
        public void Build(string personId)
        {
            PersonId=personId;
            var hit=gameObject.AddComponent<BoxCollider>();hit.size=new Vector3(1.9f,1.2f,.08f);
            gameObject.AddComponent<GalleryTarget>().personId=personId;
            portalMaterial=new Material(Resources.Load<Shader>("Shaders/ScenePortal"));
            portalMaterial.SetFloat("_Seed",Mathf.Abs(personId.GetHashCode()%1000)*.01f);
            var quad=GameObject.CreatePrimitive(PrimitiveType.Quad);quad.name="Portal vortex";
            quad.transform.SetParent(transform,false);quad.transform.localScale=new Vector3(1.9f,1.2f,1);
            quad.GetComponent<Collider>().enabled=false;Destroy(quad.GetComponent<Collider>());surface=quad.GetComponent<MeshRenderer>();surface.sharedMaterial=portalMaterial;
            surface.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;surface.receiveShadows=false;
            sparkMaterial=new Material(Resources.Load<Shader>("Shaders/PortalSpark"));
            var go=new GameObject("Portal sparks");go.transform.SetParent(transform,false);
            go.transform.localPosition=new Vector3(0,0,-.08f);go.transform.localScale=new Vector3(1.4f,.88f,1);
            sparks=go.AddComponent<ParticleSystem>();sparks.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=sparks.main;main.simulationSpace=ParticleSystemSimulationSpace.Local;main.scalingMode=ParticleSystemScalingMode.Hierarchy;
            main.startLifetime=new ParticleSystem.MinMaxCurve(1.2f,2.2f);main.startSpeed=new ParticleSystem.MinMaxCurve(.015f,.05f);
            main.startSize=new ParticleSystem.MinMaxCurve(.025f,.055f);main.maxParticles=32;main.loop=true;main.playOnAwake=false;
            main.startColor=new ParticleSystem.MinMaxGradient(new Color(.2f,1,.85f),new Color(1,.72f,.25f));
            var emission=sparks.emission;emission.rateOverTime=12;
            var shape=sparks.shape;shape.shapeType=ParticleSystemShapeType.Circle;shape.radius=.58f;shape.radiusThickness=.06f;
            var velocity=sparks.velocityOverLifetime;velocity.enabled=true;velocity.space=ParticleSystemSimulationSpace.Local;
            velocity.orbitalZ=new ParticleSystem.MinMaxCurve(.35f);
            var colors=sparks.colorOverLifetime;colors.enabled=true;
            var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},
                new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,.2f),new GradientAlphaKey(0,1)});colors.color=gradient;
            var renderer=go.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=sparkMaterial;
            renderer.renderMode=ParticleSystemRenderMode.Billboard;renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            sparks.Play();
        }
        void Update()
        {
            if(Time.unscaledTime<nextVisibilityCheck)return;nextVisibilityCheck=Time.unscaledTime+.1f;
            if(view==null)view=Camera.main;if(sparks==null||view==null)return;
            bool nearby=surface.isVisible&&(view.transform.position-transform.position).sqrMagnitude<144;
            if(nearby&&!sparks.isPlaying)sparks.Play();
            else if(!nearby&&sparks.isPlaying)sparks.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        void OnDisable(){if(sparks!=null)sparks.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);}
        void OnDestroy(){if(portalMaterial!=null)Destroy(portalMaterial);if(sparkMaterial!=null)Destroy(sparkMaterial);}
    }
}
