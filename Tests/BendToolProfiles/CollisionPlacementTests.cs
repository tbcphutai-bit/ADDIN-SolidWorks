using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ADDIN.Commands.BendCollision;
using ADDIN.Commands.BendPlacement;
using ADDIN.Commands.BendTools;

internal static partial class ToolProfileTests
{
    private static ToolProfileLibrarySnapshot CollisionLibrary(SeatingFixture f)
    {
        var lib=Library(Read(f.Master));lib.PlacementSources=new PlacementSourceSnapshot("CONTROLLED_SYNTHETIC_18_SLOTS",
            new[]{f.Assembly}.Concat(lib.PlacementSources.Tools.Skip(1)),null,new string[0]);return lib;
    }
    private static PlacementSimulationRequest CollisionPose(SeatingFixture f,ToolPoint? tip=null,string frame=null) =>
        new PlacementSimulationRequest(f.Assembly.Id,frame??f.Assembly.Punch.FrameId,tip??P(1,2),P(0,-1),P(1,2),P(0,-1));
    private static PlacementModelCapture CollisionModel(CollisionContour2D contour,int bends=1,bool production=false)
    {
        var model=Model(Extrusion(contour.Boundary),bends);
        return production?new PlacementModelCapture(model.SnapshotId,model.UpdateStamp,model.Configuration,model.Bodies,model.Bends,true,false):model;
    }
    private static CandidateCollisionResult FirstCollision(PlacementDraft draft) => draft.CollisionResults[0];
    private static CollisionMaterial2D MaterialFromHolder(HolderObstacleSet set)
    {
        var placement=new HolderObstaclePlacement2D(set,new PlacementTransform2D(),"SYNTHETIC_WORK_MM");
        var contours=placement.Contours.ToDictionary(c=>c.Id,c=>new CollisionContour2D(c.Id,c.NominalBoundary,c.BoundaryBandRadiusMm));
        return new CollisionMaterial2D(placement.WorkFrameId,placement.MaterialRegions.Select(r=>new CollisionComponent2D(contours[r.OuterId],r.HoleIds.Select(id=>contours[id]))),
            "CONTROLLED_SYNTHETIC_HOLDER_WITH_REAL_BACKEND_BANDS",true,()=>set.Current,true);
    }
    private static void CollisionPlacementTests()
    {
        Test("collision adapter: all 18 synthetic approved pairs execute without a selected tool",()=> {
            // Software-only external-contract fixture. The 18 IDs identify
            // slots; these triangles/rectangles are NOT company CAD or real
            // company approval records. No approvals leave the temp harness.
            var fixtures=new List<SeatingFixture>();
            try
            {
                for(int i=0;i<18;i++)fixtures.Add(Seating(false,true,i));
                var lib=new ToolProfileLibrarySnapshot {Tools=CompanyToolIds.All.Select(id=>new CompanyToolProfile {Id=id,Parts=new ToolGeometryPart[0],Validation=new ToolProfileValidationResult()}).ToArray(),
                    PlacementSources=new PlacementSourceSnapshot("CONTROLLED_SYNTHETIC_ALL_18",fixtures.Select(f=>f.Assembly),null,new string[0])};
                var model=CollisionModel(Rect("SHEET",1,.2,.5,.5),3,production:true);var evidence=new List<CollisionWorkPoseEvidence>();
                for(int i=0;i<fixtures.Count;i++)
                {
                    var f=fixtures[i];f.Reviews.Authority.Purposes=f.Reviews.Authority.Purposes.Concat(new[]{"CollisionWorkPose"}).ToArray();f.Reviews.Reload();
                    foreach(var section in model.Sections)
                    {
                        var contact=i%3==0?P(1,2):i%3==1?P(-1,-3):P(21,22);
                        var r=new CollisionWorkPoseRecord {ToolId=f.Assembly.Id,FeatureId=section.FeatureId,ModelSnapshotId=model.SnapshotId,ModelUpdateStamp=model.UpdateStamp,
                            Configuration=model.Configuration,SheetGeometrySha256=CollisionWorkPoseEvidence.SheetHash(section),WorkFrameId=section.Frame.FrameId,
                            MasterSourceSha256=f.Assembly.MasterSha256,PairSeatingPayloadSha256=f.Assembly.SeatingEvidence.PayloadSha256,StateId=BendStateCoverage.FinalModelObserved.ToString(),
                            ContactPointMm=new PairPointRecord {X=contact.X,Y=contact.Y},ApproachDirection=new PairPointRecord {X=0,Y=-1}};
                        r.Review=f.Reviews.Sign("CollisionWorkPose",CollisionWorkPoseEvidence.Payload(r),false);
                        var e=CollisionWorkPoseEvidence.Read(r,model,lib,f.Reviews.Verifier,f.Reviews.Root,CancellationToken.None);
                        Check(e.Approved,"Every controlled synthetic setup contract validates");evidence.Add(e);
                    }
                }
                var result=BendPlacementBuilder.BuildWithProductionPlacements(model,lib,evidence);
                Check(result.CollisionResults.Count==54,"18 tools times 3 bends, no selected tool");
                foreach(var group in result.CollisionResults.GroupBy(c=>c.FeatureId))
                {
                    Check(group.Count(c=>c.PunchResult.Status==CollisionStatus.COLLISION)==6,"Six different synthetic punch poses collide");
                    Check(group.Count(c=>c.HolderResult.Status==CollisionStatus.COLLISION)==6,"Six different synthetic holder poses collide");
                    Check(group.Count(c=>c.CombinedResult==CollisionStatus.CLEAR)==6&&group.Count(c=>c.CombinedResult==CollisionStatus.COLLISION)==12,"Automatic all-tool geometry yields nontrivial outcomes");
                }
                Check(result.WinnerToolId==null&&result.CollisionResults.All(c=>c.ManufacturingStatus=="CHUA_XAC_MINH"),"No manufactured company verdict or winner");
            }
            finally {foreach(var f in fixtures)f.Dispose();}
        });
        Test("collision adapter: 3 and 6 bends retain 54 and 108 complete independent result rows",()=> {
            using(var f=Seating())foreach(int bends in new[]{3,6})
            {
                var model=CollisionModel(Rect("SHEET",20,20,1,1),bends);var draft=BendPlacementBuilder.Build(model,CollisionLibrary(f),simulation:CollisionPose(f));
                Check(draft.CollisionResults.Count==18*bends,"All slots including unsupported tools");
                foreach(var group in draft.CollisionResults.GroupBy(r=>r.FeatureId))
                {
                    Check(group.Count()==18&&group.Select(c=>c.ToolId).SequenceEqual(CompanyToolIds.All),"Fixed 18-ID order per bend");
                    var ready=group.First();Expect(ready.PunchResult,CollisionStatus.CLEAR);Expect(ready.HolderResult,CollisionStatus.CLEAR);
                    Check(ready.CombinedResult==CollisionStatus.CLEAR&&ready.AllRequiredStatesResult==CollisionStatus.UNKNOWN,"One clear pose never proves the process");
                    Check(ready.States.Count==4&&ready.States.Where(s=>s.State!=BendStateCoverage.FinalModelObserved).All(s=>s.CombinedResult==CollisionStatus.UNKNOWN),"Unknown before/during/after states retained");
                    Check(group.Skip(1).All(c=>c.CombinedResult==CollisionStatus.UNKNOWN),"17 missing slots are not dropped or invented");
                }
                Check(draft.WinnerToolId==null&&draft.CollisionResults.All(r=>r.ManufacturingStatus=="CHUA_XAC_MINH"&&!r.SequenceVerified),"No manufacturing winner or PASS");
            }
        });
        Test("collision adapter: punch and holder detect different body collisions independently",()=> {
            using(var f=Seating())
            {
                var punch=FirstCollision(BendPlacementBuilder.Build(CollisionModel(Rect("SHEET",1,.2,.5,.5)),CollisionLibrary(f),simulation:CollisionPose(f)));
                Expect(punch.PunchResult,CollisionStatus.COLLISION);Expect(punch.HolderResult,CollisionStatus.CLEAR);Check(punch.CombinedResult==CollisionStatus.COLLISION,"Punch body hit");
                var holder=FirstCollision(BendPlacementBuilder.Build(CollisionModel(Rect("SHEET",3,6,.5,.5)),CollisionLibrary(f),simulation:CollisionPose(f)));
                Expect(holder.PunchResult,CollisionStatus.CLEAR);Expect(holder.HolderResult,CollisionStatus.COLLISION);Check(holder.CombinedResult==CollisionStatus.COLLISION,"Holder body hit");
                Check(holder.HolderResult.Witness.PointMm.X>2,"Reviewed relative holder seating (+2 mm in this synthetic fixture) was applied");
            }
        });
        Test("collision adapter: certain punch collision is retained when holder evidence becomes unknown",()=> {
            using(var f=Seating())
            {
                f.Assembly.HolderObstacles.Invalidate();var result=FirstCollision(BendPlacementBuilder.Build(CollisionModel(Rect("SHEET",1,.2,.5,.5)),CollisionLibrary(f),simulation:CollisionPose(f)));
                Expect(result.PunchResult,CollisionStatus.COLLISION);Expect(result.HolderResult,CollisionStatus.UNKNOWN);
                Check(result.CombinedResult==CollisionStatus.COLLISION,"Independent strong evidence is not erased by an unknown second pair");
            }
        });
        Test("collision adapter: missing manual pose wrong source frame and wrong datum are UNKNOWN",()=> {
            using(var f=Seating())
            {
                var model=CollisionModel(Rect("SHEET",20,20,1,1));var lib=CollisionLibrary(f);
                foreach(var pose in new PlacementSimulationRequest[]{null,CollisionPose(f,frame:"WRONG_FRAME"),CollisionPose(f,tip:P(2,2))})
                {
                    var c=FirstCollision(BendPlacementBuilder.Build(model,lib,simulation:pose));Expect(c.PunchResult,CollisionStatus.UNKNOWN);Expect(c.HolderResult,CollisionStatus.UNKNOWN);
                }
            }
        });
        Test("collision adapter: unsupported whole-sheet envelope and source-open punch stay UNKNOWN",()=> {
            using(var f=Seating())
            {
                var lib=CollisionLibrary(f);var incomplete=Model(Extrusion(Square().Curves),complete:false);
                var c=FirstCollision(BendPlacementBuilder.Build(incomplete,lib,simulation:CollisionPose(f)));Expect(c.PunchResult,CollisionStatus.UNKNOWN);
                Check(c.PunchResult.Reasons.Contains("UNSUPPORTED_2D_ENVELOPE_MULTIPLE_SLICES_OR_3D_REQUIRED"),"No partial Min silhouette used as a whole sheet");
                var a=f.Assembly;var open=Entity(a.Punch.Curves,false);open.Handle="EXPLICIT_SOURCE_OPEN";open.InstancePath="SYNTHETIC";open.Transform=new ToolTransform();
                var p=new GeometrySelection2D(a.MasterSha256,a.Punch.GeometrySha256,new[]{open},false,CancellationToken.None);
                lib.PlacementSources=new PlacementSourceSnapshot("OPEN_SYNTHETIC_SOURCE",new[]{new ToolAssemblyProfile(a.Id,a.MasterSha256,a.ManifestSha256,p,a.Holder,a.MasterDatums,true,new string[0],a.HolderObstacles,a.SeatingEvidence,a.MappingReview)}.Concat(lib.PlacementSources.Tools.Skip(1)),null,new string[0]);
                c=FirstCollision(BendPlacementBuilder.Build(CollisionModel(Rect("SHEET",20,20,1,1)),lib,simulation:CollisionPose(f)));
                Expect(c.PunchResult,CollisionStatus.UNKNOWN);Check(c.PunchResult.Reasons.Contains("OPEN_UNSUPPORTED_OR_INVALID_PUNCH_REGION"),"Open source flag not repaired");
            }
        });
        Test("collision adapter: source and model leases invalidate held collision certificates",()=> {
            using(var f=Seating())
            {
                var model=CollisionModel(Rect("SHEET",20,20,1,1));var lib=CollisionLibrary(f);var c=FirstCollision(BendPlacementBuilder.Build(model,lib,simulation:CollisionPose(f)));
                Expect(c.PunchResult,CollisionStatus.CLEAR);lib.Invalidate();Expect(c.PunchResult,CollisionStatus.UNKNOWN);Expect(c.HolderResult,CollisionStatus.UNKNOWN);
            }
            using(var f=Seating())
            {
                var model=CollisionModel(Rect("SHEET",1,.2,.5,.5));var c=FirstCollision(BendPlacementBuilder.Build(model,CollisionLibrary(f),simulation:CollisionPose(f)));
                Expect(c.PunchResult,CollisionStatus.COLLISION);model.Lease.Invalidate();Expect(c.PunchResult,CollisionStatus.UNKNOWN);
            }
        });
        Test("collision adapter: actual bounded SPLINE backend supports all three outcomes without dropping chords",()=> {
            using(var f=Reviews())
            {
                var s=Spline(2,new[]{P(0,0),P(5,10),P(10,0)},new[]{0.0,0,0,1,1,1});
                var p=HolderSelection(Square());var h=HolderSelection(new ToolDxfEntity {Type="SPLINE",Spline=s,Curves=new ToolCurve[0],UnsupportedReason="SPLINE backend"},Entity(new[]{Segment(10,0,0,0)}));
                var d=MaterialDefinition(p,h,"Solid","Solid");d.Contours.RemoveAt(1);d.Contours[0].EntityKeys=h.EntityKeys.ToArray();d.Review=f.Sign("HolderMaterialRule",HolderObstacleSet.Payload(d));
                var set=ReadMaterial(f,p,h,d);Check(set.GeometrySupportedForCollision&&h.Curves.Count==1,"Raw holder has only closing line, complete material has bounded chords");
                var material=MaterialFromHolder(set);Check(material.Contours[0].Boundary.Count>10&&material.Contours[0].UncertaintyMm>0,"Complete envelope consumed");
                Expect(Query(Material(Rect("P",4,6,2,1)),material),CollisionStatus.CLEAR);
                Expect(Query(Material(Rect("P",4,1,1,1)),material),CollisionStatus.COLLISION);
                Expect(Query(Material(Rect("P",4.9999,5.00005,.0002,.1)),material),CollisionStatus.UNKNOWN);
            }
        });
        Test("collision combination: COLLISION dominates UNKNOWN and CLEAR requires both pairs",()=> {
            foreach(CollisionStatus a in Enum.GetValues(typeof(CollisionStatus)))foreach(CollisionStatus b in Enum.GetValues(typeof(CollisionStatus)))
            {
                var result=BendStateCollisionResult.Combine(a,b);
                Check(result==(a==CollisionStatus.COLLISION||b==CollisionStatus.COLLISION?CollisionStatus.COLLISION:a==CollisionStatus.CLEAR&&b==CollisionStatus.CLEAR?CollisionStatus.CLEAR:CollisionStatus.UNKNOWN),"Three-valued independent combination");
            }
        });
        Test("collision production entry: external work pose binds exact model sheet frame and physical pair",()=> {
            using(var f=Seating(false,true))
            {
                f.Reviews.Authority.Purposes=f.Reviews.Authority.Purposes.Concat(new[]{"CollisionWorkPose"}).ToArray();f.Reviews.Reload();
                var model=CollisionModel(Rect("SHEET",20,20,1,1),production:true);var lib=CollisionLibrary(f);var section=model.Sections[0];
                var r=new CollisionWorkPoseRecord {ToolId=f.Assembly.Id,FeatureId=section.FeatureId,ModelSnapshotId=model.SnapshotId,ModelUpdateStamp=model.UpdateStamp,
                    Configuration=model.Configuration,SheetGeometrySha256=CollisionWorkPoseEvidence.SheetHash(section),WorkFrameId=section.Frame.FrameId,
                    MasterSourceSha256=f.Assembly.MasterSha256,PairSeatingPayloadSha256=f.Assembly.SeatingEvidence.PayloadSha256,StateId=BendStateCoverage.FinalModelObserved.ToString(),
                    ContactPointMm=new PairPointRecord {X=1,Y=2},ApproachDirection=new PairPointRecord {X=0,Y=-1}};
                r.Review=f.Reviews.Sign("CollisionWorkPose",CollisionWorkPoseEvidence.Payload(r),false);
                var approved=CollisionWorkPoseEvidence.Read(r,model,lib,f.Reviews.Verifier,f.Reviews.Root,CancellationToken.None);
                Check(approved.Approved,"Synthetic test of external production contract only; no actual company attestation");
                var c=FirstCollision(BendPlacementBuilder.BuildWithProductionPlacements(model,lib,new[]{approved}));
                Expect(c.PunchResult,CollisionStatus.CLEAR);Expect(c.HolderResult,CollisionStatus.CLEAR);Check(!c.SimulationOnly&&c.AllRequiredStatesResult==CollisionStatus.UNKNOWN,"External pose does not certify a sequence");
                var different=CollisionModel(Rect("OTHER_SHEET",1,.2,.5,.5),production:true);
                Expect(FirstCollision(BendPlacementBuilder.BuildWithProductionPlacements(different,lib,new[]{approved})).PunchResult,CollisionStatus.UNKNOWN);
                r.WorkFrameId="WRONG_FRAME";r.Review=f.Reviews.Sign("CollisionWorkPose",CollisionWorkPoseEvidence.Payload(r),false);
                Check(!CollisionWorkPoseEvidence.Read(r,model,lib,f.Reviews.Verifier,f.Reviews.Root,CancellationToken.None).Approved,"A signed mismatched frame cannot override actual model");
                f.Assembly.HolderObstacles.Invalidate();Expect(c.PunchResult,CollisionStatus.UNKNOWN);Expect(c.HolderResult,CollisionStatus.UNKNOWN);
            }
        });
    }
}
