# Auto Frame Joint — Native PropertyManager ONLY for selecting two Frames

**Decision date:** 2026-10-19  
**Status:** Approved UX direction; design and code handoff, not a tested implementation.  
**Branch:** `docs/auto-frame-joint-pmp-selection-20261019`  
**Supersedes:** the three-tab WinForms wizard in `14_ANTIGRAVITY_UIUX_PLAN.md` **for main interaction**. Keep technical diagnostics in a secondary expandable panel or report.

## One clear workflow

1. In SOLIDWORKS Assembly, launch **Auto Frame Joint**.
2. Native PropertyManager Page appears on the left with exactly **two selection boxes**: **Frame A** and **Frame B**. Each takes ONE Component occurrence. Optionally prepopulate from the already selected two components after verifying owner instance and mark assignment.
3. Graphics Area remains fully visible; each selection uses SOLIDWORKS native selection color. Show short one-line state `2/2 Frame` and one **Phân tích** action.
4. On Analyze, call the existing read-only analyzer through a reviewed adapter that accepts **A and B explicitly**. Do not blindly depend on unmarked global selection order.
5. Viewport displays read-only results and, **when a cut solver has genuinely generated temporary bodies**, a semi-transparent 3D ghost preview. If not available, explicitly show `Chưa có Preview vết cắt` rather than inventing a cut.
6. Cancel closes the Page and clears any temporary preview; no Part, Mate, selection, Feature Tree, configuration or files are changed for a preview. Apply Cut remains unavailable until verified solver approval.

### Minimal PMP interface

```text
AUTO FRAME JOINT                              [✓] [X]

 CHỌN FRAME
 ┌ Frame A [native selection box] ──────────────┐
 │ Click first Component in Assembly            │
 └──────────────────────────────────────────────┘
 ┌ Frame B [native selection box] ──────────────┐
 │ Click second Component in Assembly           │
 └──────────────────────────────────────────────┘
 [↔ Đổi A/B]                   2/2 đã chọn

 [Phân tích]
 ──────────────────────────────────────────────
 Góc hai trục:  —°      Dài / Ngắn: —
 Trạng thái:    Chưa phân tích
 [▸ Chi tiết kỹ thuật]     [Preview: chưa hỗ trợ]

(3D graphics area is SOLIDWORKS itself; no embedded model picture.)
```

No separate `Tab 1 / Tab 2 / Tab 3` wizard as the primary user flow. Use short labels and icons; path/config/confidence remain hidden under details.

## API verified in SOLIDWORKS help

- `ISldWorks.CreatePropertyManagerPage(...)` then `IPropertyManagerPage2.AddGroupBox`, `IPropertyManagerPageGroup.AddControl2`.
- Selection boxes: `IPropertyManagerPageSelectionbox.SetSelectionFilters(new int[]{...})`, `SingleEntityOnly=true`, `Mark=1/2`, `SetSelectionFocus()`.
- `ISelectionMgr.GetSelectedObjectCount2(mark)`, `GetSelectedObjectsComponent4(1,mark)`; marks must be distinct powers of two.
- `IPropertyManagerPage2Handler9.OnSubmitSelection` rejects non-Components and repeated same-instance selection.
- `OnSelectionboxListChanged` is query-only; **never modify selection/model** inside it. Defer actual analysis/preview to a safe action after selection processing.
- `SetSelectionColor(true, swSystemColorsSelectedItem1/2)` uses native **selection colors**, not arbitrary cyan/pink ghost colors.
- Temporary body preview is **backend-owned** and independent of selection highlight.

Source patterns already in this repo: `UI/MirrorAssemblyPropertyManagerPage.cs` (C# 7.3 COM page with marks and selection boxes). Read it first. Do not copy its **Mirror Execution / Assembly save** code.

## C# 7.3 — actual PMP selector control setup (code-owner snippet)

This code belongs in a **new isolated** PMP class (e.g. `UI/AutoFrameJoint/AutoFrameJointPropertyManagerPage.cs`) created/integrated by the backend owner **after review**; do not paste inside the Mirror class:

```csharp
using System;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

private const int FrameGroupId = 100;
private const int FrameASelectionId = 101;
private const int FrameBSelectionId = 102;
private const int FrameAMark = 1;
private const int FrameBMark = 2;

private PropertyManagerPageSelectionbox frameABox;
private PropertyManagerPageSelectionbox frameBBox;

private void AddFrameSelectors(PropertyManagerPage2 page)
{
    const string notSelected = "Chọn một Component Frame trong Assembly";

    int groupOptions =
        (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Visible |
        (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Expanded;
    int controlOptions =
        (int)swAddControlOptions_e.swControlOptions_Visible |
        (int)swAddControlOptions_e.swControlOptions_Enabled;
    short alignment = (short)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;

    PropertyManagerPageGroup group = page.AddGroupBox(
        FrameGroupId, "Chọn hai Frame", groupOptions) as PropertyManagerPageGroup;
    if (group == null) throw new InvalidOperationException("Cannot create Frame group.");

    frameABox = group.AddControl2(
        FrameASelectionId,
        (short)swPropertyManagerPageControlType_e.swControlType_Selectionbox,
        "Frame A", alignment, controlOptions, notSelected)
        as PropertyManagerPageSelectionbox;

    frameBBox = group.AddControl2(
        FrameBSelectionId,
        (short)swPropertyManagerPageControlType_e.swControlType_Selectionbox,
        "Frame B", alignment, controlOptions, notSelected)
        as PropertyManagerPageSelectionbox;

    if (frameABox == null || frameBBox == null)
        throw new InvalidOperationException("Cannot create Frame selection boxes.");

    // Important: these must be configured before page.Show2(0).
    frameABox.Mark = FrameAMark;
    frameBBox.Mark = FrameBMark;
    frameABox.SingleEntityOnly = true;
    frameBBox.SingleEntityOnly = true;
    frameABox.Height = 35;
    frameBBox.Height = 35;

    int[] componentFilter = { (int)swSelectType_e.swSelCOMPONENTS };
    frameABox.SetSelectionFilters(componentFilter);
    frameBBox.SetSelectionFilters(componentFilter);

    // Native SOLIDWORKS selection colors. Verify enum names with
    // locally installed Interop Object Browser before compiling.
    // frameABox.SetSelectionColor(true,
    //     (int)swUserPreferenceIntegerValue_e.swSystemColorsSelectedItem1);
    // frameBBox.SetSelectionColor(true,
    //     (int)swUserPreferenceIntegerValue_e.swSystemColorsSelectedItem2);
    frameABox.SetSelectionFocus();
}

private static Component2 SelectedOccurrence(ModelDoc2 asm, int mark)
{
    SelectionMgr manager = asm.SelectionManager as SelectionMgr;
    if (manager == null || manager.GetSelectedObjectCount2(mark) != 1)
        return null;
    return manager.GetSelectedObjectsComponent4(1, mark) as Component2
        ?? manager.GetSelectedObject6(1, mark) as Component2;
}

// Handler method signatures vary by installed COM interop interface:
// copy implementation signature from UI/MirrorAssemblyPropertyManagerPage.cs.
// Example in this repo:
public bool OnSubmitSelection(int id, object selection, int type,
                              ref string itemText)
{
    if (id != FrameASelectionId && id != FrameBSelectionId)
        return false;
    if (type != (int)swSelectType_e.swSelCOMPONENTS)
        return false;

    Component2 candidate = selection as Component2;
    if (candidate == null) return false;

    ModelDoc2 asm = app.ActiveDoc as ModelDoc2;
    if (asm == null || asm.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
        return false;

    int otherMark = id == FrameASelectionId ? FrameBMark : FrameAMark;
    Component2 other = SelectedOccurrence(asm, otherMark);
    if (other != null && app.IsSame(candidate, other) ==
        (int)swObjectEquality.swObjectSame)
        return false;

    itemText = candidate.Name2;
    return true;
}

public void OnSelectionboxListChanged(int id, int count)
{
    // Read-only query/update UI state only.
    // Do not call ClearSelection2, Select4, Boolean, Cut, Save or
    // temporary-body Display3 during this selection callback.
    UpdateReadyState(); // implement in the owning PMP class
}
```

**This is a deliberately partial class excerpt**, NOT a complete `IPropertyManagerPage2Handler9` implementation. A working PMP class also needs page lifetime and full callbacks (use the existing Mirror implementation as a skeleton), valid native Interop references, consistent dialog state, and error handling. The source has NOT been compiled/tested. Do not assert it runs.

### Integration requirement for existing analyzer

Current `AutoFrameJointReadOnlyProbe.AnalyzeSelected(ISldWorks)` expects two generic selected objects and reads global selection order. PMP selection uses distinct marks. The **backend owner** must add an overload accepting explicit `(ISldWorks, Component2 frameA, Component2 frameB)` (or immutable occurrence snapshot) and reuse scan code, after reviewing config and component identity. Antigravity must NOT change that code.

## Ownership

**Antigravity UI task only:** submit revised **PMP design visual** with two selectors, clear statuses, Analyze action and native viewport concept. In future, create only visual layout/control specification and UI-owned styling. No backend COM/selection/cut implementation without explicit re-assignment.

**ChatGPT/Codex code-owner:** PMP object lifecycle, handler methods, component selection marks, selection-state snapshots, explicit backend adapter, temporary-body rendering and safe cleanup.

Protected: `SWAddin.cs`, `ADDIN.csproj`, `BomTaskPaneControl*`, `Commands/**`, current Mirror logic and all other command handlers. Changes to launcher/project includes are reviewed as separate commits.

## Acceptance gates

- User selects exactly one distinct Component per box and sees A/B selection highlights in Graphics Area.
- Component can be selected from FeatureManager or Graphics Area.
- Wrong document/duplicate/suppressed/unresolved Component shows a short actionable error.
- Changing selection reverts stale Preview status, but does NOT mutate model inside callback.
- On Cancel / page close, no body preview leak, no Part changes, no lingering highlight override.
- Reports are labelled estimated, no automatic Winner/Loser or Cut available at this stage.
- Native SOLIDWORKS PMP fits narrow left pane with no horizontal scrolling.
- Compile and actual SOLIDWORKS in-process checks required before calling this done.

## Verified official references
- https://help.solidworks.com/2025/English/api/sldworksapi/Create_PropertyManager_Page_Example_CSharp.htm
- https://help.solidworks.com/2024/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IPropertyManagerPageSelectionbox~Mark.html
- https://help.solidworks.com/2023/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IPropertyManagerPageSelectionbox~SetSelectionFilters.html
- https://help.solidworks.com/2021/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IPropertyManagerPageSelectionbox~SetSelectionColor.html
- https://help.solidworks.com/2025/english/api/swpublishedapi/SOLIDWORKS.Interop.swpublished~SOLIDWORKS.Interop.swpublished.IPropertyManagerPage2Handler9~OnSelectionboxListChanged.html
