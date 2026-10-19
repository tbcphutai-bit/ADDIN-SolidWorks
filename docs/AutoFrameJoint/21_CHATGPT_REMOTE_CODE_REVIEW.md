# 21 — ChatGPT code-owner review: actual GitHub code, NOT Antigravity local WIP

**Reviewed remote SHA:** `f646c2a521c3bae92b8029557785fe2e77095478`  
**Repository:** `tbcphutai-bit/ADDIN-SolidWorks`  
**Files actually read:**
- `UI/AutoFrameJoint/AutoFrameJointSelectionPage.cs` (full)
- `Commands/AutoFrameJoint/AutoFrameJointCommandEntry.cs` (full)
- `Commands/AutoFrameJoint/AutoFrameJointReadOnlyProbe.cs` (full)
- `UI/MirrorAssemblyPropertyManagerPage.cs` (reference, full)
- `ADDIN.csproj` (registration checked)
- `SWAddin.cs` and `BomTaskPaneControl.cs` (integration points inspected)

**Important:** The 88 lines modified by Antigravity on the Windows workstation are **NOT ON THE REMOTE BRANCH** as of this review. The remote still points to ChatGPT-authored `f646c2a`. These local lines were **not reviewed**. Never claim “Anti code approved” or “Anti build verified” without uploading the exact diff/commit and local logs.

## P0 — status result immediately overwritten (confirmed code bug)
`AutoFrameJointSelectionPage.PerformAnalyze()` sets `statusLabel.Caption` to analysis result, then its `finally` invokes `UpdateReadyState()`, which always overwrites `statusLabel.Caption` with `Đã chọn đúng hai Part.`.

**Required fix:** Separate selection-validity state from last analysis status. In `UpdateReadyState`, display analysis status if A/B remain the same; changing selections must invalidate old analysis. Do not call a blanket update that discards the result.

**Illustrative correction inside UI class; adapt to Anti's actual local diff once provided:**
```csharp
private string lastAnalysisMessage;
private string lastAnalyzedPairKey;

private static string PairKey(Component2 a, Component2 b)
{
    // This is ONLY a placeholder for a stable per-occurrence key.
    // Do not use Name2 alone in production: nested instances can collide.
    return a == null || b == null ? null : a.Name2 + "\n" + b.Name2;
}

private void UpdateReadyState()
{
    if (!shown || closing) return;
    Component2 a, b;
    string reason;
    bool ready = TryReadSelection(out a, out b, out reason);
    analyzeButtonControl.Enabled = ready && !handlingAnalyze;
    string pairKey = ready ? PairKey(a, b) : null;
    if (!String.Equals(lastAnalyzedPairKey, pairKey, StringComparison.Ordinal))
    {
        lastAnalysisMessage = null;
        lastAnalyzedPairKey = null;
    }
    statusLabel.Caption = ready && !String.IsNullOrEmpty(lastAnalysisMessage)
        ? lastAnalysisMessage : reason;
}

private void PerformAnalyze()
{
    // Keep existing argument validation, try/catch, and reentrancy guards.
    // After invoking onAnalyze(a,b), store result as lastAnalysisMessage,
    // store a validated occurrence identity key, then UpdateReadyState().
    // IMPORTANT: the above PairKey example is *not final production code*.
}
```
**Code-owner review requirement:** Replace the placeholder identity key with an occurrence-aware comparison or explicit selection-generation tracking. Do not copy this sketch verbatim.

## P1 — callback is too busy / COM reentrancy risk
`OnSelectionboxListChanged` immediately calls `UpdateReadyState` -> `TryReadSelection` -> `GetModelDoc2` for both components and mutates controls. This should be minimized and host-tested. Distinguish:
- lightweight validation in selection callback (counts, marks, component type only);
- full Part/config/document validation when **Analyze** is pressed;
- no rebuild, Resolve, Select4, ClearSelection2, Save, Body Boolean or Display3 from selection-list handler.

## P1 — switching active Assembly while native page exists
`AutoFrameJointSelectionPage` stores `assembly` at construction, while `AutoFrameJointReadOnlyProbe.AnalyzePair` reads `app.ActiveDoc` later. If user activates another document, the analyzer may see a different assembly than the selected owner. Validate that selected occurrences still belong to the same original assembly or fail closed when current ActiveDoc differs. Native COM identity needs a host-verified comparison.

## P1 — false success labeling
`AutoFrameJointCommandEntry.Open` maps only `RESULT: NEEDS_REVIEW` to review but overlooks `ROLE: NEEDS_REVIEW`, `EQUAL_OR_AMBIGUOUS`, and other uncertainty flags. `Đã đọc sơ bộ` is not proof that either axis is correct. Prefer a typed report DTO/status enum instead of interpreting report strings with `Contains`, then show short `Cần kiểm tra trục Frame`.

## P1 — occurrence comparison fallback
`SameOccurrence` catches `app.IsSame` failures and checks `Component2.Name2`. Name-only comparison is not strong enough in nested assemblies. On unresolved identity, fail closed and report `NEEDS_REVIEW`, or compare verified occurrence paths/transforms with appropriate host checks; do not silently assume identity.

## P2 — geometry estimation is deliberately weak
`AutoFrameJointReadOnlyProbe.Scan` groups long linear edges and projects all sampled edge endpoints to measure span. Profiles with flanges, protruding lips, different thicknesses, holes, mirrors, fillets or multi-body can yield wrong axis or length. Existing `AxisConfidence` measures edge parallel support, **not geometric truth**. Do not use any estimated length as a cutting datum, and do not use Winner/Loser for automatic cutting.

## P2 — lifecycle / error handling
`AfterClose` calls backend callbacks from the COM handler; if cleanup throws, handler exceptions can escape into SOLIDWORKS. Wrap cleanup exceptions for diagnostics without masking user cancel or abandoning release. Ensure `Show2` failure releases retained page. Test opening the Page twice and closing via Cancel/Esc.

## Build/host evidence still missing
A report claiming `csc.exe PASS` for one class is **not equivalent** to:
- full `ADDIN.csproj` Debug x64 build PASS,
- native PropertyManager opened inside SOLIDWORKS,
- A/B selection works with separate marks and nested components,
- Analyzer status persists after button callback,
- Preview body can be displayed/cleared, or
- any Frame corner cutting correctness.

## Local Anti handoff — MUST request first
STOP concurrent agent work; do not reset/stash/discard. On the exact Anti working folder:
```powershell
git rev-parse --show-toplevel
git branch --show-current
git rev-parse HEAD
git status --short --untracked-files=all
git diff -- UI/AutoFrameJoint/AutoFrameJointSelectionPage.cs
git diff --cached -- UI/AutoFrameJoint/AutoFrameJointSelectionPage.cs
```
**Deliver the exact patch to ChatGPT** as file upload or a reviewed push to a separate `review/antigravity-afj-...` branch. Prefer a working-copy backup/commit made after user approval; do not merge to main, alter Check Hole or overwrite remote feature branch.

## Review gate
- [ ] Actual 88 local lines received.
- [ ] Direct side-by-side diff review; classify each change UI-only vs COM behavior/backend.
- [ ] Correct P0 and P1 issues with concrete code changes after seeing that diff.
- [ ] Confirm complete project build output with exact command and exit code.
- [ ] Test A/B selection / errors / cancel using a **copy** of real Assembly.
- [ ] No Part changes; no Cut, no Save.
- [ ] Only then approve a follow-up integration change.
