using UnityEngine;
namespace AncestorWalk
{
    // Distances are metres, persisted locally and independent of family/cache data.
    public sealed class TreeLayoutSettings
    {
        public float Gap=PreferenceDefaults.CubeGap, Stagger=PreferenceDefaults.GenerationStagger, Rise=PreferenceDefaults.GenerationRise;
        public const string Prefix="AncestorWalk.TreeLayout.";
        static float Safe(float value,float fallback,float min,float max)=>float.IsNaN(value)||float.IsInfinity(value)?fallback:Mathf.Clamp(value,min,max);
        public TreeLayoutSettings Validated()=>new TreeLayoutSettings{Gap=Safe(Gap,PreferenceDefaults.CubeGap,.2f,4),Stagger=Safe(Stagger,PreferenceDefaults.GenerationStagger,0,2),Rise=Safe(Rise,PreferenceDefaults.GenerationRise,0,1)};
        public static TreeLayoutSettings Load()=>new TreeLayoutSettings{Gap=PlayerPrefs.GetFloat(Prefix+"Gap",PreferenceDefaults.CubeGap),Stagger=PlayerPrefs.GetFloat(Prefix+"Stagger",PreferenceDefaults.GenerationStagger),Rise=PlayerPrefs.GetFloat(Prefix+"Rise",PreferenceDefaults.GenerationRise)}.Validated();
        public void Save(){var s=Validated();PlayerPrefs.SetFloat(Prefix+"Gap",s.Gap);PlayerPrefs.SetFloat(Prefix+"Stagger",s.Stagger);PlayerPrefs.SetFloat(Prefix+"Rise",s.Rise);PlayerPrefs.Save();}
    }
}
