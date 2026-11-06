# Feature Cut, NormalCut and depth rules

## Fresh API audit

`NativeCutStudy` re-read all 42 studied Cut definitions on 11 newly named private Read-Only Parts, in their Default configuration. It read SheetMetal thickness/R, ordered Feature identity, active suppression, end enums, both stored depths, direction flags, NormalCut, FlipSideToCut, LinkToThickness, Sketch transforms, segments and contour closure. The 42 forward-end/depth and key scalar records match the prior extraction exactly. Every copied file's byte hash remained unchanged.

| Stored forward end | Count |
| --- | ---: |
| Blind | 16 |
| Through All Both | 22 |
| Up To Vertex | 3 |
| Through All | 1 |

32 records have NormalCut=true; 11 have FlipSideToCut=true. These are independent flags. A dormant reverse depth or the stored depth of a Through All/Up To Vertex feature does not establish its effective travel. `BothDirections` and both end conditions are exported per Feature; bidirectional Through All can also be represented by two Through All ends.

Of the 16 Blind records, 14 store t, one stores t+0.10 mm, and one stores 10 mm. The t+0.10 mm record is `1618-04-bh-A6-fr4.SLDPRT`, Cut ordinal 5. Its thickness is 2.00 mm, depth 2.10 mm, NormalCut=true. Its Sketch plane is offset from the material skin; the 0.10 mm does not automatically mean 0.10 mm beyond the inner surface.

The user's 2.50→2.60 mm convention is supported as an appropriate **single-layer rule**. It is not the observed setting of all 42 records. The three newly inspected suppressed 2354 Cuts themselves use Through All/Through All Both with NormalCut, not a stored Blind 2.60 mm operation.

## Three independent distances in code

| Name | Value | Meaning |
| --- | ---: | --- |
| `SheetCutOverrun` | 0.10 mm | Nominal Blind depth allowance for a verified single sheet: t + allowance. |
| `AssemblyFitGap` | 0.10 mm | Required minimum clearance between the two resulting Frame solids. |
| `CutterEntryClearance` | 0.01 mm | Temporary-tool extension outside the entry skin, separate from nominal manufacturing depth. |

`CutterEnvelopeOverrun` independently bounds full-profile temporary masks. No shared variable turns a manufacturing depth allowance into an assembly gap.

The single-sheet factory records nominal depth t+0.10 mm. Its temporary prism also starts 0.01 mm outside the entry skin, so its total geometric length is nominal depth+0.01 mm. For t=2.50 mm these are **2.60 mm nominal** and **2.61 mm temporary-tool length**. Both values are reported.

## Applicable single-sheet plan

`AfjSheetCutTools.BlindBetweenSkins` requires API thickness, two verified skin positions, a direction pointing into the sheet and a planar boundary on the entry skin. It rejects a reversed/inconsistent skin pair. During execution, the source must expose the expected two planes; entry/exit overrun volumes are checked for additional material. A shallow allowance reaching another sheet or an unhandled Bend is refused.

The factory does not emulate NormalCut over an arbitrary curved surface. A crossing Bend requires a separately verified relief/compound plan. Generated U-joint masks that cut Web and Walls through their formed envelope retain `FORMED_PROFILE_ENVELOPE`; explicit tangent corrections use `BEND_NORMAL_RELIEF`. They are not relabeled as single-sheet Blind operations merely to apply t+0.10 mm.

## NormalCut and Bend proof

NormalCut expresses cutting normal to sheet thickness. It is not a universal numeric depth correction or a guarantee that a simple straight prism clears every curved region. The current U rule includes an inner-Wall/tangent-derived cap and near/far clearance guards. Their depth can depend on **t+R**, which differs from t+0.10 mm whenever R differs from 0.10 mm.

The 42-row report identifies removed cylindrical surfaces matching R or R+t. This observation alone does not show that all desired Bend stock was removed. The native replay compares the resulting solid with the recorded native after-Cut B-rep, using symmetric difference. It separately records the plain-prism error and the error after additional NormalCut corrections. “No unexpected residue” means relative to that specific native after state, not deletion of every Bend in the Frame.

Curved hole/slot records remain in the 42-Feature audit but are excluded from corner-mask replay. Their exclusion is not a successful corner test. Detailed replay counts and any failures are in [NATIVE_REPLAY_RESULTS.json](NATIVE_REPLAY_RESULTS.json).

All 28 corner replays pass with zero measured symmetric difference against native after bodies. Ten require additional NormalCut Bend corrections. Two further multi-prism sequences use ordinary compound cutters, not Bend corrections. Fourteen curved hole/slot records are `NOT_APPLICABLE_CORNER_REPLAY`.

Two independent planar-layer controls exercise the new factory with t=2.00 and 2.50 mm, giving nominal depths 2.10 and 2.60 mm. These thicknesses are specified synthetic-control parameters; they are not inferred from images or claimed as fresh SheetMetal getters. Their bodies, cutter intersections and volume changes are measured through API. Reversed skins and crossing-Bend inputs are refused. A connected second layer 0.05 mm beyond the inner skin is also refused by the overrun-stock guard. This does not establish a universal NormalCut implementation.

The runtime formed-U plan uses full-profile masks and Bend relief. It does not call the planar single-sheet factory or force a t+0.10 mm limit onto its multi-region cuts.

## API limitations retained

Some original Cut direction/end references are unavailable without additional feature-selection state. A stored Sketch transform plus a ReverseDirection scalar is not silently promoted to a complete reference set. The runtime solver constructs its own tool direction from verified profile/skin geometry, and does not copy a historical Cut direction or polygon.

References: [NormalCut](https://help.solidworks.com/2022/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IExtrudeFeatureData2~NormalCut.html), [end conditions](https://help.solidworks.com/2024/english/api/swconst/SOLIDWORKS.Interop.swconst~SOLIDWORKS.Interop.swconst.swEndConditions_e.html), [GetDepth](https://help.solidworks.com/2023/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IExtrudeFeatureData2~GetDepth.html).
