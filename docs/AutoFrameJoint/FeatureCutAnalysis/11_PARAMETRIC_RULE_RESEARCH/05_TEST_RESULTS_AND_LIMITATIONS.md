# Tests and limits

All runs use detached STA executables attached to the existing SOLIDWORKS session. The main Add-in project was not built. Temporary B-rep bodies and independently named copied assemblies/Parts are used. No source original is rolled back, suppressed or saved.

| Fixture | Native final / current shorter-cover policy | Native before-Cut snapshots | Private rollback / public Ghost API |
| --- | --- | --- | --- |
| AFJ-R01 | UNVERIFIED_RULE | NOT_RUN | NOT_RUN |
| AFJ-R02 | UNVERIFIED_RULE | NOT_RUN | NOT_RUN |
| AFJ-R03 | UNVERIFIED_RULE | NOT_RUN | NOT_RUN |
| AFJ-R04 | UNVERIFIED_RULE | NOT_RUN | NOT_RUN |
| AFJ-R05 | UNVERIFIED_RULE | PASS_GEOMETRY | PASS_DISPLAY3_API |
| AFJ-R06 | UNVERIFIED_RULE | PASS_GEOMETRY | PASS_DISPLAY3_API |
| AFJ-R07 | UNVERIFIED_RULE | PASS_GEOMETRY | PASS_DISPLAY3_API |
| AFJ-R08 | UNVERIFIED_RULE | PASS_GEOMETRY | PASS_DISPLAY3_API |
| AFJ-R09 | UNVERIFIED_RULE | PASS_GEOMETRY | PASS_DISPLAY3_API |
| AFJ-R10 | UNVERIFIED_RULE | PASS_GEOMETRY | PASS_DISPLAY3_API |
| AFJ-R11 | UNVERIFIED_RULE | PASS_GEOMETRY | PASS_DISPLAY3_API |
| AFJ-R12 | UNVERIFIED_RULE | PASS_GEOMETRY | PASS_DISPLAY3_API |
| DEBUG_PAIR_1 | UNVERIFIED_RULE | NOT_RUN | NOT_RUN |
| DEBUG_PAIR_2 | NOT_IDENTIFIED | NOT_RUN | NOT_RUN |


## Independent dimensions

12/12 recorded runtime variants passed. Dimensions vary Web width, Wall heights, absent/present Return, Return width, t, R, member lengths and global rotation/translation. Tall Walls verify that topology selects Web instead of longest-line guessing. These are independently constructed analytic formed solids, not scaled native Cut polylines. Check2, mass conservation, required KEEP coverage, no interference and exact 0.1 mm minimum clearance are recorded.

## Limits requiring Code Owner review

- 1618 fr1×fr2 and fr3×fr4 were attempted and refused because inclined or multi-step coverage is not verified. Their native Cut before/after data is available, but there is no generalized production rule yet.
- 2354 fr03×fr04 is identified and attempted. Opposite thickness/skin side and height levels fall outside the verified family; no successful geometry/Preview PASS is claimed. Its historical axis-distance estimate of 2.455 mm is not an assembly fit gap. Exact native body distance was measured separately as 0.
- No second distinct old Debug component pair was found; NOT_IDENTIFIED.
- Current shorter-cover policy may disagree with historical native trims. A previously trimmed native final body can lack the new required KEEP material; those cases are refused rather than silently accepted as cuttable stock.
- Equal lengths and unsupported profile families require further verified rules. Refusal records are not counted as successful cut geometry.
- Placement tests cover end-corner intent only. The current outgoing-end choice uses the opposing profile center; it rejects a centered ambiguous intersection but does not prove a complete off-center T/interior-joint classifier. Do not apply this Draft to T joints or automatic joint discovery. Explicit endpoint/topology classification needs additional evidence before general production use.
- Display3 return status and Hide cleanup are API evidence. Installed PMP clicks, human visual fit and rendered UI appearance remain UNVERIFIED because the main Add-in was neither built nor deployed and computer use was prohibited.
- The measured minimum pair gap is fixed 0.1 mm (1 µm numerical tolerance). Conservative Bend relief can create locally larger clearance; this does not prove every point of every opposing face is uniformly 0.1 mm apart.
- New dimension variants verify geometry, not manufacturing certification or unrestricted extrapolation to arbitrary Frame topology.
- One SOLIDWORKS process crash occurred during the transition between test executables. Its 0xC0000409/ucrtbase event and recovery are recorded in API diagnostics; the exact cause and production stability remain UNVERIFIED.
