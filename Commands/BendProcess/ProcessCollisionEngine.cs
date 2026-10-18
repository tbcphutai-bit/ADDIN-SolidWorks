using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using ADDIN.Commands.BendCollision;
using ADDIN.Commands.BendEnvelope;
using ADDIN.Commands.BendPlacement;

namespace ADDIN.Commands.BendProcess
{
    internal sealed class ProcessCollisionPolicy
    {
        public const string Version = "CertifiedAffineBendZMotionBandsMmV1";
        public int MaximumIntervals { get; }
        public int MaximumDepth { get; }
        public int MaximumMilliseconds { get; }
        public AxialCollisionPolicy Axial { get; }
        internal ProcessCollisionPolicy(int intervals = 1024, int depth = 18, int milliseconds = 10000, AxialCollisionPolicy axial = null)
        { MaximumIntervals = intervals; MaximumDepth = depth; MaximumMilliseconds = milliseconds; Axial = axial ?? new AxialCollisionPolicy(); }
        internal bool Valid => MaximumIntervals > 0 && MaximumIntervals <= 32768 && MaximumDepth >= 0 && MaximumDepth <= 32 &&
            MaximumMilliseconds > 0 && MaximumMilliseconds <= 60000 && Axial.Valid;
    }
    internal sealed class ProcessCollisionWitness
    {
        public double Parameter { get; }
        public string PartId { get; }
        public AxialCollisionWitness Geometry { get; }
        public string ProcessPayloadSha256 { get; }
        internal ProcessCollisionWitness(double u, string part, AxialCollisionWitness geometry, string payload)
        { Parameter = u; PartId = part; Geometry = geometry; ProcessPayloadSha256 = payload; }
    }
    internal sealed class ProcessPairResult
    {
        private readonly Func<string> invalid;
        private readonly CollisionStatus observed;
        private readonly IReadOnlyList<string> reasons;
        private readonly ProcessCollisionWitness witness;
        public CollisionPairKind Pair { get; }
        public string SegmentId { get; }
        public BendProcessPhase Phase { get; }
        public string FrameId { get; }
        public string ModelSnapshotId { get; }
        public string ProcessPayloadSha256 { get; }
        public bool SimulationOnly { get; }
        public bool Current => invalid() == null;
        public string PolicyVersion => ProcessCollisionPolicy.Version;
        public double ToleranceMm { get; }
        public CollisionStatus Status => invalid() == null ? observed : CollisionStatus.UNKNOWN;
        public ProcessCollisionWitness Witness => Status == CollisionStatus.COLLISION ? witness : null;
        public bool ExpectedContact => Status == CollisionStatus.CLEAR && ExpectedContactIntervals > 0;
        public int ExpectedContactIntervals { get; }
        public int IntervalsChecked { get; }
        public int Refinements { get; }
        public int KernelQueries { get; }
        public double ElapsedMilliseconds { get; }
        public IReadOnlyList<string> Reasons => PlacementLists.Reasons(reasons.Concat(invalid() == null ? new string[0] : new[] { invalid() }));
        internal ProcessPairResult(CollisionPairKind pair, string segment, BendProcessPhase phase, CollisionStatus status,
            IEnumerable<string> reasons, Func<string> invalid, ProcessCollisionWitness witness, int intervals, int refinements, int calls, int contacts, double elapsed,
            BendProcessPlan plan = null, double toleranceMm = 1e-5)
        { Pair = pair; SegmentId = segment; Phase = phase; observed = status; this.reasons = PlacementLists.Reasons(reasons); this.invalid = invalid;
            this.witness = witness; IntervalsChecked = intervals; Refinements = refinements; KernelQueries = calls; ExpectedContactIntervals = contacts; ElapsedMilliseconds = elapsed;
            FrameId = plan?.Binding.FrameId; ModelSnapshotId = plan?.Binding.ModelSnapshotId; ProcessPayloadSha256 = plan?.PayloadSha256; SimulationOnly = plan?.SimulationOnly == true; ToleranceMm = toleranceMm; }
    }
    internal static class ProcessCollisionEngine
    {
        private sealed class Cell
        { internal double Lo, Hi; internal int Depth; internal Cell(double lo, double hi, int depth) { Lo = lo; Hi = hi; Depth = depth; } }
        internal static ProcessPairResult Evaluate(BendProcessPlan plan, BendMotionSegment segment, CollisionPairKind pair,
            ProcessCollisionPolicy policy = null, CancellationToken token = default(CancellationToken), Func<string> externalInvalid = null, Action consume = null)
        {
            if (segment == null) throw new ArgumentNullException(nameof(segment));
            policy = policy ?? new ProcessCollisionPolicy(); var watch = Stopwatch.StartNew();
            int intervals = 0, refinements = 0, calls = 0, contacts = 0;
            // The time clock is used only while executing. Held results expire
            // by source/job leases, not by elapsed time after completion.
            Func<string> invalid = () => {
                try { return externalInvalid?.Invoke() ?? plan?.InvalidatedReason; } catch { return "PROCESS_FRESHNESS_GUARD_FAILED"; }
            };
            Func<CollisionStatus, IEnumerable<string>, ProcessCollisionWitness, ProcessPairResult> result = (status, reasons, hit) =>
                new ProcessPairResult(pair, segment.Id, segment.Phase, status, reasons, invalid, hit, intervals, refinements, calls, contacts, watch.Elapsed.TotalMilliseconds, plan, policy.Axial.CrossSection.ToleranceMm);
            Action step = () => {
                if (token.IsCancellationRequested) throw new CollisionAbort("CANCELLED");
                string reason = invalid(); if (reason != null) throw new CollisionAbort(reason);
                if (watch.ElapsedMilliseconds >= policy.MaximumMilliseconds) throw new CollisionAbort("PROCESS_TIME_BUDGET_EXCEEDED");
            };
            try
            {
                if (!policy.Valid) return result(CollisionStatus.UNKNOWN, new[] { "INVALID_PROCESS_POLICY" }, null);
                step();
                if (plan == null || !plan.Segments.Contains(segment)) return result(CollisionStatus.UNKNOWN, new[] { "UNKNOWN_PROCESS_GEOMETRY", "UNKNOWN_MACHINE_MOTION" }, null);
                if (plan.InputReasons.Count > 0) return result(CollisionStatus.UNKNOWN, plan.InputReasons, null);
                var tool = segment.Tool(pair);
                if (tool?.Valid != true) return result(CollisionStatus.UNKNOWN, new[] { "UNVERIFIED_PROCESS_TOOL_POSE_OR_FINITE_EXTENT" }, null);
                if (tool.Source.Material.FrameId != plan.Binding.FrameId) return result(CollisionStatus.UNKNOWN, new[] { "PROCESS_TOOL_FRAME_MISMATCH" }, null);
                int segmentIndex = plan.Segments.ToList().IndexOf(segment);
                if (segmentIndex > 0)
                {
                    var previous = plan.Segments[segmentIndex - 1].Tool(pair);
                    if (previous?.Valid != true || previous.SourceCanonical != tool.SourceCanonical || !BendProcessPlan.Joins(previous.Motion, tool.Motion))
                        return result(CollisionStatus.UNKNOWN, new[] { "UNKNOWN_MACHINE_MOTION_OR_TOOL_STATE_TRANSITION" }, null);
                }
                Func<SheetEnvelopeCapture, AxialToolOccupancy, AxialPairResult> query = (sheet, obstacle) => {
                    step();
                    int remaining = Math.Max(1, policy.MaximumMilliseconds - (int)watch.ElapsedMilliseconds);
                    var ap = new AxialCollisionPolicy(policy.Axial.CrossSection, policy.Axial.MaximumCells, Math.Min(remaining, policy.Axial.MaximumMilliseconds));
                    var r = AxialCollisionEngine.Evaluate(new AxialCollisionQuery(sheet, obstacle, pair, ap, invalid, () => { step(); consume?.Invoke(); calls++; }), token);
                    step(); return r;
                };
                var observed = new HashSet<double>();
                var constantActual = new Dictionary<string, AxialPairResult>();
                var constantSweep = new Dictionary<string, AxialPairResult>();
                Func<double, ProcessCollisionWitness> witnessAt = u => {
                    if (!observed.Add(u)) return null;
                    foreach (var p in segment.Parts.Where(p => p.Fidelity == ProcessGeometryFidelity.ExactGeometricSurrogate && p.Motion.AxialErrorMm == 0))
                    {
                        AxialPairResult r;
                        bool fixedPair = Constant(p.Motion) && Constant(tool.Motion);
                        if (!fixedPair || !constantActual.TryGetValue(p.Id, out r))
                        { r = query(p.At(u), tool.At(u)); if (fixedPair) constantActual[p.Id] = r; }
                        // No intersecting superset can enter this witness path.
                        if (r.Status == CollisionStatus.COLLISION) return new ProcessCollisionWitness(u, p.Id, r.Witness, plan.PayloadSha256);
                    }
                    return null;
                };
                foreach (double u in new[] { 0.0, 1.0 })
                { var hit = witnessAt(u); if (hit != null) return result(CollisionStatus.COLLISION, new[] { "WITNESSED_UNWANTED_PROCESS_INTERFERENCE" }, hit); }
                var queue = new Queue<Cell>(); queue.Enqueue(new Cell(0, 1, 0)); var unknown = new List<string>();
                while (queue.Count > 0)
                {
                    step();
                    if (++intervals > policy.MaximumIntervals) return result(CollisionStatus.UNKNOWN, new[] { "PROCESS_INTERVAL_BUDGET_EXCEEDED" }, null);
                    var cell = queue.Dequeue(); double mid = cell.Lo / 2 + cell.Hi / 2;
                    var hit = witnessAt(mid);
                    if (hit != null) return result(CollisionStatus.COLLISION, new[] { "WITNESSED_UNWANTED_PROCESS_INTERFERENCE" }, hit);
                    bool separated = true; bool contact = false; bool variableUnresolved = false; var cellReasons = new List<string>();
                    foreach (var p in segment.Parts)
                    {
                        if (segment.Contacts.Any(c => c.Proves(p, tool, segment.Phase, pair))) { contact = true; continue; }
                        AxialPairResult enclosure;
                        bool fixedPair = Constant(p.Motion) && Constant(tool.Motion);
                        if (!fixedPair || !constantSweep.TryGetValue(p.Id, out enclosure))
                        { enclosure = query(p.Sweep(cell.Lo, cell.Hi), tool.Sweep(cell.Lo, cell.Hi)); if (fixedPair) constantSweep[p.Id] = enclosure; }
                        if (enclosure.Status != CollisionStatus.CLEAR)
                        { separated = false; variableUnresolved |= !fixedPair; cellReasons.AddRange(enclosure.Reasons); }
                    }
                    if (separated) { if (contact) contacts++; continue; }
                    if (!variableUnresolved || cell.Depth >= policy.MaximumDepth)
                    { unknown.AddRange(cellReasons); unknown.Add("UNCOVERED_PROCESS_INTERVAL_OR_UNRESOLVED_CONTACT"); continue; }
                    refinements++; queue.Enqueue(new Cell(cell.Lo, mid, cell.Depth + 1)); queue.Enqueue(new Cell(mid, cell.Hi, cell.Depth + 1));
                }
                step();
                return unknown.Count > 0 ? result(CollisionStatus.UNKNOWN, unknown, null) : result(CollisionStatus.CLEAR,
                    contacts > 0 ? new[] { "CERTIFIED_WHOLE_SEGMENT_OUTSIDE_APPROVED_CONTACT", "EXPECTED_CONTACT" } :
                    new[] { "CERTIFIED_WHOLE_SEGMENT_SEPARATED" }, null);
            }
            catch (CollisionAbort ex) { return result(CollisionStatus.UNKNOWN, new[] { ex.Message }, null); }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is System.IO.InvalidDataException || ex is ArithmeticException)
            { return result(CollisionStatus.UNKNOWN, new[] { "PROCESS_GEOMETRY_OR_NUMERICAL_FAILURE" }, null); }
        }
        private static bool Constant(ProcessMotionLaw m) => m.StartAngle == m.EndAngle && m.StartAxialMm == m.EndAxialMm &&
            m.StartTranslationMm.X == m.EndTranslationMm.X && m.StartTranslationMm.Y == m.EndTranslationMm.Y;
    }
}
