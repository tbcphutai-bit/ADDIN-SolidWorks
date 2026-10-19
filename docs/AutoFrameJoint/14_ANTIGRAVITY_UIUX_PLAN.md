# Auto Frame Joint – Antigravity UI/UX Plan (review before editing)

Base commit: 0e4ab4516536ae30e97322ac91319378ee2ab4c6
Dedicated documentation branch: docs/auto-frame-joint-antigravity-ui-plan-20261019
Status: DESIGN PLAN ONLY. Do not edit actual UI until user says OK UI.

## Goal and ownership
ChatGPT owns geometry/backend/API and code review. Antigravity owns the WinForms presentation layer only. The existing Check Hole, Bend Check, commands, API calls, event handlers, SWAddin and taskpane Designer are protected.
The new backend candidate currently implements only AutoFrameJointReadOnlyProbe.AnalyzeSelected(ISldWorks), a string report; there is NO cut solver nor 3D preview engine.

## Existing styles to reuse (read before implementing)
UI/TaiTheme.cs, UI/ModernButton.cs, UI/ModernCard.cs, UI/ModernTabControl.cs, UI/TaiDialog.cs; refer to UI/BendMinimumTaskPaneIntegration.cs for existing launcher pattern but DO NOT modify it.
Use TaiTheme accents, neutral surfaces, Segoe UI; 8–12 px spacing, 32–40 px minimum clickable height, clear focus and hover. Avoid new font files, image packs, heavy gradients and a second theme.

## Design decision
One new TaskPane command tile: Auto Frame Joint — Giao góc Frame.
It opens one modeless WinForms dialog (suggested 560 × 690 px nominal, min 460 × 560, resizable). SOLIDWORKS viewport remains visible. Avoid adding another global tab to the existing crowded TaskPane.
Top: logo, title, subtitle Assembly • Analyze only, prominent but neutral non-destructive state.
Middle: simple 3-step navigation.
Bottom: fixed footer and one primary CTA. The content scrolls vertically without horizontal scrollbars.
A collapsible Diagnostics section handles debug, not a 4th tab.

## Tab 1 — Chọn Frame
- Title: Chọn hai thanh trong Assembly.
- Two separate cards with A and B; show component occurrence name, part file and config only after actual input exists.
- Main action: Đọc 2 Component đang chọn. Requires exactly two selected Part occurrences in SOLIDWORKS.
- Swap A/B only in UI state, not in Assembly or mates.
- Empty: Chưa chọn; warning: Chọn đúng hai Part trong Assembly.
- Bottom primary action: Phân tích; disabled until real adapter confirms valid selection.
- DO NOT invent buttons to choose A and B independently as the current backend does not provide such API.

Wireframe, full tab:
    ┌──────────────── AUTO FRAME JOINT ────────────────┐
    │ Assembly • Analyze only                         │
    │ ① Chọn Frame     ② Phân tích     ③ Phương án     │
    │                                                 │
    │ Hai thanh Frame đang chọn                       │
    │ ┌ Frame A ────────────────────────────────────┐ │
    │ │ Chưa chọn | Part — | Configuration —         │ │
    │ └──────────────────────────────────────────────┘ │
    │ ┌ Frame B ────────────────────────────────────┐ │
    │ │ Chưa chọn | Part — | Configuration —         │ │
    │ └──────────────────────────────────────────────┘ │
    │ [Đọc 2 Component đang chọn]    [↔ Đổi A/B]     │
    │ Thông báo: không sửa Part hay Mate              │
    │                                     [Phân tích]  │
    └─────────────────────────────────────────────────┘

## Tab 2 — Phân tích
- Read-only summary cards per Part: Estimated Length, estimated Axis, Axis-support confidence, Long/Short role.
- A central joint card: acute UNDIRECTED axis angle, NOT the actual interior corner angle.
- Winner/Loser: CHƯA XÁC NHẬN until Drawing applicability/KCC scope is explicitly validated.
- Distinguish statuses EMPTY / RUNNING / ESTIMATED / NEEDS_REVIEW / UNSUPPORTED / ERROR.
- Never label estimated length as manufacturing length and never show a fake success icon.
- Diagnostics link expands details without taking focus away from the main result.

Wireframe, full tab:
    ┌──────────────── AUTO FRAME JOINT ────────────────┐
    │ ① Chọn Frame     ② PHÂN TÍCH     ③ Phương án    │
    │ ┌ Frame A ────────────────────────────────────┐ │
    │ │ Chiều dài ước lượng: — mm  | Vai trò: —       │ │
    │ └──────────────────────────────────────────────┘ │
    │ ┌ Frame B ────────────────────────────────────┐ │
    │ │ Chiều dài ước lượng: — mm  | Vai trò: —       │ │
    │ └──────────────────────────────────────────────┘ │
    │ Góc giữa hai trục: — °                          │
    │ Winner/Loser: CHƯA XÁC NHẬN                     │
    │ ! Web/Wall/Lip/Bend chưa được phân tích          │
    │ [Chi tiết kỹ thuật ▼]                            │
    │ [Quay lại]                         [Phương án →] │
    └─────────────────────────────────────────────────┘

## Tab 3 — Phương án cắt
- Real current state: solver not implemented. Show 4 rows Web (đáy), Wall (thành), Lip (gờ), Bend (bo R), each Chưa xác định.
- Reserve preview status area for a future SOLIDWORKS temporary-body display, but make Preview 3D disabled now.
- Apply Cut must be DISABLED both visually and at event-handler level, including in demo.
- Main allowed action: Copy analysis / Export diagnostic report ONLY after non-destructive backend integration is reviewed.
- User should not believe this tab already generates cuts.

Wireframe, full tab:
    ┌──────────────── AUTO FRAME JOINT ────────────────┐
    │ ① Chọn Frame     ② Phân tích     ③ PHƯƠNG ÁN     │
    │ Chưa có bộ giải vết cắt 3D                      │
    │ Vùng            Quyết định       Nguồn           │
    │ Web / đáy       Chưa xác định     —              │
    │ Wall / thành    Chưa xác định     —              │
    │ Lip / gờ        Chưa xác định     —              │
    │ Bend / bo R     Chưa xác định     —              │
    │ [Preview 3D: khóa]                              │
    │ [Xuất báo cáo]             [Apply Cut: khóa]     │
    └─────────────────────────────────────────────────┘

## Responsive & usability acceptance
- Screenshot/mockup EACH tab separately, showing empty, estimated and error states.
- Show 100% and 150% Windows scaling and narrow layout without clipping.
- Keyboard Tab order, Esc close, Enter only valid primary action. Clear disabled state and helpful errors.
- No placeholder model images presented as SOLIDWORKS geometry, demo numbers visibly labeled.
- Use one primary CTA per tab, no repeated action buttons.

## UI code ownership after user explicitly approves screenshots
Antigravity may create ONLY:
UI/AutoFrameJoint/AutoFrameJointForm.cs
UI/AutoFrameJoint/AutoFrameJointForm.Designer.cs
UI/AutoFrameJoint/AutoFrameJointForm.resx
UI/AutoFrameJoint/AutoFrameJointViewModels.cs
docs/AutoFrameJoint/ANTIGRAVITY_UI_REVIEW.md
Do not modify Commands/, ADDIN.csproj, SWAddin.cs, BomTaskPaneControl*, HoleManagement/, unrelated UI/backend/handlers. The code owner will separately integrate the TaskPane button and project Compile Include after review.

## Approval & deliverables
Stage A: present three concrete UI previews, no code edits.
Stage B: receive explicit OK UI; build UI-only prototype in isolated worktree, DEMO mode prominently labeled, no actual API/Cut binding.
Stage C: attach UI screenshots, control tree, diff, Git status, DPI review, and an accurate report. Ask before commit/push. ChatGPT reviews before code owner integrates.
