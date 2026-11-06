# R73 — Straight end completion; cover-step direction remains unresolved

R73 fixes the positive free-edge tail in user test 1, **2354 fr03 × fr04**. The shorter fr04 has a 60 mm sheet band and only 42 mm of corresponding mate contact. The previous measured planner kept the remaining 18 mm through the raw axial stock. R73 adds one **straight end Cut** over that noncontact band using the adjacent constant physical contact end; it adds no bevel there.

API and independent original-material checks on copies establish:

- 2354: **7 new native Cuts / 9 constrained sketches**. The original outside tail is **13,248.69662374 mm³**; its intersection with final Preview and native Body is zero. Protected inside material removed: zero.
- 4458: **four corners applied cumulatively in the same copied Assembly**, 10/13/9/12 Cuts, **44 Cuts / 52 sketches**. This is a regression test, not a claim that every authored reference Cut is reproduced exactly.
- 2222: **22 Cuts / 33 sketches** mechanically verified. **The user-reported reversed cover face/step is NOT FIXED or design-approved.** Current fr6-cover policy is unchanged.

Total: **73 generated test Cuts / 94 created sketches**, six pair applications. These counts are newly generated test features, not the 43 original handoff Cut features or 42 historical before/after observations.

Every generated Cut is Normal Cut + Blind, with per-sheet t + 0.10 mm; native targets match Preview with zero measured symmetric difference. Sketch support is existing Face2, constraint status 3, with zero new planes, dangling/overdefining, FIX or Convert relations. AssemblyFitGap remains a separate 0.10 mm parameter.

Reversing A/B yields identical mapped physical target Bodies in all six pairs. That excludes selection order as the observed direction cause; it does **not** establish the intended cover/step direction. See [DIRECTION_DIAGNOSIS.json](DIRECTION_DIAGNOSIS.json).

Only `Commands/AutoFrameJoint/AutoFrameJointGhostPreview.cs` was copied to the user's main source for manual rebuild. Other five AFJ source files, project, DLL and PDB were preserved. No main Add-in build/deploy, original CAD API open, CAD save, Pack and Go or merge was performed. No CAD, machine paths, screenshots, binaries or raw debug logs are published.

The tests used an isolated SOLIDWORKS API harness with the frozen main-local six-source baseline. Installed Add-in/PMP callbacks, interactive visibility, user-perceived performance and the user's intended 2222 cover step remain unverified. API success must not be reported as complete design approval.

Files: [rules](RULES_REVIEW.md), [test matrix](TEST_MATRIX.csv), [native Cuts](NATIVE_CUT_RESULTS.csv), [sketch constraints](SKETCH_CONSTRAINT_RESULTS.csv), [API material evidence](API_EVIDENCE.json), [source integration](SOURCE_INTEGRATION.json), [manual build and limits](MANUAL_BUILD_AND_LIMITS.md).
