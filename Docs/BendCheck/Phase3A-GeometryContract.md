# Phase 3A — analytic regions and independent datum evidence

Baseline: PR #6, `f7e9abc110ca367f97cdc7de6452c5de9d0ae51c`.
This layer prepares data for a later Check Max 2D implementation. It does not
test workpiece/tool collisions, simulate motion, choose a tool or prove a bend
sequence feasible. Manufacturing status remains `CHUA_XAC_MINH`.

## Region validation is separate from source closure

`ToolRegionValidator.Validate(...)` is pure managed C#, independent of
`ToolProfileTopology.Closed()`, the DXF reader, SOLIDWORKS and WinForms. It
accepts the existing analytic `ToolCurve` LINE/ARC representation. No polyline,
arc, endpoint, source flag or source origin is changed. No additional edge,
tessellation, offset, welding, union or fill operation is performed.

`ToolProfileValidationResult.PunchClosed` retains Phase 2's observation of source
flags/endpoints. A bowtie can have `PunchClosed=true` and an invalid region.
Conversely, a joint gap within the region policy can pass connectivity while
the older, tighter endpoint observer reports false. Both observations remain
available. An explicit source-open flag always prevents a valid region, even
when its endpoints coincide.

Selected punch parts and holder/manufacturer reference parts have their own
`ToolGeometryPart.Region`. The entire selected punch has an aggregate
`ToolProfileValidationResult.Region`; incomplete source selections cannot yield
a valid aggregate. Annotations/unclassified entities are not obstacle selections
and retain `NotEvaluated`. A valid reference region does not verify its physical
role or mounting fit.

| `ToolRegionStatus` | Meaning |
| --- | --- |
| `NotEvaluated` | No boundary or incomplete punch selection |
| `ValidSingleLoop` | One simple, connected analytic loop with resolved positive area under the policy below |
| `OpenBoundary` | Open source flag and/or an unconnected boundary |
| `InvalidBoundary` | Degeneracy, ambiguous joints, branching, self-crossing/contact, overlap or unresolved/zero area |
| `Unsupported` | Unsupported geometry/numeric budget or multiple loops without an approved fill rule |

The result contains issue codes, loop count, tolerance, maximum observed joint
gap and (for a single traversable loop) diagnostic area/area threshold. A
diagnostic area alone is never a valid-region certificate; consumers must read
`Status`/`IsValidRegion` and use a current library snapshot.

### Tolerance policy: `AnalyticRegionMmV1`

- Default endpoint/contact/radial tolerance: **0.00001 mm** (`1e-5`).
- The constructor permits explicit tolerances from `1e-7` to `0.01 mm` for
  independent tests/callers. The production library uses the fixed default.
- Area must exceed **max(1e-8 mm², perimeter in mm × distance tolerance in mm)**.
  This rejects thin/unresolved regions instead of trusting a nearly zero signed
  integral. It is not a machining allowance.
- Internal angular roundoff: `1e-12 rad`; positive co-linear/co-circular overlap
  longer than `1e-10 mm` is rejected, including adjacent retracing. These smaller
  arithmetic thresholds do not enlarge the manufacturing/contact tolerance.
- Supported coordinate/center/radius magnitude: at most `10,000,000 mm`;
  at most **4,096 analytic curves** per validation. Excesses are `Unsupported`.
- Endpoints within distance tolerance are observed as equivalent joints, but
  remain at their original coordinates. A non-transitive cluster (A near B,
  B near C, A too far from C) is rejected. The reported maximum joint gap makes
  any accepted tolerance discrepancy explicit.
- A non-adjacent contact, including tangency or a gap unresolved within
  tolerance, invalidates a simple region. Adjacent curves can touch only at
  their shared endpoint(s); a second crossing or overlap still invalidates it.
- Degree-two connectivity is traversed into loops. LINE/LINE, LINE/ARC and
  ARC/ARC intersections and coincident intervals are tested analytically.
  A full circle is an analytic loop; multi-turn/inconsistent arcs are rejected.
- Area uses Green's theorem for lines and circular arcs, translated **only in
  the numerical calculation** with compensated summation. Source data retains
  its original frame. Direction/orientation does not select a material side.
- Disjoint loops and nested loops are distinguished (including mixed sets) but
  remain unsupported. No hole, island or union interpretation is invented.
- Cancellation is propagated. Work is bounded; no SOLIDWORKS COM call is made.

## Datum contract and private configuration

`CompanyToolProfile.Datums` exposes:

- `TipPointMm` and `MountDatumMm`: separate nullable source-frame points.
- `WorkingDirection`: nullable unit direction in the unchanged source frame.
- `LocalCoordinateFrame`: nullable origin plus unit, orthogonal, right-handed
  X/Y axes. Direction tolerance is `1e-10` (dimensionless). Values are checked,
  never normalized, inferred or repaired.
- Independent tip/mount/direction/frame evidence statuses and aggregate status.
- Current `SourceSha256`, selected `GeometrySha256`, `EvidenceFileSha256`.
- Reasons explaining missing/invalid/stale/unsupported data.

No lowest-point, bounding-box center, INSBASE, assumed symmetry or inferred tip
is used. Frame origin, tip and mounting point are not assumed to coincide.

The source configuration gains an optional **`DatumEvidencePath`**, separate
from DXF, Excel and the geometry selection manifest. A relative path resolves
against the configured DXF directory; an absolute path can refer to a separate
private evidence directory. It is read-only; the add-in does not create it.

```json
{
  "Version": 1,
  "DirectoryPath": "C:\\Company\\BendTools\\DXF",
  "ManifestPath": null,
  "DatumEvidencePath": "C:\\Company\\BendTools\\Evidence\\datums.json"
}
```

The process environment override is `ADDIN_BEND_TOOL_DATUM_EVIDENCE`, alongside
`ADDIN_BEND_TOOL_DXF_DIRECTORY` and optional `ADDIN_BEND_TOOL_DXF_MANIFEST`.
Environment configuration is used when the directory override is present,
as in Phase 2. No Settings UI or Excel preference schema changes are required.

Private evidence file envelope, initially containing **no verified coordinates**:

```json
{
  "Version": 1,
  "Tools": [
    {
      "Id": "AMD-STD-003",
      "SourceSha256": "<exact current source SHA256, 64 hex characters>",
      "GeometrySha256": "<exact current selected-geometry SHA256>",
      "TipPointMm": null,
      "MountDatumMm": null,
      "WorkingDirection": null,
      "LocalCoordinateFrame": null
    }
  ]
}
```

Point and direction records use nullable `X`, `Y` and optional `Evidence`.
Frame records use `OriginMm`, `XAxis`, `YAxis` (each `X`, `Y`) and `Evidence`.
Omitting a coordinate differs from supplying zero. NaN, infinity, absent
coordinates, non-unit directions and invalid frames are rejected.

### Evidence states and review binding

`DatumEvidenceStatus` is **`Missing`, `Unverified`, `VerifiedRecord`, `Stale`,
`Invalid` or `Unsupported`**. Coordinates without an approval are `Unverified`.
The four datums can have different states; approving a tip does not approve a
mounting point. The aggregate is verified only if all four records qualify.

An `Evidence` approval requires all of:

| Field | Required value / check |
| --- | --- |
| `Approval` | Explicit `Verified` (or `Unverified` for unchecked data) |
| `Method` | `MeasuredTool`, `ApprovedDrawing` or `ApprovedInspection` |
| `VerifiedBy` | Nonempty external reviewer identity |
| `VerifiedAtUtc` | Timestamp with explicit zone; not over five minutes in the future |
| `ValidUntilUtc` | Optional; must follow review time; expiry makes it stale |
| `ArtifactFile` | Relative path to the reviewed evidence artifact inside the evidence directory tree |
| `ArtifactSha256` | Exact saved artifact bytes; mismatch is stale, unreadable artifact invalid |
| `DatumPayloadSha256` | Exact approved datum payload binding described below |

An evidence artifact is read/hash-checked only. It is not executed, interpreted
as instructions, uploaded or treated as a clamp model. No URL or path traversal
is accepted. Bounds: evidence JSON 1 MiB, artifact 8 MiB, at most 72 artifacts.
Unknown/duplicate tool IDs or document versions are invalid. A malformed/missing
evidence file affects evidence states without discarding readable DXF geometry.

`GeometrySha256` (`PunchBindingV1`) binds tool ID, exact selected source bytes,
entities/instances/source records/flags/transformed analytic curves. Selecting a
different outline in the **same DXF** makes the old evidence stale. Evidence V1
supports a punch from one distinct source hash; multi-source bindings remain
`Unsupported` rather than pretending a composite hash is a DXF file hash.

`DatumPayloadSha256` (`ToolDatumPayloadMmV1`) additionally binds ID, source hash,
geometry hash, datum kind and exact coordinate/axis values. The internal
`PointPayloadHash(owner, kind, x, y)` / `FramePayloadHash(owner)` define the
canonical serialization. Copying a tip approval to a mount record, changing a
coordinate, or merely repinning a source hash cannot reuse an old approval.
Renewing these fields requires a new external review; calculating a hash alone
does not supply that review.

`VerifiedRecord` means the supplied external attestation is structurally valid
and its bindings/artifact still match. The program cannot authenticate the
reviewer's identity or independently certify their physical measurements.
**`ClampVerified` and the existing physical-verification flags remain false**;
a verified mounting point does not prove holder/clamp geometry or engagement.
No manufacturing PASS/NG is produced by any datum/region status.

Saved DXF, selection manifest, evidence file and artifact contents are rehashed
on refresh (including edits retaining size/mtime). Changed/failed inputs
invalidate the previous snapshot; held old datum objects immediately report
`Stale`, including their individual evidence states. There is no verified
last-good fallback. All data consumers must still require a current snapshot.
Expiry transitions also change the evidence fingerprint on the next refresh.

## Tests and integration boundary

New tests are synthetic C# fixtures only in
`Tests/BendToolProfiles/RegionAndDatumTests.cs`. They run with the existing
`ToolProfiles.Tests.csproj` without SOLIDWORKS, external packages or company CAD.
The optional `--company` mode reads local original files and checks hashes before
and after; raw geometry output must be directed **outside the Git checkout**.
The existing optional ezdxf QA script remains test-only.

Production integration changes only the DXF provider/models and adds two Compile
Includes. The existing managed library worker computes these validations.
No Part/BendRow/context/control or COM object is passed to this code. Check Min,
candidate-order generation, UI/Designer/event handlers and Excel are unchanged.

See [Phase 3A validation results](Validation-Phase3A.md) for actual builds/tests
and the 18-profile outcome. No CAD, company coordinates, private manifests or
review artifacts belong in the public repository.
