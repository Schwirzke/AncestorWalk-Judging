using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace AncestorWalk.Editor
{
    public static class SliderHitChecks
    {
        const BindingFlags Private=BindingFlags.NonPublic|BindingFlags.Instance;
        public static string Run()
        {
            var g=UnityEngine.Object.FindAnyObjectByType<AncestorGallery>();var nav=UnityEngine.Object.FindAnyObjectByType<GalleryNavigation>();
            if(!Application.isPlaying||g?.Tree==null||!g.CacheIndexReady)throw new Exception("Ready Play mode required");
            int count=0;void Check(bool ok,string why){if(!ok)throw new Exception(why);count++;}
            var years=g.Tree.People.Keys.ToDictionary(id=>id,g.Year);var position=nav.transform.position;var rotation=nav.transform.rotation;var look=nav.view.transform.localRotation;
            float pitch=(float)typeof(GalleryNavigation).GetField("pitch",Private).GetValue(nav);GameObject blocker=null;
            var advance=typeof(CubeFacingControls).GetMethod("AdvanceFacing",Private);var capture=typeof(GalleryNavigation).GetField("dragging",Private);
            void Step(CubeFacingControls c){advance.Invoke(c,new object[]{.4f});Physics.SyncTransforms();}
            void Eye(Vector3 target){nav.transform.position+=target-nav.view.transform.position;}
            Ray RayTo(Vector3 point)=>new Ray(nav.view.transform.position,point-nav.view.transform.position);
            void Release(YearSlider slider){slider.EndDrag();capture.SetValue(nav,null);}
            try
            {
                g.Library.Close();g.Batch.Close();g.Tutorial.Dismiss();g.ShowTree();
                var sliders=g.GetComponentsInChildren<YearSlider>().OrderBy(s=>(s.transform.position-nav.view.transform.position).sqrMagnitude).ToArray();
                Check(sliders.Length>0,"Example has a live timeline");
                foreach(var s in sliders)
                {
                    var c=s.GetComponentInParent<CubeFacingControls>();Step(c);if(!s.GetComponent<Collider>().enabled)continue;
                    var ray=RayTo(s.ThumbCollider.transform.position);Check(Physics.Raycast(ray,out var hit,100)&&YearSlider.Resolve(hit.collider)==s,"Previously missed visible thumb is physically targetable from current view");
                }
                var slider=sliders[0];string id=slider.GetComponentInParent<TimelinePanel>().PersonId;var controls=slider.GetComponentInParent<CubeFacingControls>();
                controls.SetInteractionRange(6);nav.FrameNode(id);Step(controls);var thumb=slider.ThumbCollider.transform;var visible=thumb.localScale;
                LifeTimeline.TryRange(g.Tree.People[id],out int birth,out int end);
                foreach(int year in new[]{birth,(birth+end)/2,end})
                {
                    g.SetYear(id,year);Physics.SyncTransforms();
                    foreach(var point in new[]{Vector3.zero,new Vector3(-.45f,.45f,0),new Vector3(.45f,-.45f,0),new Vector3(slider.ThumbCollider.size.x*.43f,slider.ThumbCollider.size.y*.43f,0)})
                    {
                        var ray=RayTo(thumb.TransformPoint(point));Check(nav.TryBeginYearDrag(ray),"Center, visual edges and padded thumb corners capture at lifespan endpoints/middle");
                        Check(g.Year(id)==year,"Grabbing the thumb edge does not jump the selected year");Release(slider);
                    }
                }
                g.SetYear(id,(birth+end)/2);Physics.SyncTransforms();Check(nav.TryBeginYearDrag(RayTo(thumb.position)),"Start drag through navigation input route");
                slider.Drag(RayTo(slider.transform.TransformPoint(new Vector3(2,0,0))));Check(g.Year(id)==end,"Dragging outside the rail reaches death endpoint");
                slider.Drag(RayTo(slider.transform.TransformPoint(new Vector3(-2,0,0))));Check(g.Year(id)==birth,"Dragging outside the opposite edge reaches birth endpoint");Release(slider);
                var center=controls.transform.position;Eye(center+Vector3.back*1.5f+Vector3.up*.45f);Step(controls);float nearWidth=slider.ThumbCollider.bounds.size.x,nearHeight=slider.ThumbCollider.bounds.size.y;
                Eye(center+Vector3.back*6+Vector3.up*.45f);Step(controls);Check(slider.ThumbCollider.bounds.size.x>nearWidth&&slider.ThumbCollider.bounds.size.y>nearHeight,"Invisible target grows modestly with distance");
                Check(nav.TryBeginYearDrag(RayTo(thumb.position)),"Distant thumb captures");Release(slider);
                Eye(center+new Vector3(1.5f,.45f,-2));Step(controls);Check(nav.TryBeginYearDrag(RayTo(thumb.position)),"Angled thumb captures at its own front depth");Release(slider);Check(thumb.localScale==visible,"Visible thumb geometry remains unchanged");
                blocker=GameObject.CreatePrimitive(PrimitiveType.Cube);blocker.transform.position=Vector3.Lerp(nav.view.transform.position,thumb.position,.5f);blocker.transform.localScale=Vector3.one*.2f;Physics.SyncTransforms();
                Check(!nav.TryBeginYearDrag(RayTo(thumb.position)),"Nearest solid occluder prevents clicking a slider through geometry");UnityEngine.Object.DestroyImmediate(blocker);blocker=null;
                Eye(center+Vector3.back*(controls.InteractionRange+2));Step(controls);Check(!slider.RailCollider.enabled&&!slider.ThumbCollider.enabled,"Rail and thumb fade/disable together outside the interaction range");
                Eye(center+new Vector3(0,.45f,-2));Step(controls);Check(slider.RailCollider.enabled&&slider.ThumbCollider.enabled,"Both targets return after proximity fade-in");
                for(int i=0;i<10;i++)slider.UpdateHitArea(center+Vector3.back*(4+i*.1f));long before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<1000;i++)slider.UpdateHitArea(center+Vector3.back*(4+i%20*.1f));Check(GC.GetAllocatedBytesForCurrentThread()-before==0,"Warmed hit-area sizing allocates zero managed bytes");
                g.Select(id);var ui=g.Overlay;typeof(SceneOverlay).GetMethod("ConfigureForMode",Private).Invoke(ui,new object[]{true});typeof(SceneOverlay).GetField("timelineOpen",Private).SetValue(ui,true);typeof(SceneOverlay).GetMethod("Apply",Private).Invoke(ui,new object[]{false});
                var rects=ui.GetComponentsInChildren<RectTransform>(true);var rail=rects.Single(r=>r.name=="Year slider");var handle=rects.Single(r=>r.name=="Selected year");var target=rects.Single(r=>r.name=="Year slider hit area");
                Check(target.rect.width>rail.rect.width&&target.rect.height>rail.rect.height,"Scene timeline has padded invisible target without changing rail geometry");
                g.SetYear(id,(birth+end)/2);typeof(SceneOverlay).GetMethod("Refresh",Private).Invoke(ui,null);int selected=g.Year(id);
                // Outside the visible thumb, inside its padded grab target rather than the rail-only area.
                var edge=RayTo(handle.TransformPoint(new Vector3(handle.rect.xMax+4,handle.rect.yMax+6,0)));Check(ui.HandleInput(edge,true,true,true)&&ui.IsDraggingYear,"Quest-style ray captures outside the visible scene thumb edge");Check(g.Year(id)==selected,"Scene thumb edge grab also preserves the current year");
                ui.HandleInput(RayTo(rail.TransformPoint(new Vector3(rail.rect.xMax+100,0,0))),true,false,true);Check(g.Year(id)==end,"Captured scene drag continues outside the hit area and clamps at endpoint");
                ui.HandleInput(edge,true,false,false);Check(!ui.IsDraggingYear,"Scene drag releases normally");
                string report=count+" slider hit checks passed: current-view repro, padded endpoint/corner grabs, no jump, near/far/angled rays, occlusion, proximity fades, zero-allocation sizing and scene ray capture.";Directory.CreateDirectory("Artifacts");File.WriteAllText("Artifacts/slider-hit-checks.txt",report);return report;
            }
            finally
            {
                capture.SetValue(nav,null);if(blocker!=null)UnityEngine.Object.DestroyImmediate(blocker);
                foreach(var entry in years)g.SetYear(entry.Key,entry.Value);g.ShowTree();nav.transform.SetPositionAndRotation(position,rotation);nav.view.transform.localRotation=look;typeof(GalleryNavigation).GetField("pitch",Private).SetValue(nav,pitch);
            }
        }
    }
}
