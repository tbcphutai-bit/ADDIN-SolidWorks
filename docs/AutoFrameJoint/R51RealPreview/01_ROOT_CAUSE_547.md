# Root cause investigation — 547

## Source identity

The user's installed DLL MVID was read without executing it: `618f352b-0a78-4ca9-a2e6-0e0a56ef4f74`, matching R51's supplied debug evidence. R50 UI source hash was `17a7cc2e448e2d71c5fc88af19fc99f1a710773e541a1b84de0e66ca47d919fe`.

The active local backend SHA-256 before repair was `f0fcda50d4852e715f353f9787d41d6c7d26de1ca4824e2b56752b6976c1584c`. It contained `BooleanParts`, `SubtractMaterial` and `CreateBisectorHalfBox`; it did not contain PR #40's rule engine. The current working branch/HEAD and existing unrelated changes were recorded privately before editing.

## Reproduction on temporary bodies

The 2354 component bodies, their shared solid and both bisector-clipped portions were temporary solids with `Check2=0`. All edges inspected had two adjacent faces. This does not prove a subsequent Boolean will produce a manifold result. No documented scalar edge-tolerance getter was used; numeric edge tolerances remain `NOT_AVAILABLE`.

| Operation | Error | Result |
| --- | --- | --- |
| A intersect B | 0, `swBodyOperationNoError` | One valid solid |
| Shared intersect positive half-box | 0 | One valid solid |
| Shared intersect negative half-box | 0 | One valid solid |
| A minus its clipped shared material | **547, `swBodyOperationNonManifold`** | Failed |
| Same A diagnostic with one ResetEdgeTolerances trial on fresh copies | **547** | Failed; no repeat |
| B minus its clipped shared material | 0 | **Three** valid solids; unsuitable as one connected Frame |

The shared-volume cutter inherits target/tool surfaces and exact coincidence. Clipping it by an estimated bisector can leave edge/point connections. The measured operation failure, unchanged reset result and three-piece B output establish that this construction is unsuitable for this case. They do not identify a unique faulty native edge or prove a particular tolerance was corrupt.

The additional native-like plain-U research proposal also returned 547. It is recorded as a rejected experiment and is not part of the delivered solver. The accepted approach uses independently generated volumetric tools and the current shorter-cover policy.

## Other causes checked

- `Operations2` consumes its operands. Production code passes fresh copies and does not query or reuse the consumed copies on either success or failure.
- Input and output solids are checked with `Check2` and `Check3`. Positive volumes, one connected kept body/member, mass conservation, required KEEP coverage, selected-end confinement, interference and minimum distance remain enforced.
- Exact profile/skin geometry replaces the rounded bisector data from the read-only report. The estimated axis gap of 2.455 mm is not used as an assembly material clearance.
- `ResetEdgeTolerances` is not a production fallback. Error 547 is logged by enum and rejected. No retry loop, relaxed validator or original-body fallback is used.

## Corrected geometry assumptions

PR #40's profile-origin end choice assumed the base Sketch represented the Frame center. 2354 uses one-sided stock: the profile plane is at a stock end. The solver now centers that reference along the **actual B-rep span**, then chooses the end toward the other Frame. Exact body extrema supply that span; a bounding box does not define the cut region.

The Boolean `ReverseThickness` also depends on Sketch orientation. The 2354 getter is false, while its measured skins match the inward material convention used by the rule. R51 requires actual Web planes 0/t, Wall planes 0/t/W−t/W and four base Bend radius/center matches, rather than using that flag as a geometry classifier.

## Native stability evidence

The initial PID-specific attachment used an existing SOLIDWORKS 2025 session. After the diagnostic work, Windows recorded its exit with `0xC0000374` (heap corruption). Another SOLIDWORKS 2024 process also has an error event. The precise cause and relationship of these events to individual calls are `UNVERIFIED`. A later study harness initially found no visible session and exited before CAD work; that failed run is retained.

The user opened a fresh session. Subsequent tests attached the known SOLIDWORKS 2024 PID through its ROT moniker. Multiple visible sessions later existed; the harness used that same explicit PID and did not activate or close another application instance. A functioning API result does not establish general native-process stability.

## Official references

- [swBodyOperationError_e](https://help.solidworks.com/2024/english/api/swconst/SolidWorks.Interop.swconst~SolidWorks.Interop.swconst.swBodyOperationError_e.html?id=2.10.1.94) defines 547 as non-manifold.
- [Operations2](https://help.solidworks.com/2011/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IBody2~Operations2.html) documents temporary operands, their invalidation and validity checks.
- [ResetEdgeTolerances](https://help.solidworks.com/2026/English/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.IBody2~ResetEdgeTolerances.html) is a documented diagnostic option after a non-manifold error; it did not resolve this reproduction.
