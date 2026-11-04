using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace AncestorWalk.Editor
{
    // Never ship a partial demo that silently needs paid requests on first launch.
    public sealed class ProductDemoBundleBuild : IPreprocessBuildWithReport
    {
        public int callbackOrder=>-100;
        public void OnPreprocessBuild(BuildReport report)
        {
            if(File.Exists(Path.Combine("Library","DemoAuthoring","pending")))throw new BuildFailedException("Save pending demo edits for judging before building.");
            try{Verify();}
            catch(Exception e)
            {
                string message="Fictional demo bundle is incomplete: "+e.Message;
                if((report.summary.options&UnityEditor.BuildOptions.Development)!=0)Debug.LogWarning(message+" Development build continues without the full demo.");
                else throw new BuildFailedException(message);
            }
        }
        public static string Verify()
        {
            if(File.Exists(Path.Combine("Library","DemoAuthoring","pending")))throw new InvalidDataException("Demo edits are awaiting export. Use Save demo for judging now before building.");
            string root=Path.Combine(Application.streamingAssetsPath,BundledDemoScenes.Folder);
            var manifest=JsonUtility.FromJson<BundledDemoScenes.Manifest>(File.ReadAllText(Path.Combine(root,"manifest.json")));
            string tree=PanoramaCache.ProductDemoTreeId;
            if(manifest?.version==3)return VerifyAuthored(root,manifest,tree);
            var catalog=ProductDemoData.Current;
            if(manifest==null||manifest.version!=2||manifest.treeId!=tree||manifest.sceneYear!=catalog.sceneYear)throw new InvalidDataException("Unexpected bundle identity/year.");
            var people=GedcomImporter.Parse(Resources.Load<TextAsset>("ProductDemoFamily").text).People;
            var expected=new HashSet<string>();
            foreach(var p in people.Values)
            {
                if(!LifeTimeline.Year(p.BirthDate,out int birth))throw new InvalidDataException("Missing demo birth year.");
                int end=manifest.sceneYear;if(!LifeTimeline.IsLiving(p)&&!LifeTimeline.Year(p.DeathDate,out end))throw new InvalidDataException("Missing demo death year.");
                expected.Add(p.Id+"/"+birth);expected.Add(p.Id+"/"+end);
            }
            var places=catalog.endpoints.ToDictionary(e=>e.personId+"/"+e.year,e=>e.place);
            if(expected.Count!=74||!expected.SetEquals(places.Keys))throw new InvalidDataException("Expected 74 catalog timeline endpoints.");
            var omissions=catalog.omittedScenes.ToDictionary(e=>e.personId+"/"+e.year,e=>e.reason);
            foreach(var omission in manifest.omittedScenes??Array.Empty<BundledDemoScenes.Omission>())
            {
                string key=omission.personId+"/"+omission.year;
                if(!omissions.Remove(key,out string reason)||omission.reason!=reason||!expected.Remove(key))throw new InvalidDataException("Unexpected/duplicate omitted endpoint.");
            }
            if(omissions.Count!=0)throw new InvalidDataException("Missing documented omission.");
            if(manifest.scenes?.Length!=expected.Count)throw new InvalidDataException("Missing bundled endpoint panoramas.");
            long bytes=0;
            foreach(var entry in manifest.scenes)
            {
                var r=entry.record;
                string key=entry.personId+"/"+r.year;
                if(!expected.Remove(key)||r.version!=1||r.treeId!=tree||r.personKey!=PanoramaCache.PersonKey(entry.personId)||r.place!=places[key]||r.model==null||!r.model.ToLowerInvariant().Contains("muse")||SceneReference.Optional(r.reference)!=null||SceneReference.Optional(r.basePanorama)!=null)throw new InvalidDataException("Unexpected scene or private reference.");
                TreeStore.CheckId(r.sha256);TreeStore.CheckId(entry.thumbnailSha256);
                if(r.file!=r.sha256+".png")throw new InvalidDataException("Invalid image path.");
                Check(Path.Combine(root,"images",r.file),r.sha256);Check(Path.Combine(root,"previews",r.sha256+".rgba"),entry.thumbnailSha256);
                if(new FileInfo(Path.Combine(root,"previews",r.sha256+".rgba")).Length!=524352)throw new InvalidDataException("Invalid preview size.");
                bytes+=new FileInfo(Path.Combine(root,"images",r.file)).Length+524352;
            }
            var portraits=catalog.portraits.ToDictionary(p=>p.personId,p=>p);
            if(portraits.Count!=8||manifest.portraits?.Length!=8)throw new InvalidDataException("Expected eight fictional portraits.");
            foreach(var portrait in manifest.portraits)
            {
                if(!portraits.Remove(portrait.personId,out var association)||association.photo==null||portrait.model!="muse-image-1.0"||JsonUtility.ToJson(association.photo)!=JsonUtility.ToJson(portrait.photo)||portrait.prompt!=association.prompt)throw new InvalidDataException("Portrait association/provenance mismatch.");
                var photo=portrait.photo;TreeStore.CheckId(photo.Id);
                if(photo.File!=photo.Id+".png"||photo.Width!=photo.Height||photo.Orientation!=1)throw new InvalidDataException("Invalid fictional portrait.");
                string path=Path.Combine(root,"portraits",photo.File);Check(path,photo.Id);bytes+=new FileInfo(path).Length;
            }
            return manifest.scenes.Length+" fictional Meta panoramas and previews, eight portraits verified; "+(manifest.omittedScenes?.Length??0)+" documented empty endpoints; "+bytes+" bytes.";
        }
        static string VerifyAuthored(string root,BundledDemoScenes.Manifest manifest,string tree)
        {
            string path=Path.Combine(Application.dataPath,"AncestorWalk/Resources/"+ProductDemoAuthoring.ResourceName+".json");
            Check(path,manifest.documentSha256);
            var snapshot=JsonUtility.FromJson<ProductDemoAuthoring.Snapshot>(File.ReadAllText(path));ProductDemoAuthoring.Validate(snapshot,tree);
            BundledDemoScenes.ValidateManifest(manifest,tree,snapshot.document.People.ToArray());
            var photos=snapshot.document.Photos.ToDictionary(p=>p.Id);
            foreach(var entry in manifest.portraits)
            {
                if(!photos.Remove(entry.photo.Id,out var photo)||JsonUtility.ToJson(photo)!=JsonUtility.ToJson(entry.photo)||!string.IsNullOrEmpty(photo.OriginalPath))throw new InvalidDataException("Authored demo photo mismatch.");
                Check(Path.Combine(root,"portraits",photo.File),photo.Id);
            }
            if(photos.Count!=0)throw new InvalidDataException("Missing authored demo photo.");
            foreach(var entry in manifest.scenes)
            {
                var record=entry.record;Check(Path.Combine(root,"images",record.file),record.sha256);Check(Path.Combine(root,"previews",record.sha256+".rgba"),entry.thumbnailSha256);
                if(new FileInfo(Path.Combine(root,"previews",record.sha256+".rgba")).Length!=524352)throw new InvalidDataException("Invalid preview size.");
                foreach(var reference in new[]{SceneReference.Optional(record.reference),SceneReference.Optional(record.basePanorama)})
                {
                    if(reference==null)continue;SceneReference.Validate(reference,record.place);
                    if(reference.IsPhoto){if(!snapshot.document.Photos.Any(p=>p.Id==reference.sha256&&p.File==reference.file))throw new InvalidDataException("Unattached demo reference photo.");Check(Path.Combine(root,"portraits",reference.file),reference.sha256);}
                    else Check(Path.Combine(root,"images",reference.sha256+".png"),reference.sha256);
                }
            }
            return snapshot.document.People.Count+" authored demo people, "+manifest.scenes.Length+" scenes and "+manifest.portraits.Length+" photos verified.";
        }
        static void Check(string path,string expected)
        {using var file=File.OpenRead(path);if(file.Length>32L*1024*1024)throw new InvalidDataException("Asset exceeds limit.");using var sha=SHA256.Create();if(BitConverter.ToString(sha.ComputeHash(file)).Replace("-","").ToLowerInvariant()!=expected)throw new InvalidDataException("Asset checksum mismatch.");}
    }
}
