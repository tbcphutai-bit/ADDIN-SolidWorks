using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ADDIN.Commands.BendCollision;
using ADDIN.Commands.BendEnvelope;
using ADDIN.Commands.BendPlacement;
using ADDIN.Commands.BendProcess;

namespace ADDIN.Commands.BendAssembly
{
    // A synchronous, detached display/export read, not a reusable certificate.
    // Strict held-result getters remain unchanged outside this private copy.
    // A complete graph audit brackets the copy. Cheap shared guards still run
    // at every getter; a concurrent unrelated-plan/review invalidation causes
    // the final audit to discard ALL copied rows, including earlier CLEARs.
    internal sealed class AssemblyResultReadGuard
    {
        [ThreadStatic] private static AssemblyResultReadGuard active;
        private readonly Func<string> full, shared;
        internal AssemblyResultReadGuard(Func<string> full, Func<string> shared)
        { this.full = full; this.shared = shared; }
        internal string Check() => ReferenceEquals(active, this) ? shared() : full();
        internal T Copy<T>(Func<T> copy, out double validationMs) where T : class
        {
            var clock = Stopwatch.StartNew();
            string reason = full(); validationMs = clock.Elapsed.TotalMilliseconds;
            if (reason != null) return null;
            T result;
            var previous = active; active = this;
            try { result = copy(); }
            finally { active = previous; }
            clock.Restart(); reason = full(); validationMs += clock.Elapsed.TotalMilliseconds;
            return reason == null ? result : null;
        }
    }

    internal sealed class AssemblyBatchReadSnapshot
    {
        private readonly AssemblyBatchSnapshot source;
        // This guard must be checked again before display/export. No result of
        // this point-in-time projection may certify a manufacturing decision.
        internal bool Current => source.Current;
        internal AssemblyBatchSnapshot Source => source;
        internal IReadOnlyList<AssemblyPartRead> Parts { get; }
        internal double ValidationMilliseconds { get; set; }
        internal double CopyMilliseconds { get; set; }
        internal AssemblyBatchReadSnapshot(AssemblyBatchSnapshot source)
        { this.source = source; Parts = PlacementLists.Copy(source.Parts.Select(p => new AssemblyPartRead(p))); }
    }
    internal sealed class AssemblyPartRead
    {
        internal AssemblyPartResult Source { get; }
        internal string SourceKey => Source.SourceKey;
        internal IReadOnlyList<AssemblyOccurrence> Occurrences => Source.Occurrences;
        internal int Quantity => Source.Quantity;
        internal int BendCount => Source.BendCount;
        internal bool SimulationOnly => Source.SimulationOnly;
        internal string Reason => Source.Reason;
        internal bool Current { get; }
        internal IReadOnlyList<AssemblyStaticCandidateRead> StaticCandidates { get; }
        internal IReadOnlyList<AssemblyProcessCandidateRead> ProcessCandidates { get; }
        internal AssemblyPartRead(AssemblyPartResult source)
        {
            Source = source; Current = source.Current;
            StaticCandidates = PlacementLists.Copy(source.StaticCandidates.Select(c => new AssemblyStaticCandidateRead(c)));
            ProcessCandidates = PlacementLists.Copy(source.ProcessCandidates.Select(c => new AssemblyProcessCandidateRead(c)));
        }
    }
    internal sealed class AssemblyStaticCandidateRead
    {
        internal string ToolId { get; }
        internal int FeatureId { get; }
        internal AssemblyStaticPairRead PunchResult { get; }
        internal AssemblyStaticPairRead HolderResult { get; }
        internal CollisionStatus CombinedResult => BendStateCollisionResult.Combine(PunchResult.Status, HolderResult.Status);
        internal IReadOnlyList<string> Reasons => PlacementLists.Reasons(PunchResult.Reasons.Concat(HolderResult.Reasons));
        internal AssemblyStaticCandidateRead(VolumeCandidateCollisionResult source)
        { ToolId = source.ToolId; FeatureId = source.FeatureId; PunchResult = new AssemblyStaticPairRead(source.PunchResult); HolderResult = new AssemblyStaticPairRead(source.HolderResult); }
    }
    internal sealed class AssemblyStaticPairRead
    {
        internal CollisionStatus Status { get; }
        internal IReadOnlyList<string> Reasons { get; }
        internal double? CertifiedClearanceLowerMm { get; }
        internal AxialCollisionWitness Witness { get; }
        internal AssemblyStaticPairRead(AxialPairResult source)
        { Status = source.Status; Reasons = source.Reasons; CertifiedClearanceLowerMm = source.CertifiedClearanceLowerMm; Witness = source.Witness; }
    }
    internal sealed class AssemblyProcessCandidateRead
    {
        internal string ToolId { get; }
        internal int FeatureId { get; }
        internal IReadOnlyList<AssemblyProcessPairRead> PairSegments { get; }
        internal AssemblyProcessCandidateRead(ProcessCandidateResult source)
        { ToolId = source.ToolId; FeatureId = source.FeatureId; PairSegments = PlacementLists.Copy(source.PairSegments.Select(p => new AssemblyProcessPairRead(p))); }
    }
    internal sealed class AssemblyProcessPairRead
    {
        internal CollisionPairKind Pair { get; }
        internal BendProcessPhase Phase { get; }
        internal string SegmentId { get; }
        internal CollisionStatus Status { get; }
        internal IReadOnlyList<string> Reasons { get; }
        internal ProcessCollisionWitness Witness { get; }
        internal double ToleranceMm { get; }
        internal bool ExpectedContact { get; }
        internal int ExpectedContactIntervals { get; }
        internal AssemblyProcessPairRead(ProcessPairResult source)
        {
            Pair = source.Pair; Phase = source.Phase; SegmentId = source.SegmentId; Status = source.Status;
            Reasons = source.Reasons; Witness = source.Witness; ToleranceMm = source.ToleranceMm;
            ExpectedContact = source.ExpectedContact; ExpectedContactIntervals = source.ExpectedContactIntervals;
        }
    }
}
