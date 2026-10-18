# Issue #20 — offline snapshot compatibility with Phase 3D.1

The audit branch preserves its original PR #18 commit
`36dc2484d1034c020ea4a8f0185a6f8c07740920`. Integration commit
`0a0d19e` brings in the exact PR #19 baseline
`949dd92f6cf7dd2fd1f4e8e9333920457900748f` as a second parent, without rewriting
either PR's history. PR #18 targets `feature/check-chan-phase3d1-native-coverage`.
No PR is merged, and no other branch is updated.

The compatibility changes are confined to the offline tool, managed tests and
documentation. `Commands/`, UI, Data, `ADDIN.csproj` and `SWAddin.cs` are identical
to the pinned Phase 3D.1 baseline. There are no Phase 4A changes.

## Source contract

The reader accepts the existing raw saved `PlacementModelCapture` JSON; it does
not require a different wrapper or modify saved files. A present
`Bodies[].EnvelopeTopology` must contain the Phase 3D.1 native contract:

| Field | Required validation |
| --- | --- |
| BodyId / captured body Id | Same exact body identity. |
| NativeEdgeCount / NativeFaceCount | Match the complete arrays and existing resource limits. Native edge IDs must be unique. |
| Circles | Explicit array, including `[]` for a LINE-only shell. Exactly one record for every circular edge; no duplicate, unknown or linear-edge binding. |
| Circles[].EdgeId | Explicit integer identifying that exact native circular edge. |
| Circles[].Axis | Explicit finite unit source axis, within the Phase 3D.1 `1e-12` unit policy. Never inferred from endpoints or midpoint. |
| Circles[].CurveAndEdgeSameSense | Explicit Boolean, including false. Separate from the directed coedge's Forward flag. |
| Circular edge Arc | Existing center/midpoint/radius/full-circle metadata must be present. |
| SourceSha256 | Mandatory valid SHA256, matching the reconstructed production DTO's canonical `NativeAnalyticShellV2` hash. |

The constructor of `NativeEnvelopeBody` remains the only implementation of the
V2 native digest. Source array order, axis and sense are preserved. A missing or
incorrect saved hash is rejected; no replacement hash is written. A valid hash
does not approve an inconsistent plane or winding: the original Phase 3D.1
`PrismaticEnvelopeCompiler.Read` and its `OrthogonalNativeEnvelopeCompiler`
fallback still validate the complete geometry.

Native V1 packets without circle metadata are rejected explicitly. Obtain a new
read-only capture from the x64 Phase 3D.1 producer, then obtain the appropriate
external evidence for those new bytes. Do not fill guessed metadata or edit the
saved digest. Legacy managed captures without native topology retain only the
existing uniform proof; unsupported geometry remains UNKNOWN.

## Numeric values and process architecture

The CLI and managed test project now explicitly target **x64**, with Prefer32Bit
disabled. `Decode` also rejects a non-x64 host with
`CAPTURE_REQUIRES_X64_PROCESS`. The .NET Framework native canonical hash was
observed to disagree in an AnyCPU/32-bit replay and agree exactly in x64; the
native producer is x64. The production canonical format is unchanged.

The framework JSON reader retains raw numeric token text until a field is read
as binary64 or integer. There is no intermediate Decimal conversion, no rounding,
normalization or tolerance comparison of source hash values. Tests include small
binary64 axis components, rigid transforms and equivalent reversed circle
parameters. UTF-8/BOM, typed required fields, JSON quotas and duplicate object
property rejection remain explicit.

`OfflineModelCaptureV1` signing purpose and payload binding are unchanged: proof
pins the exact original capture bytes, saved Part SHA and machine/setup scope.
Native `SourceSha256` is an additional mandatory integrity check. Neither proof
can replace the other. Stale configuration/stamp, Part mismatch, signature or
expiry failures still prevent production readiness.

## Diagnostics and local reports

The CLI additionally writes `PRIVATE-model-context.json`, containing the accepted
native contract, source hashes/circle counts, review status and per-feature
coverage tier/method/reasons. A rejected packet exposes an actionable diagnostic;
all tool slots and UNKNOWN evidence states remain in the main audit report.
This file, every audit summary, capture, Part, baseline, path/hash, coordinates,
reviewer and signed artifact remain outside GitHub.

Important reason codes include:

- `CAPTURE_NATIVE_CIRCLE_METADATA_MISSING` / `_INCOMPLETE`: recapture the full V2
  source; do not infer missing planes or senses.
- `CAPTURE_NATIVE_CIRCLE_EDGE_BINDING_INVALID`: inspect duplicate/missing/wrong
  source edge associations; do not reorder or discard source records to pass.
- `CAPTURE_NATIVE_CIRCLE_AXIS_INVALID`: invalid source axis; obtain correct source
  metadata from the producer.
- `CAPTURE_NATIVE_SOURCE_HASH_REQUIRED` / `_MISMATCH`: absent, stale or altered
  native source; obtain a fresh matching packet and review its exact bytes.
- `MISSING_CAPTURE_FIELD:*`: the named typed source field is absent.
- `INVALID_CAPTURE_JSON`: malformed JSON or duplicate object property.
- `CAPTURE_REQUIRES_X64_PROCESS`: use the provided x64 build.

Certified sheet coverage is still separate from tool readiness. Source/mapping,
material, physical seating, finite independent Punch/Holder axial occupancy, XY
pose and limit applicability remain mandatory. The tool never creates production
signatures or supplies missing measurements.

## Reproduction and validation

```powershell
& $MSBuild tools/BendToolAudit/BendToolAudit.csproj /t:Rebuild `
  /p:UseSharedCompilation=false /p:Configuration=Debug /p:Platform=x64 `
  /p:OutputPath=$PrivateCliDebug
& $MSBuild tools/BendToolAudit/BendToolAudit.csproj /t:Rebuild `
  /p:UseSharedCompilation=false /p:Configuration=Release /p:Platform=x64 `
  /p:Optimize=true /p:OutputPath=$PrivateCliRelease
& $MSBuild Tests/BendToolProfiles/ToolProfiles.Tests.csproj /t:Rebuild `
  /p:UseSharedCompilation=false /p:Platform=x64 /p:OutputPath=$PrivateTestBin
& "$PrivateTestBin/ToolProfiles.Tests.exe" --audit-snapshot-tests
& "$PrivateTestBin/ToolProfiles.Tests.exe"

# Explicit local geometry replay, not an external review or production audit:
& "$PrivateTestBin/ToolProfiles.Tests.exe" --audit-snapshot-private `
  $PrivateSavedCapture $PrivatePartCopy $NewPrivateOutput $ExpectedBendCount
```

Before the fix, both the synthetic full-circle bore and transverse quarter-bend
roundtrips failed with `CAPTURE_NATIVE_SOURCE_HASH_MISMATCH`. The added tests now
restore their exact native hash, circle axis/sense, envelope hash and certified
continuous coverage. Mutations cover missing/null/truncated circles, absent typed
fields, duplicate/unknown edge IDs, invalid axis, reversed sense, changed geometry,
bad/missing native hash, malformed JSON, Part SHA, model/config and signature.
The actual CLI is exercised with synthetic signed valid and malformed packets;
unreviewed tools remain UNKNOWN in both cases, including the four followups.

Full managed Debug/x64 and optimized TRACE-only Release/x64 each pass **252
groups / 8,437 assertions**, retaining all inherited Phase 3B.1/3C/3D/3D.1 and
Data Audit groups. Bend Planning passes **24 groups / 609 assertions**. No
SOLIDWORKS interop references or STA capture adapter are linked into the audit.

Private saved captures and their existing private Part copies were replayed in
both configurations with exact hashes and preserved coverage; detailed results
and source-byte fingerprints are local only. This is an offline replay, not a new
live SOLIDWORKS capture, physical inspection or claim of production approval.
