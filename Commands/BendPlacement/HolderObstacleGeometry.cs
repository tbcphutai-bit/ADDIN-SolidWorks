using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using ADDIN.Commands.BendTools;

namespace ADDIN.Commands.BendPlacement
{
    internal sealed class HolderContourDefinition
    {
        public string Id { get; set; }
        public string ParentId { get; set; }
        public string MaterialKind { get; set; } // Solid, Hole, Island, never inferred
        public string[] EntityKeys { get; set; }
    }
    internal sealed class HolderExcludedEntity
    {
        public string EntityKey { get; set; }
        public string Role { get; set; } // Decorative, Centerline, Reference, Annotation
    }
    internal sealed class HolderMaterialDefinition
    {
        public string ToolId { get; set; }
        public string BindingSha256 { get; set; }
        public List<HolderContourDefinition> Contours { get; set; }
        public List<HolderExcludedEntity> Excluded { get; set; }
        public string CombinationRule { get; set; } // explicit UnionOfComponentMaterialsV1, else disjoint hierarchy
        public ToolReviewProof Review { get; set; }
    }
    internal sealed class HolderEntityGeometry
    {
        public string EntityKey { get; }
        public string SourceType { get; }
        public string SourceLineType { get; }
        public string Layer { get; }
        public bool Supported { get; }
        public string Reason { get; }
        public bool? SourceClosed { get; }
        public SplineApproximation Spline { get; }
        public IReadOnlyList<ToolCurve> NominalCurves { get; }
        public double ErrorBoundMm => Spline?.MaximumDeviationMm??0;
        internal HolderEntityGeometry(ToolDxfEntity e,CancellationToken token)
        {
            EntityKey=Key(e);SourceType=e.Type;SourceLineType=e.SourceLineType;Layer=e.Layer;SourceClosed=e.SourceClosed;
            if(e.Type=="SPLINE")
            {
                Spline=ToolNurbsGeometry.Approximate(e.Spline,cancellation:token);Supported=Spline.Supported;Reason=Spline.Reason;
                NominalCurves=PlacementLists.Copy(Spline.Chords.Select(c=>c.Chord));
            }
            else
            {
                // Keep topology clustering inside a declared mm domain. An
                // oversized analytic entity is retained as unsupported rather
                // than overflowing the spatial bucket index or distance math.
                bool domain=e.Curves.All(c=>new[]{c.Start,c.End,c.Center}.All(p=>Math.Abs(p.X)<=1e7&&Math.Abs(p.Y)<=1e7)&&c.RadiusMm<=1e7);
                Supported=!e.Annotation&&!e.PaperSpace&&e.UnsupportedReason==null&&e.Curves.Count>0&&domain;
                Reason=Supported?null:domain?"UNSUPPORTED_HOLDER_ENTITY":"UNSUPPORTED_HOLDER_COORDINATE_DOMAIN";
                NominalCurves=PlacementLists.Copy(Supported?e.Curves:null);
            }
        }
        internal static string Key(ToolDxfEntity e)=>e.InstancePath+"|"+e.SourceRecord+"|"+e.Handle;
    }
    internal sealed class HolderContourRegion
    {
        public string Id { get; }
        public string ParentId { get; }
        public string MaterialKind { get; }
        public IReadOnlyList<ToolCurve> Boundary { get; }
        public ToolRegionValidation Validation { get; }
        public double BoundaryUncertaintyMm { get; }
        internal HolderContourRegion(HolderContourDefinition definition,IEnumerable<HolderEntityGeometry> members,CancellationToken token)
        {
            Id=definition.Id;ParentId=definition.ParentId;MaterialKind=definition.MaterialKind;var parts=members.ToArray();
            Boundary=PlacementLists.Copy(parts.SelectMany(e=>e.NominalCurves));
            // Explicit reviewed composition of EXISTING entities may close an
            // open entity using other source entities. No edge is added and all
            // individual source flags remain in the inventory.
            Validation=ToolRegionValidator.Validate(new[]{new ToolDxfEntity {Type="REVIEWED_HOLDER_COMPOSITE",Curves=Boundary}},cancellation:token);
            BoundaryUncertaintyMm=parts.Max(e=>e.ErrorBoundMm)+Validation.MaximumJointGapMm;
        }
    }
    internal sealed class HolderMaterialRegion
    {
        public HolderContourRegion Outer { get; }
        public IReadOnlyList<HolderContourRegion> Holes { get; }
        internal HolderMaterialRegion(HolderContourRegion outer,IEnumerable<HolderContourRegion> holes){Outer=outer;Holes=PlacementLists.Copy(holes);}
    }
    internal sealed class HolderTopologyInventory
    {
        public int ConnectedComponents { get; }
        public int ClosedSimpleComponents { get; }
        public int OpenOrBranchedComponents { get; }
        public bool IsMaterialClassification => false;
        internal HolderTopologyInventory(IReadOnlyList<HolderEntityGeometry> entities,CancellationToken token)
        {
            var curves=entities.Where(e=>e.Supported).SelectMany(e=>e.NominalCurves).ToArray();
            if(curves.Length>8192){OpenOrBranchedComponents=1;return;}
            var nodes=new Dictionary<string,List<int>>();var parent=Enumerable.Range(0,curves.Length).ToArray();
            Func<int,int> root=null;root=i=>{while(parent[i]!=i){parent[i]=parent[parent[i]];i=parent[i];}return i;};
            var points=new List<ToolPoint>();var pointEdges=new List<int>();double t=1e-5;
            for(int i=0;i<curves.Length;i++)foreach(var point in new[]{curves[i].Start,curves[i].End})
            {
                token.ThrowIfCancellationRequested();long x=(long)Math.Floor(point.X/t),y=(long)Math.Floor(point.Y/t);
                for(int dx=-1;dx<=1;dx++)for(int dy=-1;dy<=1;dy++)
                {List<int> bucket;if(nodes.TryGetValue((x+dx)+":"+(y+dy),out bucket))foreach(int n in bucket)if(ToolPoint.Distance(point,points[n])<=t)parent[root(i)]=root(pointEdges[n]);}
                string key=x+":"+y;List<int> own;if(!nodes.TryGetValue(key,out own)){own=new List<int>();nodes.Add(key,own);}own.Add(points.Count);points.Add(point);pointEdges.Add(i);
            }
            foreach(var group in Enumerable.Range(0,curves.Length).GroupBy(root))
            {
                token.ThrowIfCancellationRequested();ConnectedComponents++;
                var region=ToolRegionValidator.Validate(new[]{new ToolDxfEntity {Type="INVENTORY_ONLY",Curves=group.Select(i=>curves[i]).ToArray()}},cancellation:token);
                if(region.IsValidRegion)ClosedSimpleComponents++;else OpenOrBranchedComponents++;
            }
        }
    }
    internal sealed class HolderObstacleSet
    {
        public const string PolicyVersion="ReviewedMaterialSetWithBoundaryBandV1";
        private readonly PlacementLease lease=new PlacementLease();
        public bool Current => lease.Current&&Review.Status!=ToolReviewStatus.Stale;
        public string BindingSha256 { get; }
        public string DefinitionSha256 { get; }
        public IReadOnlyList<HolderEntityGeometry> Inventory { get; }
        public HolderTopologyInventory Topology { get; }
        public IReadOnlyList<HolderContourRegion> Contours { get; }
        public IReadOnlyList<HolderMaterialRegion> Regions { get; }
        public ToolReviewValidation Review { get; }
        public IReadOnlyList<string> Reasons { get; }
        public IReadOnlyDictionary<string,string> ReviewedRoles { get; }
        public bool GeometrySupportedForCollision => Current&&Review.RecordVerified&&Regions.Count>0&&Reasons.Count==0;
        public bool PhysicalHolderVerified => false; // seating evidence owns this separate assertion
        public bool RequiresConservativeBoundaryBand => Contours.Any(c=>c.BoundaryUncertaintyMm>0);
        public string EnvelopeContract => "NominalMaterialUnionPlusEveryBoundaryUncertaintyBandV1";
        public string RegionCombination => "UnionOfEachOuterMinusItsOwnHoles";
        public int SplineCount => Inventory.Count(e=>e.SourceType=="SPLINE");
        public int SupportedSplineCount => Inventory.Count(e=>e.SourceType=="SPLINE"&&e.Supported);
        private HolderObstacleSet(string binding,string definition,IEnumerable<HolderEntityGeometry> inventory,HolderTopologyInventory topology,IEnumerable<HolderContourRegion> contours,
            IEnumerable<HolderMaterialRegion> regions,ToolReviewValidation review,IEnumerable<string> reasons,HolderMaterialDefinition reviewedDefinition)
        {
            BindingSha256=binding;DefinitionSha256=definition;Inventory=PlacementLists.Copy(inventory);Topology=topology;Contours=PlacementLists.Copy(contours);Regions=PlacementLists.Copy(regions);Review=review;Reasons=PlacementLists.Reasons(reasons);
            var roles=new Dictionary<string,string>(StringComparer.Ordinal);
            if(review.RecordVerified&&Regions.Count>0&&Reasons.Count==0&&reviewedDefinition!=null)
            {
                foreach(var c in reviewedDefinition.Contours)foreach(string key in c.EntityKeys)roles.Add(key,c.MaterialKind+" / "+c.Id);
                foreach(var x in reviewedDefinition.Excluded??new List<HolderExcludedEntity>())roles.Add(x.EntityKey,x.Role);
            }
            ReviewedRoles=new System.Collections.ObjectModel.ReadOnlyDictionary<string,string>(roles);
        }
        internal void Invalidate(){lease.Invalidate();Review.Invalidate();}
        internal static string Binding(string id,GeometrySelection2D punch,GeometrySelection2D holder,string manifest)=>ToolEvidenceCanonical.Hash(
            "HolderBindingV1",id,punch?.SourceSha256,holder?.SourceSha256,manifest,punch?.GeometrySha256,holder?.GeometrySha256,punch?.FrameId,holder?.FrameId,SplineApproximation.PolicyVersion,PolicyVersion);
        internal static string Payload(HolderMaterialDefinition d)
        {
            var values=new List<string> {"HolderMaterialRuleV1",d.ToolId,d.BindingSha256,d.CombinationRule};
            foreach(var c in (d.Contours??new List<HolderContourDefinition>()).OrderBy(c=>c?.Id,StringComparer.Ordinal))
            {if(c==null)throw new InvalidDataException("Null contour.");values.AddRange(new[]{c.Id,c.ParentId,c.MaterialKind});values.Add(ToolEvidenceCanonical.Hash((c.EntityKeys??new string[0]).OrderBy(k=>k,StringComparer.Ordinal).ToArray()));}
            foreach(var e in (d.Excluded??new List<HolderExcludedEntity>()).OrderBy(e=>e?.EntityKey,StringComparer.Ordinal))
            {if(e==null)throw new InvalidDataException("Null role.");values.Add(e.EntityKey);values.Add(e.Role);}
            return ToolEvidenceCanonical.Hash(values.ToArray());
        }
        internal static HolderObstacleSet Read(string id,GeometrySelection2D punch,GeometrySelection2D holder,string manifest,HolderMaterialDefinition definition,
            ToolReviewVerifier verifier,string evidenceRoot,CancellationToken token)
        {
            string binding=Binding(id,punch,holder,manifest),payload=null;var reasons=new List<string>();var inventory=new List<HolderEntityGeometry>();
            var contours=new List<HolderContourRegion>();var regions=new List<HolderMaterialRegion>();var review=ToolReviewValidation.Missing("UNSUPPORTED_HOLDER_FILL_RULE");
            HolderTopologyInventory topology=null;
            try
            {
                if(holder==null)throw new InvalidDataException("MAPPING_UNRESOLVED");
                inventory=holder.SourceEntities.Select(e=>new HolderEntityGeometry(e,token)).ToList();topology=new HolderTopologyInventory(inventory,token);
                if(definition==null)throw new InvalidDataException("UNSUPPORTED_HOLDER_FILL_RULE");
                payload=Payload(definition);
                if(definition.ToolId!=id||definition.BindingSha256!=binding)throw new InvalidDataException("STALE_HOLDER_MATERIAL_BINDING");
                review=verifier.Verify("HolderMaterialRule",payload,definition.Review,evidenceRoot,token);
                if(!review.RecordVerified)throw new InvalidDataException("UNSUPPORTED_HOLDER_FILL_RULE");
                if(definition.CombinationRule!=null&&definition.CombinationRule!="DisjointMaterialHierarchyV1"&&definition.CombinationRule!="UnionOfComponentMaterialsV1")throw new InvalidDataException("UNSUPPORTED_HOLDER_COMBINATION_RULE");
                if(definition.Contours==null||definition.Contours.Count==0||definition.Contours.Count>128||definition.Contours.Any(c=>c==null||string.IsNullOrWhiteSpace(c.Id)||c.EntityKeys==null||c.EntityKeys.Length==0)||
                    definition.Contours.GroupBy(c=>c.Id,StringComparer.Ordinal).Any(g=>g.Count()>1))throw new InvalidDataException("INVALID_HOLDER_CONTOUR_DEFINITION");
                var assignments=definition.Contours.SelectMany(c=>c.EntityKeys).Concat((definition.Excluded??new List<HolderExcludedEntity>()).Select(e=>e?.EntityKey)).ToArray();
                if(assignments.Any(k=>k==null)||assignments.Distinct(StringComparer.Ordinal).Count()!=assignments.Length||!assignments.OrderBy(k=>k,StringComparer.Ordinal).SequenceEqual(inventory.Select(e=>e.EntityKey).OrderBy(k=>k,StringComparer.Ordinal)))
                    throw new InvalidDataException("INCOMPLETE_OR_DUPLICATE_HOLDER_ROLE_ASSIGNMENT");
                if((definition.Excluded??new List<HolderExcludedEntity>()).Any(e=>!new[]{"Decorative","Centerline","Reference","Annotation"}.Contains(e.Role)))throw new InvalidDataException("INVALID_HOLDER_EXCLUDED_ROLE");
                foreach(var c in definition.Contours)
                {
                    var members=c.EntityKeys.Select(k=>inventory.Single(e=>e.EntityKey==k)).ToArray();
                    if(members.Any(e=>!e.Supported))throw new InvalidDataException("UNSUPPORTED_HOLDER_BOUNDARY_ENTITY");
                    var region=new HolderContourRegion(c,members,token);contours.Add(region);
                    if(!region.Validation.IsValidRegion||region.Validation.MaximumJointGapMm>1e-10)throw new InvalidDataException("INVALID_HOLDER_CONTOUR_REGION");
                    if(!new[]{"Solid","Hole","Island"}.Contains(c.MaterialKind))throw new InvalidDataException("UNSUPPORTED_HOLDER_FILL_RULE");
                }
                foreach(var c in contours)
                {
                    var seen=new HashSet<string>();var at=c;
                    while(!string.IsNullOrEmpty(at.ParentId))
                    {
                        if(!seen.Add(at.Id))throw new InvalidDataException("CYCLIC_HOLDER_FILL_RULE");
                        var parent=contours.SingleOrDefault(p=>p.Id==at.ParentId);if(parent==null||at.MaterialKind=="Solid"||at.MaterialKind=="Hole"&&parent.MaterialKind=="Hole"||at.MaterialKind=="Island"&&parent.MaterialKind!="Hole")
                            throw new InvalidDataException("INVALID_HOLDER_MATERIAL_PARENT");at=parent;
                    }
                    if(at.MaterialKind!="Solid")throw new InvalidDataException("INVALID_ROOT_HOLDER_MATERIAL");
                }
                for(int a=0;a<contours.Count;a++)for(int b=a+1;b<contours.Count;b++)
                {
                    var first=contours[a];var second=contours[b];double uncertainty=first.BoundaryUncertaintyMm+second.BoundaryUncertaintyMm+1e-5;
                    // Explicit component union is a set expression, not a
                    // merged/largest outline. A future consumer must OR all
                    // component materials, subtracting holes per component.
                    // Cross-component intersections/touching are legitimate
                    // only when this exact combination rule was reviewed.
                    if(definition.CombinationRule=="UnionOfComponentMaterialsV1"&&Root(first,contours)!=Root(second,contours))continue;
                    if(ToolRegionValidator.BoundariesContact(first.Boundary,second.Boundary,uncertainty,token))throw new InvalidDataException("UNSUPPORTED_TOUCHING_OR_OVERLAPPING_HOLDER_UNION");
                    bool ab=ToolRegionValidator.ContainsPoint(first.Boundary,second.Boundary[0].Start),ba=ToolRegionValidator.ContainsPoint(second.Boundary,first.Boundary[0].Start);
                    if(ab!=Ancestor(first,second,contours)||ba!=Ancestor(second,first,contours))throw new InvalidDataException("MATERIAL_CONTAINMENT_DOES_NOT_MATCH_REVIEW");
                }
                regions=contours.Where(c=>c.MaterialKind!="Hole").Select(c=>new HolderMaterialRegion(c,contours.Where(h=>h.ParentId==c.Id&&h.MaterialKind=="Hole"))).ToList();
            }
            catch(OperationCanceledException){throw;}
            catch(Exception ex) when(ex is InvalidDataException||ex is ArgumentException||ex is InvalidOperationException)
            {reasons.Add(ex is InvalidDataException?ex.Message:"INVALID_HOLDER_DEFINITION");regions.Clear();}
            if(!review.RecordVerified&&definition!=null)reasons.Add(review.Reason);
            return new HolderObstacleSet(binding,payload,inventory,topology,contours,regions,review,reasons,definition);
        }
        private static bool Ancestor(HolderContourRegion parent,HolderContourRegion child,IEnumerable<HolderContourRegion> contours)
        {string at=child.ParentId;while(!string.IsNullOrEmpty(at)){if(at==parent.Id)return true;at=contours.Single(c=>c.Id==at).ParentId;}return false;}
        private static string Root(HolderContourRegion at,IEnumerable<HolderContourRegion> contours)
        {while(!string.IsNullOrEmpty(at.ParentId))at=contours.Single(c=>c.Id==at.ParentId);return at.Id;}
    }
}
