# R74 — Joint KEEP/CUT orientation and contacting terminal flaps

Final isolated SOLIDWORKS API qualification contains **6 pair applications, 72 newly generated Native Cuts and 94 created sketches**. Counts come from completed actual Native Apply reports and the final sketch ledger, not repeated primary-proof trace lines. They are separate from the original43 handoff Cuts,42 historical before/after observations and earlier generated test features.

R74 derives the retained direction from actual finite coincident-mate Face2 contacts outside the selected corner and positive original-material KEEP masks. The new authority is limited to orthogonal member axes with decisive supported contacts on both members. It changes outgoing CUT rays and the finite per-sheet removal region; it does not blindly enable the outside complement of a closed Native Cut sketch. Short-cover preference and the selected member end are separate decisions.

The terminal rule requires one actual formed bend, a real free longitudinal edge on both skins and **positive remaining paired-flat overlap after the original primary cuts**. Nonintersecting terminal bands receive no new bevel. Qualification is re-derived independently; primary-proof and final-plan observations must not be double-counted.

Final Cut parameters are API_VERIFIED: NormalCut, Blind actual t +0.10mm, original Face2 support, full3 constraint status and no new reference planes/FIX/Convert relations. Geometry and positive original-material preservation are GEOMETRY_VERIFIED. Extrapolation to other folded profiles or assemblies without decisive orientation evidence remains INFERRED/UNVERIFIED. AssemblyFitGap0.10mm is separate from SheetCutOverrun0.10mm.

Authored feature identity/order is preserved. In the disposable2354 fr03 native result, SOLIDWORKS retired one independently classified system-generated flat derivative ProfileFeature (ID850); raw generated metadata therefore is not exact. API_EVIDENCE.json reports this separately from authoritative feature preservation. Original disk files and test-copy disk files remain unchanged (NoSave).

**Failed native2222_05 is preserved separately:** Preview met the gap/overlap/remote-KEEP checks, but Native Apply failed at the next Sketch datum after one Cut. Rollback was UNVERIFIED on disposable NoSave copies. It contributes zero qualifying final Cuts. A final success does not retroactively turn that failure into PASS.

The exact qualified GhostPreview source was copied to the existing main class for the user's manual rebuild. No main Add-in Build/Deploy or merge is performed by this generator. No original CAD, binary, screenshots, local paths or raw debug stacks are published. Installed Add-in/PMP interaction, user visibility, responsiveness and broad rollback behavior are not qualified solely by these API runs.

See [test matrix](TEST_MATRIX.csv), [Native Cuts](NATIVE_CUT_RESULTS.csv), [original-material orientation](ORIENTATION_EVIDENCE.json), [terminal evidence](TERMINAL_MITER_EVIDENCE.csv), [failure history](FAILED_RUN_HISTORY.json), [rules and limits](RULES_REVIEW.md), and [source integration](SOURCE_INTEGRATION.json).
