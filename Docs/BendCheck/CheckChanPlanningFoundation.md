# Check chan – sequence-draft integration

This branch extends the EXISTING Check chan command and retains Check Min.
No new menu command, WinForms controls, Designer changes or COM add-in
registration changes are introduced.

The Check chan scan now builds a managed BendCheckPlanningDraft from the same
BendRow list already captured for minimum-edge evaluation. Candidate order
permutations are emitted to Visual Studio Debug Output with the prefix
[BEND PLAN DRAFT] and are ALWAYS marked CHUA_XAC_MINH.

The model supplied by the customer is a local proprietary SLDPRT.
Do not commit that binary file to this public repository.

Windows validation for Codex:
1. Compile this branch using the existing C# 7.3 / .NET Framework 4.8 solution.
2. Open the test native Sheet Metal SLDPRT in folded state in SOLIDWORKS.
3. Open the EXISTING Check chan command. Do not look for a new button.
4. Compare bend count, feature names, angles, R and thickness in Visual Studio
   Debug Output [BEND PLAN DRAFT] against the model and Check Min.
5. For two recognized bends the draft must list exactly two reverse orders.
   Do not assume the sample Part has two bends; verify actual count first.
6. Change/rebuild the model and verify existing stale-data invalidation.
7. Compare Check Min behavior against main: it must not change.

There is deliberately no Check Max PASS, tool choice, 2D dynamic simulation
or verified bend sequencing yet. Candidate orders are NOT shop-floor instructions.

Next: integrate verified original company DXF punch geometry; avoid treating
the schematic clamp CAD as known mating geometry. Then evaluate intermediate
folded states and tool clearance before considering any order feasible.
