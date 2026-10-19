using System;
using System.Collections.Generic;
using System.Linq;
using ADDIN.Commands.BendCollision;
using ADDIN.Commands.BendEnvelope;
using ADDIN.Commands.BendPlacement;
using ADDIN.Commands.BendProcess;

namespace ADDIN.Commands.BendAssembly
{
    // Detached managed certificates, not old result wrappers with a canceled
    // job's callbacks. Restoring always attaches the NEW source/generation guard.
    internal sealed class AssemblyStaticCertificate
    {
        private sealed class Pair
        {
            internal CollisionPairKind Kind;
            internal CollisionStatus Status;
            internal string[] Reasons;
            internal double? Lower;
            internal AxialCollisionWitness Witness;
            internal Pair(AxialPairResult r) { Kind=r.Pair; Status=r.Status; Reasons=r.Reasons.ToArray(); Lower=r.CertifiedClearanceLowerMm; Witness=r.Witness; }
        }
        private sealed class Row
        { internal string Tool; internal int Feature; internal bool Simulation; internal Pair Punch, Holder; }
        private readonly Row[] rows;
        internal bool Cacheable => rows.All(r => Stable(r.Punch.Reasons) && Stable(r.Holder.Reasons));
        internal long Bytes => 1024 + rows.Sum(r => 256L + Strings(r.Punch.Reasons) + Strings(r.Holder.Reasons));
        internal AssemblyStaticCertificate(IEnumerable<VolumeCandidateCollisionResult> results)
        { rows=results.Select(r=>new Row {Tool=r.ToolId,Feature=r.FeatureId,Simulation=r.SimulationOnly,Punch=new Pair(r.PunchResult),Holder=new Pair(r.HolderResult)}).ToArray(); }
        internal IReadOnlyList<VolumeCandidateCollisionResult> Restore(CollisionBatchRequest request, IReadOnlyList<SheetEnvelopeCapture> sheets, Func<string> invalid)
        {
            var envelopes=sheets.ToDictionary(e=>e.Binding.FeatureId);
            Func<Row,Pair,AxialPairResult> restore=(r,p)=> {
                SheetEnvelopeCapture sheet; envelopes.TryGetValue(r.Feature,out sheet); var setup=request.FindSetups(r.Tool,r.Feature,p.Kind);
                var tool=setup.Count==1?setup[0].Occupancy:null;
                var q=new AxialCollisionQuery(sheet,tool,p.Kind,invalid:invalid);
                return new AxialPairResult(q,p.Status,p.Reasons,0,0,0,p.Lower,p.Witness);
            };
            return PlacementLists.Copy(rows.Select(r=>new VolumeCandidateCollisionResult(r.Tool,r.Feature,restore(r,r.Punch),restore(r,r.Holder),
                r.Simulation||request.Model.SimulationOnly||request.ToolSimulationOnly(r.Tool))));
        }
        internal static bool Stable(IEnumerable<string> reasons) => !reasons.Any(s=>s!=null && (s.Contains("BUDGET")||s.Contains("CANCEL")||s.Contains("STALE")||s.Contains("SUPERSEDED")||s.Contains("FAILURE")));
        internal static long Strings(IEnumerable<string> values) => values.Sum(s=>32L+(s?.Length??0)*2L);
    }
    internal sealed class AssemblyProcessCertificate
    {
        private sealed class Pair
        {
            internal CollisionPairKind Kind; internal string Segment; internal BendProcessPhase Phase; internal CollisionStatus Status;
            internal string[] Reasons; internal ProcessCollisionWitness Witness; internal int Contacts; internal double Tolerance;
            internal Pair(ProcessPairResult p) {Kind=p.Pair;Segment=p.SegmentId;Phase=p.Phase;Status=p.Status;Reasons=p.Reasons.ToArray();Witness=p.Witness;Contacts=p.ExpectedContactIntervals;Tolerance=p.ToleranceMm;}
        }
        private sealed class Row {internal string Tool;internal int Feature;internal bool Simulation;internal Pair[] Pairs;}
        private readonly Row[] rows;
        internal bool Cacheable => rows.All(r=>r.Pairs.All(p=>AssemblyStaticCertificate.Stable(p.Reasons)));
        internal long Bytes => 1024+rows.Sum(r=>128L+r.Pairs.Sum(p=>192L+AssemblyStaticCertificate.Strings(p.Reasons)));
        internal AssemblyProcessCertificate(IEnumerable<ProcessCandidateResult> results)
        {rows=results.Select(r=>new Row {Tool=r.ToolId,Feature=r.FeatureId,Simulation=r.SimulationOnly,Pairs=r.PairSegments.Select(p=>new Pair(p)).ToArray()}).ToArray();}
        internal IReadOnlyList<ProcessCandidateResult> Restore(ProcessBatchRequest request,Func<string> invalid)
        {
            return PlacementLists.Copy(rows.Select(r=> {
                var found=request.FindPlans(r.Tool,r.Feature);var plan=found.Count==1?found[0]:null;
                return new ProcessCandidateResult(r.Tool,r.Feature,r.Pairs.Select(p=>new ProcessPairResult(p.Kind,p.Segment,p.Phase,p.Status,p.Reasons,
                    invalid,p.Witness,0,0,0,p.Contacts,0,plan,p.Tolerance)),r.Simulation||request.Model.SimulationOnly||plan?.SimulationOnly==true);
            }));
        }
    }
}
