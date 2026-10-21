# Auto Frame Joint Feature Cut Analysis

## Status

This report package is incomplete for native Cut-feature study. The SOLIDWORKS FeatureManager API did not return a model for any of the seven handoff Parts attempted. Eight additional 3615a Parts were found and inventoried locally but not opened through the API. No actual Cut feature is claimed as read.

## Findings

- `API_VERIFIED`: 0 Cut Features; 0 Parts successfully opened and read.
- `GEOMETRY_VERIFIED`: the supplied-derived 1618a STEP table reports six valid B-rep solids and metrics, not independently recomputed here; no Cut geometry was verified.
- `INFERRED`: reference images suggest profiles and pair-specific projected boundary variation but do not prove operation type or removed material.
- KCC 26003 defines the winner by hiding the opposing thickness end. Short-side precedence applies only to specified four-side bent-panel and cut-plate-panel classes, subject to drawing override.

See the eight numbered reports and the CSV/JSON inventory in this folder.

## Public-data boundary

No SLDPRT, SLDASM, STEP, source ZIP, machine-specific absolute path, or unapproved source image is included. Missing data is marked `NOT_AVAILABLE` or `UNVERIFIED`.

## Follow-up API access attempt

The user confirmed SOLIDWORKS was open, but the Codex command executor could not see its process or Running Object Table entry (MK_E_UNAVAILABLE, 0x800401E3). No new instance was started and no feature data was extracted. See [09_API_ACCESS_DIAGNOSTICS.md](09_API_ACCESS_DIAGNOSTICS.md) and [API_ACCESS_DIAGNOSTICS.json](API_ACCESS_DIAGNOSTICS.json) for sanitized evidence and the minimal next action.
