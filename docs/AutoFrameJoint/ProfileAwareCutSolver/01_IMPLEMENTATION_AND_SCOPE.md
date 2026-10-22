# Implementation and preserved entry points

| Existing class | Change |
|---|---|
| AutoFrameJointGhostPreview | Replace arbitrary bisector/overlap candidates in TryShowCutCandidate with measured profile → parameterized rule → separate ordered A/B plans → copied-body Boolean → validation → existing Display3 lifecycle. Append internal helpers in the same compiled file. |
| AutoFrameJointSelectionPage | KEEP and KEEP/CUT labels/tooltips/status wording only. Preserve native A/B selection, control IDs 107/108, deferred QueueCutCandidate/PerformCutCandidate callback. |
| AutoFrameJointGhostSessionManager | Documentation wording only; token/assembly/STA ownership and TryShowCutCandidate delegation preserved. |
| AutoFrameJointCommandEntry | Documentation wording only; Open and onPreviewCut wiring preserved. |

`AfjProfileAnalyzer.Analyze` reads SMBaseFlange's parent Sketch, SheetMetal t/R, native axis and folded cylindrical Bend faces. A unique Web is found from connectivity and parallel same-side Walls, so a tall Wall does not become Web merely because it is longer. Unsupported topology is refused.

`AfjJointRuleLibrary.Create` dispatches the measured family to `AfjParametricRules.Generate`. It contains no fixture dictionary. `AfjJointSolver.Solve/SolveBodies` measures actual longitudinal spans, selects shorter covers, executes independent ordered plans and checks required material coverage. `AfjTemporaryBoolean` owns disposable Copy operands, prism tools, Check2/volume/conservation checks. `AfjCutOperation`, `AfjCutPlan`, `AfjJointPlan` retain ordered operations, evidence IDs and separate A/B results.

Existing `Open`, session `TryShowCutCandidate`, Ghost `TryShowCutCandidate` and UI callback remain. KEEP and KEEP/CUT use one plan; the second mode adds colored removed material. Original-body TryShow, top-level visibility/suppression checks and cleanup are retained. Bodies are displayed at world Z +120 mm using the existing component transform path.

No new command, production feature, source CAD save, configuration switch, rollback or rebuild is called by the solver. Private-copy rollback belongs only to the detached research harness. No project file was changed.
