# R73 rule and evidence boundaries

## Same-sheet straight end completion

The measured planner now evaluates uncovered transverse intervals of a partially contacted sheet. A completion is permitted only when the gap reaches an original own free edge, the immediately adjacent same-sheet contact supplies a unique constant physical axial boundary, and actual original B-rep has positive material beyond that boundary and protected material on the body side. Interior gaps, bend-connected ends, coplanar miter continuations, secondary-obstruction cells and sloping contacts do not receive this new authority.

`AddSameSheetEndCompletions` derives the cell without Part names, fixture dimensions, bounding-box cut geometry or copying existing Cut commands. The completion reuses the actual own/mate source identity, selected family and shared height datum. `SameStraightEndCompletion` and `VerifyStraightEndCompletionSource` require fresh selection to recover that same physical source. `RecordStraightEndCompletion` records positive original inside/outside material. `VerifyStraightEndCompleteness` independently checks residual tail and protected inside material; equality between Preview and native alone is insufficient.

`SelectedMeasuredCells`, `AddMeasuredSlice`, `CloneMeasuredMiterCell` and measured metadata checks carry this authority through the existing Cut Plan, temporary Boolean and native per-fold Face2 writer. Existing selection/command/session entry points are preserved. Legacy TerminalOverhangTrim and MeasuredTerminalInherited remain false for this new cell.

**GEOMETRY_VERIFIED:** 2354 physical fr04 strip2 completion occupies 42–60 mm and inherits the adjacent 0–42 mm constant end. Original outside material is positive, Preview/native outside residual is zero and inside material loss is zero. Fixture dimensions occur in the diagnostic assertions, not the production rule. The fixture requires exactly one completion; zero witnesses cannot pass.

**API_VERIFIED:** the extra native Cut uses Blind 2.60 mm on actual 2.50 mm sheet, NormalCut true, existing Face2 support, full constraint status3, six geometric relations, zero driving dimensions and zero reference planes. Native/Preview per-Cut symmetric difference is zero. Other contact seam operations are preserved.

**INFERRED scope:** the same geometric free-edge condition may occur in other profiles. The six tested pair applications do not prove all folded profiles or every unmatched band. Cases lacking this source authority retain the existing restricted policy; this is not a global flattening of every stepped end.

## Reported direction defect in test2

The current log loaded the R72 DLL, MVID `11993f6f-4540-4df2-93e5-18398398d64e`; an old deployed DLL is not the evidence-backed explanation. Test1 selected 2354 fr03→fr04 and test2 selected 2222 fr6→fr5, reversed from the R72 harness order.

**GEOMETRY_VERIFIED:** original/swapped A/B solves have zero symmetric difference on both mapped physical targets, preserving the short physical member as cover. The 2222 mechanical result remains 22 native Cuts. The 17 mapped finite contact authorities and removed material volumes match the user's log. No global direction, skin or winner flip was applied.

**UNVERIFIED design intent:** the user clarified that the selected end is correct while the cover face/stepped trim is reversed. The current solver covers fr5 with shorter fr6. Existing nine AFJ-generated Cuts on source fr6 are preserved, but they are not an independent authored design oracle. Confirm the physical cover/step intention before changing the rule. Neither zero interference, a 0.10 mm minimum gap, A/B invariance nor native=Preview establishes that intended step.

## Manufacturing invariants

SheetCutOverrun = 0.10 mm controls Blind depth beyond actual API sheet thickness. AssemblyFitGap = 0.10 mm controls fit between members. They remain distinct. All native test Cuts use NormalCut and the existing per-sheet writer. No through-all cutter, new RefPlane, edge flange, Convert or FIX fallback is added.
