using UnityEngine;
namespace AncestorWalk
{
    public sealed class TreeNodeTarget : MonoBehaviour
    {
        public string PersonId;
        public CubeFacingControls Controls;
    }
    public enum GalleryAction { EnterScene, GenerateScene, EditLocation, ToggleSiblings }
    public sealed class GalleryTarget : MonoBehaviour
    {
        public string personId;
        public bool isReturn;
        public GalleryAction action;
    }
}
