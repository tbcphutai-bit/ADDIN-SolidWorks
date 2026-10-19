# Reviewed process input for the existing Check Chấn window

Baseline: PR #30, `07d29bd3af2dc17787fd276eb59beee97645a518`.
This delivery imports an explicit process graph into `PartSequenceSpace.Reviewed`
and executes the existing sequence/motion engines. It does not unfold a finished
Part into an assumed manufacturing process or create a manufacturing PASS.

## Configuration

The Part and Assembly backends automatically look for:

`%LOCALAPPDATA%\ADDIN\BendTools\process-provider.json`

`ADDIN_BEND_PROCESS_CONFIG` can override that file. There is no new command or
new Settings layout. The existing tool-library configuration remains separate.
An unconfigured provider produces `PROCESS_PROVIDER_NOT_CONFIGURED` in the
existing process diagnostics. It does not disable Check Min.

The local configuration has this shape (placeholders are deliberately not valid
engineering evidence):

```json
{
  "Version": 1,
  "TrustedAuthoritiesPath": "review-authorities.json",
  "AuthoritySha256": "<independently verified SHA256>",
  "Sources": [
    {
      "NativePath": "<absolute path to the native Part>",
      "Configuration": "<exact configuration name>",
      "PackagePath": "process/part-process.json",
      "PackageSha256": "<approved package SHA256>",
      "ExpectedRevision": "<approved process revision>"
    }
  ]
}
```

Paths in this configuration are relative to the configuration directory unless
absolute. Package artifact/review paths must instead stay inside their package
directory: no absolute paths, parent traversal, alternate streams or reparse
points. Matching requires exactly one Part-path/configuration entry. A package
cannot supply its own trusted public key and thereby grant itself authority.

## Package contract

The complete property names are defined by `ProcessPackageRecords.cs`.
All lengths are mm; angles are radians. Mandatory numeric fields are nullable
on input and must be supplied, including explicit zero errors and motions.

| Input | Required binding |
| --- | --- |
| Header | Version 1, revision, snapshot, update stamp, configuration, full native geometry SHA256 |
| Bends | Exact native FeatureId, PersistentId and native frame ID; no row-number substitution |
| Artifacts | Unique ID, role, relative path, SHA256; current bytes are checked |
| Registration | Whole-sheet origin/orthonormal axes/axial direction, error, source artifact and independent review |
| Geometry | Explicit analytic material contours and assigned outer/hole roles over an entire finite axial interval |
| States | Completed native FeatureIds and exact registered whole-sheet geometry/pose boundary hash |
| Transitions | Before/after state IDs, native bend ID, tool ID, setup/process artifacts, explicit phase segments and review |
| Motion | Pivot, start/end rotation, translation and axial position with all error bounds |
| Tools | Reviewed real `ToolAxialSetupRecord` plus motion, or explicitly signed simulation-only geometry |
| Graph review | Source revision, alternatives-complete assertion, native identity, registered endpoints and all plan payloads |
| Assembly use | Separate signed permission to reuse the Part-local program under equivalent eligible occurrences |

Artifact roles include `RegisteredGeometry`, `ProcessSpecification`,
`SetupConditions`, `MachineConstraints` and optional `FormingContact`.
Registration and every process/setup review use the existing independently
configured review verifier. Expired or changed evidence withdraws the proof.
Coordinates, a hash, a reference drawing or a successfully parsed signature
alone do not constitute a physical clamp inspection.

Currently the package geometry contract is
`ExplicitAnalyticPrismOverEntireAxialInterval`. It supports validated LINE/ARC
material regions with holes, not an arbitrary imported B-rep or sampled-slice
certificate. Nonparallel bend axes in the common 2D registration are rejected.
Registration error is explicitly bounded to 0.001 mm and accounted for by
material/solid/motion error bounds. The importer never tightens a supplied error
to force CLEAR. General SPLINE process material is rejected without replacing
the existing independently certified holder-SPLINE backend.

Optional planar forming contacts preserve the source-bound sheet/tool hashes,
normal, zone and finite axial interval. They go through the existing narrow
contact proof. The exact-prism definition is retained only when its error is
zero; unsupported, uncertain or rotating contacts are not blanket exclusions.

Real tools go through `ToolAxialSetupEvidence.Read` and
`ProcessToolPose.FromEvidence`, retaining the existing datum, mapping, seating,
finite length and physical-inspection gates. Punch and Upper Holder remain
independent. No V die is imported or checked. Simulation geometry is accepted
only with an authorized `SimulationProcessTool` proof whose SimulationOnly
flag is true. Such a result remains visibly SIMULATION, even if all numeric
queries complete CLEAR.

## Authoring and verification

1. Obtain the native identities and geometry snapshot from the existing STA
   capture. Define and independently verify the whole-sheet registration.
   After native geometry/configuration changes, regenerate and re-review the
   affected package rather than copying old approval hashes.
2. Supply the actual before/after states, panels, motions, constraints and
   setup/extents from reviewed process information. Do not infer them from the
   final Part or its feature-tree order.
3. Read an unsigned draft through `ProcessPackageImporter.Read`. Its
   `ReviewPayloads` expose canonical registration, per-tool simulation,
   per-plan, graph and Part-local-use payload hashes for an external review
   workflow. The draft is UNKNOWN; it is not a bypass.
4. Verify/register the source material and tool/setup facts, then authorize the
   exact payloads with an independently provisioned reviewer. The production
   importer has no private signing key and never signs anything.
5. Pin the final package revision/SHA and authority-file SHA in local
   configuration. Run the existing Check Chấn window and inspect the displayed
   evidence level, missing reasons, native IDs and independent phase pairs.

`ProcessPackageTests.cs` contains a complete software-only authoring example.
Its temporary RSA authority is explicitly simulation-only, not company setup
evidence and not an example of how to approve a factory process without review.

## Execution, freshness and sharing

Native capture stays on the STA and happens once per eligible equivalent
source/configuration. The static async adapter passes only managed DTOs and a
managed provider to file workers; it does not capture the owning session or COM
proxies in its async state machine. Intermediate alternative process states
are not substituted for the observed final envelope or collapsed into one
legacy per-bend plan.

The bounded import cache keys model/library lifetime, exact source identity,
configuration bytes, package pin and revision. Unchanged files reuse immutable
imports; failed partial reads are retried rather than negatively cached.
Source/configuration/authority changes, cancellation and disposal withdraw
appropriate leases. A managed periodic audit runs while results are displayed
(two-second polling cadence); a full file-audit barrier is required before
publication. Kernels do not perform provider file I/O or COM work.

Tool assembly identity is distinct from its per-feature setup certificate.
The former includes verified physical source/seating/frame/section identity
and exact placed material, spans and errors. The latter remains bound to its
native feature and process payload in all proofs/cache keys. This permits the
same physical tool to work on two native bends without incorrectly treating
the different per-bend review payloads as a hardware change. Changed placed
geometry, lengths, uncertainty or motion joins still prevent a common-tool
proof, including changes between phases of one bend.

## Selection preference

The user's preference is: one tool type for the whole Part first, then the
fewest tool/setup changes. `PreferredCheckedSequence` orders **only complete
CLEAR paths in the supplied checked scope** by tool-type count, change count,
then stable tool/native-ID order. Existing navigation selects this path unless
the user has pinned a selection. UNKNOWN/colliding/stale paths are ineligible.

The current solver proves a constant Punch–Holder/setup across the whole Part,
so a preferred complete path uses one tool and zero changes. A multi-tool
fallback is not currently searched: it would require explicit, reviewed tool
changeover transitions and their motion/continuity proof. Failure to find a
common tool does not prove that no multi-tool manufacturing process exists.
`WinnerToolId` stays null and manufacturing status stays `CHUA_XAC_MINH`.

## Known acceptance gates

- No reviewed company process graph or physically inspected tool/setup package
  was supplied. Native manufacturing acceptance is BLOCKED by that missing
  evidence. Controlled importer/numeric/UI tests do not replace it.
- Native dumps expose COM/timer lifetime defects fixed in source, but the
  installed in-process startup DLL remains a different build from the isolated
  product under test. Clean native shutdown still fails its audit. The source
  fixes are not claimed to have eliminated every native heap-corruption crash.
- See [the validation report](Validation-LiveProcessProvider.md) for actual
  counts, A/B observations, new-library audit and remaining NOT RUN cases.
