using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using ADDIN.UI;

namespace ADDIN.Commands
{
    internal sealed class CheckHoleReportRow
    {
        public int Id { get; set; }
        public string Status { get; set; }
        public string Kind { get; set; }
        public string ReferenceComponent { get; set; }
        public string ActualComponent { get; set; }
        public double? OffsetMm { get; set; }
        public double? AngleDeg { get; set; }
        public string Reason { get; set; }
        public double[] ReferenceCenter { get; set; }
        public double[] ActualCenter { get; set; }
        public double[] ViewAxis { get; set; }
        public bool OrientFromReferenceSide { get; set; }
        public double ViewRadiusM { get; set; }
        public bool CanMark { get; set; }
    }

    internal sealed class CheckHoleReport
    {
        public string AssemblyTitle;
        public string Configuration;
        public int LogicalHoles;
        public int GroupedHoles;
        public int UnmatchedHoles;
        public int StackCount;
        public int NgStacks;
        public int PatternNg;
        public int MissingHoles;
        public double EngineSeconds;
        public CheckHoleConnectionCoverage ConnectionCoverage;
        public CheckHoleCg2Report PartRelations;
        public string MarkerStatus = "Chưa vẽ sketch NG.";
        public readonly List<CheckHoleReportRow> Rows = new List<CheckHoleReportRow>();

        public string Summary
        {
            get
            {
                var sb = new StringBuilder();
                if (!string.IsNullOrEmpty(AssemblyTitle))
                    sb.Append(AssemblyTitle).Append(" | ").Append(Configuration ?? "").AppendLine();
                sb.Append("Lỗ: ").Append(LogicalHoles)
                  .Append(" | Đã ghép: ").Append(GroupedHoles)
                  .Append(" | Chưa ghép: ").Append(UnmatchedHoles).AppendLine();
                sb.Append("Cụm: ").Append(StackCount)
                  .Append(" | OK: ").Append(StackCount - NgStacks)
                  .Append(" | NG: ").Append(NgStacks);
                if (PatternNg > 0 || MissingHoles > 0)
                {
                    sb.AppendLine();
                    sb.Append("NG pattern: ").Append(PatternNg)
                      .Append(" | Thiếu lỗ: ").Append(MissingHoles);
                }
                else
                {
                    sb.Append(" | Thiếu lỗ: ").Append(MissingHoles);
                }
                if (EngineSeconds > 0)
                    sb.Append(" | Xử lý: ").Append(EngineSeconds.ToString("0.00", CultureInfo.InvariantCulture)).Append(" s");
                if (ConnectionCoverage != null)
                    sb.AppendLine().Append("Nhóm lỗ đã ghép (chẩn đoán): ").Append(ConnectionCoverage.GroupCount)
                      .Append(" | NG chưa rõ Master: ").Append(ConnectionCoverage.NgGroupsWithoutMaster)
                      .Append(" | Lỗ chưa ghép: ").Append(ConnectionCoverage.UnmatchedHoleCount)
                      .Append(" | ").Append(ConnectionCoverage.CoverageStatus);
                if (PartRelations != null)
                    sb.AppendLine().Append("CG2 / Quan hệ Part (AABB chẩn đoán): ")
                      .Append(PartRelations.RecordedNearbyPairs)
                      .Append(" cặp gần | Ứng viên cần xem: ")
                      .Append(PartRelations.CandidateCount)
                      .Append(" | Part không phát hiện lỗ: ")
                      .Append(PartRelations.ComponentsWithoutDetectedHoles)
                      .Append(" | BREP có vật liệu: ")
                      .Append(PartRelations.BrepSolidEvidenceCount)
                      .Append(" | BREP không giao trục: ")
                      .Append(PartRelations.BrepNoAxisHitCount)
                      .Append(" | Chưa xác minh: ")
                      .Append(PartRelations.BrepUnknownCount + PartRelations.BrepNotTestedCount)
                      .Append(" | ").Append(PartRelations.CoverageStatus)
                      .Append(" | Container Subassembly: ").Append(PartRelations.SubassemblyContainerCount)
                      .Append(" | Part chưa resolve: ").Append(PartRelations.UnresolvedLeafOccurrenceCount);
                if (!string.IsNullOrEmpty(MarkerStatus))
                    sb.AppendLine().Append(MarkerStatus);
                return sb.ToString();
            }
        }
    }

    internal static class CheckHoleCsv
    {
        private static string Cell(string value)
        {
            value = value ?? "";
            string start = value.TrimStart();
            if (start.Length > 0 && "=+-@".IndexOf(start[0]) >= 0) value = "'" + value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        public static void Write(string path, CheckHoleReport report)
        {
            using (var writer = new StreamWriter(path, false, new UTF8Encoding(true)))
            {
                writer.WriteLine("Assembly,Configuration,Status,Type,Reference component,Actual component,Offset (mm),Angle (deg),Reason");
                foreach (CheckHoleReportRow row in report.Rows)
                    writer.WriteLine(string.Join(",", new[] {
                        Cell(report.AssemblyTitle), Cell(report.Configuration), Cell(row.Status), Cell(row.Kind),
                        Cell(row.ReferenceComponent), Cell(row.ActualComponent),
                        row.OffsetMm.HasValue ? row.OffsetMm.Value.ToString("R", CultureInfo.InvariantCulture) : "",
                        row.AngleDeg.HasValue ? row.AngleDeg.Value.ToString("R", CultureInfo.InvariantCulture) : "",
                        Cell(row.Reason) }));
            }
        }
    }

    internal sealed class CheckHoleResultsForm : Form
    {
        private readonly Label summary = new Label();
        private readonly Label footer = new Label();
        private readonly DataGridView grid = new DataGridView();
        private readonly ComboBox filter = new ComboBox();
        private readonly CheckBox diagnostic = new CheckBox();
        private readonly CheckBox autoMarkers = new CheckBox();
        private readonly Button rerun = new Button();
        private readonly Button cancel = new Button();
        private readonly Button find = new Button();
        private readonly Button selectedMarkers = new Button();
        private readonly Button allMarkers = new Button();
        private readonly Button export = new Button();
        private readonly Button connectionGroups = new Button();
        private readonly Button partRelations = new Button();
        private readonly TableLayoutPanel detailLayout;
        private readonly TableLayoutPanel compactLayout = new TableLayoutPanel();
        private readonly Label compactSummary = new Label();
        private readonly Label currentIssue = new Label();
        private readonly Button previousIssue = new Button();
        private readonly Button nextIssue = new Button();
        private readonly Button compactCancel = new Button();
        private readonly Button details = new Button();
        private int issueIndex = -1;
        private bool compactMode;
        private readonly Action<bool, bool> runAction;
        private readonly Action cancelAction;
        private readonly Action<CheckHoleReportRow> findAction;
        private readonly Action<IList<int>> markAction;
        private CheckHoleReport report;
        private bool busy;

        public bool DiagnosticEnabled { get { return diagnostic.Checked; } }
        public bool AutoMarkersEnabled { get { return autoMarkers.Checked; } }
        public Action<string> PublishNotice { get; set; }

        public void SetOptions(bool diagnosticEnabled, bool autoMarkersEnabled)
        {
            diagnostic.Checked = diagnosticEnabled;
            autoMarkers.Checked = autoMarkersEnabled;
        }

        public CheckHoleResultsForm(Action<bool, bool> run, Action requestCancel,
            Action<CheckHoleReportRow> locate, Action<IList<int>> mark)
        {
            runAction = run; cancelAction = requestCancel; findAction = locate; markAction = mark;
            Text = "CHECK HOLE — Kết quả kiểm tra";
            Font = new Font("Segoe UI", 9.5F);
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1200, 720);
            MinimumSize = new Size(940, 560);
            BackColor = TaiTheme.Surface;

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(6, 6, 6, 6),
                BackColor = TaiTheme.Surface
            };
            detailLayout = layout;
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(layout);

            summary.AutoSize = true;
            summary.AutoEllipsis = false;
            summary.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
            summary.ForeColor = TaiTheme.Text;
            summary.Margin = new Padding(0, 0, 0, 8);
            summary.Text = "Đang kiểm tra lỗ...";
            layout.Controls.Add(summary, 0, 0);

            var toolbar = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                WrapContents = true,
                Margin = new Padding(0, 0, 0, 6),
                Padding = Padding.Empty
            };

            filter.DropDownStyle = ComboBoxStyle.DropDownList;
            filter.Width = 115;
            filter.Font = new Font("Segoe UI", 9F);
            filter.Items.AddRange(new object[] { "Tất cả", "NG", "OK", "Chưa ghép" });
            filter.SelectedIndex = 0;
            filter.SelectedIndexChanged += (s, e) => ApplyFilter();
            toolbar.Controls.Add(filter);

            AddButton(toolbar, find, "Tìm vị trí", () => LocateSelected());
            AddButton(toolbar, rerun, "Kiểm tra lại", () => runAction(diagnostic.Checked, autoMarkers.Checked));
            AddButton(toolbar, selectedMarkers, "Vẽ NG đã chọn", () => markAction(SelectedRows().Where(x => x.CanMark).Select(x => x.Id).ToList()));
            AddButton(toolbar, allMarkers, "Vẽ toàn bộ NG", () => markAction(null));
            AddButton(toolbar, export, "Xuất CSV", ExportReport);
            AddButton(toolbar, connectionGroups, "Nhóm liên kết", ShowConnectionGroups);
            AddButton(toolbar, partRelations, "Quan hệ Part (CG2)", ShowPartRelations);
            AddButton(toolbar, cancel, "Hủy", () => cancelAction());

            autoMarkers.Text = "Tự vẽ NG";
            autoMarkers.AutoSize = true;
            autoMarkers.Checked = true;
            autoMarkers.Font = new Font("Segoe UI", 9F);
            autoMarkers.Margin = new Padding(2, 4, 4, 2);

            diagnostic.Text = "Chẩn đoán";
            diagnostic.AutoSize = true;
            diagnostic.Font = new Font("Segoe UI", 9F);
            diagnostic.Margin = new Padding(2, 4, 4, 2);

            var return3d = new Button();
            AddButton(toolbar, return3d, "Theo dõi 3D", Show3dControls);

            toolbar.Controls.Add(autoMarkers);
            toolbar.Controls.Add(diagnostic);

            var tips = new ToolTip();
            tips.SetToolTip(autoMarkers, "Tự động tạo sketch đánh dấu NG sau khi kiểm tra xong.");
            tips.SetToolTip(diagnostic, "Bật đối chiếu chẩn đoán bổ sung và ghi log để kiểm chứng. Không đổi ngưỡng OK/NG.");
            tips.SetToolTip(filter, "Lọc danh sách theo trạng thái: Tất cả, NG, OK, hoặc Chưa ghép");
            tips.SetToolTip(find, "Làm sáng (highlight) và xoay khung nhìn 3D đến lỗ đang chọn");
            tips.SetToolTip(rerun, "Thực hiện quét và kiểm tra lại cụm assembly");
            tips.SetToolTip(selectedMarkers, "Vẽ sketch đánh dấu các lỗ NG đang chọn trong bảng");
            tips.SetToolTip(allMarkers, "Vẽ sketch đánh dấu toàn bộ các vị trí NG phát hiện được");
            tips.SetToolTip(export, "Xuất báo cáo kết quả ra file Excel CSV");
            tips.SetToolTip(return3d, "Chuyển sang chế độ xem gọn điều khiển 3D");
            layout.Controls.Add(toolbar, 0, 1);

            grid.Dock = DockStyle.Fill;
            grid.ReadOnly = true;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AutoGenerateColumns = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = true;
            grid.RowHeadersVisible = false;
            grid.BackgroundColor = TaiTheme.Surface;
            grid.BorderStyle = BorderStyle.None;
            grid.GridColor = TaiTheme.Divider;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersHeight = 32;
            grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(231, 238, 248),
                ForeColor = TaiTheme.Text,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                Padding = new Padding(4, 2, 4, 2),
                WrapMode = DataGridViewTriState.False
            };
            grid.DefaultCellStyle = new DataGridViewCellStyle
            {
                ForeColor = TaiTheme.Text,
                BackColor = TaiTheme.Surface,
                SelectionBackColor = TaiTheme.AccentSoft,
                SelectionForeColor = TaiTheme.Text,
                Font = new Font("Segoe UI", 9F),
                Padding = new Padding(4, 2, 4, 2)
            };
            grid.RowTemplate.Height = 26;
            UIHelper.EnableDoubleBuffer(grid);

            AddColumn("Status", "Trạng thái", 65, 45, DataGridViewContentAlignment.MiddleCenter);
            AddColumn("Kind", "Loại", 70, 50, DataGridViewContentAlignment.MiddleLeft);
            AddColumn("ReferenceComponent", "Component chuẩn", 130, 80, DataGridViewContentAlignment.MiddleLeft);
            AddColumn("ActualComponent", "Component đối ứng", 130, 80, DataGridViewContentAlignment.MiddleLeft);
            AddColumn("OffsetMm", "Lệch (mm)", 75, 50, DataGridViewContentAlignment.MiddleRight, "0.000");
            AddColumn("AngleDeg", "Góc (°)", 65, 45, DataGridViewContentAlignment.MiddleRight, "0.000");
            AddColumn("Reason", "Diễn giải", 180, 90, DataGridViewContentAlignment.MiddleLeft);

            grid.SelectionChanged += (s, e) => UpdateActions();
            grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) LocateSelected(); };
            grid.CellFormatting += (s, e) =>
            {
                if (e.RowIndex < 0 || e.ColumnIndex != 0) return;
                string status = Convert.ToString(e.Value);
                if (status == "NG")
                {
                    e.CellStyle.ForeColor = TaiTheme.Danger;
                    e.CellStyle.SelectionForeColor = TaiTheme.Danger;
                    e.CellStyle.Font = new Font(grid.Font, FontStyle.Bold);
                }
                else if (status == "OK")
                {
                    e.CellStyle.ForeColor = TaiTheme.Success;
                    e.CellStyle.SelectionForeColor = TaiTheme.Success;
                }
                else
                {
                    e.CellStyle.ForeColor = TaiTheme.Warning;
                    e.CellStyle.SelectionForeColor = TaiTheme.Warning;
                }
            };
            layout.Controls.Add(grid, 0, 2);

            footer.AutoSize = true;
            footer.AutoEllipsis = false;
            footer.Font = new Font("Segoe UI", 8.5F);
            footer.ForeColor = TaiTheme.TextMuted;
            footer.Margin = new Padding(0, 6, 0, 0);
            footer.Text = "Chọn dòng hoặc nhấp đúp để tìm vị trí. Lỗ chưa ghép cần xem xét, chưa được kết luận OK/NG.";
            layout.Controls.Add(footer, 0, 3);

            compactLayout.Dock = DockStyle.Fill;
            compactLayout.ColumnCount = 1;
            compactLayout.RowCount = 3;
            compactLayout.Padding = new Padding(8);
            compactLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            compactLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            compactLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            compactSummary.Dock = DockStyle.Fill;
            compactSummary.AutoSize = true;
            compactSummary.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
            compactSummary.ForeColor = TaiTheme.Text;
            compactLayout.Controls.Add(compactSummary, 0, 0);

            currentIssue.Dock = DockStyle.Fill;
            currentIssue.Font = new Font("Segoe UI", 9F);
            currentIssue.ForeColor = TaiTheme.Text;
            currentIssue.Padding = new Padding(0, 6, 0, 0);
            compactLayout.Controls.Add(currentIssue, 0, 1);

            var navigation = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
            AddButton(navigation, previousIssue, "← Lỗi trước", () => NavigateIssue(-1));
            AddButton(navigation, nextIssue, "Lỗi tiếp →", () => NavigateIssue(1));
            AddButton(navigation, details, "Chi tiết", ShowDetails);
            AddButton(navigation, compactCancel, "Hủy", () => cancelAction());
            compactLayout.Controls.Add(navigation, 0, 2);

            Controls.Add(compactLayout);
            compactLayout.Visible = false;

            FormClosing += (s, e) => { if (busy) { cancelAction(); e.Cancel = true; } };

            Action updateWrapWidths = () =>
            {
                int w = Math.Max(100, (detailLayout?.ClientSize.Width ?? ClientSize.Width) - 12);
                summary.MaximumSize = new Size(w, 0);
                footer.MaximumSize = new Size(w, 0);
                compactSummary.MaximumSize = new Size(Math.Max(100, (compactLayout?.ClientSize.Width ?? ClientSize.Width) - 12), 0);
            };
            SizeChanged += (s, e) => updateWrapWidths();
            UpdateActions();
        }

        public void Show3dControls()
        {
            compactMode = true;
            detailLayout.Visible = false;
            compactLayout.Visible = true;
            if (!TopLevel) { MinimumSize = Size.Empty; return; }
            MinimumSize = new Size(450, 280);
            Size = new Size(470, 310);
            if (!IsHandleCreated)
            {
                StartPosition = FormStartPosition.Manual;
                Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
                Location = new Point(Math.Max(area.Left, area.Right - Width - 20), area.Top + 70);
            }
            UpdateActions();
        }

        private void ShowDetails()
        {
            compactMode = false;
            compactLayout.Visible = false;
            detailLayout.Visible = true;
            if (!TopLevel) return;
            MinimumSize = new Size(940, 560);
            Size = new Size(1200, 720);
            Rectangle area = Screen.FromControl(this).WorkingArea;
            Location = new Point(area.Left + Math.Max(0, (area.Width - Width) / 2), area.Top + Math.Max(0, (area.Height - Height) / 2));
        }

        private void NavigateIssue(int step)
        {
            if (busy || report == null) return;
            var issues = report.Rows.Where(x => x.CanMark).ToList();
            if (issues.Count == 0) return;
            issueIndex = issueIndex < 0 ? (step < 0 ? issues.Count - 1 : 0)
                : (issueIndex + step + issues.Count) % issues.Count;
            CheckHoleReportRow row = issues[issueIndex];
            currentIssue.Text = "Lỗi " + (issueIndex + 1) + "/" + issues.Count + " | " + row.Kind + Environment.NewLine
                + row.ReferenceComponent + " ↔ " + row.ActualComponent + Environment.NewLine
                + (row.OffsetMm.HasValue ? "Lệch tâm: " + row.OffsetMm.Value.ToString("0.000", CultureInfo.InvariantCulture) + " mm. " : "")
                + row.Reason;
            findAction(row);
        }

        private static void AddButton(Control parent, Button button, string text, Action action)
        {
            button.Text = text;
            button.AutoSize = true;
            button.Font = new Font("Segoe UI", 9F);
            button.Padding = new Padding(5, 2, 5, 2);
            button.Margin = new Padding(0, 0, 4, 4);
            button.Click += (s, e) => action();
            parent.Controls.Add(button);
        }

        private void AddColumn(string property, string title, int width, int minWidth, DataGridViewContentAlignment alignment, string format = null)
        {
            var column = new DataGridViewTextBoxColumn
            {
                DataPropertyName = property,
                HeaderText = title,
                Name = property,
                Width = width,
                MinimumWidth = minWidth,
                FillWeight = width,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
            column.HeaderCell.Style.Alignment = alignment;
            column.DefaultCellStyle.Alignment = alignment;
            if (format != null) column.DefaultCellStyle.Format = format;
            grid.Columns.Add(column);
        }

        public void SetBusy(bool value, string message)
        {
            busy = value;
            if (message != null)
            {
                summary.Text = message;
                compactSummary.Text = message;
                if (compactMode) compactSummary.Refresh();
                else summary.Refresh();
            }
            UpdateActions();
        }

        public void BeginRun()
        {
            report = null;
            issueIndex = -1;
            currentIssue.Text = "";
            grid.DataSource = null;
            SetBusy(true, "Đang kiểm tra lỗ... Nhấn Hủy hoặc ESC để dừng.");
        }

        public void SetReport(CheckHoleReport value)
        {
            bool changedReport = !ReferenceEquals(report, value);
            report = value;
            summary.Text = value != null ? value.Summary : "";
            compactSummary.Text = value != null ? value.Summary : "";
            if (changedReport)
            {
                issueIndex = -1;
                currentIssue.Text = "Bấm Lỗi tiếp để zoom và nhìn theo trục lỗ."
                    + (TopLevel ? " Có thể kéo cửa sổ này sang bên." : "");
            }
            UpdateFilterItems();
            ApplyFilter();
        }

        private void UpdateFilterItems()
        {
            if (report == null)
            {
                filter.Items.Clear();
                filter.Items.AddRange(new object[] { "Tất cả", "NG", "OK", "Chưa ghép" });
                filter.SelectedIndex = 0;
                return;
            }

            int total = report.Rows.Count;
            int ng = report.Rows.Count(x => x.Status == "NG");
            int ok = report.Rows.Count(x => x.Status == "OK");
            int unmerged = report.Rows.Count(x => x.Status == "Chưa ghép");

            string itemAll = string.Format("Tất cả ({0})", total);
            string itemNg = string.Format("NG ({0})", ng);
            string itemOk = string.Format("OK ({0})", ok);
            string itemUnmerged = string.Format("Chưa ghép ({0})", unmerged);

            filter.BeginUpdate();
            try
            {
                filter.Items.Clear();
                filter.Items.Add(itemAll);
                filter.Items.Add(itemNg);
                filter.Items.Add(itemOk);
                filter.Items.Add(itemUnmerged);

                // Smart selection:
                // 1. If there are NG rows, select NG so errors are shown first.
                // 2. If there are NO NG rows, default to "Tất cả" so all rows (e.g. OK + Chưa ghép) are visible and NEVER empty!
                if (ng > 0)
                    filter.SelectedIndex = 1; // NG
                else
                    filter.SelectedIndex = 0; // Tất cả
            }
            finally
            {
                filter.EndUpdate();
            }
        }

        private void ApplyFilter()
        {
            if (report == null)
            {
                grid.DataSource = null;
                UpdateActions();
                return;
            }

            string selected = Convert.ToString(filter.SelectedItem) ?? "";
            if (selected.StartsWith("NG"))
                grid.DataSource = report.Rows.Where(x => x.Status == "NG").ToList();
            else if (selected.StartsWith("OK"))
                grid.DataSource = report.Rows.Where(x => x.Status == "OK").ToList();
            else if (selected.StartsWith("Chưa ghép"))
                grid.DataSource = report.Rows.Where(x => x.Status == "Chưa ghép").ToList();
            else
                grid.DataSource = report.Rows.ToList();

            UpdateActions();
        }

        private List<CheckHoleReportRow> SelectedRows()
        {
            return grid.SelectedRows.Cast<DataGridViewRow>().OrderBy(x => x.Index)
                .Select(x => x.DataBoundItem as CheckHoleReportRow).Where(x => x != null).ToList();
        }

        private void UpdateActions()
        {
            var rows = SelectedRows();
            find.Enabled = !busy && rows.Count > 0;
            selectedMarkers.Enabled = !busy && rows.Any(x => x.CanMark);
            allMarkers.Enabled = !busy && report != null && report.Rows.Any(x => x.CanMark);
            export.Enabled = !busy && report != null;
            connectionGroups.Enabled = !busy && report != null && report.ConnectionCoverage != null;
            partRelations.Enabled = !busy && report != null && report.PartRelations != null;
            rerun.Enabled = !busy;
            cancel.Enabled = busy;
            filter.Enabled = !busy;
            diagnostic.Enabled = !busy;
            autoMarkers.Enabled = !busy;
            grid.Enabled = !busy;
            bool canNavigate = !busy && report != null && report.Rows.Any(x => x.CanMark);
            previousIssue.Enabled = canNavigate; nextIssue.Enabled = canNavigate;
            compactCancel.Enabled = busy; details.Enabled = !busy;
        }

        private void LocateSelected()
        {
            CheckHoleReportRow row = SelectedRows().FirstOrDefault();
            if (!busy && row != null) findAction(row);
        }

        // CG1 diagnostic-only. Display does not select Master, change any
        // production NG decision, or create model geometry.
        private void ShowConnectionGroups()
        {
            if (busy || report == null || report.ConnectionCoverage == null) return;
            CheckHoleConnectionCoverage c = report.ConnectionCoverage;
            using (var dialog = new Form())
            {
                dialog.Text = "CHECK HOLE - Nhóm liên kết (chẩn đoán)";
                dialog.Size = new Size(1150, 580);
                dialog.MinimumSize = new Size(850, 420);
                dialog.StartPosition = FormStartPosition.CenterScreen;
                dialog.Font = new Font("Segoe UI", 9F);
                var description = new Label
                {
                    Dock = DockStyle.Top,
                    Height = 72,
                    Padding = new Padding(10),
                    Text = "Nhóm lỗ đã ghép: " + c.GroupCount
                        + " | NG chưa rõ Master: " + c.NgGroupsWithoutMaster
                        + " | Lỗ chưa ghép: " + c.UnmatchedHoleCount
                        + Environment.NewLine
                        + "Hai đầu trục chỉ là ứng viên hình học, KHÔNG phải vật chủ được xác nhận."
                        + Environment.NewLine
                        + "CG1 chưa kiểm tra quan hệ đối với Part không có lỗ."
                };
                var table = new DataGridView
                {
                    Dock = DockStyle.Fill,
                    ReadOnly = true,
                    AutoGenerateColumns = true,
                    AllowUserToAddRows = false,
                    AllowUserToDeleteRows = false,
                    RowHeadersVisible = false,
                    SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                    AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells,
                    DataSource = c.Groups
                };
                dialog.Controls.Add(table);
                dialog.Controls.Add(description);
                dialog.ShowDialog();
            }
        }


        // CG2: diagnostic-only. AABB screening is NOT physical contact or
        // confirmation that a counterpart hole is required. No model edits.
        private void ShowPartRelations()
        {
            if (busy || report == null || report.PartRelations == null) return;
            CheckHoleCg2Report c = report.PartRelations;
            using (var dialog = new Form())
            {
                dialog.Text = "CHECK HOLE - Quan hệ Part / CG2 (chẩn đoán)";
                dialog.Size = new Size(1150, 630);
                dialog.MinimumSize = new Size(850, 460);
                dialog.StartPosition = FormStartPosition.CenterScreen;
                dialog.Font = new Font("Segoe UI", 9F);

                var header = new Label
                {
                    Dock = DockStyle.Top,
                    Height = 94,
                    Padding = new Padding(8),
                    Text = "Component có bounding box: " + c.ComponentsWithBounds
                        + " | Part chưa nhận diện lỗ: " + c.ComponentsWithoutDetectedHoles
                        + " | Cặp AABB gần: " + c.RecordedNearbyPairs
                        + " | Ứng viên AABB: " + c.CandidateCount
                        + " | BREP có vật liệu: " + c.BrepSolidEvidenceCount
                        + " | BREP không giao trục: " + c.BrepNoAxisHitCount
                        + " | Chưa xác minh: " + (c.BrepUnknownCount + c.BrepNotTestedCount)
                        + Environment.NewLine
                        + "Độ phủ: " + c.CoverageStatus
                        + " | Đã thử cặp Part: " + c.ComparedComponentPairs
                        + " | Đã thử trục lỗ: " + c.TestedHoleRelations
                        + " | Subassembly container: " + c.SubassemblyContainerCount
                        + " | Part chưa resolve: " + c.UnresolvedLeafOccurrenceCount
                        + Environment.NewLine
                        + "BREP_SOLID_ON_AXIS: chỉ chứng minh có solid trên trục; "
                        + "KHÔNG khẳng định cần lỗ xuyên, không xác định Master, không tạo NG."
                };
                var tabs = new TabControl { Dock = DockStyle.Fill };

                var candidatePage = new TabPage("Ứng viên cần kiểm tra");
                candidatePage.Controls.Add(new DataGridView
                {
                    Dock = DockStyle.Fill,
                    ReadOnly = true,
                    AutoGenerateColumns = true,
                    AllowUserToAddRows = false,
                    AllowUserToDeleteRows = false,
                    RowHeadersVisible = false,
                    AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                    SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                    // Prioritize physical BREP evidence; retain ALL AABB candidates.
                    // BREP_NO_AXIS_HIT comes last, not deleted (avoid false negatives).
                    DataSource = c.ExpectedHoleCandidates
                        .OrderBy(row => row.MaterialEvidence == "BREP_SOLID_ON_AXIS" ? 0
                            : row.MaterialEvidence == "BREP_NO_AXIS_HIT" ? 2 : 1)
                        .ThenBy(row => row.Id).ToList()
                });
                tabs.TabPages.Add(candidatePage);

                var relationPage = new TabPage("Cặp Component gần nhau");
                relationPage.Controls.Add(new DataGridView
                {
                    Dock = DockStyle.Fill,
                    ReadOnly = true,
                    AutoGenerateColumns = true,
                    AllowUserToAddRows = false,
                    AllowUserToDeleteRows = false,
                    RowHeadersVisible = false,
                    AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                    SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                    DataSource = c.NearbyPairs
                });
                tabs.TabPages.Add(relationPage);

                if (c.Warnings.Count > 0)
                {
                    var warningPage = new TabPage("Giới hạn dữ liệu");
                    warningPage.Controls.Add(new TextBox
                    {
                        Multiline = true,
                        ReadOnly = true,
                        Dock = DockStyle.Fill,
                        ScrollBars = ScrollBars.Vertical,
                        Text = string.Join(Environment.NewLine, c.Warnings)
                    });
                    tabs.TabPages.Add(warningPage);
                }
                dialog.Controls.Add(tabs);
                dialog.Controls.Add(header);
                dialog.ShowDialog(this);
            }
        }

        private void ExportReport()
        {
            if (busy || report == null) return;
            string title = report.AssemblyTitle ?? "Assembly";
            foreach (char invalid in Path.GetInvalidFileNameChars()) title = title.Replace(invalid, '_');
            using (var dialog = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "CHECK-HOLE-" + title + ".csv", OverwritePrompt = true })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    CheckHoleCsv.Write(dialog.FileName, report);
                    footer.Text = "Đã xuất toàn bộ " + report.Rows.Count + " dòng kết quả.";
                }
                catch (Exception ex)
                {
                    footer.Text = "Xuất CSV bị lỗi: " + ex.Message;
                    PublishNotice?.Invoke(footer.Text);
                }
            }
        }
    }
}
