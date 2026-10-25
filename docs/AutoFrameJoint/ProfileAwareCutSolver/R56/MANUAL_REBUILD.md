# Manual Rebuild

The agent has not built/rebuilt/deployed the main Add-in and has not changed project output paths, the project file, DLL or PDB.

1. End the old Visual Studio debugging run and close old SOLIDWORKS sessions so they release the previous loaded Add-in DLL. Reload the externally edited Ghost source in Visual Studio when prompted; confirm the editor is showing the updated file.
2. Confirm the existing main `Commands/AutoFrameJoint/AutoFrameJointGhostPreview.cs` hashes to `09f0e9708a11ce962e2ee70348f6d5cae5a57269f8754545e8a0768637af748d`. Preserve the other project files and existing A/B/UI wiring.
3. Perform your normal manual Rebuild using the project's existing configuration/output/deployment setup. This document does not authorize an automatic build or deployment by the agent.
4. Load that manually built DLL through your existing workflow. In Output, confirm `[AFJ-R56] SOURCE=SECTION_GRAPH_ADJUSTED_BISECTOR_LINKED_FACE_SKETCH plane_fallback=DISABLED MVID=...`. Record the actual loaded module path/MVID and compare it with your new build. A source edit alone does not update the loaded DLL.
5. Use the existing Auto Frame Joint command and A/B selections on clean test copies. Parts already cut during older tests can retain earlier AFJ Planes, Sketches and Cuts; this source preserves those existing features rather than deleting them. Inspect newly created Preview/Cut/Sketch results, confirm zero new Planes and fully constrained Face2 Sketches, and do not count old baseline Planes as new R56 creations.
6. Detailed internal counters are disabled by default. For a deliberate diagnostic session, set process environment `AFJ_R56_PROFILE_DETAIL=1` before starting SOLIDWORKS. Existing R50/R52 boundary logs remain available without that switch.

Native harness PASS does not establish installed UI selection, Preview visibility, smoothness, Apply latency or freedom from freezing. Record those separately after your manual build/load.
