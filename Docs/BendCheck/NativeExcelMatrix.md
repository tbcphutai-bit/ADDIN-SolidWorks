# Native Excel 4200 / 6000 mm fixtures

Baseline: PR #34, `5356ab453ee7148ccf31b20191868ff4ac64f20a`.

This is test infrastructure. It does not change Check Min math, the collision
engine, the planner, WinForms/Designer, commands or installed add-in registration.

## Source contract

The source-bound 33-case run uses these three inputs:

- `深曲げ限界.xlsx` (the original workbook explicitly confirmed by the user).
- `Check_Chan_Test_Matrix_Excel_4200_6000.xlsx` (case interpretation).
- `Check_Chan_Test_Cases_4200_6000.csv` (all 33 identified records).

The user confirmed the old workbook and authorized preparing a new 33-case
matrix/CSV from it. These are newly defined native validation cases, not a
claim to have recovered an unavailable previously authored CSV. The source
envelopes are W/L 30×60, 45×75 and 50×100 from embedded images 1/2 on
`金型限界表`; reference length cells are B5/B35, and material headers are
B9:B11/B39:B41. The matrix explicitly distinguishes 18 symmetric U envelope
variants from 9 Z and 6 three-bend controlled extensions. R2 is a fixture
parameter, not an Excel/tool certification. Do not invent a missing source
interpretation or silently substitute workbook revisions.
The original files are read only. Keep the input files, interpretation sidecar,
generated CAD, screenshots and raw JSON in a private directory outside Git.

`ExcelNativeMatrixInput.cs` reads an explicit version-1 sidecar. It contains:

| Field | Meaning |
| --- | --- |
| `Purpose` | `CompanyExcel33` for company acceptance; `GeneratorValidation` for controlled diagnostic Parts only |
| `InterpretationEvidence` | Reference to the reviewed source sheet/cells/diagram and interpretation of W/L |
| `Sources` | Exactly one `Csv`; company mode also requires `ExcelExamples` and `ExcelMatrix`. Each has an absolute `Path` and `Sha256` |
| `Columns` | Logical field to exact source CSV header; never fuzzy-match headers or guess units |
| `Materials` | ST/SUS/AL to explicit installed material database and material name |
| `Sections` | One case-bound sketch interpretation, explicit inside radius in mm, and source evidence per CSV case |

Logical CSV fields are `CaseId`, `Shape`, `AxialLengthMm`,
`ReferenceAxialLengthMm`, `W`, `L`, `ThicknessMm`, `MaterialGroup`.
Shape codes are `U`, `Z`, `THREE`. Mapping a source label to those codes is an
explicit preparation step. The current fixture convention measures W on X, L
on Y and extrusion/bend length on Z in the provided template. Use a verified
XY template; an incompatible axis fails its native audit.

Each section has `CaseId`, `Evidence`, `InsideRadiusMm`, and
`SketchVerticesMm` as an open sequence of XY points in mm. The source drawing
must determine the points; W/L alone cannot determine a three-bend section.
The sketch is not assumed to be the physical outside boundary. Explicitly
account for thickness direction when preparing its source interpretation.
**Never change the requested W/L or rescale/retry a company fixture to make an
incorrect interpretation pass.** A dimension mismatch is a failed test with
the measured facts preserved.

The reader validates all cases before starting SOLIDWORKS: source hashes,
unique case/filename identities, exact CSV column bindings, finite positive
mm dimensions, material mappings, explicit radius, open orthogonal section,
U/Z signed turns and absence of self-intersections. Company mode requires
exactly 33 rows and both workbook revisions. Sources and the sidecar are
hashed again during and after execution.

`prepare_native_excel_matrix.py REVIEWED.json MATRIX.xlsx NEW-output` prepares
the private CSV and sidecar reproducibly. Reviewed JSON supplies the original
source path/hash, `Authorization`, 33 `Rows`, material mappings and each row's
source/section/radius interpretation. It embeds no company values in code.
It checks the normalized workbook's typed numeric cells against the reviewed
rows before writing input. The native B-rep audit remains the final check of
the fixture's actual outside dimensions. Keep the reviewed JSON private.

## Actual native execution

Use an external STA test process. It starts an owned SOLIDWORKS PID and never
falls back to the user's running session. It does not deploy/register the
product DLL. SOLIDWORKS may require an elevated shell on this installation.

```powershell
& .\Tests\BendToolProfiles\Run-NativeExcelMatrix.ps1 `
  -Sidecar $reviewedSidecar `
  -OutputDirectory $newPrivateRunDirectory `
  -PartTemplate $partTemplate `
  -MinimumWorkbook $existingCheckMinWorkbook `
  -PlacementConfiguration $fixedCompanyPairConfiguration `
  -Configuration Release
```

Repeat with Debug and a different new output directory. The script disables
the normal product pre/post-build deployment/kill hooks and COM registration.
It validates input before starting the owned native process. The fixed-pair
catalog from PR #34 must be configured; a missing/invalid catalog fails the
18-pair assertion rather than substituting geometry.

The native executable also accepts an optional sixth argument containing
comma-separated source case IDs. It still validates the complete 33-row input,
then reports the explicit subset and `SourceCases=33`. This allows separate
owned native sessions without changing cases or sharing unproven results.
`merge_native_excel_matrix.py --csv SOURCE.csv --output NEW-private.json
RUN1.json RUN2.json RUN3.json` requires identical sidecar/source hashes, rejects
duplicate/foreign cases and retains missing source cases as NOT_RUN with no
fabricated measurements. Shutdown acceptance remains separate after merging.

For each actual generated sheet-metal Part, the harness:

1. Creates an open native sketch and `InsertSheetMetalBaseFlange2` with explicit
   thickness, radius and axial length. Assigns/readbacks its real material.
2. Waits for new-fixture rebuild notifications before recording the live
   revision. This does not retry or suppress a stale completed check.
3. Independently measures B-rep directional extrema, physical cylindrical bend
   face lengths, native thickness, bend angles/radii, actual owning FeatureIds
   and resolving PersistentIds. The mm dimension tolerance is **0.001 mm**.
4. Runs the existing real Check Chấn form/controller/backend, cold and warm.
   Every bend must retain 18 distinct static and 18 process candidates, with
   Punch and Holder independent, and all 18 planning tool candidates.
5. Compares displayed Check Min status, measurements and limits with the
   original evaluator. Checks the material group and native face-owner IDs.
6. Compares all cold/warm facts, reasons, witnesses and error values. JSON
   object property order is ignored; numeric values are not rounded and no
   geometry tolerance is relaxed for this comparison. Arrays keep their order.
7. Checks warm capture reuse, no change to the Part's saved bytes/state, and
   first-case native Rebuild/stale withdrawal/Refresh with the same FeatureIds.
8. Checks zero worker-thread native topology captures and unchanged input bytes.

Failures remain in the per-case log; an invalid interpretation is not healed.
The original CAD/Excel/material databases and template are never saved.

## Reference lengths and production conclusions

4200/6000 mm are **Excel reference lengths**, separate from W, L and thickness.
`ABOVE_REFERENCE_NOT_A_COLLISION_VERDICT` identifies an above-reference row.
The marker is report metadata, not a tool axial setup, machine capacity
certificate or automatic COLLISION. No tool working length is inferred.

Finished fixture geometry supplies no verified intermediate process states,
mounting datum, seating or insertion/removal motion. Without a valid reviewed
process package, alternative bend orders and a common production tool remain
UNKNOWN. The existing Phase 4B provider/planner is called; no process graph is
invented. No V-die is tested and no manufacturing PASS is generated.

## Reports

The harness preserves all source-bound detailed results in
`PRIVATE-native-matrix.json`, including per-bend/per-tool independent Punch,
Holder and process rows. It also writes immutable cold/warm JSON captures
before live snapshots are invalidated.

```powershell
python .\Tests\BendToolProfiles\report_native_excel_matrix.py `
  $privateNativeJson $newPrivateReportDirectory
```

CSV includes case ID, actual fixture path, measured length/W/L/t, FeatureIds,
Check Min, per-bend candidate counts, Punch/Holder/process/planning status,
UNKNOWN reasons, cold/warm milliseconds, capture hits/queries and execution
status. `--public` replaces local fixture paths with basenames and omits raw
CAD/source coordinates; inspect outputs before publication. Do not upload
company CAD/Excel or source interpretation sidecars.

Functional case PASS and native server shutdown are separate. If SOLIDWORKS
exit is not confirmed, the executable returns nonzero and overall native
lifecycle acceptance remains FAIL. Do not increase the shutdown deadline to
report success. Zero numeric queries indicate missing evidence prechecks, not
a collision computation benchmark.

## Reproducible generator probes

`New-NativeExcelProbes.ps1` writes **nine controlled** U/Z/THREE cases using
caller-supplied installed material names. These exercise all six reference
boundary lengths and the three thickness/material groups through native CAD.
They are labeled `GeneratorValidation` and cannot count as company acceptance.

```powershell
& .\Tests\BendToolProfiles\New-NativeExcelProbes.ps1 `
  -OutputDirectory $newPrivateProbeDirectory -MaterialLibrary $materialDatabase `
  -STMaterial $steelMaterialName -SUSMaterial $stainlessMaterialName `
  -ALMaterial $aluminiumMaterialName
```

Use its sidecar with the same native runner. `ExcelNativeMatrix.Tests.csproj`
tests the input contract and JSON comparison without SOLIDWORKS. It does not
replace native acceptance.
