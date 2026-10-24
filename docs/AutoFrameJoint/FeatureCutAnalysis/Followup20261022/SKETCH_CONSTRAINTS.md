# Native Sketch support and constraints

## New 2354 end-section writer: observed results

| API observation | Short Frame A | Long Frame B |
| --- | ---: | ---: |
| Native Cut count | 1 | 1 |
| Initial Sketch support | Existing selected end Face | Existing selected end Face |
| Constrained status | 3, fully constrained | 3, fully constrained |
| Relations | 34 | 16 |
| Native-vertex Coincident relations | 24 | 12 |
| Tangent relations | 10 | 4 |
| Driving dimensions | 0 | 0 |
| New reference planes | 0 | 0 |
| Native boundary arcs | 10 | 4 |
| Native Cut vs reviewed body difference | 0 m³ | 0 m³ |

These are `API_VERIFIED` constraint/feature observations and `GEOMETRY_VERIFIED` body comparisons from `native_end2354_multi_02`. The path uses direct geometric relations; it does not call Convert Entities or introduce a dimension/FIX for each endpoint.

The direct Coincident targets in this writer are vertices of the selected Frame's native end section. Tangent relations preserve the section's native circular bends. This must not be described as a complete set of cross-Frame Sketch constraints.

## Support readback after Cut

The initial Sketch reference type was 2. After feature creation, the observed reference getter returned type 0 and did not directly expose a Face. The tested qualification therefore used the original supporting feature in the Sketch's parents, recovered support ownership and an unchanged Sketch plane. The result records `HISTORY_FACE_PARENT_AND_UNCHANGED_SKETCH_PLANE`.

Do not replace this evidence with a claim that `GetReferenceEntity` always returned the same Face after Cut. Persistence tokens used privately to investigate ownership are redacted from published reports.

An earlier writer trial failed this post-Cut qualification even while status was 3 with no dangling/overdefining relations. The corrected positive run is separate evidence; the earlier failure remains in the matrix.

## Separate other-Frame experiments

### Direct other-Frame Sketch relations

An earlier probe created a fully constrained line using two direct other-Frame Coincident relations, without Convert Entities, dimensions or FIX, but failed to restore assembly edit context. A subsequent `direct_relations2354_follow_02` probe restored that context, retained status 3 with two relations and no dangling/overdefining relations, and closed with unchanged hashes. Its overall result remains `FAIL_OR_UNVERIFIED`: assembly mates reset the requested component translation on rebuild, so the movement needed to test parameter-follow did not occur. This is neither a parameter-follow pass nor proof that the relations failed to follow a valid movement.

### Positive direct-relation movement follow

`direct_relations2354_follow_free_02` returned `PASS_NATIVE_DIRECT_GEOMETRY_FOLLOW`. The setup line had two direct Coincident relations, status 3 and no dangling/overdefining relations; no Convert Entities, dimensions or FIX relations were introduced. On the contained disposable copy, 33 mates were temporarily suppressed so the requested movement could actually occur. The other Frame retained a 1 mm translation after rebuild; the exact native Sketch point was reacquired by its original ID and followed by 1 mm within the recorded 0.0001 mm point tolerance. The owning Frame's pose was unchanged.

All 35 original mate states, component fixed state, transforms and the native point were then restored. Input/copy hashes and blank-session cleanup were verified. This tests **direct Sketch geometry relations only** and creates no Cut. It does not qualify the separate cross-end adapter, two-Part Cut cycles, arbitrary updates or the installed Add-in. The earlier failures remain in the matrix.

### Cross-Frame Cut end references

The copy-only end-condition experiment converted the tested Cuts to:

- Short Cut: Up To Surface, API code 4, non-null Face reference.
- Long Cut: Offset From Surface, API code 5, 0.10 mm offset, non-null Face reference.

Both linked bodies remained equivalent to the reviewed result before and after a disposable-copy rebuild. Changing the long base-stock parameter from 3.20 to 3.21 m and restoring it also retained equivalent bodies. This is one parameter-follow experiment. It does not prove all two-Part dependency cycles, cross-Frame Sketch relations, geometry changes or rollback paths. The cross-end adapter is not integrated into the committed candidate source.

The subsequent `native_cross_end_undo_01` qualification failed after its initial two native Cuts passed. `FinishRecordingUndoObject2` returned false on the first call and retry; recording was still open and no Undo was attempted. The adapter remains **NOT_QUALIFIED / NOT_INTEGRATED**.

## 1618 stock-copy writer

`native1618_fr3fr4_stock_01` created two actual Cuts using the same face-attached end-section representation. The long Frame A had 34 relations (24 native-vertex Coincident and 10 Tangent); short Frame B had 22 (16 Coincident and 6 Tangent). Both returned status 3, zero driving dimensions, zero new planes and zero symmetric difference against the reviewed bodies. Native depths were 69.85 mm and 402.25 mm. These axial whole-section cuts are not single-sheet `t + 0.10 mm` cuts. Existing Cuts were suppressed only on disposable copies to prepare stock. This is a native harness pass, not an installed Add-in or original-miter equivalence test.

## Unequal-width inclined 1618 stock-copy writer

The boundary-witness fr1/fr2 run produced two native end-section Cuts with fully constrained status 3/3, relation counts 22/16 (16/12 native-vertex Coincident plus 6/4 Tangent), zero dimensions/planes and zero body difference. Native depths were approximately 69.85 mm and 440.583198 mm. The support used to derive the joint includes an actual vertex boundary; this is separate from the existing end Face supporting each Sketch. The generated Cut end references remain non-associative Blind depths in this candidate; this test must not be described as integrated cross-Frame end references.

## Reversed A/B and writer rollback

The reversed 2354 positive run created two native Cuts with relation counts 16/34 and fully constrained status 3/3, zero dimensions/planes and zero body difference. A separate injected failure after both Cuts restored both bodies and Feature Trees. This tests the face writer without the experimental cross-end adapter. A later `GetTypeName2` session fault occurred before solving the stale-ticket row; it does not invalidate the completed two rows.

## Legacy 3615 writer remains separate

The 3615 T1 stock-copy run created four valid Cuts using `FIXED_PLANE_PRISM`. Its exported Sketch relation/constraint/dimension/plane counters are -1, meaning unavailable. They must not be converted to zeros or used to claim the requested new Sketch treatment. T2 was an expected shared-Part refusal and created no Cut. The final fr04/fr03 U/Taller-U regression also created four exact Cuts with the legacy fixed-plane writer; it does not establish the new Sketch requirement.

## Original 43 Feature Cut studies

The original API research captured Sketch support entities, transforms, line/arc coordinates, construction flags, contour/region counts and parent features. It did **not** capture Sketch relation graphs, dimension inventories, fully-defined state, external-reference lock/break state or persistent support IDs. Therefore original-Sketch constraints remain `NOT_AVAILABLE`; new writer observations cannot retroactively verify them.

## Remaining work

The old U-rule writer still requires the same direct-relation/face-attached treatment. Installed Add-in testing, broad parameter updates, dangling/overdefining checks after those updates and flat-pattern validation remain separate required tests.
