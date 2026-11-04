using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
namespace AncestorWalk.Editor
{
    public static class TutorialChecks
    {
        public static string Run()
        {
            if(!Application.isPlaying)throw new Exception("Play mode required");
            var gallery=UnityEngine.Object.FindAnyObjectByType<AncestorGallery>();var nav=UnityEngine.Object.FindAnyObjectByType<GalleryNavigation>();
            int n=0;void Check(bool ok,string why){if(!ok)throw new Exception(why);n++;}
            bool had=PlayerPrefs.HasKey(TutorialOverlay.SeenKey);int saved=PlayerPrefs.GetInt(TutorialOverlay.SeenKey);
            bool open=gallery.Tutorial.IsOpen,hudVisible=gallery.HudVisible;var controlMode=TreeControlTemplates.Current;TutorialOverlay fixture=null;
            TutorialOverlay Build(){var o=new GameObject("Tutorial fixture",typeof(RectTransform));fixture=o.AddComponent<TutorialOverlay>();fixture.Build(gallery);Canvas.ForceUpdateCanvases();return fixture;}
            void Dispose(){if(fixture!=null)UnityEngine.Object.DestroyImmediate(fixture.gameObject);fixture=null;}
            Ray At(TutorialOverlay t,string name){var rect=t.GetComponentsInChildren<RectTransform>(true).Single(r=>r.name==name);return new Ray(rect.position-rect.forward,rect.forward);}
            try
            {
                PlayerPrefs.DeleteKey(TutorialOverlay.SeenKey);var t=Build();Check(t.IsOpen&&t.CurrentPage==0,"New user starts at welcome");
                var configure=typeof(TutorialOverlay).GetMethod("Configure",BindingFlags.NonPublic|BindingFlags.Instance);
                Check(t.PageCount==17,"Guide covers current tree, scene and AI features");
                foreach(bool vr in new[]{false,true})foreach(TreeControlMode mode in Enum.GetValues(typeof(TreeControlMode)))
                {
                    TreeControlTemplates.Select(mode);configure.Invoke(t,new object[]{vr});t.Open();
                    for(int i=0;i<t.PageCount;i++)
                    {
                        // Refresh after changing the control template, even when presentation mode is unchanged.
                        typeof(TutorialOverlay).GetMethod("Refresh",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(t,null);
                        foreach(var text in t.GetComponentsInChildren<Text>().Where(x=>x.gameObject.name=="Tutorial text"))
                            Check(text.preferredHeight<=text.rectTransform.rect.height+1,"Tutorial copy fits "+mode+" / "+(vr?"Quest":"desktop")+": "+text.text);
                        t.Advance();
                    }
                }
                TreeControlTemplates.Select(controlMode);configure.Invoke(t,new object[]{false});t.Open();
                for(int page=0;page<t.PageCount;page++)
                {
                    Check(t.IsOpen&&t.CurrentPage==page,"Step advances in order");
                    foreach(var text in t.GetComponentsInChildren<Text>().Where(x=>x.gameObject.name=="Tutorial text"))
                        Check(text.preferredHeight<=text.rectTransform.rect.height+1,"Tutorial copy fits its box: "+text.text);
                    if(page==1){t.HandleInput(At(t,"Tutorial previous"),true,true,false);Check(t.CurrentPage==0,"Previous hit target works");t.Advance();}
                    t.HandleInput(At(t,"Tutorial next"),true,true,false);
                }
                Check(!t.IsOpen&&PlayerPrefs.GetInt(TutorialOverlay.SeenKey)==1,"Completion persists dismissal");Dispose();t=Build();Check(!t.IsOpen,"Returning user is not interrupted");
                t.HandleInput(At(t,"Tutorial help"),true,true,false);Check(t.IsOpen&&t.CurrentPage==0,"Help replays from first step");
                t.HandleInput(At(t,"Tutorial skip"),true,true,false);Check(!t.IsOpen,"Skip closes tutorial");
                t.Open();Check(t.HandleInput(default,true,false,true)&&!t.IsOpen,"B/Escape closes tutorial");
                t.Open();Check(t.HandleInput(new Ray(Vector3.one*1000,Vector3.up),true,true,false)&&t.CurrentPage==0,"Outside press is consumed without advancing");Dispose();
                gallery.ShowTree();gallery.Tutorial.Open();var position=nav.transform.position;var rotation=nav.transform.rotation;
                nav.FlyTreeSticks(Vector2.one,Vector2.one,1);nav.Fly(Vector3.one,1);nav.SmoothTurn(1,1);nav.FocusNearestNode();nav.StepNode(1);
                Check(nav.transform.position==position&&Quaternion.Angle(nav.transform.rotation,rotation)<.001f,"Tutorial blocks flight, turning and node shortcuts");
                var node=gallery.GetComponentsInChildren<TreeNodeTarget>().First();
                Check(!nav.HandleTreeHit(node.GetComponent<Collider>(),true)&&nav.PointedPersonId==null&&gallery.IsTree,"Tutorial blocks cube selection/hover");
                gallery.Tutorial.Dismiss();nav.FrameNode(node.PersonId);Check(nav.FocusedPersonId==node.PersonId,"Navigation resumes after dismissal");
                gallery.Select(node.PersonId);gallery.Tutorial.Open();Check(gallery.Tutorial.IsOpen&&!gallery.IsTree,"Help works inside historical scene");
                gallery.Tutorial.HandleInput(default,true,false,true);Check(!gallery.Tutorial.IsOpen&&!gallery.IsTree,"Close tutorial does not return from scene");
                gallery.Tutorial.Open();
                configure.Invoke(gallery.Tutorial,new object[]{true});
                Check(gallery.Tutorial.GetComponent<Canvas>().renderMode==RenderMode.WorldSpace&&gallery.Tutorial.transform.parent==Camera.main.transform,"Quest overlay follows camera");
                Check(Vector3.Distance(gallery.Tutorial.transform.localPosition,new Vector3(0,0,1.6f))<.001f,"Quest panel at comfortable fixed distance");
                Check(gallery.Tutorial.GetComponentsInChildren<Text>().Any(x=>x.text.Contains("right trigger")),"Quest instructions selected");
                configure.Invoke(gallery.Tutorial,new object[]{false});Check(gallery.Tutorial.GetComponent<Canvas>().renderMode==RenderMode.ScreenSpaceOverlay,"Desktop screen overlay restored");
                Check(gallery.Tutorial.GetComponentsInChildren<Text>().Any(x=>x.text.Contains("left click")),"Desktop instructions selected");
                gallery.Tutorial.Dismiss();gallery.EditLocation(node.PersonId);
                Check(!gallery.Tutorial.HandleInput(At(gallery.Tutorial,"Tutorial help"),true,true,false)&&!gallery.Tutorial.IsOpen,"Location editor has priority over help");gallery.LocationEditor.Close();
                gallery.ShowTree();gallery.Hud.SetVisible(true);
                var home=gallery.Hud.GetComponentsInChildren<RectTransform>(true).Single(r=>r.name=="Home");
                var hudUpdate=typeof(HudOverlay).GetMethod("Update",BindingFlags.NonPublic|BindingFlags.Instance);
                var hudConfigure=typeof(HudOverlay).GetMethod("Configure",BindingFlags.NonPublic|BindingFlags.Instance);
                var pose=new Vector3(3,2,-4);nav.transform.SetPositionAndRotation(pose,Quaternion.Euler(0,55,0));
                gallery.SetTreeRotation(new Vector2(40,15));gallery.SetTreeScale(.7f);
                var nodes=gallery.GetComponentsInChildren<TreeNodeTarget>();string nodeId=node.PersonId;
                hudUpdate.Invoke(gallery.Hud,null);Canvas.ForceUpdateCanvases();
                Ray HomeRay()=>new Ray(home.position-home.forward,home.forward);
                Check(gallery.Hud.HandleInput(HomeRay(),true,true,false,false),"Home button captures pointer selection");
                Check(gallery.IsTree&&nav.FocusedPersonId==nav.HomeNode()&&Vector3.Distance(nav.view.transform.position,gallery.TreeNodePosition(nav.HomeNode())+new Vector3(0,.35f,-Mathf.Max(2.2f,TreeLayout.Size*.5f+NodeNearFade.Distance+.25f)))<.001f,"Home frames the first visible child, accounting for camera height");
                Check(gallery.TreeScale==1&&gallery.TreeRotation==Vector2.zero&&gallery.TreePan==Vector2.zero,"Home restores tree size, rotation and pan");
                Check(nodes.SequenceEqual(gallery.GetComponentsInChildren<TreeNodeTarget>()),"Home in tree view reuses existing nodes");
                nav.transform.position=pose;gallery.Select(nodeId);nav.Return();Check(nav.transform.position==pose,"Back preserves the saved tree view");
                gallery.Select(nodeId);hudUpdate.Invoke(gallery.Hud,null);Canvas.ForceUpdateCanvases();
                var scenePose=nav.transform.position;Check(!UIFade.Interactive(home),"Home hides inside a scene");nav.GoHome();Check(!gallery.IsTree&&nav.transform.position==scenePose,"Home cannot jump out of a scene");nav.Return();hudUpdate.Invoke(gallery.Hud,null);Check(UIFade.Interactive(home),"Home reappears in tree view");
                nav.transform.position=pose;gallery.Tutorial.Open();nav.GoHome();Check(nav.transform.position==pose,"Home respects tutorial capture");gallery.Tutorial.Dismiss();
                foreach(bool vr in new[]{true,false})
                {
                    hudConfigure.Invoke(gallery.Hud,new object[]{vr});Canvas.ForceUpdateCanvases();
                    var toggle=gallery.Hud.GetComponentsInChildren<RectTransform>(true).Single(r=>r.name=="Toggle HUD");
                    var room=gallery.Hud.GetComponentsInChildren<RectTransform>(true).Single(r=>r.name=="Toggle passthrough");
                    Check(home.anchoredPosition.x-home.rect.width/2>toggle.anchoredPosition.x+toggle.rect.width/2,"Home clears HUD toggle");
                    Check(!vr||home.anchoredPosition.x-home.rect.width/2>room.anchoredPosition.x+room.rect.width/2,"Quest Home clears Room toggle");
                }
                gallery.Hud.SetVisible(false);hudUpdate.Invoke(gallery.Hud,null);Check(!UIFade.Interactive(home),"HUD hiding disables Home hit target");
                Check(!gallery.Generation.Busy,"Tutorial issues no generation requests");
                var result=n+" tutorial checks passed; no paid generation or Quest deployment.";System.IO.File.WriteAllText("Artifacts/tutorial-checks.txt",result);return result;
            }
            finally
            {
                Dispose();gallery.LocationEditor.Close();gallery.ShowTree();nav.ResetTreeView();
                if(open)gallery.Tutorial.Open();else gallery.Tutorial.Dismiss();
                gallery.Hud.SetVisible(hudVisible);TreeControlTemplates.Select(controlMode);
                if(had)PlayerPrefs.SetInt(TutorialOverlay.SeenKey,saved);else PlayerPrefs.DeleteKey(TutorialOverlay.SeenKey);PlayerPrefs.Save();
            }
        }
    }
}
