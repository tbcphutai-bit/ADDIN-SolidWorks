using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ADDIN.Commands.BendTools;

namespace ADDIN.Commands.BendPlacement
{
    internal static class PlacementLists
    {
        internal static IReadOnlyList<T> Copy<T>(IEnumerable<T> source) => Array.AsReadOnly((source??Enumerable.Empty<T>()).ToArray());
        internal static IReadOnlyList<string> Reasons(IEnumerable<string> source) => Copy(source.Distinct().OrderBy(x=>x,StringComparer.Ordinal));
    }
    internal sealed class PlacementLease
    {
        private int current=1;
        public bool Current => Volatile.Read(ref current)==1;
        internal void Invalidate() { Interlocked.Exchange(ref current,0); }
    }
    internal enum SheetCoverage { Unsupported2DEnvelope, CompleteUniformExtrusion, SimulationOnly }
    internal enum BendStateCoverage { UnknownBefore, UnknownDuring, UnknownAfter, FinalModelObserved, SimulationOnly }
    internal enum PlacementReadiness { PlacementDraft, SimulationOnly, ReadyForCollisionCheck }

    internal sealed class GeometrySelection2D
    {
        public string SourceSha256 { get; }
        public string FrameId { get; }
        public string GeometrySha256 { get; }
        public IReadOnlyList<string> EntityKeys { get; }
        public IReadOnlyList<string> UnsupportedTypes { get; }
        public IReadOnlyList<ToolCurve> Curves { get; }
        public IReadOnlyList<ToolTransform> InstanceTransforms { get; }
        public bool ObservedClosed { get; }
        public ToolRegionValidation Region { get; }
        public bool ReferenceOnly { get; }
        public bool Supported => EntityKeys.Count>0&&UnsupportedTypes.Count==0&&Curves.Count>0;
        internal GeometrySelection2D(string sha,string geometrySha,IEnumerable<ToolDxfEntity> entities,bool reference,CancellationToken cancellation)
        {
            var e=entities.ToArray();SourceSha256=sha;GeometrySha256=geometrySha;FrameId="MasterWcsMm:"+sha;ReferenceOnly=reference;
            EntityKeys=PlacementLists.Copy(e.Select(x=>x.InstancePath+"|"+x.SourceRecord+"|"+x.Handle));
            Curves=PlacementLists.Copy(e.SelectMany(x=>x.Curves));InstanceTransforms=PlacementLists.Copy(e.Select(x=>x.Transform));
            UnsupportedTypes=PlacementLists.Copy(e.Where(x=>x.UnsupportedReason!=null||x.Curves.Count==0).Select(x=>x.Type).Distinct());
            ObservedClosed=ToolProfileTopology.Closed(e);Region=ToolRegionValidator.Validate(e,cancellation:cancellation);
        }
    }
    internal sealed class ToolAssemblyProfile
    {
        public string Id { get; }
        public string MasterSha256 { get; }
        public string ManifestSha256 { get; }
        public GeometrySelection2D Punch { get; }
        public GeometrySelection2D Holder { get; }
        public ToolDatumSet MasterDatums { get; }
        public bool IdentityMatched { get; }
        public bool MappingIdentified => IdentityMatched&&Punch!=null&&Holder!=null;
        public bool ClampVerified => false;
        public bool PhysicalPairVerified => false;
        public IReadOnlyList<string> Reasons { get; }
        internal ToolAssemblyProfile(string id,string sha,string manifest,GeometrySelection2D punch,GeometrySelection2D holder,ToolDatumSet datums,bool mapped,IEnumerable<string> reasons)
        {Id=id;MasterSha256=sha;ManifestSha256=manifest;Punch=punch;Holder=holder;MasterDatums=datums;IdentityMatched=mapped;Reasons=PlacementLists.Reasons(reasons);}
    }
    internal enum BendLimitKind { MaxBendLength, DeepBendClearanceExample }
    internal sealed class BendLimitRuleDraft
    {
        public string Id { get; }
        public string FamilyId { get; }
        public BendLimitKind Kind { get; }
        public string WorkbookSha256 { get; }
        public string Sheet { get; }
        public string Locator { get; }
        public string ObjectSha256 { get; }
        public string Unit { get; }
        public double? ValueMm { get; }
        public bool FamilyMappingVerified => false;
        public bool ReferenceOnly => true;
        public IReadOnlyList<string> ToolIds { get; }
        public IReadOnlyList<string> Reasons { get; }
        internal BendLimitRuleDraft(string id,string family,BendLimitKind kind,string sha,string sheet,string locator,string objectSha,string unit,double? value,IEnumerable<string> ids,IEnumerable<string> reasons)
        {Id=id;FamilyId=family;Kind=kind;WorkbookSha256=sha;Sheet=sheet;Locator=locator;ObjectSha256=objectSha;Unit=unit;ValueMm=value;ToolIds=PlacementLists.Copy(ids);Reasons=PlacementLists.Reasons(reasons);}
    }
    internal sealed class PlacementSourceSnapshot
    {
        private readonly PlacementLease lease=new PlacementLease();
        public bool Current => lease.Current;
        public string Status => "CHUA_XAC_MINH";
        public string Fingerprint { get; }
        public IReadOnlyList<ToolAssemblyProfile> Tools { get; }
        public IReadOnlyList<BendLimitRuleDraft> LimitRules { get; }
        public IReadOnlyList<string> Reasons { get; }
        internal PlacementSourceSnapshot(string fingerprint,IEnumerable<ToolAssemblyProfile> tools,IEnumerable<BendLimitRuleDraft> rules,IEnumerable<string> reasons)
        {Fingerprint=fingerprint;Tools=PlacementLists.Copy(tools);LimitRules=PlacementLists.Copy(rules);Reasons=PlacementLists.Reasons(reasons);}
        internal void Invalidate(){lease.Invalidate();foreach(var t in Tools)t.MasterDatums?.Invalidate();}
        internal static PlacementSourceSnapshot Missing(string reason) => new PlacementSourceSnapshot("Missing:"+reason,CompanyToolIds.All.Select(id=>
            new ToolAssemblyProfile(id,null,null,null,null,null,false,new[]{reason,"MISSING_MASTER_MAPPING","UNVERIFIED_HOLDER"})),null,new[]{reason});
    }
    internal sealed class SheetSection2D
    {
        public int FeatureId { get; }
        public BendWorkFrame Frame { get; }
        public double? ThicknessMm { get; }
        public SheetCoverage Coverage { get; }
        public IReadOnlyList<ToolCurve> Boundary { get; }
        public ToolRegionValidation Region { get; }
        public IReadOnlyList<string> Reasons { get; }
        internal SheetSection2D(int id,BendWorkFrame frame,double? thickness,SheetCoverage coverage,IEnumerable<ToolCurve> curves,ToolRegionValidation region,IEnumerable<string> reasons)
        {FeatureId=id;Frame=frame;ThicknessMm=thickness;Coverage=coverage;Boundary=PlacementLists.Copy(curves);Region=region;Reasons=PlacementLists.Reasons(reasons);}
    }
    internal sealed class BendStateDraft2D
    {
        public BendStateCoverage Coverage { get; }
        public SheetSection2D Section { get; }
        public bool SequenceVerified => false;
        internal BendStateDraft2D(BendStateCoverage coverage,SheetSection2D section=null){Coverage=coverage;Section=section;}
    }
    internal sealed class ToolGeometryPlacement2D
    {
        public GeometrySelection2D Geometry { get; }
        public PlacementTransform2D ToWorkFrame { get; }
        public IReadOnlyList<ToolCurve> PlacedCurves { get; }
        public string WorkFrameId { get; }
        internal ToolGeometryPlacement2D(GeometrySelection2D geometry,PlacementTransform2D transform,string frame)
        {Geometry=geometry;ToWorkFrame=transform;WorkFrameId=frame;PlacedCurves=PlacementLists.Copy(geometry==null||transform==null?null:geometry.Curves.Select(c=>transform.Apply(c)));}
    }
    internal sealed class ToolCandidateDraft
    {
        private readonly Func<bool> current;
        public string ToolId { get; }
        public int FeatureId { get; }
        public string Status => "CHUA_XAC_MINH";
        public bool Current => current();
        public bool ReadyForCollisionCheck => false; // physical/process evidence is not established in Phase 3B
        public PlacementReadiness Readiness { get; }
        public bool SimulationOnly { get; }
        public bool PlacementGeometryComplete => Current&&Assembly?.MappingIdentified==true&&Sheet.Coverage!=SheetCoverage.Unsupported2DEnvelope&&
            PunchPlacement.ToWorkFrame!=null&&HolderPlacement.ToWorkFrame!=null&&Assembly.Punch.Supported&&Assembly.Holder.Supported&&
            Assembly.Punch.Region.IsValidRegion&&Assembly.Holder.Region.IsValidRegion&&Assembly.Punch.ObservedClosed&&Assembly.Holder.ObservedClosed&&
            Assembly.Punch.Region.MaximumJointGapMm<=1e-10&&Assembly.Holder.Region.MaximumJointGapMm<=1e-10;
        public ToolAssemblyProfile Assembly { get; }
        public DatumEvidenceStatus DatumStatus => Assembly?.MasterDatums?.Status??DatumEvidenceStatus.Missing;
        public ToolGeometryPlacement2D PunchPlacement { get; }
        public ToolGeometryPlacement2D HolderPlacement { get; }
        public SheetSection2D Sheet { get; }
        public IReadOnlyList<BendStateDraft2D> BendStates { get; }
        public IReadOnlyList<BendLimitRuleDraft> LimitRules { get; }
        private readonly IReadOnlyList<string> reasons;
        public IReadOnlyList<string> Reasons => PlacementLists.Reasons(reasons
            .Concat(Current?new string[0]:new[]{"STALE_SOURCE"})
            .Concat(DatumStatus==DatumEvidenceStatus.Stale?new[]{"STALE_DATUM_EVIDENCE"}:new string[0]));
        internal ToolCandidateDraft(string id,int feature,ToolAssemblyProfile assembly,SheetSection2D sheet,ToolGeometryPlacement2D punch,ToolGeometryPlacement2D holder,
            IEnumerable<BendLimitRuleDraft> rules,IEnumerable<string> reasons,bool simulation,Func<bool> current)
        {
            ToolId=id;FeatureId=feature;Assembly=assembly;Sheet=sheet;PunchPlacement=punch;HolderPlacement=holder;LimitRules=PlacementLists.Copy(rules);
            this.reasons=PlacementLists.Reasons(reasons);SimulationOnly=simulation;Readiness=simulation?PlacementReadiness.SimulationOnly:PlacementReadiness.PlacementDraft;this.current=current;
            BendStates=PlacementLists.Copy(new[]{new BendStateDraft2D(BendStateCoverage.UnknownBefore),new BendStateDraft2D(BendStateCoverage.UnknownDuring),
                new BendStateDraft2D(BendStateCoverage.UnknownAfter),new BendStateDraft2D(BendStateCoverage.FinalModelObserved,sheet)});
        }
    }
    internal sealed class PlacementDraft
    {
        private readonly Func<bool> current;
        public string Status => "CHUA_XAC_MINH";
        public bool Current => current();
        public string ModelSnapshotId { get; }
        public int ModelUpdateStamp { get; }
        public string Configuration { get; }
        public IReadOnlyList<SheetSection2D> Sections { get; }
        public IReadOnlyList<ToolCandidateDraft> Candidates { get; }
        public string WinnerToolId => null;
        internal PlacementDraft(string modelId,int stamp,string configuration,IEnumerable<SheetSection2D> sections,IEnumerable<ToolCandidateDraft> candidates,Func<bool> current)
        {ModelSnapshotId=modelId;ModelUpdateStamp=stamp;Configuration=configuration;Sections=PlacementLists.Copy(sections);Candidates=PlacementLists.Copy(candidates);this.current=current;}
    }
}
