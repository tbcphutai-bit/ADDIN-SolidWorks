using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using ADDIN.Commands.BendCollision;

namespace ADDIN.Commands.BendProcess
{
    // Optional managed, thread-local diagnostics. Times are inclusive and must
    // not be summed across nested stages. No proof, policy or cache key uses it.
    internal sealed class ProcessPerformanceSample
    {
        public string ToolId { get; }
        public int FeatureId { get; }
        public BendProcessPhase Phase { get; }
        public CollisionPairKind Pair { get; }
        public IReadOnlyDictionary<string, ProcessPerformanceMetric> Metrics { get; }
        internal ProcessPerformanceSample(string tool, int feature, BendProcessPhase phase, CollisionPairKind pair,
            IDictionary<string, ProcessPerformanceMetric> metrics)
        { ToolId=tool; FeatureId=feature; Phase=phase; Pair=pair;
            Metrics=new ReadOnlyDictionary<string, ProcessPerformanceMetric>(new Dictionary<string, ProcessPerformanceMetric>(metrics)); }
    }
    internal sealed class ProcessPerformanceMetric
    {
        public long Count { get; }
        public double Milliseconds { get; }
        internal ProcessPerformanceMetric(long count, double ms) { Count=count; Milliseconds=ms; }
    }
    internal sealed class ProcessPerformanceScope : IDisposable
    {
        [ThreadStatic] private static ProcessPerformanceScope active;
        private readonly ProcessPerformanceScope previous;
        private readonly Action<ProcessPerformanceSample> sink;
        private readonly string tool;
        private readonly int feature;
        private readonly BendProcessPhase phase;
        private readonly CollisionPairKind pair;
        private readonly long start;
        private sealed class Counter { internal long Count,Ticks; }
        private readonly Dictionary<string, Counter> metrics=new Dictionary<string, Counter>();
        internal ProcessPerformanceScope(Action<ProcessPerformanceSample> sink, string tool, int feature, BendProcessPhase phase, CollisionPairKind pair)
        { previous=active; this.sink=sink; this.tool=tool; this.feature=feature; this.phase=phase; this.pair=pair;
            start=sink==null?0:Stopwatch.GetTimestamp();active=sink==null?null:this; }
        internal static Measurement Measure(string stage) => new Measurement(active,stage);
        internal static void Count(string stage) { active?.Record(stage,0); }
        private void Record(string stage,long ticks)
        { Counter value;if(!metrics.TryGetValue(stage,out value))metrics[stage]=value=new Counter();value.Count++;value.Ticks+=ticks; }
        internal struct Measurement : IDisposable
        {
            private readonly ProcessPerformanceScope owner;
            private readonly string stage;
            private readonly long start;
            internal Measurement(ProcessPerformanceScope owner,string stage)
            {this.owner=owner;this.stage=stage;start=owner==null?0:Stopwatch.GetTimestamp();}
            public void Dispose(){owner?.Record(stage,Stopwatch.GetTimestamp()-start);}
        }
        public void Dispose()
        {
            active=previous;
            if(sink!=null)Record("Engine.Total",Stopwatch.GetTimestamp()-start);
            // An observer cannot change a geometry result or strand a worker.
            try
            {
                if(sink!=null)
                {
                    var copy=new Dictionary<string,ProcessPerformanceMetric>();
                    foreach(var entry in metrics)copy.Add(entry.Key,new ProcessPerformanceMetric(entry.Value.Count,entry.Value.Ticks*1000.0/Stopwatch.Frequency));
                    sink(new ProcessPerformanceSample(tool,feature,phase,pair,copy));
                }
            }catch{}
        }
    }
}
