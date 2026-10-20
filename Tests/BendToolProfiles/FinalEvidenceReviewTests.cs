using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using ADDIN.Commands.BendPlacement;
using ADDIN.Commands.BendTools;
using ADDIN.Commands.BendProcess;
using ADDIN.Commands.BendCollision;
using ADDIN.Commands.BendEnvelope;
using ADDIN.Commands.BendPlanning;

internal static partial class ToolProfileTests
{
    private static HolderMaterialDefinition ExplicitDraftRoles(HolderMaterialReviewDraft d)
    {
        string solid=d.Rows.Single(r=>r.Handle=="H").EntityKey;
        return HolderMaterialReviewDraftBuilder.PrepareUnsigned(d,new[]{new HolderContourDefinition {Id="EXPLICIT_TEST_SOLID",MaterialKind="Solid",EntityKeys=new[]{solid}}},
            d.Rows.Where(r=>r.EntityKey!=solid).Select(r=>new HolderExcludedEntity {EntityKey=r.EntityKey,Role="Reference"}),null);
    }
    private static void ReviewReject(Action action,string why)
    {bool rejected=false;try{action();}catch(Exception ex) when(ex is InvalidDataException||ex is InvalidOperationException){rejected=true;}Check(rejected,why);}
    private static void FinalEvidenceReviewTests()
    {
        Test("final review: inventory all source entities without inferred roles",()=>{
            var lib=CadPairUiLibrary();var p=lib.PlacementSources.Tools[0];var d=HolderMaterialReviewDraftBuilder.Create(p,CancellationToken.None,()=>lib.Current);
            Check(d.FixedPairMatched&&d.Rows.Count==p.Holder.SourceEntities.Count,"Complete unchanged selection");
            Check(d.Rows.All(r=>r.ProposedRole=="NeedsReview")&&!d.MaterialReviewed,"No automatic material classification");
            Check(d.Rows.Select(r=>r.EntityKey).SequenceEqual(p.Holder.EntityKeys),"Original instance/key order");
            foreach(var row in d.Rows){var e=p.Holder.SourceEntities.Single(x=>HolderEntityGeometry.Key(x)==row.EntityKey);
                Check(ReferenceEquals(e,row.Source)&&row.Blocks.SequenceEqual(e.Blocks)&&row.InsertHandles.SequenceEqual(e.InsertHandles),"Raw geometry and INSERT lineage retained");}
        });
        Test("final review: unsigned roles do not confer collision readiness",()=>{
            var p=CadPairUiLibrary().PlacementSources.Tools[0];var d=HolderMaterialReviewDraftBuilder.Create(p,CancellationToken.None);var m=ExplicitDraftRoles(d);
            Check(m.Review==null,"No private key/signature is generated");
            var read=HolderObstacleSet.Read(p.Id,p.Punch,p.Holder,p.ManifestSha256,m,ToolReviewVerifier.Load(null,CancellationToken.None),Folder(),CancellationToken.None);
            Check(!read.GeometrySupportedForCollision&&!p.ClampVerified,"Production interpreter still blocks unsigned material");
            m.Contours[0].EntityKeys[0]="EDITED";Check(d.Rows.All(r=>r.EntityKey!="EDITED"),"No mutation of source");
        });
        Test("final review: missing duplicate foreign roles and unknown exclusion rejected",()=>{
            var d=HolderMaterialReviewDraftBuilder.Create(CadPairUiLibrary().PlacementSources.Tools[0],CancellationToken.None);
            var m=ExplicitDraftRoles(d);
            ReviewReject(()=>HolderMaterialReviewDraftBuilder.PrepareUnsigned(d,m.Contours,new HolderExcludedEntity[0],null),"Incomplete roles");
            m.Excluded.Add(new HolderExcludedEntity {EntityKey=m.Contours[0].EntityKeys[0],Role="Reference"});
            ReviewReject(()=>HolderMaterialReviewDraftBuilder.PrepareUnsigned(d,m.Contours,m.Excluded,null),"Duplicate source assignment");
            m=ExplicitDraftRoles(d);m.Excluded[0].Role="AUTO_FILL";
            ReviewReject(()=>HolderMaterialReviewDraftBuilder.PrepareUnsigned(d,m.Contours,m.Excluded,null),"No invented exclusion role");
            m=ExplicitDraftRoles(d);m.Contours[0].EntityKeys[0]="FOREIGN";
            ReviewReject(()=>HolderMaterialReviewDraftBuilder.PrepareUnsigned(d,m.Contours,m.Excluded,null),"Wrong source key");
        });
        Test("final review: draft export/import source pins, no default pose or tool length",()=>{
            var d=HolderMaterialReviewDraftBuilder.Create(CadPairUiLibrary().PlacementSources.Tools[0],CancellationToken.None);
            var p=ToolEvidenceReviewWorkspace.Create(d);var copy=ToolEvidenceReviewWorkspace.Read(ToolEvidenceReviewWorkspace.Serialize(p),d);
            Check(copy.Seating.HolderSourceToPunchSource.A==null&&copy.Seating.TipPointMm.X==null&&copy.Datum.MountDatumMm.X==null,"Unknown is not zero/identity");
            Check(copy.Axial.Count==0&&copy.Simulation.Count==0&&copy.Material.Contours.Count==0,"No fake span or roles");
            ToolEvidenceReviewWorkspace.PrepareUnsigned(copy,d);
            copy.HolderInsert="SWAPPED";ReviewReject(()=>ToolEvidenceReviewWorkspace.Read(ToolEvidenceReviewWorkspace.Serialize(copy),d),"Same-block swapped INSERT rejected");
            copy=p;copy.MasterSha256=new string('a',64);ReviewReject(()=>ToolEvidenceReviewWorkspace.Read(ToolEvidenceReviewWorkspace.Serialize(copy),d),"Source revision rejected");
        });
        Test("final review: editor rejects signatures, physical assertions and child binding edits",()=>{
            var d=HolderMaterialReviewDraftBuilder.Create(CadPairUiLibrary().PlacementSources.Tools[0],CancellationToken.None);
            var p=ToolEvidenceReviewWorkspace.Create(d);p.Material.Review=new ToolReviewProof();
            ReviewReject(()=>ToolEvidenceReviewWorkspace.PrepareUnsigned(p,d),"Cannot self-sign");
            p=ToolEvidenceReviewWorkspace.Create(d);p.Seating.PhysicalInspectionConfirmed=true;
            ReviewReject(()=>ToolEvidenceReviewWorkspace.PrepareUnsigned(p,d),"Cannot self-attest physical inspection");
            p=ToolEvidenceReviewWorkspace.Create(d);p.Material.ToolId="WRONG";
            ReviewReject(()=>ToolEvidenceReviewWorkspace.PrepareUnsigned(p,d),"Child ID cannot be silently normalized");
            p=ToolEvidenceReviewWorkspace.Create(d);p.Mapping.HolderRootInsert="SWAP";
            ReviewReject(()=>ToolEvidenceReviewWorkspace.PrepareUnsigned(p,d),"Fixed mapping immutable");
        });
        Test("final review: actual verifier reports pending material/seating and native-bound inputs",()=>{
            var d=HolderMaterialReviewDraftBuilder.Create(CadPairUiLibrary().PlacementSources.Tools[0],CancellationToken.None);var p=ToolEvidenceReviewWorkspace.Create(d);
            var audit=ToolEvidenceReviewWorkspace.Audit(p,d,ToolReviewVerifier.Load(null,CancellationToken.None),Folder(),CancellationToken.None);
            Check(audit.Status=="UNKNOWN"&&!audit.MaterialReady&&!audit.CoordinatesReady&&!audit.PhysicalPairVerified,"Coordinates not certificates");
            Check(audit.Reasons.Contains("AXIAL_AND_PROCESS_REQUIRE_NATIVE_BOUND_IMPORT")&&audit.ManufacturingStatus=="CHUA_XAC_MINH","No production claim");
        });
        Test("final review: stale library withdraws draft, no export of held source",()=>{
            var lib=CadPairUiLibrary();var d=HolderMaterialReviewDraftBuilder.Create(lib.PlacementSources.Tools[0],CancellationToken.None,()=>lib.Current);
            var p=ToolEvidenceReviewWorkspace.Create(d);lib.Invalidate();
            Check(d.Status=="SOURCE_STALE"&&!d.MaterialReviewed,"Stale visible");
            ReviewReject(()=>ToolEvidenceReviewWorkspace.Read(ToolEvidenceReviewWorkspace.Serialize(p),d),"Stale source export blocked");
        });
        Test("final review: cancellation and unmatched pair preserve observations",()=>{
            var p=CadPairUiLibrary().PlacementSources.Tools[0];bool canceled=false;
            try{HolderMaterialReviewDraftBuilder.Create(p,new CancellationToken(true));}catch(OperationCanceledException){canceled=true;}Check(canceled,"Cancellation not UNKNOWN");
            var unmatched=new ToolAssemblyProfile(p.Id,p.MasterSha256,p.ManifestSha256,p.Punch,p.Holder,p.MasterDatums,false,new[]{"UNMATCHED"});
            var d=HolderMaterialReviewDraftBuilder.Create(unmatched,CancellationToken.None);
            Check(d.Rows.Count==p.Holder.EntityKeys.Count&&!d.FixedPairMatched,"Unmatched source not hidden");
            ReviewReject(()=>ExplicitDraftRoles(d),"No promotion of unmatched pair");
        });
        Test("final review: production adapter accepts format but never unsigned approval",()=>{
            var fixture=CadPairs();var original=PairProvider(fixture).Read(CancellationToken.None).PlacementSources.Tools[0];
            var d=HolderMaterialReviewDraftBuilder.Create(original,CancellationToken.None);var p=ToolEvidenceReviewWorkspace.Create(d);
            p.Revision="CONTROLLED-UNSIGNED-1";p.Material=ExplicitDraftRoles(d);
            // The real manifest label identity is explicit, not nearest-holder.
            p.Mapping.ExpectedLabel=fixture.Mapping.Tools[0].ExpectedLabel;p.Mapping.LabelHandle=fixture.Mapping.Tools[0].LabelHandle;
            string file=Path.Combine(fixture.Root,"workspace.json");File.WriteAllText(file,ToolEvidenceReviewWorkspace.Serialize(p));
            var read=BendMasterAssemblyReader.Read(fixture.Source,Path.Combine(fixture.Root,"pairs.json"),null,CancellationToken.None,null,null,null,file);
            var tool=read.Tools[0];
            Check(read.Reasons.All(r=>r!="INVALID_REVIEW_WORKSPACE_SOURCE"),"Actual production adapter consumed source-bound proposal");
            Check(!tool.GeometryCheckReady&&!tool.ClampVerified&&!tool.HolderObstacles.GeometrySupportedForCollision,"No unsigned promotion");
            p.MasterSha256=new string('f',64);File.WriteAllText(file,ToolEvidenceReviewWorkspace.Serialize(p));
            var stale=BendMasterAssemblyReader.Read(fixture.Source,Path.Combine(fixture.Root,"pairs.json"),null,CancellationToken.None,null,null,null,file);
            Check(stale.Reasons.Contains("INVALID_REVIEW_WORKSPACE_SOURCE"),"Stale source has no legacy fallback");
        });
        Test("final review: native-bound process template contains identities but no invented states or motion",()=>{
            var d=HolderMaterialReviewDraftBuilder.Create(CadPairUiLibrary().PlacementSources.Tools[0],CancellationToken.None);
            var model=CollisionModel(Rect("SHEET",0,0,1,1),3);
            var sheet=ProcessPanel(model,model.Bends[0],"SHEET",Material(Rect("SHEET",0,0,1,1)),new ProcessMotionLaw()).Source;
            var p=ToolEvidenceReviewWorkspace.Create(d,sheet);
            Check(p.Axial.Count==2&&p.Axial.All(a=>a.FeatureId==model.Bends[0].FeatureId&&a.ModelSnapshotId==model.SnapshotId&&a.ToolAxialOriginMm==null&&a.Segments.Count==0),"Native axial identity without manufactured length");
            Check(p.Simulation.Count==2&&p.Simulation.All(s=>s.SourceToWorkFrame.A==null&&s.StartMm==null&&s.Review==null),"Explicit separate simulation proposals without pose/approval");
            var process=ToolEvidenceReviewWorkspace.CreateUnsignedProcessTemplate(d,sheet);
            Check(process.Bends.Select(b=>b.FeatureId.Value).SequenceEqual(model.Bends.Select(b=>b.FeatureId)),"All source bend identities retained");
            Check(process.States.Count==0&&process.Geometry.Count==0&&process.Transitions.Count==0&&process.GraphReview==null&&process.DeclaredAlternativesComplete==null,"No process graph inferred from final model");
            model.Lease.Invalidate();ReviewReject(()=>ToolEvidenceReviewWorkspace.CreateUnsignedProcessTemplate(d,sheet),"Stale native template withheld");
        });
        FinalStaticProviderTests();
        NativeIdentityIndexTests();
    }
    private sealed class EntityIdentityFixture
    {
        internal long Pointer;internal string Persistent,Kernel;
    }
    private static void NativeIdentityIndexTests()
    {
        Test("native identity: traversal wrappers and recycled alias pointers resolve by actual persistent identity",()=>{
            var source=new[]{new EntityIdentityFixture {Pointer=1,Persistent="EDGE-A"},new EntityIdentityFixture {Pointer=2,Persistent="EDGE-B"}};
            var index=new ADDIN.Commands.NativeEntityReferenceIndex<EntityIdentityFixture>(source,e=>e.Pointer,e=>e.Persistent);
            Check(index.Resolve(source[1])==1,"Original enumeration order retained");
            Check(index.Resolve(new EntityIdentityFixture {Pointer=77,Persistent="EDGE-A"})==0,"Different wrapper same native persistent edge");
            Check(index.Resolve(new EntityIdentityFixture {Pointer=77,Persistent="EDGE-B"})==1,"Recycled transient address cannot inherit former edge");
            Check(index.PersistentResolutions==2,"Alias recovery observed explicitly");
        });
        Test("native identity: unique kernel equality fallback never matches coordinates",()=>{
            var source=new[]{new EntityIdentityFixture {Pointer=1,Kernel="FACE-A"},new EntityIdentityFixture {Pointer=2,Kernel="FACE-B"}};
            var index=new ADDIN.Commands.NativeEntityReferenceIndex<EntityIdentityFixture>(source,e=>e.Pointer,e=>e.Persistent,(a,b)=>a.Kernel==b.Kernel);
            Check(index.Resolve(new EntityIdentityFixture {Pointer=99,Kernel="FACE-B"})==1&&index.KernelResolutions==1,"Explicit native equality, not nearest geometry");
            ReviewReject(()=>index.Resolve(new EntityIdentityFixture {Pointer=99,Kernel="MISSING"}),"Missing entity is not omitted");
        });
        Test("native identity: duplicate pointers persistent references and ambiguous equality reject capture",()=>{
            ReviewReject(()=>new ADDIN.Commands.NativeEntityReferenceIndex<EntityIdentityFixture>(new[]{new EntityIdentityFixture {Pointer=1},new EntityIdentityFixture {Pointer=1}},e=>e.Pointer,e=>e.Persistent),"Duplicate source runtime identity");
            var source=new[]{new EntityIdentityFixture {Pointer=1,Persistent="SAME"},new EntityIdentityFixture {Pointer=2,Persistent="SAME"}};
            var index=new ADDIN.Commands.NativeEntityReferenceIndex<EntityIdentityFixture>(source,e=>e.Pointer,e=>e.Persistent);
            ReviewReject(()=>index.Resolve(new EntityIdentityFixture {Pointer=99,Persistent="SAME"}),"Ambiguous persistent identity");
            index=new ADDIN.Commands.NativeEntityReferenceIndex<EntityIdentityFixture>(source,e=>e.Pointer,e=>(string)null,(a,b)=>true);
            ReviewReject(()=>index.Resolve(new EntityIdentityFixture {Pointer=99}),"No first-match ambiguous kernel fallback");
        });
    }
    private sealed class FinalStaticFixture : IDisposable
    {
        internal SeatingFixture Seating;internal PlacementModelCapture Model;internal ToolProfileLibrarySnapshot Library;
        internal FinalStaticSetupDocument Document;internal ProcessProgramProvider Provider;internal string Path,Config,NativePath;
        internal void Save()
        {
            File.WriteAllText(Path,Json.Serialize(Document));
            File.WriteAllText(Config,Json.Serialize(new ProcessProviderConfiguration {Version=1,Sources=new List<ProcessProviderSource>(),
                TrustedAuthoritiesPath=Seating.Reviews.TrustPath,AuthoritySha256=PlacementSourceIo.Hash(File.ReadAllBytes(Seating.Reviews.TrustPath)),
                FinalStaticSources=new List<ProcessProviderSource> {new ProcessProviderSource {NativePath=NativePath,Configuration=Model.Configuration,
                    PackagePath=Path,ExpectedRevision=Document.Revision,PackageSha256=PlacementSourceIo.Hash(File.ReadAllBytes(Path))}}}));
        }
        internal FinalStaticSetupImport Read()=>Provider.ReadFinalStatic("CONTROLLED",NativePath,Model,Library,CancellationToken.None);
        public void Dispose(){Provider.Dispose();Seating.Dispose();}
    }
    private static FinalStaticFixture StaticInputs()
    {
        var s=Seating();var model=CollisionModel(Rect("FINAL_SHEET",0,0,1,1),2);var library=CollisionLibrary(s);
        var f=new FinalStaticFixture {Seating=s,Model=model,Library=library,Path=System.IO.Path.Combine(s.Reviews.Root,"final-static.json"),
            Config=System.IO.Path.Combine(s.Reviews.Root,"provider.json"),NativePath=System.IO.Path.Combine(s.Reviews.Root,"CONTROLLED-NATIVE-IDENTITY.txt")};
        s.Reviews.Authority.Purposes=s.Reviews.Authority.Purposes.Concat(new[]{"PunchAxialSetup","HolderAxialSetup"}).ToArray();s.Reviews.Reload();
        f.Document=new FinalStaticSetupDocument {Version=1,Revision="CONTROLLED-FINAL-STATIC-1",Records=new List<ToolAxialSetupRecord>()};
        foreach(var bend in model.Bends)
        {
            var sheet=PrismaticEnvelopeCompiler.Read(model,bend,CancellationToken.None);
            Check(sheet.Certificate.ContinuousCoverageProven,"Controlled source final native envelope proved");
            foreach(var pair in new[]{CollisionPairKind.WorkpiecePunch,CollisionPairKind.WorkpieceHolder})
            {
                var record=AxialRecord(s,sheet,pair,P(21,22));
                record.Review=s.Reviews.Sign(pair==CollisionPairKind.WorkpiecePunch?"PunchAxialSetup":"HolderAxialSetup",ToolAxialSetupEvidence.Payload(record));
                f.Document.Records.Add(record);
            }
        }
        f.Save();f.Provider=new ProcessProgramProvider(()=>f.Config);return f;
    }
    private static void FinalStaticProviderTests()
    {
        Test("final static provider: reviewed final geometry executes actual numeric independent of process graph",()=>{
            using(var f=StaticInputs())
            {
                var pins=PlacementSourceIo.Json<ProcessProviderConfiguration>(File.ReadAllBytes(f.Config));
                pins.AuthoritySha256=pins.AuthoritySha256.ToUpperInvariant();pins.FinalStaticSources[0].PackageSha256=pins.FinalStaticSources[0].PackageSha256.ToUpperInvariant();
                File.WriteAllText(f.Config,Json.Serialize(pins));
                var read=f.Read();Check(read.Current&&read.Setups.Count==4&&read.Setups.All(s=>s.Occupancy.Verified),"Existing axial verifier accepted controlled simulation records: "+string.Join(";",read.Reasons));
                Check(read.Envelopes.All(e=>e.GeometrySha256==PrismaticEnvelopeCompiler.Read(f.Model,f.Model.Bends.Single(b=>b.FeatureId==e.Binding.FeatureId),CancellationToken.None).GeometrySha256),"Restrictive freshness lease does not alter source geometry/canonical identity");
                var result=CollisionBatchWorker.RunSynchronously(new CollisionBatchRequest(f.Model,f.Library,read.Envelopes,read.Setups),new CollisionBatchOptions(workers:1));
                Check(result.Candidates.Count==36&&result.KernelQueries>0,"36 candidate rows with genuine numeric queries");
                Check(result.Candidates.Where(c=>c.ToolId==f.Seating.Assembly.Id).All(c=>c.CombinedResult==CollisionStatus.CLEAR&&c.SimulationOnly&&c.ManufacturingStatus=="CHUA_XAC_MINH"),"Checked static simulation is not process/production proof");
                Check(ReferenceEquals(read,f.Read())&&f.Provider.StaticHits==1&&f.Provider.StaticMisses==1,"Current source/model-bound cache reused");
                File.AppendAllText(System.IO.Path.Combine(f.Seating.Reviews.Root,"review-artifact.txt")," CHANGED");f.Provider.VerifyForPublicationAsync().GetAwaiter().GetResult();
                Check(!read.Current&&result.Candidates.All(c=>c.CombinedResult==CollisionStatus.UNKNOWN),"Changed artifact withdraws held numeric certificate");
            }
        });
        Test("final static provider: changed pins, duplicate identity, cancellation and unsigned setup fail closed",()=>{
            using(var f=StaticInputs())
            {
                f.Document.Records[0].Review=null;f.Save();var read=f.Read();
                Check(read.Setups.Count==4&&!read.Setups[0].Occupancy.Verified,"Unsigned pair isolated as UNKNOWN; never inferred approval");
                f.Document.Records.Add(f.Document.Records[0]);f.Save();read=f.Read();
                Check(read.Setups.Count==0&&read.Reasons.Contains("STALE_OR_AMBIGUOUS_FINAL_STATIC_RECORDS"),"No duplicate source identity");
                bool canceled=false;try{f.Provider.ReadFinalStatic("CONTROLLED",f.NativePath,f.Model,f.Library,new CancellationToken(true));}catch(OperationCanceledException){canceled=true;}Check(canceled,"Cancelled input does no work");
                File.AppendAllText(f.Path," ");read=f.Read();Check(read.Setups.Count==0,"Byte pin mismatch is not silently accepted");
            }
        });
        Test("final static provider: absent mapping is UNKNOWN without geometry capture or fabricated 4200/6000 length",()=>{
            using(var f=StaticInputs())
            {
                File.WriteAllText(f.Config,Json.Serialize(new ProcessProviderConfiguration {Version=1,Sources=new List<ProcessProviderSource>()}));
                var read=f.Read();Check(read.Setups.Count==0&&read.Envelopes.Count==0&&read.Reasons.Contains("FINAL_STATIC_NOT_MAPPED_FOR_NATIVE_PART"),"Safe fast precheck without approved setup");
            }
        });
    }
    private static int FinalReviewCompany(string[] args)
    {
        try
        {
            string output=Path.GetFullPath(args[2]);if(Directory.Exists(output))throw new IOException("New output required");Directory.CreateDirectory(output);
            var lib=new ToolProfileLibraryProvider(()=>new ToolProfileSourceConfiguration {UseMasterPairsAsLibrary=true,PlacementConfigurationPath=args[1]}).Read(CancellationToken.None);
            Check(lib.Error==null&&lib.PlacementSources.Tools.Count==18,"Company source available");
            var rows=new List<object>();
            foreach(var tool in lib.PlacementSources.Tools)
            {
                var d=HolderMaterialReviewDraftBuilder.Create(tool,CancellationToken.None,()=>lib.Current&&lib.PlacementSources.Current);
                Check(d.FixedPairMatched&&d.Rows.Count==tool.Holder.EntityKeys.Count,"Every actual Holder key "+tool.Id);
                var proposal=ToolEvidenceReviewWorkspace.Create(d);ToolEvidenceReviewWorkspace.PrepareUnsigned(proposal,d);
                File.WriteAllText(Path.Combine(output,"PRIVATE-"+tool.Id+"-UNSIGNED.json"),ToolEvidenceReviewWorkspace.Serialize(proposal));
                File.WriteAllText(Path.Combine(output,"PRIVATE-"+tool.Id+"-inventory.json"),Json.Serialize(d));
                rows.Add(new {ToolId=tool.Id,d.Status,SourceMatched=d.FixedPairMatched,Entities=d.Rows.Count,Splines=d.Rows.Count(r=>r.SourceType=="SPLINE"),
                    SupportedSplines=d.Rows.Count(r=>r.SourceType=="SPLINE"&&r.GeometrySupported),Unsupported=d.Rows.Count(r=>!r.GeometrySupported),
                    d.ConnectedComponents,d.ClosedSimpleComponents,d.OpenOrBranchedComponents,Unassigned=d.Rows.Count,
                    PunchClosed=tool.Punch.ObservedClosed,PunchRegion=tool.Punch.Region.IsValidRegion,tool.ClampVerified,d.MissingReasons});
            }
            File.WriteAllText(Path.Combine(output,"holder-review-summary.json"),Json.Serialize(new {Tools=rows,NumericCompanyQueries=0,Manufacturing="CHUA_XAC_MINH",Assertions=assertions}));
            Console.WriteLine("Holder review inventory PASS 18/18; no roles inferred, no signing keys, numeric company queries=0; assertions="+assertions);return 0;
        }
        catch(Exception ex){Console.WriteLine(ex);return 1;}
    }
}
