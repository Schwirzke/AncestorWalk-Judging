using System.Collections.Generic;
using UnityEngine;
using Unity.Profiling;
namespace AncestorWalk
{
    public sealed class CubePreviewManager : MonoBehaviour
    {
        static readonly ProfilerMarker CommitMarker=new("Tree.Previews.Commit");
        readonly List<CubePanoramaPreview> previews=new();int cursor;
        public int RegisteredCount=>previews.Count;
        public int LastFrameCommits {get;private set;}
        public void Register(CubePanoramaPreview preview)
        {previews.Add(preview);preview.SetResident(true);}
        void Update()
        {
            LastFrameCommits=0;
            foreach(var preview in previews)if(preview!=null)preview.Tick();
            using(CommitMarker.Auto())for(int i=0;i<previews.Count;i++)
            {cursor=(cursor+1)%previews.Count;var p=previews[cursor];if(p!=null&&p.LoadNext()){LastFrameCommits=1;break;}}
        }
    }
}
