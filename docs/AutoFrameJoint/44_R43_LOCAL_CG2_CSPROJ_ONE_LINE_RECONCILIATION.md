# Runbook 44 — Resolve LOCAL CG2 ADDIN.csproj divergence WITHOUT deleting Check Bend / Omote
## Code Owner decision (2026-10-21)

**DECISION:** Choose **A with an exact one-line, gated insertion**; NOT B and NOT a wholesale file replacement.

**Target:** The currently opened VS 2026 project at `C:\Tao moi\ADDIN\ADDIN.csproj`.  
**Source code:** ChatGPT's **Runbook 43** commit `3c3bcdd2158b1944d4e6bc3ba368e67d1f195fbc`.

Anti's issue #31 comment **6311399326** reports local actual branch `feat/check-hole-cg2-part-relations`, HEAD `66d1251`, local `ADDIN.csproj` Git blob `2d3c1d4393bf613c29a3e58d5c5a19720a49f558`. It contains many extra Check Bend / Omote class registrations compared with our earlier AFJ baseline. **ChatGPT has NOT independently read these local bytes**, and this blob was not available through GitHub. Thus do NOT use the full Runbook43 `ADDIN.csproj` file or assert every registration has been reviewed. The target local hash must be gated before the change.

## Step 1: Gate local baselines, NO modifications yet

In the **actual Visual Studio project folder** (NOT a new worktree):
```powershell
Set-Location 'C:\Tao moi\ADDIN'
git status -sb
git rev-parse HEAD
git hash-object 'ADDIN.csproj'
git hash-object 'UI/AutoFrameJoint/AutoFrameJointSelectionPage.cs'
git hash-object 'Commands/AutoFrameJoint/AutoFrameJointReadOnlyProbe.cs'
git hash-object 'Commands/AutoFrameJoint/AutoFrameJointCommandEntry.cs'
```

Proceed ONLY if exact current hashes are:
- `ADDIN.csproj`: `2d3c1d4393bf613c29a3e58d5c5a19720a49f558` (**real local CG2 baseline as reported by Anti**).
- `AutoFrameJointSelectionPage.cs`: `aa3a7c77fba634bb2e3f32db29daeea36c57cb9f`.
- `AutoFrameJointReadOnlyProbe.cs`: `f2700438cff8b7f3537bb1eab513dda1fa7b7956`.
- `AutoFrameJointCommandEntry.cs`: `e0bf7b1d2c76dbb73a08cc2e2b0f515c19400d70`.
- `AutoFrameJointGeometryMath.cs`: must NOT already be present; if present, inspect its hash; do not overwrite blindly.

If ANY baseline differs, STOP and send full `git status -sb`, hash and exact changed class lines to ChatGPT. No reset, merge, checkout or sweeping replace.

## Step 2: Apply the **exact C# source authored by ChatGPT** from Runbook43, DIRECTLY to the currently open project

1. **Create** `Commands/AutoFrameJoint/AutoFrameJointGeometryMath.cs` from commit `3c3bcdd`. Expected new Git blob `b7ded5feba25adb8cc88c5b2bb7b0d407398bca6`.
2. **Replace only** `Commands/AutoFrameJoint/AutoFrameJointReadOnlyProbe.cs` with exact Runbook43 C#. New blob `ecc8e68461a88fb36ca77655085cdc4c6b0f385a`.
3. **Replace only** `Commands/AutoFrameJoint/AutoFrameJointCommandEntry.cs` with exact Runbook43 C#. New blob `6ca5ab2731964e8c483adfee0b6073f33dbdf5ed`.
4. **Replace only** `UI/AutoFrameJoint/AutoFrameJointSelectionPage.cs` with exact Runbook43 C#. New blob `4dcfe9fd835b5c0acc45bef351f5ae3e9c41701a`.

This Runbook43 version **already includes all Runbook42 auto-analyze improvements**. Do not independently reapply Runbook42/older patches after copying.

## Step 3: Only one-line insertion in the EXISTING LOCAL `ADDIN.csproj`

**DO NOT copy the GitHub `ADDIN.csproj` blob `398085ff348d153c011c3e282495a2a8488c44d8`.** That is from an older CG2 base and is not valid for the current local worktree.

Find this EXISTING anchor in the LOCAL project, which is already compiled:
```xml
<Compile Include="Commands\AutoFrameJoint\AutoFrameJointReadOnlyProbe.cs" />
```

Insert EXACTLY ONE ADDITIONAL line immediately after it:
```xml
<Compile Include="Commands\AutoFrameJoint\AutoFrameJointGeometryMath.cs" />
```

For clarity, the two consecutive `Compile` elements are now:
```xml
<Compile Include="Commands\AutoFrameJoint\AutoFrameJointReadOnlyProbe.cs" />
<Compile Include="Commands\AutoFrameJoint\AutoFrameJointGeometryMath.cs" />
```

**IMPORTANT:**
- The anchor must appear exactly once, `GeometryMath.cs` must have ZERO `Compile Include` occurrences before adding it, exactly ONE after.
- Preserve original file encoding, line endings, indentation, **every other line** and project configuration.
- Keep ALL Check Hole, Check Bend, Omote, VCut, Mirror, DXF, Ghost, TaskPane and other existing items/conditional configs verbatim.
- Preserve `Debug|x64` `<OutputPath>C:\Tao moi\</OutputPath>` exactly.
- Keep `PreBuildEvent` and `PostBuildEvent` empty, as observed by Anti in the current local project. Do not create automatic Build/deploy/copy/kill steps.
- Anti's report predicts a post-insertion local project Git blob of `16ee932a0f83147dd50f8127f5579b64ec5a4cea`. **That predicted hash is NOT independently verifiable by ChatGPT on GitHub**; confirm locally and stop on mismatch (encoding/whitespace/pre-existing changes), rather than rewriting the entire project.

## Step 4: Verify live worktree without building

Run:
```powershell
Set-Location 'C:\Tao moi\ADDIN'
git status -sb
git diff --stat
git diff -- 'ADDIN.csproj'
git hash-object 'ADDIN.csproj'
git hash-object 'Commands/AutoFrameJoint/AutoFrameJointGeometryMath.cs'
git hash-object 'Commands/AutoFrameJoint/AutoFrameJointReadOnlyProbe.cs'
git hash-object 'Commands/AutoFrameJoint/AutoFrameJointCommandEntry.cs'
git hash-object 'UI/AutoFrameJoint/AutoFrameJointSelectionPage.cs'
(Select-String -Path 'ADDIN.csproj' -SimpleMatch 'AutoFrameJointGeometryMath.cs').Count
```

Accept only:
- Four C# file Git blob SHAs match the exact Runbook43 values in Step 2.
- The `ADDIN.csproj` edit is literally **one added Compile Include line; no deletions** relative to the pre-edit actual LOCAL file. On a dirty branch, review the real pre/post working-file diff, not merely `git diff` against HEAD.
- New local `ADDIN.csproj` hash matches the predicted `16ee932a0f83147dd50f8127f5579b64ec5a4cea` *if local bytes/encoding are unchanged*; if not, STOP and report actual hash and exact diff to ChatGPT.
- `GeometryMath.cs` compile registration count is 1.
- No other tracked source file was altered by this task.

Report actual `git status`, before/after hashes, `git diff` and any warnings to ChatGPT.

**ABSOLUTE HOLD:** NO Build / Rebuild / Visual Studio F5, DLL/PDB overwrite/deploy, COM registration, SOLIDWORKS close/restart, branch reset/merge/commit/push without explicit user permission. An eventual **user-initiated** Debug|x64 Build writes directly to `C:\Tao moi\ADDIN.dll`; back up both installed DLL and PDB and safely end current debug first.

## Status

This is a **source-only reconciliation handoff**. It does not assert geometry engine compilability, host performance or correct manufacturing cut. Runbook43 computes candidate joint geometry; there are no actual Web/Wall/Lip trims or Part changes.
