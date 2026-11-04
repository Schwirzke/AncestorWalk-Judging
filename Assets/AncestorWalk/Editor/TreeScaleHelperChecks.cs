using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace AncestorWalk.Editor
{
    public static class TreeScaleHelperChecks
    {
        const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        const string Report="Artifacts/TreeScaleHelper/checks.txt";
        public static string Run()
        {
            var g=UnityEngine.Object.FindAnyObjectByType<AncestorGallery>();
            if(!Application.isPlaying||g==null||!g.CacheIndexReady||MetaApi.Configured)throw new Exception("Wait for Play/cache readiness with no direct key");
            Directory.CreateDirectory("Artifacts/TreeScaleHelper");File.WriteAllText(Report,"RUNNING");g.StartCoroutine(Safe(Checks(g)));return "Scale/helper checks running";
        }
        static IEnumerator Safe(IEnumerator tests)
        {
            while(true)
            {
                object next=null;bool more=false;Exception error=null;
                try{more=tests.MoveNext();if(more)next=tests.Current;}catch(Exception e){error=e;}
                if(error!=null){File.WriteAllText(Report,"FAILED: "+error);Debug.LogException(error);yield break;}if(!more)yield break;yield return next;
            }
        }
        static IEnumerator Checks(AncestorGallery g)
        {
            int n=0;void Check(bool value,string why){if(!value)throw new Exception(why);n++;}
            Check(TreeScaling.Clamp(float.NaN)==1&&TreeScaling.Clamp(-1)==TreeScaling.Minimum&&TreeScaling.Clamp(100)==TreeScaling.Maximum,"Scale values bounded");
            Check(TreeScaling.Step(1,.19f,1)==1&&TreeScaling.Step(1,1,0)==1,"Deadzone and zero time");
            float step=1;for(int i=0;i<60;i++)step=TreeScaling.Step(step,1,1f/60);
            Check(Mathf.Abs(step-TreeScaling.Step(1,1,1))<.0001f,"Frame-rate independent proportional scaling");
            Check(Mathf.Abs(TreeScaling.Step(TreeScaling.Step(1,1,.5f),-1,.5f)-1)<.0001f,"Up/down reverse proportionally");
            var nav=UnityEngine.Object.FindAnyObjectByType<GalleryNavigation>();var prefs=nav.GetComponent<NavigationPreferences>();
            var position=nav.transform.position;var rotation=nav.transform.rotation;var look=nav.view.transform.localRotation;
            bool navEnabled=nav.enabled,open=prefs.IsOpen,tutorial=g.Tutorial.IsOpen;float scale=g.TreeScale;var angles=g.TreeRotation;string selected=g.SelectedId;
            var update=typeof(GalleryNavigation).GetMethod("Update",Hidden);Keyboard keyboard=null;Mouse mouse=null;
            try
            {
                g.AISetup.Close();g.Library.Close();g.Batch.Close();g.LocationEditor.Close();g.Tutorial.Dismiss();prefs.SetOpen(false);if(!g.IsTree)g.ShowTree();
                Check(((InputAction)typeof(GalleryNavigation).GetField("rightGrip",Hidden).GetValue(nav)).bindings.Any(b=>b.path=="<XRController>{RightHand}/gripPressed"),"Right controller grip uses new Input System binding");
                nav.enabled=false;yield return null;
                var previews=g.GetComponentsInChildren<CubePanoramaPreview>();float deadline=Time.realtimeSinceStartup+30;
                while((g.Thumbnails.PendingCount>0||previews.Any(p=>p.IsLoading))&&Time.realtimeSinceStartup<deadline)yield return null;
                var root=previews[0].transform.parent;var pivot=root.TransformPoint(g.Layout.Bounds.center);var cameraPosition=nav.view.transform.position;var cameraRotation=nav.view.transform.rotation;
                int objects=g.GetComponentsInChildren<Transform>(true).Length;long reads=g.Thumbnails.DiskReads;int pools=previews.Sum(p=>p.GetComponent<CubeFacingControls>().PoolBuilds);
                var knownObjects=new System.Collections.Generic.HashSet<Transform>(g.GetComponentsInChildren<Transform>(true));
                g.SetTreeRotation(new Vector2(42,25));pivot=root.TransformPoint(g.Layout.Bounds.center);
                foreach(float value in new[]{.05f,.25f,1f,2f,4f})
                {
                    g.SetTreeScale(value);g.SyncTreeColliders();
                    Check(Vector3.Distance(pivot,root.TransformPoint(g.Layout.Bounds.center))<.0001f,"Scaling keeps rotated pivot fixed");
                    Check(Vector3.Distance(cameraPosition,nav.view.transform.position)<.0001f&&Quaternion.Angle(cameraRotation,nav.view.transform.rotation)<.001f,"Scaling keeps viewer pose fixed");
                    foreach(var p in previews)
                    {
                        Check((g.TreeNodePosition(p.PersonId)-p.transform.position).sqrMagnitude<.0001f,"Node positions follow scale");
                        Check(g.WorldTreeBounds.Contains(p.transform.position),"Transformed bounds contain node");
                    }
                    nav.FrameNode(previews[0].PersonId);yield return new WaitForSecondsRealtime(.3f);
                    Check(previews[0].GetComponent<CubeFacingControls>().CloseOpacity>.5f,"Node framing stays outside close fade at all scales");
                    nav.transform.SetPositionAndRotation(position,rotation);nav.view.transform.localRotation=look;
                }
                int afterObjects=g.GetComponentsInChildren<Transform>(true).Length,afterPools=previews.Sum(p=>p.GetComponent<CubeFacingControls>().PoolBuilds);
                var added=g.GetComponentsInChildren<Transform>(true).Where(t=>!knownObjects.Contains(t)).ToArray();
                File.WriteAllText("Artifacts/TreeScaleHelper/measurements.txt",$"objects {objects}->{afterObjects}; pools {pools}->{afterPools}; reads {reads}->{g.Thumbnails.DiskReads}\n"+string.Join("\n",added.Select(t=>t.name+" · "+string.Join(",",t.GetComponents<Component>().Select(c=>c.GetType().Name)))));
                Check(added.All(t=>t.GetComponent<TMPro.TMP_SubMesh>()!=null)&&g.Thumbnails.DiskReads==reads&&afterPools==pools,"First exposure may initialize only TMP font submeshes; tree/pools/previews stay intact. See measurements.");
                foreach(float value in new[]{.05f,.25f,1f,2f,4f})
                {g.SetTreeScale(value);nav.FrameNode(previews[0].PersonId);yield return new WaitForSecondsRealtime(.3f);nav.transform.SetPositionAndRotation(position,rotation);nav.view.transform.localRotation=look;}
                var allAdded=g.GetComponentsInChildren<Transform>(true).Where(t=>!knownObjects.Contains(t)).ToArray();
                Check(allAdded.All(t=>t.GetComponent<TMPro.TMP_SubMesh>()!=null)&&g.GetComponentsInChildren<CubePanoramaPreview>().SequenceEqual(previews)&&g.Thumbnails.DiskReads==reads&&previews.Sum(p=>p.GetComponent<CubeFacingControls>().PoolBuilds)==pools,"Repeated scaling retains graph and pools; only first-use TMP font meshes may initialize, with no panorama reads");
                g.SetTreeScale(1);Check(nav.ScaleTreeFromStick(1,.2f)&&g.TreeScale>1,"Right-stick scale operation enlarges");
                g.Tutorial.Open();float blocked=g.TreeScale;Check(!nav.ScaleTreeFromStick(1,1)&&g.TreeScale==blocked,"Tutorial captures scale input");g.Tutorial.Dismiss();
                g.Select(previews[0].PersonId);Check(!nav.ScaleTreeFromStick(1,1)&&g.TreeScale==blocked,"Scene mode does not scale");g.ShowTree();Check(g.TreeScale==blocked,"Scene return retains tree scale");
                g.ShowTree();Check(g.TreeScale==blocked,"Tree rebuild retains scale");
                nav.ResetTreeView();Check(g.TreeScale==1,"Reset View restores size");
                // Actual desktop Update path, with synthetic Input System devices.
                keyboard=InputSystem.AddDevice<Keyboard>();mouse=InputSystem.AddDevice<Mouse>();
                void Keys(params Key[] keys){InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys));InputSystem.QueueStateEvent(mouse,new MouseState{position=new Vector2(Screen.width*.98f,Screen.height*.5f)});InputSystem.Update();update.Invoke(nav,null);}
                var before=nav.transform.position;Keys(Key.LeftShift,Key.UpArrow);Check(g.TreeScale>1&&nav.transform.position==before,"Shift+Up scales without vertical flight");
                Keys();float bigger=g.TreeScale;Keys(Key.RightShift,Key.DownArrow);Check(g.TreeScale<bigger&&nav.transform.position==before,"Either Shift+Down shrinks without vertical flight");
                Keys();float normal=g.TreeScale;Keys(Key.UpArrow);Check(g.TreeScale==normal&&nav.transform.position.y>before.y,"Plain Up still flies vertically");Keys();
                Check(DevelopmentAI.Enabled&&DevelopmentAI.TryConnection(out var helper)&&helper!=null&&!MetaApi.Configured,"Editor automatically selects existing private helper connection");
                Check(DevelopmentAI.Resolve()!=null,"Development helper requires no Meta key entry");
                g.AISetup.Open();g.AISetup.BeginTest(false);deadline=Time.realtimeSinceStartup+15;while(g.AISetup.Busy&&Time.realtimeSinceStartup<deadline)yield return null;
                Check(g.AISetup.Status.Contains("helper connected")&&!MetaApi.Configured&&MetaApi.ActiveRequests==0,"Authenticated helper health check succeeds without Meta API request");g.AISetup.Close();
                MetaApi.UseKey("fixture-direct-key-no-network");Check(!DevelopmentAI.TryConnection(out _)&&DevelopmentAI.Resolve()==null,"Explicit session key overrides development helper");MetaApi.ForgetKey();
                Check(DevelopmentAI.TryConnection(out _),"Forgetting direct key restores helper automatically");
                File.WriteAllText(Report,n+" scale/helper checks passed. Pivot/bounds/poses at 5–400%, frame independence, node framing, stable graph/pools/previews (TMP fallback submeshes initialize on first exposure), scene/rebuild/reset, new Input System grip binding and actual desktop Shift-arrow routing, helper health without API key, direct override and helper restoration. No paid API calls or Quest install.");
            }
            finally
            {
                if(keyboard!=null)InputSystem.RemoveDevice(keyboard);if(mouse!=null)InputSystem.RemoveDevice(mouse);MetaApi.ForgetKey();g.AISetup.Close();g.Tutorial.Dismiss();
                g.SetTreeScale(scale);g.SetTreeRotation(angles);if(selected!=null)g.Select(selected);else g.ShowTree();nav.transform.SetPositionAndRotation(position,rotation);nav.view.transform.localRotation=look;nav.enabled=navEnabled;prefs.SetOpen(open);if(tutorial)g.Tutorial.Open();
            }
        }
    }
}
