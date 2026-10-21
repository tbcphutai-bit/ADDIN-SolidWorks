# Check Chấn V4.5 — Result-first UI, Codex handoff

**Scope:** C# WinForms .NET Framework 4.8, existing Check Chấn source only. Source branch: `feature/check-chan-v45-result-first-ui-20261021`; based on PR #37 at `812dbb3605b656c7d5d1eda430ceb918bada1de8`. **BUILD / REBUILD / native tests: NOT RUN.** No CAD or private Excel/DXF supplied to this PR.

## User goal

A person opening Check Chấn should immediately see: **which Part and bend, the real 2D section, whether the bend is confirmed, whether Check Min passes, whether Check MAX is determined, and whether a real collision check was performed.** Hide technical process, SHA, full tool inventory and repeated UNKNOWN reasons until requested.

## Code already authored (do not redesign it)

Edits are made **in-place** in these existing UI classes; there is no V4.5 class or new `.csproj` entry:

1. `UI/BendAssemblyBatchForm.UxV4.cs`: `InstallUxV45ResultFirst` moves the existing 18-tool DataGridView card into the **Bộ dao** tab. It reuses original controls/events and places a single conclusion panel in the main view. `UxV45UpdateOutcome` is a UI-only projection of current data. The canvas gets 51% of main width, result 27%, part list 22%.
2. `UI/BendSection2DCanvas.cs`: 2D drawing occupies more space. Removed verbose repeating Excel provenance panels and reduced footer to one short statement. No change to the geometry, collision or contact witnesses.
3. `UI/BendAssemblyBatchForm.UxV43.cs`: unverified Excel family chooser is hidden from the main picture; original separate Excel reference tab remains. No fake matching.

### Outcome projection (strict)

- **BelowMinimum** from actual bend metadata → `KHÔNG ĐẠT MIN`. This does **not** automatically decide Check MAX or physical collision.
- CAD-ready count = candidates with **nonempty Punch AND Holder curves**; the 18 fallback ToolIds are not proof of 18 loaded tool profiles.
- `0/18` CAD-ready → `Va chạm: chưa kiểm tra (thiếu dao)`, **not** CLEAR/PASS.
- A genuine geometric/process candidate COLLISION may show `ỨNG VIÊN BÁO VA CHẠM`, **never** “entire Part impossible”.
- A candidate CLEAR never certifies manufacturing or bend sequencing.
- Excel case matcher, Excel→18 ToolId mapping and checked MAX process are **not implemented** here; UI explicitly reports Check MAX **chưa đối chiếu Excel**.
- `UNKNOWN` is not converted to `CLEAR`; unverified cutting-edge overlays remain hidden.

### Source-derived reference constraints

User Excel `深曲げ限界(4).xlsx` contains five family length references:
Sash 6000 mm; Gooseneck HDS 4200 mm; Special Gooseneck HDS 4200 mm; Straight HDS 4200 mm; Gooseneck HD 6000 mm. These lengths are not the maximum height/depth of a flange. W/L regions and C/L/U/Z cross-sectional samples require verified dimensional/geometric applicability; no pixel-based measurement. Prefer a **verified applicable** Excel case; do CAD fallback only if Excel applicability can be conclusively ruled out. When applicability is unresolved, preserve UNKNOWN.

18 company Punch–Holder CAD pairs belong to separate user files; do not upload their source drawings or infer Excel family→ToolId mappings.

Last `Output-Debug.txt` observed `ToolCount=18, DataCount=0, MatchedPairs=0, NumericQueries=0` and `DXF source directory is not configured` in a run. The code does not repair that external configuration; show the absence plainly. Check Min of FeatureIds 524/530 showed MeetsMinimum, not Check MAX.

## Instructions to Codex — one task

**Project:** `C:\Tao moi\ADDIN\ADDIN.csproj`. The SOLIDWORKS DLL build output remains `C:\Tao moi\ADDIN.dll`.

1. Read this file and the **three exact C# files in the GitHub branch**. Compare Git HEAD/branch/worktree and local modifications. Never reset, clean, stash, force checkout or overwrite another command's edits.
2. Fetch `origin/feature/check-chan-v45-result-first-ui-20261021`. Compare these three paths with local source and current PR #37. If local files differ beyond the expected V4.4 baseline, **STOP and report conflict**; do not blindly `git restore`.
3. Copy the **exact authored code** from this branch into the matching three existing class files. No new V4.5 class, helper class or source dependency. Preserve all other local changes.
4. Run static read-only checks only: C# declarations/hook references, `git diff --check`, `.csproj` Compile entries and non-UI file diff. Do not invoke MSBuild, Rebuild, run native/COM, modify registry/config or regenerate DLL/PDB.
5. Check visually/read-only that the main view contains **Part | 2D Section | Outcome**, and the 18-row grid is in the secondary tab; ensure original full details can still be opened.
6. Preserve Check Min math, collision kernel, `BendCheckSession`, model capture, 18 CAD pairing rules, Tool Profile provider, planner, COM ownership, and other commands.
7. Since the code is already in a GitHub branch / Draft PR, do **not** create another branch/PR or re-commit identical changes. Don't merge to main. Any further change requires reporting first.

**Report:** exact local branch/HEAD, source SHA before/after for 3 UI files, compile hooks checked, relevant diff, only UI changed, known unverified MAX/Excel/placement features, GitHub PR URL, and bold confirmation **BUILD/REBUILD/NATIVE NOT RUN**. Stop after report.

## Manual verification (only when user chooses to Rebuild)

Close SOLIDWORKS, manually Rebuild Debug/x64, restart SOLIDWORKS. Check XL-02 and one small Assembly, not 33-Part benchmark. Verify selection B1/B2 and B1–B6, canvas real geometry or clear absence reason, result card, hidden full tool matrix accessible, 0/18→unverified, technical tabs accessible, Refresh/Cancel/Close. Send real screenshots and log for review; no fabricated “PASS”.

**Known limit:** This change solves result-first presentation only. It does **not** solve the missing DXF configuration, Excel matcher, process-state placement, or collision numeric evidence.
