using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AncestorWalk
{
    public sealed class RelationshipRoute
    {
        public string PersonId;
        public Vector3[] Points;
    }
    public sealed class TreeLayoutResult
    {
        public readonly Dictionary<string, Vector3> Positions = new();
        public readonly List<Vector3[]> Routes = new();
        // Shared trunks are neutral and drawn once; individual branches carry recorded sex.
        public readonly List<RelationshipRoute> Connectors = new();
        public readonly List<string> Warnings = new();
        public readonly List<GhostSlot> Ghosts=new();
        public readonly Dictionary<string,Vector3> GhostPositions=new();
        public Bounds Bounds;
        public Vector2 ClampPan(Vector2 pan) => new Vector2(
            Mathf.Clamp(pan.x, -Bounds.max.x, -Bounds.min.x),
            Mathf.Clamp(pan.y, 6.5f - (Bounds.max.z - TreeLayout.Size*.5f), 6));
    }

    // Longest descendant path defines depth, independent of GEDCOM record order.
    public static class TreeLayout
    {
        sealed class RowUnit
        {
            public string[] Members;
            public float Desired, Center;
        }
        sealed class FamilyRouting
        {
            public string Id;public string[] Parents,Children;
            public float ChildBack,ParentFront,Clearance,MinX,MaxX,LaneY,LaneZ;
            public int Lane;public Vector3 Join;
            public HashSet<string> People;
            public Vector3[][] Paths;
            public (Vector3 A,Vector3 B)[] Segments;
        }
        static void PreparePaths(FamilyRouting family,Dictionary<string,Vector3> positions)
        {
            var paths=new List<Vector3[]>();var join=family.Join;
            foreach(string parent in family.Parents)
            {
                var a=positions[parent]-Vector3.up*(Size*.5f);
                paths.Add(new[]{a,new Vector3(a.x,join.y,a.z),new Vector3(a.x,join.y,join.z),join});
            }
            foreach(string child in family.Children)
            {
                var back=positions[child]+Vector3.forward*(Size*.5f);var childJoin=new Vector3(back.x,back.y,family.ChildBack+family.Clearance);
                paths.Add(new[]{join,new Vector3(join.x,family.LaneY,join.z),new Vector3(join.x,family.LaneY,family.LaneZ),new Vector3(back.x,family.LaneY,family.LaneZ),new Vector3(back.x,family.LaneY,childJoin.z),childJoin,back});
            }
            family.Paths=paths.ToArray();family.Segments=paths.SelectMany(r=>r.Zip(r.Skip(1),(a,b)=>(A:a,B:b))).Where(s=>(s.B-s.A).sqrMagnitude>.000001f).ToArray();
        }
        static bool RoutesConflict(FamilyRouting a,FamilyRouting b)
        {
            if(a.People.Overlaps(b.People)||a.MaxX+ConnectorWidth<b.MinX||b.MaxX+ConnectorWidth<a.MinX)return false;
            foreach(var one in a.Segments)foreach(var two in b.Segments)
            {
                // For axis-aligned segments, interval separation gives their exact 3D distance.
                float distance=0;
                for(int axis=0;axis<3;axis++)
                {
                    float gap=Mathf.Max(0,Mathf.Max(Mathf.Min(one.A[axis],one.B[axis]),Mathf.Min(two.A[axis],two.B[axis]))-Mathf.Min(Mathf.Max(one.A[axis],one.B[axis]),Mathf.Max(two.A[axis],two.B[axis])));
                    distance+=gap*gap;
                }
                if(distance<ConnectorWidth*ConnectorWidth)return true;
            }
            return false;
        }
        public const float Size = 1f;
        public const float ConnectorWidth=.035f;
        public static TreeLayoutResult Build(FamilyTree tree,TreeLayoutSettings settings=null)
        {
            var result = new TreeLayoutResult();
            settings=(settings??TreeLayoutSettings.Load()).Validated();
            float spacing=Size+settings.Gap;
            var ids = tree.People.Keys.OrderBy(id => id, StringComparer.Ordinal).ToArray();
            if (ids.Length == 0) return result;
            var children = ids.ToDictionary(id => id, _ => new SortedSet<string>(StringComparer.Ordinal));
            var parents = ids.ToDictionary(id => id, _ => new SortedSet<string>(StringComparer.Ordinal));
            var partners = ids.ToDictionary(id => id, _ => new SortedSet<string>(StringComparer.Ordinal));
            foreach (var f in tree.Families)
            {
                foreach (var id in f.Parents.Concat(f.Children).Distinct())
                    if (!children.ContainsKey(id)) result.Warnings.Add("Missing person " + id);
                foreach (var p in f.Parents.Where(children.ContainsKey))
                    foreach (var c in f.Children.Where(children.ContainsKey))
                    { children[p].Add(c); parents[c].Add(p); }
                var coParents=f.Parents.Distinct().Where(children.ContainsKey).ToArray();
                foreach(var p in coParents)foreach(var other in coParents)if(p!=other)partners[p].Add(other);
            }
            // Co-parents and siblings share generation constraints, even when only
            // one sibling has descendants. Collapse these constraints before ranking.
            var representative=ids.ToDictionary(id=>id,id=>id);
            string Root(string id){while(representative[id]!=id){representative[id]=representative[representative[id]];id=representative[id];}return id;}
            void Unite(string[] group){foreach(var id in group.Skip(1)){var a=Root(group[0]);var b=Root(id);if(StringComparer.Ordinal.Compare(a,b)<0)representative[b]=a;else representative[a]=b;}}
            foreach(var family in tree.Families)
            {Unite(family.Parents.Where(children.ContainsKey).ToArray());Unite(family.Children.Where(children.ContainsKey).ToArray());}
            foreach(var siblings in children.Values)Unite(siblings.ToArray());
            var groups=ids.GroupBy(Root).ToDictionary(g=>g.Key,g=>g.ToArray());
            var groupChildren=groups.Keys.ToDictionary(id=>id,_=>new HashSet<string>());
            var groupParents=groups.Keys.ToDictionary(id=>id,_=>new HashSet<string>());
            foreach(var p in ids)foreach(var c in children[p]){groupChildren[Root(p)].Add(Root(c));groupParents[Root(c)].Add(Root(p));}
            var remaining=groups.Keys.ToDictionary(id=>id,id=>groupChildren[id].Count);
            var groupDepth=groups.Keys.ToDictionary(id=>id,_=>0);
            var ready=new SortedSet<string>(groups.Keys.Where(id=>remaining[id]==0),StringComparer.Ordinal);
            var processed=new HashSet<string>();
            while(ready.Count>0)
            {
                var id=ready.Min;ready.Remove(id);foreach(var member in groups[id])processed.Add(member);
                foreach(var p in groupParents[id]){groupDepth[p]=Math.Max(groupDepth[p],groupDepth[id]+1);if(--remaining[p]==0)ready.Add(p);}
            }
            var depth=ids.ToDictionary(id=>id,id=>groupDepth[Root(id)]);
            if (processed.Count != ids.Length) result.Warnings.Add("Cyclic ancestry: affected relationships omitted; people remain visible.");
            // Invalid cyclic edges cannot constrain placement or produce backward connectors.
            bool Valid(string p, string c) => processed.Contains(p) && processed.Contains(c);
            var visited = new HashSet<string>(); float cursor = 0;
            foreach (var seed in ids)
            {
                if (!visited.Add(seed)) continue;
                var component = new List<string>(); var queue = new Queue<string>(); queue.Enqueue(seed);
                while (queue.Count > 0)
                {
                    var id = queue.Dequeue(); component.Add(id);
                    foreach (var next in children[id].Concat(parents[id]).Concat(partners[id])) if (visited.Add(next)) queue.Enqueue(next);
                }
                var local = new Dictionary<string, float>();
                foreach (var row in component.GroupBy(id => depth[id]).OrderBy(g => g.Key))
                {
                    float Desired(string id)
                    {
                        var placed = children[id].Where(c => Valid(id,c) && local.ContainsKey(c)).ToArray();
                        if(placed.Length==0)return 0;
                        // A co-parent row must center on the children's displayed X, not
                        // their pre-stagger coordinates. Single-parent branches retain stagger.
                        bool paired=placed.Any(c=>parents[c].Any(p=>p!=id&&Valid(p,c)&&depth[p]==row.Key));
                        return paired?placed.Average(c=>local[c]+depth[c]*settings.Stagger)-row.Key*settings.Stagger:placed.Average(c=>local[c]);
                    }
                    // Pack co-parent households as indivisible units. Reserve horizontal
                    // space between households, never between partners for their ancestors.
                    // A shared partner belongs to one compact cluster, without duplicates.
                    var unplaced=new HashSet<string>(row);
                    var units=new List<RowUnit>();
                    foreach(var rowSeed in row.OrderBy(id=>id,StringComparer.Ordinal))
                    {
                        if(!unplaced.Remove(rowSeed))continue;
                        var members=new List<string>();var pending=new Queue<string>();pending.Enqueue(rowSeed);
                        while(pending.Count>0)
                        {
                            var member=pending.Dequeue();members.Add(member);
                            foreach(var next in partners[member])if(unplaced.Remove(next))pending.Enqueue(next);
                        }
                        var ordered=members.OrderBy(Desired).ThenBy(id=>id,StringComparer.Ordinal).ToArray();
                        units.Add(new RowUnit{Members=ordered,Desired=ordered.Average(Desired)});
                    }
                    var orderedUnits=units.OrderBy(u=>u.Desired).ThenBy(u=>u.Members.OrderBy(id=>id,StringComparer.Ordinal).First(),StringComparer.Ordinal).ToArray();
                    float x=float.NegativeInfinity;RowUnit previous=null;
                    foreach(var unit in orderedUnits)
                    {
                        float between=previous==null?spacing:(previous.Members.Length+unit.Members.Length)*spacing*.5f;
                        if(previous!=null&&(previous.Members.Length>1||unit.Members.Length>1))between+=spacing*.5f;
                        x=Math.Max(unit.Desired,x+between);
                        unit.Center=x;previous=unit;
                    }
                    float shift=orderedUnits.Sum(u=>(u.Center-u.Desired)*u.Members.Length)/row.Count();
                    foreach(var unit in orderedUnits)for(int i=0;i<unit.Members.Length;i++)
                        local[unit.Members[i]]=unit.Center-shift+(i-(unit.Members.Length-1)*.5f)*spacing;
                }
                float min = component.Min(id=>local[id]+depth[id]*settings.Stagger), max = component.Max(id=>local[id]+depth[id]*settings.Stagger);
                foreach (var id in component) result.Positions[id] = new Vector3(cursor + local[id] - min + depth[id]*settings.Stagger, 2.1f + depth[id]*settings.Rise, 6.5f + depth[id] * spacing);
                cursor += max - min + spacing * 2;
            }
            float center = (result.Positions.Values.Min(p => p.x) + result.Positions.Values.Max(p => p.x)) * .5f;
            foreach (var id in ids) result.Positions[id] -= Vector3.right * center;
            result.Bounds = new Bounds(result.Positions[ids[0]], Vector3.one * Size);
            foreach (var pos in result.Positions.Values) result.Bounds.Encapsulate(new Bounds(pos, Vector3.one * Size));
            var routing=new List<FamilyRouting>();
            foreach (var family in tree.Families.OrderBy(f => f.Id, StringComparer.Ordinal))
            {
                var ps = family.Parents.Distinct().Where(result.Positions.ContainsKey).ToArray();
                var cs = family.Children.Distinct().Where(result.Positions.ContainsKey).ToArray();
                var edges = (from p in ps from c in cs where Valid(p,c) select (p,c)).ToArray();
                if (edges.Length == 0) continue;
                // Keep the parent crossbar inside its household. Overlapping
                // branch spans receive separate orthogonal lanes in the gap.
                var familyParents=edges.Select(e=>e.p).Distinct().OrderBy(p=>result.Positions[p].x).ThenBy(p=>p,StringComparer.Ordinal).ToArray();
                var familyChildren=edges.Select(e=>e.c).Distinct().OrderBy(c=>result.Positions[c].x).ThenBy(c=>c,StringComparer.Ordinal).ToArray();
                float childBack=familyChildren.Max(c=>result.Positions[c].z)+Size*.5f;
                float parentFront=familyParents.Min(p=>result.Positions[p].z)-Size*.5f;
                float clearance=Mathf.Min(.12f,Mathf.Max(.02f,(parentFront-childBack)*.2f));
                var familyJoin=new Vector3(familyParents.Average(p=>result.Positions[p].x),familyParents.Min(p=>result.Positions[p].y)-Size*.5f-.08f,parentFront-clearance);
                var members=familyParents.Concat(familyChildren).Select(id=>result.Positions[id].x).ToArray();
                routing.Add(new FamilyRouting{Id=family.Id,Parents=familyParents,Children=familyChildren,People=new HashSet<string>(familyParents.Concat(familyChildren)),ChildBack=childBack,ParentFront=parentFront,Clearance=clearance,Join=familyJoin,MinX=members.Min(),MaxX=members.Max()});
            }
            foreach(var gap in routing.GroupBy(f=>(f.ChildBack,f.ParentFront)))
            {
                var ends=new List<float>();
                foreach(var family in gap.OrderBy(f=>f.MinX).ThenBy(f=>f.MaxX).ThenBy(f=>f.Id,StringComparer.Ordinal))
                {
                    int lane=ends.FindIndex(end=>end+ConnectorWidth*3<family.MinX);
                    if(lane<0){lane=ends.Count;ends.Add(family.MaxX);}else ends[lane]=family.MaxX;
                    family.Lane=lane;
                }
                float floor=gap.Min(f=>Mathf.Min(f.Join.y,f.Children.Min(c=>result.Positions[c].y)-Size*.5f))-.12f;
                var accepted=new List<FamilyRouting>();
                foreach(var family in gap.OrderBy(f=>f.MinX).ThenBy(f=>f.MaxX).ThenBy(f=>f.Id,StringComparer.Ordinal))
                {
                    float near=family.ChildBack+family.Clearance,far=family.Join.z;
                    float margin=Mathf.Min(ConnectorWidth*1.5f,(far-near)*.4f);
                    // Start level with the child join, without the universal down-and-up dip.
                    family.LaneY=result.Positions[family.Children[0]].y;family.LaneZ=(near+far)*.5f;
                    PreparePaths(family,result.Positions);
                    if(accepted.Any(other=>RoutesConflict(family,other)))
                    {
                        // Only a real crossing/overlap earns a separated lower lane.
                        family.LaneY=floor-family.Lane*.14f;
                        family.LaneZ=Mathf.Lerp(near+margin,far-margin,(family.Lane+.5f)/ends.Count);
                        PreparePaths(family,result.Positions);
                    }
                    accepted.Add(family);
                }
            }
            foreach(var family in routing)
            {
                var familyJoin=family.Join;
                result.Routes.AddRange(family.Paths);
                foreach(var parent in family.Parents)
                {
                    var a=result.Positions[parent]-Vector3.up*(Size*.5f);
                    var drop=new Vector3(a.x,familyJoin.y,a.z);
                    result.Connectors.Add(new RelationshipRoute{PersonId=parent,Points=new[]{a,drop,new Vector3(a.x,familyJoin.y,familyJoin.z)}});
                }
                float parentMin=family.Parents.Min(p=>result.Positions[p].x),parentMax=family.Parents.Max(p=>result.Positions[p].x);
                result.Connectors.Add(new RelationshipRoute{Points=new[]{new Vector3(parentMin,familyJoin.y,familyJoin.z),new Vector3(parentMax,familyJoin.y,familyJoin.z)}});
                var laneJoin=new Vector3(familyJoin.x,family.LaneY,family.LaneZ);
                result.Connectors.Add(new RelationshipRoute{Points=new[]{familyJoin,new Vector3(familyJoin.x,family.LaneY,familyJoin.z),laneJoin}});
                float childMin=Mathf.Min(familyJoin.x,family.Children.Min(c=>result.Positions[c].x)),childMax=Mathf.Max(familyJoin.x,family.Children.Max(c=>result.Positions[c].x));
                result.Connectors.Add(new RelationshipRoute{Points=new[]{new Vector3(childMin,family.LaneY,family.LaneZ),new Vector3(childMax,family.LaneY,family.LaneZ)}});
                foreach(var child in family.Children)
                {
                    var back=result.Positions[child]+Vector3.forward*(Size*.5f);
                    var childJoin=new Vector3(back.x,back.y,family.ChildBack+family.Clearance);
                    result.Connectors.Add(new RelationshipRoute{PersonId=child,Points=new[]{new Vector3(back.x,family.LaneY,family.LaneZ),new Vector3(back.x,family.LaneY,childJoin.z),childJoin,back}});
                }
            }
            foreach(var route in result.Routes)foreach(var point in route)result.Bounds.Encapsulate(new Bounds(point,Vector3.one*ConnectorWidth));
            return result;
        }
    }
}
