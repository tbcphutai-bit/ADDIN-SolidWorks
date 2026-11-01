# R71: measured sheet contacts and native NormalCut

The 4458 sample adds inward and extra returns and a Wall slope of approximately 2.05 degrees. The Frame axes in this qualified rule remain orthogonal. Profile folds, member-axis angle, material thickness, and assembly transforms are separate inputs; none is inferred from the screenshot or a Feature name.

Only `Commands/AutoFrameJoint/AutoFrameJointGhostPreview.cs` changes. It contains the existing Preview/solver/native-writer helper classes. SelectionPage, CommandEntry, SessionManager, GeometryMath and ReadOnlyProbe keep their existing source bytes. No extra command or Preview system is added.

| Existing class/helper | Main changes and purpose |
| --- | --- |
| `AfjMeasuredContactRules` | Dispatch multi-strip or sloped folded profiles into a measured finite-contact rule; select the shorter covering member by actual geometry. |
| `AfjPerFoldSheetPlanner` | Form separate ordered sheet operations for A and B, retain noncontact material, and use actual paired skins and connected bends. `MeasuredMiter` derives the joint guide from actual virtual zero-skin profile corners, including unequal widths. |
| `MeasuredTerminalEntry`, `BindMeasuredTerminalEntry` | Read the actual cylindrical Ri/Ro pair and finite Face2 skins, select the terminal outer tangency entry face and correct inward direction, keeping the canonical profile graph intact. |
| `ExtendLaterNormalContourToOppositeSkin`, `ExtendPiecewiseGuideBoundary`, `MeasuredBound` | Intersect the actual selected entry plane with the actual adjacent opposite skin. Re-read the geometry independently when validating the contour. |
| `NormalOuterArcWindows`, `ExactRadialRuledRemoval` | Union eligible actual inner/outer-radius angular windows inside the finite Blind depth and contour. Analytic events split guide/eligibility changes; exact ruled radial sheets model the native NormalCut bend result. There is no all-depth Through All substitution. |
| Adjacent-flat carryover and `AfjTemporaryBoolean` | Model the small adjacent flat band actually reached by an oblique NormalCut at a bend tangent, while retaining the other adjacent material masks. Derive it from actual 0/t skins, cylinder tangencies, finite depth, and eligible guide minima. Validate the derived removal independently. |
| `AfjNativeUTransaction.R71PrepareMeasuredStepSketch` | Select actual current Face2 supports and stable source geometry for each sequential native operation. Native Part activation through `ActivateDoc3` is not introduced. |
| `AfjUParametricSketchWriter.TryWriteMeasuredMiterGuide`, `R59WriteWallSheetSlice` | Create native 2D sketches on existing Part faces and bind geometric references to original profile/owned guide entities. Preserve exact support identities and read constraints after closing. |
| `R71OwnAdjacentProfileHeightEndpoint`, `R59FindMeasuredHeightDatum` | Select the original structural profile corner and solve the entry/height-plane intersection. A 2 mm sheet at 2.05 degrees can require a derived projected construction length of about 2.001280838 mm; it is not an invented sheet thickness. |
| `R59MatingFiniteEdgeDatum`, `R59StableBaseProfileMatingDatum` | For perpendicular material normals and an axial mating plane, use a real original 0/t profile point's axial ordinate, with independently retained finite Face2 authority. This avoids depending on stock-end edges removed by earlier Cuts. Other coupled directions retain verified Pierce constructions. |

## Plane intersection and manufacturing parameters

For own strip origin A, transverse unit U, material normal N, actual entry offset d, and an adjacent actual plane `n dot p = s`, the contour bound is:

`v = (s - dot(A + d*N, n)) / dot(U, n)`.

Using the canonical zero skin after selecting the opposite entry face was incorrect for an oblique adjacent Wall. The final source uses the actual entry origin both when constructing and independently checking this bound.

For a persistent source corner e, own entry plane `(nO,pO)`, and actual height plane `(nH,pH)`, project e onto the own plane and solve along U:

`eP = e - nO*dot(e-pO,nO)`

`delta = dot(pH-eP,nH) / dot(U,nH)`

`intersection = eP + U*delta`.

H/V point and geometric line relations bind the original source point; a nonzero projected offset uses a derived construction parameter. These parameters are recomputed for a new Cut Plan. An automatic associative angle equation after arbitrary subsequent user edits is not qualified.

`SheetCutOverrun = 0.10 mm` controls native Blind depth `t + SheetCutOverrun`. `AssemblyFitGap = 0.10 mm` controls member fit. They remain separate even where the numerical values coincide. A projected drawing parameter may include thickness/projection and must not be interpreted as the physical fit gap itself.

## KEEP/CUT evidence

The rule processes finite contact cells and linked terminal extensions separately, keeps noncontact branches and the protected opposite stock half, and never extends a diagonal cut to an unrelated upper Wall merely because its bounding box overlaps. Each operation has its own cutter and its own actual native Blind/NormalCut readback. Temporary Boolean work uses copied bodies; native mutation starts only when applying the validated ticket.

Each generated native Cut must match its sequential Preview target by symmetric difference <= 1e-12 m3. The final run measured zero for all 44 generated Cuts. The final pair/target checks are repeated after all four corners, alongside all 52 created sketches. No Boolean fragment limit, geometry tolerance, source identity, or constraint gate was loosened for these native results.

The completed historical Parts differ from the new prepared-stock result; see SUMMARY.json for both directional differences. Dependent hole/pattern suppression and the new finite-contact rules make that comparison separate from Cut/Preview equivalence. Exact reproduction of the historical finished Parts and complete coverage of every exposed edge remain unverified.

## Evidence levels

- `API_VERIFIED`: actual native Cut type/flags/depth, original source profile, finite plane/cylinder observations, Face2 sketch support, constraint/relation readbacks, and history identities captured by the API harness.
- `GEOMETRY_VERIFIED`: independent B-rep differences, removed volumes, finite material/KEEP checks, final pair gaps and interference, and sequential/cumulative native-target parity for the four tested corners.
- `INFERRED`: extending this qualified construction to untested profiles or manufacturing cases. Such cases are not granted a PASS by this report.

The original 43 authored Cut inventory / 42 authored before-after observations remain unchanged. The 93 authored 4458 Cuts in R70 and the 44 newly generated validation Cuts in R71 are separate populations.
