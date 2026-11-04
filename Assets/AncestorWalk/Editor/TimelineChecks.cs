using System;
using System.IO;
using System.Linq;
using UnityEngine;
namespace AncestorWalk.Editor
{
    public static class TimelineChecks
    {
        public static string Run()
        {
            int count=0;void Check(bool ok,string why){if(!ok)throw new Exception(why);count++;}
            var p=new Person{BirthDate="ABT 1870",DeathDate="1941"};
            Check(LifeTimeline.TryRange(p,out int birth,out int end)&&birth==1870&&end==1941,"Lifespan parsing");
            p.DeathDate="";Check(!LifeTimeline.TryRange(p,out _,out _),"Unknown death must not mean living");
            p.KnownLiving=true;Check(LifeTimeline.TryRange(p,out _,out end)&&end==DateTime.Now.Year,"Living range");
            p.BirthDate="BET 1870 AND 1875";Check(!LifeTimeline.TryRange(p,out _,out _),"Ambiguous birth");
            var exactPerson=new Person{BirthDate="1870",DeathDate="1941"};var residences=new ResidenceTimeline();
            Check(residences.GenerationYear(exactPerson,1873)==1873,"Exact selected generation year");
            Check(residences.GenerationYear(exactPerson,1941)==1941,"Final generation year");
            Check(residences.GenerationYear(new Person{BirthDate="1870",DeathDate="1870"},1870)==1870,"Single-year lifespan");
            LifeTimeline.Pair(new[]{1870,1900},1885,out int a,out int b,out float blend);
            Check(a==1870&&b==1900&&Mathf.Approximately(blend,.5f),"Midpoint blend");
            foreach(var gap in new[]{20,100})
            {
                int start=1800;
                LifeTimeline.Pair(new[]{start,start+gap},start+gap*35/100,out a,out b,out blend);Check(Mathf.Approximately(blend,.15f),"First 35% retains 15% next scene for gap "+gap);
                LifeTimeline.Pair(new[]{start,start+gap},start+gap/2,out a,out b,out blend);Check(Mathf.Approximately(blend,.5f),"Middle blends for gap "+gap);
                LifeTimeline.Pair(new[]{start,start+gap},start+gap*65/100,out a,out b,out blend);Check(Mathf.Approximately(blend,.85f),"Final 35% retains 15% previous scene for gap "+gap);
                for(int year=start+1;year<start+gap;year++){LifeTimeline.Pair(new[]{start,start+gap},year,out a,out b,out blend);Check(blend>=.15f&&blend<=.85f,"Neither scene fully opaque between markers");}
                foreach(int year in new[]{start,start+gap}){LifeTimeline.Pair(new[]{start,start+gap},year,out a,out b,out blend);Check(a==year&&b==year&&blend==0,"Exact marker is fully in place");}
            }
            LifeTimeline.Pair(new[]{1800,1840,1860},1850,out a,out b,out blend);Check(a==1840&&b==1860&&Mathf.Approximately(blend,.5f),"Added entry uses adjacent pair");
            LifeTimeline.Pair(new[]{1870,1900},1941,out a,out b,out blend);Check(a==1900&&b==1900&&blend==0,"Beyond cached range");
            LifeTimeline.Pair(new[]{1900},1880,out a,out b,out blend);Check(a==1900&&b==1900,"Single cached year");
            var g=UnityEngine.Object.FindAnyObjectByType<AncestorGallery>();if(g==null||!Application.isPlaying)throw new Exception("Run in Play mode");
            var bytes=PanoramaCache.ReadFor(g.TreeId,"@I1@",out _);
            var migrated=PanoramaCache.ReadYear(g.TreeId,"@I1@",1870,out _);Check(PanoramaCache.Hash(bytes)==PanoramaCache.Hash(migrated),"Migration changed old panorama");
            string tree=PanoramaCache.Hash(System.Text.Encoding.UTF8.GetBytes(Guid.NewGuid().ToString()));
            var fixtureRoot=Path.Combine(PanoramaCache.DirectoryPath,"people",tree);
            try
            {
                PanoramaCache.SaveYear(tree,"fixture",1900,bytes,"fixture","test");
                PanoramaCache.SaveYear(tree,"fixture",1905,bytes,"fixture","test");
                Check(PanoramaCache.Years(tree,"fixture").SequenceEqual(new[]{1900,1905}),"Year cache enumeration");
                Check(PanoramaCache.Years(tree,"other").Length==0,"Person isolation");
                Check(PanoramaCache.Years(g.TreeId,"fixture").Length==0,"Tree isolation");
                try {PanoramaCache.SaveYear(tree,"fixture",1900,new byte[]{1},"bad","test");throw new Exception("Invalid accepted");}catch(InvalidDataException){}
                Check(PanoramaCache.Hash(PanoramaCache.ReadYear(tree,"fixture",1900,out _))==PanoramaCache.Hash(bytes),"Failed regen lost cache");
                Check(PanoramaCache.ReadYear(tree,"fixture",1905,out var rec).Length>0&&rec.year==1905,"Other year preserved");
            }
            finally{if(Directory.Exists(fixtureRoot))Directory.Delete(fixtureRoot,true);}
            g.ShowTree();Physics.SyncTransforms();
            var sliders=UnityEngine.Object.FindObjectsByType<YearSlider>(FindObjectsSortMode.None);Check(sliders.Length==3,"Gallery sliders");
            foreach(var slider in sliders)
            {
                var origin=Camera.main.transform.position;var dir=slider.transform.position-origin;
                Check(Physics.Raycast(origin,dir.normalized,out var hit,15)&&YearSlider.Resolve(hit.collider)==slider,"Slider ray blocked");
            }
            var sky=RenderSettings.skybox;g.SetYear("@I1@",1885);g.Select("@I1@");
            Check(g.SelectedId=="@I1@","Enter saved timeline");
            Check(Mathf.Approximately(RenderSettings.skybox.GetFloat("_Blend"),.5f),"Runtime shader blend");
            Check(g.Overlay!=null,"In-scene overlay exists");
            g.SetYear("@I1@",1900);
            g.SetYear("@I1@",1885);
            Check(PlayerPrefs.GetInt("year."+g.TreeId+".@I1@",0)==1885,"Selection persistence");
            g.ShowTree();Check(RenderSettings.skybox==sky,"Gallery restores skybox");
            g.Select("@I1@");
            string result=count+" timeline checks passed; no paid requests.";File.WriteAllText("Artifacts/Timeline/checks.txt",result);return result;
        }
    }
}
