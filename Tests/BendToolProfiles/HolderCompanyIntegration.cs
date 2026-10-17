using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using ADDIN.Commands.BendPlacement;
using ADDIN.Commands.BendTools;

internal static partial class ToolProfileTests
{
    // Local-only integration. Company geometry and exact evidence keys are
    // written only to the explicitly supplied PRIVATE output outside Git.
    private static int HolderCompanyIntegration(string[] args)
    {
        try
        {
            string configPath=Path.GetFullPath(args[1]),directory=Path.GetFullPath(args[2]),output=Path.GetFullPath(args[3]);Directory.CreateDirectory(output);
            var config=PlacementSourceIo.Json<BendPlacementConfiguration>(PlacementSourceIo.Read(configPath,65536,CancellationToken.None));string root=Path.GetDirectoryName(configPath);
            string masterPath=PlacementSourceIo.Resolve(root,config.MasterPath),mappingPath=PlacementSourceIo.Resolve(root,config.MasterManifestPath);
            byte[] masterBytes=PlacementSourceIo.Read(masterPath,32*1024*1024,CancellationToken.None);string originalHash=PlacementSourceIo.Hash(masterBytes);
            var mapping=PlacementSourceIo.Json<MasterAssemblyManifest>(PlacementSourceIo.Read(mappingPath,1024*1024,CancellationToken.None));
            var provider=new ToolProfileLibraryProvider(()=>new ToolProfileSourceConfiguration {DirectoryPath=directory,PlacementConfigurationPath=configPath});
            var watch=System.Diagnostics.Stopwatch.StartNew();var library=provider.Read(CancellationToken.None);watch.Stop();
            Check(library.Error==null&&library.PlacementSources.Tools.Count==18,"18 Master slots remain in production source provider");
            var tools=library.PlacementSources.Tools;
            Check(tools.All(t=>t.Punch?.Supported==true),"18 actual punch selections");Check(tools.Count(t=>t.Punch.Region.IsValidRegion)==17,"17 punch regions and source-open KIK");
            Check(tools.All(t=>!t.GeometryCheckReady&&!t.ClampVerified&&!t.PhysicalPairVerified),"No actual holder/installation approved by this test");
            var splines=new List<object>();
            foreach(var t in tools)
            {
                Check(t.HolderObstacles!=null,"Explicit holder geometry/missing state for "+t.Id);
                Check(t.HolderObstacles.Regions.Count==0&&!t.HolderObstacles.GeometrySupportedForCollision,"Actual material rules remain unreviewed");
                if(t.Holder==null)Check(t.MappingReview.Reason=="MAPPING_UNRESOLVED","No guessed repeated instance");
                foreach(var entity in t.Holder?.SourceEntities.Where(e=>e.Type=="SPLINE")??new ToolDxfEntity[0])
                {
                    var result=t.HolderObstacles.Inventory.Single(e=>e.EntityKey==HolderEntityGeometry.Key(entity));
                    splines.Add(new {ToolId=t.Id,entity.Handle,entity.InstancePath,entity.Blocks,entity.InsertHandles,entity.SourceLineType,entity.UnsupportedReason,entity.Spline,Approximation=result.Spline});
                    Check(result.Spline!=null,"Spline metadata/unsupported reason exists");
                    if(result.Spline.Supported)Check(result.Spline.Chords.All(c=>c.DeviationBoundMm<=ToolNurbsGeometry.DefaultToleranceMm),"Every actual chord has its own bound");
                }
            }
            var rows=tools.Select(t=>new {t.Id,PunchRegion=t.Punch.Region.Status.ToString(),HolderSelected=t.Holder!=null,
                SplineCount=t.HolderObstacles.SplineCount,SplineSupported=t.HolderObstacles.SupportedSplineCount,
                ParsedEntities=t.HolderObstacles.Inventory.Count,UnsupportedEntities=t.HolderObstacles.Inventory.Count(e=>!e.Supported),
                ClosedCandidateLoops=t.HolderObstacles.Topology?.ClosedSimpleComponents,OpenOrBranchedComponents=t.HolderObstacles.Topology?.OpenOrBranchedComponents,
                MaterialRegionCount=t.HolderObstacles.Regions.Count,MappingReason=t.MappingReview.Reason,MappingEvidence=t.MappingReview.Status.ToString(),
                SourceRevision="SavedShaObserved_NotExternallyAttested",MaterialEvidence=t.HolderObstacles.Review.Status.ToString(),SeatingEvidence=t.SeatingEvidence.Status.ToString(),
                t.GeometryCheckReady,t.ClampVerified,t.PhysicalPairVerified,Blockers=t.Reasons}).ToArray();
            var counts=new {Tools=18,HolderSelected=rows.Count(r=>r.HolderSelected),SplineInstances=rows.Sum(r=>r.SplineCount),SplineSupported=rows.Sum(r=>r.SplineSupported),
                HolderInventoriesFullyParsed=rows.Count(r=>r.HolderSelected&&r.UnsupportedEntities==0),MappingUnresolved=rows.Count(r=>r.MappingReason=="MAPPING_UNRESOLVED"),
                MaterialRegions=rows.Sum(r=>r.MaterialRegionCount),GeometryReady=0,PhysicallyVerified=0};
            File.WriteAllText(Path.Combine(output,"PRIVATE-holder-report.json"),Json.Serialize(new {Passed=true,Assertions=assertions,Counts=counts,Tools=rows,ReadMilliseconds=watch.ElapsedMilliseconds}));
            File.WriteAllText(Path.Combine(output,"PRIVATE-spline-qa.json"),Json.Serialize(new {MasterPath=masterPath,SourceSha256=originalHash,Splines=splines}));
            // Source facts may be prefilled. Reviewer, dates, physical claims,
            // measured datums/transforms, signatures and public keys stay blank.
            var materials=new List<HolderMaterialDefinition>();var mappings=new List<ToolHolderMappingRecord>();var pairs=new List<ToolPairSeatingRecord>();
            foreach(var t in tools)
            {
                var m=mapping.Tools.Single(d=>d.Id==t.Id);materials.Add(new HolderMaterialDefinition {ToolId=t.Id,BindingSha256=t.HolderObstacles.BindingSha256,
                    Contours=new List<HolderContourDefinition>(),Excluded=new List<HolderExcludedEntity>(),Review=new ToolReviewProof()});
                mappings.Add(new ToolHolderMappingRecord {ToolId=t.Id,BindingSha256=t.HolderObstacles.BindingSha256,ExpectedLabel=m.ExpectedLabel,LabelHandle=m.LabelHandle,
                    PunchRootInsert=m.PunchRootInsert,PunchBlock=m.PunchBlock,HolderRootInsert=m.HolderRootInsert,HolderBlock=m.HolderBlock,HolderEntityKeys=t.Holder?.EntityKeys.ToArray(),Review=new ToolReviewProof()});
                pairs.Add(new ToolPairSeatingRecord {ToolId=t.Id,MasterSourceSha256=t.MasterSha256,MasterManifestSha256=t.ManifestSha256,PunchGeometrySha256=t.Punch.GeometrySha256,
                    HolderSelectionSha256=t.Holder?.GeometrySha256,PunchSourceFrameId=t.Punch.FrameId,HolderSourceFrameId=t.Holder?.FrameId,
                    HolderSourceToPunchSource=new PairTransformRecord(),TipPointMm=new PairPointRecord(),PunchMountDatumMm=new PairPointRecord(),HolderMountDatumMm=new PairPointRecord(),
                    WorkingDirection=new PairPointRecord(),ApproachDirection=new PairPointRecord(),ContactReferenceMm=new PairPointRecord(),Review=new ToolReviewProof()});
            }
            File.WriteAllText(Path.Combine(output,"PRIVATE-holder-review-UNFILLED.json"),Json.Serialize(new HolderEvidenceDocument {Version=1,Materials=materials,Mappings=mappings}));
            File.WriteAllText(Path.Combine(output,"PRIVATE-seating-review-UNFILLED.json"),Json.Serialize(new PairSeatingDocument {Version=1,Pairs=pairs}));
            File.WriteAllText(Path.Combine(output,"PRIVATE-authorities-UNFILLED.json"),Json.Serialize(new ToolReviewAuthorities {Version=1,Keys=new List<ToolReviewAuthority>()}));
            File.WriteAllText(Path.Combine(output,"PRIVATE-operator-checklist.md"),
                "# Chưa xác minh — checklist người kiểm định\n\n"+
                "- Đối chiếu đúng revision Master/DIM và SHA nguồn; xác nhận file hậu tố (2)/(3) nếu được yêu cầu.\n"+
                "- Xác nhận từng tool ID, TEXT label, exact INSERT/path/block và đúng Punch–Holder. Bốn mapping unresolved phải được người kiểm tra xác định, không dùng vị trí giấy.\n"+
                "- Phân loại TẤT CẢ entity keys: biên vật liệu Solid/Hole/Island hoặc Centerline/Decorative/Reference/Annotation. Kiểm tra phần thật, lỗ và từng component; không chọn nét lớn nhất.\n"+
                "- Đo tip/mount của punch, mount holder, hướng làm việc/tiếp cận và frame; xác nhận relative transform, gốc tọa độ và sai số. Không lấy bbox/điểm thấp nhất.\n"+
                "- Kiểm tra trên máy đúng holder/clamp, tình trạng gá, tool revision và setup thực tế; lưu tài liệu/ảnh/biên bản ngoài Git.\n"+
                "- Người có thẩm quyền tự ghi reviewer, ngày duyệt/hết hạn, artifact và SHA; ký payload bằng khóa được quản trị viên cho phép. Không ký template chưa phân loại/chưa đo.\n"+
                "- Chỉ người kiểm định thực tế được xác nhận PhysicalInspectionConfirmed. Template để false, không có signature/reviewer/date/tọa độ tự điền.\n"+
                "- Rà lại bounded-deviation và boundary-band policy; chữ ký geometry không chứng minh không va chạm hay thứ tự chấn khả thi.\n");
            Check(PlacementSourceIo.Hash(PlacementSourceIo.Read(masterPath,32*1024*1024,CancellationToken.None))==originalHash,"Original Master unchanged");
            Console.WriteLine("HOLDER COMPANY PASS: "+Json.Serialize(counts)+"; assertions="+assertions+"; read_ms="+watch.ElapsedMilliseconds);return 0;
        }
        catch(Exception ex){Console.WriteLine("HOLDER COMPANY FAIL: "+ex.Message);return 1;}
    }
}
