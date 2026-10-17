# Check chấn Phase 3B.1 — Windows validation, 2026-10-17

Issue [#10](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/issues/10).
Reviewed the full issue and actual PR #9 code/results at baseline
`8e98774d4e39a8aaf3e9e37abc1ce058c9361585`. Branch:
`feature/check-chan-phase3b1-tool-holder`; Draft PR base:
`feature/check-chan-phase3b-placement`. No older PR or main was modified/merged.

Implementation details and evidence schema are in the
[holder contract](Phase3B1-HolderContract.md). This report distinguishes successful
software geometry tests from actual company material/setup approval.

## Build and tests

| Test | Result |
| --- | --- |
| Product Debug/x64, .NET Framework 4.8 / C# 7.3 | PASS, 0 errors, 16 existing warnings |
| Product Release/x64 | PASS, 0 errors, same 16 existing warnings |
| Pure C# DXF/region/datum/placement/holder suite | **160 tests / 6,709 assertions PASS** |
| Existing Bend Planning regression | **24 tests / 609 assertions PASS** |
| Actual 18 DIM and two aggregate DXFs, punch regression | **38 assertions PASS** |
| Actual Master/workbook placement regression | **9 assertions PASS** |
| Actual 18-slot holder inventory/templates | **285 assertions PASS** |
| Independent original SPLINE + INSERT-chain QA, ezdxf 1.4.5 | **120 instances / 1,272 chords / 21,624 samples PASS** |
| Independent analytic punch/reference regression, ezdxf 1.4.5 | **6,975 curves PASS**, max error 4.55e-13 mm |
| Native Part, six bends, Debug | **888 assertions PASS** |
| Native Part, six bends, Release | **888 assertions PASS** |
| Native uniform Part, three bends, Release | **421 assertions PASS** |
| Native Assembly regression, Release | **Functional PASS; 1,531 assertions attempted; one existing layout stress warning** |

Builds used private output/obj directories and disabled pre/post-build actions:

```powershell
& $MSBuild ADDIN.csproj /t:Rebuild /p:Configuration=Debug /p:Platform=x64 `
  /p:UseSharedCompilation=false /p:KillSolidWorksOnBuild=false `
  /p:PreBuildEvent= /p:PostBuildEvent= /p:RunPostBuildEvent=Never `
  /p:OutputPath=$PrivateDebugOutput /p:BaseIntermediateOutputPath=$PrivateDebugObj
# Repeat with Release and separate output/obj paths.
```

Warnings are the baseline CS0219/CS0649 at eight unrelated existing sites,
repeated by WPF temporary compilation. No new warning; no installed DLL/PDB
replacement, registration, debugger attachment or user SOLIDWORKS termination.

Added synthetic tests exercise polynomial/rational/nonuniform/closed/periodic
SPLINE, reflected/sheared source transforms and WCS normals (including an
undefined zero normal on a linear spline), tight tolerance, pathological counts,
knots/weights/flags, missing controls, discontinuity and cancellation. A separate
Cox basis recursion checks chord parameter intervals against certified hull
bands. Tests also cover solid/hole/island/disjoint components, explicitly
reviewed intersecting/tangent/nested component unions, per-component holes,
illegal nesting/cycles/contact, missing entity roles, reference/centerline
exclusions, SPLINE plus existing closing entities and placed uncertainty.

Evidence tests cover missing authority/signature, wrong purpose/reviewer,
tampered artifact/payload, expiry/revocation, wrong source/manifest/selection/
pose/datums, omitted nullable point/matrix values, and physical-inspection
permission. An **artifact-only edit with unchanged JSON** changes the library
fingerprint and invalidates held candidates without changing the model capture.
Corrupt optional review files/paths preserve actual punch selections and all
18 slots. Held seating cannot outlive invalidated material or datums. Exact
synthetic records for 004/008/117/452 work only with matching signed instance,
label, role and selection keys; even a newly signed wrong instance is rejected.

A complete approved **synthetic** placement becomes geometry-check-ready, with
18 total candidates. The software test of a complete external physical
attestation contract is explicitly synthetic, not a company inspection. All
candidate manufacturing statuses remain CHUA_XAC_MINH, collision readiness
false and winner null in every case. No invented company geometry was used to
make the actual source tests pass.

## Actual local source status — all 18 tools

The user-confirmed saved Master/workbook files available here have **no (2)/(3)
suffix**. Actual byte hashes were observed and pinned in private artifacts; an
external confirmation that these are the intended revision is still missing.
No byte-identity claim is made about differently named revisions. This applies
to **every row** below. Private source hashes/keys/raw geometry are not in Git.

All 18 DIM profiles and selected Master punches read. **17 punch regions are
valid; KIK remains source-open**, without added edges or altered closure flags.
There are **14 holder selections**, all now fully parsed by the separate holder
inventory. The 12 spline-bearing selections contain **120/120 supported SPLINE
instances**. The original analytic-only holder status remains separately
available; it is not the material-set result.

`Loops/components` below means **simple closed component candidates /
open-or-branched components** in the raw selected drawing, not reviewed metal
region counts. Dash means no holder selection, not zero actual geometry.

| Tool ID | Punch region | Holder selection | SPLINE supported/total | Loops/components | Mapping proof | Material / datum / seating evidence |
| --- | --- | --- | --- | --- | --- | --- |
| AMD-STD-003 | Valid | Selected reference | 10/10 | 4/28 | Unreviewed | Missing |
| AMD-STD-004 | Valid | Unresolved | — | — | MAPPING_UNRESOLVED | Missing |
| AMD-STD-0048 | Valid | Selected reference | 10/10 | 4/28 | Unreviewed | Missing |
| AMD-STD-0049 | Valid | Selected reference | 10/10 | 4/28 | Unreviewed | Missing |
| AMD-STD-008 | Valid | Unresolved | — | — | MAPPING_UNRESOLVED | Missing |
| AMD-STD-045 | Valid | Selected reference | 10/10 | 4/28 | Unreviewed | Missing |
| AMD-STD-047 | Valid | Selected reference | 10/10 | 4/28 | Unreviewed | Missing |
| AMD-STD-103 | Valid | Selected reference | 10/10 | 4/28 | Unreviewed | Missing |
| AMD-STD-109 | Valid | Selected reference | 10/10 | 4/28 | Unreviewed | Missing |
| AMD-STD-117 | Valid | Unresolved | — | — | MAPPING_UNRESOLVED | Missing |
| AMD-STD-200 | Valid | Selected reference | 10/10 | 4/28 | Unreviewed | Missing |
| AMD-STD-202 | Valid | Selected reference | 10/10 | 4/28 | Unreviewed | Missing |
| AMD-STD-210 | Valid | Selected reference | 10/10 | 4/28 | Unreviewed | Missing |
| AMD-STD-452 | Valid | Unresolved | — | — | MAPPING_UNRESOLVED | Missing |
| AMD-STD-453 | Valid | Selected reference | 10/10 | 4/28 | Unreviewed | Missing |
| AMD-202662 | Valid | Selected reference | 0/0 | 0/1 | Unreviewed | Missing |
| AMD-204662 | Valid | Selected reference | 0/0 | 0/1 | Unreviewed | Missing |
| KIK-7181328001 | Open / no region | Selected reference | 10/10 | 4/28 | Unreviewed | Missing |

Actual totals: **0 reviewed material regions, 0 geometry-check-ready assemblies,
0 verified datum sets, 0 physical holders/pairs, 0 ReadyForCollisionCheck**.
All 18 manufacturing conclusions remain **CHUA_XAC_MINH**. Fully parsing drawing
entities does not establish metal fill or physical clamp seating.

The four unresolved IDs reuse a holder block across multiple possible root
instances without a sufficient unique ID association. A read-only source audit
found no DXF GROUP objects, root INSERT XDATA or extension dictionaries providing
that association. No mapping was assigned from drawing position, order or
proximity. A private reviewed mapping must identify each exact actual instance
and the authoritative drawing/setup evidence before these can be completed.

Independent SPLINE QA checks original raw degree/flags/knots/weights/controls,
then evaluates the rational source curve and independently composes original
INSERT matrices. Maximum observed sample distance is **0.000721435 mm**;
maximum certified chord bound is **0.000970076 mm**, below the requested
0.001 mm. The samples cross-check implementation; the conservative production
bound is the outward interval control hull, not the sample maximum.
Master and DIM source hashes remain unchanged.

Workbook regression still reads the four authoritative sheets / 87 source
objects / 25 reference records. It keeps MaxBendLength separate from deep-bend
examples; family-to-tool-ID applicability is unreviewed for all 18, so
UNMAPPED_LIMIT_RULE remains. The company workbook and Min data were not edited.

## Native protection and remaining coverage limits

Owned SOLIDWORKS 2024 API processes, STA, existing production Check chấn
form/session and private Part/Assembly copies were used. No Computer Use.
Native tests preserve original source Part hashes. Rebuild/configuration/
dimension changes apply only to private test copies.

- The six-bend Part keeps **6 sections / 108 candidates**. Its holes/trim/axial
  variations remain explicitly UNSUPPORTED_2D_ENVELOPE; a partial silhouette
  cannot become a clear/non-colliding workpiece.
- The uniform Part keeps **3 complete sections / 54 candidates**, with all
  16 actual boundary curves per section, including both skins and analytic arcs.
- The initial Min/dimension/limit/Planning/UI-column snapshot is exactly equal
  to saved Phase 3B. Source errors/recovery and malformed holder/seating/authority
  files preserve that snapshot. `GeometryReadCount` stays **1**, and native arc
  metadata reads do not increase on file/evidence-only refreshes.
- Existing rebuild and configuration invalidation/rescan still work. Debug
  diagnostic-failure injection is isolated; Release omits the debug call.
- Assembly selection/all scope, duplicate moved/rotated component, alternate
  referenced configuration, grouped quantity, isolated capture, Min OK/NG,
  numbering/selection/details and automatic rebuild update remain functional.
  Three Part/config records keep **90 / 54 / 54 candidates** (5 / 3 / 3 bends).
  The unchanged 1,000-pixel mock-row stress case still reports **57 px measured
  versus 53 px row height** for an existing long material label. This is an
  explicit existing UI warning, not a clean layout PASS or a Phase 3B.1 UI fix.

One earlier run of the inherited private Assembly harness stopped at its
`Part initial pass` assertion after 1,522 assertions, **after the Assembly
grouping/Min checks succeeded**. That attempt did not record the first Part's
status, so its cause remains unproven; it is not silently counted as a pass.
An instrumented repeat completed the functional scenarios (1,531 assertions
attempted, with the one known layout warning) and recorded the first Part as
Current / Done / Default / Đạt with its original test dimension.
The private harness now also waits for `Scanned && all Items.Current` before
asserting that initial verdict, rather than accepting Done alone. This is test
precondition hardening, not a claimed production Min/UI repair. The intermittent
earlier failure remains a disclosed validation limitation; the known layout
stress warning remains separately reported. The final Current-gated run also
completed the functional scenarios with 1,531 assertions attempted and the same
one layout stress warning; neither run is claimed as a clean layout pass.

## Blockers classified for handoff

| Category | Remaining issue | Required next evidence/work |
| --- | --- | --- |
| Actual source/reviewer evidence | 004/008/117/452 exact holder association | Approved exact label/root INSERT/block/entity selection, not paper-position inference |
| Actual reviewer evidence | 14 selected references have no approved material roles | Assign every entity, solid/hole/island/component hierarchy, exclusions and union rule; external artifact and authorized signature |
| Actual source evidence | KIK punch source is open; any genuinely incomplete material boundary remains open | Company-approved complete source if required; do not heal the supplied DXF |
| Actual physical evidence | All tip/mount/local-frame/relative seating and real holder inspection records are absent | Approved measurements/drawing and machine inspection, authorized reviewer/artifact/expiry/signature |
| Actual revision evidence | Saved unsuffixed Master/workbook not externally matched to requested named revisions | Operator confirms intended revision and source hashes |
| Actual applicability evidence | Excel family examples not approved against exact 18 IDs | Reviewed family/machine/tool association; keep UNMAPPED_LIMIT_RULE meanwhile |
| Code support limit | Fit-only, nonpositive/ill-conditioned weights, nonplanar/high-degree/over-budget SPLINE | Remain explicit unsupported; no invented conversion |
| Code support limit | Degenerate/self-crossing or unresolved touching hole hierarchy within a component | Remain unsupported; explicit component set union is supported but no repaired/merged boundary is created |
| Future scope | Axial variation/holes/trim not proven by one uniform section | Multiple proven slices or native 3D envelope, not fabricated geometry |
| Future scope | No collision, process states, winner or feasible sequence | Separate later phase with complete evidence; never convert this phase's readiness into PASS/NG |

Local QA generated unfilled private material/mapping/seating/authority templates
and an operator validation checklist. Source facts only are prefilled; all
reviewer attestations, measured coordinates/pose, dates, signatures and keys
stay blank, physical confirmation stays false. They were not auto-configured.
No CAD, Excel, Part, private manifest, review artifact or design coordinates are
included in this branch.
