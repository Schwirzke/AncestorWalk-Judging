using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AncestorWalk
{
    // A view projection only: the complete document, caches and jobs remain unchanged.
    public sealed class SiblingVisibility
    {
        [Serializable] public sealed class State {public List<string> anchors=new();}
        readonly FamilyTree source;
        readonly Dictionary<string,HashSet<string>> parents=new(),children=new(),neighbors=new();
        readonly Dictionary<string,List<string[]>> childGroups=new();
        readonly Dictionary<string,HashSet<string>> siblingCache=new();
        public static string PreferenceKey(string tree)=>"AncestorWalk.SiblingVisibility.v1."+tree;
        public static State Load(string tree)
        {
            try{var state=JsonUtility.FromJson<State>(PlayerPrefs.GetString(PreferenceKey(tree),"{}"));return state?.anchors!=null&&state.anchors.Count<=2000?state:new State();}catch(ArgumentException){return new State();}
        }
        public SiblingVisibility(FamilyTree tree)
        {
            source=tree;
            foreach(string id in tree.People.Keys){parents[id]=new();children[id]=new();neighbors[id]=new();childGroups[id]=new();}
            void Link(string a,string b){if(a==b)return;neighbors[a].Add(b);neighbors[b].Add(a);}
            foreach(var family in tree.Families)
            {
                var ps=family.Parents.Where(parents.ContainsKey).Distinct().ToArray();var cs=family.Children.Where(parents.ContainsKey).Distinct().ToArray();
                foreach(var p in ps)foreach(var c in cs){parents[c].Add(p);children[p].Add(c);Link(p,c);}
                foreach(var p in ps.Skip(1))Link(ps[0],p);
                foreach(var c in cs){childGroups[c].Add(cs);if(cs.Length>0)Link(cs[0],c);}
            }
        }
        public IReadOnlyCollection<string> Siblings(string id)
        {
            if(siblingCache.TryGetValue(id,out var cached))return cached;
            cached=new HashSet<string>();if(!parents.ContainsKey(id))return cached;
            foreach(var p in parents[id])cached.UnionWith(children[p]);foreach(var group in childGroups[id])cached.UnionWith(group);cached.Remove(id);siblingCache[id]=cached;return cached;
        }
        static HashSet<string> Reach(IEnumerable<string> seeds,Dictionary<string,HashSet<string>> graph,HashSet<string> blocked=null)
        {
            var seen=new HashSet<string>();var pending=new Queue<string>();foreach(var id in seeds)if(graph.ContainsKey(id)&&(blocked==null||!blocked.Contains(id))&&seen.Add(id))pending.Enqueue(id);
            while(pending.Count>0){string id=pending.Dequeue();foreach(var next in graph[id])if((blocked==null||!blocked.Contains(next))&&seen.Add(next))pending.Enqueue(next);}return seen;
        }
        public HashSet<string> Hidden(State state)
        {
            var hidden=new HashSet<string>();
            foreach(string anchor in state.anchors.Distinct())
            {
                if(!source.People.ContainsKey(anchor)||hidden.Contains(anchor)||Siblings(anchor).Count==0)continue;
                var protectedLine=Reach(new[]{anchor},parents);protectedLine.UnionWith(Reach(new[]{anchor},children));
                var branches=Reach(Siblings(anchor).Where(id=>!protectedLine.Contains(id)),children,protectedLine);
                var component=Reach(new[]{anchor},neighbors);var retained=Reach(new[]{anchor},neighbors,branches);
                component.ExceptWith(retained);hidden.UnionWith(component);
            }
            return hidden;
        }
        public FamilyTree Project(State state)
        {
            var hidden=Hidden(state);if(hidden.Count==0)return source;var visible=new FamilyTree();
            foreach(var p in source.People)if(!hidden.Contains(p.Key))visible.People.Add(p.Key,p.Value);
            foreach(var family in source.Families)
            {
                var f=new Family{Id=family.Id};f.Parents.AddRange(family.Parents.Where(visible.People.ContainsKey));f.Children.AddRange(family.Children.Where(visible.People.ContainsKey));
                if(f.Children.Count>0||f.Parents.Count>1)visible.Families.Add(f);
            }
            visible.Warnings.AddRange(source.Warnings);return visible;
        }
    }
}
