using UnityEngine;
using UnityEngine.UI;
namespace AncestorWalk
{
    // Two circular arrows, independent of font glyph availability on Quest.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class RegenerateIcon : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();var rect=rectTransform.rect;var center=rect.center;float radius=Mathf.Min(rect.width,rect.height)*.35f,stroke=2;
            for(int arc=0;arc<2;arc++)
            {
                float start=(35+arc*180)*Mathf.Deg2Rad,end=start+140*Mathf.Deg2Rad;
                for(int i=0;i<18;i++)
                {
                    float a=Mathf.Lerp(start,end,i/18f),b=Mathf.Lerp(start,end,(i+1)/18f);
                    var u=new Vector2(Mathf.Cos(a),Mathf.Sin(a));var v=new Vector2(Mathf.Cos(b),Mathf.Sin(b));int n=mesh.currentVertCount;
                    mesh.AddVert(center+u*(radius-stroke*.5f),color,Vector2.zero);mesh.AddVert(center+u*(radius+stroke*.5f),color,Vector2.zero);
                    mesh.AddVert(center+v*(radius+stroke*.5f),color,Vector2.zero);mesh.AddVert(center+v*(radius-stroke*.5f),color,Vector2.zero);mesh.AddTriangle(n,n+1,n+2);mesh.AddTriangle(n,n+2,n+3);
                }
                var direction=new Vector2(Mathf.Cos(end),Mathf.Sin(end));var tangent=new Vector2(-direction.y,direction.x);var tip=center+direction*radius;int head=mesh.currentVertCount;
                mesh.AddVert(tip+tangent*1.5f,color,Vector2.zero);mesh.AddVert(tip-tangent*4+direction*3,color,Vector2.zero);mesh.AddVert(tip-tangent*4-direction*3,color,Vector2.zero);mesh.AddTriangle(head,head+1,head+2);
            }
        }
    }
}
