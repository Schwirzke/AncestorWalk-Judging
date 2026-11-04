using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace AncestorWalk.Editor
{
    public static class SiblingSampleChecks
    {
        public const string FilePath="Samples/Sibling Test Family.ged";
        const string Report="Artifacts/sibling-sample-play-checks.txt";
        const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        static SiblingVisibility.State Collapsed(params string[] names)=>new(){anchors=names.Select(n=>"@"+n.ToUpperInvariant()+"@").ToList()};
        public static bool Matches(TreeDocument doc,string ged)
        {
            var expected=TreeDocument.FromTree(GedcomImporter.Parse(ged),doc.Id,"Sibling Test Family");expected.SourceGedcom=ged;expected.SourceFingerprint=PanoramaCache.Hash(Encoding.UTF8.GetBytes(ged));expected.UpdatedUtc=doc.UpdatedUtc;
            return JsonUtility.ToJson(expected)==JsonUtility.ToJson(doc);
        }
        public static bool Pristine(TreeDocument doc,string ged,string storage)
        {
            if(!Matches(doc,ged))return false;
            foreach(string path in new[]{Path.Combine(storage,"Locations",doc.Id),Path.Combine(storage,"Panoramas","people",doc.Id)})
                if(Directory.Exists(path)&&Directory.EnumerateFiles(path,"*",SearchOption.AllDirectories).Any())return false;
            return true;
        }
        public static string Data()
        {
            int n=0;void Check(bool yes,string why){if(!yes)throw new Exception(why);n++;}
            string ged=File.ReadAllText(FilePath);var tree=GedcomImporter.Parse(ged);var doc=TreeDocument.FromTree(tree,TreeStore.NewId(),"Sibling Test Family");doc.Validate(true);
            Check(tree.People.Count==12&&tree.Families.Count==4&&tree.Families.Sum(f=>f.Parents.Count*f.Children.Count)==14,"12 people, 4 families and 14 parent-child edges");Check(tree.Warnings.Count==0,"Fictional GEDCOM imports without warnings");
            foreach(var p in tree.People.Values){Check(LifeTimeline.TryRange(p,out int start,out int end)&&start<=end,"Every person has a usable timeline");Check(!LifeEndpointScenes.Targets(p,new ResidenceTimeline()).Any(),"Blank locations start no automatic generation");Check(p.Sex=="M"||p.Sex=="F","Recorded sex supports branch colors");}
            var graph=new SiblingVisibility(tree);
            Check(graph.Siblings("@DANIEL@").ToHashSet().SetEquals(new[]{"@LAURA@","@MICHAEL@"}),"Adult full siblings identified");Check(graph.Siblings("@ALEX@").ToHashSet().SetEquals(new[]{"@MAYA@","@IRIS@"}),"Full and half siblings identified across families");Check(graph.Siblings("@SAM@").Count==0,"Cousins are not treated as siblings");
            var settings=new TreeLayoutSettings{Gap=1,Rise=.25f,Stagger=.5f};var full=TreeLayout.Build(tree,settings);float pitch=TreeLayout.Size+settings.Gap;
            Check(full.Positions.Values.Select(p=>p.z).Distinct().Count()==3,"Exactly three generation rows");
            foreach(var group in new[]{new[]{"@DANIEL@","@LAURA@","@MICHAEL@"},new[]{"@ALEX@","@MAYA@","@IRIS@","@SAM@"}})Check(group.Select(id=>full.Positions[id].z).Distinct().Count()==1,"Siblings with different descendants stay in one generation");
            Check(Mathf.Abs(Mathf.Abs(full.Positions["@ARTHUR@"].x-full.Positions["@EVELYN@"].x)-pitch)<.001f,"Grandparent pair stays compact");Check(Mathf.Abs(Mathf.Abs(full.Positions["@LAURA@"].x-full.Positions["@OWEN@"].x)-pitch)<.001f,"Independent parent pair stays compact");
            var sharedPartners=new[]{"@DANIEL@","@NORA@","@LEAH@"}.Select(id=>full.Positions[id].x).ToArray();Check(Mathf.Abs(sharedPartners.Max()-sharedPartners.Min()-2*pitch)<.001f,"Multiple partners remain one compact cluster");
            foreach(var test in new[]{(Collapsed("Daniel"),8),(Collapsed("Laura"),5),(Collapsed("Michael"),3),(Collapsed("Alex"),10),(Collapsed("Daniel","Alex"),6)})
            {
                string before=JsonUtility.ToJson(doc);var projection=graph.Project(test.Item1);Check(projection.People.Count==test.Item2,"Expected whole-branch projection size");Check(JsonUtility.ToJson(doc)==before&&tree.People.Count==12,"Collapsing never edits genealogy");
                var layout=TreeLayout.Build(projection,settings);Check(layout.Positions.Count==test.Item2&&layout.Warnings.Count==0,"Collapsed graph lays out without omissions or warnings");
                foreach(var route in layout.Connectors)for(int i=1;i<route.Points.Length;i++){var d=route.Points[i]-route.Points[i-1];Check((Mathf.Abs(d.x)>.0001f?1:0)+(Mathf.Abs(d.y)>.0001f?1:0)+(Mathf.Abs(d.z)>.0001f?1:0)<=1,"Collapsed connectors remain orthogonal");}
            }
            var hidden=graph.Hidden(Collapsed("Daniel"));Check(hidden.SetEquals(new[]{"@LAURA@","@MICHAEL@","@SAM@","@OWEN@"}),"Sibling descendants and detached partner branch hide together");
            var nested=Collapsed("Daniel","Laura");Check(graph.Project(nested).People.Count==8,"Hidden nested anchors remain dormant");nested.anchors.Remove("@DANIEL@");Check(graph.Project(nested).People.Count==5,"Nested collapse returns when its anchor is visible again");
            var projectionTree=graph.Project(Collapsed("Daniel"));var authoring=TreeAuthoring.Layout(doc.Id,projectionTree,settings,tree);var actualSlots=TreeAuthoring.Slots(doc.Id,tree).Where(s=>s.Children.Any(projectionTree.People.ContainsKey)).Select(s=>s.Id).ToHashSet();Check(authoring.Ghosts.Select(s=>s.Id).ToHashSet().SetEquals(actualSlots),"Collapsed authoring uses only genuine canonical missing-parent slots");Check(authoring.Ghosts.All(s=>s.ChildId!="@DANIEL@"),"Hidden relatives cannot invent missing parents");
            var shared=doc.Copy().ToTree();var extra=new Family{Id="shared-descendant"};extra.Parents.Add("@DANIEL@");extra.Children.Add("@SAM@");shared.Families.Add(extra);var sharedView=new SiblingVisibility(shared).Project(Collapsed("Daniel"));Check(sharedView.People.ContainsKey("@SAM@")&&sharedView.People.ContainsKey("@OWEN@"),"Shared descendants retain their surviving connections");
            shared.People.Add("unrelated",new Person{Id="unrelated"});Check(new SiblingVisibility(shared).Project(Collapsed("Daniel")).People.ContainsKey("unrelated"),"Disconnected families remain visible");
            var lone=new FamilyTree();lone.People.Add("one",new Person{Id="one"});Check(new SiblingVisibility(lone).Project(new SiblingVisibility.State()).People.Count==1,"Single person stays visible");Check(new SiblingVisibility(new FamilyTree()).Project(new SiblingVisibility.State()).People.Count==0,"Empty tree stays empty");
            var unknown=new Family{Id="unknown-parents"};unknown.Children.AddRange(new[]{"@ALEX@","@SAM@"});shared.Families.Add(unknown);Check(new SiblingVisibility(shared).Siblings("@SAM@").Contains("@ALEX@"),"Recorded sibling group works with unknown parents");
            var cycle=new Family{Id="cycle"};cycle.Parents.Add("@ALEX@");cycle.Children.Add("@ARTHUR@");shared.Families.Add(cycle);Check(new SiblingVisibility(shared).Project(Collapsed("Alex")).People.Count>0,"Cyclic input cannot hang or hide the anchor");
            string storage=Path.GetFullPath("Artifacts/SiblingData/"+Guid.NewGuid().ToString("N"));var store=new TreeStore(storage);var draft=TreeArchiveImport.Stage(store,Path.GetFullPath(FilePath));var saved=TreeArchiveImport.Commit(store,draft);Check(Matches(saved,ged)&&Matches(store.Load(saved.Id),ged)&&Pristine(saved,ged,storage),"Sample imports and reopens without changes");var edited=saved.Copy();edited.People[0].Name="Edited sample";Check(!Matches(edited,ged),"Edited samples are not mistaken for reusable originals");
            string moves=Path.Combine(storage,"Locations",saved.Id);Directory.CreateDirectory(moves);File.WriteAllText(Path.Combine(moves,"fixture.json"),"{}");Check(!Pristine(saved,ged,storage),"Separately saved location edits prevent sample reuse");
            File.Delete(Path.Combine(moves,"fixture.json"));string images=Path.Combine(storage,"Panoramas","people",edited.Id);Directory.CreateDirectory(images);File.WriteAllText(Path.Combine(images,"scene.json"),"{}");Check(!Pristine(saved,ged,storage),"Existing scene caches are preserved as an edited sample");
            string report=n+" sibling sample/visibility data checks passed.";Directory.CreateDirectory("Artifacts");File.WriteAllText("Artifacts/sibling-sample-data-checks.txt",report);return report;
        }
        public static string InstallAndCheck()
        {
            var g=UnityEngine.Object.FindAnyObjectByType<AncestorGallery>();if(!Application.isPlaying||g==null||!g.CacheIndexReady||g.Generation.Busy||g.Library.Busy||g.Generation.WaitingForTree(g.TreeId)>0)throw new Exception("Ready, idle Play mode required; existing jobs will not be cancelled.");
            Directory.CreateDirectory("Artifacts");File.WriteAllText(Report,"RUNNING");g.StartCoroutine(Safe(Run(g)));return "Importing/reusing Sibling Test Family and checking visibility controls.";
        }
        static IEnumerator Safe(IEnumerator test)
        {
            var stack=new Stack<IEnumerator>();stack.Push(test);while(stack.Count>0){var run=stack.Peek();bool more=false;Exception failure=null;try{more=run.MoveNext();}catch(Exception e){failure=e;}
                if(failure!=null){while(stack.Count>0)(stack.Pop() as IDisposable)?.Dispose();File.WriteAllText(Report,"FAILED: "+failure);Debug.LogException(failure);yield break;}
                if(!more){(stack.Pop() as IDisposable)?.Dispose();continue;}if(run.Current is IEnumerator nested){stack.Push(nested);continue;}yield return run.Current;}
        }
        static IEnumerator Run(AncestorGallery g)
        {
            int n=0;void Check(bool yes,string why){if(!yes)throw new Exception(why);n++;}IEnumerator Until(Func<bool> done,string why){float deadline=Time.realtimeSinceStartup+25;while(!done()&&Time.realtimeSinceStartup<deadline)yield return null;Check(done(),why);}
            var lib=g.Library;var gen=g.Generation;var nav=UnityEngine.Object.FindAnyObjectByType<GalleryNavigation>();bool navEnabled=nav.enabled;var fakeObject=new GameObject("Inactive sibling job fixture");fakeObject.SetActive(false);var fake=fakeObject.AddComponent<SceneGeneration>();
            bool build=false;string collapse=null;bool collapseExists=false;string treeId=null;bool success=false;var original=g.Document.Copy();
            void Click(string title)
            {
                RectTransform rect=null;foreach(var hit in (IEnumerable)typeof(TreeLibraryOverlay).GetField("hits",Hidden).GetValue(lib)){var r=(RectTransform)hit.GetType().GetField("Rect").GetValue(hit);if(r.name==title){rect=r;break;}}
                Check(rect!=null,"Library control exists: "+title);Canvas.ForceUpdateCanvases();lib.HandleInput(new Ray(rect.position-rect.forward,rect.forward),true,true,false);
            }
            try
            {
                g.Tutorial.Dismiss();g.AISetup.Close();g.Batch.Close();g.LocationEditor.Close();lib.Close();nav.enabled=false;
                string ged=File.ReadAllText(FilePath);var before=Directory.GetFiles(lib.Store.Root,"tree.json",SearchOption.AllDirectories).ToDictionary(p=>p,p=>PanoramaCache.Hash(File.ReadAllBytes(p)));
                string storage=Path.GetDirectoryName(lib.Store.Root);var list=Task.Run(()=>lib.Store.List().Where(d=>Pristine(d,ged,storage)).ToList());yield return Until(()=>list.IsCompleted,"Saved trees indexed");if(list.IsFaulted)throw list.Exception;var match=list.Result.FirstOrDefault(d=>gen.WaitingForTree(d.Id)==0&&gen.ActiveForTree(d.Id)==0);
                if(match==null){lib.ImportPath(Path.GetFullPath(FilePath));yield return Until(()=>!lib.Busy,"Sample staged through library");Check((string)typeof(TreeLibraryOverlay).GetField("page",Hidden).GetValue(lib)=="preview","Import review is shown");Click("Import tree");yield return Until(()=>!lib.Busy,"Sample import saved");}
                else {g.OpenDocument(lib.Store.Load(match.Id));lib.Close();}
                Check(g.Document.Name=="Sibling Test Family"&&!g.Document.IsDemo&&g.Tree.People.Count==12,"Editable saved sample opened");treeId=g.TreeId;yield return Until(()=>g.CacheIndexReady,"Sample cache index ready");
                build=g.BuildMode;collapseExists=PlayerPrefs.HasKey(SiblingVisibility.PreferenceKey(treeId));collapse=PlayerPrefs.GetString(SiblingVisibility.PreferenceKey(treeId),"");
                if(g.BuildMode)g.SetBuildMode(false);g.ExpandAllSiblings();Check(g.Layout.Positions.Count==12&&!g.BuildMode,"Sample starts uncluttered with every sphere");
                Check(g.Tree.People.Values.All(p=>g.AvailableYears(p.Id).Length==0)&&gen.WaitingForTree(treeId)==0&&gen.ActiveForTree(treeId)==0,"Sample has no generated scenes or automatic requests");
                string document=JsonUtility.ToJson(g.Document);g.SetTreeRotation(new Vector2(20,12));g.SetTreeScale(.85f);nav.FrameNode("@DANIEL@");yield return new WaitForSecondsRealtime(.5f);
                var target=g.GetComponentsInChildren<GalleryTarget>().Single(t=>t.action==GalleryAction.ToggleSiblings&&t.personId=="@DANIEL@");Check(target.GetComponent<Collider>().enabled,"Visible sibling button accepts input after fading in");
                var position=nav.transform.position;var rotation=nav.transform.rotation;Check(nav.HandleTreeHit(target.GetComponent<Collider>(),true),"Desktop/Quest pointer path activates sibling collapse");yield return null;
                Check(g.Layout.Positions.Count==8&&!g.Layout.Positions.ContainsKey("@LAURA@")&&g.Layout.Positions.ContainsKey("@DANIEL@"),"Sibling and descendant branches hide with anchor retained");
                Check(nav.transform.position==position&&nav.transform.rotation==rotation&&g.TreeRotation==new Vector2(20,12)&&Mathf.Abs(g.TreeScale-.85f)<.001f,"Collapse preserves viewer pose, rotation and scale");Check(JsonUtility.ToJson(g.Document)==document&&g.Tree.People.Count==12,"Collapse changes no saved family data");
                nav.FrameNode("@ALEX@");yield return new WaitForSecondsRealtime(.5f);target=g.GetComponentsInChildren<GalleryTarget>().Single(t=>t.action==GalleryAction.ToggleSiblings&&t.personId=="@ALEX@");Check(nav.HandleTreeHit(target.GetComponent<Collider>(),true),"Nested sibling collapse is selectable");yield return null;Check(g.Layout.Positions.Count==6&&!g.Layout.Positions.ContainsKey("@IRIS@"),"Nested collapse includes half siblings");
                g.Select("@ALEX@");yield return null;Check(g.SelectedId=="@ALEX@"&&g.IsAllTime,"Sample can enter its default panorama scene");g.SetYear("@ALEX@",2005);Check(g.Year("@ALEX@")==2005,"Timeline year selection works");g.ShowTree();yield return null;Check(g.Layout.Positions.Count==6&&g.SiblingsCollapsed("@ALEX@"),"Collapse survives a scene visit");
                Check(!nav.NearestNode().Equals("@IRIS@"),"Node navigation excludes hidden people");
                typeof(SceneGeneration).GetProperty("Ready").SetValue(fake,true);typeof(AncestorGallery).GetProperty("Generation").SetValue(g,fake);var workers=(IDictionary)typeof(SceneGeneration).GetField("workers",Hidden).GetValue(fake);var pending=new PendingScene{treeId=treeId,personId="@LAURA@",job=new SceneJob{requestId=new string('e',32)}};workers.Add(pending,Activator.CreateInstance(typeof(SceneGeneration).GetNestedType("Worker",BindingFlags.NonPublic),true));
                g.ToggleSiblings("@DANIEL@");Check(fake.ActiveCount==1&&workers.Contains(pending),"Expanding preserves a hidden person's active request identity");typeof(AncestorGallery).GetProperty("Generation").SetValue(g,gen);
                var expand=g.TreeControls.GetComponentsInChildren<RectTransform>(true).Single(r=>r.name=="Expand all siblings");Canvas.ForceUpdateCanvases();Check(g.TreeControls.HandleInput(new Ray(expand.position-expand.forward,expand.forward),true,true,false,1),"Quest-style global Expand siblings control works");yield return null;Check(g.Layout.Positions.Count==12&&!g.HasCollapsedSiblings,"Global expansion restores all people");
                g.ToggleSiblings("@MICHAEL@");yield return null;g.OpenDocument(lib.Store.Load(treeId));yield return Until(()=>g.CacheIndexReady,"Saved sample reopened");Check(g.Layout.Positions.Count==3&&g.SiblingsCollapsed("@MICHAEL@")&&new SiblingVisibility(g.Tree).Project(SiblingVisibility.Load(treeId)).People.Count==3,"Collapse preference persists through saved-tree reopening and fresh settings load");g.ExpandAllSiblings();
                g.SetBuildMode(true);yield return null;Check(g.GetComponentsInChildren<GhostSphereTarget>().Any(),"Build Tree can still reveal real missing parents");g.SetBuildMode(false);
                var properties=new MaterialPropertyBlock();var colors=new List<Color>();foreach(var line in g.GetComponentsInChildren<LineRenderer>().Where(l=>l.name=="Family relationship")){line.GetPropertyBlock(properties);colors.Add(properties.GetColor("_BaseColor"));}
                Check(colors.Any(c=>c==RelationshipColors.Male)&&colors.Any(c=>c==RelationshipColors.Female),"Sample shows blue and pink connector branches");Check(before.All(p=>File.Exists(p.Key)&&PanoramaCache.Hash(File.ReadAllBytes(p.Key))==p.Value),"Every pre-existing saved document remains byte-identical");
                Check(Matches(lib.Store.Load(treeId),ged),"Imported sample remains unchanged after all interactions");
                File.WriteAllText(Report,n+" sibling sample Play checks passed: normal import/reuse, 12 spheres, sibling/half-sibling/nested branches, pointer targets, scene/timeline return, pose, reopen, ghost slots, colors and active-job preservation. Existing trees unchanged; no AI requests.");success=true;
            }
            finally
            {
                typeof(AncestorGallery).GetProperty("Generation").SetValue(g,gen);lib.Close();g.LocationEditor.Close();
                if(treeId!=null){if(collapseExists)PlayerPrefs.SetString(SiblingVisibility.PreferenceKey(treeId),collapse);else PlayerPrefs.DeleteKey(SiblingVisibility.PreferenceKey(treeId));PlayerPrefs.SetInt(TreeAuthoring.PreferenceKey(treeId),build?1:0);PlayerPrefs.Save();}
                // Reload the chosen sample on success so it is the next tree restored on Play.
                typeof(AncestorGallery).GetField("siblingTree",Hidden).SetValue(g,null);g.OpenDocument(success?lib.Store.Load(treeId):original);g.SetTreeRotation(Vector2.zero);g.SetTreeScale(1);nav.ResetTreeView();nav.enabled=navEnabled;UnityEngine.Object.Destroy(fakeObject);
            }
        }
    }
}
