using TMPro;
using UnityEngine;

namespace AncestorWalk
{
    // Glyph-only scaling: panel geometry and interaction targets never change.
    public sealed class PanelTextSizing : MonoBehaviour
    {
        public const string PreferenceKey="AncestorWalk.DistantPanelTextBoost";
        public const float MaximumBoost=2;
        const float MaximumScale=1+MaximumBoost;
        static float boost=-1;
        public static float Boost
        {
            get{if(boost<0)boost=ValidBoost(PlayerPrefs.GetFloat(PreferenceKey,PreferenceDefaults.DistantPanelTextBoost));return boost;}
        }
        static float ValidBoost(float value)=>float.IsNaN(value)||float.IsInfinity(value)?PreferenceDefaults.DistantPanelTextBoost:Mathf.Clamp(value,0,MaximumBoost);
        public static void SetBoost(float value){boost=ValidBoost(value);PlayerPrefs.SetFloat(PreferenceKey,boost);}
        public static void ReloadPreference(){boost=-1;}
        public static float DistanceScale(float metres)=>1+Boost*Mathf.SmoothStep(0,1,Mathf.InverseLerp(2,8,metres));
        TMP_Text text;Vector3 originalScale;Vector2 area;string fittedContent;float fittedFontSize=-1,limit=MaximumScale;
        public float AppliedScale {get;private set;}=1;
        public float FittedBaseline=>Mathf.Min(1,limit);
        public float GrowthLimit=>limit/FittedBaseline;
        public int FitCount {get;private set;}
        public Vector2 Area=>area;
        public Vector3 OriginalScale=>originalScale;
        public static void Attach(TMP_Text text,Vector2 area)
        {
            var sizing=text.gameObject.AddComponent<PanelTextSizing>();
            sizing.text=text;sizing.originalScale=text.transform.localScale;sizing.area=area;
        }
        public bool Prepare()
        {
            bool refit=fittedContent!=text.text||fittedFontSize!=text.fontSize;
            if(refit)
            {
                text.ForceMeshUpdate(true);var bounds=text.textBounds;
                float x=Mathf.Max(Mathf.Abs(bounds.min.x),Mathf.Abs(bounds.max.x))*Mathf.Abs(originalScale.x);
                float y=Mathf.Max(Mathf.Abs(bounds.min.y),Mathf.Abs(bounds.max.y))*Mathf.Abs(originalScale.y);
                limit=Mathf.Min(MaximumScale, x>.00001f?(area.x*.5f-.015f)/x:MaximumScale,y>.00001f?(area.y*.5f-.015f)/y:MaximumScale);
                limit=Mathf.Max(.001f,limit);fittedContent=text.text;fittedFontSize=text.fontSize;FitCount++;
            }
            return refit;
        }
        public bool Apply(float requested)
        {
            bool refit=Prepare();float next=Mathf.Min(requested,limit);
            if(!refit&&Mathf.Abs(next-AppliedScale)<.005f)return false;
            AppliedScale=next;text.transform.localScale=originalScale*next;return true;
        }
        public void ApplyShared(float growth)
        {AppliedScale=Mathf.Min(FittedBaseline*growth,limit);text.transform.localScale=originalScale*AppliedScale;}
    }
}
