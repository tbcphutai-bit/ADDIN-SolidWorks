using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using ADDIN.Commands;

namespace ADDIN.UI
{
    // Reviewing a completed Drawing check never starts another check or reads a DXF.
    internal sealed class DrawingBomCheckReviewForm : Form
    {
        private readonly DrawingBatchCheckResult result;
        private readonly Action<DrawingCheckItemResult, DrawingBomFieldResult> navigate;
        private readonly DataGridView drawingItems = Grid("drawingItems");
        private readonly DataGridView drawingFields = Grid("drawingFields");
        private readonly Label selectedDrawing = TextLabel("Chọn Drawing để xem kết quả.", true);
        private readonly Label selectedSubtext = TextLabel("", false);
        private readonly Label itemCountBadge = new Label();
        private readonly Label fieldDetails = TextLabel("", false);
        private readonly Label calloutHeader = new Label();
        private readonly Label calloutBody = new Label();
        private readonly Label calloutFooter = new Label();
        private readonly Label status = TextLabel("", false);
        private readonly Panel calloutCard = new Panel();
        private readonly Panel detailBanner = new Panel();
        private readonly ModernButton openDrawing = Button("Mở Drawing", true, 140);
        private readonly ModernButton location = Button("Đến vị trí", false, 130);
        private readonly ModernButton report = Button("Mở báo cáo", false, 128);
        private readonly FilterPillButton pillAll;
        private readonly FilterPillButton pillOk;
        private readonly FilterPillButton pillNg;
        private readonly FilterPillButton pillWarning;
        private DrawingBomCheckStatus? activeFilter;
        private bool selecting;

        internal DrawingCheckItemResult SelectedItem
        {
            get { return drawingItems.CurrentRow?.Tag as DrawingCheckItemResult; }
        }

        internal DrawingBomFieldResult SelectedField
        {
            get { return drawingFields.CurrentRow?.Tag as DrawingBomFieldResult; }
        }

        internal DrawingBomCheckReviewForm(DrawingBatchCheckResult completed,
            Action<DrawingCheckItemResult, DrawingBomFieldResult> openLocation)
        {
            result = completed ?? throw new ArgumentNullException(nameof(completed));
            navigate = openLocation;
            Name = "DrawingBomCheckReviewForm";
            Text = "TAI TOOL — KẾT QUẢ CHECK DRAWING";
            Size = new Size(1300, 820);
            MinimumSize = new Size(1020, 640);
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 10F, FontStyle.Regular);
            ForeColor = TaiTheme.Text;
            BackColor = TaiTheme.Page;
            DoubleBuffered = true;

            ConfigureGrids();

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(12, 10, 12, 8),
                BackColor = TaiTheme.Page
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));

            // 1. Top Header Card (Style C: Modern SaaS Inspector Ribbon)
            var ribbonCard = CreateCardPanel(new Padding(12, 6, 12, 6));
            var ribbon = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                BackColor = TaiTheme.Surface,
                Margin = Padding.Empty
            };
            ribbon.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 330));
            ribbon.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            ribbon.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 136));

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
                Padding = new Padding(0, 9, 8, 0)
            };

            pillAll = new FilterPillButton("Tổng", result.TotalSelected, null, TaiTheme.Accent, TaiTheme.AccentSoft, TaiTheme.AccentBorder);
            pillOk = new FilterPillButton("OK", result.OkCount, DrawingBomCheckStatus.OK, TaiTheme.Success, TaiTheme.SuccessSoft, TaiTheme.SuccessBorder);
            pillNg = new FilterPillButton("NG", result.NgCount, DrawingBomCheckStatus.NG, TaiTheme.Danger, TaiTheme.DangerSoft, TaiTheme.DangerBorder);
            pillWarning = new FilterPillButton("Warning", result.WarningCount, DrawingBomCheckStatus.Warning, TaiTheme.Warning, TaiTheme.WarningSoft, TaiTheme.WarningBorder);

            pillAll.Click += (s, e) => ApplyFilter(null);
            pillOk.Click += (s, e) => ApplyFilter(activeFilter == DrawingBomCheckStatus.OK ? (DrawingBomCheckStatus?)null : DrawingBomCheckStatus.OK);
            pillNg.Click += (s, e) => ApplyFilter(activeFilter == DrawingBomCheckStatus.NG ? (DrawingBomCheckStatus?)null : DrawingBomCheckStatus.NG);
            pillWarning.Click += (s, e) => ApplyFilter(activeFilter == DrawingBomCheckStatus.Warning ? (DrawingBomCheckStatus?)null : DrawingBomCheckStatus.Warning);

            // RightToLeft flow: add in reverse visual order so visual left-to-right is: Tổng | OK | NG | Warning
            filterBar.Controls.Add(pillWarning);
            filterBar.Controls.Add(pillNg);
            filterBar.Controls.Add(pillOk);
            filterBar.Controls.Add(pillAll);
            ribbon.Controls.Add(filterBar, 1, 0);

            var reportWrap = new Panel { Dock = DockStyle.Fill, BackColor = TaiTheme.Surface, Padding = new Padding(4, 9, 0, 9), Margin = Padding.Empty };
            report.Name = "report";
            report.Height = 34;
            report.Dock = DockStyle.Fill;
            reportWrap.Controls.Add(report);
            ribbon.Controls.Add(reportWrap, 2, 0);

            ribbonCard.Controls.Add(ribbon);
            root.Controls.Add(ribbonCard, 0, 0);

            // 2. Main Workbench (Left Master Card + Right Inspector Card)
            var workbench = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0, 10, 0, 0),
                BackColor = TaiTheme.Page
            };
            workbench.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38F));
            workbench.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62F));

            // Left Master Card (Drawing đã kiểm tra)
            var itemCard = CreateCardPanel(new Padding(1));
            itemCard.Margin = new Padding(0, 0, 9, 0);
            var itemPane = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = TaiTheme.Surface,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            itemPane.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            itemPane.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            // Style C Dark Navy Banner for Left Header
            var itemHeader = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(30, 41, 59), // Slate-800 Dark Navy
                Margin = Padding.Empty,
                Padding = new Padding(26, 0, 12, 0)
            };
            itemHeader.Paint += PaintLeftHeader;

            var itemTitle = TextLabel("DRAWING ĐÃ KIỂM TRA", true);
            itemTitle.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            itemTitle.ForeColor = Color.White;

            itemCountBadge.AutoSize = false;
            itemCountBadge.Dock = DockStyle.Right;
            itemCountBadge.Width = 110;
            itemCountBadge.TextAlign = ContentAlignment.MiddleRight;
            itemCountBadge.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            itemCountBadge.ForeColor = Color.FromArgb(203, 213, 225); // Slate-300
            itemCountBadge.BackColor = Color.Transparent;

            itemHeader.Controls.Add(itemTitle);
            itemHeader.Controls.Add(itemCountBadge);

            var itemGridWrap = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6, 4, 6, 6), BackColor = TaiTheme.Surface, Margin = Padding.Empty };
            itemGridWrap.Controls.Add(drawingItems);
            itemPane.Controls.Add(itemHeader, 0, 0);
            itemPane.Controls.Add(itemGridWrap, 0, 1);
            itemCard.Controls.Add(itemPane);
            workbench.Controls.Add(itemCard, 0, 0);

            // Right Inspector Card
            var detailCard = CreateCardPanel(new Padding(1));
            detailCard.Margin = Padding.Empty;
            var detailPane = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                BackColor = TaiTheme.Surface,
                Padding = Padding.Empty,
                Margin = Padding.Empty
            };
            detailPane.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
            detailPane.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            detailPane.RowStyles.Add(new RowStyle(SizeType.Absolute, 130));
            detailPane.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));

            // Selected Drawing Header Banner (Style C Dark Navy)
            detailBanner.Dock = DockStyle.Fill;
            detailBanner.BackColor = Color.FromArgb(30, 41, 59); // Slate-800 Dark Navy
            detailBanner.Padding = new Padding(26, 10, 110, 8);
            detailBanner.Margin = Padding.Empty;
            UIHelper.EnableDoubleBuffer(detailBanner);
            detailBanner.Paint += PaintDetailBanner;
            detailBanner.Resize += (s, e) => detailBanner.Invalidate();

            selectedDrawing.Name = "selectedDrawing";
            selectedDrawing.Dock = DockStyle.Top;
            selectedDrawing.Height = 24;
            selectedDrawing.Font = new Font("Segoe UI", 11.5F, FontStyle.Bold);
            selectedDrawing.ForeColor = Color.White;
            selectedDrawing.AutoEllipsis = true;
            selectedDrawing.BackColor = Color.Transparent;

            selectedSubtext.Dock = DockStyle.Fill;
            selectedSubtext.Font = new Font("Segoe UI", 9.5F, FontStyle.Regular);
            selectedSubtext.ForeColor = Color.FromArgb(148, 163, 184); // Slate-400
            selectedSubtext.AutoEllipsis = true;
            selectedSubtext.BackColor = Color.Transparent;

            detailBanner.Controls.Add(selectedSubtext);
            detailBanner.Controls.Add(selectedDrawing);
            detailPane.Controls.Add(detailBanner, 0, 0);

            var fieldGridWrap = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 4, 8, 4), BackColor = TaiTheme.Surface, Margin = Padding.Empty };
            fieldGridWrap.Controls.Add(drawingFields);
            detailPane.Controls.Add(fieldGridWrap, 0, 1);

            // Executive Callout Card for Field Details
            var calloutWrap = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 6, 12, 6), BackColor = TaiTheme.Surface, Margin = Padding.Empty };
            calloutCard.Dock = DockStyle.Fill;
            calloutCard.Padding = new Padding(20, 10, 16, 10);
            calloutCard.BackColor = TaiTheme.WarningSoft;
            UIHelper.EnableDoubleBuffer(calloutCard);
            calloutCard.Paint += PaintCalloutCard;
            calloutCard.Resize += (s, e) => calloutCard.Invalidate();

            calloutHeader.Dock = DockStyle.Top;
            calloutHeader.Height = 24;
            calloutHeader.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            calloutHeader.ForeColor = TaiTheme.Danger;
            calloutHeader.BackColor = Color.Transparent;

            calloutBody.Dock = DockStyle.Fill;
            calloutBody.Font = new Font("Segoe UI", 10F, FontStyle.Regular);
            calloutBody.ForeColor = TaiTheme.Text;
            calloutBody.BackColor = Color.Transparent;

            calloutFooter.Dock = DockStyle.Bottom;
            calloutFooter.Height = 24;
            calloutFooter.Font = new Font("Segoe UI", 9.5F, FontStyle.Regular);
            calloutFooter.ForeColor = TaiTheme.TextMuted;
            calloutFooter.BackColor = Color.Transparent;

            calloutCard.Controls.Add(calloutBody);
            calloutCard.Controls.Add(calloutHeader);
            calloutCard.Controls.Add(calloutFooter);
            calloutWrap.Controls.Add(calloutCard);
            detailPane.Controls.Add(calloutWrap, 0, 2);

            // Bottom Action Bar inside Right Inspector Card
            var actionFooter = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(250, 251, 253),
                Padding = new Padding(12, 8, 16, 8),
                Margin = Padding.Empty
            };
            actionFooter.Paint += (s, e) =>
            {
                using (var pen = new Pen(TaiTheme.Divider))
                    e.Graphics.DrawLine(pen, 0, 0, actionFooter.Width, 0);
            };
            var actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Left,
                AutoSize = true,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight,
                BackColor = Color.Transparent,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            openDrawing.Name = "openDrawing";
            location.Name = "location";
            actions.Controls.Add(openDrawing);
            actions.Controls.Add(location);

            var hintLabel = TextLabel("Double-click vào dòng để nhảy đến vị trí trên bản vẽ", false);
            hintLabel.Dock = DockStyle.Fill;
            hintLabel.TextAlign = ContentAlignment.MiddleRight;
            hintLabel.Font = new Font("Segoe UI", 9.5F, FontStyle.Regular);
            hintLabel.ForeColor = TaiTheme.TextMuted;
            hintLabel.BackColor = Color.Transparent;

            actionFooter.Controls.Add(hintLabel);
            actionFooter.Controls.Add(actions);
            detailPane.Controls.Add(actionFooter, 0, 3);

            detailCard.Controls.Add(detailPane);
            workbench.Controls.Add(detailCard, 1, 0);
            root.Controls.Add(workbench, 0, 1);

            // 3. Footer Status Bar
            var statusWrap = new Panel { Dock = DockStyle.Fill, BackColor = TaiTheme.Page, Padding = new Padding(6, 4, 6, 0), Margin = Padding.Empty };
            status.Name = "reviewStatus";
            status.Text = result.Message;
            status.Font = new Font("Segoe UI", 9.5F, FontStyle.Regular);
            status.ForeColor = TaiTheme.TextMuted;
            status.AutoEllipsis = true;
            statusWrap.Controls.Add(status);
            root.Controls.Add(statusWrap, 0, 2);

            Controls.Add(root);

            drawingItems.SelectionChanged += (sender, args) =>
            {
                drawingItems.Invalidate();
                SelectItem();
            };
            drawingFields.SelectionChanged += (sender, args) =>
            {
                drawingFields.Invalidate();
                SelectField();
            };
            drawingFields.CellDoubleClick += (sender, args) => { if (args.RowIndex >= 0) NavigateSelected(true); };
            drawingItems.CellDoubleClick += (sender, args) => { if (args.RowIndex >= 0) NavigateSelected(false); };
            drawingItems.CellToolTipTextNeeded += (sender, args) =>
            {
                if (args.RowIndex >= 0 && drawingItems.Rows[args.RowIndex].Tag is DrawingCheckItemResult)
                    args.ToolTipText = ((DrawingCheckItemResult)drawingItems.Rows[args.RowIndex].Tag).DrawingPath;
            };
            drawingFields.CellToolTipTextNeeded += (sender, args) =>
            {
                if (args.RowIndex >= 0 && drawingFields.Rows[args.RowIndex].Tag is DrawingBomFieldResult)
                    args.ToolTipText = ((DrawingBomFieldResult)drawingFields.Rows[args.RowIndex].Tag).Message;
            };
            openDrawing.Click += (sender, args) => NavigateSelected(false);
            location.Click += (sender, args) => NavigateSelected(true);
            report.Click += (sender, args) => OpenReport();
            report.Enabled = !string.IsNullOrWhiteSpace(result.ReportPath) && File.Exists(result.ReportPath);

            UpdateFilterPills();
            DisplayResults();
        }

        private void ConfigureGrids()
        {
            // Left Master Grid
            drawingItems.Columns.Add("Status", "Kết quả");
            drawingItems.Columns.Add("Item", "BOM");
            drawingItems.Columns.Add("Part", "Số chi tiết");
            drawingItems.Columns.Add("Drawing", "Drawing");
            drawingItems.Columns[0].Width = 92;
            drawingItems.Columns[1].Width = 62;
            drawingItems.Columns[1].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            drawingItems.Columns[1].HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
            drawingItems.Columns[2].Width = 140;
            drawingItems.Columns[2].DefaultCellStyle.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            drawingItems.Columns[3].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            drawingItems.Columns[3].DefaultCellStyle.ForeColor = TaiTheme.TextMuted;

            // Right Inspector Grid
            drawingFields.Columns.Add("Status", "Kết quả");
            drawingFields.Columns.Add("Field", "Nội dung kiểm tra");
            drawingFields.Columns.Add("Bom", "BOM / Model");
            drawingFields.Columns.Add("Drawing", "Drawing");
            drawingFields.Columns[0].Width = 92;
            drawingFields.Columns[1].Width = 220;
            drawingFields.Columns[1].DefaultCellStyle.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            drawingFields.Columns[2].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            drawingFields.Columns[3].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

            drawingItems.CellPainting += PaintGridCell;
            drawingFields.CellPainting += PaintGridCell;
        }

        private void ApplyFilter(DrawingBomCheckStatus? filter)
        {
            if (activeFilter == filter) return;
            activeFilter = filter;
            UpdateFilterPills();
            DisplayResults();
        }

        private void UpdateFilterPills()
        {
            pillAll.IsActive = activeFilter == null;
            pillOk.IsActive = activeFilter == DrawingBomCheckStatus.OK;
            pillNg.IsActive = activeFilter == DrawingBomCheckStatus.NG;
            pillWarning.IsActive = activeFilter == DrawingBomCheckStatus.Warning;
        }

        internal void PositionOnScreen(Rectangle workingArea)
        {
            StartPosition = FormStartPosition.Manual;
            Width = Math.Min(Width, workingArea.Width);
            Height = Math.Min(Height, workingArea.Height);
            Left = workingArea.Left + Math.Max(0, (workingArea.Width - Width) / 2);
            Top = workingArea.Top + Math.Max(0, (workingArea.Height - Height) / 2);
        }

        private void DisplayResults()
        {
            selecting = true;
            try
            {
                drawingItems.Rows.Clear();
                var allItems = result.Items.Where(item => item != null).ToList();
                var filtered = activeFilter == null
                    ? allItems
                    : allItems.Where(item => item.Status == activeFilter.Value).ToList();

                foreach (DrawingCheckItemResult item in filtered)
                {
                    int index = drawingItems.Rows.Add(StatusText(item.Status), item.BomItemNumber,
                        item.PartNumber, Path.GetFileName(item.DrawingPath ?? ""));
                    drawingItems.Rows[index].Tag = item;
                    ColorRow(drawingItems.Rows[index], item.Status, false);
                }

                itemCountBadge.Text = activeFilter == null
                    ? filtered.Count + " bản vẽ"
                    : filtered.Count + " / " + allItems.Count + " bản vẽ";

                if (drawingItems.Rows.Count > 0)
                    drawingItems.CurrentCell = drawingItems.Rows[0].Cells[0];
            }
            finally { selecting = false; }
            SelectItem();
        }

        private void SelectItem()
        {
            if (selecting) return;
            DrawingCheckItemResult item = SelectedItem;
            selecting = true;
            try
            {
                drawingFields.Rows.Clear();
                if (item == null)
                {
                    selectedDrawing.Text = activeFilter == null
                        ? "Chưa có Drawing được xử lý."
                        : "Không có Drawing ở bộ lọc " + StatusText(activeFilter.Value) + ".";
                    selectedSubtext.Text = "Chọn bộ lọc khác trên thanh thống kê để xem danh sách.";
                }
                else
                {
                    selectedDrawing.Text = item.PartNumber + "   ·   " + Path.GetFileName(item.DrawingPath ?? "");
                    selectedSubtext.Text = string.IsNullOrWhiteSpace(item.Component)
                        ? (item.DrawingPath ?? "")
                        : item.Component;
                }
                detailBanner.Invalidate();

                openDrawing.Enabled = item != null && navigate != null && !string.IsNullOrWhiteSpace(item.DrawingPath);
                if (item != null)
                {
                    foreach (DrawingBomFieldResult field in item.Fields.Where(field => field != null))
                    {
                        int index = drawingFields.Rows.Add(StatusText(field.Status), field.FieldName,
                            field.BomValue, field.DrawingValue);
                        drawingFields.Rows[index].Tag = field;
                        ColorRow(drawingFields.Rows[index], field.Status, true);
                    }
                    DataGridViewRow first = drawingFields.Rows.Cast<DataGridViewRow>().FirstOrDefault(row =>
                        ((DrawingBomFieldResult)row.Tag).Status == DrawingBomCheckStatus.NG)
                        ?? drawingFields.Rows.Cast<DataGridViewRow>().FirstOrDefault(row =>
                        ((DrawingBomFieldResult)row.Tag).Status == DrawingBomCheckStatus.Warning)
                        ?? drawingFields.Rows.Cast<DataGridViewRow>().FirstOrDefault();
                    if (first != null) drawingFields.CurrentCell = first.Cells[0];
                }
            }
            finally { selecting = false; }
            SelectField();
        }

        private void SelectField()
        {
            if (selecting) return;
            DrawingCheckItemResult item = SelectedItem;
            DrawingBomFieldResult field = SelectedField;
            location.Enabled = item != null && field != null && navigate != null
                && !string.IsNullOrWhiteSpace(item.DrawingPath);

            if (field == null)
            {
                calloutHeader.Text = "THÔNG TIN CHI TIẾT";
                calloutHeader.ForeColor = TaiTheme.TextMuted;
                calloutBody.Text = item == null ? "Không có kết quả để hiển thị." : item.Note;
                calloutFooter.Text = "";
                fieldDetails.Text = calloutBody.Text;
                calloutCard.Invalidate();
                return;
            }

            DrawingBomCheckStatus st = field.Status;
            if (st == DrawingBomCheckStatus.NG)
            {
                calloutHeader.Text = "● PHÁT HIỆN SAI LỆCH (NG)  —  [ Nguồn: " + field.Source + " ]";
                calloutHeader.ForeColor = TaiTheme.Danger;
            }
            else if (st == DrawingBomCheckStatus.Warning)
            {
                calloutHeader.Text = "▲ CẢNH BÁO CẦN LƯU Ý (WARNING)  —  [ Nguồn: " + field.Source + " ]";
                calloutHeader.ForeColor = TaiTheme.Warning;
            }
            else
            {
                calloutHeader.Text = "✔ ĐỐI CHIẾU KHỚP HOÀN TOÀN (OK)  —  [ Nguồn: " + field.Source + " ]";
                calloutHeader.ForeColor = TaiTheme.Success;
            }

            calloutBody.Text = field.Message;
            calloutFooter.Text = !string.IsNullOrWhiteSpace(field.DrawingViewName)
                ? "View: " + field.DrawingViewName + "   ·   Bấm nút \"Đến vị trí\" hoặc Double-click để phóng tới vị trí trên Drawing."
                : "Bấm nút \"Đến vị trí\" hoặc Double-click để phóng tới vị trí trên Drawing.";

            fieldDetails.Text = field.Message + "\r\nNguồn: " + field.Source + "\r\n" + calloutFooter.Text;
            calloutCard.Invalidate();
        }

        private void NavigateSelected(bool atField)
        {
            DrawingCheckItemResult item = SelectedItem;
            DrawingBomFieldResult field = atField ? SelectedField : null;
            if (item == null || navigate == null || string.IsNullOrWhiteSpace(item.DrawingPath)
                || (atField && field == null)) return;
            try
            {
                navigate(item, field);
                status.ForeColor = TaiTheme.TextMuted;
                status.Text = atField ? "Đã hướng tới vị trí " + field.FieldName + " trên Drawing."
                    : "Đã mở Drawing " + Path.GetFileName(item.DrawingPath) + ".";
            }
            catch (Exception ex)
            {
                status.ForeColor = TaiTheme.Warning;
                status.Text = "Không mở được Drawing: " + ex.Message;
            }
        }

        private void OpenReport()
        {
            if (string.IsNullOrWhiteSpace(result.ReportPath) || !File.Exists(result.ReportPath)) return;
            try { Process.Start(new ProcessStartInfo(result.ReportPath) { UseShellExecute = true }); }
            catch (Exception ex)
            {
                status.ForeColor = TaiTheme.Warning;
                status.Text = "Đã lưu báo cáo nhưng không mở được: " + ex.Message;
            }
        }

        private static string StatusText(DrawingBomCheckStatus value)
        {
            return value == DrawingBomCheckStatus.Warning ? "Warning" : value.ToString();
        }

        private static void ColorRow(DataGridViewRow row, DrawingBomCheckStatus value, bool isFieldGrid)
        {
            // Keep row surfaces clean white so 18-50 rows remain easy on the eyes; status pill carries the hue.
            row.DefaultCellStyle.BackColor = TaiTheme.Surface;
            row.DefaultCellStyle.SelectionBackColor = TaiTheme.AccentSoft;
            row.DefaultCellStyle.SelectionForeColor = TaiTheme.Text;

            Color statusColor = value == DrawingBomCheckStatus.NG ? TaiTheme.Danger
                : value == DrawingBomCheckStatus.Warning ? TaiTheme.Warning : TaiTheme.Success;
            row.Cells[0].Style.ForeColor = statusColor;

            if (isFieldGrid && value != DrawingBomCheckStatus.OK && row.Cells.Count > 3)
            {
                row.Cells[3].Style.ForeColor = statusColor;
                row.Cells[3].Style.SelectionForeColor = statusColor;
                row.Cells[3].Style.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            }
        }

        private void PaintBrandHeader(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            TaiTheme.Prepare(g);

            Rectangle iconRect = new Rectangle(2, 6, 36, 36);
            TaiTheme.FillRound(g, iconRect, 8, TaiTheme.Accent, TaiTheme.Accent);
            using (var pen = new Pen(Color.White, 2.5F) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                g.DrawLines(pen, new[]
                {
                    new Point(iconRect.X + 9, iconRect.Y + 18),
                    new Point(iconRect.X + 15, iconRect.Y + 24),
                    new Point(iconRect.X + 27, iconRect.Y + 12)
                });
            }

            using (var titleFont = new Font("Segoe UI", 12F, FontStyle.Bold))
            using (var subFont = new Font("Segoe UI", 9F, FontStyle.Regular))
            {
                TextRenderer.DrawText(g, "CHECK DRAWING", titleFont, new Point(48, 5), TaiTheme.Text,
                    TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                string subText = result.Canceled
                    ? "Đã dừng giữa chừng · Đối chiếu BOM & Bản vẽ"
                    : "Kết quả đối chiếu BOM & Bản vẽ kỹ thuật";
                TextRenderer.DrawText(g, subText, subFont, new Point(49, 27), TaiTheme.TextMuted,
                    TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            }
        }

        private void PaintLeftHeader(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            TaiTheme.Prepare(g);

            // Cyan Accent Pill
            int pillX = 14;
            int pillH = 18;
            int pillY = (44 - pillH) / 2;
            using (var brush = new SolidBrush(Color.FromArgb(56, 189, 248))) // Cyan-400
                g.FillRectangle(brush, pillX, pillY, 3.5F, pillH);
        }

        private void PaintDetailBanner(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            TaiTheme.Prepare(g);

            // Cyan Accent Pill
            int pillX = 14;
            int pillH = 26;
            int pillY = (detailBanner.Height - pillH) / 2;
            using (var brush = new SolidBrush(Color.FromArgb(56, 189, 248))) // Cyan-400
                g.FillRectangle(brush, pillX, pillY, 3.5F, pillH);

            DrawingCheckItemResult item = SelectedItem;
            if (item == null) return;

            Rectangle badgeRect = new Rectangle(detailBanner.Width - 100, (detailBanner.Height - 28) / 2, 86, 28);
            DrawStatusPill(g, badgeRect, item.Status);
        }

        private void PaintCalloutCard(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            TaiTheme.Prepare(g);

            DrawingBomCheckStatus currentStatus = SelectedField?.Status
                ?? SelectedItem?.Status
                ?? DrawingBomCheckStatus.OK;

            Color fill = currentStatus == DrawingBomCheckStatus.NG ? TaiTheme.DangerSoft
                : currentStatus == DrawingBomCheckStatus.Warning ? TaiTheme.WarningSoft
                : TaiTheme.SuccessSoft;
            Color border = currentStatus == DrawingBomCheckStatus.NG ? TaiTheme.DangerBorder
                : currentStatus == DrawingBomCheckStatus.Warning ? TaiTheme.WarningBorder
                : TaiTheme.SuccessBorder;
            Color stripe = currentStatus == DrawingBomCheckStatus.NG ? TaiTheme.Danger
                : currentStatus == DrawingBomCheckStatus.Warning ? TaiTheme.Warning
                : TaiTheme.Success;

            e.Graphics.Clear(TaiTheme.Surface);
            Rectangle rect = new Rectangle(0, 0, calloutCard.Width - 1, calloutCard.Height - 1);
            TaiTheme.FillRound(g, rect, 8, fill, border);

            // Left solid accent bar (5px thick)
            using (var stripeBrush = new SolidBrush(stripe))
            {
                g.FillRectangle(stripeBrush, new Rectangle(1, 6, 5, Math.Max(4, calloutCard.Height - 12)));
            }
        }

        private static void PaintGridCell(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0) return;
            var grid = sender as DataGridView;
            if (grid == null) return;

            bool isSelected = (e.State & DataGridViewElementStates.Selected) == DataGridViewElementStates.Selected;

            if (e.ColumnIndex == 0)
            {
                e.PaintBackground(e.CellBounds, true);

                // Left accent indicator on selected row
                if (isSelected)
                {
                    using (var accentBrush = new SolidBrush(TaiTheme.Accent))
                        e.Graphics.FillRectangle(accentBrush, e.CellBounds.X, e.CellBounds.Y, 3.5F, e.CellBounds.Height);
                }

                DrawingBomCheckStatus cellStatus = DrawingBomCheckStatus.OK;
                object tag = grid.Rows[e.RowIndex].Tag;
                if (tag is DrawingCheckItemResult itemTag) cellStatus = itemTag.Status;
                else if (tag is DrawingBomFieldResult fieldTag) cellStatus = fieldTag.Status;

                int pillW = Math.Min(76, e.CellBounds.Width - 10);
                int pillH = 24;
                Rectangle pillRect = new Rectangle(
                    e.CellBounds.X + (e.CellBounds.Width - pillW) / 2,
                    e.CellBounds.Y + (e.CellBounds.Height - pillH) / 2,
                    pillW,
                    pillH);

                DrawStatusPill(e.Graphics, pillRect, cellStatus);
                e.Handled = true;
            }
        }

        private static void DrawStatusPill(Graphics g, Rectangle bounds, DrawingBomCheckStatus status)
        {
            TaiTheme.Prepare(g);
            Color fill = status == DrawingBomCheckStatus.NG ? TaiTheme.DangerSoft
                : status == DrawingBomCheckStatus.Warning ? TaiTheme.WarningSoft
                : TaiTheme.SuccessSoft;
            Color border = status == DrawingBomCheckStatus.NG ? TaiTheme.DangerBorder
                : status == DrawingBomCheckStatus.Warning ? TaiTheme.WarningBorder
                : TaiTheme.SuccessBorder;
            Color fg = status == DrawingBomCheckStatus.NG ? TaiTheme.Danger
                : status == DrawingBomCheckStatus.Warning ? TaiTheme.Warning
                : TaiTheme.Success;

            TaiTheme.FillRound(g, bounds, bounds.Height / 2, fill, border);

            int dotSize = 7;
            int dotX = bounds.X + 10;
            int dotY = bounds.Y + (bounds.Height - dotSize) / 2;
            using (var brush = new SolidBrush(fg))
                g.FillEllipse(brush, dotX, dotY, dotSize, dotSize);

            Rectangle textRect = new Rectangle(dotX + dotSize + 4, bounds.Y, bounds.Width - (dotSize + 16), bounds.Height);
            using (var font = new Font("Segoe UI", 9F, FontStyle.Bold))
            {
                TextRenderer.DrawText(g, StatusText(status), font, textRect, fg,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            }
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

        private static DataGridView Grid(string name)
        {
            var grid = new DataGridView
            {
                Name = name,
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                AutoGenerateColumns = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                RowHeadersVisible = false,
                BorderStyle = BorderStyle.None,
                BackgroundColor = TaiTheme.Surface,
                GridColor = TaiTheme.Divider,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single,
                EnableHeadersVisualStyles = false,
                ColumnHeadersHeight = 40,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                RowTemplate = { Height = 38 },
                Margin = Padding.Empty,
                ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
                {
                    BackColor = Color.FromArgb(245, 247, 250),
                    ForeColor = Color.FromArgb(30, 41, 59),
                    Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold),
                    SelectionBackColor = Color.FromArgb(245, 247, 250),
                    Padding = new Padding(8, 0, 8, 0)
                },
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    ForeColor = TaiTheme.Text,
                    BackColor = TaiTheme.Surface,
                    SelectionBackColor = TaiTheme.AccentSoft,
                    SelectionForeColor = TaiTheme.Text,
                    Padding = new Padding(8, 2, 8, 2),
                    Font = new Font("Segoe UI", 10F, FontStyle.Regular)
                }
            };
            UIHelper.EnableDoubleBuffer(grid);
            return grid;
        }

        private static Label TextLabel(string text, bool bold)
        {
            return new Label
            {
                Text = text,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = bold ? TaiTheme.Text : TaiTheme.TextMuted,
                Font = new Font("Segoe UI", 10F, bold ? FontStyle.Bold : FontStyle.Regular),
                Margin = Padding.Empty,
                UseMnemonic = false
            };
        }

        private static ModernButton Button(string text, bool primary, int width)
        {
            return new ModernButton
            {
                Text = text,
                Width = width,
                Height = 36,
                Kind = primary ? ModernButtonKind.Primary : ModernButtonKind.Standard,
                BorderRadius = 6,
                NormalColor = primary ? TaiTheme.Accent : TaiTheme.Surface,
                HoverColor = primary ? TaiTheme.AccentHover : TaiTheme.AccentSoft,
                PressColor = primary ? TaiTheme.AccentPressed : TaiTheme.SurfacePressed,
                BorderColor = primary ? TaiTheme.Accent : TaiTheme.BorderStrong,
                ForeColor = primary ? Color.White : TaiTheme.Text,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Margin = new Padding(0, 0, 8, 0)
            };
        }

        // Interactive KPI filter pill in the top header bar (Tổng / OK / NG / Warning)
        private sealed class FilterPillButton : Control
        {
            private readonly string label;
            private readonly int count;
            private readonly Color hue;
            private readonly Color softFill;
            private readonly Color softBorder;
            private bool hovered;
            private bool isActive;

            public bool IsActive
            {
                get { return isActive; }
                set { if (isActive != value) { isActive = value; Invalidate(); } }
            }

            public FilterPillButton(string label, int count, DrawingBomCheckStatus? status, Color hue, Color softFill, Color softBorder)
            {
                this.label = label;
                this.count = count;
                this.hue = hue;
                this.softFill = softFill;
                this.softBorder = softBorder;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                         ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.Selectable, true);
                Cursor = Cursors.Hand;
                Height = 34;
                Width = MeasureWidth(label, count);
                Margin = new Padding(6, 0, 0, 0);
            }

            private static int MeasureWidth(string text, int num)
            {
                using (var f1 = new Font("Segoe UI", 9.5F, FontStyle.Regular))
                using (var f2 = new Font("Segoe UI", 10.5F, FontStyle.Bold))
                {
                    int w1 = TextRenderer.MeasureText(text, f1).Width;
                    int w2 = TextRenderer.MeasureText(num.ToString(), f2).Width;
                    return Math.Max(88, w1 + w2 + 36);
                }
            }

            protected override void OnMouseEnter(EventArgs e)
            {
                base.OnMouseEnter(e);
                hovered = true;
                Invalidate();
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                hovered = false;
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.Clear(TaiTheme.Surface);
                TaiTheme.Prepare(g);

                Rectangle rect = new Rectangle(1, 1, Width - 3, Height - 3);
                Color fill = isActive ? softFill : hovered ? TaiTheme.Page : Color.FromArgb(249, 250, 252);
                Color border = isActive ? hue : hovered ? softBorder : TaiTheme.Border;

                TaiTheme.FillRound(g, rect, 16, fill, border);
                if (isActive)
                {
                    Rectangle inner = new Rectangle(2, 2, Width - 5, Height - 5);
                    TaiTheme.FillRound(g, inner, 15, Color.Transparent, Color.FromArgb(90, hue));
                }

                int dotSize = 8;
                int dotX = 13;
                int dotY = (Height - dotSize) / 2;
                using (var dotBrush = new SolidBrush(hue))
                    g.FillEllipse(dotBrush, dotX, dotY, dotSize, dotSize);

                using (var lblFont = new Font("Segoe UI", 9.5F, isActive ? FontStyle.Bold : FontStyle.Regular))
                using (var cntFont = new Font("Segoe UI", 10.5F, FontStyle.Bold))
                {
                    Color lblColor = isActive ? hue : TaiTheme.TextMuted;
                    Color cntColor = isActive ? hue : (count > 0 ? hue : TaiTheme.Text);

                    Size lblSize = TextRenderer.MeasureText(g, label, lblFont, Size.Empty, TextFormatFlags.NoPadding);
                    int textY = (Height - lblSize.Height) / 2;
                    TextRenderer.DrawText(g, label, lblFont, new Point(dotX + dotSize + 7, textY), lblColor,
                        TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

                    string cntText = count.ToString();
                    Size cntSize = TextRenderer.MeasureText(g, cntText, cntFont, Size.Empty, TextFormatFlags.NoPadding);
                    int cntY = (Height - cntSize.Height) / 2;
                    TextRenderer.DrawText(g, cntText, cntFont, new Point(Width - cntSize.Width - 13, cntY), cntColor,
                        TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                }
            }
        }
    }
}
