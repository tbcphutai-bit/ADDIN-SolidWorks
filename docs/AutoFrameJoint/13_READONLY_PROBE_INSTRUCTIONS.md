# 13 — Auto Frame Joint read-only C# Command spike

## What has been coded

Location: `Commands/AutoFrameJoint/AutoFrameJointReadOnlyProbe.cs`  
Project registration: `ADDIN.csproj` contains one new `<Compile Include="Commands\AutoFrameJoint\AutoFrameJointReadOnlyProbe.cs" />`.

**Status: implementation candidate, NOT compiled in a local SOLIDWORKS environment.**

Entry points:

```csharp
string report = ADDIN.Commands.AutoFrameJoint.AutoFrameJointReadOnlyProbe
    .AnalyzeSelected(swApp);

// Only when called from SOLIDWORKS UI thread:
ADDIN.Commands.AutoFrameJoint.AutoFrameJointReadOnlyProbe
    .Run(swApp); // shows one MessageBox
```

These methods are callable from existing Add-in C# logic when a UI action is wired. The new source file itself does **not** insert a toolbar or TaskPane button, because that would modify production UI and event handlers before the probe has been build-verified.

## What it actually does
1. Read-only: requires an active Assembly and exactly 2 selected component occurrences (can be selected Component2 or geometry within the Component).
2. Resolve each occurrence and its referenced Part document; FAIL SAFELY if lightweight, suppressed, unresolved, not a Part or active configuration mismatches referenced configuration.
3. Require one visible solid body per Part for this initial, deliberately limited spike.
4. Read each straight edge of that body, transform its vertices through Component2.GetTotalTransform(false) to Assembly coordinates.
5. Cluster longer straight edges by undirected parallelism, estimate a longitudinal axis and its support fraction.
6. Project sampled vertices onto that axis to get an **estimated span** in millimetres (not flat pattern length or necessarily end-to-end nominal Frame length).
7. Report provisional long/short and acute angle between axes, or NEEDS_REVIEW where ambiguous.
8. NEVER changes Part, Feature Tree, mates, selection, active configuration, document state or saves a file.

## What it does NOT do
- Does NOT automatically detect web/wall/lip, profile section loops, or manufacturing vết cắt.
- Does NOT determine 'Winner/Loser' (KCC 26003 only applies within its documented scope and may be overridden by the drawing).
- Does NOT infer panel inside/outside or the interior angle from just two undirected axes.
- Does NOT identify both tip locations for all possible assemblies, repeated Part configurations or composite bodies.
- Does NOT calculate a cut plane, generate preview bodies, or call any cut/trim API.
- Does NOT handle multi-body or model-config mismatch automatically. It returns NEEDS_REVIEW instead.
- Does NOT contain an actual TaskPane button yet. Copilot must report this clearly.

## Instructions to Copilot
Work on branch `feat/auto-frame-joint-readonly-probe-20261019` in an isolated clone/worktree, preserving all uncommitted work.
1. Read `Commands/AutoFrameJoint/AutoFrameJointReadOnlyProbe.cs` and its project registration.
2. Open project `ADDIN.csproj` in Visual Studio, inspect the installed SolidWorks Interop versions.
3. Execute one proper solution Build. If a compiler error occurs in the new file, fix it **without editing Check Hole/Bend, unrelated UI/backend files**.
4. Do not run `tools/AutoFrameJointResearch` numeric tests; this spike is independent of that prior test-loop.
5. After successful Build, ask user approval to add a single command launch point; DO NOT alter existing UI automatically.
6. Once a launch point exists and user opens a **copy of a test Assembly**, the user selects two Part occurrences and clicks Analyze.
7. Collect and report: 2 component names/paths/configs; visible solid count; inferred lengths/axes/support fraction; exact rejected reason; any COM exception.
8. If it returns NEEDS_REVIEW on real geometry, this can be a correct, safe outcome. No fitting or cutting of ambiguous cases.

**Important caveat:** IComponent2.GetModelDoc2 can return null on lightweight/suppressed parts and can reference an unexpected active configuration; code intentionally avoids changing that state. Use local type library/Object Browser before adjusting version-specific API signatures.

## Suggested minimal future UI hook (not added by this commit)
After Build passes and user approves an integration commit, connect a temporary button in the existing TaskPane to:

```csharp
private void btnAutoFrameAnalyze_Click(object sender, EventArgs e)
{
    // When called from SolidWorks UI thread, in current Add-in context:
    ADDIN.Commands.AutoFrameJoint.AutoFrameJointReadOnlyProbe.Run(swApp);
}
```

Replace `swApp` with the actual existing ISldWorks field/property in that form; do not introduce duplicate application instances. Do not let Antigravity overwrite Codex's backend code.

## One-time manual sanity check in SOLIDWORKS
1. Open an **Assembly copy**, select two real Frame Part occurrences (no need to save).
2. Execute Analyze (after a launch button is approved and wired).
3. Verify which Part was called A/B, compare displayed estimated length with SOLIDWORKS Measure, and note whether the estimated axis is correct.
4. Test one rotated pair and one different profile. Report WRONG_AXIS explicitly rather than trusting a high support fraction.
5. Report to ChatGPT before implementing geometry cuts.

This is progress toward a real command, not proof that an arbitrary profile can be cut correctly.
