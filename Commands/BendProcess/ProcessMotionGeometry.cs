using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ADDIN.Commands.BendCollision;
using ADDIN.Commands.BendEnvelope;
using ADDIN.Commands.BendPlacement;
using ADDIN.Commands.BendTools;

namespace ADDIN.Commands.BendProcess
{
    // Explicit affine law in normalized process parameter u in [0,1]. Angles
    // are radians, all lengths/errors mm. Only rotation about registered bend Z
    // and translation are supported. This is NOT inferred from finished CAD.
    internal sealed class ProcessMotionLaw
    {
        public ToolPoint PivotMm { get; }
        public double StartAngle { get; }
        public double EndAngle { get; }
        public ToolPoint StartTranslationMm { get; }
        public ToolPoint EndTranslationMm { get; }
        public double StartAxialMm { get; }
        public double EndAxialMm { get; }
        public double PositionErrorMm { get; }
        public double AngleErrorRadians { get; }
        public double AxialErrorMm { get; }
        private readonly bool valid;
        private readonly Lazy<string> canonical;
        public bool Valid => valid;
        private bool ValidateInputs() => new[] { StartAngle, EndAngle, StartAxialMm, EndAxialMm, PositionErrorMm, AngleErrorRadians, AxialErrorMm }
            .All(ToolPoint.Finite) && Math.Abs(StartAngle) <= 8 * Math.PI && Math.Abs(EndAngle) <= 8 * Math.PI &&
            PositionErrorMm >= 0 && PositionErrorMm <= .01 && AngleErrorRadians >= 0 && AngleErrorRadians <= .01 &&
            AxialErrorMm >= 0 && AxialErrorMm <= .001 && new[] { PivotMm, StartTranslationMm, EndTranslationMm }
            .All(p => Math.Max(Math.Abs(p.X), Math.Abs(p.Y)) <= 100000) && Math.Max(Math.Abs(StartAxialMm), Math.Abs(EndAxialMm)) <= 100000;
        internal ProcessMotionLaw(double startAngle = 0, double endAngle = 0, ToolPoint pivot = default(ToolPoint),
            ToolPoint startTranslation = default(ToolPoint), ToolPoint endTranslation = default(ToolPoint),
            double startAxial = 0, double endAxial = 0, double positionError = 0, double angleError = 0, double axialError = 0)
        { PivotMm = pivot; StartAngle = startAngle; EndAngle = endAngle; StartTranslationMm = startTranslation; EndTranslationMm = endTranslation;
            StartAxialMm = startAxial; EndAxialMm = endAxial; PositionErrorMm = positionError; AngleErrorRadians = angleError; AxialErrorMm = axialError;
            valid=ValidateInputs();canonical=new Lazy<string>(BuildCanonical); }
        internal double Angle(double u) => StartAngle + (EndAngle - StartAngle) * u;
        internal double Axial(double u) => StartAxialMm + (EndAxialMm - StartAxialMm) * u;
        internal ToolPoint Translation(double u) => new ToolPoint(StartTranslationMm.X + (EndTranslationMm.X - StartTranslationMm.X) * u,
            StartTranslationMm.Y + (EndTranslationMm.Y - StartTranslationMm.Y) * u);
        internal PlacementTransform2D At(double u)
        {
            double c = Math.Cos(Angle(u)), s = Math.Sin(Angle(u)); var t = Translation(u);
            return new PlacementTransform2D(c, s, -s, c, PivotMm.X - c * PivotMm.X + s * PivotMm.Y + t.X,
                PivotMm.Y - s * PivotMm.X - c * PivotMm.Y + t.Y);
        }
        internal string Canonical => canonical.Value;
        private string BuildCanonical() => ToolEvidenceCanonical.Hash("AffineBendZMotionMmV1", ToolEvidenceCanonical.Point(PivotMm),
            ToolEvidenceCanonical.Point(StartTranslationMm), ToolEvidenceCanonical.Point(EndTranslationMm),
            ToolEvidenceCanonical.Hash(new[] { StartAngle, EndAngle, StartAxialMm, EndAxialMm, PositionErrorMm, AngleErrorRadians, AxialErrorMm }
                .Select(ToolEvidenceCanonical.Number).ToArray()));
        internal double Band(CollisionMaterial2D material, double lo, double hi)
        {
            // Radius bound includes EVERY analytic boundary and hole, not a
            // sampled furthest vertex. Arc center+radius is conservative.
            double radius = material.Contours.SelectMany(c => c.Boundary).Max(c => c.Kind == "ARC" ?
                ToolPoint.Distance(c.Center, PivotMm) + c.RadiusMm : Math.Max(ToolPoint.Distance(c.Start, PivotMm), ToolPoint.Distance(c.End, PivotMm)));
            var half = CollisionInterval.P(Math.Max((lo + hi) / 2 - lo, hi - (lo + hi) / 2));
            var vx = CollisionInterval.P(EndTranslationMm.X) - CollisionInterval.P(StartTranslationMm.X);
            var vy = CollisionInterval.P(EndTranslationMm.Y) - CollisionInterval.P(StartTranslationMm.Y);
            var reach = CollisionInterval.P(radius) * ((CollisionInterval.P(Math.Abs(EndAngle - StartAngle)) * half) + CollisionInterval.P(AngleErrorRadians)) +
                (vx.Square() + vy.Square()).Sqrt() * half + CollisionInterval.P(PositionErrorMm);
            // Policy guard for bounded coordinates, libm and rigid transform
            // roundoff. It is never removed from source/SPLINE uncertainty.
            return (reach + CollisionInterval.P(1e-8 + 1e-12 * (1 + radius + Math.Abs(PivotMm.X) + Math.Abs(PivotMm.Y) +
                Math.Abs(StartTranslationMm.X) + Math.Abs(StartTranslationMm.Y) + Math.Abs(EndTranslationMm.X) + Math.Abs(EndTranslationMm.Y)))).Hi;
        }
        internal CollisionMaterial2D Move(CollisionMaterial2D source, double lo, double hi, string prefix)
        {
            using(ProcessPerformanceScope.Measure("Motion.MoveMaterial"))
            {
            if (source == null) return null;
            var t = At((lo + hi) / 2); double band = Band(source, lo, hi);
            Func<CollisionContour2D, CollisionContour2D> move = c => new CollisionContour2D(prefix + ":" + c.Id,
                c.Boundary.Select(t.Apply), (CollisionInterval.P(c.UncertaintyMm) + CollisionInterval.P(band)).Hi, c.Validation);
            return new CollisionMaterial2D(source.FrameId, source.Components.Select(c => new CollisionComponent2D(move(c.Outer), c.Holes.Select(move))),
                ToolEvidenceCanonical.Hash(source.Provenance, Canonical), source.SimulationOnly, () => source.Current, true);
            }
        }
        internal bool Identity => StartAngle == 0 && EndAngle == 0 && StartTranslationMm.X == 0 && StartTranslationMm.Y == 0 &&
            EndTranslationMm.X == 0 && EndTranslationMm.Y == 0 && StartAxialMm == 0 && EndAxialMm == 0 &&
            PositionErrorMm == 0 && AngleErrorRadians == 0 && AxialErrorMm == 0;
    }

    internal enum ProcessGeometryRole { UnfoldedBlank, InProcessPanels, PostStepWorkpiece, FinalModelObserved }
    internal enum ProcessGeometryFidelity { ExactGeometricSurrogate, CertifiedEnclosureOnly, MissingBendZoneBound }

    internal sealed class ProcessSheetPart
    {
        public string Id { get; }
        public SheetEnvelopeCapture Source { get; }
        public ProcessMotionLaw Motion { get; }
        public ProcessGeometryFidelity Fidelity { get; }
        public ProcessGeometryRole Role { get; }
        public bool Current => Source.Current && ExactPrismMaterial?.Current != false;
        // Optional exact *definition*, only made by ExplicitPrism; used for
        // the narrow planar-contact proof. Native tolerances cannot be erased.
        internal CollisionMaterial2D ExactPrismMaterial { get; }
        internal AxialInterval ExactPrismDomain { get; }
        public string Canonical => ToolEvidenceCanonical.Hash("ProcessSheetPartV1", Id, Source.GeometrySha256, Motion.Canonical,
            Fidelity.ToString(), Role.ToString());
        internal ProcessSheetPart(string id, SheetEnvelopeCapture source, ProcessMotionLaw motion,
            ProcessGeometryRole role = ProcessGeometryRole.InProcessPanels, ProcessGeometryFidelity fidelity = ProcessGeometryFidelity.ExactGeometricSurrogate)
            : this(id, source, motion, role, fidelity, null, null) { }
        private ProcessSheetPart(string id, SheetEnvelopeCapture source, ProcessMotionLaw motion, ProcessGeometryRole role,
            ProcessGeometryFidelity fidelity, CollisionMaterial2D exact, AxialInterval domain)
        { Id = id; Source = source ?? throw new ArgumentNullException(nameof(source)); Motion = motion ?? throw new ArgumentNullException(nameof(motion));
            Role = role; Fidelity = fidelity; ExactPrismMaterial = exact; ExactPrismDomain = domain; }
        internal static ProcessSheetPart ExplicitPrism(string id, PlacementModelCapture model, PlacementBendSnapshot bend,
            CollisionMaterial2D material, AxialInterval domain, ProcessMotionLaw motion, ProcessGeometryFidelity fidelity = ProcessGeometryFidelity.ExactGeometricSurrogate) =>
            new ProcessSheetPart(id, AnalyticSheetEnvelope.Controlled(model, bend, new AxialPrismaticSweep(domain, material)), motion,
                ProcessGeometryRole.InProcessPanels, fidelity, material, domain);
        internal static ProcessSheetPart RegisteredPrism(string id,SheetEnvelopeCapture source,ProcessMotionLaw motion,ProcessGeometryRole role,
            ProcessGeometryFidelity fidelity,CollisionMaterial2D exact,AxialInterval domain)
        {
            if(source.Certificate.Method!="ExplicitReviewedProcessPrismV1"||exact?.Valid!=true||exact.FrameId!=source.Binding.FrameId||domain==null)
                throw new InvalidDataException("INVALID_IMPORTED_PRISM_DEFINITION");
            return new ProcessSheetPart(id,source,motion,role,fidelity,source.Certificate.SolidToleranceMm==0?exact:null,
                source.Certificate.SolidToleranceMm==0?domain:null);
        }
        internal SheetEnvelopeCapture At(double u) => Envelope(u, u);
        internal SheetEnvelopeCapture Sweep(double lo, double hi) => Envelope(lo, hi);
        private SheetEnvelopeCapture Envelope(double lo, double hi)
        {
            using(ProcessPerformanceScope.Measure("Motion.Envelope"))
            {
            if (!Source.Certificate.ContinuousCoverageProven || !Motion.Valid || Source.Domain == null)
                return Source.ObservationsOnly(new double[0]);
            return new SheetEnvelopeCapture(Source.Binding, new[] { new MotionSolid(Source, Motion, lo, hi, Id) },
                Source.Certificate.Tier, lo == hi ? "ExplicitProcessStateV1" : "WholeAffineMotionBoundaryBandV1",
                Source.SimulationOnly, Source.Certificate.SolidToleranceMm + Motion.AxialErrorMm + 1e-8, new string[0]);
            }
        }
        // Complete axial source enclosure combined with a complete angular /
        // translation displacement disk. Sweep snapshots are SUPERSETS, and
        // must NEVER be used as actual material collision witnesses.
        private sealed class MotionSolid : AxialSolidPrimitive
        {
            private readonly SheetEnvelopeCapture source;
            private readonly ProcessMotionLaw motion;
            private readonly double lo, hi, dzlo, dzhi;
            private readonly string id;
            internal MotionSolid(SheetEnvelopeCapture source, ProcessMotionLaw motion, double lo, double hi, string id)
                : base(new AxialInterval(source.Domain.StartMm + Math.Min(motion.Axial(lo), motion.Axial(hi)),
                    source.Domain.EndMm + Math.Max(motion.Axial(lo), motion.Axial(hi))))
            { this.source = source; this.motion = motion; this.lo = lo; this.hi = hi; this.id = id;
                dzlo = Math.Min(motion.Axial(lo), motion.Axial(hi)); dzhi = Math.Max(motion.Axial(lo), motion.Axial(hi)); }
            public override bool Valid => source.Certificate.ContinuousCoverageProven && motion.Valid;
            public override string RequiredFrameId => source.Binding.FrameId;
            public override string Canonical => ToolEvidenceCanonical.Hash("MotionSolidV1", source.GeometrySha256, motion.Canonical,
                ToolEvidenceCanonical.Number(lo), ToolEvidenceCanonical.Number(hi), id);
            public override IEnumerable<double> Events => source.Certificate.Intervals.SelectMany(i => new[] {
                i.StartMm + dzlo, i.StartMm + dzhi, i.EndMm + dzlo, i.EndMm + dzhi }).Distinct();
            internal override IEnumerable<CollisionComponent2D> Section(double z, string frame, string prefix)
            {
                if (lo == hi) return motion.Move(source.Slice(z - dzlo).Material, lo, hi, prefix + id)?.Components ?? new CollisionComponent2D[0];
                return Enclose(new AxialInterval(Math.Max(Domain.StartMm, z - 1e-8), Math.Min(Domain.EndMm, z + 1e-8)), frame, prefix);
            }
            internal override IEnumerable<CollisionComponent2D> Enclose(AxialInterval z, string frame, string prefix)
            {
                double a = Math.Max(source.Domain.StartMm, (CollisionInterval.P(z.StartMm) - CollisionInterval.P(dzhi)).Lo),
                    b = Math.Min(source.Domain.EndMm, (CollisionInterval.P(z.EndMm) - CollisionInterval.P(dzlo)).Hi);
                a = Math.Max(source.Domain.StartMm, a - motion.AxialErrorMm - 1e-8);
                b = Math.Min(source.Domain.EndMm, b + motion.AxialErrorMm + 1e-8);
                return b <= a ? new CollisionComponent2D[0] : motion.Move(source.Enclose(new AxialInterval(a, b)), lo, hi, prefix + id)?.Components ?? new CollisionComponent2D[0];
            }
        }
    }

    internal sealed class ProcessToolPose
    {
        private readonly Lazy<string> sourceCanonical;
        private readonly Lazy<string> assemblyCanonical;
        public AxialToolOccupancy Source { get; }
        public ProcessMotionLaw Motion { get; }
        public string SetupSourceSha256 { get; }
        internal ToolAxialSetupEvidence SetupEvidence { get; private set; }
        internal ProcessToolPose(AxialToolOccupancy source, ProcessMotionLaw motion, string setupSourceSha256)
            : this(source,motion,setupSourceSha256,null) { }
        private ProcessToolPose(AxialToolOccupancy source,ProcessMotionLaw motion,string setupSourceSha256,string hardware)
        { Source = source; Motion = motion; SetupSourceSha256 = setupSourceSha256;sourceCanonical=new Lazy<string>(BuildSourceCanonical);
            assemblyCanonical=new Lazy<string>(()=>hardware==null?SourceCanonical:ToolEvidenceCanonical.Hash("WholePartToolHardwareAndPlacedVolumeV1",hardware,
                Source?.Material==null?null:AxialSolidPrimitive.MaterialHash(Source.Material),Source==null?null:ToolEvidenceCanonical.Hash(Source.Segments.Select(s=>
                    ToolEvidenceCanonical.Hash(ToolEvidenceCanonical.Number(s.StartMm),ToolEvidenceCanonical.Number(s.EndMm))).ToArray()),
                Source==null?null:ToolEvidenceCanonical.Number(Source.AxialUncertaintyMm))); }
        internal static ProcessToolPose FromEvidence(ToolAxialSetupEvidence evidence, SheetEnvelopeCapture observed,
            ToolProfileLibrarySnapshot library, ProcessMotionLaw motion)
        {
            if (evidence == null || !evidence.AppliesTo(observed, library)) throw new ArgumentException("Mismatched process tool setup source");
            return new ProcessToolPose(evidence.Occupancy, motion, evidence.PayloadSha256,evidence.HardwareSourceSha256) { SetupEvidence = evidence };
        }
        internal static ProcessToolPose ImportedSimulation(AxialToolOccupancy source,ProcessMotionLaw motion,string payload,string geometryIdentity)
        {
            if(source?.SimulationOnly!=true||!ToolDatumEvidence.IsSha256(geometryIdentity))throw new InvalidDataException("IMPORTED_SIMULATION_TOOL_IDENTITY_REQUIRED");
            return new ProcessToolPose(source,motion,payload,geometryIdentity);
        }
        public bool Current => Source?.Current == true;
        public bool Valid => Source?.Verified == true && Motion?.Valid == true && ToolDatumEvidence.IsSha256(SetupSourceSha256);
        // Geometry/laws are immutable DTOs; only their Current leases are live.
        // Cache identity bytes, never an approval/freshness decision.
        internal string SourceCanonical => sourceCanonical.Value;
        // Per-bend signed setup payloads necessarily have different FeatureId
        // bindings. Hardware equality compares the independently bound physical
        // source AND complete placed volume instead; it never shares a proof.
        internal string AssemblyCanonical => assemblyCanonical.Value;
        private string BuildSourceCanonical() => ToolEvidenceCanonical.Hash("ProcessToolSourceV1", SetupSourceSha256,
            Source?.Material == null ? null : AxialSolidPrimitive.MaterialHash(Source.Material), Source?.Provenance,
            Source == null ? null : ToolEvidenceCanonical.Hash(Source.Segments.Select(s => ToolEvidenceCanonical.Hash(
                ToolEvidenceCanonical.Number(s.StartMm), ToolEvidenceCanonical.Number(s.EndMm))).ToArray()),
            Source == null ? null : ToolEvidenceCanonical.Number(Source.AxialUncertaintyMm));
        internal string Canonical => ToolEvidenceCanonical.Hash("ProcessToolPoseV1", SourceCanonical, Motion?.Canonical);
        internal AxialToolOccupancy At(double u) => Sweep(u, u);
        internal AxialToolOccupancy Sweep(double lo, double hi)
        {
            using(ProcessPerformanceScope.Measure("Motion.ToolPose"))
            {
            if (!Valid) return Source;
            double a = Math.Min(Motion.Axial(lo), Motion.Axial(hi)), b = Math.Max(Motion.Axial(lo), Motion.Axial(hi));
            return new AxialToolOccupancy(Motion.Move(Source.Material, lo, hi, "TOOL"), Source.Segments.Select(s =>
                new AxialInterval(s.StartMm + a, s.EndMm + b)), Source.AxialUncertaintyMm + Motion.AxialErrorMm + 1e-8,
                ToolEvidenceCanonical.Hash(Canonical, ToolEvidenceCanonical.Number(lo), ToolEvidenceCanonical.Number(hi)),
                Source.SimulationOnly, () => Current, Source.Reasons);
            }
        }
    }
}
