using System.Collections.Generic;
using UnityEngine;
namespace AncestorWalk
{
    // The location keyboard is world geometry; give it the same fade as canvas panels.
    public sealed class WorldUIFade : MonoBehaviour
    {
        Renderer[] renderers;TextMesh[] labels;CanvasGroup group;Material transparent;float alpha;bool closing;
        sealed class Tint {public Color Color;}
        readonly Dictionary<Renderer,Tint> colors=new();readonly Dictionary<TextMesh,Color> textColors=new();
        MaterialPropertyBlock block;
        void Awake()
        {
            renderers=GetComponentsInChildren<Renderer>(true);labels=GetComponentsInChildren<TextMesh>(true);group=gameObject.AddComponent<CanvasGroup>();group.alpha=0;
            transparent=new Material(Resources.Load<Shader>("Shaders/CubeControlFade"));block=new MaterialPropertyBlock();
            foreach(var text in labels){textColors[text]=text.color;text.GetComponent<Renderer>().sortingOrder=102;}
            foreach(var r in renderers)if(r.GetComponent<TextMesh>()==null){r.sortingOrder=r.name=="Location editor backdrop"?100:r is LineRenderer?103:101;var mat=r.sharedMaterial;colors[r]=new Tint{Color=mat!=null&&mat.HasProperty("_Color")?mat.color:Color.white};r.sharedMaterial=transparent;}
            Apply();
        }
        public void Close()
        {if(closing)return;closing=true;foreach(var c in GetComponentsInChildren<Collider>())c.enabled=false;foreach(var input in GetComponentsInChildren<TMPro.TMP_InputField>())input.DeactivateInputField();group.interactable=group.blocksRaycasts=false;}
        void Update(){alpha=Mathf.MoveTowards(alpha,closing?0:1,Time.unscaledDeltaTime/UIFade.Duration);Apply();if(closing&&alpha==0)Destroy(gameObject);}
        void Apply()
        {
            group.alpha=alpha;
            foreach(var entry in colors){var r=entry.Key;if(r==null)continue;var color=entry.Value.Color;if(r.sharedMaterial!=transparent){if(r.sharedMaterial.HasProperty("_Color"))color=r.sharedMaterial.color;entry.Value.Color=color;r.sharedMaterial=transparent;}color.a*=alpha;block.Clear();block.SetColor("_Color",color);r.SetPropertyBlock(block);}
            foreach(var entry in textColors)if(entry.Key!=null){var color=entry.Value;color.a*=alpha;entry.Key.color=color;}
        }
        void OnDestroy(){if(transparent!=null)Destroy(transparent);}
    }
}
