using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
namespace AncestorWalk.Editor
{
    public static class LocationMarkerChecks
    {
        const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        public static string Run()
        {
            int n=0;void Check(bool ok,string why){if(!ok)throw new Exception(why);n++;}
            bool Same(Color a,Color b)=>Mathf.Abs(a.r-b.r)<.001f&&Mathf.Abs(a.g-b.g)<.001f&&Mathf.Abs(a.b-b.b)<.001f;
            var person=new Person{BirthDate="1900",DeathDate="1980",BirthPlace="York",DeathPlace="York"};
            var moves=new ResidenceTimeline{entries=new List<Residence>{new(){year=1940,place="C"},new(){year=1910,place="A"},new(){year=1920,place="B"}}};
            var built=TimelineMarker.Build(person,moves,new[]{1900,1915,1940});
            Check(built.Select(x=>x.Year).SequenceEqual(new[]{1900,1910,1915,1920,1940}),"Sorted union of move dates and generated years");
            Check(!built.Single(x=>x.Year==1910).Generated,"Later image does not mark the move's exact year as generated");
            Check(!built.Single(x=>x.Year==1920).Generated,"Image in prior/next residence does not mark this move generated");
            Check(built.Single(x=>x.Year==1940).Generated&&built.Count(x=>x.Year==1940)==1,"Exact generated/move coincidence is one gold tick");
            Check(built.Single(x=>x.Year==1940).LocationMove&&built.Single(x=>x.Year==1910).LocationMove&&!built.Single(x=>x.Year==1915).LocationMove,"Move/image kind retained, including coincident year");
            Check(TimelineMarker.Build(person,moves,Array.Empty<int>()).All(x=>!x.Generated),"No images leaves all moves pending");
            Check(TimelineMarker.Build(person,moves,new[]{1900,1915,1920,1940}).Single(x=>x.Year==1920).Generated,"Exact-year generation changes pending status");
            Check(TimelineMarker.Build(new Person(),moves,Array.Empty<int>()).Length==0,"Missing lifespan has no misplaced ticks");
            var gallery=UnityEngine.Object.FindAnyObjectByType<AncestorGallery>();var nav=UnityEngine.Object.FindAnyObjectByType<GalleryNavigation>();
            string id=gallery.Tree.People.Keys.First(i=>gallery.AvailableYears(i).Length>0&&LifeEndpointScenes.TrySceneRange(gallery.Tree.People[i],out int b,out int e)&&e-b>gallery.AvailableYears(i).Length);
            var map=(Dictionary<string,TimelineMarker[]>)typeof(AncestorGallery).GetField("timelineMarkers",Private).GetValue(gallery);
            var realMarkers=map[id];LifeEndpointScenes.TrySceneRange(gallery.Tree.People[id],out int first,out int last);int pendingYear=Enumerable.Range(first,last-first+1).First(y=>!gallery.AvailableYears(id).Contains(y));
            var original=new[]{new TimelineMarker(gallery.AvailableYears(id)[0],true),new TimelineMarker(pendingYear,false,true)}.OrderBy(m=>m.Year).ToArray();map[id]=original;
            int pending=Array.FindIndex(original,m=>!m.Generated),savedYear=gallery.Year(id);string selected=gallery.SelectedId;
            var position=nav.transform.position;var rotation=nav.transform.rotation;var look=nav.view.transform.localRotation;
            var refresh=typeof(AncestorGallery).GetMethod("RefreshPersonUI",Private);var advance=typeof(CubeFacingControls).GetMethod("AdvanceFacing",Private);
            string prefix="overlay."+gallery.TreeId+"."+PanoramaCache.PersonKey(id)+".";bool hadTimeline=PlayerPrefs.HasKey(prefix+"timeline");int timeline=PlayerPrefs.GetInt(prefix+"timeline");
            bool seenHad=PlayerPrefs.HasKey(TutorialOverlay.SeenKey);int seen=PlayerPrefs.GetInt(TutorialOverlay.SeenKey);
            Color Read(Renderer r){var b=new MaterialPropertyBlock();r.GetPropertyBlock(b);return b.GetColor("_Color");}
            try
            {
                gallery.Tutorial.Dismiss();nav.GetComponent<NavigationPreferences>().SetOpen(false);gallery.ShowTree();nav.FrameNode(id);
                var controls=gallery.GetComponentsInChildren<CubeFacingControls>().Single(c=>c.GetComponent<CubePanoramaPreview>().PersonId==id);
                var panel=controls.Panel.GetComponentInChildren<TimelinePanel>(true);
                var ticks=(List<GameObject>)typeof(TimelinePanel).GetField("markers",Private).GetValue(panel);
                var renderer=ticks[pending].GetComponent<Renderer>();
                advance.Invoke(controls,new object[]{.3f});Check(Same(Read(renderer),TimelineMarker.PendingColor),"Pending move initially purple on sphere panel");
                int imageIndex=Array.FindIndex(original,m=>!m.LocationMove);Check(imageIndex>=0&&ticks[pending].transform.localScale.y>ticks[imageIndex].transform.localScale.y,"Saved move marker taller than image tick on sphere");
                Check(ticks.Count(t=>t.activeSelf)==original.Length,"Every move/generated year gets one tick");
                int pool=controls.PoolBuilds,count=controls.GetComponentsInChildren<Transform>(true).Length;
                var generated=original.Select((m,i)=>new TimelineMarker(m.Year,i==pending||m.Generated,m.LocationMove,m.Generating,m.MissingLocation)).ToArray();map[id]=generated;refresh.Invoke(gallery,new object[]{id});
                Check(Same(Read(renderer),gallery.gold.color),"Existing move tick turns gold after scene status changes");
                Check(pool==controls.PoolBuilds&&count==controls.GetComponentsInChildren<Transform>(true).Length,"Status changes do not rebuild panel objects");
                nav.transform.position+=Vector3.right*3;advance.Invoke(controls,new object[]{.04f});map[id]=original;refresh.Invoke(gallery,new object[]{id});
                Check(Same(Read(renderer),TimelineMarker.PendingColor),"Pending status restores while face fades");
                gallery.Select(id);var ui=gallery.Overlay;if(!ui.TimelineOpen)ui.ToggleTimeline();
                gallery.SetYear(id,original[pending].Year);
                Check(!gallery.IsAllTime&&gallery.SceneYears(id).SequenceEqual(gallery.AvailableYears(id)),"Pending move keeps nearest cached panorama pair");
                var sceneRefresh=typeof(SceneOverlay).GetMethod("Refresh",Private);var configure=typeof(SceneOverlay).GetMethod("ConfigureForMode",Private);
                foreach(bool vr in new[]{false,true})
                {
                    configure.Invoke(ui,new object[]{vr});sceneRefresh.Invoke(ui,null);
                    var images=(List<Image>)typeof(SceneOverlay).GetField("tickImages",Private).GetValue(ui);
                    Check(Same(images[pending].color,TimelineMarker.PendingColor),"Desktop/VR pending move purple");
                    Check(images[pending].rectTransform.rect.height>images[imageIndex].rectTransform.rect.height,"Desktop/VR distinguishes tall move from short image");
                    Check(images.Count(i=>i.gameObject.activeSelf)==original.Length,"Desktop/VR sorted move/generated union");
                    int objects=ui.GetComponentsInChildren<Transform>(true).Length;map[id]=generated;sceneRefresh.Invoke(ui,null);
                    Check(Same(images[pending].color,images.First(i=>i.color!=TimelineMarker.PendingColor).color),"Desktop/VR move changes to marker gold");
                    Check(objects==ui.GetComponentsInChildren<Transform>(true).Length,"Desktop/VR status change reuses ticks");
                    map[id]=original.Where((m,i)=>i!=pending).ToArray();sceneRefresh.Invoke(ui,null);Check(images.Count(i=>i.gameObject.activeSelf)==original.Length-1,"Removed move hides extra tick");
                    map[id]=original;sceneRefresh.Invoke(ui,null);Check(images.Count(i=>i.gameObject.activeSelf)==original.Length&&Same(images[pending].color,TimelineMarker.PendingColor),"Restoring move reuses purple tick");
                }
                var result=n+" location marker checks passed; sorted union, residence coverage, generation/removal updates, pooled sphere panels and desktop/VR timelines; no saved-location or panorama writes.";
                System.IO.File.WriteAllText("Artifacts/location-marker-checks.txt",result);return result;
            }
            finally
            {
                map[id]=realMarkers;gallery.SetYear(id,savedYear);if(selected==null)gallery.ShowTree();else gallery.Select(selected);
                nav.transform.SetPositionAndRotation(position,rotation);nav.view.transform.localRotation=look;
                if(hadTimeline)PlayerPrefs.SetInt(prefix+"timeline",timeline);else PlayerPrefs.DeleteKey(prefix+"timeline");
                if(seenHad)PlayerPrefs.SetInt(TutorialOverlay.SeenKey,seen);else PlayerPrefs.DeleteKey(TutorialOverlay.SeenKey);PlayerPrefs.Save();
            }
        }
    }
}
