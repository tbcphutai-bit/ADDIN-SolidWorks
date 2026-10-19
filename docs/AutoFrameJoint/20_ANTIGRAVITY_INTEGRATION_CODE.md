# 20 — ANTIGRAVITY EXACT CODE HANDOFF / BUILD & HOST CHECK

**Author:** ChatGPT (code owner). **Stage:** native PMP selection + explicit read-only analysis.
**Working branch:** `feat/auto-frame-joint-antigravity-code-handoff-20261019`
**Baseline:** `3fd3e0f` (396-line PMP + earlier geometry probe).
**No 3D cut preview, no Apply Cut yet.** We cannot claim the new integrated project compiles without an actual VS/MSBuild run in the user's SOLIDWORKS environment.

## Code files ALREADY WRITTEN — do not regenerate them
1. `UI/AutoFrameJoint/AutoFrameJointSelectionPage.cs` — **existing 396-line native PMP** with Frame A/B boxes, selection marks 1/2, duplicate/Part checks, one Analyze action and Cancel cleanup. This class was already in `3fd3e0f`; Antigravity may have **+88 local lines**, so review `git diff` before overwriting anything.
2. `Commands/AutoFrameJoint/AutoFrameJointReadOnlyProbe.cs` — **updated**: `AnalyzePair(ISldWorks app, Component2 a, Component2 b)` receives explicit A/B. `AnalyzeSelected(app)` remains a compatibility wrapper. Geometry code still *only estimates* axes/length and returns warnings.
3. `Commands/AutoFrameJoint/AutoFrameJointCommandEntry.cs` — **new complete C# entry class**. Retains a live PMP reference and injects the callback into the page. It does not display preview and does not modify Part.
4. `ADDIN.csproj` — **code-owner-only diff**: adds precisely two Compile Include elements for Page + Entry, retaining the existing ReadOnlyProbe entry. Do not let Antigravity manually change project code.

### The two exact project Compile entries (already in this branch)
```xml
<Compile Include="UI\AutoFrameJoint\AutoFrameJointSelectionPage.cs" />
<Compile Include="Commands\AutoFrameJoint\AutoFrameJointCommandEntry.cs" />
```

## How A/B travels from native PropertyManager to analysis
```text
Selectionbox Frame A     Mark=1
Selectionbox Frame B     Mark=2
    | OnSubmitSelection => filter swSelCOMPONENTS
    | OnSelectionboxListChanged => read-only status
    v
AutoFrameJointSelectionPage.PerformAnalyze()
    | onAnalyze(Component2 frameA, Component2 frameB)
    v
AutoFrameJointCommandEntry.Open(ISldWorks)
    | AutoFrameJointReadOnlyProbe.AnalyzePair(app, frameA, frameB)
    v
C# read-only geometry estimator
    | short status into native PMP label; long report => System.Diagnostics.Trace
    v
NO cut / no SAVE / no temporary-body preview
```

## EXACT ENTRY-POINT CODE (already saved in Commands/AutoFrameJoint/AutoFrameJointCommandEntry.cs)
```csharp
// Run ONLY from an existing command UI event on SOLIDWORKS UI thread,
// after the code-owner separately wires a dedicated launcher.
ADDIN.Commands.AutoFrameJoint.AutoFrameJointCommandEntry.Open(swApp);
```
Where `swApp` is the existing instance of `SolidWorks.Interop.sldworks.ISldWorks` in `BomTaskPaneControl.cs` or other integration owner. **Do not** create a new instance of SOLIDWORKS. The new entry class intentionally does not edit taskpane UI, so the user will not see a button until code-owner wires it.

### Exact expected value at analyze callback
```csharp
// The concrete implementation is in the GitHub C# file.
string report = AutoFrameJointReadOnlyProbe.AnalyzePair(
    app, frameA, frameB);
System.Diagnostics.Trace.WriteLine("[AFJ-READONLY] " + report);
// User sees a SHORT honest status instead of a wall of text.
```

## Antigravity instructions — step by step

### Step 0 — preserve the user’s local work
Before touching file content, STOP Copilot on the same files. In the separate `ADDIN-AutoFrameJoint-Phase1` clone (NOT Check Hole), run:
```powershell
git rev-parse --show-toplevel
git branch --show-current
git status --short --untracked-files=all
git diff -- UI/AutoFrameJoint/AutoFrameJointSelectionPage.cs
git diff -- Commands/AutoFrameJoint/AutoFrameJointReadOnlyProbe.cs
```
If `AutoFrameJointSelectionPage.cs` differs (Anti reported +88 lines), show actual diff to ChatGPT BEFORE switching branch, cherry-pick or copying. Do not `git reset`, `git clean`, `git stash` or discard changes without asking. If practical, create another isolated clone so the original WIP is untouched.

### Step 1 — read three concrete C# sources
Open each **from branch `feat/auto-frame-joint-antigravity-code-handoff-20261019`**:
- `UI/AutoFrameJoint/AutoFrameJointSelectionPage.cs`
- `Commands/AutoFrameJoint/AutoFrameJointReadOnlyProbe.cs`
- `Commands/AutoFrameJoint/AutoFrameJointCommandEntry.cs`

Check constructors/callbacks match:
```csharp
new AutoFrameJointSelectionPage(
  ISldWorks app,
  Func<Component2, Component2, string> onAnalyze,
  Action onClearPreview,
  Action<AutoFrameJointSelectionPage> onClosed)
```
(The `new` line above is the conceptual signature, not a valid C# expression: look at the actual constructor declaration.)

### Step 2 — what Antigravity IS allowed to change
For now UI changes are confined to `UI/AutoFrameJoint/AutoFrameJointSelectionPage.cs` **after** reviewing local diff and with user authorization. Anti may adjust:
- brief Vietnamese captions, selector heights, collapsed information area, tab order, spacing;
- native PMP hints/status wording; error text;
- demo-only styling without fake backend results.

Anti may NOT change COM callback signatures, selection mark values, `GetSelectedObjectCount2` logic, component identity logic, `AnalyzePair`, `ADDIN.csproj`, `SWAddin.cs`, `BomTaskPaneControl*`, `Commands/**`, Mirror, Check Hole/Check Bend, or any existing handlers.

### Step 3 — build check (run by code owner or reported by Anti without editing backend)
Visual Studio Developer PowerShell, in this isolated branch:
```powershell
msbuild .\ADDIN.csproj /t:Rebuild /p:Configuration=Debug /p:Platform=x64 /v:minimal
Write-Host "BUILD_EXIT=$LASTEXITCODE"
```
If the installed solution uses an x64-specific configuration, confirm it exists before selecting. If `msbuild` is absent from PATH, use Developer PowerShell/VS Build. **Do not claim BUILD PASS without actual compiler output and Exit 0**.

When compilation fails:
- If error is inside `UI/AutoFrameJoint/AutoFrameJointSelectionPage.cs`, Anti may fix UI-only type/property mismatch after checking actual installed Interop and report the minimal change.
- If error concerns `Commands/**`, `SWAddin.cs`, `ADDIN.csproj`, or another production file: STOP, send compiler error to ChatGPT/code owner; do not expand Anti's scope.
- Never adjust Add-in target framework or force-disable entire commands to make the build pass.

### Step 4 — native SOLIDWORKS host smoke test (after a launcher is integrated)
Code owner separately adds one toolbar/taskpane command that invokes:
```csharp
AutoFrameJointCommandEntry.Open(swApp);
```
User opens a **copy** of Assembly and validates:
1. Command opens left native PropertyManager with **Frame A** and **Frame B**.
2. Each selection box holds only one Component occurrence.
3. Same component cannot occupy both selectors.
4. Two valid Parts enable `Phân tích`; clicking it prints provisional report and does **not** alter Part/Mate.
5. Selecting a Subassembly, blank/invalid occurrence, or switching document yields a safe message.
6. Cancel clears page state and backend temporary-body cleanup hook (currently no bodies).
7. There is **NO** Apply Cut and **NO** 3D cut preview yet.

### Step 5 — report and Git status
Antigravity must hand back:
```text
Repository folder / branch / HEAD
Local diff (all modified files) and protected Copilot edits
Files read from commit
Build command + exit code + errors
UI visual evidence (native PMP or isolated preview)
PMP callback and marks review
Uncompiled/unintegrated host limitations
Files changed by Anti / reason
What remains for ChatGPT (launcher, preview, solver)
```
Do NOT commit/push while local Copilot work is unresolved or until user explicitly approves.

## Files ChatGPT/code owner changed in this branch
- `Commands/AutoFrameJoint/AutoFrameJointReadOnlyProbe.cs` — adds AnalyzePair (explicit component arguments; preserves original analysis logic).
- `Commands/AutoFrameJoint/AutoFrameJointCommandEntry.cs` — implements entrypoint, callback, PMP lifecycle.
- `ADDIN.csproj` — registers the two UI/entry C# files for old-style .NET Framework build.
- This instruction document.

**Intentionally NOT changed:** existing `SWAddin.cs`, `BomTaskPaneControl.cs`, `BomTaskPaneControl.Designer.cs`, Check Hole, Mirror, Bend, and Antigravity's locally edited 88-line diff.

## Known limitations
- No Part cutting, no preview body and no proven general corner solver.
- `AnalyzePair` is still a heuristic axis estimator; winner/loser not automatically resolved.
- Any existing geometry analyzer limitations remain (multibody, config mismatch, B-rep stability).
- No production launcher yet, so code can compile but won't visibly open until code-owner wires one.
- GitHub cannot see user’s uncommitted local files. Do not claim Anti's +88 lines have been integrated here.
- Native PMP on installed SOLIDWORKS / .NET Interop must be tested on the user workstation.
