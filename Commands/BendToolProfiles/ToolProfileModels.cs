using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;

namespace ADDIN.Commands.BendTools
{
    internal enum ToolGeometryRole { CompanyPunchProfile, HolderClampReference, VerifiedHolderGeometry, ManufacturerReference, Annotations, UnclassifiedReference }

    internal static class CompanyToolIds
    {
        internal static readonly IReadOnlyList<string> All=Array.AsReadOnly(new[] {
            "AMD-STD-003","AMD-STD-004","AMD-STD-0048","AMD-STD-0049","AMD-STD-008","AMD-STD-045",
            "AMD-STD-047","AMD-STD-103","AMD-STD-109","AMD-STD-117","AMD-STD-200","AMD-STD-202",
            "AMD-STD-210","AMD-STD-452","AMD-STD-453","AMD-202662","AMD-204662","KIK-7181328001" });
    }

    internal sealed class ToolProfileProvenance
    {
        public string Path { get; internal set; }
        public string Sha256 { get; internal set; }
        public bool FileExists { get; internal set; }
        public long ByteCount { get; internal set; }
        public string DxfVersion { get; internal set; }
        public int? InsUnits { get; internal set; }
        public double? UnitToMm { get; internal set; }
        public double InsBaseX { get; internal set; }
        public double InsBaseY { get; internal set; }
        public double InsBaseZ { get; internal set; }
        public string Error { get; internal set; }
    }

    internal sealed class ToolGeometryPart
    {
        public ToolGeometryRole Role { get; internal set; }
        public ToolProfileProvenance Source { get; internal set; }
        public IReadOnlyList<ToolDxfEntity> Entities { get; internal set; }
        public ToolRegionValidation Region { get; internal set; } = ToolRegionValidator.Validate(null);
    }

    internal sealed class ToolProfileValidationResult
    {
        public string Status => "CHUA_XAC_MINH";
        public bool SourceFilesExist { get; internal set; }
        public bool PunchGeometryReadComplete { get; internal set; }
        public bool PunchEntitiesSupported { get; internal set; }
        public bool PunchClosed { get; internal set; }
        public ToolRegionValidation Region { get; internal set; } = ToolRegionValidator.Validate(null);
        internal ToolDatumSet DatumEvidence;
        public DatumEvidenceStatus DatumEvidenceStatus => DatumEvidence?.Status ?? ADDIN.Commands.BendTools.DatumEvidenceStatus.Missing;
        // Legacy physical-verification flags are deliberately not promoted by
        // a geometrically valid region or an externally attested datum record.
        public bool ToolTipOriginVerified => false;
        public bool CoordinateFrameVerified => false;
        public bool ClampVerified => false;
        public IReadOnlyList<string> Reasons { get; internal set; }
    }

    internal sealed class CompanyToolProfile
    {
        public string Id { get; internal set; }
        public IReadOnlyList<ToolGeometryPart> Parts { get; internal set; }
        public ToolProfileValidationResult Validation { get; internal set; }
        public ToolDatumSet Datums { get; internal set; }
    }

    internal sealed class ToolProfileLibrarySnapshot
    {
        public bool UsesFixedCadPairs { get; internal set; }
        private int current=1;
        public bool Current => Volatile.Read(ref current)==1;
        public string Status => "CHUA_XAC_MINH";
        public string Fingerprint { get; internal set; }
        public string ConfigurationSha256 { get; internal set; }
        public IReadOnlyList<CompanyToolProfile> Tools { get; internal set; }
        public IReadOnlyList<ToolProfileProvenance> Sources { get; internal set; }
        public string Error { get; internal set; }
        public BendPlacement.PlacementSourceSnapshot PlacementSources { get; internal set; } = BendPlacement.PlacementSourceSnapshot.Missing("PLACEMENT_SOURCE_NOT_CONFIGURED");
        internal void Invalidate()
        {
            Interlocked.Exchange(ref current,0);
            if (Tools!=null) foreach (var tool in Tools) tool.Datums?.Invalidate();
            PlacementSources?.Invalidate();
        }

        // A separate diagnostic summary: never serialize proprietary coordinates
        // into the Bend Planning report or use this data as a manufacturing PASS.
        internal string ToDebugJson() => new JavaScriptSerializer().Serialize(new {
            Status, Current, Fingerprint, ConfigurationSha256, Error, ToolCount=Tools.Count,
            DataCount=Tools.Count(t=>t.Validation.PunchGeometryReadComplete),
            Tools=Tools.Select(t=>new { t.Id, Validation=new { t.Validation.Status, t.Validation.SourceFilesExist,
                t.Validation.PunchGeometryReadComplete, t.Validation.PunchEntitiesSupported, t.Validation.PunchClosed,
                GeometryValidationStatus=t.Validation.Region.Status.ToString(), t.Validation.Region.LoopCount, t.Validation.Region.Issues,
                DatumEvidenceStatus=t.Validation.DatumEvidenceStatus.ToString(), t.Validation.ToolTipOriginVerified,
                t.Validation.CoordinateFrameVerified, t.Validation.ClampVerified, t.Validation.Reasons }, Parts=t.Parts.Select(p=>new {
                Role=p.Role.ToString(), p.Source.Sha256, p.Source.Error,
                GeometryValidationStatus=p.Region.Status.ToString(), p.Region.LoopCount, p.Region.Issues,
                EntityCount=p.Entities.Count, CurveCount=p.Entities.Sum(e=>e.Curves.Count),
                ArcCount=p.Entities.Sum(e=>e.Curves.Count(c=>c.Kind=="ARC")) }) }) });
    }
}
