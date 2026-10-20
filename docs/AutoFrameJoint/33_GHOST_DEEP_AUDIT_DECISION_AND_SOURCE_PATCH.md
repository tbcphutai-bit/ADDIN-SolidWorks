# 33 — ChatGPT review of Antigravity Deep Audit: source-only correction and lifetime design gate

## Ground truth
- Anti Deep Audit: `review/afj-ghost-p0-deep-audit-20261020` at `1d1fa2fffe10abded24a316146134592c21a13b9`
- Actual Antigravity delta vs `bf5e21a`: **ONE markdown report only**, no backend code, no compiled output.
- Source under review: `Commands/AutoFrameJoint/AutoFrameJointGhostPreview.cs` (was 282 lines).
- Other files read: `AutoFrameJointCommandEntry.cs`, `AutoFrameJointSelectionPage.cs`, `ADDIN.csproj`.
- **This branch:** `fix/afj-ghost-current-state-visibility-20261020`.
- Another command still uses SOLIDWORKS for debugging: **SOURCE ONLY; no build, rebuild, csc, deploy, DLL copy, registry, COM host test or merge**.

## Code-owner verdict by proposal

| Anti recommendation | Code-owner verdict | Actual action |
|---|---|---|
| Accurate preview status wording | ACCEPT | Patched GhostPreview.cs |
| Better pending Hide failure message | ACCEPT | Patched GhostPreview.cs |
| Reject hidden/suppressed Component occurrences | ACCEPT with precise IsHidden(false) semantics | Patched GhostPreview.cs |
| `static AutoFrameJointGhostPreview activeGhost` in CommandEntry | **DEFER** | Not wired or compiled; needs lifecycle ownership design |
| Automatic cleanup retry when Assembly becomes active | **DEFER** | No event hook added; prevent invisible document manipulation |
| Apply component transform manually | DEFER | Explicit real SOLIDWORKS alignment tests first; no double-transform risk |
| Body info user/normal classification | DEFER | New joint solver stage only, not this read-only ghost |
| Multi-body/rotated/mirrored cut previews | Runtime validation only | No geometry-modifying code added |

## Exact real C# changes

File: `Commands/AutoFrameJoint/AutoFrameJointGhostPreview.cs`.

### A. At top of TryShow after validation that frameA/frameB are top-level components, before any Display3:

```csharp
// Check visible occurrence state in the active Assembly without
// changing suppression, visibility or resolving lightweight parts.
// IsHidden(false) checks display visibility without treating
// every Lightweight occurrence as hidden.
if (frameA.IsSuppressed() || frameB.IsSuppressed())
{
    message = "Frame A hoặc B đang Suppressed. Hãy Unsuppress trước khi xem.";
    return false;
}

if (frameA.IsHidden(false) || frameB.IsHidden(false))
{
    message = "Frame A hoặc B đang Hidden. Hãy Show Component trước khi xem.";
    return false;
}
```

- This does **not** unsuppress, unhide, change Component options or resolve Lightweight components.
- The 2025 SOLIDWORKS documentation documents `IComponent2.IsHidden(bool ConsiderSuppressed)`. Its remarks say `IsHidden(true)` can return true for a lightweight component; `false` is appropriate for visibility after a separate suppression check. Even the state of an occurrence can change with active configuration.
- `GetModelDoc2()` may still be null if Lightweight; this is correctly rejected later. No auto-resolve occurs.

### B. Accurate message on successful overlay

```csharp
message = String.Format(
    "Đang xem bản sao trạng thái hiện tại: {0} body A + {1} body B. CHƯA MÔ PHỎNG VẾT CẮT MỚI.",
    countA, countB);
```

Unlike "Đây là hình gốc, CHƯA CẮT", this does not incorrectly assert an uncut part. `Component2.GetBodies3` may return user bodies modified by Assembly cuts. See official `GetBodies3` docs and `BodiesInfo`.

### C. Pending-Hide error shows retained count

```csharp
message = cleaned
    ? "Không tạo được Preview: " + ex.Message
    : "Lỗi Preview (" + ex.Message + "); còn " + shown.Count +
      " body chưa dọn được bằng Hide(): " + cleanError;
```

The count is of **tracked potentially remaining bodies**, not proven visible objects. When `Hide` throws, the manager keeps the references for retry. It is NOT proof of 100% cleanup.

## Why Gemini's static activeGhost suggestion is NOT merged now

Current `AutoFrameJointCommandEntry.Open()` does NOT instantiate GhostPreview. `AutoFrameJointSelectionPage` onClearPreview is a deliberate no-op. Adding a lone static field without approved lifecycle semantics would create a false sense of safety.

**Required future owner protocol before UI integration:**
1. A single session owner manages `GhostPreview` tied to the source Assembly's COM identity. No stale manager may be overwritten while `IsShowing` is true.
2. Prevent creation of a new visible preview if a prior session has pending Hide failures; warn and provide a deliberate, UI-thread-only cleanup retry.
3. Keep a *strong reference* while any Hide is pending; do not release COM wrappers forcibly.
4. Cleanup is requested on **PMP AfterClose**, and also on explicit preview reset, but must not be called under selection box callbacks. If a different Assembly is active, defer; do not silently ActivateDoc.
5. Keep same-session original ModelDoc2 and PartDoc references until cleanup is resolved; if documents were closed or unloaded, mark state STOP/MANUAL REVIEW rather than asserting success.
6. No automatic retry loop or timer, no hidden cross-Assembly cleanup, no dependence on GC finalizers. The eventual owner design needs separate source patch and **real Host Smoke** after baseline PMP test.

**Do not add** `private static AutoFrameJointGhostPreview activeGhost` to CommandEntry merely to satisfy a checklist.

## Official API references

- `IsHidden` (2025): https://help.solidworks.com/2025/English/api/sldworksapi/SolidWorks.interop.sldworks~SolidWorks.interop.sldworks.IComponent2~IsHidden.html
- `IsSuppressed` (2023): https://help.solidworks.com/2023/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IComponent2~IsSuppressed.html
- `GetBodies3`: https://help.solidworks.com/2018/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IComponent2~GetBodies3.html

## Antigravity instructions NOW (source-only)

1. Checkout/review the new branch only in a safe isolated worktree. **Do not interrupt the existing user SOLIDWORKS debug session.**
2. Read `AutoFrameJointGhostPreview.cs` in full and inspect exact 3 changes. Check `IsSuppressed()` and `IsHidden(false)` against checked-in Interop metadata, without running any build or compiler.
3. Verify all original P0 fixes remain (`shown.Add(tracked)` before `Display3`, `Hide(entry.Part)`, best-effort `RedrawSafely()`).
4. Ensure no Source/CAD write operations were introduced and no hidden/resolved component is forcibly changed.
5. Analyze owner protocol above and propose a lifetime state diagram / exact C# class skeleton for future integration. **Do not wire current PMP or backend until approved**.
6. Return a report-only commit listing any actual code issue by line, proposed corrections and skipped runtime cases. Never mark these changes as Host PASS.
7. Do NOT edit `ADDIN.csproj`, `AutoFrameJointCommandEntry.cs`, `AutoFrameJointSelectionPage.cs`, Check Hole, Mirror, Bend or any installed DLL/PDB.

## After user says competing debug is finished

A. Isolated Debug x64 build with installed DLL/PDB SHA guard, report Interop compile errors.  
B. Native PMP Frame A/B host smoke baseline.  
C. Separate lifetime-owner and explicit `Xem trạng thái A/B` UI integration commit.  
D. Actual overlay tests: translated, rotated, mirrored, multi-body, assembly-level cut, hidden/suppressed and cleanup failures; ensure no model edits.  
E. **Only after** this is stable, plan a preview for real profile-specific joint cutting.

**Static source patch delivered, not host-tested and not yet wired into PMP.**
