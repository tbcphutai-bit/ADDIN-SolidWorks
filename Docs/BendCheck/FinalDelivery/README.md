# FINAL CHECK CHẤN — Codex research and implementation packet

**Branch built on PR #35; planning/reference commit only. Not production code, not independently native tested.**

## Read in this order

1. [CODEX_FINAL_TASK.md](CODEX_FINAL_TASK.md) — binding single integrated implementation assignment, user goals, code source anchors, required final architecture proposal.
2. [ARCHITECTURE_AND_EVIDENCE.md](ARCHITECTURE_AND_EVIDENCE.md) — current real C# API map, evidence barriers, 18 Holder role workflow, setup/axial/process, alternatives.
3. [VALIDATION_AND_REPORT.md](VALIDATION_AND_REPORT.md) — G0–G7 acceptance, 33 existing native cases / 72 bends, 18 real CAD pairs, numerics and shutdown A/B, report schema.
4. [Reference/HolderMaterialReviewDraft.cs](Reference/HolderMaterialReviewDraft.cs) — unsigned managed Holder inventory and explicit reviewer-assigned contour proposal, using existing HolderObstacleSet contracts.
5. [Reference/FinalEvidenceGate.cs](Reference/FinalEvidenceGate.cs) — pure output gate separating numeric evidence, simulation, missing inputs and native lifecycle.
6. [Reference/FinalEvidenceGateTests.cs](Reference/FinalEvidenceGateTests.cs) — assertions to integrate into existing test harness before reporting PASS.

The *.cs in Reference/ are **deliberately not included in ADDIN.csproj / ToolProfiles.Tests.csproj**. They are review-ready candidate code, not alleged compiled/verified functionality. Codex must adapt and wire them to the real source, run Debug/Release builds and native tests and report actual results. This branch intentionally changes no UI, production engine, COM or Add-in registration.

## Message to send to Codex

Read all six paths in this folder, especially CODEX_FINAL_TASK.md. Inspect existing code against each requirement and propose the final architecture with implementation. Do not merely summarize the docs. Implement on your own new branch based on PR #35 (or coordinate a new branch from this planning PR if needed); open one Draft PR with actual code and test reports. Keep original Check Min and company CAD/Excel unchanged. Do not merge main. When missing company setup/process evidence blocks production collision, build an unsigned reviewer workflow and report UNKNOWN, never fabricate physical verification.

## Key reminders

- User has the 18 real tools; no need to ask them to re-upload/recreate the DXF or for 33 different fixtures.
- Drawing pairing is already done by PR #34; user requested exact CAD as drawn, not alternative clamps.
- PR #35 shows native 33 Parts, 72 bends, Check Min 3 real NG, but **zero numeric queries for real physical setups**.
- Excel 4200/6000 mm are references, not automatic axial tool lengths.
- Clean SOLIDWORKS shutdown remains an acceptance blocker and is separate from functional test PASS.
- Simulation geometry can demonstrate software behavior but never silently become a production recommendation.
