// Authored regression only: NOT RUN during this source-only delivery.
// All geometry, signatures and IDs below belong to controlled test fixtures.
// Nothing here establishes physical evidence for any company tool or Part.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ADDIN.Commands.BendAssembly;
using ADDIN.Commands.BendCollision;
using ADDIN.Commands.BendPlacement;
using ADDIN.Commands.BendTools;
using ADDIN.UI;

internal static partial class ToolProfileTests
{
    private static ToolReviewValidation V46Review(string payload) => new ToolReviewValidation(
        ToolReviewStatus.VerifiedExternalRecord,"CONTROLLED_UNIT_FIXTURE_ONLY",payload,expiry:DateTime.UtcNow.AddHours(1));
    private static BendLimitSelection V46Case() => new BendLimitSelection {
        Id="CONTROLLED-4200",FamilyId="CONTROLLED-FAMILY",Kind="MaxBendLength",Sheet="Limits",Locator="cell:A1",
        Unit="m",NumberPattern=@"\A対応長さ:\s*(?<value>[0-9.]+)M\z",ToolIds=new[]{CompanyToolIds.All[0]},
        MachineSystem="HDS",MaterialCode="ST",SectionKind="U",PoseId="CONTROLLED-U-90",
        ThicknessMinMm=1,ThicknessMaxMm=3,AngleMinDeg=80,AngleMaxDeg=100,WMinMm=5,WMaxMm=90,LMinMm=5,LMaxMm=90 };
    private static BendLimitRuleDraft V46Rule(BendLimitSelection selection=null) => new BendLimitRuleDraft(
        "CONTROLLED-4200","CONTROLLED-FAMILY",BendLimitKind.MaxBendLength,new string('a',64),"Limits","cell:A1",new string('b',64),"mm",4200,
        new[]{CompanyToolIds.All[0]},new string[0],V46Review(new string('c',64)),new BendExcelCaseConditions(selection??V46Case()));
    private static BendExcelObservation V46Observation() => new BendExcelObservation {
        Current=true,ToolId=CompanyToolIds.All[0],MachineSystem="HDS",MaterialCode="ST",SectionKind="U",PoseId="CONTROLLED-U-90",
        ThicknessMm=2,AngleDeg=90,Wmm=30,Lmm=35,BendLengthMm=4200,MeasurementToleranceMm=0,AngleToleranceDeg=0,
        SourceBinding=new string('d',64),WorkbookSha256=new string('a',64),Review=V46Review(new string('d',64)) };
    private static AssemblyOccurrence V46Occurrence(string configuration="Default",string path="ROOT/PART") =>
        new AssemblyOccurrence(path,"ROOT","CONTROLLED.SLDPRT","CONTROLLED_SOURCE",configuration,
            new double[]{1,0,0,0,1,0,0,0,1,0,0,0,1},AssemblyOccurrenceKind.SheetMetal,true);
    private static AssemblyPartResult V46Raw(IEnumerable<AssemblyBendMetadata> metadata,int count=0) =>
        new AssemblyPartResult("CONTROLLED_SOURCE",new[]{V46Occurrence()},null,null,null,count,false,()=>null,null,metadata);
    private static AssemblyBatchSnapshot V46Snapshot(AssemblyPartResult part)
    {
        var library=ToolProfileLibraryProvider.Failure("DXF source directory is not configured.");
        var occurrence=V46Occurrence();
        var request=new AssemblyBatchRequest("CONTROLLED","Default",AssemblyCheckScope.EntireAssembly,new PlacementLease(),library,
            new[]{new AssemblySourceInput("CONTROLLED_SOURCE",null,new[]{occurrence})},new[]{occurrence});
        return new AssemblyBatchSnapshot(request,new ADDIN.Commands.BendEnvelope.CollisionBatchRunState(),new[]{part},0,0,0,0,0,0,1,
            new long[8],0,0,null,()=>null,0,0);
    }
    private static AssemblyBendMetadata V46Metadata(int id,string min="MeetsMinimum") =>
        new AssemblyBendMetadata(id,"CONTROLLED_PID_"+id,"CONTROLLED_BEND_"+id,90,.1,min,min,min,null,"ST");

    private static void V46SourceRegression()
    {
        SourceSettingsReadRegression();
        AssemblyBodyMetadataTests();
        Test("V46 blocked Assembly: empty inventory exposes the body and tool-source failures together",()=>{
            var raw=new AssemblyPartResult("CONTROLLED_SOURCE",new[]{V46Occurrence()},"UNKNOWN_ASSEMBLY_BODY_METADATA",
                null,null,0,false,()=>null);
            var part=BatchViewModelConverter.PartsFromSnapshot(V46Snapshot(raw)).Single();
            var outcome=BatchCheckOutcome.Project(part,null,null,false);
            Check(part.Bends.Count==0&&!part.NativeInventoryKnown,"Missing capture is never a verified zero-bend Part");
            Check(outcome.State==BatchOutcomeState.UNKNOWN_COVERAGE&&outcome.Caption=="CHƯA ĐỌC ĐƯỢC NẾP CHẤN",
                "Unknown capture remains an explicit blocked result");
            Check(outcome.Reason.Contains("body")&&outcome.Reason.Contains("Nguồn dao:")&&outcome.Reason.Contains("cấu hình"),
                "Neither blocker is hidden behind a generic coverage message");
            Check(part.Reason.Contains("; UNRECOGNIZED_BEND_INVENTORY:"),"Diagnostic codes remain separately translatable");
        });
        Test("V46 blocked Assembly: Lightweight has a Resolve/Refresh instruction without a fabricated check",()=>{
            var occurrence=new AssemblyOccurrence("ROOT/CONTROLLED","ROOT","CONTROLLED.SLDPRT","CONTROLLED_SOURCE","Default",
                new double[]{1,0,0,0,1,0,0,0,1,0,0,0,1},AssemblyOccurrenceKind.Lightweight,false,
                reason:"LIGHTWEIGHT_NEEDS_EXPLICIT_USER_RESOLVE");
            var raw=new AssemblyPartResult("CONTROLLED_SOURCE",new[]{occurrence},"LIGHTWEIGHT_NEEDS_EXPLICIT_USER_RESOLVE",
                null,null,0,false,()=>null);
            var part=BatchViewModelConverter.PartsFromSnapshot(V46Snapshot(raw)).Single();
            part.ToolSourceError=null;part.LoadedPunchCount=18;
            var outcome=BatchCheckOutcome.Project(part,null,null,false);
            Check(outcome.Reason.Contains("Lightweight")&&outcome.Reason.Contains("Resolved")&&outcome.Reason.Contains("Refresh"),
                "The existing result panel states the required user action");
            Check(part.Bends.Count==0&&outcome.State==BatchOutcomeState.UNKNOWN_COVERAGE,"No source Part result is promoted to instance evidence");
            raw.NativeInventoryKnown=true;part.NativeInventoryKnown=true;
            var empty=BatchCheckOutcome.Project(part,null,null,false);
            Check(empty.Caption!="CHƯA ĐỌC ĐƯỢC NẾP CHẤN"&&empty.State==BatchOutcomeState.UNKNOWN_COVERAGE,
                "A separately verified empty inventory stays distinct from capture failure");
        });
        Test("V46 source error presentation: typed prefix is translated while full diagnostic is retained",()=>{
            foreach(string prefix in new[]{"SOURCE_SETTINGS_ACCESS_DENIED","SOURCE_SETTINGS_IO_FAILED","SOURCE_SETTINGS_INVALID","SOURCE_SETTINGS_FAILED"})
            {
                string code=prefix+": CONTROLLED_SETTINGS_PATH";var message=BendCheckDisplayText.Reason(code);
                Check(message.OriginalCode==code&&!message.Caption.Contains("chưa có diễn giải"),"Stable settings reason: "+prefix);
            }
        });
        Test("V46 inventory: FeatureIds 524/530 survive zero candidates and inconsistent backend count",()=>{
            var raw=V46Raw(new[]{V46Metadata(524),V46Metadata(530)});
            var part=BatchViewModelConverter.PartsFromSnapshot(V46Snapshot(raw)).Single();
            Check(part.Bends.Select(b=>b.FeatureId).SequenceEqual(new[]{524,530}),"Native metadata is the inventory");
            Check(part.Bends.Select(b=>b.BendIndex).SequenceEqual(new[]{1,2}),"Stable B1/B2 numbering");
            Check(part.Bends.All(b=>b.ToolCandidates.Count==18&&b.ToolCandidates.All(t=>t.GeometricStatus==CollisionStatus.UNKNOWN)),"18 UNKNOWN slots per known bend");
            Check(!BatchViewModelConverter.FromSnapshot(V46Snapshot(raw)).CompletionConfirmed,"Contradictory recognition is not 100 percent completion");
        });
        Test("V46 inventory: candidate-only FeatureId is never invented as a native bend",()=>{
            var query=new ADDIN.Commands.BendEnvelope.AxialCollisionQuery(null,null,CollisionPairKind.WorkpiecePunch);
            var collision=new ADDIN.Commands.BendEnvelope.AxialPairResult(query,CollisionStatus.COLLISION,new string[0],0,0,0);
            var raw=new AssemblyPartResult("CONTROLLED_SOURCE",new[]{V46Occurrence()},null,
                new[]{new ADDIN.Commands.BendEnvelope.VolumeCandidateCollisionResult(CompanyToolIds.All[0],999,collision,collision,true)},null,1,true,()=>null);
            Check(BatchViewModelConverter.NativeFeatureInventory(raw).Length==0,"No native metadata/source inventory supplied");
            Check(raw.HasCandidateCollision&&raw.GeometricStatus==CollisionStatus.UNKNOWN,"Candidate collision is not Part verdict");
        });
        Test("V46 inventory: six and twelve bends, exact configuration-specific native IDs",()=>{
            foreach(int count in new[]{6,12})
            {
                var ids=Enumerable.Range(0,count).Select(i=>524+i*6).ToArray();
                var raw=V46Raw(ids.Select(id=>V46Metadata(id)),count);raw.NativeInventoryKnown=true;
                Check(BatchViewModelConverter.NativeFeatureInventory(raw).SequenceEqual(ids),"No ten-bend UI limit");
            }
            var first=V46Occurrence("Default","ROOT/A/PART");var other=V46Occurrence("Alternate","ROOT/B/PART");
            Check(first.Configuration!=other.Configuration&&first.Id!=other.Id,"Duplicate names retain occurrence path/configuration");
        });
        Test("V46 outcome: unchanged Min 524/530 is independent of missing DXF",()=>{
            var raw=V46Raw(new[]{V46Metadata(524),V46Metadata(530,"BelowMinimum")},2);raw.NativeInventoryKnown=true;
            var part=BatchViewModelConverter.PartsFromSnapshot(V46Snapshot(raw)).Single();
            Check(BatchCheckOutcome.Project(part,part.Bends[0],part.Bends[0].ToolCandidates[0],false).State==BatchOutcomeState.UNKNOWN_CONFIG,"Min pass is not collision pass");
            Check(BatchCheckOutcome.Project(part,part.Bends[1],part.Bends[1].ToolCandidates[0],false).State==BatchOutcomeState.MIN_NG,"Min NG remains scoped to B2");
            Check(BatchCheckOutcome.Project(part,part.Bends[0],null,true).State==BatchOutcomeState.RUNNING,"No old clear while running");
        });
        Test("V46 source: immutable cache and same-size same-time content invalidation",()=>{
            string folder=Folder(),path=Path.Combine(folder,"amd-std-003_DIM.dxf");Save(folder,Path.GetFileName(path),Dxf(Poly()));
            using(var provider=Provider(folder))
            {
                var first=provider.Read(CancellationToken.None);var repeat=provider.Read(CancellationToken.None);
                Check(ReferenceEquals(first,repeat),"Reuse unchanged parsed geometry");
                var stamp=File.GetLastWriteTimeUtc(path);string text=File.ReadAllText(path).Replace("10\n4\n","10\n5\n");
                File.WriteAllText(path,text,new UTF8Encoding(false));File.SetLastWriteTimeUtc(path,stamp);
                var next=provider.Read(CancellationToken.None);
                Check(!first.Current&&next.Current&&first.Fingerprint!=next.Fingerprint,"Full SHA, not file metadata");
            }
        });
        Test("V46 source: nested Excel/review bytes are recorded in the same effective identity",()=>{
            string folder=Folder(),path=Path.Combine(folder,"review.txt");Save(folder,"review.txt","CONTROLLED A");
            ToolSourceIdentity identity;
            using(var scope=new ToolSourceReadScope())
            {
                scope.ConfigurationKey="CONTROLLED";PlacementSourceIo.Read(path,1024,CancellationToken.None);identity=scope.Seal(CancellationToken.None);
            }
            try
            {
                Check(identity.Current,"Recorded actual bytes");var stamp=File.GetLastWriteTimeUtc(path);Save(folder,"review.txt","CONTROLLED B");File.SetLastWriteTimeUtc(path,stamp);
                Check(!identity.Validate(CancellationToken.None),"Unchanged settings cannot preserve changed review/Excel bytes");
            }
            finally {identity.Invalidate();}
        });
        Test("V46 source: Refresh during pending read cannot publish the canceled generation",()=>{
            string folder=Folder();Save(folder,"amd-std-003_DIM.dxf",Dxf(Poly()));
            using(var entered=new ManualResetEventSlim(false))using(var release=new ManualResetEventSlim(false))
            {
                var provider=new ToolProfileLibraryProvider(()=>{entered.Set();release.Wait();return new ToolProfileSourceConfiguration{DirectoryPath=folder};});
                using(var session=new ToolProfileLibrarySession(provider))
                {
                    session.Refresh();Check(entered.Wait(5000),"Worker entered");session.Refresh();release.Set();
                    Wait(()=>{session.Poll();return session.Snapshot.Error==null;});
                    Check(session.Snapshot.Current,"Only new generation accepted");
                    var held=session.Snapshot;var audit=session.VerifyForPublicationAsync();session.Refresh();
                    Wait(()=>audit.IsCompleted);var reviewed=audit.GetAwaiter().GetResult();
                    Check(!held.Current,"Refresh immediately revokes the displayed old source");
                    Check(reviewed.Error!=null||ReferenceEquals(reviewed,held)&&!reviewed.Current,
                        "Superseded audit cannot be accepted as this run's evidence");
                }
            }
        });
        Test("V46 source: a stable failure retains 18 declared IDs, never geometry",()=>{
            using(var provider=new ToolProfileLibraryProvider(()=>new ToolProfileSourceConfiguration()))
            {
                var first=provider.Read(CancellationToken.None);var again=provider.Read(CancellationToken.None);
                Check(ReferenceEquals(first,again)&&first.Current&&first.Tools.Count==18,"Stable source-health result can publish recognized Part inventory");
                Check(first.Tools.All(t=>!t.Validation.PunchGeometryReadComplete)&&first.PlacementSources.Tools.All(t=>!t.ClampVerified),"No fallback CAD or physical setup");
            }
        });
        Test("V46 Excel: verified 4200/6000 compare bend-line length, not W/L",()=>{
            var measured=V46Observation();var rule=V46Rule();
            Check(BendExcelCaseMatcher.Evaluate(rule,measured).ExceedsCertifiedLength==false,"4200 exact boundary");
            measured.BendLengthMm=4210;Check(BendExcelCaseMatcher.Evaluate(rule,measured).ExceedsCertifiedLength==true,"4210 exceeds 4200");
            var six=new BendLimitRuleDraft("CONTROLLED-6000","CONTROLLED",BendLimitKind.MaxBendLength,new string('a',64),"Limits","cell:A1",new string('b',64),"mm",6000,
                rule.ToolIds,new string[0],V46Review(new string('c',64)),rule.ApprovedConditions);
            measured.BendLengthMm=6000;Check(BendExcelCaseMatcher.Evaluate(six,measured).ExceedsCertifiedLength==false,"6000 exact boundary; W=30 and L=35 unchanged");
            measured.BendLengthMm=6010;Check(BendExcelCaseMatcher.Evaluate(six,measured).ExceedsCertifiedLength==true,"6010 exceeds 6000");
        });
        Test("V46 Excel: missing W/L, stale source, unknown pose or mismatched workbook cannot authorize fallback",()=>{
            var rule=V46Rule();var measured=V46Observation();measured.Wmm=null;
            Check(BendExcelCaseMatcher.Evaluate(rule,measured).Applicability==BendExcelCaseApplicability.MATCH_UNRESOLVED,"Missing W");
            measured.Wmm=30;measured.PoseId=null;Check(!measured.Complete,"Missing pose");
            measured.PoseId="CONTROLLED-U-90";measured.WorkbookSha256=new string('f',64);
            Check(BendExcelCaseMatcher.Evaluate(rule,measured).Applicability==BendExcelCaseApplicability.MATCH_UNRESOLVED,"Wrong workbook");
            measured.WorkbookSha256=new string('a',64);measured.Review.Invalidate();
            Check(BendExcelCaseMatcher.Evaluate(rule,measured).Applicability==BendExcelCaseApplicability.MATCH_UNRESOLVED,"Stale measurements");
        });
        Test("V46 Excel: near-boundary uncertainty is UNKNOWN, never a negative match",()=>{
            var measured=V46Observation();measured.MeasurementToleranceMm=.01;
            Check(BendExcelCaseMatcher.Evaluate(V46Rule(),measured).Applicability==BendExcelCaseApplicability.MATCH_UNRESOLVED,"Length error band crosses limit");
            measured.MeasurementToleranceMm=1e-14;
            Check(BendExcelCaseMatcher.Evaluate(V46Rule(),measured).Applicability==BendExcelCaseApplicability.MATCH_UNRESOLVED,"Tiny positive uncertainty at exact limit must not round to PASS");
            measured.MeasurementToleranceMm=.01;
            measured.BendLengthMm=4000;measured.Wmm=90.005;
            Check(BendExcelCaseMatcher.Evaluate(V46Rule(),measured).Applicability==BendExcelCaseApplicability.MATCH_UNRESOLVED,"W error band overlaps condition boundary");
        });
        Test("V46 Excel: a single negative rule is not an exhaustive no-case certificate",()=>{
            var measured=V46Observation();measured.MaterialCode="SUS";var result=BendExcelCaseMatcher.Evaluate(V46Rule(),measured);
            Check(result.Applicability==BendExcelCaseApplicability.NOT_APPLICABLE_CONFIRMED&&!result.AllowsCadFallback,"Only this rule was ruled out");
            Check(result.AdmissionReason(false)!=null,"No production CAD admission");
            var certificate=V46Review(new string('e',64));var all=new BendExcelCaseResult(result.Applicability,null,"CONTROLLED_EXHAUSTIVE_SCOPE",certificate);
            Check(all.AllowsCadFallback&&all.AdmissionReason(false)==null,"Explicit reviewed exhaustive certificate");
            certificate.Invalidate();Check(!all.AllowsCadFallback,"Revoked certificate cannot authorize CAD");
        });
        Test("V46 Excel: applicability and motion have distinct gates",()=>{
            var unresolved=new BendExcelCaseResult(BendExcelCaseApplicability.MATCH_UNRESOLVED,null,"MISSING_MAPPING");
            var pass=new BendExcelCaseResult(BendExcelCaseApplicability.MATCH_CONFIRMED,false,"CONTROLLED_SIGNED_CASE");
            var fail=new BendExcelCaseResult(BendExcelCaseApplicability.MATCH_CONFIRMED,true,"CONTROLLED_SIGNED_CASE");
            Check(unresolved.AdmissionReason(false)!=null&&unresolved.AdmissionReason(true)!=null,"Unresolved is not no-case");
            Check(pass.AdmissionReason(false)!=null&&pass.AdmissionReason(true)==null,"Excel governs static MAX; reviewed process still needs numeric motion");
            Check(fail.AdmissionReason(true)!=null,"Exceeded certified condition is not an admitted process");
        });
        Test("V46 Excel: overlapping unequal certified limits are conflicting even when both pass",()=>{
            var first=V46Rule();
            var second=new BendLimitRuleDraft("CONTROLLED-6000","CONTROLLED-FAMILY",BendLimitKind.MaxBendLength,new string('a',64),"Limits","cell:A2",new string('b',64),"mm",6000,
                first.ToolIds,new string[0],V46Review(new string('c',64)),first.ApprovedConditions);
            var observed=V46Observation();observed.BendLengthMm=4000;
            var placement=new PlacementSourceSnapshot("CONTROLLED",null,new[]{first,second},new string[0]);
            var result=BendExcelApplicabilityAdapter.MatchReviewedRules(placement,observed);
            Check(result.Applicability==BendExcelCaseApplicability.MATCH_UNRESOLVED&&result.Evidence=="CONFLICTING_APPROVED_EXCEL_LIMIT_VALUES",
                "No hidden min/max/intersection rule is invented");
        });
        Test("V46 importer: signed measurement clone, tamper rejection and no coordinate-only approval",()=>{
            using(var reviews=Reviews(false))
            {
                reviews.Authority.Purposes=new[]{"BendExcelNativeMeasurements"};reviews.Reload();
                var record=new BendExcelMeasurementRecord {SourceKey="CONTROLLED",ModelSnapshotId="CONTROLLED",Configuration="Default",FeatureId=524,
                    PersistentId="CONTROLLED_PID",ToolId=CompanyToolIds.All[0],WorkbookSha256=new string('a',64),Wmm=30,Lmm=35};
                record.Review=reviews.Sign("BendExcelNativeMeasurements",BendExcelMeasurement.Payload(record),false);
                var imported=new BendExcelMeasurement(record,reviews.Verifier,reviews.Root,CancellationToken.None);
                Check(imported.Review.Status==ToolReviewStatus.VerifiedExternalRecord,"Signature validates record only, not physical clamp");
                record.WorkbookSha256=new string('b',64);Check(imported.WorkbookSha256==new string('a',64),"Mutable input cannot alter imported record");
                var tampered=new BendExcelMeasurement(record,reviews.Verifier,reviews.Root,CancellationToken.None);
                Check(tampered.Review.Status==ToolReviewStatus.Stale,"Changed workbook invalidates signed payload");
                record.Review=null;Check(new BendExcelMeasurement(record,reviews.Verifier,reviews.Root,CancellationToken.None).Review.Status!=ToolReviewStatus.VerifiedExternalRecord,"Numbers alone are not approval");
            }
        });
        Test("V46 workbook: real cell/object hash, signed pose conditions, exhaustive case set and tamper",()=>{
            using(var reviews=Reviews(false))
            {
                reviews.Authority.Purposes=new[]{"BendExcelCaseApplicability","BendExcelCaseSetCompleteness","BendExcelReferenceDisposition"};reviews.Reload();
                string workbook=Workbook(reviews.Root),sha=PlacementSourceIo.Hash(File.ReadAllBytes(workbook));var selection=V46Case();
                selection.ExpectedObjectSha256=BendLimitWorkbookReader.ReadObjects(File.ReadAllBytes(workbook),CancellationToken.None).Single(o=>o.Locator=="cell:A1").Sha256;
                string payload=ToolEvidenceCanonical.Hash("BendExcelCaseNumericV2",sha,selection.Id,selection.FamilyId,selection.Kind,
                    selection.Sheet,selection.Locator,selection.ExpectedObjectSha256,"m",ToolEvidenceCanonical.Number(4200),
                    ToolEvidenceCanonical.Hash(selection.ToolIds.OrderBy(x=>x,StringComparer.Ordinal).ToArray()),new BendExcelCaseConditions(selection).Binding);
                selection.ApplicabilityReview=reviews.Sign("BendExcelCaseApplicability",payload,false);
                var manifest=new BendLimitManifest {Version=1,WorkbookSha256=sha,CaseSetScope="AllCheckChanCasesInWorkbook",Rules=new List<BendLimitSelection>{selection}};
                string path=Path.Combine(reviews.Root,"limits.json");File.WriteAllText(path,Json.Serialize(manifest));
                var first=BendLimitWorkbookReader.Read(workbook,path,CancellationToken.None,reviews.TrustPath);
                Check(first.Rules.Single().FamilyMappingVerified&&first.Rules.Single().ValueMm==4200,"Exact source conversion m -> mm");
                manifest.CaseSetReview=reviews.Sign("BendExcelCaseSetCompleteness",BendLimitWorkbookReader.CaseSetPayload(sha,first.Rules),false);
                File.WriteAllText(path,Json.Serialize(manifest));var next=BendLimitWorkbookReader.Read(workbook,path,CancellationToken.None,reviews.TrustPath);
                Check(next.CaseSetReview.Status==ToolReviewStatus.VerifiedExternalRecord,"Exhaustive scope is separately signed");
                var referenceObject=BendLimitWorkbookReader.ReadObjects(File.ReadAllBytes(workbook),CancellationToken.None).Single(o=>o.Locator=="cell:B1");
                var reference=new BendLimitSelection {Id="CONTROLLED-REFERENCE",FamilyId="CONTROLLED",Kind="DeepBendClearanceExample",Sheet="Limits",Locator="cell:B1",
                    ExpectedObjectSha256=referenceObject.Sha256,Unit="mm",ToolIds=new[]{CompanyToolIds.All[0]}};
                manifest.Rules.Add(reference);File.WriteAllText(path,Json.Serialize(manifest));
                Check(!BendLimitWorkbookReader.Read(workbook,path,CancellationToken.None,reviews.TrustPath).Rules.Last().ReviewedInformationalReference,"Informational exclusion needs independent signed review");
                reference.ReferenceDispositionReview=reviews.Sign("BendExcelReferenceDisposition",BendLimitWorkbookReader.ReferencePayload(sha,reference.Id,reference.FamilyId,
                    BendLimitKind.DeepBendClearanceExample,reference.Sheet,reference.Locator,referenceObject.Sha256),false);
                File.WriteAllText(path,Json.Serialize(manifest));var mixed=BendLimitWorkbookReader.Read(workbook,path,CancellationToken.None,reviews.TrustPath);
                manifest.CaseSetReview=reviews.Sign("BendExcelCaseSetCompleteness",BendLimitWorkbookReader.CaseSetPayload(sha,mixed.Rules),false);
                File.WriteAllText(path,Json.Serialize(manifest));mixed=BendLimitWorkbookReader.Read(workbook,path,CancellationToken.None,reviews.TrustPath);
                Check(mixed.Rules.First().FamilyMappingVerified&&mixed.Rules.Last().ReviewedInformationalReference&&mixed.CaseSetReview.Status==ToolReviewStatus.VerifiedExternalRecord,
                    "Explicitly reviewed example does not poison approved numeric cases or completeness");
                selection.PoseId="TAMPERED_POSE";File.WriteAllText(path,Json.Serialize(manifest));
                Check(!BendLimitWorkbookReader.Read(workbook,path,CancellationToken.None,reviews.TrustPath).Rules.First().FamilyMappingVerified,"Changed pose cannot reuse review");
            }
        });
        Test("V46 planner: fewer tool changes before fewer setup changes",()=>{
            Func<string,string,int,string,string,BendVerifiedTransition> edge=(a,b,f,t,s)=>new BendVerifiedTransition {
                BeforeStateId=a,AfterStateId=b,FeatureId=f,ToolId=t,VerifiedSetupId=s,SourceRevision="CONTROLLED_REV",EvidenceReference="CONTROLLED_GRAPH_ONLY",
                GeometryVerified=true,SetupVerified=true,ProcessVerified=true,SourceCurrent=true};
            var edges=new[]{edge("S","A",524,CompanyToolIds.All[0],"A1"),edge("A","T",530,CompanyToolIds.All[0],"A2"),
                edge("S","B",524,CompanyToolIds.All[1],"B"),edge("B","T",530,CompanyToolIds.All[2],"B")};
            BendVerifiedPlanResult plan;string reason;
            Check(BendVerifiedToolChangePlanner.TryChoose("S",new[]{"T"},new[]{524,530},edges,"CONTROLLED_REV",out plan,out reason),"Complete graph has an evidenced path");
            Check(plan.ToolChanges==0&&plan.SetupChanges==1,"Prefer one tool despite an extra reviewed setup change");
            edges[1].SourceCurrent=false;
            Check(BendVerifiedToolChangePlanner.TryChoose("S",new[]{"T"},new[]{524,530},edges,"CONTROLLED_REV",out plan,out reason)&&plan.ToolChanges==1,"Never reuse a stale transition to prefer a tool");
        });
        Test("V46 planner: incomplete graph gives UNKNOWN without invented sequence",()=>{
            BendVerifiedPlanResult plan;string reason;
            Check(!BendVerifiedToolChangePlanner.TryChoose("S",new[]{"T"},new[]{524,530},new BendVerifiedTransition[0],"CONTROLLED_REV",out plan,out reason)&&plan==null,"No fabricated recommendation");
        });
    }
    // Source-only regression fragment for V46SourceRegression.cs. NOT RUN.
    // Invoke SourceSettingsReadRegression() from V46SourceRegression().
    private static void SourceSettingsReadRegression()
    {
        var prior=ToolProfileSourceSettings.OverrideNames.ToDictionary(n=>n,n=>Environment.GetEnvironmentVariable(n));
        try
        {
            foreach(string name in ToolProfileSourceSettings.OverrideNames)Environment.SetEnvironmentVariable(name,null);
            Test("Source settings: direct read retains an existing approved fixed-pair selection",()=>{
                string folder=Folder(),path=Path.Combine(folder,"source.json"),placement=Path.Combine(folder,"placement.json");
                File.WriteAllText(path,new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new ToolProfileSourceConfiguration {
                    Version=1,UseMasterPairsAsLibrary=true,PlacementConfigurationPath=placement}),new UTF8Encoding(false));
                ToolSourceIdentity identity;
                using(var scope=new ToolSourceReadScope())
                {
                    var selected=new ToolProfileSourceSettings(path).Load();scope.ConfigurationKey="CONTROLLED_SETTINGS";
                    Check(selected.UseMasterPairsAsLibrary&&selected.PlacementConfigurationPath==placement,"Read actual settings instead of defaulting to an empty DXF folder");
                    identity=scope.Seal(CancellationToken.None);
                }
                try { Check(identity.Current&&identity.Validate(CancellationToken.None),"Approved settings bytes have a current SHA-bound identity"); }
                finally { identity.Invalidate(); }
            });
            Test("Source settings: genuine absence retains defaults and explicit overrides",()=>{
                string folder=Folder(),path=Path.Combine(folder,"absent.json");ToolSourceIdentity identity;
                using(var scope=new ToolSourceReadScope())
                {
                    var selected=new ToolProfileSourceSettings(path).Load();scope.ConfigurationKey="CONTROLLED_ABSENT";
                    Check(selected.Version==1&&!selected.UseMasterPairsAsLibrary&&selected.DirectoryPath==null,"A genuinely missing file preserves default behavior");
                    identity=scope.Seal(CancellationToken.None);
                }
                try
                {
                    Check(identity.Current,"A direct FileNotFound result is a known missing source");
                    File.WriteAllText(path,"{}",new UTF8Encoding(false));
                    Check(!identity.Validate(CancellationToken.None),"Creating previously absent settings revokes the old identity");
                }
                finally { identity.Invalidate(); }
                string overridePath=Path.Combine(folder,"explicit-placement.json");
                Environment.SetEnvironmentVariable("ADDIN_BEND_USE_MASTER_PAIRS","1");
                Environment.SetEnvironmentVariable("ADDIN_BEND_PLACEMENT_CONFIG",overridePath);
                try
                {
                    var selected=new ToolProfileSourceSettings(Path.Combine(folder,"still-absent.json")).Load();
                    Check(selected.UseMasterPairsAsLibrary&&selected.PlacementConfigurationPath==overridePath,"Only explicitly supplied ADDIN overrides are applied");
                }
                finally
                {
                    Environment.SetEnvironmentVariable("ADDIN_BEND_USE_MASTER_PAIRS",null);
                    Environment.SetEnvironmentVariable("ADDIN_BEND_PLACEMENT_CONFIG",null);
                }
            });
            Test("Source settings: false File.Exists probe is not proof of a missing source",()=>{
                string directory=Folder();Check(!File.Exists(directory),"A directory produces the same false metadata probe as an inaccessible file");
                using(var scope=new ToolSourceReadScope())
                {
                    InvalidDataException failure=null;
                    try { new ToolProfileSourceSettings(directory).Load(); }
                    catch(InvalidDataException ex) { failure=ex; }
                    Check(failure!=null&&failure.Message.StartsWith("SOURCE_SETTINGS_ACCESS_DENIED:",StringComparison.Ordinal)&&
                        failure.InnerException is UnauthorizedAccessException,"Direct open reports the actual access exception, never empty default settings");
                    var identity=scope.Seal(CancellationToken.None);
                    try { Check(!identity.Current,"An unreadable path cannot acquire a usable source identity"); }
                    finally { identity.Invalidate(); }
                }
            });
            Test("Source settings: sharing and malformed JSON failures retain actionable reasons",()=>{
                string folder=Folder(),path=Path.Combine(folder,"source.json");File.WriteAllText(path,"{}",new UTF8Encoding(false));
                using(var locked=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None))
                using(var scope=new ToolSourceReadScope())
                {
                    InvalidDataException failure=null;
                    try { new ToolProfileSourceSettings(path).Load(); }
                    catch(InvalidDataException ex) { failure=ex; }
                    Check(failure!=null&&failure.Message.StartsWith("SOURCE_SETTINGS_IO_FAILED:",StringComparison.Ordinal)&&failure.InnerException is IOException,
                        "Sharing errors retain their original exception instead of becoming missing settings");
                    var identity=scope.Seal(CancellationToken.None);
                    try { Check(!identity.Current,"A failed settings open is not a current geometry source"); }
                    finally { identity.Invalidate(); }
                }
                File.WriteAllText(path,"{ invalid json",new UTF8Encoding(false));
                using(var provider=new ToolProfileLibraryProvider(new ToolProfileSourceSettings(path).Load))
                {
                    var snapshot=provider.Read(CancellationToken.None);
                    Check(snapshot.Error!=null&&snapshot.Error.StartsWith("SOURCE_SETTINGS_INVALID:",StringComparison.Ordinal),"Malformed settings surface a stable actionable source error");
                    Check(snapshot.Tools.Count==18&&snapshot.Tools.All(t=>!t.Validation.PunchGeometryReadComplete),"Invalid settings retain 18 UNKNOWN slots with zero read geometry");
                }
            });
        }
        finally
        {
            foreach(var value in prior)Environment.SetEnvironmentVariable(value.Key,value.Value);
        }
    }

}
