# R56 test matrix

All results are native API harness results. Installed Add-in interaction and UI smoothness remain UNVERIFIED.

| Case | Pair | Source | Status | Cuts | Gap mm | Overlap mm³ | Cold Preview ms | Native Apply ms |
|---|---|---|---|---:|---:|---:|---:|---:|
| R56-15-CORNER-1 | 2354/fr01 × 2354/fr04 | 09f0e9708a11 | PASS_NATIVE_API_HARNESS | 4 | 0.1 | 0 | 43912 | 424201 |
| R56-15-CORNER-2 | 2354/fr04 × 2354/fr03 | 09f0e9708a11 | PASS_NATIVE_API_HARNESS | 4 | 0.1 | 0 | 29211 | 390430 |
| R56-15-CORNER-3 | 2354/fr03 × 2354/fr02 | 09f0e9708a11 | PASS_NATIVE_API_HARNESS | 4 | 0.1 | 0 | 47574 | 436173 |
| R56-15-CORNER-4 | 2354/fr02 × 2354/fr01 | 09f0e9708a11 | PASS_NATIVE_API_HARNESS | 4 | 0.1 | 0 | 64001 | 425952 |
| R56-12-RAW-4332-PAIR-1 | 4332/fr3a × 4332/fr5 | 6bd0957dc0ca | PAIR_PASS_WITHIN_RUN_FAILED_ON_SEPARATE_UNQUALIFIED_FIXTURE | 4 | 0.1 | 0 | 125410 | 856823 |
| R56-12-UNQUALIFIED-FIXTURE-PAIR-2 | 4332/fr1 × 4332/fr3a | 6bd0957dc0ca | REJECTED_UNVERIFIED_NATIVE_CONSTRUCTION_NOT_A_PASS | 0 | NOT_AVAILABLE | NOT_AVAILABLE | 9709 | NOT_AVAILABLE |
| R56-15-RAW-4332-PAIR-1 | 4332/fr3a × 4332/fr5 | 09f0e9708a11 | PASS_NATIVE_API_HARNESS | 4 | 0.1 | 0 | 132673 | 899666 |

The historical source12 raw pair is separate evidence. Its second requested pair was rejected before solver/native Apply because the native formed-Bend/axis construction was unqualified; it is not a passed case. Source15 raw status is updated only after its own run completes, including source/copy hash and blank-session cleanup gates.

Timings include cross-process native API calls and built-in ownership checks. They do not establish installed UI responsiveness. Cold Preview remains slow; cached P1 repetition is a separate measurement.
