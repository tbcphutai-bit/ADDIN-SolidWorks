# 35 — ChatGPT review of Antigravity Session Manager and safe recovery diagnostics

**Source-only; user postponed Rebuild.** No MSBuild, no SOLIDWORKS start/stop, no DLL or PDB copy, no Deploy/Restore, no COM registration or merge.

## Exact review evidence

- Antigravity review at `review/afj-ghost-session-manager-review-20261020`, commit `e85e28dd0082be69281846824bdd751f180212f4`.
- Original source at `ff19e37dee5f2350b6ceb83918e39bf51873aca5`.
- Diff `ff19e37..e85e28d` contains only one new Markdown review file; Antigravity made no C# changes.
- Reviewed `Commands/AutoFrameJoint/AutoFrameJointGhostSessionManager.cs` and `AutoFrameJointGhostPreview.cs`, `CommandEntry`, and `ADDIN.csproj`. This review is static only.

## IMPORTANT: rejecting the proposed ForceDiscard implementation

Antigravity suggested:
```csharp
if (current.CloseRequested &&
    !IsBoundAssemblyActive(current.App, current.Assembly))
{
    current.State = SessionState.Closed;
    current = null;
}
```
**Do not implement this.** `IsBoundAssemblyActive` tests whether `app.ActiveDoc` is the specific Assembly, **not whether the bound Assembly is closed**. It also returns false when user switches to another still-open Assembly or COM identity temporarily cannot be read.

Such a branch might discard the **only** managed chain keeping potentially displayed `Body2` copies and their Part contexts alive, exactly the invariant this manager is designed to protect. Even independently determining a document is closed would not prove SOLIDWORKS disposed every temporary body safely. No unconditional/force disposal is acceptable at this stage.

If the source document has actually closed while Hide is pending, the current fail-closed design intentionally blocks another Ghost session; the safe operational path is to save CAD work, diagnose the specific host instance and if recovery is impossible restart SOLIDWORKS deliberately. Do **not** automatically close SOLIDWORKS or pretend an orphaned body was cleaned.

## Real C# code added by ChatGPT in this new branch

File: `Commands/AutoFrameJoint/AutoFrameJointGhostSessionManager.cs`.

**New method, already implemented in full:**
```csharp
public bool TryDescribeRecovery(out string message)
```

It is a **read-only diagnostic**, not a cleanup API:
1. When no session: reports that there is nothing pending.
2. Rejects wrong managed UI-thread calls before making SOLIDWORKS COM calls.
3. Checks active bound Assembly identity (only), `Ghost.IsShowing` (tracked references, not guaranteed visible), and `CloseRequested`.
4. When there are tracked bodies but source Assembly isn't active: explains that inactivity **does not imply document closure**, and does not discard.
5. When page has closed and bodies remain: directs a future UI to the already-implemented `RetryClosedCleanup` method on the bound Assembly and owning thread.
6. When active page owns bodies: directs a future UI to the correct token's `TryClear`.
7. Never calls `Hide`, `Display3`, `ActivateDoc`, `current = null`, or any model-writing APIs.

Return `true` means diagnostics were obtained, **not successful cleanup**. `false` means caller is on the wrong thread. These messages are designed for future Manual Recovery UI and Debug Output, NOT presently displayed in the running PropertyManager.

**UNCHANGED:** `AutoFrameJointGhostPreview.cs`, `AutoFrameJointCommandEntry.cs`, `AutoFrameJointSelectionPage.cs`, `SWAddin.cs`, `BomTaskPaneControl.cs`, all other commands, `ADDIN.csproj`, installed DLLs.

## Antigravity's source review: what is justified and what is not

| Antigravity statement | ChatGPT finding |
|---|---|
| Shared manager retains a reference chain while `Ghost.IsShowing` | Correct, within same running process and as long as no external unsound mutation |
| GUID token rejects stale callbacks | Correct in public token-gated APIs; `RetryClosedCleanup` is deliberately owner-only and does not use a page token |
| Initial STA check and later managed-thread checks | They restrict calls, but do NOT prove all COM threading/reentrancy circumstances safe at runtime |
| `FinishEmpty` avoids discard while bodies tracked | Correct for manager state; `Hide` success cannot be guaranteed |
| Every API compiles and every host behavior is correct | NOT VERIFIED without Build and SOLIDWORKS host |
| ForceDiscard when source isn't active | **UNSAFE / REJECTED** |

## Next steps for Antigravity — exact task

1. Checkout a new clean isolated worktree at `fix/afj-ghost-session-safe-recovery-diagnostics-20261020`.
2. Read the full `AutoFrameJointGhostSessionManager.cs` and this document; verify the `TryDescribeRecovery` method and that source C# still has NO `ForceDiscardSessionIfDocumentClosed`.
3. Review the possible `TryDescribeRecovery` states with mocked event sequences **on paper only**, not compiled; note that `active` means **currently active**, never “is open”.
4. Report exact C# line number, API issue, unsafe wording or missing branch. Do not auto-edit backend; let ChatGPT decide corrections.
5. Check `ADDIN.csproj` stays safe (Debug x64 output relative, Pre/PostBuild empty).
6. Do not Build/Rebuild/csc, start/stop SOLIDWORKS or VS, deploy/register DLL, run CAD or merge.
7. Send review-only report and commit for ChatGPT direct inspection.

## Deferred integration and host validation

After the user releases SOLIDWORKS debugging:
1. Isolated Debug x64 Rebuild, validate installed DLL/PDB hashes before/after unchanged.
2. Original native PMP selection-only host smoke: pick two distinct Part occurrences, Analyze, Cancel, confirm no cut.
3. NEW reviewed wiring commit to introduce explicit `Xem trạng thái A/B` button; only its callback calls `manager.TryShow(token,...)`.
4. `AfterClose` calls `CloseFromPage` and must report/log pending cleanup without discarding the manager. No Hide/Display in selection-change events.
5. Future user-triggered Recovery UI invokes `TryDescribeRecovery` on SOLIDWORKS UI thread, then explicit `RetryClosedCleanup` if appropriate.
6. Test source Assembly changed/inactive, actually closed, missing Part, rotated/mirrored and multi-body Frame, Hide failure, preview twice, stale token and wrong managed thread. No saving or changing original geometry.

**Status:** Code exists on review branch; no runtime results yet. There is still NO user-visible 3D Ghost Preview button and NO profile-specific cutting.
