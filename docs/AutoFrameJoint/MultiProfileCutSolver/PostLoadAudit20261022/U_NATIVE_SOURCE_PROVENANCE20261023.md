# Native prototype source provenance — 2026-10-23

These are standalone experimental native API harnesses. Their compiled helpers are not evidence that the deployed Add-in contains this new writer, and their passing results do not qualify installed UI performance or the Part-open hang.

| Evidence group | Source provenance | Scope |
|---|---|---|
| Native line-context matrix | `evidence/AfjLineContextMatrixProbe.cs.txt` and `evidence/LINE_CONTEXT_MATRIX_PROBE_NOTES.md` | Diagnoses handle/document context; its underconstrained diagnostic Sketch and recorded Part-manager null result remain explicit. |
| Full cover / single native cover Cut / loser seam / full loser | `U_NATIVE_SOURCE_MANIFEST.csv` | Exact public helper/caller copies; private and public source SHA-256 are compared. |
| Two cap Sketches and width-follow V3 | `U_NATIVE_FOLLOW_CAPS20261023_SOURCE_MANIFEST.csv` | Uses the actual compiled `.cs` helpers/callers, including the corrected V2 cap helper; the earlier raw `.cs.txt` candidate with a compiler variable-shadow error is not substituted for the compiled source. |
| Four native Cuts with copy rebuild/save/reopen | `U_FOUR_CUT_ROUNDTRIP_SOURCE_MANIFEST.csv` | Frozen run02 harness plus three actual helper files; each manifest entry includes frozen and public-copy SHA-256 and the current-vs-frozen comparison. |
| Interrupted parametric writer / owned rollback | `U_PARAMETRIC_ROLLBACK_INTERRUPTED20261023.json` | Only progress/log fingerprints and interruption state; no completed writer/rollback qualification or final source identity is asserted from that run. |

## Shared compilation baseline

The detached harness compilations use the six preexisting AFJ source snapshots listed in `U_NATIVE_COMPILATION_BASELINE20261023.csv`, plus SOLIDWORKS Interop references and the process-attachment helper. The new helper manifests identify the experimental caller/helpers; they are not by themselves a complete redistributable build environment. No Interop binary, Add-in DLL/PDB or CAD is published.

`DLL_VERIFICATION.json` separately verifies the user's loaded Add-in MVID and compares its 459 AFJ methods with the independently compiled local-source baseline. The new native prototype results do not alter that existing DLL verification or imply that any prototype has been built/deployed into the Add-in.

## Evidence boundaries retained

- Four-Cut run01 remains the original harness `KeyNotFoundException` before any native Cut; it is not rewritten as a geometry failure or replaced by run02.
- Four-Cut run02 created four native Cuts. Its fifth plan-operation record was an empty near-bend subtraction, skipped without creating a feature.
- Four-Cut exact geometry checks apply before Save/close at their recorded checkpoints. After Read-Only reopen, the checks cover body health, volume/area and reacquired native Sketch/reference state; exact post-reopen B-rep equality was not measured.
- Width-follow V1/V2 historical FAIL records remain visible. V3 checks both endpoint sets and restores the original dimension; it establishes Sketch following only, without a solver rerun or a native Cut variant.
- Interrupted rollback01 remains a separate `INTERRUPTED_NO_FINAL_RESULT` observation. It supplies neither PASS nor FAIL geometry/rollback evidence.

All public artifacts are text files. They exclude machine-specific paths, CAD geometry/coordinates, transforms, native COM persistence payloads and binary output.
