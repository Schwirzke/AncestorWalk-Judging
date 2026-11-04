using UnityEngine;
namespace AncestorWalk
{
    // Shared by full-scene and preview loaders: one expensive texture stage per frame.
    public static class PanoramaFrameBudget
    {
        static int frame=-1;
        public static bool TryTake(){if(frame==Time.frameCount)return false;frame=Time.frameCount;return true;}
    }
}
