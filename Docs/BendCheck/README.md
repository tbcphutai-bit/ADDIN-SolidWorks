# BEND CHECK – read-only backend V0

This branch starts a **new independent Bend Check command** in the existing
\`ADDIN-SolidWorks\` solution (.NET Framework 4.8, C# 7.3). Existing TaskPane,
Designer files, registration, feature commands and CAD documents remain unchanged.

## Invoking it

Backend entry point: \`SwAddin.RunBendCheckJson()\`.
It returns a JSON report for the active native Sheet Metal Part, with:

- Validation of Part + single solid body + SheetMetal and FlatPattern features.
- Detection of **active OneBend** features and available bend metadata.
- Up to 24 possible bend orders, explicitly \`NOT_EVALUATED\`.
- The 18 company DXF identifiers (metadata inventory only).
- Explicit \`UNVERIFIED\` status and per-feature warnings.

No UI button is added yet: expose the entry point to Codex for subsequent
integration with the existing TaskPane. This protects the user's WinForms files.

Set optional environment variable \`ADDIN_BEND_TOOL_DXF_DIR\` to a folder
containing original \`amd-std-004_DIM.dxf\`-style files to list whether those
files are present. **Presence is not validation of profiles or clamps**.
Do not use the schematic clamp profiles as fit-verified geometry.

## Deliberately not implemented

- Accurate 2D cross-sections and tool mating datum alignment
- Per-step folding transformations and 2D swept interference
- Die/holder/press clearance and tonnage verification
- Ranking or declaring a bend order feasible
- Manufacturing PASS / FAIL classification
- WinForms UI and installation tests

The command MUST NOT return PASS from an unverified 2D or 3D check.
Tool names and Excel 4200/6000-mm references are not approved machine capacities.

## Next coding milestones for Codex

1. Add and validate real DXF profile geometry (and provenance).
2. Extract a cross-section per bend and construct intermediate states.
3. Implement a 2D collision evaluator with explicit tool datum and tolerances.
4. Add tooling constraints, then permit confirmed sequence evaluation.
5. Integrate command invocation with existing TaskPane **without touching
   Designer files**, and test on real SOLIDWORKS on Windows.

## Acceptance checks for this branch

- Solution project explicitly includes BendCheckCommand.cs.
- SWAddin COM callback code and TaskPane lifecycle are unchanged except
  for the new entry method.
- Part with OneBend features returns bend inventory and candidate orders.
- Unsupported/unknown cases return UNVERIFIED with issues, never PASS.
- No document/configuration/feature changes are made.
- Requires Windows + matching SOLIDWORKS Interop references for compilation.
