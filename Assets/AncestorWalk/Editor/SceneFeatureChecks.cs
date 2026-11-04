using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace AncestorWalk.Editor
{
    public static class SceneFeatureChecks
    {
        public static string Run()
        {
            var gallery=UnityEngine.Object.FindAnyObjectByType<AncestorGallery>();
            if(gallery==null || !Application.isPlaying)throw new Exception("Run in gallery Play mode.");
            int count=0;
            void Check(bool pass,string message){if(!pass)throw new Exception(message);count++;}
            gallery.ShowTree();
            Check(gallery.HasScene("@I1@"),"Clara cache missing");
            var original=PanoramaCache.Read(out _);var migrated=PanoramaCache.ReadFor(gallery.TreeId,"@I1@",out var record);
            Check(PanoramaCache.Hash(original)==PanoramaCache.Hash(migrated),"Clara migration changed image");
            Check(record.personKey==PanoramaCache.PersonKey("@I1@"),"Person identity mismatch");

            var buttons=UnityEngine.Object.FindObjectsByType<GalleryTarget>(FindObjectsSortMode.None);
            Check(buttons.Count(x=>x.personId=="@I1@"&&!x.isReturn)==1,"Clara must have only Enter Scene");
            Check(buttons.Count(x=>x.personId=="@I3@"&&!x.isReturn)==1,"Rose must always have Enter Scene");
            Physics.SyncTransforms();
            foreach(var button in buttons)
            {
                var origin=button.transform.position-Vector3.forward*.6f;var direction=Vector3.forward;
                Check(Physics.Raycast(origin,direction.normalized,out var hit,15) && hit.collider.gameObject==button.gameObject,"Button raycast blocked: "+button.name);
            }
            var sky=RenderSettings.skybox;
            string config=Path.Combine(PanoramaCache.StorageRoot,"scene-service.json");string backup=config+".check-backup";
            if(File.Exists(backup))throw new Exception("Existing connection backup requires inspection");
            bool moved=File.Exists(config);
            if(moved)File.Move(config,backup);
            try
            {
                gallery.Select("@I1@");Check(gallery.SelectedId=="@I1@","Offline entry failed");
                gallery.ShowTree();Check(RenderSettings.skybox==sky,"Background restore failed");
                gallery.Select("@I3@");gallery.GenerateScene("@I3@");Check(!gallery.Generation.Busy && gallery.Generation.Message("@I3@").Contains("helper"),"Missing configuration not reported");
            }
            finally {if(moved)File.Move(backup,config);}
            string fixtureTree=PanoramaCache.Hash(System.Text.Encoding.UTF8.GetBytes(Guid.NewGuid().ToString()));
            string fixtureRoot=Path.Combine(PanoramaCache.DirectoryPath,"people",fixtureTree);
            try
            {
                PanoramaCache.SaveFor(fixtureTree,"@test@",original,"fixture","test-fixture");
                Check(PanoramaCache.HasFor(fixtureTree,"@test@"),"Cache write/read failed");
                Check(!PanoramaCache.HasFor(fixtureTree,"@other@"),"Person cache leaked");
                Check(!PanoramaCache.HasFor(gallery.TreeId,"@test@"),"Tree cache leaked");
                try{PanoramaCache.SaveFor(fixtureTree,"@test@",new byte[]{1},"bad","test");throw new Exception("Invalid image accepted");}catch(InvalidDataException){}
                Check(PanoramaCache.HasFor(fixtureTree,"@test@"),"Failed replacement lost old scene");
                var imagePath=Path.Combine(fixtureRoot,PanoramaCache.PersonKey("@test@"),PanoramaCache.Hash(original)+".png");
                File.WriteAllBytes(imagePath,new byte[]{1,2,3});Check(!PanoramaCache.HasFor(fixtureTree,"@test@"),"Corrupt scene advertised as usable");
            }
            finally {if(Directory.Exists(fixtureRoot))Directory.Delete(fixtureRoot,true);}
            gallery.ShowTree();
            var result=count+" scene feature checks passed (no paid requests).";
            Directory.CreateDirectory("Artifacts");File.WriteAllText("Artifacts/scene-feature-checks.txt",result);return result;
        }
    }
}
