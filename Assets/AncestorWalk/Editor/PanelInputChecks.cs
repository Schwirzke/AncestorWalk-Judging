using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace AncestorWalk.Editor
{
    public static class PanelInputChecks
    {
        const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        public static string Run()
        {
            var g=UnityEngine.Object.FindAnyObjectByType<AncestorGallery>();
            var nav=UnityEngine.Object.FindAnyObjectByType<GalleryNavigation>();
            if(!Application.isPlaying||g?.Tree==null||!g.CacheIndexReady||g.Generation.Busy)
                throw new Exception("Ready, idle Play mode required.");
            int checks=0;void Check(bool value,string why){if(!value)throw new Exception(why);checks++;}
            var selected=g.SelectedId;var angles=g.TreeRotation;var position=nav.transform.position;
            var rotation=nav.transform.rotation;var look=nav.view.transform.localRotation;
            var pitch=typeof(GalleryNavigation).GetField("pitch",Hidden);var oldPitch=(float)pitch.GetValue(nav);
            var capture=typeof(GalleryNavigation).GetField("dragging",Hidden);
            var advance=typeof(CubeFacingControls).GetMethod("AdvanceFacing",Hidden);
            var update=typeof(GalleryNavigation).GetMethod("Update",Hidden);
            var prefs=nav.GetComponent<NavigationPreferences>();bool prefOpen=prefs.IsOpen,tutorial=g.Tutorial.IsOpen;
            bool hud=g.HudVisible;var oldMouse=Mouse.current;Mouse mouse=null;GameObject behind=null;
            string personId=null;int oldYear=0;
            void Step(CubeFacingControls controls,float seconds=.4f){advance.Invoke(controls,new object[]{seconds});Physics.SyncTransforms();}
            void Eye(Vector3 eye){nav.transform.position+=eye-nav.view.transform.position;}
            Collider Hit(Transform face,Vector3 local)
            {
                var point=face.TransformPoint(local);var ray=new Ray(point-face.forward*.35f,face.forward);
                Check(Physics.Raycast(ray,out var hit,.7f),"Ray reaches panel surface");return hit.collider;
            }
            try
            {
                g.Library.Close();g.Batch.Close();g.Tutorial.Dismiss();prefs.SetOpen(false);g.Hud.SetVisible(false);g.ShowTree();
                var control=g.GetComponentsInChildren<CubeFacingControls>(true).First(c=>c.gameObject.activeInHierarchy&&c.Panel.GetComponentInChildren<YearSlider>(true)!=null);
                var slider=control.Panel.GetComponentInChildren<YearSlider>(true);
                personId=slider.GetComponentInParent<TimelinePanel>(true).PersonId;oldYear=g.Year(personId);
                control.SetInteractionRange(12);
                var backing=control.Panel.Find("Panel backing").GetComponent<BoxCollider>();
                Check(backing!=null,"Live panel retains a full background collider");
                foreach(var pose in new[]{Vector2.zero,new Vector2(135,55)})
                {
                    g.SetTreeRotation(pose);
                    foreach(var side in new[]{Vector3.back,Vector3.right,Vector3.forward,Vector3.left})
                    {
                        Eye(control.transform.TransformPoint(side*10+Vector3.up*3));Step(control);
                        foreach(var point in new[]{new Vector3(0,.7f,.08f),new Vector3(-1.7f,.9f,.08f),new Vector3(1.7f,-.95f,.08f),new Vector3(0,.25f,.08f)})
                            Check(Hit(control.Panel,point)==backing,"Names, dates, blank space and panel corners shield background targets on every rotated face");
                        var thumb=slider.ThumbCollider.transform;
                        var ray=new Ray(thumb.position-control.Panel.forward*.4f,control.Panel.forward);
                        Check(nav.TryBeginYearDrag(ray),"Live thumb remains in front of the panel blocker");
                        Check(g.Year(personId)==oldYear,"Thumb grab does not change year");
                        slider.EndDrag();capture.SetValue(nav,null);
                    }
                }
                g.SetTreeRotation(Vector2.zero);
                Eye(control.transform.TransformPoint(Vector3.back*10+Vector3.up*3));Step(control);
                // An actual desktop click has an otherwise selectable sphere/scene behind the name.
                var pointOnName=control.Panel.TransformPoint(new Vector3(0,.7f,.08f));
                nav.transform.rotation=control.Panel.rotation;nav.view.transform.localRotation=Quaternion.identity;pitch.SetValue(nav,0f);
                Eye(pointOnName-control.Panel.forward*.4f);Step(control);
                behind=GameObject.CreatePrimitive(PrimitiveType.Cube);behind.name="Panel input fixture";
                behind.transform.position=pointOnName+control.Panel.forward*.25f;behind.transform.localScale=Vector3.one*.12f;
                behind.GetComponent<Renderer>().enabled=false;
                var scene=behind.AddComponent<GalleryTarget>();scene.personId=personId;
                var node=behind.AddComponent<TreeNodeTarget>();node.PersonId=personId;node.Controls=control;
                mouse=InputSystem.AddDevice<Mouse>();Physics.SyncTransforms();
                void Click()
                {
                    var screen=nav.view.WorldToScreenPoint(pointOnName);
                    InputSystem.QueueStateEvent(mouse,new MouseState{position=screen});InputSystem.Update();
                    InputSystem.QueueStateEvent(mouse,new MouseState{position=screen}.WithButton(MouseButton.Left));InputSystem.Update();
                    update.Invoke(nav,null);
                    InputSystem.QueueStateEvent(mouse,new MouseState{position=screen});InputSystem.Update();
                }
                var eye=nav.view.transform.position;Click();
                Check(g.IsTree&&nav.view.transform.position==eye,"Desktop name click cannot teleport to a sphere behind the panel");
                Check(nav.PointedPersonId==null,"Panel hover clears sphere/portal pointing");
                UnityEngine.Object.DestroyImmediate(node);Click();
                Check(g.IsTree&&g.SelectedId==null,"Desktop name click cannot enter a scene behind the panel");
                UnityEngine.Object.DestroyImmediate(behind);behind=null;
                Eye(control.transform.TransformPoint(Vector3.right*10+Vector3.up*3));Step(control,.05f);
                var outgoing=control.transform.Find("Pooled panel face 0");var shield=outgoing.GetComponent<BoxCollider>();
                Check(outgoing.gameObject.activeSelf&&shield.enabled,"Outgoing face shields targets during its fade");
                Check(Hit(outgoing,new Vector3(0,.7f,.08f))==shield,"Quest-style ray hits the fading panel instead of background geometry");
                Check(YearSlider.Resolve(shield)==null&&!shield.GetComponent<GalleryTarget>(),"Fade shield is not an actionable control");
                Step(control);Check(!outgoing.gameObject.activeSelf&&!shield.enabled,"Completed side fade releases its shield");
                Eye(control.transform.position+Vector3.back*(control.InteractionRange+3));Step(control);
                Check(!backing.enabled&&!control.Panel.gameObject.activeSelf,"Fully hidden panel cannot intercept rays");
                foreach(Transform child in control.transform)if(child.name.StartsWith("Pooled panel face"))
                    Check(!child.gameObject.activeSelf&&!child.GetComponent<BoxCollider>().enabled,"Hidden pooled faces cannot intercept rays");
                Eye(control.transform.TransformPoint(Vector3.back*10+Vector3.up*3));Step(control);
                Check(backing.enabled&&slider.RailCollider.enabled&&slider.ThumbCollider.enabled,"Returning restores panel shield and slider input");
                int objects=UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length;
                var tick=(Action<float>)advance.CreateDelegate(typeof(Action<float>),control);tick(.016f);
                long before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<1000;i++)tick(.016f);
                Check(GC.GetAllocatedBytesForCurrentThread()==before,"Warmed panel shield updates allocate zero managed bytes");
                Check(UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length==objects,"Held panel updates create no panel objects");
                string result=checks+" panel input checks passed: all sides, rotated tree, text/corners, live thumb capture, actual desktop click-through repro, fading and hidden faces, Quest-style rays and pooled object stability.";
                Directory.CreateDirectory("Artifacts");File.WriteAllText("Artifacts/panel-input-checks.txt",result);return result;
            }
            finally
            {
                capture.SetValue(nav,null);nav.ClearTreePointing();if(behind!=null)UnityEngine.Object.DestroyImmediate(behind);
                if(mouse!=null)InputSystem.RemoveDevice(mouse);if(oldMouse!=null&&oldMouse.added)oldMouse.MakeCurrent();
                if(personId!=null)g.SetYear(personId,oldYear);g.SetTreeRotation(angles);
                if(selected==null)g.ShowTree();else g.Select(selected);
                nav.transform.SetPositionAndRotation(position,rotation);nav.view.transform.localRotation=look;pitch.SetValue(nav,oldPitch);
                prefs.SetOpen(prefOpen);g.Hud.SetVisible(hud);if(tutorial)g.Tutorial.Open();
            }
        }
    }
}
