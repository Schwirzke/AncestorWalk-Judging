using UnityEngine;
namespace AncestorWalk
{
    public static class TreeScaling
    {
        public const float Minimum=.05f,Maximum=4,OctavesPerSecond=.75f;
        public static float Clamp(float value)=>float.IsNaN(value)||float.IsInfinity(value)?1:Mathf.Clamp(value,Minimum,Maximum);
        public static float Step(float scale,float axis,float seconds)
        {
            if(float.IsNaN(axis)||float.IsInfinity(axis)||float.IsNaN(seconds)||float.IsInfinity(seconds)||seconds<=0)return Clamp(scale);
            float input=Mathf.Sign(axis)*Mathf.Clamp01((Mathf.Abs(axis)-.2f)/.8f);
            return Clamp(Clamp(scale)*Mathf.Pow(2,input*OctavesPerSecond*seconds));
        }
    }
}
