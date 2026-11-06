# R56 — qualified folded-profile source and native cut evidence

Frozen solver source SHA256: `09f0e9708a11ce962e2ee70348f6d5cae5a57269f8754545e8a0768637af748d`.

Source delivery: **copied into the existing main Ghost class; actual six-main-source detached compilation passed**. The Add-in DLL/PDB and project outputs have not been rebuilt or deployed by the agent.

The exact source15 harness completed all four corners sequentially on one 2354 Assembly copy: **16 native Cut-Extrude Features, 16 fully constrained Face2 Sketches, zero new reference planes**. All five final Part bodies match their independent targets with zero measured symmetric difference. All four final pair gaps are 0.10 mm and interference is zero. Original and copied CAD disk hashes stayed unchanged; copied documents closed without Save, Rebuild or Pack and Go.

The generalized raw 4332 source15 case is **PASS_NATIVE_API_HARNESS**. The earlier source12 fr3a × fr5 case is reported separately. Source15 currently has 20 qualified generated native Cuts across 5 qualified cases. These generated Cuts are separate from the prior authored training corpus of 43 Cuts and 42 before/after captures.

Zero native-versus-reviewed-target difference proves that native Features reproduce the current code-derived Cut Plan. It does not prove manufacturing equivalence to every authored finished reference. In particular, the tested constant-stock near/inward-fold cuts and local Wall rules involving added end stock have different qualification scopes. Added-end-stock Wall constructions remain outside general manufacturing qualification; no universal protection or cuttability is claimed for unknown shapes or the preserved legacy route.

The delivered class remains `Commands/AutoFrameJoint/AutoFrameJointGhostPreview.cs`. Existing A/B selection, command entry, session manager and protected UI flow are retained. No new Preview command was added. The API harness calls the existing Ghost/solver/native Apply implementation; it is not an installed Add-in UI test.

**Manual Rebuild is still required.** The agent did not build or deploy the Add-in. Actual Preview visibility and UI smoothness remain UNVERIFIED in these qualification runs. Cold Preview and native Apply timings are recorded without claiming they are fast.

- [Implementation](IMPLEMENTATION.md) and [KEEP/CUT rules](RULES_KEEP_CUT.md).
- [Test matrix](TEST_MATRIX.md), [CSV](TEST_MATRIX.csv) and [JSON](TEST_MATRIX.json).
- [Native before/after scalars](BEFORE_AFTER_API.json) and [sanitized native log evidence](NATIVE_LOG_EVIDENCE.txt).
- [Manual Rebuild](MANUAL_REBUILD.md), [limitations](LIMITATIONS.md) and [source manifest](SOURCE_MANIFEST.json).

Only scalar/API evidence is included. No original CAD, B-rep payload, image, placement coordinates, native tokens, customer name or machine path is published. Existing R55 reports remain preserved.
