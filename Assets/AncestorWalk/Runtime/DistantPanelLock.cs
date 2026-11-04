using UnityEngine;
namespace AncestorWalk
{
    public static class DistantPanelLock
    {
        public const string PreferenceKey="AncestorWalk.DistantPanelLockDistance";
        public const float MaximumDistance=30f,ReleaseBuffer=.25f;
        public static float ValidDistance(float value)=>float.IsNaN(value)||float.IsInfinity(value)?PreferenceDefaults.DistantPanelLockDistance:Mathf.Clamp(value,0,MaximumDistance);
        public static float Distance=>ValidDistance(PlayerPrefs.GetFloat(PreferenceKey,PreferenceDefaults.DistantPanelLockDistance));
        public static bool Frozen(float distance,float limit,bool wasFrozen)=>limit>0&&distance>limit-(wasFrozen?Mathf.Min(ReleaseBuffer,limit*.25f):0);
    }
}
