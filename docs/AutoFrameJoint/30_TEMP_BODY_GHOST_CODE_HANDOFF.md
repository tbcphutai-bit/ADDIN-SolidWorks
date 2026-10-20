# 30 — Auto Frame Joint 3D Ghost Preview (SOURCE-ONLY code handoff)

**AUTHOR:** ChatGPT, backend/code owner.  
**BRANCH:** `feat/afj-temporary-body-ghost-engine-source-only-20261020`  
**BASE:** `e9f3ea434957b255d38f56bcac6d8d523496f5c4` (safe isolated-output project).  
**STATUS:** C# SOURCE WRITTEN, REGISTERED IN CSPROJ, **NOT BUILT, NOT HOST-TESTED, NOT CONNECTED TO UI**. The user currently has another command debugging in SOLIDWORKS. **DO NOT RUN MSBUILD, START/STOP SOLIDWORKS, COPY DLL/PDB, REGISTER COM, OR MERGE** until the user explicitly says the other debugging is finished.

## What ChatGPT has actually written

1. **NEW** `Commands/AutoFrameJoint/AutoFrameJointGhostPreview.cs` (complete implementation):
   - `AutoFrameJointGhostPreview(ISldWorks swApp, ModelDoc2 originalAssembly)`
   - `bool TryShow(Component2 frameA, Component2 frameB, out string message)`
   - `bool TryClear(out string error)`
   - `bool IsShowing`
   - Captures Assembly identity, rejects unknown `app.IsSame` results, rejects identical components, checks both Part documents and top-level components.
   - Copies each Solid Body with `IBody2.Copy()`, requires `IsTemporaryBody()`, and calls `Display3(Component2, COLORREF, swTempBodySelectOptionNone)`.
   - Attempts cyan for A, orange for B, 65% transparency on COPIED bodies only. Some hosts may ignore transparency, so do not claim exact rendering until tested.
   - Keeps strong references to shown bodies, invokes `Hide(PartDoc)` for cleanup; if Hide throws, retains the body references for a future cleanup attempt.
   - Clears previous overlays before showing new ones, and attempts cleanup after any failed partial render. No changes to original geometry; no Feature, Cut, Mate, Transform, Save, AddBody or boolean operations.
   - Rejects nested-subassembly components because SOLIDWORKS `IBody2.Display3` docs state this mode does not support them.
   - Limits drawing to 12 Solid Bodies per component to avoid unlimited previews.

2. **EDIT** `ADDIN.csproj`: one extra compile entry:
```xml
<Compile Include="Commands\AutoFrameJoint\AutoFrameJointGhostPreview.cs" />
```
   The source-only project retains the previous safe `bin\x64\Debug\` OutputPath, empty PreBuildEvent and PostBuildEvent. No other project code is modified.

**INTENTIONALLY UNCHANGED:** `AutoFrameJointSelectionPage.cs`, `AutoFrameJointCommandEntry.cs`, `AutoFrameJointReadOnlyProbe.cs`, `BomTaskPaneControl.cs`, `SWAddin.cs`, Check Hole, Mirror, Bend, any actual installed DLL.

## Crucial semantic distinction

**This is NOT a cut-preview.** It displays **unaltered copies of the two already existing Frame bodies at their existing positions**. Its purpose is to validate drawing of color-coded temporary geometry, COM lifecycle and safe cleanup before a real joint solver exists. It cannot display bevels, cutouts or protruding lip preservation correctly until we have a solver based on profile-specific cutting rules. User must never be told “góc đã cắt” at this stage.

### Why not wire the preview now?

The current native PMP has only `Phân tích` and Cancel and has not yet passed a real in-process host smoke test. Immediate Display3 calls in `OnSelectionboxListChanged` risk COM reentrancy and selection corruption. **Do not add ghost logic to selection-change callbacks.** We should test the existing PMP first, then add one explicit `Xem hình A/B` button and a safe lifecycle.

## SOLIDWORKS official API signatures verified

```csharp
object IBody2.Copy();
bool IBody2.IsTemporaryBody();
int IBody2.Display3(object Component, int Color, int Option);
void IBody2.Hide(object Part);
object IComponent2.GetBodies3(int BodyType, out object BodiesInfo);
```

Use `swTempBodySelectOptions_e.swTempBodySelectOptionNone == 0`, not `swTempBodySelectable` (which sets `swModifyBlock`). The Color argument is COLORREF, so use `ColorTranslator.ToWin32`, NOT `Color.ToArgb()`. `Hide` should be called while the body reference is still alive. Only a real SOLIDWORKS session can verify whether `Hide(part)` cleans up overlays rendered in the component context. If Hide fails, stop; do not force COM releases of displayed objects.

Official references:
- Display3: https://help.solidworks.com/2023/english/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.IBody2~Display3.html
- Hide: https://help.solidworks.com/2023/English/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.IBody2~Hide.html
- GetBodies3: https://help.solidworks.com/2018/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IComponent2~GetBodies3.html
- Copy: https://help.solidworks.com/2018/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IBody2~Copy.html
- MaterialPropertyValues2: https://help.solidworks.com/2010/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IBody2~MaterialPropertyValues2.html
- GetParent: https://help.solidworks.com/2025/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IComponent2~GetParent.html

## Very specific instructions for Antigravity — NOW, during other command's debug

1. Read the real C# file `Commands/AutoFrameJoint/AutoFrameJointGhostPreview.cs` IN FULL. Do not replace it with a generated mock, UI preview HTML, pseudocode or three-tab WinForms draft.
2. Read the exact `ADDIN.csproj` diff, and ensure one new `Compile Include` for ghost engine.
3. Review the C# syntax and namespace/type matches against already checked-in SOLIDWORKS Interop files; report suspected compiler/runtime issues. Do not claim Build PASS from code-reading.
4. **Do not edit**: `AutoFrameJointSelectionPage.cs`, `AutoFrameJointCommandEntry.cs`, `SWAddin.cs`, Check Hole, Mirror, Bend, any common TaskPane class, event handlers.
5. **Do not build, rebuild or interact with running SOLIDWORKS**. Keep the current debug session untouched.
6. Submit a review-only report with: source branch and SHA, file/line issue list, detailed questions, assessment of `GetBodies3/Copy/Display3/Hide` COM availability, needed host cases. Report potential bugs to ChatGPT; do not patch backend independently.
7. Do not push/merge additional C# edits without code-owner review. If you need to suggest an API correction, include exact existing line and the proposed corrected line.

## Exact future UI integration pattern — AFTER the current native PMP host passes

**These are integration instructions, NOT changes already applied.** ChatGPT/code owner must approve and write this diff later.

### A. PropertyManager (UI only)
Existing file: `UI/AutoFrameJoint/AutoFrameJointSelectionPage.cs`

- Add ID `ButtonShowBodies = 105`, leaving existing IDs 101/102/103/104 unchanged.
- Add `Func<Component2, Component2, string> onShowBodies;` as a constructor callback, preserving existing `onAnalyze` logic and selection marks 1/2.
- Add a **SECOND native button** `Xem hình A/B` with clear caption `Hình gốc, chưa cắt`; disabled until both selectors are valid.
- On clicking `ButtonShowBodies`, use the same `TryReadSelection(out a, out b, out reason, true)` logic as Analyze. Invoke `onShowBodies(a,b)` ONLY after validation, NOT inside `OnSelectionboxListChanged`.
- Do not parse free-form Analyze text to determine preview permission.
- Keep preview lifecycle separate: if the A/B pair changes, the old temporary overlays must be cleared at a safe point before drawing a new preview. DO NOT call `Display3` or `Hide` directly inside selection-list callbacks.
- On Cancel/AfterClose, invoke backend cleanup, report any `TryClear` failure and do NOT discard retained body references.

### B. Command entry integration (backend/code-owner only)
Existing file: `Commands/AutoFrameJoint/AutoFrameJointCommandEntry.cs`. After the PMP baseline test succeeds, instantiate an engine using the **captured** original Assembly:

```csharp
AutoFrameJointGhostPreview ghost =
    new AutoFrameJointGhostPreview(app, sourceAssembly);

// NEW separate callback from PMP's preview button. This is a
// wiring snippet for the NEXT integration commit, not compiled today.
Func<Component2, Component2, string> showBodies = (a, b) =>
{
    string message;
    return ghost.TryShow(a, b, out message)
        ? message
        : "Không thể hiển thị: " + message;
};

// Page AfterClose callback; always inspect Hide errors.
Action clearGhost = () =>
{
    string error;
    if (!ghost.TryClear(out error))
        Trace.WriteLine("[AFJ-GHOST] Cleanup pending: " + error);
};
```

**Critical lifetime rule:** the future command entry must keep `ghost` alive until every temporary body is hidden. A ghost with failed `Hide` must be retained for recovery; do not drop the only reference or call `Marshal.FinalReleaseComObject` while displayed. Review cleanup design and onClosed event order before enabling the UI button.

### C. Do not confuse Preview with cutting
No `FeatureCut`, `Body2.Operations2`, `Save3`, `EditPart`, suppression, mating or modifying original Body2. Actual cut preview requires a separate `JointCutSolver` and per-profile cut rules established from real Frame examples.

## Later build/host validation — DO NOT RUN NOW

After the user says the other debug is finished and after current native PMP base host test:
1. Build `ADDIN.csproj` Debug x64 from isolated worktree; verify no installed `C:\Tao moi\ADDIN.dll` or `.pdb` hash changes.
2. Static error check of the new ghost class. No standalone compile success is equal to host validation.
3. On COPIED assembly, with two top-level resolved Part components, explicitly invoke the preview button after code-owner wiring.
4. Verify 2 colors overlay at correct component positions; no shifted or double-transformed geometry.
5. Try multi-body Part <=12, missing/unresolved Part, nested subassembly (must fail clearly), no Solid Body, active doc switched.
6. Click Preview twice; it must Hide the prior copies before redrawing.
7. Cancel: no lingering temporary bodies; if Hide fails, record trace and stop.
8. Test selection of components after ghost draw, ensure nonselectable overlay does not block selection; test Panel status and Analyze independence.
9. Feature Tree/Mates/configuration/document dirty flag unchanged; never save CAD in a ghost-test.
10. Inspect all COM/Interop errors and report to ChatGPT before accepting.

## Acceptance gate

The current source-only phase is done when the new C# file is present on GitHub with a single `Compile Include` and this runbook. It is **NOT** a working end-user Preview button yet. Do not claim the user has a cut visualization or even that Render/Hide works in host until actual tests pass.
