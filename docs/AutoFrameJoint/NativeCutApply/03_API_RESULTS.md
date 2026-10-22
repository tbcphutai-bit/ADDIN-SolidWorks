# Execution results

Use [API_RESULTS.json](API_RESULTS.json), [TEST_MATRIX.csv](TEST_MATRIX.csv), [native Feature CSV](FEATURE_CUT_INVENTORY.csv) and `evidence/native_final_2354.log.txt` as the machine-readable evidence. CSV `exported_native_definition_records` counts returned parameter DTOs; `native_cut_creation_events` also includes public-manager creation events. Unmeasured gap/interference defaults in legacy rejection DTOs are not treated as measurements in the CSV.

## Final source qualifications

- 2354 fr04 × fr03: public GhostPreview P1 → SessionManager.TryApplyCut → four native Cut-Extrudes; final symmetric difference A=0, B=0; native writer validates 0.10 mm gap and zero interference. Second apply without a fresh Preview is refused. Ghost/session cleanup completes. API Display3 execution does not prove a person saw the result.
- 2354 deliberate mismatch after cuts on A and B: all four native step bodies match the expected temporary Boolean; final mismatch is caught; both Parts restore geometry and exact feature sequence by deleting only owned features. This is an intentional rejection and a PASS of rollback, not a successful apply.
- 2354 stale ticket: no native feature creation, original bodies remain unchanged.
- Both local/branch variants: explicit detached compilation; 47/47 managed controls per variant, zero selection-interface accesses inside callbacks, no runtime native Label.Caption fallback. Managed controls include selection replacement before OK, accepted submit before list notification, missing selection, undefer/Cancel, invalid direction, stale ticket, foreign apartment and failed Undo-recording closure. These are not installed Add-in tests.

## Other data families

- 1618 fr1×fr2 and fr3×fr4: actual native solver refuses `no unique Web between parallel outward Walls`. No Part tree/stamp change was observed.
- 3615 T1/T2 current final Parts: required KEEP material is missing under the current short-Frame-cover policy. Both are refused; no native Cut writer is called.
- Earlier 3615 T1 stock-copy run: existing Cuts were explicitly suppressed on disposable copies; four new native Cuts matched the plan and produced gap 0.10 mm/interference 0. This state is a qualification fixture, not the original already-trimmed Part state. Two disposable copies changed on disk in that run; input hashes stayed unchanged.
- Final isolated T1 attempt using the local variant: `RPC_E_SERVERFAULT 0x80010105` reading `IFeature.GetTypeName2` in copy-only stock preparation. This is **FAIL**, before the writer. Final T2 suppressed-reference qualification is **NOT_RUN** because the batch stopped. The earlier T1 PASS does not overwrite this failure.

Final 2354 and final failed T1 attempt preserve all measured input/copy file hashes. Native tests ran through independent executables, not the installed ADDIN.dll. The source includes actual native Cut writing; it still needs the owner's manual Build and an Add-in OK/Cancel test. Flat-pattern and native NormalCut=true write tests remain NOT_RUN.
