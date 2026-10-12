using System;
using System.Diagnostics;
using System.Drawing;
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
        private readonly Label fieldDetails = TextLabel("", false);
        private readonly Label status = TextLabel("", false);
        private readonly ModernButton openDrawing = Button("Mở Drawing", true, 122);
        private readonly ModernButton location = Button("Đến vị trí", false, 116);
        private readonly ModernButton report = Button("Mở báo cáo", false, 120);
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
            Size = new Size(1210, 760);
            MinimumSize = new Size(980, 600);
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 9F);
            ForeColor = TaiTheme.Text;
            BackColor = TaiTheme.Page;

            drawingItems.Columns.Add("Status", "Kết quả");
            drawingItems.Columns.Add("Item", "BOM");
            drawingItems.Columns.Add("Part", "Số chi tiết");
            drawingItems.Columns.Add("Drawing", "Drawing");
            drawingItems.Columns[0].Width = 70;
            drawingItems.Columns[1].Width = 52;
            drawingItems.Columns[2].Width = 114;
            drawingItems.Columns[3].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            drawingFields.Columns.Add("Status", "Kết quả");
            drawingFields.Columns.Add("Field", "Nội dung kiểm tra");
            drawingFields.Columns.Add("Bom", "BOM / Model");
            drawingFields.Columns.Add("Drawing", "Drawing");
            drawingFields.Columns[0].Width = 75;
            drawingFields.Columns[1].Width = 178;
            drawingFields.Columns[2].AutoSizeMode = drawingFields.Columns[3].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
                Padding = new Padding(8), BackColor = BackColor };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 53));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            var ribbon = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1,
                BackColor = TaiTheme.Surface, Padding = new Padding(12, 6, 8, 6), Margin = Padding.Empty };
            ribbon.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 175));
            ribbon.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            ribbon.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 122));
            var title = TextLabel("CHECK DRAWING", true);
            title.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            ribbon.Controls.Add(title, 0, 0);
            var counts = TextLabel("Tổng " + result.TotalSelected + "     OK " + result.OkCount
                + "     NG " + result.NgCount + "     Warning " + result.WarningCount
                + (result.Canceled ? "     Đã dừng" : ""), true);
            counts.TextAlign = ContentAlignment.MiddleRight;
            counts.Padding = new Padding(0, 0, 16, 0);
            ribbon.Controls.Add(counts, 1, 0);
            report.Name = "report";
            ribbon.Controls.Add(report, 2, 0);
            root.Controls.Add(ribbon, 0, 0);

            var workbench = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2,
                RowCount = 1, Margin = new Padding(0, 7, 0, 0) };
            workbench.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 37));
            workbench.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 63));
            var itemPane = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1,
                RowCount = 2, BackColor = TaiTheme.Surface, Padding = new Padding(7), Margin = new Padding(0, 0, 5, 0) };
            itemPane.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            itemPane.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            itemPane.Controls.Add(TextLabel("Drawing đã kiểm tra", true), 0, 0);
            itemPane.Controls.Add(drawingItems, 0, 1);
            workbench.Controls.Add(itemPane, 0, 0);

            var detailPane = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1,
                RowCount = 4, BackColor = TaiTheme.Surface, Padding = new Padding(8), Margin = Padding.Empty };
            detailPane.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            detailPane.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            detailPane.RowStyles.Add(new RowStyle(SizeType.Absolute, 115));
            detailPane.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            selectedDrawing.Name = "selectedDrawing";
            selectedDrawing.AutoEllipsis = true;
            detailPane.Controls.Add(selectedDrawing, 0, 0);
            detailPane.Controls.Add(drawingFields, 0, 1);
            fieldDetails.Name = "fieldDetails";
            fieldDetails.Padding = new Padding(3, 9, 3, 0);
            fieldDetails.AutoEllipsis = true;
            detailPane.Controls.Add(fieldDetails, 0, 2);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight, Margin = Padding.Empty, Padding = new Padding(0, 5, 0, 0) };
            openDrawing.Name = "openDrawing";
            location.Name = "location";
            actions.Controls.Add(openDrawing);
            actions.Controls.Add(location);
            detailPane.Controls.Add(actions, 0, 3);
            workbench.Controls.Add(detailPane, 1, 0);
            root.Controls.Add(workbench, 0, 1);
            status.Name = "reviewStatus";
            status.Text = result.Message;
            status.AutoEllipsis = true;
            root.Controls.Add(status, 0, 2);
            Controls.Add(root);

            drawingItems.SelectionChanged += (sender, args) => SelectItem();
            drawingFields.SelectionChanged += (sender, args) => SelectField();
            drawingFields.CellDoubleClick += (sender, args) => { if (args.RowIndex >= 0) NavigateSelected(true); };
            drawingItems.CellToolTipTextNeeded += (sender, args) => {
                if (args.RowIndex >= 0 && drawingItems.Rows[args.RowIndex].Tag is DrawingCheckItemResult)
                    args.ToolTipText = ((DrawingCheckItemResult)drawingItems.Rows[args.RowIndex].Tag).DrawingPath;
            };
            drawingFields.CellToolTipTextNeeded += (sender, args) => {
                if (args.RowIndex >= 0 && drawingFields.Rows[args.RowIndex].Tag is DrawingBomFieldResult)
                    args.ToolTipText = ((DrawingBomFieldResult)drawingFields.Rows[args.RowIndex].Tag).Message;
            };
            openDrawing.Click += (sender, args) => NavigateSelected(false);
            location.Click += (sender, args) => NavigateSelected(true);
            report.Click += (sender, args) => OpenReport();
            report.Enabled = !string.IsNullOrWhiteSpace(result.ReportPath) && File.Exists(result.ReportPath);
            DisplayResults();
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
                foreach (DrawingCheckItemResult item in result.Items.Where(item => item != null))
                {
                    int index = drawingItems.Rows.Add(StatusText(item.Status), item.BomItemNumber,
                        item.PartNumber, Path.GetFileName(item.DrawingPath ?? ""));
                    drawingItems.Rows[index].Tag = item;
                    ColorRow(drawingItems.Rows[index], item.Status);
                }
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
                selectedDrawing.Text = item == null ? "Chưa có Drawing được xử lý."
                    : item.PartNumber + "  ·  " + Path.GetFileName(item.DrawingPath ?? "") + "\r\n" + item.Component;
                openDrawing.Enabled = item != null && navigate != null && !string.IsNullOrWhiteSpace(item.DrawingPath);
                if (item != null)
                {
                    foreach (DrawingBomFieldResult field in item.Fields.Where(field => field != null))
                    {
                        int index = drawingFields.Rows.Add(StatusText(field.Status), field.FieldName,
                            field.BomValue, field.DrawingValue);
                        drawingFields.Rows[index].Tag = field;
                        ColorRow(drawingFields.Rows[index], field.Status);
                    }
                    DataGridViewRow first = drawingFields.Rows.Cast<DataGridViewRow>().FirstOrDefault(row =>
                        ((DrawingBomFieldResult)row.Tag).Status == DrawingBomCheckStatus.NG)
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
            location.Enabled = item != null && field != null && field.HasDrawingLocation && navigate != null
                && !string.IsNullOrWhiteSpace(item.DrawingPath);
            if (field == null)
            {
                fieldDetails.Text = item == null ? "Không có kết quả để hiển thị." : item.Note;
                return;
            }
            fieldDetails.Text = field.Message + "\r\nNguồn: " + field.Source + "\r\n"
                + (field.HasDrawingLocation
                    ? "View: " + field.DrawingViewName + "  ·  Chọn Đến vị trí để xem nội dung trên Drawing."
                    : "Chưa có vị trí Drawing cho nội dung này. Chọn Mở Drawing để xem bản vẽ.");
        }

        private void NavigateSelected(bool atField)
        {
            DrawingCheckItemResult item = SelectedItem;
            DrawingBomFieldResult field = atField ? SelectedField : null;
            if (item == null || navigate == null || string.IsNullOrWhiteSpace(item.DrawingPath)
                || (atField && (field == null || !field.HasDrawingLocation))) return;
            try
            {
                navigate(item, field);
                status.ForeColor = TaiTheme.TextMuted;
                status.Text = atField ? "Đã mở vị trí " + field.FieldName + " trên Drawing."
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

        private static void ColorRow(DataGridViewRow row, DrawingBomCheckStatus value)
        {
            row.DefaultCellStyle.BackColor = value == DrawingBomCheckStatus.NG ? TaiTheme.DangerSoft
                : value == DrawingBomCheckStatus.Warning ? TaiTheme.WarningSoft : TaiTheme.SuccessSoft;
            row.Cells[0].Style.ForeColor = value == DrawingBomCheckStatus.NG ? TaiTheme.Danger
                : value == DrawingBomCheckStatus.Warning ? TaiTheme.Warning : TaiTheme.Success;
        }

        private static DataGridView Grid(string name)
        {
            return new DataGridView {
                Name = name, Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
                AllowUserToDeleteRows = false, AllowUserToResizeRows = false, AutoGenerateColumns = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false,
                RowHeadersVisible = false, BorderStyle = BorderStyle.None, BackgroundColor = TaiTheme.Surface,
                GridColor = TaiTheme.Divider, CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None, EnableHeadersVisualStyles = false,
                ColumnHeadersHeight = 34, RowTemplate = { Height = 31 }, Margin = Padding.Empty,
                ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle {
                    BackColor = TaiTheme.Page, ForeColor = TaiTheme.TextMuted,
                    Font = new Font("Segoe UI", 9F), SelectionBackColor = TaiTheme.Page },
                DefaultCellStyle = new DataGridViewCellStyle {
                    ForeColor = TaiTheme.Text, BackColor = TaiTheme.Surface,
                    SelectionBackColor = TaiTheme.AccentSoft, SelectionForeColor = TaiTheme.Text,
                    Padding = new Padding(5, 1, 5, 1), Font = new Font("Segoe UI", 9F) }
            };
        }

        private static Label TextLabel(string text, bool bold)
        {
            return new Label { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = bold ? TaiTheme.Text : TaiTheme.TextMuted,
                Font = new Font("Segoe UI", 9F, bold ? FontStyle.Bold : FontStyle.Regular),
                Margin = Padding.Empty, UseMnemonic = false };
        }

        private static ModernButton Button(string text, bool primary, int width)
        {
            return new ModernButton { Text = text, Width = width, Height = 32, BorderRadius = 4,
                NormalColor = primary ? TaiTheme.Accent : TaiTheme.Surface,
                HoverColor = primary ? TaiTheme.AccentHover : TaiTheme.AccentSoft,
                PressColor = primary ? TaiTheme.AccentPressed : TaiTheme.SurfacePressed,
                BorderColor = primary ? TaiTheme.Accent : TaiTheme.Border,
                ForeColor = primary ? Color.White : TaiTheme.Text,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold), Margin = new Padding(0, 0, 7, 0) };
        }
    }
}
