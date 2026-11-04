using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace AncestorWalk.Editor
{
    public static class DesktopMovementChecks
    {
        public static string Run()
        {
            if(!Application.isPlaying)throw new Exception("Play mode required");
            int n=0;void Check(bool ok,string why){if(!ok)throw new Exception(why);n++;}
            var nav=UnityEngine.Object.FindAnyObjectByType<GalleryNavigation>();var g=nav.gallery;
            var prefs=nav.GetComponent<NavigationPreferences>();var position=nav.transform.position;var rotation=nav.transform.rotation;var look=nav.view.transform.localRotation;
            var pitch=typeof(GalleryNavigation).GetField("pitch",BindingFlags.Instance|BindingFlags.NonPublic);float savedPitch=(float)pitch.GetValue(nav);
            var update=typeof(GalleryNavigation).GetMethod("Update",BindingFlags.Instance|BindingFlags.NonPublic);
            var toggleFrame=typeof(NavigationPreferences).GetField("lastToggleFrame",BindingFlags.Instance|BindingFlags.NonPublic);
            bool opened=prefs.IsOpen;string selected=g.SelectedId;float speed=prefs.TreePanSpeed;var mode=TreeControlTemplates.Current;
            bool seenHad=PlayerPrefs.HasKey(TutorialOverlay.SeenKey);int seen=PlayerPrefs.GetInt(TutorialOverlay.SeenKey);
            var originalKeyboard=Keyboard.current;var originalMouse=Mouse.current;Keyboard keyboard=null;Mouse mouse=null;
            Vector3 Press(params Key[] keys)
            {
                nav.transform.position=Vector3.zero;
                Canvas.ForceUpdateCanvases();toggleFrame.SetValue(prefs,-1);
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys));InputSystem.QueueStateEvent(mouse,new MouseState{position=new Vector2(Screen.width*.98f,Screen.height*.5f)});InputSystem.Update();
                update.Invoke(nav,null);return nav.transform.position;
            }
            try
            {
                if(g.Library.Busy||g.Generation.Busy)throw new Exception("Wait for idle library/generation");
                g.Library.Close();g.Tutorial.Dismiss();g.LocationEditor.Close();prefs.SetOpen(false);g.ShowTree();prefs.SetTreePanSpeed(2);
                keyboard=InputSystem.AddDevice<Keyboard>();mouse=InputSystem.AddDevice<Mouse>();
                foreach(float yaw in new[]{0f,90f,180f,270f})foreach(float tilt in new[]{-70f,0f,70f})
                {
                    nav.transform.rotation=Quaternion.Euler(0,yaw,0);nav.view.transform.localRotation=Quaternion.Euler(tilt,0,0);
                    foreach(float direction in new[]{-1f,1f})
                    {
                        nav.transform.position=Vector3.zero;nav.FlyDesktop(Vector3.forward*direction,.5f);
                        Check(Vector3.Distance(nav.transform.position,nav.view.transform.forward*direction)<.001f,"Desktop forward/back follow yaw and pitch");
                        var actual=Press(direction>0?Key.W:Key.S);
                        Check(actual.magnitude>0&&Vector3.Dot(actual.normalized,nav.view.transform.forward*direction)>.999f,"Real W/S events through Update follow the view");
                    }
                    nav.transform.position=Vector3.zero;nav.FlyDesktop(Vector3.right,.5f);
                    Check(Vector3.Distance(nav.transform.position,nav.view.transform.right)<.001f,"Desktop strafe retains view-relative direction");
                    nav.transform.position=Vector3.zero;nav.FlyDesktop(Vector3.up,.5f);
                    Check(Vector3.Distance(nav.transform.position,Vector3.up)<.001f,"Explicit height uses world Y");
                }
                Check(Press(Key.W,Key.S)==Vector3.zero,"Opposite keys cancel");
                var normal=Press(Key.W);var boosted=Press(Key.W,Key.LeftShift);
                Check(Vector3.Distance(boosted,normal*3)<.001f,"Keyboard boost remains threefold");
                nav.transform.position=Vector3.zero;nav.FlyDesktop(Vector3.one,1);
                Check(nav.transform.position.magnitude<=2.001f,"Diagonal speed remains bounded");
                TreeControlTemplates.Select(TreeControlMode.Fly);nav.transform.position=Vector3.zero;nav.FlyTreeSticks(Vector2.up,Vector2.zero,.5f);
                Check(Vector3.Distance(nav.transform.position,nav.view.transform.forward)<.001f,"Quest Fly retains full look-relative motion");
                g.Select(g.Document.People[0].Id);nav.transform.rotation=Quaternion.Euler(0,90,0);nav.view.transform.localRotation=Quaternion.Euler(60,0,0);
                var forward=Press(Key.W);var backward=Press(Key.S);
                Check(forward.x>0&&forward.y==0&&Mathf.Abs(forward.z)<.001f,"Scene W follows view yaw with floor height");
                Check(backward.x<0&&backward.y==0&&Mathf.Abs(backward.z)<.001f,"Scene S opposes view yaw with floor height");
                string result=n+" desktop movement checks passed: real Input System W/S events across yaw/pitch, view-relative tree/scene movement, strafing, height, opposite keys, boost, diagonal limit and Quest Fly regression.";
                Directory.CreateDirectory("Artifacts");File.WriteAllText("Artifacts/desktop-movement-checks.txt",result);return result;
            }
            finally
            {
                if(keyboard!=null)InputSystem.RemoveDevice(keyboard);if(mouse!=null)InputSystem.RemoveDevice(mouse);originalKeyboard?.MakeCurrent();originalMouse?.MakeCurrent();
                TreeControlTemplates.Select(mode);prefs.SetTreePanSpeed(speed);g.Tutorial.Dismiss();g.LocationEditor.Close();
                if(selected==null)g.ShowTree();else g.Select(selected);
                nav.transform.SetPositionAndRotation(position,rotation);nav.view.transform.localRotation=look;pitch.SetValue(nav,savedPitch);prefs.SetOpen(opened);
                if(seenHad)PlayerPrefs.SetInt(TutorialOverlay.SeenKey,seen);else PlayerPrefs.DeleteKey(TutorialOverlay.SeenKey);PlayerPrefs.Save();
            }
        }
    }
}
