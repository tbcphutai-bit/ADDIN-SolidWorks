using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using ADDIN.Commands.DxfChecking;

namespace ADDIN.UI
{
    // A modern progress modal synchronized with TaiTheme: smooth rounded styling,
    // clear step information, clean path cards and custom-painted progress bar.
    internal sealed class DxfBomCheckProgressForm : Form
    {
        readonly Label status = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        readonly TextBox folderPath = CreatePathBox("txtDxfSourceDirectory"), currentPath = CreatePathBox("txtCurrentDxfPath");
        readonly ProgressBar progress = new ProgressBar { Visible = false };
        readonly ModernProgressBar modernProgress = new ModernProgressBar { Dock = DockStyle.Fill };
        readonly Label stepBadge = new Label();
        readonly ToolTip toolTip = new ToolTip();
        int completedRows, totalRows;

        internal DxfBomCheckProgressForm(string directory)
        {
            Text = "TAI TOOL — Đang kiểm tra DXF ↔ DRAWING";
            ClientSize = new Size(820, 310);
            Font = new Font("Segoe UI", 10F);
            BackColor = TaiTheme.Page;
            ForeColor = TaiTheme.Text;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            ControlBox = false;
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            DoubleBuffered = true;

            var outer = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12),
                BackColor = TaiTheme.Page
            };
            Controls.Add(outer);

            var card = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = TaiTheme.Surface,
                Padding = new Padding(1)
            };
            UIHelper.EnableDoubleBuffer(card);
            card.Paint += (s, e) =>
            {
                e.Graphics.Clear(TaiTheme.Page);
                TaiTheme.Prepare(e.Graphics);
                Rectangle frame = new Rectangle(0, 0, card.Width - 1, card.Height - 1);
                TaiTheme.FillRound(e.Graphics, frame, 8, TaiTheme.Surface, TaiTheme.Border);
            };
            outer.Controls.Add(card);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                BackColor = TaiTheme.Surface,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); // Header
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); // Status
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62)); // Folder
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62)); // Current file
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 60)); // Progress & Footer
            card.Controls.Add(root);

            // Row 0: Dark Navy Header Banner
            var header = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(30, 41, 59),
                Margin = Padding.Empty,
                Padding = new Padding(26, 0, 14, 0)
            };
            header.Paint += (s, e) =>
            {
                TaiTheme.Prepare(e.Graphics);
                int pillX = 14;
                int pillH = 18;
                int pillY = (header.Height - pillH) / 2;
                using (var brush = new SolidBrush(Color.FromArgb(56, 189, 248)))
                    e.Graphics.FillRectangle(brush, pillX, pillY, 3.5F, pillH);
            };

            var headerTitle = new Label
            {
                Text = "TAI TOOL — ĐANG KIỂM TRA DXF ↔ DRAWING",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Color.White,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.Transparent
            };

            stepBadge.Dock = DockStyle.Right;
            stepBadge.Width = 120;
            stepBadge.TextAlign = ContentAlignment.MiddleRight;
            stepBadge.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
            stepBadge.ForeColor = Color.FromArgb(203, 213, 225);
            stepBadge.BackColor = Color.Transparent;
            stepBadge.Text = "Đang khởi tạo…";

            header.Controls.Add(headerTitle);
            header.Controls.Add(stepBadge);
            root.Controls.Add(header, 0, 0);

            // Row 1: Status Line
            var statusPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(16, 6, 16, 0),
                BackColor = TaiTheme.Surface
            };
            status.Font = new Font("Segoe UI", 11.5F, FontStyle.Bold);
            status.ForeColor = TaiTheme.Accent;
            status.Text = "Đang lập danh sách DXF trong thư mục đã chọn…";
            statusPanel.Controls.Add(status);
            root.Controls.Add(statusPanel, 0, 1);

            // Row 2: Folder Path Card
            var folderSection = CreatePathSection("Thư mục DXF đã chọn", folderPath, TaiIcon.FileSearch, toolTip);
            folderPath.Text = directory ?? "";
            toolTip.SetToolTip(folderPath, folderPath.Text);
            root.Controls.Add(folderSection, 0, 2);

            // Row 3: Current DXF File Card
            var currentSection = CreatePathSection("File DXF đang kiểm tra", currentPath, TaiIcon.Compare, toolTip);
            currentPath.Text = "Đang chờ xác định file DXF…";
            toolTip.SetToolTip(currentPath, currentPath.Text);
            root.Controls.Add(currentSection, 0, 3);

            // Row 4: Modern Progress Bar & Cancel Hint
            var footer = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = Padding.Empty,
                Padding = new Padding(16, 10, 16, 10),
                BackColor = TaiTheme.Surface
            };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));

            footer.Controls.Add(modernProgress, 0, 0);

            var cancelNote = new Label
            {
                Text = "Nhấn Cancel trên Drawing BOM để dừng",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight,
                Font = new Font("Segoe UI", 9F),
                ForeColor = TaiTheme.TextMuted
            };
            footer.Controls.Add(cancelNote, 1, 0);
            root.Controls.Add(footer, 0, 4);

            Shown += (s, e) =>
            {
                folderPath.SelectionLength = 0;
                currentPath.SelectionLength = 0;
                modernProgress.Focus();
            };

            Controls.Add(progress); // Kept invisible for compatibility
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
            modernProgress.Maximum = Math.Max(1, total);
            modernProgress.Value = 0;
            stepBadge.Text = "0 / " + total;
            status.Text = "Đang bắt đầu kiểm tra " + total + " chi tiết…";
            Refresh();
        }

        internal void UpdateProgress(int current, int total)
        {
            completedRows = current;
            totalRows = total;
            progress.Maximum = Math.Max(1, total);
            progress.Value = Math.Min(progress.Maximum, Math.Max(0, current));
            modernProgress.Maximum = Math.Max(1, total);
            modernProgress.Value = Math.Min(modernProgress.Maximum, Math.Max(0, current));
            stepBadge.Text = current + " / " + total;
            status.Text = current >= total ? "Đang hoàn tất báo cáo…" : "Đã xử lý " + current + "/" + total + " dòng BOM";
            Refresh();
        }

        internal void SetCurrentFile(DxfBomCheckItem item)
        {
            if (IsDisposed || item == null) return;
            bool resolved = !string.IsNullOrWhiteSpace(item.DxfPath);
            int currentStep = Math.Min(completedRows + 1, totalRows);
            stepBadge.Text = currentStep + " / " + totalRows;
            status.Text = (resolved ? "Đang kiểm tra " : "Đang tìm DXF ") + currentStep + "/" + totalRows + " · 図番 " + item.PartNumber;
            currentPath.Text = resolved ? item.DxfPath : "Chưa xác định file DXF; đang đọc Drawing của 図番 " + item.PartNumber + ".";
            toolTip.SetToolTip(currentPath, currentPath.Text);
            Refresh();
        }

        internal static TextBox CreatePathBox(string name)
        {
            return new TextBox
            {
                Name = name,
                Dock = DockStyle.Fill,
                Multiline = false,
                ReadOnly = true,
                TabStop = false,
                BackColor = Color.FromArgb(248, 250, 252),
                ForeColor = TaiTheme.Text,
                BorderStyle = BorderStyle.None,
                Font = new Font("Segoe UI", 9.5F)
            };
        }

        private static Panel CreatePathSection(string labelText, TextBox box, TaiIcon icon, ToolTip tip)
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(16, 2, 16, 2),
                BackColor = TaiTheme.Surface
            };

            var label = new Label
            {
                Text = labelText,
                Dock = DockStyle.Top,
                Height = 18,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = TaiTheme.TextMuted,
                TextAlign = ContentAlignment.BottomLeft
            };
            panel.Controls.Add(label);

            var boxCard = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(8, 4, 8, 4),
                BackColor = Color.FromArgb(248, 250, 252)
            };
            UIHelper.EnableDoubleBuffer(boxCard);
            boxCard.Paint += (s, e) =>
            {
                TaiTheme.Prepare(e.Graphics);
                Rectangle rect = new Rectangle(0, 0, boxCard.Width - 1, boxCard.Height - 1);
                TaiTheme.FillRound(e.Graphics, rect, 6, Color.FromArgb(248, 250, 252), TaiTheme.Border);

                // Draw leading vector icon
                Rectangle iconRect = new Rectangle(8, (boxCard.Height - 16) / 2, 16, 16);
                TaiIcons.Draw(e.Graphics, icon, iconRect, TaiTheme.Accent);
            };

            var innerBox = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(22, 2, 4, 2),
                BackColor = Color.Transparent
            };
            box.Dock = DockStyle.Fill;
            innerBox.Controls.Add(box);
            boxCard.Controls.Add(innerBox);
            panel.Controls.Add(boxCard);

            boxCard.BringToFront();
            if (tip != null) tip.SetToolTip(boxCard, box.Text);
            return panel;
        }
    }

    // A modern progress bar control following the TaiTheme rounded pill design system.
    internal sealed class ModernProgressBar : Control
    {
        int value;
        int maximum = 100;
        int minimum = 0;

        public int Minimum
        {
            get { return minimum; }
            set { minimum = value; Invalidate(); }
        }

        public int Maximum
        {
            get { return maximum; }
            set { maximum = Math.Max(minimum + 1, value); Invalidate(); }
        }

        public int Value
        {
            get { return value; }
            set { this.value = Math.Max(minimum, Math.Min(maximum, value)); Invalidate(); }
        }

        public ModernProgressBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            DoubleBuffered = true;
            Height = 22;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            TaiTheme.Prepare(g);

            Rectangle track = new Rectangle(0, 0, Width - 1, Height - 1);
            int radius = Height / 2;
            TaiTheme.FillRound(g, track, radius, Color.FromArgb(231, 235, 241), TaiTheme.Border);

            float pct = maximum > minimum ? (float)(value - minimum) / (maximum - minimum) : 0F;
            int fillWidth = (int)Math.Round(track.Width * Math.Min(1F, Math.Max(0F, pct)));
            if (fillWidth >= radius)
            {
                Rectangle fillRect = new Rectangle(0, 0, fillWidth, Height - 1);
                using (var brush = new LinearGradientBrush(track, TaiTheme.Accent, Color.FromArgb(41, 128, 235), LinearGradientMode.Horizontal))
                using (var path = UIHelper.GetRoundPath(fillRect, radius))
                {
                    g.FillPath(brush, path);
                }
            }
            else if (fillWidth > 0)
            {
                Rectangle fillRect = new Rectangle(0, 0, fillWidth, Height - 1);
                using (var brush = new SolidBrush(TaiTheme.Accent))
                    g.FillRectangle(brush, fillRect);
            }

            int percentInt = (int)Math.Round(pct * 100F);
            string text = percentInt + "% (" + value + "/" + maximum + ")";
            using (var font = new Font("Segoe UI", 9F, FontStyle.Bold))
            {
                Color textColor = pct > 0.55F ? Color.White : TaiTheme.Text;
                TextRenderer.DrawText(g, text, font, track, textColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
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

        // Modernized header & ribbon elements
        readonly DxfStatusPillButton pillAll, pillPass, pillCheck, pillWarn, pillFail, pillError;
        readonly Label selectedBannerTitle = new Label();
        readonly Label selectedDxfPathLabel = new Label();
        readonly Panel contextBanner = new Panel();
        readonly Panel calloutCard = new Panel();
        readonly Label calloutTitle = new Label();
        readonly Label bomCardBadge = new Label();
        readonly Label previewCardBadge = new Label();
        readonly Label issueCardBadge = new Label();
        ModernButtonTone currentCalloutTone = ModernButtonTone.Success;
        string activeFilter;

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
            Size = new Size(1420, 850);
            MinimumSize = new Size(1100, 680);
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 10F);
            BackColor = TaiTheme.Page;
            ForeColor = TaiTheme.Text;
            DoubleBuffered = true;

            rows = Grid();
            issues = Grid();

            fit = CreateButton("Toàn bộ", TaiIcon.FitView, ModernButtonTone.Accent, 105);
            focus = CreateButton("Vị trí lỗi", TaiIcon.Measure, ModernButtonTone.Danger, 115);
            drawingOnly = CreateButton("Drawing", TaiIcon.Sheet, ModernButtonTone.Neutral, 105);
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
                Padding = new Padding(12, 10, 12, 8),
                ColumnCount = 1,
                RowCount = 4,
                BackColor = TaiTheme.Page
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 60)); // 0: Top Ribbon
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72)); // 1: Selected Item Banner
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // 2: 3 Cards Workbench
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46)); // 3: Bottom Actions
            Controls.Add(root);

            // Row 0: Top Header Ribbon Card (Branded Icon + Title + Status Pill Badges)
            var ribbonCard = CreateCardPanel(new Padding(12, 6, 12, 6));
            var ribbon = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = TaiTheme.Surface,
                Margin = Padding.Empty
            };
            ribbon.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 380));
            ribbon.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            ribbon.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var brandPanel = new Panel { Dock = DockStyle.Fill, BackColor = TaiTheme.Surface, Margin = Padding.Empty };
            brandPanel.Paint += PaintBrandHeader;
            ribbon.Controls.Add(brandPanel, 0, 0);

            var filterBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                BackColor = TaiTheme.Surface,
                Margin = Padding.Empty,
                Padding = new Padding(0, 7, 4, 0)
            };

            pillAll = new DxfStatusPillButton("Tổng", 0, null, TaiTheme.Accent, TaiTheme.AccentSoft, TaiTheme.AccentBorder);
            pillPass = new DxfStatusPillButton("PASS", 0, "PASS", TaiTheme.Success, TaiTheme.SuccessSoft, TaiTheme.SuccessBorder);
            pillCheck = new DxfStatusPillButton("CHECK", 0, "CHECK", TaiTheme.Warning, TaiTheme.WarningSoft, TaiTheme.WarningBorder);
            pillWarn = new DxfStatusPillButton("WARN", 0, "WARN", TaiTheme.Warning, TaiTheme.WarningSoft, TaiTheme.WarningBorder);
            pillFail = new DxfStatusPillButton("FAIL", 0, "FAIL", TaiTheme.Danger, TaiTheme.DangerSoft, TaiTheme.DangerBorder);
            pillError = new DxfStatusPillButton("ERROR", 0, "ERROR", TaiTheme.Danger, TaiTheme.DangerSoft, TaiTheme.DangerBorder);

            pillAll.Click += (s, e) => ApplyFilter(null);
            pillPass.Click += (s, e) => ApplyFilter(activeFilter == "PASS" ? null : "PASS");
            pillCheck.Click += (s, e) => ApplyFilter(activeFilter == "CHECK" ? null : "CHECK");
            pillWarn.Click += (s, e) => ApplyFilter(activeFilter == "WARN" ? null : "WARN");
            pillFail.Click += (s, e) => ApplyFilter(activeFilter == "FAIL" ? null : "FAIL");
            pillError.Click += (s, e) => ApplyFilter(activeFilter == "ERROR" ? null : "ERROR");

            // Added in Right-To-Left order
            filterBar.Controls.Add(pillError);
            filterBar.Controls.Add(pillFail);
            filterBar.Controls.Add(pillWarn);
            filterBar.Controls.Add(pillCheck);
            filterBar.Controls.Add(pillPass);
            filterBar.Controls.Add(pillAll);
            ribbon.Controls.Add(filterBar, 1, 0);

            ribbonCard.Controls.Add(ribbon);
            root.Controls.Add(ribbonCard, 0, 0);

            // Row 1: Selected Item Dark Navy Banner
            contextBanner.Dock = DockStyle.Fill;
            contextBanner.BackColor = Color.FromArgb(30, 41, 59);
            contextBanner.Padding = new Padding(26, 8, 120, 8);
            contextBanner.Margin = new Padding(0, 6, 0, 6);
            UIHelper.EnableDoubleBuffer(contextBanner);
            contextBanner.Paint += PaintDetailBanner;
            contextBanner.Resize += (s, e) => contextBanner.Invalidate();

            selectedBannerTitle.Dock = DockStyle.Top;
            selectedBannerTitle.Height = 24;
            selectedBannerTitle.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            selectedBannerTitle.ForeColor = Color.White;
            selectedBannerTitle.AutoEllipsis = true;
            selectedBannerTitle.BackColor = Color.Transparent;

            var dxfPathWrap = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                Padding = new Padding(0, 3, 0, 0)
            };
            selectedDxfPathLabel.Dock = DockStyle.Fill;
            selectedDxfPathLabel.Font = new Font("Segoe UI", 9.25F, FontStyle.Regular);
            selectedDxfPathLabel.ForeColor = Color.FromArgb(148, 163, 184);
            selectedDxfPathLabel.AutoEllipsis = true;
            selectedDxfPathLabel.BackColor = Color.Transparent;
            dxfPathWrap.Controls.Add(selectedDxfPathLabel);

            contextBanner.Controls.Add(dxfPathWrap);
            contextBanner.Controls.Add(selectedBannerTitle);
            root.Controls.Add(contextBanner, 0, 1);

            // Row 2: 3 Cards Workbench
            var workbench = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                Margin = Padding.Empty,
                BackColor = TaiTheme.Page
            };
            workbench.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 435));
            workbench.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
            workbench.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
            root.Controls.Add(workbench, 0, 2);

            // Card 1: BOM Rows (Generous column widths so no headers truncate to ...)
            AddColumn(rows, "bom", "BOM", 60, DataGridViewContentAlignment.MiddleCenter);
            AddColumn(rows, "part", "図番 (Part)", 120, DataGridViewContentAlignment.MiddleLeft);
            var colSheetView = AddColumn(rows, "sheetView", "シート/ビュー", 125, DataGridViewContentAlignment.MiddleLeft);
            colSheetView.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            AddColumn(rows, "result", "Kết quả", 115, DataGridViewContentAlignment.MiddleCenter);
            rows.CellPainting += PaintBomGridCell;
            workbench.Controls.Add(CreateModernCard("DANH SÁCH KIỂM TRA (BOM)", rows, bomCardBadge), 0, 0);

            // Card 2: Geometry Preview
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
                Padding = new Padding(2, 4, 2, 4),
                Margin = Padding.Empty
            };
            previewButtons.Controls.AddRange(new Control[] { fit, focus, drawingOnly, dxfOnly });
            previewArea.Controls.Add(previewButtons, 0, 0);
            previewArea.Controls.Add(preview, 0, 1);

            var hint = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(248, 250, 252),
                Padding = new Padding(8, 0, 8, 0)
            };
            hint.Paint += PaintPreviewLegend;
            previewArea.Controls.Add(hint, 0, 2);
            workbench.Controls.Add(CreateModernCard("Biên dạng Drawing / DXF", previewArea, previewCardBadge), 1, 0);

            // Card 3: Issues & Executive Callout
            AddColumn(issues, "code", "Mã lỗi", 85, DataGridViewContentAlignment.MiddleCenter);
            AddColumn(issues, "dev", "Lệch (mm)", 95, DataGridViewContentAlignment.MiddleRight);
            var colDesc = AddColumn(issues, "desc", "Chi tiết lỗi", 120, DataGridViewContentAlignment.MiddleLeft);
            colDesc.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            issues.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            issues.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            issues.Paint += PaintIssuesGridEmptyState;

            // Callout Card replacing raw multiline TextBox with vertical scrollbars
            calloutCard.Dock = DockStyle.Fill;
            calloutCard.Padding = new Padding(16, 8, 12, 8);
            calloutCard.Margin = Padding.Empty;
            UIHelper.EnableDoubleBuffer(calloutCard);
            calloutCard.Paint += PaintCalloutCard;

            calloutTitle.Dock = DockStyle.Top;
            calloutTitle.Height = 22;
            calloutTitle.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
            calloutTitle.ForeColor = TaiTheme.Success;
            calloutTitle.TextAlign = ContentAlignment.MiddleLeft;
            calloutTitle.Text = "✔ KHÔNG CÓ SAI LỆCH";
            calloutTitle.BackColor = Color.Transparent;

            detail.Dock = DockStyle.Fill;
            detail.Multiline = true;
            detail.ReadOnly = true;
            detail.WordWrap = true;
            detail.ScrollBars = ScrollBars.None;
            detail.BackColor = TaiTheme.SuccessSoft;
            detail.ForeColor = TaiTheme.Text;
            detail.BorderStyle = BorderStyle.None;
            detail.Font = new Font("Segoe UI", 9.5F);

            calloutCard.Controls.Add(detail);
            calloutCard.Controls.Add(calloutTitle);

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
            issueArea.Controls.Add(calloutCard, 0, 1);
            workbench.Controls.Add(CreateModernCard("Kết quả & Vị trí lỗi", issueArea, issueCardBadge), 2, 0);

            // Row 3: Bottom Action Ribbon
            var bottomBar = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = Padding.Empty,
                Padding = new Padding(4, 6, 4, 0),
                BackColor = TaiTheme.Page
            };
            bottomBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottomBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));

            var bottomInfo = new Label
            {
                Text = "Khớp biên dạng tỷ lệ 1:1 mm theo hệ tọa độ View gốc của Drawing.",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 9F),
                ForeColor = TaiTheme.TextMuted
            };
            bottomBar.Controls.Add(bottomInfo, 0, 0);

            var actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Padding = Padding.Empty,
                Margin = Padding.Empty
            };
            close.Click += (s, e) => Close();
            CancelButton = close;
            actions.Controls.Add(close);
            actions.Controls.Add(openReport);
            bottomBar.Controls.Add(actions, 1, 0);
            root.Controls.Add(bottomBar, 0, 3);

            // Event handlers
            rows.SelectionChanged += (s, e) => { if (!setting) ShowSelected(); };
            issues.SelectionChanged += (s, e) => { if (!setting) ShowIssue(true); };
            fit.Click += (s, e) => ShowOverlay(null, false);
            focus.Click += (s, e) => ShowIssue(true);
            drawingOnly.Click += (s, e) => { preview.Set(drawingGeometry, null); UpdateCalloutDetail("Drawing gốc theo hệ View 1:1 mm.", ModernButtonTone.Neutral); };
            dxfOnly.Click += (s, e) => { if (selected != null && selected.PreviewDxfEdges != null) preview.Set(PreviewFile("DXF gốc", selected.PreviewDxfEdges, new Translation2D()), null); UpdateCalloutDetail("DXF gốc giữ nguyên tọa độ và kích thước.", ModernButtonTone.Neutral); };
            openReport.Click += (s, e) => { if (batch != null && File.Exists(batch.ReportPath)) try { Process.Start(new ProcessStartInfo(batch.ReportPath) { UseShellExecute = true }); } catch (Exception error) { UpdateCalloutDetail("Không mở được báo cáo: " + error.Message, ModernButtonTone.Danger); } };

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
            activeFilter = null;
            summary.Text = string.Format(
                "{0} PASS   ·   {1} CHECK   ·   {2} WARN   ·   {3} FAIL   ·   {4} ERROR",
                batch.Passed, batch.Checks, batch.Warnings, batch.Failed, batch.Errors);
            summary.ForeColor = (batch.Failed > 0 || batch.Errors > 0)
                ? TaiTheme.Danger
                : (batch.Warnings > 0 || batch.Checks > 0 ? TaiTheme.Warning : TaiTheme.Success);

            pillAll.Count = batch.Items.Count;
            pillPass.Count = batch.Passed;
            pillCheck.Count = batch.Checks;
            pillWarn.Count = batch.Warnings;
            pillFail.Count = batch.Failed;
            pillError.Count = batch.Errors;

            openReport.Enabled = !string.IsNullOrWhiteSpace(batch.ReportPath) && File.Exists(batch.ReportPath);
            ApplyFilter(null);
        }

        void ApplyFilter(string statusFilter)
        {
            activeFilter = statusFilter;
            pillAll.IsActive = string.IsNullOrEmpty(activeFilter);
            pillPass.IsActive = activeFilter == "PASS";
            pillCheck.IsActive = activeFilter == "CHECK";
            pillWarn.IsActive = activeFilter == "WARN";
            pillFail.IsActive = activeFilter == "FAIL";
            pillError.IsActive = activeFilter == "ERROR";

            PopulateRows(activeFilter);
        }

        void PopulateRows(string filterStatus)
        {
            setting = true;
            try
            {
                rows.Rows.Clear();
                issues.Rows.Clear();
                preview.Set(null, null);
                selected = null;
                drawingGeometry = dxfGeometry = null;

                if (batch != null)
                {
                    foreach (var item in batch.Items)
                    {
                        if (filterStatus != null && item.Status != filterStatus) continue;

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

                    bomCardBadge.Text = (filterStatus == null ? "" : filterStatus + ": ") + rows.Rows.Count + " / " + batch.Items.Count + " chi tiết";

                    rows.ClearSelection();
                    int chosen = -1;
                    for (int i = 0; i < rows.Rows.Count; i++)
                    {
                        var item = rows.Rows[i].Tag as DxfBomCheckItem;
                        if (item != null && item.Result != null && item.PreviewDrawingEdges != null && item.PreviewDrawingEdges.Count > 0)
                        {
                            chosen = i;
                            break;
                        }
                    }
                    if (chosen < 0 && rows.Rows.Count > 0) chosen = 0;
                    if (chosen >= 0)
                    {
                        rows.Rows[chosen].Selected = true;
                        rows.CurrentCell = rows.Rows[chosen].Cells[0];
                    }
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

                if (selected == null)
                {
                    selectedBannerTitle.Text = "Chọn một chi tiết trong bảng BOM để xem kết quả kiểm tra.";
                    selectedDxfPathLabel.Text = "Nguồn DXF: " + (batch == null ? "—" : batch.DxfSourceMode == "AUTO" ? "Tự động dò theo Drawing / BOM" : batch.DxfSourcePath);
                    checkedDxfPath.Text = selectedDxfPathLabel.Text;
                    context.Text = selectedBannerTitle.Text;
                    previewCardBadge.Text = "";
                    issueCardBadge.Text = "";
                    UpdateCalloutDetail("Chưa chọn chi tiết.", ModernButtonTone.Neutral);
                    contextBanner.Invalidate();
                    return;
                }

                selectedBannerTitle.Text = "図番 (Part): " + selected.PartNumber + "   ·   Bản vẽ: " + Path.GetFileName(selected.DrawingPath) + "   ·   シート / ビュー: " + (selected.Sheet ?? "") + " / " + (selected.View ?? "");
                context.Text = selectedBannerTitle.Text;

                string dxfSource = batch != null && batch.DxfSourceMode == "AUTO" ? "Tự động dò theo Drawing / BOM" : (batch != null ? batch.DxfSourcePath : "");
                string dxfFileText = string.IsNullOrWhiteSpace(selected.DxfPath)
                    ? "Chưa tìm thấy file DXF tương ứng (Nguồn: " + dxfSource + ")"
                    : "File DXF: " + selected.DxfPath;
                selectedDxfPathLabel.Text = dxfFileText;
                checkedDxfPath.Text = dxfFileText;
                toolTips.SetToolTip(selectedDxfPathLabel, dxfFileText);

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

                issueCardBadge.Text = issues.Rows.Count == 0 ? "0 sai lệch" : issues.Rows.Count + " vị trí";
                previewCardBadge.Text = PreviewIsAligned ? "Đã căn X/Y" : (drawingGeometry != null ? "Drawing 1:1" : "");

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
            contextBanner.Invalidate();
            ShowOverlay(null, false);
            ShowIssue(false);
        }

        void ShowIssue(bool focusLocation)
        {
            var mismatch = issues.SelectedRows.Count == 0 ? null : issues.SelectedRows[0].Tag as GeometryMismatch;
            if (mismatch == null)
            {
                if (selected != null)
                {
                    bool isPass = selected.Status == "PASS";
                    string msg = !string.IsNullOrWhiteSpace(selected.Message) ? selected.Message
                        : isPass ? "Biên dạng giữa Drawing và DXF khớp hoàn toàn. Không phát hiện sai lệch."
                        : "Không tìm thấy file DXF hoặc chi tiết có lỗi.";
                    UpdateCalloutDetail(msg, isPass ? ModernButtonTone.Success : ModernButtonTone.Warning);
                }
                return;
            }

            string info = (mismatch.ReviewIssue ? "CHECK-" : "ERR-") + mismatch.Id.ToString("00") + " · " + mismatch.Type + "\r\n"
                + mismatch.Description + "\r\n"
                + "Drawing: " + (mismatch.HasDrawingPosition ? PointText(mismatch.DrawingPosition) : "—") + "\r\n"
                + "DXF" + (PreviewIsAligned ? " đã dịch" : " gốc") + ": " + (mismatch.HasDxfPosition ? PointText(mismatch.DxfPosition) : "—");
            UpdateCalloutDetail(info, mismatch.ReviewIssue ? ModernButtonTone.Warning : ModernButtonTone.Danger);
            ShowOverlay(mismatch, focusLocation);
        }

        void UpdateCalloutDetail(string text, ModernButtonTone tone)
        {
            detail.Text = text ?? "";
            currentCalloutTone = tone;
            Color fill = tone == ModernButtonTone.Danger ? TaiTheme.DangerSoft
                : tone == ModernButtonTone.Warning ? TaiTheme.WarningSoft
                : tone == ModernButtonTone.Neutral ? TaiTheme.Page
                : TaiTheme.SuccessSoft;
            Color titleColor = tone == ModernButtonTone.Danger ? TaiTheme.Danger
                : tone == ModernButtonTone.Warning ? TaiTheme.Warning
                : tone == ModernButtonTone.Neutral ? TaiTheme.TextMuted
                : TaiTheme.Success;

            calloutTitle.ForeColor = titleColor;
            calloutTitle.Text = tone == ModernButtonTone.Danger ? "✖ LỖI SAI LỆCH HÌNH HỌC"
                : tone == ModernButtonTone.Warning ? "⚠ CẦN KIỂM TRA (CHECK)"
                : tone == ModernButtonTone.Neutral ? "ℹ THÔNG TIN BIÊN DẠNG"
                : "✔ ĐẠT — KHÔNG CÓ SAI LỆCH";
            detail.BackColor = fill;
            calloutCard.Invalidate();
        }

        void ShowOverlay(GeometryMismatch mismatch, bool focusLocation)
        {
            if (drawingGeometry == null || dxfGeometry == null || selected == null || selected.Result == null || !selected.Result.AlignmentFound)
            {
                preview.Set(drawingGeometry ?? dxfGeometry, null);
                ShowCheckLocations(drawingGeometry != null, drawingGeometry == null);
                if (selected != null && selected.Result != null && !selected.Result.AlignmentFound)
                    UpdateCalloutDetail(selected.Message + "\r\nChưa căn được X/Y. Dùng nút 'Drawing' hoặc 'DXF gốc' để xem từng biên dạng.", ModernButtonTone.Warning);
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

        private void PaintBrandHeader(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            TaiTheme.Prepare(g);

            Rectangle iconRect = new Rectangle(2, 6, 36, 36);
            TaiTheme.FillRound(g, iconRect, 8, TaiTheme.Accent, TaiTheme.Accent);
            TaiIcons.Draw(g, TaiIcon.Compare, new Rectangle(iconRect.X + 6, iconRect.Y + 6, 24, 24), Color.White);

            using (var titleFont = new Font("Segoe UI", 12F, FontStyle.Bold))
            using (var subFont = new Font("Segoe UI", 8.5F, FontStyle.Regular))
            {
                TextRenderer.DrawText(g, "SO SÁNH DXF ↔ DRAWING", titleFont, new Point(48, 6), TaiTheme.Text,
                    TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, "Đối chiếu biên dạng hình học giữa file DXF và Drawing", subFont, new Point(49, 29), TaiTheme.TextMuted,
                    TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            }
        }

        private void PaintDetailBanner(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(TaiTheme.Page);
            TaiTheme.Prepare(g);

            Rectangle frame = new Rectangle(0, 0, contextBanner.Width - 1, contextBanner.Height - 1);
            TaiTheme.FillRound(g, frame, 8, Color.FromArgb(30, 41, 59), Color.FromArgb(51, 65, 85));

            // Cyan Accent Pill
            int pillX = 14;
            int pillH = 26;
            int pillY = (contextBanner.Height - pillH) / 2;
            using (var brush = new SolidBrush(Color.FromArgb(56, 189, 248)))
                g.FillRectangle(brush, pillX, pillY, 3.5F, pillH);

            if (selected != null)
            {
                Rectangle badgeRect = new Rectangle(contextBanner.Width - 110, (contextBanner.Height - 28) / 2, 96, 28);
                DrawStatusPill(g, badgeRect, selected.Status);
            }
        }

        private void PaintCalloutCard(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            TaiTheme.Prepare(g);

            Color fill = currentCalloutTone == ModernButtonTone.Danger ? TaiTheme.DangerSoft
                : currentCalloutTone == ModernButtonTone.Warning ? TaiTheme.WarningSoft
                : currentCalloutTone == ModernButtonTone.Neutral ? TaiTheme.Page
                : TaiTheme.SuccessSoft;
            Color border = currentCalloutTone == ModernButtonTone.Danger ? TaiTheme.DangerBorder
                : currentCalloutTone == ModernButtonTone.Warning ? TaiTheme.WarningBorder
                : currentCalloutTone == ModernButtonTone.Neutral ? TaiTheme.Border
                : TaiTheme.SuccessBorder;
            Color stripe = currentCalloutTone == ModernButtonTone.Danger ? TaiTheme.Danger
                : currentCalloutTone == ModernButtonTone.Warning ? TaiTheme.Warning
                : currentCalloutTone == ModernButtonTone.Neutral ? TaiTheme.TextMuted
                : TaiTheme.Success;

            if (detail.BackColor != fill) detail.BackColor = fill;
            e.Graphics.Clear(TaiTheme.Surface);
            Rectangle rect = new Rectangle(0, 0, calloutCard.Width - 1, calloutCard.Height - 1);
            TaiTheme.FillRound(g, rect, 8, fill, border);

            // Left solid accent bar (5px thick)
            using (var stripeBrush = new SolidBrush(stripe))
                g.FillRectangle(stripeBrush, new Rectangle(1, 6, 5, Math.Max(4, calloutCard.Height - 12)));
        }

        private void PaintBomGridCell(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0) return;
            bool isSelected = (e.State & DataGridViewElementStates.Selected) == DataGridViewElementStates.Selected;

            if (e.ColumnIndex == 0)
            {
                e.Paint(e.CellBounds, DataGridViewPaintParts.All);
                if (isSelected)
                {
                    using (var accentBrush = new SolidBrush(TaiTheme.Accent))
                        e.Graphics.FillRectangle(accentBrush, e.CellBounds.X, e.CellBounds.Y, 3.5F, e.CellBounds.Height);
                }
                e.Handled = true;
            }
            else if (e.ColumnIndex == 3)
            {
                e.PaintBackground(e.CellBounds, true);
                if (isSelected)
                {
                    using (var bgBrush = new SolidBrush(TaiTheme.AccentSoft))
                        e.Graphics.FillRectangle(bgBrush, e.CellBounds);
                }

                var item = rows.Rows[e.RowIndex].Tag as DxfBomCheckItem;
                string status = item?.Status ?? "";
                int pillW = Math.Min(118, e.CellBounds.Width - 8);
                int pillH = 24;
                Rectangle pillRect = new Rectangle(
                    e.CellBounds.X + (e.CellBounds.Width - pillW) / 2,
                    e.CellBounds.Y + (e.CellBounds.Height - pillH) / 2,
                    pillW,
                    pillH);

                DrawStatusPill(e.Graphics, pillRect, status);
                e.Handled = true;
            }
        }

        private void PaintIssuesGridEmptyState(object sender, PaintEventArgs e)
        {
            if (issues.Rows.Count == 0)
            {
                TaiTheme.Prepare(e.Graphics);
                using (var fontTitle = new Font("Segoe UI", 10F, FontStyle.Bold))
                using (var fontSub = new Font("Segoe UI", 9F, FontStyle.Regular))
                {
                    string line1 = selected == null ? "Chưa chọn chi tiết để xem lỗi" : "✔ Không có sai lệch hình học";
                    string line2 = selected == null ? "Chọn một dòng trong bảng BOM bên trái" : "Biên dạng Drawing và DXF khớp hoàn toàn";
                    Color c1 = selected == null ? TaiTheme.TextMuted : TaiTheme.Success;
                    Color c2 = TaiTheme.TextFaint;

                    int midY = issues.Height / 2 - 20;
                    Rectangle r1 = new Rectangle(10, midY, issues.Width - 20, 22);
                    Rectangle r2 = new Rectangle(10, midY + 22, issues.Width - 20, 20);
                    TextRenderer.DrawText(e.Graphics, line1, fontTitle, r1, c1, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    TextRenderer.DrawText(e.Graphics, line2, fontSub, r2, c2, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
            }
        }

        private static void PaintPreviewLegend(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            TaiTheme.Prepare(g);

            int y = 8;
            int x = 8;

            // Green dot: Drawing
            using (var b = new SolidBrush(Color.FromArgb(22, 125, 76)))
                g.FillEllipse(b, x, y + 2, 8, 8);
            x += 12;
            using (var font = new Font("Segoe UI", 8.5F, FontStyle.Bold))
            {
                TextRenderer.DrawText(g, "Drawing", font, new Point(x, y), TaiTheme.Text);
                x += 54;

                // Orange dot: DXF đã dịch
                using (var b = new SolidBrush(Color.FromArgb(217, 119, 6)))
                    g.FillEllipse(b, x, y + 2, 8, 8);
                x += 12;
                TextRenderer.DrawText(g, "DXF đã dịch", font, new Point(x, y), TaiTheme.Text);
                x += 76;

                // Hint
                using (var subFont = new Font("Segoe UI", 8.25F, FontStyle.Regular))
                {
                    TextRenderer.DrawText(g, "·   Lăn chuột: Thu phóng   ·   Chuột giữa: Di chuyển", subFont, new Point(x, y + 1), TaiTheme.TextMuted);
                }
            }
        }

        private static void DrawStatusPill(Graphics g, Rectangle bounds, string status)
        {
            TaiTheme.Prepare(g);
            Color fill, border, fg;
            string text;
            switch (status)
            {
                case "PASS":
                    fill = TaiTheme.SuccessSoft;
                    border = TaiTheme.SuccessBorder;
                    fg = TaiTheme.Success;
                    text = "✔ Đạt (PASS)";
                    break;
                case "CHECK":
                    fill = TaiTheme.WarningSoft;
                    border = TaiTheme.WarningBorder;
                    fg = TaiTheme.Warning;
                    text = "⚠ CHECK";
                    break;
                case "WARN":
                    fill = TaiTheme.WarningSoft;
                    border = TaiTheme.WarningBorder;
                    fg = TaiTheme.Warning;
                    text = "⚠ Cảnh báo";
                    break;
                case "FAIL":
                    fill = TaiTheme.DangerSoft;
                    border = TaiTheme.DangerBorder;
                    fg = TaiTheme.Danger;
                    text = "✖ Lỗi (FAIL)";
                    break;
                case "ERROR":
                    fill = TaiTheme.DangerSoft;
                    border = TaiTheme.DangerBorder;
                    fg = TaiTheme.Danger;
                    text = "✖ Lỗi (ERROR)";
                    break;
                default:
                    fill = TaiTheme.Page;
                    border = TaiTheme.Border;
                    fg = TaiTheme.TextMuted;
                    text = string.IsNullOrEmpty(status) ? "—" : status;
                    break;
            }

            TaiTheme.FillRound(g, bounds, bounds.Height / 2, fill, border);

            int dotSize = 7;
            int dotX = bounds.X + 8;
            int dotY = bounds.Y + (bounds.Height - dotSize) / 2;
            using (var brush = new SolidBrush(fg))
                g.FillEllipse(brush, dotX, dotY, dotSize, dotSize);

            Rectangle textRect = new Rectangle(dotX + dotSize + 4, bounds.Y, bounds.Width - (dotSize + 14), bounds.Height);
            using (var font = new Font("Segoe UI", 9F, FontStyle.Bold))
            {
                TextRenderer.DrawText(g, text, font, textRect, fg,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            }
        }

        static Control CreateModernCard(string title, Control content, Label badgeLabel)
        {
            var card = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = TaiTheme.Surface,
                Margin = new Padding(3),
                Padding = new Padding(1)
            };
            UIHelper.EnableDoubleBuffer(card);
            card.Paint += (s, e) =>
            {
                e.Graphics.Clear(TaiTheme.Page);
                TaiTheme.Prepare(e.Graphics);
                Rectangle frame = new Rectangle(0, 0, card.Width - 1, card.Height - 1);
                TaiTheme.FillRound(e.Graphics, frame, 8, TaiTheme.Surface, TaiTheme.Border);
            };

            var inner = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = TaiTheme.Surface,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            inner.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            inner.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var header = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(30, 41, 59),
                Margin = Padding.Empty,
                Padding = new Padding(26, 0, 12, 0)
            };
            header.Paint += (s, e) =>
            {
                TaiTheme.Prepare(e.Graphics);
                int pillX = 14;
                int pillH = 18;
                int pillY = (header.Height - pillH) / 2;
                using (var brush = new SolidBrush(Color.FromArgb(56, 189, 248)))
                    e.Graphics.FillRectangle(brush, pillX, pillY, 3.5F, pillH);
            };

            var titleLbl = new Label
            {
                Text = title,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Color.White,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.Transparent
            };

            badgeLabel.Dock = DockStyle.Right;
            badgeLabel.Width = 140;
            badgeLabel.TextAlign = ContentAlignment.MiddleRight;
            badgeLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            badgeLabel.ForeColor = Color.FromArgb(203, 213, 225);
            badgeLabel.BackColor = Color.Transparent;

            header.Controls.Add(titleLbl);
            header.Controls.Add(badgeLabel);
            inner.Controls.Add(header, 0, 0);

            var contentWrap = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = TaiTheme.Surface,
                Padding = new Padding(6, 4, 6, 6),
                Margin = Padding.Empty
            };
            content.Dock = DockStyle.Fill;
            contentWrap.Controls.Add(content);
            inner.Controls.Add(contentWrap, 0, 1);

            card.Controls.Add(inner);
            return card;
        }

        private static Panel CreateCardPanel(Padding padding)
        {
            var card = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = TaiTheme.Surface,
                Padding = padding,
                Margin = Padding.Empty
            };
            UIHelper.EnableDoubleBuffer(card);
            card.Paint += (s, e) =>
            {
                e.Graphics.Clear(TaiTheme.Page);
                TaiTheme.Prepare(e.Graphics);
                Rectangle frame = new Rectangle(0, 0, card.Width - 1, card.Height - 1);
                TaiTheme.FillRound(e.Graphics, frame, 8, TaiTheme.Surface, TaiTheme.Border);
            };
            card.Resize += (s, e) => card.Invalidate();
            return card;
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
                Height = 32,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                Kind = ModernButtonKind.Standard,
                Tone = tone,
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 0, 6, 0)
            };
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
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = 38
            };
            grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(246, 248, 251),
                ForeColor = TaiTheme.TextMuted,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                Padding = new Padding(6, 4, 6, 4),
                WrapMode = DataGridViewTriState.False
            };
            grid.DefaultCellStyle = new DataGridViewCellStyle
            {
                ForeColor = TaiTheme.Text,
                BackColor = TaiTheme.Surface,
                SelectionBackColor = TaiTheme.AccentSoft,
                SelectionForeColor = TaiTheme.Text,
                Font = new Font("Segoe UI", 9.5F),
                Padding = new Padding(6, 3, 6, 3)
            };
            grid.RowTemplate.Height = 32;
            UIHelper.EnableDoubleBuffer(grid);
            return grid;
        }

        // An interactive status pill button in the top ribbon matching CompletedCheckReviewForm.
        private sealed class DxfStatusPillButton : Control
        {
            readonly string label;
            int count;
            readonly Color hue, softFill, softBorder;
            bool isActive, hovered;

            public string StatusKey { get; }

            public bool IsActive
            {
                get { return isActive; }
                set { if (isActive == value) return; isActive = value; Invalidate(); }
            }

            public int Count
            {
                get { return count; }
                set { if (count == value) return; count = value; Width = MeasureWidth(label, count); Invalidate(); }
            }

            public DxfStatusPillButton(string label, int count, string statusKey, Color hue, Color softFill, Color softBorder)
            {
                this.label = label;
                this.count = count;
                StatusKey = statusKey;
                this.hue = hue;
                this.softFill = softFill;
                this.softBorder = softBorder;
                Height = 32;
                Width = MeasureWidth(label, count);
                Cursor = Cursors.Hand;
                DoubleBuffered = true;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            }

            private static int MeasureWidth(string text, int num)
            {
                using (var f1 = new Font("Segoe UI", 9F, FontStyle.Regular))
                using (var f2 = new Font("Segoe UI", 10F, FontStyle.Bold))
                {
                    int w1 = TextRenderer.MeasureText(text, f1).Width;
                    int w2 = TextRenderer.MeasureText(num.ToString(), f2).Width;
                    return Math.Max(76, w1 + w2 + 34);
                }
            }

            protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered = true; Invalidate(); }
            protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = false; Invalidate(); }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.Clear(TaiTheme.Surface);
                TaiTheme.Prepare(g);

                Rectangle rect = new Rectangle(1, 1, Width - 3, Height - 3);
                Color fill = isActive ? softFill : hovered ? TaiTheme.Page : Color.FromArgb(249, 250, 252);
                Color border = isActive ? hue : hovered ? softBorder : TaiTheme.Border;

                TaiTheme.FillRound(g, rect, Height / 2, fill, border);
                if (isActive)
                {
                    Rectangle inner = new Rectangle(2, 2, Width - 5, Height - 5);
                    TaiTheme.FillRound(g, inner, Height / 2 - 1, Color.Transparent, Color.FromArgb(90, hue));
                }

                int dotSize = 7;
                int dotX = 10;
                int dotY = (Height - dotSize) / 2;
                using (var dotBrush = new SolidBrush(hue))
                    g.FillEllipse(dotBrush, dotX, dotY, dotSize, dotSize);

                using (var lblFont = new Font("Segoe UI", 9F, isActive ? FontStyle.Bold : FontStyle.Regular))
                using (var cntFont = new Font("Segoe UI", 9.5F, FontStyle.Bold))
                {
                    Color lblColor = isActive ? hue : TaiTheme.TextMuted;
                    Color cntColor = isActive ? hue : (count > 0 ? hue : TaiTheme.Text);

                    Size lblSize = TextRenderer.MeasureText(g, label, lblFont, Size.Empty, TextFormatFlags.NoPadding);
                    int textY = (Height - lblSize.Height) / 2;
                    TextRenderer.DrawText(g, label, lblFont, new Point(dotX + dotSize + 5, textY), lblColor,
                        TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

                    string cntText = count.ToString();
                    Size cntSize = TextRenderer.MeasureText(g, cntText, cntFont, Size.Empty, TextFormatFlags.NoPadding);
                    int cntY = (Height - cntSize.Height) / 2;
                    TextRenderer.DrawText(g, cntText, cntFont, new Point(Width - cntSize.Width - 11, cntY), cntColor,
                        TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                }
            }
        }
    }
}
