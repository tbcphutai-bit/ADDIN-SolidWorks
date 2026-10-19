# Native Excel 4200 / 6000 validation

Date: 2026-10-20. Baseline PR #34: `5356ab453ee7148ccf31b20191868ff4ac64f20a`.
Branch: `feature/check-chan-native-excel-4200-6000`.

## Source and case definition

The user explicitly confirmed the existing `深曲げ限界.xlsx` and authorized
preparing **33 new cases from it**. The generated matrix/CSV are a new,
source-bound test definition, not a recovered previously authored CSV.
Original workbook SHA256:
`afbb2e6245031604ddbb83738a7eae1ec9c1948dcd1128d61ae25d7213f962df`.

The workbook's `金型限界表` provides orange W/L frames 30×60, 45×75 and
50×100, embedded images 1/2, reference lengths at B5/B35 and material headers
B9:B11/B39:B41. All source sheets were inspected; the 4 m special tools are
outside this 4.2 m/6 m test definition. No ToolId or machine certification is
inferred from the overview groups.

| Case definition | Count | Meaning |
| --- | --- | --- |
| U | 18 | Symmetric U variants of source W/L envelopes; not a trace of every unequal flange shown in the overview |
| Z | 9 | Explicit controlled Z extensions using the source W/L dimensions; no claim that the Excel depicts/certifies these Z cases |
| Three bends | 6 | Explicit controlled extensions using the sourced dimensions; no inferred production sequence |
| ST3.2 / SUS3.0 / AL3.0 | 11 each | Native materials assigned from the installed company material library |
| Below / at / above axial reference | 11 each | 4190/4200/4210 and 5990/6000/6010 mm; independent of section W/L |

R2 is an explicit fixture parameter, not a tool radius or an Excel-derived
process certificate. Every case records its source and interpretation. The
matrix contains typed numeric inputs and the original source illustrations
for local review. The Excel/CSV/sidecars/generated CAD remain private.

## Actual execution

Windows, SOLIDWORKS 2024 SP5, .NET Framework 4.8, MSBuild 18.10.1.42706, x64.
No Computer Use. The native generator/capture run on STA in owned SOLIDWORKS
PIDs. Three explicit 11-case partitions use one immutable 33-case source;
merge rejects duplicate/foreign cases and incompatible source revisions.

| Check | Result |
| --- | --- |
| Product Debug/x64 and Release/x64 | PASS, zero build errors |
| Native harness Debug/x64 and Release/x64 | PASS build |
| New input/semantic comparison, each configuration | PASS: 13 tests / 46 assertions |
| Full managed regression, each configuration | PASS: 302 tests / 9,112 assertions |
| Existing WinForms UI/render, each configuration | PASS: 16 groups / 150 assertions |
| Bend Planning regression | PASS: 24 tests / 609 assertions |
| Actual company CAD catalog | PASS: 18/18 fixed pairs / 16,977 assertions |
| Native 33-case Release | Functional PASS: 33 PASS / 0 FAIL / 0 NOT_RUN, 4,077 assertions |
| Native 33-case Debug | Functional PASS: 33 PASS / 0 FAIL / 0 NOT_RUN, 4,077 assertions |
| Original inputs/template/materials/Min workbook | SHA256 unchanged in completed runs |
| Company Master DXF | SHA256 unchanged: `e3f34e62d088e74aedde1c54694428c1faca0504aa6176ea89ebf777bda4b016` |
| Native server shutdown | NOT_CONFIRMED in completed partitions; executable exit 1 and overall native lifecycle acceptance FAIL |
| New physical process orders / common production tool | UNKNOWN, reviewed setup/process evidence missing |
| Native Assembly 120/25 / independent numeric oracles | NOT RUN again in this test-only change; inherited results are not relabeled as new tests |

Each configuration covers **33 real saved SLDPRT files, 72 physical native
bends and 1,296 static plus 1,296 process candidates**. Every identified bend
has 18 distinct tools and independent Punch/Holder results. FeatureIds and
PersistentIds are read from actual cylindrical-face owners and resolve in
SOLIDWORKS. Their equality with captured UI bend identities is asserted.

The independent native audit measures exact B-rep extrema, finite bend-face
axial lengths, thickness, angle and inside radius. Outside W/L and axial
length are checked at 0.001 mm absolute tolerance. No source Part is changed
and no wrong dimension is silently rescaled or retried until it passes.

## Check Min and manufacturing status

Debug/Release: **69 bends meet Check Min; 3 bends are BelowMinimum**. The NG cases
are XL-19/20/21, native FeatureId 53: ST3.2, end L/A = 15 mm versus A min
15.2 mm. Their other Z/B dimension is 60 mm versus B min 21.2 mm. These are
correctly recorded test outcomes, so the software execution remains PASS.
Check Min status, measurements and limits match the unchanged evaluator on
both cold/warm runs. No Check Min math is edited.

All 1,296 static candidates retain independent Punch and Holder UNKNOWN,
with observed gate `TOOL_AXIAL_EXTENT_UNVERIFIED`. All process candidates are
UNKNOWN with `NO_STATE_INFERENCE_FROM_FINAL_MODEL`, `UNKNOWN_MACHINE_MOTION`
and `UNKNOWN_PROCESS_GEOMETRY`. Phase 4B retains 18 NeedsEvidence candidates
per Part with `PROCESS_PROVIDER_NOT_CONFIGURED`, empty common-tool sets and
no production winner. No alternative order is invented from final geometry.

All 11 above-reference cases are explicitly flagged, without an automatic
COLLISION. 4200/6000 mm are source references, not certified ToolId working
lengths. The catalog has 17 valid closed Punch profiles and one unchanged
open KIK Punch; all 18 Holder selections, including SPLINE/reference entities,
are retained. CAD association does not approve material roles, seating, datum,
axial working length or actual process motion. No V-die is evaluated.

## Timing and cache

Completed Release run: cold run-to-visible **3.308–5.358 s**, mean **4.079 s**;
warm **0.842–1.232 s**, mean **1.025 s**. Cold native captures: 33. Warm captures:
0, with 33 actual capture hits. Cold and warm facts/reasons/witnesses/error
values are compared; JSON property order is ignored without rounding numeric
facts. First case of each partition also proves Rebuild/stale withdrawal/
Refresh and identity retention. Worker-thread native capture count is zero.

Numeric queries are **0** because axial/process proof is missing. These times
measure loading/capture/evidence gates/display, not real-tool collision solving.
The reports retain per-case generation times and cold/warm stage metrics.

Debug cold run-to-visible: **3.527–6.383 s**, mean **4.517 s**; warm:
**0.951–1.354 s**, mean **1.173 s**. All 33 native geometry records and complete
result graphs match Release exactly, including Min values, native IDs,
statuses, reasons, witnesses and error values. Each configuration has 33 cold
captures and 33 warm capture hits, 0 warm captures and 0 worker COM captures.

## Stability and diagnostics retained

- Initial generator diagnostics caught sketch-versus-physical thickness offsets.
  The fixture interpretations were corrected explicitly and native measured.
  No company CAD or requested source dimensions were changed to pass a test.
- Early cold/warm checks falsely failed on reflected JSON property order.
  Saved cold/warm captures proved equal parsed values. Regression rejects
  changed statuses, witnesses, errors and array ordering; no engine tolerance
  or intermediate collision requirement is weakened.
- An earlier preparatory multi-Part diagnostic lost RPC after a stale result
  on a three-bend case. The failed run is retained privately. Later functional
  success is not a root-cause fix for that observation or for native shutdown.
- New-fixture notifications settle before the live revision is observed. A
  completed stale result is still withdrawn, not automatically retried as PASS.
- The inherited clean-shutdown limitation remains: functional case PASS is
  separate from native lifecycle FAIL; the ten-second audit is not increased.

## Delivery and remaining evidence

[Runner/input contract](NativeExcelMatrix.md).
`NativeExcelMatrixResults/Company33/` contains all 33 actual case rows and logs
per configuration. Private tables retain absolute fixture paths and full
per-bend/per-tool details. Public tables redact paths and omit raw source
coordinates. Original CAD/Excel, generated SLDPRT, coordinate-bearing evidence,
source selection manifests and crash dumps are not committed.

The test infrastructure and software observations are implemented. Physical
Punch/Holder motion, alternative order feasibility and one common production
tool still require verified axial lengths, material roles, datums/seating,
registered intermediate sheet states and reviewed machine/process graphs.
Native clean shutdown also remains unresolved. No manufacturing PASS is given.
