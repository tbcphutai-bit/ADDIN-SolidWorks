# Private evidence input workflow

All files below are private company-side files. Examples give field contracts, not usable approvals. Never publish CAD coordinates, manifests, review artifacts, signatures or authority keys with the PR.

1. Run existing **Check Chấn**, select a native bend and one of its 18 source-bound tools, then open **Hồ sơ dao**. The initial status is MATERIAL_ROLE_PENDING. The source view is drawing WCS, not a mounted assembly.
2. Assign every Holder entity explicitly to a contour (`Solid`, `Hole`, `Island` with IDs/parent IDs) or exclusion (`Decorative`, `Centerline`, `Reference`, `Annotation`). The software must not choose the largest contour or infer roles from layers. **Áp dụng vai trò** checks complete unique assignment. It is still unsigned.
3. **Xuất chưa ký** saves a private `ToolEvidenceReviewProposal`. Populate its `Revision` and technical fields using measured/reviewed sources. Known source hashes, block/INSERT and native identifiers are prefilled; coordinates, transforms, spans and errors are not. A new external approval must bind the resulting canonical payload. The editor cannot issue signatures or attest physical inspection.
4. Independent review uses the existing purposes `HolderMaterialRule`, `ToolHolderMapping`, `ToolPairSeating`, `PunchAxialSetup`, `HolderAxialSetup`, or explicit `CadReferenceSimulation` with SimulationOnly. Use existing trusted authorities configured outside the proposal; importing a document containing a public key cannot create engineering authority. Datum approval artifacts remain in the existing datum evidence format.
5. A placement configuration can use `ReviewWorkspacePath` for one reviewed proposal or `{ "Version": 1, "Proposals": [...] }`. Do not also configure `HolderEvidencePath`/`PairSeatingEvidencePath`. `MasterDatumEvidencePath` and `ReviewAuthoritiesPath` retain their existing separate meanings. Source revisions, expiry and invalid signatures remain fail-closed. A proposal with no signature is intentionally rejected for collision material use.

## Final-static input

The existing private `process-provider.json` configuration accepts:

```json
{
  "Version": 1,
  "TrustedAuthoritiesPath": "<already trusted authority file>",
  "AuthoritySha256": "<pinned SHA256>",
  "Sources": [],
  "FinalStaticSources": [
    {
      "NativePath": "<exact native Part file>",
      "Configuration": "<exact configuration>",
      "PackagePath": "<private final-static.json>",
      "PackageSha256": "<pinned SHA256>",
      "ExpectedRevision": "<reviewed revision>"
    }
  ]
}
```

`final-static.json` is `{ "Version": 1, "Revision": "...", "Records": [...] }`, where each record uses the existing `ToolAxialSetupRecord`. The proposal's native-bound `Axial` entries are **blank authoring templates**, not already valid records. Required fields include:

| Field | Meaning |
|---|---|
| ToolId / FeatureId / Pair | Actual selected company tool, native feature and `WorkpiecePunch` or `WorkpieceHolder` |
| ModelSnapshotId / ModelUpdateStamp / Configuration | Current native capture; do not reuse across rebuild/config changes |
| EnvelopeGeometrySha256 / FrameId | Exact **final native** envelope and frame, not a process-state envelope |
| MasterSourceSha256 / ProfileGeometrySha256 / SeatingPayloadSha256 | Exact source, Punch geometry or reviewed Holder material, and approved seating |
| AxialSectionContract | Independently reviewed `UniformProfileOverListedSegments` |
| ToolAxialOriginMm / ToolAxisSign | Measured origin and +1/-1 axis in the native work frame |
| Segments | Actual useful local Start/End in mm; no infinite/default/Excel reference span |
| AxialUncertaintyMm | Reviewed nonnegative uncertainty, within existing supported policy |
| ContactPointMm / ApproachDirection / MirrorApproved | Explicit placement and independently approved mirror conditions |
| Review | Existing external purpose-specific proof and artifact; nullable input is not zero/approved |

The importer compiles full final-envelope coverage and verifies these records using existing code. Missing records keep all 18 candidate rows; an unsupported sheet envelope remains UNKNOWN. Static results never become a process graph.

On unsigned export, the pane prepares the exact final native envelope on a managed worker and fills its digest. It does not export the fast-precheck placeholder as a proven envelope. Edited/stale/reselected input withdraws an in-flight export. No datum, placement, length or approval is generated during this step.

An existing process-only PartLocal approval is preserved when no static setup is supplied. It cannot authorize a broader program containing new static setup records; cross-instance numeric sharing remains conservative. Native geometry still groups/captures once for truly equivalent sources. Do not claim additional batch acceleration without the corresponding scope approval.

## Process / simulation input

**Xuất quy trình trống** copies current native FeatureId/PersistentId/frame/revision binding into `ProcessPackageRecord` and leaves geometry, states, transitions, graph completeness, motion and approvals absent. Add the reviewed whole-sheet geometry, all five phases, before/after states, constraints and source artifacts through the existing importer. Map the resulting package in the existing configuration's `Sources`, with exact SHA/revision and native file/configuration.

For software-only use of the actual CAD pairs, use `CadReferenceSimulationRecord` in the existing per-transition tool input. It requires a separately approved SimulationOnly pose, finite span and errors. Every Holder material band is retained. No replacement tool geometry is permitted in fixed-company-pair mode. An unsigned simulation proposal cannot run numeric collision just because it contains coordinates.

## What still requires company evidence

The available source does not contain approved solid/hole/reference assignments, measured tip/mount datums, physical seating, real working axial segments, or a reviewed intermediate whole-sheet process graph for the 33 Parts. Those cannot be supplied by this application. The 4200/6000 mm Excel references are not certificates for any ToolId. KIK's open Punch remains invalid as a closed material region.

No need to recreate or re-upload the 18 CAD pairs or the 33 fixtures. The missing work is explicit engineering review/measurement of the existing sources and process, followed by independent verification. Until then the real-company numeric/sequence verdict is UNKNOWN / CHUA_XAC_MINH.
