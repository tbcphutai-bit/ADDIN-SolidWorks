using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using ADDIN.Commands.BendCollision;
using ADDIN.Commands.BendPlacement;
using ADDIN.Commands.BendTools;

namespace ADDIN.Commands.BendEnvelope
{
    internal sealed class ToolAxialSegmentRecord
    {
        public double? LocalStartMm { get; set; }
        public double? LocalEndMm { get; set; }
    }
    internal sealed class ToolAxialSetupRecord
    {
        public string ToolId { get; set; }
        public int FeatureId { get; set; }
        public string Pair { get; set; }
        public string ModelSnapshotId { get; set; }
        public int ModelUpdateStamp { get; set; }
        public string Configuration { get; set; }
        public string EnvelopeGeometrySha256 { get; set; }
        public string FrameId { get; set; }
        public string MasterSourceSha256 { get; set; }
        public string ProfileGeometrySha256 { get; set; }
        public string SeatingPayloadSha256 { get; set; }
        public string AxialSectionContract { get; set; }
        public double? ToolAxialOriginMm { get; set; }
        public int? ToolAxisSign { get; set; }
        public double? AxialUncertaintyMm { get; set; }
        public List<ToolAxialSegmentRecord> Segments { get; set; }
        public PairPointRecord ContactPointMm { get; set; }
        public PairPointRecord ApproachDirection { get; set; }
        public bool MirrorApproved { get; set; }
        public ToolReviewProof Review { get; set; }
    }
    internal sealed class ToolAxialSetupEvidence
    {
        private readonly SheetEnvelopeCapture envelope;
        private readonly ToolProfileLibrarySnapshot library;
        public string ToolId { get; }
        public int FeatureId { get; }
        public CollisionPairKind Pair { get; }
        public ToolReviewValidation Review { get; }
        public AxialToolOccupancy Occupancy { get; }
        public string PayloadSha256 { get; }
        internal bool AppliesTo(SheetEnvelopeCapture sheet, ToolProfileLibrarySnapshot source) =>
            ReferenceEquals(envelope, sheet) && ReferenceEquals(library, source) && FeatureId == sheet.Binding.FeatureId;
        private ToolAxialSetupEvidence(string id, int feature, CollisionPairKind pair, string payload, ToolReviewValidation review,
            SheetEnvelopeCapture envelope, ToolProfileLibrarySnapshot library, AxialToolOccupancy occupancy)
        { ToolId = id; FeatureId = feature; Pair = pair; PayloadSha256 = payload; Review = review; this.envelope = envelope; this.library = library; Occupancy = occupancy; }
        private static ToolPoint Point(PairPointRecord p) => p == null || !p.X.HasValue || !p.Y.HasValue ?
            throw new InvalidDataException("MISSING_AXIAL_SETUP_CONTACT_OR_APPROACH") : new ToolPoint(p.X.Value, p.Y.Value);
        internal static string Payload(ToolAxialSetupRecord r)
        {
            if (!r.ToolAxialOriginMm.HasValue || !r.ToolAxisSign.HasValue || !r.AxialUncertaintyMm.HasValue ||
                r.Segments == null || r.Segments.Any(s => s == null || !s.LocalStartMm.HasValue || !s.LocalEndMm.HasValue))
                throw new InvalidDataException("MISSING_TOOL_AXIAL_POSITION_LENGTH_OR_ERROR");
            return ToolEvidenceCanonical.Hash("ToolAxialSetupV1", r.ToolId, r.FeatureId.ToString(System.Globalization.CultureInfo.InvariantCulture), r.Pair,
                r.ModelSnapshotId, r.ModelUpdateStamp.ToString(System.Globalization.CultureInfo.InvariantCulture), r.Configuration, r.EnvelopeGeometrySha256,
                r.FrameId, r.MasterSourceSha256, r.ProfileGeometrySha256, r.SeatingPayloadSha256, r.AxialSectionContract,
                ToolEvidenceCanonical.Number(r.ToolAxialOriginMm.Value), r.ToolAxisSign.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ToolEvidenceCanonical.Number(r.AxialUncertaintyMm.Value), ToolEvidenceCanonical.Hash(r.Segments.Select(s => ToolEvidenceCanonical.Hash(
                    ToolEvidenceCanonical.Number(s.LocalStartMm.Value), ToolEvidenceCanonical.Number(s.LocalEndMm.Value))).ToArray()),
                ToolEvidenceCanonical.Point(Point(r.ContactPointMm)), ToolEvidenceCanonical.Point(Point(r.ApproachDirection)), r.MirrorApproved.ToString());
        }
        internal static ToolAxialSetupEvidence Read(ToolAxialSetupRecord record, SheetEnvelopeCapture envelope, ToolProfileLibrarySnapshot library,
            ToolReviewVerifier verifier, string evidenceRoot, CancellationToken token)
        {
            if (record == null || envelope == null || library == null) throw new ArgumentNullException("Axial setup source");
            // Freeze nested mutable records before hashing, verifying and
            // deriving coordinates. Caller edits cannot change signed input.
            var serializer=new JavaScriptSerializer {MaxJsonLength=1024*1024};
            record=serializer.Deserialize<ToolAxialSetupRecord>(serializer.Serialize(record));
            var reasons = new List<string>(); string payload = null; ToolAssemblyProfile assembly = null;
            CollisionMaterial2D material = null; var intervals = new List<AxialInterval>(); double error = 0;
            var review = ToolReviewValidation.Missing("MISSING_TOOL_AXIAL_SETUP_REVIEW"); CollisionPairKind pair;
            if (!Enum.TryParse(record.Pair, out pair) || !Enum.IsDefined(typeof(CollisionPairKind), pair))
                throw new InvalidDataException("INVALID_AXIAL_PAIR_KIND");
            try
            {
                token.ThrowIfCancellationRequested(); payload = Payload(record);
                assembly = library.PlacementSources.Tools.SingleOrDefault(t => t.Id == record.ToolId);
                if (assembly?.IdentityMatched != true || assembly.MasterDatums.Status != DatumEvidenceStatus.VerifiedRecord ||
                    !assembly.SeatingEvidence.CoordinateRecordReady) throw new InvalidDataException("UNVERIFIED_TOOL_DATUM_OR_SEATING");
                var binding = envelope.Binding;
                if (record.ModelSnapshotId != binding.ModelSnapshotId || record.ModelUpdateStamp != binding.UpdateStamp || record.Configuration != binding.Configuration ||
                    record.FeatureId != binding.FeatureId || record.EnvelopeGeometrySha256 != envelope.GeometrySha256 || record.FrameId != binding.FrameId ||
                    record.MasterSourceSha256 != assembly.MasterSha256 || record.SeatingPayloadSha256 != assembly.SeatingEvidence.PayloadSha256)
                    throw new InvalidDataException("STALE_OR_MISMATCHED_AXIAL_SETUP");
                if (record.AxialSectionContract != "UniformProfileOverListedSegments" || Math.Abs(record.ToolAxisSign.Value) != 1)
                    throw new InvalidDataException("UNSUPPORTED_TOOL_AXIAL_ALIGNMENT_OR_SECTION");
                string profile = pair == CollisionPairKind.WorkpiecePunch ? assembly.Punch?.GeometrySha256 : assembly.HolderObstacles?.DefinitionSha256;
                if (record.ProfileGeometrySha256 != profile) throw new InvalidDataException("MISMATCHED_AXIAL_PROFILE_SOURCE");
                review = verifier.Verify(pair == CollisionPairKind.WorkpiecePunch ? "PunchAxialSetup" : "HolderAxialSetup", payload, record.Review, evidenceRoot, token);
                if (!review.RecordVerified) throw new InvalidDataException("TOOL_AXIAL_EXTENT_UNVERIFIED");
                if (!review.SimulationOnly && (!assembly.PhysicalPairVerified || envelope.SimulationOnly))
                    throw new InvalidDataException("UNVERIFIED_PHYSICAL_PAIR_OR_SIMULATION_ENVELOPE");
                error = record.AxialUncertaintyMm.Value;
                if (!ToolPoint.Finite(error) || error < 0 || error > .1 || record.Segments.Count == 0 || record.Segments.Count > 128)
                    throw new InvalidDataException("INVALID_TOOL_AXIAL_SEGMENTS_OR_ERROR");
                foreach (var segment in record.Segments)
                {
                    var local = new AxialInterval(segment.LocalStartMm.Value, segment.LocalEndMm.Value);
                    double a = record.ToolAxialOriginMm.Value + record.ToolAxisSign.Value * local.StartMm;
                    double b = record.ToolAxialOriginMm.Value + record.ToolAxisSign.Value * local.EndMm;
                    intervals.Add(new AxialInterval(Math.Min(a, b), Math.Max(a, b)));
                }
                error = (CollisionInterval.P(error) + CollisionInterval.P(1e-9 + 1e-12 * (Math.Abs(record.ToolAxialOriginMm.Value) + intervals.Max(i => Math.Abs(i.StartMm) + Math.Abs(i.EndMm))))).Hi;
                var contact = Point(record.ContactPointMm); var approach = Point(record.ApproachDirection); PlacementTransform2D.Unit(approach);
                var punch = PlacementTransform2D.Align(assembly.SeatingEvidence.TipPointMm.Value, assembly.SeatingEvidence.WorkingDirection.Value, contact, approach, record.MirrorApproved);
                if (pair == CollisionPairKind.WorkpiecePunch)
                {
                    if (assembly.Punch?.Supported != true || !assembly.Punch.ObservedClosed || !assembly.Punch.Region.IsValidRegion)
                        throw new InvalidDataException("OPEN_UNSUPPORTED_OR_INVALID_PUNCH_REGION");
                    var geometry = new ToolGeometryPlacement2D(assembly.Punch, punch, binding.FrameId);
                    var contour = new CollisionContour2D("PUNCH", geometry.PlacedCurves,
                        PlacementCollisionEvaluator.PlacementUncertainty(assembly.Punch.Curves, punch), assembly.Punch.Region);
                    material = new CollisionMaterial2D(binding.FrameId, new[] { new CollisionComponent2D(contour) }, profile, review.SimulationOnly, () => binding.Current, true);
                }
                else
                {
                    if (!assembly.MappingReview.RecordVerified || assembly.HolderObstacles?.GeometrySupportedForCollision != true)
                        throw new InvalidDataException("UNREVIEWED_HOLDER_MAPPING_OR_MATERIAL");
                    var placed = new HolderObstaclePlacement2D(assembly.HolderObstacles, punch.Then(assembly.SeatingEvidence.HolderSourceToPunchSource), binding.FrameId);
                    if (!placed.HasConservativeGeometry || placed.RegionCombination != "UnionOfEachOuterMinusItsOwnHoles" ||
                        placed.EnvelopeContract != "NominalMaterialUnionPlusEveryBoundaryUncertaintyBandV1") throw new InvalidDataException("INVALID_HOLDER_ENVELOPE_CONTRACT");
                    var contours = placed.Contours.ToDictionary(c => c.Id, c => new CollisionContour2D(c.Id, c.NominalBoundary, c.BoundaryBandRadiusMm,
                        assembly.HolderObstacles.Contours.Single(s => s.Id == c.Id).Validation));
                    material = new CollisionMaterial2D(binding.FrameId, placed.MaterialRegions.Select(r => new CollisionComponent2D(contours[r.OuterId], r.HoleIds.Select(id => contours[id]))),
                        profile, review.SimulationOnly, () => binding.Current, true);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (ex is InvalidDataException || ex is ArgumentException || ex is InvalidOperationException)
            { reasons.Add(ex is InvalidDataException ? ex.Message : "INVALID_AXIAL_SETUP"); }
            var approved = review; var sourceAssembly = assembly;
            Func<bool> current = () => envelope.Current && library.Current && library.PlacementSources.Current &&
                approved.Status != ToolReviewStatus.Stale && sourceAssembly?.MasterDatums.Status != DatumEvidenceStatus.Stale &&
                sourceAssembly?.SeatingEvidence.Status != ToolReviewStatus.Stale &&
                (pair == CollisionPairKind.WorkpiecePunch || sourceAssembly?.MappingReview.Status != ToolReviewStatus.Stale && sourceAssembly?.HolderObstacles?.Current != false);
            var occupancy = new AxialToolOccupancy(material, intervals, error, payload ?? "UNVERIFIED_AXIAL_SETUP", review.SimulationOnly || envelope.SimulationOnly, current, reasons);
            return new ToolAxialSetupEvidence(record.ToolId, record.FeatureId, pair, payload, review, envelope, library, occupancy);
        }
    }
}
