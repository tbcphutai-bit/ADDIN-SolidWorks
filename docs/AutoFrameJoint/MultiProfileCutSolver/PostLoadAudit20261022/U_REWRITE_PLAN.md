# Proposed U miter geometric Sketch rewrite

**Status: INFERRED design, not compiled or native-tested.** This preserves `ORTHOGONAL_U_COVER` / `ORTHOGONAL_U_TALLER_COVER`, their current seam, operation order within each Part, cut depths and required KEEP/CUT result. It does not substitute a generic butt. Existing successful B-rep comparisons remain the acceptance oracle.

## 1. Carry intent out of Generate

Do not attempt to recover geometric intent from arbitrary polygon coordinates inside `WriteTool`. Extend the existing internal plan with a U-specific intent record produced beside the current polygons:

- Actual component/Part/configuration and native axis/outer-Web support.
- Source native feature ownership and persistent tokens for Web faces, Wall faces, longitudinal Web/Bend tangent edges, original profile Sketch segments and axial end supports.
- Common `o, ul, uw, d` basis, `sn, qn, sf, qf`, whether the near seam was clamped, and the exact source scalar of each boundary.
- Per polygon edge tags such as `MASTER_SEAM`, `SEAM_GAP`, `WALL_INNER_GAP`, `NEAR_TANGENT`, `FAR_TANGENT`, `RETURN_OUTER`, and `TOOL_CLOSURE`.
- Distinct manufacturing values: `AssemblyFitGap`, `CutterEnvelopeOverrun`, `SheetCutOverrun`, and tool closure margin. Equal numeric values must not merge their meanings.

Keep the existing `AfjToolDefinition` polygons as the independent temporary Boolean target. Reject any native relation solution that does not reproduce that target.

## 2. Existing support face and start offset

**All five current U tools are parallel to the original Web.** Their native polygon normal is the transformed common depth vector `d`; the planar start is `z=-ToolOverrun`. No U tool in this code has a sketch plane parallel to a vertical Wall. Wall faces provide boundary datums, not replacement sketch supports. A parallel Return face could theoretically support the sketch but adds an unnecessary large offset; prefer the original outer Web at `z=0`.

| Operation | Existing start/end depth interval | Existing face choice | Native representation proposal |
|---|---|---|---|
| `LOSER_STEPPED_WEB_WALL_RETURN` | `[-E, l.MaxDepth+E]` | Long Frame outer Web `z=0` | Face-attached 2D Sketch, start offset `-E`, unchanged Blind length `l.MaxDepth+2E`. |
| `COVER_WEB_WALL` | `[-E, w.MaxDepth+E]` | Short Frame outer Web `z=0` | Same representation with short Frame depth. |
| `NORMAL_BEND_CAP` | `[-E, w.t+w.R]` | Short outer Web | Same support/start offset; Blind length `w.t+w.R+E`. |
| `FAR_BEND_CLEARANCE` | `[-E, max(l.t+l.R,w.t+w.R)+g]` | Long outer Web | Same support/start offset; retain the exact cap correction depth. |
| `NEAR_BEND_CLEARANCE` | Same cap interval | Long outer Web | Same; omit when the reviewed temporary subtraction removes no material. |

Here `E=CutterEnvelopeOverrun`, `g=AssemblyFitGap`. These are formed-profile cuts, not automatically single-sheet `t+SheetCutOverrun` cuts. Preserve the existing operation's NormalCut mapping and cap correction geometry until a different representation is independently qualified.

Project the reviewed polygon along its normal onto the selected Web plane for 2D entity creation. Set the Cut's start offset so its physical start remains the original reviewed plane; retain the reviewed Blind length. Read back and verify `FromType`, `FromOffsetDistance`, `FromOffsetReverse`, end condition, depth, cut direction, NormalCut and FlipSideToCut. Start-offset direction and extrusion reverse are independent signs.

The signed distance must be computed from actual native face geometry and the plan, not guessed from image orientation or feature name. Use a copied-Part offset probe to calibrate `FeatureCut4` start flip against the actual Sketch normal, then validate native result versus the exact temporary tool. `IExtrudeFeatureData2.FromType` uses `swExtrudeFromType_e`; the `FeatureCut4` T0 argument uses `swStartConditions_e`. Do not interchange enum names merely because current numeric values happen to match.

## 3. Direct datum constraints and master seam

Each Sketch is attached to a real native Web face. Use direct relations to original native geometry: `COINCIDENT` for points on proven Frame edges/vertices, `COLLINEAR` for construction guides on coplanar native tangent edges, and `PARALLEL` / `PERPENDICULAR` for directions. No Convert Entities, blanket FIX, per-point coordinate dimensions or snapshot-only construction geometry.

In the common Web plane, Web/Bend tangent edges give usable coplanar guides:

- Short Frame near tangent: `s=sn=w.t+w.R`.
- Short Frame far tangent: `s=sf=w.W-w.t-w.R`.
- Long Frame far tangent: `q=qf=l.W-l.t-l.R`.

The far seam point is the intersection of the latter two actual native tangent guides. Set the Sketch point directly coincident to both perpendicular guides; do not dimension its X/Y.

The near point has **two distinct modes**, copied from the mathematical rule, not from old feature names:

1. **Unclamped** `qn=qf-(sf-sn)`: the current rule has exactly equal coordinate increments. A construction right triangle with legs parallel to the two Frame axes and an `EQUAL` relation can define that 45-degree seam after anchoring the far point and constraining the near point to `s=sn`.
2. **Clamped** `qn=l.t`: use the actual long inner near-Wall plane as the second datum if the native point/Face coincidence relation qualifies in this context. If a Face relation cannot represent the required Web-plane intersection, use one meaningful radius/setback dimension from the long near tangent, linked to the source sheet-metal radius. Do not substitute equal legs. For equal Web70, t2.5, R0.1 mm, the current clamp gives near `(2.6,2.5)` and far `(67.4,67.4)` mm; that seam is **not exactly45°**.

A native Face relation may operate on its underlying plane rather than its finite trimmed patch. This behavior must be measured for the intended constraints. It must not be claimed as an actual point lying on a trimmed Face unless the corresponding native closest-point check proves that fact.

The losing seam is `PARALLEL` to the master seam at a **normal** distance `g=0.10 mm`; this directly expresses the current `gapShift=g*sqrt(1+ds²)` formula. One manufacturing gap dimension is appropriate. Reuse its magnitude for independent Wall and relief offsets through equal construction-gap segments or a deliberately owned parameter linkage; do not independently dimension every polygon point.

## 4. Per-operation relation recipe

| Native Cut profile | Critical associative geometry | Additional minimal control |
|---|---|---|
| Short `COVER_WEB_WALL` | Contains the master seam and near/far tangent guides; other orthogonal segments parallel to native longitudinal edges and coincident to source Wall/return datum intersections. | Near clamp mode, if required; one reusable fit-gap seed can live in its construction geometry. |
| Long `LOSER_STEPPED_WEB_WALL_RETURN` | Losing seam parallel to the master, normal gap g; horizontal/vertical branches reference corresponding actual Wall/tangent guides. Far Wall boundary `s=w.W-w.t-g` is offset from the short inner far-Wall datum. | Shared fit gap; exact signed-side readback to exclude the other valid parallel-offset branch. |
| Long `FAR_BEND_CLEARANCE` | `s=sf-gapShift` and `q=qf-g` are linked to existing master tangent/seam geometry and the shared g construction. | Reuse source seam slope; do not replace gapShift by g or force45°. |
| Long `NEAR_BEND_CLEARANCE` | `s=-g` from the short outer near-Wall datum and `q=l.t+g` from the long inner near-Wall datum. | Shared gap only; skip empty subtraction. |
| Short `NORMAL_BEND_CAP` | `s=w.W-w.t`, `q=qf` and `q=l.W` from actual inner-Wall, tangent and outer-Wall datum planes. | Existing extrusion cap depth; no new arbitrary 2D positions. |

For features whose far polygon edges deliberately extend outside the stock, retain robust tool overrun. Define a closure guide parallel to an actual native axial end/outer support, with one meaningful shared **ToolClosureMargin** offset. The current `m=max(10 mm,5*max(t+R))` can seed that parameter. Reuse its magnitude across closure legs by geometric equality. Do not snap every far closure to the stock boundary: coincident/tangent tool boundaries can reintroduce Boolean547, and are not necessary to express the cut intent.

ToolClosureMargin is not the assembly gap. It may remain a bounded cutter-clearance parameter while the critical trim lines follow the actual Frame geometry. Do not add one coordinate dimension per far point.

## 5. Cross-Frame context and dependency order

The present standalone Part writer cannot create genuine relations to the other selected component. Reuse the separately probed assembly edit context rather than adding a command:

1. Enter `AssemblyDoc.EditPart2` for the existing target component, confirm the actual edit target and configuration.
2. Create and close the 2D Sketch through the **active assembly model's SketchManager**. The Part manager did not open a Sketch in the earlier native in-context probe. Resolve all entities through the correct component context (`GetCorrespondingEntity`) and prove world/native/Sketch coordinate transforms.
3. Relate the actual Sketch entities to original source Frame datums and the master seam. Do not run `ActivateDoc3(part)` while this edit context is active.
4. Exit to EditAssembly, verify the new owned Feature is in the correct Part's FeatureManager, then validate its body and parents.

A useful first candidate is to create the short cover profile/master seam first and let the long profile and relief sketches follow it. **This is not automatically an acyclic model:** the master may itself need long Frame geometry. Only original pre-AFJ source feature datums may feed the master; never reference a newly created Cut face that depends on that master. Inspect source-owner IDs, Feature parents and external-reference statuses after each creation. A cyclic, dangling, out-of-context or unresolvable reference is a refusal, not a reason to replace the relations with coordinates.

If SOLIDWORKS cannot qualify that feature dependency graph, use one explicitly owned face-attached assembly master Sketch referencing original source datums, with the Part Cut sketches following it. This adds no reference plane but expands the transaction to include the assembly Sketch. It requires native dependency/cycle testing and exact rollback of the owned assembly feature as well as the Part features. It is an alternative to investigate, not a claim that an assembly master is already implemented or proven.

## 6. Native qualification gates

Start with the exact current fr03/fr04 U-taller pair on copies. Test each representation change separately:

1. Existing Web support + signed start offset, first using the unchanged reviewed polygon. Prove exact native B-rep equivalence and zero new planes before geometric-relation work.
2. Direct own-Frame datum relations; prove full status3, no dangling/overdefining relations, unchanged seed solution and zero coordinate/FIX/Convert fallback.
3. Master seam near/far modes and losing parallel gap; prove actual cross-Sketch entity identity and feature ownership, not merely matching coordinates.
4. Cross-Frame references in assembly context. Move one source input on copies, measure actual relation-driven follow, then restore the original pose. This must not secretly rerun the solver to simulate associativity.
5. All nonempty U tools sequentially; recheck every prior Sketch after each later Cut, full KEEP and final gap/interference/body identity. Preserve source transform checks and independent configurations/shared-Part refusals.
6. Native model regeneration, Part close/reopen and subsequent edit on copies; record exact body, reference and feature-error results. Installed Add-in operation is a separate qualification from harness PASS.
7. Inject failure on the second Part and restore exact original Part trees/bodies; if an assembly master was added, delete only its registered owned ID and restore the assembly tree too. Always exit the edit context in `finally`; no Save.

Only then route the U writer to the geometric representation. Failed or incomplete references should yield an explicit unsupported apply result; successful immediate B-rep alone does not establish modeling intent or reopen safety.

## Official API scope

Installed Interop metadata confirms `FromOffsetDistance`, `FromOffsetReverse`, `FromType`, Feature parent/external-reference APIs. [FromType](https://help.solidworks.com/2024/english/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.IExtrudeFeatureData2~FromType.html), [FromOffsetDistance](https://help.solidworks.com/2026/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IExtrudeFeatureData2~FromOffsetDistance.html), and [FromOffsetReverse](https://help.solidworks.com/2026/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IExtrudeFeatureData2~FromOffsetReverse.html) document the start-offset properties. This establishes API capability; it does not qualify signs, constraints or the proposed dependency graph for the actual U cuts.
