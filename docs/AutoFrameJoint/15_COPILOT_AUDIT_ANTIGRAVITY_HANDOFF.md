# Copilot local status audit + Antigravity handoff

## Known from GitHub, NOT inferred from VS local folders
Current remote heads checked during this work:
- docs/auto-frame-joint-phase1-20261019 = e496ba37759a90a3f94d2d22e9caa9d8d75af243
- fix/auto-frame-joint-finite-inputs-20261019 = 3445b68c961a3879a196b8ee0b1921558a498c2b
- feat/auto-frame-joint-readonly-probe-20261019 = 0e4ab4516536ae30e97322ac91319378ee2ab4c6
- feat/check-hole-connection-groups-cg1 is ANOTHER ongoing feature.
These do not show whether Copilot modified or successfully built uncommitted files on Windows.

## First action: stop simultaneous editing
Stop any currently active Copilot Agent working on AutoFrameJoint. Save editor buffers, but DO NOT delete/undo changes or run git reset, git clean, git stash without consent. Keep Check Hole isolated.

## Exact read-only audit commands (Developer PowerShell)
Run for the two candidate folders from user screenshots; if actual paths differ, change them:

    $dirs = @('C:\Tao moi\ADDIN', 'C:\Tao moi\ADDIN-AutoFrameJoint-Phase1')
    foreach ($dir in $dirs) {
      Write-Host "==== $dir ===="
      if (-not (Test-Path -LiteralPath $dir)) { Write-Host 'NOT_FOUND'; continue }
      git -C $dir rev-parse --show-toplevel
      git -C $dir branch --show-current
      git -C $dir rev-parse HEAD
      git -C $dir status --short --untracked-files=all
      git -C $dir diff --name-status
      git -C $dir diff --cached --name-status
      git -C $dir log -5 --oneline
      git -C $dir remote -v
    }

Then inspect local research files and local status:

    $root = 'C:\Tao moi\ADDIN-AutoFrameJoint-Phase1'
    Get-ChildItem -LiteralPath (Join-Path $root 'tools\AutoFrameJointResearch') -File |
      Select-Object Name,Length,LastWriteTime
    git -C $root diff -- tools/AutoFrameJointResearch
    git -C $root diff --cached -- tools/AutoFrameJointResearch

Do not post file contents, customer Part names or local absolute paths to public GitHub if confidential. Give the user the local report first.

## Audit findings must distinguish
- Saved on remote: committed & pushed.
- Saved locally: file or commit in local repo, not necessarily pushed.
- Built: ONLY if exact MSBuild output and exit code found.
- Tested: ONLY if exact test output, exit code and timestamp found.
- Works in SOLIDWORKS: ONLY after real host-run of integrated Add-in.
The last screenshots showed a previous SELFTEST_EXIT=1 and FrameJointGeometryTests.cs under Miscellaneous Files, so neither PASS nor failure to compile latest source can be assumed. The VS status bar branch could differ from the separate terminal working directory.

## Required local report
Title: COPILOT_LOCAL_STATUS (LOCAL ONLY until user permits upload)
Fields:
- Time of inspection
- Path aliases / current branches / HEADs for both repos
- git status short + staged + unstaged + untracked paths
- Changed AutoFrameJoint files, any Copilot code not committed
- Last VERIFIED build and test command / actual output / exit
- Which changes need preservation
- Safe to proceed with UI preview? YES / NO / UNKNOWN
- Any blocker accessing the isolated clone

## Exact Antigravity execution order
1. Perform the above audit; if cannot access Visual Studio local folder, say UNKNOWN and ask user to paste the audit output. Do NOT claim the Copilot work is lost.
2. Read docs/AutoFrameJoint/14_ANTIGRAVITY_UIUX_PLAN.md and existing UI/TaiTheme.cs, ModernButton.cs, ModernCard.cs, ModernTabControl.cs.
3. Prepare **three per-tab preview screenshots**, plus empty/error variant; do NOT change source.
4. User reviews preview, explicitly says OK UI.
5. Only then create UI-only Form/Designer/ViewModels in own worktree; never modify Add-in backend/API/event handlers.
6. Produce ANTIGRAVITY_UI_REVIEW.md (locally first), diff --stat, accessibility/DPI screenshots, and testing evidence. Ask before committing and pushing.

## Anti-only workspace scope
Allowed after user approves: UI/AutoFrameJoint/** and UI review report.
Forbidden always unless code owner gives separate change request: SWAddin.cs, ADDIN.csproj, BomTaskPaneControl*, Commands/**, HoleManagement/**, Check Hole/Bend backend, existing event handlers, source CAD or drawing files.
No fake success indicators. Preview 3D and Apply Cut remain disabled until future validated solver and separate approval.

## Prompt for Antigravity (copy and send)
You are SOLIDWORKS-UI-DESIGNER (C# WinForms). Read docs/AutoFrameJoint/14_ANTIGRAVITY_UIUX_PLAN.md and this file. First audit local Copilot changes using read-only git commands, preserving all WIP. Then design EXACTLY THREE tabs: Chọn Frame, Phân tích, Phương án cắt. Make a full preview for each tab at 100% and 150% DPI, including disabled Cut and warning states. Match TaiTheme/ModernButton/ModernCard. Do NOT edit source or push until I explicitly say OK UI. Never modify SWAddin, ADDIN.csproj, taskpane Designer, backend/API, events, Check Hole or Bend. After preview, report source refs, local audit findings, screenshot paths and unresolved UX questions.
