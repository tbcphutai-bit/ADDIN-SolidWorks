# Manual build and limits

Read SOURCE_INTEGRATION.json before assuming the main class was copied. The user rebuilds and loads the existing Add-in through their normal workflow; this generator never builds or deploys it. The currently installed DLL may still be older than the qualified source. Check the source hash, loaded module MVID and AFJ-R74 runtime marker rather than a caption alone.

All current qualification is isolated API harness work on copies. A successful harness run does not establish installed Add-in/PMP callbacks, interactive visibility, speed, standalone Part reopen/save behavior, or exhaustive rollback. First-preview and Native Apply timings in TEST_MATRIX.csv are measured observations, not universal performance guarantees.

The failed05 observation remains ROLLBACK_UNVERIFIED. Automatic empty-Sketch deletion was only a possible explanation for its cleanup error; do not claim that behavior proved or repaired without explicit empty-definition and exact restoration evidence.

The main source copy retains the exact API-qualified bytes. Git publication removes spaces from one otherwise empty source line so git diff --check passes; SOURCE_INTEGRATION.json records both hashes and the normalized Git blob hash. That formatting-only branch combination was compiled independently, without running the main project or deploying a DLL.

No CAD originals, STEP, ZIP, DLL/PDB, screenshots, raw call stacks, usernames or machine paths belong in this report folder. Keep the PR Draft and do not merge automatically.
