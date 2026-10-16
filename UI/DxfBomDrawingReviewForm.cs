using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using ADDIN.Commands.DxfChecking;

namespace ADDIN.UI
{
    internal sealed class DxfBomCheckProgressForm : Form
    {
        readonly Label status = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        readonly TextBox folderPath = CreatePathBox("txtDxfSourceDirectory"), currentPath = CreatePathBox("txtCurrentDxfPath");
        readonly ProgressBar progress = new ProgressBar { Dock = DockStyle.Fill, Style = ProgressBarStyle.Continuous };
        int completedRows, totalRows;

        internal DxfBomCheckProgressForm(string directory)
        {
            Text = "TAI TOOL — Đang kiểm tra DXF ↔ DRAWING";
            ClientSize = new Size(860, 290);
            Font = new Font("Segoe UI", 10F);
            BackColor = TaiTheme.Page;
            ForeColor = TaiTheme.Text;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            ControlBox = false;
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 1, RowCount = 6 };
            foreach (int height in new[] { 34, 22, 46, 22, 88, 28 })
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            Controls.Add(root);

            status.Font = new Font("Segoe UI", 11.5F, FontStyle.Bold);
            status.ForeColor = TaiTheme.Accent;
            status.Text = "Đang lập danh sách DXF trong thư mục đã chọn…";
            root.Controls.Add(status, 0, 0);

            var lblFolder = new Label
            {
                Text = "Thư mục DXF đã chọn",
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                ForeColor = TaiTheme.TextMuted,
                TextAlign = ContentAlignment.BottomLeft
            };
            root.Controls.Add(lblFolder, 0, 1);
            folderPath.Text = directory ?? "";
            root.Controls.Add(folderPath, 0, 2);

            var lblCurrent = new Label
            {
                Text = "File DXF đang kiểm tra",
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                ForeColor = TaiTheme.TextMuted,
                TextAlign = ContentAlignment.BottomLeft
            };
            root.Controls.Add(lblCurrent, 0, 3);
            currentPath.Text = "Đang chờ xác định file DXF…";
            root.Controls.Add(currentPath, 0, 4);

            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
            footer.Controls.Add(progress, 0, 0);
            var cancelNote = new Label
            {
                Text = "Nhấn Cancel trên Drawing BOM để dừng",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight,
                Font = new Font("Segoe UI", 9F),
                ForeColor = TaiTheme.TextMuted
            };
            footer.Controls.Add(cancelNote, 1, 0);
            root.Controls.Add(footer, 0, 5);
        }

        internal void PositionOnScreen(Rectangle area)
        {
            StartPosition = FormStartPosition.Manual;
            Location = new Point(area.Left + Math.Max(0, (area.Width - Width) / 2), area.Top + Math.Max(0, (area.Height - Height) / 2));
        }

        internal void Begin(int total)
        {
            totalRows = total;
            completedRows = 0;
            progress.Maximum = Math.Max(1, total);
            progress.Value = 0;
            Refresh();
        }

        internal void UpdateProgress(int current, int total)
        {
            completedRows = current;
            totalRows = total;
            progress.Maximum = Math.Max(1, total);
            progress.Value = Math.Min(progress.Maximum, Math.Max(0, current));
            status.Text = current >= total ? "Đang hoàn tất báo cáo…" : "Đã xử lý " + current + "/" + total + " dòng BOM";
            Refresh();
        }

        internal void SetCurrentFile(DxfBomCheckItem item)
        {
            if (IsDisposed || item == null) return;
            bool resolved = !string.IsNullOrWhiteSpace(item.DxfPath);
            status.Text = (resolved ? "Đang kiểm tra " : "Đang tìm DXF ") + Math.Min(completedRows + 1, totalRows) + "/" + totalRows + " · 図番 " + item.PartNumber;
            currentPath.Text = resolved ? item.DxfPath : "Chưa xác định file DXF; đang đọc Drawing của 図番 " + item.PartNumber + ".";
            currentPath.SelectionStart = 0;
            currentPath.SelectionLength = 0;
            currentPath.ScrollToCaret();
            Refresh();
        }

        internal static TextBox CreatePathBox(string name)
        {
            return new TextBox
            {
                Name = name,
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                WordWrap = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = TaiTheme.Surface,
                ForeColor = TaiTheme.Text,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9.5F)
            };
        }
    }

    // Displays the exact, already checked geometry. This form never opens CAD
    // documents, reads DXF files or re-runs a comparison when a row is selected.
    internal sealed class DxfBomDrawingReviewForm : Form
    {
        readonly DataGridView rows, issues;
        readonly DxfPreview preview = new DxfPreview();
        readonly Label titleLabel = new Label();
        readonly Label summary = new Label();
        readonly Label context = new Label();
        readonly TextBox checkedDxfPath = DxfBomCheckProgressForm.CreatePathBox("txtCheckedDxfPath");
        readonly TextBox detail = new TextBox();
        readonly ModernButton fit, focus, drawingOnly, dxfOnly, openReport, close;
        readonly ToolTip toolTips = new ToolTip();

        DxfBomBatchResult batch;
        DxfBomCheckItem selected;
        DxfFileResult drawingGeometry, dxfGeometry;
        bool setting;

        internal int ResultRowCount { get { return rows.Rows.Count; } }
        internal int IssueRowCount { get { return issues.Rows.Count; } }
        internal int PreviewDrawingEntityCount { get { return drawingGeometry == null ? 0 : drawingGeometry.Edges.Count; } }
        internal int PreviewDxfEntityCount { get { return dxfGeometry == null ? 0 : dxfGeometry.Edges.Count; } }
        internal bool PreviewIsAligned { get { return selected != null && selected.Result != null && selected.Result.AlignmentFound; } }

        internal DxfBomDrawingReviewForm(DxfBomBatchResult result)
        {
            Text = "TAI TOOL — DXF ↔ DRAWING";
            Size = new Size(1400, 840);
            MinimumSize = new Size(1100, 680);
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 10F);
            BackColor = TaiTheme.Page;
            ForeColor = TaiTheme.Text;
            DoubleBuffered = true;

            rows = Grid();
            issues = Grid();

            fit = CreateButton("Toàn bộ", TaiIcon.FitView, ModernButtonTone.Accent, 100);
            focus = CreateButton("Vị trí lỗi", TaiIcon.Measure, ModernButtonTone.Danger, 115);
            drawingOnly = CreateButton("Drawing", TaiIcon.Sheet, ModernButtonTone.Neutral, 100);
            dxfOnly = CreateButton("DXF gốc", TaiIcon.Compare, ModernButtonTone.Neutral, 105);
            openReport = CreateButton("Mở báo cáo", TaiIcon.Export, ModernButtonTone.Accent, 140);
            close = CreateButton("Đóng", TaiIcon.Close, ModernButtonTone.Neutral, 95);

            toolTips.SetToolTip(fit, "Phóng to vừa khung toàn bộ biên dạng Drawing và DXF");
            toolTips.SetToolTip(focus, "Phóng to chi tiết vào vị trí phát hiện sai lệch");
            toolTips.SetToolTip(drawingOnly, "Chỉ hiển thị biên dạng từ Drawing gốc (tỷ lệ 1:1 mm)");
            toolTips.SetToolTip(dxfOnly, "Chỉ hiển thị biên dạng file DXF gốc (tọa độ gốc)");
            toolTips.SetToolTip(openReport, "Mở file báo cáo kết quả chi tiết trong trình duyệt");

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
                ColumnCount = 1,
                RowCount = 4
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            Controls.Add(root);

            // Row 0: Top Banner
            var banner = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = Padding.Empty
            };
            banner.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 320));
            banner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            titleLabel.Text = "SO SÁNH DXF ↔ DRAWING";
            titleLabel.Font = new Font("Segoe UI", 13F, FontStyle.Bold);
            titleLabel.ForeColor = TaiTheme.Accent;
            titleLabel.Dock = DockStyle.Fill;
            titleLabel.TextAlign = ContentAlignment.MiddleLeft;
            banner.Controls.Add(titleLabel, 0, 0);

            summary.Dock = DockStyle.Fill;
            summary.TextAlign = ContentAlignment.MiddleRight;
            summary.Font = new Font("Segoe UI", 10.5F, FontStyle.Bold);
            banner.Controls.Add(summary, 1, 0);
            root.Controls.Add(banner, 0, 0);

            // Row 1: Source & Context Area
            var sourceArea = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = TaiTheme.Surface,
                Padding = new Padding(8, 5, 8, 5),
                Margin = new Padding(0, 0, 0, 4)
            };
            sourceArea.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            sourceArea.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            context.Dock = DockStyle.Fill;
            context.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            context.ForeColor = TaiTheme.Text;
            context.AutoEllipsis = true;
            context.TextAlign = ContentAlignment.MiddleLeft;
            sourceArea.Controls.Add(context, 0, 0);

            checkedDxfPath.Dock = DockStyle.Fill;
            checkedDxfPath.Font = new Font("Segoe UI", 9.5F);
            checkedDxfPath.BackColor = TaiTheme.Page;
            checkedDxfPath.ForeColor = TaiTheme.TextMuted;
            checkedDxfPath.BorderStyle = BorderStyle.FixedSingle;
            sourceArea.Controls.Add(checkedDxfPath, 0, 1);
            root.Controls.Add(sourceArea, 0, 1);

            // Row 2: 3 Cards Workbench
            var workbench = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                Margin = Padding.Empty
            };
            workbench.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
            workbench.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 43));
            workbench.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 29));
            root.Controls.Add(workbench, 0, 2);

            // Card 1: Rows
            AddColumn(rows, "bom", "BOM", 50, DataGridViewContentAlignment.MiddleCenter);
            AddColumn(rows, "part", "図番 (Part)", 90, DataGridViewContentAlignment.MiddleLeft);
            var colSheetView = AddColumn(rows, "sheetView", "シート / 図面ビュー", 120, DataGridViewContentAlignment.MiddleLeft);
            colSheetView.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            AddColumn(rows, "result", "Kết quả", 120, DataGridViewContentAlignment.MiddleCenter);
            workbench.Controls.Add(Card("DANH SÁCH KIỂM TRA (BOM)", rows), 0, 0);

            // Card 2: Preview
            var previewArea = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = TaiTheme.Surface
            };
            previewArea.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            previewArea.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            previewArea.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));

            var previewButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                WrapContents = false,
                Padding = new Padding(4, 4, 4, 4),
                Margin = Padding.Empty
            };
            previewButtons.Controls.AddRange(new Control[] { fit, focus, drawingOnly, dxfOnly });
            previewArea.Controls.Add(previewButtons, 0, 0);
            previewArea.Controls.Add(preview, 0, 1);

            var hint = new Label
            {
                Text = "Xanh: Drawing · Cam: DXF đã dịch X/Y · Lăn chuột: Phóng to/Thu nhỏ · Chuột giữa: Di chuyển",
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9F),
                ForeColor = TaiTheme.TextMuted,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(6, 0, 0, 0)
            };
            previewArea.Controls.Add(hint, 0, 2);
            workbench.Controls.Add(Card("Biên dạng Drawing / DXF", previewArea), 1, 0);

            // Card 3: Issues
            AddColumn(issues, "code", "Mã lỗi", 75, DataGridViewContentAlignment.MiddleCenter);
            AddColumn(issues, "dev", "Lệch (mm)", 85, DataGridViewContentAlignment.MiddleRight);
            var colDesc = AddColumn(issues, "desc", "Chi tiết lỗi", 120, DataGridViewContentAlignment.MiddleLeft);
            colDesc.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            issues.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            issues.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;

            detail.Dock = DockStyle.Fill;
            detail.Multiline = true;
            detail.ReadOnly = true;
            detail.WordWrap = true;
            detail.ScrollBars = ScrollBars.Vertical;
            detail.BackColor = TaiTheme.Page;
            detail.ForeColor = TaiTheme.Text;
            detail.BorderStyle = BorderStyle.FixedSingle;
            detail.Font = new Font("Segoe UI", 9.5F);

            var issueArea = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = TaiTheme.Surface
            };
            issueArea.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            issueArea.RowStyles.Add(new RowStyle(SizeType.Absolute, 135));
            issueArea.Controls.Add(issues, 0, 0);
            issueArea.Controls.Add(detail, 0, 1);
            workbench.Controls.Add(Card("Kết quả & Vị trí lỗi", issueArea), 2, 0);

            // Row 3: Bottom Actions
            var actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(0, 4, 0, 0),
                Margin = Padding.Empty
            };
            close.Click += (s, e) => Close();
            CancelButton = close;
            actions.Controls.AddRange(new Control[] { openReport, close });
            root.Controls.Add(actions, 0, 3);

            // Event handlers
            rows.SelectionChanged += (s, e) => { if (!setting) ShowSelected(); };
            issues.SelectionChanged += (s, e) => { if (!setting) ShowIssue(true); };
            fit.Click += (s, e) => ShowOverlay(null, false);
            focus.Click += (s, e) => ShowIssue(true);
            drawingOnly.Click += (s, e) => { preview.Set(drawingGeometry, null); detail.Text = "Drawing gốc theo hệ View 1:1 mm."; };
            dxfOnly.Click += (s, e) => { if (selected != null && selected.PreviewDxfEdges != null) preview.Set(PreviewFile("DXF gốc", selected.PreviewDxfEdges, new Translation2D()), null); detail.Text = "DXF gốc giữ nguyên tọa độ và kích thước."; };
            openReport.Click += (s, e) => { if (batch != null && File.Exists(batch.ReportPath)) try { Process.Start(new ProcessStartInfo(batch.ReportPath) { UseShellExecute = true }); } catch (Exception error) { detail.Text = "Không mở được báo cáo: " + error.Message; } };

            SetBatch(result);
        }

        internal void PositionOnScreen(Rectangle area)
        {
            StartPosition = FormStartPosition.Manual;
            Size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height));
            Location = new Point(area.Left + Math.Max(0, (area.Width - Width) / 2), area.Top + Math.Max(0, (area.Height - Height) / 2));
        }

        internal void SetBatch(DxfBomBatchResult result)
        {
            batch = result ?? throw new ArgumentNullException(nameof(result));
            setting = true;
            try
            {
                rows.Rows.Clear();
                issues.Rows.Clear();
                preview.Set(null, null);
                selected = null;
                drawingGeometry = dxfGeometry = null;

                summary.Text = string.Format(
                    "{0} PASS   ·   {1} CHECK   ·   {2} WARN   ·   {3} FAIL   ·   {4} ERROR",
                    batch.Passed, batch.Checks, batch.Warnings, batch.Failed, batch.Errors);
                summary.ForeColor = (batch.Failed > 0 || batch.Errors > 0)
                    ? TaiTheme.Danger
                    : (batch.Warnings > 0 || batch.Checks > 0 ? TaiTheme.Warning : TaiTheme.Success);

                string dxfSource = batch.DxfSourceMode == "AUTO"
                    ? "Tự động dò theo thư mục Drawing / BOM"
                    : batch.DxfSourceMode + ": " + batch.DxfSourcePath;
                context.Text = (batch.Message ?? "Đã hoàn thành kiểm tra") + "   |   BOM: " + Path.GetFileName(batch.BomPath);
                checkedDxfPath.Text = "Nguồn DXF: " + dxfSource;

                foreach (var item in batch.Items)
                {
                    string statusDisplay;
                    Color statusColor;
                    switch (item.Status)
                    {
                        case "PASS":
                            statusDisplay = "✔ Đạt (PASS)";
                            statusColor = TaiTheme.Success;
                            break;
                        case "CHECK":
                            statusDisplay = "⚠ CHECK";
                            statusColor = TaiTheme.Warning;
                            break;
                        case "WARN":
                            statusDisplay = "⚠ Cảnh báo";
                            statusColor = TaiTheme.Warning;
                            break;
                        case "FAIL":
                            statusDisplay = "✖ Lỗi (FAIL)";
                            statusColor = TaiTheme.Danger;
                            break;
                        case "ERROR":
                            statusDisplay = "✖ Lỗi (ERROR)";
                            statusColor = TaiTheme.Danger;
                            break;
                        default:
                            statusDisplay = item.Status ?? "";
                            statusColor = TaiTheme.TextMuted;
                            break;
                    }

                    int index = rows.Rows.Add(item.ItemNumber, item.PartNumber, (item.Sheet ?? "") + " / " + (item.View ?? ""), statusDisplay);
                    rows.Rows[index].Tag = item;
                    var cell = rows.Rows[index].Cells[3];
                    cell.Style.ForeColor = statusColor;
                    cell.Style.SelectionForeColor = statusColor;
                    cell.Style.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
                }

                openReport.Enabled = !string.IsNullOrWhiteSpace(batch.ReportPath) && File.Exists(batch.ReportPath);
                rows.ClearSelection();
                int chosen = batch.Items.FindIndex(item => item.Result != null && item.PreviewDrawingEdges != null && item.PreviewDrawingEdges.Count > 0);
                if (chosen < 0 && rows.Rows.Count > 0) chosen = 0;
                if (chosen >= 0)
                {
                    rows.Rows[chosen].Selected = true;
                    rows.CurrentCell = rows.Rows[chosen].Cells[0];
                }
            }
            finally { setting = false; }
            ShowSelected();
        }

        void ShowSelected()
        {
            selected = rows.SelectedRows.Count == 0 ? null : rows.SelectedRows[0].Tag as DxfBomCheckItem;
            setting = true;
            try
            {
                issues.Rows.Clear();
                drawingGeometry = dxfGeometry = null;
                preview.Set(null, null);
                fit.Enabled = focus.Enabled = drawingOnly.Enabled = dxfOnly.Enabled = false;

                checkedDxfPath.Text = "Nguồn DXF: " + (batch.DxfSourceMode == "AUTO" ? "Tự động dò theo thư mục Drawing / BOM" : batch.DxfSourcePath);
                if (selected == null) return;

                context.Text = "図番 (Part): " + selected.PartNumber + "   |   Bản vẽ: " + Path.GetFileName(selected.DrawingPath) + "   |   シート / ビュー: " + (selected.Sheet ?? "") + " / " + (selected.View ?? "");
                checkedDxfPath.Text = string.IsNullOrWhiteSpace(selected.DxfPath)
                    ? "Chưa tìm thấy file DXF tương ứng.\r\nNguồn đã chọn: " + (batch.DxfSourceMode == "AUTO" ? "Tự động dò theo Drawing / BOM" : batch.DxfSourcePath)
                    : "File DXF: " + selected.DxfPath;
                checkedDxfPath.SelectionStart = 0;
                checkedDxfPath.SelectionLength = 0;
                checkedDxfPath.ScrollToCaret();

                detail.Text = selected.Message ?? "";

                if (selected.PreviewDrawingEdges != null)
                    drawingGeometry = PreviewFile("Drawing", selected.PreviewDrawingEdges, new Translation2D());
                if (selected.PreviewDxfEdges != null)
                    dxfGeometry = PreviewFile("DXF", selected.PreviewDxfEdges, selected.Result != null && selected.Result.AlignmentFound ? selected.Result.Translation : new Translation2D());

                if (selected.Result != null)
                {
                    foreach (var mismatch in selected.Result.Mismatches)
                    {
                        int index = issues.Rows.Add((mismatch.ReviewIssue ? "CHECK-" : "ERR-") + mismatch.Id.ToString("00"), Number(mismatch.Deviation), mismatch.Description);
                        issues.Rows[index].Tag = mismatch;
                        if (mismatch.ReviewIssue) issues.Rows[index].DefaultCellStyle.ForeColor = TaiTheme.Warning;
                    }
                }

                issues.ClearSelection();
                int located = selected.Result == null ? -1 : selected.Result.Mismatches.FindIndex(mismatch => mismatch.HasDrawingPosition || mismatch.HasDxfPosition);
                if (located >= 0)
                {
                    issues.Rows[located].Selected = true;
                    issues.CurrentCell = issues.Rows[located].Cells[0];
                }

                fit.Enabled = drawingGeometry != null || dxfGeometry != null;
                drawingOnly.Enabled = drawingGeometry != null;
                dxfOnly.Enabled = dxfGeometry != null;
                focus.Enabled = selected.Result != null && selected.Result.AlignmentFound && located >= 0;
            }
            finally { setting = false; }
            ShowOverlay(null, false);
            ShowIssue(false);
        }

        void ShowIssue(bool focusLocation)
        {
            var mismatch = issues.SelectedRows.Count == 0 ? null : issues.SelectedRows[0].Tag as GeometryMismatch;
            if (mismatch == null) return;
            detail.Text = (mismatch.ReviewIssue ? "CHECK-" : "ERR-") + mismatch.Id.ToString("00") + " · " + mismatch.Type + "\r\n"
                + mismatch.Description + "\r\n"
                + "Drawing: " + (mismatch.HasDrawingPosition ? PointText(mismatch.DrawingPosition) : "—") + "\r\n"
                + "DXF" + (PreviewIsAligned ? " đã dịch" : " gốc") + ": " + (mismatch.HasDxfPosition ? PointText(mismatch.DxfPosition) : "—");
            ShowOverlay(mismatch, focusLocation);
        }

        void ShowOverlay(GeometryMismatch mismatch, bool focusLocation)
        {
            if (drawingGeometry == null || dxfGeometry == null || selected == null || selected.Result == null || !selected.Result.AlignmentFound)
            {
                preview.Set(drawingGeometry ?? dxfGeometry, null);
                ShowCheckLocations(drawingGeometry != null, drawingGeometry == null);
                if (selected != null && selected.Result != null && !selected.Result.AlignmentFound)
                    detail.Text = selected.Message + "\r\nChưa căn được X/Y. Dùng Drawing / DXF gốc để xem từng biên dạng.";
                return;
            }
            bool located = mismatch != null && (mismatch.HasDrawingPosition || mismatch.HasDxfPosition);
            DxfPoint a = located ? (mismatch.HasDrawingPosition ? mismatch.DrawingPosition : mismatch.DxfPosition) : new DxfPoint();
            DxfPoint b = located ? (mismatch.HasDxfPosition ? mismatch.DxfPosition : mismatch.DrawingPosition) : new DxfPoint();
            preview.SetComparison(drawingGeometry, new DxfPartComparison
            {
                First = drawingGeometry,
                Second = dxfGeometry,
                SecondToFirst = DxfTransform.Identity,
                Exact = !located,
                DifferenceA = a,
                DifferenceB = b,
                MaxBoundaryGap = located ? mismatch.Deviation : 0
            }, focusLocation && located);
            preview.ComparisonMarkerLabel = mismatch != null && mismatch.ReviewIssue ? "CHECK-" + mismatch.Id.ToString("00") : "Khác";
            preview.ComparisonMarkerIsCheck = mismatch != null && mismatch.ReviewIssue;
            ShowCheckLocations(true, true);
        }

        void ShowCheckLocations(bool includeDrawing, bool includeDxf)
        {
            if (selected?.Result == null) return;
            preview.SetCheckLocations(selected.Result.Mismatches.Where(m => m.ReviewIssue &&
                ((includeDrawing && m.HasDrawingPosition) || (includeDxf && m.HasDxfPosition)))
                .Select(m => new KeyValuePair<DxfPoint, string>(m.HasDrawingPosition ? m.DrawingPosition : m.DxfPosition, "CHECK-" + m.Id.ToString("00"))));
        }

        static DxfFileResult PreviewFile(string name, IEnumerable<DxfEdge> edges, Translation2D translation)
        {
            return new DxfFileResult
            {
                RelativeName = name,
                Edges = edges.Select((edge, index) => new DxfEdge
                {
                    Type = edge.Type,
                    OriginalType = edge.OriginalType,
                    Layer = edge.Layer,
                    Handle = "PREVIEW-" + name + "-" + index.ToString(CultureInfo.InvariantCulture),
                    Space = "MODEL",
                    Start = translation.Apply(edge.Start),
                    End = translation.Apply(edge.End),
                    Breaks = new List<double>(edge.Breaks),
                    Evaluate = t => translation.Apply(edge.At(t))
                }).ToList()
            };
        }

        static string Number(double value) { return value.ToString("0.000000", CultureInfo.InvariantCulture); }
        static string PointText(DxfPoint point) { return Number(point.X) + ", " + Number(point.Y) + " mm"; }

        static DataGridViewColumn AddColumn(DataGridView grid, string name, string headerText, int width, DataGridViewContentAlignment alignment)
        {
            var col = new DataGridViewTextBoxColumn
            {
                Name = name,
                HeaderText = headerText,
                Width = width,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
            col.HeaderCell.Style.Alignment = alignment;
            col.DefaultCellStyle.Alignment = alignment;
            grid.Columns.Add(col);
            return col;
        }

        static ModernButton CreateButton(string text, TaiIcon icon, ModernButtonTone tone, int width)
        {
            return new ModernButton
            {
                Text = text,
                Icon = icon,
                Width = width,
                Height = 34,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                Kind = ModernButtonKind.Standard,
                Tone = tone,
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 0, 8, 0)
            };
        }

        static Control Card(string title, Control content)
        {
            var card = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = TaiTheme.Surface,
                Margin = new Padding(3),
                Padding = new Padding(6)
            };
            card.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var label = new Label
            {
                Text = title,
                Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
                ForeColor = TaiTheme.Accent,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(4, 0, 0, 0)
            };
            card.Controls.Add(label, 0, 0);
            content.Dock = DockStyle.Fill;
            card.Controls.Add(content, 0, 1);
            return card;
        }

        static DataGridView Grid()
        {
            var grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                BackgroundColor = TaiTheme.Surface,
                BorderStyle = BorderStyle.None,
                GridColor = TaiTheme.Divider,
                EnableHeadersVisualStyles = false,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.EnableResizing,
                ColumnHeadersHeight = 36
            };
            grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(231, 238, 248),
                ForeColor = TaiTheme.Text,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Padding = new Padding(6, 4, 6, 4),
                WrapMode = DataGridViewTriState.False
            };
            grid.DefaultCellStyle = new DataGridViewCellStyle
            {
                ForeColor = TaiTheme.Text,
                BackColor = TaiTheme.Surface,
                SelectionBackColor = TaiTheme.AccentSoft,
                SelectionForeColor = TaiTheme.Text,
                Font = new Font("Segoe UI", 10F),
                Padding = new Padding(6, 3, 6, 3)
            };
            grid.RowTemplate.Height = 30;
            UIHelper.EnableDoubleBuffer(grid);
            return grid;
        }
    }
}
