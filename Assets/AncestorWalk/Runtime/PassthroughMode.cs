using System.Collections;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Meta;
using UnityEngine.XR.OpenXR.Features.CompositionLayers;

namespace AncestorWalk
{
    // The compositor shows the room; no camera pixels are read, recorded or uploaded.
    // Environment components still own their skyboxes. Only camera clearing changes.
    [RequireComponent(typeof(Camera))]
    public sealed class PassthroughMode : MonoBehaviour
    {
        public const string PreferenceKey = "AncestorWalk.Passthrough";
        public const string SceneOpacityKey="AncestorWalk.PassthroughSceneOpacity";
        public static float SceneOpacity=>ValidSceneOpacity(PlayerPrefs.GetFloat(SceneOpacityKey,PreferenceDefaults.PassthroughSceneOpacity));
        public static float ValidSceneOpacity(float value)=>float.IsNaN(value)||float.IsInfinity(value)?PreferenceDefaults.PassthroughSceneOpacity:Mathf.Clamp01(value);
        public static void SetSceneOpacity(float value)
        {PlayerPrefs.SetFloat(SceneOpacityKey,ValidSceneOpacity(value));PlayerPrefs.Save();Camera.main?.GetComponent<PassthroughMode>()?.ApplyCamera();}
        public bool Active { get; private set; }
        public bool Starting => startup != null;
        public bool IsEditorPreview => Active && !XRSettings.isDeviceActive;
        public string Status { get; private set; } = "Show your room behind the tree and scene controls";
        public CameraClearFlags ImmersiveFlags { get; private set; }
        Camera view;
        Color immersiveColor;
        ARSession session;
        ARCameraManager cameraManager;
        GameObject sessionObject;
        Coroutine startup;
        bool ownsCameraManager;
        bool sceneBackground;

        void Awake()
        {
            view = GetComponent<Camera>();
            immersiveColor = view.backgroundColor;
            ImmersiveFlags = view.clearFlags;
        }

        void Start()
        {
            // Every launch starts in full VR; the room remains an explicit HUD toggle.
            SetActive(false);
        }

        public void Toggle() => SetActive(!Active && !Starting);

        public void SetActive(bool active)
        {
            if (startup != null) { StopCoroutine(startup); startup = null; }
            if (!active)
            {
                Active = false;
                if (cameraManager != null) cameraManager.enabled = false;
                ApplyCamera();
                Status = "Immersive panoramas · switch to passthrough";
                Save(false);
                return;
            }
            if (Active) return;
            if (!XRSettings.isDeviceActive && (Application.isEditor || Application.platform != RuntimePlatform.Android))
            {
                Active = true;
                ApplyCamera();
                Status = "Editor background preview · the live room is visible on Quest";
                Save(true);
                return;
            }
            Status = "Starting passthrough…";
            startup = StartCoroutine(StartProviderSafely());
        }

        IEnumerator StartProviderSafely()
        {
            var provider = StartProvider();
            while (true)
            {
                bool more = false; object next = null; bool failed = false;
                try { more = provider.MoveNext(); if (more) next = provider.Current; }
                catch (System.Exception) { failed = true; }
                if (failed) { Fail("Passthrough could not start · immersive view kept"); yield break; }
                if (!more) yield break;
                yield return next;
            }
        }

        IEnumerator StartProvider()
        {
            // Yield before any early exit so the Coroutine field cannot retain a completed handle.
            yield return null;
            float deadline = Time.realtimeSinceStartup + 8;
            while (!XRSettings.isDeviceActive && Time.realtimeSinceStartup < deadline) yield return null;
            var feature = OpenXRSettings.Instance != null ? OpenXRSettings.Instance.GetFeature<ARCameraFeature>() : null;
            var layers = OpenXRSettings.Instance != null ? OpenXRSettings.Instance.GetFeature<OpenXRCompositionLayersFeature>() : null;
            if (!XRSettings.isDeviceActive || feature == null || !feature.enabled || layers == null || !layers.enabled || !OpenXRRuntime.IsExtensionEnabled("XR_FB_passthrough"))
            { Fail("Passthrough unavailable on this XR runtime · immersive view kept"); yield break; }
            deadline = Time.realtimeSinceStartup + 8;

            if (session == null)
            {
                sessionObject = new GameObject("Passthrough session");
                sessionObject.transform.SetParent(transform, false);
                sessionObject.SetActive(false);
                session = sessionObject.AddComponent<ARSession>();
                session.attemptUpdate = false;
                session.matchFrameRateRequested = false;
                sessionObject.SetActive(true);
            }
            while ((session.subsystem == null || !session.subsystem.running) && Time.realtimeSinceStartup < deadline) yield return null;
            if (session.subsystem == null || !session.subsystem.running)
            { Fail("Passthrough session could not start · immersive view kept"); yield break; }

            if (cameraManager == null)
            {
                cameraManager = view.GetComponent<ARCameraManager>();
                if (cameraManager == null) { cameraManager = view.gameObject.AddComponent<ARCameraManager>(); ownsCameraManager = true; }
            }
            cameraManager.enabled = true;
            while ((cameraManager.subsystem == null || !cameraManager.subsystem.running) && Time.realtimeSinceStartup < deadline) yield return null;
            if (cameraManager.subsystem == null || !cameraManager.subsystem.running)
            { Fail("Passthrough camera could not start · immersive view kept"); yield break; }
            Active = true;
            ApplyCamera();
            Status = "Passthrough · return to immersive panoramas";
            Save(true);
            startup = null;
        }

        void Fail(string message)
        {
            startup = null;
            Active = false;
            if (cameraManager != null) cameraManager.enabled = false;
            ApplyCamera();
            Status = message;
        }

        static void Save(bool active) { PlayerPrefs.SetInt(PreferenceKey, active ? 1 : 0); PlayerPrefs.Save(); }

        void ApplyCamera()
        {
            if (view == null) return;
            bool showScene=Active&&sceneBackground;
            view.clearFlags = Active&&!showScene ? CameraClearFlags.SolidColor : ImmersiveFlags;
            view.backgroundColor = Active ? new Color(.07f, .09f, .12f, 0) : immersiveColor;
            var panorama=RenderSettings.skybox;
            if(panorama!=null&&panorama.HasProperty("_Opacity"))panorama.SetFloat("_Opacity",showScene?SceneOpacity:1);
        }

        public static void SetSceneBackground(Camera camera,bool scene)
        {
            var mode=camera!=null?camera.GetComponent<PassthroughMode>():null;if(mode==null)return;
            mode.sceneBackground=scene;mode.ApplyCamera();
        }

        public static CameraClearFlags BackgroundFlags(Camera camera)
        {
            var mode = camera != null ? camera.GetComponent<PassthroughMode>() : null;
            return mode != null ? mode.ImmersiveFlags : camera != null ? camera.clearFlags : CameraClearFlags.Skybox;
        }

        public static void SetBackground(Camera camera, CameraClearFlags immersiveFlags)
        {
            if (camera == null) return;
            var mode = camera.GetComponent<PassthroughMode>();
            if (mode == null) { camera.clearFlags = immersiveFlags; return; }
            mode.ImmersiveFlags = immersiveFlags;
            mode.ApplyCamera();
        }

        void OnDisable()
        {
            if (startup != null) { StopCoroutine(startup); startup = null; }
            Active = false;
            if (cameraManager != null) cameraManager.enabled = false;
            ApplyCamera();
        }

        void OnDestroy()
        {
            if (ownsCameraManager && cameraManager != null) Destroy(cameraManager);
            if (sessionObject != null) Destroy(sessionObject);
        }
    }
}
