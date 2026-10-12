using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using ADDIN.Commands.DrawingDxfExporting;

namespace ADDIN.UI
{
    internal sealed class BomDxfExportPane : UserControl
    {
        private static readonly Color Ink = Color.FromArgb(24, 42, 66), Muted = Color.FromArgb(105, 122, 143);
        private readonly TextBox folder = new TextBox();
        private readonly CheckBox bend = Toggle("Đường uốn", false), laser = Toggle("Vạch dấu"), sketches = Toggle("Sketch MARK");
        private readonly ComboBox checkMode = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, Margin = Padding.Empty };
        private readonly ModernButton start = Button("Xuất", true), stop = Button("Dừng", false);
        private readonly ModernButton browse = Button("Chọn…", false), back = Button("BOM", false);
        private readonly ModernButton report = Button("Báo cáo", false), output = Button("Thư mục", false);
        private readonly ModernButton openDxf = Button("Mở DXF", false);
        private readonly ModernButton review = Button("Preview / Sửa", false);
        private readonly Label status = Label("Tick dòng trong BOM, chọn thư mục rồi bấm Xuất DXF.", false);
        private readonly TextBox detail = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill, BackColor = Color.FromArgb(248, 250, 252), BorderStyle = BorderStyle.FixedSingle };
        private readonly Label selectedLabel = Label("FLAT VIEW · 1:1", false);
        private readonly ProgressBar progress = new ProgressBar();
        private readonly DataGridView results = new DataGridView();
        private readonly DxfStatPill ok = new DxfStatPill("OK", Color.FromArgb(16, 130, 98), Color.FromArgb(235, 250, 244), Color.FromArgb(190, 229, 209));
        private readonly DxfStatPill warn = new DxfStatPill("NG", Color.FromArgb(207, 65, 78), Color.FromArgb(255, 240, 241), Color.FromArgb(239, 202, 208));
        private readonly DxfStatPill error = new DxfStatPill("LỖI", Color.FromArgb(166, 106, 21), Color.FromArgb(255, 249, 232), Color.FromArgb(239, 220, 174));
        private readonly Dictionary<int, DataGridViewRow> rowMap = new Dictionary<int, DataGridViewRow>();
        private bool running, selecting;
        private string reportPath, selectedDxfPath;
        private bool hasCheckResults;
        internal event Action StartRequested, StopRequested, BackRequested;
        internal event Action<string> ReviewRequested;

        internal BomDxfExportPane()
        {
            Name = "bomDxfExportPane"; Font = TaiTheme.Body; BackColor = Color.White;
            ForeColor = Ink; BorderStyle = BorderStyle.FixedSingle; AutoScaleMode = AutoScaleMode.Dpi;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), ColumnCount = 1, RowCount = 11 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (float height in new[] { 52F, 24F, 36F, 34F, 30F, 38F, 38F, 44F, 5F }) root.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            root.RowStyles[3].SizeType = SizeType.AutoSize;
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 140)); Controls.Add(root);
            var heading = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
            heading.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34)); heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 68));
            heading.Controls.Add(new DxfLogo { Margin = new Padding(0, 5, 0, 0) }, 0, 0);
            var titles = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = Padding.Empty };
            titles.RowStyles.Add(new RowStyle(SizeType.Absolute, 26)); titles.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            titles.Controls.Add(Label("XUẤT + CHECK DXF", true), 0, 0); titles.Controls.Add(selectedLabel, 0, 1);
            heading.Controls.Add(titles, 1, 0); heading.Controls.Add(back, 2, 0); root.Controls.Add(heading, 0, 0);
            root.Controls.Add(Label("THƯ MỤC XUẤT", true), 0, 1);
            var folderRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            folderRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64));
            folder.Dock = DockStyle.Fill; folder.BackColor = Color.FromArgb(248, 250, 252); folder.Margin = new Padding(0, 4, 6, 0);
            folderRow.Controls.Add(folder, 0, 0); folderRow.Controls.Add(browse, 1, 0); root.Controls.Add(folderRow, 0, 2);
            browse.Click += (s, e) => {
                using (var dialog = new FolderBrowserDialog { Description = "Thư mục xuất DXF", SelectedPath = Directory.Exists(folder.Text) ? folder.Text : "" })
                    if (dialog.ShowDialog(this) == DialogResult.OK) folder.Text = dialog.SelectedPath;
            };
            var options = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = Padding.Empty };
            options.Controls.Add(bend); options.Controls.Add(laser); options.Controls.Add(sketches); root.Controls.Add(options, 0, 3);
            laser.CheckedChanged += (s, e) => sketches.Enabled = laser.Checked && !running;
            var tips = new ToolTip { ShowAlways = true };
            tips.SetToolTip(laser, "Đưa cạnh của feature tên MARK/-mark và miệng lỗ côn/bậc vào LASER_MARK màu vàng. Biên ngoài vẫn cắt ở CUT.");
            tips.SetToolTip(sketches, "Giữ sketch trên Drawing View thuộc layer chứa MARK. Chỉ xuất khi bật Vạch dấu.");
            checkMode.Items.AddRange(new object[] { "Xuất tới đâu → Check tới đó", "Xuất hết → Check toàn bộ" });
            checkMode.SelectedIndex = 0; root.Controls.Add(checkMode, 0, 4);
            var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = Padding.Empty };
            actions.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            for (int i = 0; i < 4; i++) actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            actions.Controls.Add(start, 0, 0); actions.Controls.Add(stop, 1, 0); actions.Controls.Add(report, 2, 0); actions.Controls.Add(output, 3, 0);
            root.Controls.Add(actions, 0, 5);
            start.Click += (s, e) => StartRequested?.Invoke(); stop.Click += (s, e) => StopRequested?.Invoke(); back.Click += (s, e) => BackRequested?.Invoke();
            report.Click += (s, e) => Open(reportPath); output.Click += (s, e) => Open(folder.Text);
            var metrics = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
            metrics.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            for (int i = 0; i < 3; i++) metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333F));
            ok.Dock = warn.Dock = error.Dock = DockStyle.Fill;
            metrics.Controls.Add(ok, 0, 0); metrics.Controls.Add(warn, 1, 0); metrics.Controls.Add(error, 2, 0); root.Controls.Add(metrics, 0, 6);
            status.Dock = DockStyle.Fill; root.Controls.Add(status, 0, 7);
            progress.Dock = DockStyle.Fill; progress.Margin = Padding.Empty; root.Controls.Add(progress, 0, 8);
            results.Dock = DockStyle.Fill; results.BackgroundColor = Color.White; results.BorderStyle = BorderStyle.None;
            results.ReadOnly = true; results.AllowUserToAddRows = results.AllowUserToDeleteRows = results.AllowUserToResizeRows = false;
            results.RowHeadersVisible = false; results.MultiSelect = false; results.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            results.EnableHeadersVisualStyles = false; results.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            results.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(241, 245, 250), ForeColor = Ink,
                Font = TaiTheme.SmallStrong, WrapMode = DataGridViewTriState.True, SelectionBackColor = Color.FromArgb(241, 245, 250) };
            results.DefaultCellStyle = new DataGridViewCellStyle { ForeColor = Ink, SelectionBackColor = Color.FromArgb(225, 238, 253), SelectionForeColor = Ink };
            results.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(249, 251, 254);
            results.GridColor = Color.FromArgb(235, 240, 246); results.RowTemplate.Height = 32;
            results.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            results.ColumnHeadersHeight = 44;
            results.Columns.Add(new DataGridViewTextBoxColumn { Name = "part", HeaderText = "部品\n番号", FillWeight = 19, MinimumWidth = 48 });
            results.Columns.Add(new DataGridViewTextBoxColumn { Name = "state", HeaderText = "Xuất", FillWeight = 17, MinimumWidth = 44 });
            results.Columns.Add(new DataGridViewTextBoxColumn { Name = "check", HeaderText = "Check", FillWeight = 18, MinimumWidth = 48 });
            results.Columns.Add(new DataGridViewTextBoxColumn { Name = "file", HeaderText = "DXF / bước chạy", FillWeight = 46, MinimumWidth = 80 });
            results.SelectionChanged += (s, e) => { if (!selecting) ShowSelected(); }; root.Controls.Add(results, 0, 9);
            var detailCard = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0, 5, 0, 0) };
            detailCard.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); detailCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var detailHeading = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
            detailHeading.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            detailHeading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            detailHeading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120)); detailHeading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 94));
            detailHeading.Controls.Add(Label("CHI TIẾT", true), 0, 0); detailHeading.Controls.Add(review, 1, 0); detailHeading.Controls.Add(openDxf, 2, 0);
            detailCard.Controls.Add(detailHeading, 0, 0); detailCard.Controls.Add(detail, 0, 1); root.Controls.Add(detailCard, 0, 10);
            openDxf.Click += (s, e) => Open(selectedDxfPath);
            review.Click += (s, e) => ReviewRequested?.Invoke(SelectedOutputPath());
            results.CellDoubleClick += (s, e) => { if (!running && hasCheckResults && e.RowIndex >= 0) ReviewRequested?.Invoke(SelectedOutputPath()); };
            detail.Text = "Vạch dấu: lấy cạnh feature có tên MARK và miệng lỗ côn/bậc. Sketch MARK: giữ nét MARK trên Drawing. Chọn dòng để xem lỗi.";
            SetRunning(false); report.Enabled = false; output.Enabled = false;
        }
        private static CheckBox Toggle(string text, bool ticked = true) { return new CheckBox { Text = text, Checked = ticked, AutoSize = true, Margin = new Padding(0, 4, 10, 4) }; }
        private static Label Label(string text, bool bold) { return new Label { Text = text, Dock = DockStyle.Fill, ForeColor = bold ? Ink : Muted,
            Font = bold ? TaiTheme.BodyStrong : TaiTheme.Small, Margin = Padding.Empty, AutoEllipsis = true }; }
        private static ModernButton Button(string text, bool primary)
        {
            return new ModernButton { Text = text, Dock = DockStyle.Fill, Margin = new Padding(0, 2, 4, 2), BorderRadius = 5,
                Font = TaiTheme.BodyStrong, ForeColor = primary ? Color.White : Ink,
                NormalColor = primary ? Color.FromArgb(37, 99, 211) : Color.White,
                BorderColor = primary ? Color.FromArgb(37, 99, 211) : Color.FromArgb(218, 226, 236),
                HoverColor = primary ? Color.FromArgb(29, 78, 190) : Color.FromArgb(241, 245, 250), Padding = new Padding(2), AutoSize = false };
        }
        internal void Prepare(string targetFolder, int selected, bool replaceFolder = false) { if (replaceFolder || string.IsNullOrWhiteSpace(folder.Text)) folder.Text = targetFolder; selectedLabel.Text = selected + " dòng · Flat 1:1 · Tắt màu"; }
        internal BomDxfExportOptions ReadOptions()
        {
            string path = folder.Text.Trim();
            if (path.Length == 0 || !Path.IsPathRooted(path)) throw new ArgumentException("Chọn đường dẫn thư mục đầy đủ.");
            path = Path.GetFullPath(path); if (File.Exists(path)) throw new ArgumentException("Thư mục đang trỏ đến một file.");
            return new BomDxfExportOptions { TargetFolder = path, IncludeBendLines = bend.Checked, IncludeLaserMarks = laser.Checked,
                RecognizeFeatureMarks = laser.Checked, KeepDrawingMarkSketches = laser.Checked && sketches.Checked, CheckWhileExporting = checkMode.SelectedIndex == 0 };
        }
        internal void BeginBatch(int total)
        {
            rowMap.Clear(); results.Rows.Clear(); selectedDxfPath = null; detail.Clear(); hasCheckResults = false;
            progress.Value = 0; progress.Maximum = Math.Max(1, total * 2); ok.Count = warn.Count = error.Count = 0;
            reportPath = null; report.Enabled = false; output.Enabled = false;
            selectedLabel.Text = total + " dòng · Flat 1:1 · Tắt màu"; SetRunning(true); ShowMessage("Đang chuẩn bị các dòng được chọn…", false);
        }
        internal void ShowItem(BomDxfFileResult item, int done, int total)
        {
            if (IsDisposed) return;
            selecting = true;
            try {
                if (!rowMap.TryGetValue(item.BomRow, out DataGridViewRow row)) { row = results.Rows[results.Rows.Add()]; rowMap.Add(item.BomRow, row); }
                row.Tag = item; row.Cells[0].Value = item.PartNumber; row.Cells[1].Value = StatusText(item.Status);
                row.Cells[2].Value = item.CheckStatus == "NG" ? "NG " + item.CheckIssueCount : StatusText(item.CheckStatus);
                row.Cells[3].Value = item.Status == "RUNNING" ? item.Phase : item.CheckStatus == "CHECKING" ? "Check biên CUT" : Path.GetFileName(item.OutputPath);
                row.Cells[2].ToolTipText = item.CheckMessage;
                row.Cells[3].ToolTipText = item.CheckMessage + "\r\n" + item.Message + "\r\n" + item.OutputPath;
                row.Cells[1].Style.ForeColor = item.Status == "ERROR" ? Color.FromArgb(207, 65, 78) : item.Status == "WARN" ? Color.FromArgb(166, 106, 21) : item.Status == "OK" ? Color.FromArgb(16, 130, 98) : Muted;
                int checkedRows = rowMap.Values.Count(r => new[] { "OK", "NG", "ERROR", "CANCEL" }.Contains(((BomDxfFileResult)r.Tag).CheckStatus));
                progress.Maximum = Math.Max(1, total * 2); progress.Value = Math.Min(progress.Maximum, Math.Max(0, done + checkedRows));
                row.Cells[2].Style.ForeColor = item.CheckStatus == "NG" || item.CheckStatus == "ERROR" ? Color.FromArgb(207, 65, 78) : item.CheckStatus == "OK" ? Color.FromArgb(16, 130, 98) : Muted;
                ok.Count = rowMap.Values.Count(r => ((BomDxfFileResult)r.Tag).CheckStatus == "OK");
                warn.Count = rowMap.Values.Count(r => ((BomDxfFileResult)r.Tag).CheckStatus == "NG");
                error.Count = rowMap.Values.Count(r => ((BomDxfFileResult)r.Tag).Status == "ERROR" || ((BomDxfFileResult)r.Tag).CheckStatus == "ERROR");
                var activeExport = rowMap.Values.Select(r => (BomDxfFileResult)r.Tag).FirstOrDefault(i => i.Status == "RUNNING");
                var activeCheck = rowMap.Values.Select(r => (BomDxfFileResult)r.Tag).FirstOrDefault(i => i.CheckStatus == "CHECKING");
                status.Text = "Xuất " + done + "/" + total + (activeExport == null ? "" : " · " + activeExport.PartNumber) +
                    "\r\nCheck " + checkedRows + "/" + total + (activeCheck == null ? "" : " · " + activeCheck.PartNumber);
                if (item.Status == "RUNNING" || (activeExport == null && item.CheckStatus == "CHECKING") || results.SelectedRows.Count == 0) {
                    results.ClearSelection(); row.Selected = true; results.CurrentCell = row.Cells[0];
                    if (results.IsHandleCreated && results.ClientSize.Height > results.ColumnHeadersHeight + results.RowTemplate.Height) {
                        try { results.FirstDisplayedScrollingRowIndex = row.Index; }
                        catch (InvalidOperationException) { /* A dock/resize can temporarily leave no row viewport. */ }
                    }
                }
            }
            finally { selecting = false; }
            ShowSelected(); Refresh(); Application.DoEvents();
        }
        private static string StatusText(string state) { switch (state) { case "READY": return "Chờ"; case "RUNNING": return "Chạy";
            case "QUEUED": return "Chờ";
            case "REUSED": return "Chung"; case "CHECKING": return "Chạy"; case "SKIP": return "—";
            case "CANCEL": return "Dừng"; case "ERROR": return "LỖI"; default: return state; } }
        internal void Complete(BomDxfBatchResult batch)
        {
            hasCheckResults = batch.CheckBatch != null && batch.CheckBatch.Files.Count > 0;
            SetRunning(false); reportPath = File.Exists(batch.CheckReportPath) ? batch.CheckReportPath : batch.ReportPath;
            report.Enabled = File.Exists(reportPath); output.Enabled = Directory.Exists(batch.TargetFolder);
            ShowMessage((batch.Canceled ? "Đã dừng · " : "") + "Xuất: " + batch.Exported + " file · " + batch.Errors + " lỗi\r\n" +
                "Check: " + batch.CheckPassed + " OK · " + batch.CheckNg + " NG · " + batch.CheckErrors + " lỗi",
                batch.Errors > 0 || batch.CheckNg > 0 || batch.CheckErrors > 0); if (!batch.Canceled) progress.Value = progress.Maximum;
        }
        internal void ShowMessage(string message, bool failed) { status.Text = message; status.ForeColor = failed ? Color.FromArgb(207, 65, 78) : Muted; }
        internal void SetRunning(bool value)
        {
            running = value; start.Enabled = back.Enabled = browse.Enabled = folder.Enabled = bend.Enabled = laser.Enabled = checkMode.Enabled = !value;
            sketches.Enabled = !value && laser.Checked; stop.Enabled = value;
            openDxf.Enabled = !value && File.Exists(selectedDxfPath);
            review.Enabled = !value && hasCheckResults;
        }
        private string SelectedOutputPath()
        { return (results.SelectedRows.Count > 0 ? results.SelectedRows[0].Tag as BomDxfFileResult : null)?.OutputPath; }
        private void ShowSelected()
        {
            var item = results.SelectedRows.Count > 0 ? results.SelectedRows[0].Tag as BomDxfFileResult : null;
            if (item == null) { selectedDxfPath = null; openDxf.Enabled = false; return; }
            detail.Text = item.PartNumber + " · " + item.SheetName + " / " + item.ViewName +
                "\r\nXuất: " + StatusText(item.Status) + " · " + (item.Status == "ERROR" ? item.Message : item.Phase) +
                "\r\nCheck: " + StatusText(item.CheckStatus) + " · " + item.CheckMessage +
                (item.CheckIssues.Count == 0 ? "" : "\r\n" + string.Join("\r\n", item.CheckIssues)) + "\r\n" + item.OutputPath;
            selectedDxfPath = File.Exists(item.CheckOutputPath) ? item.CheckOutputPath : item.OutputPath;
            openDxf.Text = item.CheckStatus == "NG" && File.Exists(item.CheckOutputPath) ? "DXF lỗi" : "Mở DXF";
            openDxf.Enabled = !running && File.Exists(selectedDxfPath);
        }
        private void Open(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception ex) { ShowMessage("Không mở được kết quả: " + ex.Message, true); }
        }
    }
}
