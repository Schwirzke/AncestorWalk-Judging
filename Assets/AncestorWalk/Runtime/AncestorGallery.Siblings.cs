using System.Linq;
using UnityEngine;

namespace AncestorWalk
{
    public sealed partial class AncestorGallery
    {
        string siblingTree;SiblingVisibility siblingGraph;SiblingVisibility.State siblingState;FamilyTree visibleTree;
        void PrepareSiblingView()
        {
            if(siblingTree!=TreeId||siblingState==null){siblingTree=TreeId;siblingState=SiblingVisibility.Load(TreeId);}
            siblingGraph=new SiblingVisibility(Tree);siblingState.anchors.RemoveAll(id=>id==null||!Tree.People.ContainsKey(id)||siblingGraph.Siblings(id).Count==0);
            visibleTree=siblingGraph.Project(siblingState);
        }
        public int SiblingCount(string id)=>siblingGraph?.Siblings(id).Count??0;
        public bool SiblingsCollapsed(string id)=>siblingState?.anchors.Contains(id)??false;
        public bool HasCollapsedSiblings=>(siblingState?.anchors.Count??0)>0;
        public void ToggleSiblings(string id)
        {
            if(!IsTree||!Layout.Positions.ContainsKey(id)||SiblingCount(id)==0||(FindAnyObjectByType<GalleryNavigation>()?.IsDraggingYear??false))return;
            if(!siblingState.anchors.Remove(id))siblingState.anchors.Add(id);SaveSiblingView();
        }
        public void ExpandAllSiblings()
        {if(!IsTree||!HasCollapsedSiblings||(FindAnyObjectByType<GalleryNavigation>()?.IsDraggingYear??false))return;siblingState.anchors.Clear();SaveSiblingView();}
        void SaveSiblingView()
        {
            PlayerPrefs.SetString(SiblingVisibility.PreferenceKey(TreeId),JsonUtility.ToJson(siblingState));PlayerPrefs.Save();
            FindAnyObjectByType<GalleryNavigation>()?.ClearTreePointing();ShowTree();
        }
        void SiblingButton(Person person,Vector3 center)
        {
            int count=SiblingCount(person.Id);if(count==0)return;
            var button=Box("Sibling branches",center+new Vector3(0,1.075f,-.055f),new Vector3(3.4f,.26f,.04f),teal);
            var block=new MaterialPropertyBlock();var color=new Color(.08f,.28f,.30f);block.SetColor("_BaseColor",color);block.SetColor("_Color",color);button.GetComponent<Renderer>().SetPropertyBlock(block);
            var target=button.AddComponent<GalleryTarget>();target.personId=person.Id;target.action=GalleryAction.ToggleSiblings;
            var label=Label((SiblingsCollapsed(person.Id)?"+ Show":"− Hide")+" siblings ("+count+")",center+new Vector3(0,1.075f,-.095f),.045f,paper);label.name="Sibling branches label";
            PanelTextSizing.Attach(label,new Vector2(3.25f,.22f));
        }
    }
}
