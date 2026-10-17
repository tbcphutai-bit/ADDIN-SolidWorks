using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using ADDIN.Commands.BendCollision;
using ADDIN.Commands.BendPlacement;
using ADDIN.Commands.BendTools;

namespace ADDIN.Commands.BendEnvelope
{
    internal sealed class AxialToolOccupancy
    {
        private readonly Func<bool> current;
        public CollisionMaterial2D Material { get; }
        public IReadOnlyList<AxialInterval> Segments { get; }
        public double AxialUncertaintyMm { get; }
        public IReadOnlyList<string> Reasons { get; }
        public string Provenance { get; }
        public bool SimulationOnly { get; }
        public bool Current { get { try { return current() && Material?.Current != false; } catch { return false; } } }
        public bool Verified => Current && Reasons.Count == 0 && Material?.Valid == true && Segments.Count > 0;
        internal AxialToolOccupancy(CollisionMaterial2D material, IEnumerable<AxialInterval> segments, double error, string provenance,
            bool simulation, Func<bool> current, IEnumerable<string> missing)
        {
            Material = material; Segments = PlacementLists.Copy(segments); AxialUncertaintyMm = error; Provenance = provenance;
            SimulationOnly = simulation; this.current = current ?? (() => true);
            var reasons = new List<string>(missing ?? new string[0]);
            if (Segments.Count == 0 || Segments.Count > 128) reasons.Add("TOOL_AXIAL_EXTENT_UNVERIFIED");
            if (!ToolPoint.Finite(error) || error < 0 || error > .1) reasons.Add("INVALID_TOOL_AXIAL_ERROR_BOUND");
            if (string.IsNullOrWhiteSpace(provenance)) reasons.Add("MISSING_TOOL_AXIAL_PROVENANCE");
            Reasons = PlacementLists.Reasons(reasons);
        }
        // Controlled geometry test entry, NEVER a production approval.
        internal static AxialToolOccupancy Controlled(CollisionMaterial2D material, IEnumerable<AxialInterval> intervals, double error = 0) =>
            new AxialToolOccupancy(material, intervals, error, "CONTROLLED_SYNTHETIC_AXIAL_VOLUME", true, null, null);
    }
    internal sealed class AxialCollisionPolicy
    {
        public CollisionPolicy2D CrossSection { get; }
        public int MaximumCells { get; }
        public int MaximumMilliseconds { get; }
        internal AxialCollisionPolicy(CollisionPolicy2D crossSection = null, int maximumCells = 4096, int maximumMilliseconds = 5000)
        { CrossSection = crossSection ?? new CollisionPolicy2D(); MaximumCells = maximumCells; MaximumMilliseconds = maximumMilliseconds; }
        internal bool Valid => CrossSection.Valid && MaximumCells > 0 && MaximumCells <= 32768 && MaximumMilliseconds > 0 && MaximumMilliseconds <= 60000;
    }
    internal sealed class AxialCollisionQuery
    {
        public SheetEnvelopeCapture Sheet { get; }
        public AxialToolOccupancy Tool { get; }
        public CollisionPairKind Pair { get; }
        public AxialCollisionPolicy Policy { get; }
        private readonly Func<string> invalid;
        private readonly Action consume;
        public string InvalidatedReason
        {
            get
            {
                try
                {
                    string external = invalid?.Invoke();
                    if (external != null) return external;
                    if (Sheet?.Current == false) return "STALE_MODEL";
                    if (Tool?.Current == false) return "STALE_TOOL_SOURCE_OR_EVIDENCE";
                    return null;
                }
                catch { return "STALE_INPUT_GUARD_FAILURE"; }
            }
        }
        internal AxialCollisionQuery(SheetEnvelopeCapture sheet, AxialToolOccupancy tool, CollisionPairKind pair,
            AxialCollisionPolicy policy = null, Func<string> invalid = null, Action consumeKernelQuery = null)
        { Sheet = sheet; Tool = tool; Pair = pair; Policy = policy ?? new AxialCollisionPolicy(); this.invalid = invalid; consume = consumeKernelQuery; }
        internal void ConsumeKernelQuery() { consume?.Invoke(); }
    }
    internal sealed class AxialCollisionWitness
    {
        public ToolPoint PointMm { get; }
        public double AxialMm { get; }
        public double CrossSectionInteriorMarginLowerMm { get; }
        public double AxialOccupancyMarginLowerMm { get; }
        public bool ObservedSliceOnly { get; }
        public string WorkpieceComponent { get; }
        public string ToolComponent { get; }
        internal AxialCollisionWitness(CollisionWitness2D witness, double z, double axialMargin, bool sliceOnly)
        { PointMm = witness.PointMm; AxialMm = z; CrossSectionInteriorMarginLowerMm = witness.InteriorMarginLowerMm;
            AxialOccupancyMarginLowerMm = axialMargin; ObservedSliceOnly = sliceOnly; WorkpieceComponent = witness.WorkpieceComponent; ToolComponent = witness.ObstacleComponent; }
    }
    internal sealed class AxialPairResult
    {
        private readonly AxialCollisionQuery query;
        private readonly CollisionStatus observed;
        private readonly IReadOnlyList<string> reasons;
        private readonly double? lower;
        private readonly AxialCollisionWitness witness;
        public CollisionPairKind Pair => query.Pair;
        public CollisionStatus Status => query.InvalidatedReason == null ? observed : CollisionStatus.UNKNOWN;
        public string StateId => BendStateCoverage.FinalModelObserved.ToString();
        public string PolicyVersion => "CertifiedAxialSolidPartitionV1/" + CollisionPolicy2D.Version;
        public string FrameId => query.Sheet?.Binding.FrameId;
        public string SourceSha256 => query.Sheet?.Binding.SourceSha256;
        public string ToolProvenance => query.Tool?.Provenance;
        public double? CertifiedClearanceLowerMm => Status == CollisionStatus.CLEAR ? lower : null;
        public AxialCollisionWitness Witness => Status == CollisionStatus.COLLISION ? witness : null;
        public IReadOnlyList<string> Reasons => PlacementLists.Reasons(reasons.Concat(query.InvalidatedReason == null ? new string[0] : new[] { query.InvalidatedReason }));
        public int CellsChecked { get; }
        public int KernelQueries { get; }
        public double ElapsedMilliseconds { get; }
        internal AxialPairResult(AxialCollisionQuery query, CollisionStatus status, IEnumerable<string> reasons, int cells, int calls,
            double elapsed, double? lower = null, AxialCollisionWitness witness = null)
        { this.query = query; observed = status; this.reasons = PlacementLists.Reasons(reasons); CellsChecked = cells; KernelQueries = calls; ElapsedMilliseconds = elapsed; this.lower = lower; this.witness = witness; }
    }
    internal static class AxialCollisionEngine
    {
        internal static AxialPairResult Evaluate(AxialCollisionQuery query, CancellationToken token = default(CancellationToken))
        {
            var watch = Stopwatch.StartNew(); int cells = 0, calls = 0;
            Func<CollisionStatus, IEnumerable<string>, double?, AxialCollisionWitness, AxialPairResult> result = (status, reasons, lower, witness) =>
                new AxialPairResult(query, status, reasons, cells, calls, watch.Elapsed.TotalMilliseconds, lower, witness);
            Action step = () => {
                if (token.IsCancellationRequested) throw new CollisionAbort("CANCELLED");
                if (query.InvalidatedReason != null) throw new CollisionAbort(query.InvalidatedReason);
                if (watch.ElapsedMilliseconds >= query.Policy.MaximumMilliseconds) throw new CollisionAbort("AXIAL_TIME_BUDGET_EXCEEDED");
                if (cells > query.Policy.MaximumCells) throw new CollisionAbort("AXIAL_CELL_BUDGET_EXCEEDED");
            };
            try
            {
                if (!query.Policy.Valid) return result(CollisionStatus.UNKNOWN, new[] { "INVALID_AXIAL_POLICY" }, null, null);
                step();
                var sheet = query.Sheet; var tool = query.Tool; double tolerance = query.Policy.CrossSection.ToleranceMm;
                if (sheet == null) return result(CollisionStatus.UNKNOWN, new[] { "AXIAL_COVERAGE_UNKNOWN" }, null, null);
                if (tool == null || !tool.Verified) return result(CollisionStatus.UNKNOWN,
                    new[] { "TOOL_AXIAL_EXTENT_UNVERIFIED" }.Concat(tool?.Reasons ?? new string[0]), null, null);
                if (tool.Material.FrameId != sheet.Binding.FrameId) return result(CollisionStatus.UNKNOWN, new[] { "FRAME_MISMATCH" }, null, null);
                var uncertain = new List<string>(); double clearance = double.PositiveInfinity;
                var cert = sheet.Certificate;
                // Exact observed slices can prove overlap even without global
                // coverage. They cannot prove absence between observations.
                if (!cert.ContinuousCoverageProven)
                {
                    foreach (var slice in sheet.ObservedSlices)
                    {
                        step(); var hit = CheckSlice(query, slice, null, tool, token, watch, step, ref calls);
                        if (hit != null) return result(CollisionStatus.COLLISION, new[] { "CERTIFIED_AXIAL_SLICE_INTERIOR_WITNESS" }, null, hit);
                    }
                    return result(CollisionStatus.UNKNOWN, sheet.Reasons.Concat(new[] { "AXIAL_COVERAGE_UNKNOWN" }), null, null);
                }
                foreach (var interval in cert.Intervals)
                    foreach (var segment in tool.Segments)
                    {
                        cells++; step();
                        double band = (CollisionInterval.P(cert.SolidToleranceMm) + CollisionInterval.P(tool.AxialUncertaintyMm)).Hi;
                        double axialGap = Math.Max(0, Math.Max(
                            (CollisionInterval.P(segment.StartMm) - CollisionInterval.P(interval.EndMm) - CollisionInterval.P(band)).Lo,
                            (CollisionInterval.P(interval.StartMm) - CollisionInterval.P(segment.EndMm) - CollisionInterval.P(band)).Lo));
                        if (axialGap > tolerance) { clearance = Math.Min(clearance, axialGap); continue; }
                        // Include both cap neighborhoods; an event exactly at a
                        // segment end is not silently skipped as a zero volume.
                        // A same-Z XY distance alone is NOT a 3D clearance
                        // bound: nearer material may lie just beyond a tool
                        // end. Certify a full axial neighborhood and include
                        // its outside-distance bound in the global minimum.
                        double padding=(CollisionInterval.P(band)+CollisionInterval.P(2)*CollisionInterval.P(tolerance)).Hi;
                        double lo = Math.Max(interval.StartMm, (CollisionInterval.P(segment.StartMm)-CollisionInterval.P(padding)).Lo);
                        double hi = Math.Min(interval.EndMm, (CollisionInterval.P(segment.EndMm)+CollisionInterval.P(padding)).Hi);
                        if (hi <= lo) { uncertain.Add("AXIAL_CAP_CONTACT_OR_UNCERTAINTY"); continue; }
                        if(lo>interval.StartMm||hi<interval.EndMm)
                            clearance=Math.Min(clearance,(CollisionInterval.P(padding)-CollisionInterval.P(band)).Lo);
                        var overlap = new AxialInterval(lo, hi);
                        var actual = sheet.Slice(overlap.MidMm);
                        var witness = CheckSlice(query, actual, interval, tool, token, watch, step, ref calls);
                        if (witness != null) return result(CollisionStatus.COLLISION, new[] { "CERTIFIED_AXIAL_VOLUME_INTERIOR_WITNESS" }, null, witness);
                        step(); var enclosing = sheet.Enclose(overlap);
                        if (enclosing == null) continue; // Proven void of the complete source expression.
                        query.ConsumeKernelQuery();
                        step();
                        var narrow = CollisionEngine2D.Evaluate(new CollisionQuery2D(query.Pair, enclosing, tool.Material,
                            BendStateCoverage.FinalModelObserved.ToString(), RemainingPolicy(query, watch), () => query.InvalidatedReason == null), token);
                        calls++; step();
                        if (narrow.Status == CollisionStatus.CLEAR) clearance = Math.Min(clearance, narrow.CertifiedClearanceLowerMm.Value);
                        else uncertain.AddRange(narrow.Reasons.Concat(new[] { "CONTINUOUS_ENVELOPE_NOT_SEPARATED" }));
                    }
                step();
                if (uncertain.Count > 0) return result(CollisionStatus.UNKNOWN, uncertain, null, null);
                // A completely void axial overlap is CLEAR, but no arbitrary
                // distance or infinity is published as a numerical certificate.
                return result(CollisionStatus.CLEAR, new[] { "CERTIFIED_ALL_AXIAL_INTERVALS_SEPARATED_OR_VOID" },
                    double.IsPositiveInfinity(clearance) ? (double?)null : clearance, null);
            }
            catch (CollisionAbort ex) { return result(CollisionStatus.UNKNOWN, new[] { ex.Message }, null, null); }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is System.IO.InvalidDataException || ex is ArithmeticException)
            { return result(CollisionStatus.UNKNOWN, new[] { "AXIAL_GEOMETRY_OR_NUMERICAL_FAILURE" }, null, null); }
        }
        private static CollisionPolicy2D RemainingPolicy(AxialCollisionQuery query, Stopwatch watch)
        {
            var p = query.Policy.CrossSection;
            return new CollisionPolicy2D(p.ToleranceMm, p.MaximumWork, Math.Max(1, Math.Min(p.MaximumMilliseconds,
                query.Policy.MaximumMilliseconds - (int)watch.ElapsedMilliseconds)), p.MaximumSubdivisionDepth);
        }
        private static AxialCollisionWitness CheckSlice(AxialCollisionQuery q, AxialSlice2D slice, AxialInterval provenCell,
            AxialToolOccupancy tool, CancellationToken token, Stopwatch watch, Action step, ref int calls)
        {
            if (slice.ProvenEmpty || slice.Material == null || !ToolDatumEvidence.IsSha256(slice.SourceSha256)) return null;
            double tol = q.Policy.CrossSection.ToleranceMm;
            foreach (var segment in tool.Segments)
            {
                double margin = Math.Min(slice.AxialMm - segment.StartMm, segment.EndMm - slice.AxialMm) - tool.AxialUncertaintyMm;
                margin=Math.Min(margin,slice.MaterialAxialMarginMm);
                if (provenCell != null) margin = Math.Min(margin, Math.Min(slice.AxialMm - provenCell.StartMm,
                    provenCell.EndMm - slice.AxialMm) - q.Sheet.Certificate.SolidToleranceMm);
                else margin = Math.Min(margin, tol * 2); // Only claims a slice intersection, not a 3D interior ball.
                if (margin <= tol) continue;
                step();
                q.ConsumeKernelQuery();
                step();
                var pair = CollisionEngine2D.Evaluate(new CollisionQuery2D(q.Pair, slice.Material, tool.Material,
                    BendStateCoverage.FinalModelObserved.ToString(), RemainingPolicy(q,watch), () => q.InvalidatedReason == null), token);
                calls++; step();
                if (pair.Status == CollisionStatus.COLLISION)
                    return new AxialCollisionWitness(pair.Witness, slice.AxialMm, margin, provenCell==null);
            }
            return null;
        }
    }
}
