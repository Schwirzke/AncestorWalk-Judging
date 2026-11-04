using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace AncestorWalk.Editor
{
    public static class SceneHoverTurnChecks
    {
        public static string Run()
        {
            int n=0;void Check(bool ok,string why){if(!ok)throw new Exception(why);n++;}
            var nav=UnityEngine.Object.FindAnyObjectByType<GalleryNavigation>();var g=nav.gallery;
            var position=nav.transform.position;var rotation=nav.transform.rotation;var look=nav.view.transform.localRotation;
            bool seenHad=PlayerPrefs.HasKey(TutorialOverlay.SeenKey);int seen=PlayerPrefs.GetInt(TutorialOverlay.SeenKey);
            string prefix="overlay."+g.TreeId+"."+PanoramaCache.PersonKey("@I3@")+".";bool had=PlayerPrefs.HasKey(prefix+"timeline");int expanded=PlayerPrefs.GetInt(prefix+"timeline");
            try
            {
                g.Tutorial.Dismiss();g.LocationEditor.Close();nav.GetComponent<NavigationPreferences>().SetOpen(false);g.Select("@I3@");var ui=g.Overlay;
                typeof(SceneOverlay).GetMethod("ConfigureForMode",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(ui,new object[]{true});
                if(!ui.TimelineOpen)ui.ToggleTimeline();Canvas.ForceUpdateCanvases();
                var rects=ui.GetComponentsInChildren<RectTransform>(true);
                Ray At(RectTransform r,Vector3 offset=default){var p=r.TransformPoint(offset);return new Ray(p-r.forward,r.forward);}
                foreach(string name in new[]{"Toggle timeline","Bottom timeline","Year slider"})
                {
                    var r=rects.Single(t=>t.name==name);
                    foreach(float axis in new[]{-1f,1f,.6f})
                    {
                        var ray=At(r);Check(ui.HandleInput(ray,true,false,false),"Hover captured by "+name);
                        var eye=nav.view.transform.position;float yaw=nav.transform.eulerAngles.y;
                        Check(nav.TrySceneHoverTurn(false,false,axis,.1f),"Scene hover admits turning");
                        float expected=Mathf.Sign(axis)*Mathf.Clamp01((Mathf.Abs(axis)-.2f)/.8f)*90*.1f;
                        Check(Mathf.Abs(Mathf.DeltaAngle(yaw,nav.transform.eulerAngles.y)-expected)<.001f,"Exactly one proportional turn");
                        Check(Vector3.Distance(eye,nav.view.transform.position)<.001f,"Turning retains camera pivot");
                    }
                }
                var rail=rects.Single(t=>t.name=="Year slider");var thumb=rects.Single(t=>t.name=="Selected year");
                var dragRay=At(rail,new Vector3(thumb.localPosition.x,0,0));ui.HandleInput(dragRay,true,true,true);
                Check(ui.IsDraggingYear,"Timeline drag is reported");float before=nav.transform.eulerAngles.y;
                Check(!nav.TrySceneHoverTurn(false,true,1,.1f)&&nav.transform.eulerAngles.y==before,"Held drag blocks hover turning");
                Check(!nav.TrySceneHoverTurn(false,false,1,.1f),"Release frame remains captured before UI processes release");
                ui.HandleInput(dragRay,true,false,false);Check(!ui.IsDraggingYear&&nav.TrySceneHoverTurn(false,false,1,.1f),"Turning resumes after release");
                Check(!nav.TrySceneHoverTurn(true,true,1,.1f),"Button press does not turn before click dispatch");
                g.Tutorial.Open();Check(!nav.TrySceneHoverTurn(false,false,1,.1f),"Tutorial remains modal");g.Tutorial.Dismiss();
                g.EditLocation("@I3@");Check(!nav.TrySceneHoverTurn(false,false,1,.1f),"Location editing remains modal");g.LocationEditor.Close();
                float resting=nav.transform.eulerAngles.y;nav.TrySceneHoverTurn(false,false,.1f,1);Check(nav.transform.eulerAngles.y==resting,"Stick dead zone preserved");
                g.ShowTree();Check(!nav.TrySceneHoverTurn(false,false,1,.1f),"Tree control routing unchanged");
                string result=n+" scene hover/turn checks passed; simulated VR UI, no API calls or Quest deployment.";System.IO.File.WriteAllText("Artifacts/scene-hover-turn-checks.txt",result);return result;
            }
            finally
            {
                g.LocationEditor.Close();g.Tutorial.Dismiss();g.ShowTree();nav.transform.SetPositionAndRotation(position,rotation);nav.view.transform.localRotation=look;
                if(had)PlayerPrefs.SetInt(prefix+"timeline",expanded);else PlayerPrefs.DeleteKey(prefix+"timeline");
                if(seenHad)PlayerPrefs.SetInt(TutorialOverlay.SeenKey,seen);else PlayerPrefs.DeleteKey(TutorialOverlay.SeenKey);PlayerPrefs.Save();
            }
        }
    }
}
