using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Profiling;
using UnityEngine;
namespace AncestorWalk.Editor
{
    public static class TreeMotionProfile
    {
        public static string Run(string label)
        {
            var g=UnityEngine.Object.FindAnyObjectByType<AncestorGallery>();
            if(!Application.isPlaying||g==null)throw new Exception("Play mode required");
            g.StartCoroutine(Sample(g,label));return "Sampling sideways/orbit flight: "+label;
        }
        static IEnumerator Sample(AncestorGallery g,string label)
        {
            var nav=UnityEngine.Object.FindAnyObjectByType<GalleryNavigation>();g.ShowTree();nav.ResetTreeView();
            var center=g.TreeNodePosition(g.Layout.Positions.Keys.First());
            var origin=nav.transform.position;var rotation=nav.transform.rotation;var look=nav.view.transform.localRotation;
            var times=new List<double>();var allocations=new List<long>();
            using var cpu=ProfilerRecorder.StartNew(ProfilerCategory.Internal,"Main Thread",1);
            using var gc=ProfilerRecorder.StartNew(ProfilerCategory.Memory,"GC Allocated In Frame",1);
            try
            {
                for(int i=0;i<480;i++)
                {
                    float angle=i*Mathf.PI*4/480;
                    var eye=center+new Vector3(Mathf.Sin(angle)*5,.7f,-Mathf.Cos(angle)*3.5f);
                    nav.transform.position+=eye-nav.view.transform.position;
                    nav.transform.rotation=Quaternion.LookRotation((center+Vector3.up*.3f-eye).normalized,Vector3.up);
                    yield return new WaitForEndOfFrame();
                    if(i>=60){times.Add(cpu.Valid&&cpu.LastValue>0?cpu.LastValue/1000000.0:Time.unscaledDeltaTime*1000);allocations.Add(gc.Valid?gc.LastValue:0);}
                }
                times.Sort();Directory.CreateDirectory("Artifacts/Performance");
                File.WriteAllText("Artifacts/Performance/"+label+".json", "{\"frames\":"+times.Count+",\"medianMainThreadMs\":"+times[times.Count/2].ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+",\"p95MainThreadMs\":"+times[(int)(times.Count*.95)].ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+",\"maxMainThreadMs\":"+times.Last().ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+",\"meanFrameGcBytes\":"+allocations.Average().ToString("F0",System.Globalization.CultureInfo.InvariantCulture)+",\"scope\":\"Unity Editor, includes Editor/render waits; not headset timing\"}");
            }
            finally{nav.transform.SetPositionAndRotation(origin,rotation);nav.view.transform.localRotation=look;}
        }
    }
}
