# Small line-context matrix probe

`AfjLineContextMatrixProbe.cs` is independent of the Add-in source and solver. It compiled successfully as a private library against the installed SOLIDWORKS Interop assemblies using the .NET Framework64 compiler. The author did not load SOLIDWORKS or execute the probe.

```csharp
AfjLineContextMatrixProbe.Result result = AfjLineContextMatrixProbe.Run(
    app, copiedAssemblyModel, copiedTargetComponent, freshCopyRoot,
    trySingleHorizontal: false,
    stageSink: stage => WritePrivateProgress(stage));
```

The caller owns the blank isolated process guard, opening copied files, original/copy hash checks and closing copied documents without Save. The helper independently checks the target Part and Assembly paths are inside `freshCopyRoot`. It does not activate documents, invoke a solver, make Cuts, create dimensions, rebuild, save, FIX or Convert.

## Observations

The helper obtains one unique base-profile Web and actual outer-Web Face. After opening the in-context Sketch through the Assembly manager, it creates one construction line through the Assembly SketchManager and another at a different position through the Part SketchManager. It reports each native segment's ID, owner Sketch ID, type and coordinates.

Inputs include direct created segments, Assembly→Part normalization through Part.Extension.GetCorresponding, Part→Assembly mapping through Component.GetCorresponding, and reacquisition from the owned Feature's specific native Sketch. Corresponding native-owned segments are also mapped to Assembly context. It queries the Part-active, Assembly-active and owned-Feature-specific Sketch relation managers separately. The first Assembly-created input is queried both as a DispatchWrapper array and raw object array; the other inputs use DispatchWrapper to keep the test bounded.

Every input is selected once through Select4, then **both Assembly and Part SelectionManager counts** are recorded. A successful Select4 return alone is not reported as a selected entity in the required context.

Optional `trySingleHorizontal=true` makes at most one Horizontal relation request, only if that exact manager/input query advertised it. The result includes actual native relation type and relation-count changes. No Parallel or external relation is attempted by this diagnostic. A native void selection command is not used as a success proxy.

All result classes expose public primitive fields/lists. No COM objects, persistent-reference tokens or machine path fields are included. Exception strings remain private diagnostic input and should still pass the publisher's normal sanitization.

Success status is an **observation completion**, never a geometry solver PASS: `MATRIX_COMPLETED_OBSERVATION_ONLY` or `MATRIX_COMPLETED_WITH_LINE_CREATION_FAILURE`. There is no fully constrained or permanent-cut claim. Stage callbacks run before each boundary to identify a blocked native call. Native Sketch ownership, dimensions/new planes and restored edit context are checked before returning. Sketch/edit context cleanup runs in `finally`.

## Compilation evidence

- Source SHA256: `3B3E63162EC6A38482B84A50943A9E1BC67C597DF67568DB591291001E615A6E`
- Private DLL SHA256: `3B7D3C074388DA9983B358AC967C0102086FDDC6F15B2A65AEBC6D163DA1B0A8`
- Native execution by author: `NOT_RUN`.
- Add-in project Build/Deploy: `NOT_RUN`.

The primary object-context APIs are documented by [Component.GetCorresponding](https://help.solidworks.com/2022/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IComponent2~GetCorresponding.html) and [ModelDocExtension.GetCorresponding](https://help.solidworks.com/2012/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IModelDocExtension~GetCorresponding.html). The helper's context matrix remains unqualified until native results are recorded.
