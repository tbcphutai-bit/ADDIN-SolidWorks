# Runbook 39 — Paste ChatGPT Auto Frame Joint code DIRECTLY into the current CG2 project; manual Build to the original output path

**Decision approved by user:** Antigravity copies/writes the exact code authored by ChatGPT into the **existing classes and project at `C:\Tao moi\ADDIN`**, preserving all Check Hole, Bend, Mirror and other commands. **Do not build automatically.** Restore **Debug | x64 OutputPath to `C:\Tao moi\`**, the original installed Add-in DLL location.

**CODE OWNER:** ChatGPT. **Antigravity:** direct code copy/paste implementer only, NO own backend rewriting.

**GitHub code branch:** `fix/afj-cg2-original-output-manual-build-20261020`. This branch contains the complete 6-source-class + three integration-file patch from the prior CG2 integration commit, plus the corrected .csproj output path. It is a SOURCE DELIVERY/REFERENCE. The requested target is the **currently open Visual Studio worktree**, not merely a different checkout.

## What differs from Runbook 38

The **ONLY new source configuration change** is within the existing `ADDIN.csproj` Debug|x64 PropertyGroup:

```xml
<PropertyGroup Condition="'$(Configuration)|$(Platform)' == 'Debug|x64'">
  <DebugSymbols>true</DebugSymbols>
  <OutputPath>C:\Tao moi\</OutputPath>
  <DefineConstants>DEBUG;TRACE</DefineConstants>
  <RegisterForComInterop>false</RegisterForComInterop>
  <DebugType>full</DebugType>
  <PlatformTarget>x64</PlatformTarget>
  <LangVersion>7.3</LangVersion>
  <ErrorReport>prompt</ErrorReport>
</PropertyGroup>
```

At the bottom of `ADDIN.csproj`, these MUST remain empty:

```xml
<PreBuildEvent></PreBuildEvent>
<PostBuildEvent></PostBuildEvent>
```

**No automatic Build, no MSBuild, no F5, no pre-build kill, no post-build copy.** A **user-initiated** Build later will write `ADDIN.dll` **directly to `C:\Tao moi\ADDIN.dll`**, potentially replacing the registered live DLL. It is NOT isolated. Therefore, **before** any manual Build later, the user must save work, stop conflicting SOLIDWORKS debugging, and back up the existing `C:\Tao moi\ADDIN.dll` AND `ADDIN.pdb` as one pair with SHA256. Do not silently perform that now.

Other configurations' OutputPaths are unchanged.

## 1. Antigravity must use the already-open local project; no separate worktree-only finish

**TARGET:** `C:\Tao moi\ADDIN\ADDIN.csproj` currently opened in Visual Studio.

Do not simply checkout another worktree and report done while leaving `C:\Tao moi\ADDIN` unchanged.

First inspect without mutation:

```powershell
Set-Location 'C:\Tao moi\ADDIN'
git status -sb
git rev-parse HEAD
git remote -v
git diff -- UI/BomCommandLayout.cs BomTaskPaneControl.cs ADDIN.csproj
```

The last remote Check Hole CG2 worktree HEAD observed on GitHub was moving independently. Before you write, compare the TARGET versions of the three files against the base for this code package, `fc7f5348de45de046726a139ef2685e57ab52aea`. Check for already existing local AFJ changes. **If any target base file differs in C# or project settings, STOP and ask ChatGPT to reconcile exact source; do not overwrite user edits.** Current repo differences since this base were in docs/antigravity/cg2, but the local worktree is not independently accessible to ChatGPT.

## 2. Exact files to copy/paste into the live source folder (do not build)

**Six new complete C# classes (COPY entire content verbatim from ChatGPT commit):**

1. `UI/AutoFrameJointTaskPaneLauncher.cs`
2. `UI/AutoFrameJoint/AutoFrameJointSelectionPage.cs`
3. `Commands/AutoFrameJoint/AutoFrameJointCommandEntry.cs`
4. `Commands/AutoFrameJoint/AutoFrameJointReadOnlyProbe.cs`
5. `Commands/AutoFrameJoint/AutoFrameJointGhostPreview.cs`
6. `Commands/AutoFrameJoint/AutoFrameJointGhostSessionManager.cs`

**Three existing CG2 files:**

7. `UI/BomCommandLayout.cs`: insert `EnsureAutoFrameJointButton()` and `btnAutoFrameJoint` as tile 8; preserve every other button and layout condition.
8. `BomTaskPaneControl.cs`: only add `btnAutoFrameJoint` to the Assembly enable/disable path and macro button hit-test; preserve other commands.
9. `ADDIN.csproj`: six Compile Include entries, `Debug|x64` OutputPath **`C:\Tao moi\`**, empty PreBuild/PostBuild events. Do not touch any other project configuration.

**No edits to:** `BomTaskPaneControl.Designer.cs`, `SWAddin.cs`, Check Hole, Check Bend, Mirror, Check DXF, installed DLL or PDB. Existing button placement is right-hand tile on row 4 adjacent to Check chấn.

Antigravity must NOT invent additional code or redesign UI. If the local source layout differs, STOP and paste the smallest exact diff in the report.

## 3. Verify code was pasted into THE CURRENT project, not into another worktree

Run only read-only commands after the file edits, from the original `C:\Tao moi\ADDIN`:

```powershell
git status -sb
git diff --stat
git diff -- ADDIN.csproj UI/BomCommandLayout.cs BomTaskPaneControl.cs
git hash-object "UI/AutoFrameJointTaskPaneLauncher.cs"
git hash-object "UI/AutoFrameJoint/AutoFrameJointSelectionPage.cs"
git hash-object "Commands/AutoFrameJoint/AutoFrameJointCommandEntry.cs"
git hash-object "Commands/AutoFrameJoint/AutoFrameJointReadOnlyProbe.cs"
git hash-object "Commands/AutoFrameJoint/AutoFrameJointGhostPreview.cs"
git hash-object "Commands/AutoFrameJoint/AutoFrameJointGhostSessionManager.cs"
git hash-object "UI/BomCommandLayout.cs"
git hash-object "BomTaskPaneControl.cs"
git hash-object "ADDIN.csproj"
```

A `git status` report can show the newly copied files as UNTRACKED until staged; that does NOT mean they are missing from the filesystem. For this local handoff, use `git status --short -uall` to show them. Do not stage, commit or push without explicit instruction.

The expected blob SHA values for the 9 file paths are defined by the GitHub code-owner commit. Verify all 9 and provide results in the final report.

## 4. Only when user explicitly asks to perform a manual build later

- Confirm the correct open Visual Studio project: `C:\Tao moi\ADDIN\ADDIN.csproj`.
- Confirm selected `Debug | x64` and `OutputPath = C:\Tao moi\`.
- **STOP conflicting debug/close SOLIDWORKS safely BEFORE writing that output directory** (output directly replaces the installed Add-in DLL).
- Back up the installed DLL AND PDB from `C:\Tao moi\` with independent SHA256 and a reliable restore path.
- Execute Build/Rebuild MANUALLY by user approval only. No agent-initiated builds.
- Check whether both DLL/PDB were produced by the expected source, check Error List, and start a clean SOLIDWORKS session deliberately.
- Test Macro row 4 right-hand **Giao góc Frame**, PMP A/B and Cancel cleanup in a copied test Assembly. No actual cut function exists yet.

## 5. Acceptance

- Source is pasted into `C:\Tao moi\ADDIN` (not only a detached worktree);
- All nine files match the code-owner GitHub package; three CG2 existing files retain all non-AFJ source;
- Debug x64 writes to the OLD location `C:\Tao moi\`;
- No automated build or auto-kill/copy event;
- No files installed or running SOLIDWORKS changed now.

**This is a source-only operation, not a runtime test or a claim that the button is already visible in SOLIDWORKS.**
