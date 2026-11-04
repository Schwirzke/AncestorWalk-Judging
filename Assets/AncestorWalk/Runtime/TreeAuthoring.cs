using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AncestorWalk
{
    // Presentation-only slots. They never enter a TreeDocument or an AI queue.
    public sealed class GhostSlot
    {
        public string TreeId,Id,FamilyId,ChildId,Signature;
        public int Ordinal;
        public string[] Children,Parents;
        public bool IsRoot=>ChildId==null;
    }
    public static class TreeAuthoring
    {
        public static string PreferenceKey(string tree)=>"AncestorWalk.BuildTree."+tree;
        public static List<GhostSlot> Slots(string treeId,FamilyTree tree)
        {
            var result=new List<GhostSlot>();
            void Add(string family,string child,string[] children,string[] parents,int ordinal)
            {
                string key=family??"root";
                result.Add(new GhostSlot{TreeId=treeId,FamilyId=family,ChildId=child,Children=children,Parents=parents,Ordinal=ordinal,
                    Id="ghost-"+PanoramaCache.Hash(System.Text.Encoding.UTF8.GetBytes(treeId+"\n"+key+"\n"+ordinal)),
                    Signature=string.Join("\n",parents)+"\n->\n"+string.Join("\n",children)});
            }
            if(tree.People.Count==0){Add(null,null,Array.Empty<string>(),Array.Empty<string>(),0);return result;}
            var grouped=new HashSet<string>();
            foreach(var family in tree.Families.OrderBy(f=>f.Id,StringComparer.Ordinal))
            {
                var children=family.Children.Distinct().Where(tree.People.ContainsKey).OrderBy(x=>x,StringComparer.Ordinal).ToArray();foreach(string id in children)grouped.Add(id);
                // Preserve imported diagnostics; do not silently replace a dangling reference.
                if(children.Length==0||family.Parents.Any(id=>!tree.People.ContainsKey(id)))continue;
                var parents=family.Parents.Distinct().OrderBy(x=>x,StringComparer.Ordinal).ToArray();
                for(int i=parents.Length;i<2;i++)Add(family.Id,children[0],children,parents,i);
            }
            foreach(string child in tree.People.Keys.Where(id=>!grouped.Contains(id)).OrderBy(id=>id,StringComparer.Ordinal))
            {
                string family="family-"+PanoramaCache.Hash(System.Text.Encoding.UTF8.GetBytes(treeId+"\n"+child));
                for(int i=0;i<2;i++)Add(family,child,new[]{child},Array.Empty<string>(),i);
            }
            return result;
        }
        public static TreeDocument AddPerson(TreeDocument current,GhostSlot slot,out string personId)
        {
            if(ProductDemoAuthoring.ReadOnly(current))throw new InvalidOperationException("Copy the demo before editing.");
            if(slot==null||slot.TreeId!=current.Id)throw new InvalidOperationException("This parent slot belongs to another tree.");
            slot=Slots(current.Id,current.ToTree()).SingleOrDefault(s=>s.Id==slot.Id&&s.Signature==slot.Signature);
            if(slot==null)throw new InvalidOperationException("This parent slot changed. Choose its current ghost sphere.");
            var copy=current.Copy();personId="person-"+Guid.NewGuid().ToString("N");copy.People.Add(new Person{Id=personId,Name="New person"});
            if(!slot.IsRoot)
            {
                var family=copy.Families.SingleOrDefault(f=>f.Id==slot.FamilyId);
                if(family==null){family=new FamilyRecord{Id=slot.FamilyId,Children=slot.Children.ToList()};copy.Families.Add(family);}
                family.Parents.Add(personId);
            }
            ValidateChange(current,copy);return copy;
        }
        public static void ValidateChange(TreeDocument current,TreeDocument next,string detailsId=null)
        {
            if(ProductDemoAuthoring.ReadOnly(current)||next.Id!=current.Id||ProductDemoAuthoring.ReadOnly(next))throw new InvalidOperationException("Authoring must stay in the current editable tree.");
            next.Validate(true);
            var mask=current.Copy();mask.People=next.People;mask.Families=next.Families;mask.UpdatedUtc=next.UpdatedUtc;
            if(JsonUtility.ToJson(mask)!=JsonUtility.ToJson(next))throw new InvalidOperationException("Authoring cannot change other tree data.");
            var old=current.People.ToDictionary(p=>p.Id);var added=next.People.Where(p=>!old.ContainsKey(p.Id)).Select(p=>p.Id).ToHashSet();
            if(next.People.Count!=current.People.Count+(detailsId==null?1:0)||added.Count!=(detailsId==null?1:0)||detailsId!=null&&!old.ContainsKey(detailsId))throw new InvalidOperationException("Invalid person authoring change.");
            foreach(var p in current.People)
            {var revised=next.People.SingleOrDefault(x=>x.Id==p.Id);if(revised==null||p.Id!=detailsId&&JsonUtility.ToJson(p)!=JsonUtility.ToJson(revised))throw new InvalidOperationException("An unrelated person changed.");}
            if(detailsId!=null){if(JsonUtility.ToJson(new FamilyList{Items=current.Families})!=JsonUtility.ToJson(new FamilyList{Items=next.Families}))throw new InvalidOperationException("Save relationships separately.");return;}
            foreach(var f in current.Families)
            {
                var revised=next.Families.SingleOrDefault(x=>x.Id==f.Id);
                if(revised==null||!f.Children.SequenceEqual(revised.Children)||!f.Parents.SequenceEqual(revised.Parents.Where(id=>!added.Contains(id)))||revised.Parents.Except(f.Parents).Any(id=>!added.Contains(id)))throw new InvalidOperationException("An unrelated relationship changed.");
            }
            if(next.Families.Count(f=>f.Parents.Any(added.Contains))!=(current.People.Count==0?0:1))throw new InvalidOperationException("Add one parent connection at a time.");
            foreach(var f in next.Families.Where(f=>current.Families.All(old=>old.Id!=f.Id)))
                if(f.Parents.Count!=1||!added.Contains(f.Parents[0])||f.Children.Count==0||f.Children.Any(id=>!old.ContainsKey(id)))throw new InvalidOperationException("Invalid new parent family.");
        }
        [Serializable] sealed class FamilyList {public List<FamilyRecord> Items;}
        public static TreeLayoutResult Layout(string treeId,FamilyTree tree,TreeLayoutSettings settings,FamilyTree canonical=null)
        {
            var slots=Slots(treeId,canonical??tree).Where(s=>s.IsRoot||s.Children.Any(tree.People.ContainsKey)).ToList();var projected=new FamilyTree();foreach(var p in tree.People)projected.People.Add(p.Key,p.Value);
            foreach(var family in tree.Families){var f=new Family{Id=family.Id};f.Parents.AddRange(family.Parents);f.Children.AddRange(family.Children);projected.Families.Add(f);}projected.Warnings.AddRange(tree.Warnings);
            foreach(var slot in slots)
            {
                projected.People.Add(slot.Id,new Person{Id=slot.Id,Name="Ghost"});if(slot.IsRoot)continue;
                var f=projected.Families.SingleOrDefault(f=>f.Id==slot.FamilyId);if(f==null){f=new Family{Id=slot.FamilyId};f.Children.AddRange(slot.Children.Where(tree.People.ContainsKey));projected.Families.Add(f);}f.Parents.Add(slot.Id);
            }
            var result=TreeLayout.Build(projected,settings);
            foreach(var slot in slots){result.Ghosts.Add(slot);result.GhostPositions.Add(slot.Id,result.Positions[slot.Id]);result.Positions.Remove(slot.Id);}
            return result;
        }
    }
}
