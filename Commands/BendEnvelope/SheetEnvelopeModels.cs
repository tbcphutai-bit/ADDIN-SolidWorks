using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ADDIN.Commands.BendCollision;
using ADDIN.Commands.BendPlacement;
using ADDIN.Commands.BendTools;

namespace ADDIN.Commands.BendEnvelope
{
    internal enum EnvelopeCoverageTier { UnknownCoverage, Uniform2DProven, AxialSlicesWithCertifiedCoverage, VerifiedNative3D }

    internal sealed class AxialInterval
    {
        public double StartMm { get; }
        public double EndMm { get; }
        public double MidMm => StartMm / 2 + EndMm / 2;
        public double LengthMm => EndMm - StartMm;
        internal AxialInterval(double start, double end)
        {
            if (!ToolPoint.Finite(start) || !ToolPoint.Finite(end) || start >= end || Math.Max(Math.Abs(start), Math.Abs(end)) > 1000000)
                throw new InvalidDataException("INVALID_AXIAL_INTERVAL");
            StartMm = start; EndMm = end;
        }
        internal AxialInterval Intersect(AxialInterval other)
        {
            double lo = Math.Max(StartMm, other.StartMm), hi = Math.Min(EndMm, other.EndMm);
            return hi > lo ? new AxialInterval(lo, hi) : null;
        }
        internal bool Contains(double z) => z >= StartMm && z <= EndMm;
    }

    internal sealed class EnvelopeBinding
    {
        private readonly Func<bool> registrationCurrent;
        private readonly string registrationPayload;
        internal PlacementModelCapture Model { get; }
        public string ModelSnapshotId => Model.SnapshotId;
        public int UpdateStamp => Model.UpdateStamp;
        public string Configuration => Model.Configuration;
        public int FeatureId { get; }
        public string BodyId { get; }
        public BendWorkFrame WorkFrame { get; }
        public string FrameId => WorkFrame.FrameId;
        public string SourceSha256 { get; }
        public bool Current => Model.Lease.Current && (registrationCurrent == null || registrationCurrent());
        internal EnvelopeBinding(PlacementModelCapture model, PlacementBendSnapshot bend, string sourceHash)
            : this(model, bend, sourceHash, null, null, null) { }
        private EnvelopeBinding(PlacementModelCapture model, PlacementBendSnapshot bend, string sourceHash,
            BendWorkFrame registered, string registrationPayload, Func<bool> registrationCurrent)
        {
            if (model == null || bend?.Frame == null || !ToolDatumEvidence.IsSha256(sourceHash) ||
                !model.Bends.Contains(bend)) throw new InvalidDataException("INVALID_ENVELOPE_SOURCE_BINDING");
            Model = model; FeatureId = bend.FeatureId; BodyId = bend.BodyId; WorkFrame = registered ?? bend.Frame; SourceSha256 = sourceHash;
            this.registrationCurrent = registrationCurrent; this.registrationPayload = registrationPayload;
        }
        internal static EnvelopeBinding Registered(PlacementModelCapture model, PlacementBendSnapshot bend, string source,
            BendWorkFrame frame, ToolReviewValidation review, Func<bool> current)
        {
            if (frame == null || review?.RecordVerified != true || current == null) throw new InvalidDataException("UNVERIFIED_PROCESS_FRAME_REGISTRATION");
            return new EnvelopeBinding(model, bend, source, frame, review.PayloadSha256,
                () => current() && review.RecordVerified);
        }
        internal EnvelopeBinding WithAdditionalFreshness(Func<bool> current)
        {
            if(current==null)throw new ArgumentNullException(nameof(current));
            return new EnvelopeBinding(Model,Model.Bends.Single(b=>b.FeatureId==FeatureId),SourceSha256,WorkFrame,registrationPayload,()=>Current&&current());
        }
        private string NativeCanonical => ToolEvidenceCanonical.Hash("SheetEnvelopeBindingV1", ModelSnapshotId,
            UpdateStamp.ToString(System.Globalization.CultureInfo.InvariantCulture), Configuration, BodyId,
            FeatureId.ToString(System.Globalization.CultureInfo.InvariantCulture), FrameId, SourceSha256,
            Vector(WorkFrame.OriginMm), Vector(WorkFrame.X), Vector(WorkFrame.Y), Vector(WorkFrame.BendAxis));
        internal string Canonical => registrationPayload == null ? NativeCanonical :
            ToolEvidenceCanonical.Hash("ReviewedRegisteredEnvelopeBindingV1", NativeCanonical, registrationPayload);
        internal static string Vector(PlacementVector3 p) => ToolEvidenceCanonical.Hash(ToolEvidenceCanonical.Number(p.X),
            ToolEvidenceCanonical.Number(p.Y), ToolEvidenceCanonical.Number(p.Z));
    }

    internal sealed class AxialSlice2D
    {
        public double AxialMm { get; }
        public CollisionMaterial2D Material { get; }
        public bool ProvenEmpty { get; }
        public string SourceSha256 { get; }
        public string EvidenceMethod { get; }
        public double MaterialAxialMarginMm { get; }
        internal AxialSlice2D(double axial, CollisionMaterial2D material, bool empty, string source, string method,double materialAxialMargin)
        { AxialMm = axial; Material = material; ProvenEmpty = empty; SourceSha256 = source; EvidenceMethod = method; MaterialAxialMarginMm=materialAxialMargin; }
    }

    internal sealed class EnvelopeCoverageCertificate
    {
        private readonly EnvelopeBinding binding;
        public EnvelopeCoverageTier Tier { get; }
        public string Method { get; }
        public string SourceSha256 => binding.SourceSha256;
        public string FrameId => binding.FrameId;
        public double SolidToleranceMm { get; }
        public IReadOnlyList<AxialInterval> Intervals { get; }
        public bool Current => binding.Current;
        public bool ContinuousCoverageProven => Current && Tier != EnvelopeCoverageTier.UnknownCoverage;
        internal EnvelopeCoverageCertificate(EnvelopeBinding binding, EnvelopeCoverageTier tier, string method,
            IEnumerable<AxialInterval> intervals, double tolerance)
        { this.binding = binding; Tier = tier; Method = method; Intervals = PlacementLists.Copy(intervals); SolidToleranceMm = tolerance; }
    }

    internal sealed class SheetEnvelopeCapture
    {
        private readonly IReadOnlyList<AxialSolidPrimitive> primitives;
        public EnvelopeBinding Binding { get; }
        public EnvelopeCoverageCertificate Certificate { get; }
        public IReadOnlyList<AxialSlice2D> ObservedSlices { get; }
        public IReadOnlyList<string> Reasons { get; }
        public string GeometrySha256 { get; }
        // Same analytic solids in a registered frame, without a bend-specific
        // capture ID. Used only for exact process endpoint continuity.
        internal string ShapeSha256 { get; }
        public bool SimulationOnly { get; }
        public bool Current => Binding.Current;
        public AxialInterval Domain { get; }
        internal SheetEnvelopeCapture WithAdditionalFreshness(Func<bool> current) => new SheetEnvelopeCapture(Binding.WithAdditionalFreshness(current),
            primitives,Certificate.Tier,Certificate.Method,SimulationOnly,Certificate.SolidToleranceMm,Reasons,ObservedSlices);
        internal SheetEnvelopeCapture(EnvelopeBinding binding, IEnumerable<AxialSolidPrimitive> solids,
            EnvelopeCoverageTier tier, string method, bool simulation, double tolerance, IEnumerable<string> reasons,
            IEnumerable<AxialSlice2D> observations = null)
        {
            Binding = binding; primitives = PlacementLists.Copy(solids); SimulationOnly = simulation;
            Reasons = PlacementLists.Reasons(reasons ?? new string[0]);
            var events = primitives.SelectMany(p => p.Events).Distinct().OrderBy(x => x).ToArray();
            var intervals = new List<AxialInterval>();
            for (int i = 1; i < events.Length; i++) intervals.Add(new AxialInterval(events[i - 1], events[i]));
            if (events.Length > 1) Domain = new AxialInterval(events.First(), events.Last());
            if (primitives.Count == 0 || primitives.Any(p => !p.Valid) || !ToolPoint.Finite(tolerance) || tolerance < 0 || tolerance > .01 || intervals.Count > 4096)
                tier = EnvelopeCoverageTier.UnknownCoverage;
            Certificate = new EnvelopeCoverageCertificate(binding, tier, method, intervals, tolerance);
            GeometrySha256 = ToolEvidenceCanonical.Hash("SheetEnvelopeV1", binding.Canonical, tier.ToString(), method,
                ToolEvidenceCanonical.Number(tolerance), ToolEvidenceCanonical.Hash(primitives.Select(p => p.Canonical).ToArray()));
            ShapeSha256 = ToolEvidenceCanonical.Hash("SheetEnvelopeShapeV1", binding.FrameId, tier.ToString(), method,
                ToolEvidenceCanonical.Number(tolerance), ToolEvidenceCanonical.Hash(primitives.Select(p => p.Canonical).ToArray()));
            ObservedSlices = PlacementLists.Copy(observations ?? (tier == EnvelopeCoverageTier.UnknownCoverage ? new AxialSlice2D[0] :
                intervals.Select(i => Slice(i.MidMm)).ToArray()));
        }
        internal AxialSlice2D Slice(double axial)
        {
            var pieces = primitives.Select((p, i) => new { p, i }).Where(x => x.p.Domain.Contains(axial))
                .SelectMany(x => x.p.Section(axial, Binding.FrameId, "P" + x.i)).ToArray();
            var stations=Certificate.Intervals.SelectMany(i=>new[]{i.StartMm,i.EndMm}).ToArray();
            double margin=stations.Length==0?0:stations.Min(z=>Math.Abs(z-axial))-Certificate.SolidToleranceMm;
            return new AxialSlice2D(axial, Material(pieces), pieces.Length == 0, Binding.SourceSha256, Certificate.Method,margin);
        }
        internal CollisionMaterial2D Enclose(AxialInterval interval)
        {
            var pieces = primitives.Select((p, i) => new { p, i }).Where(x => x.p.Domain.Intersect(interval) != null)
                .SelectMany(x => x.p.Enclose(x.p.Domain.Intersect(interval), Binding.FrameId, "P" + x.i)).ToArray();
            return Material(pieces);
        }
        private CollisionMaterial2D Material(IEnumerable<CollisionComponent2D> components)
        {
            var array = components.ToArray();
            return array.Length == 0 ? null : new CollisionMaterial2D(Binding.FrameId, array,
                Binding.SourceSha256, SimulationOnly, () => Current, true);
        }
        // Observations retain their exact source provenance, but deliberately
        // lose the source interval proof. Finite slices cannot restore it.
        internal SheetEnvelopeCapture ObservationsOnly(IEnumerable<double> axial) => new SheetEnvelopeCapture(Binding,
            new AxialSolidPrimitive[0], EnvelopeCoverageTier.UnknownCoverage, "ObservedSlicesOnly", SimulationOnly,
            Certificate.SolidToleranceMm, new[] { "AXIAL_COVERAGE_UNKNOWN", "FINITE_SLICES_DO_NOT_PROVE_CONTINUOUS_COVERAGE" }, axial.Select(Slice));
    }
}
