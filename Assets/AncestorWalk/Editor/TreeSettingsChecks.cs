using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
namespace AncestorWalk.Editor
{
    public static class TreeSettingsChecks
    {
        public static string Run()
        {
            int n=0;void Check(bool pass,string message){if(!pass)throw new Exception(message);n++;}
            var tree=new FamilyTree();for(int i=0;i<4;i++)tree.People.Add(i.ToString(),new Person{Id=i.ToString()});
            for(int i=1;i<4;i++){var f=new Family{Id=i.ToString()};f.Parents.Add(i.ToString());f.Children.Add((i-1).ToString());tree.Families.Add(f);}
            var layout=TreeLayout.Build(tree,new TreeLayoutSettings{Gap=1,Stagger=.5f,Rise=.25f});
            for(int i=1;i<4;i++)
            {var delta=layout.Positions[i.ToString()]-layout.Positions[(i-1).ToString()];Check(Mathf.Abs(delta.z-2)<.001f,"One metre gap in depth");Check(Mathf.Abs(delta.y-.25f)<.001f,"Quarter metre rise each generation");Check(Mathf.Abs(delta.x-.5f)<.001f,"Half metre stagger each generation");}
            var siblings=new Family{Id="siblings"};tree.People.Add("sibling",new Person{Id="sibling"});siblings.Parents.Add("1");siblings.Children.Add("0");siblings.Children.Add("sibling");tree.Families.Add(siblings);
            layout=TreeLayout.Build(tree,new TreeLayoutSettings{Gap=1,Stagger=.5f,Rise=.25f});Check(Mathf.Abs(Mathf.Abs(layout.Positions["0"].x-layout.Positions["sibling"].x)-2)<.001f,"One metre edge gap between siblings");
            var custom=TreeLayout.Build(tree,new TreeLayoutSettings{Gap=2,Stagger=1,Rise=.75f});
            Check(Mathf.Abs(custom.Positions["3"].z-custom.Positions["2"].z-3)<.001f,"Custom row gap");Check(Mathf.Abs(custom.Positions["3"].y-custom.Positions["2"].y-.75f)<.001f,"Custom rise");
            foreach(var route in custom.Routes)foreach(var point in route)Check(!float.IsNaN(point.x+point.y+point.z),"Valid routes");
            var g=UnityEngine.Object.FindAnyObjectByType<AncestorGallery>();var prefs=UnityEngine.Object.FindAnyObjectByType<NavigationPreferences>();
            var saved=TreeLayoutSettings.Load();g.ShowTree();g.ResetTreePan();g.PanTree(new Vector2(.3f,-.2f));var pan=g.TreePan;
            try
            {
                prefs.SetTreeLayout(2,1,.75f);var loaded=TreeLayoutSettings.Load();
                Check(loaded.Gap==2&&loaded.Stagger==1&&loaded.Rise==.75f,"Settings persist and reload");
                Check(g.TreePan==pan,"Rebuild preserves pan within bounds");
                var actual=TreeLayout.Build(g.Tree,loaded);Check(actual.Positions.All(p=>g.Layout.Positions[p.Key]==p.Value),"Live rebuild uses settings");
                prefs.SetOpen(true);Check(prefs.IsOpen,"Settings remain open during adjustment");
                var gap=prefs.GetComponentsInChildren<Slider>(true).Single(s=>s.name=="Sphere gap");gap.value=1.5f;
                Check(TreeLayoutSettings.Load().Gap==1.5f,"UI slider updates saved layout");
            }
            finally{prefs.SetTreeLayout(saved.Gap,saved.Stagger,saved.Rise);prefs.SetOpen(false);g.ResetTreePan();}
            return n+" tree setting checks passed";
        }
    }
}
