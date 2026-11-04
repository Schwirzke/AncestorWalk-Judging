using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Meta;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace AncestorWalk.Editor
{
    public static class PassthroughChecks
    {
        const string Report = "Artifacts/Passthrough/checks.txt";
        public static string Run()
        {
            var gallery = UnityEngine.Object.FindAnyObjectByType<AncestorGallery>();
            if (!Application.isPlaying || gallery == null || !gallery.CacheIndexReady || XRSettings.isDeviceActive)
                throw new Exception("Wait for desktop Play mode cache indexing");
            Directory.CreateDirectory("Artifacts/Passthrough");
            File.WriteAllText(Report, "RUNNING");
            gallery.StartCoroutine(RunSafely(Checks(gallery)));
            return "Passthrough transition checks running";
        }

        static IEnumerator RunSafely(IEnumerator checks)
        {
            while (true)
            {
                object next = null; bool more = false; Exception failure = null;
                try { more = checks.MoveNext(); if (more) next = checks.Current; }
                catch (Exception e) { failure = e; }
                if (failure != null) { File.WriteAllText(Report, "FAILED: " + failure); Debug.LogException(failure); yield break; }
                if (!more) yield break;
                yield return next;
            }
        }

        static IEnumerator Checks(AncestorGallery g)
        {
            int count = 0;
            void Check(bool condition, string why) { if (!condition) throw new Exception(why); count++; }
            bool had = PlayerPrefs.HasKey(PassthroughMode.PreferenceKey);
            int saved = PlayerPrefs.GetInt(PassthroughMode.PreferenceKey);
            bool hadOpacity=PlayerPrefs.HasKey(PassthroughMode.SceneOpacityKey);float savedOpacity=PassthroughMode.SceneOpacity;
            bool active = g.Passthrough.Active, hud = g.HudVisible, tutorial = g.Tutorial.IsOpen;
            string selected = g.SelectedId;
            var rotation = g.TreeRotation;
            var nav = UnityEngine.Object.FindAnyObjectByType<GalleryNavigation>();
            bool navigation = nav.enabled;
            string person = g.Tree.People.Keys.FirstOrDefault(id => g.AvailableYears(id).Length > 0) ?? g.Tree.People.Keys.First();
            int year = g.Year(person);
            try
            {
                g.AISetup.Close(); g.Library.Close(); g.Batch.Close(); g.Tutorial.Dismiss(); g.LocationEditor.Close();
                nav.enabled = false;
                PassthroughMode.SetSceneOpacity(PreferenceDefaults.PassthroughSceneOpacity);
                g.Passthrough.SetActive(false); g.ShowTree(); yield return null;
                var camera = Camera.main;
                var sky = RenderSettings.skybox;
                var position = camera.transform.position;
                var orientation = camera.transform.rotation;
                var nodes = g.GetComponentsInChildren<CubePanoramaPreview>();
                var xr = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
                Check(xr.GetFeature<ARCameraFeature>().enabled && xr.GetFeature<ARSessionFeature>().enabled, "Android camera and session features enabled");
                Check(xr.GetFeature<UnityEngine.XR.OpenXR.Features.CompositionLayers.OpenXRCompositionLayersFeature>().enabled, "Android compositor layer provider enabled");
                Check(camera.clearFlags == CameraClearFlags.Skybox, "Tree starts immersive");
                long reads = PanoramaCache.ImageReadCount;
                g.Passthrough.Toggle();
                Check(g.Passthrough.Active && g.Passthrough.IsEditorPreview, "Desktop explicitly reports background preview");
                Check(camera.clearFlags == CameraClearFlags.SolidColor && camera.backgroundColor.a == 0, "Transparent camera clear for compositor");
                Check(PlayerPrefs.GetInt(PassthroughMode.PreferenceKey) == 1, "Room choice persisted");
                Check(RenderSettings.skybox == sky && g.GetComponentsInChildren<CubePanoramaPreview>().SequenceEqual(nodes), "Toggle retains tree nodes and panorama material");
                Check(PanoramaCache.ImageReadCount == reads, "Toggle causes no panorama reads");
                Check(camera.transform.position == position && camera.transform.rotation == orientation, "Toggle does not move viewer");
                g.SetTreeRotation(new Vector2(25, 15));
                var treeRotation = g.TreeRotation;
                g.Select(person); yield return null;
                Check(g.Passthrough.Active && camera.clearFlags == CameraClearFlags.Skybox&&Mathf.Approximately(RenderSettings.skybox.GetFloat("_Opacity"),.25f), "Scene entry keeps room mode and draws a quarter-opacity panorama");
                float until = Time.realtimeSinceStartup + 15;
                while (g.IsSceneLoading && Time.realtimeSinceStartup < until) yield return null;
                Check(camera.backgroundColor.a == 0 && camera.clearFlags == CameraClearFlags.Skybox&&Mathf.Approximately(RenderSettings.skybox.GetFloat("_Opacity"),.25f), "Async panorama loading retains quarter opacity");
                var years = g.AvailableYears(person);
                if (years.Length > 0) g.SetYear(person, years.Last());
                yield return null;
                Check(g.Passthrough.Active&&Mathf.Approximately(RenderSettings.skybox.GetFloat("_Opacity"),.25f), "Year changes preserve scene opacity");
                var material=RenderSettings.skybox;PassthroughMode.SetSceneOpacity(.6f);
                Check(RenderSettings.skybox==material&&Mathf.Approximately(material.GetFloat("_Opacity"),.6f),"Opacity changes immediately without rebuilding or reloading the panorama");
                Check(PassthroughMode.SceneOpacity==.6f,"Scene opacity persists");
                Check(Mathf.Approximately(PassthroughMode.ValidSceneOpacity(float.NaN),.25f)&&PassthroughMode.ValidSceneOpacity(-1)==0&&PassthroughMode.ValidSceneOpacity(2)==1,"Opacity validates defaults and endpoints");
                var fixture=new GameObject("Panorama alpha fixture");var fixtureCamera=fixture.AddComponent<Camera>();fixtureCamera.enabled=false;fixtureCamera.clearFlags=CameraClearFlags.Skybox;fixtureCamera.backgroundColor=Color.clear;fixtureCamera.cullingMask=0;
                var target=new RenderTexture(16,16,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);target.Create();var pixel=new Texture2D(16,16,TextureFormat.RGBA32,false,true);var oldTarget=RenderTexture.active;
                try
                {
                    PassthroughMode.SetSceneOpacity(.25f);RenderPipeline.SubmitRenderRequest(fixtureCamera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
                    RenderTexture.active=target;pixel.ReadPixels(new Rect(0,0,16,16),0,0);pixel.Apply();Check(Mathf.Abs(pixel.GetPixel(8,8).a-.25f)<.02f,"GPU skybox output preserves quarter alpha for compositor blending");
                    PassthroughMode.SetSceneOpacity(0);RenderPipeline.SubmitRenderRequest(fixtureCamera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});pixel.ReadPixels(new Rect(0,0,16,16),0,0);pixel.Apply();var clear=pixel.GetPixel(8,8);
                    Check(clear.a<.01f&&clear.r<.01f&&clear.g<.01f&&clear.b<.01f,"Zero opacity produces transparent premultiplied output");
                }
                finally{RenderTexture.active=oldTarget;UnityEngine.Object.DestroyImmediate(fixture);target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(pixel);}
                PassthroughMode.SetSceneOpacity(.25f);
                int sceneYear = g.Year(person);
                var panorama = RenderSettings.skybox;
                g.Passthrough.Toggle();
                Check(!g.Passthrough.Active && camera.clearFlags == CameraClearFlags.Skybox, "VR restores scene panorama");
                Check(RenderSettings.skybox.GetFloat("_Opacity")==1,"Immersive scene stays fully opaque regardless of saved room opacity");
                Check(g.Year(person) == sceneYear && RenderSettings.skybox == panorama, "Returning to VR retains selected year and current material");
                g.Hud.SetVisible(false); yield return null;
                typeof(HudOverlay).GetMethod("Configure",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(g.Hud,new object[]{true});
                var button = (RectTransform)g.Hud.transform.Find("Toggle passthrough");
                Check(button.gameObject.activeInHierarchy && g.Hud.GetComponent<Canvas>().enabled, "Room/VR control remains when HUD hidden");
                Check(g.Hud.HandleInput(new Ray(button.position-button.forward,button.forward),true,true,false,false), "Controller ray captures toggle click");
                Check(g.Passthrough.Active, "Controller-style click enables room preview");
                g.ShowTree(); yield return null;
                Check(g.Passthrough.Active && camera.clearFlags == CameraClearFlags.SolidColor, "Return to tree preserves room mode");
                Check(g.TreeRotation == treeRotation, "Scene round trip retains tree rotation");
                g.Passthrough.Toggle();
                Check(!g.Passthrough.Active && camera.clearFlags == CameraClearFlags.Skybox && camera.backgroundColor.a > 0, "Tree VR restores original opaque camera color");
                for (int i = 0; i < 12; i++) { g.Passthrough.Toggle(); Check(camera.clearFlags == CameraClearFlags.SolidColor, "Repeated room entry"); g.Passthrough.Toggle(); Check(camera.clearFlags == CameraClearFlags.Skybox, "Repeated VR restore"); }
                Check(g.GetComponentsInChildren<CubePanoramaPreview>().Length == nodes.Length, "Repeated toggles never recreate tree");
                var prefs=UnityEngine.Object.FindAnyObjectByType<NavigationPreferences>();
                typeof(NavigationPreferences).GetMethod("Configure",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(prefs,new object[]{true});
                g.Hud.SetVisible(true);prefs.SetOpen(true);Canvas.ForceUpdateCanvases();
                Check(prefs.GetComponentInChildren<Canvas>(true).renderMode==RenderMode.WorldSpace,"Settings is camera-relative on Quest");
                void Click(RectTransform rect){Canvas.ForceUpdateCanvases();Check(prefs.HandleControllerInput(new Ray(rect.position-rect.forward,rect.forward),true,true,false),"Settings captures controller click");prefs.HandleControllerInput(new Ray(rect.position-rect.forward,rect.forward),false,false,false);}
                var scenes=prefs.GetComponentsInChildren<Button>(true).Single(b=>b.name=="Scenes");Click((RectTransform)scenes.transform);
                var opacity=prefs.GetComponentsInChildren<Slider>(true).Single(s=>s.name=="Passthrough scene opacity");var rail=(RectTransform)opacity.transform;var edge=rail.TransformPoint(new Vector3(rail.rect.xMax,0,0));
                Check(prefs.HandleControllerInput(new Ray(edge-rail.forward,rail.forward),true,true,false),"Controller grabs opacity slider");Check(PassthroughMode.SceneOpacity==1&&prefs.CapturingPointer,"Controller drag updates opacity and captures input");
                prefs.HandleControllerInput(new Ray(edge-rail.forward,rail.forward),false,false,false);Check(!prefs.CapturingPointer,"Release ends capture");
                Click((RectTransform)prefs.GetComponentsInChildren<Button>(true).Single(b=>b.name=="Reset scene settings").transform);Check(PassthroughMode.SceneOpacity==.25f,"Scene reset restores quarter opacity");
                Check(!g.AISetup.GetComponentsInChildren<RectTransform>(true).Any(r=>r.name=="Open AI setup"),"AI Setup has no standalone launcher to cover Home");
                Click((RectTransform)prefs.GetComponentsInChildren<Button>(true).Single(b=>b.name=="AI Setup").transform);Check(g.AISetup.IsOpen&&!prefs.IsOpen,"Quest opens AI Setup through Settings");g.AISetup.Close();
                typeof(NavigationPreferences).GetMethod("Configure",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(prefs,new object[]{false});
                File.WriteAllText(Report, count + " passthrough checks passed. GPU premultiplied alpha at quarter/zero opacity, opaque VR, scene/year transitions, saved opacity/reset, Quest Settings controller buttons/sliders, AI Setup routing, HUD-hidden restore, stable tree objects and no toggle PNG reads. Physical Quest compositor and startup require device validation; no APK installed.");
            }
            finally
            {
                g.SetYear(person, year); g.SetTreeRotation(rotation);
                if (selected != null) g.Select(selected); else g.ShowTree();
                g.Passthrough.SetActive(active); g.Hud.SetVisible(hud);
                PassthroughMode.SetSceneOpacity(savedOpacity);if(!hadOpacity)PlayerPrefs.DeleteKey(PassthroughMode.SceneOpacityKey);
                if (tutorial) g.Tutorial.Open(); else g.Tutorial.Dismiss();
                nav.enabled = navigation;
                if (had) PlayerPrefs.SetInt(PassthroughMode.PreferenceKey, saved); else PlayerPrefs.DeleteKey(PassthroughMode.PreferenceKey);
                PlayerPrefs.Save();
            }
        }
    }
}
