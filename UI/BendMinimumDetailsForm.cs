using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using ADDIN.Commands;

namespace ADDIN.UI
{
    // Also hosted inside Check chấn. Set Bend Line keeps the same entry point.
    internal sealed class BendMinimumDetailsForm : Form
    {
        private readonly Action<VCutPropertySetupCommand.BendRow> select, inspect;
        private readonly DataGridView grid, lookup;
        private readonly TextBox details;
        private readonly BendMinimumSectionPreview preview;
        private readonly Label heading, status, modelNotice, ruleHeading;
        private readonly ModernButton locate, datum;
        private readonly ComboBox filter;
        private readonly CheckBox parameters;
        private readonly TableLayoutPanel inspector;
        private readonly Panel parameterPane;
        private readonly ToolTip tips = new ToolTip();
        private BendMinimumInspection inspection;
        private BendMinimumContext context;
        private BendMinimumPreview selectedPreview;
        private List<VCutPropertySetupCommand.BendRow> allRows = new List<VCutPropertySetupCommand.BendRow>();
        private bool ready, refreshing;
        private int previewFeature = int.MinValue, activeSide = -1;
        private string preferredSelection;
        internal string SelectedBendIdentity => Identity(Selected());
        internal void PreferSelection(string identity) { preferredSelection = identity; }

        internal BendMinimumDetailsForm(Action<VCutPropertySetupCommand.BendRow> select, Action<VCutPropertySetupCommand.BendRow> inspect)
            : this(select, inspect, false) { }
        internal BendMinimumDetailsForm(Action<VCutPropertySetupCommand.BendRow> select, Action<VCutPropertySetupCommand.BendRow> inspect, bool embedded)
        {
            this.select = select; this.inspect = inspect;
            Text = "TAI TOOL — KIỂM TRA KÍCH THƯỚC CHẤN TỐI THIỂU"; Name = "BendMinimumDetailsForm";
            Font = new Font("Segoe UI", 10F); ForeColor = TaiTheme.Text; BackColor = TaiTheme.Page;
            AutoScaleMode = AutoScaleMode.Dpi; Size = new Size(1220, 800); MinimumSize = new Size(1000, 680);
            ShowInTaskbar = false; StartPosition = FormStartPosition.CenterParent; DoubleBuffered = true;
            if (embedded) { TopLevel = false; FormBorderStyle = FormBorderStyle.None; MinimumSize = Size.Empty; Dock = DockStyle.Fill; }
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(8), Margin = Padding.Empty };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, embedded ? 0 : 54));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            heading = new Label { Dock = DockStyle.Fill, Font = new Font(Font.FontFamily, 11.5F, FontStyle.Bold), AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, ForeColor = TaiTheme.Accent };
            root.Controls.Add(heading, 0, 0);
            var split = new SplitContainer { Dock = DockStyle.Fill, BackColor = TaiTheme.Page, SplitterWidth = 8, Size = new Size(1200, 680), SplitterDistance = 370, Panel1MinSize = 330, Panel2MinSize = 480 };
            var list = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Margin = Padding.Empty };
            list.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            list.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); list.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            list.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); list.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
            var bar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
            bar.Controls.Add(new Label { Text = "DANH SÁCH NẾP CHẤN", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font(Font.FontFamily, 10.5F, FontStyle.Bold), ForeColor = TaiTheme.Accent }, 0, 0);
            filter = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 9.5F), Name = "bendFilter" };
            filter.Items.AddRange(new object[] { "Tất cả nếp", "Chỉ nếp lỗi (NG)", "Nếp Đạt (OK)", "Chưa xác minh" }); filter.SelectedIndex = 0;
            filter.SelectedIndexChanged += (s, e) => PopulateGridRows(); bar.Controls.Add(filter, 1, 0); list.Controls.Add(bar, 0, 0);
            grid = CreateGrid("gridMinimumDetails");
            AddColumn(grid, "no", "#", 40, 36, false); AddColumn(grid, "bend", "ベンド名 (Nếp chấn)", 175, 120, true);
            AddColumn(grid, "result", "Check Min", 115, 95, false);
            AddColumn(grid, "side1", "A辺 (Phía A)", 105, 80, false); AddColumn(grid, "side2", "B辺 (Phía B)", 105, 80, false);
            grid.Columns[3].Visible = grid.Columns[4].Visible = false;
            list.Controls.Add(grid, 0, 1); split.Panel1.Controls.Add(list);

            inspector = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Margin = Padding.Empty };
            inspector.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            inspector.RowStyles.Add(new RowStyle(SizeType.AutoSize)); inspector.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            inspector.RowStyles.Add(new RowStyle(SizeType.Absolute, 0)); inspector.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
            inspector.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            status = new Label { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(12, 10, 12, 10), Margin = new Padding(0, 0, 0, 6), BackColor = TaiTheme.Surface, Font = new Font("Segoe UI", 10.5F) };
            inspector.Controls.Add(status, 0, 0);
            preview = new BendMinimumSectionPreview { Margin = Padding.Empty }; preview.DatumSelected += ChooseSide; inspector.Controls.Add(preview, 0, 1);
            parameters = new CheckBox { Text = "Xem bảng tra dao cối (Excel)", AutoSize = true, Dock = DockStyle.Left, Font = new Font(Font.FontFamily, 10F, FontStyle.Bold), ForeColor = TaiTheme.Accent, Name = "toggleBendParameters", Cursor = Cursors.Hand };
            tips.SetToolTip(parameters, "Mở bảng thông số kích thước tối thiểu tra từ file Excel tiêu chuẩn dao cối và ghi chú kỹ thuật.");
            Action sizeParameters = () => list.RowStyles[3].Height = parameters.Checked ? Math.Min(260, Math.Max(200, list.Height - 170)) : 0;
            parameters.CheckedChanged += (s, e) => { parameterPane.Visible = parameters.Checked; sizeParameters(); };
            list.Resize += (s, e) => sizeParameters();
            list.Controls.Add(parameters, 0, 2);
            parameterPane = new Panel { Dock = DockStyle.Fill, Visible = false, Margin = Padding.Empty, BackColor = TaiTheme.Surface };
            var information = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Margin = Padding.Empty };
            information.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            information.RowStyles.Add(new RowStyle(SizeType.Absolute, 145)); information.RowStyles.Add(new RowStyle(SizeType.Absolute, 50)); information.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            lookup = CreateGrid("minimumLookup"); AddColumn(lookup, "parameter", "外寸 mm (Phủ bì)", 140, 110, true);
            AddColumn(lookup, "a", "L / A辺", 75, 55, true); AddColumn(lookup, "b", "Z / B辺", 75, 55, true); AddColumn(lookup, "c", "C(U) / C辺", 85, 65, true);
            lookup.DefaultCellStyle.Padding = new Padding(4, 4, 4, 4);
            information.Controls.Add(lookup, 0, 0);
            ruleHeading = new Label { Dock = DockStyle.Fill, AutoSize = true, ForeColor = TaiTheme.Accent, Font = new Font("Segoe UI", 9.5F, FontStyle.Bold), Padding = new Padding(4) }; information.Controls.Add(ruleHeading, 0, 1);
            details = new TextBox { Name = "txtMinimumExplanation", Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, ScrollBars = ScrollBars.Both, WordWrap = false, BackColor = TaiTheme.Surface, Font = new Font("Segoe UI", 9.5F) };
            information.Controls.Add(details, 0, 2); parameterPane.Controls.Add(information); list.Controls.Add(parameterPane, 0, 3);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = Padding.Empty };
            datum = new ModernButton { Text = "Xem vị trí lỗi (NG)", Icon = TaiIcon.Measure, Width = 160, Height = 34, Font = new Font("Segoe UI", 10F, FontStyle.Bold), Kind = ModernButtonKind.Standard, Tone = ModernButtonTone.Danger, Cursor = Cursors.Hand };
            locate = new ModernButton { Text = "Xem trên 3D", Icon = TaiIcon.BendFlat, Width = 135, Height = 34, Font = new Font("Segoe UI", 10F, FontStyle.Bold), Kind = ModernButtonKind.Standard, Tone = ModernButtonTone.Accent, Cursor = Cursors.Hand };
            tips.SetToolTip(datum, "Làm sáng (highlight) vị trí mốc đo bị vi phạm kích thước tối thiểu trên mô hình 3D");
            tips.SetToolTip(locate, "Làm sáng (highlight) toàn bộ nếp chấn này trên mô hình 3D SolidWorks");
            datum.Click += (s, e) => ChooseSide(-1); locate.Click += (s, e) => Locate(-2);
            if (!embedded) { var close = new ModernButton { Text = "Đóng", Icon = TaiIcon.Close, Width = 90, Height = 34, Font = new Font("Segoe UI", 10F), Kind = ModernButtonKind.Standard, DialogResult = DialogResult.Cancel, Cursor = Cursors.Hand }; close.Click += (s, e) => Close(); CancelButton = close; actions.Controls.Add(close); }
            actions.Controls.Add(datum); actions.Controls.Add(locate); inspector.Controls.Add(actions, 0, 4);
            split.Panel2.Controls.Add(inspector); root.Controls.Add(split, 0, 1);
            modelNotice = new Label { Dock = DockStyle.Fill, ForeColor = TaiTheme.TextMuted, Font = new Font("Segoe UI", 10F), TextAlign = ContentAlignment.MiddleLeft }; root.Controls.Add(modelNotice, 0, 2); Controls.Add(root);
            // SelectionChanged may run while CurrentRow still points at the old
            // cell. Use the event after CurrentCell has actually changed.
            grid.CurrentCellChanged += (s, e) => { if (!refreshing) { activeSide = -1; ShowSelected(); Locate(-2); } };
            grid.CellClick += (s, e) => { if (!refreshing && e.RowIndex >= 0) { if (e.ColumnIndex >= 3) ChooseSide(e.ColumnIndex - 3); else Locate(-2); } };
            lookup.CellClick += (s, e) => { if (e.RowIndex > 0) ChooseSide(e.RowIndex - 1); };
            Shown += (s, e) => ShowSelected();
        }
        internal static DataGridView CreateGrid(string name)
        {
            var result = new DataGridView { Name = name, Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
                AllowUserToDeleteRows = false, AllowUserToResizeRows = false, RowHeadersVisible = false, MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells, BackgroundColor = TaiTheme.Surface, BorderStyle = BorderStyle.None,
                EnableHeadersVisualStyles = false, GridColor = TaiTheme.Divider, ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.EnableResizing,
                ColumnHeadersHeight = 36 };
            result.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(231, 238, 248), ForeColor = TaiTheme.Text, Font = new Font("Segoe UI", 10F, FontStyle.Bold), Padding = new Padding(6), WrapMode = DataGridViewTriState.True };
            result.DefaultCellStyle = new DataGridViewCellStyle { ForeColor = TaiTheme.Text, BackColor = TaiTheme.Surface, SelectionBackColor = TaiTheme.AccentSoft, SelectionForeColor = TaiTheme.Text,
                Padding = new Padding(6, 6, 6, 6), WrapMode = DataGridViewTriState.True, Font = new Font("Segoe UI", 10F) };
            var wrapped = new Dictionary<string, string>();
            result.CellFormatting += (s, e) =>
            {
                var value = e.Value as string;
                if (value == null || e.ColumnIndex < 0 || e.RowIndex < 0) return;
                int width = Math.Max(16, result.Columns[e.ColumnIndex].Width - e.CellStyle.Padding.Horizontal - 8);
                var font = e.CellStyle.Font ?? result.Font;
                string key = width + ":" + font.GetHashCode() + ":" + value;
                string text;
                if (!wrapped.TryGetValue(key, out text))
                {
                    text = WrapCellText(value, font, width);
                    if (wrapped.Count > 2048) wrapped.Clear(); wrapped[key] = text;
                }
                e.Value = text; e.FormattingApplied = true;
                result.Rows[e.RowIndex].Cells[e.ColumnIndex].ToolTipText = value;
            };
            // Fill columns change width after a DPI/parent resize. WinForms can
            // retain the old row height, including non-displayed Assembly rows.
            bool queued = false;
            Action fitRows = () =>
            {
                if (queued || !result.IsHandleCreated || result.IsDisposed) return;
                queued = true; result.BeginInvoke(new Action(() => { queued = false;
                    if (!result.IsDisposed) result.AutoResizeRows(DataGridViewAutoSizeRowsMode.AllCells); }));
            };
            result.ColumnWidthChanged += (s, e) => fitRows(); result.SizeChanged += (s, e) => fitRows();
            UIHelper.EnableDoubleBuffer(result); return result;
        }
        // GDI word-wrap can clip an unbroken Part/configuration code. Insert
        // display-only line breaks; retain the complete original value and tag.
        private static string WrapCellText(string value, Font font, int width)
        {
            var flags = TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
            var lines = new List<string>();
            foreach (string line in value.Replace("\r\n", "\n").Split('\n'))
            {
                string remaining = line;
                while (remaining.Length > 0 && TextRenderer.MeasureText(remaining, font, Size.Empty, flags).Width > width)
                {
                    int[] positions = StringInfo.ParseCombiningCharacters(remaining);
                    int lo = 1, hi = positions.Length, count = 1;
                    while (lo <= hi)
                    {
                        int mid = (lo + hi) / 2, end = mid == positions.Length ? remaining.Length : positions[mid];
                        if (TextRenderer.MeasureText(remaining.Substring(0, end), font, Size.Empty, flags).Width <= width)
                        { count = mid; lo = mid + 1; } else hi = mid - 1;
                    }
                    int split = count == positions.Length ? remaining.Length : positions[count];
                    lines.Add(remaining.Substring(0, split)); remaining = remaining.Substring(split);
                }
                lines.Add(remaining);
            }
            return string.Join("\n", lines);
        }
        internal static void AddColumn(DataGridView grid, string name, string title, int width, int min, bool fill)
        { grid.Columns.Add(new DataGridViewTextBoxColumn { Name = name, HeaderText = title, Width = width, MinimumWidth = min, FillWeight = width,
            AutoSizeMode = fill ? DataGridViewAutoSizeColumnMode.Fill : DataGridViewAutoSizeColumnMode.None, SortMode = DataGridViewColumnSortMode.NotSortable }); }
        private VCutPropertySetupCommand.BendRow Selected() => grid.CurrentRow?.Tag as VCutPropertySetupCommand.BendRow;
        private static string Identity(VCutPropertySetupCommand.BendRow row) => row == null ? "" : string.IsNullOrEmpty(row.PersistentId) ? "id:" + row.FeatureId : row.PersistentId;
        private int Number(VCutPropertySetupCommand.BendRow row) => allRows.FindIndex(r => Identity(r) == Identity(row)) + 1;
        private static string Mm(double? value) => value.HasValue ? value.Value.ToString("0.###", CultureInfo.InvariantCulture) : "—";
        private static bool Pass(BendMinimumRowResult check) => check != null && (check.Status == BendMinimumStatus.MeetsMinimum || check.Status == BendMinimumStatus.GroovedAccepted);
        private void PopulateGridRows()
        {
            if (grid == null || refreshing) return; string selected = preferredSelection ?? Identity(Selected()); preferredSelection = null; refreshing = true;
            try
            {
                var rows = allRows.Where(r => { var c = r.MinimumContext?.Evaluate(r); return filter.SelectedIndex == 0 ||
                    filter.SelectedIndex == 1 && c?.Status == BendMinimumStatus.BelowMinimum || filter.SelectedIndex == 2 && Pass(c) ||
                    filter.SelectedIndex == 3 && !Pass(c) && c?.Status != BendMinimumStatus.BelowMinimum; }).ToList();
                // Reuse rows while polling so the current selection and scroll do not jump.
                while (grid.Rows.Count > rows.Count) grid.Rows.RemoveAt(grid.Rows.Count - 1);
                while (grid.Rows.Count < rows.Count) grid.Rows.Add();
                foreach (var pair in rows.Select((r, i) => new { r, i }))
                {
                    var c = pair.r.MinimumContext?.Evaluate(pair.r); var view = grid.Rows[pair.i]; view.Tag = pair.r;
                    view.SetValues(Number(pair.r), pair.r.FeatureName, !ready ? "Chờ cập nhật" : Pass(c) ? c.Status == BendMinimumStatus.GroovedAccepted ? "Cạnh bào" : "Đạt" : c?.Status == BendMinimumStatus.BelowMinimum ? "NG" : "Chưa xác minh", Side(c, 0), Side(c, 1));
                    view.Cells[2].Style.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
                    view.Cells[2].Style.ForeColor = !ready ? TaiTheme.Warning : c?.Status == BendMinimumStatus.BelowMinimum ? TaiTheme.Danger : Pass(c) ? TaiTheme.Success : TaiTheme.Warning;
                    view.Cells[2].ToolTipText = c?.Summary;
                }
                var next = grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => Identity(r.Tag as VCutPropertySetupCommand.BendRow) == selected) ?? grid.Rows.Cast<DataGridViewRow>().FirstOrDefault();
                if (next != null && grid.CurrentRow != next) grid.CurrentCell = next.Cells[1];
            }
            finally { refreshing = false; }
            ShowSelected();
        }
        private static string Side(BendMinimumRowResult c, int index)
        { if (c == null || index >= c.Sides.Count) return "—"; var s = c.Sides[index]; return s.Measure.Shape + "/" + s.Measure.Dimension + " " + Mm(s.Measure.ValueMm) + " / " + Mm(s.LimitMm); }
        private void Locate(int side)
        {
            var row = Selected(); if (!ready || row?.MinimumContext == null || !row.MinimumContext.CheckCurrent()) return;
            if (inspection != null) { inspection.Select(row, side); inspection.Update(allRows, row.FeatureId); }
            else if (side == -2) select?.Invoke(row); else inspect?.Invoke(row);
        }
        private void ChooseSide(int side)
        {
            var c = Selected()?.MinimumContext?.Evaluate(Selected()); activeSide = side >= 0 ? side : c == null ? -1 : c.Sides.FindIndex(s => s.Measure == c.Datum);
            ShowSelected(); Locate(activeSide);
        }
        private void ShowSelected()
        {
            var row = Selected();
            if (ready && row?.MinimumContext != null && !row.MinimumContext.CheckCurrent()) { ready = false; inspection?.Dispose(); inspection = null; previewFeature = int.MinValue; }
            var check = row?.MinimumContext?.Evaluate(row); var rule = check?.Sides.Select(s => s.Check.Rule).FirstOrDefault(r => r != null);
            if (row == null)
            {
                status.Text = "Chọn nếp chấn trong danh sách bên trái để xem chi tiết.";
                status.ForeColor = TaiTheme.TextMuted;
            }
            else
            {
                string statusDesc;
                if (!ready)
                {
                    statusDesc = "⏳ Đang tính toán kiểm tra kích thước tối thiểu...";
                    status.ForeColor = TaiTheme.Warning;
                }
                else if (check == null)
                {
                    statusDesc = "❓ Chưa có dữ liệu hình học của nếp chấn này";
                    status.ForeColor = TaiTheme.Warning;
                }
                else if (check.Status == BendMinimumStatus.GroovedAccepted)
                {
                    statusDesc = "✔ ĐẠT (OK): Cạnh đã gán bào rãnh V (cho phép chấn kích thước nhỏ)";
                    status.ForeColor = TaiTheme.Success;
                }
                else if (check.Status == BendMinimumStatus.MeetsMinimum)
                {
                    statusDesc = "✔ ĐẠT (OK): Kích thước phủ bì thỏa mãn tiêu chuẩn dao cối";
                    status.ForeColor = TaiTheme.Success;
                }
                else if (check.Status == BendMinimumStatus.BelowMinimum)
                {
                    var worstSide = check.Sides.FirstOrDefault(s => s.Check.Status == BendMinimumStatus.BelowMinimum);
                    string sideName = worstSide != null && worstSide.Measure != null
                        ? (worstSide.Measure.Side == "Phía A" ? "Phía A (A辺)" : "Phía B (B辺)")
                        : "Kích thước";
                    string measuredVal = worstSide != null && worstSide.Measure != null ? Mm(worstSide.Measure.ValueMm) : "?";
                    string limitVal = worstSide != null && worstSide.LimitMm.HasValue ? Mm(worstSide.LimitMm.Value) : "?";
                    statusDesc = string.Format("✖ LỖI (NG): {0} đo được {1} mm < Min {2} mm (Dưới kích thước tối thiểu)",
                        sideName, measuredVal, limitVal);
                    status.ForeColor = TaiTheme.Danger;
                }
                else
                {
                    statusDesc = "❓ " + (check.Summary ?? "Chưa xác minh");
                    status.ForeColor = TaiTheme.Warning;
                }
                status.Text = string.Format("#{0} · {1}\n{2}", Number(row), row.FeatureName, statusDesc);
            }
            string text = check?.Details ?? "";
            if (rule != null) text += "\r\n" + rule.SourceSheet + " · dòng " + rule.SourceRow + "\r\n" + rule.Notes;
            if (details.Text != text) details.Text = text;
            ruleHeading.Text = rule == null ? "Chưa tìm thấy tiêu chuẩn dao cối phù hợp trong bảng Excel"
                : string.Format("Vật liệu: {0} · 板厚 t = {1} mm · Rãnh cối V = {2} · Chiều rộng cối = {3} mm · Bán kính chấn ir = {4} mm",
                    rule.MaterialGroup, Mm(rule.ThicknessMm), Mm(rule.VOpeningMm), Mm(rule.DieOutsideWidthMm), Mm(rule.InsideRadiusMm));
            tips.SetToolTip(ruleHeading, text);
            while (lookup.Rows.Count < 3) lookup.Rows.Add();
            lookup.Rows[0].SetValues("Tối thiểu (Min)", Mm(rule?.MinimumA), Mm(rule?.MinimumB), Mm(rule?.MinimumC));
            lookup.Rows[0].DefaultCellStyle.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
            for (int i = 0; i < 2; i++)
            {
                var side = check != null && i < check.Sides.Count ? check.Sides[i] : null; var m = side?.Measure;
                lookup.Rows[i + 1].SetValues(i == 0 ? "A辺 実測 (Phía A)" : "B辺 実測 (Phía B)", m?.Dimension == "A" ? Mm(m.ValueMm) : "—", m?.Dimension == "B" ? Mm(m.ValueMm) : "—", m?.Dimension == "C" ? Mm(m.ValueMm) : "—");
                lookup.Rows[i + 1].DefaultCellStyle.ForeColor = side?.Check.Status == BendMinimumStatus.BelowMinimum ? TaiTheme.Danger : TaiTheme.Text;
                lookup.Rows[i + 1].DefaultCellStyle.Font = side?.Check.Status == BendMinimumStatus.BelowMinimum ? new Font("Segoe UI", 9.5F, FontStyle.Bold) : new Font("Segoe UI", 9.5F);
            }
            lookup.ClearSelection(); locate.Enabled = datum.Enabled = ready && row != null && (inspection == null || inspection.CanLocate);
            if (row != null && previewFeature != row.FeatureId) { selectedPreview = ready ? inspection?.Preview(row) : null; previewFeature = row.FeatureId; }
            if (!ready) selectedPreview = new BendMinimumPreview { Error = "Chờ model sau rebuild / quét lại." };
            preview.SetSection(selectedPreview, check, Number(row), activeSide);
        }
        internal void UpdateResults(IList<VCutPropertySetupCommand.BendRow> rows, string part, string result, string counts, string notice, bool ready)
        {
            this.ready = ready && rows.Count > 0 && rows.All(r => r.MinimumContext != null && r.MinimumContext.Current);
            heading.Text = part + "\n" + result + " · " + counts;
            var next = rows.Select(r => r.MinimumContext).FirstOrDefault(c => c != null);
            if (next != context || !this.ready) { inspection?.Dispose(); inspection = null; context = next; selectedPreview = null; previewFeature = int.MinValue; }
            if (this.ready && inspection == null) inspection = context?.InspectionFactory?.Invoke();
            allRows = rows.ToList(); PopulateGridRows(); inspection?.Update(allRows, Selected()?.FeatureId ?? 0);
            modelNotice.Text = inspection?.MarkerError != null ? "Nhãn 3D chưa khả dụng: " + inspection.MarkerError : "Đơn vị: mm (phủ bì) · Click vào nếp chấn hoặc mốc đo để làm sáng vị trí trên 3D";
            tips.SetToolTip(modelNotice, inspection?.MarkerError ?? notice);
        }
        protected override void Dispose(bool disposing)
        { if (disposing) { inspection?.Dispose(); inspection = null; tips.Dispose(); } base.Dispose(disposing); }
    }
}
