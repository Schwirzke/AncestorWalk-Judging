using TMPro;
using UnityEngine;
namespace AncestorWalk
{
    public static class WorldText
    {
        static TMP_FontAsset font;
        public static TMP_FontAsset Font=>font!=null?font:(font=Resources.Load<TMP_FontAsset>("Fonts/LatoSDF"));
        public static TextMeshPro Create(GameObject owner,string value,float characterSize,Color color)
        {
            var text=owner.AddComponent<TextMeshPro>();text.font=Font;text.fontSharedMaterial=Font.material;
            text.fontSize=64*characterSize*1.04f;text.alignment=TextAlignmentOptions.Center;
            text.textWrappingMode=TextWrappingModes.NoWrap;text.overflowMode=TextOverflowModes.Overflow;
            text.rectTransform.sizeDelta=new Vector2(20,5);text.margin=Vector4.zero;text.richText=false;
            text.extraPadding=true;text.color=color;text.text=value;text.ForceMeshUpdate();return text;
        }
    }
}
