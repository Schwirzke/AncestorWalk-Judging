using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
namespace AncestorWalk.Editor
{
    public static class OverlayComfortChecks
    {
        const BindingFlags Hidden=BindingFlags.NonPublic|BindingFlags.Instance;
        public static string Run()
        {
            if(!Application.isPlaying)throw new Exception("Play mode required");
            int n=0;void Check(bool ok,string why){if(!ok)throw new Exception(why);n++;}
            var g=UnityEngine.Object.FindAnyObjectByType<AncestorGallery>();bool open=g.Tutorial.IsOpen;
            bool had=PlayerPrefs.HasKey(SceneOverlay.InlineCitationsKey);int saved=PlayerPrefs.GetInt(SceneOverlay.InlineCitationsKey);
            void Mode(object target,bool vr){target.GetType().GetMethod(target is TutorialOverlay?"Configure":"ConfigureForMode",Hidden).Invoke(target,new object[]{vr});Canvas.ForceUpdateCanvases();}
            RectTransform Find(Component parent,string name)=>parent.GetComponentsInChildren<RectTransform>(true).Single(r=>r.name==name);
            void Click(SceneOverlay ui,string name){var r=Find(ui,name);ui.HandleInput(new Ray(r.position-r.forward,r.forward),true,true,false);}
            Text Body(SceneOverlay ui)=>(Text)typeof(SceneOverlay).GetField("guideBody",Hidden).GetValue(ui);
            try
            {
                PlayerPrefs.DeleteKey(SceneOverlay.InlineCitationsKey);g.Tutorial.Dismiss();g.Select(g.Tree.People.Values.First(p=>LifeTimeline.TryRange(p,out _,out _)).Id);var ui=g.Overlay;ui.OpenGuide();
                var answer=new GuideAnswer{answer="In 1870, railways [1] and homes [2]. Keep a year [1870] and an unknown marker [3].",sources=new[]{new GuideSource{title="Museum",url="https://example.org/1"},new GuideSource{title="Archive",url="https://example.org/2"}}};
                string raw=answer.answer;typeof(HistoricalGuide).GetProperty("Answer").SetValue(ui.Guide,answer);typeof(SceneOverlay).GetMethod("RefreshGuide",Hidden).Invoke(ui,null);
                Check(!ui.InlineCitationsVisible&&!Body(ui).text.Contains("[1]")&&!Body(ui).text.Contains("[2]"),"Inline markers hidden by default");
                Check(Body(ui).text.Contains("[1870]")&&Body(ui).text.Contains("[3]"),"Unrelated bracketed numbers retained");
                Check(answer.answer==raw&&answer.sources.Length==2,"Original answer and source map untouched");
                Click(ui,"Toggle inline citations");Check(ui.InlineCitationsVisible&&Body(ui).text==raw,"Citation icon reveals original markers");
                Check(PlayerPrefs.GetInt(SceneOverlay.InlineCitationsKey)==1,"Citation preference saved");
                Click(ui,"SOURCES");Check(ui.GetComponentsInChildren<Text>().Any(t=>t.text.Contains("[1] Museum")),"Numbered source browser remains available");Click(ui,"SOURCES");
                Click(ui,"Toggle inline citations");Check(!ui.InlineCitationsVisible&&!Body(ui).text.Contains("[1]"),"Second icon press hides markers");
                ui.CloseGuide();ui.OpenGuide();Check(!ui.InlineCitationsVisible,"Choice survives close/reopen");
                float sceneDensity=ui.GetComponent<Canvas>().scaleFactor;Mode(ui,true);Check(ui.GetComponent<Canvas>().scaleFactor==3&&ui.GetComponent<CanvasScaler>().dynamicPixelsPerUnit==3,"VR text rasterizes at triple density");
                var timeline=Find(ui,"Bottom timeline");Check(timeline.rect.height<100,"Timeline uses a compact layout");
                foreach(string name in new[]{"←","i","Toggle timeline","ASK"})
                {var r=Find(ui,name);Check(Mathf.Abs(r.anchoredPosition.x)<=350&&r.anchoredPosition.y==-215,"Scene dock inset: "+name);Check(r.anchoredPosition.y+r.rect.height/2<timeline.anchoredPosition.y-timeline.rect.height/2,"Dock does not overlap timeline");}
                Check(Find(ui,"Historical guide").sizeDelta.x==740,"Guide card is narrower without scaling type");
                Check(Find(ui,"×").anchoredPosition.x==334&&Find(ui,"×").rect.width>=40,"Close button inset with preserved target size");
                Click(ui,"Toggle inline citations");Check(ui.InlineCitationsVisible&&Body(ui).text.Contains("[1]"),"VR ray hits relocated citation icon");
                Check(answer.answer==raw,"VR reflow preserves sourced original");
                Mode(ui,false);Check(Find(ui,"←").anchoredPosition==new Vector2(-440,-238)&&Mathf.Approximately(ui.GetComponent<Canvas>().scaleFactor,sceneDensity),"Desktop dock and density restored");
                var endpoint=Find(ui,"Birth year");
                var endpointCorners=new Vector3[4];var dockCorners=new Vector3[4];endpoint.GetWorldCorners(endpointCorners);Find(ui,"ASK").GetWorldCorners(dockCorners);
                Check(endpointCorners[0].y>dockCorners[1].y,"Desktop endpoint year is clear of bottom buttons");
                Check(Find(ui,"Historical guide").sizeDelta.x==820&&Body(ui).rectTransform.sizeDelta.x==750,"Desktop guide widths restored");ui.CloseGuide();
                g.ShowTree();var controls=g.TreeControls;bool expanded=controls.IsExpanded;controls.SetExpanded(true);float flightDensity=controls.GetComponent<Canvas>().scaleFactor;Mode(controls,true);
                Check(Find(controls,"Tree navigation").sizeDelta.x==760,"Flight panel fits central safe area");
                Check(Find(controls,"Toggle flight controls").anchoredPosition==new Vector2(350,-215),"Flight icon inset");
                var names=new[]{"Left","Right","Forward","Back","Up","Down","Reset View","Nearest · F / X","Previous · [ / Y","Next · ] / A"};
                var buttons=names.Select(x=>Find(controls,x)).ToArray();foreach(var r in buttons)Check(Mathf.Abs(r.anchoredPosition.x)+r.rect.width/2<=380,"Flight target stays in panel: "+r.name);
                for(int i=0;i<buttons.Length;i++)for(int j=i+1;j<buttons.Length;j++)Check(Mathf.Abs(buttons[i].anchoredPosition.y-buttons[j].anchoredPosition.y)>=(buttons[i].rect.height+buttons[j].rect.height)/2||Mathf.Abs(buttons[i].anchoredPosition.x-buttons[j].anchoredPosition.x)>=(buttons[i].rect.width+buttons[j].rect.width)/2,"Flight buttons do not overlap");
                Mode(controls,false);Check(Find(controls,"Tree navigation").sizeDelta.x==920&&Find(controls,"Toggle flight controls").anchoredPosition.x==440&&Mathf.Approximately(controls.GetComponent<Canvas>().scaleFactor,flightDensity),"Desktop flight layout and density restored");controls.SetExpanded(expanded);
                float helpDensity=g.Tutorial.GetComponent<Canvas>().scaleFactor;Mode(g.Tutorial,true);Check(Find(g.Tutorial,"Tutorial help").anchoredPosition==new Vector2(-320,210),"Help icon inset");
                Check(g.Tutorial.GetComponent<CanvasScaler>().dynamicPixelsPerUnit==3,"Tutorial text receives higher density");Mode(g.Tutorial,false);
                Check(Find(g.Tutorial,"Tutorial help").anchoredPosition==new Vector2(-409,234)&&Mathf.Approximately(g.Tutorial.GetComponent<Canvas>().scaleFactor,helpDensity),"Desktop Help anchor and density restored");
                var result=n+" citation/VR overlay checks passed; simulated VR layout, no paid calls or deployment.";System.IO.File.WriteAllText("Artifacts/overlay-comfort-checks.txt",result);return result;
            }
            finally
            {
                if(had)PlayerPrefs.SetInt(SceneOverlay.InlineCitationsKey,saved);else PlayerPrefs.DeleteKey(SceneOverlay.InlineCitationsKey);PlayerPrefs.Save();
                g.ShowTree();if(open)g.Tutorial.Open();else g.Tutorial.Dismiss();
            }
        }
    }
}
