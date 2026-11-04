using UnityEngine;

namespace AncestorWalk
{
    public static class NodeNearFade
    {
        public const string PreferenceKey = "AncestorWalk.NodeNearFadeDistance";
        public const float MaximumDistance = 2f;
        public const float FadeSeconds = .2f;
        public static float ValidDistance(float value) => float.IsNaN(value) || float.IsInfinity(value)
            ? PreferenceDefaults.NodeNearFadeDistance : Mathf.Clamp(value, 0, MaximumDistance);
        public static float Distance => ValidDistance(PlayerPrefs.GetFloat(PreferenceKey, PreferenceDefaults.NodeNearFadeDistance));
        public static float Opacity(float surfaceDistance, float fadeDistance) => fadeDistance <= 0 ? 1
            : Mathf.SmoothStep(0, 1, Mathf.Clamp01(surfaceDistance / fadeDistance));
        // Transform math stays valid while renderers/colliders are hidden and avoids physics synchronization.
        public static float BoxDistance(Vector3 eye, Transform frame, Vector3 center, Vector3 size)
        {
            var local = frame.InverseTransformPoint(eye) - center;
            var half = size * .5f;
            var closest = new Vector3(Mathf.Clamp(local.x, -half.x, half.x), Mathf.Clamp(local.y, -half.y, half.y), Mathf.Clamp(local.z, -half.z, half.z));
            return Vector3.Distance(eye, frame.TransformPoint(center + closest));
        }
        public static float SphereDistance(Vector3 eye, SphereCollider sphere)
        {
            var scale = sphere.transform.lossyScale;
            float radius = sphere.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            return Mathf.Max(0, Vector3.Distance(eye, sphere.transform.TransformPoint(sphere.center)) - radius);
        }
    }
}
