# Native Cut Feature Details

Every entry below is an actual IFeature with type Cut, or ICE whose GetTypeName returns Cut; GetDefinition supports IExtrudeFeatureData2. No Revolved/Swept/Lofted Cut was found in these trees. CutListFolder is a sheet-metal body/cut-list folder, not a Cut operation. HoleWzd, CurvePattern and LibraryFeature are separately inventoried.

All lengths from the API are metres; tables convert to mm. Raw direction-2 and depth values are retained even if that direction/end condition makes them inactive. Through All/Both never becomes a blind-depth cut merely because GetDepth returns a nonzero stored value. Open-profile side is determined by FlipSideToCut plus the sketch/stock context, not by inventing a closed polygon.

## 1618-04-bh-A6-fr1.SLDPRT — ｶｯﾄ - 押し出し110

`API_VERIFIED`: path 23; `ICE` / legacy `Cut`; config `Default`; suppressed=False.

| Parameter | API value |
| --- | --- |
| Forward End Condition | swEndCondBlind |
| Reverse End Condition (raw) | swEndCondThroughAll |
| Stored depth forward / reverse (mm) | 2 / 10 |
| ReverseDirection / BothDirections | False / False |
| FlipSideToCut / NormalCut | False / False |
| Thin / link to thickness | False / True |
| Draft enabled forward / reverse | False / False |
| FromType / FromOffsetDistance (m) | 0 / 0 |
| Affected-body count on analysis copy | 1 |
| Affected-body identifiers | [{"body_name": "ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1"}] |
| Forward reference on analysis copy | {"type_code": -1, "entity": null} |

Original current final geometry exposes 4 faces owned by this feature. These are not a before/after removed volume. Scope body names refer to the rollback-stage body on the copy, not automatically the final body's name.

Sketch `a`: 4 profile segments + 3 construction segments; API contours=1, regions=2. Endpoint-graph closure (derived)=True.
Model-space sketch normal: [0, -0, -1]; plane class (derived)=axis_aligned. Model-space profile bounds mm: [-1654.25, 36.5, -2, -1638.25, 46.5, -2]. Full segment coordinates, plane transform and supporting entity are in JSON.

`INFERRED` purpose: OTHER_MACHINING_OR_UNKNOWN; native end location: not established. A local-end bounding box is a screening heuristic, not proof of a specific assembly joint. Removed stock and retained stock masks remain UNVERIFIED without a before-state body.

## 1618-04-bh-A6-fr1.SLDPRT — ｶｯﾄ - 押し出し1

`API_VERIFIED`: path 29; `Cut` / legacy `Cut`; config `Default`; suppressed=False.

| Parameter | API value |
| --- | --- |
| Forward End Condition | swEndCondThroughAllBoth |
| Reverse End Condition (raw) | swEndCondThroughAll |
| Stored depth forward / reverse (mm) | 2.8 / 0 |
| ReverseDirection / BothDirections | False / True |
| FlipSideToCut / NormalCut | True / True |
| Thin / link to thickness | False / False |
| Draft enabled forward / reverse | False / False |
| FromType / FromOffsetDistance (m) | 0 / 0 |
| Affected-body count on analysis copy | 1 |
| Affected-body identifiers | [{"body_name": "ｶｰﾌﾞ ﾊﾟﾀｰﾝ1"}] |
| Forward reference on analysis copy | {"type_code": -1, "entity": null} |

Original current final geometry exposes 9 faces owned by this feature. These are not a before/after removed volume. Scope body names refer to the rollback-stage body on the copy, not automatically the final body's name.

Sketch `ｽｹｯﾁ2`: 5 profile segments + 0 construction segments; API contours=1, regions=0. Endpoint-graph closure (derived)=False.
Model-space sketch normal: [0, 0, -1]; plane class (derived)=axis_aligned. Model-space profile bounds mm: [-1793.15, -15, -2, -1730.15, 80.666802, -2]. Full segment coordinates, plane transform and supporting entity are in JSON.

`INFERRED` purpose: END_LOCAL_CANDIDATE; native end location: negative_end. A local-end bounding box is a screening heuristic, not proof of a specific assembly joint. Removed stock and retained stock masks remain UNVERIFIED without a before-state body.

## 1618-04-bh-A6-fr1.SLDPRT — ｶｯﾄ - 押し出し2

`API_VERIFIED`: path 31; `Cut` / legacy `Cut`; config `Default`; suppressed=False.

| Parameter | API value |
| --- | --- |
| Forward End Condition | swEndCondThroughAllBoth |
| Reverse End Condition (raw) | swEndCondThroughAll |
| Stored depth forward / reverse (mm) | 2 / 2.3 |
| ReverseDirection / BothDirections | False / True |
| FlipSideToCut / NormalCut | False / True |
| Thin / link to thickness | False / True |
| Draft enabled forward / reverse | False / False |
| FromType / FromOffsetDistance (m) | 0 / 0 |
| Affected-body count on analysis copy | 1 |
| Affected-body identifiers | [{"body_name": "ｶｯﾄ - 押し出し1"}] |
| Forward reference on analysis copy | {"type_code": -1, "entity": null} |

Original current final geometry exposes 8 faces owned by this feature. These are not a before/after removed volume. Scope body names refer to the rollback-stage body on the copy, not automatically the final body's name.

Sketch `ｽｹｯﾁ3`: 5 profile segments + 0 construction segments; API contours=1, regions=0. Endpoint-graph closure (derived)=False.
Model-space sketch normal: [0, 0, -1]; plane class (derived)=axis_aligned. Model-space profile bounds mm: [1730.15, -15, -2, 1793.15, 80.666802, -2]. Full segment coordinates, plane transform and supporting entity are in JSON.

`INFERRED` purpose: END_LOCAL_CANDIDATE; native end location: positive_end. A local-end bounding box is a screening heuristic, not proof of a specific assembly joint. Removed stock and retained stock masks remain UNVERIFIED without a before-state body.

## 1618-04-bh-A6-fr1.SLDPRT — ｶｯﾄ - 押し出し112

`API_VERIFIED`: path 33; `Cut` / legacy `Cut`; config `Default`; suppressed=False.

| Parameter | API value |
| --- | --- |
| Forward End Condition | swEndCondThroughAllBoth |
| Reverse End Condition (raw) | swEndCondThroughAll |
| Stored depth forward / reverse (mm) | 2 / 10 |
| ReverseDirection / BothDirections | False / True |
| FlipSideToCut / NormalCut | False / True |
| Thin / link to thickness | False / True |
| Draft enabled forward / reverse | False / False |
| FromType / FromOffsetDistance (m) | 0 / 0 |
| Affected-body count on analysis copy | 1 |
| Affected-body identifiers | [{"body_name": "ｶｯﾄ - 押し出し2"}] |
| Forward reference on analysis copy | {"type_code": -1, "entity": null} |

Original current final geometry exposes 2 faces owned by this feature. These are not a before/after removed volume. Scope body names refer to the rollback-stage body on the copy, not automatically the final body's name.

Sketch `ｽｹｯﾁ16`: 4 profile segments + 0 construction segments; API contours=1, regions=2. Endpoint-graph closure (derived)=True.
Model-space sketch normal: [0, 0.948683, 0.316228]; plane class (derived)=oblique. Model-space profile bounds mm: [1784.25, 79.333334, -17.000405, 1793.15, 80.666802, -13]. Full segment coordinates, plane transform and supporting entity are in JSON.

`INFERRED` purpose: END_LOCAL_CANDIDATE; native end location: positive_end. A local-end bounding box is a screening heuristic, not proof of a specific assembly joint. Removed stock and retained stock masks remain UNVERIFIED without a before-state body.

## 1618-04-bh-A6-fr2.SLDPRT — ｶｯﾄ - 押し出し14

`API_VERIFIED`: path 23; `ICE` / legacy `Cut`; config `Default`; suppressed=False.

| Parameter | API value |
| --- | --- |
| Forward End Condition | swEndCondBlind |
| Reverse End Condition (raw) | swEndCondThroughAll |
| Stored depth forward / reverse (mm) | 2 / 10 |
| ReverseDirection / BothDirections | False / False |
| FlipSideToCut / NormalCut | False / False |
| Thin / link to thickness | False / True |
| Draft enabled forward / reverse | False / False |
| FromType / FromOffsetDistance (m) | 0 / 0 |
| Affected-body count on analysis copy | 1 |
| Affected-body identifiers | [{"body_name": "ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1"}] |
| Forward reference on analysis copy | {"type_code": -1, "entity": null} |

Original current final geometry exposes 4 faces owned by this feature. These are not a before/after removed volume. Scope body names refer to the rollback-stage body on the copy, not automatically the final body's name.

Sketch `a`: 4 profile segments + 3 construction segments; API contours=1, regions=2. Endpoint-graph closure (derived)=True.
Model-space sketch normal: [0, -0, -1]; plane class (derived)=axis_aligned. Model-space profile bounds mm: [-431.75, 22.5, -2, -415.75, 32.5, -2]. Full segment coordinates, plane transform and supporting entity are in JSON.

`INFERRED` purpose: OTHER_MACHINING_OR_UNKNOWN; native end location: not established. A local-end bounding box is a screening heuristic, not proof of a specific assembly joint. Removed stock and retained stock masks remain UNVERIFIED without a before-state body.

## 1618-04-bh-A6-fr2.SLDPRT — ｶｯﾄ - 押し出し3

`API_VERIFIED`: path 28; `Cut` / legacy `Cut`; config `Default`; suppressed=False.

| Parameter | API value |
| --- | --- |
| Forward End Condition | swEndCondThroughAllBoth |
| Reverse End Condition (raw) | swEndCondThroughAll |
| Stored depth forward / reverse (mm) | 2 / 0 |
| ReverseDirection / BothDirections | True / True |
| FlipSideToCut / NormalCut | False / True |
| Thin / link to thickness | False / True |
| Draft enabled forward / reverse | False / False |
| FromType / FromOffsetDistance (m) | 0 / 0 |
| Affected-body count on analysis copy | 1 |
| Affected-body identifiers | [{"body_name": "ｶｰﾌﾞ ﾊﾟﾀｰﾝ2"}] |
| Forward reference on analysis copy | {"type_code": -1, "entity": null} |

Original current final geometry exposes 6 faces owned by this feature. These are not a before/after removed volume. Scope body names refer to the rollback-stage body on the copy, not automatically the final body's name.

Sketch `ｽｹｯﾁ4`: 5 profile segments + 0 construction segments; API contours=1, regions=6. Endpoint-graph closure (derived)=False.
Model-space sketch normal: [0, 0, -1]; plane class (derived)=axis_aligned. Model-space profile bounds mm: [509.75, -0, -2, 572.75, 65, -2]. Full segment coordinates, plane transform and supporting entity are in JSON.

`INFERRED` purpose: END_LOCAL_CANDIDATE; native end location: positive_end. A local-end bounding box is a screening heuristic, not proof of a specific assembly joint. Removed stock and retained stock masks remain UNVERIFIED without a before-state body.

## 1618-04-bh-A6-fr2.SLDPRT — ｶｯﾄ - 押し出し1

`API_VERIFIED`: path 31; `Cut` / legacy `Cut`; config `Default`; suppressed=False.

| Parameter | API value |
| --- | --- |
| Forward End Condition | swEndCondThroughAllBoth |
| Reverse End Condition (raw) | swEndCondThroughAll |
| Stored depth forward / reverse (mm) | 2 / 0 |
| ReverseDirection / BothDirections | False / True |
| FlipSideToCut / NormalCut | True / True |
| Thin / link to thickness | False / True |
| Draft enabled forward / reverse | False / False |
| FromType / FromOffsetDistance (m) | 0 / 0 |
| Affected-body count on analysis copy | 1 |
| Affected-body identifiers | [{"body_name": "ｶｯﾄ - 押し出し3"}] |
| Forward reference on analysis copy | {"type_code": -1, "entity": null} |

Original current final geometry exposes 6 faces owned by this feature. These are not a before/after removed volume. Scope body names refer to the rollback-stage body on the copy, not automatically the final body's name.

Sketch `ｽｹｯﾁ2`: 6 profile segments + 0 construction segments; API contours=1, regions=6. Endpoint-graph closure (derived)=False.
Model-space sketch normal: [0, 0, -1]; plane class (derived)=axis_aligned. Model-space profile bounds mm: [-573.75, -0, -2, -480.75, 65, -2]. Full segment coordinates, plane transform and supporting entity are in JSON.

`INFERRED` purpose: END_LOCAL_CANDIDATE; native end location: negative_end. A local-end bounding box is a screening heuristic, not proof of a specific assembly joint. Removed stock and retained stock masks remain UNVERIFIED without a before-state body.

## 1618-04-bh-A6-fr2.SLDPRT — ｶｯﾄ - 押し出し15

`API_VERIFIED`: path 33; `Cut` / legacy `Cut`; config `Default`; suppressed=False.

| Parameter | API value |
| --- | --- |
| Forward End Condition | swEndCondThroughAllBoth |
| Reverse End Condition (raw) | swEndCondThroughAll |
| Stored depth forward / reverse (mm) | 2 / 10 |
| ReverseDirection / BothDirections | False / True |
| FlipSideToCut / NormalCut | True / True |
| Thin / link to thickness | False / True |
| Draft enabled forward / reverse | False / False |
| FromType / FromOffsetDistance (m) | 0 / 0 |
| Affected-body count on analysis copy | 1 |
| Affected-body identifiers | [{"body_name": "ｶｯﾄ - 押し出し1"}] |
| Forward reference on analysis copy | {"type_code": -1, "entity": null} |

Original current final geometry exposes 1 faces owned by this feature. These are not a before/after removed volume. Scope body names refer to the rollback-stage body on the copy, not automatically the final body's name.

Sketch `ｽｹｯﾁ7`: 1 profile segments + 0 construction segments; API contours=1, regions=2. Endpoint-graph closure (derived)=False.
Model-space sketch normal: [0, 1, 0]; plane class (derived)=axis_aligned. Model-space profile bounds mm: [-558.083334, 65, -13, -554.45, 65, -2.1]. Full segment coordinates, plane transform and supporting entity are in JSON.

`INFERRED` purpose: END_LOCAL_CANDIDATE; native end location: negative_end. A local-end bounding box is a screening heuristic, not proof of a specific assembly joint. Removed stock and retained stock masks remain UNVERIFIED without a before-state body.

## 1618-04-bh-A6-fr3.SLDPRT — ｶｯﾄ - 押し出し110

`API_VERIFIED`: path 23; `ICE` / legacy `Cut`; config `Default`; suppressed=False.

| Parameter | API value |
| --- | --- |
| Forward End Condition | swEndCondBlind |
| Reverse End Condition (raw) | swEndCondThroughAll |
| Stored depth forward / reverse (mm) | 2 / 10 |
| ReverseDirection / BothDirections | False / False |
| FlipSideToCut / NormalCut | False / False |
| Thin / link to thickness | False / True |
| Draft enabled forward / reverse | False / False |
| FromType / FromOffsetDistance (m) | 0 / 0 |
| Affected-body count on analysis copy | 1 |
| Affected-body identifiers | [{"body_name": "ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1"}] |
| Forward reference on analysis copy | {"type_code": -1, "entity": null} |

Original current final geometry exposes 4 faces owned by this feature. These are not a before/after removed volume. Scope body names refer to the rollback-stage body on the copy, not automatically the final body's name.

Sketch `a`: 4 profile segments + 3 construction segments; API contours=1, regions=2. Endpoint-graph closure (derived)=True.
Model-space sketch normal: [0, -0, -1]; plane class (derived)=axis_aligned. Model-space profile bounds mm: [-1654.25, 22.5, -2, -1638.25, 32.5, -2]. Full segment coordinates, plane transform and supporting entity are in JSON.

`INFERRED` purpose: OTHER_MACHINING_OR_UNKNOWN; native end location: not established. A local-end bounding box is a screening heuristic, not proof of a specific assembly joint. Removed stock and retained stock masks remain UNVERIFIED without a before-state body.

## 1618-04-bh-A6-fr3.SLDPRT — ｶｯﾄ - 押し出し1

`API_VERIFIED`: path 29; `Cut` / legacy `Cut`; config `Default`; suppressed=False.

| Parameter | API value |
| --- | --- |
| Forward End Condition | swEndCondUpToVertex |
| Reverse End Condition (raw) | swEndCondUpToVertex |
| Stored depth forward / reverse (mm) | 0 / 0 |
| ReverseDirection / BothDirections | True / False |
| FlipSideToCut / NormalCut | False / True |
| Thin / link to thickness | False / False |
| Draft enabled forward / reverse | False / False |
| FromType / FromOffsetDistance (m) | 0 / 0 |
| Affected-body count on analysis copy | 1 |
| Affected-body identifiers | [{"body_name": "ｶｰﾌﾞ ﾊﾟﾀｰﾝ1"}] |
| Forward reference on analysis copy | {"type_code": 1, "entity": {"vertex_point_m": [-1.8, 0.065, -0.012899999999999991]}} |

Original current final geometry exposes 5 faces owned by this feature. These are not a before/after removed volume. Scope body names refer to the rollback-stage body on the copy, not automatically the final body's name.

Sketch `ｽｹｯﾁ2`: 7 profile segments + 0 construction segments; API contours=1, regions=1. Endpoint-graph closure (derived)=True.
Model-space sketch normal: [0, 0, -1]; plane class (derived)=axis_aligned. Model-space profile bounds mm: [-1800, -0, 0, -1730.15, 90, 0]. Full segment coordinates, plane transform and supporting entity are in JSON.

`INFERRED` purpose: END_LOCAL_CANDIDATE; native end location: negative_end. A local-end bounding box is a screening heuristic, not proof of a specific assembly joint. Removed stock and retained stock masks remain UNVERIFIED without a before-state body.

## 1618-04-bh-A6-fr3.SLDPRT — ｶｯﾄ - 押し出し116

`API_VERIFIED`: path 31; `Cut` / legacy `Cut`; config `Default`; suppressed=False.

| Parameter | API value |
| --- | --- |
| Forward End Condition | swEndCondUpToVertex |
| Reverse End Condition (raw) | swEndCondThroughAll |
| Stored depth forward / reverse (mm) | 2 / 10 |
| ReverseDirection / BothDirections | False / True |
| FlipSideToCut / NormalCut | False / True |
| Thin / link to thickness | False / True |
| Draft enabled forward / reverse | False / False |
| FromType / FromOffsetDistance (m) | 0 / 0 |
| Affected-body count on analysis copy | 1 |
| Affected-body identifiers | [{"body_name": "ｶｯﾄ - 押し出し1"}] |
| Forward reference on analysis copy | {"type_code": 1, "entity": {"vertex_point_m": [1.8, 0.065, -0.0020999999999999994]}} |

Original current final geometry exposes 8 faces owned by this feature. These are not a before/after removed volume. Scope body names refer to the rollback-stage body on the copy, not automatically the final body's name.

Sketch `ｽｹｯﾁ27`: 7 profile segments + 0 construction segments; API contours=1, regions=1. Endpoint-graph closure (derived)=True.
Model-space sketch normal: [0, 0, 1]; plane class (derived)=axis_aligned. Model-space profile bounds mm: [1730.15, -0, 0, 1800, 90, 0]. Full segment coordinates, plane transform and supporting entity are in JSON.

`INFERRED` purpose: END_LOCAL_CANDIDATE; native end location: positive_end. A local-end bounding box is a screening heuristic, not proof of a specific assembly joint. Removed stock and retained stock masks remain UNVERIFIED without a before-state body.

## 1618-04-bh-A6-fr3.SLDPRT — ｶｯﾄ - 押し出し114

`API_VERIFIED`: path 33; `Cut` / legacy `Cut`; config `Default`; suppressed=False.

| Parameter | API value |
| --- | --- |
| Forward End Condition | swEndCondThroughAllBoth |
| Reverse End Condition (raw) | swEndCondThroughAll |
| Stored depth forward / reverse (mm) | 2 / 10 |
| ReverseDirection / BothDirections | False / True |
| FlipSideToCut / NormalCut | False / True |
| Thin / link to thickness | False / True |
| Draft enabled forward / reverse | False / False |
| FromType / FromOffsetDistance (m) | 0 / 0 |
| Affected-body count on analysis copy | 1 |
| Affected-body identifiers | [{"body_name": "ｶｯﾄ - 押し出し116"}] |
| Forward reference on analysis copy | {"type_code": -1, "entity": null} |

Original current final geometry exposes 2 faces owned by this feature. These are not a before/after removed volume. Scope body names refer to the rollback-stage body on the copy, not automatically the final body's name.

Sketch `ｽｹｯﾁ22`: 8 profile segments + 0 construction segments; API contours=2, regions=3. Endpoint-graph closure (derived)=True.
Model-space sketch normal: [0, 0, -1]; plane class (derived)=axis_aligned. Model-space profile bounds mm: [-1800, 0, -17, 1800, 2, -17]. Full segment coordinates, plane transform and supporting entity are in JSON.

`INFERRED` purpose: OTHER_MACHINING_OR_UNKNOWN; native end location: not established. A local-end bounding box is a screening heuristic, not proof of a specific assembly joint. Removed stock and retained stock masks remain UNVERIFIED without a before-state body.

## 1618-04-bh-A6-fr3.SLDPRT — ｶｯﾄ - 押し出し115

`API_VERIFIED`: path 36; `Cut` / legacy `Cut`; config `Default`; suppressed=False.

| Parameter | API value |
| --- | --- |
| Forward End Condition | swEndCondUpToVertex |
| Reverse End Condition (raw) | swEndCondThroughAll |
| Stored depth forward / reverse (mm) | 2 / 10 |
| ReverseDirection / BothDirections | False / True |
| FlipSideToCut / NormalCut | False / True |
| Thin / link to thickness | False / True |
| Draft enabled forward / reverse | False / False |
| FromType / FromOffsetDistance (m) | 0 / 0 |
| Affected-body count on analysis copy | 1 |
| Affected-body identifiers | [{"body_name": "ｶｯﾄ - 押し出し114"}] |
| Forward reference on analysis copy | {"type_code": 1, "entity": {"vertex_point_m": [-1.7931499999999996, 0.065, -0.012899999999999991]}} |

Original current final geometry exposes 3 faces owned by this feature. These are not a before/after removed volume. Scope body names refer to the rollback-stage body on the copy, not automatically the final body's name.

Sketch `ｽｹｯﾁ23`: 4 profile segments + 0 construction segments; API contours=1, regions=3. Endpoint-graph closure (derived)=True.
Model-space sketch normal: [0, 0, -1]; plane class (derived)=axis_aligned. Model-space profile bounds mm: [-1800, 45, -28.6, -1784.25, 90, -28.6]. Full segment coordinates, plane transform and supporting entity are in JSON.

`INFERRED` purpose: END_LOCAL_CANDIDATE; native end location: negative_end. A local-end bounding box is a screening heuristic, not proof of a specific assembly joint. Removed stock and retained stock masks remain UNVERIFIED without a before-state body.

## 1618-04-bh-A6-fr3.SLDPRT — ｶｯﾄ - 押し出し117

`API_VERIFIED`: path 38; `Cut` / legacy `Cut`; config `Default`; suppressed=False.

| Parameter | API value |
| --- | --- |
| Forward End Condition | swEndCondThroughAllBoth |
| Reverse End Condition (raw) | swEndCondThroughAll |
| Stored depth forward / reverse (mm) | 2 / 10 |
| ReverseDirection / BothDirections | False / True |
| FlipSideToCut / NormalCut | True / True |
| Thin / link to thickness | False / True |
| Draft enabled forward / reverse | False / False |
| FromType / FromOffsetDistance (m) | 0 / 0 |
| Affected-body count on analysis copy | 1 |
| Affected-body identifiers | [{"body_name": "ｶｯﾄ - 押し出し115"}] |
| Forward reference on analysis copy | {"type_code": -1, "entity": null} |

Original current final geometry exposes 4 faces owned by this feature. These are not a before/after removed volume. Scope body names refer to the rollback-stage body on the copy, not automatically the final body's name.

Sketch `ｽｹｯﾁ28`: 1 profile segments + 0 construction segments; API contours=1, regions=2. Endpoint-graph closure (derived)=False.
Model-space sketch normal: [0, 0, -1]; plane class (derived)=axis_aligned. Model-space profile bounds mm: [1793.15, 45, -28.6, 1793.15, 90, -28.6]. Full segment coordinates, plane transform and supporting entity are in JSON.

`INFERRED` purpose: END_LOCAL_CANDIDATE; native end location: positive_end. A local-end bounding box is a screening heuristic, not proof of a specific assembly joint. Removed stock and retained stock masks remain UNVERIFIED without a before-state body.

## 1618-04-bh-A6-fr3.SLDPRT — ｶｯﾄ - 押し出し119

`API_VERIFIED`: path 42; `ICE` / legacy `Cut`; config `Default`; suppressed=False.

| Parameter | API value |
| --- | --- |
| Forward End Condition | swEndCondBlind |
| Reverse End Condition (raw) | swEndCondBlind |
| Stored depth forward / reverse (mm) | 2 / 10 |
| ReverseDirection / BothDirections | False / False |
| FlipSideToCut / NormalCut | False / True |
| Thin / link to thickness | False / True |
| Draft enabled forward / reverse | False / False |
| FromType / FromOffsetDistance (m) | 0 / 0 |
| Affected-body count on analysis copy | 1 |
| Affected-body identifiers | [{"body_name": "ﾎﾞｽ - 押し出し1"}] |
| Forward reference on analysis copy | {"type_code": -1, "entity": null} |

Original current final geometry exposes 1 faces owned by this feature. These are not a before/after removed volume. Scope body names refer to the rollback-stage body on the copy, not automatically the final body's name.

Sketch `a1`: 1 profile segments + 2 construction segments; API contours=1, regions=2. Endpoint-graph closure (derived)=True.
Model-space sketch normal: [0, 0, -1]; plane class (derived)=axis_aligned. Model-space profile bounds mm: [-1728.35, 74.9, -28.6, -1724.15, 79.1, -28.6]. Full segment coordinates, plane transform and supporting entity are in JSON.

`INFERRED` purpose: END_LOCAL_CANDIDATE; native end location: negative_end. A local-end bounding box is a screening heuristic, not proof of a specific assembly joint. Removed stock and retained stock masks remain UNVERIFIED without a before-state body.

## 1618-04-bh-A6-fr4.SLDPRT — ｶｯﾄ - 押し出し14

`API_VERIFIED`: path 23; `ICE` / legacy `Cut`; config `Default`; suppressed=False.

| Parameter | API value |
| --- | --- |
| Forward End Condition | swEndCondBlind |
| Reverse End Condition (raw) | swEndCondThroughAll |
| Stored depth forward / reverse (mm) | 2 / 10 |
| ReverseDirection / BothDirections | False / False |
| FlipSideToCut / NormalCut | False / False |
| Thin / link to thickness | False / True |
| Draft enabled forward / reverse | False / False |
| FromType / FromOffsetDistance (m) | 0 / 0 |
| Affected-body count on analysis copy | 1 |
| Affected-body identifiers | [{"body_name": "ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1"}] |
| Forward reference on analysis copy | {"type_code": -1, "entity": null} |

Original current final geometry exposes 4 faces owned by this feature. These are not a before/after removed volume. Scope body names refer to the rollback-stage body on the copy, not automatically the final body's name.

Sketch `a`: 4 profile segments + 3 construction segments; API contours=1, regions=2. Endpoint-graph closure (derived)=True.
Model-space sketch normal: [0, -0, -1]; plane class (derived)=axis_aligned. Model-space profile bounds mm: [445.75, 32.5, -2, 461.75, 42.5, -2]. Full segment coordinates, plane transform and supporting entity are in JSON.

`INFERRED` purpose: OTHER_MACHINING_OR_UNKNOWN; native end location: not established. A local-end bounding box is a screening heuristic, not proof of a specific assembly joint. Removed stock and retained stock masks remain UNVERIFIED without a before-state body.

## 1618-04-bh-A6-fr4.SLDPRT — ｶｯﾄ - 押し出し3

`API_VERIFIED`: path 28; `Cut` / legacy `Cut`; config `Default`; suppressed=False.

| Parameter | API value |
| --- | --- |
| Forward End Condition | swEndCondThroughAllBoth |
| Reverse End Condition (raw) | swEndCondThroughAll |
| Stored depth forward / reverse (mm) | 2 / 2.3 |
| ReverseDirection / BothDirections | True / True |
| FlipSideToCut / NormalCut | True / True |
| Thin / link to thickness | False / True |
| Draft enabled forward / reverse | False / False |
| FromType / FromOffsetDistance (m) | 0 / 0 |
| Affected-body count on analysis copy | 1 |
| Affected-body identifiers | [{"body_name": "ｶｰﾌﾞ ﾊﾟﾀｰﾝ1"}] |
| Forward reference on analysis copy | {"type_code": -1, "entity": null} |

Original current final geometry exposes 6 faces owned by this feature. These are not a before/after removed volume. Scope body names refer to the rollback-stage body on the copy, not automatically the final body's name.

Sketch `ｽｹｯﾁ4`: 6 profile segments + 0 construction segments; API contours=1, regions=6. Endpoint-graph closure (derived)=False.
Model-space sketch normal: [0, 0, -1]; plane class (derived)=axis_aligned. Model-space profile bounds mm: [-573.75, -12, -2, -480.75, 65, -2]. Full segment coordinates, plane transform and supporting entity are in JSON.

`INFERRED` purpose: END_LOCAL_CANDIDATE; native end location: negative_end. A local-end bounding box is a screening heuristic, not proof of a specific assembly joint. Removed stock and retained stock masks remain UNVERIFIED without a before-state body.

## 1618-04-bh-A6-fr4.SLDPRT — ｶｯﾄ - 押し出し1

`API_VERIFIED`: path 31; `Cut` / legacy `Cut`; config `Default`; suppressed=False.

| Parameter | API value |
| --- | --- |
| Forward End Condition | swEndCondThroughAllBoth |
| Reverse End Condition (raw) | swEndCondThroughAll |
| Stored depth forward / reverse (mm) | 2 / 2.3 |
| ReverseDirection / BothDirections | False / True |
| FlipSideToCut / NormalCut | False / True |
| Thin / link to thickness | False / True |
| Draft enabled forward / reverse | False / False |
| FromType / FromOffsetDistance (m) | 0 / 0 |
| Affected-body count on analysis copy | 1 |
| Affected-body identifiers | [{"body_name": "ｶｯﾄ - 押し出し3"}] |
| Forward reference on analysis copy | {"type_code": -1, "entity": null} |

Original current final geometry exposes 6 faces owned by this feature. These are not a before/after removed volume. Scope body names refer to the rollback-stage body on the copy, not automatically the final body's name.

Sketch `ｽｹｯﾁ2`: 5 profile segments + 0 construction segments; API contours=1, regions=6. Endpoint-graph closure (derived)=False.
Model-space sketch normal: [0, 0, -1]; plane class (derived)=axis_aligned. Model-space profile bounds mm: [509.75, -12, -2, 572.75, 65, -2]. Full segment coordinates, plane transform and supporting entity are in JSON.

`INFERRED` purpose: END_LOCAL_CANDIDATE; native end location: positive_end. A local-end bounding box is a screening heuristic, not proof of a specific assembly joint. Removed stock and retained stock masks remain UNVERIFIED without a before-state body.

## 1618-04-bh-A6-fr4.SLDPRT — ｶｯﾄ - 押し出し15

`API_VERIFIED`: path 33; `Cut` / legacy `Cut`; config `Default`; suppressed=False.

| Parameter | API value |
| --- | --- |
| Forward End Condition | swEndCondBlind |
| Reverse End Condition (raw) | swEndCondBlind |
| Stored depth forward / reverse (mm) | 10 / 15 |
| ReverseDirection / BothDirections | False / True |
| FlipSideToCut / NormalCut | False / True |
| Thin / link to thickness | False / False |
| Draft enabled forward / reverse | False / False |
| FromType / FromOffsetDistance (m) | 0 / 0 |
| Affected-body count on analysis copy | 1 |
| Affected-body identifiers | [{"body_name": "ｶｯﾄ - 押し出し1"}] |
| Forward reference on analysis copy | {"type_code": -1, "entity": null} |

Original current final geometry exposes 3 faces owned by this feature. These are not a before/after removed volume. Scope body names refer to the rollback-stage body on the copy, not automatically the final body's name.

Sketch `ｽｹｯﾁ5`: 5 profile segments + 0 construction segments; API contours=1, regions=4. Endpoint-graph closure (derived)=True.
Model-space sketch normal: [0, -1, 0]; plane class (derived)=axis_aligned. Model-space profile bounds mm: [-573.75, 0, -17, -553.75, 0, 0]. Full segment coordinates, plane transform and supporting entity are in JSON.

`INFERRED` purpose: END_LOCAL_CANDIDATE; native end location: negative_end. A local-end bounding box is a screening heuristic, not proof of a specific assembly joint. Removed stock and retained stock masks remain UNVERIFIED without a before-state body.

## 1618-04-bh-A6-fr4.SLDPRT — ｶｯﾄ - 押し出し16

`API_VERIFIED`: path 35; `Cut` / legacy `Cut`; config `Default`; suppressed=False.

| Parameter | API value |
| --- | --- |
| Forward End Condition | swEndCondBlind |
| Reverse End Condition (raw) | swEndCondBlind |
| Stored depth forward / reverse (mm) | 2.1 / 0 |
| ReverseDirection / BothDirections | False / False |
| FlipSideToCut / NormalCut | False / True |
| Thin / link to thickness | False / False |
| Draft enabled forward / reverse | False / False |
| FromType / FromOffsetDistance (m) | 0 / 0 |
| Affected-body count on analysis copy | 1 |
| Affected-body identifiers | [{"body_name": "ｶｯﾄ - 押し出し15"}] |
| Forward reference on analysis copy | {"type_code": -1, "entity": null} |

Original current final geometry exposes 3 faces owned by this feature. These are not a before/after removed volume. Scope body names refer to the rollback-stage body on the copy, not automatically the final body's name.

Sketch `ｽｹｯﾁ6`: 4 profile segments + 0 construction segments; API contours=1, regions=3. Endpoint-graph closure (derived)=True.
Model-space sketch normal: [0, 0, -1]; plane class (derived)=axis_aligned. Model-space profile bounds mm: [552.65, -12, -17, 572.75, 2, -17]. Full segment coordinates, plane transform and supporting entity are in JSON.

`INFERRED` purpose: END_LOCAL_CANDIDATE; native end location: positive_end. A local-end bounding box is a screening heuristic, not proof of a specific assembly joint. Removed stock and retained stock masks remain UNVERIFIED without a before-state body.

## 1618-04-bh-A6-中骨1.SLDPRT — ｶｯﾄ - 押し出し13

`API_VERIFIED`: path 24; `Cut` / legacy `Cut`; config `Default`; suppressed=False.

| Parameter | API value |
| --- | --- |
| Forward End Condition | swEndCondThroughAll |
| Reverse End Condition (raw) | swEndCondBlind |
| Stored depth forward / reverse (mm) | 0 / 0 |
| ReverseDirection / BothDirections | False / False |
| FlipSideToCut / NormalCut | False / True |
| Thin / link to thickness | False / False |
| Draft enabled forward / reverse | False / False |
| FromType / FromOffsetDistance (m) | 0 / 0 |
| Affected-body count on analysis copy | 1 |
| Affected-body identifiers | [{"body_name": "ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1"}] |
| Forward reference on analysis copy | {"type_code": -1, "entity": null} |

Original current final geometry exposes 18 faces owned by this feature. These are not a before/after removed volume. Scope body names refer to the rollback-stage body on the copy, not automatically the final body's name.

Sketch `ｽｹｯﾁ6`: 8 profile segments + 0 construction segments; API contours=2, regions=5. Endpoint-graph closure (derived)=True.
Model-space sketch normal: [0, 0, -1]; plane class (derived)=axis_aligned. Model-space profile bounds mm: [-1800, -40, -27, 1800, 40, -27]. Full segment coordinates, plane transform and supporting entity are in JSON.

`INFERRED` purpose: OTHER_MACHINING_OR_UNKNOWN; native end location: not established. A local-end bounding box is a screening heuristic, not proof of a specific assembly joint. Removed stock and retained stock masks remain UNVERIFIED without a before-state body.

## 3615-04-bh-T1-fr1.SLDPRT — ｶｯﾄ - 押し出し1

`API_VERIFIED`: path 24; `Cut` / legacy `Cut`; config `Default`; suppressed=False.

| Parameter | API value |
| --- | --- |
| Forward End Condition | swEndCondThroughAllBoth |
| Reverse End Condition (raw) | swEndCondThroughAll |
| Stored depth forward / reverse (mm) | 2 / 10 |
| ReverseDirection / BothDirections | False / True |
| FlipSideToCut / NormalCut | True / True |
| Thin / link to thickness | False / True |
| Draft enabled forward / reverse | False / False |
| FromType / FromOffsetDistance (m) | 0 / 0 |
| Affected-body count on analysis copy | 1 |
| Affected-body identifiers | [{"body_name": "ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1"}] |
| Forward reference on analysis copy | {"type_code": -1, "entity": null} |

Original current final geometry exposes 8 faces owned by this feature. These are not a before/after removed volume. Scope body names refer to the rollback-stage body on the copy, not automatically the final body's name.

Sketch `ｽｹｯﾁ2`: 6 profile segments + 0 construction segments; API contours=1, regions=2. Endpoint-graph closure (derived)=False.
Model-space sketch normal: [0, 0, -1]; plane class (derived)=axis_aligned. Model-space profile bounds mm: [-817.5, -0, -2, -754.4, 63, -2]. Full segment coordinates, plane transform and supporting entity are in JSON.

`INFERRED` purpose: END_LOCAL_CANDIDATE; native end location: negative_end. A local-end bounding box is a screening heuristic, not proof of a specific assembly joint. Removed stock and retained stock masks remain UNVERIFIED without a before-state body.

## 3615-04-bh-T1-fr1.SLDPRT — ｶｯﾄ - 押し出し2

`API_VERIFIED`: path 26; `Cut` / legacy `Cut`; config `Default`; suppressed=False.

| Parameter | API value |
| --- | --- |
| Forward End Condition | swEndCondThroughAllBoth |
| Reverse End Condition (raw) | swEndCondThroughAll |
| Stored depth forward / reverse (mm) | 2 / 10 |
| ReverseDirection / BothDirections | False / True |
| FlipSideToCut / NormalCut | False / True |
| Thin / link to thickness | False / True |
| Draft enabled forward / reverse | False / False |
| FromType / FromOffsetDistance (m) | 0 / 0 |
| Affected-body count on analysis copy | 1 |
| Affected-body identifiers | [{"body_name": "ｶｯﾄ - 押し出し1"}] |
| Forward reference on analysis copy | {"type_code": -1, "entity": null} |

Original current final geometry exposes 8 faces owned by this feature. These are not a before/after removed volume. Scope body names refer to the rollback-stage body on the copy, not automatically the final body's name.

Sketch `ｽｹｯﾁ3`: 5 profile segments + 0 construction segments; API contours=1, regions=2. Endpoint-graph closure (derived)=False.
Model-space sketch normal: [0, 0, -1]; plane class (derived)=axis_aligned. Model-space profile bounds mm: [756.5, -0, -2, 804.5, 63, -2]. Full segment coordinates, plane transform and supporting entity are in JSON.

`INFERRED` purpose: END_LOCAL_CANDIDATE; native end location: positive_end. A local-end bounding box is a screening heuristic, not proof of a specific assembly joint. Removed stock and retained stock masks remain UNVERIFIED without a before-state body.

## 3615-04-bh-T1-fr1.SLDPRT — ｶｯﾄ - 押し出し11

`API_VERIFIED`: path 28; `ICE` / legacy `Cut`; config `Default`; suppressed=False.

| Parameter | API value |
| --- | --- |
| Forward End Condition | swEndCondBlind |
| Reverse End Condition (raw) | swEndCondThroughAll |
| Stored depth forward / reverse (mm) | 2 / 10 |
| ReverseDirection / BothDirections | False / False |
| FlipSideToCut / NormalCut | False / False |
| Thin / link to thickness | False / True |
| Draft enabled forward / reverse | False / False |
| FromType / FromOffsetDistance (m) | 0 / 0 |
| Affected-body count on analysis copy | 1 |
| Affected-body identifiers | [{"body_name": "ｶｯﾄ - 押し出し2"}] |
| Forward reference on analysis copy | {"type_code": -1, "entity": null} |

Original current final geometry exposes 4 faces owned by this feature. These are not a before/after removed volume. Scope body names refer to the rollback-stage body on the copy, not automatically the final body's name.

Sketch `a`: 4 profile segments + 3 construction segments; API contours=1, regions=2. Endpoint-graph closure (derived)=True.
Model-space sketch normal: [0, 0, -1]; plane class (derived)=axis_aligned. Model-space profile bounds mm: [-665.5, 20, -2, -649.5, 30, -2]. Full segment coordinates, plane transform and supporting entity are in JSON.

`INFERRED` purpose: OTHER_MACHINING_OR_UNKNOWN; native end location: not established. A local-end bounding box is a screening heuristic, not proof of a specific assembly joint. Removed stock and retained stock masks remain UNVERIFIED without a before-state body.

## 3615-04-bh-T1-fr1.SLDPRT — ｶｯﾄ - 押し出し13

`API_VERIFIED`: path 33; `ICE` / legacy `Cut`; config `Default`; suppressed=False.

| Parameter | API value |
| --- | --- |
| Forward End Condition | swEndCondBlind |
| Reverse End Condition (raw) | swEndCondBlind |
| Stored depth forward / reverse (mm) | 2 / 10 |
| ReverseDirection / BothDirections | False / False |
| FlipSideToCut / NormalCut | False / True |
| Thin / link to thickness | False / True |
| Draft enabled forward / reverse | False / False |
| FromType / FromOffsetDistance (m) | 0 / 0 |
| Affected-body count on analysis copy | 1 |
| Affected-body identifiers | [{"body_name": "ｶｰﾌﾞ ﾊﾟﾀｰﾝ2"}] |
| Forward reference on analysis copy | {"type_code": -1, "entity": null} |

Original current final geometry exposes 1 faces owned by this feature. These are not a before/after removed volume. Scope body names refer to the rollback-stage body on the copy, not automatically the final body's name.

Sketch `a1`: 1 profile segments + 2 construction segments; API contours=1, regions=2. Endpoint-graph closure (derived)=True.
Model-space sketch normal: [0, 0, -1]; plane class (derived)=axis_aligned. Model-space profile bounds mm: [-759.6, 55.4, -32, -755.4, 59.6, -32]. Full segment coordinates, plane transform and supporting entity are in JSON.

`INFERRED` purpose: OTHER_MACHINING_OR_UNKNOWN; native end location: not established. A local-end bounding box is a screening heuristic, not proof of a specific assembly joint. Removed stock and retained stock masks remain UNVERIFIED without a before-state body.

## 3615-04-bh-T1-fr2.SLDPRT — ｶｯﾄ - 押し出し1

`API_VERIFIED`: path 24; `Cut` / legacy `Cut`; config `Default`; suppressed=False.

| Parameter | API value |
| --- | --- |
| Forward End Condition | swEndCondThroughAllBoth |
| Reverse End Condition (raw) | swEndCondThroughAll |
| Stored depth forward / reverse (mm) | 2 / 10 |
| ReverseDirection / BothDirections | False / True |
| FlipSideToCut / NormalCut | False / True |
| Thin / link to thickness | False / True |
| Draft enabled forward / reverse | False / False |
| FromType / FromOffsetDistance (m) | 0 / 0 |
| Affected-body count on analysis copy | 1 |
| Affected-body identifiers | [{"body_name": "ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1"}] |
| Forward reference on analysis copy | {"type_code": -1, "entity": null} |

Original current final geometry exposes 8 faces owned by this feature. These are not a before/after removed volume. Scope body names refer to the rollback-stage body on the copy, not automatically the final body's name.

Sketch `ｽｹｯﾁ2`: 5 profile segments + 0 construction segments; API contours=1, regions=2. Endpoint-graph closure (derived)=False.
Model-space sketch normal: [0, 0, -1]; plane class (derived)=axis_aligned. Model-space profile bounds mm: [476.5, -0, -2, 524.5, 63, -2]. Full segment coordinates, plane transform and supporting entity are in JSON.

`INFERRED` purpose: END_LOCAL_CANDIDATE; native end location: positive_end. A local-end bounding box is a screening heuristic, not proof of a specific assembly joint. Removed stock and retained stock masks remain UNVERIFIED without a before-state body.

## 3615-04-bh-T1-fr2.SLDPRT — ｶｯﾄ - 押し出し2

`API_VERIFIED`: path 26; `Cut` / legacy `Cut`; config `Default`; suppressed=False.

| Parameter | API value |
| --- | --- |
| Forward End Condition | swEndCondThroughAllBoth |
| Reverse End Condition (raw) | swEndCondThroughAll |
| Stored depth forward / reverse (mm) | 2 / 10 |
| ReverseDirection / BothDirections | False / True |
| FlipSideToCut / NormalCut | True / True |
| Thin / link to thickness | False / True |
| Draft enabled forward / reverse | False / False |
| FromType / FromOffsetDistance (m) | 0 / 0 |
| Affected-body count on analysis copy | 1 |
| Affected-body identifiers | [{"body_name": "ｶｯﾄ - 押し出し1"}] |
| Forward reference on analysis copy | {"type_code": -1, "entity": null} |

Original current final geometry exposes 8 faces owned by this feature. These are not a before/after removed volume. Scope body names refer to the rollback-stage body on the copy, not automatically the final body's name.

Sketch `ｽｹｯﾁ3`: 6 profile segments + 0 construction segments; API contours=1, regions=2. Endpoint-graph closure (derived)=False.
Model-space sketch normal: [0, 0, -1]; plane class (derived)=axis_aligned. Model-space profile bounds mm: [-537.5, -0, -2, -474.4, 63, -2]. Full segment coordinates, plane transform and supporting entity are in JSON.

`INFERRED` purpose: END_LOCAL_CANDIDATE; native end location: negative_end. A local-end bounding box is a screening heuristic, not proof of a specific assembly joint. Removed stock and retained stock masks remain UNVERIFIED without a before-state body.

## 3615-04-bh-T1-fr2.SLDPRT — ｶｯﾄ - 押し出し11

`API_VERIFIED`: path 28; `ICE` / legacy `Cut`; config `Default`; suppressed=False.

| Parameter | API value |
| --- | --- |
| Forward End Condition | swEndCondBlind |
| Reverse End Condition (raw) | swEndCondThroughAll |
| Stored depth forward / reverse (mm) | 2 / 10 |
| ReverseDirection / BothDirections | False / False |
| FlipSideToCut / NormalCut | False / False |
| Thin / link to thickness | False / True |
| Draft enabled forward / reverse | False / False |
| FromType / FromOffsetDistance (m) | 0 / 0 |
| Affected-body count on analysis copy | 1 |
| Affected-body identifiers | [{"body_name": "ｶｯﾄ - 押し出し2"}] |
| Forward reference on analysis copy | {"type_code": -1, "entity": null} |

Original current final geometry exposes 4 faces owned by this feature. These are not a before/after removed volume. Scope body names refer to the rollback-stage body on the copy, not automatically the final body's name.

Sketch `a`: 4 profile segments + 3 construction segments; API contours=1, regions=2. Endpoint-graph closure (derived)=True.
Model-space sketch normal: [0, 0, -1]; plane class (derived)=axis_aligned. Model-space profile bounds mm: [-385.5, 20, -2, -369.5, 30, -2]. Full segment coordinates, plane transform and supporting entity are in JSON.

`INFERRED` purpose: OTHER_MACHINING_OR_UNKNOWN; native end location: not established. A local-end bounding box is a screening heuristic, not proof of a specific assembly joint. Removed stock and retained stock masks remain UNVERIFIED without a before-state body.

## 3615-04-bh-T1-fr3.SLDPRT — ｶｯﾄ - 押し出し1

`API_VERIFIED`: path 24; `Cut` / legacy `Cut`; config `Default`; suppressed=False.

| Parameter | API value |
| --- | --- |
| Forward End Condition | swEndCondThroughAllBoth |
| Reverse End Condition (raw) | swEndCondThroughAll |
| Stored depth forward / reverse (mm) | 2 / 10 |
| ReverseDirection / BothDirections | False / True |
| FlipSideToCut / NormalCut | True / True |
| Thin / link to thickness | False / True |
| Draft enabled forward / reverse | False / False |
| FromType / FromOffsetDistance (m) | 0 / 0 |
| Affected-body count on analysis copy | 1 |
| Affected-body identifiers | [{"body_name": "ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1"}] |
| Forward reference on analysis copy | {"type_code": -1, "entity": null} |

Original current final geometry exposes 8 faces owned by this feature. These are not a before/after removed volume. Scope body names refer to the rollback-stage body on the copy, not automatically the final body's name.

Sketch `ｽｹｯﾁ2`: 5 profile segments + 0 construction segments; API contours=1, regions=2. Endpoint-graph closure (derived)=False.
Model-space sketch normal: [0, 0, -1]; plane class (derived)=axis_aligned. Model-space profile bounds mm: [756.5, -13, -2, 804.5, 50, -2]. Full segment coordinates, plane transform and supporting entity are in JSON.

`INFERRED` purpose: END_LOCAL_CANDIDATE; native end location: positive_end. A local-end bounding box is a screening heuristic, not proof of a specific assembly joint. Removed stock and retained stock masks remain UNVERIFIED without a before-state body.

## 3615-04-bh-T1-fr3.SLDPRT — ｶｯﾄ - 押し出し2

`API_VERIFIED`: path 26; `Cut` / legacy `Cut`; config `Default`; suppressed=False.

| Parameter | API value |
| --- | --- |
| Forward End Condition | swEndCondThroughAllBoth |
| Reverse End Condition (raw) | swEndCondThroughAll |
| Stored depth forward / reverse (mm) | 2 / 10 |
| ReverseDirection / BothDirections | False / True |
| FlipSideToCut / NormalCut | False / True |
| Thin / link to thickness | False / True |
| Draft enabled forward / reverse | False / False |
| FromType / FromOffsetDistance (m) | 0 / 0 |
| Affected-body count on analysis copy | 1 |
| Affected-body identifiers | [{"body_name": "ｶｯﾄ - 押し出し1"}] |
| Forward reference on analysis copy | {"type_code": -1, "entity": null} |

Original current final geometry exposes 8 faces owned by this feature. These are not a before/after removed volume. Scope body names refer to the rollback-stage body on the copy, not automatically the final body's name.

Sketch `ｽｹｯﾁ3`: 5 profile segments + 0 construction segments; API contours=1, regions=2. Endpoint-graph closure (derived)=False.
Model-space sketch normal: [0, 0, -1]; plane class (derived)=axis_aligned. Model-space profile bounds mm: [-804.5, -13, -2, -756.5, 50, -2]. Full segment coordinates, plane transform and supporting entity are in JSON.

`INFERRED` purpose: END_LOCAL_CANDIDATE; native end location: negative_end. A local-end bounding box is a screening heuristic, not proof of a specific assembly joint. Removed stock and retained stock masks remain UNVERIFIED without a before-state body.

## 3615-04-bh-T1-fr3.SLDPRT — ｶｯﾄ - 押し出し11

`API_VERIFIED`: path 28; `ICE` / legacy `Cut`; config `Default`; suppressed=False.

| Parameter | API value |
| --- | --- |
| Forward End Condition | swEndCondBlind |
| Reverse End Condition (raw) | swEndCondThroughAll |
| Stored depth forward / reverse (mm) | 2 / 10 |
| ReverseDirection / BothDirections | False / False |
| FlipSideToCut / NormalCut | False / False |
| Thin / link to thickness | False / True |
| Draft enabled forward / reverse | False / False |
| FromType / FromOffsetDistance (m) | 0 / 0 |
| Affected-body count on analysis copy | 1 |
| Affected-body identifiers | [{"body_name": "ｶｯﾄ - 押し出し2"}] |
| Forward reference on analysis copy | {"type_code": -1, "entity": null} |

Original current final geometry exposes 4 faces owned by this feature. These are not a before/after removed volume. Scope body names refer to the rollback-stage body on the copy, not automatically the final body's name.

Sketch `a`: 4 profile segments + 3 construction segments; API contours=1, regions=2. Endpoint-graph closure (derived)=True.
Model-space sketch normal: [0, 0, -1]; plane class (derived)=axis_aligned. Model-space profile bounds mm: [649.5, 20, -2, 665.5, 30, -2]. Full segment coordinates, plane transform and supporting entity are in JSON.

`INFERRED` purpose: OTHER_MACHINING_OR_UNKNOWN; native end location: not established. A local-end bounding box is a screening heuristic, not proof of a specific assembly joint. Removed stock and retained stock masks remain UNVERIFIED without a before-state body.

## 3615-04-bh-T1-fr3.SLDPRT — ｶｯﾄ - 押し出し13

`API_VERIFIED`: path 33; `ICE` / legacy `Cut`; config `Default`; suppressed=False.

| Parameter | API value |
| --- | --- |
| Forward End Condition | swEndCondBlind |
| Reverse End Condition (raw) | swEndCondBlind |
| Stored depth forward / reverse (mm) | 2 / 10 |
| ReverseDirection / BothDirections | False / False |
| FlipSideToCut / NormalCut | False / True |
| Thin / link to thickness | False / True |
| Draft enabled forward / reverse | False / False |
| FromType / FromOffsetDistance (m) | 0 / 0 |
| Affected-body count on analysis copy | 1 |
| Affected-body identifiers | [{"body_name": "ｶｰﾌﾞ ﾊﾟﾀｰﾝ2"}] |
| Forward reference on analysis copy | {"type_code": -1, "entity": null} |

Original current final geometry exposes 1 faces owned by this feature. These are not a before/after removed volume. Scope body names refer to the rollback-stage body on the copy, not automatically the final body's name.

Sketch `a1`: 1 profile segments + 2 construction segments; API contours=1, regions=2. Endpoint-graph closure (derived)=True.
Model-space sketch normal: [0, 0, -1]; plane class (derived)=axis_aligned. Model-space profile bounds mm: [-759.6, -9.6, -32, -755.4, -5.4, -32]. Full segment coordinates, plane transform and supporting entity are in JSON.

`INFERRED` purpose: OTHER_MACHINING_OR_UNKNOWN; native end location: not established. A local-end bounding box is a screening heuristic, not proof of a specific assembly joint. Removed stock and retained stock masks remain UNVERIFIED without a before-state body.

## 3615-04-bh-T1-fr4.SLDPRT — ｶｯﾄ - 押し出し1

`API_VERIFIED`: path 24; `Cut` / legacy `Cut`; config `Default`; suppressed=False.

| Parameter | API value |
| --- | --- |
| Forward End Condition | swEndCondThroughAllBoth |
| Reverse End Condition (raw) | swEndCondThroughAll |
| Stored depth forward / reverse (mm) | 2 / 10 |
| ReverseDirection / BothDirections | False / True |
| FlipSideToCut / NormalCut | True / True |
| Thin / link to thickness | False / True |
| Draft enabled forward / reverse | False / False |
| FromType / FromOffsetDistance (m) | 0 / 0 |
| Affected-body count on analysis copy | 1 |
| Affected-body identifiers | [{"body_name": "ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1"}] |
| Forward reference on analysis copy | {"type_code": -1, "entity": null} |

Original current final geometry exposes 6 faces owned by this feature. These are not a before/after removed volume. Scope body names refer to the rollback-stage body on the copy, not automatically the final body's name.

Sketch `ｽｹｯﾁ2`: 4 profile segments + 0 construction segments; API contours=1, regions=2. Endpoint-graph closure (derived)=False.
Model-space sketch normal: [0, 0, -1]; plane class (derived)=axis_aligned. Model-space profile bounds mm: [-522.4, -0, -2, -474.4, 50, -2]. Full segment coordinates, plane transform and supporting entity are in JSON.

`INFERRED` purpose: END_LOCAL_CANDIDATE; native end location: negative_end. A local-end bounding box is a screening heuristic, not proof of a specific assembly joint. Removed stock and retained stock masks remain UNVERIFIED without a before-state body.

## 3615-04-bh-T1-fr4.SLDPRT — ｶｯﾄ - 押し出し2

`API_VERIFIED`: path 26; `Cut` / legacy `Cut`; config `Default`; suppressed=False.

| Parameter | API value |
| --- | --- |
| Forward End Condition | swEndCondThroughAllBoth |
| Reverse End Condition (raw) | swEndCondThroughAll |
| Stored depth forward / reverse (mm) | 2 / 10 |
| ReverseDirection / BothDirections | False / True |
| FlipSideToCut / NormalCut | False / True |
| Thin / link to thickness | False / True |
| Draft enabled forward / reverse | False / False |
| FromType / FromOffsetDistance (m) | 0 / 0 |
| Affected-body count on analysis copy | 1 |
| Affected-body identifiers | [{"body_name": "ｶｯﾄ - 押し出し1"}] |
| Forward reference on analysis copy | {"type_code": -1, "entity": null} |

Original current final geometry exposes 5 faces owned by this feature. These are not a before/after removed volume. Scope body names refer to the rollback-stage body on the copy, not automatically the final body's name.

Sketch `ｽｹｯﾁ3`: 4 profile segments + 0 construction segments; API contours=1, regions=2. Endpoint-graph closure (derived)=False.
Model-space sketch normal: [0, 0, -1]; plane class (derived)=axis_aligned. Model-space profile bounds mm: [474.4, -0, -2, 522.4, 50, -2]. Full segment coordinates, plane transform and supporting entity are in JSON.

`INFERRED` purpose: END_LOCAL_CANDIDATE; native end location: positive_end. A local-end bounding box is a screening heuristic, not proof of a specific assembly joint. Removed stock and retained stock masks remain UNVERIFIED without a before-state body.

## 3615-04-bh-T1-fr4.SLDPRT — ｶｯﾄ - 押し出し11

`API_VERIFIED`: path 28; `ICE` / legacy `Cut`; config `Default`; suppressed=False.

| Parameter | API value |
| --- | --- |
| Forward End Condition | swEndCondBlind |
| Reverse End Condition (raw) | swEndCondThroughAll |
| Stored depth forward / reverse (mm) | 2 / 10 |
| ReverseDirection / BothDirections | False / False |
| FlipSideToCut / NormalCut | False / False |
| Thin / link to thickness | False / True |
| Draft enabled forward / reverse | False / False |
| FromType / FromOffsetDistance (m) | 0 / 0 |
| Affected-body count on analysis copy | 1 |
| Affected-body identifiers | [{"body_name": "ｶｯﾄ - 押し出し2"}] |
| Forward reference on analysis copy | {"type_code": -1, "entity": null} |

Original current final geometry exposes 4 faces owned by this feature. These are not a before/after removed volume. Scope body names refer to the rollback-stage body on the copy, not automatically the final body's name.

Sketch `a`: 4 profile segments + 3 construction segments; API contours=1, regions=2. Endpoint-graph closure (derived)=True.
Model-space sketch normal: [0, 0, -1]; plane class (derived)=axis_aligned. Model-space profile bounds mm: [-385.5, 20, -2, -369.5, 30, -2]. Full segment coordinates, plane transform and supporting entity are in JSON.

`INFERRED` purpose: OTHER_MACHINING_OR_UNKNOWN; native end location: not established. A local-end bounding box is a screening heuristic, not proof of a specific assembly joint. Removed stock and retained stock masks remain UNVERIFIED without a before-state body.

## 3615-04-bh-T2-fr1.SLDPRT — ｶｯﾄ - 押し出し1

`API_VERIFIED`: path 24; `Cut` / legacy `Cut`; config `Default`; suppressed=False.

| Parameter | API value |
| --- | --- |
| Forward End Condition | swEndCondThroughAllBoth |
| Reverse End Condition (raw) | swEndCondThroughAll |
| Stored depth forward / reverse (mm) | 2 / 10 |
| ReverseDirection / BothDirections | False / True |
| FlipSideToCut / NormalCut | True / True |
| Thin / link to thickness | False / True |
| Draft enabled forward / reverse | False / False |
| FromType / FromOffsetDistance (m) | 0 / 0 |
| Affected-body count on analysis copy | 1 |
| Affected-body identifiers | [{"body_name": "ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1"}] |
| Forward reference on analysis copy | {"type_code": -1, "entity": null} |

Original current final geometry exposes 8 faces owned by this feature. These are not a before/after removed volume. Scope body names refer to the rollback-stage body on the copy, not automatically the final body's name.

Sketch `ｽｹｯﾁ2`: 6 profile segments + 0 construction segments; API contours=1, regions=2. Endpoint-graph closure (derived)=False.
Model-space sketch normal: [0, 0, -1]; plane class (derived)=axis_aligned. Model-space profile bounds mm: [-580, -0, -2, -516.9, 63, -2]. Full segment coordinates, plane transform and supporting entity are in JSON.

`INFERRED` purpose: END_LOCAL_CANDIDATE; native end location: negative_end. A local-end bounding box is a screening heuristic, not proof of a specific assembly joint. Removed stock and retained stock masks remain UNVERIFIED without a before-state body.

## 3615-04-bh-T2-fr1.SLDPRT — ｶｯﾄ - 押し出し2

`API_VERIFIED`: path 26; `Cut` / legacy `Cut`; config `Default`; suppressed=False.

| Parameter | API value |
| --- | --- |
| Forward End Condition | swEndCondThroughAllBoth |
| Reverse End Condition (raw) | swEndCondThroughAll |
| Stored depth forward / reverse (mm) | 2 / 10 |
| ReverseDirection / BothDirections | False / True |
| FlipSideToCut / NormalCut | False / True |
| Thin / link to thickness | False / True |
| Draft enabled forward / reverse | False / False |
| FromType / FromOffsetDistance (m) | 0 / 0 |
| Affected-body count on analysis copy | 1 |
| Affected-body identifiers | [{"body_name": "ｶｯﾄ - 押し出し1"}] |
| Forward reference on analysis copy | {"type_code": -1, "entity": null} |

Original current final geometry exposes 7 faces owned by this feature. These are not a before/after removed volume. Scope body names refer to the rollback-stage body on the copy, not automatically the final body's name.

Sketch `ｽｹｯﾁ3`: 6 profile segments + 0 construction segments; API contours=1, regions=2. Endpoint-graph closure (derived)=False.
Model-space sketch normal: [0, 0, -1]; plane class (derived)=axis_aligned. Model-space profile bounds mm: [516.9, -0, -2, 580, 63, -2]. Full segment coordinates, plane transform and supporting entity are in JSON.

`INFERRED` purpose: END_LOCAL_CANDIDATE; native end location: positive_end. A local-end bounding box is a screening heuristic, not proof of a specific assembly joint. Removed stock and retained stock masks remain UNVERIFIED without a before-state body.

## 3615-04-bh-T2-fr1.SLDPRT — ｶｯﾄ - 押し出し11

`API_VERIFIED`: path 28; `ICE` / legacy `Cut`; config `Default`; suppressed=False.

| Parameter | API value |
| --- | --- |
| Forward End Condition | swEndCondBlind |
| Reverse End Condition (raw) | swEndCondThroughAll |
| Stored depth forward / reverse (mm) | 2 / 10 |
| ReverseDirection / BothDirections | False / False |
| FlipSideToCut / NormalCut | False / False |
| Thin / link to thickness | False / True |
| Draft enabled forward / reverse | False / False |
| FromType / FromOffsetDistance (m) | 0 / 0 |
| Affected-body count on analysis copy | 1 |
| Affected-body identifiers | [{"body_name": "ｶｯﾄ - 押し出し2"}] |
| Forward reference on analysis copy | {"type_code": -1, "entity": null} |

Original current final geometry exposes 4 faces owned by this feature. These are not a before/after removed volume. Scope body names refer to the rollback-stage body on the copy, not automatically the final body's name.

Sketch `a`: 4 profile segments + 3 construction segments; API contours=1, regions=2. Endpoint-graph closure (derived)=True.
Model-space sketch normal: [0, 0, -1]; plane class (derived)=axis_aligned. Model-space profile bounds mm: [-428, 20, -2, -412, 30, -2]. Full segment coordinates, plane transform and supporting entity are in JSON.

`INFERRED` purpose: OTHER_MACHINING_OR_UNKNOWN; native end location: not established. A local-end bounding box is a screening heuristic, not proof of a specific assembly joint. Removed stock and retained stock masks remain UNVERIFIED without a before-state body.

## 3615-04-bh-T2-fr1.SLDPRT — ｶｯﾄ - 押し出し13

`API_VERIFIED`: path 33; `ICE` / legacy `Cut`; config `Default`; suppressed=False.

| Parameter | API value |
| --- | --- |
| Forward End Condition | swEndCondBlind |
| Reverse End Condition (raw) | swEndCondBlind |
| Stored depth forward / reverse (mm) | 2 / 10 |
| ReverseDirection / BothDirections | False / False |
| FlipSideToCut / NormalCut | False / True |
| Thin / link to thickness | False / True |
| Draft enabled forward / reverse | False / False |
| FromType / FromOffsetDistance (m) | 0 / 0 |
| Affected-body count on analysis copy | 1 |
| Affected-body identifiers | [{"body_name": "ｶｰﾌﾞ ﾊﾟﾀｰﾝ2"}] |
| Forward reference on analysis copy | {"type_code": -1, "entity": null} |

Original current final geometry exposes 1 faces owned by this feature. These are not a before/after removed volume. Scope body names refer to the rollback-stage body on the copy, not automatically the final body's name.

Sketch `a1`: 1 profile segments + 2 construction segments; API contours=1, regions=2. Endpoint-graph closure (derived)=True.
Model-space sketch normal: [0, 0, -1]; plane class (derived)=axis_aligned. Model-space profile bounds mm: [-522.1, 55.4, -32, -517.9, 59.6, -32]. Full segment coordinates, plane transform and supporting entity are in JSON.

`INFERRED` purpose: OTHER_MACHINING_OR_UNKNOWN; native end location: not established. A local-end bounding box is a screening heuristic, not proof of a specific assembly joint. Removed stock and retained stock masks remain UNVERIFIED without a before-state body.

## 3615-04-bh-T2-fr2.SLDPRT — ｶｯﾄ - 押し出し1

`API_VERIFIED`: path 24; `Cut` / legacy `Cut`; config `Default`; suppressed=False.

| Parameter | API value |
| --- | --- |
| Forward End Condition | swEndCondThroughAllBoth |
| Reverse End Condition (raw) | swEndCondThroughAll |
| Stored depth forward / reverse (mm) | 2 / 10 |
| ReverseDirection / BothDirections | False / True |
| FlipSideToCut / NormalCut | False / True |
| Thin / link to thickness | False / True |
| Draft enabled forward / reverse | False / False |
| FromType / FromOffsetDistance (m) | 0 / 0 |
| Affected-body count on analysis copy | 1 |
| Affected-body identifiers | [{"body_name": "ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1"}] |
| Forward reference on analysis copy | {"type_code": -1, "entity": null} |

Original current final geometry exposes 8 faces owned by this feature. These are not a before/after removed volume. Scope body names refer to the rollback-stage body on the copy, not automatically the final body's name.

Sketch `ｽｹｯﾁ2`: 5 profile segments + 0 construction segments; API contours=1, regions=2. Endpoint-graph closure (derived)=False.
Model-space sketch normal: [0, 0, -1]; plane class (derived)=axis_aligned. Model-space profile bounds mm: [601.5, -0, -2, 649.5, 63, -2]. Full segment coordinates, plane transform and supporting entity are in JSON.

`INFERRED` purpose: END_LOCAL_CANDIDATE; native end location: positive_end. A local-end bounding box is a screening heuristic, not proof of a specific assembly joint. Removed stock and retained stock masks remain UNVERIFIED without a before-state body.

## 3615-04-bh-T2-fr2.SLDPRT — ｶｯﾄ - 押し出し2

`API_VERIFIED`: path 26; `Cut` / legacy `Cut`; config `Default`; suppressed=False.

| Parameter | API value |
| --- | --- |
| Forward End Condition | swEndCondThroughAllBoth |
| Reverse End Condition (raw) | swEndCondThroughAll |
| Stored depth forward / reverse (mm) | 2 / 10 |
| ReverseDirection / BothDirections | False / True |
| FlipSideToCut / NormalCut | True / True |
| Thin / link to thickness | False / True |
| Draft enabled forward / reverse | False / False |
| FromType / FromOffsetDistance (m) | 0 / 0 |
| Affected-body count on analysis copy | 1 |
| Affected-body identifiers | [{"body_name": "ｶｯﾄ - 押し出し1"}] |
| Forward reference on analysis copy | {"type_code": -1, "entity": null} |

Original current final geometry exposes 8 faces owned by this feature. These are not a before/after removed volume. Scope body names refer to the rollback-stage body on the copy, not automatically the final body's name.

Sketch `ｽｹｯﾁ3`: 5 profile segments + 0 construction segments; API contours=1, regions=2. Endpoint-graph closure (derived)=False.
Model-space sketch normal: [0, 0, -1]; plane class (derived)=axis_aligned. Model-space profile bounds mm: [-649.5, -0, -2, -601.5, 63, -2]. Full segment coordinates, plane transform and supporting entity are in JSON.

`INFERRED` purpose: END_LOCAL_CANDIDATE; native end location: negative_end. A local-end bounding box is a screening heuristic, not proof of a specific assembly joint. Removed stock and retained stock masks remain UNVERIFIED without a before-state body.

## 3615-04-bh-T2-fr2.SLDPRT — ｶｯﾄ - 押し出し11

`API_VERIFIED`: path 28; `ICE` / legacy `Cut`; config `Default`; suppressed=False.

| Parameter | API value |
| --- | --- |
| Forward End Condition | swEndCondBlind |
| Reverse End Condition (raw) | swEndCondThroughAll |
| Stored depth forward / reverse (mm) | 2 / 10 |
| ReverseDirection / BothDirections | False / False |
| FlipSideToCut / NormalCut | False / False |
| Thin / link to thickness | False / True |
| Draft enabled forward / reverse | False / False |
| FromType / FromOffsetDistance (m) | 0 / 0 |
| Affected-body count on analysis copy | 1 |
| Affected-body identifiers | [{"body_name": "ｶｯﾄ - 押し出し2"}] |
| Forward reference on analysis copy | {"type_code": -1, "entity": null} |

Original current final geometry exposes 4 faces owned by this feature. These are not a before/after removed volume. Scope body names refer to the rollback-stage body on the copy, not automatically the final body's name.

Sketch `a`: 4 profile segments + 3 construction segments; API contours=1, regions=2. Endpoint-graph closure (derived)=True.
Model-space sketch normal: [0, 0, -1]; plane class (derived)=axis_aligned. Model-space profile bounds mm: [-510.5, 20, -2, -494.5, 30, -2]. Full segment coordinates, plane transform and supporting entity are in JSON.

`INFERRED` purpose: OTHER_MACHINING_OR_UNKNOWN; native end location: not established. A local-end bounding box is a screening heuristic, not proof of a specific assembly joint. Removed stock and retained stock masks remain UNVERIFIED without a before-state body.

## 4114-04-HN-C07-12_New.SLDPRT — ｶｯﾄ - 押し出し1

`API_VERIFIED`: path 26; `ICE` / legacy `Cut`; config `ﾃﾞﾌｫﾙﾄ`; suppressed=False.

| Parameter | API value |
| --- | --- |
| Forward End Condition | swEndCondBlind |
| Reverse End Condition (raw) | swEndCondBlind |
| Stored depth forward / reverse (mm) | 2.3 / 10 |
| ReverseDirection / BothDirections | False / False |
| FlipSideToCut / NormalCut | False / True |
| Thin / link to thickness | False / True |
| Draft enabled forward / reverse | False / False |
| FromType / FromOffsetDistance (m) | 0 / 0 |
| Affected-body count on analysis copy | 1 |
| Affected-body identifiers | [{"body_name": "ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1"}] |
| Forward reference on analysis copy | {"type_code": -1, "entity": null} |

Original current final geometry exposes 2 faces owned by this feature. These are not a before/after removed volume. Scope body names refer to the rollback-stage body on the copy, not automatically the final body's name.

Sketch `ｽｹｯﾁ6`: 2 profile segments + 0 construction segments; API contours=2, regions=3. Endpoint-graph closure (derived)=True.
Model-space sketch normal: [1, 0, 0]; plane class (derived)=axis_aligned. Model-space profile bounds mm: [2.3, -27.1, -84.1, 2.3, -22.9, -27.9]. Full segment coordinates, plane transform and supporting entity are in JSON.

`INFERRED` purpose: OTHER_MACHINING_OR_UNKNOWN; native end location: not established. A local-end bounding box is a screening heuristic, not proof of a specific assembly joint. Removed stock and retained stock masks remain UNVERIFIED without a before-state body.

## Other feature types with manufacturing effects

The following are real API nodes but are not added to the 43 Cut-Extrude count. In particular a Hole Wizard can remove material and a pattern can repeat a slot; neither is automatically a frame-end joint operation.

| Part | Path | Name | Type |
| --- | --- | --- | --- |
| 1618-04-bh-A6-fr1.SLDPRT | 24 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ1 | CurvePattern |
| 1618-04-bh-A6-fr1.SLDPRT | 25 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ2 | CurvePattern |
| 1618-04-bh-A6-fr1.SLDPRT | 26 | スタッド10x16<1> | LibraryFeature |
| 1618-04-bh-A6-fr1.SLDPRT | 34 | M4 すきま穴1 | HoleWzd |
| 1618-04-bh-A6-fr1.SLDPRT | 35 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ11 | CurvePattern |
| 1618-04-bh-A6-fr1.SLDPRT | 36 | M4-NCサラ(表)<1> | LibraryFeature |
| 1618-04-bh-A6-1.SLDPRT | 25 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ1 | CurvePattern |
| 1618-04-bh-A6-1.SLDPRT | 26 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ2 | CurvePattern |
| 1618-04-bh-A6-1.SLDPRT | 27 | スタッド7.5<1> | LibraryFeature |
| 1618-04-bh-A6-1.SLDPRT | 30 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ11 | CurvePattern |
| 1618-04-bh-A6-1.SLDPRT | 31 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ21 | CurvePattern |
| 1618-04-bh-A6-1.SLDPRT | 32 | スタッド7.5<2> | LibraryFeature |
| 1618-04-bh-A6-1.SLDPRT | 35 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ12 | CurvePattern |
| 1618-04-bh-A6-1.SLDPRT | 36 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ22 | CurvePattern |
| 1618-04-bh-A6-1.SLDPRT | 37 | スタッド7.5<3> | LibraryFeature |
| 1618-04-bh-A6-1.SLDPRT | 40 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ13 | CurvePattern |
| 1618-04-bh-A6-1.SLDPRT | 41 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ23 | CurvePattern |
| 1618-04-bh-A6-1.SLDPRT | 42 | スタッド7.5<4> | LibraryFeature |
| 1618-04-bh-A6-fr2.SLDPRT | 24 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ1 | CurvePattern |
| 1618-04-bh-A6-fr2.SLDPRT | 25 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ2 | CurvePattern |
| 1618-04-bh-A6-fr2.SLDPRT | 26 | スタッド10x16<1> | LibraryFeature |
| 1618-04-bh-A6-fr3.SLDPRT | 24 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ1 | CurvePattern |
| 1618-04-bh-A6-fr3.SLDPRT | 25 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ2 | CurvePattern |
| 1618-04-bh-A6-fr3.SLDPRT | 26 | スタッド10x16<1> | LibraryFeature |
| 1618-04-bh-A6-fr3.SLDPRT | 43 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ11 | CurvePattern |
| 1618-04-bh-A6-fr3.SLDPRT | 44 | 4.2<1> | LibraryFeature |
| 1618-04-bh-A6-fr4.SLDPRT | 24 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ1 | CurvePattern |
| 1618-04-bh-A6-fr4.SLDPRT | 25 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ2 | CurvePattern |
| 1618-04-bh-A6-fr4.SLDPRT | 26 | スタッド10x16<1> | LibraryFeature |
| 3615-04-bh-T1-fr1.SLDPRT | 29 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ1 | CurvePattern |
| 3615-04-bh-T1-fr1.SLDPRT | 30 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ2 | CurvePattern |
| 3615-04-bh-T1-fr1.SLDPRT | 31 | スタッド10x16<1> | LibraryFeature |
| 3615-04-bh-T1-fr1.SLDPRT | 34 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ11 | CurvePattern |
| 3615-04-bh-T1-fr1.SLDPRT | 35 | 4.2<1> | LibraryFeature |
| 3615-04-bh-T1-fr2.SLDPRT | 29 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ1 | CurvePattern |
| 3615-04-bh-T1-fr2.SLDPRT | 30 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ2 | CurvePattern |
| 3615-04-bh-T1-fr2.SLDPRT | 31 | スタッド10x16<1> | LibraryFeature |
| 3615-04-bh-T1-fr3.SLDPRT | 29 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ1 | CurvePattern |
| 3615-04-bh-T1-fr3.SLDPRT | 30 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ2 | CurvePattern |
| 3615-04-bh-T1-fr3.SLDPRT | 31 | スタッド10x16<1> | LibraryFeature |
| 3615-04-bh-T1-fr3.SLDPRT | 34 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ11 | CurvePattern |
| 3615-04-bh-T1-fr3.SLDPRT | 35 | 4.2<1> | LibraryFeature |
| 3615-04-bh-T1-fr4.SLDPRT | 29 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ1 | CurvePattern |
| 3615-04-bh-T1-fr4.SLDPRT | 30 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ2 | CurvePattern |
| 3615-04-bh-T1-fr4.SLDPRT | 31 | スタッド10x16<1> | LibraryFeature |
| 3615-04-bh-T2-fr1.SLDPRT | 29 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ1 | CurvePattern |
| 3615-04-bh-T2-fr1.SLDPRT | 30 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ2 | CurvePattern |
| 3615-04-bh-T2-fr1.SLDPRT | 31 | スタッド10x16<1> | LibraryFeature |
| 3615-04-bh-T2-fr1.SLDPRT | 34 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ11 | CurvePattern |
| 3615-04-bh-T2-fr1.SLDPRT | 35 | 4.2<1> | LibraryFeature |
| 3615-04-bh-T2-fr2.SLDPRT | 29 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ1 | CurvePattern |
| 3615-04-bh-T2-fr2.SLDPRT | 30 | ｶｰﾌﾞ ﾊﾟﾀｰﾝ2 | CurvePattern |
| 3615-04-bh-T2-fr2.SLDPRT | 31 | スタッド10x16<1> | LibraryFeature |
