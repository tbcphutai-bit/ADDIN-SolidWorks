# 31 — Independent ChatGPT code audit of Gemini's Ghost Preview Review

**Repository:** `tbcphutai-bit/ADDIN-SolidWorks`  
**Gemini report reviewed:** `review/afj-ghost-preview-code-review-20261020`, commit `433946f5e017d85ed3a11ec9b5476e7ff7a77aee`.  
**Code base:** `feat/afj-temporary-body-ghost-engine-source-only-20261020`, commit `d72be71b99930a6b6c2d6e3d6fabb17e39298d34`.  
**Fix branch:** `fix/afj-ghost-preview-lifecycle-p0-20261020`.

**MODE: SOURCE-ONLY. NO BUILD, NO REBUILD, NO SOLIDWORKS, NO COM REGISTRATION, NO DLL/PDB CHANGES.** Another user command is using SOLIDWORKS for debugging. No merge to main or other command branches.

## A. Exactly what Gemini changed

Direct `git compare d72be71..433946f` shows **only** `docs/AutoFrameJoint/ANTIGRAVITY_GHOST_PREVIEW_REVIEW.md` was added. Gemini did not edit the C# class. Its 7 x PASS are self-reported static/reflection checks, **not a SOLIDWORKS in-process render/cleanup test**.

## B. What independently checks out

| Item | Verdict |
| --- | --- |
| `Body2.Copy()` and `IsTemporaryBody()` signatures | Supported by official SOLIDWORKS API |
| `Body2.Display3(object,int,int)`, COLORREF, 0 success | Supported by API docs |
| `Component2.GetBodies3(int,out object)` | API signature supported; actual frame body coordinates still require host test |
| `GetParent()==null` for top-level component | Official API confirms this |
| `Hide(object Part)` | Official docs require **the specified Part context**; the existing `entry.Part` is plausible and should NOT be blindly replaced with Assembly |
| `GraphicsRedraw2()` | Documented `void` redraw call, marked obsolete/superseded by model-view redraw but available; usable as a best-effort fallback |
| `ADDIN.csproj` compile entry | Exactly one |
| Renderer actually works | NOT TESTED |

Official API references:
- `Display3`: https://help.solidworks.com/2023/english/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.IBody2~Display3.html
- `Hide`: https://help.solidworks.com/2023/English/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.IBody2~Hide.html
- `GetParent`: https://help.solidworks.com/2024/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IComponent2~GetParent.html
- `GraphicsRedraw2`: https://help.solidworks.com/2022/English/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.IModelDoc2~GraphicsRedraw2.html

## C. Source issues Gemini missed

### P0 — COM reference registration was AFTER `Display3` (original L215-223)
Original code called `copy.Display3(...)` and only added `new ShownBody(copy,part)` to `shown` *after* a successful return. If COM throws after partially displaying the body, `TryShow` catches and calls `ClearInternal`, but the interrupted body was never added to `shown`. The last strong reference to a possibly displayed body can disappear when the stack unwinds. This defeats the stated COM lifetime guarantee.

**FIXED IN REAL C# CODE:** append `ShownBody tracked = new ShownBody(copy, part); shown.Add(tracked);` **before** `Display3`. If `Display3` returns a nonzero code OR throws, the cleanup path retains the reference and attempts `Hide(entry.Part)`. If Hide fails, `shown` keeps the reference, fails closed and reports cleanup pending. This may conservatively retain a body after a failed Display; a false-positive retained reference is safer than letting a possibly displayed body be collected. Do not force `Marshal.FinalReleaseComObject`.

### P1 — explicit graphics redraw not present
The original `ShowComponent` and `ClearInternal` had no `GraphicsRedraw2`. This could leave stale raster images depending on host version / rendering state. Gemini raised this, but no host evidence proves a redraw is always required.

**FIXED SOURCE-ONLY:** one `RedrawSafely()` after both bodies are displayed and one after any attempted cleanup, **only if the original Assembly is still ActiveDoc**. Best-effort logging; never activate or modify any other document. `GraphicsRedraw2` being obsolete is acknowledged; a later host test can decide whether an `IModelView` equivalent is needed.

### P1 — Gemini's proposed `Hide(sourceAssembly)` fallback is NOT an accepted fix
Official docs describe `IBody2.Hide(Part)` as using the specified **Part** context; `IBody2.IHide` explicitly takes `PartDoc`. Blind fallback with `sourceAssembly` (an AssemblyDoc) is not documented as valid. Trying a second Hide with a wrong context might hide nothing and falsely clear a body reference. **DO NOT APPLY Gemini's fallback.** Keep `Hide(entry.Part)`, log failures, retain body reference, verify actual rendering context via host test.

### P1 — Lifecycle across future command/PMP closure remains incomplete
Currently the GhostPreview class is **NOT WIRED INTO** `AutoFrameJointCommandEntry` or `AutoFrameJointSelectionPage`. No button invokes `TryShow`, and on-clear callback in CommandEntry is still no-op. Gemini saying “Cancel safe PASS” describes internal potential cleanup only, not a running user scenario. The future host wiring MUST retain the ghost instance until every `Hide` succeeds; otherwise the class instance can be garbage-collected with active bodies. Do not silently integrate it now.

### P2 — Real visual precision is unknown
`GetBodies3`, body config, Component occurrence transforms and appearance are API-dependent at runtime. No host test has demonstrated a correct overlay on the user's multi-face Frame. Rendering the original uncut bodies on top of originals might be partially occluded and NOT show a cut. Never label this as verified cut preview.

### P2 — UI/UX integration untested
PMP must first pass its original selection-only host smoke test. Only then consider an explicit `Xem hình gốc A/B` button. Do not call `Display3`/Hide inside `OnSelectionboxListChanged`.

## D. Exact code files changed in this fix

1. `Commands/AutoFrameJoint/AutoFrameJointGhostPreview.cs`:
   - add managed body to `shown` **before** `Display3`;
   - add `RedrawSafely()` best-effort helper;
   - redraw once after the pair, once after attempted cleanup;
   - document that `IsShowing` includes pending-cleanup bodies;
   - no `Hide(sourceAssembly)` fallback.
2. This audit doc: `docs/AutoFrameJoint/31_CHATGPT_GHOST_CODE_AUDIT_AND_FIX.md`.

**UNCHANGED:** `ADDIN.csproj`, PMP, CommandEntry, TaskPane, `SWAddin.cs`, Check Hole, Bend, Mirror and any installed DLL/PDB. This is intentionally a separate branch and source-only patch.

## E. Antigravity next task

1. Check out a NEW isolated worktree of the precise fix branch; preserve any current uncommitted changes.
2. Read `AutoFrameJointGhostPreview.cs` in full, compare against commit `d72be71` and this audit.
3. Check the `shown.Add(tracked)` call now precedes `Display3` and the `Hide(entry.Part)` path remains.
4. Review C# and Interop compile signatures WITHOUT executing `csc`, MSBuild, SOLIDWORKS or COM.
5. If any objection, cite exact line, reason and official API; do not claim runtime PASS.
6. Create a REPORT-ONLY review branch with findings. Do not modify backend or merge.
7. Wait for the user to say the other class's debug is finished before build/deploy or in-process tests.

## F. Future test matrix — DEFERRED

After other class debugging ends: (1) isolated Debug x64 build/no installed hash change; (2) base PMP Frame A/B host smoke; (3) separate button integration; (4) 1 body each; (5) multi-body; (6) Display3 throws or returns nonzero; (7) second Preview clears prior; (8) Cancel and document switch; (9) fail Hide with retained references; (10) no CAD/model dirty changes. Must record screenshots, errors and original installed DLL/PDB hashes before and after.

**Acceptance**: source correctness review is IN PROGRESS; host stability and actual 3D appearance are NOT VERIFIED.
