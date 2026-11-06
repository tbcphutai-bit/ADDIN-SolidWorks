# Manual rebuild and remaining limits

The R73 straight-tail fix is in the existing main `AutoFrameJointGhostPreview.cs` class. Working CRLF and normalized Git LF source hashes are in SOURCE_INTEGRATION.json. The currently installed DLL remains the user's R72 module; the agent did not build, copy or deploy a main binary. The user must rebuild and load the Add-in through their normal workflow.

The new runtime evidence is `[AFJ-R73] STRAIGHT_END_COMPLETION_SELECTED`, `STRAIGHT_END_COMPLETENESS_VERIFIED` and `STRAIGHT_END_COMPLETENESS_COUNT`. An unchanged existing R72 entry marker alone does not identify the source hash. A profile with no qualifying completion may emit expected=0; the known2354 fixture explicitly requires one positive18mm completion.

For 2354 fr03 × fr04, expect one extra straight Cut on fr04's noncontact free-edge band, with no extra bevel. API-copy qualification is complete. Installed Add-in interaction and user visual acceptance still require manual checking after rebuild.

2222 cover/step direction remains unresolved. This revision does not reverse its member axis or global cover policy. Do not interpret its mechanical API PASS as a fix for the reported direction defect.

The isolated session closed its copied documents without saving. ExitApp initially exceeded its5s wait; a later process check confirmed the owned process exited without Kill, and the Add-in startup setting was restored. No user session was closed by the test tool.

No installed Add-in/PMP callback qualification, interactive responsiveness benchmark, standalone Part reopen/save qualification, broad rollback qualification or exhaustive multi-profile design approval is claimed. The4458 tests use the same explicitly reviewed stock-preparation fixture asR72 and preserve its authored-reference differences as limitations.
