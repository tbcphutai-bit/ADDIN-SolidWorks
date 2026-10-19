using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using ADDIN.Commands.BendAssembly;
using ADDIN.Commands.BendCollision;
using ADDIN.Commands.BendEnvelope;
using ADDIN.Commands.BendPlacement;
using ADDIN.Commands.BendProcess;
using ADDIN.Commands.BendTools;

namespace ADDIN.Commands.BendPlanning
{
    internal sealed class ProcessProgramImport
    {
        private readonly PlacementLease lease;
        public PartSequenceSpace Sequence { get; }
        public IReadOnlyList<BendProcessPlan> Plans { get; }
        public IReadOnlyList<SheetEnvelopeCapture> Envelopes { get; }
        public IReadOnlyList<ToolAxialSetupEvidence> Setups { get; }
        public AssemblyProgramUse Use { get; }
        public string Revision { get; }
        public string PackageSha256 { get; }
        public IReadOnlyList<string> Reasons { get; }
        public IReadOnlyDictionary<string,string> Files { get; }
        public IReadOnlyDictionary<string,string> ReviewPayloads { get; }
        public bool Current => lease.Current && Sequence.InvalidReason == null;
        internal void Invalidate() { lease.Invalidate(); Sequence.Invalidate(); }
        internal ProcessProgramImport(PlacementLease lease, PartSequenceSpace sequence, IEnumerable<BendProcessPlan> plans,
            IEnumerable<SheetEnvelopeCapture> envelopes, IEnumerable<ToolAxialSetupEvidence> setups, AssemblyProgramUse use,
            string revision, string sha, IEnumerable<string> reasons, IDictionary<string,string> files, IDictionary<string,string> payloads)
        {
            this.lease=lease;Sequence=sequence;Plans=PlacementLists.Copy(plans);Envelopes=PlacementLists.Copy(envelopes);
            Setups=PlacementLists.Copy(setups);Use=use;Revision=revision;PackageSha256=sha;Reasons=PlacementLists.Reasons(reasons);
            Files=new System.Collections.ObjectModel.ReadOnlyDictionary<string,string>(new Dictionary<string,string>(files,StringComparer.OrdinalIgnoreCase));
            ReviewPayloads=new System.Collections.ObjectModel.ReadOnlyDictionary<string,string>(new Dictionary<string,string>(payloads));
        }
        internal static ProcessProgramImport Missing(PlacementModelCapture model, params string[] reasons) =>
            new ProcessProgramImport(new PlacementLease(),PartSequenceSpace.Missing(model,reasons),null,null,null,null,null,null,reasons,
                new Dictionary<string,string>(),new Dictionary<string,string>());
    }
    // Pure managed file/geometry importer. No COM, controls, signing private
    // keys, DXF repair, native-shape unfolding or guessed manufacturing data.
    internal static class ProcessPackageImporter
    {
        private static double Required(double? value,string why)
        { if(!value.HasValue||!ToolPoint.Finite(value.Value))throw new InvalidDataException(why);return value.Value; }
        private static ToolPoint Point(double[] p)
        { if(p==null||p.Length!=2)throw new InvalidDataException("PROCESS_POINT_MM_REQUIRED");return new ToolPoint(p[0],p[1]); }
        private static PlacementVector3 Vector(double[] p)
        { if(p==null||p.Length!=3||p.Any(x=>!ToolPoint.Finite(x)))throw new InvalidDataException("PROCESS_REGISTERED_VECTOR_REQUIRED");return new PlacementVector3(p[0],p[1],p[2]); }
        private static T Kind<T>(string text) where T:struct
        { T value;if(!Enum.TryParse(text,out value)||!Enum.IsDefined(typeof(T),value)||value.ToString()!=text)throw new InvalidDataException("UNSUPPORTED_PROCESS_ENUM");return value; }
        private static IReadOnlyList<T> Bounded<T>(IList<T> rows,int maximum,string why)
        { if(rows==null||rows.Count>maximum||rows.Any(r=>r==null))throw new InvalidDataException(why);return rows.ToList().AsReadOnly(); }
        internal static string SafePath(string root,string relative)
        {
            if(string.IsNullOrWhiteSpace(relative)||Path.IsPathRooted(relative)||relative.IndexOf(':')>=0)throw new InvalidDataException("PROCESS_ARTIFACT_PATH_MUST_BE_RELATIVE");
            string basePath=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            string full=Path.GetFullPath(Path.Combine(basePath,relative));
            if(!full.StartsWith(basePath,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("PROCESS_ARTIFACT_OUTSIDE_PACKAGE");
            for(var part=new FileInfo(full) as FileSystemInfo;part!=null&&!string.Equals(part.FullName,basePath.TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase);part=part is FileInfo?((FileInfo)part).Directory:((DirectoryInfo)part).Parent)
                if(part.Exists&&(part.Attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("PROCESS_ARTIFACT_REPARSE_POINT_UNSUPPORTED");
            return full;
        }
        internal static string RegistrationPayload(ProcessPackageRecord p,PlacementModelCapture model)
        {
            var r=p.Registration??throw new InvalidDataException("MISSING_WHOLE_SHEET_REGISTRATION");
            return ToolEvidenceCanonical.Hash("WholeSheetProcessRegistrationV1",p.Revision,model.SnapshotId,model.Configuration,
                model.UpdateStamp.ToString(System.Globalization.CultureInfo.InvariantCulture),PartSequenceSpace.TargetGeometryHash(model),
                ToolEvidenceCanonical.Hash(model.Bends.Select(b=>ToolEvidenceCanonical.Hash(b.FeatureId.ToString(),b.PersistentId,b.Frame.FrameId,
                    EnvelopeBinding.Vector(b.Frame.OriginMm),EnvelopeBinding.Vector(b.Frame.X),EnvelopeBinding.Vector(b.Frame.Y),EnvelopeBinding.Vector(b.Frame.BendAxis))).ToArray()),
                p.Artifacts?.SingleOrDefault(a=>a.Id==r.SourceArtifactId)?.Sha256,
                r.FrameId,EnvelopeBinding.Vector(Vector(r.OriginWorldMm)),EnvelopeBinding.Vector(Vector(r.X)),EnvelopeBinding.Vector(Vector(r.Y)),
                EnvelopeBinding.Vector(Vector(r.BendAxis)),ToolEvidenceCanonical.Number(Required(r.ErrorMm,"MISSING_REGISTRATION_ERROR_MM")),r.SourceArtifactId);
        }
        internal static ProcessMotionLaw Motion(ProcessMotionRecord r,double registrationError)
        {
            if(r==null)throw new InvalidDataException("MISSING_EXPLICIT_PROCESS_MOTION");
            var motion=new ProcessMotionLaw(Required(r.StartAngleRadians,"MISSING_PROCESS_ANGLE"),Required(r.EndAngleRadians,"MISSING_PROCESS_ANGLE"),
                Point(r.PivotMm),Point(r.StartTranslationMm),Point(r.EndTranslationMm),Required(r.StartAxialMm,"MISSING_AXIAL_MOTION"),
                Required(r.EndAxialMm,"MISSING_AXIAL_MOTION"),Required(r.PositionErrorMm,"MISSING_PROCESS_ERROR"),
                Required(r.AngleErrorRadians,"MISSING_PROCESS_ERROR"),Required(r.AxialErrorMm,"MISSING_PROCESS_ERROR"));
            if(!motion.Valid||motion.PositionErrorMm<registrationError||motion.AxialErrorMm<registrationError)
                throw new InvalidDataException("INVALID_MOTION_OR_UNACCOUNTED_REGISTRATION_ERROR");
            return motion;
        }
        internal static CollisionMaterial2D Material(ProcessMaterialRecord r,string frame,string provenance,bool simulation,Func<bool> current,double registrationError=0)
        {
            if(r==null)throw new InvalidDataException("MISSING_EXPLICIT_PROCESS_MATERIAL");
            var records=Bounded(r.Contours,128,"PROCESS_CONTOUR_LIMIT");
            if(records.Count==0||records.Select(c=>c.Id).Distinct().Count()!=records.Count)throw new InvalidDataException("AMBIGUOUS_PROCESS_CONTOUR_ID");
            var contours=new Dictionary<string,CollisionContour2D>(StringComparer.Ordinal);int count=0;
            foreach(var contour in records)
            {
                var curves=Bounded(contour.Curves,8192,"PROCESS_CURVE_LIMIT");count+=curves.Count;if(count>8192)throw new InvalidDataException("PROCESS_CURVE_LIMIT");
                var analytic=curves.Select(c=>c.Kind=="LINE"?ToolCurve.Line(Point(c.StartMm),Point(c.EndMm),new ToolTransform()):c.Kind=="ARC"?
                    ToolCurve.Arc(Point(c.StartMm),Point(c.EndMm),Point(c.CenterMm),Required(c.RadiusMm,"MISSING_PROCESS_ARC_RADIUS"),
                        Required(c.SweepRadians,"MISSING_PROCESS_ARC_SWEEP"),new ToolTransform()):throw new InvalidDataException("UNSUPPORTED_PROCESS_CURVE_NO_APPROXIMATION")).ToArray();
                if(string.IsNullOrWhiteSpace(contour.Id))throw new InvalidDataException("MISSING_PROCESS_CONTOUR_ID");
                contours.Add(contour.Id,new CollisionContour2D(contour.Id,analytic,Required(contour.UncertaintyMm,"MISSING_BOUNDARY_UNCERTAINTY")+registrationError));
            }
            var regions=Bounded(r.Regions,128,"PROCESS_MATERIAL_REGION_LIMIT");var used=new List<string>();
            var components=regions.Select(region=>{
                if(region.HoleIds==null||region.HoleIds.Distinct().Count()!=region.HoleIds.Length)throw new InvalidDataException("MISSING_OR_DUPLICATE_HOLE_ROLES");
                var ids=new[]{region.OuterId}.Concat(region.HoleIds).ToArray();
                if(ids.Any(id=>id==null||!contours.ContainsKey(id)))throw new InvalidDataException("UNBOUND_PROCESS_MATERIAL_ROLE");used.AddRange(ids);
                return new CollisionComponent2D(contours[region.OuterId],region.HoleIds.Select(id=>contours[id]));
            }).ToArray();
            if(used.Count!=contours.Count||used.Distinct().Count()!=used.Count)throw new InvalidDataException("UNASSIGNED_OR_REUSED_PROCESS_BOUNDARY");
            var material=new CollisionMaterial2D(frame,components,provenance,simulation,current);
            if(!material.Valid)throw new InvalidDataException(material.InvalidReason);return material;
        }
        internal static ProcessProgramImport Read(string path,string sourceKey,PlacementModelCapture model,ToolProfileLibrarySnapshot library,
            ToolReviewVerifier verifier,CancellationToken token)
        {
            if(model==null)throw new ArgumentNullException(nameof(model));
            verifier=verifier??ToolReviewVerifier.Load(null,token);
            var lease=new PlacementLease();var files=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            var payloads=new Dictionary<string,string>();var plans=new List<BendProcessPlan>();var sheets=new List<SheetEnvelopeCapture>();
            var setups=new List<ToolAxialSetupEvidence>();string sha=null,revision=null;var reasons=new List<string>();PartSequenceSpace sequence=null;AssemblyProgramUse use=null;
            try
            {
                if(library==null||!model.CompleteBodyCoverage)throw new InvalidDataException("PROCESS_COMPLETE_NATIVE_MODEL_OR_TOOL_LIBRARY_MISSING");
                if(model.Bends.Any(b=>b.Frame==null))throw new InvalidDataException("MISSING_NATIVE_BEND_FRAME_FOR_PROCESS_REGISTRATION");
                path=Path.GetFullPath(path);string root=Path.GetDirectoryName(path);var bytes=PlacementSourceIo.Read(path,4*1024*1024,token);
                sha=PlacementSourceIo.Hash(bytes);files[path]=sha;var p=new System.Web.Script.Serialization.JavaScriptSerializer{MaxJsonLength=4*1024*1024,RecursionLimit=64}
                    .Deserialize<ProcessPackageRecord>(System.Text.Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF'));
                if(p==null||p.Version!=1||string.IsNullOrWhiteSpace(p.Revision)||p.DeclaredAlternativesComplete==null)throw new InvalidDataException("INVALID_PROCESS_PACKAGE_VERSION_OR_REVISION");revision=p.Revision;
                if(p.ModelSnapshotId!=model.SnapshotId||p.Configuration!=model.Configuration||p.ModelUpdateStamp!=model.UpdateStamp||
                    p.NativeGeometrySha256!=PartSequenceSpace.TargetGeometryHash(model))throw new InvalidDataException("STALE_PROCESS_NATIVE_MODEL_CONFIGURATION_OR_GEOMETRY");
                var bends=Bounded(p.Bends,1024,"PROCESS_BEND_LIMIT");
                if(bends.Count!=model.Bends.Count||bends.Select(b=>b.FeatureId).Distinct().Count()!=bends.Count||bends.Any(b=>!b.FeatureId.HasValue||
                    string.IsNullOrWhiteSpace(b.PersistentId)||!model.Bends.Any(n=>n.FeatureId==b.FeatureId&&n.PersistentId==b.PersistentId&&n.Frame.FrameId==b.NativeFrameId)))
                    throw new InvalidDataException("STALE_OR_AMBIGUOUS_NATIVE_FEATURE_PERSISTENT_IDENTITY");
                var artifacts=Bounded(p.Artifacts,128,"PROCESS_ARTIFACT_LIMIT");
                if(artifacts.Select(a=>a.Id).Distinct().Count()!=artifacts.Count||artifacts.Any(a=>string.IsNullOrWhiteSpace(a.Id)||!ToolDatumEvidence.IsSha256(a.Sha256)))throw new InvalidDataException("INVALID_PROCESS_ARTIFACT_IDENTITY");
                foreach(var a in artifacts)
                {
                    token.ThrowIfCancellationRequested();string full=SafePath(root,a.Path);string actual=PlacementSourceIo.Hash(PlacementSourceIo.Read(full,4*1024*1024,token));
                    if(!string.Equals(actual,a.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("STALE_PROCESS_ARTIFACT_SHA256:"+a.Id);files[full]=actual;
                }
                Func<string,string,string> artifact=(id,role)=>{
                    var a=artifacts.SingleOrDefault(x=>x.Id==id);if(a==null||a.Role!=role)throw new InvalidDataException("MISSING_PROCESS_ARTIFACT_ROLE:"+role);return a.Sha256;
                };
                Action<ToolReviewProof> proofFile=proof=>{
                    if(proof==null)return;string file=SafePath(root,proof.ArtifactPath);var actual=PlacementSourceIo.Hash(PlacementSourceIo.Read(file,4*1024*1024,token));
                    if(!string.Equals(actual,proof.ArtifactSha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("STALE_PROCESS_REVIEW_ARTIFACT");files[file]=actual;
                };
                var r=p.Registration??throw new InvalidDataException("MISSING_WHOLE_SHEET_REGISTRATION");
                artifact(r.SourceArtifactId,"RegisteredGeometry");double error=Required(r.ErrorMm,"MISSING_REGISTRATION_ERROR_MM");
                if(error<0||error>.001||string.IsNullOrWhiteSpace(r.FrameId))throw new InvalidDataException("INVALID_REGISTERED_PROCESS_FRAME_OR_ERROR");
                string registration=RegistrationPayload(p,model);payloads["Registration"]=registration;proofFile(r.Review);
                var registrationReview=verifier.Verify("WholeSheetProcessRegistration",registration,r.Review,root,token);
                if(!registrationReview.RecordVerified)throw new InvalidDataException("UNVERIFIED_WHOLE_SHEET_PROCESS_REGISTRATION:"+registrationReview.Reason);
                var frame=new BendWorkFrame(r.FrameId,Vector(r.OriginWorldMm),Vector(r.X),Vector(r.Y),Vector(r.BendAxis));
                if(model.Bends.Any(b=>Math.Abs(Math.Abs(PlacementVector3.Dot(b.Frame.BendAxis,frame.BendAxis))-1)>1e-12))
                    throw new InvalidDataException("UNSUPPORTED_NONPARALLEL_BEND_REGISTRATION_2D");
                var geometry=Bounded(p.Geometry,256,"PROCESS_GEOMETRY_LIMIT");
                if(geometry.Select(g=>g.Id).Distinct().Count()!=geometry.Count)throw new InvalidDataException("AMBIGUOUS_PROCESS_GEOMETRY_ID");
                var materialCache=new Dictionary<Tuple<ProcessMaterialRecord,string,bool>,CollisionMaterial2D>();
                Func<ProcessPrismRecord,bool,CollisionMaterial2D> material=(g,simulation)=>{
                    string origin=artifact(g.SourceArtifactId,"RegisteredGeometry");var key=Tuple.Create(g.Material,origin,simulation);CollisionMaterial2D cached;
                    if(!materialCache.TryGetValue(key,out cached))materialCache.Add(key,cached=Material(g.Material,frame.FrameId,origin,simulation,
                        ()=>model.Lease.Current&&lease.Current&&registrationReview.RecordVerified,error));return cached;
                };
                var sheetCache=new Dictionary<Tuple<int,ProcessPrismRecord>,SheetEnvelopeCapture>();
                var transitions=Bounded(p.Transitions,2048,"PROCESS_TRANSITION_LIMIT");var edges=new List<SequenceTransition>();
                foreach(var t in transitions)
                {
                    token.ThrowIfCancellationRequested();var bend=model.Bends.SingleOrDefault(b=>b.FeatureId==t.FeatureId&&b.PersistentId==t.PersistentId);
                    if(bend==null||!CompanyToolIds.All.Contains(t.ToolId))throw new InvalidDataException("PROCESS_TRANSITION_NATIVE_IDENTITY_OR_TOOL_MISMATCH");
                    var binding=EnvelopeBinding.Registered(model,bend,registration,frame,registrationReview,()=>lease.Current);
                    var local=new Dictionary<string,SheetEnvelopeCapture>();
                    Func<ProcessPrismRecord,SheetEnvelopeCapture> prism=g=>{
                        if(g==null||g.SectionContract!="ExplicitAnalyticPrismOverEntireAxialInterval")throw new InvalidDataException("UNSUPPORTED_PROCESS_GEOMETRY_CONTRACT");
                        var key=Tuple.Create(bend.FeatureId,g);SheetEnvelopeCapture cached;if(sheetCache.TryGetValue(key,out cached))return cached;
                        double tol=Required(g.ToleranceMm,"MISSING_PROCESS_SOLID_ERROR");if(tol<error||tol<0||tol>.1)throw new InvalidDataException("INVALID_PROCESS_SOLID_ERROR");
                        var mat=material(g,registrationReview.SimulationOnly||model.SimulationOnly);
                        cached=new SheetEnvelopeCapture(binding,new[]{new AxialPrismaticSweep(new AxialInterval(Required(g.StartMm,"MISSING_PROCESS_AXIAL_SPAN"),Required(g.EndMm,"MISSING_PROCESS_AXIAL_SPAN")),mat)},
                            EnvelopeCoverageTier.Uniform2DProven,"ExplicitReviewedProcessPrismV1",mat.SimulationOnly,tol,new string[0]);
                        sheetCache.Add(key,cached);return cached;
                    };
                    Func<ProcessToolRecord,SheetEnvelopeCapture,CollisionPairKind,ProcessToolPose> tool=(record,sheet,pair)=>{
                        if(record==null)return null;var law=Motion(record.Motion,error);
                        if(record.AxialSetup!=null&&record.SimulationGeometry!=null)throw new InvalidDataException("AMBIGUOUS_PROCESS_TOOL_SOURCE");
                        if(record.AxialSetup!=null)
                        {
                            if(record.AxialSetup.ToolId!=t.ToolId||record.AxialSetup.FeatureId!=t.FeatureId||record.AxialSetup.Pair!=pair.ToString())throw new InvalidDataException("MISMATCHED_PROCESS_TOOL_SETUP_IDENTITY");
                            proofFile(record.AxialSetup.Review);var setup=ToolAxialSetupEvidence.Read(record.AxialSetup,sheet,library,verifier,root,token);setups.Add(setup);
                            return ProcessToolPose.FromEvidence(setup,sheet,library,law);
                        }
                        if(record.SimulationGeometry==null)return null;
                        var fixture=prism(record.SimulationGeometry);string payload=ToolEvidenceCanonical.Hash("ImportedSimulationToolV1",t.ToolId,pair.ToString(),registration,fixture.GeometrySha256);
                        payloads[t.Id+"/"+pair+"/"+record.SimulationGeometry.Id]=payload;proofFile(record.SimulationReview);
                        var review=verifier.Verify("SimulationProcessTool",payload,record.SimulationReview,root,token);
                        bool approvedSimulation=review.RecordVerified&&review.SimulationOnly;
                        var nominal=material(record.SimulationGeometry,true);
                        var source=new AxialToolOccupancy(nominal,new[]{fixture.Domain},fixture.Certificate.SolidToleranceMm,payload,true,
                            ()=>fixture.Current&&review.Status!=ToolReviewStatus.Stale,approvedSimulation?new string[0]:new[]{"SIMULATION_TOOL_REQUIRES_SIGNED_SIMULATION_ONLY_PROOF"});
                        string identity=ToolEvidenceCanonical.Hash("ImportedSimulationHardwareV1",t.ToolId,pair.ToString(),registration,fixture.ShapeSha256);
                        return ProcessToolPose.ImportedSimulation(source,law,payload,identity);
                    };
                    var segments=new List<BendMotionSegment>();
                    foreach(var s in Bounded(t.Segments,64,"PROCESS_SEGMENT_LIMIT"))
                    {
                        var panels=Bounded(s.Panels,32,"PROCESS_PANEL_LIMIT").Select(panel=>{
                            SheetEnvelopeCapture source;if(!local.TryGetValue(panel.GeometryId,out source))
                            {var g=geometry.SingleOrDefault(x=>x.Id==panel.GeometryId);source=prism(g);local.Add(panel.GeometryId,source);}
                            var definition=geometry.Single(g=>g.Id==panel.GeometryId);
                            var exact=material(definition,registrationReview.SimulationOnly||model.SimulationOnly);
                            return ProcessSheetPart.RegisteredPrism(panel.Id,source,Motion(panel.Motion,error),Kind<ProcessGeometryRole>(panel.Role),Kind<ProcessGeometryFidelity>(panel.Fidelity),exact,source.Domain);
                        }).ToArray();
                        if(panels.Length==0)throw new InvalidDataException("EMPTY_PROCESS_PANEL_STATE");
                        var phase=Kind<BendProcessPhase>(s.Phase);
                        var contacts=Bounded(s.Contacts??new List<ProcessContactRecord>(),32,"PROCESS_CONTACT_LIMIT").Select(c=>{
                            if(!c.NormalIsX.HasValue||!c.SheetPositiveSide.HasValue||!ToolDatumEvidence.IsSha256(c.SheetGeometrySha256)||!ToolDatumEvidence.IsSha256(c.ToolSetupSha256))
                                throw new InvalidDataException("MISSING_PROCESS_CONTACT_BINDING");
                            return new PlanarFormingContact(c.PartId,c.SheetGeometrySha256,c.ToolSetupSha256,artifact(c.EvidenceArtifactId,"FormingContact"),phase,
                                c.NormalIsX.Value,c.SheetPositiveSide.Value,Required(c.PlaneMm,"MISSING_CONTACT_PLANE"),Required(c.TangentMinMm,"MISSING_CONTACT_ZONE"),
                                Required(c.TangentMaxMm,"MISSING_CONTACT_ZONE"),new AxialInterval(Required(c.AxialStartMm,"MISSING_CONTACT_AXIAL_SPAN"),Required(c.AxialEndMm,"MISSING_CONTACT_AXIAL_SPAN")));
                        }).ToArray();
                        segments.Add(new BendMotionSegment(s.Id,phase,panels,tool(s.Punch,panels[0].Source,CollisionPairKind.WorkpiecePunch),
                            tool(s.Holder,panels[0].Source,CollisionPairKind.WorkpieceHolder),artifact(s.ConstraintsArtifactId,"MachineConstraints"),contacts));
                    }
                    proofFile(t.Review);var plan=BendProcessPlan.Reviewed(binding,t.ToolId,artifact(t.ProcessArtifactId,"ProcessSpecification"),segments,library,verifier,t.Review,root,token);
                    payloads[t.Id]=plan.PayloadSha256;plans.Add(plan);sheets.AddRange(local.Values);reasons.AddRange(plan.InputReasons);
                    edges.Add(new SequenceTransition(t.Id,t.BeforeStateId,t.AfterStateId,artifact(t.SetupArtifactId,"SetupConditions"),plan));
                }
                var states=Bounded(p.States,4096,"PROCESS_STATE_LIMIT").Select(s=>{
                    if(s.CompletedFeatureIds==null)throw new InvalidDataException("MISSING_COMPLETED_NATIVE_FEATURE_SET");return new SequenceSheetState(s.Id,s.GeometryPoseSha256,s.CompletedFeatureIds);
                }).ToArray();
                proofFile(p.GraphReview);sequence=PartSequenceSpace.Reviewed(model,artifact(p.SpecificationArtifactId,"ProcessSpecification"),p.Revision,p.InitialStateId,states,edges,
                    p.DeclaredAlternativesComplete.Value,verifier,p.GraphReview,root,token);payloads["PartSequenceSpace"]=sequence.PayloadSha256;reasons.AddRange(sequence.Reasons);
                // A source graph may contain several intermediate sheet states.
                // Do not feed those as the single observed final envelope to the
                // static worker or collapse all alternative plans into one plan.
                var program=new AssemblySourceInput(sourceKey,model,new[]{new AssemblyOccurrence("IMPORT",null,"IMPORT",sourceKey,model.Configuration,
                    new double[]{1,0,0,0,1,0,0,0,1,0,0,0,1},AssemblyOccurrenceKind.SheetMetal,true)},conditionsRevision:revision,sequence:sequence);
                payloads["AssemblyPartLocalProgramUse"]=AssemblyProgramUse.Payload(sourceKey,AssemblyCacheKeys.Program(program));proofFile(p.PartLocalUseReview);
                use=AssemblyProgramUse.Reviewed(sourceKey,AssemblyCacheKeys.Program(program),verifier,p.PartLocalUseReview,root,token);
            }
            catch(OperationCanceledException){lease.Invalidate();throw;}
            catch(Exception ex) when(ex is InvalidDataException||ex is IOException||ex is UnauthorizedAccessException||ex is ArgumentException||ex is InvalidOperationException||ex is FormatException||ex is KeyNotFoundException)
            {reasons.Add(ex is InvalidDataException?ex.Message:"INVALID_PROCESS_PACKAGE:"+ex.GetType().Name);sequence=null;}
            if(sequence==null)sequence=PartSequenceSpace.Missing(model,reasons.ToArray());
            return new ProcessProgramImport(lease,sequence,plans,sheets,setups,use,revision,sha,reasons,files,payloads);
        }
    }
}
