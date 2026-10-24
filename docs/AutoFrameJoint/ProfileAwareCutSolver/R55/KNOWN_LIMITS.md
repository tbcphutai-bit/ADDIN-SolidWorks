# Known limits and remaining verification

- Native four-corner PASS is scoped to the actual 2354 source snapshot, the recorded orthogonal U family and the same disposable Assembly copy. It is not a claim that every folded profile or every W23 corner is solved.
- Installed Add-in interaction was not run. The user still needs a manual Rebuild/reload and an installed UI selection → Preview → OK check.
- Display3 status zero is API evidence; the native audit records Preview visibility as `UNVERIFIED`. The separate viewport-only capture run reports `PASS_FOUR_PREVIEW_API_CAPTURES_NOT_NATIVE_APPLY_TEST_VISUAL_INSPECTION_REQUIRED` and is not a native Apply test. Separate private-image review status is `FOUR_STATIC_NATIVE08_API_PREVIEW_CAPTURES_VISUALLY_REVIEWED_NOT_INSTALLED_UI`. Images and CAD are intentionally excluded from publication.
- Cross-process native harness timing: cold Solve 24317–54706 ms; first Preview 26257–56293 ms; unchanged P1 repeat 147–279 ms; warm P1→P2 2699–3934 ms; warm P2→P1 317–525 ms. Cold/new-pair analysis remains slow. Warm cache measurements do not prove fast first Solve, installed Add-in responsiveness or freedom from freezing. Native Apply timings are also retained in TEST_MATRIX; they are not an installed Add-in benchmark.
- Exposed-area probes measure the stated axial Wall/Lip caps. Diagonal Web-edge exposure, total visibility from all directions and visual finish remain unverified.
- Unequal-width seams preserve the 45-degree projected diagonal plus shoulders. The current equal-width legacy branch still clamps a near endpoint to the inner-wall datum, so its seam is slightly non-45; a claim that every generated seam is an exact bisector would be incorrect.
- New Sketches have the reported geometric constraints and existing Face2 support. They still use the small documented shared gap/tool-closure dimensions; there is no claim of zero dimensions.
- The native generated cuts use their observed Blind depths/flags. `SheetCutOverrun` and `AssemblyFitGap` share the value 0.10 mm but have independent meanings. Native `NormalCut` flags are not inferred from an operation name.
- Existing fr04 Plane/Sketch/Cut 802/803/809 are preserved baseline history. Their presence is not a newly created R55 Plane.
- A bounded top-level Feature walk can encounter an identical repeated identity. The finite flat inventory and identity/order guards remain active; no arbitrary baseline Feature is deleted to pass.
- Failed native attempts and rollback outcomes are preserved in SANITIZED_FAILURES. A resumed exact checkpoint, if recorded, is a source/harness lineage rather than an independent fresh restart of every earlier operation.
- No document Save/Rebuild or disk reopen of modified generated Cuts is part of this run. In-memory B-rep/Feature validation does not prove post-save reopening stability.
- W23 prior metadata does not prove before/after removed material, complete Bend R clearance or successful native Apply. Its third-member ft1 corner requires separate context rules.
- No original CAD or placement transforms are published. Source hashes and aggregate feature IDs/counts support provenance without exposing CAD payloads.
