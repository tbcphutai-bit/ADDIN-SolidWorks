# R68 — Preview retains its palette with A/B selected

Source update: [commit 9df4c5e in Draft PR45](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/commit/9df4c5ec8284abd7a91d90ce3d2dcbb8286d6156). This PR39 update contains reports only.

The user's latest DLL already contains R66 material assignments. This is a rendering-context bug, not another stale-DLL finding. With original A/B components selected, their selection highlight also shades temporary bodies displayed in their component context blue. Setting temporary body/face materials, DisableHighlight, Copy2(false), or repainting after Display3 did not remove it.

The existing GhostPreview now uses a resolved, visible, non-selected top-level occurrence as its render coordinate system. Each temporary body is transformed `world + common lift → inverse(render occurrence)`. It does not borrow or modify that occurrence's geometry. The original A/B marks, selection callbacks, native materials, view preferences and Cut Plan are unchanged. No component, command, plane or native feature is added.

Cleanup stores the actual display Part and Component and calls Hide in the matching context. Failed Hide references remain owned. Cache reuse verifies the render occurrence's selection state, identity, visibility, suppression, configuration and transform; an invalid context causes redisplay. Both the original-body Preview and KEEP/CUT paths use this mechanism.

## API evidence

Private copies of the existing 4332 Assembly were used; raw CAD and rendered CAD images remain local.

| Observation | Purple pixels | Yellow pixels |
|---|---:|---:|
| R66, A/B selected | 0 | 0 |
| Independent display occurrence, A/B still selected | 347 | 1613 |
| Final source, P1 | 345 | 1679 |
| Final source, P2 | 325 | 1674 |
| Final source, P1 again | 347 | 1680 |

The final-source checks retained the exact A/B native identities and marks 1/2 throughout. For both displayed KEEP bodies, converting back through the actual display occurrence and removing the lift gives symmetric B-rep difference **0 m³** against the approved Preview. The completed preceding run also passed camera/lift/cache/Hide checks, restored its blank host and preserved source/copy file hashes. See [machine-readable evidence](COLOR_API.json).

An additional native PMP probe is **not PASS**: the harness incorrectly assumed selecting B would not automatically invoke Preview, then canceled before its explicit button action. Native AfterClose returned, but genuine idle/onClosed completion was not observed. This must not be counted as an installed command test. The color assertions above precede that auxiliary failure; the earlier completed Preview-only run supplies the complete cleanup/hash checks. No physical button click or rebuilt-DLL smoke test is claimed. The failed auxiliary probe was explicitly aborted as FAIL after confirming native Cancel had returned and no native command remained active; its own cleanup restored a blank host and verified all source/copy hashes unchanged. No idle event or PASS was fabricated.

## Main-source integration

Only `Commands/AutoFrameJoint/AutoFrameJointGhostPreview.cs` was copied into the main source, after checking the original source hash and backing it up locally. Source SHA-256: `B56D83128310C0A3AF0549507B6E371FEC28C160888147A78EDB9CA73864A095`.

The installed DLL is unchanged: MVID `b83932a1-dd5c-4e2d-920c-769c0a0740bd`, SHA-256 `51EA97F15BFD260D4E34FF2ADDB332E1840180619B0E244D4ABDEED7A4F56F86`. It contains R66, not this R68 source update. **The user must rebuild using the existing output/deployment configuration.** No project, output path, DLL or PDB was changed. After rebuilding, confirm `[AFJ-R68-COLOR] ... independent_from_selection=True` in the existing command log, and inspect the purple/yellow Preview while A/B remain selected.

## Limits

- If every usable occurrence is selected, including an Assembly containing only the two selected Frames, there may be no independent display context. Geometry Preview remains available and explicitly reports `COLOR_UNVERIFIED`; color success is not claimed for that fallback.
- The solver/native-writer portion of GhostPreview.cs is text-identical to the prior source after newline normalization. No new manufacturing rules, Cut qualification or native Cut performance result is claimed.
- Ghost palette: KEEP A purple / B yellow; CUT A red / B blue. The pixel gates here verify KEEP purple/yellow; they do not independently isolate the blue CUT pixels from the blue scene background.
- CAD files, CAD bitmaps, binaries, full local logs and private paths are excluded from GitHub. No merge was performed.

Primary API reference: [IBody2.Display3](https://help.solidworks.com/2020/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IBody2~Display3.html). The selection-color cause and the neutral-context behavior above are measured host observations, not claims made by that documentation.

## Final retry and stability limit

A bounded final retry in the same separately owned, previously used test process crashed while `OpenDoc6` was opening the second copied selected Part, **before GhostPreview or the native PMP test ran**. The process exit code was `-1073740940`; Windows recorded `0xC0000374` in `ntdll.dll`. Cause is **UNVERIFIED**; the process had earlier diagnostic assemblies and the installed background Add-in loaded. The crash cannot be used as a color PASS or attributed to a specific component without further evidence.

The parent audit completed disk checks: all 19 original fixture files and all 19 copied files retain their expected SHA-256 values. No save, main Build or deployment occurred. The owned process exited; the preexisting user process was left untouched and the original Add-in startup setting was 1. The color-only source remains API verified as above, while complete installed-command stability remains **UNVERIFIED**. Further API retries were stopped.
