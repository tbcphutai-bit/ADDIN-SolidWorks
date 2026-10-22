# Sanitized native experiment log observations

These are selected phase/feature observations. Full private logs and persistence tokens are not published.

## native_cross_end_undo_01

Private evidence SHA256: `f391cb66289ea8b9b75918623c5a6a67e45bea26fe76be9d46e71de76dd789d7`.

```text
[AFJ-HARNESS] APPLY_BEGIN utc=2026-10-22T13:52:10.6092495Z
[AFJ-END-FACE] representation=FACE_ATTACHED_NATIVE_END_SECTION cap_faces=11 boundary_edges=24 native_vertex_relations=24 tangent_relations=10 sketch_status=3 sketch_dims=0 new_planes=0 plan_length_m=0.14700000000000002 actual_blind_depth_m=0.14500000000000002
[AFJ-APPLY] CUT_VERIFIED frame=A id=FOLDED_RULE:SHORT_AXIAL_COVER feature=AFJ_0b2a7f8f_A_01_Cut delta_m3=0
[AFJ-END-FACE] representation=FACE_ATTACHED_NATIVE_END_SECTION cap_faces=5 boundary_edges=12 native_vertex_relations=12 tangent_relations=4 sketch_status=3 sketch_dims=0 new_planes=0 plan_length_m=0.22509999999999986 actual_blind_depth_m=0.22310000000000008
[AFJ-APPLY] CUT_VERIFIED frame=B id=FOLDED_RULE:LONG_AXIAL_BUTT feature=AFJ_0b2a7f8f_B_01_Cut delta_m3=0
[AFJ-HARNESS] APPLY_END ms=49861
[AFJ-END-UNDO] GROUP_FINISH finished=False
```

## native1618_fr3fr4_stock_01

Private evidence SHA256: `457b67eeb9e356e7c78bf8c5cdd911853914f33f4e77255f66d1639b139d1e2e`.

```text
[AFJ-HARNESS] APPLY_BEGIN utc=2026-10-22T13:54:41.8067579Z
[AFJ-END-FACE] representation=FACE_ATTACHED_NATIVE_END_SECTION cap_faces=11 boundary_edges=24 native_vertex_relations=24 tangent_relations=10 sketch_status=3 sketch_dims=0 new_planes=0 plan_length_m=0.071849999999999747 actual_blind_depth_m=0.069849999999999746
[AFJ-APPLY] CUT_VERIFIED frame=A id=FOLDED_RULE:LONG_AXIAL_BUTT feature=AFJ_24959a20_A_01_Cut delta_m3=0
[AFJ-END-FACE] representation=FACE_ATTACHED_NATIVE_END_SECTION cap_faces=7 boundary_edges=16 native_vertex_relations=16 tangent_relations=6 sketch_status=3 sketch_dims=0 new_planes=0 plan_length_m=0.40424999999999922 actual_blind_depth_m=0.40224999999999922
[AFJ-APPLY] CUT_VERIFIED frame=B id=FOLDED_RULE:SHORT_AXIAL_COVER feature=AFJ_24959a20_B_01_Cut delta_m3=0
[AFJ-HARNESS] APPLY_END ms=57463
```

## Direct relation movement probe

`direct_relations2354_follow_02`: two Coincident relations, constraint status 3, no dangling/overdefining relations, restored edit context. Requested translation was not applied after rebuild; overall FAIL_OR_UNVERIFIED. Hashes and blank cleanup were true.

## Positive direct-relation follow

`direct_relations2354_follow_free_02`: `PASS_NATIVE_DIRECT_GEOMETRY_FOLLOW`; exact native point followed the retained 1 mm component movement. The owning pose stayed unchanged, and all 35 original mate states (33 temporarily suppressed), fixed state, transforms and point were restored. This was a direct-relation-only test with no Cut. Hashes and blank cleanup were true.

Private result SHA256: `03f4ce205e02f9e3995ace94657e8f5284bf7f46c8317e9af4c449adb2efd567`.

## native3615_t1_stock_01

Private log SHA256: `121ebe9aeaf07a6d2f4b0bc8be4e37b19f93f3e24d4eba306001c36b16b925f0`.

```text
[AFJ-HARNESS] APPLY_BEGIN utc=2026-10-22T14:03:36.5724879Z
[AFJ-APPLY] CUT_VERIFIED frame=A id=U_RULE:COVER_WEB_WALL feature=AFJ_4e2cfced_A_01_Cut delta_m3=0
[AFJ-APPLY] CUT_VERIFIED frame=A id=U_RULE:NORMAL_BEND_CAP feature=AFJ_4e2cfced_A_02_Cut delta_m3=0
[AFJ-APPLY] CUT_VERIFIED frame=B id=U_RULE:LOSER_STEPPED_WEB_WALL_RETURN feature=AFJ_4e2cfced_B_01_Cut delta_m3=0
[AFJ-APPLY] CUT_VERIFIED frame=B id=U_RULE:FAR_BEND_CLEARANCE feature=AFJ_4e2cfced_B_02_Cut delta_m3=0
[AFJ-HARNESS] APPLY_END ms=26446
```

## native3615_t2_shared_scope_01

Private log SHA256: `7009173b2c2d3dc2ba1f81778e06b20ce16e2c8d8664f38be2adb324d2c9c797`.

```text
[AFJ-HARNESS] APPLY_BEGIN utc=2026-10-22T14:05:40.3269004Z
[AFJ-HARNESS] APPLY_END ms=273
```

## native1618_fr1fr2_stock_01

Private log SHA256: `210d3d3fe52e2b9daadf27aeac5ba59de46589cbb3b69acd601d938ea0e03dd7`.

```text
```

## public2354_folded_boundary_01

Private log SHA256: `b286b909a7bff7a8781ecbb666bf2bad01b0d762bb9ac84e71e823a90cbb2456`.

```text
[AFJ-R52-PERF] phase=source_state ms=180 cache=MISS
[AFJ-R52-PERF] phase=solve ms=37369 completed=True
[AFJ-R52] stage=DISPLAY3_END group=A_KEEP status=0 tracked=1 actual_visibility=UNVERIFIED
[AFJ-R52] stage=DISPLAY3_END group=B_KEEP status=0 tracked=2 actual_visibility=UNVERIFIED
[AFJ-R52-PERF] phase=display ms=423 reuse_keep=False
[AFJ-R52-PERF] phase=preview_total ms=38238 reused=False closing=False
[AFJ-R52-PERF] phase=source_state ms=192 cache=HIT
[AFJ-R52-PERF] phase=display mode=UNCHANGED no_solve=True no_hide=True no_display3=True
[AFJ-R52-PERF] phase=preview_total ms=269 reused=True closing=False
[AFJ-R52-PERF] phase=source_state ms=205 cache=HIT
[AFJ-R52] stage=DISPLAY3_END group=A_CUT status=0 tracked=3 actual_visibility=UNVERIFIED
[AFJ-R52] stage=DISPLAY3_END group=B_CUT status=0 tracked=4 actual_visibility=UNVERIFIED
[AFJ-R52-PERF] phase=display ms=457 reuse_keep=True
[AFJ-R52-PERF] phase=preview_total ms=727 reused=True closing=False
[AFJ-R52-PERF] phase=source_state ms=191 cache=HIT
[AFJ-R52-PERF] phase=display ms=155 reuse_keep=True
[AFJ-R52-PERF] phase=preview_total ms=410 reused=True closing=False
[AFJ-APPLY-PERF] phase=native_writer ms=48873
```
