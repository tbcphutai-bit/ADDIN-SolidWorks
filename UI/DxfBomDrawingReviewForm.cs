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
    // Displays the exact, already checked geometry. This form never opens CAD
    // documents, reads DXF files or re-runs a comparison when a row is selected.
    internal sealed class DxfBomDrawingReviewForm : Form
    {
        static readonly Color Ink = Color.FromArgb(24, 42, 66), Muted = Color.FromArgb(105, 122, 143), Blue = Color.FromArgb(37, 99, 211), Red = Color.FromArgb(207, 65, 78);
        readonly DataGridView rows = Grid(), issues = Grid();
        readonly DxfPreview preview = new DxfPreview();
        readonly Label summary = Label("", 11, true), context = Label("", 9, false), detail = Label("", 9, false);
        readonly ModernButton fit = Button("Toàn bộ", 95), focus = Button("Vị trí lỗi", 105), drawingOnly = Button("Drawing", 95), dxfOnly = Button("DXF gốc", 95), openReport = Button("Mở báo cáo", 125);
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
            Text = "TAI TOOL — DXF ↔ DRAWING"; Size = new Size(1380, 820); MinimumSize = new Size(1050, 620);
            StartPosition = FormStartPosition.CenterParent; Font = new Font("Segoe UI", 9); BackColor = Color.FromArgb(243, 246, 251); ForeColor = Ink;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), ColumnCount = 1, RowCount = 4 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); Controls.Add(root);
            summary.Dock = DockStyle.Fill; summary.TextAlign = ContentAlignment.MiddleLeft; root.Controls.Add(summary, 0, 0);
            context.Dock = DockStyle.Fill; context.ForeColor = Muted; root.Controls.Add(context, 0, 1);
            var workbench = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
            workbench.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26)); workbench.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 43)); workbench.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 31)); root.Controls.Add(workbench, 0, 2);
            foreach (string header in new[] { "BOM", "Chi tiết", "Sheet / View", "Kết quả" }) rows.Columns.Add(header, header);
            rows.Columns[0].Width = 48; rows.Columns[1].Width = 63; rows.Columns[2].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill; rows.Columns[3].Width = 72;
            workbench.Controls.Add(Card("Danh sách đã kiểm tra", rows), 0, 0);
            var previewArea = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Color.White };
            previewArea.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); previewArea.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); previewArea.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            var previewButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(4, 2, 4, 2) };
            previewButtons.Controls.AddRange(new Control[] { fit, focus, drawingOnly, dxfOnly }); previewArea.Controls.Add(previewButtons, 0, 0); previewArea.Controls.Add(preview, 0, 1);
            var hint = Label("Xanh: Drawing · Cam: DXF đã dịch X/Y · Lăn chuột: zoom", 8, false); hint.Dock = DockStyle.Fill; hint.ForeColor = Muted; hint.Padding = new Padding(5, 5, 0, 0); previewArea.Controls.Add(hint, 0, 2);
            workbench.Controls.Add(Card("Biên dạng Drawing / DXF", previewArea), 1, 0);
            foreach (string header in new[] { "Mã", "Lệch mm", "Chi tiết" }) issues.Columns.Add(header, header);
            issues.Columns[0].Width = 65; issues.Columns[1].Width = 72; issues.Columns[2].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            issues.DefaultCellStyle.WrapMode = DataGridViewTriState.True; issues.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            var issueArea = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Color.White };
            issueArea.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); issueArea.RowStyles.Add(new RowStyle(SizeType.Absolute, 135)); issueArea.Controls.Add(issues, 0, 0);
            detail.Dock = DockStyle.Fill; detail.Padding = new Padding(8); detail.ForeColor = Muted; issueArea.Controls.Add(detail, 0, 1); workbench.Controls.Add(Card("Kết quả và vị trí lỗi", issueArea), 2, 0);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 5, 0, 0) };
            var close = Button("Đóng", 80); close.Click += (s, e) => Close(); actions.Controls.AddRange(new Control[] { openReport, close }); root.Controls.Add(actions, 0, 3);
            rows.SelectionChanged += (s, e) => { if (!setting) ShowSelected(); };
            issues.SelectionChanged += (s, e) => { if (!setting) ShowIssue(true); };
            fit.Click += (s, e) => ShowOverlay(null, false); focus.Click += (s, e) => ShowIssue(true);
            drawingOnly.Click += (s, e) => { preview.Set(drawingGeometry, null); detail.Text = "Drawing gốc theo hệ View 1:1 mm."; };
            dxfOnly.Click += (s, e) => { if (selected != null && selected.PreviewDxfEdges != null) preview.Set(PreviewFile("DXF gốc", selected.PreviewDxfEdges, new Translation2D()), null); detail.Text = "DXF gốc giữ nguyên tọa độ và kích thước."; };
            openReport.Click += (s, e) => { if (batch != null && File.Exists(batch.ReportPath)) try { Process.Start(new ProcessStartInfo(batch.ReportPath) { UseShellExecute = true }); } catch (Exception error) { detail.Text = "Không mở được báo cáo: " + error.Message; } };
            SetBatch(result);
        }
        internal void PositionOnScreen(Rectangle area)
        { StartPosition = FormStartPosition.Manual; Size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height)); Location = new Point(area.Left + Math.Max(0, (area.Width - Width) / 2), area.Top + Math.Max(0, (area.Height - Height) / 2)); }
        internal void SetBatch(DxfBomBatchResult result)
        {
            batch = result ?? throw new ArgumentNullException(nameof(result)); setting = true;
            try
            {
                rows.Rows.Clear(); issues.Rows.Clear(); preview.Set(null, null); selected = null; drawingGeometry = dxfGeometry = null;
                summary.Text = "DXF ↔ DRAWING   " + batch.Passed + " PASS · " + batch.Warnings + " WARN · " + batch.Failed + " FAIL · " + batch.Errors + " ERROR";
                context.Text = batch.Message + "\r\nBOM: " + Path.GetFileName(batch.BomPath);
                foreach (var item in batch.Items)
                {
                    int index = rows.Rows.Add(item.ItemNumber, item.PartNumber, (item.Sheet ?? "") + " / " + (item.View ?? ""), item.Status); rows.Rows[index].Tag = item;
                    rows.Rows[index].DefaultCellStyle.ForeColor = item.Status == "PASS" ? Color.FromArgb(16, 130, 98) : item.Status == "WARN" ? Color.FromArgb(166, 106, 21) : Red;
                }
                openReport.Enabled = !string.IsNullOrWhiteSpace(batch.ReportPath) && File.Exists(batch.ReportPath);
                rows.ClearSelection(); int chosen = batch.Items.FindIndex(item => item.Result != null && item.PreviewDrawingEdges != null && item.PreviewDrawingEdges.Count > 0);
                if (chosen < 0 && rows.Rows.Count > 0) chosen = 0;
                if (chosen >= 0) { rows.Rows[chosen].Selected = true; rows.CurrentCell = rows.Rows[chosen].Cells[0]; }
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
                issues.Rows.Clear(); drawingGeometry = dxfGeometry = null; preview.Set(null, null);
                fit.Enabled = focus.Enabled = drawingOnly.Enabled = dxfOnly.Enabled = false;
                if (selected == null) return;
                context.Text = "Chi tiết " + selected.PartNumber + " · " + Path.GetFileName(selected.DrawingPath) + " · " + selected.Sheet + " / " + selected.View + "\r\nDXF: " + selected.DxfPath;
                detail.Text = selected.Message;
                if (selected.PreviewDrawingEdges != null) drawingGeometry = PreviewFile("Drawing", selected.PreviewDrawingEdges, new Translation2D());
                if (selected.PreviewDxfEdges != null) dxfGeometry = PreviewFile("DXF", selected.PreviewDxfEdges, selected.Result != null && selected.Result.AlignmentFound ? selected.Result.Translation : new Translation2D());
                if (selected.Result != null) foreach (var mismatch in selected.Result.Mismatches)
                { int index = issues.Rows.Add("ERR-" + mismatch.Id.ToString("00"), Number(mismatch.Deviation), mismatch.Description); issues.Rows[index].Tag = mismatch; }
                issues.ClearSelection(); int located = selected.Result == null ? -1 : selected.Result.Mismatches.FindIndex(mismatch => mismatch.HasDrawingPosition || mismatch.HasDxfPosition);
                if (located >= 0) { issues.Rows[located].Selected = true; issues.CurrentCell = issues.Rows[located].Cells[0]; }
                fit.Enabled = drawingGeometry != null || dxfGeometry != null; drawingOnly.Enabled = drawingGeometry != null; dxfOnly.Enabled = dxfGeometry != null;
                focus.Enabled = selected.Result != null && selected.Result.AlignmentFound && located >= 0;
            }
            finally { setting = false; }
            ShowOverlay(null, false); ShowIssue(false);
        }
        void ShowIssue(bool focusLocation)
        {
            var mismatch = issues.SelectedRows.Count == 0 ? null : issues.SelectedRows[0].Tag as GeometryMismatch;
            if (mismatch == null) return;
            detail.Text = "ERR-" + mismatch.Id.ToString("00") + " · " + mismatch.Type + "\r\n" + mismatch.Description + "\r\nDrawing: " + (mismatch.HasDrawingPosition ? PointText(mismatch.DrawingPosition) : "—") + "\r\nDXF" + (PreviewIsAligned ? " đã dịch" : " gốc") + ": " + (mismatch.HasDxfPosition ? PointText(mismatch.DxfPosition) : "—");
            ShowOverlay(mismatch, focusLocation);
        }
        void ShowOverlay(GeometryMismatch mismatch, bool focusLocation)
        {
            if (drawingGeometry == null || dxfGeometry == null || selected == null || selected.Result == null || !selected.Result.AlignmentFound)
            {
                preview.Set(drawingGeometry ?? dxfGeometry, null);
                if (selected != null && selected.Result != null && !selected.Result.AlignmentFound) detail.Text = selected.Message + "\r\nChưa căn được X/Y. Dùng Drawing / DXF gốc để xem từng biên dạng.";
                return;
            }
            bool located = mismatch != null && (mismatch.HasDrawingPosition || mismatch.HasDxfPosition);
            DxfPoint a = located ? (mismatch.HasDrawingPosition ? mismatch.DrawingPosition : mismatch.DxfPosition) : new DxfPoint();
            DxfPoint b = located ? (mismatch.HasDxfPosition ? mismatch.DxfPosition : mismatch.DrawingPosition) : new DxfPoint();
            preview.SetComparison(drawingGeometry, new DxfPartComparison { First = drawingGeometry, Second = dxfGeometry, SecondToFirst = DxfTransform.Identity, Exact = !located, DifferenceA = a, DifferenceB = b, MaxBoundaryGap = located ? mismatch.Deviation : 0 }, focusLocation && located);
        }
        static DxfFileResult PreviewFile(string name, IEnumerable<DxfEdge> edges, Translation2D translation)
        { return new DxfFileResult { RelativeName = name, Edges = edges.Select((edge, index) => new DxfEdge { Type = edge.Type, OriginalType = edge.OriginalType, Layer = edge.Layer, Handle = "PREVIEW-" + name + "-" + index.ToString(CultureInfo.InvariantCulture), Space = "MODEL", Start = translation.Apply(edge.Start), End = translation.Apply(edge.End), Breaks = new List<double>(edge.Breaks), Evaluate = t => translation.Apply(edge.At(t)) }).ToList() }; }
        static string Number(double value) { return value.ToString("0.###", CultureInfo.InvariantCulture); }
        static string PointText(DxfPoint point) { return Number(point.X) + ", " + Number(point.Y) + " mm"; }
        static Label Label(string text, float size, bool bold) { return new Label { Text = text, Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular), ForeColor = Ink, AutoEllipsis = true }; }
        static ModernButton Button(string text, int width) { return new ModernButton { Text = text, Size = new Size(width, 30), BorderRadius = 5, Font = new Font("Segoe UI", 9, FontStyle.Bold), NormalColor = Color.White, HoverColor = Color.FromArgb(232, 240, 253), PressColor = Color.FromArgb(219, 233, 254), BorderColor = Color.FromArgb(205, 218, 237), ForeColor = Blue }; }
        static Control Card(string title, Control content)
        { var card = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Color.White, Margin = new Padding(3), Padding = new Padding(5) }; card.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); card.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); var label = Label(title, 9.5F, true); label.Dock = DockStyle.Fill; label.Padding = new Padding(3, 4, 0, 0); card.Controls.Add(label, 0, 0); content.Dock = DockStyle.Fill; card.Controls.Add(content, 0, 1); return card; }
        static DataGridView Grid()
        { return new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, BackgroundColor = Color.White, BorderStyle = BorderStyle.None, GridColor = Color.FromArgb(230, 237, 245), EnableHeadersVisualStyles = false, ColumnHeadersHeight = 34, ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(243, 246, 250), ForeColor = Muted }, DefaultCellStyle = new DataGridViewCellStyle { SelectionBackColor = Color.FromArgb(227, 239, 255), SelectionForeColor = Ink, Padding = new Padding(3) }, RowTemplate = { Height = 30 } }; }
    }
}
