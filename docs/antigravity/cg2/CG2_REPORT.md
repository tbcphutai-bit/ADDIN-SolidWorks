# CHECK HOLE CG2 — ANTIGRAVITY VERIFICATION REPORT

> STATUS: **NOT_STARTED — TEMPLATE ONLY.**
> This file was created by ChatGPT. It is **not proof** CG2 is built, tested or accepted.
> Anti must replace fields with actual evidence, commit and push this report to the CG2 branch.

## A. Version and handoff

| Item | Value |
|---|---|
| Repo | `tbcphutai-bit/ADDIN-SolidWorks` |
| Base branch | `feat/check-hole-connection-groups-cg1` |
| Working branch | `feat/check-hole-cg2-part-relations` |
| Source SHA actually compiled | NOT_RECORDED |
| Report SHA (after push) | NOT_RECORDED |
| SOLIDWORKS version and Interop | NOT_RECORDED |
| Debug x64 | NOT_TESTED |
| Release x64 | NOT_TESTED |
| Offline tests | NOT_TESTED |
| Real CAD tests | NOT_TESTED |

## B. Code-completeness audit

| File | Exists | Reviewed line-by-line | Compile/integration result |
|---|---|---|---|
| `Commands/CheckAssemblyHole.PartRelations.cs` | YES | NOT_VERIFIED | NOT_TESTED |
| `Commands/CheckAssemblyHole.cs` | YES | NOT_VERIFIED | NOT_TESTED |
| `Commands/CheckHoleResultsForm.cs` | YES | NOT_VERIFIED | NOT_TESTED |
| `ADDIN.csproj` | YES | NOT_VERIFIED | NOT_TESTED |

Confirm exactly one `BuildCg2PartRelations` call and exactly one CG2 compile entry; no changed production NG/Recovery/Pattern/marker semantics. Collect `git diff --check` and source diff relative to the CG1 base.

## C. Full rebuild logs

| Target | PASS/FAIL/BLOCKED | Compiler invoked? | Errors | Warnings | SHA/log |
|---|---|---|---|---|---|
| Debug|x64 | NOT_TESTED | NOT_VERIFIED | | | |
| Release|x64 | NOT_TESTED | NOT_VERIFIED | | | |

## D. Real CAD test matrix

| ID | Scenario | Status | CG1 production result vs CG2 result | CG2 candidates/coverage | Evidence |
|---|---|---|---|---|---|
| CG2-T01 | Aligned 2-part holes | NOT_TESTED | | | |
| CG2-T02 | A–B–C, B lacks hole | NOT_TESTED | | | |
| CG2-T03 | Blind/thread valid joint | NOT_TESTED | | | |
| CG2-T04 | Independent B–D site | NOT_TESTED | | | |
| CG2-T05 | AABB false positive on concave Part | NOT_TESTED | | | |
| CG2-T06 | Repeated Part file, distinct occurrences | NOT_TESTED | | | |
| CG2-T07 | Invalid bounds / suppressed / unavailable | NOT_TESTED | | | |
| CG2-T08 | Zero-hole Assembly | NOT_TESTED | | | |
| CG2-T09 | Large model budget/partial coverage | NOT_TESTED | | | |
| CG2-T10 | Rerun/cancel/switch/close | NOT_TESTED | | | |
| CG2-T11 | Existing Recovery/Pattern/NG fixtures | NOT_TESTED | | | |
| CG2-T12 | CG2 API error / fallback | NOT_TESTED | | | |

**Required invariant:** production `OK/NG/Pattern/Missing/marker/CSV` must not change when CG2 reports candidates or partial/ERROR_UNVERIFIED. Mark the result *NOT_VERIFIED* if fixture is absent.

## E. Safety and limitations

- [ ] All work on disposable Pack-and-Go assembly fixtures, not active production CAD.
- [ ] Candidate rows remain `CHECK_ONLY`; no inferred master, new NG, or Part modification.
- [ ] Real material/face-to-face contact **NOT IMPLEMENTED** in this first AABB screening pass.
- [ ] Gap and axis screening heuristics have false positives and are diagnostic-only.
- [ ] Any unhandled/partial scan is shown as `PARTIAL_UNVERIFIED` or `ERROR_UNVERIFIED`.
- [ ] No performance regression above baseline without justification.
- [ ] Commit SHA and diff can be independently inspected on GitHub.

## F. Decision

```text
CODE_COMPLETE=NOT_VERIFIED
CG2_AABB_SCREENING=NOT_TESTED
CG2_EXACT_MATERIAL_CONTACT=NOT_IMPLEMENTED
PRODUCTION_REGRESSION=NOT_VERIFIED
READY_FOR_CHATGPT_REVIEW=NO
READY_FOR_MERGE=NO
```

## G. Publish actual report on GitHub

1. Complete code review, builds and CAD tests; link exact commands, timestamp and evidence.
2. Replace each template field with verified facts; leave missing cases `NOT_TESTED`.
3. Commit/push this report on `feat/check-hole-cg2-part-relations`.
4. Leave PR Draft; do not merge CG1 or CG2 without user authorization.
5. Return **code SHA, report SHA, PR link, remaining blockers**. ChatGPT reviews source itself, not just this report.
