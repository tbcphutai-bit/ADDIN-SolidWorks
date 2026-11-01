# Native U Sketch research milestones

**Research observed 2026-10-22; report updated 2026-10-23.** This supplements the post-load audit, preserving its README and historical DLL/source verification. Fresh disposable copies use A=2354 fr03 and B=fr04, retaining `ORTHOGONAL_U_TALLER_COVER` and the independent existing cutter targets. These are experimental native API harness results, not integrated Add-in behavior.

## Results and exact scope

| Native run | Recorded native result | Constraint observations | Actual Cut / reopen scope |
| --- | --- | --- | --- |
| `native_u_cover_master_05` | `PASS_NATIVE_FULL_COVER_SKETCH_ONLY_ONE_MARGIN_DIM_NO_CUT_NO_FOLLOW_TEST` | Cover status3, 28 relations, 0 dangling/overdefining, one ToolClosureMargin dimension, 0 new planes | Closed eight-vertex cover Sketch only; no Cut or parameter-follow test |
| `native_u_cover_cut_roundtrip_01` | `PASS_NATIVE_ONE_COVER_CUT_REBUILD_COPY_REOPEN_NOT_FULL_WRITER` | Cover status3, 28 relations, one dimension, 0 new planes | One actual cover Cut matched its target; explicit copy rebuild and fresh-copy Save/Read-Only reopen passed |
| `native_u_loser_seam_01` | `PASS_NATIVE_LOSER_SEAM_CORNERS_ONLY_ONE_GAP_DIM_NO_CUT_NO_FOLLOW_TEST` | Cover28 relations plus open losing seam/corner probe16; both status3; one margin plus one gap dimension | No closed losing polygon, Cut or follow test |
| `native_u_full_loser_01` | `PASS_NATIVE_FULL_LOSER_SKETCH_ONE_GAP_DIM_SHARED_MASTER_MARGIN_NO_CUT_NO_FOLLOW_TEST` | Cover28 + full loser36 relations; both status3, 0 dangling/overdefining, 0 new planes | Complete eight-vertex losing Sketch only; no losing Cut, reliefs or follow test |

The full Sketch pair has **two meaningful driving dimensions total**: `ToolClosureMargin=13 mm` in the master, and `AssemblyFitGap=0.10 mm` in the loser. Verified native Equal-length references share the master's margin; the loser does not dimension that margin again. The full losing Sketch has36 relations; the earlier open seam probe has16. These are measured native states for the bounded experiments, not claims about the original legacy writer or every U variant.

## Correct assembly-context entities

The qualified helpers create each Sketch through assembly edit context, then reacquire its entities from **the original Assembly-context Feature's `GetSpecificFeature2`**, matching original native IDs, type/construction state and seed geometry before adding relations. Reacquisition through the Part FeatureManager is only a later ownership check. Direct line-return handles and Part-context Sketch handles with numerically equal IDs are not interchangeable with the qualified Assembly-context handles.

The full losing Sketch references the actual master seam and the specific master margin guide through native relation definition-entity identity. It uses direct geometric relations without Convert Entities, blanket FIX or per-point coordinate dimensions. Some native Wall Face constraints refer to the **underlying plane** beyond the trimmed patch (`finite_trim=false`, about2.6 mm from the trimmed patch). These are datum-plane relations, not claimed physical contact on the finite Face.

## One cover Cut: before Save versus after reopen

The one-Cut experiment created a native Blind cover Cut with error0, depth60.20 mm, NormalCut=false, FlipSideToCut=false and reverse=false. It retained the reviewed physical start using start-offset type3, magnitude0.10 mm and offset_reverse=false. These are API readbacks for that exact operation, not a general direction rule or a single-sheet `t+0.10 mm` Cut.

**Before Save:** exact full B-rep symmetric difference was0 against the independent **single cover-operation** target after Cut, after explicit copy rebuild and after returning to the assembly. The consumed cover Sketch remained status3. This is not the completed four-Cut joint. The full solver candidate's0.10 mm gap is separate from the partial one-Cut experiment.

**After fresh-copy Save and Read-Only reopen:** both bodies passed Check2 with valid solid type; volume/area and face/edge counts were preserved. The cover volume delta was −2.168404344971009e−19 m³ with area delta0; the untouched long Part volume delta was6.505213034913027e−19 m³ with area delta0. The reopened cover Sketch stayed face-attached/status3 with28 relations, no dangling/overdefining/locked/broken relations, one margin dimension and0 new planes/errors. **Exact B-rep symmetric difference was not measured after reopen.** The post-reopen checks must not be reported as full B-rep equality.

Only fresh disposable copied Parts were saved. Their hash changes are expected; every source CAD hash stayed unchanged. The three Sketch-only runs left both source and copy hashes unchanged. All four runs closed their owned documents and restored a blank session.

## Not qualified by these runs

- A native losing Cut or the required bend-relief Cuts from these newly constrained Sketches.
- A complete four-Cut U transaction, prior Sketches after later topology changes, dependency cycles or owned-feature failure rollback for that transaction.
- Parameter changes producing relation-driven follow without rerunning the solver.
- Other widths, clamp/clipping/return variants or reversed selections.
- Existing Add-in writer integration, installed UI operation, smoothness or the cause of the earlier installed Part-open hang.

The earlier four-plane/status2 legacy Sketch failure remains valid. These bounded passes do not silently repair the production route or establish that the full losing Sketch has created a Cut.

## Artifacts

- [U_NATIVE_MILESTONES.json](U_NATIVE_MILESTONES.json): sanitized native observations and comparison boundaries.
- [U_NATIVE_MILESTONES.csv](U_NATIVE_MILESTONES.csv): one row per bounded experiment.
- [U_NATIVE_SKETCH_OBSERVATIONS.csv](U_NATIVE_SKETCH_OBSERVATIONS.csv): separate master, open-seam and full-loser counts.
- [U_NATIVE_SOURCE_MANIFEST.csv](U_NATIVE_SOURCE_MANIFEST.csv): helper copies and SHA256 provenance.
- `evidence/*.cs.txt`: latest qualified Assembly-context/reacquired helpers plus the one-Cut copy-round-trip caller. They depend on existing internal AFJ and harness support; they are research sources, not an Add-in replacement.

No CAD, serialized B-rep, private path, persistence payload, DLL/PDB or full native machine log is included. The report generator performed no COM, main Add-in source edit, Git operation, Build or Deploy.
