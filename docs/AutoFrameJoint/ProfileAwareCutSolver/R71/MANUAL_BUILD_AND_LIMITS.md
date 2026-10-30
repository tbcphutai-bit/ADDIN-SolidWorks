# Source integration and manual rebuild

The exact four-corner API-qualified source was copied into the main `Commands/AutoFrameJoint/AutoFrameJointGhostPreview.cs`. Its SHA256 is `B29462871487F1F8D802DAA1F08EA81A4B375489ED542EB6D9D989C5C8730766`.

The other five AFJ source files, main project, deployed DLL and PDB were checked unchanged. No main Add-in Build/Rebuild/F5/Deploy was performed. The independent diagnostic DLL is not a deployable product build and is not uploaded.

1. Close the running Add-in/SOLIDWORKS debug session before your manual rebuild so an old loaded DLL is not mistaken for this source.
2. Open your existing Add-in solution and rebuild it yourself using your existing output/post-build settings. No project/output changes are required by R71.
3. Check the loaded DLL path and MVID in your new debug log. Its MVID must reflect your rebuild; it is not the diagnostic module MVID in the API report.
4. Test the normal A/B selection and Preview/apply workflow on disposable CAD first. The API run here called the production candidate classes directly; it did not exercise the rebuilt installed DLL or native PropertyManager callbacks.

The existing Preview palette and lift off the physical frame plane are retained. Actual rendered visibility and user-interface smoothness are UNVERIFIED in this R71 qualification. Per-corner native-apply times are recorded, not presented as a speedup: approximately 26.4, 47.0, 34.7 and 65.4 seconds in this instrumented cumulative run.

The current rule qualifies the four recorded orthogonal member pairs and their measured folds. It does not establish arbitrary member-axis angles, every fold topology, or terminal Bend projection beyond the admitted 90-degree sector. Unsupported cases must return `UNVERIFIED_RULE`. The area of exposed angled-profile edges and general failure-path rollback remain UNVERIFIED. Review the optional completed-reference differences before treating the new cuts as an exact replacement of the authored finished sample.

No original CAD was saved or uploaded. The four-corner fixture restores selected stock only on copies, suppressing explicitly reviewed Cut IDs and qualified dependent features and bounding the BaseFlange D1. Both ends of fr1/fr4 are selected; no preserved far-end claim applies to those two fixture members. All 24 original CAD hashes and snapshot/copy disk hashes remained unchanged. Copies were closed without saving.
