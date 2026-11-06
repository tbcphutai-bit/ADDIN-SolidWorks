# Native Cut construction and validation

The writer consumes the reviewed parametric Cut Plan. It does not copy the 43 historical Feature commands, replace the Part with an imported body, infer sheet thickness from an image, or use a bounding box as the cut rule.

For each subtractive prism, it first runs the same temporary Boolean against a copy of the current body. Empty steps are skipped and body splits are refused. It constructs a fixed datum plane from the plan polygon/direction, creates a 2D sketch, reads native `ModelToSketchTransform`, maps points/arcs into that sketch and derives direction from the measured native sketch normal. Native Cut defaults to negative sketch normal; the reverse flag is checked against alignment. It then calls `FeatureCut4`, reads back `ExtrudeFeatureData2`, checks error/type/depth/end condition/NormalCut/FlipSide/direction, validates the native body and compares it to that step's expected B-rep.

Instant3D returns `GetTypeName2() == ICE`; the writer obtains its underlying `GetTypeName() == Cut`. This is required for real Cut recognition. [Official feature type contract](https://help.solidworks.com/2022/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IFeature~GetTypeName2.html).

## Thickness and two independent 0.10 mm conventions

| Policy | Meaning |
|---|---|
| `SheetCutOverrun = 0.10 mm` | Blind `t + 0.10 mm` only for qualified cuts through one sheet layer |
| `AssemblyFitGap = 0.10 mm` | Required clearance between the two remaining assembled Frames |
| `FORMED_PROFILE_ENVELOPE` | Explicit full formed-profile reach; cannot be replaced by `t + 0.10 mm` |
| `BEND_NORMAL_RELIEF` | Explicit verified bend/return relief prism; not automatically a single-sheet depth |

`UseNativeNormalCut` enables native NormalCut only for `SINGLE_SHEET_BLIND_T_PLUS_OVERRUN` with NormalCut intent. Full formed envelopes and explicit bend relief keep native NormalCut false, preserving the separately validated R51 masks. Post-cut exact comparison rejects a native normalization that changes the reviewed shape. Native NormalCut=true has not received a new end-to-end write qualification in this task.

The 2354 native definition records from the rollback qualification have Blind depths 60.2, 2.7, 42.2 and 2.8 mm; NormalCut=false, FlipSideToCut=false, ReverseDirection=true. All four native step bodies have symmetric difference 0 against the plan. These are actual API records from that run, not a universal depth recipe. The public-manager final run separately creates four native Cuts with final symmetric differences 0, minimum clearance 0.10 mm and interference 0.

The old R51 rule core handles KEEP/CUT coverage, sheet skins and explicit bend masks. The new native writer requires Check2=0, no Check3 faults, one valid solid per Frame, retained sheet-metal body identity, and exact agreement within `max(1e-12 m³, expectedVolume*1e-8)`. Final pair checks use actual native resulting bodies at the reviewed Component transforms. It does not accept non-manifold Boolean error 547 or silently adjust cutter placement after a failure.

Each successful tool owns a new datum/sketch/Cut family. The datum is a fixed snapshot, not an associative joint. Native-group Undo was insufficient across sketch-edit boundaries in a failed trial. Automatic rollback now removes only recorded new Feature IDs, re-reads live features after absorbed-sketch deletion, checks bounded progress and verifies both original body and Feature Tree. Original or unrelated features are never selected for this rollback. The original Undo stack and dirty state are not claimed restored.

Sources: [FeatureCut4](https://help.solidworks.com/2024/english/api/sldworksapi/solidworks.interop.sldworks~solidworks.interop.sldworks.ifeaturemanager~featurecut4.html), [fixed plane](https://help.solidworks.com/2021/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IModelDoc2~CreatePlaneFixed2.html), [Undo recording](https://help.solidworks.com/2025/English/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.IModelDocExtension~FinishRecordingUndoObject2.html).
