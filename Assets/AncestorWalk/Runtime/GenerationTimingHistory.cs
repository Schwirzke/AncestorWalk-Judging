using System;
using System.Collections.Generic;
using System.Linq;
namespace AncestorWalk
{
    [Serializable] public sealed class GenerationTimingHistory
    {
        [Serializable] public sealed class Sample {public bool edit;public double seconds;}
        public List<Sample> samples=new();
        public const double InitialSeconds=60;
        public static bool Valid(double seconds)=>!double.IsNaN(seconds)&&!double.IsInfinity(seconds)&&seconds>0&&seconds<=86400;
        public void Normalize(){samples=(samples??new()).Where(s=>s!=null&&Valid(s.seconds)).TakeLast(30).ToList();}
        public double Average(bool edit)
        {
            var matching=samples.Where(s=>s.edit==edit).ToArray();
            return matching.Length>=3?matching.Average(s=>s.seconds):samples.Count>0?samples.Average(s=>s.seconds):InitialSeconds;
        }
        public void Record(bool edit,double seconds){if(!Valid(seconds))return;samples.Add(new Sample{edit=edit,seconds=seconds});if(samples.Count>30)samples.RemoveAt(0);}
        public static int Percent(double elapsed,double expected,bool saved=false)=>saved?100:!Valid(expected)||double.IsNaN(elapsed)?0:(int)Math.Max(0,Math.Min(95,Math.Floor(elapsed/expected*100)));
    }
}
