# CHECK HOLE CG2 — Antigravity: Auto Part Relations (diagnostic v1)

**REPOSITORY:** `tbcphutai-bit/ADDIN-SolidWorks`  
**SOURCE BRANCH:** `feat/check-hole-cg2-part-relations`  
**BASE:** `feat/check-hole-connection-groups-cg1` (unmerged CG1). **Do not merge main.**  
**ENVIRONMENT:** SOLIDWORKS add-in, C# 7.3, WinForms, .NET Framework 4.8, Debug|x64 + Release|x64.

## 0. SOURCE IS ALREADY COMMITTED BY CHATGPT

This document is the Antigravity hand-off. Do not re-add or paste duplicate classes. Read the **real C# branch files**, then compare with the embedded full source below. Anti is responsible for independent Visual Studio/SOLIDWORKS integration tests and limited UI corrections; ChatGPT reviews the resulting GitHub diff.

Changed source files:
1. `Commands/CheckAssemblyHole.PartRelations.cs` — new CG2 broad-phase, approximate component bounding boxes and hole-axis candidate screening.
2. `Commands/CheckAssemblyHole.cs` — one diagnostics call after `lastReport.ConnectionCoverage`, fail-closed warning on CG2 failure, preserve engine verdicts and cancel behavior.
3. `Commands/CheckHoleResultsForm.cs` — `CheckHoleReport.PartRelations`, added summary + read-only "Quan hệ Part (CG2)" dialog with two grids and warnings.
4. `ADDIN.csproj` — compile the new file.

## 1. Exact CG2 V1 scope

CG1 only groups already matched holes. CG2 V1 **also scans component occurrences with no detected holes** and provides:
- **Nearby component pairs**: axis-aligned bounding box separation <= 2 mm, *broad-phase only*.
- **Expected-hole candidates**: for a hole source on component A, a nearby component B's expanded AABB is intersected by the hole axis within 60 mm, and B has no *recognized likely coaxial* hole within 4 mm radial / 3 degrees. A candidate is `CHECK_ONLY`.
- A component occurrence can have several different candidate sites; **NEVER** choose a Part-global master.
- Coverage: eligible occurrences, unreadable/skipped, occurrences with boxes but without recognized holes, pair/hole comparison counts, capped output and `PARTIAL_UNVERIFIED` when incomplete.
- UI labels explicitly say **AABB does not prove physical contact or target material at hole, fastening intent, required counterpart, or NG**.

**This first CG2 iteration DOES NOT** calculate exact BREP contact or material intersection. Do not claim full automatic missing-hole verification. Such confirmation requires later CG2 precise-geometry rules + CG3/CG4 validation.

**Any CG2 result MUST NOT:**
- change `HoleStack`, `EvaluateStack`, any tolerance in the existing engine, Recovery, Pattern, Missing Hole, or the 3D Sketch NG creation;
- create/delete/save Part or Assembly features;
- write missing-hole NG, automatic Master, or new Check Hole CSV rows;
- require the user to manually select source or target Part;
- silently claim complete coverage when bounding boxes/read attempts fail or budget caps apply.

## 2. Code layout and algorithm

```text
ReadAssemblyHoles (existing)
  -> Merge / Recovery / Evaluate / Pattern (existing)
  -> BuildReport (existing)
  -> BuildConnectionCoverage (CG1 existing)
  -> BuildCg2PartRelations (NEW, READ ONLY)
       -> GetComponents(false) => all eligible occurrences, including no-hole
       -> Component2.GetBox(false, false) => approximate assembly-space AABBs
       -> AABB gap broad-phase => nearby component graph
       -> for each source HoleAxis / nearby component => axis vs target AABB
       -> if no likely coaxial recognized counterpart => CHECK_ONLY candidate
       -> bounded work, incomplete coverage status
  -> marker and original SetReport (existing)
  -> read-only "Quan hệ Part (CG2)" dialog
```

Diagnostic-specific thresholds are **not production NG tolerances**: AABB near gap 2 mm, target box padding 0.5 mm, max axial distance 60 mm, known-hole suppression radial 4 mm and angle 3 degrees. These numbers are heuristic screens requiring CAD calibration; no production NG decision may depend on them.

Work budget: 300,000 component-pair checks and 500,000 hole-neighbor checks, store max 1,500 pair rows and max 2,000 candidates; when limited the report must clearly communicate incomplete diagnostics.

## 3. Mandatory independent code completeness checks

- [ ] Source file exists, is included **once** in `ADDIN.csproj`, compiles C# 7.3; no generated Designer modification.
- [ ] `BuildCg2PartRelations` executes once per completed Check Hole run; `PartRelations` attached only to that report; cancellation clears the result with existing cancellation handling.
- [ ] No CG2 exception changes production OK/NG: on a CG2-specific exception only, report `ERROR_UNVERIFIED` and preserve the production result. `OperationCanceledException` propagates; the captured model-state guard runs again before markers.
- [ ] CG2 reads component occurrences (not unique Part files), includes those without recognized holes.
- [ ] Repeated/blank occurrence IDs, invalid boxes or exceptions are reported as incomplete rather than silently suggesting `ALL_CHECKED`.
- [ ] AABB-only/contact/expected-hole distinction is explicit; `CHECK_ONLY` never becomes `NG`.
- [ ] `PartRelations` dialog has tabs for candidates and nearby pairs, both grids read-only; no model writes, no fabricated Master.
- [ ] Button disabled while busy; no stale report reused for a different Assembly.
- [ ] Existing CG1 "Nhóm liên kết" and NG markers/CSV unchanged.
- [ ] No unbounded O(N x M) COM calls: one box read per eligible occurrence; pair/hole loops capped with `PARTIAL_UNVERIFIED`.

## 4. SOLIDWORKS CAD regression test matrix — each item must be PASS, FAIL, NOT_TESTED, or BLOCKED

| Test ID | Scenario | Expected diagnostic behavior |
|---|---|---|
| CG2-T01 | Two Parts, aligned holes | Pair of nearby AABBs may appear; no new NG; known counterpart should suppress CHECK_ONLY |
| CG2-T02 | A–B–C layered, B genuinely lacks hole | CG2 screens B and shows CHECK_ONLY candidate if bbox intersects source hole axis and gap/reach conditions are met; never NG |
| CG2-T03 | B solid legitimately does not require through hole (blind/thread) | Candidate may appear, but remains CHECK_ONLY, never missing-hole NG |
| CG2-T04 | Independent B–D site plus A–B–C | Separate occurrence/site candidate rows; no global Master on B |
| CG2-T05 | Concave Part whose AABB overlaps axis but no material there | False positive expected from broad-phase; explicitly CHECK_ONLY/low confidence |
| CG2-T06 | Distinct component occurrences pointing at same file path | Count occurrences separately; no unique-by-file merging |
| CG2-T07 | Suppressed/unknown component, invalid box, partial reference | Coverage `PARTIAL_UNVERIFIED`; not all checked |
| CG2-T08 | Assembly with zero recognized holes | Nearby pairs can still exist; candidate list empty; no NG |
| CG2-T09 | Large Assembly near capped work | `IsPartial=true` and `PARTIAL_UNVERIFIED`; performance recorded; not complete |
| CG2-T10 | Rerun, Cancel, change Assembly, close UI | No stale CG2 rows and production marker side effects |
| CG2-T11 | Normal / Recovery / Missing Pattern sample from CG1 | EXACT same production OK/NG/Missing/Pattern/CSV/Sketch markers |
| CG2-T12 | Deliberate CG2 bounds/API exception | `ERROR_UNVERIFIED`, production report still available, no new NG |

**VERY IMPORTANT:** source is written here but **not yet compiled by ChatGPT against SOLIDWORKS Interop or run in CAD**. If `Component2.GetBox(false,false)` has a signature/version issue, report exact diagnostic; fix only CG2 code and demonstrate the same behavior in the actual installed Interop. No claim PASS for unexecuted tests.

### Regression evidence
Compare CG1 base vs CG2 on the *same* disposable Pack-and-Go fixture/configuration with original `main` unaffected. Match per-row production `report.Rows`, all numeric counters, original CSV export and marker inventory. **Do not run the test harness against working CAD or an active user session.** If missing fixture/dependencies, mark `BLOCKED_ISOLATION`.

## 5. Full code source — `Commands/CheckAssemblyHole.PartRelations.cs`

**Already committed. DO NOT create another copy.** Compare against this section.

```csharp
// CG2 / PART RELATIONS V1 — READ-ONLY SPATIAL SCREENING.
// C# 7.3 / .NET Framework 4.8 / SOLIDWORKS.
// AABB is broad-phase ONLY: overlap or axis crossing does NOT prove solid
// contact, material removal, intended fastening, or a missing-hole NG.
// No selection, feature edits, Part edits, master decisions, or production NG.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SolidWorks.Interop.sldworks;

namespace ADDIN.Commands
{
    internal sealed class CheckHoleCg2RelationRow
    {
        public int Id { get; set; }
        public string FirstOccurrence { get; set; }
        public string SecondOccurrence { get; set; }
        public double BoxSeparationMm { get; set; }
        public string Evidence { get; set; }
        public string Confidence { get; set; }
    }

    internal sealed class CheckHoleCg2ExpectedHoleRow
    {
        public int Id { get; set; }
        public string SourceOccurrence { get; set; }
        public string TargetOccurrence { get; set; }
        public string SourceHoleLabel { get; set; }
        public double BoxSeparationMm { get; set; }
        public double AxisToBoxMm { get; set; }
        public string Classification { get; set; }
        public string Reason { get; set; }
    }

    internal sealed class CheckHoleCg2Report
    {
        public int TotalOccurrenceCount;
        public int EligibleComponentCount;
        public int ComponentsWithBounds;
        public int ComponentsWithoutDetectedHoles;
        public int SkippedOrUnreadableCount;
        public int ComparedComponentPairs;
        public int TestedHoleRelations;
        public int RecordedNearbyPairs;
        public int CandidateCount;
        public bool IsPartial;
        public string CoverageStatus;
        public readonly List<CheckHoleCg2RelationRow> NearbyPairs =
            new List<CheckHoleCg2RelationRow>();
        public readonly List<CheckHoleCg2ExpectedHoleRow> ExpectedHoleCandidates =
            new List<CheckHoleCg2ExpectedHoleRow>();
        public readonly List<string> Warnings = new List<string>();
    }

    public sealed partial class CheckAssemblyHole
    {
        // These CG2 diagnostic bounds are NOT position or angle NG tolerances.
        private const double Cg2NearBoxGapM = 0.002;    // broad-phase AABB gap: 2 mm
        private const double Cg2AxisBoxPadM = 0.0005;   // 0.5 mm for broad phase
        private const double Cg2MaxAxisReachM = 0.060;  // 60 mm around source hole
        private const double Cg2KnownHoleRadialM = 0.004; // suppress likely known counterpart
        private const double Cg2KnownHoleAngleDeg = 3.0;
        private const int Cg2MaxPairChecks = 300000;
        private const int Cg2MaxHoleChecks = 500000;
        private const int Cg2MaxStoredPairs = 1500;
        private const int Cg2MaxStoredCandidates = 2000;

        private sealed class Cg2Box
        {
            public string Occurrence;
            public double[] Min;
            public double[] Max;
        }

        /// <summary>
        /// Scans ALL eligible component OCCURRENCES including those with zero
        /// detected holes. Returns low-confidence AABB candidates for CG3/CG4
        /// investigation. Nothing here creates or changes production NG.
        /// </summary>
        private CheckHoleCg2Report BuildCg2PartRelations(
            AssemblyDoc assembly, List<HoleAxis> holes)
        {
            if (assembly == null) throw new ArgumentNullException("assembly");
            if (holes == null) throw new ArgumentNullException("holes");

            CheckHoleCg2Report report = new CheckHoleCg2Report();
            object[] components = ToObjectArray(assembly.GetComponents(false));
            report.TotalOccurrenceCount = components.Length;
            var boxes = new List<Cg2Box>();
            var indexed = new Dictionary<string, Cg2Box>(StringComparer.OrdinalIgnoreCase);
            var byOccurrence = holes.Where(h => h != null)
                .GroupBy(h => h.ComponentOccurrence ?? "", StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < components.Length; i++)
            {
                if ((i & 31) == 0) ThrowIfCanceled();
                Component2 component = components[i] as Component2;
                if (!IsUsableComponent(component))
                {
                    report.SkippedOrUnreadableCount++;
                    continue;
                }
                report.EligibleComponentCount++;
                string occurrence = SafeComponentName(component);
                if (string.IsNullOrWhiteSpace(occurrence) || indexed.ContainsKey(occurrence))
                {
                    report.SkippedOrUnreadableCount++;
                    Cg2Warn(report, "Empty or repeated component occurrence identity; skipped.");
                    continue;
                }

                try
                {
                    // Component2.GetBox returns *approximate assembly-space* AABB.
                    // Do not use it as proof of actual material intersection.
                    double[] raw = ToDoubleArray(component.GetBox(false, false));
                    Cg2Box b = Cg2ReadBox(occurrence, raw);
                    if (b == null)
                    {
                        report.SkippedOrUnreadableCount++;
                        Cg2Warn(report, "Invalid bounding box: " + occurrence);
                        continue;
                    }
                    boxes.Add(b);
                    indexed.Add(occurrence, b);
                    if (!byOccurrence.ContainsKey(occurrence))
                        report.ComponentsWithoutDetectedHoles++;
                }
                catch (Exception ex)
                {
                    report.SkippedOrUnreadableCount++;
                    Cg2Warn(report, "Cannot read bounds for " + occurrence + ": " + ex.GetType().Name);
                }
            }

            boxes.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Occurrence, b.Occurrence));
            var adjacent = new Dictionary<string, List<Tuple<Cg2Box, double>>>(
                StringComparer.OrdinalIgnoreCase);
            bool pairBudgetExhausted = false;
            for (int i = 0; i < boxes.Count && !pairBudgetExhausted; i++)
            {
                if ((i & 31) == 0) ThrowIfCanceled();
                for (int j = i + 1; j < boxes.Count; j++)
                {
                    if (report.ComparedComponentPairs >= Cg2MaxPairChecks)
                    {
                        report.IsPartial = true;
                        pairBudgetExhausted = true;
                        break;
                    }
                    report.ComparedComponentPairs++;
                    double gap = Cg2BoxGap(boxes[i], boxes[j]);
                    if (gap > Cg2NearBoxGapM) continue;
                    report.RecordedNearbyPairs++;
                    Cg2AddNeighbor(adjacent, boxes[i], boxes[j], gap);
                    Cg2AddNeighbor(adjacent, boxes[j], boxes[i], gap);
                    if (report.NearbyPairs.Count < Cg2MaxStoredPairs)
                    {
                        report.NearbyPairs.Add(new CheckHoleCg2RelationRow
                        {
                            Id = report.NearbyPairs.Count + 1,
                            FirstOccurrence = boxes[i].Occurrence,
                            SecondOccurrence = boxes[j].Occurrence,
                            BoxSeparationMm = gap * 1000.0,
                            Evidence = "AABB_NEAR_ONLY",
                            Confidence = "CANDIDATE_REVIEW"
                        });
                    }
                    else report.IsPartial = true;
                }
            }

            int sourceHoleId = 0;
            bool holeBudgetExhausted = false;
            foreach (HoleAxis hole in holes)
            {
                if ((sourceHoleId++ & 63) == 0) ThrowIfCanceled();
                if (!Cg2ValidVector(hole == null ? null : hole.Center) ||
                    !Cg2ValidVector(hole.Direction)) continue;

                string source = hole.ComponentOccurrence ?? "";
                List<Tuple<Cg2Box, double>> neighbors;
                if (!adjacent.TryGetValue(source, out neighbors)) continue;

                foreach (Tuple<Cg2Box, double> n in neighbors)
                {
                    if (report.TestedHoleRelations >= Cg2MaxHoleChecks)
                    {
                        report.IsPartial = true;
                        holeBudgetExhausted = true;
                        break;
                    }
                    report.TestedHoleRelations++;

                    double distanceOnAxis;
                    if (!Cg2AxisCrossesBoxNear(hole.Center, hole.Direction,
                        n.Item1, out distanceOnAxis)) continue;

                    List<HoleAxis> targetHoles;
                    if (byOccurrence.TryGetValue(n.Item1.Occurrence, out targetHoles)
                        && targetHoles.Any(h => Cg2LikelyKnownCounterpart(hole, h)))
                        continue;

                    report.CandidateCount++;
                    if (report.ExpectedHoleCandidates.Count < Cg2MaxStoredCandidates)
                    {
                        report.ExpectedHoleCandidates.Add(new CheckHoleCg2ExpectedHoleRow
                        {
                            Id = report.ExpectedHoleCandidates.Count + 1,
                            SourceOccurrence = source,
                            TargetOccurrence = n.Item1.Occurrence,
                            SourceHoleLabel = "Hole#" + sourceHoleId.ToString(CultureInfo.InvariantCulture),
                            BoxSeparationMm = n.Item2 * 1000.0,
                            AxisToBoxMm = distanceOnAxis * 1000.0,
                            Classification = "CHECK_ONLY",
                            Reason = "Axis crosses approximate component bounding box; " +
                                "actual material/contact/fastening intent NOT verified."
                        });
                    }
                    else report.IsPartial = true;
                }
                if (holeBudgetExhausted) break;
            }

            report.ComponentsWithBounds = boxes.Count;
            if (report.SkippedOrUnreadableCount > 0 ||
                report.IsPartial || boxes.Count == 0)
                report.CoverageStatus = "PARTIAL_UNVERIFIED";
            else
                report.CoverageStatus = "BBOX_SCREENED_NOT_MATERIAL_VERIFIED";

            // Explicitly communicate if all boxes are screened but there is
            // still NO proof of contact, mate, solid at axis, or missing hole.
            return report;
        }

        private static void Cg2Warn(CheckHoleCg2Report report, string warning)
        {
            if (report.Warnings.Count < 20)
                report.Warnings.Add(warning);
        }

        private static Cg2Box Cg2ReadBox(string occurrence, double[] raw)
        {
            if (raw == null || raw.Length < 6) return null;
            var min = new double[3];
            var max = new double[3];
            for (int k = 0; k < 3; k++)
            {
                if (double.IsNaN(raw[k]) || double.IsInfinity(raw[k]) ||
                    double.IsNaN(raw[k + 3]) || double.IsInfinity(raw[k + 3]))
                    return null;
                min[k] = Math.Min(raw[k], raw[k + 3]);
                max[k] = Math.Max(raw[k], raw[k + 3]);
            }
            return new Cg2Box { Occurrence = occurrence, Min = min, Max = max };
        }

        private static double Cg2BoxGap(Cg2Box a, Cg2Box b)
        {
            double squared = 0;
            for (int k = 0; k < 3; k++)
            {
                double sep = Math.Max(0, Math.Max(a.Min[k] - b.Max[k], b.Min[k] - a.Max[k]));
                squared += sep * sep;
            }
            return Math.Sqrt(squared);
        }

        private static void Cg2AddNeighbor(
            Dictionary<string, List<Tuple<Cg2Box, double>>> neighbors,
            Cg2Box from, Cg2Box to, double gap)
        {
            List<Tuple<Cg2Box, double>> list;
            if (!neighbors.TryGetValue(from.Occurrence, out list))
            {
                list = new List<Tuple<Cg2Box, double>>();
                neighbors.Add(from.Occurrence, list);
            }
            list.Add(Tuple.Create(to, gap));
        }

        private static bool Cg2ValidVector(double[] v)
        {
            return v != null && v.Length >= 3 &&
                !v.Take(3).Any(d => double.IsNaN(d) || double.IsInfinity(d));
        }

        private static bool Cg2AxisCrossesBoxNear(
            double[] center, double[] direction, Cg2Box target, out double minAbsAxialDistance)
        {
            minAbsAxialDistance = double.PositiveInfinity;
            double lengthSq = direction[0]*direction[0] +
                direction[1]*direction[1] + direction[2]*direction[2];
            if (lengthSq < 1e-16) return false;
            double invLen = 1.0 / Math.Sqrt(lengthSq);
            double tmin = -double.MaxValue, tmax = double.MaxValue;
            for (int k = 0; k < 3; k++)
            {
                double d = direction[k] * invLen;
                double lo = target.Min[k] - Cg2AxisBoxPadM;
                double hi = target.Max[k] + Cg2AxisBoxPadM;
                if (Math.Abs(d) < 1e-12)
                {
                    if (center[k] < lo || center[k] > hi) return false;
                    continue;
                }
                double a = (lo - center[k]) / d;
                double b = (hi - center[k]) / d;
                if (a > b) { double swap = a; a = b; b = swap; }
                tmin = Math.Max(tmin, a);
                tmax = Math.Min(tmax, b);
                if (tmin > tmax) return false;
            }
            minAbsAxialDistance = tmin <= 0 && tmax >= 0
                ? 0 : Math.Min(Math.Abs(tmin), Math.Abs(tmax));
            return minAbsAxialDistance <= Cg2MaxAxisReachM;
        }

        private static bool Cg2LikelyKnownCounterpart(HoleAxis a, HoleAxis b)
        {
            if (b == null || !Cg2ValidVector(b.Center) ||
                !Cg2ValidVector(b.Direction)) return false;
            double alenSq = a.Direction.Take(3).Sum(d => d*d);
            double blenSq = b.Direction.Take(3).Sum(d => d*d);
            if (alenSq < 1e-16 || blenSq < 1e-16) return false;
            double dot = 0, projection = 0, distanceSq = 0;
            for (int k = 0; k < 3; k++)
            {
                dot += a.Direction[k] * b.Direction[k];
                double delta = b.Center[k] - a.Center[k];
                projection += delta * a.Direction[k];
                distanceSq += delta*delta;
            }
            double cosine = Math.Min(1.0, Math.Abs(dot) / Math.Sqrt(alenSq * blenSq));
            if (Math.Acos(cosine) * 180.0 / Math.PI > Cg2KnownHoleAngleDeg)
                return false;
            double radialSq = Math.Max(0, distanceSq - projection*projection/alenSq);
            return radialSq <= Cg2KnownHoleRadialM * Cg2KnownHoleRadialM;
        }
    }
}
```

## 6. Exact integration after CG1 in `Commands/CheckAssemblyHole.cs`

```csharp
                // CG2: broad-phase component screening only. Never changes production NG.
                // AABB overlap is not proof of solid contact or a missing hole.
                try
                {
                    lastReport.PartRelations = BuildCg2PartRelations(assembly, holes);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception cg2Error)
                {
                    Debug.WriteLine("[CHECK HOLE CG2] diagnostics unavailable: " + cg2Error);
                    lastReport.PartRelations = new CheckHoleCg2Report
                    {
                        IsPartial = true,
                        CoverageStatus = "ERROR_UNVERIFIED"
                    };
                    lastReport.PartRelations.Warnings.Add(cg2Error.GetType().Name);
                }
                ThrowIfCanceled();
                if (!string.Equals(inputState, CaptureModelState(model), StringComparison.Ordinal))
                    throw new InvalidOperationException("Model đã thay đổi trong khi CG2 quét; hãy chạy lại.");
```

## 7. Exact WinForms method `ShowPartRelations()`

```csharp
        // CG2: diagnostic-only. AABB screening is NOT physical contact or
        // confirmation that a counterpart hole is required. No model edits.
        private void ShowPartRelations()
        {
            if (busy || report == null || report.PartRelations == null) return;
            CheckHoleCg2Report c = report.PartRelations;
            using (var dialog = new Form())
            {
                dialog.Text = "CHECK HOLE - Quan hệ Part / CG2 (chẩn đoán)";
                dialog.Size = new Size(1150, 630);
                dialog.MinimumSize = new Size(850, 460);
                dialog.StartPosition = FormStartPosition.CenterScreen;
                dialog.Font = new Font("Segoe UI", 9F);

                var header = new Label
                {
                    Dock = DockStyle.Top,
                    Height = 94,
                    Padding = new Padding(8),
                    Text = "Component có bounding box: " + c.ComponentsWithBounds
                        + " | Part chưa nhận diện lỗ: " + c.ComponentsWithoutDetectedHoles
                        + " | Cặp AABB gần: " + c.RecordedNearbyPairs
                        + " | Ứng viên xem lại: " + c.CandidateCount
                        + Environment.NewLine
                        + "Độ phủ: " + c.CoverageStatus
                        + " | Đã thử cặp Part: " + c.ComparedComponentPairs
                        + " | Đã thử trục lỗ: " + c.TestedHoleRelations
                        + Environment.NewLine
                        + "CHỈ LÀ AABB/BROAD-PHASE: không chứng minh tiếp xúc vật liệu, "
                        + "không chứng minh thiếu lỗ, không xác định Master, không tạo NG."
                };
                var tabs = new TabControl { Dock = DockStyle.Fill };

                var candidatePage = new TabPage("Ứng viên cần kiểm tra");
                candidatePage.Controls.Add(new DataGridView
                {
                    Dock = DockStyle.Fill,
                    ReadOnly = true,
                    AutoGenerateColumns = true,
                    AllowUserToAddRows = false,
                    AllowUserToDeleteRows = false,
                    RowHeadersVisible = false,
                    AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                    SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                    DataSource = c.ExpectedHoleCandidates
                });
                tabs.TabPages.Add(candidatePage);

                var relationPage = new TabPage("Cặp Component gần nhau");
                relationPage.Controls.Add(new DataGridView
                {
                    Dock = DockStyle.Fill,
                    ReadOnly = true,
                    AutoGenerateColumns = true,
                    AllowUserToAddRows = false,
                    AllowUserToDeleteRows = false,
                    RowHeadersVisible = false,
                    AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                    SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                    DataSource = c.NearbyPairs
                });
                tabs.TabPages.Add(relationPage);

                if (c.Warnings.Count > 0)
                {
                    var warningPage = new TabPage("Giới hạn dữ liệu");
                    warningPage.Controls.Add(new TextBox
                    {
                        Multiline = true,
                        ReadOnly = true,
                        Dock = DockStyle.Fill,
                        ScrollBars = ScrollBars.Vertical,
                        Text = string.Join(Environment.NewLine, c.Warnings)
                    });
                    tabs.TabPages.Add(warningPage);
                }
                dialog.Controls.Add(tabs);
                dialog.Controls.Add(header);
                dialog.ShowDialog(this);
            }
        }
```

The remaining WinForms insertions are already in the branch: report field, summary line, button field, toolbar subscription, enabled state. Anti must verify each is present **exactly once** in `Commands/CheckHoleResultsForm.cs`.

## 8. Work to perform now (Antigravity)

1. Checkout `feat/check-hole-cg2-part-relations`. Protect local dirty work. Read this document and the four code files.
2. Review diff against CG1 base: `git diff origin/feat/check-hole-connection-groups-cg1...HEAD`. Do not compare against `main` without noting CG1 dependency.
3. Build full solution Debug|x64 and Release|x64. Fix **only new CG2 integration errors**; do not change engine verdict logic.
4. Test CG2-T01–T12 with **isolated** Pack-and-Go copies, confirming counts and UI; no real CAD or markers modified.
5. Write `docs/antigravity/cg2/CG2_REPORT.md` with true statuses, build logs, test evidence and code-completeness audit. Include any sample names, SHA of code tested, timings and regression table. Do not fabricate pass.
6. Commit + push to this CG2 branch only, leave draft PR unmerged, report code/report SHA and PR link. **Wait for ChatGPT to read the code itself.**

## 9. Future CG2 precise-geometry follow-up (NOT part of this code review)

V1 screens AABBs only; exact solid material tests, real face-to-face contact, local thickness/void, threading/blind-hole intent and confidence-ranking must be developed separately with SOLIDWORKS fixture validation. Do not silently promote `CHECK_ONLY` from the current stage to production NG.

## 10. Required delivery response

```text
CG2 BRANCH: feat/check-hole-cg2-part-relations
SOURCE COMMIT SHA:
REPORT COMMIT SHA:
DEBUG|x64: PASS / FAIL / BLOCKED_ENVIRONMENT
RELEASE|x64: PASS / FAIL / BLOCKED_ENVIRONMENT
CG2-T01..T12: one verdict and evidence for every test
CG1 vs CG2 production verdict/counters/marker/CSV differences:
AABB candidate false-positive/false-negative notes:
Incomplete/unreadable components or capped work:
CODE_COMPLETE: YES / NO / NOT_VERIFIED
READY_FOR_CHATGPT_REVIEW: YES / NO
READY_FOR_MERGE: NO
PR URL (Draft only):
```
