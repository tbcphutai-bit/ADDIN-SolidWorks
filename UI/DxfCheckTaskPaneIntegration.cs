using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ADDIN
{
    public partial class BomTaskPaneControl
    {
        private UI.ModernButton btnCheckDxf, btnDxfSource, btnCheckDxfMacro, btnStandaloneCurveMacro;
        private UI.DxfCheckForm dxfCheckForm;
        private string loadedDxfBomPath;
        private string selectedDxfSourceDirectory;
        private Commands.DxfChecking.DxfBomBatchResult lastDxfBomResult;
        private UI.DxfBomDrawingReviewForm dxfBomDrawingReviewForm;

        private void RunBomDxfBatch()
        {
            if (drawingBomCommandInProgress) return;
            if (actions == null || actions.LoadedBomContext != Commands.BomCommandContext.Detail || string.IsNullOrWhiteSpace(loadedDxfBomPath))
            { lblStatus.Text = "Hãy Load BOM chi tiết rồi tick các dòng cần kiểm tra DXF."; return; }
            if (!ChooseDxfSourceDirectory())
            { lblStatus.Text = "Đã hủy chọn thư mục DXF; chưa chạy kiểm tra."; return; }
            string sourceDirectory = selectedDxfSourceDirectory;
            Commands.DxfChecking.DxfBomBatchResult completedResult = null;
            using (var progressView = new UI.DxfBomCheckProgressForm(sourceDirectory))
            {
                progressView.PositionOnScreen(Screen.FromControl(this).WorkingArea);
                RunDrawingBomCommand(() =>
                {
                    try
                    {
                        progressView.Show(this); progressView.Refresh();
                        completedResult = new Commands.DxfChecking.CheckDxfBomBatch(swApp, dgvModelBom).RunSilent(
                            dgvModelBom, loadedDxfBomPath,
                            total => { progressView.Begin(total); BeginProgress(total); },
                            (current, total) => { progressView.UpdateProgress(current, total); UpdateProgress(current, total); },
                            FinishProgress, IsDrawingBomCancelRequested, null, sourceDirectory, null,
                            item =>
                            {
                                progressView.SetCurrentFile(item);
                                lblStatus.Text = string.IsNullOrWhiteSpace(item.DxfPath)
                                    ? "Đang tìm DXF cho chi tiết " + item.PartNumber
                                    : "Đang kiểm tra DXF: " + item.DxfPath;
                                bomCommandToolTip?.SetToolTip(lblStatus, item.DxfPath ?? sourceDirectory);
                                lblStatus.Refresh();
                            });
                        lastDxfBomResult = completedResult;
                        lblStatus.Text = completedResult.Message;
                        bomCommandToolTip?.SetToolTip(lblStatus, completedResult.ReportPath ?? "");
                    }
                    catch (Exception ex) { lblStatus.Text = "DXF ↔ DRAWING: " + ex.Message; }
                }, true, true);
            }
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
                btnCheckDxf.Subtitle = "Chọn thư mục DXF";
                btnCheckDxf.Enabled = false;
                tabDrawingBom.Controls.Add(btnCheckDxf);
            }
            if (btnDxfSource == null)
            {
                btnDxfSource = CreateCheckDxfButton("btnDxfSource");
                btnDxfSource.Click -= btnCheckDxf_Click;
                btnDxfSource.Click += (s, e) => ChooseDxfSourceDirectory();
                btnDxfSource.Text = "THƯ MỤC DXF";
                btnDxfSource.Enabled = false;
                btnDxfSource.Visible = false;
                UpdateDxfSourceButton();
            }
            if (btnStandaloneCurveMacro == null)
            {
                btnStandaloneCurveMacro = CreateCheckDxfButton("btnStandaloneCurveMacro");
                btnStandaloneCurveMacro.Click -= btnCheckDxf_Click;
                btnStandaloneCurveMacro.Click += (s, e) => RunStandaloneCurveMacro();
                btnStandaloneCurveMacro.Text = "CHECK ĐƯỜNG";
                btnStandaloneCurveMacro.Subtitle = "Macro rời Drawing / DXF";
                btnStandaloneCurveMacro.Visible = false;
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

        private bool ChooseDxfSourceDirectory()
        {
            string initial = Directory.Exists(selectedDxfSourceDirectory) ? selectedDxfSourceDirectory :
                Path.GetDirectoryName(loadedDxfBomPath);
            using (var dialog = new FolderBrowserDialog {
                Description = "Chọn thư mục DXF cho lần kiểm tra này. Chỉ dùng file trong thư mục đã chọn và các thư mục con.",
                SelectedPath = Directory.Exists(initial) ? initial : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                ShowNewFolderButton = false
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath)) return false;
                selectedDxfSourceDirectory = dialog.SelectedPath; UpdateDxfSourceButton();
                return true;
            }
        }

        private void UpdateDxfSourceButton()
        {
            if (btnDxfSource == null) return;
            string path = selectedDxfSourceDirectory;
            btnDxfSource.Subtitle = string.IsNullOrWhiteSpace(path) ? "Chọn khi bấm kiểm tra" : Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            string tip = string.IsNullOrWhiteSpace(path)
                ? "Bấm DXF ↔ DRAWING để chọn thư mục DXF trước mỗi lần kiểm tra."
                : "Thư mục DXF gần nhất: " + path + ". DXF ↔ DRAWING sẽ hỏi chọn thư mục trước mỗi lần chạy.";
            bomCommandToolTip?.SetToolTip(btnDxfSource, tip);
        }

        private void ResetDxfSource()
        {
            selectedDxfSourceDirectory = null;
            UpdateDxfSourceButton();
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
