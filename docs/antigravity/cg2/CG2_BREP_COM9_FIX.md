# CG2 BREP — CHATGPT FIX CHÍNH THỨC: COM 9-DOUBLE RAY STRIDE

**Branch:** `feat/check-hole-cg2-part-relations`  
**PR:** https://github.com/tbcphutai-bit/ADDIN-SolidWorks/pull/33  
**Trách nhiệm:** ChatGPT **viết C#**, Antigravity **chỉ pull/copy nguyên văn, build, kiểm thử và báo cáo**. Cấm Anti tự chữa lỗi code.

## A. Review thật và nguyên nhân lỗi

Anti vừa push commit `520d4969241990aa10163458f4dcde2ff0b601f2`, chỉ sửa hai file báo cáo, không thay C#. Các log cho biết SOLIDWORKS 2024 SP5.0 trả 2 điểm giao ray trên tấm B đặc dày 3mm, `GetRayIntersectionsPoints()` trả 18 `double` (9/hit):

```text
hit 0: 0,0,17, 3.432,0.000,-1.094, 0,-1,0
hit 1: 0,0,33, 3.432,0.003,-1.094, 0, 1,0
```

Lỗi C# trước đây: `Cg2ClassifyRayPointData` dùng `offset=i*6`, nên hit 1 lấy nhầm normal `ny=-1` thành RayIndex, trả `UNVERIFIED_RAY_TOPOLOGY` thay vì xác định vật liệu.

**Code mới do ChatGPT viết đã trực tiếp nằm trong đúng file GitHub**. Không copy vào class lần thứ hai.

- `Commands/CheckAssemblyHole.MaterialEvidence.cs`: chấp nhận đúng **6 hoặc 9 giá trị/hit theo kích thước mảng/count**, đặt `offset=i*stride`; mọi layout lạ -> `UNVERIFIED_RAY_LAYOUT`, không báo hit hay no-hit giả. Kiểm COM index, flag, tọa độ hữu hạn trước khi tin kết quả.
- `docs/antigravity/cg2/CG2_TEST_HARNESS.cs`: giữ test 6-value, thêm **test đúng mẫu 18 giá trị/2 hits** với `17=ENTER|FACE`, `33=EXIT|FACE`, đòi `BREP_SOLID_ON_AXIS` và `MaterialSpan=0.003m`; thêm test mảng dài 17 và ray-index sai bắt buộc `UNVERIFIED`.

**Nguồn SOLIDWORKS chính thức:** https://help.solidworks.com/2017/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IModelDoc2~GetRayIntersectionsPoints.html  
và https://help.solidworks.com/2025/English/api/swconst/SolidWorks.Interop.swconst~SolidWorks.Interop.swconst.swRayPtsResults_e.html  
Docs nói normal có thể được trả về khi option NORMALS, nhưng **thực nghiệm Anti quan sát 9 doubles ngay cả khi code chỉ yêu cầu ENTRY_EXIT**. Vì vậy code xác nhận layout ở runtime thay vì đoán theo option.

## B. MỘT LƯỢT VIỆC CHO ANTIGRAVITY — KHÔNG TỰ SỬA CODE

1. Pull branch CG2 **HEAD mới nhất**. Xác nhận file `Commands/CheckAssemblyHole.MaterialEvidence.cs` chứa đúng một `Cg2ClassifyRayPointData`, đoạn xác định `stride=9` hoặc `stride=6`; `CG2_TEST_HARNESS.cs` có `PASS: 9-double COM layout returns 3.000mm solid span`.
2. Build Debug|x64 và Release|x64; chạy offline CG2_TEST_HARNESS với DLL vừa build. Bắt buộc test 6/9 stride, malformed layout, wrong RayIndex, ray-slab, disclaimer đều PASS. Nếu lỗi compiler, **DỪNG, không sửa C#**, gửi compiler error đầy đủ cho ChatGPT.
3. Trong **sandbox Pack-and-Go đã được phê duyệt**, chạy lại **`Fixture_BREP_Positive.SLDASM`**: A có lỗ, B là tấm đặc chắn ngang trục. Trong report CG2 mới yêu cầu `BREP_SOLID_ON_AXIS >= 1` (fixture trước có 12 candidate rays), `MaterialHitCount >= 2`, `MaterialSpanMm` xấp xỉ **3.000 mm**, không còn lỗi `UNVERIFIED_RAY_TOPOLOGY` tại cùng site. Khi có zero hit hoặc UNKNOWN, báo FAIL/NOT_VERIFIED, không tự tuyên bố thành công.
4. Chạy fixture đối chứng `Fixture_OK.SLDASM` có lỗ đối ứng: giữ `BREP_NO_AXIS_HIT` hoặc suppression theo source đối ứng; không tạo NG. Kiểm tra bảo vệ `PARTIAL_UNVERIFIED` và candidates không biến mất.
5. So **production verdicts** CG1/CG2 trên các fixture cần thiết bằng isolated runner. Phân biệt strict CSV byte parity (lần trước chỉ 4/6 PASS, có baseline nondeterminism) với normalized equivalence. **Không sửa/ẩn kết quả FAIL**. Không test marker generation trên CAD gốc.
6. Gộp kết quả vào `docs/antigravity/cg2/CG2_REPORT.md` và `CG2_ANTI_CODE_STATUS_REPORT.md`, báo full source SHA + DLL SHA256, câu lệnh, exit codes, số hit/unknown, elapsed time, file/class/method đã áp dụng, commit/push 1 lần và dừng để ChatGPT review.

**Giới hạn chưa giải quyết:** 984 target subassembly không resolve thành Part, hiệu năng Assembly lớn trước đo ~197.6s, material ray hit không chứng minh fastening intent hoặc bắt buộc cần lỗ, marker generation vẫn NOT_TESTED. **Không merge PR #33, không triển khai CG3.**

## C. Tiêu chí kết thúc đơn giản

```text
CHATGPT_CSHARP_PATCH_APPLIED=YES
ANTI_CSHARP_MODIFICATION=NO
BUILD_DEBUG_X64=PASS/FAIL
BUILD_RELEASE_X64=PASS/FAIL
OFFLINE_6_AND_9_STRIDE_TEST=PASS/FAIL
LIVE_3MM_SOLID_POSITIVE=PASS/FAIL/NOT_TESTED
LIVE_POSITIVE_MATERIAL_SPAN_MM=<measured>
LIVE_POSITIVE_COM_HITS=<measured>
NEGATIVE_HOLE_FIXTURE=PASS/FAIL/NOT_TESTED
CG1_VS_CG2_VERDICTS=PASS/FAIL/NOT_VERIFIED
RAW_CSV_BYTE_PARITY=PASS/FAIL/NOT_TESTED
SOURCE_SHA=<full remote sha>
COMPILED_DLL_SHA256=<hash>
READY_FOR_CHATGPT_SOURCE_REVIEW=YES
READY_FOR_MERGE=NO
```

## D. FULL C# CODE — CheckAssemblyHole.MaterialEvidence.cs

**Đã commit trong đúng file:** `Commands/CheckAssemblyHole.MaterialEvidence.cs`.

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

        /// <summary>
        /// Actual infinite-line versus AABB slab intersection, then a finite ray
        /// that begins BEFORE the target AABB (rather than inside its solid).
        /// AABB is approximate; failure to establish the entire test window
        /// returns UNVERIFIED_RAY_RANGE, NEVER BREP_NO_AXIS_HIT.
        /// </summary>
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

            // Exact slab algorithm for the APPROXIMATE target AABB. The previous
            // projection method summed componentwise min/max dot products;
            // those are NOT ray/box intersections (a parallel displaced ray
            // incorrectly passed), so it could use an invalid ray window.
            double entryT = double.NegativeInfinity;
            double exitT = double.PositiveInfinity;
            for (int k = 0; k < 3; k++)
            {
                double lo = box.Min[k];
                double hi = box.Max[k];
                if (double.IsNaN(lo) || double.IsNaN(hi) ||
                    double.IsInfinity(lo) || double.IsInfinity(hi) ||
                    lo > hi) return false;

                double d = unit[k];
                if (Math.Abs(d) < 1e-12)
                {
                    if (source[k] < lo || source[k] > hi) return false;
                    continue;
                }

                double a = (lo - source[k]) / d;
                double b = (hi - source[k]) / d;
                if (a > b) { double swap = a; a = b; b = swap; }
                entryT = Math.Max(entryT, a);
                exitT = Math.Min(exitT, b);
                if (entryT > exitT) return false;
            }
            if (double.IsNaN(entryT) || double.IsInfinity(entryT) ||
                double.IsNaN(exitT) || double.IsInfinity(exitT))
                return false;

            // Start OUTSIDE the component AABB. Do not claim "no solid" if
            // the finite diagnostic window truncates a target body.
            // Keep the diagnostic ±60 mm window but use as much exterior
            // clearance as it allows. A 55 mm target exit leaves only 5 mm,
            // so requiring a fixed 10 mm at BOTH ends would reject a valid ray.
            const double minimumOutsideMarginM = 0.00005;
            double beforeSlack = entryT + Cg2MaxAxisReachM;
            double afterSlack = Cg2MaxAxisReachM - exitT;
            if (beforeSlack < minimumOutsideMarginM ||
                afterSlack < minimumOutsideMarginM)
                return false;
            double fromT = entryT - Math.Min(Cg2BrepStartMarginM, beforeSlack);
            double toT = exitT + Math.Min(Cg2BrepStartMarginM, afterSlack);
            if (toT - fromT < Cg2MinMaterialSpanM)
                return false;

            start = new[] {
                source[0] + unit[0]*fromT,
                source[1] + unit[1]*fromT,
                source[2] + unit[2]*fromT };
            maxDistM = toT - fromT;
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

            // SOLIDWORKS 2024 SP5 returned 9 doubles per hit with normals
            // even though only ENTRY_EXIT was requested. Never assume a fixed
            // stride: Cg2ClassifyRayPointData verifies the full array layout.
            // RayIntersections is read-only; no sketch or feature is created.
            int count = model.Extension.RayIntersections(
                bodies, start, direction,
                (int)swRayPtsOpts_e.swRayPtsOptsENTRY_EXIT,
                Cg2BrepHitRadiusM, Cg2BrepOffsetM, true);
            if (count < 0) return "UNVERIFIED_RAY_RETURN";
            hitCount = count;
            if (count == 0) return "BREP_NO_AXIS_HIT";
            if (count > 200) return "UNVERIFIED_TOO_MANY_RAY_HITS";
            double[] points = ToDoubleArray(model.GetRayIntersectionsPoints());
            return Cg2ClassifyRayPointData(points, count, bodies.Length,
                start, direction, limitM, out longestSpanM);
        }

        // Pure parser of the native SOLIDWORKS ray results, testable with
        // artificial ENTRY/EXIT arrays; COM tests must additionally prove
        // GetRayIntersectionsPoints uses the same indexing in the installed
        // SOLIDWORKS version.
        private static string Cg2ClassifyRayPointData(
            double[] points, int count, int bodyCount,
            double[] start, double[] direction, double limitM,
            out double longestSpanM)
        {
            longestSpanM = 0;
            if (count <= 0 || bodyCount <= 0 || points == null ||
                !Cg2ValidVector(start) || !Cg2ValidVector(direction))
                return "UNVERIFIED_RAY_POINTS";

            // Expected layouts per SOLIDWORKS API documentation:
            // 6 doubles: body, ray, flags, hit X/Y/Z
            // 9 doubles: same six, followed by hit normal X/Y/Z.
            // On 2024 SP5, the installed COM API produced 9 even with
            // swRayPtsOptsENTRY_EXIT only. Reject every other shape instead
            // of reading a normal component as the next hit's RayIndex.
            int stride;
            if (points.Length == count * 9)
                stride = 9;
            else if (points.Length == count * 6)
                stride = 6;
            else
                return "UNVERIFIED_RAY_LAYOUT";

            double mag = Math.Sqrt(direction.Take(3).Sum(x => x*x));
            if (mag < 1e-12) return "UNVERIFIED_ZERO_RAY";
            var rays = new List<Cg2RayHit>();
            for (int i = 0; i < count; i++)
            {
                int offset = i * stride;
                for (int k = 0; k < stride; k++)
                    if (double.IsNaN(points[offset + k]) ||
                        double.IsInfinity(points[offset + k]))
                        return "UNVERIFIED_RAY_COORDINATES";

                // COM indices and bitmask must be integral; never silently
                // round malformed payloads into an apparently valid hit.
                double bodyValue = points[offset];
                double rayValue = points[offset + 1];
                double flagValue = points[offset + 2];
                if (bodyValue != Math.Truncate(bodyValue) ||
                    rayValue != Math.Truncate(rayValue) ||
                    flagValue != Math.Truncate(flagValue) ||
                    bodyValue < 0 || bodyValue >= bodyCount ||
                    rayValue != 0 || flagValue < 0 ||
                    flagValue > int.MaxValue)
                    return "UNVERIFIED_RAY_TOPOLOGY";
                int body = (int)bodyValue;
                int flags = (int)flagValue;
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

## E. FULL OFFLINE BREP TEST METHOD

**Đã commit trong đúng method:** `CG2_TEST_HARNESS.cs :: RunBrepScreeningOfflineTests`.

```csharp
        private static int RunBrepScreeningOfflineTests(Assembly asm)
        {
            Console.WriteLine("\n--- CG2 BREP OFFLINE: source index, bounded ray, fields ---");
            int errors = 0;
            Type checker = asm.GetType("ADDIN.Commands.CheckAssemblyHole");
            Type row = asm.GetType("ADDIN.Commands.CheckHoleCg2ExpectedHoleRow");
            Type report = asm.GetType("ADDIN.Commands.CheckHoleCg2Report");
            if (checker == null || row == null || report == null)
            {
                Console.WriteLine("FAIL: BREP-related types missing");
                return 1;
            }
            foreach (string field in new[] {
                "BrepRayTestedCount", "BrepSolidEvidenceCount",
                "BrepNoAxisHitCount", "BrepUnknownCount", "BrepNotTestedCount" })
            {
                if (report.GetField(field) == null) { errors++; Console.WriteLine("FAIL: " + field); }
            }
            foreach (string field in new[] {
                "MaterialEvidence", "MaterialSpanMm", "MaterialHitCount" })
            {
                if (row.GetProperty(field) == null) { errors++; Console.WriteLine("FAIL: " + field); }
            }

            MethodInfo source = checker.GetMethod(
                "Cg2TryGetSourceIndex", BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo prep = checker.GetMethod(
                "Cg2PrepareBoundedRay", BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo boxMaker = checker.GetMethod(
                "Cg2ReadBox", BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo enrich = checker.GetMethod(
                "EnrichCg2BrepMaterialEvidence", BindingFlags.NonPublic | BindingFlags.Instance);
            if (source == null || prep == null || boxMaker == null || enrich == null)
            {
                Console.WriteLine("FAIL: material screening methods missing");
                return errors + 1;
            }

            object[] testIndex = { "Hole#2", 3, -1 };
            bool validIndex = (bool)source.Invoke(null, testIndex);
            object[] invalidIndex = { "Hole#4", 3, -1 };
            bool invalidAccepted = (bool)source.Invoke(null, invalidIndex);
            if (!validIndex || (int)testIndex[2] != 1 || invalidAccepted)
            {
                Console.WriteLine("FAIL: CG2 source Hole# index mapping");
                errors++;
            }
            else Console.WriteLine("PASS: Hole#2 -> zero-based 1; out-of-range source rejected");

            object b = boxMaker.Invoke(null, new object[] {
                "B-1", new[] { 0.04, -0.01, -0.01, 0.055, 0.01, 0.01 } });
            object[] args = {
                new[] { 0.0, 0.0, 0.0 }, new[] { 1.0, 0.0, 0.0 },
                b, null, null, 0.0
            };
            bool intersects = (bool)prep.Invoke(null, args);
            double[] start = args[3] as double[];
            double[] unit = args[4] as double[];
            double length = (double)args[5];
            if (!intersects || start == null || unit == null
                || Math.Abs(start[0] - 0.03) > 1e-9
                || Math.Abs(unit[0] - 1.0) > 1e-9
                || length <= 0 || length > 0.060001)
            {
                Console.WriteLine("FAIL: bounded ray clipping");
                errors++;
            }
            else Console.WriteLine("PASS: bounded source axis clipped to near-target range");

            // A shifted parallel ray NEVER intersects the target AABB.
            // Previous projection-based code incorrectly returned true.
            object[] shifted = {
                new[] { 0.0, 0.03, 0.0 }, new[] { 1.0, 0.0, 0.0 },
                b, null, null, 0.0
            };
            bool falseHit = (bool)prep.Invoke(null, shifted);
            if (falseHit)
            {
                Console.WriteLine("FAIL: ray parallel to AABB, but outside Y slab, was accepted");
                errors++;
            }
            else Console.WriteLine("PASS: ray outside target Y slab correctly rejected");

            // A rotated/diagonal axis must intersect the actual shared
            // interval of all three slabs, not merely projected box extents.
            object diagonalBox = boxMaker.Invoke(null, new object[] {
                "rotated-B", new[] { 0.02, 0.02, -0.005, 0.035, 0.035, 0.005 } });
            object[] diagonal = {
                new[] { 0.0, 0.0, 0.0 }, new[] { 1.0, 1.0, 0.0 },
                diagonalBox, null, null, 0.0
            };
            bool diagonalHit = (bool)prep.Invoke(null, diagonal);
            if (!diagonalHit || diagonal[3] == null || diagonal[4] == null ||
                (double)diagonal[5] <= 0)
            {
                Console.WriteLine("FAIL: valid diagonal ray missed target AABB");
                errors++;
            }
            else Console.WriteLine("PASS: diagonal ray-AABB slab intersection");

            object farBox = boxMaker.Invoke(null, new object[] {
                "far-B", new[] { 0.055, -0.01, -0.01, 0.08, 0.01, 0.01 } });
            object[] truncated = {
                new[] { 0.0, 0.0, 0.0 }, new[] { 1.0, 0.0, 0.0 },
                farBox, null, null, 0.0
            };
            if ((bool)prep.Invoke(null, truncated))
            {
                Console.WriteLine("FAIL: target AABB beyond 60mm window was accepted");
                errors++;
            }
            else Console.WriteLine("PASS: incomplete ray range rejected, must remain UNVERIFIED");


            MethodInfo classifier = checker.GetMethod(
                "Cg2ClassifyRayPointData", BindingFlags.NonPublic | BindingFlags.Static);
            if (classifier == null)
            {
                Console.WriteLine("FAIL: CG2 pure BREP ENTRY/EXIT result classifier missing");
                errors++;
            }
            else
            {
                // Each hit is 6 doubles (without normals) OR 9 doubles
                // (with normals), as verified on SOLIDWORKS 2024 SP5.
                // Synthetic tests prove parser math, not the COM call itself.
                double[] enterExit = {
                    0, 0, 16, 0.020, 0, 0,
                    0, 0, 32, 0.024, 0, 0
                };
                object[] okRay = {
                    enterExit, 2, 1, new[] { 0.0, 0.0, 0.0 },
                    new[] { 1.0, 0.0, 0.0 }, 0.05, 0.0
                };
                string hit = (string)classifier.Invoke(null, okRay);
                if (hit != "BREP_SOLID_ON_AXIS" ||
                    Math.Abs((double)okRay[6]-0.004) > 1e-9)
                {
                    Console.WriteLine("FAIL: synthetic ENTRY->EXIT positive-material span");
                    errors++;
                }
                else Console.WriteLine("PASS: synthetic 4mm solid ENTRY/EXIT span confirmed");
                
                // Exact 2024 SP5 COM layout observed by Antigravity from a
                // 3mm solid target: body, ray, flags, xyz, surface normal xyz.
                // Normal ny=-1 on hit 0 MUST NOT be read as hit 1 RayIndex.
                double[] native9 = {
                    0, 0, 17, 3.432, 0.000, -1.094, 0, -1, 0,
                    0, 0, 33, 3.432, 0.003, -1.094, 0,  1, 0
                };
                object[] nineHit = {
                    native9, 2, 1, new[] { 3.432, -0.010, -1.094 },
                    new[] { 0.0, 1.0, 0.0 }, 0.030, 0.0
                };
                string nineResult = (string)classifier.Invoke(null, nineHit);
                if (nineResult != "BREP_SOLID_ON_AXIS" ||
                    Math.Abs((double)nineHit[6] - 0.003) > 1e-9)
                {
                    Console.WriteLine("FAIL: SOLIDWORKS 2024 nine-double/hit ENTER-EXIT layout; result=" +
                        nineResult);
                    errors++;
                }
                else Console.WriteLine("PASS: 9-double COM layout returns 3.000mm solid span");

                // Unknown result-array shapes must remain UNVERIFIED rather
                // than guess 6 or 9 values per native intersection.
                object[] badShape = {
                    new double[17], 2, 1, new[] { 3.432, -0.010, -1.094 },
                    new[] { 0.0, 1.0, 0.0 }, 0.030, 0.0
                };
                if ((string)classifier.Invoke(null, badShape) != "UNVERIFIED_RAY_LAYOUT")
                {
                    Console.WriteLine("FAIL: malformed COM ray payload was accepted");
                    errors++;
                }
                else Console.WriteLine("PASS: unsupported ray-result stride remains UNVERIFIED");

                // A malformed second RayIndex is topology ambiguity, never
                // a material-positive hit.
                double[] invalidRayIndex = (double[])native9.Clone();
                invalidRayIndex[10] = 1;
                object[] invalidIndexArgs = {
                    invalidRayIndex, 2, 1, new[] { 3.432, -0.010, -1.094 },
                    new[] { 0.0, 1.0, 0.0 }, 0.030, 0.0
                };
                if ((string)classifier.Invoke(null, invalidIndexArgs) !=
                    "UNVERIFIED_RAY_TOPOLOGY")
                {
                    Console.WriteLine("FAIL: malformed second ray index was accepted");
                    errors++;
                }
                else Console.WriteLine("PASS: invalid second ray index remains UNVERIFIED");


                double[] onlyEnter = { 0, 0, 16, 0.020, 0, 0 };
                object[] ambiguous = {
                    onlyEnter, 1, 1, new[] { 0.0, 0.0, 0.0 },
                    new[] { 1.0, 0.0, 0.0 }, 0.05, 0.0
                };
                if ((string)classifier.Invoke(null, ambiguous) !=
                    "UNVERIFIED_AMBIGUOUS_HITS")
                {
                    Console.WriteLine("FAIL: lone ENTRY classified as proven material");
                    errors++;
                }
                else Console.WriteLine("PASS: lone ENTRY remains UNVERIFIED");
            }

            if (errors == 0)
                Console.WriteLine("UNIT_OFFLINE_PASS: BREP field contracts & bounded source ray; "
                    + "real SOLIDWORKS material intersection NOT_TESTED in this harness");
            return errors;
        }
```

Mọi patch C# phát sinh tiếp theo do ChatGPT viết và commit. **Anti tuyệt đối không tự biên tập code trong hai khối này.**
