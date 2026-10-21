# Runbook 45 — CODE OWNER authorization to apply Runbook 43 to the UPDATED CG2 / Check chấn project

**Date:** 2026-10-21. **Code Owner:** ChatGPT. **Implementer:** Antigravity exact copy only. **NO AUTOMATIC BUILD.**

## Why this supersedes Runbook 44's hash gate

Antigravity Issue #31 comment **6311860286** reported the actual open Visual Studio local project `C:\Tao moi\ADDIN` changed while agents were working in parallel:

- local branch: `integrate/check-chan-pr37-vs-20261021`, reportedly HEAD `66d1251`
- `ADDIN.csproj` old local SHA `2d3c1d4393bf613c29a3e58d5c5a19720a49f558`
- NOW actual local project SHA **`004713797ea45906ad54cb805c727460e9fcf22a`**
- Two additional Check chấn compile entries added at reported lines 585–586:
  ```xml
  <Compile Include="UI\BendSection2DCanvas.cs" />
  <Compile Include="UI\BendAssemblyBatchForm.UxV43.cs" />
  ```
- No Auto Frame Joint C# was modified and Runbook43 remains un-applied.
- Local `0047137...` Git blob/HEAD is not independently accessible in GitHub; the approval below is CONDITIONAL on Anti locally confirming its exact state.

**DECISION: APPROVE** Runbook 43 integration with a 1-line CURRENT-LOCAL project patch. DO NOT use `ADDIN.csproj` from ANY GitHub AFJ branch. This document supersedes ONLY the obsolete old .csproj SHA gate in Runbook44. **Existing 4 C# Runbook43 source SHA values are UNCHANGED.**

## 1. Read-only preflight on the real Visual Studio worktree

Run from `C:\Tao moi\ADDIN`:

```powershell
Set-Location 'C:\Tao moi\ADDIN'
git status -sb
git rev-parse HEAD
git hash-object 'ADDIN.csproj'
git hash-object 'UI/AutoFrameJoint/AutoFrameJointSelectionPage.cs'
git hash-object 'Commands/AutoFrameJoint/AutoFrameJointReadOnlyProbe.cs'
git hash-object 'Commands/AutoFrameJoint/AutoFrameJointCommandEntry.cs'
Test-Path 'Commands/AutoFrameJoint/AutoFrameJointGeometryMath.cs'
Select-String -Path 'ADDIN.csproj' -SimpleMatch 'AutoFrameJointGeometryMath.cs'
Select-String -Path 'ADDIN.csproj' -SimpleMatch 'UI\BendSection2DCanvas.cs'
Select-String -Path 'ADDIN.csproj' -SimpleMatch 'UI\BendAssemblyBatchForm.UxV43.cs'
```

**Require EXACT source baselines**:
- current `ADDIN.csproj` = `004713797ea45906ad54cb805c727460e9fcf22a`;
- `UI/AutoFrameJoint/AutoFrameJointSelectionPage.cs` = `aa3a7c77fba634bb2e3f32db29daeea36c57cb9f`;
- `Commands/AutoFrameJoint/AutoFrameJointReadOnlyProbe.cs` = `f2700438cff8b7f3537bb1eab513dda1fa7b7956`;
- `Commands/AutoFrameJoint/AutoFrameJointCommandEntry.cs` = `e0bf7b1d2c76dbb73a08cc2e2b0f515c19400d70`;
- `AutoFrameJointGeometryMath.cs` does NOT exist;
- old `AutoFrameJointReadOnlyProbe.cs` Compile Include appears ONCE in .csproj; `GeometryMath.cs` appears ZERO times;
- two NEW Check chấn registrations ABOVE appear ONCE each. No unsupported missing .cs references.

If file hashes differ or another agent is currently writing the same files, STOP and report exact diff to ChatGPT. Never silently reset/change branch to force a match. No Build is required for this preflight.

## 2. Apply EXACT ChatGPT-authored C# from Runbook 43 commit `3c3bcdd2158b1944d4e6bc3ba368e67d1f195fbc`

Copy directly into the ACTUAL working project, not a detached worktree:

| Action | Full path relative to project | EXPECTED NEW Git SHA |
|---|---|---|
| CREATE | `Commands/AutoFrameJoint/AutoFrameJointGeometryMath.cs` | `b7ded5feba25adb8cc88c5b2bb7b0d407398bca6` |
| REPLACE EXACT | `Commands/AutoFrameJoint/AutoFrameJointReadOnlyProbe.cs` | `ecc8e68461a88fb36ca77655085cdc4c6b0f385a` |
| REPLACE EXACT | `Commands/AutoFrameJoint/AutoFrameJointCommandEntry.cs` | `6ca5ab2731964e8c483adfee0b6073f33dbdf5ed` |
| REPLACE EXACT | `UI/AutoFrameJoint/AutoFrameJointSelectionPage.cs` | `4dcfe9fd835b5c0acc45bef351f5ae3e9c41701a` |

Do NOT alter GhostPreview/GhostSessionManager or any Check Hole/Check chấn/Omote/Mirror/VCut code.

## 3. Edit ONLY ONE LINE inside current LOCAL `ADDIN.csproj`

**NEVER replace the complete `ADDIN.csproj`.** In the current local project preserve every existing byte/line/indent, except insert exactly this one registration after the existing, uniquely-matched anchor:

```xml
<Compile Include="Commands\AutoFrameJoint\AutoFrameJointReadOnlyProbe.cs" />
<Compile Include="Commands\AutoFrameJoint\AutoFrameJointGeometryMath.cs" />
```

The first line already exists. The second line is the **ONLY** project line to add (copy the indentation used by neighboring `Compile` lines). The two Check chấn rows `UI\BendSection2DCanvas.cs` and `UI\BendAssemblyBatchForm.UxV43.cs` MUST remain in the final project, along with all other 40+ Check Bend/Omote modules and other commands.

Anti calculates expected new **LOCAL** `ADDIN.csproj` Git blob:
`fdc7ae4f5b49c199e6d72c28f171e634d4e3caef`.

This is an ANTI-PREDICTED local value **not independently verified by ChatGPT**, since current local file bytes are unavailable remotely. If output SHA differs, STOP and provide the real before→after diff; do not force to an older `ADDIN.csproj` SHA by replacing other content.

Preserve:
- `Debug|x64 <OutputPath>C:\Tao moi\</OutputPath>`
- locally empty `PreBuildEvent` and `PostBuildEvent`.
- original XML encoding and line endings.

## 4. Mandatory verification, before ANY Build

```powershell
git status -sb
git diff --stat
git diff -- 'ADDIN.csproj'
git hash-object 'ADDIN.csproj'
git hash-object 'Commands/AutoFrameJoint/AutoFrameJointGeometryMath.cs'
git hash-object 'Commands/AutoFrameJoint/AutoFrameJointReadOnlyProbe.cs'
git hash-object 'Commands/AutoFrameJoint/AutoFrameJointCommandEntry.cs'
git hash-object 'UI/AutoFrameJoint/AutoFrameJointSelectionPage.cs'
(Select-String -Path 'ADDIN.csproj' -SimpleMatch 'AutoFrameJointGeometryMath.cs').Count
(Select-String -Path 'ADDIN.csproj' -SimpleMatch 'UI\BendSection2DCanvas.cs').Count
(Select-String -Path 'ADDIN.csproj' -SimpleMatch 'UI\BendAssemblyBatchForm.UxV43.cs').Count
```

Check:
1. Project before SHA = `0047137...`, new expected SHA = `fdc7ae4...` when same local file bytes and one-line insertion only.
2. `ADDIN.csproj` actual **before/after difference must be exactly ONE inserted Compile Include, ZERO deletions**. Because other worktrees/agents may have unstaged changes, compare a pre-change snapshot or use Git against its actual same-file baseline; `git diff` versus HEAD alone may include unrelated earlier edits.
3. Four C# hashes equal the ChatGPT source values above; exactly one GeometryMath Compile Include.
4. Both new Check Bend Compile Include lines still present exactly once, every other class unaffected.
5. Do not stage/commit/push/merge/reset any branch without separate authorization.

If any failure -> STOP and report precise problem to ChatGPT.

**ABSOLUTE HOLD:** NO automatic Build/Rebuild, F5, DLL/PDB deployment, SOLIDWORKS close/restart, registry change. Eventually, the USER manually builds Debug|x64 and this output writes directly into `C:\Tao moi\ADDIN.dll`, so preserve a safe DLL/PDB backup before proceeding.

## Status

The source is ready for **conditional** integration. The new local `ADDIN.csproj` has NOT been edited by ChatGPT in this remote environment, no C# has been built or host-tested, and manufacturing profile-specific trim logic is not yet implemented.
