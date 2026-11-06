# CG2 — ONE-SHOT SOLID MATERIAL SCREENING (C# ĐÃ VIẾT)

**Mục tiêu:** Một lượt triển khai, một lượt Antigravity build/test và gửi báo cáo, một lượt ChatGPT review. Không mở thêm nhiều phase/đợt docs nếu không có lỗi thực.

**Repository:** `tbcphutai-bit/ADDIN-SolidWorks`
**Working branch:** `feat/check-hole-cg2-part-relations`
**PR:** https://github.com/tbcphutai-bit/ADDIN-SolidWorks/pull/33 (DRAFT, KHÔNG MERGE)
**Base:** `feat/check-hole-connection-groups-cg1` (chưa merge).
**Target:** C# 7.3 / .NET Framework 4.8 / SOLIDWORKS Windows, Debug|x64 & Release|x64.
**Status:** ChatGPT wrote source on GitHub; **not built or CAD-tested by ChatGPT**. Antigravity must verify reality.

## QUY ĐỊNH PHÂN CÔNG ĐÃ ĐÍNH CHÍNH — CHATGPT VIẾT CODE, ANTI CHỈ ÁP DỤNG

**Đây là yêu cầu trực tiếp của chủ dự án, có ưu tiên cao hơn bất kỳ câu chữ cũ nào trong tài liệu:**

- **ChatGPT sở hữu 100% thiết kế thuật toán và mã C#**, kể cả bản sửa compile/API/logic. ChatGPT phải cung cấp source hoàn chỉnh và chỉ rõ tên file, class, method, anchor đặt code, `ADDIN.csproj` và việc thêm/xóa đoạn cũ nếu cần.
- **Antigravity chỉ copy/paste hoặc đồng bộ CHÍNH XÁC code ChatGPT đã viết vào đúng file/class**, build, chạy kiểm thử SOLIDWORKS trong sandbox, thu thập lỗi, ghi báo cáo, commit/push. **KHÔNG tự viết, sửa, đổi tên, tái cấu trúc, tối ưu hay chữa lỗi C#** khi chưa được ChatGPT cung cấp patch cụ thể.
- Nếu compile/API/behavior FAIL, **DỪNG thay đổi code**, gửi nguyên compiler error/code location/SOLIDWORKS Interop version/fixture và log lên PR. **ChatGPT viết patch mới**, Anti chỉ áp dụng rồi build/test lại.
- Chỉ chỉnh tài liệu/báo cáo theo nội dung ChatGPT cung cấp; không biến NOT_TESTED/FAIL thành PASS.
- **TÌNH TRẠNG RIÊNG CỦA CG2 HIỆN TẠI:** ChatGPT đã commit code trực tiếp vào các file class của branch. Anti phải **pull để lấy đúng code**, kiểm tra rằng code đã nằm ở đúng vị trí; **không copy/paste lần nữa thành class/method trùng**. Những bản vá *tiếp theo* sẽ được ChatGPT viết đầy đủ trên GitHub để Anti thực hiện đúng thao tác copy/paste/apply, không tự sáng tác.
- Giữ PR #33 Draft, không merge và không bắt đầu CG3.

**Anti chỉ có một đợt bàn giao:** kiểm tra code đã đặt đúng, build/test một lượt, báo đầy đủ kết quả. Nếu có lỗi cần patch, báo về ChatGPT để người viết code sửa.

---

## 1. Cụ thể đã có code gì?

| File | Code thật đã commit |
|---|---|
| `Commands/CheckAssemblyHole.MaterialEvidence.cs` | NEW: `EnrichCg2BrepMaterialEvidence`, `Cg2LoadTargetBodyContext`, `Cg2PrepareBoundedRay`, `Cg2ReadRayEvidence`; 500-ray budget and unknown classifications. FULL SOURCE ở mục 5. |
| `Commands/CheckAssemblyHole.PartRelations.cs` | ADDED material fields `MaterialEvidence`, `MaterialSpanMm`, `MaterialHitCount` và 5 counters vào report; giữ nguyên AABB detector và CHECK_ONLY. |
| `Commands/CheckAssemblyHole.cs` | ADDED **one** BREP enrichment call after AABB screening. Dedicated non-fatal `catch`; Cancellation propagates. Không đổi `EvaluateStack`, Recovery, Missing/Pattern, marker. |
| `Commands/CheckHoleResultsForm.cs` | Shows counts **BREP có vật liệu / không giao trục / chưa xác minh** and puts proven solid rays first, retaining all candidates. |
| `ADDIN.csproj` | compile entry cho material module chính xác một lần. |
| `docs/antigravity/cg2/CG2_TEST_HARNESS.cs` | Added `RunBrepScreeningOfflineTests` that asserts source-index and ray range functions, plus corrected UI summary expectation. **Offline != CAD.** |

### Chính xác tính năng mới

- CG2 vẫn tự động duyệt occurrences; không yêu cầu chọn Part thủ công.
- AABB chỉ là vòng lọc đầu. Với ứng viên AABB, BREP check lấy solid bodies từ chính Component `GetBodies3(swSolidBody, out info)`, chuyển trục Assembly sang tọa độ local bằng inverse `Component.Transform2`, kiểm tra `IModelDocExtension.RayIntersections` ở chế độ `ENTRY_EXIT`.
- Để tránh báo bậy, chỉ xác nhận **có vật liệu thật trên trục** khi cùng một solid có cặp `ENTER -> EXIT` cách nhau ít nhất 0.10 mm; phân loại `BREP_SOLID_ON_AXIS`.
- Không có hit theo API: `BREP_NO_AXIS_HIT` (giảm ưu tiên; không xóa ứng viên). Lỗi COM, part không resolve, tọa độ/solid bất thường, grazing: `UNVERIFIED_*` (không ép thành âm tính).
- Capped at **500 ray tests**; những ứng viên còn lại là `NOT_TESTED_BUDGET`, báo `PARTIAL_UNVERIFIED`. 500 chỉ là budget UX/performance, không phải số Part/hole tối đa có thể tồn tại.
- **Không bao giờ** tự gán NG, Master Hole hoặc “thiếu lỗ chắc chắn”. `BREP_SOLID_ON_AXIS` chứng minh material trên đường ray, **KHÔNG chứng minh hai bề mặt tiếp xúc/ý định mối ghép/cần khoan lỗ xuyên**.
- Existing candidate IDs, AABB candidates, CG1 production report, CSV and NG marker unchanged. Không tự xóa/sửa CAD.

### Trạng thái bắt buộc xuất đúng

```text
BREP_SOLID_ON_AXIS = verified ray ENTER->EXIT span on same Body2 (not missing-hole NG)
BREP_NO_AXIS_HIT = ray API returned no solid-body intersections in tested span
UNVERIFIED_* = cannot make physical assertion; do not classify negative
NOT_TESTED_BUDGET = not processed; counts incomplete
CG2_CLASSIFICATION=CHECK_ONLY
CG2_REAL_FACE_CONTACT=NOT_IMPLEMENTED
CG2_REQUIRED_MISSING_HOLE_INTENT=NOT_IMPLEMENTED
CG2_MASTER_INFERENCE=NOT_IMPLEMENTED
READY_FOR_MERGE=NO
```

## 2. ANTIGRAVITY — thực hiện TẤT CẢ trong một đợt

**(A) Pull, kiểm code C# ChatGPT đã viết và xác nhận đúng vị trí. KHÔNG tự sửa C# dù có lỗi compile/API/BREP. Gửi đầy đủ lỗi để ChatGPT viết patch.**

```powershell
git fetch origin
git switch feat/check-hole-cg2-part-relations
git pull --ff-only origin feat/check-hole-cg2-part-relations
git rev-parse HEAD
git diff --check
```

Không copy code vào file trùng. Đọc đủ 6 file ở bảng trên. Verify API trong SOLIDWORKS Interop thực tế: `Component2.GetBodies3(int, out object)`, `MathTransform.Inverse()`, `Body2.GetBodyBox()`, `ModelDocExtension.RayIntersections(object, object, object, int, double, double, bool)` và `ModelDoc2.GetRayIntersectionsPoints()`. 

**(B) Rebuild + TEST một lần, ghi PASS/FAIL/NOT_TESTED theo từng case:**

```powershell
MSBuild.exe ADDIN.csproj /t:Rebuild /p:Configuration=Debug /p:Platform=x64 /v:minimal
if ($LASTEXITCODE -ne 0) { throw "BUILD_DEBUG_X64_FAIL" }
MSBuild.exe ADDIN.csproj /t:Rebuild /p:Configuration=Release /p:Platform=x64 /v:minimal
if ($LASTEXITCODE -ne 0) { throw "BUILD_RELEASE_X64_FAIL" }
```

- Chạy standalone `CG2_TEST_HARNESS.cs` theo cách build harness hiện có, phải có `UNIT_OFFLINE_PASS` cho `Cg2TryGetSourceIndex`, `Cg2PrepareBoundedRay`, mới phát sinh UI summary.
- **CAD-M01:** Solid Part B thật chắn đường trục tại site lỗ A; từ BREP `ENTER/EXIT` trả `BREP_SOLID_ON_AXIS`, span >= 0.1mm. Log target occurrence và số hits.
- **CAD-M02:** Part B có lỗ đồng trục thực, hoặc khối lõm AABB trùng tia nhưng không có material: `BREP_NO_AXIS_HIT` hoặc ứng viên AABB ban đầu bị suppressed bởi known hole. **Không tạo NG**.
- **CAD-M03:** Hai occurrences của cùng Part với **transform khác nhau** (dịch, xoay 90°), có target solid thật và khoảng trống thật; xác nhận frame transformation/calibration đúng. Nếu `UNVERIFIED_BODY_COORDINATES`, ghi FAIL/NOT_VERIFIED thay vì giả PASS.
- **CAD-M04:** Multi-body + Assembly-level cut: thực sự kiểm tra `GetBodies3` có trả body đúng instance sau cut; nếu API không chứng minh, ghi `UNVERIFIED_*` và giải thích giới hạn.
- **CAD-M05:** Unresolved/lightweight/suppressed/invalid/budget: phải hiển thị `PARTIAL_UNVERIFIED` và candidate không bị xóa, không bị tự quy NG; có ít nhất 1 case kích hoạt cap/unknown thật.
- **CAD-M06:** Assembly 916 holes / 1,297 AABB candidates trước đây: báo số `BREP_SOLID_ON_AXIS`, `BREP_NO_AXIS_HIT`, `UNVERIFIED`, `NOT_TESTED_BUDGET`, elapsed seconds, số calls và peak memory. **Không gọi 1,297 là missing holes.**
- **REGRESSION:** cùng bản sao Pack-and-Go, so trước/sau code *production* OK/NG, stacks, Missing/Pattern, row signatures, CSV writer và marker read-only. Log strict raw CSV bytes riêng; nếu fail như trước thì báo FAIL và không tự suy nguyên nhân. Marker creation parity vẫn NOT_TESTED nếu để AutoCreateMarkers=false.

**BẮT BUỘC dùng sandbox Pack-and-Go cô lập, có consent, không có phiên SOLIDWORKS người dùng đang hoạt động; giữ full fail-closed sandbox guards.** Không sửa Part/Assembly/Marker trên dữ liệu thật. Test ray API trả 0 và trả hit có thật; một danh sách toàn `UNVERIFIED` không được tính là BREP validation PASS.

**(C) Push một báo cáo hoàn chỉnh rồi dừng chờ ChatGPT kiểm tra trực tiếp.**

- Ghi tất cả số liệu trong `docs/antigravity/cg2/CG2_REPORT.md` và cập nhật bảng `CG2_ANTI_CODE_STATUS_REPORT.md` (class/method touched, before/after, author, API details, SHA DLL/log, C# changed/not changed).
- Commit + push CHỈ branch CG2; phản hồi PR #33 full SHA và summary 6 CAD cases; nếu COM API không tương thích thì Anti ghi nguyên compiler diagnostics để ChatGPT tự viết bản sửa; Anti KHÔNG được tự chỉnh method hay engine.
- **Không merge main, không đẩy CG1, không khởi động CG3.** ChatGPT sẽ đọc source diff và ra **một review tổng hợp duy nhất** thay vì bắt Anti báo cáo từng phần.

## 3. Acceptance gates rõ ràng

| Gate | Đạt khi |
|---|---|
| Build | Debug/Release x64 thật, 0 errors; những warning mới có nguyên nhân |
| BREP | Chứng minh cả hit và no-hit trên controlled fixtures; COM coordinate setup đã kiểm chứng |
| Safe unknown | Không có proof thì giữ `UNVERIFIED`; cap/exception hiển thị `PARTIAL_UNVERIFIED` |
| Performance | Có thời gian/số ray đo trên Assembly lớn, không freeze/hang; mọi cap được công bố |
| Regression | Production OK/NG/Pattern/Missing/marker không đổi; raw CSV parity báo đúng PASS/FAIL |
| Reporting | 1 commit source + test + log + report, chuẩn tên method và số liệu; chưa có test thì để NOT_TESTED |
| Merge | **READY_FOR_MERGE=NO** cho đến khi người dùng cho phép |

## 4. Code integration đã commit — CheckAssemblyHole.Run

```csharp
                // CG2 BREP diagnostic: actual solid hit evidence, never promotes CHECK_ONLY to NG.
                // Any COM/body/transform failure leaves the candidate UNVERIFIED.
                try
                {
                    if (lastReport.PartRelations != null)
                        EnrichCg2BrepMaterialEvidence(assembly, holes, lastReport.PartRelations);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception cg2BrepError)
                {
                    Debug.WriteLine("[CHECK HOLE CG2 BREP] material evidence unavailable: " + cg2BrepError);
                    if (lastReport.PartRelations != null)
                    {
                        lastReport.PartRelations.IsPartial = true;
                        lastReport.PartRelations.CoverageStatus = "PARTIAL_UNVERIFIED";
                        lastReport.PartRelations.Warnings.Add(
                            "BREP material screening failed: " + cg2BrepError.GetType().Name);
                    }
                }
```

## 5. TOÀN BỘ SOURCE MỚI — CheckAssemblyHole.MaterialEvidence.cs

Source này ĐÃ commit và nằm đúng file class trên GitHub. Anti chỉ pull/áp dụng nguyên vẹn; nếu có lỗi, báo về ChatGPT để ChatGPT sửa rồi cung cấp patch mới. KHÔNG chèn bản thứ hai.

```csharp
// CG2 BREP MATERIAL EVIDENCE — diagnostic-only, read-only.
// Runs AFTER existing CG2 AABB screening. Never assigns NG or required-hole intent.
// C# 7.3, SOLIDWORKS >= 2018 SP02 API, .NET Framework 4.8.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands
{
    public sealed partial class CheckAssemblyHole
    {
        // The ray test is deliberately capped; it is NOT a full-assembly validator.
        private const int Cg2MaxBrepRayChecks = 500;
        private const int Cg2MaxBrepBodiesPerOccurrence = 40;
        private const double Cg2MinMaterialSpanM = 0.00010; // 0.10 mm, diagnostic evidence threshold
        private const double Cg2BrepStartMarginM = 0.010; // start outside approximate target box
        private const double Cg2BrepOffsetM = 0.000001; // 1 micron, SOLIDWORKS ray entry/exit
        private const double Cg2BrepHitRadiusM = 0.0; // exact axis, not tolerance-expanded cylinder
        private const double Cg2BrepNumericalToleranceM = 0.00002;

        private sealed class Cg2TargetBodyContext
        {
            public Component2 Component;
            public ModelDoc2 PartModel;
            public Body2[] Bodies;
            public MathTransform FromWorldToPart;
            public Cg2Box WorldBox;
            public string Failure;
        }

        private sealed class Cg2RayHit
        {
            public int BodyIndex;
            public int Flags;
            public double AlongRayM;
        }

        /// <summary>
        /// Adds actual SOLIDWORKS BREP ray-intersection evidence to CG2 candidates.
        /// Calibrates local-body vs assembly coordinates before each target is trusted;
        /// unresolvable targets stay UNVERIFIED. NEVER removes an AABB candidate or
        /// mutates production stack verdicts, sketch markers, Part, Assembly or CSV.
        /// </summary>
        private void EnrichCg2BrepMaterialEvidence(
            AssemblyDoc assembly, List<HoleAxis> holes, CheckHoleCg2Report report)
        {
            if (assembly == null || holes == null || report == null)
                return;

            foreach (CheckHoleCg2ExpectedHoleRow candidate in report.ExpectedHoleCandidates)
            {
                candidate.MaterialEvidence = "NOT_TESTED";
                candidate.MaterialSpanMm = 0.0;
                candidate.MaterialHitCount = 0;
            }

            object[] components = ToObjectArray(assembly.GetComponents(false));
            var lookup = new Dictionary<string, Component2>(StringComparer.OrdinalIgnoreCase);
            foreach (object obj in components)
            {
                Component2 component = obj as Component2;
                if (!IsUsableComponent(component)) continue;
                string key = SafeComponentName(component);
                if (!string.IsNullOrWhiteSpace(key) && !lookup.ContainsKey(key))
                    lookup.Add(key, component);
            }

            var cachedContexts = new Dictionary<string, Cg2TargetBodyContext>(
                StringComparer.OrdinalIgnoreCase);

            // Sort only the diagnostic traversal priority, never mutate candidate IDs,
            // the original hole list, CG1 stack order, or production CSV rows.
            List<CheckHoleCg2ExpectedHoleRow> prioritized =
                report.ExpectedHoleCandidates
                    .OrderBy(c => c.BoxSeparationMm)
                    .ThenBy(c => c.AxisToBoxMm)
                    .ThenBy(c => c.Id)
                    .ToList();

            foreach (CheckHoleCg2ExpectedHoleRow row in prioritized)
            {
                ThrowIfCanceled();
                if (report.BrepRayTestedCount >= Cg2MaxBrepRayChecks)
                {
                    row.MaterialEvidence = "NOT_TESTED_BUDGET";
                    report.BrepNotTestedCount++;
                    report.IsPartial = true;
                    continue;
                }

                int holeIndex;
                if (!Cg2TryGetSourceIndex(row.SourceHoleLabel, holes.Count, out holeIndex)
                    || holes[holeIndex] == null
                    || !string.Equals(holes[holeIndex].ComponentOccurrence,
                                      row.SourceOccurrence, StringComparison.OrdinalIgnoreCase)
                    || !Cg2ValidVector(holes[holeIndex].Center)
                    || !Cg2ValidVector(holes[holeIndex].Direction))
                {
                    Cg2MarkBrepUnknown(row, report, "UNVERIFIED_SOURCE_HOLE");
                    continue;
                }

                Component2 target;
                if (!lookup.TryGetValue(row.TargetOccurrence, out target))
                {
                    Cg2MarkBrepUnknown(row, report, "UNVERIFIED_TARGET_OCCURRENCE");
                    continue;
                }

                Cg2TargetBodyContext context;
                if (!cachedContexts.TryGetValue(row.TargetOccurrence, out context))
                {
                    context = Cg2LoadTargetBodyContext(target);
                    cachedContexts.Add(row.TargetOccurrence, context);
                }

                if (!string.IsNullOrEmpty(context.Failure))
                {
                    Cg2MarkBrepUnknown(row, report, context.Failure);
                    continue;
                }

                // Compute the segment of the candidate axis within the target's
                // assembly-space AABB. It must be within +-60 mm of the source hole.
                double[] rayStartWorld;
                double[] unitWorld;
                double maxDistanceM;
                if (!Cg2PrepareBoundedRay(holes[holeIndex].Center,
                    holes[holeIndex].Direction, context.WorldBox,
                    out rayStartWorld, out unitWorld, out maxDistanceM))
                {
                    Cg2MarkBrepUnknown(row, report, "UNVERIFIED_RAY_RANGE");
                    continue;
                }

                double[] rayStartLocal = TransformPoint(rayStartWorld, context.FromWorldToPart);
                double[] rayDirLocal = TransformVector(unitWorld, context.FromWorldToPart);
                if (!Cg2ValidVector(rayStartLocal) || !Cg2ValidVector(rayDirLocal))
                {
                    Cg2MarkBrepUnknown(row, report, "UNVERIFIED_TRANSFORM");
                    continue;
                }

                string evidence;
                double spanM;
                int hitCount;
                try
                {
                    // Ray intersections work in the target part-local frame.
                    // Component.GetBodies3 includes instance-level body changes;
                    // bounding-center calibration in Cg2LoadTargetBodyContext
                    // detects suspicious coordinate mismatch and refuses to claim.
                    evidence = Cg2ReadRayEvidence(context.PartModel, context.Bodies,
                        rayStartLocal, rayDirLocal, maxDistanceM, out spanM, out hitCount);
                }
                catch (Exception ex)
                {
                    Cg2Warn(report, "BREP ray API error on a target occurrence: "
                        + ex.GetType().Name);
                    evidence = "UNVERIFIED_RAY_API";
                    spanM = 0;
                    hitCount = 0;
                }

                row.MaterialEvidence = evidence;
                row.MaterialHitCount = hitCount;
                row.MaterialSpanMm = spanM * 1000.0;
                report.BrepRayTestedCount++;

                if (evidence == "BREP_SOLID_ON_AXIS")
                    report.BrepSolidEvidenceCount++;
                else if (evidence == "BREP_NO_AXIS_HIT")
                    report.BrepNoAxisHitCount++;
                else
                {
                    report.BrepUnknownCount++;
                    report.IsPartial = true;
                }
            }

            if (report.BrepNotTestedCount > 0)
                Cg2Warn(report, "BREP ray budget reached: "
                    + report.BrepNotTestedCount.ToString(CultureInfo.InvariantCulture)
                    + " candidates NOT TESTED. AABB remains CHECK_ONLY.");

            if (report.IsPartial)
                report.CoverageStatus = "PARTIAL_UNVERIFIED";

            // A hit proves BREP material along the tested axis only.
            // It does NOT prove a through-hole is required, or that a joint exists.
        }

        private static void Cg2MarkBrepUnknown(
            CheckHoleCg2ExpectedHoleRow row, CheckHoleCg2Report report, string why)
        {
            row.MaterialEvidence = why;
            report.BrepUnknownCount++;
            report.IsPartial = true;
        }

        private static bool Cg2TryGetSourceIndex(
            string label, int count, out int index)
        {
            index = -1;
            if (string.IsNullOrEmpty(label) ||
                !label.StartsWith("Hole#", StringComparison.Ordinal))
                return false;
            int oneBased;
            if (!int.TryParse(label.Substring(5), NumberStyles.None,
                              CultureInfo.InvariantCulture, out oneBased)
                || oneBased < 1 || oneBased > count)
                return false;
            index = oneBased - 1;
            return true;
        }

        private Cg2TargetBodyContext Cg2LoadTargetBodyContext(Component2 component)
        {
            var result = new Cg2TargetBodyContext { Component = component };
            try
            {
                ModelDoc2 part = component.GetModelDoc2() as ModelDoc2;
                if (part == null || !(part is PartDoc))
                {
                    result.Failure = "UNVERIFIED_NOT_RESOLVED_PART";
                    return result;
                }
                MathTransform placement = component.Transform2;
                MathTransform inverse = placement == null
                    ? null : placement.Inverse() as MathTransform;
                if (placement == null || inverse == null)
                {
                    result.Failure = "UNVERIFIED_NO_COMPONENT_TRANSFORM";
                    return result;
                }

                object info;
                var bodies = ToObjectArray(component.GetBodies3(
                    (int)swBodyType_e.swSolidBody, out info))
                    .OfType<Body2>().ToArray();
                if (bodies.Length == 0 || bodies.Length > Cg2MaxBrepBodiesPerOccurrence)
                {
                    result.Failure = "UNVERIFIED_NO_OR_TOO_MANY_SOLIDS";
                    return result;
                }

                Cg2Box worldBox = Cg2ReadBox(SafeComponentName(component),
                    ToDoubleArray(component.GetBox(false, false)));
                if (worldBox == null)
                {
                    result.Failure = "UNVERIFIED_COMPONENT_BOX";
                    return result;
                }

                // Calibration checks the coordinate-space assumption; approximate
                // bounding boxes are used ONLY to reject suspicious COM geometry,
                // never to prove a material hit.
                if (!Cg2BodyCoordinatesCalibrated(bodies, placement, worldBox))
                {
                    result.Failure = "UNVERIFIED_BODY_COORDINATES";
                    return result;
                }

                result.PartModel = part;
                result.Bodies = bodies;
                result.FromWorldToPart = inverse;
                result.WorldBox = worldBox;
                return result;
            }
            catch (Exception ex)
            {
                result.Failure = "UNVERIFIED_BODY_API_" + ex.GetType().Name;
                return result;
            }
        }

        private bool Cg2BodyCoordinatesCalibrated(
            Body2[] bodies, MathTransform placement, Cg2Box expectedWorld)
        {
            var localMin = new[] {
                double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity };
            var localMax = new[] {
                double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity };

            foreach (Body2 body in bodies)
            {
                double[] box = ToDoubleArray(body.GetBodyBox());
                if (box.Length < 6) return false;
                for (int k = 0; k < 3; k++)
                {
                    if (double.IsInfinity(box[k]) || double.IsNaN(box[k])
                        || double.IsInfinity(box[k+3]) || double.IsNaN(box[k+3]))
                        return false;
                    localMin[k] = Math.Min(localMin[k], Math.Min(box[k], box[k+3]));
                    localMax[k] = Math.Max(localMax[k], Math.Max(box[k], box[k+3]));
                }
            }
            double[] centerLocal = new[] {
                (localMin[0] + localMax[0])*0.5,
                (localMin[1] + localMax[1])*0.5,
                (localMin[2] + localMax[2])*0.5 };
            double[] world = TransformPoint(centerLocal, placement);
            if (!Cg2ValidVector(world)) return false;
            double sq = 0, diagSq = 0;
            for (int k = 0; k < 3; k++)
            {
                double center = (expectedWorld.Min[k] + expectedWorld.Max[k])*0.5;
                double delta = world[k] - center;
                sq += delta * delta;
                double dim = expectedWorld.Max[k] - expectedWorld.Min[k];
                diagSq += dim * dim;
            }
            // 2mm / 5% of total target diagonal: diagnostic rejection only.
            return Math.Sqrt(sq) <= Math.Max(0.002, Math.Sqrt(diagSq)*0.05);
        }

        private static bool Cg2PrepareBoundedRay(
            double[] source, double[] direction, Cg2Box box,
            out double[] start, out double[] unit, out double maxDistM)
        {
            start = null;
            unit = null;
            maxDistM = 0;
            if (!Cg2ValidVector(source) || !Cg2ValidVector(direction) || box == null)
                return false;
            double mag = Math.Sqrt(direction.Take(3).Sum(x => x*x));
            if (mag < 1e-12) return false;
            unit = direction.Take(3).Select(x => x/mag).ToArray();

            double minT = 0, maxT = 0;
            for (int k = 0; k < 3; k++)
            {
                double p = (box.Min[k] - source[k])*unit[k];
                double q = (box.Max[k] - source[k])*unit[k];
                minT += Math.Min(p, q);
                maxT += Math.Max(p, q);
            }
            minT = Math.Max(minT-Cg2BrepStartMarginM, -Cg2MaxAxisReachM);
            maxT = Math.Min(maxT+Cg2BrepStartMarginM, Cg2MaxAxisReachM);
            if (maxT <= minT + Cg2MinMaterialSpanM) return false;
            start = new[] {
                source[0] + unit[0]*minT,
                source[1] + unit[1]*minT,
                source[2] + unit[2]*minT };
            maxDistM = maxT - minT;
            return true;
        }

        private static string Cg2ReadRayEvidence(
            ModelDoc2 model, Body2[] bodies,
            double[] start, double[] direction, double limitM,
            out double longestSpanM, out int hitCount)
        {
            longestSpanM = 0;
            hitCount = 0;
            if (model == null || bodies == null || bodies.Length == 0)
                return "UNVERIFIED_NO_BODY";

            // With only ENTRY_EXIT option, GetRayIntersectionsPoints gives
            // six doubles per hit: BodyIndex, RayIndex, Type, x, y, z.
            // This is a read-only geometry operation, not a sketch/edit.
            int count = model.Extension.RayIntersections(
                bodies, start, direction,
                (int)swRayPtsOpts_e.swRayPtsOptsENTRY_EXIT,
                Cg2BrepHitRadiusM, Cg2BrepOffsetM, true);
            if (count < 0) return "UNVERIFIED_RAY_RETURN";
            hitCount = count;
            if (count == 0) return "BREP_NO_AXIS_HIT";
            if (count > 200) return "UNVERIFIED_TOO_MANY_RAY_HITS";
            double[] points = ToDoubleArray(model.GetRayIntersectionsPoints());
            if (points == null || points.Length < 6*count)
                return "UNVERIFIED_RAY_POINTS";

            double mag = Math.Sqrt(direction.Take(3).Sum(x => x*x));
            if (mag < 1e-12) return "UNVERIFIED_ZERO_RAY";
            var rays = new List<Cg2RayHit>();
            for (int i = 0; i < count; i++)
            {
                int offset = i * 6;
                int body = (int)Math.Round(points[offset]);
                int ray = (int)Math.Round(points[offset + 1]);
                int flags = (int)Math.Round(points[offset + 2]);
                if (body < 0 || body >= bodies.Length || ray != 0)
                    return "UNVERIFIED_RAY_TOPOLOGY";
                double t = 0;
                for (int k = 0; k < 3; k++)
                {
                    if (double.IsNaN(points[offset+3+k]) ||
                        double.IsInfinity(points[offset+3+k]))
                        return "UNVERIFIED_RAY_COORDINATES";
                    t += (points[offset+3+k]-start[k]) * direction[k]/mag;
                }
                if (t < -Cg2BrepNumericalToleranceM ||
                    t > limitM + Cg2BrepNumericalToleranceM)
                    continue;
                rays.Add(new Cg2RayHit { BodyIndex=body, Flags=flags, AlongRayM=t });
            }
            if (rays.Count == 0)
                return "UNVERIFIED_HITS_OUTSIDE_TEST_SPAN";

            // A positive result must have a definite ENTER => EXIT segment
            // on the SAME SOLID BODY, not a single touching edge/grazing hit.
            foreach (var group in rays.GroupBy(h => h.BodyIndex))
            {
                double? entry = null;
                foreach (Cg2RayHit hit in group.OrderBy(h => h.AlongRayM))
                {
                    bool entering = (hit.Flags & (int)swRayPtsResults_e.swRayPtsResultsENTER) != 0;
                    bool leaving  = (hit.Flags & (int)swRayPtsResults_e.swRayPtsResultsEXIT) != 0;
                    if (entering && !leaving) entry = hit.AlongRayM;
                    else if (leaving && !entering && entry.HasValue)
                    {
                        double span = hit.AlongRayM - entry.Value;
                        if (span > longestSpanM) longestSpanM = span;
                        entry = null;
                    }
                    else entry = null; // ambiguous grazing/edge events
                }
            }
            return longestSpanM >= Cg2MinMaterialSpanM
                ? "BREP_SOLID_ON_AXIS" : "UNVERIFIED_AMBIGUOUS_HITS";
        }
    }
}
```

## 6. API references (official)

- `IModelDocExtension.RayIntersections`: https://help.solidworks.com/2026/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IModelDocExtension~RayIntersections.html
- `IModelDoc2.GetRayIntersectionsPoints`: https://help.solidworks.com/2017/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IModelDoc2~GetRayIntersectionsPoints.html
- `IComponent2.GetBodies3`: https://help.solidworks.com/2018/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IComponent2~GetBodies3.html
- `IMathTransform.Inverse`: https://help.solidworks.com/2022/english/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.IMathTransform~Inverse.html

## 7. MẪU PHẢN HỒI BẮT BUỘC CỦA ANTI (sau khi push)

```text
CG2_BREP_ONE_SHOT=FINISHED
PR=33
HEAD_SHA=<full pushed SHA>
C_SHARP_FILES_CHANGED=<count and names>
BUILD_DEBUG_X64=<PASS/FAIL>
BUILD_RELEASE_X64=<PASS/FAIL>
OFFLINE_BREP_UNIT_TEST=<PASS/FAIL>
CAD_M01..CAD_M06=<each with PASS/FAIL/NOT_TESTED, evidence>
BREP_SOLID_ON_AXIS=<measured count>
BREP_NO_AXIS_HIT=<measured count>
BREP_UNKNOWN=<measured count>
BREP_NOT_TESTED_BUDGET=<measured count>
CG1_VS_CG2_PRODUCTION_VERDICTS=<PASS/FAIL/NOT_VERIFIED>
STRICT_CSV_BYTE_PARITY=<PASS/FAIL/NOT_TESTED>
MARKER_GENERATION_PARITY=NOT_TESTED
READY_FOR_CHATGPT_SOURCE_REVIEW=YES
READY_FOR_MERGE=NO
```

**Nhắc lại:** BREP ray chỉ kiểm tra solid chắn trục lỗ, không phải phép đo face-to-face contact/bolted design intent. Không thêm NG mới vì kết quả CG2.
