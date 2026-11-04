using UnityEngine;
namespace AncestorWalk
{
    // Bundled symbolic artwork stays infinitely distant while the viewer flies.
    public sealed class TreeEnvironment : MonoBehaviour
    {
        public const string BrightnessKey="AncestorWalk.TreePanoramaBrightness";
        static float ClampBrightness(float value)=>float.IsNaN(value)||float.IsInfinity(value)?PreferenceDefaults.PanoramaBrightness:Mathf.Clamp(value,0,1.5f);
        public static float Brightness=>ClampBrightness(PlayerPrefs.GetFloat(BrightnessKey,PreferenceDefaults.PanoramaBrightness));
        public static void SetBrightness(float value)
        {
            value=ClampBrightness(value);PlayerPrefs.SetFloat(BrightnessKey,value);PlayerPrefs.Save();
            foreach(var environment in Object.FindObjectsByType<TreeEnvironment>(FindObjectsSortMode.None))
                if(environment.material!=null)environment.material.SetFloat("_Exposure",value);
        }
        Material previousSkybox,material;Camera view;CameraClearFlags previousFlags;
        Renderer floor;bool previousFloorVisible,applied;
        Collider floorCollider;
        public void Build()
        {
            previousSkybox=RenderSettings.skybox;view=Camera.main;
            if(view!=null){previousFlags=PassthroughMode.BackgroundFlags(view);PassthroughMode.SetBackground(view,CameraClearFlags.Skybox);}
            floor=GameObject.Find("Teleport floor")?.GetComponent<Renderer>();
            if(floor!=null){previousFloorVisible=floor.enabled;floor.enabled=false;}
            floorCollider=floor!=null?floor.GetComponent<Collider>():null;
            if(floorCollider!=null)floorCollider.enabled=false;
            var image=Resources.Load<Texture2D>("HumanGenerationsPanorama");
            material=new Material(Resources.Load<Material>("ClaraYork1870Panorama")){name="Human generations · tree background"};
            material.SetTexture("_MainTex",image);material.SetTexture("_SecondTex",image);
            material.SetFloat("_Blend",0);material.SetFloat("_Transition",1);
            material.SetFloat("_HorizonLift",0);material.SetFloat("_SecondLift",0);
            material.SetFloat("_Exposure",Brightness);
            RenderSettings.skybox=material;PassthroughMode.SetSceneBackground(view,false);applied=true;
        }
        void OnDisable()
        {
            if(!applied)return;applied=false;RenderSettings.skybox=previousSkybox;
            PassthroughMode.SetBackground(view,previousFlags);if(floor!=null)floor.enabled=previousFloorVisible;
            if(floorCollider!=null)floorCollider.enabled=false;
        }
        void OnDestroy(){if(material!=null)Destroy(material);}
    }
}
