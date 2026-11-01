# Native rollback diagnostics — 2026-10-23

Copy-only standalone API harnesses. **No owned-feature rollback is qualified by these runs.** No production writer integration or installed Add-in result is claimed.

| Run suffix | Registered Sketches / verified native Cuts | Observed outcome |
|---|---:|---|
| Rollback02 | 4 / 4 | Controlled failure after four Cuts. Journal stopped before deleting anything because an original Assembly Feature was absent from enumeration; holder count/IDs were not captured in this run. |
| Rollback03 | 1 / 0 | Controlled failure after registering the first empty Sketch. Part A/B trees returned to baseline. Assembly enumeration omitted 13 original `InContextFeatHolder` IDs; no rollback deletion. |
| Rollback04 | 1 / 0 | Same bounded missing-holder observation with V3 persistence diagnostics; Part A/B trees returned to baseline. No rollback deletion. |
| Rollback05 | 4 / 4 | Native Cuts and recorded Sketch/B-rep checks completed. Explicit Assembly rebuild returned true; the following V3 diagnostic failed with RPC error and the owned SOLIDWORKS process faulted. `Journal.Rollback` was not entered. |
| No-Sketch context control01 | 0 / 0 | All 77 original Assembly IDs and exact Part trees/B-reps remained. Raw FAIL is retained because exact JSON mass-property comparison failed on floating-point roundoff. |
| Empty-Sketch control01 | One empty Sketch / 0 | Native actual-Face Sketch open/close reproduced the 13 missing Assembly holder IDs without AFJ solver, Cut or explicit rebuild. Parts were unchanged; all source/copy hashes and blank-session cleanup were verified. |

The 13 missing IDs in runs03/04 are **424, 425, 435, 437, 438, 439, 440, 441, 443, 446, 447, 448, 449**, all `InContextFeatHolder`. Their absence is an API enumeration observation; it is not proof that original CAD data was deleted. Run04's persistent-reference resolution state is **1 (`swPersistReferencedObject_Invalid`)**, with null resolution. **Deleted is state 4**, so do not label these observations as Deleted. No token bytes are published.

## Process fault in run05

The owned `SLDWORKS.exe` process 21232 (`0x52f0`) has a correlated Windows event at **2026-10-23 12:30:53 Asia/Bangkok**: `ntdll.dll` version `10.0.26100.9711`, exception `0xC0000374`, offset `0x0000000000118095`.

The managed diagnostic exception records `0x800706BE` at `ISldWorks.IsSame` inside V3 `WritePrivateSnapshotDiff / Read`, after the explicit Assembly rebuild returned true. Cleanup then recorded `0x800706BA`; blank-session cleanup could not be verified. Source and copied CAD file hashes remained unchanged because no Save was called. The observed call stack and event timing **do not establish that IsSame or the rebuild caused memory corruption**, and do not prove the cause of the user's earlier installed Part-open hang.

## Keep geometry and transaction results separate

Runs02/05 completed four native Cuts with their recorded exact B-rep and fully constrained Sketch checks before the controlled rollback/diagnostic stage. Those measurements remain visible; they do not establish successful rollback. The earlier, separate `native_u_four_cut_roundtrip_02` copy rebuild/save/reopen qualification is retained in [U_FOUR_CUT_ROUNDTRIP.json](U_FOUR_CUT_ROUNDTRIP.json), with its original comparison limits.

The interrupted rollback01 attempt remains separately labelled [INTERRUPTED_NO_FINAL_RESULT](U_PARAMETRIC_ROLLBACK_INTERRUPTED20261023.json); no partial count, PASS or geometry failure is inferred from it.

The no-Sketch context control's numerical assertion was corrected in a separate V2 candidate using explicit absolute/relative tolerances. Its saved numbers can be examined offline, but the original native FAIL is not rewritten as PASS. A corrected control02 candidate has not been run by this report.

## Empty-Sketch control and fixture binding

The completed SAFE control records one 2D Sketch on an actual original Face, with **0 segments, 0 relations and 0 dimensions**, immediately closed by its verified new active Sketch ID. Its status remains `EMPTY_SKETCH_CONTROL_COMPLETED_PARTS_UNCHANGED_ASSEMBLY_TREE_CHANGED`, not a rollback PASS. No solver, Cut, explicit rebuild, Save or rollback was called.

Before the first Sketch mutation, all 13 original Assembly holder tokens already returned **Invalid (1)** with null resolution. Their API external-reference rows were **OutOfContext (status 4 in the external-reference status namespace)** and still pointed to **ORIGINAL_ASSEMBLY_CONTEXT**, outside the copied **TEST_ASSEMBLY_CONTEXT**. Private context paths and document names are replaced by these roles. External-reference status 4 is distinct from persistent-reference Deleted state 4; they belong to different API enumerations.

After the empty Sketch lifecycle, Part A retained all 37 original Feature rows and Part B all 40; exact B-rep differences were zero. The recorded qualified external-reference signature comparisons were identical for those 37/40 Part rows and the 64 remaining non-holder Assembly rows. These are bounded API fingerprints, **not a mathematical proof that every source reference is preserved**, nor a conclusion that disappearing metadata is harmless for all models.

The next qualification uses a copied Assembly package that remaps references into TEST_ASSEMBLY_CONTEXT, then repeats the strict rollback gates. These gates have not been relaxed. The empty-Sketch control isolates the observation from the AFJ rules; it does not by itself explain the process fault or the user's installed Part-open hang.

## Evidence and provenance

The JSON contains only allowlisted primitive results, actual native Cut parameters, Sketch counters, generic feature types/IDs, scope counts, final protection/cleanup flags and private evidence SHA-256. API readbacks, geometry checks and unverified causal inferences are explicitly separated. No private path, CAD, native coordinates, transforms, names identifying private documents, persistence bytes, DLL/PDB or full machine logs are published. Unfrozen current harness source is not substituted for historical compile provenance.

- [U_ROLLBACK_DIAGNOSTICS20261023.json](U_ROLLBACK_DIAGNOSTICS20261023.json)
- [U_ROLLBACK_DIAGNOSTICS20261023.csv](U_ROLLBACK_DIAGNOSTICS20261023.csv)
