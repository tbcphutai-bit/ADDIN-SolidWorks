using System;
using System.Linq;
using ADDIN.Commands.BendCollision;
using ADDIN.Commands.BendEnvelope;
using ADDIN.Commands.BendPlacement;
using ADDIN.Commands.BendTools;

namespace ADDIN.Commands.BendProcess
{
    // Deliberately narrow allowed-contact proof: two exact stationary LINE
    // prisms on opposite sides of an explicitly reviewed supporting plane.
    // No area/volume penetration is exempted. General sliding/rolling ARC or
    // uncertain NURBS forming contact requires a different proof => UNKNOWN.
    internal sealed class PlanarFormingContact
    {
        public string PartId { get; }
        public string SheetGeometrySha256 { get; }
        public string ToolSetupSha256 { get; }
        public string EvidenceSha256 { get; }
        public BendProcessPhase Phase { get; }
        public bool NormalIsX { get; }
        public bool SheetPositiveSide { get; }
        public double PlaneMm { get; }
        public double TangentMinMm { get; }
        public double TangentMaxMm { get; }
        public AxialInterval AxialZone { get; }
        internal PlanarFormingContact(string part, string sheetHash, string toolSetupHash, string evidenceHash, BendProcessPhase phase,
            bool normalIsX, bool sheetPositiveSide, double planeMm, double tangentMinMm, double tangentMaxMm, AxialInterval axialZone)
        { PartId = part; SheetGeometrySha256 = sheetHash; ToolSetupSha256 = toolSetupHash; EvidenceSha256 = evidenceHash; Phase = phase;
            NormalIsX = normalIsX; SheetPositiveSide = sheetPositiveSide; PlaneMm = planeMm; TangentMinMm = tangentMinMm; TangentMaxMm = tangentMaxMm; AxialZone = axialZone; }
        internal string Canonical => ToolEvidenceCanonical.Hash("ExactStationaryPrismSurfaceContactV1", PartId, SheetGeometrySha256,
            ToolSetupSha256, EvidenceSha256, Phase.ToString(), NormalIsX.ToString(), SheetPositiveSide.ToString(),
            ToolEvidenceCanonical.Hash(new[] { PlaneMm, TangentMinMm, TangentMaxMm, AxialZone?.StartMm ?? double.NaN,
                AxialZone?.EndMm ?? double.NaN }.Select(ToolEvidenceCanonical.Number).ToArray()));
        internal bool Proves(ProcessSheetPart part, ProcessToolPose tool, BendProcessPhase phase, CollisionPairKind pair)
        {
            if (pair != CollisionPairKind.WorkpiecePunch || Phase != phase || phase == BendProcessPhase.ApproachLoad || phase == BendProcessPhase.RetractUnload ||
                !ToolDatumEvidence.IsSha256(EvidenceSha256) || PartId != part.Id || SheetGeometrySha256 != part.Source.GeometrySha256 ||
                ToolSetupSha256 != tool.SetupSourceSha256 || !part.Motion.Identity || !tool.Motion.Identity ||
                part.Fidelity != ProcessGeometryFidelity.ExactGeometricSurrogate || part.ExactPrismMaterial == null || part.ExactPrismDomain == null ||
                !tool.Valid || tool.Source.AxialUncertaintyMm != 0 || AxialZone == null ||
                !new[] { PlaneMm, TangentMinMm, TangentMaxMm }.All(ToolPoint.Finite) || TangentMaxMm <= TangentMinMm) return false;
            var sheet = part.ExactPrismMaterial; var punch = tool.Source.Material;
            if (!sheet.Valid || !punch.Valid || sheet.FrameId != punch.FrameId ||
                sheet.Contours.Concat(punch.Contours).Any(c => c.UncertaintyMm != 0 || c.Boundary.Any(e => e.Kind != "LINE"))) return false;
            Func<ToolPoint, double> n = p => NormalIsX ? p.X : p.Y;
            Func<ToolPoint, double> t = p => NormalIsX ? p.Y : p.X;
            var a = sheet.Contours.SelectMany(c => c.Boundary).SelectMany(e => new[] { e.Start, e.End }).ToArray();
            var b = punch.Contours.SelectMany(c => c.Boundary).SelectMany(e => new[] { e.Start, e.End }).ToArray();
            if (SheetPositiveSide ? a.Any(p => n(p) < PlaneMm) || b.Any(p => n(p) > PlaneMm) :
                a.Any(p => n(p) > PlaneMm) || b.Any(p => n(p) < PlaneMm)) return false;
            // Require actual face-to-face surface overlap, not merely a
            // declared zone. All possible contact is contained by that zone.
            var ca = a.Where(p => n(p) == PlaneMm).Select(t).ToArray();
            var cb = b.Where(p => n(p) == PlaneMm).Select(t).ToArray();
            if (ca.Length < 2 || cb.Length < 2 || Math.Min(ca.Max(), cb.Max()) <= Math.Max(ca.Min(), cb.Min()) ||
                ca.Concat(cb).Any(x => x < TangentMinMm || x > TangentMaxMm)) return false;
            var ea = sheet.Contours.SelectMany(c => c.Boundary).Where(e => n(e.Start) == PlaneMm && n(e.End) == PlaneMm).ToArray();
            var eb = punch.Contours.SelectMany(c => c.Boundary).Where(e => n(e.Start) == PlaneMm && n(e.End) == PlaneMm).ToArray();
            if (!ea.Any(x => eb.Any(y => Math.Min(Math.Max(t(x.Start), t(x.End)), Math.Max(t(y.Start), t(y.End))) >
                Math.Max(Math.Min(t(x.Start), t(x.End)), Math.Min(t(y.Start), t(y.End)))))) return false;
            bool touches = false;
            foreach (var z in tool.Source.Segments)
            {
                var common = z.Intersect(part.ExactPrismDomain); if (common == null) continue;
                if (common.StartMm < AxialZone.StartMm || common.EndMm > AxialZone.EndMm) return false;
                touches = true;
            }
            return touches;
        }
    }
}
