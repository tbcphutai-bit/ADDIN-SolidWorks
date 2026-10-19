using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using ADDIN.Commands.BendAssembly;
using ADDIN.Commands.BendCollision;
using ADDIN.Commands.BendEnvelope;
using ADDIN.Commands.BendPlacement;
using ADDIN.Commands.BendPlanning;
using ADDIN.Commands.BendProcess;
using ADDIN.Commands.BendTools;

internal static partial class ToolProfileTests
{
    private sealed class PackageFixture : IDisposable
    {
        internal ReviewFixture Review;
        internal PlacementModelCapture Model;
        internal ToolProfileLibrarySnapshot Library;
        internal ProcessPackageRecord Package;
        internal string Path, NativePath, ConfigurationPath;
        internal void Save()=>File.WriteAllText(Path,Json.Serialize(Package));
        internal ProcessProgramImport Read()=>ProcessPackageImporter.Read(Path,"PART",Model,Library,Review.Verifier,CancellationToken.None);
        internal void Configure()
        {
            Save();ConfigurationPath=System.IO.Path.Combine(Review.Root,"provider.json");NativePath=System.IO.Path.Combine(Review.Root,"CONTROLLED-IDENTITY-NOT-CAD.txt");
            File.WriteAllText(NativePath,"CONTROLLED SOFTWARE FIXTURE NATIVE IDENTITY ONLY; NOT CAD");
            File.WriteAllText(ConfigurationPath,Json.Serialize(new ProcessProviderConfiguration{Version=1,TrustedAuthoritiesPath="trust.json",
                AuthoritySha256=PlacementSourceIo.Hash(File.ReadAllBytes(Review.TrustPath)),Sources=new List<ProcessProviderSource>{new ProcessProviderSource{
                    NativePath=NativePath,Configuration=Model.Configuration,PackagePath=System.IO.Path.GetFileName(Path),ExpectedRevision=Package.Revision,
                    PackageSha256=PlacementSourceIo.Hash(File.ReadAllBytes(Path))}}}));
        }
        public void Dispose()=>Review.Dispose();
    }
    private static double[] XY(ToolPoint p)=>new[]{p.X,p.Y};
    private static double[] XYZ(PlacementVector3 p)=>new[]{p.X,p.Y,p.Z};
    private static ProcessMotionRecord Law(ProcessMotionLaw p)=>new ProcessMotionRecord{PivotMm=XY(p.PivotMm),StartTranslationMm=XY(p.StartTranslationMm),
        EndTranslationMm=XY(p.EndTranslationMm),StartAngleRadians=p.StartAngle,EndAngleRadians=p.EndAngle,StartAxialMm=p.StartAxialMm,EndAxialMm=p.EndAxialMm,
        PositionErrorMm=p.PositionErrorMm,AngleErrorRadians=p.AngleErrorRadians,AxialErrorMm=p.AxialErrorMm};
    private static ProcessMaterialRecord ImportMaterial(CollisionMaterial2D m)=>new ProcessMaterialRecord{
        Contours=m.Contours.Select(c=>new ProcessContourRecord{Id=c.Id,UncertaintyMm=c.UncertaintyMm,Curves=c.Boundary.Select(e=>new ProcessCurveRecord{
            Kind=e.Kind,StartMm=XY(e.Start),EndMm=XY(e.End),CenterMm=XY(e.Center),RadiusMm=e.RadiusMm,SweepRadians=e.SweepRadians}).ToList()}).ToList(),
        Regions=m.Components.Select(c=>new ProcessMaterialRegionRecord{OuterId=c.Outer.Id,HoleIds=c.Holes.Select(h=>h.Id).ToArray()}).ToList()};
    private static ProcessPrismRecord ImportPrism(string id,CollisionMaterial2D material,AxialInterval domain)=>new ProcessPrismRecord{
        Id=id,SourceArtifactId="GEOMETRY",SectionContract="ExplicitAnalyticPrismOverEntireAxialInterval",StartMm=domain.StartMm,EndMm=domain.EndMm,
        ToleranceMm=0,Material=ImportMaterial(material)};
    private static PackageFixture ImportedPackage(int bends=2)
    {
        var original=SequenceExample(bends:bends);var f=new PackageFixture{Review=Reviews(),Library=original.Library};
        f.Review.Authority.Purposes=f.Review.Authority.Purposes.Concat(new[]{"WholeSheetProcessRegistration","BendProcessPlan","SimulationProcessTool",
            "PartSequenceSpace","AssemblyPartLocalProgramUse"}).ToArray();f.Review.Reload();
        f.Model=new PlacementModelCapture("CONTROLLED_IMPORTED_NATIVE_IDENTITY",1,original.Model.Configuration,original.Model.Bodies,
            original.Model.Bends.Select(b=>new PlacementBendSnapshot(b.FeatureId,b.BodyId,b.Frame,b.ThicknessMm,b.BendMinMm,b.BendMaxMm,
                Convert.ToBase64String(Encoding.UTF8.GetBytes("CONTROLLED-PERSISTENT-"+b.FeatureId)))),true,true);
        var frame=f.Model.Bends[0].Frame;
        f.Path=System.IO.Path.Combine(f.Review.Root,"process.json");
        var seed=original.Space.Transitions[0].Plan.Segments[0].Parts[0].Source;
        f.Package=new ProcessPackageRecord{Version=1,Revision="CONTROLLED-REV1",Configuration=f.Model.Configuration,ModelSnapshotId=f.Model.SnapshotId,ModelUpdateStamp=f.Model.UpdateStamp,
            NativeGeometrySha256=PartSequenceSpace.TargetGeometryHash(f.Model),Bends=f.Model.Bends.Select(b=>new ProcessNativeBendRecord{
                FeatureId=b.FeatureId,PersistentId=b.PersistentId,NativeFrameId=b.Frame.FrameId}).ToList(),InitialStateId="S",DeclaredAlternativesComplete=true,SpecificationArtifactId="PROCESS",
            Artifacts=new List<ProcessArtifactRecord>(),Geometry=new List<ProcessPrismRecord>{ImportPrism("SHEET",seed.Slice(seed.Domain.MidMm).Material,seed.Domain)},
            Registration=new ProcessRegistrationRecord{FrameId=frame.FrameId,OriginWorldMm=XYZ(frame.OriginMm),X=XYZ(frame.X),Y=XYZ(frame.Y),BendAxis=XYZ(frame.BendAxis),ErrorMm=0,SourceArtifactId="GEOMETRY"},
            States=original.Space.States.Select(s=>new ProcessStateRecord{Id=s.Id,CompletedFeatureIds=s.CompletedFeatureIds.ToArray(),GeometryPoseSha256=ProcessHash("UNREVIEWED_DRAFT_STATE")}).ToList(),
            Transitions=new List<ProcessTransitionRecord>()};
        foreach(var role in new[]{Tuple.Create("GEOMETRY","RegisteredGeometry"),Tuple.Create("PROCESS","ProcessSpecification"),
            Tuple.Create("SETUP","SetupConditions"),Tuple.Create("CONSTRAINTS","MachineConstraints")})
        {
            string name=role.Item1+".txt";File.WriteAllText(System.IO.Path.Combine(f.Review.Root,name),"CONTROLLED SIMULATION ONLY: "+role.Item2);
            f.Package.Artifacts.Add(new ProcessArtifactRecord{Id=role.Item1,Role=role.Item2,Path=name,Sha256=PlacementSourceIo.Hash(File.ReadAllBytes(System.IO.Path.Combine(f.Review.Root,name)))});
        }
        f.Package.Registration.Review=f.Review.Sign("WholeSheetProcessRegistration",ProcessPackageImporter.RegistrationPayload(f.Package,f.Model));
        foreach(var edge in original.Space.Transitions)
        {
            var b=f.Model.Bends.Single(x=>x.FeatureId==edge.FeatureId);
            Func<ProcessToolPose,string,ProcessToolRecord> tool=(t,id)=>new ProcessToolRecord{Motion=Law(t.Motion),SimulationGeometry=ImportPrism(id,t.Source.Material,t.Source.Segments.Single())};
            f.Package.Transitions.Add(new ProcessTransitionRecord{Id=edge.Id,ToolId=edge.ToolId,FeatureId=b.FeatureId,PersistentId=b.PersistentId,
                BeforeStateId=edge.BeforeStateId,AfterStateId=edge.AfterStateId,SetupArtifactId="SETUP",ProcessArtifactId="PROCESS",Segments=edge.Plan.Segments.Select(s=>
                    new ProcessSegmentRecord{Id=s.Id,Phase=s.Phase.ToString(),ConstraintsArtifactId="CONSTRAINTS",Panels=s.Parts.Select(p=>new ProcessPanelRecord{
                        Id=p.Id,GeometryId="SHEET",Role=p.Role.ToString(),Fidelity=p.Fidelity.ToString(),Motion=Law(p.Motion)}).ToList(),Punch=tool(s.Punch,"PUNCH"),Holder=tool(s.Holder,"HOLDER")}).ToList()});
        }
        // Authoring pass exposes exact immutable payload hashes only. Nothing
        // signs in production; this private fixture owns a simulation-only key.
        f.Save();var draft=f.Read();Check(draft.Sequence.Transitions.Count==f.Package.Transitions.Count,string.Join(";",draft.Reasons));
        foreach(var transition in f.Package.Transitions)
        {
            transition.Review=f.Review.Sign("BendProcessPlan",draft.ReviewPayloads[transition.Id]);
            foreach(var s in transition.Segments)
            {
                s.Punch.SimulationReview=f.Review.Sign("SimulationProcessTool",draft.ReviewPayloads[transition.Id+"/WorkpiecePunch/PUNCH"]);
                s.Holder.SimulationReview=f.Review.Sign("SimulationProcessTool",draft.ReviewPayloads[transition.Id+"/WorkpieceHolder/HOLDER"]);
            }
        }
        foreach(var state in f.Package.States)
        {
            var before=draft.Sequence.Transitions.FirstOrDefault(t=>t.BeforeStateId==state.Id);
            var after=draft.Sequence.Transitions.FirstOrDefault(t=>t.AfterStateId==state.Id);
            state.GeometryPoseSha256=PartSequenceSpace.Boundary((before??after).Plan,before==null);
        }
        f.Save();draft=f.Read();f.Package.GraphReview=f.Review.Sign("PartSequenceSpace",draft.ReviewPayloads["PartSequenceSpace"]);
        f.Package.PartLocalUseReview=f.Review.Sign("AssemblyPartLocalProgramUse",draft.ReviewPayloads["AssemblyPartLocalProgramUse"]);
        f.Configure();return f;
    }
    private static void ProcessPackageTests()
    {
        Test("process import: signed file -> registered native IDs -> all 18 real numeric sequence candidates",()=>{
            using(var f=ImportedPackage())using(var planner=new PartSequencePlanner())
            {
                var imported=f.Read();Check(imported.Current&&imported.Sequence.Reasons.Count==0,string.Join(";",imported.Reasons));
                Check(imported.Plans.All(p=>p.InputReasons.Count==0),"Every supplied simulation plan is reviewed");
                var result=planner.Run("PART",f.Model,f.Library,imported.Sequence);
                Check(result.Tools.Count==18&&result.KernelQueries>0&&result.CommonToolIds.Count==17,"Actual imported search: queries="+result.KernelQueries+", common="+result.CommonToolIds.Count+", reasons="+string.Join(";",result.Tools.SelectMany(t=>t.Reasons).Distinct()));
                Check(result.Tools[0].Sequences.Any(s=>s.Status==CollisionStatus.COLLISION)&&result.Tools[0].Sequences.Any(s=>s.Status==CollisionStatus.CLEAR),"Different imported orders have witnessed different outcomes");
                Check(result.SimulationOnly&&result.EvidenceLevel==SequenceEvidenceLevel.SimulationOnly&&result.WinnerToolId==null,"File review never promotes a fixture to production");
                var preferred=result.PreferredCheckedSequence;
                Check(preferred!=null&&preferred.Status==CollisionStatus.CLEAR&&preferred.ToolTypeCount==1&&preferred.ToolChangeCount==0,
                    "Prefer one proven common tool with zero changes; never prefer the first colliding branch");
                Check(result.Tools.SelectMany(t=>t.Sequences).SelectMany(s=>s.Steps).All(s=>new[]{7,42}.Contains(s.FeatureId)),"Native IDs never replaced by display row numbers");
            }
        });
        Test("process import: source SHA revision persistent ID and registration failures remain UNKNOWN",()=>{
            using(var f=ImportedPackage())using(var planner=new PartSequencePlanner())
            {
                string original=Json.Serialize(f.Package);
                foreach(int kind in Enumerable.Range(0,6))
                {
                    f.Package=Json.Deserialize<ProcessPackageRecord>(original);
                    if(kind==0)f.Package.Bends[0].PersistentId="WRONG";
                    if(kind==1)f.Package.ModelUpdateStamp++;
                    if(kind==2)f.Package.NativeGeometrySha256=ProcessHash("OTHER_GEOMETRY");
                    if(kind==3)f.Package.Registration.FrameId="UNREVIEWED_FRAME";
                    if(kind==4)f.Package.Transitions[0].Segments[0].Panels[0].Motion.PositionErrorMm=null;
                    if(kind==5)f.Package.Geometry[0].Material.Contours[0].Curves[0].Kind="SPLINE";
                    f.Save();var imported=f.Read();var result=planner.Run("PART",f.Model,f.Library,imported.Sequence);
                    Check(imported.Reasons.Count>0&&result.CommonToolIds.Count==0&&result.Tools.Count==18&&result.PreferredCheckedSequence==null,"Invalid source never creates a selected tool");
                }
            }
        });
        Test("process import: missing geometry review and missing process state cannot be fabricated",()=>{
            using(var f=ImportedPackage())using(var planner=new PartSequencePlanner())
            {
                f.Package.Registration.Review=null;f.Save();var missing=f.Read();Check(missing.Reasons.Any(r=>r.StartsWith("UNVERIFIED_WHOLE_SHEET_PROCESS_REGISTRATION")),"Hash/coordinates alone cannot register native geometry");
                Check(planner.Run("PART",f.Model,f.Library,missing.Sequence).KernelQueries==0,"Missing registration blocks actual numeric proof");
            }
        });
        Test("process import: one unapproved simulation tool stays isolated and cannot receive physical authority",()=>{
            using(var f=ImportedPackage())using(var planner=new PartSequencePlanner())
            {
                foreach(var t in f.Package.Transitions.Where(t=>t.ToolId==CompanyToolIds.All[0]))foreach(var s in t.Segments)s.Punch.SimulationReview=null;
                f.Save();var data=f.Read();var result=planner.Run("PART",f.Model,f.Library,data.Sequence);
                Check(result.Tools[0].Decision==SequenceToolDecision.NeedsEvidence&&result.CommonToolIds.Count==16,"Valid siblings still execute; absent signed tool evidence never becomes CLEAR");
                Check(result.SimulationOnly&&result.ManufacturingStatus=="CHUA_XAC_MINH","No production result from imported simulations");
            }
        });
        Test("process provider: cold warm publication audit and changed artifact withdraw held proofs",()=>{
            using(var f=ImportedPackage())using(var provider=new ProcessProgramProvider(()=>f.ConfigurationPath))using(var planner=new PartSequencePlanner())
            {
                var a=provider.Read("PART",f.NativePath,f.Model,f.Library,CancellationToken.None);var b=provider.Read("PART",f.NativePath,f.Model,f.Library,CancellationToken.None);
                Check(ReferenceEquals(a,b)&&provider.Hits==1&&provider.Misses==1,"Unchanged verified inputs reuse one immutable import");
                var result=planner.Run("PART",f.Model,f.Library,a.Sequence);Check(result.CommonToolIds.Count==17,"Provider actually reaches numeric planner");
                File.AppendAllText(System.IO.Path.Combine(f.Review.Root,"GEOMETRY.txt")," CHANGED");provider.VerifyForPublicationAsync().GetAwaiter().GetResult();
                Check(!a.Current&&!result.Current&&result.CommonToolIds.Count==0&&result.PreferredCheckedSequence==null,"Managed audit invalidates held graph/geometry without worker COM");
                var changed=provider.Read("PART",f.NativePath,f.Model,f.Library,CancellationToken.None);Check(changed.Reasons.Any(r=>r.StartsWith("STALE_PROCESS_ARTIFACT_SHA256")),"Changed external source cannot reuse cached approval");
            }
        });
        Test("process provider: missing mapping duplicate config pin mismatch cancellation and dispose",()=>{
            using(var f=ImportedPackage())using(var provider=new ProcessProgramProvider(()=>f.ConfigurationPath))
            {
                Check(provider.Read("PART",f.NativePath+"OTHER",f.Model,f.Library,CancellationToken.None).Reasons.Contains("PROCESS_PACKAGE_NOT_MAPPED_FOR_PART_CONFIGURATION"),"Exact path/config required");
                using(var cts=new CancellationTokenSource()){cts.Cancel();bool cancelled=false;try{provider.Read("PART",f.NativePath,f.Model,f.Library,cts.Token);}catch(OperationCanceledException){cancelled=true;}Check(cancelled,"Cancellation remains cancellation");}
                var current=provider.Read("PART",f.NativePath,f.Model,f.Library,CancellationToken.None);provider.Dispose();Check(!current.Current,"Provider lifetime withdraws held imports");
            }
        });
        Test("process provider: duplicate mappings pinned revisions packages and authority changes are explicit",()=>{
            using(var f=ImportedPackage())using(var provider=new ProcessProgramProvider(()=>f.ConfigurationPath))
            {
                var good=provider.Read("PART",f.NativePath,f.Model,f.Library,CancellationToken.None);
                string original=File.ReadAllText(f.ConfigurationPath);
                foreach(int kind in Enumerable.Range(0,4))
                {
                    var config=Json.Deserialize<ProcessProviderConfiguration>(original);
                    if(kind==0)config.Sources.Add(config.Sources[0]);
                    if(kind==1)config.Sources[0].ExpectedRevision="UNREVIEWED-REV2";
                    if(kind==2)config.Sources[0].PackageSha256=ProcessHash("WRONG_PACKAGE");
                    if(kind==3)config.AuthoritySha256=ProcessHash("WRONG_AUTHORITY");
                    File.WriteAllText(f.ConfigurationPath,Json.Serialize(config));var bad=provider.Read("PART",f.NativePath,f.Model,f.Library,CancellationToken.None);
                    Check(bad.Reasons.Count>0&&bad.Sequence.Reasons.Count>0,"Bad source selection/pin remains a visible missing graph");
                    Check(!good.Current,"A configuration edit immediately withdraws the previous import");
                }
            }
        });
        Test("process import: traversal and explicit open or degenerate material are rejected",()=>{
            bool traversal=false;try{ProcessPackageImporter.SafePath(Folder(),"../outside.txt");}catch(InvalidDataException){traversal=true;}Check(traversal,"No artifact traversal");
            using(var f=ImportedPackage())
            {
                f.Package.Geometry[0].Material.Contours[0].Curves.RemoveAt(0);f.Save();var bad=f.Read();Check(bad.Reasons.Count>0&&bad.Sequence.Reasons.Count>0,"Open source is never repaired to close a material region");
            }
        });
        Test("process provider: a restored missing artifact is retried rather than negatively cached",()=>{
            using(var f=ImportedPackage())using(var provider=new ProcessProgramProvider(()=>f.ConfigurationPath))
            {
                string path=System.IO.Path.Combine(f.Review.Root,"GEOMETRY.txt"),data=File.ReadAllText(path);File.Delete(path);
                var bad=provider.Read("PART",f.NativePath,f.Model,f.Library,CancellationToken.None);
                Check(bad.Sequence.InputReasons.Count>0,"Missing artifact is an explicit failed import");
                File.WriteAllText(path,data);var good=provider.Read("PART",f.NativePath,f.Model,f.Library,CancellationToken.None);
                Check(good.Sequence.Reasons.Count==0&&!ReferenceEquals(good,bad),"Restored original artifact recovers only after a complete fresh audit");
            }
        });
        Test("process provider: malformed configuration path withdraws previously held evidence",()=>{
            using(var f=ImportedPackage())
            {
                string configured=f.ConfigurationPath;
                using(var provider=new ProcessProgramProvider(()=>configured))
                {
                    var good=provider.Read("PART",f.NativePath,f.Model,f.Library,CancellationToken.None);Check(good.Current,"Initial imported proof is current");
                    configured="\0";provider.VerifyForPublicationAsync().GetAwaiter().GetResult();Check(!good.Current,"Invalid path cannot leave old proof live by faulting the audit task");
                    Check(provider.Read("PART",f.NativePath,f.Model,f.Library,CancellationToken.None).Sequence.InputReasons.Count>0,"Invalid configuration remains an explicit missing graph");
                    configured=f.ConfigurationPath;var restored=provider.Read("PART",f.NativePath,f.Model,f.Library,CancellationToken.None);
                    Check(restored.Current&&!ReferenceEquals(restored,good),"Restoration requires fresh verified input rather than reviving an old lease");
                }
            }
        });
        Test("process import: explicit forming contact bindings are imported and malformed zones rejected",()=>{
            using(var f=ImportedPackage())
            {
                var artifact=f.Package.Artifacts[0];artifact.Role="RegisteredGeometry";
                File.WriteAllText(System.IO.Path.Combine(f.Review.Root,"CONTACT.txt"),"CONTROLLED CONTACT EVIDENCE ONLY");
                f.Package.Artifacts.Add(new ProcessArtifactRecord{Id="CONTACT",Role="FormingContact",Path="CONTACT.txt",Sha256=PlacementSourceIo.Hash(File.ReadAllBytes(System.IO.Path.Combine(f.Review.Root,"CONTACT.txt")))});
                var segment=f.Package.Transitions[0].Segments[0];
                segment.Contacts=new List<ProcessContactRecord>{new ProcessContactRecord{PartId=segment.Panels[0].Id,SheetGeometrySha256=ProcessHash("sheet"),ToolSetupSha256=ProcessHash("tool"),
                    EvidenceArtifactId="CONTACT",NormalIsX=true,SheetPositiveSide=true,PlaneMm=0,TangentMinMm=0,TangentMaxMm=1,AxialStartMm=0,AxialEndMm=1}};
                f.Save();var parsed=f.Read();Check(parsed.Plans.Count>0&&parsed.Plans[0].Segments[0].Contacts.Count==1,"The importer preserves the exact supplied contact constraint");
                Check(parsed.Plans[0].InputReasons.Count>0,"Editing a contact withdraws the old signed plan review");
                segment.Contacts[0].NormalIsX=null;f.Save();Check(f.Read().Sequence.InputReasons.Contains("MISSING_PROCESS_CONTACT_BINDING"),"An omitted normal is never silently defaulted");
            }
        });
    }
    internal static Tuple<PartPlanningResult,PlacementModelCapture> ImportedUiFixture()
    {
        if(temporary==null){temporary=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"ADDIN-ImportedUi-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temporary);}
        using(var f=ImportedPackage())using(var planner=new PartSequencePlanner())
        {var imported=f.Read();return Tuple.Create(planner.Run("PART",f.Model,f.Library,imported.Sequence),f.Model);}
    }
}
