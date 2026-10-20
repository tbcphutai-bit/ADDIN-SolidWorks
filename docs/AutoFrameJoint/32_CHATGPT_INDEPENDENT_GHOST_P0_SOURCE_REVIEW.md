# 32 — ChatGPT independent full source review of Ghost P0 (NO BUILD)

Reviewed on 2026-10-20 from the actual repository:
- Anti review HEAD: `651c3ea09743ff24b766a35a4cdae32536bdf99f`
- Parent code patch: `03531fc67d5cf5b7d3d7860d0f7ec9698676f224`
- C# full source: `Commands/AutoFrameJoint/AutoFrameJointGhostPreview.cs` (282 lines as reviewed)
- Connected context: `UI/AutoFrameJoint/AutoFrameJointSelectionPage.cs`, `Commands/AutoFrameJoint/AutoFrameJointCommandEntry.cs`, `Commands/AutoFrameJoint/AutoFrameJointReadOnlyProbe.cs`, `ADDIN.csproj`.
- Git compare `03531fc...651c3ea` proves Antigravity added only `docs/AutoFrameJoint/ANTIGRAVITY_GHOST_P0_FIX_REVIEW.md`. No new C# modifications.

**Status: STATIC SOURCE REVIEW ONLY.** No MSBuild, csc, SOLIDWORKS host testing, DLL/PDB deploy, COM registration, file copy, merging or debugger interruption has been performed by ChatGPT. The user's other SOLIDWORKS command is still debugging.

## Confirmed from actual C# source

1. `shown.Add(tracked)` (around line 219) occurs **before** `copy.Display3(component,colorRef,swTempBodySelectOptionNone)` (around line 224). This closes the previous path that lost the strong body reference when Display3 threw or returned failure.
2. `TryShow` catches Display errors and attempts `ClearInternal`. Hide failures remain referenced in `shown` for an additional attempt. This is **a cleanup opportunity, not a guarantee of successful COM cleanup**.
3. `TryClear` uses `busy` as a reentrancy guard, but is NOT a cross-thread synchronization mechanism; future callers must use SOLIDWORKS UI thread/STA.
4. `RedrawSafely` redraws only if `IsOriginalAssemblyActive()` proves the originally captured Assembly is still active. It catches COM redraw exceptions and logs warnings.
5. `Hide(entry.Part)` uses the Part's `ModelDoc2` as documented. There is NO unsupported `Hide(sourceAssembly)` fallback.
6. No real Cut/Boolean/Mate, no Feature Tree edits, no Save or model write API in the ghost class.
7. Class is registered once in `ADDIN.csproj`. **It remains disconnected from the UI**, and `AutoFrameJointCommandEntry` / PMP callbacks still have not been wired to ghost.
8. `Display3` is documented as returning 0 on success, 1/2/3 on errors; component must be a Part instance not nested in a subassembly.

## Findings Gemini overstates or omits

### P1 — “eliminates all COM leak risk” is NOT proven
Relevant C#: `TryShow` catch (around lines 108-117), `ClearInternal` (around lines 254-280).

Registering a body before `Display3` ensures it can still be passed to `Hide` **while the manager is alive**. But `Hide` can throw, the displayed body can survive, and the future PMP owner has no persistent retention/retry policy. `IsShowing` is `shown.Count != 0`, which measures outstanding references, **not confirmed on-screen visibility**. Do not label "leak-free", "GC safe 100%" or "Cancel PASS" based only on this code. A later integration must KEEP the manager alive if cleanup fails and provide a retry after returning to the original document.

### P1 — UI/PMP cleanup is NOT connected
Relevant: Ghost constructor and methods, `AutoFrameJointCommandEntry.cs` (~93 lines), `AutoFrameJointSelectionPage.cs` (~524 lines). The command's `onClearPreview` is not a Ghost cleanup call, because the engine is not instantiated in the command entry. No user can currently invoke Ghost in a host from the existing PMP. Host-based Cancel/AfterClose tests are mandatory after wiring, never counted PASS now.

### P1 — “Original, uncut geometry” wording may be misleading
`ShowComponent` uses `IComponent2.GetBodies3`, which official API says can return **user bodies modified by an assembly-level feature** (e.g. an assembly cut). Thus copies of these bodies may reflect the component's CURRENT assembly-visible geometry rather than its original uncut part geometry. The message “Đây là hình gốc, CHƯA CẮT.” (around lines 102-105) is not reliable for a component with existing assembly cuts. **Suggestion for a future code-owner-only UX patch:** say "Bản sao trạng thái hiện tại A/B; chưa mô phỏng VẾT CẮT MỚI" and document that the source body is from `GetBodies3`.

### P1 — Correct visual alignment not established
The code copies each body and immediately calls `Display3(Component2,...)` without applying a transform. This may be appropriate for the component context; it is NOT yet proven to be positioned correctly for different component transforms/orientations. SOLIDWORKS's official Display Temporary Body example explicitly uses `Body2.ApplyTransform` for a sample displacement, showing that transforms can matter, but it does **not** establish that the current code must always apply a transform. **Do not add `Transform2` blindly**: that may double-transform. Verify placement in a real Assembly with rotated/transformed components.

### P2 — No filter for hidden or suppressed Components
Current validation checks for top-level `GetParent()==null`, non-null Part `GetModelDoc2` and `GetBodies3`, but does not explicitly consult `Component2.IsHidden` or `IsSuppressed`. The resulting overlay might unexpectedly reveal hidden geometry. Before enabling a Preview button, decide whether to reject hidden/suppressed occurrences and test behavior. `IComponent2.IsHidden` has documented configuration-dependent behavior.

### P2 — Assembly-specific user bodies and `BodiesInfo` not used
`GetBodies3` supplies both bodies and `BodiesInfo` identifying normal/user bodies. The current code ignores that `out info`. That is not by itself a compile error. It matters if previews must distinguish body changed by assembly cut vs untouched source. Future profile-cut solver should not silently conflate them.

### P2 — “No race condition” has not been verified
`busy` is a plain bool. It protects synchronous callback reentrancy on one thread but is NOT a general thread-safe barrier. SOLIDWORKS API calls must not run from background workers here. The source alone cannot prove all upcoming COM callback lifecycles safe.

### P2 — Redraw is best effort, not a postcondition
The redraw wrapper catches and logs failures; `TryShow` can still return true if `GraphicsRedraw2` throws or the document changes between preview calls. “Redraw guaranteed” is not supported.

## API sources for this audit

- Display3: https://help.solidworks.com/2025/English/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.IBody2~Display3.html
- Hide: https://help.solidworks.com/2024/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IBody2~Hide.html
- GetBodies3: https://help.solidworks.com/2018/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IComponent2~GetBodies3.html
- IsHidden: https://help.solidworks.com/2016/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IComponent2~IsHidden.html
- Display Temporary Body official example: https://help.solidworks.com/2025/english/api/sldworksapi/Display_Temporary_Body_Example_VBNET.htm

## Next steps for Antigravity — source-only while SOLIDWORKS is occupied

1. Read this audit and actual `AutoFrameJointGhostPreview.cs`, `AutoFrameJointSelectionPage.cs` and `AutoFrameJointCommandEntry.cs` in full.
2. Mark every 7/7 "PASS" from prior review as **static-only** and distinguish from host-runtime PASS.
3. **Do not rewrite backend or wire UI now**. Propose line-level changes for the profile/visibility messages and the future cleanup ownership protocol, with specific method signatures and tests; send to ChatGPT for code-owner approval.
4. No Build/Rebuild/csc, no DLL copy/Deploy/Restore, no SOLIDWORKS/Visual Studio actions, no COM re-registration, no branch merges.
5. After the other debugging finishes, first do isolated build/no installed SHA changes, then native PMP A/B host smoke, then a separate Ghost wiring patch and real host cleanup/geometry tests.

**Reviewer verdict:** P0 reference-order patch is a legitimate safety improvement. Gemini's absolute “PASS 100% / leak-free / no crash / no race” assertions are not supported by static inspection. Do not certify Ghost Preview as a working SOLIDWORKS command or cut preview at this stage.
