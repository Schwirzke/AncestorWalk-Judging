using System;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace AncestorWalk.Editor
{
    public static class MissingLocationMarkerChecks
    {
        const BindingFlags Hidden=BindingFlags.NonPublic|BindingFlags.Instance;
        public static string Data()
        {
            int n=0;void Check(bool value,string why){if(!value)throw new Exception(why);n++;}
            var p=new Person{BirthDate="1826",BirthPlace="Baden-Württemberg, Germany",DeathDate="18 Dec 1904"};var places=new ResidenceTimeline();
            TimelineMarker[] Build(params int[] years)=>TimelineMarker.Build(p,places,years);
            Check(Build(1826).Select(m=>m.Year).SequenceEqual(new[]{1826,1904}),"Missing death location adds its exact endpoint year");
            var end=Build(1826).Single(m=>m.Year==1904);Check(end.MissingLocation&&end.Blinking&&!end.Generated&&!end.LocationMove,"Missing endpoint is a blinking warning, not a saved move/image");
            Check(end.DisplayColor(Color.yellow)==TimelineMarker.MissingLocationColor,"Missing endpoint is red");
            Check(TimelineMarker.SelectionState(1904,Build(1826))==TimelineSelectionState.Unmarked,"Missing location does not turn thumb purple or gold");
            places.entries.Add(new Residence{year=1880,place="York"});Check(Build().Single(m=>m.Year==1904).MissingLocation,"Earlier move does not invent a death location");
            places.entries.Add(new Residence{year=1904,place="Paris"});Check(!Build().Any(m=>m.MissingLocation),"Exact-year saved move resolves missing endpoint location");
            places.entries.Clear();p.DeathPlace="Paris";Check(!Build().Any(m=>m.MissingLocation),"Recorded death place resolves the warning");
            p.BirthPlace=" ";Check(Build().Single(m=>m.Year==1826).MissingLocation,"Blank birth place adds red start marker");
            var cached=Build(1826).Single(m=>m.Year==1826);
            Check(cached.Generated&&TimelineMarker.SelectionState(1826,Build(1826))==TimelineSelectionState.Generated,"Existing scene retains gold thumb while endpoint fact is missing");
            Check(!cached.Blinking&&cached.DisplayColor(Color.yellow)==Color.yellow,"Compatible generated endpoint is steady gold even with a blank event location");
            var working=TimelineMarker.Build(p,places,new[]{1826},new[]{1826}).Single(m=>m.Year==1826);Check(working.Generating&&working.Blinking&&working.DisplayColor(Color.yellow)==TimelineMarker.GeneratingColor,"Regeneration uses green instead of a missing-location warning");
            var uncachedWorking=TimelineMarker.Build(p,places,Array.Empty<int>(),new[]{1826}).Single(m=>m.Year==1826);
            Check(uncachedWorking.DisplayColor(Color.yellow)==TimelineMarker.GeneratingColor&&uncachedWorking.Blinking,"New generation takes priority over missing location");
            Check(Build().Single(m=>m.Year==1826).DisplayColor(Color.yellow)==TimelineMarker.MissingLocationColor,"Uncached or incompatible scene keeps the red warning");
            places.entries.Add(new Residence{year=1826,place="Berlin"});Check(!Build().Any(m=>m.MissingLocation),"Exact birth-year edit clears start warning");
            places.entries.Clear();p.BirthPlace="York";p.DeathPlace="";p.DeathDate="";p.KnownLiving=true;Check(!Build().Any(m=>m.MissingLocation),"Living endpoint does not imply a missing death place");
            p.KnownLiving=false;Check(!Build().Any(m=>m.MissingLocation),"Unknown death date creates no invented endpoint");
            p.BirthDate="";p.DeathDate="1904";Check(Build().Single().MissingLocation,"Death-only record supports missing-location indicator");
            p.BirthDate="1904";p.BirthPlace="York";Check(Build().Length==1&&Build()[0].MissingLocation,"Same-year events coalesce while retaining missing death fact");
            places.entries.Add(new Residence{year=1904,place="Paris"});Check(Build().Single().LocationMove&&!Build().Single().MissingLocation,"Same-year explicit place resolves both endpoints");
            Check(TimelineMarker.Build(new Person(),new ResidenceTimeline(),Array.Empty<int>()).Length==0,"No usable dates creates no misplaced warning");
            Check(!TimelineMarker.Same(new[]{end},new[]{new TimelineMarker(1904,false)}),"State comparison notices cleared missing-location flag");
            string result=n+" missing-location marker data checks passed.";Directory.CreateDirectory("Artifacts");File.WriteAllText("Artifacts/missing-location-data.txt",result);return result;
        }
        public static string Play()
        {
            var g=UnityEngine.Object.FindAnyObjectByType<AncestorGallery>();if(!Application.isPlaying||g?.Tree==null||g.Generation.Busy)throw new Exception("Idle Play mode required");
            int n=0;void Check(bool value,string why){if(!value)throw new Exception(why);n++;}
            var p=g.Tree.People.Values.First(p=>g.Markers(p.Id).Any(m=>m.MissingLocation&&!m.Generated)&&LifeTimeline.TryRange(p,out _,out _));
            string id=p.Id,selected=g.SelectedId;int year=g.Year(id);bool hud=g.HudVisible;
            string pref="overlay."+g.TreeId+"."+PanoramaCache.PersonKey(id)+".timeline";bool hadPref=PlayerPrefs.HasKey(pref);int oldPref=PlayerPrefs.GetInt(pref);
            var map=(Dictionary<string,TimelineMarker[]>)typeof(AncestorGallery).GetField("timelineMarkers",Hidden).GetValue(g);var original=map[id];int index=Array.FindIndex(original,m=>m.MissingLocation&&!m.Generated);
            var nav=UnityEngine.Object.FindAnyObjectByType<GalleryNavigation>();var position=nav.transform.position;var rotation=nav.transform.rotation;var look=nav.view.transform.localRotation;
            var refresh=typeof(AncestorGallery).GetMethod("RefreshPersonUI",Hidden);var advance=typeof(CubeFacingControls).GetMethod("AdvanceFacing",Hidden);var block=new MaterialPropertyBlock();
            try
            {
                g.Tutorial.Dismiss();g.Hud.SetVisible(true);g.ShowTree();nav.FrameNode(id);
                var controls=g.GetComponentsInChildren<TreeNodeTarget>().Single(x=>x.PersonId==id).Controls;controls.SetInteractionRange(20);advance.Invoke(controls,new object[]{.4f});
                var panel=controls.Panel.GetComponentInChildren<TimelinePanel>(true);var ticks=(List<Renderer>)typeof(TimelinePanel).GetField("markerRenderers",Hidden).GetValue(panel);var tick=ticks[index];tick.GetPropertyBlock(block);
                Check(block.GetColor("_Color").r==TimelineMarker.MissingLocationColor.r&&block.GetFloat("_Blink")==1,"World endpoint uses red GPU blink");
                Check(tick.sortingOrder>30&&(tick.GetComponent<Collider>()==null||!tick.GetComponent<Collider>().enabled),"Warning renders over thumb without blocking input");
                int pool=controls.PoolBuilds;nav.transform.position+=Vector3.right*3;advance.Invoke(controls,new object[]{.04f});
                Check(controls.GetComponentsInChildren<Renderer>(true).Any(r=>{r.GetPropertyBlock(block);return r.name=="Pooled panel element"&&block.GetFloat("_Blink")==1&&block.GetColor("_Color").r==TimelineMarker.MissingLocationColor.r;}),"Fading panel copies retain red blink");
                var updated=original.Select(m=>new TimelineMarker(m.Year,m.Generated,m.LocationMove,m.Generating)).ToArray();map[id]=updated;refresh.Invoke(g,new object[]{id});tick.GetPropertyBlock(block);
                Check(block.GetFloat("_Blink")==0,"Clearing warning disables shader blink");Check(controls.PoolBuilds==pool,"Warning state changes reuse pooled panels");
                var generated=original.Select((m,i)=>new TimelineMarker(m.Year,i==index||m.Generated,m.LocationMove,m.Generating,m.MissingLocation)).ToArray();map[id]=generated;refresh.Invoke(g,new object[]{id});tick.GetPropertyBlock(block);
                Check(block.GetFloat("_Blink")==0&&block.GetColor("_Color").r==g.gold.color.r,"Generated world endpoint stops red blink and turns gold");
                map[id]=original;refresh.Invoke(g,new object[]{id});
                g.Select(id);g.SetYear(id,original[index].Year);if(!g.Overlay.TimelineOpen)g.Overlay.ToggleTimeline();
                var scene=g.Overlay;var mode=typeof(SceneOverlay).GetMethod("ConfigureForMode",Hidden);var draw=typeof(SceneOverlay).GetMethod("Refresh",Hidden);
                foreach(bool vr in new[]{false,true})
                {
                    mode.Invoke(scene,new object[]{vr});draw.Invoke(scene,null);var images=(List<Image>)typeof(SceneOverlay).GetField("tickImages",Hidden).GetValue(scene);var image=images[index];
                    Check(image.color.r==TimelineMarker.MissingLocationColor.r&&image.color.g==TimelineMarker.MissingLocationColor.g,"Desktop/Quest warning stays red while blinking");
                    var thumb=scene.GetComponentsInChildren<RectTransform>(true).Single(t=>t.name=="Selected year");Check(image.transform.GetSiblingIndex()>thumb.GetSiblingIndex(),"Endpoint warning remains above selected thumb");
                    Check(((List<Image>)typeof(SceneOverlay).GetField("blinkingTicks",Hidden).GetValue(scene)).Contains(image),"Warning participates in blink updates");
                    map[id]=generated;draw.Invoke(scene,null);Check(image.color.r!=TimelineMarker.MissingLocationColor.r&&!((List<Image>)typeof(SceneOverlay).GetField("blinkingTicks",Hidden).GetValue(scene)).Contains(image),"Generated desktop/Quest endpoint becomes steady gold");
                    map[id]=original;draw.Invoke(scene,null);
                }
                string result=n+" missing-location marker Play checks passed; no family/cache edits or Meta calls.";File.WriteAllText("Artifacts/missing-location-play.txt",result);return result;
            }
            finally{map[id]=original;g.SetYear(id,year);if(selected==null)g.ShowTree();else g.Select(selected);g.Hud.SetVisible(hud);nav.transform.SetPositionAndRotation(position,rotation);nav.view.transform.localRotation=look;if(hadPref)PlayerPrefs.SetInt(pref,oldPref);else PlayerPrefs.DeleteKey(pref);PlayerPrefs.Save();}
        }
    }
}
