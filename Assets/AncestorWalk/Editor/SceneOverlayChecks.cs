using System;
using System.Linq;
using UnityEngine;
namespace AncestorWalk.Editor
{
    public static class SceneOverlayChecks
    {
        public static string Run()
        {
            int n=0;void Check(bool ok,string reason){if(!ok)throw new Exception(reason);n++;}
            var g=UnityEngine.Object.FindFirstObjectByType<AncestorGallery>();var ui=g.Overlay;
            Check(ui!=null,"Overlay exists");Canvas.ForceUpdateCanvases();
            var rail=ui.GetComponentsInChildren<RectTransform>(true).Single(r=>r.name=="Year slider");
            Ray At(RectTransform rect,float x=0){var point=rect.TransformPoint(new Vector3(x,0,0));return new Ray(point-rect.forward*10,rect.forward);}
            bool original=ui.TimelineOpen;if(!original)ui.ToggleTimeline();
            var position=Camera.main.transform.position;int year=g.Year(g.SelectedId);LifeTimeline.TryRange(g.Tree.People[g.SelectedId],out _,out int end);
            Check(ui.HandleInput(At(rail,rail.rect.xMax),true,true,true),"Slider captures ray");
            ui.HandleInput(At(rail,rail.rect.xMax+100),true,false,true);
            Check(g.Year(g.SelectedId)==end,"Dragging clamps to last year");
            Check(Camera.main.transform.position==position,"Drag does not move camera");
            Check(ui.HandleInput(At(rail),true,false,false),"Release captured");
            ui.ToggleTimeline();Check(!rail.gameObject.activeInHierarchy,"Collapsed timeline hides controls");
            Check(!ui.HandleInput(At(rail),true,true,true),"Collapsed timeline cannot capture input");
            var clock=ui.GetComponentsInChildren<RectTransform>(true).Single(r=>r.name=="Toggle timeline");
            Check(ui.HandleInput(At(clock),true,true,true)&&ui.TimelineOpen,"Clock reopens timeline");
            Check(PlayerPrefs.GetInt("overlay.timeline")==1,"Expanded preference saved");
            var alpha=rail.parent.GetComponent<UnityEngine.UI.Image>().color.a;Check(alpha>0&&alpha<1,"Panel is translucent");
            Check(!g.GetComponentsInChildren<Transform>().Any(t=>t.name=="Evidence plaque"||t.name=="Timeline panel"),"Old panels removed");
            g.SetYear(g.SelectedId,year);if(!original)ui.ToggleTimeline();
            return n+" overlay checks passed; no generation requests.";
        }
    }
}
