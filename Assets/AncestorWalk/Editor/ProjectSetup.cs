using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Interactions;
using UnityEngine.XR.OpenXR.Features.MetaQuestSupport;
using Unity.XR.CoreUtils;

namespace AncestorWalk.Editor
{
    public static class ProjectSetup
    {
        public const string ScenePath = "Assets/AAA/WordChain.unity";
        [MenuItem("Ancestor Walk/Configure Quest")]
        public static void ConfigureQuest()
        {
            // OpenXR 1.19 Meta validation reads this selection even for explicit Android checks.
            EditorUserBuildSettings.selectedBuildTargetGroup = BuildTargetGroup.Android;
            PlayerSettings.companyName = "Flexibendi LLC"; PlayerSettings.productName = "Ancestor Walk";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android,"com.flexibendi.ancestorwalk");
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android,ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android,false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android,new[]{GraphicsDeviceType.Vulkan});
            PlayerSettings.colorSpace = ColorSpace.Linear;
            var settingsObject = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
            settingsObject.FindProperty("activeInputHandler").intValue = 1;
            settingsObject.ApplyModifiedPropertiesWithoutUndo();
            const string path = "Assets/XR/XRGeneralSettingsPerBuildTarget.asset";
            Directory.CreateDirectory("Assets/XR");
            if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey,out XRGeneralSettingsPerBuildTarget settings))
            {
                settings = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(settings,path);
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey,settings,true);
            }
            if (!settings.HasManagerSettingsForBuildTarget(BuildTargetGroup.Android)) settings.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);
            var general = settings.SettingsForBuildTarget(BuildTargetGroup.Android); general.InitManagerOnStart = true;
            if (!XRPackageMetadataStore.AssignLoader(general.Manager,"UnityEngine.XR.OpenXR.OpenXRLoader",BuildTargetGroup.Android))
                throw new System.Exception("Could not assign Android OpenXR loader.");
            var xr = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            xr.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;
            xr.latencyOptimization = OpenXRSettings.LatencyOptimization.PrioritizeInputPolling;
            var touch = xr.GetFeature<OculusTouchControllerProfile>(); var quest = xr.GetFeature<MetaQuestFeature>();
            if (touch == null || quest == null) throw new System.Exception("Quest features unavailable.");
            touch.enabled = true; quest.enabled = true;
            var cameraFeature=xr.GetFeature<UnityEngine.XR.OpenXR.Features.Meta.ARCameraFeature>();
            var sessionFeature=xr.GetFeature<UnityEngine.XR.OpenXR.Features.Meta.ARSessionFeature>();
            var layersFeature=xr.GetFeature<UnityEngine.XR.OpenXR.Features.CompositionLayers.OpenXRCompositionLayersFeature>();
            if(cameraFeature==null||sessionFeature==null||layersFeature==null)throw new System.Exception("Meta passthrough features unavailable.");
            cameraFeature.enabled=true;sessionFeature.enabled=true;layersFeature.enabled=true;
            EditorUtility.SetDirty(cameraFeature);EditorUtility.SetDirty(sessionFeature);EditorUtility.SetDirty(layersFeature);
            var oldQuest = xr.GetFeature<UnityEngine.XR.OpenXR.Features.OculusQuestSupport.OculusQuestFeature>();
            if (oldQuest != null) { oldQuest.enabled=false; EditorUtility.SetDirty(oldQuest); }
            EditorUtility.SetDirty(touch); EditorUtility.SetDirty(quest); EditorUtility.SetDirty(xr); EditorUtility.SetDirty(general);
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/Mobile_RPAsset.asset");
            pipeline.msaaSampleCount = 4; pipeline.supportsHDR = false; pipeline.renderScale = 1;
            GraphicsSettings.defaultRenderPipeline = pipeline; QualitySettings.renderPipeline = pipeline;
            QualitySettings.vSyncCount = 0; EditorUtility.SetDirty(pipeline); AssetDatabase.SaveAssets();
        }
        [MenuItem("Ancestor Walk/Create Gallery Scene")]
        public static void CreateScene()
        {
            // Preserve all existing work before creating the new scene. Never edit scene YAML.
            for (int i=0;i<EditorSceneManager.sceneCount;i++)
            {
                var open = EditorSceneManager.GetSceneAt(i);
                if (open.isDirty && (string.IsNullOrEmpty(open.path) || !EditorSceneManager.SaveScene(open)))
                    throw new System.Exception("Save the unnamed scene before creating the gallery.");
            }
            if (File.Exists(ScenePath)) throw new System.Exception("Gallery already exists; open it instead of overwriting.");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            Directory.CreateDirectory("Assets/AAA"); Directory.CreateDirectory("Assets/AncestorWalk/Materials");
            Material ink = Mat("Ink",new Color(.045f,.09f,.12f));
            Material stone = Mat("Stone",new Color(.27f,.36f,.36f));
            Material gold = Mat("Gold",new Color(.83f,.64f,.31f));
            Material teal = Mat("Teal",new Color(.27f,.76f,.69f));
            Material paper = Mat("Paper",new Color(.94f,.9f,.79f));
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "Teleport floor";
            floor.transform.position = new Vector3(0,-.1f,3); floor.transform.localScale = new Vector3(16,.2f,18); floor.GetComponent<Renderer>().sharedMaterial = stone;
            floor.GetComponent<Collider>().enabled=false;
            var light = new GameObject("Sun").AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.5f; light.shadows = LightShadows.None;
            light.transform.rotation = Quaternion.Euler(40,-30,0);
            RenderSettings.ambientLight = new Color(.55f,.6f,.65f);
            var gallery = new GameObject("Family gallery").AddComponent<AncestorGallery>();
            gallery.familyData = Resources.Load<TextAsset>("ProductDemoFamily"); gallery.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            gallery.ink=ink; gallery.stone=stone; gallery.gold=gold; gallery.teal=teal; gallery.paper=paper;
            var rig = new GameObject("Player origin");
            var offset = new GameObject("Floor offset"); offset.transform.SetParent(rig.transform); offset.transform.localPosition = new Vector3(0,1.65f,0);
            var cameraObject = new GameObject("Main Camera",typeof(Camera),typeof(AudioListener)); cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(offset.transform,false);
            var camera = cameraObject.GetComponent<Camera>(); camera.fieldOfView=75; camera.nearClipPlane=.05f; camera.farClipPlane=80; camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.035f,.065f,.085f);
            camera.GetUniversalAdditionalCameraData().renderPostProcessing=false;
            var pose = cameraObject.AddComponent<TrackedPoseDriver>();
            pose.positionInput = new InputActionProperty(new InputAction("Head position",InputActionType.Value,"<XRHMD>/centerEyePosition"));
            pose.rotationInput = new InputActionProperty(new InputAction("Head rotation",InputActionType.Value,"<XRHMD>/centerEyeRotation"));
            pose.ignoreTrackingState = true;
            var origin = rig.AddComponent<XROrigin>(); origin.Origin=rig; origin.Camera=camera; origin.CameraFloorOffsetObject=offset; origin.CameraYOffset=1.65f; origin.RequestedTrackingOriginMode=XROrigin.TrackingOriginMode.Floor;
            var hand = new GameObject("Right aim"); hand.transform.SetParent(offset.transform,false);
            var pointer = hand.AddComponent<LineRenderer>(); pointer.sharedMaterial=Mat("LaserWhite",Color.white); pointer.positionCount=2; pointer.startWidth=.008f; pointer.endWidth=.004f;
            var navigation = rig.AddComponent<GalleryNavigation>(); navigation.view=camera; navigation.rightHand=hand.transform; navigation.pointer=pointer; navigation.gallery=gallery;
            EditorSceneManager.SaveScene(scene,ScenePath);
            EditorBuildSettings.scenes = new[]{new EditorBuildSettingsScene(ScenePath,true)};
            AssetDatabase.SaveAssets(); SceneDirtyStatus.Write();
        }
        static Material Mat(string name, Color color)
        {
            string path = "Assets/AncestorWalk/Materials/"+name+".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Unlit")); AssetDatabase.CreateAsset(m,path); }
            m.color=color; EditorUtility.SetDirty(m); return m;
        }
        [MenuItem("Ancestor Walk/Build Quest APK")]
        public static void BuildQuest()
        {
            Directory.CreateDirectory("Builds");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions{ scenes=new[]{ScenePath},locationPathName="Builds/AncestorWalk-Quest.apk",target=BuildTarget.Android,options=BuildOptions.Development });
            Directory.CreateDirectory("Artifacts"); File.WriteAllText("Artifacts/build-result.txt",report.summary.result+"\nErrors: "+report.summary.totalErrors);
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) throw new System.Exception("Quest build failed.");
        }
    }
    [InitializeOnLoad]
    public static class SceneDirtyStatus
    {
        static double next;
        static SceneDirtyStatus() { EditorApplication.update += Tick; }
        static void Tick() { if(EditorApplication.timeSinceStartup < next) return; next=EditorApplication.timeSinceStartup+2; Write(); }
        public static void Write()
        {
            bool dirty=false; for(int i=0;i<EditorSceneManager.sceneCount;i++) dirty |= EditorSceneManager.GetSceneAt(i).isDirty;
            Directory.CreateDirectory("Temp");
            File.WriteAllText("Temp/unity-scene-dirty-status.txt","updated_utc="+System.DateTime.UtcNow.ToString("O")+"\nactive_scene_dirty="+UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty.ToString().ToLowerInvariant()+"\nany_scene_dirty="+dirty.ToString().ToLowerInvariant());
        }
    }
}
