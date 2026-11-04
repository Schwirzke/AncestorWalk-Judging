using UnityEngine;
using TMPro;
namespace AncestorWalk
{
    public sealed class GhostSphereTarget:MonoBehaviour
    {
        public GhostSlot Slot {get;private set;}
        Renderer surface;MaterialPropertyBlock block;TMP_Text label;Camera view;AncestorGallery owner;float baseRange,gap;
        public void Build(AncestorGallery gallery,GhostSlot slot,Material material)
        {
            owner=gallery;Slot=slot;gap=TreeLayoutSettings.Load().Gap;SetRange(NavigationPreferences.InteractionGenerations);surface=GetComponent<Renderer>();surface.sharedMaterial=material;block=new MaterialPropertyBlock();view=Camera.main;
            var text=new GameObject("Ghost label");text.transform.SetParent(transform,false);label=WorldText.Create(text,"+\n"+(slot.IsRoot?"ADD FIRST PERSON":"ADD PARENT"),.015f,Color.white);SetPointed(false);
        }
        public void SetPointed(bool value){if(surface==null)return;block.SetColor("_Color",value?new Color(1,.78f,.35f,.4f):new Color(.25f,.85f,.8f,.22f));surface.SetPropertyBlock(block);}
        public void SetRange(float generations){baseRange=generations*(TreeLayout.Size+gap);}
        void LateUpdate()
        {
            if(view==null||label==null)return;var offset=transform.position-view.transform.position;if(offset.sqrMagnitude<.0001f)return;
            var direction=offset.normalized;label.transform.SetPositionAndRotation(transform.position-direction*(TreeLayout.Size*.54f*owner.TreeScale),Quaternion.LookRotation(direction,Vector3.up));
            float range=baseRange*owner.TreeScale;
            var color=label.color;float alpha=1-Mathf.SmoothStep(range,range+1,offset.magnitude);if(color.a!=alpha){color.a=alpha;label.color=color;}
        }
    }
}
