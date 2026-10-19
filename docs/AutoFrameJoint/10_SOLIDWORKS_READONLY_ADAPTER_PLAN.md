# 10 — Next-step SOLIDWORKS read-only adapter (NOT approved to implement yet)

This page provides step-by-step API implementation criteria for a **later** phase. It is design material only; do not touch production Add-in in Phase 1.

## Existing source constraints (verified on GitHub)
- Existing `ADDIN.csproj` is classic MSBuild, .NET Framework **4.8** with `LangVersion 7.3` on x64. New `.cs` files **must** be explicitly listed as `<Compile Include="..."/> ` if eventually integrated.
- `SWAddin.cs` exposes `ISldWorks swApp` / task pane and must not be altered during research.
- Existing `Commands/CheckAssemblyHole.cs` already handles selection and geometry; reference existing patterns, but do not copy side-effecting marker/sketch functions into this read-only command.

## Proposed file boundaries, Phase 2 (separate feature branch)
```text
Commands/AutoFrameJoint/
  FrameSelectionReader.cs         // COM only; read selected occurrences
  FrameInstanceSnapshot.cs        // neutral DTO, immutable property getters
  FrameAxisAnalyzer.cs            // candidate axis + confidence
  SectionSampler.cs               // stable B-rep section readings
  FrameJointAnalyzer.cs           // invoke pure geometry + evidence
  FrameJointTrace.cs              // diagnostics to user-approved log location
Tests/AutoFrameJoint/
  ... adapter tests/mock data only ...
```
Review class names against source code to prevent collisions.

## Exact development task order
1. Verify `ISldWorks` in `SwAddin`; obtain current `ModelDoc2` with `swApp.ActiveDoc` and check doc type Assembly using correct interop enum.
2. Inspect `SelectionMgr` in local SW type library, retrieve exactly TWO **Component2 occurrences** (not just two Part file names), and capture `GetPathName()`, `ReferencedConfiguration`, component IDs, selection indexes and instance transforms. If user selected face/edge, resolve owning Component explicitly; stop if ambiguous.
3. Resolve lightweight/suppressed; **do not silently change document states**. When unavailable, report `UNSUPPORTED` with actionable reason.
4. Read body references without modifying document. If multiple solid bodies or geometry not loaded, report ambiguous. Do not assume bounding box in XYZ yields Frame axis.
5. Use the correct `MathTransform` mapping and SI-to-mm conversion; verify mirrored/det<0 instance and tests with points whose expected assembly coordinates are known. Cache only within one analysis run.
6. Extract candidate stable section(s) and axis with confidence, then call `JointGeometry.Analyze`. Until automatic axis detector exists, only allow explicit user-selected reference axes and label confidence as `USER_ASSIGNED`.
7. Serialize **analysis report only**, no sketch, feature, mate, hidden/suppressed component or CAD save.
8. Integrate into current TaskPane **only after user authorization**; initial mode = `Analyze only`.

## Warning: THIS IS NOT CODE THAT AUTOMATICALLY CUTS
The pure geometry executable uses **user-supplied finite axis spans**. It does not inspect topology and does not know the actual winner/loser without scoped design evidence. Do not use it to cut an arbitrary frame.

## SOLIDWORKS API verification checklist
Before Copilot writes an API call, confirm availability/signatures in the SW interop references installed locally. Candidates to inspect:
- `ISldWorks.ActiveDoc`
- `IModelDoc2.GetType()`
- `IModelDoc2.SelectionManager`
- `ISelectionMgr.GetSelectedObjectCount2`, `GetSelectedObject6`, `GetSelectedObjectsComponent4`
- `IComponent2.GetModelDoc2`, `GetPathName`, `ReferencedConfiguration`, `Transform2` / `GetTotalTransform`
- `IBody2` and B-rep face/edge traversal
- `MathUtility` and `MathTransform`

Do not assert compilation of unverified interop snippets. Keep COM read-only adaptation in a separate layer and tests independent of SOLIDWORKS.

## Read-only analysis output example
```json
{
  "caseId": "AFJ-3615-T1",
  "status": "NEEDS_REVIEW",
  "selectedOccurrences": ["componentA<1>", "componentB<1>"],
  "axisConfidence": ["UNKNOWN", "UNKNOWN"],
  "winnerSource": "KCC26003_SCOPE_UNCONFIRMED",
  "cutCandidates": [],
  "appliedFeatures": 0
}
```

If real production data differs from the sample, never invent values.
