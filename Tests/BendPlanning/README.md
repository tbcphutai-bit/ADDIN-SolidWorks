# Bend Planning validation

The managed tests link the production `Commands/BendCheckPlanningDraft.cs`
with a small managed row/context contract. They require .NET Framework 4.8
MSBuild, but **no SOLIDWORKS installation, PIAs, CAD, or test packages**.
They do not test the minimum-dimension algorithm using a substitute evaluator;
that comparison is performed separately against the real product assembly.

```powershell
& $MSBuild Tests/BendPlanning/BendPlanning.Tests.csproj /t:Rebuild /p:UseSharedCompilation=false
& Tests/BendPlanning/bin/BendPlanning.Tests.exe
```

Cases cover 0–9 bends, complete/truncated/reverse permutations, unique identity,
null/malformed rows, optional/duplicate persistence, immutable input snapshots,
stale context, evaluator exceptions, non-finite measurements, and JSON escaping
and culture. Check Min status is retained independently of the always-unverified
manufacturing status.

## Native Windows checks (SOLIDWORKS 2024)

Build the **product in Debug/x64** into a private output directory first. Disable
the existing pre/post-build events so the build does not kill SOLIDWORKS or
replace the installed add-in. For example, from the repository root:

```powershell
$Out = Join-Path $env:TEMP 'CheckChan-Issue4'
& $MSBuild ADDIN.csproj /t:Rebuild /p:Configuration=Debug /p:Platform=x64 `
  /p:UseSharedCompilation=false /p:KillSolidWorksOnBuild=false `
  /p:PreBuildEvent= /p:PostBuildEvent= /p:RunPostBuildEvent=Never `
  "/p:OutputPath=$Out/product/" "/p:BaseIntermediateOutputPath=$Out/product-obj/"

& $MSBuild Tests/BendPlanning/NativePlanning.Validation.csproj /t:Rebuild `
  /p:UseSharedCompilation=false "/p:OutputPath=$Out/native/" `
  "/p:BaseIntermediateOutputPath=$Out/native-obj/"
& "$Out/native/NativePlanning.Validation.exe" "$Out/product/ADDIN.dll" `
  "$Out/private-part" $PrivatePartPath $ExcelPath planning changes
```

PIAs default to `USERPROFILE/Downloads`; override
`/p:SolidWorksInteropDirectory=...` if necessary. SOLIDWORKS API activation must
be permitted by the local Windows security context. The harness refuses to use
an application whose process already existed before its activation.

The harness opens the existing production `BendCheckForm` (the same constructor
used by the current Check chấn button), runs its actual capture/timer/Excel/detail
path on STA, and creates no alternative command. It does not register or replace
the installed DLL. It compares the rows and planning snapshot with native bend
definitions and cylindrical face owners, including ID/name/angle/R/t/persistence.
It parses the actual `[BEND PLAN DRAFT]` debug records as JSON.

Only a private **copy** of `$PrivatePartPath` is opened. The optional `changes`
argument tests sketch editing, rebuild, configuration changes, automatic rescan,
shared-context invalidation and an injected failing planning debug listener.
Where no driving linear sketch dimension is exposed, the edit adds construction
geometry instead. The original Part hash must remain unchanged. A baseline build
without planning is supported by replacing `planning changes` with `main`.

**Outputs contain private model identifiers and measurements. Keep the entire
output directory outside the repository and do not upload the reports, debug
output, screenshots, Parts or assemblies.** Compare the local `initial.minimum`
JSON arrays from baseline/current runs to check Min regression.

## Synthetic L / U / Z

```powershell
& $MSBuild Tests/BendPlanning/NativePlanning.Validation.csproj /t:Rebuild `
  /p:SyntheticFixtures=true /p:UseSharedCompilation=false `
  "/p:OutputPath=$Out/synthetic-runner/" "/p:BaseIntermediateOutputPath=$Out/synthetic-obj/"
& "$Out/synthetic-runner/SyntheticPlanning.Validation.exe" "$Out/product/ADDIN.dll" `
  "$Out/synthetic-fixtures" $ExcelPath $PartTemplatePath
```

These fixtures use artificial open-profile sections and the native base flange
API. They have native Feature Trees and cylindrical bend faces. Expected counts
are L: 1 bend / 1 candidate; U: 2 / 2; Z: 2 / 2. Each two-bend case must contain
both reverse orders, all `CHUA_XAC_MINH`. The existing Min graph must recognize
L, C(U), and Z respectively. Fixture material is unspecified: these tests verify
inventory, section identity and permutations, **not a manufacturing Min pass**.

No native fixtures are checked into this repository. Limits remain ≤24 candidates
for ≤8 bends; `Exhaustive` refers only to enumerating permutations, never to
verifying feasibility.
