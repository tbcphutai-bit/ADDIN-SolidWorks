# Source handoff and manual rebuild

Only `Commands/AutoFrameJoint/AutoFrameJointGhostPreview.cs` was copied into the main source. Its exact tested local SHA256 is `93B73678BB508BD7CFB68BAF8984AFB5316CE3759959574A4640678E5C273543`. Five other AFJ sources, the project and installed DLL/PDB kept their frozen hashes. The repository branch's six-source combination also compiled independently; its preserved UI baseline has minor differences from the main-local API baseline, recorded in SOURCE_INTEGRATION.json.

The existing main DLL remains MVID `da813a56-ec88-45be-b43c-8895306d3c23`. It does **not** contain this source update. No output/deployment path was changed.

1. End the current Add-in command and stop the user's debugger/SOLIDWORKS session as needed for their normal build process.
2. Rebuild the user's existing Add-in project manually with its existing configuration and output/deployment setup.
3. On the next actual Preview call, check the new `[AFJ-R72] SOURCE=PLANAR_MEASURED_SHEET_NORMAL_BLIND` marker and loaded DLL MVID. The MVID will belong to the user's new build, not to the diagnostic DLLs.

Native tests used copies and the owned STA SOLIDWORKS API session. They did not execute the installed DLL/PMP callbacks after the user's rebuild. Do not infer rendered Preview visibility, UI smoothness or speedup from these tests. All exposed edges are not automatically guaranteed covered; the solver retains coverage warnings. The full-annulus secondary clearance certificate is bounded to measured Ri = fit gap and the stated complete retained-edge geometry. Terminal turns beyond the admitted sector and other unsupported geometries remain UNVERIFIED_RULE. Earlier failed-run rollback remains UNVERIFIED. No CAD save/reopen roundtrip was added to this qualification.

The 4458 fixture preparation is a copy-only research setup; production never applies those suppression/D1 preparation operations. Exact authored completed-shape reproduction is not claimed. Source references and per-Cut native B-rep equality remain mandatory.
