using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
namespace AncestorWalk
{
    // Remember authored desktop layouts; change VR positions without shrinking hit targets or type.
    public sealed class OverlayComfort
    {
        struct Layout {public Vector2 Position,Size;}
        readonly Dictionary<RectTransform,Layout> defaults=new();
        public Vector2 Original(RectTransform rect)
        {
            if(!defaults.TryGetValue(rect,out var layout)){layout=new Layout{Position=rect.anchoredPosition,Size=rect.sizeDelta};defaults.Add(rect,layout);}
            return layout.Position;
        }
        public void Set(RectTransform rect,bool vr,Vector2? position=null,Vector2? size=null)
        {
            Original(rect);var original=defaults[rect];rect.anchoredPosition=vr?(position??original.Position):original.Position;
            rect.sizeDelta=vr?(size??original.Size):original.Size;
        }
        public Vector2 OriginalSize(RectTransform rect){Original(rect);return defaults[rect].Size;}
        public static void TextDensity(Canvas canvas,bool vr)
        {
            var scaler=canvas.GetComponent<CanvasScaler>();scaler.dynamicPixelsPerUnit=vr?3:1;
            // Recalculate through CanvasScaler so its cached scale stays in sync on mode changes.
            scaler.enabled=false;scaler.enabled=true;
        }
    }
}
