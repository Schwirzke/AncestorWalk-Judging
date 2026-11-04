using UnityEngine;
namespace AncestorWalk
{
    // Visibility intent changes immediately; visuals finish their short unscaled fade.
    public sealed class UIFade : MonoBehaviour
    {
        public const float Duration=.2f;
        CanvasGroup group;Canvas ownedCanvas;bool initialized,requested;bool deactivate;
        public bool Requested=>requested;
        void Initialize(){if(initialized)return;initialized=true;requested=gameObject.activeSelf;group=GetComponent<CanvasGroup>();if(group==null)group=gameObject.AddComponent<CanvasGroup>();group.alpha=requested?1:0;}
        public static bool Visible(GameObject target){if(target==null)return false;var f=target.GetComponent<UIFade>();return f!=null&&f.initialized?f.requested:target.activeSelf;}
        public static bool Interactive(Transform target)
        {for(var t=target;t!=null;t=t.parent){var f=t.GetComponent<UIFade>();if(f!=null&&!f.requested)return false;}return target!=null&&target.gameObject.activeInHierarchy;}
        public static void Set(GameObject target,bool visible)
        {
            if(target==null)return;var f=target.GetComponent<UIFade>();if(f==null)f=target.AddComponent<UIFade>();f.Initialize();f.deactivate=true;f.Request(visible);
        }
        public static void Set(Canvas canvas,bool visible)
        {
            if(canvas==null)return;var f=canvas.GetComponent<UIFade>();if(f==null)f=canvas.gameObject.AddComponent<UIFade>();f.Initialize();f.ownedCanvas=canvas;f.deactivate=false;f.Request(visible);
        }
        void Request(bool visible)
        {
            requested=visible;group.interactable=group.blocksRaycasts=visible;
            if(visible){gameObject.SetActive(true);if(ownedCanvas!=null)ownedCanvas.enabled=true;}
            else if(ownedCanvas!=null&&group.alpha>0)ownedCanvas.enabled=true;
        }
        void Update()
        {
            group.alpha=Mathf.MoveTowards(group.alpha,requested?1:0,Time.unscaledDeltaTime/Duration);
            if(!requested&&group.alpha==0){if(ownedCanvas!=null)ownedCanvas.enabled=false;else if(deactivate)gameObject.SetActive(false);}
        }
    }
}
