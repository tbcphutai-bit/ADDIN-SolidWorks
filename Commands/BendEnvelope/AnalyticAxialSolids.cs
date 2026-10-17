using System;
using System.Collections.Generic;
using System.Linq;
using ADDIN.Commands.BendCollision;
using ADDIN.Commands.BendPlacement;
using ADDIN.Commands.BendTools;

namespace ADDIN.Commands.BendEnvelope
{
    // These are volume definitions, not sampled sections. The controlled
    // factory ALWAYS marks them simulation-only. Production definitions come
    // only from the separate complete native B-rep proof compiler.
    internal abstract class AxialSolidPrimitive
    {
        public AxialInterval Domain { get; }
        public abstract bool Valid { get; }
        public abstract string Canonical { get; }
        public virtual string RequiredFrameId => null;
        public virtual IEnumerable<double> Events => new[] { Domain.StartMm, Domain.EndMm };
        protected AxialSolidPrimitive(AxialInterval domain) { Domain = domain; }
        internal abstract IEnumerable<CollisionComponent2D> Section(double axial, string frame, string prefix);
        internal abstract IEnumerable<CollisionComponent2D> Enclose(AxialInterval axial, string frame, string prefix);
        internal static string MaterialHash(CollisionMaterial2D material) => ToolEvidenceCanonical.Hash(material.FrameId,
            ToolEvidenceCanonical.Hash(material.Components.Select(c => ToolEvidenceCanonical.Hash(ContourHash(c.Outer),
                ToolEvidenceCanonical.Hash(c.Holes.Select(ContourHash).ToArray()))).ToArray()));
        private static string ContourHash(CollisionContour2D c) => ToolEvidenceCanonical.Hash(c.Id, ToolEvidenceCanonical.Number(c.UncertaintyMm),
            ToolEvidenceCanonical.Hash(c.Boundary.Select(e => ToolEvidenceCanonical.Hash(e.Kind, ToolEvidenceCanonical.Point(e.Start),
                ToolEvidenceCanonical.Point(e.End), ToolEvidenceCanonical.Point(e.Center), ToolEvidenceCanonical.Number(e.RadiusMm),
                ToolEvidenceCanonical.Number(e.SweepRadians))).ToArray()));
        internal static IReadOnlyList<CollisionComponent2D> Move(CollisionMaterial2D material, string prefix, double x, double y, double band)
        {
            var transform = new PlacementTransform2D(txMm: x, tyMm: y);
            Func<CollisionContour2D, CollisionContour2D> copy = c => new CollisionContour2D(prefix + ":" + c.Id,
                c.Boundary.Select(transform.Apply), (CollisionInterval.P(c.UncertaintyMm) + CollisionInterval.P(band)).Hi, c.Validation);
            return PlacementLists.Copy(material.Components.Select(c => new CollisionComponent2D(copy(c.Outer), c.Holes.Select(copy))));
        }
    }

    internal sealed class AxialPrismaticSweep : AxialSolidPrimitive
    {
        private readonly CollisionMaterial2D material;
        private readonly ToolPoint velocity;
        private readonly double reference, tolerance;
        public override string RequiredFrameId => material?.FrameId;
        public override bool Valid => material?.Valid == true && ToolPoint.Finite(velocity.X) && ToolPoint.Finite(velocity.Y) &&
            ToolPoint.Finite(reference) && ToolPoint.Finite(tolerance) && tolerance >= 0 && tolerance <= .01;
        public override string Canonical => ToolEvidenceCanonical.Hash("ExactLinearTranslationSweepV1", MaterialHash(material),
            ToolEvidenceCanonical.Point(velocity), ToolEvidenceCanonical.Number(reference), ToolEvidenceCanonical.Number(tolerance),
            ToolEvidenceCanonical.Number(Domain.StartMm), ToolEvidenceCanonical.Number(Domain.EndMm));
        internal AxialPrismaticSweep(AxialInterval domain, CollisionMaterial2D material, ToolPoint velocity = default(ToolPoint),
            double referenceAxial = 0, double sourceTolerance = 0) : base(domain)
        { this.material = material; this.velocity = velocity; reference = referenceAxial; tolerance = sourceTolerance; }
        private double EvaluationBand(double axial)
        {
            var delta=CollisionInterval.P(axial)-CollisionInterval.P(reference);
            var tx=CollisionInterval.P(velocity.X)*delta;var ty=CollisionInterval.P(velocity.Y)*delta;
            double x=velocity.X*(axial-reference),y=velocity.Y*(axial-reference);
            double error=Math.Max(Math.Abs(tx.Lo-x),Math.Abs(tx.Hi-x))+Math.Max(Math.Abs(ty.Lo-y),Math.Abs(ty.Hi-y));
            double magnitude=material.Contours.SelectMany(c=>c.Boundary).Max(c=>new[]{c.Start,c.End,c.Center}.Max(p=>Math.Max(Math.Abs(p.X),Math.Abs(p.Y)))+c.RadiusMm);
            return (CollisionInterval.P(tolerance)+CollisionInterval.P(error)+CollisionInterval.P(64*2.2204460492503131e-16*(1+magnitude+Math.Abs(x)+Math.Abs(y))+1e-9)).Hi;
        }
        internal override IEnumerable<CollisionComponent2D> Section(double axial, string frame, string prefix) =>
            Move(material, prefix, velocity.X * (axial - reference), velocity.Y * (axial - reference), EvaluationBand(axial));
        internal override IEnumerable<CollisionComponent2D> Enclose(AxialInterval axial, string frame, string prefix)
        {
            // Every translated boundary, INCLUDING hole boundaries, lies in
            // this complete interval displacement disk. A sampled maximum is
            // never used. Keeping the bands also shrinks certain interiors.
            var speed = (CollisionInterval.P(velocity.X).Square() + CollisionInterval.P(velocity.Y).Square()).Sqrt();
            double half=Math.Max((CollisionInterval.P(axial.MidMm)-CollisionInterval.P(axial.StartMm)).Hi,
                (CollisionInterval.P(axial.EndMm)-CollisionInterval.P(axial.MidMm)).Hi);
            var reach = speed * CollisionInterval.P(half);
            double band = (reach + CollisionInterval.P(EvaluationBand(axial.MidMm))).Hi;
            return Move(material, prefix, velocity.X * (axial.MidMm - reference), velocity.Y * (axial.MidMm - reference), band);
        }
    }

    // Exact box minus a through cylinder parallel to work X (and perpendicular
    // to bend Z). At any Z, sqrt(r^2-(z-z0)^2) gives the removed Y stripe.
    // This is a controlled analytic solid definition, not an inferred hole.
    internal sealed class CrossAxisBoreBox : AxialSolidPrimitive
    {
        private readonly double x0, x1, y0, y1, cy, cz, radius;
        public override bool Valid => new[] { x0, x1, y0, y1, cy, cz, radius }.All(ToolPoint.Finite) &&
            x1 > x0 && y1 > y0 && radius > 0 && radius < 10000;
        public override string Canonical => ToolEvidenceCanonical.Hash("BoxMinusThroughXCylinderV1",
            ToolEvidenceCanonical.Hash(new[] { x0, x1, y0, y1, cy, cz, radius, Domain.StartMm, Domain.EndMm }.Select(ToolEvidenceCanonical.Number).ToArray()));
        public override IEnumerable<double> Events => base.Events.Concat(new[] { cz - radius, cz, cz + radius }.Where(Domain.Contains)).Distinct();
        internal CrossAxisBoreBox(AxialInterval domain, double x0, double x1, double y0, double y1, double centerY, double centerZ, double radius) : base(domain)
        { this.x0 = x0; this.x1 = x1; this.y0 = y0; this.y1 = y1; cy = centerY; cz = centerZ; this.radius = radius; }
        internal override IEnumerable<CollisionComponent2D> Section(double axial, string frame, string prefix)
        {
            var delta = CollisionInterval.P(axial) - CollisionInterval.P(cz);
            var square = CollisionInterval.P(radius).Square() - delta.Square();
            // Roundoff is retained in the boundary band of this exact slice.
            var half = square.Hi <= 0 ? CollisionInterval.P(0) : square.Sqrt();
            return Stripe(half.Lo, prefix, half.Hi - half.Lo + 1e-9);
        }
        internal override IEnumerable<CollisionComponent2D> Enclose(AxialInterval axial, string frame, string prefix)
        {
            var delta = new CollisionInterval(axial.StartMm, axial.EndMm) - CollisionInterval.P(cz);
            var square = CollisionInterval.P(radius).Square() - delta.Square();
            double minimumHalf = square.Lo <= 0 ? 0 : square.Sqrt().Lo;
            // Subtract the smallest void over the ENTIRE slab. The resulting
            // material is an enclosing set for every true cross-section.
            return Stripe(minimumHalf, prefix, 1e-9);
        }
        private IEnumerable<CollisionComponent2D> Stripe(double half, string prefix, double error)
        {
            if (half <= 1e-10) return new[] { Rectangle(prefix + ":BOX", x0, x1, y0, y1, error) };
            var result = new List<CollisionComponent2D>();
            if (cy - half > y0) result.Add(Rectangle(prefix + ":LOWER", x0, x1, y0, Math.Min(y1, cy - half), error));
            if (cy + half < y1) result.Add(Rectangle(prefix + ":UPPER", x0, x1, Math.Max(y0, cy + half), y1, error));
            return result;
        }
        internal static CollisionComponent2D Rectangle(string id, double x0, double x1, double y0, double y1, double error)
        {
            var points = new[] { new ToolPoint(x0, y0), new ToolPoint(x1, y0), new ToolPoint(x1, y1), new ToolPoint(x0, y1) };
            return new CollisionComponent2D(new CollisionContour2D(id, points.Select((p, i) =>
                ToolCurve.Line(p, points[(i + 1) % 4], new ToolTransform())), error));
        }
    }

    internal static class AnalyticSheetEnvelope
    {
        internal static SheetEnvelopeCapture Controlled(PlacementModelCapture model, PlacementBendSnapshot bend, params AxialSolidPrimitive[] solids)
        {
            var hash = ToolEvidenceCanonical.Hash("CONTROLLED_SYNTHETIC_VOLUME_NOT_COMPANY_DATA", ToolEvidenceCanonical.Hash(solids.Select(p => p.Canonical).ToArray()));
            var binding = new EnvelopeBinding(model, bend, hash);
            if(solids.Any(s=>s.RequiredFrameId!=null&&s.RequiredFrameId!=binding.FrameId))
                return new SheetEnvelopeCapture(binding,solids,EnvelopeCoverageTier.UnknownCoverage,"RejectedSourceFrame",true,1e-9,new[]{"FRAME_MISMATCH","AXIAL_COVERAGE_UNKNOWN"});
            return new SheetEnvelopeCapture(binding, solids, EnvelopeCoverageTier.AxialSlicesWithCertifiedCoverage,
                "CompleteAnalyticSolidPartitionV1", true, 1e-9, new[] { "SIMULATION_ONLY", "FINAL_MODEL_ONLY" });
        }
    }
}
