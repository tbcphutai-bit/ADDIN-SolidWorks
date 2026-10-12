using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using ADDIN.Commands.DxfChecking;

namespace ADDIN.UI
{
    internal sealed class DxfDrawingViewCheckForm : Form
    {
        private static readonly Color Ink = Color.FromArgb(24, 42, 66), Muted = Color.FromArgb(105, 122, 143), Blue = Color.FromArgb(37, 99, 211),
            Green = Color.FromArgb(16, 130, 98), Red = Color.FromArgb(207, 65, 78), Amber = Color.FromArgb(166, 106, 21);
        private static readonly Dictionary<string, string> RecentFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly CheckDxfDrawingViewCommand command;
        private readonly DrawingMismatchMarker marker = new DrawingMismatchMarker();
        private readonly TextBox file = new TextBox(), layers = new TextBox(), report = new TextBox();
        private readonly NumericUpDown tolerance = new NumericUpDown();
        private readonly DataGridView errors = Grid();
        private readonly ModernButton browse = Button("Chọn DXF", false, 110), run = Button("Kiểm tra", true, 110),
            mark = Button("Đánh dấu trên Drawing", true, 190), clear = Button("Xóa dấu vừa tạo", false, 150), save = Button("Lưu báo cáo", false, 120),
            fit = Button("Toàn bộ", false, 100), focus = Button("Phóng chỗ lệch", false, 140);
        private readonly Label resultStatus = Label("CHƯA KIỂM TRA", 12F, true, Muted), status = Label("Chọn DXF xuất từ View này rồi bấm Kiểm tra.", 9F, false, Muted),
            selectedName = Label("Biên dạng DXF / Drawing", 11F, true, Ink), selectionDetail = Label("Chọn một dòng lỗi để phóng tới vị trí trên hình chồng.", 9F, false, Muted),
            scaleValue = Label("—", 11F, true, Muted), outerValue = Label("—", 11F, true, Muted), insideValue = Label("—", 11F, true, Muted), deviationValue = Label("—", 11F, true, Muted);
        private readonly DxfPreview preview = new DxfPreview();
        private readonly SplitContainer workbench = new SplitContainer { Dock = DockStyle.Fill, SplitterWidth = 5, BackColor = Color.FromArgb(226, 232, 240) };
        private readonly TabPage errorsTab = new TabPage("Vị trí lỗi (0)") { BackColor = Color.White, Padding = new Padding(3) };
        private DxfCheckResult result;
        private DxfFileResult previewDxf, previewDrawing;
        private readonly string key;
        private bool running;

        public DxfDrawingViewCheckForm(CheckDxfDrawingViewCommand checkCommand)
        {
            command = checkCommand ?? throw new ArgumentNullException(nameof(checkCommand));
            key = command.DrawingPath + "|" + command.SheetName + "|" + command.ViewName;
            Text = "TAI TOOL — CHECK DXF VIEW"; StartPosition = FormStartPosition.CenterParent;
            Size = new Size(1360, 820); MinimumSize = new Size(1020, 640); Font = new Font("Segoe UI", 9F);
            ForeColor = Ink; BackColor = Color.FromArgb(243, 246, 251); DoubleBuffered = true;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(8, 6, 8, 6), ColumnCount = 1, RowCount = 7 };
            foreach (int height in new[] { 46, 38, 36, 68 }) root.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26)); Controls.Add(root);
            var ribbon = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(8, 6, 8, 6), ColumnCount = 4, Margin = Padding.Empty };
            ribbon.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); ribbon.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            ribbon.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); ribbon.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var brand = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            brand.Controls.Add(new DxfLogo { Margin = new Padding(0, 2, 6, 0) });
            var title = Label("CHECK DXF VIEW", 11.5F, true, Ink); title.AutoSize = true; title.Margin = new Padding(0, 5, 14, 0); brand.Controls.Add(title); ribbon.Controls.Add(brand, 0, 0);
            file.Dock = DockStyle.Fill; file.BorderStyle = BorderStyle.None; file.BackColor = Color.FromArgb(248, 250, 252); file.ForeColor = Ink; file.Font = new Font("Segoe UI", 9.5F);
            var path = new Panel { Dock = DockStyle.Fill, BackColor = file.BackColor, Padding = new Padding(8, 7, 8, 4), Margin = new Padding(0, 0, 6, 0) }; path.Controls.Add(file);
            ribbon.Controls.Add(path, 1, 0); ribbon.Controls.Add(browse, 2, 0); ribbon.Controls.Add(run, 3, 0); root.Controls.Add(ribbon, 0, 0);
            browse.Click += Browse; run.Click += Run; AcceptButton = run;
            string recent; file.Text = RecentFiles.TryGetValue(key, out recent) && File.Exists(recent) ? recent : command.SuggestedFile();
            var context = Label("Drawing: " + Path.GetFileName(command.DrawingPath) + "   |   Sheet: " + command.SheetName + "   |   View: " + command.ViewName +
                "\r\nDrawing chuẩn 1:1 mm · DXF giữ nguyên kích thước · Căn bằng dịch X/Y", 9F, false, Muted);
            context.Dock = DockStyle.Fill; context.Padding = new Padding(4, 2, 0, 0); root.Controls.Add(context, 0, 1);
            var options = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty, Padding = new Padding(4, 2, 0, 0) };
            var toleranceTitle = Label("Dung sai (mm):", 9F, false, Ink); toleranceTitle.AutoSize = true; toleranceTitle.Margin = new Padding(0, 5, 5, 0); options.Controls.Add(toleranceTitle);
            tolerance.DecimalPlaces = 3; tolerance.Minimum = .001M; tolerance.Maximum = 10; tolerance.Increment = .05M; tolerance.Value = .20M; tolerance.Width = 85; options.Controls.Add(tolerance);
            var layerTitle = Label("Bỏ layer (dấu ;):", 9F, false, Ink); layerTitle.AutoSize = true; layerTitle.Margin = new Padding(16, 5, 5, 0); options.Controls.Add(layerTitle);
            layers.Text = new DrawingDxfCheckOptions().IgnoredDxfLayers; layers.Width = 320; options.Controls.Add(layers); root.Controls.Add(options, 0, 2);
            var summary = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1, BackColor = Color.White, Margin = new Padding(0, 2, 0, 5) };
            summary.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            summary.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170)); for (int i = 0; i < 4; i++) summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            resultStatus.Dock = DockStyle.Fill; resultStatus.TextAlign = ContentAlignment.MiddleCenter; summary.Controls.Add(resultStatus, 0, 0);
            summary.Controls.Add(Card("TỈ LỆ DXF", scaleValue), 1, 0); summary.Controls.Add(Card("BIÊN NGOÀI", outerValue), 2, 0);
            summary.Controls.Add(Card("LỖ / BIÊN BÊN TRONG", insideValue), 3, 0); summary.Controls.Add(Card("LỆCH LỚN NHẤT", deviationValue), 4, 0); root.Controls.Add(summary, 0, 3);
            var previewPane = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.White, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty };
            previewPane.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); previewPane.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            previewPane.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); previewPane.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            selectedName.Dock = DockStyle.Fill; selectedName.Padding = new Padding(8, 4, 0, 0); previewPane.Controls.Add(selectedName, 0, 0);
            var previewActions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(4, 0, 0, 0), Margin = Padding.Empty };
            previewActions.Controls.Add(fit); previewActions.Controls.Add(focus); previewPane.Controls.Add(previewActions, 0, 1); previewPane.Controls.Add(preview, 0, 2);
            var hint = Label("Lăn chuột: Zoom · Kéo chuột trái: Pan · Bấm dòng lỗi để xem chỗ lệch", 8.5F, false, Muted); hint.Dock = DockStyle.Fill; hint.Padding = new Padding(8, 4, 0, 0); previewPane.Controls.Add(hint, 0, 3);
            workbench.Panel1.Controls.Add(previewPane);
            var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(12, 6), Font = new Font("Segoe UI", 9.5F, FontStyle.Bold) };
            var reportTab = new TabPage("Báo cáo chi tiết") { BackColor = Color.White, Padding = new Padding(5) };
            var errorLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
            errorLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); errorLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 110));
            foreach (string heading in new[] { "Mã", "Loại", "Lệch mm", "Chi tiết", "Drawing X", "Drawing Y", "DXF X đã dịch", "DXF Y đã dịch" }) errors.Columns.Add(heading, heading);
            int[] widths = { 70, 160, 85, 270, 105, 105, 115, 115 }; for (int i = 0; i < widths.Length; i++) errors.Columns[i].Width = widths[i];
            selectionDetail.Dock = DockStyle.Fill; selectionDetail.Padding = new Padding(6); errorLayout.Controls.Add(errors, 0, 0); errorLayout.Controls.Add(selectionDetail, 0, 1); errorsTab.Controls.Add(errorLayout);
            report.Multiline = true; report.ReadOnly = true; report.ScrollBars = ScrollBars.Both; report.WordWrap = false; report.Dock = DockStyle.Fill;
            report.Font = new Font("Consolas", 9.5F); report.BackColor = Color.White; report.ForeColor = Ink; report.BorderStyle = BorderStyle.None; reportTab.Controls.Add(report);
            tabs.TabPages.AddRange(new[] { errorsTab, reportTab }); workbench.Panel2.Controls.Add(tabs); root.Controls.Add(workbench, 0, 4); workbench.SizeChanged += (s, e) => SizeWorkbench();
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty, Padding = new Padding(0, 4, 0, 0) };
            mark.NormalColor = Green; mark.HoverColor = Color.FromArgb(12, 116, 87); mark.PressColor = Color.FromArgb(9, 99, 75); mark.BorderColor = Green;
            mark.Click += (s, e) => Attempt(() => { int count = marker.Mark(command, result); status.Text = "Đã tạo " + count + " note trên layer DXF_CHECK_ERROR. Drawing chưa được lưu."; });
            clear.Click += (s, e) => Attempt(() => { marker.Clear(command); status.Text = "Đã xóa dấu do cửa sổ này tạo."; }); save.Click += SaveReport;
            var close = Button("Đóng", false, 80); close.Click += (s, e) => Close(); actions.Controls.AddRange(new Control[] { mark, clear, save, close }); root.Controls.Add(actions, 0, 5);
            status.Dock = DockStyle.Fill; status.TextAlign = ContentAlignment.MiddleLeft; root.Controls.Add(status, 0, 6);
            fit.Click += (s, e) => ShowOverlay(null, false); focus.Click += (s, e) => ShowOverlay(SelectedMismatch() ?? WorstLocatedMismatch(), true); errors.SelectionChanged += (s, e) => SelectMismatch();
            file.TextChanged += InvalidateResult; layers.TextChanged += InvalidateResult; tolerance.ValueChanged += InvalidateResult;
            ResetResult("Chọn đúng DXF xuất từ View này, kiểm tra dung sai và bấm Kiểm tra.");
        }

        internal void PositionOnScreen(Rectangle area)
        {
            StartPosition = FormStartPosition.Manual; Size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height));
            Location = new Point(area.Left + Math.Max(0, (area.Width - Width) / 2), area.Top + Math.Max(0, (area.Height - Height) / 2));
        }
        private void SizeWorkbench()
        {
            if (workbench.Width < 800) return; workbench.Panel1MinSize = 360; workbench.Panel2MinSize = 400;
            workbench.SplitterDistance = Math.Max(360, Math.Min(workbench.Width - 405, (int)(workbench.Width * .55)));
        }
        private void InvalidateResult(object sender, EventArgs args) { if (!running) ResetResult("Đường dẫn hoặc tùy chọn đã thay đổi. Bấm Kiểm tra để lấy kết quả mới."); }
        private void ResetResult(string message)
        {
            result = null; previewDxf = previewDrawing = null; errors.Rows.Clear(); errorsTab.Text = "Vị trí lỗi (0)";
            mark.Enabled = save.Enabled = fit.Enabled = focus.Enabled = false; resultStatus.Text = "CHƯA KIỂM TRA"; resultStatus.ForeColor = Muted;
            foreach (Label value in new[] { scaleValue, outerValue, insideValue, deviationValue }) { value.Text = "—"; value.ForeColor = Muted; }
            report.Text = status.Text = message; selectionDetail.Text = "Chọn một dòng lỗi để phóng tới vị trí trên hình chồng."; selectedName.Text = "Biên dạng DXF / Drawing"; preview.Set(null, null);
        }
        private void Browse(object sender, EventArgs args)
        {
            using (var dialog = new OpenFileDialog { Filter = "DXF (*.dxf)|*.dxf", CheckFileExists = true, Title = "Chọn DXF vừa xuất từ " + command.ViewName })
            {
                if (!string.IsNullOrEmpty(command.DrawingPath)) dialog.InitialDirectory = Path.GetDirectoryName(command.DrawingPath);
                if (dialog.ShowDialog(this) == DialogResult.OK) file.Text = dialog.FileName;
            }
        }
        private void Run(object sender, EventArgs args)
        {
            if (running) return; ResetResult("Đang đọc Drawing View và đối chiếu DXF..."); running = true;
            run.Enabled = browse.Enabled = file.Enabled = layers.Enabled = tolerance.Enabled = clear.Enabled = false; Cursor = Cursors.WaitCursor;
            resultStatus.Text = "ĐANG KIỂM TRA"; status.Refresh(); resultStatus.Refresh(); report.Refresh();
            var options = new DrawingDxfCheckOptions { ToleranceMm = (double)tolerance.Value, IgnoredDxfLayers = layers.Text };
            try
            {
                // Keep native SolidWorks COM access on this form's STA thread.
                var check = command.Run(file.Text.Trim(), options); RecentFiles[key] = file.Text.Trim(); PresentResult(check, options);
            }
            catch (Exception ex) { PresentError(ex.Message); }
            finally { running = false; run.Enabled = browse.Enabled = file.Enabled = layers.Enabled = tolerance.Enabled = clear.Enabled = true; Cursor = Cursors.Default; }
        }
        private void PresentResult(DxfCheckResult check, DrawingDxfCheckOptions options)
        {
            previewDxf = previewDrawing = null; preview.Set(null, null);
            result = check; report.Text = FormatReport(check, options); resultStatus.Text = check.Status == "WARN" ? "WARN — NÉT CẮT" : check.Passed ? "PASS — KHỚP" : "FAIL — CẦN XEM"; resultStatus.ForeColor = check.Status == "WARN" ? Amber : check.Passed ? Green : Red;
            SetValue(scaleValue, check.ScaleEvaluated ? (check.ScalePassed ? "PASS · 1:1" : "FAIL · " + check.EstimatedScale.ToString("0.000", CultureInfo.InvariantCulture) + "×") : "CHƯA KIỂM", check.ScaleEvaluated, check.ScalePassed);
            SetValue(outerValue, check.AlignmentFound ? (check.OuterContourPassed ? "PASS" : "FAIL") : "CHƯA KIỂM", check.AlignmentFound, check.OuterContourPassed);
            SetValue(insideValue, check.AlignmentFound ? check.MatchedInsideContours + " / " + check.DrawingInsideContourCount + " khớp" : "CHƯA KIỂM", check.AlignmentFound, check.FailedInsideContours == 0);
            SetValue(deviationValue, check.AlignmentFound ? Number(check.MaximumDeviation) + " mm" : "CHƯA ĐO", check.AlignmentFound, check.MaximumDeviation <= options.ToleranceMm);
            status.Text = (check.GeometryPassed ? (check.CutQualityPassed ? "DXF khớp Drawing View." : "Hình học khớp; DXF có nét cắt cần sửa.") : "Có " + check.Mismatches.Count + " mục cần đối chiếu.") + " Dung sai " + Number(options.ToleranceMm) + " mm · Bỏ qua " + command.IgnoredEntityCount + " đối tượng theo layer.";
            errors.Rows.Clear();
            foreach (var m in check.Mismatches)
            {
                int index = errors.Rows.Add("ERR-" + m.Id.ToString("00"), MismatchName(m.Type), Number(m.Deviation), (m.CutQualityIssue ? "[NÉT CẮT] " : "[HÌNH HỌC] ") + m.Description,
                    m.HasDrawingPosition ? Number(m.DrawingPosition.X) : "—", m.HasDrawingPosition ? Number(m.DrawingPosition.Y) : "—",
                    m.HasDxfPosition ? Number(m.DxfPosition.X) : "—", m.HasDxfPosition ? Number(m.DxfPosition.Y) : "—"); errors.Rows[index].Tag = m;
            }
            errorsTab.Text = "Vị trí lỗi (" + check.Mismatches.Count + ")"; mark.Enabled = check.Mismatches.Count > 0; save.Enabled = true;
            if (check.AlignmentFound && command.PreviewDrawingEdges != null && command.PreviewDxfEdges != null)
            {
                previewDrawing = PreviewFile("Drawing " + command.ViewName, command.PreviewDrawingEdges, new Translation2D());
                previewDxf = PreviewFile(Path.GetFileName(file.Text), command.PreviewDxfEdges, check.Translation);
            }
            fit.Enabled = previewDxf != null; focus.Enabled = previewDxf != null && WorstLocatedMismatch() != null;
            selectedName.Text = Path.GetFileName(file.Text) + " ↔ " + command.ViewName; errors.ClearSelection(); ShowOverlay(null, false);
            selectionDetail.Text = check.Passed ? "PASS: biên ngoài và các lỗ / biên bên trong khớp trong dung sai." : "Bấm một dòng lỗi để xem mô tả, tọa độ và phóng tới vị trí. Tọa độ dùng hệ View 1:1 mm.";
            if (!check.AlignmentFound) selectionDetail.Text += "\r\nChưa căn được X/Y nên chưa có hình chồng hợp lệ.";
        }
        private void PresentError(string message)
        {
            ResetResult(message); resultStatus.Text = "ERROR — CHƯA SO"; resultStatus.ForeColor = Amber;
            report.Text = "ERROR — Không có kết quả kiểm tra hợp lệ.\r\n" + message;
            status.Text = "Không thể hoàn tất kiểm tra. Xem nguyên nhân trong báo cáo chi tiết."; selectionDetail.Text = message; save.Enabled = true;
        }
        private static void SetValue(Label label, string text, bool evaluated, bool passed) { label.Text = text; label.ForeColor = !evaluated ? Muted : passed ? Green : Red; }
        private static DxfFileResult PreviewFile(string name, IEnumerable<DxfEdge> edges, Translation2D offset)
        {
            return new DxfFileResult { RelativeName = name, Edges = edges.Select(edge => new DxfEdge { Type = edge.Type, Space = "MODEL",
                Breaks = new List<double>(edge.Breaks), Evaluate = t => offset.Apply(edge.At(t)) }).ToList() };
        }
        private GeometryMismatch SelectedMismatch() { return errors.SelectedRows.Count == 0 ? null : errors.SelectedRows[0].Tag as GeometryMismatch; }
        private GeometryMismatch WorstLocatedMismatch() { return result?.Mismatches.Where(m => m.HasDrawingPosition || m.HasDxfPosition).OrderByDescending(m => m.Deviation).FirstOrDefault(); }
        private void SelectMismatch()
        {
            var m = SelectedMismatch(); if (m == null) return;
            selectionDetail.Text = "ERR-" + m.Id.ToString("00") + " · " + m.Type + " · Lệch " + Number(m.Deviation) + " mm\r\n" + m.Description +
                "\r\nDrawing: " + (m.HasDrawingPosition ? Number(m.DrawingPosition.X) + ", " + Number(m.DrawingPosition.Y) : "—") +
                "   |   DXF đã dịch: " + (m.HasDxfPosition ? Number(m.DxfPosition.X) + ", " + Number(m.DxfPosition.Y) : "—"); ShowOverlay(m, true);
        }
        private void ShowOverlay(GeometryMismatch mismatch, bool zoomToError)
        {
            if (previewDxf == null || previewDrawing == null || result == null) return;
            bool located = mismatch != null && (mismatch.HasDrawingPosition || mismatch.HasDxfPosition);
            DxfPoint drawingPoint = located ? (mismatch.HasDrawingPosition ? mismatch.DrawingPosition : mismatch.DxfPosition) : new DxfPoint();
            DxfPoint dxfPoint = located ? (mismatch.HasDxfPosition ? mismatch.DxfPosition : mismatch.DrawingPosition) : new DxfPoint();
            // DxfPreview renders this supplied identity transform; it never fits geometry.
            preview.SetComparison(previewDxf, new DxfPartComparison { First = previewDxf, Second = previewDrawing, SecondToFirst = DxfTransform.Identity,
                Exact = !located, DifferenceA = dxfPoint, DifferenceB = drawingPoint, MaxBoundaryGap = located ? mismatch.Deviation : 0 }, zoomToError && located);
        }
        private string FormatReport(DxfCheckResult check, DrawingDxfCheckOptions options)
        {
            var text = new StringBuilder(); text.AppendLine("DXF CHECK RESULT — " + check.Status);
            text.AppendLine("Drawing: " + command.DrawingPath); text.AppendLine("Sheet: " + command.SheetName); text.AppendLine("Drawing View: " + command.ViewName);
            text.AppendLine("DXF: " + file.Text); text.AppendLine("Tolerance: " + Number(options.ToleranceMm) + " mm");
            text.AppendLine("Ignored layers: " + options.IgnoredDxfLayers + " (" + command.IgnoredEntityCount + " entities)");
            text.AppendLine(); text.AppendLine("Scale: " + (!check.ScaleEvaluated ? "NOT CHECKED" : check.ScalePassed ? "PASS" : "SCALE FAIL"));
            if (check.ScaleEvaluated) text.AppendLine("Estimated DXF scale: " + check.EstimatedScale.ToString("0.000000", CultureInfo.InvariantCulture));
            foreach (string measurement in check.ScaleMeasurements) text.AppendLine("  " + measurement);
            text.AppendLine("Translation X/Y: " + (check.AlignmentFound ? Number(check.Translation.OffsetX) + ", " + Number(check.Translation.OffsetY) + " mm" : "NOT ALIGNED"));
            text.AppendLine("Positions below use View-local 1:1 mm coordinates.");
            text.AppendLine("Outer contour: " + (check.AlignmentFound ? (check.OuterContourPassed ? "PASS" : "FAIL") : "NOT CHECKED"));
            text.AppendLine("Inside contours — Drawing: " + check.DrawingInsideContourCount + "; DXF: " + check.DxfInsideContourCount + (check.AlignmentFound ? "; Matched: " + check.MatchedInsideContours + "; Failed: " + check.FailedInsideContours : "; NOT CHECKED"));
            text.AppendLine("Maximum measured deviation: " + (check.AlignmentFound ? Number(check.MaximumDeviation) + " mm" : "NOT MEASURED"));
            text.AppendLine("Geometry: " + (check.GeometryPassed ? "PASS" : "FAIL"));
            text.AppendLine("Cut quality: " + (check.CutQualityPassed ? "PASS" : "WARN"));
            if (check.Source != null)
            {
                text.AppendLine("Configuration: " + check.Source.Configuration);
                text.AppendLine("Drawing SHA256: " + check.Source.DrawingSha256);
                text.AppendLine("DXF SHA256: " + check.Source.DxfSha256);
                text.AppendLine("Unsaved Drawing before check: " + check.Source.DrawingWasUnsaved);
                text.AppendLine("Add-in build: " + check.Source.AddinVersion);
            }
            foreach (var m in check.Mismatches)
            {
                text.AppendLine(); text.AppendLine("ERR-" + m.Id.ToString("00") + " — " + m.Type); text.AppendLine(m.Description);
                if (m.HasDrawingPosition) text.AppendLine("Drawing position: " + Number(m.DrawingPosition.X) + ", " + Number(m.DrawingPosition.Y));
                if (m.HasDxfPosition) text.AppendLine("DXF position (translation applied): " + Number(m.DxfPosition.X) + ", " + Number(m.DxfPosition.Y)); text.AppendLine("Deviation: " + Number(m.Deviation) + " mm");
            }
            text.AppendLine(); text.AppendLine("FINAL RESULT: " + check.Status); return text.ToString();
        }
        private static string Number(double value) { return value.ToString("0.000", CultureInfo.InvariantCulture); }
        private static string MismatchName(string type)
        {
            switch (type)
            {
                case "OUTER_CONTOUR_MISMATCH": case "Outer contour mismatch": return "Biên ngoài lệch";
                case "SCALE FAIL": return "Sai tỉ lệ";
                case "Missing inside contour": return "Thiếu lỗ / rãnh";
                case "Extra DXF contour": return "Dư lỗ / rãnh";
                case "Inside contour mismatch": return "Lỗ / rãnh lệch";
                case "Open DXF geometry": return "Nét DXF hở";
                case "Extra outer contour": return "Dư biên ngoài";
                case "DXF contour": return "DXF thiếu biên kín";
                case "Alignment": return "Chưa căn X/Y";
                default: return type;
            }
        }
        private void SaveReport(object sender, EventArgs args)
        {
            Attempt(() => { using (var dialog = new SaveFileDialog { Filter = "Text report (*.txt)|*.txt", FileName = Path.GetFileNameWithoutExtension(file.Text) + "_DXF_VIEW_CHECK.txt" })
                if (dialog.ShowDialog(this) == DialogResult.OK) File.WriteAllText(dialog.FileName, report.Text, new UTF8Encoding(true)); });
        }
        private void Attempt(Action action) { try { action(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); } }
        private static Label Label(string text, float size, bool bold, Color color) { return new Label { Text = text, Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular), ForeColor = color, AutoEllipsis = true }; }
        private static ModernButton Button(string text, bool primary, int width)
        {
            return new ModernButton { Text = text, Size = new Size(width, 32), BorderRadius = 6, TabStop = true,
                NormalColor = primary ? Blue : Color.White, HoverColor = primary ? Color.FromArgb(29, 83, 185) : Color.FromArgb(240, 245, 252),
                PressColor = primary ? Color.FromArgb(26, 71, 154) : Color.FromArgb(226, 235, 248), BorderColor = primary ? Blue : Color.FromArgb(218, 226, 236),
                ForeColor = primary ? Color.White : Ink, Font = new Font("Segoe UI", 9.5F, FontStyle.Bold) };
        }
        private static Control Card(string title, Label value)
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(10, 6, 6, 4), Margin = new Padding(3), BackColor = Color.FromArgb(248, 250, 252) };
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 18)); panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var heading = Label(title, 8F, true, Muted); heading.Dock = DockStyle.Fill; value.Dock = DockStyle.Fill; panel.Controls.Add(heading, 0, 0); panel.Controls.Add(value, 0, 1); return panel;
        }
        private static DataGridView Grid()
        {
            var grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                BackgroundColor = Color.White, BorderStyle = BorderStyle.None, EnableHeadersVisualStyles = false, CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                GridColor = Color.FromArgb(235, 240, 246), ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None, ColumnHeadersHeight = 34,
                ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(243, 246, 250), ForeColor = Muted, Font = new Font("Segoe UI", 9F, FontStyle.Bold) },
                DefaultCellStyle = new DataGridViewCellStyle { Font = new Font("Segoe UI", 9.5F), ForeColor = Ink, SelectionBackColor = Color.FromArgb(227, 239, 255), SelectionForeColor = Ink, Padding = new Padding(4, 3, 3, 3) },
                AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(251, 253, 255) }, RowTemplate = { Height = 30 } };
            UIHelper.EnableDoubleBuffer(grid); return grid;
        }
    }
}
