using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using ADDIN.Commands.BendPlacement;
using ADDIN.Commands.BendTools;

internal static partial class ToolProfileTests
{
    private sealed class ReviewFixture : IDisposable
    {
        internal string Root,TrustPath;internal RSA Key;internal ToolReviewAuthority Authority;internal ToolReviewVerifier Verifier;internal DateTime Now;
        internal ToolReviewProof Sign(string purpose,string payload,bool simulation=true)
        {
            var proof=new ToolReviewProof {AuthorityKeyId=Authority.KeyId,ReviewerId=Authority.ReviewerId,ApprovedUtc=Now.AddMinutes(-1).ToString("o"),ExpiresUtc=Now.AddDays(1).ToString("o"),
                ArtifactPath="review-artifact.txt",ArtifactSha256=PlacementSourceIo.Hash(File.ReadAllBytes(Path.Combine(Root,"review-artifact.txt"))),PayloadSha256=payload,SimulationOnly=simulation};
            proof.SignatureBase64=Convert.ToBase64String(Key.SignData(Encoding.UTF8.GetBytes(ToolReviewVerifier.SignatureContent(purpose,proof)),HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1));return proof;
        }
        internal void Reload(){File.WriteAllText(TrustPath,Json.Serialize(new ToolReviewAuthorities {Version=1,Keys=new List<ToolReviewAuthority>{Authority}}));Verifier=ToolReviewVerifier.Load(TrustPath,CancellationToken.None,()=>Now);}
        public void Dispose()=>Key.Dispose();
    }
    private static ReviewFixture Reviews(bool simulation=true,bool physical=false)
    {
        var f=new ReviewFixture {Root=Folder(),Key=new RSACryptoServiceProvider(2048),Now=DateTime.UtcNow};var pub=f.Key.ExportParameters(false);
        f.Authority=new ToolReviewAuthority {KeyId="SYNTHETIC-TEST-KEY",ReviewerId="SYNTHETIC AUTHORIZED TEST REVIEWER",Purposes=new[]{"HolderMaterialRule","ToolHolderMapping","ToolPairSeating"},
            RsaModulusBase64=Convert.ToBase64String(pub.Modulus),RsaExponentBase64=Convert.ToBase64String(pub.Exponent),ValidFromUtc=f.Now.AddDays(-2).ToString("o"),ValidToUtc=f.Now.AddDays(10).ToString("o"),SimulationOnly=simulation,MayAttestPhysicalInspection=physical};
        f.TrustPath=Path.Combine(f.Root,"trust.json");File.WriteAllText(Path.Combine(f.Root,"review-artifact.txt"),"SYNTHETIC SIGNED REVIEW TEST ARTIFACT - NO PHYSICAL COMPANY INSPECTION");f.Reload();return f;
    }
    private static GeometrySelection2D HolderSelection(params ToolDxfEntity[] entities)
    {
        for(int i=0;i<entities.Length;i++){entities[i].Handle="SYNTHETIC-"+i;entities[i].SourceRecord=i;entities[i].InstancePath="SYNTHETIC_INSTANCE";entities[i].Transform=entities[i].Transform??new ToolTransform();}
        return new GeometrySelection2D(new string('d',64),null,entities,true,CancellationToken.None);
    }
    private static HolderMaterialDefinition MaterialDefinition(GeometrySelection2D punch,GeometrySelection2D holder,params string[] kinds)
    {
        return new HolderMaterialDefinition {ToolId=CompanyToolIds.All[0],BindingSha256=HolderObstacleSet.Binding(CompanyToolIds.All[0],punch,holder,"SYNTHETIC_MANIFEST"),Excluded=new List<HolderExcludedEntity>(),
            Contours=holder.EntityKeys.Select((key,i)=>new HolderContourDefinition {Id="C"+i,MaterialKind=kinds[i],ParentId=kinds[i]=="Hole"?"C0":kinds[i]=="Island"?"C1":null,EntityKeys=new[]{key}}).ToList()};
    }
    private static HolderObstacleSet ReadMaterial(ReviewFixture f,GeometrySelection2D punch,GeometrySelection2D holder,HolderMaterialDefinition definition)=>
        HolderObstacleSet.Read(CompanyToolIds.All[0],punch,holder,"SYNTHETIC_MANIFEST",definition,f.Verifier,f.Root,CancellationToken.None);
    private sealed class SeatingFixture : IDisposable
    {
        internal ReviewFixture Reviews;internal ToolAssemblyProfile Assembly;internal MasterFixture Master;internal ToolPairSeatingRecord Record;internal HolderMaterialDefinition Material;
        public void Dispose()=>Reviews.Dispose();
    }
    private static SeatingFixture Seating(bool simulation=true,bool physical=false,int toolIndex=0)
    {
        var f=new SeatingFixture {Reviews=Reviews(simulation,physical),Master=Master(toolIndex+1)};var original=Read(f.Master).Tools[toolIndex];
        double offset=toolIndex*100;var selection=f.Master.Mapping.Tools[toolIndex];
        var tool=new CompanyToolProfile {Id=original.Id,Parts=new[]{new ToolGeometryPart {Role=ToolGeometryRole.CompanyPunchProfile,Source=new ToolProfileProvenance {Sha256=original.MasterSha256},Entities=original.Punch.SourceEntities}},
            Validation=new ToolProfileValidationResult {PunchGeometryReadComplete=true}};
        var datumRecord=DatumRecord(tool);datumRecord.TipPointMm.X+=offset;datumRecord.MountDatumMm.X+=offset;datumRecord.LocalCoordinateFrame.OriginMm.X+=offset;
        ApproveAll(datumRecord);var datums=Bound(tool,datumRecord);
        f.Material=new HolderMaterialDefinition {ToolId=original.Id,BindingSha256=HolderObstacleSet.Binding(original.Id,original.Punch,original.Holder,original.ManifestSha256),
            Contours=new List<HolderContourDefinition>{new HolderContourDefinition {Id="SOLID",MaterialKind="Solid",EntityKeys=original.Holder.EntityKeys.ToArray()}},Excluded=new List<HolderExcludedEntity>()};
        f.Material.Review=f.Reviews.Sign("HolderMaterialRule",HolderObstacleSet.Payload(f.Material),simulation);
        var material=HolderObstacleSet.Read(original.Id,original.Punch,original.Holder,original.ManifestSha256,f.Material,f.Reviews.Verifier,f.Reviews.Root,CancellationToken.None);
        var map=new ToolHolderMappingRecord {ToolId=original.Id,BindingSha256=f.Material.BindingSha256,ExpectedLabel=selection.ExpectedLabel,LabelHandle="L"+toolIndex,PunchRootInsert="I"+toolIndex,PunchBlock=selection.PunchBlock,
            HolderRootInsert="I"+toolIndex,HolderBlock=selection.HolderBlock,HolderEntityKeys=original.Holder.EntityKeys.ToArray()};
        map.Review=f.Reviews.Sign("ToolHolderMapping",map.Payload(),simulation);
        var mapReview=f.Reviews.Verifier.Verify("ToolHolderMapping",map.Payload(),map.Review,f.Reviews.Root,CancellationToken.None);
        f.Record=new ToolPairSeatingRecord {ToolId=original.Id,MasterSourceSha256=original.MasterSha256,MasterManifestSha256=original.ManifestSha256,PunchGeometrySha256=original.Punch.GeometrySha256,
            HolderSelectionSha256=original.Holder.GeometrySha256,HolderMaterialDefinitionSha256=material.DefinitionSha256,PunchSourceFrameId=original.Punch.FrameId,HolderSourceFrameId=original.Holder.FrameId,
            HolderSourceToPunchSource=new PairTransformRecord {A=1,B=0,C=0,D=1,TxMm=2,TyMm=0},TipPointMm=new PairPointRecord {X=1+offset,Y=2},PunchMountDatumMm=new PairPointRecord {X=6+offset,Y=12},HolderMountDatumMm=new PairPointRecord {X=4+offset,Y=12},
            WorkingDirection=new PairPointRecord {X=0,Y=-1},ApproachDirection=new PairPointRecord {X=0,Y=-1},ContactReferenceMm=new PairPointRecord {X=1+offset,Y=2},ContactReferenceFrameId=original.Punch.FrameId,
            DatumLocalFrameSha256=ToolPairSeatingEvidence.LocalFrameHash(datums),PhysicalInspectionConfirmed=physical};
        f.Record.Review=f.Reviews.Sign("ToolPairSeating",ToolPairSeatingEvidence.Payload(f.Record),simulation);
        var seating=ToolPairSeatingEvidence.Read(original.Id,original.MasterSha256,original.ManifestSha256,original.Punch,original.Holder,material,datums,f.Record,f.Reviews.Verifier,f.Reviews.Root,CancellationToken.None);
        f.Assembly=new ToolAssemblyProfile(original.Id,original.MasterSha256,original.ManifestSha256,original.Punch,original.Holder,datums,true,new string[0],material,seating,mapReview);return f;
    }
    private static ToolSplineData Spline(int degree,ToolPoint[] points,double[] knots,double[] weights=null,int flags=0,ToolTransform transform=null)=>
        new ToolSplineData(degree,flags,points,knots,weights??new double[0],new ToolPoint[0],transform??new ToolTransform());
    // Independent Cox basis recursion, not knot insertion or de Casteljau.
    private static ToolPoint BasisEvaluate(ToolSplineData s,double parameter)
    {
        var u=s.Knots;int n=s.SourceControlPoints.Count;
        if(parameter==u[n])parameter=BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(parameter)+(parameter>0?-1:1));
        var basis=Enumerable.Range(0,u.Count-1).Select(i=>u[i]<=parameter&&parameter<u[i+1]?1.0:0).ToArray();
        for(int p=1;p<=s.Degree;p++)
        {
            var next=new double[basis.Length-1];
            for(int i=0;i<next.Length;i++)next[i]=(u[i+p]==u[i]?0:(parameter-u[i])/(u[i+p]-u[i])*basis[i])+(u[i+p+1]==u[i+1]?0:(u[i+p+1]-parameter)/(u[i+p+1]-u[i+1])*basis[i+1]);
            basis=next;
        }
        double x=0,y=0,w=0;for(int i=0;i<n;i++){double weight=basis[i]*(s.Weights.Count==0?1:s.Weights[i]);x+=weight*s.SourceControlPoints[i].X;y+=weight*s.SourceControlPoints[i].Y;w+=weight;}
        return s.SourceToMm.Apply(P(x/w,y/w));
    }
    private static void BoundChecks(ToolSplineData spline,SplineApproximation result)
    {
        Check(result.Supported,"Supported spline: "+result.Reason);Check(result.RequiresConservativeBoundaryBand,"Nominal curve cannot be mistaken for exact source");
        foreach(var chord in result.Chords)
        {
            Check(chord.DeviationBoundMm>0&&chord.DeviationBoundMm<=result.RequestedToleranceMm,"Outward interval hull bound");
            foreach(double fraction in new[]{0.0,.125,.25,.5,.75,.875,1})
            {var point=BasisEvaluate(spline,chord.ParameterMin*(1-fraction)+chord.ParameterMax*fraction);Check(ToolNurbsGeometry.DistanceToSegment(point,chord.Chord.Start,chord.Chord.End)<=chord.DeviationBoundMm+1e-8,"Independent basis sample within certified band");}
        }
    }
    private static void HolderTests()
    {
        Test("holder spline: cubic polynomial and nonuniform rational spans have hull bounds",()=> {
            BoundChecks(Spline(3,new[]{P(0,0),P(1,3),P(4,-2),P(5,0)},new[]{0.0,0,0,0,1,1,1,1}),
                ToolNurbsGeometry.Approximate(Spline(3,new[]{P(0,0),P(1,3),P(4,-2),P(5,0)},new[]{0.0,0,0,0,1,1,1,1})));
            var s=Spline(2,new[]{P(0,0),P(1,2),P(3,0),P(4,3),P(6,0)},new[]{0.0,0,0,.2,.7,1,1,1},new[]{1.0,.6,2,.7,1},4);
            BoundChecks(s,ToolNurbsGeometry.Approximate(s));
        });
        Test("holder spline: rational full circle remains closed without source repair",()=> {
            double w=Math.Sqrt(.5);var s=Spline(2,new[]{P(1,0),P(1,1),P(0,1),P(-1,1),P(-1,0),P(-1,-1),P(0,-1),P(1,-1),P(1,0)},
                new[]{0.0,0,0,.25,.25,.5,.5,.75,.75,1,1,1},new[]{1.0,w,1,w,1,w,1,w,1},5);
            var r=ToolNurbsGeometry.Approximate(s);BoundChecks(s,r);Check(ToolPoint.Distance(r.Chords[0].Chord.Start,r.Chords.Last().Chord.End)<1e-10,"Actual supplied closed endpoints");
        });
        Test("holder spline: unclamped periodic domain and reflected affine transforms",()=> {
            var s=Spline(2,new[]{P(0,0),P(2,0),P(2,2),P(0,2),P(0,0),P(2,0)},Enumerable.Range(0,9).Select(i=>(double)i).ToArray(),flags:2,
                transform:new ToolTransform(-2,0,.4,3,100,-70));BoundChecks(s,ToolNurbsGeometry.Approximate(s));
        });
        Test("holder spline: DXF WCS control points and rational metadata retain source identity",()=> {
            string entity=Tags(0,"SPLINE",5,"S",8,"HOLDER",70,12,71,2,72,6,73,3,74,0,40,0,40,0,40,0,40,1,40,1,40,1,41,1,41,.7,41,1,
                10,1,20,0,30,0,10,1,20,1,30,0,10,0,20,1,30,0,210,0,220,0,230,-1);
            var e=Parse(Dxf(Insert("B",10,20,-2,3,30),Block("B",entity))).Entities.Single();Check(e.Spline!=null&&e.Curves.Count==0&&e.UnsupportedReason.Contains("SPLINE"),"Legacy punch path still unsupported, metadata is separate");
            Point(e.Spline.SourceControlPoints[0],1,0);Check(e.SourceClosed==false&&e.Spline.Rational,"Raw flags retained; negative normal does not reflect WCS controls");BoundChecks(e.Spline,ToolNurbsGeometry.Approximate(e.Spline));
        });
        Test("holder spline: pathological weights/knots/degrees/closure and fit-only stay unsupported",()=> {
            var bad=new[]{Spline(0,new[]{P(0,0),P(1,1)},new[]{0.0,0,1}),Spline(2,new[]{P(0,0),P(1,1),P(2,0)},new[]{0.0,0,0,1,1,1},new[]{1.0,0,1},4),
                Spline(2,new[]{P(0,0),P(1,1),P(2,0)},new[]{0.0,0,0,1,1,1},new[]{1.0,-1,1},4),Spline(2,new[]{P(0,0),P(1,1),P(2,0)},new[]{0.0,0,.5,.2,1,1}),
                Spline(2,new[]{P(0,0),P(1,1),P(2,0)},new[]{0.0,0,0,1,1,1},flags:1),Spline(3,new ToolPoint[0],new double[0])};
            foreach(var s in bad){var r=ToolNurbsGeometry.Approximate(s);Check(!r.Supported&&r.Chords.Count==0&&r.Reason!=null,"No partial fake curve");}
        });
        Test("holder spline: zero normal on a WCS linear spline is not an OCS failure",()=> {
            string data=Tags(0,"SPLINE",70,28,71,2,72,6,73,3,74,0,40,0,40,0,40,0,40,1,40,1,40,1,10,0,20,0,30,0,10,1,20,0,30,0,10,2,20,0,30,0,210,0,220,0,230,0);
            var e=Parse(Dxf(data)).Entities.Single();Check(e.Spline!=null&&e.Spline.SourceNormal.All(n=>n==0),"Undefined normal preserved, WCS points determine plane");BoundChecks(e.Spline,ToolNurbsGeometry.Approximate(e.Spline));
            e=Parse(Dxf(data.Replace("30\n0\n","30\n1\n"))).Entities.Single();Check(e.Spline==null&&e.UnsupportedReason!=null,"Actual nonzero Z is not flattened");
        });
        Test("holder spline: tighter tolerance and cancellation are explicit",()=> {
            var s=Spline(2,new[]{P(0,0),P(1,4),P(3,0)},new[]{0.0,0,0,1,1,1});var a=ToolNurbsGeometry.Approximate(s,.01);var b=ToolNurbsGeometry.Approximate(s,.0001);
            Check(a.Supported&&b.Supported&&b.Chords.Count>a.Chords.Count,"Tolerance controls bounded subdivision");bool canceled=false;try{ToolNurbsGeometry.Approximate(s,cancellation:new CancellationToken(true));}catch(OperationCanceledException){canceled=true;}Check(canceled,"No fake unsupported result on cancellation");
        });
        HolderEvidenceTests();
        HolderRevisionTests();
    }
    private static void HolderEvidenceTests()
    {
        Test("holder material: multiple loops without a reviewed fill rule remain unknown",()=> {
            using(var f=Reviews())
            {var p=HolderSelection(Square());var h=HolderSelection(Square(),Circle(5,5,2));var set=ReadMaterial(f,p,h,null);
                Check(!set.GeometrySupportedForCollision&&set.Regions.Count==0&&set.Reasons.Contains("UNSUPPORTED_HOLDER_FILL_RULE"),"No even-odd/largest-loop default");Check(set.Topology.ClosedSimpleComponents==2,"Topology inventory is independent of material assignment");}
        });
        Test("holder material: reviewed solid hole island and disjoint component form a material set",()=> {
            using(var f=Reviews())
            {
                var p=HolderSelection(Square());var h=HolderSelection(Square(),Circle(5,5,3),Circle(5,5,1),Square(30,0));var d=MaterialDefinition(p,h,"Solid","Hole","Island","Solid");d.Review=f.Sign("HolderMaterialRule",HolderObstacleSet.Payload(d));
                var set=ReadMaterial(f,p,h,d);Check(set.GeometrySupportedForCollision,"Explicit signed hierarchy: "+string.Join(",",set.Reasons));Check(set.Regions.Count==3&&set.Regions[0].Holes.Count==1,"Disjoint union and nested island are represented independently");Check(!set.PhysicalHolderVerified,"Geometry is not physical seating");
            }
        });
        Test("holder material: illegal nesting, cyclic parenting and overlapping solids fail closed",()=> {
            using(var f=Reviews())
            {
                var p=HolderSelection(Square());
                foreach(int mode in new[]{0,1,2})
                {
                    var h=mode==2?HolderSelection(Square(),Square(5,0)):HolderSelection(Square(),Circle(5,5,2));var d=MaterialDefinition(p,h,"Solid",mode==0?"Solid":"Hole");
                    if(mode==1){d.Contours[0].ParentId="C1";d.Contours[0].MaterialKind="Island";}
                    d.Review=f.Sign("HolderMaterialRule",HolderObstacleSet.Payload(d));var set=ReadMaterial(f,p,h,d);Check(!set.GeometrySupportedForCollision&&set.Regions.Count==0,"No illegal fill/overlap/union: "+mode);
                }
            }
        });
        Test("holder material: source centerline/decorative entities require explicit signed roles",()=> {
            using(var f=Reviews())
            {
                var p=HolderSelection(Square());var h=HolderSelection(Square(),Entity(new[]{Segment(-5,5,15,5)}));var d=MaterialDefinition(p,h,"Solid","Solid");d.Contours.RemoveAt(1);
                d.Review=f.Sign("HolderMaterialRule",HolderObstacleSet.Payload(d));Check(!ReadMaterial(f,p,h,d).GeometrySupportedForCollision,"Missing role is not silently discarded");
                d.Excluded.Add(new HolderExcludedEntity {EntityKey=h.EntityKeys[1],Role="Centerline"});d.Review=f.Sign("HolderMaterialRule",HolderObstacleSet.Payload(d));Check(ReadMaterial(f,p,h,d).GeometrySupportedForCollision,"Explicit reviewed non-material role");
                d.Excluded[0].Role="Anything";d.Review=f.Sign("HolderMaterialRule",HolderObstacleSet.Payload(d));Check(!ReadMaterial(f,p,h,d).GeometrySupportedForCollision,"Unknown exclusion role refused");
            }
        });
        Test("holder material: explicitly reviewed component union preserves overlapping solids and per-component holes",()=> {
            using(var f=Reviews())
            {
                var p=HolderSelection(Square());var h=HolderSelection(Square(),Circle(5,5,2),Square(5,0));var d=MaterialDefinition(p,h,"Solid","Hole","Solid");d.CombinationRule="UnionOfComponentMaterialsV1";
                d.Review=f.Sign("HolderMaterialRule",HolderObstacleSet.Payload(d));var set=ReadMaterial(f,p,h,d);Check(set.GeometrySupportedForCollision&&set.Regions.Count==2,"Explicit union retains overlapping true components: "+string.Join(",",set.Reasons));
                Func<ToolPoint,bool> material=point=>set.Regions.Any(r=>ToolRegionValidator.ContainsPoint(r.Outer.Boundary,point)&&!r.Holes.Any(hole=>ToolRegionValidator.ContainsPoint(hole.Boundary,point)));
                Check(!material(P(4,5))&&material(P(6,5))&&material(P(12,5))&&!material(P(20,5)),"Second component fills its own material across the first component's hole; no global hole subtraction");
                var placed=new HolderObstaclePlacement2D(set,new PlacementTransform2D(),"TEST");Check(placed.MaterialRegions.Count==2&&placed.RegionCombination=="UnionOfEachOuterMinusItsOwnHoles","Union semantics survive placement");
                d.CombinationRule=null;Check(!ReadMaterial(f,p,h,d).GeometrySupportedForCollision,"Changing combination without re-review invalidates proof");
            }
        });
        Test("holder material: tangent and nested component unions require explicit material declaration",()=> {
            using(var f=Reviews())
            {
                foreach(var h in new[]{HolderSelection(Circle(0,0,2),Circle(4,0,2)),HolderSelection(Square(),Circle(5,5,1))})
                {
                    var p=HolderSelection(Square());var d=MaterialDefinition(p,h,"Solid","Solid");d.CombinationRule="UnionOfComponentMaterialsV1";d.Review=f.Sign("HolderMaterialRule",HolderObstacleSet.Payload(d));
                    Check(ReadMaterial(f,p,h,d).GeometrySupportedForCollision,"Tangent/nested components are an explicit union, never an inferred hole");
                }
            }
        });
        Test("holder material: bounded SPLINE boundary retains a conservative band in placed output",()=> {
            using(var f=Reviews())
            {
                var s=Spline(2,new[]{P(0,0),P(2,3),P(4,0)},new[]{0.0,0,0,1,1,1});var spline=new ToolDxfEntity {Type="SPLINE",Spline=s,Curves=new ToolCurve[0],UnsupportedReason="Spline backend required"};
                var p=HolderSelection(Square());var h=HolderSelection(spline,Entity(new[]{Segment(4,0,0,0)}));var d=MaterialDefinition(p,h,"Solid","Solid");d.Contours.RemoveAt(1);d.Contours[0].EntityKeys=h.EntityKeys.ToArray();
                d.Review=f.Sign("HolderMaterialRule",HolderObstacleSet.Payload(d));var set=ReadMaterial(f,p,h,d);Check(set.GeometrySupportedForCollision&&set.RequiresConservativeBoundaryBand,"Spline + existing closing line, no added source edge: "+string.Join(",",set.Reasons));
                var placed=new HolderObstaclePlacement2D(set,new PlacementTransform2D(-1,0,0,1,40,20,true),"TEST");Check(placed.HasConservativeGeometry&&placed.Contours[0].BoundaryBandRadiusMm>0,"Error bound survives rigid mirror placement");
                Check(placed.Contours[0].BoundaryBandRadiusMm>set.Contours[0].BoundaryUncertaintyMm&&placed.Contours[0].PlacementUncertaintyMm>0,"Rigid numerical error is propagated, not rounded away");
                var far=new HolderObstaclePlacement2D(set,new PlacementTransform2D(txMm:1e9),"TEST");Check(!far.HasConservativeGeometry&&far.Contours.Count==0&&far.Reason!=null,"Out-of-policy work coordinate remains unsupported");
                Check(placed.Contours[0].NominalBoundary.Count>2&&set.EnvelopeContract.Contains("EveryBoundary"),"No raw LINE-only subset substituted for SPLINE obstacle");
            }
        });
        Test("review: coordinates and reviewer text alone are not authority or physical evidence",()=> {
            using(var f=Reviews())
            {
                string payload=PlacementSourceIo.Hash("synthetic");var proof=f.Sign("HolderMaterialRule",payload);proof.SignatureBase64=null;
                Check(!f.Verifier.Verify("HolderMaterialRule",payload,proof,f.Root,CancellationToken.None).RecordVerified,"Signature required");
                var missing=ToolReviewVerifier.Load(null,CancellationToken.None);Check(!missing.Verify("HolderMaterialRule",payload,f.Sign("HolderMaterialRule",payload),f.Root,CancellationToken.None).RecordVerified,"Explicit trusted key file required");
            }
        });
        Test("review: tampered payload artifact signature purpose and unauthorized reviewer are rejected",()=> {
            using(var f=Reviews())
            {
                string payload=PlacementSourceIo.Hash("synthetic");var proof=f.Sign("HolderMaterialRule",payload);
                Check(f.Verifier.Verify("HolderMaterialRule",payload,proof,f.Root,CancellationToken.None).Status==ToolReviewStatus.VerifiedSimulation,"Authorized synthetic record");
                Check(!f.Verifier.Verify("HolderMaterialRule",new string('f',64),proof,f.Root,CancellationToken.None).RecordVerified,"Payload hash pin");
                Check(!f.Verifier.Verify("ToolPairSeating",payload,proof,f.Root,CancellationToken.None).RecordVerified,"Purpose is signed");
                proof.ReviewerId="UNAUTHORIZED";Check(!f.Verifier.Verify("HolderMaterialRule",payload,proof,f.Root,CancellationToken.None).RecordVerified,"Exact authorized reviewer identity");
                proof=f.Sign("HolderMaterialRule",payload);File.WriteAllText(Path.Combine(f.Root,"review-artifact.txt"),"tampered");Check(f.Verifier.Verify("HolderMaterialRule",payload,proof,f.Root,CancellationToken.None).Status==ToolReviewStatus.Stale,"Artifact bytes, not just file coordinates");
            }
        });
        Test("review: expiry and authority revocation invalidate held observations",()=> {
            using(var f=Reviews())
            {
                string payload=PlacementSourceIo.Hash("synthetic");var proof=f.Sign("HolderMaterialRule",payload);var review=f.Verifier.Verify("HolderMaterialRule",payload,proof,f.Root,CancellationToken.None);
                f.Now=f.Now.AddDays(2);Check(review.Status==ToolReviewStatus.Stale&&!review.RecordVerified,"Held record expires without a geometry edit");
                f.Authority.Purposes=new string[0];f.Reload();Check(!f.Verifier.Verify("HolderMaterialRule",payload,proof,f.Root,CancellationToken.None).RecordVerified,"Revoked role cannot verify");
            }
        });
        Test("seating: fully signed synthetic fixture can be geometry-ready but never manufacture PASS",()=> {
            using(var f=Seating())
            {
                var a=f.Assembly;Check(a.HolderObstacles.GeometrySupportedForCollision&&a.SeatingEvidence.CoordinateRecordReady&&a.GeometryCheckReady,"Complete required geometry/evidence: "+string.Join(",",a.SeatingEvidence.Reasons));
                Check(a.SeatingEvidence.MasterSourceSha256==a.MasterSha256&&a.SeatingEvidence.HolderSelectionSha256==a.Holder.GeometrySha256&&a.SeatingEvidence.HolderMountDatumMm.HasValue,"Read-only result retains exact binding and distinct holder datum");
                Check(a.SeatingEvidence.Review.ReviewerId==f.Reviews.Authority.ReviewerId&&a.SeatingEvidence.Review.ArtifactSha256==f.Record.Review.ArtifactSha256,"Verified provenance is retained without the mutable input record");
                Check(!a.PhysicalPairVerified&&!a.ClampVerified&&a.SeatingEvidence.SimulationOnly,"Synthetic approval never asserts physical company inspection");
                var library=Library(Read(f.Master));library.PlacementSources=new PlacementSourceSnapshot("SYNTHETIC",new[]{a}.Concat(library.PlacementSources.Tools.Skip(1)),null,new string[0]);
                var model=Model(Extrusion(Square().Curves));var sim=new PlacementSimulationRequest(a.Id,a.Punch.FrameId,P(1,2),P(0,-1),P(7,8),P(0,-1));
                var draft=BendPlacementBuilder.Build(model,library,simulation:sim);Check(draft.Candidates.Count==18&&draft.Candidates.Count(c=>c.GeometryCheckReady)==1,"One full fixture and 17 missing retained");
                Check(draft.Candidates.All(c=>c.Status=="CHUA_XAC_MINH"&&!c.ReadyForCollisionCheck)&&draft.WinnerToolId==null,"No collision/winner/production verdict");
                var candidate=draft.Candidates[0];Point(candidate.HolderPlacement.ToWorkFrame.Apply(P(4,12)),candidate.PunchPlacement.ToWorkFrame.Apply(P(6,12)).X,candidate.PunchPlacement.ToWorkFrame.Apply(P(6,12)).Y);
                a.HolderObstacles.Invalidate();Check(!a.SeatingEvidence.CoordinateRecordReady&&a.SeatingEvidence.Status==ToolReviewStatus.Stale&&!candidate.GeometryCheckReady,"Held seating and placement cannot outlive their material review");
            }
        });
        Test("seating: physical flags need an authorized external inspection and complete matching evidence",()=> {
            using(var f=Seating(false,true))
            {
                Check(f.Assembly.PhysicalPairVerified&&f.Assembly.ClampVerified,"Only synthetic test of the signed physical-attestation contract, not company evidence");
                f.Reviews.Authority.MayAttestPhysicalInspection=false;f.Reviews.Reload();var a=f.Assembly;
                var noPermission=ToolPairSeatingEvidence.Read(a.Id,a.MasterSha256,a.ManifestSha256,a.Punch,a.Holder,a.HolderObstacles,a.MasterDatums,f.Record,f.Reviews.Verifier,f.Reviews.Root,CancellationToken.None);
                Check(noPermission.CoordinateRecordReady&&!noPermission.PhysicalInspectionAccepted,"Physical checkbox cannot override authority's allowed inspection role");
                f.Assembly.MappingReview.Invalidate();Check(!f.Assembly.PhysicalPairVerified,"No physical flag survives stale mapping review");
            }
            using(var f=Seating(false,false)){Check(!f.Assembly.PhysicalPairVerified,"Signed coordinates without physical attestation are insufficient");}
        });
        Test("seating: source manifest selection transform and datum mismatch fail closed",()=> {
            using(var f=Seating())
            {
                var a=f.Assembly;
                foreach(int mode in new[]{0,1,2,3,4})
                {
                    var r=Json.Deserialize<ToolPairSeatingRecord>(Json.Serialize(f.Record));
                    if(mode==0)r.MasterSourceSha256=new string('f',64);if(mode==1)r.MasterManifestSha256=new string('f',64);if(mode==2)r.HolderSelectionSha256=new string('f',64);if(mode==3)r.HolderSourceToPunchSource.TxMm=3;if(mode==4)r.TipPointMm.X+=1;
                    var result=ToolPairSeatingEvidence.Read(a.Id,a.MasterSha256,a.ManifestSha256,a.Punch,a.Holder,a.HolderObstacles,a.MasterDatums,r,f.Reviews.Verifier,f.Reviews.Root,CancellationToken.None);
                    Check(!result.CoordinateRecordReady&&!result.PhysicalInspectionAccepted,"Wrong pair/review/pose/data: "+mode);
                }
                a.MasterDatums.Invalidate();Check(a.SeatingEvidence.Status==ToolReviewStatus.Stale&&!a.GeometryCheckReady,"Held datum invalidation propagates");
            }
        });
        Test("mapping: 004 008 117 452 are never attached to a nearby or repeated instance",()=> {
            var f=Master();foreach(string id in new[]{"AMD-STD-004","AMD-STD-008","AMD-STD-117","AMD-STD-452"}){var d=f.Mapping.Tools.Single(t=>t.Id==id);d.HolderRootInsert=null;d.HolderBlock=null;d.Holder=null;}Save(f);
            var r=Read(f);Check(r.Tools.Count==18,"18 slots");foreach(string id in new[]{"AMD-STD-004","AMD-STD-008","AMD-STD-117","AMD-STD-452"})
            {var t=r.Tools.Single(x=>x.Id==id);Check(t.Holder==null&&!t.MappingIdentified&&t.MappingReview.Reason=="MAPPING_UNRESOLVED"&&!t.PhysicalPairVerified,"No nearest/INSERT order fallback for "+id);}
        });
        Test("sheet readiness: unsupported 2D requires measured slices or 3D, never empty geometry PASS",()=> {
            var b=Extrusion(Square().Curves);var section=Model(b,complete:false).Sections[0];var envelope=new SheetEnvelopeReadiness(section);
            Check(!envelope.HasUsableEnvelope&&envelope.RequiredMethod==SheetEnvelopeRequirement.MultipleSlicesOrNative3DRequired,"No manufactured replacement sheet");
        });
    }
    private static void HolderRevisionTests()
    {
        Test("holder revision: artifact-only change invalidates a published snapshot and its candidates",()=> {
            using(var f=Seating())
            {
                string holderPath=Path.Combine(f.Reviews.Root,"holder.json"),config=Path.Combine(f.Master.Root,"placement.json");
                File.WriteAllText(holderPath,Json.Serialize(new HolderEvidenceDocument {Version=1,Materials=new List<HolderMaterialDefinition>{f.Material}}));
                File.WriteAllText(config,Json.Serialize(new BendPlacementConfiguration {Version=1,MasterPath=f.Master.Source,MasterManifestPath=f.Master.Manifest,HolderEvidencePath=holderPath,ReviewAuthoritiesPath=f.Reviews.TrustPath}));
                var provider=new ToolProfileLibraryProvider(()=>new ToolProfileSourceConfiguration {DirectoryPath=f.Master.Root,PlacementConfigurationPath=config});
                var first=provider.Read(CancellationToken.None);var model=Model(Extrusion(Square().Curves));var draft=BendPlacementBuilder.Build(model,first);
                Check(first.PlacementSources.Tools[0].HolderObstacles.GeometrySupportedForCollision,"Signed material accepted independently of missing seating");
                var unchanged=provider.Read(CancellationToken.None);Check(ReferenceEquals(first,unchanged),"No cache churn without a changed observation");
                File.WriteAllText(Path.Combine(f.Reviews.Root,"review-artifact.txt"),"CHANGED ARTIFACT, UNCHANGED JSON");
                var second=provider.Read(CancellationToken.None);
                Check(first.Fingerprint!=second.Fingerprint&&!first.Current&&!draft.Current&&model.Lease.Current,"Artifact observation participates in fingerprint; model capture untouched");
                Check(second.PlacementSources.Tools.Count==18&&!second.PlacementSources.Tools[0].HolderObstacles.GeometrySupportedForCollision,"Stale review cannot retain material readiness");
            }
        });
        Test("holder revision: corrupt optional review sources preserve all source punch selections",()=> {
            foreach(string content in new[]{"{broken","{\"Version\":999}","{\"Version\":1,\"Materials\":[null]}"})
            {
                var m=Master();string path=Path.Combine(m.Root,"bad.json");File.WriteAllText(path,content);
                var read=BendMasterAssemblyReader.Read(m.Source,m.Manifest,null,CancellationToken.None,path,path,path);
                Check(read.Tools.Count==18&&read.Tools.All(t=>t.Punch?.Supported==true),"Review parsing is isolated from actual source geometry");
                Check(read.Tools.All(t=>!t.GeometryCheckReady&&!t.PhysicalPairVerified),"Corrupt review never authorizes a setup");
            }
            var master=Master(1);var badPath=BendMasterAssemblyReader.Read(master.Source,master.Manifest,null,CancellationToken.None,"bad\0path");
            Check(badPath.Tools[0].Punch!=null&&badPath.Reasons.Contains("INVALID_HOLDER_EVIDENCE_SOURCE"),"Invalid optional path does not discard master geometry");
        });
        Test("mapping: four formerly unresolved IDs accept only signed exact instance and label evidence",()=> {
            using(var f=Reviews())
            {
                var master=Master();var baseline=Read(master);var maps=new List<ToolHolderMappingRecord>();
                foreach(string id in new[]{"AMD-STD-004","AMD-STD-008","AMD-STD-117","AMD-STD-452"})
                {
                    var d=master.Mapping.Tools.Single(t=>t.Id==id);var a=baseline.Tools.Single(t=>t.Id==id);
                    var map=new ToolHolderMappingRecord {ToolId=id,BindingSha256=HolderObstacleSet.Binding(id,a.Punch,a.Holder,a.ManifestSha256),ExpectedLabel=d.ExpectedLabel,LabelHandle=d.LabelHandle,
                        PunchRootInsert=d.PunchRootInsert,PunchBlock=d.PunchBlock,HolderRootInsert=d.HolderRootInsert,HolderBlock=d.HolderBlock,HolderEntityKeys=a.Holder.EntityKeys.ToArray()};
                    map.Review=f.Sign("ToolHolderMapping",map.Payload());maps.Add(map);
                }
                string path=Path.Combine(f.Root,"mappings.json");Action save=()=>File.WriteAllText(path,Json.Serialize(new HolderEvidenceDocument {Version=1,Mappings=maps}));save();
                var result=BendMasterAssemblyReader.Read(master.Source,master.Manifest,null,CancellationToken.None,path,null,f.TrustPath);
                Check(result.Tools.Count(t=>t.MappingReview.RecordVerified)==4,"Synthetic operator-specified exact selections, no automatic mapping");
                maps[0].HolderRootInsert=maps[1].HolderRootInsert;maps[0].Review=f.Sign("ToolHolderMapping",maps[0].Payload());save();
                result=BendMasterAssemblyReader.Read(master.Source,master.Manifest,null,CancellationToken.None,path,null,f.TrustPath);
                Check(result.Tools.Single(t=>t.Id==maps[0].ToolId).MappingReview.Status==ToolReviewStatus.Stale,"Even a signed wrong instance cannot override current manifest");
                Check(result.Tools.All(t=>!t.GeometryCheckReady&&!t.PhysicalPairVerified),"Mapping signature alone is neither material nor seating inspection");
            }
        });
        Test("seating: omitted coordinates or matrix values cannot default to an approved origin",()=> {
            using(var f=Seating())
            {
                foreach(int mode in new[]{0,1,2})
                {
                    var r=Json.Deserialize<ToolPairSeatingRecord>(Json.Serialize(f.Record));
                    if(mode==0)r.HolderSourceToPunchSource.TxMm=null;if(mode==1)r.HolderMountDatumMm.X=null;if(mode==2)r.ApproachDirection.Y=null;
                    var a=f.Assembly;var result=ToolPairSeatingEvidence.Read(a.Id,a.MasterSha256,a.ManifestSha256,a.Punch,a.Holder,a.HolderObstacles,a.MasterDatums,r,f.Reviews.Verifier,f.Reviews.Root,CancellationToken.None);
                    Check(!result.CoordinateRecordReady&&!result.PhysicalInspectionAccepted,"Missing values remain missing");
                }
            }
        });
        Test("holder revision: changed spline controls and fill policy invalidate their exact evidence binding",()=> {
            using(var f=Reviews())
            {
                Func<double,GeometrySelection2D> selection=height=>HolderSelection(new ToolDxfEntity {Type="SPLINE",Spline=Spline(2,new[]{P(0,0),P(2,height),P(4,0)},new[]{0.0,0,0,1,1,1}),Curves=new ToolCurve[0],UnsupportedReason="SPLINE"},Entity(new[]{Segment(4,0,0,0)}));
                var p=HolderSelection(Square());var h=selection(3);var d=MaterialDefinition(p,h,"Solid","Solid");d.Contours.RemoveAt(1);d.Contours[0].EntityKeys=h.EntityKeys.ToArray();d.Review=f.Sign("HolderMaterialRule",HolderObstacleSet.Payload(d));
                Check(ReadMaterial(f,p,h,d).GeometrySupportedForCollision,"Original signed source selection");var edited=selection(4);
                Check(h.GeometrySha256!=edited.GeometrySha256&&!ReadMaterial(f,p,edited,d).GeometrySupportedForCollision,"Spline control changes bind even if source ID and entity keys are unchanged");
                Check(!HolderObstacleSet.Read(d.ToolId,p,h,"DIFFERENT_MANIFEST",d,f.Verifier,f.Root,CancellationToken.None).GeometrySupportedForCollision,"Manifest revision is pinned independently of geometry");
            }
        });
    }
}
