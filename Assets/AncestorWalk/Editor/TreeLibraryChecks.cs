using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace AncestorWalk.Editor
{
    public static class TreeLibraryChecks
    {
        const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        const string Ged="0 HEAD\n1 CHAR UTF-8\n1 GEDC\n2 VERS 7.0\n0 @I1@ INDI\n1 NAME Zoë /Example/\n1 BIRT\n2 DATE ABT 1900\n2 PLAC Reykjavík, Iceland\n1 DEAT\n2 DATE 1980\n1 OBJE @M1@\n0 @I2@ INDI\n1 NAME Sam /Example/\n1 BIRT\n2 DATE 1930\n2 PLAC Kyoto, Japan\n1 DEAT\n2 DATE 2000\n1 OBJE\n2 FILE media/inline.jpg\n3 TITL Childhood photograph\n2 DATE 1936\n0 @F1@ FAM\n1 HUSB @I1@\n1 CHIL @I2@\n0 @M1@ OBJE\n1 FILE media/portrait.png\n2 TITL Family portrait\n1 NOTE Original photo caption\n0 TRLR";
        static byte[] Image(bool jpg=false)
        {var t=new Texture2D(64,96,TextureFormat.RGBA32,false);var pixels=new Color[64*96];for(int y=0;y<96;y++)for(int x=0;x<64;x++)pixels[y*64+x]=x<32?Color.red:Color.blue;t.SetPixels(pixels);t.Apply();var data=jpg?t.EncodeToJPG():t.EncodeToPNG();UnityEngine.Object.DestroyImmediate(t);return data;}
        static void Zip(string path,params (string Name,byte[] Bytes)[] entries)
        {using var zip=new ZipArchive(File.Create(path),ZipArchiveMode.Create);foreach(var pair in entries){var e=zip.CreateEntry(pair.Name);using var s=e.Open();s.Write(pair.Bytes,0,pair.Bytes.Length);}}
        public static string Data()
        {
            int n=0;void Check(bool ok,string why){if(!ok)throw new Exception(why);n++;}void Reject(Action action,string why){bool rejected=false;try{action();}catch{rejected=true;}Check(rejected,why);}
            string root=Path.GetFullPath("Library/TreeLibraryValidation/"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var store=new TreeStore(Path.Combine(root,"storage"));var png=Image();var jpg=Image(true);
            try
            {
                var empty=new TreeDocument{Id=TreeStore.NewId(),Name="Empty fixture"};store.Save(empty);Check(store.Load(empty.Id).People.Count==0,"Empty trees persist");
                var p=new Person{Id="stable-person",Name="A person",BirthDate="1950",KnownLiving=true,BirthPlace="Oslo"};empty.People.Add(p);store.Save(empty);string id=empty.Id;string personId=p.Id;p.Name="Renamed";store.Save(empty);
                Check(store.Load(id).People[0].Name=="Renamed"&&empty.Id==id&&p.Id==personId,"Edits retain tree/person identity");
                var unknown=new Person{Id="unknown",Name="Dates unknown"};empty.People.Add(unknown);store.Save(empty);Check(!LifeTimeline.TryRange(unknown,out _,out _),"Missing dates remain unknown");
                empty.Families.Add(new FamilyRecord{Id="family",Parents=new List<string>{p.Id},Children=new List<string>{unknown.Id}});store.Save(empty);Check(store.Load(id).ToTree().Families[0].Children[0]==unknown.Id,"Family edges round trip");
                var cycle=empty.Copy();cycle.Families.Add(new FamilyRecord{Id="cycle",Parents=new List<string>{unknown.Id},Children=new List<string>{p.Id}});Reject(()=>store.Save(cycle),"Cycle rejected");
                Check(store.Load(id).Families.Count==1,"Failed edit preserves saved tree");
                var missing=empty.Copy();missing.Families[0].Parents.Add("missing");Reject(()=>store.Save(missing),"Editing rejects dangling edges");
                Reject(()=>TreeStore.CheckId("../outside"),"Tree path traversal rejected");
                foreach(var path in new[]{"../evil.png","/evil.png","C:\\evil.png","a/../../evil.png","a\0.png"})Reject(()=>TreeArchiveImport.SafeRelative(path),"Unsafe archive path rejected");
                Check(TreeArchiveImport.SafeRelative("media\\photo.png")=="media/photo.png","Windows relative separators normalized");
                PhotoHeader.Read(png,out int width,out int height,out int orientation);Check(width==64&&height==96&&orientation==1,"PNG header dimensions");
                PhotoHeader.Read(jpg,out width,out height,out orientation);Check(width==64&&height==96,"JPEG header dimensions");Reject(()=>PhotoHeader.Read(new byte[100],out _,out _,out _),"Invalid image rejected");
                var huge=(byte[])png.Clone();huge[16]=127;Reject(()=>PhotoHeader.Read(huge,out _,out _,out _),"Oversize decode dimensions rejected");
                var links=GedcomMedia.Parse(Ged);Check(links.Count==2&&links[0].PersonId=="@I1@"&&links[1].PersonId=="@I2@","Shared and inline photo links parsed");
                Check(links[0].Title=="Family portrait"&&links[0].Description=="Original photo caption"&&links[1].Date=="1936","Photo title/date/caption preserved");
                string archive=Path.Combine(root,"Synthetic family.gdz");Zip(archive,("gedcom.ged",Encoding.UTF8.GetBytes(Ged)),("media/portrait.png",png),("media/inline.jpg",jpg),("media/duplicate.png",png));
                int worker=Thread.CurrentThread.ManagedThreadId;var staged=Task.Run(()=>{worker=Thread.CurrentThread.ManagedThreadId;return TreeArchiveImport.Stage(store,archive);}).GetAwaiter().GetResult();
                Check(worker!=Thread.CurrentThread.ManagedThreadId,"Archive staging runs on a worker");Check(staged.Document.People.Count==2&&staged.Document.Families.Count==1,"Archive imports graph");
                Check(staged.Document.Photos.Count==2,"Identical photo bytes deduplicated");Check(staged.Document.People.All(x=>x.PhotoIds.Count==1&&!string.IsNullOrEmpty(x.PrimaryPhotoId)),"Images assigned to people");
                Check(staged.Document.People[0].Name=="Zoë Example"&&staged.Document.People[0].BirthDate=="ABT 1900","Unicode and uncertain dates preserved");
                Check(!Directory.Exists(store.DocumentDirectory(staged.Document.Id))&&store.List().Count==1,"Staging is not published before confirmation");
                var imported=TreeArchiveImport.Commit(store,staged);Check(store.List().Count==2&&store.Load(imported.Id).Photos.Count==2,"Confirmed import publishes complete document");
                File.Delete(archive);Check(imported.Photos.All(x=>File.Exists(Path.Combine(store.MediaRoot,x.File)))&&File.Exists(Path.Combine(store.DocumentDirectory(imported.Id),"source.ged")),"Original archive no longer needed");
                imported.Name="Edited import";store.Save(imported);Check(store.Load(imported.Id).People[0].PhotoIds.Count==1,"Photos survive edits and reopen");
                string loose=Path.Combine(root,"loose.ged");File.WriteAllText(loose,Ged);Directory.CreateDirectory(Path.Combine(root,"media"));File.WriteAllBytes(Path.Combine(root,"media","portrait.png"),png);File.WriteAllBytes(Path.Combine(root,"media","inline.jpg"),jpg);
                var looseDraft=TreeArchiveImport.Stage(store,loose);Check(looseDraft.Document.Photos.Count==2,"Loose GEDCOM resolves local referenced images");TreeArchiveImport.Cleanup(store,looseDraft);Check(!Directory.Exists(looseDraft.Directory),"Cancelled preview removes its staging files");
                string multi=Path.Combine(root,"multi.zip");Zip(multi,("one.ged",Encoding.UTF8.GetBytes(Ged)),("two.ged",Encoding.UTF8.GetBytes(Ged)));var choices=TreeArchiveImport.Stage(store,multi);
                Check(choices.Document==null&&choices.GedcomChoices.Count==2&&choices.Directory==null,"Multiple GEDCOM files require selection");var chosen=TreeArchiveImport.Stage(store,multi,"two.ged");Check(chosen.Document.People.Count==2&&chosen.Document.Warnings.Count>=2,"Selected GEDCOM reports missing photos");TreeArchiveImport.Cleanup(store,chosen);
                string bad=Path.Combine(root,"bad.zip");Zip(bad,("../escape.ged",Encoding.UTF8.GetBytes(Ged)));Reject(()=>TreeArchiveImport.Stage(store,bad),"Malicious ZIP path rejected");Check(!File.Exists(Path.Combine(root,"escape.ged")),"No archive escape written");
                Zip(bad,("gedcom.ged",Encoding.UTF8.GetBytes(Ged)),("gedcom.ged",Encoding.UTF8.GetBytes(Ged)));Reject(()=>TreeArchiveImport.Stage(store,bad),"Duplicate archive paths rejected");
                Zip(bad,("gedcom.ged",Encoding.UTF8.GetBytes("broken")));Reject(()=>TreeArchiveImport.Stage(store,bad),"Bad GEDCOM rejected without publishing");
                Check(store.List().Count==2,"Failed imports preserve existing trees");
                using(var cancel=new CancellationTokenSource()){cancel.Cancel();Reject(()=>TreeArchiveImport.Stage(store,multi,null,cancel.Token),"Cancellation stops import");}
                Check(!Directory.GetDirectories(store.Root).Any(d=>Path.GetFileName(d).StartsWith(".import-")),"Failed and cancelled stages cleaned");
                string utf16=Path.Combine(root,"unicode.ged");File.WriteAllText(utf16,Ged.Replace("UTF-8","UNICODE"),Encoding.Unicode);var unicode=TreeArchiveImport.Stage(store,utf16);Check(unicode.Document.People[0].Name=="Zoë Example","UTF16 BOM accepted");TreeArchiveImport.Cleanup(store,unicode);
                File.WriteAllText(utf16,Ged.Replace("UTF-8","ANSEL"));Reject(()=>TreeArchiveImport.Stage(store,utf16),"Unsupported encoding asks for UTF8 export");
                var copied=TreeArchiveImport.AddPhoto(store,Path.Combine(root,"media","portrait.png"));Check(copied.Id==imported.Photos[0].Id,"Manual photo import shares content identity");
                var invalid=empty.Copy();invalid.People[0].DeathDate="1940";Reject(()=>store.Save(invalid),"Death before birth rejected");
                var demo=TreeDocument.FromTree(GedcomImporter.Parse(Resources.Load<TextAsset>("DemoFamily").text),PanoramaCache.DemoTreeId,"Demo",true);Reject(()=>store.Save(demo),"Built-in demo is protected");
                string result=n+" tree library/data checks passed: stable identities, empty/edit/reopen, relationships, cycle/atomic failures, GEDZIP/ZIP/loose GED, UTF8/UTF16, inline/shared photos, originals, deduplication, missing media, archive safeguards and cancellation. Synthetic files only.";
                Directory.CreateDirectory("Artifacts");File.WriteAllText("Artifacts/tree-library-data-checks.txt",result);return result;
            }
            finally{if(Directory.Exists(root))Directory.Delete(root,true);}
        }
        public static string Play()
        {
            var g=UnityEngine.Object.FindAnyObjectByType<AncestorGallery>();if(g==null||!Application.isPlaying)throw new Exception("Play mode required");Directory.CreateDirectory("Artifacts");File.WriteAllText("Artifacts/tree-library-play-checks.txt","RUNNING");g.StartCoroutine(Runner(Checks(g)));return "Running tree library UI/photo checks";
        }
        public static string Access()
        {
            if(!Application.isPlaying)throw new Exception("Play mode required");
            var g=UnityEngine.Object.FindAnyObjectByType<AncestorGallery>();var lib=g.Library;
            if(lib.Busy||g.Generation.Busy)throw new Exception("Wait for current operations before synthetic access checks");
            int n=0;void Check(bool ok,string why){if(!ok)throw new Exception(why);n++;}
            var original=g.Generation;bool hud=g.HudVisible;g.Tutorial.Dismiss();lib.Close();
            var holder=new GameObject("Inactive library access generation fixture");holder.SetActive(false);var fake=holder.AddComponent<SceneGeneration>();
            var workers=(IDictionary)typeof(SceneGeneration).GetField("workers",Private).GetValue(fake);
            var pending=new PendingScene{treeId=g.TreeId,job=new SceneJob{requestId=new string('a',32)}};
            workers.Add(pending,Activator.CreateInstance(typeof(SceneGeneration).GetNestedType("Worker",BindingFlags.NonPublic),true));
            typeof(AncestorGallery).GetProperty("Generation").SetValue(g,fake);
            try
            {
                foreach(bool paused in new[]{false,true})
                {
                    var queue=(SceneQueueState)typeof(SceneGeneration).GetField("queue",Private).GetValue(fake);queue.automaticPaused=paused;
                    typeof(TreeLibraryOverlay).GetMethod("Configure",Private).Invoke(lib,new object[]{true});
                    var launcher=(RectTransform)typeof(TreeLibraryOverlay).GetField("launcher",Private).GetValue(lib);launcher.gameObject.SetActive(true);Canvas.ForceUpdateCanvases();
                    var ray=new Ray(launcher.position-launcher.forward,launcher.forward);
                    Check(lib.HandleInput(ray,true,true,false)&&lib.IsOpen,"VR Trees trigger opens during active generation");
                    Check(lib.GetComponent<Canvas>().renderMode==RenderMode.WorldSpace&&lib.transform.parent==Camera.main.transform,"Library is camera relative in VR");
                    typeof(TreeLibraryOverlay).GetMethod("Update",Private).Invoke(lib,null);
                    var status=(Text)typeof(TreeLibraryOverlay).GetField("status",Private).GetValue(lib);
                    Check(status.gameObject.activeInHierarchy&&status.text.Contains("Finishing 1"),"Active generation has visible feedback");
                    bool rejected=false;try{typeof(TreeLibraryOverlay).GetMethod("Save",Private).Invoke(lib,new object[]{g.Document,null});}catch(TargetInvocationException e){rejected=e.InnerException is InvalidOperationException;}
                    Check(rejected&&!lib.Busy,"Save is blocked before any document write while generation is active");
                    Check(fake.QueuePaused==paused&&fake.ActiveCount==1,"Library preserves pause preference and active requests");
                    lib.HandleInput(ray,true,false,true);Check(!lib.IsOpen,"Quest Back closes the library");
                }
                workers.Clear();lib.Open();Check(lib.IsOpen,"Idle library opens normally");lib.Close();
                string result=n+" tree-library access checks passed: VR launcher while generating, visible feedback, save guard, pause/active preservation and Back. No network requests or family writes.";
                File.WriteAllText("Artifacts/tree-library-access-checks.txt",result);return result;
            }
            finally
            {lib.Close();workers.Clear();typeof(AncestorGallery).GetProperty("Generation").SetValue(g,original);UnityEngine.Object.DestroyImmediate(holder);g.Hud.SetVisible(hud);typeof(TreeLibraryOverlay).GetMethod("Configure",Private).Invoke(lib,new object[]{UnityEngine.XR.XRSettings.isDeviceActive});}
        }
        static IEnumerator Runner(IEnumerator tests)
        {while(true){object next=null;bool more=false;Exception error=null;try{more=tests.MoveNext();if(more)next=tests.Current;}catch(Exception e){error=e;}if(error!=null){File.WriteAllText("Artifacts/tree-library-play-checks.txt","FAILED: "+error);Debug.LogException(error);yield break;}if(!more)yield break;yield return next;}}
        static IEnumerator Checks(AncestorGallery g)
        {
            int n=0;void Check(bool ok,string why){if(!ok)throw new Exception(why);n++;}
            var lib=g.Library;var old=g.Document;bool oldHud=g.HudVisible;string created=null,importedId=null;string fixture=Path.GetFullPath("Library/TreeLibraryValidation/play-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(fixture);
            void Click(string text)
            {
                var hits=(IEnumerable)typeof(TreeLibraryOverlay).GetField("hits",Private).GetValue(lib);RectTransform found=null;
                foreach(var hit in hits){var r=(RectTransform)hit.GetType().GetField("Rect").GetValue(hit);if(r.name==text||r.name.StartsWith(text,StringComparison.Ordinal)){found=r;break;}}
                if(found==null)throw new Exception("Button not found: "+text);var ray=new Ray(found.position-found.forward*10,found.forward);lib.HandleInput(ray,true,true,false);
            }
            void Enter(string value){Click("Clear");int objects=lib.GetComponentsInChildren<Transform>(true).Length;var method=typeof(TreeLibraryOverlay).GetMethod("Type",Private);foreach(char c in value)method.Invoke(lib,new object[]{c});Check(lib.GetComponentsInChildren<Transform>(true).Length==objects,"Typing updates one label without rebuilding keyboard objects");Click("Done");}
            try
            {
                while(lib.Busy)yield return null;g.Tutorial.Dismiss();lib.Open();yield return null;Canvas.ForceUpdateCanvases();Check(lib.IsOpen,"Library opens");
                Click("New Tree");Click("Name:");Enter("Synthetic UI family");Click("Create tree");while(lib.Busy)yield return null;created=g.TreeId;
                Check(g.Document.Name=="Synthetic UI family"&&g.Tree.People.Count==0&&g.IsTree,"Create empty tree through UI");
                lib.Open();Click("Edit current tree:");
                Click("Add person");Click("Name:");Enter("Alex Example");Click("Birth date:");Enter("1900");Click("Birth location:");Enter("Kyoto, Japan");Click("Death date:");Enter("1980");Click("Save person");while(lib.Busy)yield return null;
                Check(g.Tree.People.Count==1&&g.Document.People[0].Name=="Alex Example","Person form saves");string first=g.Document.People[0].Id;
                Click("Add person");Click("Name:");Enter("Blair Example");Click("Birth date:");Enter("1930");Click("Death date:");Enter("2000");Click("Save person");while(lib.Busy)yield return null;Check(g.Tree.People.Count==2,"Additional person saved");
                Click("Alex Example");Click("Relationships");Click("New family group");Click("Children");Click("Add child");Click("Blair Example");Click("Save family");while(lib.Busy)yield return null;
                Check(g.Tree.Families.Count==1&&g.Tree.Families[0].Parents.Contains(first)&&g.Tree.Families[0].Children.Count==1,"Family editor saves parent/child links");
                Check(g.Layout.Positions.Values.Max(p=>p.z)>g.Layout.Positions.Values.Min(p=>p.z),"New relationships update generation layout");
                lib.Close();g.OpenDocument(lib.Store.Load(created));Check(g.Tree.People.Count==2&&g.Tree.Families.Count==1&&g.TreeId==created,"Saved tree reopens with stable ID");
                string archive=Path.Combine(fixture,"Synthetic photo family.gdz");var png=Image();Zip(archive,("gedcom.ged",Encoding.UTF8.GetBytes(Ged)),("media/portrait.png",png),("media/inline.jpg",Image(true)));
                lib.ImportPath(archive);while(lib.Busy)yield return null;Check((string)typeof(TreeLibraryOverlay).GetField("page",Private).GetValue(lib)=="preview","Import presents preview");Check(g.TreeId==created,"Import does not replace active tree before confirmation");
                Click("Import tree");while(lib.Busy)yield return null;importedId=g.TreeId;yield return null;
                Check(!lib.IsOpen&&g.Tree.People.Count==2&&g.Document.Photos.Count==2,"Confirmed import opens graph and photos");File.Delete(archive);
                var portraits=g.GetComponentsInChildren<PhotoPortrait>();Check(portraits.Length==2,"Primary portraits attach to sphere panels");
                var navigation=UnityEngine.Object.FindAnyObjectByType<GalleryNavigation>();foreach(var person in g.Document.People)
                {navigation.FrameNode(person.Id);yield return new WaitForSecondsRealtime(.4f);float until=Time.realtimeSinceStartup+20;var portrait=portraits.Single(p=>p.name=="Person sphere "+person.Id);while(portrait.Surface.sharedMaterial.mainTexture==Texture2D.grayTexture&&Time.realtimeSinceStartup<until)yield return null;Check(portrait.Surface.sharedMaterial.mainTexture!=Texture2D.grayTexture,"Visible portrait loads asynchronously");}
                Check(portraits.All(p=>p.Surface.sharedMaterial.shader.name=="AncestorWalk/Person Photo"),"Pooled fade handling preserves photo materials");
                var firstPortrait=portraits[0];navigation.FrameNode(g.Document.People[0].Id);yield return new WaitForSecondsRealtime(.4f);int photoReads=g.Photos.Reads;
                navigation.transform.position+=Vector3.back*100;yield return new WaitForSecondsRealtime(.4f);navigation.FrameNode(g.Document.People[0].Id);yield return new WaitForSecondsRealtime(.4f);
                Check(firstPortrait.Surface.sharedMaterial.mainTexture!=Texture2D.grayTexture&&g.Photos.Reads==photoReads,"Portrait reappears from warm cache after distance fade");
                var p0=g.Document.People[0];g.Select(p0.Id);lib.OpenPhotos(p0.Id);yield return null;float deadline=Time.realtimeSinceStartup+20;var raw=lib.GetComponentInChildren<RawImage>();while(raw!=null&&raw.texture==null&&Time.realtimeSinceStartup<deadline)yield return null;
                Check(raw!=null&&raw.texture!=null&&raw.texture.width==1024,"Scene photo browser loads large preview");Check(g.Photos.UnusedCount<=32,"Photo texture unused budget stays bounded");
                Click("Use as portrait");while(lib.Busy)yield return null;Check(g.SelectedId==p0.Id&&!g.IsTree,"Choosing a portrait inside a scene preserves the scene");
                lib.Close();g.ShowTree();yield return null;Check(g.Document.Photos.All(p=>File.Exists(Path.Combine(lib.Store.MediaRoot,p.File))),"Photos remain usable after deleting ZIP and switching views");
                while(!g.CacheIndexReady)yield return null;
                var cachedPerson=g.Document.People[0];PanoramaCache.SaveYear(g.TreeId,cachedPerson.Id,1910,File.ReadAllBytes("Assets/AncestorWalk/Resources/ClaraYork1870Panorama.png"),"fixture","fixture",place:cachedPerson.BirthPlace);g.RefreshPerson(cachedPerson.Id);
                long reads=PanoramaCache.ImageReadCount,hashes=PanoramaCache.ImageValidationCount;var edit=g.Document.Copy();edit.People[0].Name="Renamed cached person";g.OpenDocument(edit);yield return null;
                Check(PanoramaCache.ImageReadCount==reads&&PanoramaCache.ImageValidationCount==hashes&&g.CachedYears(cachedPerson.Id).Contains(1910),"Editing a tree reuses validated panorama records without PNG reads/hashes");
                var big=new TreeDocument{Id=TreeStore.NewId(),Name="Large synthetic fixture"};for(int i=0;i<100;i++)big.People.Add(new Person{Id="big-"+i,Name="Person "+i,BirthDate="1900",DeathDate="1980"});
                g.OpenDocument(big);Check(g.IsBuildingTree,"Large tree starts async construction");int previous=0,frames=0;deadline=Time.realtimeSinceStartup+40;
                while(g.IsBuildingTree&&Time.realtimeSinceStartup<deadline){int count=g.GetComponentsInChildren<TreeNodeTarget>().Length;Check(count-previous<=4,"Large tree adds at most four nodes per frame");previous=count;frames++;yield return null;}
                Check(!g.IsBuildingTree&&g.Layout.Positions.Count==100&&frames>1,"Every large-tree node becomes reachable without truncation");
                g.ShowTree();Check(g.IsBuildingTree,"Large rebuild can start");g.OpenDocument(old);yield return null;Check(!g.IsBuildingTree&&g.TreeId==old.Id,"Switching cancels stale tree construction");
                string result=n+" tree library Play mode checks passed: controller-ray forms, new tree, people, family relationship, layout, reopen, import preview/confirmation, portrait materials, async thumbnails, scene photos and offline originals. Synthetic data only.";File.WriteAllText("Artifacts/tree-library-play-checks.txt",result);
            }
            finally
            {
                lib.Close();g.OpenDocument(old);g.Hud.SetVisible(oldHud);if(created!=null&&Directory.Exists(lib.Store.DocumentDirectory(created)))Directory.Delete(lib.Store.DocumentDirectory(created),true);if(importedId!=null&&Directory.Exists(lib.Store.DocumentDirectory(importedId)))Directory.Delete(lib.Store.DocumentDirectory(importedId),true);if(Directory.Exists(fixture))Directory.Delete(fixture,true);
                ((List<TreeDocument>)typeof(TreeLibraryOverlay).GetField("library",Private).GetValue(lib)).RemoveAll(d=>d.Id==created||d.Id==importedId);
                if(importedId!=null){string panoramas=Path.Combine(PanoramaCache.DirectoryPath,"people",importedId);if(Directory.Exists(panoramas))Directory.Delete(panoramas,true);}
            }
        }
    }
}
