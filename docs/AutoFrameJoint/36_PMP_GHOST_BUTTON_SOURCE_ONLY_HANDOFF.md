# 36 — Native PMP Ghost Preview button: complete source-only integration

**AUTHOR/DECISION MAKER: ChatGPT (C# CODE OWNER)**  
**IMPLEMENTER: Antigravity must COPY/PASTE the EXACT code already committed. No backend analysis/rewrites.**  
**BUILD: FORBIDDEN at this stage by user; another command is debugging SOLIDWORKS.**

Repository: `tbcphutai-bit/ADDIN-SolidWorks`  
Source branch: `feat/afj-pmp-ghost-button-source-only-20261020`  
Base commit: `18af6671762715593e0a82702fe119cc65fcf1ce`.

## 1. EXACT two source files written by ChatGPT

1. **EDIT** `UI/AutoFrameJoint/AutoFrameJointSelectionPage.cs`:
   - New PMP button **ID 105** `ButtonShowBodies`, label **Xem trạng thái A/B**.
   - Preserve IDs `101` Frame A, `102` Frame B, `103` Analyze, `104` Status; preserve marks `1` and `2`.
   - New separate callback `Func<Component2, Component2, string> onShowBodies` injected via constructor **after** `onAnalyze`.
   - Button is disabled until the existing shallow `TryReadSelection(..., false)` says the pair is valid.
   - Clicking ID 105 calls `PerformShowBodies()`, which validates `TryReadSelection(..., true)` and calls `onShowBodies(a,b)` only on explicit click.
   - `handlingShowBodies` avoids overlapping synchronous Analyze and Preview execution.
   - Reuses existing pair-scoped text status cache. If either selected occurrence changes, the old result text is invalidated; pressing the button again updates the overlay. **Note**: selection changes do NOT hide the old Ghost automatically; the backend refreshes it on the next explicit button press. The static explanatory UI label must make clear this is not the proposed cut.
   - `AfterClose` continues to call backend `onClearPreview` first, then `onClosed`, dropping the button reference. No `Display3` in selection-change handlers.
   - Information label explicitly says **Xem trạng thái A/B: hình học hiện tại, chưa có vết cắt mới**.

2. **EDIT** `Commands/AutoFrameJoint/AutoFrameJointCommandEntry.cs`:
   - Capture the correct active source Assembly and start a **unique tokenized session** via `AutoFrameJointGhostSessionManager.Shared.TryBegin(app, sourceAssembly, out token, out message)`.
   - Preserve the existing `AutoFrameJointReadOnlyProbe.AnalyzePair(...)` analysis callback. Do NOT change detection of long/short Frame.
   - New `onShowBodies` callback calls `manager.TryShow(pageToken, frameA, frameB, out status)` ONLY after the UI validates A/B and user clicks.
   - Existing `onClearPreview` callback now calls `manager.CloseFromPage(pageToken, out status)` during PMP `AfterClose`. Failed Hide only logs `PENDING`; the persistent `Shared` manager retains COM body references.
   - A command setup or `Show` exception triggers fallback `CloseFromPage` if `AfterClose` has not run. Never replace/force-discard a pending session.
   - It does not run Save, EditPart, Cut, Transform, Mate, Resolve, suppress, hide or change geometry.
   - Other sessions are blocked while one current session has not closed; user must return to bound Assembly for manual cleanup retry.

**NO changes** to `AutoFrameJointGhostPreview.cs`, `AutoFrameJointGhostSessionManager.cs`, `AutoFrameJointReadOnlyProbe.cs`, `SWAddin.cs`, TaskPane, `ADDIN.csproj`, Check Hole, Check Bend, Mirror, CNC features or other commands.

## 2. Exact Antigravity COPY/PASTE instructions

1. In a **separate isolated worktree**, open `feat/afj-pmp-ghost-button-source-only-20261020`, the precise approved commit. Do NOT checkout over other agent's uncommitted edits or files.
2. Open the two files above. **Use their complete committed contents as the source of truth**. Replace only those two files in your development worktree. Do not auto-generate new code or selectively reinterpret pseudocode.
3. Perform read-only checks:
```powershell
git rev-parse HEAD
git status --short
git hash-object "UI/AutoFrameJoint/AutoFrameJointSelectionPage.cs"
git hash-object "Commands/AutoFrameJoint/AutoFrameJointCommandEntry.cs"
git hash-object "Commands/AutoFrameJoint/AutoFrameJointGhostSessionManager.cs"
git hash-object "Commands/AutoFrameJoint/AutoFrameJointGhostPreview.cs"
git hash-object "ADDIN.csproj"
```
4. Compare local file hashes against exact Git blob SHA values in the ChatGPT handoff. Any mismatch: STOP; report the whole diff without “fixing” backend independently.
5. Check only by source-reading that `AutoFrameJointSelectionPage` new constructor parameter matches the corresponding `new AutoFrameJointSelectionPage(...)` call in `AutoFrameJointCommandEntry`.
6. Check `OnButtonPress(105)` routes to `PerformShowBodies` and `OnSelectionboxListChanged` does NOT call any geometry/display/cleanup API.
7. Check `AfterClose` still invokes `onClearPreview()` before `onClosed`, and CommandEntry calls `CloseFromPage(pageToken)`.
8. Report file hashes, unchanged other-command files, and any suspicious C# / SOLIDWORKS Interop signature by exact source line. NO backend editing.

**ABSOLUTE HOLD:** No Build/Rebuild/csc/MSBuild, Visual Studio F5, running/stopping SOLIDWORKS, COM registration, DLL/PDB copy/Deploy/Restore or branch merge. The user's other class still uses SOLIDWORKS for debugging.

## 3. UX currently staged

The native PMP (not a static HTML preview) will show:

```text
AUTO FRAME JOINT
┌─ Chọn hai Frame ───────────────────────────────┐
│ Frame A [selected Part occurrence, mark 1]     │
│ Frame B [selected Part occurrence, mark 2]     │
│ [Phân tích]                                     │
│ [Xem trạng thái A/B]  (disabled until 2 valid)  │
│ Status: hình hiện tại; chưa mô phỏng vết cắt     │
└────────────────────────────────────────────────┘
┌─ Thông tin ────────────────────────────────────┐
│ Chưa mô phỏng vết cắt mới                       │
└────────────────────────────────────────────────┘
[Cancel]
```

**Preview button is NOT the actual Miter/Cut result!** It displays Temporary Body copies of the **current** A and B bodies at their existing assembly locations. When the user changes A/B, previous overlays may remain until they press Preview again or Cancel. Do not claim ghost overlays change immediately at selection change.

## 4. Deferred Build / Host Smoke (only after user ends other debug)

1. Baseline hash `C:\Tao moi\ADDIN.dll` and `ADDIN.pdb`. Use isolated worktree with safe `bin\x64\Debug\` output and no Pre/PostBuild auto-copy.
2. Rebuild Debug x64 and check compiler diagnostics. Confirm installed DLL/PDB hashes unchanged. **No F5 and no deploy yet.**
3. On user-approved copied/test Assembly, host-test original Analyze and two selected Components first; then explicit Ghost Preview.
4. First preview: 2 colors, correct transformed/mirrored Frame positions, no model feature change.
5. Change selection to another Frame: UI status must not claim current preview is an updated cut; explicit button refreshes overlay.
6. Preview twice; confirm previous Ghost bodies hidden before new render.
7. Cancel/PMP AfterClose; verify no lingering temporary bodies. Force a controlled Hide error in test environment; verify session is retained and a new PMP is blocked until safe cleanup.
8. Switch ActiveDoc while PMP open/ghost displayed: no ActivateDoc. Cleanup defers and manager retains reference.
9. One valid Part and one invalid/nested/hidden/suppressed/lightweight Part should fail safely.
10. Confirm source Assembly Feature Tree, Mate definitions, Save/dirty state untouched. Capture raw Host/Interop errors and exact DLL/PDB hashes.
11. Do NOT enable real profile-dependent cut or Save; first build/host acceptance gate is **read-only ghost visualization**.

## 5. Known remaining concerns (CODE OWNER must review later)

- Another worktree with old ADDIN.csproj can still overwrite installed DLL; all agents must adopt isolated-output policy before running builds.
- A source document that is closed while body cleanup fails may require an explicit operator recovery process. NEVER infer “closed” from “not active” and NEVER use `ForceDiscard`.
- No actual user-accessible manual retry button exists in this patch. `RetryClosedCleanup` exists on the manager and must be exposed in a separate future UI/host-reviewed command if needed.
- The SOLIDWORKS API may impose rendering/thread/configuration constraints beyond static checks. “Source present” ≠ “host tested”.
- The precise on-screen geometry of component-context `Display3` must be validated before any claim of correct placement.

**Status:** complete source staged; **NOT BUILT, NOT DEPLOYED, NOT RUNTIME TESTED.**
