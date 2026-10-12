using System;
using System.Drawing;
using System.Windows.Forms;

namespace ADDIN
{
    public partial class BomTaskPaneControl
    {
        private UI.ModernButton btnCheckDxf, btnCheckDxfMacro, btnStandaloneCurveMacro;
        private UI.DxfCheckForm dxfCheckForm;
        private string loadedDxfBomPath;
        private Commands.DxfChecking.DxfBomBatchResult lastDxfBomResult;
        private UI.DxfBomDrawingReviewForm dxfBomDrawingReviewForm;

        private void RunBomDxfBatch()
        {
            if (drawingBomCommandInProgress) return;
            if (actions == null || actions.LoadedBomContext != Commands.BomCommandContext.Detail || string.IsNullOrWhiteSpace(loadedDxfBomPath))
            { lblStatus.Text = "Hãy Load BOM chi tiết rồi tick các dòng cần kiểm tra DXF."; return; }
            Commands.DxfChecking.DxfBomBatchResult completedResult = null;
            RunDrawingBomCommand(() =>
            {
                try
                {
                    completedResult = new Commands.DxfChecking.CheckDxfBomBatch(swApp, dgvModelBom).RunSilent(
                        dgvModelBom, loadedDxfBomPath, BeginProgress, UpdateProgress, FinishProgress, IsDrawingBomCancelRequested);
                    lastDxfBomResult = completedResult;
                    lblStatus.Text = completedResult.Message;
                    bomCommandToolTip?.SetToolTip(lblStatus, completedResult.ReportPath ?? "");
                }
                catch (Exception ex) { lblStatus.Text = "DXF ↔ DRAWING: " + ex.Message; }
            }, true, true);
            // Show cached results after the current session's display/input state
            // has been restored. Opening this window performs no CAD/file checks.
            if (completedResult == null || completedResult.Items.Count == 0) return;
            ShowBomDxfComparisonReview(completedResult);
        }

        private void ShowBomDxfComparisonReview(Commands.DxfChecking.DxfBomBatchResult result)
        {
            if (drawingBomCommandInProgress || result == null) return;
            if (dxfBomDrawingReviewForm == null || dxfBomDrawingReviewForm.IsDisposed)
            {
                dxfBomDrawingReviewForm = new UI.DxfBomDrawingReviewForm(result);
                dxfBomDrawingReviewForm.PositionOnScreen(Screen.FromControl(this).WorkingArea);
                dxfBomDrawingReviewForm.FormClosed += (s, e) => dxfBomDrawingReviewForm = null;
            }
            else dxfBomDrawingReviewForm.SetBatch(result);
            if (!dxfBomDrawingReviewForm.Visible) dxfBomDrawingReviewForm.Show(this);
            dxfBomDrawingReviewForm.Activate();
        }

        private void EnsureCheckDxfButtons()
        {
            if (btnCheckDxf == null)
            {
                btnCheckDxf = CreateCheckDxfButton("btnCheckDxf");
                btnCheckDxf.Text = "DXF ↔ DRAWING";
                btnCheckDxf.Subtitle = "DXF ↔ Drawing theo BOM";
                btnCheckDxf.Enabled = false;
                tabDrawingBom.Controls.Add(btnCheckDxf);
            }
            if (btnStandaloneCurveMacro == null)
            {
                btnStandaloneCurveMacro = CreateCheckDxfButton("btnStandaloneCurveMacro");
                btnStandaloneCurveMacro.Click -= btnCheckDxf_Click;
                btnStandaloneCurveMacro.Click += (s, e) => RunStandaloneCurveMacro();
                btnStandaloneCurveMacro.Text = "CHECK ĐƯỜNG";
                btnStandaloneCurveMacro.Subtitle = "Macro rời Drawing / DXF";
                tabDrawingBom.Controls.Add(btnStandaloneCurveMacro);
            }
            if (btnCheckDxfMacro == null)
            {
                btnCheckDxfMacro = CreateCheckDxfButton("btnCheckDxfMacro");
                tabModelMacroPage.Controls.Add(btnCheckDxfMacro);
            }

        }

        private UI.ModernButton CreateCheckDxfButton(string name)
        {
            UI.ModernButton button = new UI.ModernButton
            {
                Name = name, Text = "CHECK DXF FILE", Subtitle = "Kiểm tra file DXF 2D", Size = new Size(112, 46),
                AutoSize = false, AutoEllipsis = false, TextAlign = ContentAlignment.MiddleCenter,
                TextImageRelation = TextImageRelation.Overlay, Padding = new Padding(4, 2, 4, 2),
                Dock = DockStyle.None, Anchor = AnchorStyles.Top | AnchorStyles.Left,
                NormalColor = Color.FromArgb(242, 246, 252), BorderColor = Color.FromArgb(180, 200, 225),
                BorderRadius = 4,
                HoverColor = Color.FromArgb(226, 236, 248),
                PressColor = Color.FromArgb(205, 222, 245), ForeColor = Color.FromArgb(25, 45, 80),
                SubtitleColor = Color.FromArgb(70, 95, 130),
                Font = new Font("Segoe UI", 8.25F, FontStyle.Bold), TabIndex = 30
            };
            button.Click += btnCheckDxf_Click;
            return button;
        }

        private void btnCheckDxf_Click(object sender, EventArgs e)
        {
            if (ReferenceEquals(sender, btnCheckDxf)) { RunBomDxfBatch(); return; }
            if (dxfCheckForm != null && !dxfCheckForm.IsDisposed)
            { dxfCheckForm.Activate(); return; }
            dxfCheckForm = new UI.DxfCheckForm();
            dxfCheckForm.PositionOnScreen(Screen.FromControl(this).WorkingArea);
            dxfCheckForm.FormClosed += (s, args) => dxfCheckForm = null;
            dxfCheckForm.Show(this);
        }

        private void CloseDxfCheckWindow()
        {
            if (dxfCheckForm != null && !dxfCheckForm.IsDisposed) dxfCheckForm.Close();
            if (dxfBomDrawingReviewForm != null && !dxfBomDrawingReviewForm.IsDisposed) dxfBomDrawingReviewForm.Close();
            if (bomDxfReviewForm != null && !bomDxfReviewForm.IsDisposed) bomDxfReviewForm.Close();
        }

    }
}
