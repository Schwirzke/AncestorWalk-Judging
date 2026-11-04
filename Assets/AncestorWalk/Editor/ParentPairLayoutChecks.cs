using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace AncestorWalk.Editor
{
    public static class ParentPairLayoutChecks
    {
        public static string Run()
        {
            int n=0;void Check(bool ok,string why){if(!ok)throw new Exception(why);n++;}
            FamilyTree Pedigree(int generations)
            {
                var tree=new FamilyTree();int count=(1<<generations)-1;
                for(int i=0;i<count;i++)tree.People.Add("p"+i,new Person{Id="p"+i});
                for(int i=0;i<(count-1)/2;i++){var f=new Family{Id="f"+i};f.Children.Add("p"+i);f.Parents.Add("p"+(2*i+1));f.Parents.Add("p"+(2*i+2));tree.Families.Add(f);}
                return tree;
            }
            void CheckLayout(FamilyTree tree,TreeLayoutSettings settings,bool isolatedPairs)
            {
                var result=TreeLayout.Build(tree,settings);float pitch=TreeLayout.Size+settings.Gap;
                Check(result.Positions.Count==tree.People.Count,"No duplicated or omitted people");
                foreach(var row in result.Positions.Values.GroupBy(p=>p.z))
                {var xs=row.Select(p=>p.x).OrderBy(x=>x).ToArray();for(int i=1;i<xs.Length;i++)Check(xs[i]-xs[i-1]>=pitch-.001f,"Every row has nonoverlapping spheres with saved gap");}
                foreach(var f in tree.Families)
                {
                    var ps=f.Parents.Distinct().Where(result.Positions.ContainsKey).ToArray();var cs=f.Children.Distinct().Where(result.Positions.ContainsKey).ToArray();
                    if(isolatedPairs&&ps.Length==2)Check(Mathf.Abs(Mathf.Abs(result.Positions[ps[0]].x-result.Positions[ps[1]].x)-pitch)<.001f,"Parent pair is exactly one saved pitch apart");
                    foreach(var p in ps)foreach(var c in cs)Check(result.Positions[p].z>result.Positions[c].z,"Ancestor generations retain depth order");
                }
                foreach(var p in result.Positions.Values)Check(!float.IsInfinity(p.x)&&!float.IsNaN(p.x)&&result.Bounds.Contains(p),"Finite positions inside navigation bounds");
                var reversed=new FamilyTree();foreach(var p in tree.People.Reverse())reversed.People.Add(p.Key,p.Value);
                foreach(var f in tree.Families.AsEnumerable().Reverse())
                {var copy=new Family{Id=f.Id};copy.Parents.AddRange(f.Parents.AsEnumerable().Reverse());copy.Children.AddRange(f.Children.AsEnumerable().Reverse());reversed.Families.Add(copy);}
                var other=TreeLayout.Build(reversed,settings);Check(result.Positions.All(p=>other.Positions[p.Key]==p.Value),"Deterministic under people, family, parent and child reordering");
            }
            foreach(float gap in new[]{.2f,1f,4f})foreach(float stagger in new[]{0f,.5f,2f})
                CheckLayout(Pedigree(6),new TreeLayoutSettings{Gap=gap,Stagger=stagger,Rise=.25f},true);
            var branches=TreeLayout.Build(Pedigree(3),new TreeLayoutSettings{Gap=1});
            Check(Mathf.Min(branches.Positions["p5"].x,branches.Positions["p6"].x)-Mathf.Max(branches.Positions["p3"].x,branches.Positions["p4"].x)>=2.999f,"Different parent pairs have a larger gap than partners inside each pair");
            var asymmetric=Pedigree(5);asymmetric.Families.RemoveAll(f=>int.Parse(f.Id.Substring(1))%3==0&&f.Id!="f0");
            asymmetric.People.Add("sibling",new Person{Id="sibling"});asymmetric.Families[0].Children.Add("sibling");
            CheckLayout(asymmetric,new TreeLayoutSettings{Gap=1,Stagger=1.3f,Rise=.5f},true);
            var complex=new FamilyTree();foreach(string id in new[]{"a","b","c","d","e","kid1","kid2","kid3","alone"})complex.People.Add(id,new Person{Id=id});
            void Family(string id,string[] parents,string[] kids){var f=new Family{Id=id};f.Parents.AddRange(parents);f.Children.AddRange(kids);complex.Families.Add(f);}
            Family("first",new[]{"a","b"},new[]{"kid1"});Family("second",new[]{"a","c"},new[]{"kid2"});Family("no-children",new[]{"d","e"},Array.Empty<string>());
            CheckLayout(complex,new TreeLayoutSettings{Gap=1,Stagger=.5f,Rise=.25f},false);
            var grouped=TreeLayout.Build(complex,new TreeLayoutSettings{Gap=1});
            var cluster=new[]{"a","b","c"}.Select(id=>grouped.Positions[id]).ToArray();
            Check(Mathf.Abs(cluster.Max(p=>p.x)-cluster.Min(p=>p.x)-4)<.001f,"Shared-partner family stays one compact household cluster");
            Check(Mathf.Abs(Mathf.Abs(grouped.Positions["d"].x-grouped.Positions["e"].x)-2)<.001f,"Co-parents without children stay together");
            Family("third-parent",new[]{"a","b","c"},new[]{"kid3"});CheckLayout(complex,new TreeLayoutSettings{Gap=.5f,Stagger=2,Rise=1},false);
            Family("missing",new[]{"missing"},new[]{"kid1"});Family("cycle",new[]{"kid1"},new[]{"a"});
            var invalid=TreeLayout.Build(complex,new TreeLayoutSettings());
            Check(invalid.Positions.Count==complex.People.Count&&invalid.Warnings.Any(w=>w.Contains("Missing"))&&invalid.Warnings.Any(w=>w.Contains("Cyclic")),"Invalid families remain bounded and diagnostic");
            Directory.CreateDirectory("Artifacts");string report=n+" parent-pair layout checks passed: six generations, custom gaps/stagger/rise, asymmetric branches, siblings, disconnected people, shared partners, three parents, childless pairs, invalid relationships and record-order determinism.";
            File.WriteAllText("Artifacts/parent-pair-layout-checks.txt",report);return report;
        }
    }
}
