using System;
using System.Linq;
using UnityEngine;
namespace AncestorWalk.Editor
{
    public static class TreeChecks
    {
        public static string Layouts()
        {
            int n=0;void Check(bool ok,string msg){if(!ok)throw new Exception(msg);n++;}
            var tree=GedcomImporter.Parse(Resources.Load<TextAsset>("DemoFamily").text);
            var r=TreeLayout.Build(tree,new TreeLayoutSettings{Gap=1,Stagger=.5f,Rise=.25f});
            foreach(var f in tree.Families)foreach(var p in f.Parents)foreach(var c in f.Children)
                Check(r.Positions[p].z-r.Positions[c].z>=1.999f,"Parents behind child");
            Check(r.Routes.Count==tree.Families.Sum(f=>f.Parents.Distinct().Count()+f.Children.Distinct().Count()),"Each family shares parent branches across its children");
            foreach(float stagger in new[]{0f,.5f,1.3f,2f})
            {
                var centered=TreeLayout.Build(tree,new TreeLayoutSettings{Gap=1,Stagger=stagger,Rise=.25f});
                foreach(var f in tree.Families.Where(f=>f.Parents.Count>1&&f.Children.Count>0))
                    Check(Mathf.Abs(Mathf.Abs(centered.Positions[f.Parents[0]].x-centered.Positions[f.Parents[1]].x)-2)<.001f,"Parent pair stays one pitch apart regardless of ancestor width or stagger");
            }
            var family=tree.Families.First();tree.People.Add("sibling-centering",new Person{Id="sibling-centering"});family.Children.Add("sibling-centering");
            var siblingsCentered=TreeLayout.Build(tree,new TreeLayoutSettings{Gap=1,Stagger=1.3f,Rise=.25f});
            Check(Mathf.Abs(family.Parents.Average(id=>siblingsCentered.Positions[id].x)-family.Children.Average(id=>siblingsCentered.Positions[id].x))<.001f,"Sibling group centered between parents");
            family.Children.Remove("sibling-centering");tree.People.Remove("sibling-centering");
            var ancestral=new FamilyTree();for(int i=0;i<7;i++)ancestral.People.Add(i.ToString(),new Person{Id=i.ToString()});
            for(int i=0;i<3;i++){var f=new Family{Id=i.ToString()};f.Children.Add(i.ToString());f.Parents.Add((i*2+1).ToString());f.Parents.Add((i*2+2).ToString());ancestral.Families.Add(f);}
            var nested=TreeLayout.Build(ancestral,new TreeLayoutSettings{Gap=1,Stagger=1.3f,Rise=.25f});
            foreach(var f in ancestral.Families)Check(Mathf.Abs(Mathf.Abs(nested.Positions[f.Parents[0]].x-nested.Positions[f.Parents[1]].x)-2)<.001f,"Nested ancestor pairs remain compact while branches fan outward");
            Check(Mathf.Abs(nested.Positions["0"].x-(nested.Positions["1"].x+nested.Positions["2"].x)*.5f)<.001f,"Root child centered below compact parent pair");
            foreach(var row in nested.Positions.Values.GroupBy(p=>p.z))
            {var xs=row.Select(p=>p.x).OrderBy(x=>x).ToArray();for(int i=1;i<xs.Length;i++)Check(xs[i]-xs[i-1]>=1.999f,"Fanning ancestor pairs never overlap");}
            var large=new FamilyTree();for(int i=0;i<40;i++)large.People.Add(i.ToString(),new Person{Id=i.ToString()});
            for(int i=0;i<15;i++){var f=new Family{Id="f"+i};f.Parents.Add(i.ToString());f.Children.Add((i+1).ToString());large.Families.Add(f);}
            var sibling=new Family{Id="siblings"};sibling.Parents.Add("0");sibling.Children.Add("16");sibling.Children.Add("17");large.Families.Add(sibling);
            var multiple=new Family{Id="second"};multiple.Parents.Add("0");multiple.Parents.Add("18");multiple.Children.Add("19");large.Families.Add(multiple);
            r=TreeLayout.Build(large,new TreeLayoutSettings{Gap=1,Stagger=.5f,Rise=.25f});Check(r.Positions["1"].z==r.Positions["16"].z&&r.Positions["16"].z==r.Positions["17"].z,"Siblings aligned despite unequal descendants");Check(r.Positions.Count==40,"All people beyond first 12 visible");
            foreach(var f in large.Families)foreach(var p in f.Parents)foreach(var c in f.Children)Check(r.Positions[p].z>r.Positions[c].z,"Additional generations ordered");
            var positions=r.Positions.Values.ToArray();for(int i=0;i<positions.Length;i++)for(int j=i+1;j<positions.Length;j++)
                Check(Mathf.Abs(positions[i].z-positions[j].z)>=1.999f||Mathf.Abs(positions[i].x-positions[j].x)>=1.999f,"Cube spacing");
            var reverse=new FamilyTree();foreach(var id in large.People.Keys.Reverse())reverse.People.Add(id,large.People[id]);reverse.Families.AddRange(large.Families.AsEnumerable().Reverse());
            var other=TreeLayout.Build(reverse,new TreeLayoutSettings{Gap=1,Stagger=.5f,Rise=.25f});Check(r.Positions.All(p=>other.Positions[p.Key]==p.Value),"Record ordering independent");
            foreach(var pos in positions){var pan=r.ClampPan(new Vector2(-pos.x,6.5f-pos.z));Check(Mathf.Abs(pos.x+pan.x)<.001f&&Mathf.Abs(pos.z+pan.y-6.5f)<.001f,"Each person can reach viewing position");}
            var bad=new Family{Id="bad"};bad.Parents.Add("missing");bad.Parents.Add("15");bad.Children.Add("0");large.Families.Add(bad);
            r=TreeLayout.Build(large,new TreeLayoutSettings{Gap=1,Stagger=.5f,Rise=.25f});Check(r.Positions["1"].z==r.Positions["16"].z&&r.Positions["16"].z==r.Positions["17"].z,"Siblings aligned despite unequal descendants");Check(r.Positions.Count==40&&r.Warnings.Any(w=>w.Contains("Missing"))&&r.Warnings.Any(w=>w.Contains("Cyclic")),"Missing/cyclic relationships safe");
            return n+" layout checks passed";
        }
        public static string Runtime()
        {
            int n=0;void Check(bool ok,string msg){if(!ok)throw new Exception(msg);n++;}
            var g=UnityEngine.Object.FindAnyObjectByType<AncestorGallery>();g.ShowTree();g.ResetTreePan();Physics.SyncTransforms();
            var nav=UnityEngine.Object.FindAnyObjectByType<GalleryNavigation>();nav.ResetTreeView();var camera=Camera.main.transform.position;
            foreach(var control in g.GetComponentsInChildren<CubeFacingControls>())typeof(CubeFacingControls).GetMethod("AdvanceFacing",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(control,new object[]{.3f});
            Physics.SyncTransforms();
            Check(g.Layout.Positions.Count==g.Tree.People.Count,"Every person built");
            var nodes=g.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("Person cube ")).ToArray();
            Check(nodes.Length==g.Tree.People.Count,"Cubes built");
            foreach(var node in nodes)
            {
                var body=node.GetComponentsInChildren<MeshRenderer>().First(r=>Vector3.Distance(r.transform.lossyScale,Vector3.one*TreeLayout.Size)<.001f);
                Check(body!=null,"Solid cube dimensions");
                var floating=node.GetComponent<CubeFacingControls>();
                floating.SetPointed(true);typeof(CubeFacingControls).GetMethod("AdvanceFacing",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(floating,new object[]{.3f});Physics.SyncTransforms();
                Check(floating.Panel.position.y>body.bounds.max.y&&floating.Panel.GetComponentInChildren<TimelinePanel>(true)!=null,"Information panel above cube owns timeline");
                Check(floating.Portal.GetComponent<GalleryTarget>()!=null&&!floating.Portal.IsChildOf(floating.Panel),"Independent portal owns scene target");
                Check(Mathf.Abs(Mathf.Max(Mathf.Abs(floating.Panel.localPosition.x),Mathf.Abs(floating.Panel.localPosition.z))-1.9f)<.001f,"Panel anchor stays on cube perimeter");
                Check(Mathf.Abs(floating.Panel.Find("Panel backing").GetComponent<Renderer>().bounds.min.y-body.bounds.max.y-.02f)<.001f,"Panel bottom stays two centimetres above cube");
                foreach(var target in node.GetComponentsInChildren<GalleryTarget>())
                {var ray=new Ray(target.transform.position-target.transform.forward*.6f,target.transform.forward);Check(Physics.Raycast(ray,out var hit,1)&&hit.collider.gameObject==target.gameObject,"Buttons clear of body collider");}
                var slider=node.GetComponentInChildren<YearSlider>(true);int original=g.Year(node.name.Substring(12));
                var p=slider.transform.position;slider.Drag(new Ray(p+slider.transform.right*10-slider.transform.forward,slider.transform.forward));
                LifeTimeline.TryRange(g.Tree.People[node.name.Substring(12)],out _,out int end);Check(g.Year(node.name.Substring(12))==end,"Year drag callback");g.SetYear(node.name.Substring(12),original);
            }
            g.PanTree(new Vector2(1,-2));Check(Camera.main.transform.position==camera,"Pan leaves viewpoint still");var saved=g.TreePan;
            var cached=g.Tree.People.Keys.FirstOrDefault(g.HasScene);
            if(cached!=null){g.Select(cached);Check(g.Overlay!=null,"Cached scene entry works");g.ShowTree();Check(g.TreePan==saved,"Pan restored on return");}
            g.PanTree(new Vector2(0,-10000));Check(g.TreePan==g.Layout.ClampPan(g.TreePan),"Pan clamped");
            Check(g.GetComponentsInChildren<Transform>().Count(t=>t.name.StartsWith("Person cube "))==g.Tree.People.Count,"All cubes visible from all directions");
            g.ResetTreePan();Check(g.TreePan==Vector2.zero,"Reset restores framing");
            bool expanded=g.TreeControls.IsExpanded;g.TreeControls.SetExpanded(true);
            Canvas.ForceUpdateCanvases();
            var controls=g.TreeControls.GetComponentsInChildren<RectTransform>();
            Ray At(string name){var r=controls.Single(t=>t.name==name);return new Ray(r.position-r.forward*10,r.forward);}
            Check(g.TreeControls.HandleInput(At("Right"),true,true,true,2)&&Camera.main.transform.position.x>camera.x,"Held overlay flies right");
            var after=Camera.main.transform.position;g.TreeControls.HandleInput(At("Right"),true,false,false,2);Check(Camera.main.transform.position==after,"Release stops flight");
            g.TreeControls.HandleInput(At("Forward"),true,true,true,2);Check(Camera.main.transform.position.z>camera.z,"Forward button flies in depth");
            g.TreeControls.HandleInput(At("Reset View"),true,true,true,2);Check(g.TreePan==Vector2.zero&&Camera.main.transform.position==camera,"Reset overlay callback");
            var person=g.Tree.People.Keys.First();g.Select(person);g.EditLocation(person);Check(g.LocationEditor.IsOpen,"Location editing opens in scene");g.LocationEditor.Close();g.ShowTree();
            Check(Camera.main.transform.position==camera,"Overlay controls never move viewpoint");
            var prefs=UnityEngine.Object.FindAnyObjectByType<NavigationPreferences>();float speed=prefs.TreePanSpeed;prefs.SetTreePanSpeed(3);Check(PlayerPrefs.GetFloat("AncestorWalk.TreePanSpeed")==3,"Speed saved");prefs.SetTreePanSpeed(speed);
            g.TreeControls.SetExpanded(expanded);
            return n+" runtime checks passed; no generation requests";
        }
        public static string Rendering()
        {
            int n=0;void Check(bool ok,string message){if(!ok)throw new Exception(message);n++;}
            var gallery=UnityEngine.Object.FindAnyObjectByType<AncestorGallery>();gallery.ShowTree();
            foreach(var controls in gallery.GetComponentsInChildren<CubeFacingControls>())
            {
                var panel=controls.Panel;
                Check(panel.GetComponent<UnityEngine.Rendering.SortingGroup>()!=null,"Panel sorting group");
                foreach(var renderer in panel.GetComponentsInChildren<MeshRenderer>(true))
                {
                    int order=renderer.GetComponent<TMPro.TMP_Text>()!=null?40:renderer.name=="Panel backing"?0:
                        renderer.name=="Year slider"?10:renderer.name=="Cached year"?20:renderer.name=="Selected year"?30:5;
                    Check(renderer.sortingOrder==order&&renderer.sharedMaterial.renderQueue==3000,"Stable transparent layer order");
                }
                var rail=panel.GetComponentInChildren<YearSlider>(true).transform;
                var timeline=rail.parent;var thumb=timeline.Find("Selected year");
                foreach(Transform tick in timeline)if(tick.name=="Cached year")
                {
                    Check(tick.localPosition.z+tick.localScale.z*.5f<rail.localPosition.z-rail.localScale.z*.5f,"Tick separated from rail");
                    Check(thumb.localPosition.z+thumb.localScale.z*.5f<tick.localPosition.z-tick.localScale.z*.5f,"Thumb separated from ticks");
                }
                // Recreated cache markers must receive the same material/ordering immediately.
                var script=timeline.GetComponentInParent<TimelinePanel>(true);
                typeof(TimelinePanel).GetField("tickKey",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).SetValue(script,null);
                script.Refresh();
                Check(timeline.GetComponentsInChildren<MeshRenderer>(true).Where(r=>r.name=="Cached year").All(r=>r.sortingOrder==20&&r.sharedMaterial.renderQueue==3000),"Dynamic ticks retain layers");
            }
            return n+" panel rendering checks passed";
        }
        public static string Proximity()
        {
            int n=0;void Check(bool ok,string message){if(!ok)throw new Exception(message);n++;}
            var gallery=UnityEngine.Object.FindAnyObjectByType<AncestorGallery>();gallery.ShowTree();
            var nav=UnityEngine.Object.FindAnyObjectByType<GalleryNavigation>();var saved=nav.transform.position;
            try
            {
                foreach(var controls in gallery.GetComponentsInChildren<CubeFacingControls>())
                {
                    var step=typeof(CubeFacingControls).GetMethod("AdvanceFacing",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
                    var slider=controls.Panel.GetComponentInChildren<YearSlider>(true);
                    controls.SetPointed(true);
                    var center=controls.GetComponent<CubePanoramaPreview>().Bounds.center;
                    foreach(var direction in new[]{Vector3.back,Vector3.up})foreach(bool near in new[]{true,false})
                    {
                        nav.transform.position+=center+direction*(controls.InteractionRange+(near?-.01f:.01f))-Camera.main.transform.position;
                        step.Invoke(controls,new object[]{.3f});
                        Check(controls.InteractionsVisible==near&&controls.Portal.gameObject.activeSelf==near,"Portal distance threshold");
                        Check(slider.gameObject.activeInHierarchy==near&&slider.GetComponent<Collider>().enabled==near,"Slider visibility and hit target");
                        Check(controls.Panel.gameObject.activeInHierarchy==near,"Whole panel follows distance visibility");
                        if(!near)Check(controls.Portal.GetComponentInChildren<ParticleSystem>(true).particleCount==0,"Hidden portal particles cleared");
                    }
                }
            }
            finally{nav.transform.position=saved;}
            return n+" proximity visibility checks passed";
        }
    }
}
