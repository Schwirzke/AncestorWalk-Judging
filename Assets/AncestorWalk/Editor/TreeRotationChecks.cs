using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

namespace AncestorWalk.Editor
{
    public static class TreeRotationChecks
    {
        const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        const string Report="Artifacts/tree-rotation-play-checks.txt";
        public static string Data()
        {
            int n=0;void Check(bool ok,string why){if(!ok)throw new Exception(why);n++;}
            var bounds=new Bounds(new Vector3(12,4,18),new Vector3(40,6,20));var pan=new Vector2(3,-2);
            foreach(float yaw in new[]{-720f,-15,0,90,180,359,725})foreach(float tilt in new[]{-100f,-85,0,45,85,120})
            {
                var angles=TreeRotationPose.Normalize(new Vector2(yaw,tilt));var q=TreeRotationPose.Orientation(angles);
                Check(angles.x>=0&&angles.x<360&&Mathf.Abs(angles.y)<=85,"Yaw wraps and tilt clamps");
                var matrix=Matrix4x4.TRS(TreeRotationPose.Position(bounds.center,pan,q),q,Vector3.one);
                Check(Vector3.Distance(matrix.MultiplyPoint3x4(bounds.center),bounds.center+new Vector3(3,0,-2))<.001f,"Rotation holds the calculated pivot plus pan");
                var world=TreeRotationPose.WorldBounds(bounds,matrix);
                for(int i=0;i<8;i++)
                {
                    var corner=bounds.center+Vector3.Scale(bounds.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
                    Check(Vector3.Distance(world.ClosestPoint(matrix.MultiplyPoint3x4(corner)),matrix.MultiplyPoint3x4(corner))<.001f,"Transformed bounds include every corner");
                }
            }
            Check(TreeRotationPose.Normalize(new Vector2(float.NaN,float.PositiveInfinity))==Vector2.zero,"Invalid angle input remains finite");
            var point=new Bounds(new Vector3(1,2,3),Vector3.zero);var rotation=TreeRotationPose.Orientation(new Vector2(45,80));
            var pose=Matrix4x4.TRS(TreeRotationPose.Position(point.center,Vector2.zero,rotation),rotation,Vector3.one);
            Check(Vector3.Distance(TreeRotationPose.WorldBounds(point,pose).center,point.center)<.001f,"Single-point pivot remains fixed");
            Directory.CreateDirectory("Artifacts");string result=n+" rotation pose/bounds checks passed.";File.WriteAllText("Artifacts/tree-rotation-data-checks.txt",result);return result;
        }
        public static string Play()
        {
            var g=UnityEngine.Object.FindAnyObjectByType<AncestorGallery>();if(!Application.isPlaying||g?.Tree==null)throw new Exception("Ready Play mode required");
            Directory.CreateDirectory("Artifacts");File.WriteAllText(Report,"RUNNING");g.StartCoroutine(Run(Checks(g)));return "Running whole-tree rotation checks.";
        }
        static IEnumerator Run(IEnumerator checks)
        {
            int errors=0;void Log(string message,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors++;}
            Application.logMessageReceived+=Log;
            try
            {
                while(true)
                {
                    bool more=false;object next=null;Exception failure=null;try{more=checks.MoveNext();if(more)next=checks.Current;}catch(Exception e){failure=e;}
                    if(failure!=null){File.WriteAllText(Report,"FAILED: "+failure);Debug.LogException(failure);yield break;}
                    if(!more){if(errors>0)File.WriteAllText(Report,"FAILED: "+errors+" runtime errors");else File.AppendAllText(Report," Zero runtime errors.");yield break;}
                    yield return next;
                }
            }
            finally{Application.logMessageReceived-=Log;(checks as IDisposable)?.Dispose();}
        }
        static TreeDocument Fixture(int people)
        {
            var doc=new TreeDocument{Id=TreeStore.NewId(),Name="Rotation fixture"};
            for(int i=0;i<people;i++)doc.People.Add(new Person{Id="rotation-"+i,Name="Person "+i,BirthDate="1900",DeathDate="1980"});
            if(people>=3)doc.Families.Add(new FamilyRecord{Id="family",Parents=new(){"rotation-1","rotation-2"},Children=new(){"rotation-0"}});
            return doc;
        }
        static IEnumerator Checks(AncestorGallery g)
        {
            int n=0;void Check(bool ok,string why){if(!ok)throw new Exception(why);n++;}
            var nav=UnityEngine.Object.FindAnyObjectByType<GalleryNavigation>();var prefs=nav.GetComponent<NavigationPreferences>();
            while(PlayReloadRecovery.IsRecovering||g.Library.Busy||!g.CacheIndexReady)yield return null;
            var original=g.Document;var angles=g.TreeRotation;var pan=g.TreePan;var position=nav.transform.position;var rotation=nav.transform.rotation;var look=nav.view.transform.localRotation;
            var pitch=typeof(GalleryNavigation).GetField("pitch",Hidden);float oldPitch=(float)pitch.GetValue(nav);var layoutSettings=TreeLayoutSettings.Load();
            bool hud=g.HudVisible,expanded=g.TreeControls.IsExpanded,tab=g.TreeControls.RotationTab,preferencesOpen=prefs.IsOpen;
            bool hadTab=PlayerPrefs.HasKey(TreeNavigationOverlay.RotationTabKey);int oldTab=PlayerPrefs.GetInt(TreeNavigationOverlay.RotationTabKey);
            bool hadTutorial=PlayerPrefs.HasKey(TutorialOverlay.SeenKey);int oldTutorial=PlayerPrefs.GetInt(TutorialOverlay.SeenKey);
            var oldKeyboard=Keyboard.current;var oldMouse=Mouse.current;Keyboard keyboard=null;Mouse mouse=null;
            var update=typeof(GalleryNavigation).GetMethod("Update",Hidden);var advance=typeof(CubeFacingControls).GetMethod("AdvanceFacing",Hidden);
            RectTransform Find(string name)=>g.TreeControls.GetComponentsInChildren<RectTransform>(true).Single(r=>r.name==name);
            void Mode(bool vr){typeof(TreeNavigationOverlay).GetMethod("ConfigureForMode",Hidden).Invoke(g.TreeControls,new object[]{vr});Canvas.ForceUpdateCanvases();}
            bool Hit(string name,bool vr,bool pressed,bool held)
            {
                var rect=Find(name);if(!vr){InputSystem.QueueStateEvent(mouse,new MouseState{position=RectTransformUtility.WorldToScreenPoint(null,rect.position)});InputSystem.Update();}
                return g.TreeControls.HandleInput(new Ray(rect.position-rect.forward*2,rect.forward),vr,pressed,held,prefs.TreePanSpeed);
            }
            void KeyFrame(params Key[] keys)
            {
                typeof(NavigationPreferences).GetField("lastToggleFrame",Hidden).SetValue(prefs,-1);
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys));InputSystem.Update();update.Invoke(nav,null);
            }
            try
            {
                g.Library.Close();g.Tutorial.Dismiss();g.LocationEditor.Close();prefs.SetOpen(false);g.Hud.SetVisible(true);
                keyboard=InputSystem.AddDevice<Keyboard>();mouse=InputSystem.AddDevice<Mouse>();
                g.OpenDocument(Fixture(7));while(!g.CacheIndexReady)yield return null;yield return null;
                Check(g.TreeRotation==Vector2.zero,"Different tree starts unrotated");
                var floor=GameObject.Find("Teleport floor").GetComponent<Collider>();Check(!floor.enabled,"Hidden floor cannot intercept tree picking");
                var pivot=g.WorldTreeBounds.center;var sky=RenderSettings.skybox;var layout=g.Layout;
                var startPosition=nav.transform.position;var startRotation=nav.transform.rotation;var startLook=nav.view.transform.localRotation;
                nav.RotateTree(Vector2.right,2);Check(Mathf.Abs(g.TreeRotation.x-60)<.001f,"Time-based 30 degrees per second");
                Check(Vector3.Distance(pivot,g.WorldTreeBounds.center)<.001f,"World pivot remains fixed");
                Check(nav.transform.position==startPosition&&nav.transform.rotation==startRotation&&nav.view.transform.localRotation==startLook&&RenderSettings.skybox==sky,"Rotation leaves viewer and background still");
                g.ResetTreeRotation();nav.RotateTree(Vector2.one,1);Check(Mathf.Abs(g.TreeRotation.magnitude-30)<.001f,"Combined rotation input normalized");
                nav.RotateTree(Vector2.up,100);Check(g.TreeRotation.y==85,"Upper tilt limit");nav.RotateTree(Vector2.down,100);Check(g.TreeRotation.y==-85,"Lower tilt limit");
                nav.RotateTree(Vector2.right,100);Check(g.TreeRotation.x>=0&&g.TreeRotation.x<360,"Turning wraps without roll");
                foreach(var requested in new[]{new Vector2(90,40),new Vector2(180,-85),new Vector2(359,85)})
                {
                    g.SetTreeRotation(requested);var q=g.TreeOrientation;
                    foreach(var pair in g.Layout.Positions)
                    {
                        Check(Vector3.Distance(g.TreeNodePosition(pair.Key),pivot+q*(pair.Value-g.Layout.Bounds.center))<.001f,"All nodes share the same pivot transform");
                        Check(Vector3.Distance(g.WorldTreeBounds.ClosestPoint(g.TreeNodePosition(pair.Key)),g.TreeNodePosition(pair.Key))<.001f,"World navigation bounds contain rotated nodes");
                    }
                    foreach(var line in g.GetComponentsInChildren<LineRenderer>().Where(l=>l.name=="Family relationship"))
                    {Check(!line.useWorldSpace,"Relationship lines use tree-local coordinates");for(int i=0;i<line.positionCount;i++)Check(Vector3.Distance(line.transform.TransformPoint(line.GetPosition(i)),pivot+q*(line.GetPosition(i)-g.Layout.Bounds.center))<.001f,"Connector follows rotated tree");}
                    var id=g.Document.People[0].Id;nav.ClearTreePointing();nav.FrameNode(id);g.SyncTreeColliders();
                    Check(Vector3.Angle(nav.view.transform.forward,q*Vector3.forward)<.01f,"Node jump faces rotated front");
                    var target=g.GetComponentsInChildren<TreeNodeTarget>().Single(t=>t.PersonId==id);
                    var ray=new Ray(nav.view.transform.position,(target.transform.position-nav.view.transform.position).normalized);
                    Check(Physics.Raycast(ray,out var hit,10)&&hit.collider.GetComponent<TreeNodeTarget>()==target,"Rotated sphere collider is pickable");
                    Check(nav.view.farClipPlane>=Vector3.Distance(nav.view.transform.position,g.WorldTreeBounds.center)+g.WorldTreeBounds.extents.magnitude,"Camera clip follows world bounds");
                    var face=target.transform.parent.GetComponent<CubeFacingControls>();face.SetPointed(true);advance.Invoke(face,new object[]{.3f});
                    Check(Vector3.Dot(face.Portal.forward,(face.Portal.position-nav.view.transform.position).normalized)>.999f,"Portal still faces viewer on rotated tree");face.SetPointed(false);
                    Physics.SyncTransforms();var slider=face.Panel.GetComponentInChildren<YearSlider>(true);int selectedYear=g.Year(id);
                    Check(nav.TryBeginYearDrag(new Ray(nav.view.transform.position,slider.ThumbCollider.transform.position-nav.view.transform.position)),"Rotated panel thumb remains draggable");
                    Check(g.Year(id)==selectedYear,"Rotated thumb grab preserves year");slider.EndDrag();typeof(GalleryNavigation).GetField("dragging",Hidden).SetValue(nav,null);
                }
                var headPose=Quaternion.Euler(-25,14,10);nav.view.transform.localRotation=headPose;
                typeof(GalleryNavigation).GetMethod("FrameNodeForMode",Hidden).Invoke(nav,new object[]{g.Document.People[0].Id,true});
                Check(nav.view.transform.localRotation==headPose,"Quest framing preserves tracked local head pitch and roll");
                Check(Vector3.Distance(nav.view.transform.position,g.TreeNodePosition(g.Document.People[0].Id)+g.TreeOrientation*new Vector3(0,.35f,-2.2f))<.001f,"Quest framing accounts for tracked camera offset");
                g.TreeControls.SetExpanded(true);g.TreeControls.SetRotationTab(false);Mode(false);
                Check(Hit("Rotate tab",false,true,false)&&g.TreeControls.RotationTab,"Desktop Rotate tab hit");
                Check(PlayerPrefs.GetInt(TreeNavigationOverlay.RotationTabKey)==1,"Selected tab saved");
                foreach(bool vr in new[]{false,true})
                {
                    Mode(vr);var panel=Find("Tree navigation");Check(panel.sizeDelta==(vr?new Vector2(760,180):new Vector2(920,126)),"Original panel footprint retained");
                    g.ResetTreeRotation();Check(Hit("Turn Right",vr,true,true)&&g.TreeRotation.x>0,"Held turn button on desktop/Quest ray");
                    float yaw=g.TreeRotation.x;Hit("Turn Right",vr,false,false);Check(g.TreeRotation.x==yaw,"Button release stops rotation");
                    Hit("Tilt Up",vr,true,true);Check(g.TreeRotation.y<0,"Tilt Up direction");Hit("Reset Rotation",vr,true,false);Check(g.TreeRotation==Vector2.zero,"Reset Rotation button");
                    var names=new[]{"Move tab","Rotate tab","Turn Left","Turn Right","Tilt Up","Tilt Down","Reset Rotation","Nearest · F / X","Previous · [ / Y","Next · ] / A"};
                    var targets=names.Select(Find).ToArray();
                    foreach(var r in targets)Check(Mathf.Abs(r.anchoredPosition.x)+r.rect.width*.5f<=panel.rect.width*.5f,"Targets fit central panel safe area");
                    for(int i=0;i<targets.Length;i++)for(int j=i+1;j<targets.Length;j++)Check(Mathf.Abs(targets[i].anchoredPosition.y-targets[j].anchoredPosition.y)>=(targets[i].rect.height+targets[j].rect.height)*.5f||Mathf.Abs(targets[i].anchoredPosition.x-targets[j].anchoredPosition.x)>=(targets[i].rect.width+targets[j].rect.width)*.5f,"Control targets do not overlap");
                }
                Mode(false);g.ResetTreeRotation();Hit("Turn Right",false,false,false);KeyFrame(Key.C);Check(g.TreeRotation.x>0,"Keyboard rotation works while hovering UI");
                g.ResetTreeRotation();KeyFrame(Key.K);Check(g.TreeRotation.x>0,"K turns the tree right");
                g.ResetTreeRotation();KeyFrame(Key.J);Check(g.TreeRotation.x>180,"J turns the tree left");
                g.ResetTreeRotation();KeyFrame(Key.I);Check(g.TreeRotation.y<0,"I tilts the tree up");
                g.ResetTreeRotation();KeyFrame(Key.M);Check(g.TreeRotation.y>0,"M tilts the tree down without passthrough consuming input");
                g.ResetTreeRotation();KeyFrame(Key.Z);Check(g.TreeRotation.x>350,"Z turns left");g.ResetTreeRotation();KeyFrame(Key.R);Check(g.TreeRotation.y<0,"R tilts up");g.ResetTreeRotation();KeyFrame(Key.V);Check(g.TreeRotation.y>0,"V tilts down");
                var heldAngles=g.TreeRotation;KeyFrame(Key.Z,Key.C,Key.R,Key.V);Check(g.TreeRotation==heldAngles,"Opposite rotation keys cancel");KeyFrame();
                var dragging=typeof(GalleryNavigation).GetField("dragging",Hidden);dragging.SetValue(nav,g.GetComponentsInChildren<YearSlider>(true)[0]);
                Check(!nav.RotateTree(Vector2.one,1),"Year drag pauses rotation");dragging.SetValue(nav,null);
                var capture=typeof(NavigationPreferences).GetField("pointerCaptured",Hidden);capture.SetValue(prefs,true);Check(!nav.RotateTree(Vector2.one,1),"Preference drag pauses rotation");capture.SetValue(prefs,false);
                g.Tutorial.Open();Check(!nav.RotateTree(Vector2.one,1),"Tutorial pauses rotation");g.Tutorial.Dismiss();
                g.Library.Open();Check(!nav.RotateTree(Vector2.one,1),"Library/text editing pauses rotation");g.Library.Close();
                g.SetTreeRotation(new Vector2(62,-24));nav.FrameNode(g.Document.People[0].Id);var beforeScene=nav.transform.position;
                g.Select(g.Document.People[0].Id);yield return null;Check(!nav.RotateTree(Vector2.one,1),"Historical scene cannot rotate hidden tree");Check(floor.enabled,"Historical scene restores floor collider");g.ShowTree();yield return null;
                Check(!floor.enabled&&g.TreeControls.RotationTab,"Tree return hides floor collider and restores Rotate tab");
                Check(g.TreeRotation==new Vector2(62,-24)&&Vector3.Distance(nav.transform.position,beforeScene)<.001f,"Scene return restores rotation and viewer");
                g.OpenDocument(g.Document.Copy());yield return null;Check(g.TreeRotation==new Vector2(62,-24),"Same-tree edit retains rotation");
                prefs.SetTreeLayout(layoutSettings.Gap<3.8f?layoutSettings.Gap+.1f:3.5f,layoutSettings.Stagger,layoutSettings.Rise);yield return null;
                Check(g.TreeRotation==new Vector2(62,-24)&&Vector3.Distance(g.WorldTreeBounds.center,g.Layout.Bounds.center+new Vector3(g.TreePan.x,0,g.TreePan.y))<.001f,"Layout change recalculates pivot and retains angles");
                prefs.SetTreeLayout(layoutSettings.Gap,layoutSettings.Stagger,layoutSettings.Rise);yield return null;
                foreach(var c in g.GetComponentsInChildren<CubeFacingControls>())c.SetInteractionRange(20);
                var controls=g.GetComponentsInChildren<CubeFacingControls>();var steps=controls.Select(c=>(Action<float>)Delegate.CreateDelegate(typeof(Action<float>),c,advance)).ToArray();
                for(int pass=0;pass<2;pass++)for(int face=0;face<4;face++){g.SetTreeRotation(new Vector2(face*90,25));foreach(var step in steps)step(.3f);}
                int objects=g.GetComponentsInChildren<Transform>(true).Length,pools=controls.Sum(c=>c.PoolBuilds),reads=g.Thumbnails.DiskReads;layout=g.Layout;long pngReads=PanoramaCache.ImageReadCount;
                long allocated=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<1000;i++){nav.RotateTree(new Vector2(1,.1f),.016f);foreach(var step in steps)step(.016f);}allocated=GC.GetAllocatedBytesForCurrentThread()-allocated;
                Check(ReferenceEquals(g.Layout,layout)&&g.GetComponentsInChildren<Transform>(true).Length==objects&&controls.Sum(c=>c.PoolBuilds)==pools,"Held rotation neither rebuilds layout nor creates tree/panel objects");
                Check(g.Thumbnails.DiskReads==reads&&PanoramaCache.ImageReadCount==pngReads,"Held rotation does not reload panoramas");Check(allocated==0,"Warmed rotation/facing loop allocates zero managed bytes");
                var unchangedView=nav.transform.position;g.ResetTreeRotation();Check(g.TreeRotation==Vector2.zero&&nav.transform.position==unchangedView,"Reset Rotation leaves viewer position unchanged");
                g.SetTreeRotation(new Vector2(90,45));nav.ResetTreeView();Check(g.TreeRotation==Vector2.zero&&nav.transform.position==Vector3.zero,"Reset View clears orientation and viewer pose");
                g.OpenDocument(Fixture(0));yield return null;Check(!nav.RotateTree(Vector2.one,1)&&g.TreeRotation==Vector2.zero,"Empty tree safely disables rotation");
                g.OpenDocument(Fixture(1));yield return null;var only=g.Document.People[0].Id;var single=g.TreeNodePosition(only);g.SetTreeRotation(new Vector2(120,-85));Check(Vector3.Distance(single,g.TreeNodePosition(only))<.001f,"Single sphere rotates around its own center");
                g.OpenDocument(Fixture(100));g.SetTreeRotation(new Vector2(45,30));float deadline=Time.realtimeSinceStartup+40;while(g.IsBuildingTree&&Time.realtimeSinceStartup<deadline)yield return null;
                Check(!g.IsBuildingTree&&g.Layout.Positions.Count==100&&g.TreeRotation==new Vector2(45,30),"Asynchronous large-tree build applies requested rotation after construction");
                g.OpenDocument(original);while(!g.CacheIndexReady)yield return null;yield return null;
                g.SetTreeRotation(new Vector2(32,-18));Check(g.GetComponentsInChildren<TreeNodeTarget>().Length==original.People.Count,"Imported tree keeps every sphere while rotated");
                foreach(var p in g.Layout.Positions)Check(Vector3.Distance(g.WorldTreeBounds.ClosestPoint(g.TreeNodePosition(p.Key)),g.TreeNodePosition(p.Key))<.001f,"Imported rotated bounds include every person");
                File.WriteAllText(Report,n+" rotation Play checks passed: pivot, angle limits, tree/scene state, transformed bounds/picking/framing, UI/Quest-ray controls, real keyboard hover/capture, resets, layout edits, empty/single/large/imported trees. Warm 1000-step rotation/facing loop: "+allocated+" managed bytes, no objects/layout rebuilds/panorama reads.");
            }
            finally
            {
                if(keyboard!=null)InputSystem.RemoveDevice(keyboard);if(mouse!=null)InputSystem.RemoveDevice(mouse);oldKeyboard?.MakeCurrent();oldMouse?.MakeCurrent();
                g.Library.Close();g.LocationEditor.Close();g.Tutorial.Dismiss();prefs.CancelPointerCapture();typeof(GalleryNavigation).GetField("dragging",Hidden).SetValue(nav,null);
                if(hadTab)PlayerPrefs.SetInt(TreeNavigationOverlay.RotationTabKey,oldTab);else PlayerPrefs.DeleteKey(TreeNavigationOverlay.RotationTabKey);
                prefs.SetTreeLayout(layoutSettings.Gap,layoutSettings.Stagger,layoutSettings.Rise);g.OpenDocument(original);g.SetTreeRotation(angles);g.PanTree(pan-g.TreePan);
                g.TreeControls.SetExpanded(expanded);g.TreeControls.SetRotationTab(tab);if(!hadTab)PlayerPrefs.DeleteKey(TreeNavigationOverlay.RotationTabKey);
                g.Hud.SetVisible(hud);nav.transform.SetPositionAndRotation(position,rotation);nav.view.transform.localRotation=look;pitch.SetValue(nav,oldPitch);prefs.SetOpen(preferencesOpen);
                if(hadTutorial)PlayerPrefs.SetInt(TutorialOverlay.SeenKey,oldTutorial);else PlayerPrefs.DeleteKey(TutorialOverlay.SeenKey);PlayerPrefs.Save();
            }
        }
    }
}
