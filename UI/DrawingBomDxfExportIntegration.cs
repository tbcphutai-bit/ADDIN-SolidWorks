using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ADDIN.Commands;
using ADDIN.Commands.DrawingDxfExporting;
using ADDIN.Commands.DxfChecking;
using SolidWorks.Interop.sldworks;

namespace ADDIN
{
    public partial class BomTaskPaneControl
    {
        private UI.ModernButton btnExportBomDxf;
        private UI.BomDxfExportPane bomDxfExportPane;
        private string lastBomDxfExportFolder, bomDxfFolderSource;
        private BomDxfBatchResult lastBomDxfPreviewResult, reviewedBomDxfBatch;
        private UI.DxfCheckForm bomDxfReviewForm;
        private bool bomDxfReviewUpdating;
        private bool IsBomDxfExportPaneOpen { get { return bomDxfExportPane != null && bomDxfExportPane.Visible; } }

        private void EnsureDrawingBomDxfExportButton()
        {
            EnsureDxfSplineColumn();
            if (btnExportBomDxf != null) return;
            btnExportBomDxf = new UI.ModernButton {
                Name = "btnExportBomDxf", Text = "XUẤT DXF", Subtitle = "Flat view + Check DXF", Enabled = false,
                Size = new Size(112, 46), AutoSize = false, TextAlign = ContentAlignment.MiddleCenter,
                Padding = new Padding(4, 2, 4, 2), NormalColor = Color.FromArgb(236, 253, 245), BorderColor = Color.FromArgb(167, 243, 208),
                BorderRadius = 4, HoverColor = Color.FromArgb(209, 250, 229), PressColor = Color.FromArgb(167, 243, 208),
                ForeColor = Color.FromArgb(6, 95, 70), SubtitleColor = Color.FromArgb(71, 85, 105),
                Font = new Font("Segoe UI", 8.25F, FontStyle.Bold), TabIndex = 32
            };
            btnExportBomDxf.Click += (sender, args) => OpenDrawingBomDxfMonitor();
            tabDrawingBom.Controls.Add(btnExportBomDxf);
            bomCommandToolTip?.SetToolTip(btnExportBomDxf,
                "Cập nhật → tick dòng BOM → XUẤT DXF. Xuất Flat Pattern, tắt màu rồi tự Check biên CUT. Kết quả xuất / Check DXF theo dõi ngay trong Task Pane.");
        }
        private int SelectedBomDxfRowCount()
        {
            if (dgvModelBom.IsCurrentCellDirty) dgvModelBom.CommitEdit(DataGridViewDataErrorContexts.Commit);
            dgvModelBom.EndEdit();
            return dgvModelBom.Rows.Cast<DataGridViewRow>().Count(r => !r.IsNewRow && Convert.ToBoolean(r.Cells[0].Value ?? false));
        }
        private void OpenDrawingBomDxfMonitor()
        {
            if (drawingBomCommandInProgress) return;
            if (actions == null || actions.LoadedBomContext != BomCommandContext.Detail || string.IsNullOrWhiteSpace(loadedDxfBomPath)) {
                lblStatus.Text = "Bấm Cập nhật để load BOM chi tiết rồi tick các dòng cần xuất DXF."; return;
            }
            int selected = SelectedBomDxfRowCount();
            if (selected == 0) { lblStatus.Text = "Tick ít nhất một dòng BOM cần xuất DXF."; return; }
            EnsureDrawingBomDxfMonitor();
            // The DXF folder follows the loaded BOM: <folder of the BOM Drawing>\<手配番号>. A folder the user
            // chose stays while the same BOM is loaded.
            bool otherBom = !string.Equals(bomDxfFolderSource, loadedDxfBomPath, StringComparison.OrdinalIgnoreCase);
            if (otherBom) { bomDxfFolderSource = loadedDxfBomPath; lastBomDxfExportFolder = null; }
            string folder = lastBomDxfExportFolder ?? Path.Combine(Path.GetDirectoryName(loadedDxfBomPath), FirstTickedOrderNumber());
            bomDxfExportPane.Prepare(folder, selected, otherBom); bomDxfExportPane.Visible = true;
            LayoutDrawingBomCommands(); bomDxfExportPane.BringToFront();
        }
        // 手配番号 of the first ticked row whose Part is in memory. "" when none can be read: the export then
        // adds the 手配番号 folder itself.
        private string FirstTickedOrderNumber()
        {
            foreach (DataGridViewRow row in dgvModelBom.Rows)
            {
                if (row.IsNewRow || !Convert.ToBoolean(row.Cells[0].Value ?? false)) continue;
                foreach (Component2 component in (row.Tag as object[] ?? new object[0]).OfType<Component2>())
                {
                    try
                    {
                        string order = DxfOutPut.OrderFolderName(component.GetModelDoc2() as ModelDoc2, component.ReferencedConfiguration);
                        if (order.Length > 0) return order;
                    }
                    catch (COMException) { }
                }
            }
            return "";
        }
        private void EnsureDrawingBomDxfMonitor()
        {
            if (bomDxfExportPane != null) return;
            bomDxfExportPane = new UI.BomDxfExportPane { Visible = false };
            bomDxfExportPane.StartRequested += RunDrawingBomDxfExport;
            bomDxfExportPane.StopRequested += () => button1.PerformClick();
            bomDxfExportPane.BackRequested += () => { if (drawingBomCommandInProgress) return; bomDxfExportPane.Visible = false; LayoutDrawingBomCommands(); };
            bomDxfExportPane.ReviewRequested += ShowDrawingBomDxfReview;
            tabDrawingBom.Controls.Add(bomDxfExportPane);
        }
        private void RunDrawingBomDxfExport()
        {
            if (drawingBomCommandInProgress || bomDxfReviewUpdating || bomDxfExportPane == null) return;
            if (bomDxfReviewForm?.IsChecking == true) {
                bomDxfExportPane.ShowMessage("Đang kiểm tra DXF đã sửa. Chờ hoàn tất hoặc bấm Dừng trong bảng Check DXF.", true); return;
            }
            if (actions == null || actions.LoadedBomContext != BomCommandContext.Detail || string.IsNullOrWhiteSpace(loadedDxfBomPath)) {
                bomDxfExportPane.ShowMessage("Bấm ← BOM rồi Cập nhật lại BOM chi tiết.", true); return;
            }
            BomDxfExportOptions options;
            int selected = SelectedBomDxfRowCount();
            if (selected == 0) { bomDxfExportPane.ShowMessage("Tick ít nhất một dòng BOM cần xuất.", true); return; }
            try { options = bomDxfExportPane.ReadOptions(); }
            catch (Exception ex) { bomDxfExportPane.ShowMessage(ex.Message, true); return; }
            options.ReportFolder = Path.GetDirectoryName(loadedDxfBomPath);
            bool reviewWasVisible = bomDxfReviewForm?.Visible == true;
            bomDxfReviewForm?.Hide();
            lastBomDxfExportFolder = options.TargetFolder; bomDxfExportPane.BeginBatch(selected); bomDxfExportPane.Refresh();
            BomDxfBatchResult completedResult = null;
            RunDrawingBomCommand(() => {
                try {
                    var result = completedResult = new DrawingBomDxfExport(swApp, dgvModelBom).Run(dgvModelBom, loadedDxfBomPath, options,
                        BeginProgress, UpdateProgress, FinishProgress, IsDrawingBomCancelRequested, bomDxfExportPane.ShowItem);
                    lblStatus.Text = result.Message; bomCommandToolTip?.SetToolTip(lblStatus, result.CheckReportPath ?? result.ReportPath); bomDxfExportPane.Complete(result);
                }
                catch (Exception ex) { lblStatus.Text = "Xuất DXF: " + ex.Message; bomDxfExportPane.ShowMessage(lblStatus.Text, true); }
                finally { bomDxfExportPane.SetRunning(false); }
            }, true, true);
            // The CAD input lock has been released before opening the editor UI.
            if (completedResult != null) {
                lastBomDxfPreviewResult = completedResult;
                if (completedResult.CheckBatch?.Files.Count > 0) ShowDrawingBomDxfReview(null);
            }
            else if (reviewWasVisible && bomDxfReviewForm != null && !bomDxfReviewForm.IsDisposed) bomDxfReviewForm.Show(this);
        }

        private void ShowDrawingBomDxfReview(string selectedSource)
        {
            if (drawingBomCommandInProgress || lastBomDxfPreviewResult?.CheckBatch == null) return;
            if (bomDxfReviewForm == null || bomDxfReviewForm.IsDisposed) {
                bomDxfReviewForm = new UI.DxfCheckForm(false);
                bomDxfReviewForm.PositionOnScreen(Screen.FromControl(this).WorkingArea);
                bomDxfReviewForm.FormClosed += (s, e) => { bomDxfReviewForm = null; reviewedBomDxfBatch = null; };
                bomDxfReviewForm.BatchChanged += RefreshDrawingBomDxfReview;
            }
            if (!bomDxfReviewForm.ShowExportedBatch(lastBomDxfPreviewResult.CheckBatch, selectedSource)) {
                bomDxfExportPane.ShowMessage("Bảng Check DXF đang kiểm tra bản sửa; chờ bước này hoàn tất.", true); return;
            }
            reviewedBomDxfBatch = lastBomDxfPreviewResult;
            if (!bomDxfReviewForm.Visible) bomDxfReviewForm.Show(this);
            bomDxfReviewForm.Activate();
        }

        private void RefreshDrawingBomDxfReview(DxfBatchResult checkedBatch)
        {
            var batch = reviewedBomDxfBatch;
            if (batch == null || checkedBatch == null || IsDisposed || Disposing) return;
            bomDxfReviewUpdating = true;
            try {
                batch.CheckBatch = checkedBatch; batch.CheckReportPath = checkedBatch.ExcelPath;
                foreach (var item in batch.Items) {
                    var file = checkedBatch.Files.FirstOrDefault(f => string.Equals(f.SourcePath, item.OutputPath, StringComparison.OrdinalIgnoreCase));
                    if (file == null) continue;
                    BomDxfAutoCheck.Apply(item, file);
                    if (ReferenceEquals(batch, lastBomDxfPreviewResult)) bomDxfExportPane.ShowItem(item, batch.ProcessedRows, batch.SelectedRows);
                }
                if (ReferenceEquals(batch, lastBomDxfPreviewResult)) bomDxfExportPane.Complete(batch);
                try { DrawingBomDxfExport.WriteReport(batch); }
                catch (Exception ex) { bomDxfExportPane.ShowMessage("Đã cập nhật Check DXF; chưa ghi được báo cáo xuất: " + ex.Message, true); }
            }
            finally { bomDxfReviewUpdating = false; }
        }
    }
}
