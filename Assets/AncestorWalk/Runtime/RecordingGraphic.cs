using UnityEngine;
using UnityEngine.UI;

namespace AncestorWalk
{
    // Font-independent UI versions of the location editor's line-art mic/listening cue.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class RecordingGraphic : MaskableGraphic
    {
        public bool waveform;
        void Update(){if(waveform)SetVerticesDirty();}
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();var rect=rectTransform.rect;
            if(waveform)
            {
                Vector2 previous=default;float time=Time.unscaledTime;
                for(int i=0;i<33;i++)
                {
                    float u=i/32f;
                    var point=rect.center+new Vector2((u-.5f)*rect.width,Mathf.Sin(u*Mathf.PI*10-time*8)*(.014f+.012f*Mathf.Sin(time*2.3f))*Mathf.Sin(u*Mathf.PI)*(rect.height/.06f));
                    if(i>0)Stroke(mesh,previous,point,1.5f);previous=point;
                }
                return;
            }
            float scale=Mathf.Min(rect.width/.20f,rect.height/.27f);
            Vector2 At(float x,float y)=>rect.center+new Vector2(x,y)*scale;
            Vector2 last=default,first=default;
            for(int i=0;i<24;i++)
            {
                float angle=i*Mathf.PI*2/24;var point=At(Mathf.Cos(angle)*.042f,.035f+Mathf.Sin(angle)*.075f);
                if(i==0)first=point;else Stroke(mesh,last,point,1.5f);last=point;
            }
            Stroke(mesh,last,first,1.5f);
            for(int i=0;i<17;i++)
            {
                float angle=Mathf.PI+i*Mathf.PI/16;var point=At(Mathf.Cos(angle)*.079f,.025f+Mathf.Sin(angle)*.09f);
                if(i>0)Stroke(mesh,last,point,1.5f);last=point;
            }
            Stroke(mesh,At(0,-.065f),At(0,-.115f),1.5f);
            Stroke(mesh,At(-.045f,-.115f),At(.045f,-.115f),1.5f);
        }
        void Stroke(VertexHelper mesh,Vector2 from,Vector2 to,float width)
        {
            var direction=to-from;if(direction.sqrMagnitude<.000001f)return;
            var offset=new Vector2(-direction.y,direction.x).normalized*(width*.5f);int start=mesh.currentVertCount;
            mesh.AddVert(from-offset,color,Vector2.zero);mesh.AddVert(from+offset,color,Vector2.zero);
            mesh.AddVert(to+offset,color,Vector2.zero);mesh.AddVert(to-offset,color,Vector2.zero);
            mesh.AddTriangle(start,start+1,start+2);mesh.AddTriangle(start,start+2,start+3);
        }
    }
}
