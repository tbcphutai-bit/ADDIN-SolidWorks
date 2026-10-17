using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Script.Serialization;
using ADDIN.Commands.BendTools;
using ADDIN.Commands.BendCollision;

namespace ADDIN.Commands.BendPlacement
{
    // An explicit manual/synthetic placement is useful for math inspection, but
    // cannot be mistaken for a production datum/approach certificate.
    internal sealed class PlacementSimulationRequest
    {
        public string ToolId { get; }
        public string SourceFrameId { get; }
        public ToolPoint TipMm { get; }
        public ToolPoint WorkingDirection { get; }
        public ToolPoint TargetContactMm { get; }
        public ToolPoint TargetApproach { get; }
        public bool Mirror { get; }
        public bool SimulationOnly => true;
        public PlacementRegistration2D Registration { get; }
        internal PlacementSimulationRequest(string id,string frame,ToolPoint tip,ToolPoint direction,ToolPoint target,ToolPoint approach,bool mirror=false,PlacementRegistration2D registration=null)
        {PlacementTransform2D.Unit(direction);PlacementTransform2D.Unit(approach);ToolId=id;SourceFrameId=frame;TipMm=tip;WorkingDirection=direction;TargetContactMm=target;TargetApproach=approach;Mirror=mirror;Registration=registration;}
    }
    internal sealed class PlacementRegistration2D
    {
        public string FromFrame { get; }
        public string ToFrame { get; }
        public PlacementTransform2D Transform { get; }
        public bool SimulationOnly => true;
        internal PlacementRegistration2D(string from,string to,PlacementTransform2D transform){FromFrame=from;ToFrame=to;Transform=transform;}
    }
    internal static class BendPlacementBuilder
    {
        internal static PlacementDraft Build(PlacementModelCapture model,ToolProfileLibrarySnapshot library,Func<bool> ownerCurrent=null,PlacementSimulationRequest simulation=null)
            => BuildCore(model,library,ownerCurrent,simulation,null);
        // Separate production entry: there is no fallback from a missing
        // external work-pose approval to a manual or inferred placement.
        internal static PlacementDraft BuildWithProductionPlacements(PlacementModelCapture model,ToolProfileLibrarySnapshot library,
            IEnumerable<CollisionWorkPoseEvidence> production,Func<bool> ownerCurrent=null)
            => BuildCore(model,library,ownerCurrent,null,production);
        // Managed preparation for the Phase 3D worker. It deliberately skips
        // constructor-time kernel calls; the legacy synchronous path is kept.
        internal static PlacementDraft PrepareForAsync(PlacementModelCapture model,ToolProfileLibrarySnapshot library)
            => BuildCore(model,library,null,null,null,false);
        private static PlacementDraft BuildCore(PlacementModelCapture model,ToolProfileLibrarySnapshot library,Func<bool> ownerCurrent,
            PlacementSimulationRequest simulation,IEnumerable<CollisionWorkPoseEvidence> production,bool evaluateLegacyCollision=true)
        {
            if(model==null)throw new ArgumentNullException(nameof(model));
            var source=library?.PlacementSources??PlacementSourceSnapshot.Missing("PLACEMENT_SOURCE_NOT_CONFIGURED");
            Func<bool> current=()=>model.Lease.Current&&library?.Current==true&&source.Current&&(ownerCurrent==null||ownerCurrent());
            var sections=model.Sections;var candidates=new List<ToolCandidateDraft>();
            var poses=(production??new CollisionWorkPoseEvidence[0]).ToArray();
            foreach(var sheet in sections)foreach(string id in CompanyToolIds.All)
            {
                var r=new List<string>(source.Reasons);var found=source.Tools.Where(t=>string.Equals(t.Id,id,StringComparison.OrdinalIgnoreCase)).ToArray();
                var assembly=found.Length==1?found[0]:null;
                if(assembly==null)r.Add("MISSING_OR_AMBIGUOUS_MASTER_MAPPING");else r.AddRange(assembly.Reasons);
                if(!current())r.Add("STALE_SOURCE");
                if(sheet.Coverage==SheetCoverage.Unsupported2DEnvelope)r.Add("UNSUPPORTED_2D_ENVELOPE");
                if(assembly?.MasterDatums?.Status!=DatumEvidenceStatus.VerifiedRecord)r.Add("MISSING_DATUM_EVIDENCE");
                if(assembly?.Holder==null||assembly.ClampVerified!=true)r.Add("UNVERIFIED_HOLDER");
                if(assembly?.Holder!=null&&assembly.HolderObstacles?.GeometrySupportedForCollision!=true)r.Add("UNSUPPORTED_HOLDER_GEOMETRY");
                var rules=source.LimitRules.Where(rule=>rule.ToolIds.Contains(id,StringComparer.OrdinalIgnoreCase)).ToArray();
                if(rules.Length==0||rules.Any(rule=>!rule.FamilyMappingVerified))r.Add("UNMAPPED_LIMIT_RULE");
                r.Add("UNKNOWN_BEFORE_DURING_AFTER_STATES");
                if(model.SimulationOnly)r.Add("SIMULATION_ONLY");
                PlacementTransform2D transform=null,holderTransform=null;bool sim=simulation!=null&&string.Equals(simulation.ToolId,id,StringComparison.OrdinalIgnoreCase);
                bool collisionPose=false;CollisionWorkPoseEvidence approvedPose=null;
                if(sim&&assembly?.Punch!=null&&assembly.Holder!=null&&assembly.MappingIdentified)
                {
                    r.Add("SIMULATION_ONLY");var tip=simulation.TipMm;var direction=simulation.WorkingDirection;
                    bool frame=simulation.SourceFrameId==assembly.Punch.FrameId;
                    if(!frame&&simulation.Registration!=null&&simulation.Registration.FromFrame==simulation.SourceFrameId&&simulation.Registration.ToFrame==assembly.Punch.FrameId)
                    {tip=simulation.Registration.Transform.Apply(tip);direction=simulation.Registration.Transform.Direction(direction);frame=true;}
                    if(frame&&assembly.Punch.FrameId==assembly.Holder.FrameId)
                    {
                        transform=PlacementTransform2D.Align(tip,direction,simulation.TargetContactMm,simulation.TargetApproach,simulation.Mirror);
                        holderTransform=transform;
                        collisionPose=assembly.MasterDatums?.Status==DatumEvidenceStatus.VerifiedRecord && assembly.MasterDatums.TipPointMm.HasValue && assembly.MasterDatums.WorkingDirection.HasValue &&
                            ToolPoint.Distance(tip,assembly.MasterDatums.TipPointMm.Value)<=1e-8 && ToolPoint.Distance(direction,assembly.MasterDatums.WorkingDirection.Value)<=1e-10;
                        if(assembly.SeatingEvidence.CoordinateRecordReady)
                        {
                            // A reviewed relative pose is separate from the
                            // paper-space placement of the Master instances.
                            if(ToolPoint.Distance(tip,assembly.SeatingEvidence.TipPointMm.Value)<=1e-8&&ToolPoint.Distance(direction,assembly.SeatingEvidence.WorkingDirection.Value)<=1e-10)
                                holderTransform=transform.Then(assembly.SeatingEvidence.HolderSourceToPunchSource);
                            else{holderTransform=null;r.Add("SIMULATION_DATUM_DOES_NOT_MATCH_SEATING_RECORD");}
                        }
                    }
                    else r.Add("UNVERIFIED_FRAME_REGISTRATION");
                }
                else
                {
                    var matches=poses.Where(p=>p!=null&&p.ToolId==id&&p.FeatureId==sheet.FeatureId).ToArray();
                    if(matches.Length==1&&matches[0].AppliesTo(model,library,sheet,assembly))
                    { approvedPose=matches[0];transform=approvedPose.PunchToWork;holderTransform=approvedPose.HolderToWork;collisionPose=true; }
                    else
                    { r.Add(matches.Length>1?"DUPLICATE_PRODUCTION_COLLISION_POSE":"MISSING_WORK_CONTACT_AND_APPROACH_EVIDENCE"); if(matches.Length==1)r.AddRange(matches[0].Reasons); }
                    var dimensioned=library?.Tools.SingleOrDefault(t=>string.Equals(t.Id,id,StringComparison.OrdinalIgnoreCase));
                    if(dimensioned?.Datums?.SourceSha256!=null&&assembly?.MasterSha256!=null&&dimensioned.Datums.SourceSha256!=assembly.MasterSha256)
                        r.Add("UNVERIFIED_FRAME_REGISTRATION");
                }
                candidates.Add(new ToolCandidateDraft(id,sheet.FeatureId,assembly,sheet,new ToolGeometryPlacement2D(assembly?.Punch,transform,sheet.Frame?.FrameId),
                    new ToolGeometryPlacement2D(assembly?.Holder,holderTransform,sheet.Frame?.FrameId),rules,r,sim||model.SimulationOnly||assembly?.SeatingEvidence.SimulationOnly==true||assembly?.HolderObstacles?.Review.SimulationOnly==true,
                    ()=>current()&&(approvedPose==null||approvedPose.Current),collisionPose,approvedPose,evaluateLegacyCollision));
            }
            return new PlacementDraft(model.SnapshotId,model.UpdateStamp,model.Configuration,sections,candidates,current);
        }
        internal static string Diagnostic(PlacementDraft draft)=>new JavaScriptSerializer().Serialize(new {
            draft.Status,draft.Current,BendCount=draft.Sections.Count,CandidateCount=draft.Candidates.Count,
            ReadyCount=draft.Candidates.Count(c=>c.ReadyForCollisionCheck),SimulationCount=draft.Candidates.Count(c=>c.SimulationOnly),
            GeometryCheckReadyCount=draft.Candidates.Count(c=>c.GeometryCheckReady),
            CollisionPolicy=CollisionPolicy2D.Version,
            CollisionCounts=new {Clear=draft.CollisionResults.Count(c=>c.CombinedResult==CollisionStatus.CLEAR),Collision=draft.CollisionResults.Count(c=>c.CombinedResult==CollisionStatus.COLLISION),Unknown=draft.CollisionResults.Count(c=>c.CombinedResult==CollisionStatus.UNKNOWN)},
            Tools=CompanyToolIds.All.Select(id=>new {Id=id,Count=draft.Candidates.Count(c=>c.ToolId==id),
                Reasons=draft.Candidates.Where(c=>c.ToolId==id).SelectMany(c=>c.Reasons).Distinct().OrderBy(s=>s).ToArray()}) });
    }
}
