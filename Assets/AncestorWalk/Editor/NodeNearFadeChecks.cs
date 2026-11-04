using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace AncestorWalk.Editor
{
    public static class NodeNearFadeChecks
    {
        const string Report="Artifacts/NodeNearFade/checks.txt";
        public static string Run()
        {
            var gallery=UnityEngine.Object.FindAnyObjectByType<AncestorGallery>();
            if(!Application.isPlaying||gallery==null||!gallery.CacheIndexReady)throw new Exception("Wait for desktop Play mode cache indexing");
            Directory.CreateDirectory("Artifacts/NodeNearFade");File.WriteAllText(Report,"RUNNING");gallery.StartCoroutine(Safe(Checks(gallery)));return "Near fade checks running";
        }
        static IEnumerator Safe(IEnumerator tests)
        {
            while(true)
            {
                object next=null;bool more=false;Exception error=null;
                try{more=tests.MoveNext();if(more)next=tests.Current;}catch(Exception e){error=e;}
                if(error!=null){File.WriteAllText(Report,"FAILED: "+error);Debug.LogException(error);yield break;}
                if(!more)yield break;yield return next;
            }
        }
        static IEnumerator Checks(AncestorGallery g)
        {
            int n=0;void Check(bool ok,string why){if(!ok)throw new Exception(why);n++;}
            Check(NodeNearFade.ValidDistance(-1)==0&&NodeNearFade.ValidDistance(20)==2,"Preference clamps to 0–2 metres");
            Check(NodeNearFade.ValidDistance(float.NaN)==PreferenceDefaults.NodeNearFadeDistance&&NodeNearFade.ValidDistance(float.PositiveInfinity)==PreferenceDefaults.NodeNearFadeDistance,"Invalid preferences use baseline");
            Check(NodeNearFade.Opacity(0,0)==1&&NodeNearFade.Opacity(0,.4f)==0&&NodeNearFade.Opacity(.4f,.4f)==1,"Disabled and fade endpoints");
            Check(Mathf.Abs(NodeNearFade.Opacity(.2f,.4f)-.5f)<.0001f,"Surface distance has a smooth midpoint");
            float last=-1;for(int i=0;i<=20;i++){float alpha=NodeNearFade.Opacity(i*.025f,.4f);Check(alpha>=last&&alpha>=0&&alpha<=1,"Continuous monotonic opacity");last=alpha;}
            var nav=UnityEngine.Object.FindAnyObjectByType<GalleryNavigation>();var prefs=nav.GetComponent<NavigationPreferences>();
            bool navEnabled=nav.enabled,had=PlayerPrefs.HasKey(NodeNearFade.PreferenceKey),tutorial=g.Tutorial.IsOpen,prefsOpen=prefs.IsOpen;
            float saved=PlayerPrefs.GetFloat(NodeNearFade.PreferenceKey);var eye=Camera.main.transform;var oldPosition=eye.position;
            string selected=g.SelectedId;var rotation=g.TreeRotation;
            var layout=TreeLayoutSettings.Load();float farDistance=NavigationPreferences.InteractionGenerations;
            try
            {
                g.AISetup.Close();g.Library.Close();g.Batch.Close();g.LocationEditor.Close();g.Tutorial.Dismiss();nav.enabled=false;
                if(!g.IsTree)g.ShowTree();yield return null;
                var previews=g.GetComponentsInChildren<CubePanoramaPreview>();float deadline=Time.realtimeSinceStartup+25;
                while((g.Thumbnails.PendingCount>0||previews.Any(p=>p.IsLoading))&&Time.realtimeSinceStartup<deadline)yield return null;
                Check(g.Thumbnails.PendingCount==0&&previews.All(p=>!p.IsLoading),"Existing previews are warm before movement checks");
                yield return new WaitForSecondsRealtime(.4f);
                prefs.SetCloseFadeDistance(.4f);
                var node=g.GetComponentsInChildren<CubeFacingControls>().First();var preview=node.GetComponent<CubePanoramaPreview>();
                var sphere=node.GetComponentsInChildren<SphereCollider>().Single();var surface=sphere.GetComponent<Renderer>();
                float radius=sphere.radius*Mathf.Abs(sphere.transform.lossyScale.x);Vector3 center=sphere.transform.TransformPoint(sphere.center);
                eye.position=center-Vector3.up*(radius+1);yield return new WaitForSecondsRealtime(.4f);
                Check(Mathf.Abs(node.DistanceToSurface(eye.position)-1)<.001f,"Distance is from sphere surface");
                Check(node.CloseOpacity==1&&preview.Visibility==1&&!surface.forceRenderingOff,"Normal viewing retains full sphere visibility");
                Check(preview.PreviewMaterial.renderQueue==(int)RenderQueue.Geometry&&preview.PreviewMaterial.GetFloat("_ZWrite")==1,"Fully visible sphere stays in opaque rendering");
                int pools=node.PoolBuilds;var material=preview.PreviewMaterial;var texture=material.GetTexture("_MainTex");
                eye.position=center-Vector3.up*(radius+.2f);yield return new WaitForSecondsRealtime(.3f);
                Check(Mathf.Abs(node.CloseOpacity-.5f)<.01f&&Mathf.Abs(preview.Visibility-node.CloseOpacity)<.001f,"Sphere and panel share near opacity");
                Check(preview.PreviewMaterial.renderQueue==(int)RenderQueue.Transparent&&preview.PreviewMaterial.GetFloat("_ZWrite")==0,"Partial sphere uses alpha blending without depth occlusion");
                var text=node.Panel.GetComponentsInChildren<TMPro.TMP_Text>().First();Check(Mathf.Abs(text.color.a-node.CloseOpacity*node.InteractionOpacity)<.01f,"Panel text receives near multiplier");
                eye.position=center;yield return new WaitForSecondsRealtime(.3f);
                Check(node.CloseOpacity==0&&surface.forceRenderingOff&&!node.Panel.gameObject.activeSelf,"Inside node fully hides sphere and panel");
                Check(!sphere.enabled&&!node.Panel.GetComponentsInChildren<Collider>(true).Any(c=>c.enabled),"Hidden node and panel have no active hit targets");
                Check(!node.Portal.gameObject.activeSelf&&!node.PortalInteractive,"Hidden node leaves no floating portal");
                Check(preview.IsResident&&preview.PreviewMaterial==material&&material.GetTexture("_MainTex")==texture,"Hiding retains preview material, residency and texture");
                long reads=g.Thumbnails.DiskReads;
                eye.position=center-Vector3.up*(radius+1);yield return new WaitForSecondsRealtime(.4f);
                Check(node.CloseOpacity==1&&!surface.forceRenderingOff&&node.Panel.gameObject.activeSelf&&sphere.enabled,"Leaving restores sphere, panel and hit targets");
                Check(node.PoolBuilds==pools&&g.Thumbnails.DiskReads==reads,"Near round trip does not rebuild panel pool or reread preview");
                var advance=(Action<float>)Delegate.CreateDelegate(typeof(Action<float>),node,typeof(CubeFacingControls).GetMethod("AdvanceFacing",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance));
                for(int i=0;i<10;i++)advance(.016f);
                long allocationStart=GC.GetAllocatedBytesForCurrentThread();
                for(int i=0;i<100;i++){eye.position=center-Vector3.up*(radius+1+i*.0001f);advance(.016f);}
                long allocations=GC.GetAllocatedBytesForCurrentThread()-allocationStart;
                Check(allocations==0,"Warmed moving near-fade/control loop allocates no managed memory");
                var panel=node.Panel.Find("Panel backing").GetComponent<BoxCollider>();
                eye.position=panel.transform.TransformPoint(panel.center+Vector3.back*(panel.size.z*.5f))-panel.transform.forward*.1f;
                yield return new WaitForSecondsRealtime(.4f);
                Check(node.DistanceToSurface(eye.position)<.11f&&node.CloseOpacity<.3f,"Approaching panel alone fades node");
                Check(!node.PortalInteractive,"Portal input follows near visibility");
                prefs.SetCloseFadeDistance(0);yield return new WaitForSecondsRealtime(.3f);
                Check(node.CloseOpacity==1&&NodeNearFade.Distance==0,"Zero disables near fading immediately without rebuild");
                prefs.SetCloseFadeDistance(1.25f);Check(PlayerPrefs.GetFloat(NodeNearFade.PreferenceKey)==1.25f&&g.GetComponentsInChildren<CubeFacingControls>().All(c=>c.CloseFadeDistance==1.25f),"Preference persists and updates every existing node");
                g.ShowTree();yield return null;Check(g.GetComponentsInChildren<CubeFacingControls>().All(c=>c.CloseFadeDistance==1.25f),"Rebuilt tree uses saved preference");
                g.SetTreeRotation(new Vector2(52,35));yield return null;
                node=g.GetComponentsInChildren<CubeFacingControls>().First();sphere=node.GetComponentsInChildren<SphereCollider>().Single();
                center=sphere.transform.TransformPoint(sphere.center);eye.position=center;yield return new WaitForSecondsRealtime(.4f);
                Check(node.CloseOpacity==0,"Rotated and tilted node still hides at its actual world position");
                var frame=new GameObject("Near fade oriented box fixture").transform;
                frame.SetPositionAndRotation(new Vector3(12,4,-8),Quaternion.Euler(25,51,12));frame.localScale=Vector3.one*2;
                Check(NodeNearFade.BoxDistance(frame.position,frame,Vector3.zero,Vector3.one)==0,"Oriented box interior has zero distance");
                Check(Mathf.Abs(NodeNearFade.BoxDistance(frame.TransformPoint(new Vector3(1,0,0)),frame,Vector3.zero,Vector3.one)-1)<.001f,"Oriented scaled box distance is measured in metres");UnityEngine.Object.Destroy(frame.gameObject);
                // Reset is an actual preference action, including the new distance default.
                prefs.GetComponentsInChildren<Button>(true).Single(b=>b.name=="Reset layout").onClick.Invoke();
                Check(NodeNearFade.Distance==PreferenceDefaults.NodeNearFadeDistance,"Reset layout restores near fade baseline");
                File.WriteAllText(Report,n+" near fade checks passed: surface distances, smooth opacity, per-node panel/sphere/portal visibility, transparent/opaque rendering, hidden hit targets, retained texture residency, no pool rebuild/reload on movement, zero managed bytes in 100 warmed moving updates, live/persistent/off/reset preferences and rotated trees. Desktop Play mode only; no paid calls or Quest deployment.");
            }
            finally
            {
                prefs.SetTreeLayout(layout.Gap,layout.Stagger,layout.Rise);prefs.SetInteractionDistance(farDistance);
                if(had)PlayerPrefs.SetFloat(NodeNearFade.PreferenceKey,saved);else PlayerPrefs.DeleteKey(NodeNearFade.PreferenceKey);
                PlayerPrefs.Save();prefs.SetCloseFadeDistance(NodeNearFade.Distance);if(!had){PlayerPrefs.DeleteKey(NodeNearFade.PreferenceKey);PlayerPrefs.Save();}
                g.SetTreeRotation(rotation);if(selected!=null)g.Select(selected);else g.ShowTree();eye.position=oldPosition;
                prefs.SetOpen(prefsOpen);if(tutorial)g.Tutorial.Open();else g.Tutorial.Dismiss();nav.enabled=navEnabled;
            }
        }
    }
}
