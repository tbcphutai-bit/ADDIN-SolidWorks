# 19 — ANTIGRAVITY COPY-READY PACKAGE — Auto Frame Joint Native PMP

**User decision:** Use native SOLIDWORKS PropertyManager **ONLY to select Frame A and Frame B**. View 3D directly in the SOLIDWORKS Graphics Area. Do not build the old 3-tab WinForms wizard.

**Baseline branch:** `feat/auto-frame-joint-pmp-ui-copyready-20261019`.  
**Source file written by ChatGPT:** `UI/AutoFrameJoint/AutoFrameJointSelectionPage.cs`.  
**Status:** full C# PMP selection class with all handler methods written, **NOT compiled or run inside SOLIDWORKS yet**.

## 1. The user workflow (minimal text)
- Open Assembly -> launch Auto Frame Joint.
- Click **Frame A**, select one Part Component in FeatureManager or graphics.
- Click **Frame B**, select another Part Component.
- Native PMP only shows `Chọn hai Frame`, two selection boxes, one **Phân tích** button, one short status line.
- Button is enabled only when two distinct loaded Part occurrences are selected.
- Click `Phân tích`: UI passes explicit `Component2 A, Component2 B` to a **backend-owned** callback. Without backend hookup, it honestly says not connected.
- Preview, when implemented by code owner, is **temporary 3D geometry in SOLIDWORKS viewport**, not a fake HTML thumbnail; no Cut or Apply in current phase.
- Close/Cancel: backend cleanup callback clears any temporary bodies and lifetime reference.

## 2. Exact file Antigravity should copy

Do NOT rewrite the handler or generate speculative SOLIDWORKS API signatures.

- Source: `UI/AutoFrameJoint/AutoFrameJointSelectionPage.cs` in this branch (already authored by ChatGPT).
- Destination in user's active UI branch: **exact same path** `UI/AutoFrameJoint/AutoFrameJointSelectionPage.cs`.
- Namespace: `ADDIN.UI.AutoFrameJoint`.
- Class: `AutoFrameJointSelectionPage : PropertyManagerPage2Handler9`.
- Target: .NET Framework 4.8, C# 7.3, Windows x64.
- References already in main Add-in: `SolidWorks.Interop.sldworks`, `SolidWorks.Interop.swconst`, `SolidWorks.Interop.swpublished`.

**CRITICAL:** The file being in Git does not automatically include it in old-style `ADDIN.csproj`. Integration of `<Compile Include="UI\AutoFrameJoint\AutoFrameJointSelectionPage.cs" />` belongs to code owner, NOT Anti, and is a separate reviewed change. Antigravity MUST NOT modify project, backend, SWAddin or TaskPane launcher.

## 3. Class contract / provided constructor

```csharp
public AutoFrameJointSelectionPage(
    ISldWorks app,
    Func<Component2, Component2, string> onAnalyze,
    Action onClearPreview,
    Action<AutoFrameJointSelectionPage> onClosed)
```

- `onAnalyze(a,b)` receives **two distinct Part instances with marks 1 and 2**.
- Callback may return only a short read-only message; NO Cut invocation until solver approved.
- `onClearPreview()` backend callback runs **after page closes**, not from inside selection-list callback.
- `onClosed(page)` lets command owner clear the retained reference to handler (prevents premature garbage collection while SOLIDWORKS displays it).
- Page is created with **Cancel** only, because `Phân tích` is an in-page button; there is no Apply Cut or model mutation.
- Selection boxes filter only `swSelCOMPONENTS`; Face/Edge selection is **not supported by this first UI class**, unless backend scope is revised with user consent.
- Selected Part with `GetModelDoc2()==null` reports `hãy Resolve Component` (UI never silently resolves components).

## 4. Code-owner ONLY — example launcher pattern (do not paste into existing backend without review)

```csharp
// Illustration of ownership / dependency injection, not compiled against current source.
// Use real ISldWorks field (already maintained by the Add-in). Retain page lifetime.
private AutoFrameJointSelectionPage autoFrameJointPage;

private void OpenAutoFrameJointSelector(ISldWorks app)
{
    if (autoFrameJointPage != null && autoFrameJointPage.IsOpen)
        return;

    autoFrameJointPage = new AutoFrameJointSelectionPage(
        app,
        (a, b) =>
        {
            // The actual existing AutoFrameJointReadOnlyProbe.AnalyzeSelected(app)
            // is NOT compatible: it reads unmarked global selection.
            // Backend owner must implement AnalyzePair(app, a, b) first.
            return "Đã chọn " + a.Name2 + " và " + b.Name2
                 + ". Chưa kết nối bộ phân tích góc.";
        },
        () =>
        {
            // Future backend: clear temporary bodies, hide ghost and release.
            // Current phase has none. No Part modifications allowed.
        },
        page =>
        {
            if (Object.ReferenceEquals(autoFrameJointPage, page))
                autoFrameJointPage = null;
        });

    autoFrameJointPage.Show();
}
```

The launcher snippet belongs to code owner and is a **compilation draft**, not a verified integration. Change the actual owner class, namespace and lifetime only after reviewing current SWAddin structure. Do not request Anti to implement `AnalyzePair`.

## 5. Why the existing analyzer cannot be wired blindly

`Commands/AutoFrameJoint/AutoFrameJointReadOnlyProbe.cs` currently exposes `AnalyzeSelected(ISldWorks)`, reading `GetSelectedObjectCount2(-1)` and unmarked selection order. The new PMP has **two different marks** (1 = A, 2 = B).

Backend owner must add an overload `AnalyzePair(ISldWorks app, Component2 a, Component2 b)` and reuse read-only `Scan` on those explicit components, rather than relying on the global selection collection. Without this fix, UI must show `Chưa kết nối` and **cannot pretend to analyze**.

## 6. Antigravity execution instructions — exact order

**STEP 0 / SAFETY**: Stop previous Copilot/Anti work on same files. Audit the local worktree using `15_COPILOT_AUDIT_ANTIGRAVITY_HANDOFF.md`. Never reset, discard uncommitted changes or overwrite Check Hole.

**STEP 1 / READ**: Read this entire package, `18_PMP_SELECTION_ONLY.md`, the copy-ready C# class, plus `UI/MirrorAssemblyPropertyManagerPage.cs` for callback conventions.

**STEP 2 / COPY**: Put the C# class at `UI/AutoFrameJoint/AutoFrameJointSelectionPage.cs`. If already present with same contents, do not paste a duplicate class. Use namespace and all method signatures from source. Do not create a Designer.cs; this is native PropertyManager, NOT a WinForms Form.

**STEP 3 / NO EXTRA CHANGES**: Do not alter `ADDIN.csproj`, `SWAddin.cs`, `BomTaskPaneControl*`, `Commands/**`, old Mirror class, existing event handlers, or any unrelated file.

**STEP 4 / REVIEW**: Show `git status --short`, `git diff --check`, `git diff --stat`. Summarize all new methods and verify no backend touched. If build is blocked because class is intentionally excluded from production csproj, state `NOT_COMPILED_NOT_IN_PROJECT`, not PASS.

**STEP 5 / HANDOFF**: Prepare `docs/AutoFrameJoint/ANTIGRAVITY_PMP_HANDOFF.md` (locally first) containing project/branch/SHA, exact C# file path, class name, controls/marks, source diffs, and code-owner integration tasks.

**STEP 6 / USER REVIEW**: Present a Native PMP visual mockup and status details (A selected, B selected, duplicate, unresolved, neither). Wait for user acceptance. Do not commit/push without confirmation.

## 7. Acceptance checklist (must verify in real SOLIDWORKS after separate integration)

- [ ] Native PMP shows Frame A and B **separately** and remains visible while selecting in graphics.
- [ ] One Component in each selector; cannot select same occurrence twice.
- [ ] Can select from Assembly tree and by clicking allowed Part Component in viewport.
- [ ] Part vs Subassembly validated; lightweight requires manual Resolve.
- [ ] Analyze remains disabled until both distinct loaded Part Components chosen.
- [ ] Clicking Analyze delivers A and B explicitly to backend callback.
- [ ] Selection callback only reads status; no Boolean/Cut/Model changes.
- [ ] Cancel invokes cleanup and drops retained page reference.
- [ ] No 3-tab WinForms, no unimplemented or fake 3D cut preview.
- [ ] Build in actual Visual Studio x64 succeeds AFTER reviewed project integration; no claim from screenshots alone.

## 8. Detailed phases and responsibility
| Phase | Implementer | Result |
|---|---|---|
| PMP selection visual + isolated UI class | Antigravity using ChatGPT-authored code | UI-only handoff |
| Integrate PMP into Add-in, explicit selection analyzer | ChatGPT/code owner | Read-only first in-host demo |
| Temporary body ghost preview | ChatGPT/code owner | Actual transparent preview in SOLIDWORKS Graphics Area |
| Frame Profile solver (Web, Wall, Lip, Bend) | ChatGPT/code owner | Proposed cutting bodies based on verified rules |
| Apply Cut | Code owner, later approval | Actual geometry changes only after safe Preview/validation |

## 9. Official API references
- SOLIDWORKS PropertyManager sample: https://help.solidworks.com/2019/english/api/sldworksapi/create_propertymanager_page_example_csharp.htm
- IPropertyManagerPage2Handler9: https://help.solidworks.com/2024/english/api/swpublishedapi/SolidWorks.Interop.swpublished~SolidWorks.Interop.swpublished.IPropertyManagerPage2Handler9_members.html
- Selection List changed caution: https://help.solidworks.com/2025/english/api/swpublishedapi/SOLIDWORKS.Interop.swpublished~SOLIDWORKS.Interop.swpublished.IPropertyManagerPage2Handler9~OnSelectionboxListChanged.html
- Label Caption: https://help.solidworks.com/2023/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IPropertyManagerPageLabel~Caption.html
- Control Enabled: https://help.solidworks.com/2025/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IPropertyManagerPageControl~Enabled.html
