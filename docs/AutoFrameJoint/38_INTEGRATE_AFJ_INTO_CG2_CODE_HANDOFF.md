# 38 — Integrate completed Auto Frame Joint launcher/PMP/Ghost source into Check Hole CG2

**CODE OWNER:** ChatGPT. **IMPLEMENTER:** Antigravity COPY/PASTE EXACT code only.  
**STATUS:** Source-only integration, NOT BUILT, NOT DEPLOYED, NOT VERIFIED IN SOLIDWORKS.

## Verified root cause of missing right-hand Macro button

Copilot's local report says Visual Studio worktree `C:\Tao moi\ADDIN` is at branch `feat/check-hole-cg2-part-relations`, local HEAD `b1457ec`. That local source has **zero AutoFrameJoint code**. The installed `C:\Tao moi\ADDIN.dll` matches the Check Hole CG2 Debug x64 build SHA, so it correctly loads **without** the Frame Joint button.

**Remote caveat:** latest GitHub `feat/check-hole-cg2-part-relations` at the time this integration branch was created is `fc7f5348de45de046726a139ef2685e57ab52aea`, not `b1457ec`. The local `b1457ec` Git object was not available to the GitHub connector. **DO NOT assume the local worktree is identical to remote CG2. NEVER force-checkout, reset, merge or overwrite the local CG2 branch.** First confirm status, commit, and the exact changed file contents. If local CG2 differs, report the differences to ChatGPT before applying.

This integration is authored ON TOP OF the **remote CG2 branch**, preserving its Check Hole command and core source.

## Exact integration commit contents

**Base:** `fc7f5348de45de046726a139ef2685e57ab52aea` (CG2 remote HEAD before integration).

**Exact six new C# files copied byte-for-byte from ChatGPT Auto Frame Joint branch at `32897ab87c769f232c8886c6909bdd6fbc90b6d5`:**

| New source file | Expected Git blob SHA |
|---|---|
| `UI/AutoFrameJointTaskPaneLauncher.cs` | `ab9a14fe4069ae371ed7b3d95c9aa62d9319ee9b` |
| `UI/AutoFrameJoint/AutoFrameJointSelectionPage.cs` | `743a93a9f737b00b8e5e6e6e1148fc0de758ba2d` |
| `Commands/AutoFrameJoint/AutoFrameJointCommandEntry.cs` | `e0bf7b1d2c76dbb73a08cc2e2b0f515c19400d70` |
| `Commands/AutoFrameJoint/AutoFrameJointReadOnlyProbe.cs` | `f2700438cff8b7f3537bb1eab513dda1fa7b7956` |
| `Commands/AutoFrameJoint/AutoFrameJointGhostPreview.cs` | `6c5eaf44b3c1bf7d57b823a6d67a6129ddf049d8` |
| `Commands/AutoFrameJoint/AutoFrameJointGhostSessionManager.cs` | `b85515701921d62a86417eb2d49c166d4e9ba769` |

**Exactly three existing CG2 files patched (do not replace them wholesale with the AFJ branch copies):**

### 1. `UI/BomCommandLayout.cs`

In `EnsureModelMacroControls()` run both:
```csharp
EnsureBendMinimumButton();
EnsureAutoFrameJointButton();
```
before the early return when `cardMacroCommands != null`.

At both the `MoveControlToCard` list and the later `macroTiles` list, the LAST two tiles must remain **Check chấn** / **Giao góc Frame**:
```csharp
btnRepair3DSpline, btnVCutPropertySetup, btnCheckDxfMacro, btnCheckBendMinimum,
btnAutoFrameJoint
```
Thus tile 8 lands in **row 4, RIGHT**, next to tile 7 Check chấn, when two-column layout is active. No Designer edits and no duplicate button creation.

### 2. `BomTaskPaneControl.cs`

Within `IsModelMacroSupported(Control button, int documentType)`:
```csharp
if (ReferenceEquals(button, btnCheckAssemblyHole) ||
    ReferenceEquals(button, btnAutoFrameJoint))
    return documentType == (int)swDocumentTypes_e.swDocASSEMBLY;
```
Within `UpdateModelMacroCommandAvailability(int documentType)`:
```csharp
UpdateModelMacroButton(btnCheckBendMinimum, documentType, "Part hoặc Assembly");
UpdateModelMacroButton(btnAutoFrameJoint, documentType, "Assembly");
```
The disabled-tile hit-test `Control[] macroButtons` also includes `btnAutoFrameJoint`. No Check Hole/Check Bend/Mirror method bodies changed.

### 3. `ADDIN.csproj`

Register all 6 C# files exactly once near the existing Mirror PMP compile include:
```xml
<Compile Include="UI\AutoFrameJointTaskPaneLauncher.cs" />
<Compile Include="UI\AutoFrameJoint\AutoFrameJointSelectionPage.cs" />
<Compile Include="Commands\AutoFrameJoint\AutoFrameJointCommandEntry.cs" />
<Compile Include="Commands\AutoFrameJoint\AutoFrameJointReadOnlyProbe.cs" />
<Compile Include="Commands\AutoFrameJoint\AutoFrameJointGhostPreview.cs" />
<Compile Include="Commands\AutoFrameJoint\AutoFrameJointGhostSessionManager.cs" />
```
For **Debug|x64 only**, switch build output from installed COM directory `C:\Tao moi\` to isolated `bin\x64\Debug\`.
Remove dangerous PreBuild `taskkill /f /im sldworks.exe` and automatic PostBuild `copy /Y ... C:\Tao moi\ADDIN.dll`. Keep PreBuildEvent/PostBuildEvent empty. Other configurations and ALL existing compile items stay unchanged.

### Do NOT alter

`BomTaskPaneControl.Designer.cs`, `SWAddin.cs`, Check Hole code, Check Bend code, Mirror code, DXF code, Assembly model, registry, installed DLL/PDB or another worktree.

The launcher is a **partial class** of `BomTaskPaneControl` and owns its `private ModernButton btnAutoFrameJoint` field. Do not duplicate that field inside the WinForms Designer.

## Copy/paste steps Antigravity must follow

1. Inspect current local `C:\Tao moi\ADDIN` without editing:
```powershell
git status --short
git branch --show-current
git rev-parse HEAD
git remote -v
```
If HEAD is local `b1457ec`, determine why it differs from remote `fc7f534` first. Preserve all local changes; no `git reset --hard`, checkout over local files or blind merge.
2. Use a **separate worktree** for `integrate/afj-into-check-hole-cg2-20261020`; fetch from GitHub and read its exact commit. The integration branch already contains ALL source code; **Anti must not generate/rewrite it**.
3. In the integration worktree read all nine C#/.csproj files above. Copy the complete files exactly as committed where your target worktree has **identical CG2 base source**. If target's CG2 source differs, stop and report exact diff with affected lines; the Code Owner will author a specific reconciliation patch.
4. Run read-only Git verification:
```powershell
git rev-parse HEAD
git status --short
git hash-object "UI/AutoFrameJointTaskPaneLauncher.cs"
git hash-object "UI/AutoFrameJoint/AutoFrameJointSelectionPage.cs"
git hash-object "Commands/AutoFrameJoint/AutoFrameJointCommandEntry.cs"
git hash-object "Commands/AutoFrameJoint/AutoFrameJointReadOnlyProbe.cs"
git hash-object "Commands/AutoFrameJoint/AutoFrameJointGhostPreview.cs"
git hash-object "Commands/AutoFrameJoint/AutoFrameJointGhostSessionManager.cs"
git hash-object "UI/BomCommandLayout.cs"
git hash-object "BomTaskPaneControl.cs"
git hash-object "ADDIN.csproj"
git diff --stat
```
5. Confirm that `IsModelMacroSupported(btnAutoFrameJoint)` permits Assembly only, that Macro array has 8 entries, and that `OnButtonPress(105)` uses `GhostSessionManager.TryShow` (not a Cut operation).
6. Report branch, HEAD, nine hashes, Git diff, and any source/compile concerns to ChatGPT. Do not self-modify backend; Code Owner alone writes the correction.

**NOW: NO Rebuild/Build/csc, DLL copy/Deploy/Restore, COM registration, SOLIDWORKS restart/close, branch merge into running CG2 or Check Hole worktree.**

## Later test and deployment (only on user's explicit permission)

1. Finish other command's SOLIDWORKS debug first, preserving all drawings/changes.
2. Build the INTEGRATED `ADDIN.csproj` Debug x64; it should output to `bin\x64\Debug\` without changing the installed `C:\Tao moi\ADDIN.dll`. Record installed DLL/PDB SHA256 before and after.
3. Fix compiler errors via ChatGPT-authored patches only.
4. Verify all existing CG2 tests with new AFJ class included; test read-only AFJ startup separately.
5. Only after successful checks, perform deliberate backup/hash-controlled deploy as one consistent DLL+PDB pair, restart SOLIDWORKS normally, confirm loaded assembly file location and a suitable version/hash.
6. Open Assembly → TAI TOOL → Model → Macro: expect **Giao góc Frame** in row 4 right of **Check chấn**, enabled for Assembly. Click launches the native PMP, choose Frame A/B, Analyze and `Xem trạng thái A/B`. No real profile-dependent cut yet; Cancel must clean temporary geometry.

**Important:** No C# source is currently executing just because the repository contains it. User must not infer launch success until the integrated DLL has been built, deployed and host-tested.
