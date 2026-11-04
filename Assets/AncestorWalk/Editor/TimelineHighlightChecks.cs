using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
namespace AncestorWalk.Editor
{
    public static class TimelineHighlightChecks
    {
        const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        public static string Run()
        {
            if(!Application.isPlaying)throw new Exception("Play mode required");
            int n=0;void Check(bool ok,string why){if(!ok)throw new Exception(why);n++;}
            var g=UnityEngine.Object.FindAnyObjectByType<AncestorGallery>();var nav=UnityEngine.Object.FindAnyObjectByType<GalleryNavigation>();
            var selected=g.SelectedId;var position=nav.transform.position;var rotation=nav.transform.rotation;var look=nav.view.transform.localRotation;
            var years=(Dictionary<string,int[]>)typeof(AncestorGallery).GetField("cachedYears",Private).GetValue(g);
            string id=g.Tree.People.Keys.First(i=>g.AvailableYears(i).Length>0);var originals=years[id];int originalYear=g.Year(id);
            var p=g.Tree.People[id];LifeTimeline.TryRange(p,out int birth,out int end);
            var markerMap=(Dictionary<string,TimelineMarker[]>)typeof(AncestorGallery).GetField("timelineMarkers",Private).GetValue(g);
            void SetYears(int[] values){years[id]=values;markerMap[id]=TimelineMarker.Build(p,g.Locations(id),values);}
            bool hadTutorial=PlayerPrefs.HasKey(TutorialOverlay.SeenKey);int seen=PlayerPrefs.GetInt(TutorialOverlay.SeenKey);
            var advance=typeof(CubeFacingControls).GetMethod("AdvanceFacing",Private);
            var refresh=typeof(AncestorGallery).GetMethod("RefreshPersonUI",Private);
            string prefix="overlay."+g.TreeId+"."+PanoramaCache.PersonKey(id)+".";bool hadTimeline=PlayerPrefs.HasKey(prefix+"timeline");int timelineSaved=PlayerPrefs.GetInt(prefix+"timeline");
            bool Same(Color a,Color b)=>Mathf.Abs(a.r-b.r)<.001f&&Mathf.Abs(a.g-b.g)<.001f&&Mathf.Abs(a.b-b.b)<.001f;
            Color Read(Renderer r){var block=new MaterialPropertyBlock();r.GetPropertyBlock(block);return block.GetColor("_Color");}
            try
            {
                g.Tutorial.Dismiss();nav.GetComponent<NavigationPreferences>().SetOpen(false);g.ShowTree();nav.FrameNode(id);
                var controls=g.GetComponentsInChildren<CubeFacingControls>().Single(c=>c.GetComponent<CubePanoramaPreview>().PersonId==id);
                var panel=controls.Panel.GetComponentInChildren<TimelinePanel>(true);
                var thumb=panel.GetComponentsInChildren<Renderer>(true).Single(r=>r.name=="Selected year");
                void Step(float dt)=>advance.Invoke(controls,new object[]{dt});
                var markerYear=originals.First(y=>y>=birth&&y<=end);int adjacent=Enumerable.Range(birth,end-birth+1).First(y=>!originals.Contains(y)&&!g.Locations(id).entries.Any(e=>e.year==y));
                g.SetYear(id,markerYear);Step(.3f);Check(Same(Read(thumb),g.gold.color),"Generated year thumb is gold");
                var marker=panel.GetComponentsInChildren<Renderer>().First(r=>r.name=="Cached year");Check(Same(Read(thumb),Read(marker)),"Thumb exactly matches marker RGB");
                int builds=controls.PoolBuilds,objects=controls.GetComponentsInChildren<Transform>(true).Length;
                for(int i=0;i<6;i++)
                {
                    int year=i%2==0?adjacent:markerYear;g.SetYear(id,year);Step(.3f);
                    Check(Same(Read(thumb),i%2==0?g.teal.color:g.gold.color),"Same-marker-list scrubbing changes thumb");
                    Check(g.Year(id)==year,"No year snapping");
                }
                Check(controls.PoolBuilds==builds&&controls.GetComponentsInChildren<Transform>(true).Length==objects,"Color changes create no panel objects");
                nav.transform.position+=Vector3.right*3;Step(.04f);
                var visible=controls.GetComponentsInChildren<MeshRenderer>().Where(r=>r.sortingOrder==30).ToArray();
                Check(visible.Length>=2,"Live and fading thumbs are present");var alpha=visible.Select(r=>Read(r).a).ToArray();
                g.SetYear(id,adjacent);
                for(int i=0;i<visible.Length;i++){Check(Same(Read(visible[i]),g.teal.color),"Fading face thumb color synchronized");Check(Mathf.Abs(Read(visible[i]).a-alpha[i])<.001f,"Color preserves face alpha");}
                nav.transform.position+=Vector3.back*100;Step(.3f);g.SetYear(id,markerYear);Check(Read(thumb).a==0,"Hidden thumb stays transparent on highlight");
                nav.FrameNode(id);Step(.3f);SetYears(Array.Empty<int>());refresh.Invoke(g,new object[]{id});Check(Same(Read(thumb),g.teal.color),"No compatible years uses teal");
                SetYears(new[]{birth,end}.Distinct().ToArray());refresh.Invoke(g,new object[]{id});
                foreach(int year in years[id]){g.SetYear(id,year);Check(Same(Read(thumb),g.gold.color),"Generated lifespan endpoint highlights");}
                SetYears(originals);refresh.Invoke(g,new object[]{id});g.Select(id);var ui=g.Overlay;
                var sceneRefresh=typeof(SceneOverlay).GetMethod("Refresh",Private);var mode=typeof(SceneOverlay).GetMethod("ConfigureForMode",Private);
                Image SceneThumb()=>ui.GetComponentsInChildren<RectTransform>(true).Single(r=>r.name=="Selected year").GetComponent<Image>();
                if(!ui.TimelineOpen)ui.ToggleTimeline();
                foreach(bool vr in new[]{false,true})
                {
                    mode.Invoke(ui,new object[]{vr});
                    foreach(int year in new[]{markerYear,adjacent,markerYear})
                    {
                        g.SetYear(id,year);sceneRefresh.Invoke(ui,null);
                        var tick=ui.GetComponentsInChildren<RectTransform>().First(r=>r.name=="Cached year").GetComponent<Image>();
                        Check(Same(SceneThumb().color,year==markerYear?tick.color:g.teal.color),"Scene timeline exact-match color in desktop/VR mode");
                        Check(g.Year(id)==year,"Scene selection unchanged");
                    }
                    SetYears(Array.Empty<int>());sceneRefresh.Invoke(ui,null);Check(Same(SceneThumb().color,g.teal.color),"Empty scene timeline teal");
                    SetYears(originals);
                }
                string result=n+" timeline highlight checks passed; exact generated years, pooled face alpha, no panel recreation, desktop/VR overlays; no API calls or Quest deployment.";
                System.IO.File.WriteAllText("Artifacts/timeline-highlight-checks.txt",result);return result;
            }
            finally
            {
                SetYears(originals);g.SetYear(id,originalYear);g.LocationEditor.Close();
                if(selected==null)g.ShowTree();else g.Select(selected);
                nav.transform.SetPositionAndRotation(position,rotation);nav.view.transform.localRotation=look;
                if(hadTimeline)PlayerPrefs.SetInt(prefix+"timeline",timelineSaved);else PlayerPrefs.DeleteKey(prefix+"timeline");
                if(hadTutorial)PlayerPrefs.SetInt(TutorialOverlay.SeenKey,seen);else PlayerPrefs.DeleteKey(TutorialOverlay.SeenKey);PlayerPrefs.Save();
            }
        }
    }
}
