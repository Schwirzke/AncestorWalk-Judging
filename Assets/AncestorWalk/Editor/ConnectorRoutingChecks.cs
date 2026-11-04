using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace AncestorWalk.Editor
{
    public static class ConnectorRoutingChecks
    {
        sealed class Segment{public HashSet<string> People;public string Family;public Vector3 A,B;}
        static int Axis(Vector3 delta)=>Mathf.Abs(delta.x)>.0001f?0:Mathf.Abs(delta.y)>.0001f?1:2;
        static bool Conflict(Vector3 a,Vector3 b,Vector3 c,Vector3 d)
        {
            int one=Axis(b-a),two=Axis(d-c);
            bool Within(float x,float start,float end)=>x>=Mathf.Min(start,end)-.0001f&&x<=Mathf.Max(start,end)+.0001f;
            if(one!=two){int third=3-one-two;return Mathf.Abs(a[third]-c[third])<TreeLayout.ConnectorWidth&&Within(c[one],a[one],b[one])&&Within(a[two],c[two],d[two]);}
            float separation=0;for(int axis=0;axis<3;axis++)if(axis!=one)separation+=(a[axis]-c[axis])*(a[axis]-c[axis]);
            return separation<TreeLayout.ConnectorWidth*TreeLayout.ConnectorWidth&&Mathf.Min(Mathf.Max(a[one],b[one]),Mathf.Max(c[one],d[one]))-Mathf.Max(Mathf.Min(a[one],b[one]),Mathf.Min(c[one],d[one]))>.01f;
        }
        public static int UnrelatedConflicts(FamilyTree tree,TreeLayoutResult layout)
        {
            var segments=new List<Segment>();int index=0;
            foreach(var family in tree.Families.OrderBy(f=>f.Id,StringComparer.Ordinal))
            {
                var parents=family.Parents.Distinct().Where(layout.Positions.ContainsKey).ToArray();var children=family.Children.Distinct().Where(layout.Positions.ContainsKey).ToArray();if(parents.Length==0||children.Length==0)continue;
                var people=new HashSet<string>(parents.Concat(children));
                for(int i=0;i<parents.Length+children.Length;i++)
                {
                    var route=layout.Routes[index++];
                    for(int j=1;j<route.Length;j++){var a=route[j-1];var b=route[j];if((b-a).sqrMagnitude>.000001f)segments.Add(new Segment{Family=family.Id,People=people,A=a,B=b});}
                }
            }
            int conflicts=0;for(int i=0;i<segments.Count;i++)for(int j=i+1;j<segments.Count;j++)
            {var a=segments[i];var b=segments[j];if(a.Family!=b.Family&&!a.People.Overlaps(b.People)&&Conflict(a.A,a.B,b.A,b.B))conflicts++;}return conflicts;
        }
        static FamilyTree Pedigree()
        {
            var tree=new FamilyTree();for(int i=0;i<63;i++)tree.People.Add("p"+i,new Person{Id="p"+i});
            for(int i=0;i<31;i++){var f=new Family{Id="f"+i};f.Parents.Add("p"+(i*2+1));f.Parents.Add("p"+(i*2+2));f.Children.Add("p"+i);tree.Families.Add(f);}return tree;
        }
        public static string Data()
        {
            int n=0;void Check(bool ok,string why){if(!ok)throw new Exception(why);n++;}
            void CheckRoutes(FamilyTree tree,TreeLayoutResult layout)
            {
                int index=0;foreach(var f in tree.Families.OrderBy(f=>f.Id,StringComparer.Ordinal))
                {
                    var ps=f.Parents.Distinct().OrderBy(p=>layout.Positions[p].x).ThenBy(p=>p,StringComparer.Ordinal).ToArray();var cs=f.Children.Distinct().OrderBy(c=>layout.Positions[c].x).ThenBy(c=>c,StringComparer.Ordinal).ToArray();
                    if(ps.Length==0||cs.Length==0)continue;
                    Vector3? familyJoin=null;
                    foreach(var p in ps)
                    {
                        var route=layout.Routes[index++];var join=route[route.Length-1];familyJoin??=join;
                        Check(route[0]==layout.Positions[p]-Vector3.up*.5f&&route[1].y<route[0].y,"Parent exits below the sphere");
                        Check(join==familyJoin&&join.x>=ps.Min(x=>layout.Positions[x].x)-.001f&&join.x<=ps.Max(x=>layout.Positions[x].x)+.001f,"Crossbar stays in parent household");
                    }
                    foreach(var c in cs)
                    {
                        var route=layout.Routes[index++];var back=layout.Positions[c]+Vector3.forward*.5f;var childJoin=route[route.Length-2];
                        Check(route[0]==familyJoin&&route[route.Length-1]==back,"Shared parents fan to each child back center");
                        Check(childJoin.x==back.x&&childJoin.y==back.y&&childJoin.z>back.z&&childJoin.z<route[0].z,"Individual child join stays centered and level in gap");
                    }
                }
                Check(index==layout.Routes.Count,"Parent routes are drawn once per family, not once per sibling");
                Check(layout.Routes.All(r=>r.Zip(r.Skip(1),(a,b)=>new[]{Mathf.Abs(a.x-b.x),Mathf.Abs(a.y-b.y),Mathf.Abs(a.z-b.z)}.Count(d=>d>.0001f)<=1).All(x=>x)),"Every segment is axis aligned, with no slanted connectors");
                Check(layout.Routes.SelectMany(r=>r).All(p=>!float.IsNaN(p.x+p.y+p.z)&&layout.Bounds.Contains(p)),"All route points are finite and in navigation bounds");
            }
            foreach(float gap in new[]{.2f,1f,4f})foreach(float rise in new[]{0f,.5f,1f})foreach(float stagger in new[]{0f,.7f,2f})
            {
                var tree=Pedigree();var settings=new TreeLayoutSettings{Gap=gap,Rise=rise,Stagger=stagger};var layout=TreeLayout.Build(tree,settings);CheckRoutes(tree,layout);
                Check(UnrelatedConflicts(tree,layout)==0,"Six-generation unrelated branches remain separated in 3D including line width");
                var reverse=new FamilyTree();foreach(var p in tree.People.Reverse())reverse.People.Add(p.Key,p.Value);
                foreach(var f in tree.Families.AsEnumerable().Reverse()){var copy=new Family{Id=f.Id};copy.Parents.AddRange(f.Parents.AsEnumerable().Reverse());copy.Children.AddRange(f.Children.AsEnumerable().Reverse());reverse.Families.Add(copy);}
                var other=TreeLayout.Build(reverse,settings);Check(layout.Routes.Count==other.Routes.Count&&layout.Routes.Select((r,i)=>r.SequenceEqual(other.Routes[i])).All(x=>x),"Routing is deterministic under record reordering");
            }
            var siblings=Pedigree();foreach(string id in new[]{"sibling-a","sibling-b"}){siblings.People.Add(id,new Person{Id=id});siblings.Families[0].Children.Add(id);}
            CheckRoutes(siblings,TreeLayout.Build(siblings,new TreeLayoutSettings()));
            var shared=new FamilyTree();foreach(string id in new[]{"a","b","c","one","two","alone"})shared.People.Add(id,new Person{Id=id});
            var first=new Family{Id="first"};first.Parents.AddRange(new[]{"a","b"});first.Children.Add("one");shared.Families.Add(first);
            var second=new Family{Id="second"};second.Parents.AddRange(new[]{"a","c"});second.Children.Add("two");shared.Families.Add(second);
            var single=new Family{Id="single"};single.Parents.Add("c");single.Children.Add("two");shared.Families.Add(single);CheckRoutes(shared,TreeLayout.Build(shared,new TreeLayoutSettings()));
            Check(TreeLayout.Build(new FamilyTree()).Routes.Count==0,"Empty tree has no routes");
            var simple=new FamilyTree();foreach(string id in new[]{"parent-a","parent-b","child-a","child-b"})simple.People.Add(id,new Person{Id=id});
            var household=new Family{Id="household"};household.Parents.AddRange(new[]{"parent-a","parent-b"});household.Children.AddRange(new[]{"child-a","child-b"});simple.Families.Add(household);
            foreach(float rise in new[]{0f,.25f,1f})
            {
                var level=TreeLayout.Build(simple,new TreeLayoutSettings{Rise=rise});
                foreach(var route in level.Routes.Skip(2))Check(route.Skip(1).All(p=>Mathf.Abs(p.y-route[route.Length-1].y)<.0001f),"Unobstructed child routes stay level with their joins without a down/up detour");
            }
            string report=n+" connector routing checks passed: orthogonal lanes, six generations, gaps/rise/stagger, separated branches, shared sibling rails, single/shared parents, deterministic routes, endpoints and bounds.";Directory.CreateDirectory("Artifacts");File.WriteAllText("Artifacts/connector-routing-checks.txt",report);return report;
        }
    }
}
