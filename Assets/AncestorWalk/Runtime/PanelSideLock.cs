using UnityEngine;

namespace AncestorWalk
{
    public static class PanelSideLock
    {
        public const string PreferenceKey="AncestorWalk.PanelSideLockDistance";
        public const float MaximumDistance=30f,ReleaseBuffer=.25f;
        public static float ValidDistance(float value)=>float.IsNaN(value)||float.IsInfinity(value)
            ?PreferenceDefaults.PanelSideLockDistance:Mathf.Clamp(value,0,MaximumDistance);
        public static float Distance=>ValidDistance(PlayerPrefs.GetFloat(PreferenceKey,PreferenceDefaults.PanelSideLockDistance));
    }
}
