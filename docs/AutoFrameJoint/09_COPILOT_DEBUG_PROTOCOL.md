# 09 — Autonomous debugging protocol (Copilot VS Agent)

## Direct answer
GitHub Copilot Agent in Visual Studio **can often** invoke terminal/build/test tools and fix compiler/unit-test issues in loops **when the local VS installation and permissions allow**. It is **not guaranteed** to control SOLIDWORKS windows, attach to every COM callback, reproduce an interactive Assembly selection or validate manufacturing cuts unattended.

### Debug hierarchy
1. **Static/compile**: parse and build under original target framework + C# language version. Auto-fix compiler errors inside allowed paths only.
2. **Unit**: run `AutoFrameJointResearch.exe --self-test`, capture exit code; add regression test before fixing geometry bug.
3. **Trace/algorithm**: print A/B axes, lengths, transforms, KCC applicability, determinant, t/u, status; guard against nonfinite values.
4. **Integration in SOLIDWORKS (future approved phase)**: run on local machine with SW installed, open **working copy** of Assembly, select test pair, invoke **read-only** analysis; capture Output/Trace and exception.
5. **Design validation**: overlay end cuts and check protected lips & winner/loser with reference models. Human must approve ambiguous outcomes.

### Agent loop (mandatory)
```text
1. Read the failing compiler output or test.
2. Identify the concrete error and relevant file/line.
3. Add a reproducing minimal test if it is a logic bug.
4. Change only the smallest permitted files.
5. Build again; rerun all tests, not just the failing one.
6. Record exact commands, exit codes and before/after result.
7. If blocked or still failing after 5 isolated attempts, STOP and report BLOCKED.
```

### Error classifications
| Code | Example | Copilot action |
|---|---|---|
| BUILD_ERROR | C# compile/error CSxxxx | fix and rebuild |
| TEST_FAILURE | wrong intersection angle/winner | reproduce in test, fix |
| COM_API_VERSION | Interop member differs across SW versions | inspect local Object Browser/type library, do not invent method |
| COM_RUNTIME | RPC disconnected, model state invalid, stale RCW | diagnostic and ask for host run if needed |
| SW_SELECTION | wrong Component/Configuration/Occurrence | read-only log from user's SW session |
| CAD_TOPOLOGY | invalid B-rep, unstable section | mark UNSUPPORTED or NEEDS_REVIEW |
| RULE_UNKNOWN | lip preserve vs trim not proven | no auto-cut; request design approval |
| USER_ENVIRONMENT | no MSBuild/SW installed or denied permission | report exact blocker |

### Mandatory diagnostics for later SOLIDWORKS adapter
UTC timestamp; command version; Part path normalized/redacted for reports; component occurrence and configuration; selected entity type; body count; local->assembly transform; geometry units; axis candidate confidence; detected angle (acute vs interior); declared KCC scope; winner decision source; why analysis was rejected; stack trace.

### User's minimum manual steps (only if required)
- For isolated kernel: ideally **none** after Copilot has execution permission.
- For real SOLIDWORKS COM behavior: user may need to **open a copy of the Assembly**, choose two frame instances, click **Analyze** in a future read-only command, and share Trace/error output.
- User does NOT need to debug math line by line. Copilot can work from build logs/tests/diagnostics. But no Copilot response replaces actual SOLIDWORKS validation.

### Hard stop constraints
- Never auto-apply geometry cuts.
- Never delete/suppress a feature to 'make tests pass'.
- Never force reset/rebase branch or discard uncommitted changes.
- Never claim successful Debug/Build/Test without the tool output.
- Treat an uncertain corner rule as `NEEDS_REVIEW`, **not** as success or arbitrary default.
