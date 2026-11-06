# Runbook 42 — Reliable A/B readiness + read-only automatic analysis

**Code owner: ChatGPT. Antigravity: exact copy/paste into the original Visual Studio project only. NO automatic Build, Rebuild, deployment or host interaction.**

## 1. User-provided evidence and exact conclusion

The user supplied `Văn bản đã dán (1)(20261021-062153).txt`, a SOLIDWORKS Debug Output log (297 physical lines), and a screenshot where Frame A and B both display in the PropertyManager but the buttons remain disabled and the status says "Đã chọn Frame A — tiếp tục chọn Frame B."

Evidence in the new log:
- First selection: `[AFJ-SEL] List box=101 count=1`, then `[AFJ-SEL] A->B AUTO FOCUS` (lines 216–217). There is NO `List box=102` event in that first attempt, so the log does **not** prove B was committed to its selection box even though a screenshot shows a B-like label.
- After two `[AFJ-GHOST] AfterClose CLEAN` messages (lines 252 and 258), another attempt shows A, auto focus, and `[AFJ-SEL] List box=102 count=1` (lines 260–262).
- `[AFJ-READONLY]` (lines 264–292) then reports A=3200mm, B=1500mm, axes 90°, `ROLE: A = LONG / B = SHORT`. **The Analyze backend DOES work in the second run.**
- The log ends with debugger exit code `0xffffffff`; the log alone cannot identify if this is a crash, VS debug stop, or another process exit cause.
- Repeated `[MAKE HOLE SAFE]` / XTool WPF Binding messages are not attributable to Auto Frame Joint without additional evidence.

**Source issue:** The Runbook 41 `Selected(mark)` accepts `GetSelectedObjectsComponent4()` or `GetSelectedObject6() as Component2`; if SOLIDWORKS holds a face or edge object (despite a component-like display label), the returned `IEntity` could fail the direct cast, leaving readiness false. This is a defensive correction, **not a proven root cause** for the first incomplete selection. Also the Runbook 41 UX requires manually pressing the Analyze button after both picks, contrary to the user's expectation of immediate visible analysis.

## 2. Exact new C# behavior (fully authored in the source commit)

Changed ONLY `UI/AutoFrameJoint/AutoFrameJointSelectionPage.cs`.

1. Preserve existing `swSelFACES, swSelEDGES, swSelCOMPONENTS` filters, A=1/B=2 marks and one-click auto focus A → B.
2. Resolve each marked selection to the owning **Component occurrence**: first `SelectionMgr.GetSelectedObjectsComponent4(1, mark)`, then `GetSelectedObject6(1, mark) as Component2`, then `(IEntity)GetSelectedObject6(1, mark)` and `IEntity.GetComponent()`. No unmarked/global object search and no selection clearing/reselecting.
3. Log `[AFJ-SEL] READY=<true/false> A-count=<n> B-count=<n> reason=...` after the native selection callback ends, to distinguish missing B mark vs failed component resolution.
4. When `OnSelectionboxListChanged(id=102, count=1)` occurs, queue normal readiness refresh via the existing WinForms UI handle, THEN queue a **second** `BeginInvoke` to automatically invoke the existing **read-only** `PerformAnalyze()` for the valid A/B pair. Never perform B-rep/analysis inside `OnSelectionboxListChanged`, `OnSubmitSelection` or on a worker thread. This leaves the native SOLIDWORKS pick callback fast.
5. `IsPreviousPair()` and `lastAnalysisMessage` stop duplicate scans of the unchanged pair. User can still press **Phân tích** manually to rerun. The **Xem trạng thái A/B** (Ghost preview) button still requires an explicit click. No cut, save, part edit, or automatic Ghost rendering.
6. On `AfterClose`/failed `Show2`, dispose the queued UI window and invalidate flags. Original Ghost session cleanup remains unchanged.

## 3. Anti's EXACT direct-to-class task

**New code branch:** `fix/afj-ready-selection-auto-analysis-20261021`.  
**Old code owner base:** `13bc7d42ea36cc8420f6083b3f53b77b97395cb5`.

**Target:** `C:\Tao moi\ADDIN\UI\AutoFrameJoint\AutoFrameJointSelectionPage.cs`.

Before editing:
```powershell
cd 'C:\Tao moi\ADDIN'
git status -sb
git rev-parse HEAD
git hash-object UI/AutoFrameJoint/AutoFrameJointSelectionPage.cs
```

**Expected OLD blob SHA:** `f669836408286742ac60cd1378595fec75578b09`.
If the hash differs, STOP and report exact local source differences. Do NOT overwrite another agent's work. If old SHA matches, copy the **entire class source file** EXACTLY from the new ChatGPT-owned GitHub commit; do not independently reconstruct code.

Compare NEW local `git hash-object` with the exact new blob reported by GitHub. Do not continue on mismatch.

Keep `ADDIN.csproj` unchanged: Debug x64 `<OutputPath>C:\Tao moi\</OutputPath>`, empty `PreBuildEvent` and `PostBuildEvent`. Do not edit `BomTaskPaneControl.cs`, AutoFrameJoint backend, Ghost engine, other commands or Designer.

Report old/new class hashes, path, git diff, source warnings and exact local project HEAD. **NO automatic Build/Rebuild/F5, no DLL/PDB writes, no running/closing SOLIDWORKS, no merging, no Git commit to CG2 branch unless authorized.**

## 4. Manual test criteria after later user-approved Build

The user-controlled Build to `C:\Tao moi\` overwrites installed DLL/PDB directly; save work, end any debug session and back up both outputs before building.

Test on a copy of Assembly:
1. Click face of A once: A receives name; automatic focus B; `[AFJ-SEL] List box=101 count=1`.
2. Click face of B once: B receives a name; new `[AFJ-SEL] READY=True A-count=1 B-count=1`; Analyze and View A/B enable. **Without clicking Analyze, read-only automatic analysis produces a short result**, logs `AUTO ANALYZE START`/END and full `[AFJ-READONLY]` report.
3. If B label is visible but buttons disabled, capture `[AFJ-SEL] READY=False A-count=? B-count=?`. A count of 1 and B count 0 proves the SelectionMgr B mark is missing; counts 1/1 with ready false suggests component resolution, duplicate or active assembly identity issue.
4. Change B or A: new pair must cause another read-only auto analysis (not reuse stale message).
5. Duplicate A/B occurrence must be rejected; no false readiness.
6. Click Xem trạng thái A/B; Cancel; Ghost cleanup must still be complete.
7. Record any `Defer UI failed`, `AUTO ANALYZE NOT READY`, COM exceptions, freezes, wrong frame name or repeated analysis.
8. Note timing: heavy geometry analysis may briefly pause the UI after the pick. If noticed, report the exact delay rather than assuming the source is perfect.

**Status:** source patch only, uncompiled/unverified in SOLIDWORKS. Log validates previous analysis but does not prove why B was not accepted in the screenshot.
