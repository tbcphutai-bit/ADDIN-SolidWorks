# Unexpected original Assembly hash change — 2026-10-23

**Status: cause UNVERIFIED; original Assembly was not restored or overwritten.**

During the separate Pack and Go copy-preparation attempt at **13:45:54 Asia/Bangkok**, the disk hash of `2354-02-lc-B11.SLDASM` changed. All six source documents were read back as Read-Only. The harness called `SavePackAndGo` with six destinations in a fresh copy folder and received six status values of 0. It did not call a source Save, modeling or rebuild API. A hash guard then failed and stopped qualification. The user confirmed that they did **not** click Save.

| Observation | Before | After |
| --- | --- | --- |
| Original Assembly bytes | 113534 | 113340 |
| Original Assembly SHA-256 | `8f528a6b3de428da1008013a56e59220e23638c1fea29dba69bca96934afd2c0` | `c1815b3fdbad8cef752a450fa7aef6385c2278c663e27ff303748be8aca0f0bb` |
| Five original Part hashes | Recorded before the attempt | All five unchanged |

The changed Assembly was preserved privately as observed. A candidate Assembly entry in the available archive had a different length and SHA-256 from the before value. No exact before-hash backup was found in the checked recovery sources, so no automatic restoration was attempted. The current file was preserved; these checks do not establish whether its modeling contents or references changed.

The timing is established. The precise native/API cause is **UNVERIFIED**. Read-Only API observations and absence of a source Save call do not justify claiming the source remained unchanged. This attempt is not a successful CAD preservation qualification and is separate from the feature-writer experiments on disposable copies.

Further testing stopped using these original documents. Subsequent geometry experiments used copied inputs only. Their own hash results remain scoped to their recorded source sets; they do not negate this incident. No CAD, archive or private machine path is published.

Machine-readable facts: [SOURCE_HASH_INCIDENT20261023.json](SOURCE_HASH_INCIDENT20261023.json).
