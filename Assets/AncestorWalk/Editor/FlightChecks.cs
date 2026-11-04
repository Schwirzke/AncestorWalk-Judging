using System;
using System.Linq;
using UnityEngine;
namespace AncestorWalk.Editor
{
    public static class FlightChecks
    {
        public static string Run()
        {
            int n=0;void Check(bool ok,string message){if(!ok)throw new Exception(message);n++;}
            var tree=GedcomImporter.Parse(Resources.Load<TextAsset>("DemoFamily").text);var layout=TreeLayout.Build(tree,new TreeLayoutSettings{Gap=1,Stagger=.5f,Rise=.25f});
            foreach(var family in tree.Families)
            {
                foreach(var parent in family.Parents)
                {var route=layout.Routes.First(r=>Vector3.Distance(r[0],layout.Positions[parent]-Vector3.up*.5f)<.001f);Check(Mathf.Abs(route[1].x-route[0].x)<.001f&&Mathf.Abs(route[1].z-route[0].z)<.001f,"Parent bottom exits vertically");Check(Mathf.Abs(route[1].y-route[route.Length-1].y)<.001f,"Parent run stays level with shared junction");}
                foreach(var child in family.Children)
                {var route=layout.Routes.First(r=>Vector3.Distance(r[r.Length-1],layout.Positions[child]+Vector3.forward*.5f)<.001f);var join=route[route.Length-2];Check(join.z>route[route.Length-1].z,"Child back receives from generation gap");Check(Mathf.Abs(join.y-route[route.Length-1].y)<.001f,"Junction level with child back center");Check(Mathf.Abs(join.x-layout.Positions[child].x)<.001f,"Join X centered on child");}
            }
            var sibling=new Person{Id="sibling-fixture"};tree.People.Add(sibling.Id,sibling);tree.Families[0].Children.Add(sibling.Id);
            var siblingLayout=TreeLayout.Build(tree,new TreeLayoutSettings{Gap=1,Stagger=1.3f,Rise=.25f});
            foreach(var child in tree.Families[0].Children)
            {
                var bottom=siblingLayout.Positions[child]+Vector3.forward*.5f;
                var route=siblingLayout.Routes.Single(r=>Vector3.Distance(r[r.Length-1],bottom)<.001f);
                Check(Mathf.Abs(route[route.Length-2].x-bottom.x)<.001f,"Sibling join uses individual child X, not family midpoint");
                Check(route[route.Length-2].z>bottom.z,"Join stays in generation gap");
            }
            var g=UnityEngine.Object.FindAnyObjectByType<AncestorGallery>();var nav=UnityEngine.Object.FindAnyObjectByType<GalleryNavigation>();var prefs=UnityEngine.Object.FindAnyObjectByType<NavigationPreferences>();
            g.ShowTree();prefs.SetOpen(false);nav.ResetTreeView();var start=nav.transform.position;var speed=prefs.TreePanSpeed;var savedLayout=TreeLayoutSettings.Load();
            var savedMode=TreeControlTemplates.Current;
            try
            {
                TreeControlTemplates.Select(TreeControlMode.TwoStickFlight);
                prefs.SetTreePanSpeed(2);
                nav.Fly(Vector3.forward,1);Check(Vector3.Distance(nav.transform.position,start+Vector3.forward*2)<.001f,"Forward distance is speed times elapsed time");
                nav.Fly(Vector3.up,1);Check(Mathf.Abs(nav.transform.position.y-start.y-2)<.001f,"Up flight");nav.Fly(Vector3.down,1);Check(Mathf.Abs(nav.transform.position.y-start.y)<.001f,"Down flight");
                nav.ResetTreeView();nav.transform.rotation=Quaternion.Euler(0,90,0);nav.Fly(Vector3.forward,1);Check(Vector3.Distance(nav.transform.position,start+Vector3.right*2)<.001f,"Flight follows viewing direction");
                nav.ResetTreeView();nav.view.transform.localRotation=Quaternion.Euler(-45,0,0);nav.Fly(Vector3.forward,1);Check(nav.transform.position.y>1&&nav.transform.position.z>1,"Forward follows pitched view");
                nav.ResetTreeView();nav.Fly(Vector3.forward,1,true);Check(Mathf.Abs(nav.transform.position.z-start.z-6)<.001f,"Shift boost factor");
                nav.ResetTreeView();nav.Fly(Vector3.one,1);Check(Vector3.Distance(nav.transform.position,start)<=2.001f,"Diagonal speed normalized");
                foreach(var pose in new[]{Vector3.zero,new Vector3(-60,90,0),new Vector3(45,180,0)})
                {
                    nav.ResetTreeView();nav.transform.rotation=Quaternion.Euler(0,pose.y,0);nav.view.transform.localRotation=Quaternion.Euler(pose.x,0,0);
                    var origin=nav.transform.position;
                    nav.FlyTreeSticks(Vector2.right,Vector2.zero,1);Check(Vector3.Distance(nav.transform.position,origin+Vector3.right*2)<.001f,"Quest left horizontal follows fixed X at "+pose);
                    nav.transform.position=origin;nav.FlyTreeSticks(Vector2.up,Vector2.zero,1);Check(Vector3.Distance(nav.transform.position,origin+Vector3.up*2)<.001f,"Quest left vertical follows fixed Y at "+pose);
                    nav.transform.position=origin;nav.FlyTreeSticks(Vector2.zero,Vector2.up,1);Check(Vector3.Distance(nav.transform.position,origin+Vector3.forward*2)<.001f,"Quest right vertical follows fixed Z at "+pose);
                }
                nav.ResetTreeView();nav.FlyTreeSticks(Vector2.one,Vector2.up,1);Check(Mathf.Abs(nav.transform.position.magnitude-2)<.001f,"Two-stick diagonal speed capped");
                nav.ResetTreeView();nav.FlyTreeSticks(Vector2.one*.1f,new Vector2(1,.1f),1);Check(nav.transform.position==Vector3.zero,"Stick dead zones and turn axis do not translate");
                nav.FlyTreeSticks(Vector2.zero,Vector2.down,.5f);Check(Vector3.Distance(nav.transform.position,Vector3.back)<.001f,"Reverse depth is time based");
                nav.ResetTreeView();nav.FlyTreeSticks(new Vector2(-1,-1),Vector2.down,1);Check(nav.transform.position.x<0&&nav.transform.position.y<0&&nav.transform.position.z<0,"All three axes support reverse input");
                nav.ResetTreeView();
                var before=nav.transform.position;prefs.SetOpen(true);nav.Fly(Vector3.up,1);Check(nav.transform.position.y>before.y,"Open preferences allows flight");prefs.SetOpen(false);
                nav.Fly(Vector3.right,100);Check(nav.transform.position.x>100,"Flight is not constrained to old tree pan bounds");
                Check(g.GetComponentsInChildren<Transform>().Count(t=>t.name.StartsWith("Person sphere "))==g.Tree.People.Count,"No directional sphere culling");
                nav.transform.SetPositionAndRotation(new Vector3(2,3,-4),Quaternion.Euler(0,35,0));nav.view.transform.localRotation=Quaternion.Euler(-20,0,0);
                var position=nav.transform.position;var rotation=nav.transform.rotation;var look=nav.view.transform.localRotation;
                g.Select(g.Tree.People.Keys.First());Check(nav.transform.position==Vector3.zero,"Scene entry resets scene navigation");
                nav.Fly(Vector3.up,1);Check(nav.transform.position==Vector3.zero,"Tree flight inactive in historical scene");
                nav.FlyTreeSticks(Vector2.one,Vector2.up,1);Check(nav.transform.position==Vector3.zero,"Two-stick flight inactive in historical scene");
                nav.Return();Check(nav.transform.position==position&&Quaternion.Angle(nav.transform.rotation,rotation)<.001f&&Quaternion.Angle(nav.view.transform.localRotation,look)<.001f,"Return restores full tree pose");
                prefs.SetTreeLayout(1,.5f,.25f);Check(nav.transform.position==position,"Layout change retains flight position");
                nav.ResetTreeView();Check(nav.transform.position==Vector3.zero&&Quaternion.Angle(nav.transform.rotation,Quaternion.identity)<.001f,"Reset restores initial view");
            }
            finally{TreeControlTemplates.Select(savedMode);prefs.SetTreeLayout(savedLayout.Gap,savedLayout.Stagger,savedLayout.Rise);prefs.SetTreePanSpeed(speed);nav.ResetTreeView();prefs.SetOpen(false);}
            return n+" connector/flight checks passed; no API requests";
        }
    }
}
