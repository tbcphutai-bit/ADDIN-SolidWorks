# AFJ delivery checkpoint — 2026-10-23

**The constrained U replacement is not enabled in the production Add-in.** The installed DLL identity remains `420dfcfc-a6ee-4861-ae82-f54d916e32e4`; no main Add-in Build or Deploy was performed. The latest work is a bounded native API result and a guarded code candidate for review. It is not a completed installed repair or proof of a visible Preview.

## Useful result already obtained

On disposable copies of the actual 2354 fr03/fr04 pair, the separate completed roundtrip created **four linked, fully constrained Sketches and four actual Cut-Extrudes**. The Sketches use existing Web Faces, with **80 geometric relations, two meaningful driving parameters and zero new RefPlanes**. They do not use Convert, FIX or per-point coordinate dimensions. All four Sketches retained status3 with zero dangling/overdefining relations after each Cut and the recorded copied-Part/Assembly rebuilds.

Each actual Cut matched its independent sequential temporary Boolean target with symmetric difference0 before Save. The fifth planned near-bend subtraction removed no material and was skipped. Final overlap was0 and gap was0.10mm. Both copied Parts reopened Read-Only with valid bodies and retained constrained Sketch observations. Disk-reopen checks covered body health, volume/area and Sketch/support; they did **not** repeat a full B-rep symmetric-difference comparison after disk reopen. See [four-Cut report](U_FOUR_CUT_ROUNDTRIP.md).

The two parameters are the assembly fit gap and a shared tool-closure margin. The sheet overrun remains a separate manufacturing parameter; the formed-profile Cut depths are not all `t+0.10mm`. The independent source-width follow test changed70→71→70mm and preserved the signed0.10mm gap without rerunning the solver; it qualified Sketch follow only.

## Latest transaction result: FAIL / UNVERIFIED

The latest `native_u_normalized_fixture_rollback_01` test first prepared a disposable in-memory fixture to remove thirteen already-invalid, OutOfContext Assembly holders through a known empty-Sketch lifecycle. Original Part histories and B-rep remained equal during preparation. This preparation is **not enabled in production** and is not a general metadata-deletion rule.

The new writer then created all four constrained Sketches (80 relations, two dimensions, zero new planes). **Two actual native Cuts completed** with recorded exact temporary-target agreement. While locating the next Sketch, `IFeature.GetFirstSubFeature` in `FindById` returned **`0x80010105 (RPC_E_SERVERFAULT)`**. The intended failure after Cut4 was never reached. The rollback journal then encountered the same HRESULT while reading `IFeature.GetTypeName2`, before any recorded deletion. Result: **`ROLLBACK_UNVERIFIED`, zero recorded Delete calls**. Partial tree observations after this COM fault are not proof that original Features were deleted or restored.

The harness closed its owned copies without Save and restored a blank session. Its frozen source set and copied disk files retained their own hashes. This does not prove in-memory rollback, dirty/Undo restoration, installed Add-in stability or the cause of the user's Part-open hang.

## Source preservation incident

A separate original-source Pack and Go attempt detected an **unexpected hash change in `2354-02-lc-B11.SLDASM` despite Read-Only observations**. Five original Parts were unchanged. The user confirmed no Save action. The precise cause is unverified; no exact backup was restored and the observed file was preserved. This is explicitly reported in [SOURCE_HASH_INCIDENT20261023.md](SOURCE_HASH_INCIDENT20261023.md). Earlier successful hash checks apply only to their individual runs.

## Review boundary

The existing A/B selection and Preview route remain the integration entrypoints. Enabling the replacement requires a qualified native transaction/rollback path and an installed Add-in check. Additional folded profiles, other joint poses, visible Preview smoothness and the installed Part-open hang remain **UNVERIFIED**. A guarded source candidate must not be described as an active fix while its gate remains off.

Machine-readable checkpoint: [FINAL_DELIVERY20261023.json](FINAL_DELIVERY20261023.json). Earlier failed and interrupted trials remain in [U_ROLLBACK_DIAGNOSTICS20261023.md](U_ROLLBACK_DIAGNOSTICS20261023.md).

## Source package for immediate review

[The full gated source candidate](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/tree/feat/afj-multi-profile-cut-preview-20261022/research/AutoFrameJoint/ConstrainedUWriter20261023) is published on the source PR branch in `research/`, outside the main project Compile items. It includes the constrained writer, pair transaction, strict journal, integration patch, compile scope and source hashes. It is **not installed or enabled**; the default gate rejects U Apply if that candidate is integrated unchanged. Existing production source remains unchanged. This package must not be copied over a different local baseline.
