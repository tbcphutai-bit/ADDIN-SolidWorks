# 16 — C# WinForms UI contract for Antigravity (after preview approval)

Language: C# 7.3, .NET Framework 4.8, System.Windows.Forms, namespace ADDIN.UI.AutoFrameJoint.
Scope: UI ONLY; this is a contract/spec example, not an already-built form.

## Proposed Designer control names
- frm / AutoFrameJointForm : modeless Form
- lblTitle, lblMode, lblStatus : main header
- tabSteps : ModernTabControl; tabSelect, tabAnalyze, tabProposal
- lblFrameA, lblFrameB, lblConfigA, lblConfigB, lblLenA, lblLenB
- lblAxisConfidenceA, lblAxisConfidenceB, lblAxisAngle
- lblWinnerStatus, lblWebState, lblWallState, lblLipState, lblBendState
- btnRefreshSelection, btnAnalyze, btnSwapFrame, btnBack, btnNext
- btnPreview3D, btnApplyCut, btnCopyReport, btnClose
- txtDiagnostics (read-only), pnlDiagnostics (collapsible)

## State DTO. Antigravity may copy this into UI/AutoFrameJoint/AutoFrameJointViewModels.cs AFTER user explicitly approves UI previews

    namespace ADDIN.UI.AutoFrameJoint
    {
        public enum JointUiStatus
        {
            Empty, Ready, Analyzing, Estimated, NeedsReview, Unsupported, Error
        }

        public sealed class JointFrameRow
        {
            public string Occurrence { get; set; }
            public string Configuration { get; set; }
            public double? EstimatedLengthMm { get; set; }
            public double? AxisSupportFraction { get; set; }
            public string Role { get; set; }
        }

        public sealed class JointUiSnapshot
        {
            public JointUiStatus Status { get; set; }
            public JointFrameRow FrameA { get; set; }
            public JointFrameRow FrameB { get; set; }
            public double? AcuteAxisAngleDeg { get; set; }
            public string WinnerLoserDescription { get; set; }
            public string Diagnostics { get; set; }
            public bool IsDemo { get; set; }
        }
    }

## Event boundary. Do not call SOLIDWORKS COM directly from UI

    using System;
    using System.Windows.Forms;

    namespace ADDIN.UI.AutoFrameJoint
    {
        public partial class AutoFrameJointForm : Form
        {
            public event EventHandler RefreshSelectionRequested;
            public event EventHandler AnalyzeRequested;

            private void btnRefreshSelection_Click(object sender, EventArgs e)
            {
                EventHandler handler = RefreshSelectionRequested;
                if (handler != null) handler(this, EventArgs.Empty);
            }

            private void btnAnalyze_Click(object sender, EventArgs e)
            {
                if (!btnAnalyze.Enabled) return;
                EventHandler handler = AnalyzeRequested;
                if (handler != null) handler(this, EventArgs.Empty);
            }

            public void SetSnapshot(JointUiSnapshot snapshot)
            {
                if (snapshot == null) throw new ArgumentNullException("snapshot");
                lblStatus.Text = snapshot.IsDemo
                    ? "DỮ LIỆU MẪU — KHÔNG PHẢI KẾT QUẢ SOLIDWORKS"
                    : snapshot.Status.ToString();

                lblFrameA.Text = snapshot.FrameA == null
                    ? "Chưa chọn" : (snapshot.FrameA.Occurrence ?? "Không rõ");
                lblFrameB.Text = snapshot.FrameB == null
                    ? "Chưa chọn" : (snapshot.FrameB.Occurrence ?? "Không rõ");

                lblAxisAngle.Text = snapshot.AcuteAxisAngleDeg.HasValue
                    ? snapshot.AcuteAxisAngleDeg.Value.ToString("0.##") + "° (góc giữa hai trục)"
                    : "Chưa đo";
                lblWinnerStatus.Text = snapshot.WinnerLoserDescription
                    ?? "Chưa xác nhận";
                txtDiagnostics.Text = snapshot.Diagnostics ?? "";

                // Future cutting functions MUST remain inert in this phase.
                btnPreview3D.Enabled = false;
                btnApplyCut.Enabled = false;

                // In demo mode NEVER trigger real command.
                btnAnalyze.Enabled = !snapshot.IsDemo
                    && (snapshot.Status == JointUiStatus.Ready
                        || snapshot.Status == JointUiStatus.NeedsReview);
            }

            private void btnApplyCut_Click(object sender, EventArgs e)
            {
                // Intentionally no backend call. Keep disabled for this phase.
                btnApplyCut.Enabled = false;
            }
        }
    }

The form event handlers above are **NEW isolated UI handlers** only. Existing event handlers elsewhere in the Add-in are strictly protected. Their event names must match the designer, and no controller should subscribe until ChatGPT validates the integration.

## Boundaries
- The production API currently only returns report text from AutoFrameJointReadOnlyProbe.AnalyzeSelected(ISldWorks); it does NOT expose two separate selection events or a cut plan. Do not pretend it does.
- No debugger/COM calls in the form constructor. No mutation of Assembly, no change to active configuration, no Save.
- Use TaiTheme styles, ModernCard and ModernButton. Build a UI-only mock after authorization; if project cannot compile because not integrated, report that honestly and provide screenshots from isolated designer harness only.
- Do not add this form to ADDIN.csproj or modify TaskPane launcher. That integration belongs to the backend code owner in a separate later commit.

## Review gates
Stage A: three separate visual previews (no code edits).
Stage B: explicit user approval for UI appearance.
Stage C: UI-only code + screenshots/diff; no claim of SOLIDWORKS integration.
Stage D: ChatGPT review then small project/launcher integration commit by code owner.
