# RUNBOOK 48 — BOTH-FRAME REAL SOLID CUT PREVIEW P1/P2 — ANTIGRAVITY COPY ONLY

## Scope and integrity

- **Code owner and C# author: ChatGPT. Implementer: Antigravity (exact COPY/PASTE only).**
- **Original worktree:** C:\Tao moi\ADDIN. Do not change/check out its local branch.
- **Source branch:** feat/afj-both-frame-real-solid-cut-candidates-20261021.
- **Confirmed user rule:** BOTH Frame A and Frame B require material removal.
- **Missing manufacturing details:** The actual Web/Wall/Lip/protruding-lip cut boundaries, which bisector orientation to use, and drawing overrides are still not verified. DO NOT assert that P1/P2 are approved manufacturing cuts. They are intentionally labeled provisional candidate cuts.

## Four exact Code Owner C# files to paste DIRECTLY into existing classes

All paths are relative to C:\Tao moi\ADDIN. Use the **full source** at each link and make no additional class or .csproj.

1. Commands/AutoFrameJoint/AutoFrameJointGhostPreview.cs
   - BEFORE blob: 6c5eaf44b3c1bf7d57b823a6d67a6129ddf049d8
   - AFTER blob: dff0c7ad4ab4b6d88ff6379994e2f3bbbba0aaa1
   - https://github.com/tbcphutai-bit/ADDIN-SolidWorks/blob/feat/afj-both-frame-real-solid-cut-candidates-20261021/Commands/AutoFrameJoint/AutoFrameJointGhostPreview.cs
2. Commands/AutoFrameJoint/AutoFrameJointGhostSessionManager.cs
   - BEFORE blob: b85515701921d62a86417eb2d49c166d4e9ba769
   - AFTER blob: 8a852954cbb9914555e21d58396c229ce70c655c
   - https://github.com/tbcphutai-bit/ADDIN-SolidWorks/blob/feat/afj-both-frame-real-solid-cut-candidates-20261021/Commands/AutoFrameJoint/AutoFrameJointGhostSessionManager.cs
3. Commands/AutoFrameJoint/AutoFrameJointCommandEntry.cs
   - BEFORE blob: 6ca5ab2731964e8c483adfee0b6073f33dbdf5ed
   - AFTER blob: c047d326a7b08dfee4ba87a9a069d3f8a9305bb8
   - https://github.com/tbcphutai-bit/ADDIN-SolidWorks/blob/feat/afj-both-frame-real-solid-cut-candidates-20261021/Commands/AutoFrameJoint/AutoFrameJointCommandEntry.cs
4. UI/AutoFrameJoint/AutoFrameJointSelectionPage.cs
   - BEFORE blob (Runbook 47): f64d9eb6bc802e1317bad01f2257405d074d4c30
   - ALTERNATIVE BEFORE (Runbook 43, only if UNMODIFIED): 4dcfe9fd835b5c0acc45bef351f5ae3e9c41701a
   - AFTER blob: 3557e67b92e5d78434b79e34d48f775b7bbfebb3
   - https://github.com/tbcphutai-bit/ADDIN-SolidWorks/blob/feat/afj-both-frame-real-solid-cut-candidates-20261021/UI/AutoFrameJoint/AutoFrameJointSelectionPage.cs

The fixed math file AutoFrameJointGeometryMath.cs is unchanged at SHA c7750638196642927267734e9e81e2aaff99fe7a. Original Check Hole, Check Bend, Omote, Mirror, DXF, core add-in code, ADDIN.csproj and Debug x64 OutputPath MUST stay untouched.

## Implemented C# behavior

UI:
- Keeps native green OK, red Cancel and explicit Kết thúc (không cắt Part) from Runbook47, A/B selection, automatic read-only Analyze, and original Xem trạng thái A/B.
- Adds **Preview cắt thử A+B — P1** and **Preview cắt thử A+B — P2**, with clear candidate/unverified tooltips. No Boolean on native selection callbacks.

Backend:
- Explicit user click only, original Assembly current, two DISTINCT top-level visible/unsuppressed Part occurrences, loaded Parts with matching active referenced configurations; exactly ONE source solid per Part.
- GetBodies3 -> Copy -> GetTotalTransform(false) -> ApplyTransform to assembly world space, on TEMPORARY solids only.
- Read provisional joint XYZ and candidate plane normal from already existing algorithm. Require FINITE_AXES_NEAREST, nearest points inside spans; no invented plane when unsupported.
- Boolean SWBODYINTERSECT of actual temporary WORLD B-reps. Intersection is an actual solid, not mere overlap of bounding boxes.
- Partition actual intersection into positive/negative portions of P1 or P2 using temporary box bodies created by IModeler.CreateBodyFromBox3.
- SWBODYCUT on temporary copies of A and B separately; BOTH need positive removed material. Verify partition and each final kept B-rep volume approximately conserved.
- Render actual candidate surviving solids and removed solids as temporary Display3 bodies, shifted in WORLD-Z by +120 mm to show alongside untouched original parts. Different colors indicate kept and removed.
- Store ALL displayed temporary bodies in existing Ghost Session; after Finish, Cancel, or next preview, Hide and release them. If Hide fails, do not lose references; report pending cleanup.
- NEVER write Part, Assembly, FeatureManager, save/SaveAs, mate, config, or output DLL/PDB.

Safety and scientific caveats:
- P1/P2 are the two *theoretical* bisector normals of ESTIMATED frame design axes. Thus P1/P2 are **visual candidate geometry only**, NOT verified Web/Wall/Lip production trim. The actual drawing/profile-specific rule must be checked separately.
- If true body intersection is absent, candidate halves are empty, Boolean reports errors, volume conservation fails, or unsupported component context is encountered: fail CLOSED; never show a fabricated cut.
- SOLIDWORKS API Boolean may fail on tiny or non-manifold geometry; capture errors; do not retry with guessed tolerances.
- .NET Framework 4.8 / SOLIDWORKS 2024 COM compilation and host behavior have NOT been validated here. This is source-only static review.

API references:
- Official Operations2 remarks: operands are invalidated after Boolean; copy beforehand:
  https://help.solidworks.com/2011/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IBody2~Operations2.html
- Official CreateBodyFromBox3 nine doubles, face center, axis, width, length, height:
  https://help.solidworks.com/2016/english/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.IModeler~CreateBodyFromBox3.html
- Official Display3 temporary body and top-level component restriction:
  https://help.solidworks.com/2023/english/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.IBody2~Display3.html

## Instructions for Antigravity: JUST COPY, DO NOT CODE

1. Work in current C:\Tao moi\ADDIN. STOP other agents editing these four classes. Do NOT checkout, reset, stash, merge, clean, or replace .csproj.
2. Read each of the four existing local files and run git hash-object -- PATH. Match the BEFORE blob exactly. For UI SelectionPage, either the Runbook43 or Runbook47 BEFORE SHA is accepted. All other mismatches => STOP and report to ChatGPT; NEVER overwrite concurrent changes.
3. Copy the full four ChatGPT-authored GitHub C# classes into those exact four original local paths. NO extra .cs files, NO independent coding, NO redesign, NO format changes.
4. Verify four AFTER blob SHAs exactly; if BOM/CRLF or concurrent differences explain mismatch, STOP and report actual sha/diff rather than manipulating project state.
5. Check git diff --check, git diff --stat, git diff for the four only and current project status. Confirm ADDIN.csproj unchanged; original Debug x64 output C:\Tao moi\ and empty Pre/PostBuild remain.
6. Confirm TryShowCutCandidate exists in GhostPreview, GhostSessionManager and CommandEntry connection; both PMP button IDs P1/P2 dispatch; original AfterClose still performs preview cleanup.
7. Report file-by-file BEFORE/AFTER SHA, diff summary, other files touched BY THIS TASK (should be zero), and SOURCE COPIED — NO BUILD TESTED.

**HARD STOP:** Do NOT Build/Rebuild/F5/Deploy, write DLL/PDB, close/restart SOLIDWORKS, commit/push/merge local WIP, alter Check Bend/Check Hole or run automated COM/cutting operations.

## Manual host acceptance — only when user personally builds after review

1. Back up currently deployed ADDIN.dll / ADDIN.pdb at original output C:\Tao moi\ before manual x64 Debug build.
2. Confirm original A/B selection and automatic analysis remains correct; Xem trạng thái A/B remains original-copy overlay.
3. Click P1: if actual overlap exists, expect two temporary after-cut bodies and colored removed material, shifted WORLD-Z +120 mm; UI reports both removed volumes in mm³ and warns unverified.
4. Click P2: old temporary bodies must hide and the second alternative candidate appears.
5. If solids do not intersect, candidate geometry is unverified or Boolean fails: refuse to show a fake cut, display reason and debug log.
6. Test red X, green OK and Kết thúc (không cắt Part). All ghosts clean, source Part/Assembly unchanged and command can reopen; collect [AFJ-CUT-CANDIDATE], [AFJ-GHOST], and [AFJ-PMP].
7. Verify that NO real part Cut Feature, Save or assembly mate has been created.

**STATUS: COMPLETE SOURCE HANDOFF FOR TWO REAL B-REP MATHEMATICAL CANDIDATES. BUILD NOT TESTED, HOST NOT TESTED, MANUFACTURING RULE NOT VERIFIED.**
