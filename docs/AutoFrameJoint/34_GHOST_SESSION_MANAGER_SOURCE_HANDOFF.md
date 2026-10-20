# 34 — Ghost Session Manager: source delivered, build postponed

**Owner:** ChatGPT (backend). **Current phase:** CODE ONLY, no build, no DLL deploy, no SOLIDWORKS test, no merge.

Source branch: `feat/afj-ghost-session-owner-source-only-20261020`  
Base commit: `ecf9e6e24b978cf7cf5200189adab4874b4029e3`

## 1. Scope and actual files

New C# (complete implementation, not pseudocode):

`Commands/AutoFrameJoint/AutoFrameJointGhostSessionManager.cs`

Existing `ADDIN.csproj` gets exactly one new compile entry:
```xml
<Compile Include="Commands\AutoFrameJoint\AutoFrameJointGhostSessionManager.cs" />
```

Existing `Commands/AutoFrameJoint/AutoFrameJointGhostPreview.cs` remains the same as commit `ecf9e6e`. It already has the P0 safety fix (body ownership BEFORE Display3), Hidden/Suppressed checks, status wording, and best-effort Hide/Redraw.

**NOT CHANGED:** `AutoFrameJointCommandEntry.cs`, native PMP `AutoFrameJointSelectionPage.cs`, `SWAddin.cs`, TaskPane code, Check Hole, Bend, Mirror, source CAD. Existing OutputPath remains `bin\x64\Debug\`, and Pre/PostBuild event are empty.

## 2. Why this manager exists

Avoid a single replaceable `static activeGhost` field that can become an orphaned COM-body owner on the next command run.

Instead, there is ONE permanent `AutoFrameJointGhostSessionManager.Shared` coordinator. It captures and retains a single current session that contains:
- `Guid Token` per page invocation to reject stale callbacks.
- `ISldWorks App` and `ModelDoc2 Assembly` COM references for the exact source Assembly.
- `AutoFrameJointGhostPreview Ghost` retaining `Body2` and original `ModelDoc2 Part` references until Hide finishes.
- `UiThreadId` for synchronous UI-thread callback validation; no Timer/Task/thread pool.
- `SessionState`: `Ready`, `Showing`, `PendingCleanup`, `DeferredDocument`, `Closed`.
- `CloseRequested` distinguishes active PMP vs AfterClose with pending Hide failures.

**Strong-reference guarantee is LIMITED**: the manager will not intentionally discard a session with tracked bodies. It does not prove `Hide` always works, graphics redraw completes, or process crashes cannot lose temporary state.

## 3. Complete public surface (already coded)

```csharp
AutoFrameJointGhostSessionManager.Shared;
bool TryBegin(ISldWorks app, ModelDoc2 assembly, out Guid token, out string message);
bool TryShow(Guid token, Component2 frameA, Component2 frameB, out string message);
bool TryClear(Guid token, out string message);
bool CloseFromPage(Guid token, out string message);
bool RetryClosedCleanup(out string message);
bool HasSession { get; }
bool HasPendingBodies { get; }
SessionState? CurrentState { get; }
```

### Expected transitions

| Event | Initial | Result |
|---|---|---|
| `TryBegin` with current active Assembly and no session | none | Ready + new GUID |
| `TryBegin` while any old session still owns a PMP/body | any live state | Reject, do not overwrite |
| `TryShow` with correct token/Assembly | Ready/Showing | Showing on success |
| `TryShow` partial COM error, Hide fails | Ready/Showing | PendingCleanup + retain Ghost |
| `TryClear` (manual reset) when original Assembly active | Showing | Ready if all Hide complete |
| User changed ActiveDoc before cleanup | Showing | DeferredDocument, do NOT ActivateDoc |
| `CloseFromPage` with no pending bodies | Ready | Closed + release session |
| `CloseFromPage` while Hide fails | Showing | PendingCleanup + retain session |
| User explicitly returns to original Assembly and calls `RetryClosedCleanup` | PendingCleanup/DeferredDocument | Closed if Hide works; otherwise keep |
| Any callback from a stale GUID or wrong thread | any | Reject, do not touch another session |

## 4. CRITICAL: integration plan only — NOT applied in this branch

The original PMP has not yet passed real host smoke. **Do not connect new manager from this commit.** After success of standalone Debug x64 build and base PMP host test, ChatGPT will author a separate UI/backend integration commit.

Planned command entry pattern (future code, NOT currently in `AutoFrameJointCommandEntry.cs`):

```csharp
Guid pageToken;
string status;
var owner = AutoFrameJointGhostSessionManager.Shared;
if (!owner.TryBegin(app, sourceAssembly, out pageToken, out status))
    throw new InvalidOperationException(status);

// future UI callback for EXPLICIT new Preview button ONLY:
Func<Component2, Component2, string> showGhost = (a, b) =>
{
    string result;
    owner.TryShow(pageToken, a, b, out result);
    return result;
};

// reuse the PMP's existing AfterClose cleanup callback:
Action clearAfterClose = () =>
{
    string result;
    bool ok = owner.CloseFromPage(pageToken, out result);
    if (!ok) Trace.WriteLine("[AFJ-SESSION] " + result);
};
```

Important: if page construction or `page.Show()` throws AFTER a session begins, the command-entry catch must call `CloseFromPage(pageToken, ...)` and retain the manager if Hide fails. Do not lose the `pageToken` or owner before the fallback.

**Native UI on the next, separately reviewed patch:**
- Add one new button ID 105 `Xem trạng thái A/B`; keep IDs 101-104 and selection marks 1/2.
- Add a new `Func<Component2,Component2,string>` preview callback alongside existing onAnalyze; do not swap responsibilities.
- Invoke only from `OnButtonPress(105)`, after `TryReadSelection(..., true)`.
- Never call `Display3` or `Hide` in `OnSelectionboxListChanged`.
- Keep the text `Chưa mô phỏng vết cắt mới`; no Cut features, boolean ops, Save or Mate.
- When A/B selection changes, no automatic COM render calls. If user presses preview again, manager/engine clears the previous copy while the original Assembly is active.

## 5. Antigravity action NOW — source review only

1. Switch/read an **isolated worktree** on branch above, preserving user/other command changes.
2. Read `Commands/AutoFrameJoint/AutoFrameJointGhostSessionManager.cs` completely, and `AutoFrameJointGhostPreview.cs`, `AutoFrameJointCommandEntry.cs`, `AutoFrameJointSelectionPage.cs`.
3. Check every public signature, C# 7.3 syntax, nested state ownership, token and UI-thread guard, cleanup handling in mismatched documents and caught COM exceptions. Report exact offending line + correction; do not silently rewrite code.
4. Check `ADDIN.csproj` includes new manager exactly once, and no auto postbuild DLL copy/force-close.
5. **DO NOT** run Build/Rebuild/csc/MSBuild, Visual Studio F5, SOLIDWORKS, COM regasm, dll copy/deploy/restore, merge, or runtime preview tests. Another class owns that SOLIDWORKS debugging session.
6. Post a GitHub report-only commit with `PASS STATIC`, `RISK`, `NOT TESTED` per scenario, exact branch/commit, changed files. Do not claim 100% leak-free or SOLIDWORKS Host PASS.

## 6. Conditions before any user Rebuild later

- User confirms the competing SOLIDWORKS debugging is finished.
- In *this isolated worktree*, confirm HEAD and check clean source.
- Baseline SHA256 of installed `C:\Tao moi\ADDIN.dll` **and** `ADDIN.pdb` saved.
- Build **Debug | x64** using the safe `ADDIN.csproj`, output `bin\x64\Debug\`; no F5, no registration, no DLL copy.
- Confirm installed pair SHA256 unchanged BEFORE/AFTER; if changed, stop and investigate an unrelated writer.
- Resolve compiler errors only in a follow-up code-owner-reviewed commit.
- Native PMP A/B host smoke (on copied Assembly) must pass **before** adding Preview button integration.
- Only then validate ghost render/Hide on rotated/transformed/hidden/suppressed/multi-body components in real SOLIDWORKS.

## 7. Explicit known limitations

- Manager is **present in source and compiled only after future Build**. It is not instantiated by the current `AutoFrameJointCommandEntry` and is not visible to end users.
- User can open only ONE preview session at a time. If cleanup fails, another session is intentionally blocked until manual retry with the bound Assembly active.
- `Thread.ManagedThreadId` is a guard against calling from a different managed thread, not proof of all COM apartment correctness.
- A try/catch cannot ensure Hide succeeds after closed/invalid Part document, nor can it survive process shutdown.
- Another agent worktree using the older `ADDIN.csproj` can still overwrite installed DLL if its build is run. Coordinate worktrees.
- The geometry currently shown is an overlay of *current* Frame bodies, **not** the user's profile-dependent miter cut.
