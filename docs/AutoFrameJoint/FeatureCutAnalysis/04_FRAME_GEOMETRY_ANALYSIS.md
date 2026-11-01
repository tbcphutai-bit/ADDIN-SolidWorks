# Frame Geometry and Region Analysis

Native thickness and Bend R below are read from ISheetMetalFeatureData. Bounding spans and volume are measured on the final native B-rep in the reported configuration. Local XYZ coordinates are Part coordinates; no Assembly transform has been applied.

| Part | Config | XYZ spans (mm) | Thickness / inner Bend R (mm) | Volume (mm³) |
| --- | --- | --- | --- | --- |
| 1618-04-bh-A6-fr1.SLDPRT | Default | [3586.3, 95.666802, 17.63286] | 2 / 0.1 | 841441.673422 |
| 1618-04-bh-A6-1.SLDPRT | Default | [3592.5, 18.1, 1147.5] | 3 / 0.1 | 12388528.372081 |
| 1618-04-bh-A6-fr2.SLDPRT | Default | [1130.833334, 65, 17] | 2 / 0.1 | 189468.110862 |
| 1618-04-bh-A6-fr3.SLDPRT | Default | [3600.4, 90, 28.6] | 2 / 0.1 | 1170179.377746 |
| 1618-04-bh-A6-fr4.SLDPRT | Default | [1131.466667, 77, 17] | 2 / 0.1 | 224057.615028 |
| 1618-04-bh-A6-中骨1.SLDPRT | Default | [3450.5, 80, 27] | 2 / 0.1 | 884097.487985 |
| 3615-04-bh-T1-1.SLDPRT | Default | [1615, 1055, 3] | 3 / 0.1 | 5111475 |
| 3615-04-bh-T1-fr1.SLDPRT | Default | [1622, 63, 32] | 2 / 0.1 | 378811.144308 |
| 3615-04-bh-T1-fr2.SLDPRT | Default | [1062, 63, 32] | 2 / 0.1 | 243385.60228 |
| 3615-04-bh-T1-fr3.SLDPRT | Default | [1609, 63, 32] | 2 / 0.1 | 378723.448444 |
| 3615-04-bh-T1-fr4.SLDPRT | Default | [1044.8, 50, 32] | 2 / 0.1 | 216515.888538 |
| 3615-04-bh-T2-1.SLDPRT | Default | [1140, 1305, 3] | 3 / 0.1 | 4463100 |
| 3615-04-bh-T2-fr1.SLDPRT | Default | [1160, 63, 32] | 2 / 0.1 | 264003.970647 |
| 3615-04-bh-T2-fr2.SLDPRT | Default | [1299, 63, 32] | 2 / 0.1 | 303812.640722 |
| 4114-04-HN-C07-12_New.SLDPRT | ﾃﾞﾌｫﾙﾄ | [13.3, 90, 116.8] | 2.3 / 0.1 | 29884.629286 |


## 1618a fr1: supported region reading

`API_VERIFIED`: the base profile Sketch has a 75 mm main run, a 15 mm side run, a 15 mm return run and a 17.92 mm inclined run. Sheet thickness is 2 mm and inner Bend R is 0.1 mm. `INFERRED` semantic labels in native coordinates: the broad run is the Web, the side run a Wall, its folded return a Lip, and the inclined opposite run another edge/Wall region. These labels need assembly orientation before calling a side inner or outer.

`API_VERIFIED`: Cut-Extrude1 uses an open 5-segment Sketch2 near native negative X; Cut-Extrude2 uses an open 5-segment Sketch3 near positive X. Both use Through All Both, NormalCut=true, with opposite FlipSideToCut settings. Their outlines include axial runs, small steps and an oblique segment. This identifies separate end-trim definitions; it does not establish a universal miter plane or the complete removed-stock solid.

`API_VERIFIED`: Cut-Extrude112 uses a 4-segment rectangle, 8.9 × 4.216797 mm in its Sketch plane, at the positive end. Its sketch normal is approximately [0, 0.948683, 0.316228], so it is a separate trim on the inclined side region. The two-dimensional rectangle must be transformed from its own plane. It cannot be replayed on the broad Web plane without changing the operation.

`INFERRED`: terminal portions of the Web/Wall transition are processed by the open end profiles, while the small inclined-edge patch is processed separately. A retained outside Lip boundary cannot be assigned to the opposing Frame from these standalone Parts alone. Bend material must follow thickness and the actual 0.1 mm inner radius; do not approximate the entire bend as a zero-thickness intersection line.

## Other Parts and differences

1618a fr3 has seven Cut-Extrude definitions, including three Up To Vertex terminations. This is a distinct construction from the Through All Both end trims of fr1: the limiting vertex matters. fr2 and fr4 have four and five Cut-Extrude definitions respectively, so the shorter members also contain machining; that does not mean every one of their cuts belongs to the pictured corner.

Four 3615a T1 source files initially load in flat configuration. Their flat body spans cannot be used as folded Web/Wall heights. The primary table uses a separate Read-Only load of Default with FlatPattern suppressed; the original flat records remain in local extraction evidence. T2 was already read in Default. Every T1/T2 Frame contains actual Cut features, but there is no verified assembly-space region correspondence yet.

No cut-before/cut-after body difference was generated. Final faces returned by Feature.GetFaces establish surviving feature-owned boundaries, not the entire removed material or stock that existed before a later feature. The CASE_MATRIX explicitly keeps this distinction.
