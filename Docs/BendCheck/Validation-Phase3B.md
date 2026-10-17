# Check chấn Phase 3B — Windows validation, 2026-10-17

Issue [#8](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/issues/8).
Baseline PR #7 / `7f2f49a6cbea1ce559d2c0ee94bd0176ab0b04d7` was reviewed,
including its code and reported Phase 3A results; there were no PR review
comments at the time. Work is on `feature/check-chan-phase3b-placement`, targeting
`feature/check-chan-phase3a-geometry`. No main/dependency branch or old PR changed.

## Build

Debug/x64 and Release/x64: **PASS, 0 errors, 16 existing warnings per build**
(CS0219/CS0649, eight existing sites repeated by WPF's temporary compilation).
No new warning. C# 7.3 / .NET Framework 4.8; explicit Compile Include maintained.
Builds went to private output/obj directories; installed DLL/PDB were untouched.

The local MSBuild is Visual Studio 18 Community, amd64. Safe build invocation
(repeat with Debug and Release, and separate output/obj directories):

```powershell
& $MSBuild ADDIN.csproj /t:Rebuild /p:Configuration=Debug /p:Platform=x64 `
  /p:UseSharedCompilation=false /p:KillSolidWorksOnBuild=false `
  /p:PreBuildEvent= /p:PostBuildEvent= /p:RunPostBuildEvent=Never `
  /p:OutputPath=$PrivateDebugOutput /p:BaseIntermediateOutputPath=$PrivateDebugObj
```

## Managed and source validation

| Test set | Result |
| --- | --- |
| Pure C# DXF/region/datum/placement | **133 tests / 1,166 assertions PASS** |
| Existing Bend Planning | **24 tests / 609 assertions PASS** |
| 18 DIM + two aggregate DXFs, Phase 2/3A regression | **38 assertions PASS** |
| Actual Master/workbook → 18-slot placement pipeline | **9 assertions PASS** |
| Independent ezdxf 1.4.5 comparison | **6,975 curves PASS**, max error 4.55e-13 mm |

The 37 added tests cover reverse-sense native edge parameter/status handling,
metres/mm, 3D/2D frame round trips, transform chains and
inverses, tip distinct from mount, rotation/mirror/R/sweep, invalid scale/shear/
directions; exact tool identity, 004 versus 0048, missing/duplicate/array roots,
overlapping roles, nested/reflected blocks, annotation exclusion, open/SPLINE/
multiple holder loops; complete L/U/Z/step material sections, both skins/bend
arcs/back walls, unsupported box/hole/tab/trim/incomplete bodies; 18 candidates
for every bend, explicit registration, manual/synthetic SIMULATION_ONLY, source/
config/manifest/model/datum stale state and private diagnostic exclusions.

Workbook tests distinguish length from clearance examples and source facts from
tool applicability; shared/inline/phonetic text, cell/drawing/media provenance,
explicit units, zero, workbook/object SHA, cached formulas, image transcription,
boolean/error/date cell types, missing locators and external relationships.
Cancellation is propagated. Fixtures are synthetic software test data, without
company source geometry. The company integration uses the actual 18 tools with
an explicitly synthetic sheet harness; actual Part geometry is tested natively.

## Local source status

The user confirmed two local source directories. Their available saved Master
and workbook basenames have **no `(2)`/`(3)` suffix**. The tests pin actual saved
file SHA rather than claiming an unverified filename revision. The separate
revision names in Issue #8 have not been established as byte-identical. Private
selection manifests, hashes and inventories remain outside the checkout.

Results in both the DIM sources and the selected company Master punches:
**18 read, 17 closed / valid single regions, KIK open**. No source edge or
polyline flag was repaired. Company outlines were selected rather than replacing
them with different manufacturer variants shown beside them in the Master.

The explicit private source mapping found 14 candidate holder references: nine
indexed manufacturer holders, two uniquely indexed references, the two prior
explicit AMTS III instance mappings, and the named KIK reference. Four repeated
holder instances do not have sufficient unique association to their tool IDs;
they were not assigned by position or INSERT order. Identity labels match for
all 18 punches; complete selected-role mappings exist for 14, not 18 verified
physical assemblies.

Of the 14 selected references, **12 contain SPLINE**, and the two AMTS III
references contain multiple loops. Thus **0 holders have a supported single
obstacle region**, **0 physical holders/clamps are verified**, **0 verified
datum sets**, and **0 READY_FOR_COLLISION_CHECK**. LINE/ARC portions and
unsupported metadata remain available for inspection; no fake region is made.

| Tool ID | Punch | Holder reference | Datum / physical clamp | Result |
| --- | --- | --- | --- | --- |
| AMD-STD-003 | Closed / valid | SPLINE unsupported | Missing / unverified | CHUA_XAC_MINH |
| AMD-STD-004 | Closed / valid | Mapping unresolved | Missing / unverified | CHUA_XAC_MINH |
| AMD-STD-0048 | Closed / valid | SPLINE unsupported | Missing / unverified | CHUA_XAC_MINH |
| AMD-STD-0049 | Closed / valid | SPLINE unsupported | Missing / unverified | CHUA_XAC_MINH |
| AMD-STD-008 | Closed / valid | Mapping unresolved | Missing / unverified | CHUA_XAC_MINH |
| AMD-STD-045 | Closed / valid | SPLINE unsupported | Missing / unverified | CHUA_XAC_MINH |
| AMD-STD-047 | Closed / valid | SPLINE unsupported | Missing / unverified | CHUA_XAC_MINH |
| AMD-STD-103 | Closed / valid | SPLINE unsupported | Missing / unverified | CHUA_XAC_MINH |
| AMD-STD-109 | Closed / valid | SPLINE unsupported | Missing / unverified | CHUA_XAC_MINH |
| AMD-STD-117 | Closed / valid | Mapping unresolved | Missing / unverified | CHUA_XAC_MINH |
| AMD-STD-200 | Closed / valid | SPLINE unsupported | Missing / unverified | CHUA_XAC_MINH |
| AMD-STD-202 | Closed / valid | SPLINE unsupported | Missing / unverified | CHUA_XAC_MINH |
| AMD-STD-210 | Closed / valid | SPLINE unsupported | Missing / unverified | CHUA_XAC_MINH |
| AMD-STD-452 | Closed / valid | Mapping unresolved | Missing / unverified | CHUA_XAC_MINH |
| AMD-STD-453 | Closed / valid | SPLINE unsupported | Missing / unverified | CHUA_XAC_MINH |
| AMD-202662 | Closed / valid | Multiple loops unsupported | Missing / unverified | CHUA_XAC_MINH |
| AMD-204662 | Closed / valid | Multiple loops unsupported | Missing / unverified | CHUA_XAC_MINH |
| KIK-7181328001 | Open / no region | SPLINE unsupported | Missing / unverified | CHUA_XAC_MINH |

Workbook: all four authoritative sheets were read; **87 source objects** with
stable cell/drawing/media evidence, **25 explicitly mapped reference records**.
Family/machine source facts and deep examples remain distinct. Mapping from
these family panels to the individual 18 tool IDs is not verified, so all 18
retain UNMAPPED_LIMIT_RULE. No length or image example becomes a default limit.
Existing Min workbook/data was neither edited nor replaced.

## Native regression and coverage

SOLIDWORKS 2024, owned API processes, STA, production Check chấn form/session,
private Part copies. No Computer Use; no attaching to a user session. Harness
rebuild/configuration/dimension edits apply only to test copies.

- Part with six native bends: **744 assertions PASS in Debug and Release**.
  **6 sections / 108 candidates** are generated; all remain CHUA_XAC_MINH, no
  winner. The real part has trim/holes/axial variation; all six sections are
  explicitly UNSUPPORTED_2D_ENVELOPE, with no partial silhouette treated as full.
- Its initial native bend/Min/dimension/limits/Planning/UI-column snapshot is
  **exactly equal to saved Phase 3A**. Model source SHA is unchanged.
- A second actual uniform Part: **355 assertions PASS (Release)**, **3 complete
  sections / 54 candidates**. Every section retains 16 actual boundary curves,
  including both skins and analytic inner/outer arcs. Manufacturing readiness
  remains 0 because physical tool/process evidence is missing.
- This positive native case exposed an adapter defect during development:
  reverse-sense edge parameters evaluated on the underlying ICurve chose a
  complementary arc. The adapter now evaluates on IEdge and checks the packed
  success status. The pure parameter/status regression and the native complete
  region assertion prevent accepting that case as merely unsupported. No Min
  endpoint/kind or measurement rule was changed.
- Placement-config failure, missing CAD, malformed manifest/evidence and recovery
  preserve Min. Held source/draft objects invalidate; stamp/config/dirty state
  stays unchanged on source-only updates. GeometryReadCount stays **1** and the
  appended ArcMetadataReadCount does **not increase** on library/evidence edits.
- Rebuild hides old placement and uses the existing rescan; switching a private
  copy's configuration also hides its old placement. Debug diagnostic exception
  injection is isolated; Release correctly omits Debug.WriteLine.
- Assembly regression: **functional PASS, 1,531 assertions attempted**, with
  the **one pre-existing layout stress warning** (1,000 px mock row: measured
  57 px versus row 53 px). It is not reported as a clean layout PASS and no UI
  repair was made outside scope. Grouping, moved/rotated duplicate component,
  referenced alternate configuration, isolated working copy, Min OK/NG, marker/
  selection behavior, details and rebuild update remain correct. Three unique
  Part/configuration records retain **90 / 54 / 54 candidates** for **5 / 3 / 3
  bends**. Source refresh does not increase COM arc reads. Original source Part
  hashes remain unchanged.

The inherited local Assembly fixture/harness and raw reports contain private
model references and remain outside Git. The generic native Part/source harness
and pure placement tests are included in the repository.

## Changed files and remaining work

Production additions (all under `Commands/BendPlacement/`):

- `BendPlacementMath.cs`, `BendPlacementModels.cs`
- `BendMasterAssemblyReader.cs`, `BendLimitWorkbookReader.cs`, `BendPlacementSources.cs`
- `BendSheetSection.cs`, `BendPlacementNativeCapture.cs`, `BendPlacementBuilder.cs`

Integration: `ADDIN.csproj`, `Commands/BendCheckSession.cs`,
`Commands/BendPanelGeometry.cs` (optional arc append only),
`Commands/BendToolProfiles/ToolProfileLibrary.cs`, `ToolProfileModels.cs`.
Tests: `Tests/BendToolProfiles/PlacementTests.cs`, `PlacementCompanyIntegration.cs`,
`ToolProfiles.Tests.csproj`, `ToolProfileTests.cs`, `CompanyToolIntegration.cs`
(read sharing fix for open source files), `NativeToolLibraryValidation.cs`, README.
Documents: this report and `Phase3B-PlacementContract.md`.

No WinForms/Designer/events, Check Min evaluation/measurement rules, Excel/Data,
planning permutations or other commands were changed. No collision engine,
V-die geometry/check, Check Max PASS/NG, feasible sequence or tool winner.
Unresolved work is explicit: four holder associations, SPLINE/multi-loop holder
policy, physical mounting/pair and tip/mount/approach/contact evidence, verified
family applicability, and 3D/conservative envelopes for nonuniform parts. These
are source/algorithm limitations, not tests bypassed with invented geometry.

No company CAD/Excel/Part, design coordinates, actual private selection keys,
raw geometry or internal source manifests are included in the public change.
