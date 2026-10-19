using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using ADDIN.Commands.BendPlacement;
using ADDIN.Commands.BendTools;
using ADDIN.Commands.BendPlanning;
using ADDIN.Commands.BendCollision;
using ADDIN.Commands.BendEnvelope;
using ADDIN.Commands.BendProcess;

internal static partial class ToolProfileTests
{
    private static MasterFixture CadPairs()
    {
        var f=new MasterFixture {Root=Folder()};f.Source=Path.Combine(f.Root,"pairs.dxf");f.Manifest=Path.Combine(f.Root,"pairs.json");
        var blocks=new StringBuilder();var entities=new StringBuilder();var definitions=new List<MasterToolSelection>();
        // Reused definitions, different INSERT instances, a reference line and
        // a NURBS: none may disappear merely because it cannot be filled yet.
        string spline=Tags(0,"SPLINE",5,"S",8,"CLAMP_REFERENCE",70,8,71,2,72,6,73,3,74,0,40,0,40,0,40,0,40,1,40,1,40,1,
            10,0,20,0,30,0,10,1,20,2,30,0,10,3,20,0,30,0,210,0,220,0,230,1);
        blocks.Append(Block("H_SHARED",Poly(handle:"H")+Arc()+spline+Line(0,0,0,5,"REFERENCE","FRAME_REFERENCE"),2,3));
        for(int i=0;i<18;i++)
        {
            string id=CompanyToolIds.All[i],block="TOOL_"+id.Replace('-','_'),p="IP"+i,h="IH"+i,frame="F"+i;double x=i*100;
            blocks.Append(Block(block,Poly(i!=17,handle:"P"+i)));
            entities.Append(Tags(0,"INSERT",5,p,8,"PUNCH",2,block,10,x+10,20,10));
            entities.Append(Tags(0,"INSERT",5,h,8,"HOLDER",2,"H_SHARED",10,x+40,20,30,41,-1,42,-1,50,30));
            entities.Append(Tags(0,"LWPOLYLINE",5,frame,8,"BOX",90,4,70,1,10,x,20,0,10,x+90,20,0,10,x+90,20,90,10,x,20,90));
            entities.Append(Tags(0,"TEXT",5,"L"+i,8,"LABEL",1,id));
            definitions.Add(new MasterToolSelection {Id=id,ExpectedLabel=id,LabelHandle="L"+i,PunchRootInsert=p,PunchBlock=block,
                HolderRootInsert=h,HolderBlock="H_SHARED",Punch=new ToolProfileSelection {Blocks=new[]{block}},Holder=new ToolProfileSelection {Blocks=new[]{"H_SHARED"}},
                PresentationPair=new CadPresentationPairRecord {SlotId="Slot"+i,AssociationBasis="CompanyAuthorizedDrawingSlot",EvidenceReference="SYNTHETIC_AUTHORIZATION",
                    FrameHandle=frame,FrameLayer="BOX",Punch=new CadPresentationRoleRecord {RootLayer="PUNCH",Layers=new[]{"PUNCH"},EntityCount=1},
                    Holder=new CadPresentationRoleRecord {RootLayer="HOLDER",Layers=new[]{"HOLDER","CLAMP_REFERENCE","FRAME_REFERENCE"},EntityCount=4}}});
        }
        string dxf=Dxf(entities.ToString(),blocks.ToString());Save(f.Root,"pairs.dxf",dxf);
        f.Mapping=new MasterAssemblyManifest {Version=2,MasterSha256=ToolProfileLibraryProvider.Hash(Encoding.UTF8.GetBytes(dxf)),Tools=definitions};
        Save(f.Root,"pairs.json",Json.Serialize(f.Mapping));return f;
    }
    private static void PairManifest(MasterFixture f)=>Save(f.Root,"pairs.json",Json.Serialize(f.Mapping));
    private static ToolProfileLibraryProvider PairProvider(MasterFixture f)
    {
        Save(f.Root,"placement.json",Json.Serialize(new BendPlacementConfiguration {Version=1,MasterPath=f.Source,MasterManifestPath=f.Manifest}));
        return new ToolProfileLibraryProvider(()=>new ToolProfileSourceConfiguration {UseMasterPairsAsLibrary=true,PlacementConfigurationPath=Path.Combine(f.Root,"placement.json")});
    }
    internal static ToolProfileLibrarySnapshot CadPairUiLibrary()
    {
        if(temporary==null){temporary=Path.Combine(Path.GetTempPath(),"ADDIN-CadPairUi-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temporary);}
        return PairProvider(CadPairs()).Read(CancellationToken.None);
    }
    private static void CompanyCadPairTests()
    {
        Test("CAD fixed pairs retain all 18 exact instances without physical promotion",()=>{
            var f=CadPairs();var r=Read(f);Check(r.Tools.Count==18&&r.Tools.All(p=>p.MappingIdentified&&p.CadPair.Matched),"18/18 fixed slots");
            Check(r.Tools.Select(p=>p.CadPair.HolderInsert).Distinct().Count()==18,"Reused holder definitions remain distinct instances");
            Check(r.Tools.All(p=>!p.ClampVerified&&!p.GeometryCheckReady&&p.CadPair.ReferenceOnly&&p.SeatingEvidence.TipPointMm==null),"No seating inferred from drawing positions");
            Check(r.Tools.All(p=>p.CadPair.CoordinateSystem=="DXF_PRESENTATION_WCS_ONLY"),"Explicit presentation frame contract");
        });
        Test("CAD same-block holder permutation is rejected by fixed source slots",()=>{
            var f=CadPairs();string h=f.Mapping.Tools[0].HolderRootInsert;f.Mapping.Tools[0].HolderRootInsert=f.Mapping.Tools[1].HolderRootInsert;f.Mapping.Tools[1].HolderRootInsert=h;PairManifest(f);
            var r=Read(f);Check(r.Tools.Take(2).All(p=>!p.MappingIdentified&&!p.CadPair.Matched&&p.Reasons.Contains("CAD_HOLDER_OUTSIDE_FIXED_SLOT")),"Unique handles and same block/layers cannot disguise wrong pairs");
            Check(r.Tools.Skip(2).All(p=>p.CadPair.Matched),"Unrelated pairs remain identified");
        });
        Test("CAD duplicate holder INSERT and duplicate slots fail closed",()=>{
            var f=CadPairs();f.Mapping.Tools[1].HolderRootInsert=f.Mapping.Tools[0].HolderRootInsert;PairManifest(f);
            Check(Read(f).Tools.Take(2).All(p=>!p.MappingIdentified&&!p.CadPair.Matched),"Shared physical instance rejected in both slots");
            f=CadPairs();f.Mapping.Tools[1].PresentationPair.SlotId=f.Mapping.Tools[0].PresentationPair.SlotId;PairManifest(f);
            Check(Read(f).Tools.All(p=>!p.MappingIdentified),"Duplicate authored slot rejected");
        });
        Test("CAD correct label cannot validate a different complete Punch Holder and cell",()=>{
            var f=CadPairs();var a=f.Mapping.Tools[0];var b=f.Mapping.Tools[1];
            a.PunchRootInsert=b.PunchRootInsert;a.PunchBlock=b.PunchBlock;a.Punch=b.Punch;
            a.HolderRootInsert=b.HolderRootInsert;a.HolderBlock=b.HolderBlock;a.Holder=b.Holder;
            a.PresentationPair=b.PresentationPair;a.PresentationPair.SlotId="ALTERED_SLOT";
            // Remove the second definition's overlap so it is the intrinsic
            // ID-to-block binding, not the duplicate-root check, that rejects.
            b.PunchRootInsert="MISSING_P";b.HolderRootInsert="MISSING_H";b.PresentationPair=new CadPresentationPairRecord {SlotId="MISSING",FrameHandle="MISSING"};PairManifest(f);
            var changed=Read(f).Tools[0];Check(!changed.MappingIdentified&&changed.Reasons.Contains("CAD_PUNCH_BLOCK_TOOL_ID_MISMATCH"),"No ID spoofing via an unrelated cell");
        });
        Test("CAD block root layer and full entity inventory are mandatory",()=>{
            foreach(int mode in new[]{0,1,2,3}){
                var f=CadPairs();var t=f.Mapping.Tools[0];
                if(mode==0)t.HolderBlock="OTHER";if(mode==1)t.PresentationPair.Holder.RootLayer="OTHER";
                if(mode==2)t.Holder.EntityTypes=new[]{"LWPOLYLINE"};if(mode==3)t.PresentationPair.Holder.Layers=new[]{"HOLDER"};PairManifest(f);
                Check(!Read(f).Tools[0].CadPair.Matched,"No partial role, omitted SPLINE or incorrect layer/block accepted "+mode);
            }
        });
        Test("CAD source primitives and original transforms survive library ingestion",()=>{
            var f=CadPairs();var r=Read(f);var raw=Parse(File.ReadAllText(f.Source));var pair=r.Tools[0];
            var expected=raw.Entities.Where(e=>e.InsertHandles.FirstOrDefault()=="IH0"&&!e.Annotation).ToArray();
            Check(pair.Holder.SourceEntities.Count==4&&pair.Holder.SourceEntities.Any(e=>e.Type=="SPLINE"&&e.Spline!=null),"NURBS retained, not dropped or replaced");
            foreach(var e in pair.Holder.SourceEntities){var original=expected.Single(o=>o.Handle==e.Handle);var q=P(1.25,-2.5);
                Point(e.Transform.Apply(q),original.Transform.Apply(q).X,original.Transform.Apply(q).Y);
                Check(e.Curves.Count==original.Curves.Count&&e.SourceClosed==original.SourceClosed,"Curves and polyline flag preserved");
                if(e.Spline!=null)Check(e.Spline.SourceControlPoints.SequenceEqual(original.Spline.SourceControlPoints)&&e.Spline.Knots.SequenceEqual(original.Spline.Knots),"Exact source spline arrays");}
            var lib=PairProvider(f).Read(CancellationToken.None);Check(lib.Sources.Count==1&&lib.Tools.All(t=>t.Parts.Count==2),"Punch and holder use the same Master, no DIM fallback");
            Check(lib.Tools[17].Validation.PunchGeometryReadComplete&&!lib.Tools[17].Validation.PunchClosed&&!lib.Tools[17].Validation.Region.IsValidRegion,"Open punch remains open and invalid as material");
        });
        Test("CAD source change invalidates held library and cannot remap by proximity",()=>{
            var f=CadPairs();var provider=PairProvider(f);var a=provider.Read(CancellationToken.None);
            File.AppendAllText(f.Source,"\n");var b=provider.Read(CancellationToken.None);
            Check(!a.Current&&b.Fingerprint!=a.Fingerprint&&b.PlacementSources.Tools.All(p=>!p.MappingIdentified),"Source SHA mismatch invalidates all held pairs");
            Check(b.PlacementSources.Reasons.Contains("STALE_MASTER_MAPPING"),"Explicit stale reason");
        });
        Test("CAD pair candidates keep 18 per bend and no final-part or process winner",()=>{
            var f=CadPairs();var lib=PairProvider(f).Read(CancellationToken.None);var model=Model(Extrusion(Square().Curves),3);
            var draft=BendPlacementBuilder.Build(model,lib);Check(draft.Candidates.Count==54,"18 x 3 candidates");
            Check(draft.Candidates.All(c=>c.Assembly.CadPair.Matched&&c.PunchPlacement.ToWorkFrame==null&&c.HolderPlacement.ToWorkFrame==null),"Pair identity available without inventing a machine frame");
            Check(draft.Candidates.All(c=>!c.ReadyForCollisionCheck&&c.Collision.CombinedResult==ADDIN.Commands.BendCollision.CollisionStatus.UNKNOWN)&&draft.WinnerToolId==null,"No manufacturing/tool-selection promotion");
        });
        Test("CAD authored association is independent of manifest order",()=>{
            var f=CadPairs();f.Mapping.Tools.Reverse();PairManifest(f);var r=Read(f);
            Check(r.Tools.All(p=>p.CadPair.Matched&&p.CadPair.PunchInsert=="IP"+CompanyToolIds.All.ToList().IndexOf(p.Id)),"Exact root identity, no list-order pairing");
        });
        Test("CAD source-bound reviewed simulation executes real numeric collision without mounting certification",()=>{
            var f=CadPairs();var library=PairProvider(f).Read(CancellationToken.None);
            var sheet=Volume(Prism(Material(Rect("SHEET",0,0,1,1))));
            using(var review=Reviews()){
                review.Authority.Purposes=review.Authority.Purposes.Concat(new[]{"CadReferenceSimulation"}).ToArray();review.Reload();
                foreach(bool separated in new[]{false,true}){
                    var p=library.PlacementSources.Tools[0];var r=new CadReferenceSimulationRecord {SourceSha256=p.MasterSha256,ManifestSha256=p.ManifestSha256,
                        SelectionGeometrySha256=p.Punch.GeometrySha256,AxialSectionContract="ExplicitSimulationUniformProfileOverListedSpan",StartMm=-9,EndMm=9,
                        BoundaryErrorMm=1e-8,AxialErrorMm=1e-8,SourceToWorkFrame=new PairTransformRecord {A=1,B=0,C=0,D=1,TxMm=separated?100:-10,TyMm=separated?100:-10}};
                    r.Review=review.Sign("CadReferenceSimulation",CadReferenceSimulation.Payload(r,p.Id,CollisionPairKind.WorkpiecePunch,sheet));
                    var pose=CadReferenceSimulation.Read(r,p.Id,CollisionPairKind.WorkpiecePunch,sheet,library,new ProcessMotionLaw(),review.Verifier,review.Root,CancellationToken.None);
                    var result=AxialCollisionEngine.Evaluate(new AxialCollisionQuery(sheet,pose.Source,CollisionPairKind.WorkpiecePunch));
                    VolumeExpect(result,separated?CollisionStatus.CLEAR:CollisionStatus.COLLISION);
                    Check(pose.Source.SimulationOnly&&!p.ClampVerified&&p.SeatingEvidence.TipPointMm==null,"No inferred/certified physical datum or seating");
                    Check(p.Punch.SourceEntities[0].Curves[0].Start.X==10,"Source was not moved by the simulation pose");
                    r.SourceToWorkFrame.TxMm+=1;bool rejected=false;
                    try{CadReferenceSimulation.Read(r,p.Id,CollisionPairKind.WorkpiecePunch,sheet,library,new ProcessMotionLaw(),review.Verifier,review.Root,CancellationToken.None);}catch(InvalidDataException){rejected=true;}
                    Check(rejected,"Pose cannot change under the same review");
                }
            }
        });
        Test("CAD simulation rejects missing review, stale source, open punch and unclassified holder",()=>{
            var f=CadPairs();var library=PairProvider(f).Read(CancellationToken.None);var sheet=Volume(Prism(Material(Rect("SHEET",0,0,1,1))));
            using(var review=Reviews()){
                review.Authority.Purposes=review.Authority.Purposes.Concat(new[]{"CadReferenceSimulation"}).ToArray();review.Reload();
                foreach(int mode in new[]{0,1,2,3}){
                    var p=library.PlacementSources.Tools[mode==2?17:0];var pair=mode==3?CollisionPairKind.WorkpieceHolder:CollisionPairKind.WorkpiecePunch;
                    var r=new CadReferenceSimulationRecord {SourceSha256=p.MasterSha256,ManifestSha256=p.ManifestSha256,
                        SelectionGeometrySha256=(mode==3?p.Holder:p.Punch).GeometrySha256,AxialSectionContract="ExplicitSimulationUniformProfileOverListedSpan",StartMm=-9,EndMm=9,
                        BoundaryErrorMm=0,AxialErrorMm=0,SourceToWorkFrame=new PairTransformRecord {A=1,B=0,C=0,D=1,TxMm=0,TyMm=0}};
                    if(mode!=0)r.Review=review.Sign("CadReferenceSimulation",CadReferenceSimulation.Payload(r,p.Id,pair,sheet));
                    if(mode==1)r.SourceSha256=new string('1',64);bool rejected=false;
                    try{CadReferenceSimulation.Read(r,p.Id,pair,sheet,library,new ProcessMotionLaw(),review.Verifier,review.Root,CancellationToken.None);}catch(InvalidDataException){rejected=true;}
                    Check(rejected,"Unproven geometry/pose not made usable "+mode);
                }
            }
        });
        Test("CAD common-tool hardware identity survives distinct native bend bindings",()=>{
            var library=PairProvider(CadPairs()).Read(CancellationToken.None);var p=library.PlacementSources.Tools[0];var model=CollisionModel(Rect("SHEET",0,0,1,1),2);
            using(var review=Reviews()){
                review.Authority.Purposes=review.Authority.Purposes.Concat(new[]{"CadReferenceSimulation"}).ToArray();review.Reload();
                var poses=new List<ProcessToolPose>();
                foreach(var bend in model.Bends){var sheet=ProcessPanel(model,bend,"SHEET",Material(Rect("SHEET",0,0,1,1)),new ProcessMotionLaw()).Source;
                    var r=new CadReferenceSimulationRecord {SourceSha256=p.MasterSha256,ManifestSha256=p.ManifestSha256,SelectionGeometrySha256=p.Punch.GeometrySha256,
                        AxialSectionContract="ExplicitSimulationUniformProfileOverListedSpan",StartMm=-9,EndMm=9,BoundaryErrorMm=0,AxialErrorMm=0,
                        SourceToWorkFrame=new PairTransformRecord {A=1,B=0,C=0,D=1,TxMm=-10,TyMm=-10}};
                    r.Review=review.Sign("CadReferenceSimulation",CadReferenceSimulation.Payload(r,p.Id,CollisionPairKind.WorkpiecePunch,sheet));
                    poses.Add(CadReferenceSimulation.Read(r,p.Id,CollisionPairKind.WorkpiecePunch,sheet,library,new ProcessMotionLaw(),review.Verifier,review.Root,CancellationToken.None));}
                Check(poses.Select(t=>t.SetupSourceSha256).Distinct().Count()==2,"Native bend reviews remain independent");
                Check(poses.Select(t=>t.AssemblyCanonical).Distinct().Count()==1,"Identical source pair and placed volume can form a common-tool route");
            }
        });
        Test("CAD Holder simulation uses reviewed material roles without claiming physical seating",()=>{
            var library=PairProvider(CadPairs()).Read(CancellationToken.None);var original=library.PlacementSources.Tools[0];
            using(var review=Reviews()){
                review.Authority.Purposes=review.Authority.Purposes.Concat(new[]{"CadReferenceSimulation"}).ToArray();review.Reload();
                string solid=original.Holder.EntityKeys.Single(k=>k.EndsWith("|H",StringComparison.Ordinal));
                var definition=new HolderMaterialDefinition {ToolId=original.Id,BindingSha256=HolderObstacleSet.Binding(original.Id,original.Punch,original.Holder,original.ManifestSha256),
                    Contours=new List<HolderContourDefinition>{new HolderContourDefinition {Id="DECLARED_SYNTHETIC_SOLID",MaterialKind="Solid",EntityKeys=new[]{solid}}},
                    Excluded=original.Holder.EntityKeys.Where(k=>k!=solid).Select(k=>new HolderExcludedEntity {EntityKey=k,Role="Reference"}).ToList()};
                definition.Review=review.Sign("HolderMaterialRule",HolderObstacleSet.Payload(definition));
                var set=HolderObstacleSet.Read(original.Id,original.Punch,original.Holder,original.ManifestSha256,definition,review.Verifier,review.Root,CancellationToken.None);
                Check(set.GeometrySupportedForCollision,"Explicitly reviewed synthetic holder roles: "+string.Join(";",set.Reasons));
                var replacement=new ToolAssemblyProfile(original.Id,original.MasterSha256,original.ManifestSha256,original.Punch,original.Holder,original.MasterDatums,true,original.Reasons,
                    set,original.SeatingEvidence,original.MappingReview,original.CadPair,original.Source);
                library.PlacementSources=new PlacementSourceSnapshot("CONTROLLED_REVIEWED_MATERIAL",library.PlacementSources.Tools.Select(p=>p.Id==original.Id?replacement:p),null,new string[0]);
                var entity=original.Holder.SourceEntities.Single(e=>e.Handle=="H");var center=entity.Transform.Apply(P(2,2.0/3));
                var sheet=Volume(Prism(Material(Rect("SHEET",center.X-.1,center.Y-.1,.2,.2))));
                var r=new CadReferenceSimulationRecord {SourceSha256=original.MasterSha256,ManifestSha256=original.ManifestSha256,SelectionGeometrySha256=original.Holder.GeometrySha256,
                    AxialSectionContract="ExplicitSimulationUniformProfileOverListedSpan",StartMm=-9,EndMm=9,BoundaryErrorMm=1e-8,AxialErrorMm=1e-8,
                    SourceToWorkFrame=new PairTransformRecord {A=1,B=0,C=0,D=1,TxMm=0,TyMm=0}};
                r.Review=review.Sign("CadReferenceSimulation",CadReferenceSimulation.Payload(r,original.Id,CollisionPairKind.WorkpieceHolder,sheet));
                var pose=CadReferenceSimulation.Read(r,original.Id,CollisionPairKind.WorkpieceHolder,sheet,library,new ProcessMotionLaw(),review.Verifier,review.Root,CancellationToken.None);
                VolumeExpect(AxialCollisionEngine.Evaluate(new AxialCollisionQuery(sheet,pose.Source,CollisionPairKind.WorkpieceHolder)),CollisionStatus.COLLISION);
                Check(pose.Source.SimulationOnly&&!replacement.GeometryCheckReady&&!replacement.ClampVerified,"Simulation has no manufactured mounting certificate");
                set.Invalidate();Check(!pose.Current,"Revoked holder material withdraws held simulation");
            }
        });
        Test("Phase 4B cannot replace fixed CAD pairs with unrelated simulation geometry",()=>{
            using(var f=ImportedPackage()){
                f.Library=PairProvider(CadPairs()).Read(CancellationToken.None);var imported=f.Read();
                Check(imported.Reasons.Contains("FIXED_CAD_PAIR_REQUIRES_SOURCE_BOUND_SIMULATION_GEOMETRY"),"Importer names the rejected substitute source");
                using(var planner=new PartSequencePlanner()){var result=planner.Run("PART",f.Model,f.Library,imported.Sequence);
                    Check(result.Tools.Count==18&&result.CommonToolIds.Count==0&&result.WinnerToolId==null,"18 preserved, no invented common-tool solution");}
            }
        });
        Test("Phase 4B missing fixed catalog cannot silently enable legacy replacement tools",()=>{
            using(var f=ImportedPackage()){
                f.Library=new ToolProfileLibraryProvider(()=>new ToolProfileSourceConfiguration {UseMasterPairsAsLibrary=true,
                    PlacementConfigurationPath=Path.Combine(f.Review.Root,"MISSING-CATALOG.json")}).Read(CancellationToken.None);
                Check(f.Library.UsesFixedCadPairs&&f.Library.PlacementSources.Tools.All(p=>p.CadPair==null),"Fixed mode survives missing mappings");
                Check(f.Read().Reasons.Contains("FIXED_CAD_PAIR_REQUIRES_SOURCE_BOUND_SIMULATION_GEOMETRY"),"No replacement path on missing/stale catalog");
                int loads=0;var provider=new ToolProfileLibraryProvider(()=>{if(loads++>0)throw new IOException("CONTROLLED_UNREADABLE_SETTINGS");
                    return new ToolProfileSourceConfiguration {UseMasterPairsAsLibrary=true,PlacementConfigurationPath=Path.Combine(f.Review.Root,"MISSING-CATALOG.json")};});
                var held=provider.Read(CancellationToken.None);f.Library=provider.Read(CancellationToken.None);
                Check(!held.Current&&f.Library.UsesFixedCadPairs&&f.Library.Error!=null,"Unreadable settings cannot lose the selected source mode");
                Check(f.Read().Reasons.Contains("PROCESS_TOOL_LIBRARY_UNAVAILABLE"),"Unreadable catalog cannot enable replacement fixture tools");
            }
        });
    }
    private static int CadPairInventory(string[] args)
    {
        try{
            var bytes=File.ReadAllBytes(args[1]);var doc=ToolDxfReader.Read(bytes,new ToolProfileProvenance {Path=Path.GetFullPath(args[1]),Sha256=ToolProfileLibraryProvider.Hash(bytes)},CancellationToken.None);
            if(doc.Source.Error!=null)throw new InvalidDataException(doc.Source.Error);
            File.WriteAllText(args[2],Json.Serialize(new {doc.Source.Sha256,Roots=doc.RootInserts.Select(r=>new {r.Handle,r.Block,r.Layer,r.Rows,r.Columns,
                Layers=doc.Entities.Where(e=>e.InsertHandles.FirstOrDefault()==r.Handle&&!e.Annotation&&!e.PaperSpace).Select(e=>e.Layer).Distinct().OrderBy(l=>l).ToArray(),
                EntityCount=doc.Entities.Count(e=>e.InsertHandles.FirstOrDefault()==r.Handle&&!e.Annotation&&!e.PaperSpace)})}));
            Console.WriteLine("PRIVATE source identity inventory: "+doc.RootInserts.Count+" root INSERTs; no pairing generated.");return 0;
        }catch(Exception ex){Console.WriteLine(ex);return 1;}
    }
    private static int CadPairCompany(string[] args)
    {
        try{
            string config=Path.GetFullPath(args[1]),output=Path.GetFullPath(args[2]);Directory.CreateDirectory(output);
            var provider=new ToolProfileLibraryProvider(()=>new ToolProfileSourceConfiguration {UseMasterPairsAsLibrary=true,PlacementConfigurationPath=config});
            var lib=provider.Read(CancellationToken.None);var pairs=lib.PlacementSources.Tools;
            Check(lib.Error==null&&lib.Sources.Count==1,"A single actual company Master source");
            var before=File.ReadAllBytes(lib.Sources.Single().Path);var doc=ToolDxfReader.Read(before,new ToolProfileProvenance {Sha256=ToolProfileLibraryProvider.Hash(before)},CancellationToken.None);
            Check(pairs.Count==18&&pairs.All(p=>p.MappingIdentified&&p.CadPair?.Matched==true),"18/18 exact source pairs");
            Check(pairs.Select(p=>p.CadPair.HolderInsert).Distinct().Count()==18&&pairs.Select(p=>p.CadPair.PunchInsert).Distinct().Count()==18,"No reused root instances");
            var settings=PlacementSourceIo.Json<BendPlacementConfiguration>(File.ReadAllBytes(config));
            string configRoot=Path.GetDirectoryName(config),manifestPath=PlacementSourceIo.Resolve(configRoot,settings.MasterManifestPath);
            var wrong=PlacementSourceIo.Json<MasterAssemblyManifest>(File.ReadAllBytes(manifestPath));
            var a=wrong.Tools.Single(t=>t.Id=="AMD-STD-004");var b=wrong.Tools.Single(t=>t.Id=="AMD-STD-008");
            Check(a.HolderBlock==b.HolderBlock,"Actual source includes reused holder block, distinct INSERTs");
            string old=a.HolderRootInsert;a.HolderRootInsert=b.HolderRootInsert;b.HolderRootInsert=old;
            a.Holder.InsertHandles=new[]{a.HolderRootInsert};b.Holder.InsertHandles=new[]{b.HolderRootInsert};
            string wrongPath=Path.Combine(output,"PRIVATE-deliberately-wrong-map.json");File.WriteAllText(wrongPath,Json.Serialize(wrong));
            var rejected=BendMasterAssemblyReader.Read(lib.Sources.Single().Path,wrongPath,null,CancellationToken.None);
            Check(rejected.Tools.Where(t=>t.Id==a.Id||t.Id==b.Id).All(t=>!t.MappingIdentified&&t.Reasons.Contains("CAD_HOLDER_OUTSIDE_FIXED_SLOT")),"Wrong same-block actual-source pairs are rejected");
            Check(rejected.Tools.Where(t=>t.Id!=a.Id&&t.Id!=b.Id).All(t=>t.MappingIdentified),"Other 16 actual pairs stay identified");
            foreach(var p in pairs){
                foreach(var g in new[]{p.Punch,p.Holder}){
                    string root=g.SourceEntities.First().InsertHandles[0];var all=doc.Entities.Where(e=>!e.Annotation&&!e.PaperSpace&&e.InsertHandles.FirstOrDefault()==root).ToArray();
                    Check(new HashSet<string>(all.Select(e=>e.InstancePath+"|"+e.Handle)).SetEquals(g.SourceEntities.Select(e=>e.InstancePath+"|"+e.Handle)),"Complete instance geometry "+p.Id);
                    foreach(var e in g.SourceEntities){var original=all.Single(x=>x.Handle==e.Handle&&x.InstancePath==e.InstancePath);
                        Check(e.Type==original.Type&&e.Curves.Count==original.Curves.Count&&e.SourceClosed==original.SourceClosed,"Entity retained "+p.Id);
                        var q=e.Transform.Apply(P(1,2));Point(q,original.Transform.Apply(P(1,2)).X,original.Transform.Apply(P(1,2)).Y);
                        if(e.Spline!=null)Check(e.Spline.Knots.SequenceEqual(original.Spline.Knots)&&e.Spline.ControlPointsMm.SequenceEqual(original.Spline.ControlPointsMm),"Spline data retained "+p.Id);
                    }
                }
            }
            Check(pairs.Count(p=>p.Punch.ObservedClosed&&p.Punch.Region.IsValidRegion)==17,"17 valid closed punches");
            var kik=pairs.Single(p=>p.Id=="KIK-7181328001");Check(!kik.Punch.ObservedClosed&&!kik.Punch.Region.IsValidRegion,"KIK open is never repaired");
            Check(pairs.All(p=>!p.ClampVerified&&!p.GeometryCheckReady&&p.SeatingEvidence.TipPointMm==null),"CAD source is not physical setup evidence");
            var model=Model(Extrusion(Square().Curves),6);var draft=BendPlacementBuilder.Build(model,lib);
            Check(draft.Candidates.Count==108&&draft.Candidates.All(c=>c.Assembly.CadPair.Matched&&c.PunchPlacement.ToWorkFrame==null),"18 pairs on each of six synthetic bends, no guessed alignment");
            Check(ToolProfileLibraryProvider.Hash(File.ReadAllBytes(lib.Sources.Single().Path))==doc.Source.Sha256,"Company source unchanged");
            var rows=pairs.Select(p=>new {p.Id,p.CadPair.SlotId,p.CadPair.Status,p.CadPair.PunchBlock,p.CadPair.PunchInsert,p.CadPair.HolderBlock,p.CadPair.HolderInsert,
                PunchEntities=p.Punch.EntityKeys.Count,HolderEntities=p.Holder.EntityKeys.Count,PunchClosed=p.Punch.ObservedClosed,PunchRegion=p.Punch.Region.IsValidRegion,
                HolderSplines=p.Holder.SourceEntities.Count(e=>e.Spline!=null),p.CadPair.ReferenceOnly,p.ClampVerified,p.Reasons}).ToArray();
            File.WriteAllText(Path.Combine(output,"PRIVATE-company-cad-pairs.json"),Json.Serialize(new {Assertions=assertions,Tools=rows,SourceSha256=doc.Source.Sha256}));
            Console.WriteLine("Company fixed CAD pairs PASS: 18/18; assertions="+assertions+"; closed/valid Punch=17; physical setup=UNKNOWN 18/18.");return 0;
        }catch(Exception ex){Console.WriteLine("Company CAD pair FAIL: "+ex);return 1;}
    }
}
