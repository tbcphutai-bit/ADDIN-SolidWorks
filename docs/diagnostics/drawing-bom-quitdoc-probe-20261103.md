# CHECK DRAWING BOM: owned read-only Drawing close probe (2026-11-03)

## Result from prior managed-only test

- Active DLL confirmed by `[DRAWING SCOPE MANAGED BEGIN] nativeSubclass=OFF redrawBlock=OFF`.
- Batch processed the sixth Drawing and produced a comparison result.
- Rows 1–5: `CloseDoc CALL` then `CloseDoc RETURN`.
- Row 6: `CloseDoc CALL title=4331-04-TI-p1-c1 - 6` then native `0xC0000374`, no `CloseDoc RETURN`.
- Therefore Windows subclassing was *not necessary* for this failure. It does not mean that `CloseDoc` itself created the heap corruption; earlier operations may have damaged memory.

## Precisely one runtime A/B change

In `CheckDrawingBom.RunBatch` cleanup:
- For Drawing opened by this command only (`openedByCommand == true`), verify exact document path and read-only status.
- If verified, call `ISldWorks.QuitDoc(title)` instead of `ISldWorks.CloseDoc(title)`.
- If unverified, log `QuitDoc SKIPPED unsafe target` and leave it open for manual review.
- No change to checks, work-copy logic, silent implementation, UI or project file.

`QuitDoc` discards changes; this is why the ownership/path/read-only guards are mandatory.
This is **not** a verified fix, **not** a universal substitute for `CloseDoc`, and might hit the same crash. Do NOT merge before tests.

## Why the window still appears

The managed-only prior branch deliberately removed redraw blocking as an A/B test. Further, `CheckDrawingBom.OpenDrawingDocumentSilent` explicitly executes `DocumentVisible(true, swDocDRAWING)`, and `ActivateDrawingForDisplayRead` calls `model.Visible = true` and `ActivateDoc3`. SOLIDWORKS' `swOpenDocOptions_Silent` means *dialog suppression*, not a guarantee of invisible windows.

The existing logic reads linked title-block values and comments that invisible Drawings return blank display values. Blindly changing `DocumentVisible(true)` to `false` will break correctness. Genuine no-window checking requires another architecture that can resolve linked note values without activating the Drawing (or a separately controlled worker session, if separately authorized). Do not hide the entire host SOLIDWORKS application or create a second instance without approval.

## Handoff

1. Back up local files; ensure there are no uncommitted changes that would be overwritten.
2. Compare this branch against `fix/drawing-silent-managed-only-diagnostic-20261103`.
3. Apply **only** the cleanup hunk of `Commands/CheckDrawingBom.cs`. The rest is inherited and unchanged.
4. Do not auto-build/deploy or push from Antigravity.
5. On user-authorized build, validate Debug x64, old DLL output location and loaded binary.
6. Run on copies of the same 12 Drawing and Part files, capture `[BATCH CLEANUP]` and final result.
7. If `QuitDoc CALL` lacks `QuitDoc RETURN`, **stop re-running** and capture native crash dump + stack. Analyze COM lifetime of flat-side copy and `CheckPartWorkingCopy.Dispose`.
8. Check no source modifications; if `QuitDoc SKIPPED`, close only after inspecting why the guard failed.
9. Do not call this true silent; that requirement remains open.
