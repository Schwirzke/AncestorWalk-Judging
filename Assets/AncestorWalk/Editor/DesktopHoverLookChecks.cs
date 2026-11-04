using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace AncestorWalk.Editor
{
    public static class DesktopHoverLookChecks
    {
        const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        public static string Run()
        {
            if(!Application.isPlaying)throw new Exception("Play mode required");
            int checks=0;void Check(bool ok,string why){if(!ok)throw new Exception(why);checks++;}
            var nav=UnityEngine.Object.FindAnyObjectByType<GalleryNavigation>();var g=nav.gallery;
            var prefs=nav.GetComponent<NavigationPreferences>();var position=nav.transform.position;var rotation=nav.transform.rotation;var look=nav.view.transform.localRotation;
            var pitch=typeof(GalleryNavigation).GetField("pitch",Private);float savedPitch=(float)pitch.GetValue(nav);
            var update=typeof(GalleryNavigation).GetMethod("Update",Private);
            var capture=typeof(NavigationPreferences).GetField("pointerCaptured",Private);
            var toggleFrame=typeof(NavigationPreferences).GetField("lastToggleFrame",Private);
            bool opened=prefs.IsOpen;string selected=g.SelectedId;
            bool seenHad=PlayerPrefs.HasKey(TutorialOverlay.SeenKey);int seen=PlayerPrefs.GetInt(TutorialOverlay.SeenKey);
            string prefix="overlay."+g.TreeId+"."+PanoramaCache.PersonKey("@I3@")+".";bool timelineHad=PlayerPrefs.HasKey(prefix+"timeline");int timelineSaved=PlayerPrefs.GetInt(prefix+"timeline");
            var originalMouse=Mouse.current;var originalKeyboard=Keyboard.current;
            Mouse mouse=null;Keyboard keyboard=null;
            void Frame(Vector2 at,bool shouldTurn)
            {
                toggleFrame.SetValue(prefs,-1);
                InputSystem.QueueStateEvent(mouse,new MouseState{position=at,delta=new Vector2(7,-2)}.WithButton(MouseButton.Right));InputSystem.Update();
                float yaw=nav.transform.eulerAngles.y;update.Invoke(nav,null);
                float expected=shouldTurn?7*prefs.LookSensitivity:0;
                Check(Mathf.Abs(Mathf.DeltaAngle(yaw,nav.transform.eulerAngles.y)-expected)<.002f,"Update applies mouse yaw exactly once: "+at+" expected "+expected);
            }
            Vector2 At(Component owner,string name)=>RectTransformUtility.WorldToScreenPoint(null,owner.GetComponentsInChildren<RectTransform>(true).Single(t=>t.name==name).position);
            try
            {
                mouse=InputSystem.AddDevice<Mouse>();keyboard=InputSystem.AddDevice<Keyboard>();
                g.Tutorial.Dismiss();g.LocationEditor.Close();prefs.SetOpen(false);g.Select("@I3@");var ui=g.Overlay;
                if(!ui.TimelineOpen)ui.ToggleTimeline();Canvas.ForceUpdateCanvases();
                foreach(string name in new[]{"Toggle timeline","Bottom timeline","Year slider"})
                {
                    var at=At(ui,name);Frame(at,true);Check(ui.HandleInput(default,false,false,false),"UI still captures hover on "+name);
                }
                var help=At(g.Tutorial,"Tutorial help");Frame(help,true);Check(g.Tutorial.HandleInput(default,false,false,false),"Help hover still captured");
                prefs.SetOpen(true);Canvas.ForceUpdateCanvases();Frame(At(prefs,"Sphere spin speed"),true);
                Check(prefs.IsOpen&&!prefs.CapturingPointer,"Open preferences hover does not capture look");
                capture.SetValue(prefs,true);Frame(At(prefs,"Sphere spin speed"),false);Check(!prefs.CapturingPointer,"Preference drag release processed without look");prefs.SetOpen(false);
                var overlayDrag=typeof(SceneOverlay).GetField("dragging",Private);overlayDrag.SetValue(ui,true);
                Frame(At(ui,"Year slider"),false);Check(!ui.IsDraggingYear,"Scene slider release remains captured before UI releases it");
                Frame(At(ui,"Year slider"),true);
                Check(!nav.TryDesktopLook(true,true,true,Vector2.one),"Press/hold blocks look");
                Check(!nav.TryDesktopLook(false,false,false,Vector2.one),"Right mouse required");
                g.Tutorial.Open();Frame(new Vector2(Screen.width*.5f,Screen.height*.5f),false);g.Tutorial.Dismiss();
                g.EditLocation("@I3@");Frame(Vector2.zero,false);g.LocationEditor.Close();
                nav.TryDesktopLook(true,false,false,new Vector2(0,10000));Check(Mathf.Abs(nav.view.transform.localEulerAngles.x-295)<.01f,"Scene pitch lower clamp preserved");
                nav.TryDesktopLook(true,false,false,new Vector2(0,-10000));Check(Mathf.Abs(nav.view.transform.localEulerAngles.x-65)<.01f,"Scene pitch upper clamp preserved");
                g.ShowTree();Frame(At(g.Tutorial,"Tutorial help"),true);
                Frame(At(g.TreeControls,"Toggle flight controls"),true);
                prefs.SetOpen(true);Frame(At(prefs,"Sphere spin speed"),true);prefs.SetOpen(false);
                nav.TryDesktopLook(true,false,false,new Vector2(0,-10000));Check(Mathf.Abs(nav.view.transform.localEulerAngles.x-89)<.01f,"Tree pitch clamp preserved");
                string result=checks+" desktop hover/look checks passed; real Input System mouse events through navigation Update, no API calls or Quest deployment.";
                System.IO.File.WriteAllText("Artifacts/desktop-hover-look-checks.txt",result);return result;
            }
            finally
            {
                if(mouse!=null)InputSystem.RemoveDevice(mouse);if(keyboard!=null)InputSystem.RemoveDevice(keyboard);
                originalMouse?.MakeCurrent();originalKeyboard?.MakeCurrent();
                capture.SetValue(prefs,false);g.LocationEditor.Close();g.Tutorial.Dismiss();
                if(selected==null)g.ShowTree();else g.Select(selected);
                nav.transform.SetPositionAndRotation(position,rotation);nav.view.transform.localRotation=look;pitch.SetValue(nav,savedPitch);prefs.SetOpen(opened);
                if(timelineHad)PlayerPrefs.SetInt(prefix+"timeline",timelineSaved);else PlayerPrefs.DeleteKey(prefix+"timeline");
                if(seenHad)PlayerPrefs.SetInt(TutorialOverlay.SeenKey,seen);else PlayerPrefs.DeleteKey(TutorialOverlay.SeenKey);PlayerPrefs.Save();
            }
        }
    }
}
