# Runbook 46 — CS0558 fix: ChatGPT-authored C# / Antigravity exact copy only

## Authority / Scope

- **Code owner / author:** ChatGPT.
- **Implementer:** Antigravity. **Only copy and paste the ChatGPT-authored source into the existing local class file. Do not independently generate, rewrite, refactor, rename, or redesign code.**
- Repository: `tbcphutai-bit/ADDIN-SolidWorks`
- Fix branch: `fix/afj-r43-cs0558-public-operators-20261021`
- Exact corrected source commit: `a5c387b9408965ebcfed9e7b3725d15882858824`
- Corrected source file: [AutoFrameJointGeometryMath.cs (full 201-line class)](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/blob/a5c387b9408965ebcfed9e7b3725d15882858824/Commands/AutoFrameJoint/AutoFrameJointGeometryMath.cs)
- **Destination (existing class file, no new class):** `C:\Tao moi\ADDIN\Commands\AutoFrameJoint\AutoFrameJointGeometryMath.cs`
- Original Runbook 43 Git blob SHA: `b7ded5feba25adb8cc88c5b2bb7b0d407398bca6`
- Corrected ChatGPT-authored Git blob SHA: `c7750638196642927267734e9e81e2aaff99fe7a`
- No change to `ADDIN.csproj`, DLL/PDB, Debug x64 OutputPath, build events, other commands, or other C# files.

## Root cause of Visual Studio error

Visual Studio reports four `CS0558` errors at lines 16, 18, 20, and 22 of `AutoFrameJointGeometryMath.cs`. The C# user-defined operators were declared `internal static` but **must be `public static`**. The original defective code was already present in the Runbook 43 source committed by ChatGPT; **this is not evidence that Antigravity introduced the mistake**.

## ChatGPT-authored exact replacement code

Replace precisely these four existing operator declarations and retain all other members of the existing `AutoFrameJointGeometryMath` class unchanged:

```csharp
public static V operator +(V a, V b)
{ return new V(a.X + b.X, a.Y + b.Y, a.Z + b.Z); }

public static V operator -(V a, V b)
{ return new V(a.X - b.X, a.Y - b.Y, a.Z - b.Z); }

public static V operator *(V a, double t)
{ return new V(a.X * t, a.Y * t, a.Z * t); }

public static V operator /(V a, double t)
{ return new V(a.X / t, a.Y / t, a.Z / t); }
```

The **entire corrected 201-line source file** is available at the immutable commit link above. Do not create an extra helper class, copy into another project or rename the existing nested `V` struct. Its effective type visibility remains controlled by `internal static class AutoFrameJointGeometryMath` and `internal struct V`.

## Antigravity: copy-only instructions in the REAL local Visual Studio project

1. Work only in the currently open original folder `C:\Tao moi\ADDIN`. **Do not checkout/change Git branches or replace the local .csproj**: the local project contains independent Check Bend/Check Hole/Omote changes not included in the fix branch.
2. Read the **actual existing local** `Commands\AutoFrameJoint\AutoFrameJointGeometryMath.cs`. Before changing anything, check that the original four `internal static V operator` declarations appear **exactly once each** and all their bodies match the snippet above, except for the visibility keyword. If a single declaration or body is different, **STOP** and report to the code owner instead of overwriting.
3. Optional extra gate if local source is byte-for-byte the Runbook 43 version: `git hash-object -- Commands/AutoFrameJoint/AutoFrameJointGeometryMath.cs` should return original SHA `b7ded5feba25adb8cc88c5b2bb7b0d407398bca6`. If SHA differs due to concurrent work, BOM, or line endings, STOP and report exact local SHA and the four operator blocks; do not force overwrite.
4. **COPY**, do not author: use the full GitHub corrected source from commit `a5c387b...` and paste into **the existing local** `AutoFrameJointGeometryMath.cs` **only after the original baseline is verified**. Equivalently, replace only the four `internal static` tokens in the four declarations with `public static`, using the exact snippet supplied by ChatGPT above. Do not touch any other lines.
5. Read-only verify four `public static V operator` declarations, no `internal static V operator` declarations, unchanged class name/namespace/algorithms, 201 lines when using the same newline convention. If using identical bytes to the source commit, SHA should be `c7750638196642927267734e9e81e2aaff99fe7a`. **Do not alter BOM/CRLF just to force a hash match**; report a mismatch with exact differences.
6. Read-only report `git status --short`, `git diff -- Commands/AutoFrameJoint/AutoFrameJointGeometryMath.cs` (note a newly untracked class may not appear in git diff), before/after SHA, and whether any other local file changed. When this file is untracked, display the four verified operator declarations explicitly.
7. STOP after source-only copy and report **SOURCE FIX APPLIED — BUILD NOT TESTED**. The user decides when to back up DLL/PDB and manually rebuild in Visual Studio.

### Hard safety constraints

**NO BUILD, REBUILD, F5, RUN, DEPLOY, COM REGISTRATION, DLL/PDB OVERWRITE, SOLIDWORKS RESTART, OUTPUTPATH CHANGE. NO GIT COMMIT, PUSH, MERGE, RESET, CHECKOUT, STASH OR CLEAN IN LOCAL WORKTREE.**

Protect Check Bend, Check Hole, Omote, Mirror, DXF, Auto Frame Joint UI, shared add-in backend and all other files. Neither ChatGPT nor Antigravity has verified that the entire local `.NET Framework 4.8` solution compiles after the patch; these four corrected operators specifically address the four displayed `CS0558` compile errors.

## Completion report format

- Real local worktree, branch/HEAD, destination file
- 4 exact before/after operator signatures
- Pre-copy and post-copy Git blob SHA (plus any BOM/CRLF note)
- File changes detected, and confirmation all other files untouched by this task
- **NO BUILD/REBUILD/F5/DEPLOY performed**
- Final state: `SOURCE FIX APPLIED — BUILD NOT TESTED`
