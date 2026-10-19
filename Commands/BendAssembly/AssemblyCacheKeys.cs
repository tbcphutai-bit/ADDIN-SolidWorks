using System;
using System.Linq;
using System.Runtime.CompilerServices;
using ADDIN.Commands.BendCollision;
using ADDIN.Commands.BendEnvelope;
using ADDIN.Commands.BendPlacement;
using ADDIN.Commands.BendProcess;
using ADDIN.Commands.BendTools;

namespace ADDIN.Commands.BendAssembly
{
    internal static class AssemblyCacheKeys
    {
        internal const string GeometryPolicy = "NativeAnalyticShellV2/CompleteOrthogonalPlaneCylinderSectionFamilyV1/mm/SourceBoundLeaseV1";
        private sealed class Token { internal readonly string Id = Guid.NewGuid().ToString("N"); }
        private static readonly ConditionalWeakTable<object, Token> tokens = new ConditionalWeakTable<object, Token>();
        // Prevent a bound envelope or signed setup being transplanted onto an
        // unrelated capture which happens to carry the same strings/stamp.
        internal static string TokenOf(object value) => value == null ? "MISSING" : tokens.GetValue(value, _ => new Token()).Id;
        internal static string Geometry(AssemblySourceInput s) => ToolEvidenceCanonical.Hash(GeometryPolicy, s.SourceKey, TokenOf(s.Model),
            s.Model?.SnapshotId, s.Model?.Configuration, s.Model?.UpdateStamp.ToString(System.Globalization.CultureInfo.InvariantCulture),
            s.Model == null ? null : ToolEvidenceCanonical.Hash(s.Model.Bodies.Select(b => ToolEvidenceCanonical.Hash(b.Id, b.EnvelopeTopology?.SourceSha256,
                ToolEvidenceCanonical.Hash(b.Edges.Select(e => ToolEvidenceCanonical.Hash(e.Id.ToString(), e.Kind.ToString(), EnvelopeBinding.Vector(e.A),
                    EnvelopeBinding.Vector(e.B), e.Arc == null ? null : ToolEvidenceCanonical.Hash(EnvelopeBinding.Vector(e.Arc.CenterMm),
                        EnvelopeBinding.Vector(e.Arc.MidMm), ToolEvidenceCanonical.Number(e.Arc.RadiusMm), e.Arc.FullCircle.ToString()))).ToArray()))).ToArray()),
            s.Model == null ? null : ToolEvidenceCanonical.Hash(s.Model.Bends.Select(b => ToolEvidenceCanonical.Hash(b.FeatureId.ToString(), b.BodyId,
                b.Frame?.FrameId, b.Frame == null ? null : EnvelopeBinding.Vector(b.Frame.OriginMm), b.Frame == null ? null : EnvelopeBinding.Vector(b.Frame.X),
                b.Frame == null ? null : EnvelopeBinding.Vector(b.Frame.Y), b.Frame == null ? null : EnvelopeBinding.Vector(b.Frame.BendAxis),
                ToolEvidenceCanonical.Number(b.BendMinMm), ToolEvidenceCanonical.Number(b.BendMaxMm))).ToArray()),
            // ObservationsOnly deliberately has no volume certificate; two
            // different observed slices can have the same volume hash. Respect
            // the original exact envelope-object binding used by setup proofs.
            ToolEvidenceCanonical.Hash(s.Envelopes.Select(e => ToolEvidenceCanonical.Hash(TokenOf(e),e.GeometrySha256,
                ToolEvidenceCanonical.Hash(e.ObservedSlices.Select(v=>ToolEvidenceCanonical.Hash(ToolEvidenceCanonical.Number(v.AxialMm),v.SourceSha256,
                    v.Material==null?null:AxialSolidPrimitive.MaterialHash(v.Material),ToolEvidenceCanonical.Number(v.MaterialAxialMarginMm))).ToArray()))).ToArray()));
        internal static string Catalog(ToolProfileLibrarySnapshot library) => ToolEvidenceCanonical.Hash("AssemblyCatalogV1", TokenOf(library),
            library?.Fingerprint, library?.ConfigurationSha256, library?.PlacementSources?.Fingerprint);
        internal static string Program(AssemblySourceInput s) => ToolEvidenceCanonical.Hash("AssemblyWholeProgramV1", s.ConditionsRevision,
            ToolEvidenceCanonical.Hash(s.Setups.Select(e => ToolEvidenceCanonical.Hash(e.ToolId, e.FeatureId.ToString(), e.Pair.ToString(), e.PayloadSha256)).OrderBy(x => x).ToArray()),
            ToolEvidenceCanonical.Hash(s.Plans.Select(p => p.PayloadSha256).OrderBy(x => x).ToArray()));
        // Kept separate from the externally reviewed program payload: equal
        // coordinates are not equal authority or immutable object bindings.
        internal static string EvidenceBindings(AssemblySourceInput s) => ToolEvidenceCanonical.Hash("AssemblyEvidenceBindingsV1",
            ToolEvidenceCanonical.Hash(s.Setups.Select(e=>ToolEvidenceCanonical.Hash(TokenOf(e),e.PayloadSha256,e.Review.Status.ToString(),
                e.Occupancy.Verified.ToString(),e.Occupancy.SimulationOnly.ToString())).OrderBy(x=>x,StringComparer.Ordinal).ToArray()),
            ToolEvidenceCanonical.Hash(s.Plans.Select(p=>ToolEvidenceCanonical.Hash(TokenOf(p),p.PayloadSha256,p.SimulationOnly.ToString(),
                ToolEvidenceCanonical.Hash(p.InputReasons.ToArray()))).OrderBy(x=>x,StringComparer.Ordinal).ToArray()));
        internal static string Policy(AssemblyBatchOptions options)
        {
            var a = options.StaticPolicy; var c = a.CrossSection; var p = options.ProcessPolicy;
            return ToolEvidenceCanonical.Hash(CollisionPolicy2D.Version, ProcessCollisionPolicy.Version,
                ToolEvidenceCanonical.Hash(new[] { c.ToleranceMm, c.MaximumWork, c.MaximumMilliseconds, c.MaximumSubdivisionDepth,
                    a.MaximumCells, a.MaximumMilliseconds, p.MaximumIntervals, p.MaximumDepth, p.MaximumMilliseconds,
                    p.Axial.CrossSection.ToleranceMm, p.Axial.CrossSection.MaximumWork, p.Axial.CrossSection.MaximumMilliseconds,
                    p.Axial.CrossSection.MaximumSubdivisionDepth, p.Axial.MaximumCells, p.Axial.MaximumMilliseconds }.Select(ToolEvidenceCanonical.Number).ToArray()));
        }
        internal static bool CatalogCurrent(ToolProfileLibrarySnapshot lib) => lib?.Current == true && lib.PlacementSources?.Current != false &&
            (lib.PlacementSources == null || lib.PlacementSources.Tools.All(t => t.MasterDatums?.Status != DatumEvidenceStatus.Stale && t.MappingReview?.Status != ToolReviewStatus.Stale &&
                t.SeatingEvidence?.Status != ToolReviewStatus.Stale && t.HolderObstacles?.Current != false));
    }
    internal sealed class AssemblyGeometryBundle
    {
        public AssemblySourceInput Source { get; }
        public System.Collections.Generic.IReadOnlyList<SheetEnvelopeCapture> Envelopes { get; }
        public bool Current => Source.Current && Envelopes.All(e => e.Current);
        // Conservative accounting weight, not the CLR's exact retained heap.
        // Count limits are hard bounds; observed GC heap is reported separately.
        internal long Bytes => 8192 + (Source.Model?.Bodies.Sum(b =>
            512L+b.Edges.Sum(e=>256L+e.Faces.Count*8L)+b.Faces.Sum(f=>256L+f.Edges.Count*8L)+
            (b.EnvelopeTopology==null?0:b.EnvelopeTopology.Edges.Count*256L+b.EnvelopeTopology.Faces.Sum(f=>256L+f.Loops.Sum(l=>96L+l.Edges.Count*48L))))??0) +
            Envelopes.Sum(e=>1024L+e.ObservedSlices.Sum(s=>(s.Material?.Contours.Sum(c=>c.Boundary.Count)??0)*256L)) +
            Source.Setups.Count*1024L+Source.Plans.Sum(p=>2048L+p.Segments.Sum(s=>1024L+s.Parts.Count*2048L));
        internal AssemblyGeometryBundle(AssemblySourceInput source, System.Collections.Generic.IEnumerable<SheetEnvelopeCapture> envelopes)
        { Source = source; Envelopes = PlacementLists.Copy(envelopes); }
    }
}
