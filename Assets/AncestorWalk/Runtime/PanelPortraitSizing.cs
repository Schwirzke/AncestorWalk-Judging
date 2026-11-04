using UnityEngine;

namespace AncestorWalk
{
    // A separate padded column keeps photo growth independent of long text fitting.
    // The existing saved text-boost preference supplies the same distance curve.
    public sealed class PanelPortraitSizing : MonoBehaviour
    {
        public const float NearSize=.8f,MaximumSize=1.1f;
        public const float LabelX=.6f,LabelWidth=2.1f;
        public static readonly Vector3 Position=new Vector3(-1.15f,.4f,-.08f);
        Vector3 baseline;
        public float AppliedScale {get;private set;}=1;
        public static void Attach(Renderer surface)
        {var sizing=surface.gameObject.AddComponent<PanelPortraitSizing>();sizing.baseline=surface.transform.localScale;}
        public bool Apply(float requested)
        {
            float next=Mathf.Clamp(requested,1,MaximumSize/NearSize);
            if(Mathf.Abs(next-AppliedScale)<.005f)return false;
            AppliedScale=next;transform.localScale=baseline*next;return true;
        }
    }
}
