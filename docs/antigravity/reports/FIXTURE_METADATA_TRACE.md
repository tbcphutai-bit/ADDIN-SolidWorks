# CHECK HOLE CG1 — FIXTURE METADATA & REPRODUCIBILITY TRACE

> Source file: [`docs/antigravity/reports/FIXTURE_GENERATOR.cs`](FIXTURE_GENERATOR.cs)
> Date: 2026-10-19
> Environment: SOLIDWORKS 2024 SP5 / Windows 11 / .NET 4.8 / C# 7.3

---

## 1. Overview of Standalone Test Fixtures

| Fixture Name | Purpose | Base Assembly | Component Configuration | Geometric Modification | Expected Check Hole Metrics |
|---|---|---|---|---|---|
| `Fixture_OK.SLDASM` | Nominal aligned 2-part joint | `1618-02-bh-A10.SLDASM` | Unsuppressed: `1618-04-bh-A10-1-1`, `1618-04-bh-A10-fr1-2`<br>Suppressed: All other parts | None (as modeled) | Holes=57, Grouped=24, Unmatched=33, Stacks=12, **NG=0** |
| `Fixture_NG_Offset.SLDASM` | Misaligned 2-part joint | `Fixture_OK.SLDASM` | Same components as OK | Feature `合致` (`MateGroup`) and all 19 sub-mates suppressed.<br>Translation: $\Delta X = +0.3000\text{ mm}$ on `fr1` | Holes=57, Grouped=24, Unmatched=33, Stacks=12, **NG=12** |
| `Fixture_Unmatched.SLDASM` | Single part model | `Fixture_OK.SLDASM` | Unsuppressed: `1618-04-bh-A10-1-1`<br>Suppressed: `fr1` and all others | None | Holes=32, Grouped=0, Stacks=0, **Unmatched=32**, NG=0 |
| `Fixture_MultiLayer_ABC.SLDASM` | 3-layer multi-part joint | `1618-02-bh-A10.SLDASM` | Unsuppressed: `1618-04-bh-A10-1-1`, `fr1-2`, `fr3-1`<br>Suppressed: All other parts | None (3 parts share nominal hole joints) | Holes=82, Grouped=48, Unmatched=34, Stacks=24, **NG=0** |
| `Fixture_OldMarker.SLDASM` | Stale marker negative test | `Fixture_OK.SLDASM` | Same as OK | Added 3D sketch named `CHECK-HOLE-NG-STALE-MARKER` | **Rejected before run** (Exit code 1) |

---

## 2. In-Depth Trace for `Fixture_NG_Offset.SLDASM`

### 2.1 Assembly Component Positions

| Component | State | Initial Position ($X, Y, Z$) mm | Applied Translation ($\Delta X, \Delta Y, \Delta Z$) mm | Final Position ($X, Y, Z$) mm |
|---|---|---|---|---|
| `1618-04-bh-A10-1-1` | Fixed (Origin) | $(0.000, 0.000, 0.000)$ | $(0.000, 0.000, 0.000)$ | $(0.000, 0.000, 0.000)$ |
| `1618-04-bh-A10-fr1-2` | Floating (Mates Suppressed) | $(1797.500, 3.000, -1052.500)$ | $(+0.300, 0.000, 0.000)$ | $(1797.800, 3.000, -1052.500)$ |

### 2.2 Mate Suppression Trace
In `1618-02-bh-A10.SLDASM`, the assembly contains a top-level mate feature:
- **Feature Name:** `合致`
- **Type Name:** `MateGroup`
- **Sub-features (19 total):** `一致19`, `一致21`, `一致23`, `距離5`, `一致24`, `距離15`, `一致52`, `対称5`, `距離26`, `距離27`, `対称6`, `距離28`, `対称7`, `対称8`, `距離29`, `対称9`, `距離30`, `距離31`, `対称11`.
- **Suppression State:** All 19 sub-features suppressed using `feat.SetSuppression2((int)swFeatureSuppressionAction_e.swSuppressFeature, ...)` so `EditRebuild3()` will not solve mates back to nominal.

### 2.3 Hole Geometry & Stack Transverse Distance
The 12 hole pairs formed between `1618-04-bh-A10-1-1` and `1618-04-bh-A10-fr1-2` share the following geometric attributes:
- **Hole Axis Direction:** $(0.00, 1.00, 0.00)$ (along global $Y$-axis).
- **Hole Centers in $X$:** $X \in \{145.00, 445.00, 745.00, 1045.00, 1345.00, 1645.00, 1945.00, 2245.00, 2545.00, 2845.00, 3145.00, 3445.00\}\text{ mm}$.
- **Hole Centers in $Z$:** $Z = -1094.00\text{ mm}$.
- **Transverse Center Distance:**
  $$\text{Offset} = \sqrt{(\Delta X)^2 + (\Delta Z)^2} = \sqrt{(0.3000\text{ mm})^2 + 0^2} = 0.3000\text{ mm}$$
- **Engine Tolerance Thresholds:**
  - `PositionToleranceM = 0.00010 m` ($0.1000\text{ mm}$)
  - `SearchPositionToleranceM = 0.0030 m` ($3.000\text{ mm}$)
- **Pairing & Evaluation Verdict:**
  1. $\text{Offset} \le 3.000\text{ mm} \implies$ Accepted by `BuildCandidateStacks` into a 2-hole stack.
  2. $\text{Offset} = 0.3000\text{ mm} > 0.1000\text{ mm} \implies$ Evaluated by `EvaluateStack` as **`IsNg = true`**.
  3. Reason string: `"Lệch tâm vượt dung sai; góc trong cụm có lỗ biến dạng chưa được kết luận"`.

---

## 3. Results Comparison: `main` Baseline vs `CG1` Candidate

```text
Assembly: Fixture_NG_Offset.SLDASM
MVID Baseline:  8008c4c2-2258-456a-8cf5-5b6fed1d742b (main)
MVID Candidate: 456f9ade-ac27-4395-854f-857ad2521fd0 (CG1)
```

| Row ID | Reference Component | Actual Component | Offset (mm) | Angle (deg) | Baseline Status | Candidate Status | CG1 Group Decision |
|---|---|---|---|---|---|---|---|
| 1 | `1618-04-bh-A10-1-1` | `1618-04-bh-A10-fr1-2` | 0.3000 | N/A | **NG** | **NG** | `CHUA_XAC_DINH_MASTER` |
| 2 | `1618-04-bh-A10-1-1` | `1618-04-bh-A10-fr1-2` | 0.3000 | N/A | **NG** | **NG** | `CHUA_XAC_DINH_MASTER` |
| 3 | `1618-04-bh-A10-1-1` | `1618-04-bh-A10-fr1-2` | 0.3000 | N/A | **NG** | **NG** | `CHUA_XAC_DINH_MASTER` |
| 4 | `1618-04-bh-A10-1-1` | `1618-04-bh-A10-fr1-2` | 0.3000 | N/A | **NG** | **NG** | `CHUA_XAC_DINH_MASTER` |
| 5 | `1618-04-bh-A10-1-1` | `1618-04-bh-A10-fr1-2` | 0.3000 | N/A | **NG** | **NG** | `CHUA_XAC_DINH_MASTER` |
| 6 | `1618-04-bh-A10-1-1` | `1618-04-bh-A10-fr1-2` | 0.3000 | N/A | **NG** | **NG** | `CHUA_XAC_DINH_MASTER` |
| 7 | `1618-04-bh-A10-1-1` | `1618-04-bh-A10-fr1-2` | 0.3000 | N/A | **NG** | **NG** | `CHUA_XAC_DINH_MASTER` |
| 8 | `1618-04-bh-A10-1-1` | `1618-04-bh-A10-fr1-2` | 0.3000 | N/A | **NG** | **NG** | `CHUA_XAC_DINH_MASTER` |
| 9 | `1618-04-bh-A10-1-1` | `1618-04-bh-A10-fr1-2` | 0.3000 | N/A | **NG** | **NG** | `CHUA_XAC_DINH_MASTER` |
| 10 | `1618-04-bh-A10-1-1` | `1618-04-bh-A10-fr1-2` | 0.3000 | N/A | **NG** | **NG** | `CHUA_XAC_DINH_MASTER` |
| 11 | `1618-04-bh-A10-1-1` | `1618-04-bh-A10-fr1-2` | 0.3000 | N/A | **NG** | **NG** | `CHUA_XAC_DINH_MASTER` |
| 12 | `1618-04-bh-A10-1-1` | `1618-04-bh-A10-fr1-2` | 0.3000 | N/A | **NG** | **NG** | `CHUA_XAC_DINH_MASTER` |
| 13–45 | N/A | `1618-04-bh-A10-fr1-2` or `1-1` | N/A | N/A | **Chưa ghép** | **Chưa ghép** | N/A (33 unmatched holes) |

**Conclusion:** 100% equivalence between `main` baseline and `CG1` candidate. Real NG detection (`NgStacks = 12 >= 1`) verified.

---

## 4. Hardened Generator Safeguards & Execution Contract (Review #8)

In response to Review #8 BLOCKER A, [`FIXTURE_GENERATOR.cs`](FIXTURE_GENERATOR.cs) has been hardened with the following strict fail-closed safety contracts:

1. **Sandbox Root & Consent Token Validation:**
   - Requires physical existence of `CG1_SANDBOX_OK.txt` in `sandboxRoot`.
   - Strictly enforces that `fixtureDir` resides within `sandboxRoot`.
   - Refuses execution if `fixtureDir` or `sandboxRoot` overlaps with the git repository or active project workspace (`Directory.GetCurrentDirectory()`).
2. **Pre-Execution Component Reference Containment:**
   - Opens base assembly (`1618-02-bh-A10.SLDASM`) read-only and iterates all components via `VerifyReferencesUnderSandbox`.
   - Ensures zero components reference paths outside `sandboxRoot` and all dependencies exist on disk.
3. **Overwrite Protection:**
   - `allowOverwrite` parameter defaults to `false`.
   - Refuses to overwrite existing fixtures unless explicitly invoked with `--force-overwrite`.
4. **Complete Resource Lifecycle Management (`try/finally`):**
   - Every opened `ModelDoc2` document is wrapped in a strict `try/finally` block ensuring `app.CloseDoc(doc.GetTitle())` is called under all success and exception paths.
5. **Rigorous API Return Value Checks:**
   - `OpenDoc6`: Verified `doc != null` and `swErrors == 0`.
   - `GetComponents`: Verified non-null and non-empty.
   - `SetSuppression2`: Verified return code is `swSuppressionChangeOk`.
   - `SaveAs`: Verified boolean return is `true`, `swErrors == 0`, and file exists on disk with length $> 0$.
6. **Geometric Transformation & Readback Assertions:**
   - Exactly 19 sub-mates in feature `合致` (`MateGroup`) verified suppressed (`suppressedMateCount == 19`).
   - Component `fr1` lateral offset readback verified after `EditRebuild3()`: $\Delta X = 0.000300\text{ m} \pm 10^{-6}\text{ m}$ ($+0.3000\text{ mm}$).
   - Stale marker feature `CHECK-HOLE-NG-STALE-MARKER` verified on `Fixture_OldMarker.SLDASM`.
7. **Standalone CLI Self-Test:**
   - `FIXTURE_GENERATOR.exe --self-test` executes automated unit testing of sandbox consent, path containment, and negative rejection without requiring a running SOLIDWORKS instance.
