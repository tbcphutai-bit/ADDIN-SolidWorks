# Evidence and reproducibility

- `HARNESS_PROVENANCE.json`: detached executable hashes and backend source variants. Executables, CAD and private B-rep streams are not published.
- `SOURCE_PROTECTION.json`: initial, immediate pre-apply and post-apply hashes; actual checkout identity; intended backend-only write. Two already-changed output hashes are disclosed.
- `source_integration_audit.json`: local/branch baseline and resulting hashes; preserved overlay, Display3, Hide and cleanup methods.
- `local_backend.patch`: compatibility patch for the older local source baseline. Already applied locally; review before reuse. Git needs `--unidiff-zero` for this patch.
- `*.cs.txt`: standalone research/control harness sources. Source inputs and CAD streams stay private. These are not main Add-in project items.
- `diagnostic.json` and console log: legacy 547 reproduction and one failed tolerance-reset trial. No production retry was retained.
- `2354_candidate_plain_u_native_derived.json`: rejected alternative that still produced 547; absent from production.
- `transition_events.json`: Windows failure evidence, including heap-corruption events. A unique causal API call is unproven. A failed missing-session harness startup is retained too.
- Connection logs: observed ProgID/ROT/HRESULT/elevation/session state; final connection attaches existing PID only and performs no application activation.
- API logs and result JSON: native audit, replay, acceptance, controls, final local rerun and final branch rerun. Empty compile logs mean no compiler diagnostics; compile success alone is not a geometry pass.

Machine paths/usernames are redacted. Units are meters/m³ unless a field explicitly says mm/mm³. Sanitization does not change numeric geometry or error codes. `REPORT_MANIFEST.csv` hashes UTF-8 text normalized to LF without BOM, excluding the manifest itself.
