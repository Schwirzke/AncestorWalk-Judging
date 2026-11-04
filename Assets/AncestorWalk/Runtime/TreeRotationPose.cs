using UnityEngine;

namespace AncestorWalk
{
    public static class TreeRotationPose
    {
        public const float DegreesPerSecond=30,MaximumTilt=85;
        public static Vector2 Normalize(Vector2 angles)=>new(
            float.IsNaN(angles.x)||float.IsInfinity(angles.x)?0:Mathf.Repeat(angles.x,360),
            float.IsNaN(angles.y)||float.IsInfinity(angles.y)?0:Mathf.Clamp(angles.y,-MaximumTilt,MaximumTilt));
        public static Quaternion Orientation(Vector2 angles)=>Quaternion.Euler(angles.y,angles.x,0);
        public static Vector3 Position(Vector3 pivot,Vector2 pan,Quaternion orientation,float scale=1)
            =>new Vector3(pan.x,0,pan.y)+pivot-orientation*(pivot*scale);
        public static Bounds WorldBounds(Bounds local,Matrix4x4 matrix)
        {
            var result=new Bounds(matrix.MultiplyPoint3x4(local.min),Vector3.zero);
            for(int i=1;i<8;i++)result.Encapsulate(matrix.MultiplyPoint3x4(local.center+Vector3.Scale(local.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1))));
            return result;
        }
    }
}
