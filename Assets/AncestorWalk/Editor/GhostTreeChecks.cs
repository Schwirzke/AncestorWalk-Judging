using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace AncestorWalk.Editor
{
    public static class GhostTreeChecks
    {
        const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        const string Report="Artifacts/ghost-tree-checks.txt";
        static TreeDocument Empty()=>new TreeDocument{Id=TreeStore.NewId(),Name="Synthetic ghost family"};
        public static string Data()
        {
            int n=0;void Check(bool value,string why){if(!value)throw new Exception(why);n++;}
            void Reject(Action a,string why){bool fail=false;try{a();}catch{fail=true;}Check(fail,why);}
            var doc=Empty();var root=TreeAuthoring.Slots(doc.Id,doc.ToTree()).Single();Check(root.IsRoot&&doc.People.Count==0,"Empty tree has one presentation-only root");
            var before=TreeStore.Serialize(doc);var layout=TreeAuthoring.Layout(doc.Id,doc.ToTree(),new TreeLayoutSettings());Check(layout.Positions.Count==0&&layout.Ghosts.Count==1&&layout.Bounds.Contains(layout.GhostPositions[root.Id]),"Root ghost participates in bounds without becoming a person");Check(TreeStore.Serialize(doc)==before,"Projection never mutates genealogy");
            var first=TreeAuthoring.AddPerson(doc,root,out string child);Check(first.People.Single().Name=="New person"&&first.Families.Count==0,"Root activation creates one unnamed real record");
            Check(!LifeEndpointScenes.Targets(first.People[0],new ResidenceTimeline()).Any(),"Unknown ghost-created person has no generation targets");
            Reject(()=>TreeAuthoring.AddPerson(first,root,out _),"Repeated root activation is stale");
            var parents=TreeAuthoring.Slots(first.Id,first.ToTree());Check(parents.Count==2&&parents.All(p=>p.ChildId==child),"First person exposes exactly two parent ghosts");
            var second=TreeAuthoring.AddPerson(first,parents[0],out string parent);Check(second.Families.Single().Parents.SequenceEqual(new[]{parent})&&second.Families[0].Children.SequenceEqual(new[]{child}),"Parent creation atomically attaches the child");
            Check(TreeAuthoring.Slots(second.Id,second.ToTree()).Count==3,"Only real parents expand the frontier");Reject(()=>TreeAuthoring.AddPerson(second,parents[1],out _),"Old sibling slot snapshot cannot create a duplicate");
            var remaining=TreeAuthoring.Slots(second.Id,second.ToTree()).Single(p=>p.ChildId==child);var third=TreeAuthoring.AddPerson(second,remaining,out _);
            Check(TreeAuthoring.Slots(third.Id,third.ToTree()).Count==4&&TreeAuthoring.Slots(third.Id,third.ToTree()).All(p=>p.ChildId!=child),"Two real parents leave only their four missing ancestors");
            for(int i=0;i<6;i++){var slot=TreeAuthoring.Slots(third.Id,third.ToTree()).First();third=TreeAuthoring.AddPerson(third,slot,out _);}third.Validate(true);Check(third.People.Count==9,"Repeated ancestry expansion remains finite and valid");
            var projected=TreeAuthoring.Layout(third.Id,third.ToTree(),new TreeLayoutSettings());Check(projected.Positions.Count==third.People.Count&&projected.GhostPositions.Count==TreeAuthoring.Slots(third.Id,third.ToTree()).Count,"Layout separates real nodes and ghosts");
            var points=projected.Positions.Values.Concat(projected.GhostPositions.Values).ToArray();for(int i=0;i<points.Length;i++)for(int j=i+1;j<points.Length;j++)Check((points[i]-points[j]).magnitude>=TreeLayout.Size-.001f,"Expanded node bodies do not overlap");
            foreach(var route in projected.Routes)for(int i=1;i<route.Length;i++){var d=route[i]-route[i-1];Check((Mathf.Abs(d.x)>.0001f?1:0)+(Mathf.Abs(d.y)>.0001f?1:0)+(Mathf.Abs(d.z)>.0001f?1:0)<=1,"Authoring connectors remain orthogonal");}
            var siblings=first.Copy();siblings.People.Add(new Person{Id="sibling",Name="Sibling"});siblings.Families.Add(new FamilyRecord{Id="shared",Children=new List<string>{child,"sibling"}});
            Check(TreeAuthoring.Slots(siblings.Id,siblings.ToTree()).Count==2,"Siblings share one pair of missing parents");
            siblings.Families.Add(new FamilyRecord{Id="second-group",Children=new List<string>{child}});Check(TreeAuthoring.Slots(siblings.Id,siblings.ToTree()).Count==4,"Separate family groups keep separate parent slots");
            var missing=first.Copy();missing.Families.Add(new FamilyRecord{Id="dangling",Parents=new List<string>{"absent"},Children=new List<string>{child}});Check(TreeAuthoring.Slots(missing.Id,missing.ToTree()).Count==0,"Dangling imports are not silently repaired by ghosts");
            var demo=doc.Copy();demo.IsDemo=true;Reject(()=>TreeAuthoring.AddPerson(demo,root,out _),"Read-only demo cannot materialize ghosts");var foreign=Empty();Reject(()=>TreeAuthoring.AddPerson(foreign,root,out _),"Foreign-tree targets rejected");
            var changed=second.Copy();changed.People.Single(p=>p.Id==parent).Name="Renamed parent";TreeAuthoring.ValidateChange(second,changed,parent);Check(true,"Details edit can change the just-created person only");changed.People.Single(p=>p.Id==child).Name="Unrelated edit";Reject(()=>TreeAuthoring.ValidateChange(second,changed,parent),"Narrow update rejects unrelated person edits");
            var limit=Empty();for(int i=0;i<2000;i++)limit.People.Add(new Person{Id="person-"+i});var fullSlot=TreeAuthoring.Slots(limit.Id,limit.ToTree())[0];Reject(()=>TreeAuthoring.AddPerson(limit,fullSlot,out _),"Person limit is enforced before saving");
            string dir=Path.GetFullPath("Artifacts/GhostTreeData/"+Guid.NewGuid().ToString("N"));var store=new TreeStore(dir);store.Save(third);var reopened=store.Load(third.Id);Check(reopened.People.Count==third.People.Count&&TreeAuthoring.Slots(reopened.Id,reopened.ToTree()).Count==projected.Ghosts.Count,"Real records and derived slots reopen correctly");Check(!File.ReadAllText(Path.Combine(store.DocumentDirectory(third.Id),"tree.json")).Contains("ghost-"),"No virtual identities enter persisted genealogy");
            return n+" ghost graph/layout/storage checks passed.";
        }
        public static string Run()
        {
            var g=UnityEngine.Object.FindAnyObjectByType<AncestorGallery>();if(!Application.isPlaying||g==null||!g.CacheIndexReady||g.Generation.Busy)throw new Exception("Ready, idle Play mode required.");
            File.WriteAllText(Report,"RUNNING");g.StartCoroutine(Safe(Checks(g)));return "Ghost tree checks running with synthetic records and a fake helper.";
        }
        static IEnumerator Safe(IEnumerator tests)
        {
            while(true){bool more=false;Exception error=null;try{more=tests.MoveNext();}catch(Exception e){error=e;}if(error!=null){(tests as IDisposable)?.Dispose();File.WriteAllText(Report,"FAILED: "+error);Debug.LogException(error);yield break;}if(!more)yield break;yield return tests.Current;}
        }
        static IEnumerator Checks(AncestorGallery g)
        {
            int n=0;void Check(bool v,string why){if(!v)throw new Exception(why);n++;}
            var original=g.Document;var originalRotation=g.TreeRotation;var originalScale=g.TreeScale;var originalPan=g.TreePan;var originalGeneration=g.Generation;var lib=g.Library;var originalStore=lib.Store;var originalList=new List<TreeDocument>((List<TreeDocument>)typeof(TreeLibraryOverlay).GetField("library",Hidden).GetValue(lib));bool hud=g.HudVisible,tutorial=g.Tutorial.IsOpen;
            var nav=UnityEngine.Object.FindAnyObjectByType<GalleryNavigation>();bool navEnabled=nav.enabled;var pos=nav.transform.position;var rot=nav.transform.rotation;var head=nav.view.transform.localRotation;
            string last=PlayerPrefs.GetString("AncestorWalk.LastTree","");bool guard=SessionState.GetBool("AncestorWalk.EndpointChecks",false);SessionState.SetBool("AncestorWalk.EndpointChecks",true);
            var png=File.ReadAllBytes("Assets/AncestorWalk/Resources/ClaraYork1870Panorama.png");using var helper=new SceneQueueChecks.FakeHelper(png){Hold=true};
            var go=new GameObject("Ghost authoring fake generation");go.SetActive(false);var gen=go.AddComponent<SceneGeneration>();string dir=Path.GetFullPath("Artifacts/GhostTreeUI/"+Guid.NewGuid().ToString("N"));
            typeof(SceneGeneration).GetField("TestStorage",Hidden).SetValue(gen,dir);typeof(SceneGeneration).GetField("TestConnection",Hidden).SetValue(gen,new SceneServiceConnection{url=helper.Url,token=new string('a',64)});go.SetActive(true);
            var store=new TreeStore(dir);string created=null;
            void Click(string name)
            {
                var hits=(IEnumerable)typeof(TreeLibraryOverlay).GetField("hits",Hidden).GetValue(lib);RectTransform match=null;
                foreach(var item in hits){var r=(RectTransform)item.GetType().GetField("Rect").GetValue(item);if(r.name==name||r.name.StartsWith(name,StringComparison.Ordinal)){match=r;break;}}
                Check(match!=null,"Library control exists: "+name);lib.HandleInput(new Ray(match.position-match.forward,match.forward),true,true,false);
            }
            try
            {
                g.Tutorial.Dismiss();g.AISetup.Close();g.Batch.Close();lib.Close();g.LocationEditor.Close();g.Hud.SetVisible(true);nav.enabled=false;
                float deadline=Time.realtimeSinceStartup+10;while(!gen.Ready&&Time.realtimeSinceStartup<deadline)yield return null;Check(gen.Ready,"Fake queue ready");
                typeof(AncestorGallery).GetProperty("Generation").SetValue(g,gen);typeof(TreeLibraryOverlay).GetField("store",Hidden).SetValue(lib,store);
                while(lib.Busy)yield return null;lib.Open();Click("New Tree");Click("Create tree");while(lib.Busy)yield return null;created=g.TreeId;yield return null;
                Check(g.IsTree&&g.Tree.People.Count==0&&g.BuildMode&&!lib.IsOpen,"New Tree closes the library and enables empty authoring view");
                var ghost=g.GetComponentsInChildren<GhostSphereTarget>().Single();Check(!ShaderUtil.ShaderHasError(ghost.GetComponent<Renderer>().sharedMaterial.shader),"Ghost rim shader compiles");Check(ghost.Slot.IsRoot&&g.Layout.Bounds.Contains(g.Layout.GhostPositions[ghost.Slot.Id]),"Empty root ghost is visible and included in bounds");
                Check(ghost.GetComponent<SphereCollider>()!=null&&ghost.GetComponent<CubePanoramaPreview>()==null&&ghost.GetComponentInChildren<YearSlider>()==null&&ghost.GetComponentInChildren<ScenePortal>()==null,"Ghost has only creation input, without panorama or timeline components");
                var stale=ghost.Slot;Check(nav.HandleTreeHit(ghost.GetComponent<Collider>(),true),"Quest/desktop shared hit path handles a ghost before teleport");lib.CreateFromGhost(stale);while(lib.Busy)yield return null;yield return null;
                Check(g.Tree.People.Count==1&&lib.IsOpen&&g.SelectedId==null,"One click materializes exactly one real person and opens details");string child=g.Document.People[0].Id;
                Check(g.GetComponentsInChildren<GhostSphereTarget>(true).Count()==2&&store.Load(created).People.Count==1,"Real node is saved before showing two parent slots");
                lib.Close();Check(g.Document.People[0].Name=="New person"&&gen.WaitingCount==0&&helper.Posts==0,"Closing optional details keeps the person and starts no AI work");
                lib.CreateFromGhost(stale);Check(!lib.Busy&&g.Tree.People.Count==1&&!string.IsNullOrEmpty(lib.Message),"Stale activation leaves the graph intact with an error");lib.Close();
                var baseDoc=g.Document.Copy();baseDoc.People.Single(p=>p.Id==child).BirthDate="1900";baseDoc.People.Single(p=>p.Id==child).BirthPlace="York";store.Save(baseDoc);g.ApplyAuthoringDocument(baseDoc,child);
                gen.Enqueue(created,g.Tree.People[child],true,new[]{(1900,"York")});deadline=Time.realtimeSinceStartup+15;while((!gen.Busy||helper.Posts<1)&&Time.realtimeSinceStartup<deadline)yield return null;Check(gen.Busy,"An unrelated scene request is active before adding a parent");string request=gen.ActiveJobs.Single().job.requestId;
                g.SetTreeRotation(new Vector2(25,15));g.SetTreeScale(.8f);var pose=nav.transform.position;var angles=g.TreeRotation;
                var parentGhost=g.GetComponentsInChildren<GhostSphereTarget>().First();nav.HandleTreeHit(parentGhost.GetComponent<Collider>(),true);while(lib.Busy)yield return null;yield return null;
                Check(g.Tree.People.Count==2&&g.Tree.Families.Single().Children.Contains(child)&&g.Tree.Families[0].Parents.Count==1,"Parent is attached while the unrelated generation remains active");
                Check(gen.Busy&&gen.ActiveJobs.Single().job.requestId==request&&g.TreeRotation==angles&&g.TreeScale==.8f&&nav.transform.position==pose,"Additive edit preserves active identity, tree pose and viewer position");
                string parent=g.Tree.Families[0].Parents[0];var draft=(TreeDocument)typeof(TreeLibraryOverlay).GetField("draft",Hidden).GetValue(lib);draft.People.Single(p=>p.Id==parent).Name="Synthetic parent";Click("Save person");while(lib.Busy)yield return null;
                Check(!lib.IsOpen&&g.Tree.People[parent].Name=="Synthetic parent"&&gen.Busy,"New-parent details save and return directly to the tree during other work");
                Check(g.GetComponentsInChildren<GhostSphereTarget>().Length==3,"One missing co-parent plus two grandparents appear");
                int requests=helper.Posts;g.SetBuildMode(false);yield return null;Check(!g.BuildMode&&g.GetComponentsInChildren<GhostSphereTarget>().Length==0&&g.Tree.People.Count==2,"Build mode hides ghosts without changing people");g.SetBuildMode(true);yield return null;
                Check(g.BuildMode&&g.GetComponentsInChildren<GhostSphereTarget>().Length==3&&helper.Posts==requests,"Build mode restores the finite frontier without generating images");
                helper.Hold=false;deadline=Time.realtimeSinceStartup+20;while((gen.Busy||gen.WaitingCount>0)&&Time.realtimeSinceStartup<deadline)yield return null;Check(!gen.Busy&&gen.CompletedCount==1,"The original active request completes normally after authoring");
                g.OpenDocument(store.Load(created));yield return null;Check(g.BuildMode&&g.Tree.People.Count==2,"Reopening retains build mode and saved family");
                var imported=TreeDocument.FromTree(g.Tree,TreeStore.NewId(),"Synthetic imported tree");store.Save(imported);g.OpenDocument(imported);yield return null;Check(!g.BuildMode&&g.GetComponentsInChildren<GhostSphereTarget>().Length==0,"Imported/existing tree defaults to normal browsing");
                var toggle=g.TreeControls.GetComponentsInChildren<RectTransform>(true).Single(r=>r.name=="Build Tree toggle");Canvas.ForceUpdateCanvases();Check(g.TreeControls.HandleInput(new Ray(toggle.position-toggle.forward,toggle.forward),true,true,false,1),"Quest-style ray can toggle authoring");yield return null;Check(g.BuildMode&&g.GetComponentsInChildren<GhostSphereTarget>().Length==3,"Every editable tree can enable parent ghosts");
                var currentSlot=g.GetComponentsInChildren<GhostSphereTarget>().First().Slot;var goodStore=lib.Store;string blocker=Path.Combine(dir,"not-a-directory");File.WriteAllText(blocker,"fixture");typeof(TreeLibraryOverlay).GetField("store",Hidden).SetValue(lib,new TreeStore(blocker));lib.CreateFromGhost(currentSlot);while(lib.Busy)yield return null;
                Check(g.Tree.People.Count==2&&g.GetComponentsInChildren<GhostSphereTarget>(true).Length==3&&!string.IsNullOrEmpty(lib.Message),"Atomic save failure retains ghost and real graph with a visible error");lib.Close();typeof(TreeLibraryOverlay).GetField("store",Hidden).SetValue(lib,goodStore);
                Check(helper.Posts==1,"Ghosts, toggles and invalid saves never issue AI requests");
                File.WriteAllText(Report,n+" ghost UI/integration checks passed: new/empty/root/parent/details, duplicate and stale clicks, active request retention/completion, rotated/scaled pose, mode persistence/import defaults, Quest-style targets and atomic save failure. Synthetic records and fake helper only.");
                PlayerPrefs.DeleteKey(TreeAuthoring.PreferenceKey(imported.Id));
            }
            finally
            {
                lib.Close();typeof(TreeLibraryOverlay).GetField("store",Hidden).SetValue(lib,originalStore);typeof(TreeLibraryOverlay).GetField("library",Hidden).SetValue(lib,originalList);typeof(AncestorGallery).GetProperty("Generation").SetValue(g,originalGeneration);g.OpenDocument(original);g.SetTreeRotation(originalRotation);g.SetTreeScale(originalScale);g.PanTree(originalPan-g.TreePan);
                nav.enabled=navEnabled;nav.transform.SetPositionAndRotation(pos,rot);nav.view.transform.localRotation=head;g.Hud.SetVisible(hud);if(tutorial)g.Tutorial.Open();
                if(created!=null)PlayerPrefs.DeleteKey(TreeAuthoring.PreferenceKey(created));if(string.IsNullOrEmpty(last))PlayerPrefs.DeleteKey("AncestorWalk.LastTree");else PlayerPrefs.SetString("AncestorWalk.LastTree",last);SessionState.SetBool("AncestorWalk.EndpointChecks",guard);UnityEngine.Object.Destroy(go);
            }
        }
    }
}
