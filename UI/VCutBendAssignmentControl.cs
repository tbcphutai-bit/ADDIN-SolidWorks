using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using ADDIN.Commands;

namespace ADDIN.UI
{
    public partial class VCutBendAssignmentControl : UserControl
    {
        private const string Unassigned = "未設定";
        private IDictionary<string, string> properties;
        private bool loadingRows, applyingFilters, saveAllowed, savedSinceRefresh, nativeCommand, nativeReleased;
        internal List<VCutPropertySetupCommand.BendRow> Rows { get; private set; }
        internal IDictionary<string, string> Properties { get { return properties; } }
        internal event EventHandler DraftChanged, SaveRequested, RefreshRequested, CloseRequested;
        internal event Action<VCutPropertySetupCommand.BendRow> BendSelected;
        private bool HasUnsavedChanges { get { return Rows != null && Rows.Any(row => row.Kind != row.OriginalKind); } }
        private bool HasBlockingIssues { get { return Rows != null && Rows.Any(row => VCutPropertySetupCommand.GetDraftIssue(row, Rows) != VCutBendIssue.None); } }
        internal string CurrentFeatureName
        {
            get { return gridBends.CurrentRow == null ? "" : Convert.ToString(gridBends.CurrentRow.Cells[colFeature.Index].Value) ?? ""; }
        }

        public VCutBendAssignmentControl()
        {
            InitializeComponent();
            EnableDoubleBuffer(gridBends);
            ApplyUnifiedGridStyle();
            gridBends.CellPainting += GridBends_CellPainting;
            gridBends.CurrentCellDirtyStateChanged += (sender, args) => {
                if (gridBends.IsCurrentCellDirty) gridBends.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            gridBends.CellValueChanged += (sender, args) => {
                if (loadingRows || args.RowIndex < 0 || args.ColumnIndex != colKind.Index) return;
                // Commit the entire draft before cross-row validation.
                foreach (DataGridViewRow viewRow in gridBends.Rows)
                {
                    var row = viewRow.Tag as VCutPropertySetupCommand.BendRow;
                    if (row != null) row.Kind = KindForChoice(Convert.ToString(viewRow.Cells[colKind.Index].Value));
                }
                saveAllowed = true;
                RevalidateDraft();
                DraftChanged?.Invoke(this, EventArgs.Empty);
            };
            gridBends.CellEndEdit += (sender, args) => {
                if (args.RowIndex >= 0 && args.ColumnIndex == colKind.Index) ConfigureAssignmentCell(gridBends.Rows[args.RowIndex]);
            };
            gridBends.SelectionChanged += (sender, args) => {
                if (loadingRows || applyingFilters) return;
                ShowSelectedDetail();
                HighlightCurrentRow();
            };
            gridBends.CellClick += (sender, args) => {
                if (args.RowIndex < 0) return;
                if (args.ColumnIndex == colFeature.Index) HighlightCurrentRow();
                if (args.ColumnIndex != colKind.Index || gridBends.Rows[args.RowIndex].Cells[colKind.Index].ReadOnly || !IsHandleCreated) return;
                int rowIndex = args.RowIndex;
                BeginInvoke((Action)(() => {
                    if (nativeReleased || IsDisposed || Disposing || gridBends.CurrentCell == null ||
                        gridBends.CurrentCell.RowIndex != rowIndex || gridBends.CurrentCell.ColumnIndex != colKind.Index) return;
                    gridBends.BeginEdit(true);
                    var editor = gridBends.EditingControl as ComboBox;
                    if (editor != null) editor.DroppedDown = true;
                }));
            };
            gridBends.DataError += (sender, args) => { args.ThrowException = false; };
            txtSearch.TextChanged += (sender, args) => { if (!loadingRows) ApplyFilters(); };
            chkIssuesOnly.CheckedChanged += (sender, args) => { if (!loadingRows) ApplyFilters(); };
            btnSave.Click += (sender, args) => {
                CommitEditor();
                if (!btnSave.Enabled) return;
                SaveRequested?.Invoke(this, EventArgs.Empty);
            };
            btnRefresh.Click += (sender, args) => {
                if (!ConfirmDiscardChanges("Refresh")) return;
                saveAllowed = false;
                UpdateSaveState();
                RefreshRequested?.Invoke(this, EventArgs.Empty);
            };
            btnClose.Click += (sender, args) => {
                if (ConfirmDiscardChanges("đóng bảng")) CloseRequested?.Invoke(this, EventArgs.Empty);
            };
            gridBends.SizeChanged += (sender, args) => UpdateResponsiveColumns();
            toolTip.SetToolTip(txtSearch, "Find bend, mark or bend table / 曲げ・加工印・テーブルを検索");
            toolTip.SetToolTip(btnRefresh, "Read external feature/table changes / 外部の曲げ・テーブル変更を再読込");
            toolTip.SetToolTip(btnClose, "Close; unsaved changes require confirmation / 未保存の変更は確認して閉じる");
            UpdateSaveState();
        }

        private void CommitEditor()
        {
            if (gridBends.IsCurrentCellDirty) gridBends.CommitEdit(DataGridViewDataErrorContexts.Commit);
            gridBends.EndEdit();
        }
        internal bool ConfirmDiscardChanges(string action)
        {
            CommitEditor();
            return !HasUnsavedChanges || VCutNoticeDialog.Confirm(this, "Thông tin bào chưa được lưu",
                "Bạn đã sửa Mark nhưng chưa lưu.\r\n\r\nNếu " + action + ", các thay đổi chưa lưu sẽ bị bỏ.\r\nBạn có muốn tiếp tục không?") == DialogResult.Yes;
        }
        private void UpdateSaveState()
        {
            btnSave.Visible = true;
            btnSave.Enabled = properties != null && saveAllowed && !HasBlockingIssues && (!savedSinceRefresh || HasUnsavedChanges);
            btnSave.NormalColor = btnSave.Enabled ? Color.FromArgb(0, 101, 202) : Color.FromArgb(229, 235, 243);
            btnSave.HoverColor = btnSave.Enabled ? Color.FromArgb(0, 87, 177) : btnSave.NormalColor;
            btnSave.PressColor = btnSave.Enabled ? Color.FromArgb(0, 70, 148) : btnSave.NormalColor;
            btnSave.ForeColor = btnSave.Enabled ? Color.White : Color.FromArgb(90, 105, 125);
            btnSave.SubtitleColor = btnSave.ForeColor;
            toolTip.SetToolTip(btnSave, HasBlockingIssues
                ? "Resolve NG rows before saving / NG行を修正して保存"
                : savedSinceRefresh && !HasUnsavedChanges ? "Saved / 保存済み"
                : !saveAllowed ? "Refresh external bend data first / 更新で曲げを再読込"
                : "Write marks and replace V溝1 / V溝2 / C溝 properties / 加工印とプロパティを保存");
            btnSave.Invalidate();
        }
        internal void ConfigureForNativeCommand()
        {
            nativeCommand = true;
            // Native host captures the draft and saves only after HWND teardown.
            // The three footer buttons remain available in both hosts.
            btnRefresh.Visible = btnSave.Visible = btnClose.Visible = true;
            UpdateSaveState();
        }
        internal void ReleaseNativeCommand()
        {
            nativeReleased = true;
            loadingRows = true;
            Rows = null;
            properties = null;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct NativeWindowPosition
        {
            internal IntPtr Window, InsertAfter;
            internal int X, Y, Width, Height;
            internal uint Flags;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct NativeClientRect { internal int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")]
        private static extern IntPtr GetParent(IntPtr window);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetClientRect(IntPtr window, out NativeClientRect rect);
        protected override void WndProc(ref Message message)
        {
            if (nativeCommand && message.Msg == 0x0046 && message.LParam != IntPtr.Zero)
            {
                var position = Marshal.PtrToStructure<NativeWindowPosition>(message.LParam);
                var parent = GetParent(message.HWnd);
                int left = (position.Flags & 2) == 0 ? position.X : Left;
                NativeClientRect rect;
                if ((position.Flags & 1) == 0 && parent != IntPtr.Zero &&
                    GetClientRect(parent, out rect) && rect.Right > rect.Left && left >= 0)
                {
                    // Size only the embedded table, without COM calls in WndProc.
                    position.Width = Math.Max(1, rect.Right - rect.Left - 2 * left);
                    Marshal.StructureToPtr(position, message.LParam, false);
                }
            }
            base.WndProc(ref message);
        }

        internal void LoadRows(List<VCutPropertySetupCommand.BendRow> rows,
            IDictionary<string, string> propertyValues, string partTitle,
            string configuration, string preferredFeatureName = null, bool refreshed = false)
        {
            loadingRows = true;
            try
            {
                saveAllowed = refreshed;
                savedSinceRefresh = false;
                Rows = rows ?? new List<VCutPropertySetupCommand.BendRow>();
                properties = propertyValues ?? new Dictionary<string, string>();
                lblDocument.Text = "Part / 部品: " + Clean(partTitle) + "\r\nConfig / 設定: " + Clean(configuration);
                toolTip.SetToolTip(lblDocument, lblDocument.Text);
                string material = string.Join(", ", Rows.Select(r => r.Material).Where(m => !string.IsNullOrWhiteSpace(m)).Distinct());
                var thicknesses = Rows.Where(r => r.ModelThicknessMm.HasValue).Select(r => r.ModelThicknessMm.Value).Distinct().ToList();
                string stock = thicknesses.Count == 1 ? Format(thicknesses[0]) + " mm" : thicknesses.Count > 1 ? "Mixed / 複数" : "?";
                lblMaterial.Text = "Material / 材質: " + (material.Length == 0 ? "?" : material) + "   Stock / 板厚: " + stock;
                toolTip.SetToolTip(lblMaterial, lblMaterial.Text);
                gridBends.Rows.Clear();
                foreach (var row in Rows)
                {
                    string value = row.ValueMm.HasValue ? Format(row.ValueMm) : row.IsGrooveTable || !string.IsNullOrWhiteSpace(row.TableError) ? "?" : "—";
                    int index = gridBends.Rows.Add(gridBends.Rows.Count + 1, FeatureName(row), "", Clean(row.FeatureType), value, ChoiceForKind(row.Kind));
                    var viewRow = gridBends.Rows[index];
                    viewRow.Tag = row;
                    viewRow.Cells[colFeature.Index].ToolTipText = FeatureName(row) + " [" + row.FeatureType + "]";
                    viewRow.Cells[colType.Index].ToolTipText = row.FeatureType;
                    viewRow.Cells[colValue.Index].ToolTipText = "Remaining thickness / 残厚: " + value + " mm\r\n" + row.TableSource + ": " + row.Table;
                    ConfigureAssignmentCell(viewRow);
                    UpdateStatus(viewRow);
                }
                DataGridViewRow preferred = gridBends.Rows.Cast<DataGridViewRow>().FirstOrDefault(v =>
                    string.Equals(Convert.ToString(v.Cells[colFeature.Index].Value), Clean(preferredFeatureName), StringComparison.OrdinalIgnoreCase));
                if (gridBends.Rows.Count > 0) gridBends.CurrentCell = (preferred ?? gridBends.Rows[0]).Cells[colFeature.Index];
            }
            finally { loadingRows = false; }
            RevalidateDraft();
        }
        internal void HighlightCurrentRow()
        {
            if (nativeReleased || loadingRows || applyingFilters) return;
            var row = gridBends.CurrentRow == null ? null : gridBends.CurrentRow.Tag as VCutPropertySetupCommand.BendRow;
            if (row != null) BendSelected?.Invoke(row);
        }
        internal void RefreshAfterSave()
        {
            savedSinceRefresh = true;
            RevalidateDraft();
        }
        private void RevalidateDraft()
        {
            if (nativeReleased) return;
            foreach (DataGridViewRow viewRow in gridBends.Rows) UpdateStatus(viewRow);
            UpdatePropertyPreview();
            UpdateSaveState();
            ApplyFilters();
        }
        private void ApplyFilters()
        {
            if (nativeReleased || applyingFilters) return;
            applyingFilters = true;
            var previous = gridBends.CurrentRow;
            try
            {
                gridBends.CurrentCell = null;
                string query = Clean(txtSearch.Text).Trim();
                foreach (DataGridViewRow viewRow in gridBends.Rows)
                {
                    var row = viewRow.Tag as VCutPropertySetupCommand.BendRow;
                    string text = row == null ? "" : FeatureName(row) + " " + row.FeatureType + " " + row.Kind + " " + row.Table + " " + viewRow.ErrorText;
                    viewRow.Visible = (!chkIssuesOnly.Checked || viewRow.ErrorText.StartsWith("NG", StringComparison.Ordinal)) &&
                        (query.Length == 0 || Clean(text).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
                }
                var selected = previous != null && previous.Visible ? previous : gridBends.Rows.Cast<DataGridViewRow>().FirstOrDefault(v => v.Visible);
                if (selected != null) gridBends.CurrentCell = selected.Cells[colFeature.Index];
                lblEmpty.Visible = selected == null;
                lblEmpty.Text = Rows == null || Rows.Count == 0 ? "No bend features / 曲げなし" : "No matching bends / 該当する曲げなし";
                if (lblEmpty.Visible) lblEmpty.BringToFront();
            }
            finally { applyingFilters = false; }
            UpdateSummary();
            ShowSelectedDetail();
        }
        private void UpdateSummary()
        {
            var viewRows = gridBends.Rows.Cast<DataGridViewRow>().ToList();
            int ok = viewRows.Count(r => Convert.ToString(r.Cells[colStatus.Index].Value) == "✓");
            int ng = viewRows.Count(r => r.ErrorText.StartsWith("NG", StringComparison.Ordinal));
            int pending = viewRows.Count(r => Convert.ToString(r.Cells[colStatus.Index].Value) == "…");
            int normal = viewRows.Count - ok - ng - pending;
            lblSummary.Text = "✓ " + ok + "   … " + pending + "   × " + ng + "   — " + normal + "    Shown / 表示 " + viewRows.Count(r => r.Visible) + "/" + viewRows.Count;
            lblSummary.ForeColor = ng > 0 ? Color.FromArgb(185, 28, 28) : pending > 0 ? Color.FromArgb(160, 90, 10) : Color.FromArgb(16, 120, 65);
            toolTip.SetToolTip(lblSummary, "✓ Saved / 保存済み   … Pending / 未保存   × NG / 要確認   — Normal bend / 通常曲げ");
        }
        private void UpdatePropertyPreview()
        {
            if (properties == null || Rows == null) return;
            string[] names = { "V溝1", "V溝2", "C溝" };
            Label[] cards = { lblPropertyV1, lblPropertyV2, lblPropertyC };
            string[] symbols = { "○", "◎", "●" };
            for (int i = 0; i < names.Length; i++)
            {
                string raw; properties.TryGetValue(names[i], out raw);
                string saved = string.IsNullOrWhiteSpace(raw) ? "—" : raw;
                var assigned = Rows.Where(r => r.Kind == names[i]).ToList();
                bool invalid = assigned.Any(r => VCutPropertySetupCommand.GetDraftIssue(r, Rows) != VCutBendIssue.None);
                string next = invalid ? "NG" : assigned.Count == 0 ? "—" : Format(assigned[0].ValueMm);
                double stored;
                bool matched = assigned.Count == 0 ? string.IsNullOrWhiteSpace(raw) : !invalid &&
                    TryPropertyValue(raw, out stored) && Math.Abs(stored - assigned[0].ValueMm.Value) <= VCutPropertySetupCommand.ToleranceMm;
                cards[i].Text = symbols[i] + " " + names[i] + "\r\n" + (matched ? saved + " mm" : saved + " → " + next + (invalid ? "" : " mm"));
                cards[i].ForeColor = invalid ? Color.FromArgb(185, 28, 28) : matched ? Color.FromArgb(30, 50, 85) : Color.FromArgb(150, 85, 5);
                cards[i].BackColor = invalid ? Color.FromArgb(255, 240, 240) : matched ? Color.FromArgb(246, 249, 253) : Color.FromArgb(255, 248, 230);
                toolTip.SetToolTip(cards[i], names[i] + "\r\nSaved / 保存値: " + saved +
                    "\r\nNext / 予定値: " + (assigned.Count == 0 ? "Clear / 解除" : next + " mm") +
                    "\r\nSave writes only after all rows pass / 全行の確認後に保存");
            }
        }
        private void ShowSelectedDetail()
        {
            if (nativeReleased || IsDisposed || Disposing) return;
            var selected = gridBends.CurrentRow;
            var row = selected == null ? null : selected.Tag as VCutPropertySetupCommand.BendRow;
            if (row == null)
            {
                lblDetail.Text = "Select a bend to inspect its table and values.\r\n曲げを選択してテーブルと値を確認。";
                lblStatus.Text = "Select a bend / 曲げを選択";
                lblStatus.ForeColor = Color.FromArgb(90, 105, 125);
                return;
            }
            string raw = "";
            if (properties != null && !string.IsNullOrEmpty(row.Kind)) properties.TryGetValue(row.Kind, out raw);
            string detail = FeatureName(row) + "  [" + Clean(row.FeatureType) + "]\r\n" +
                "Angle / 角度: " + Format(row.BendAngleDeg) + "°   R: " + Format(row.BendRadiusMm) + " mm\r\n" +
                "Stock / 板厚: " + Format(row.ModelThicknessMm) + " mm   Table / 表: " + Format(row.TableThicknessMm) + " mm\r\n" +
                "Bend table / 曲げ表: " + Clean(Path.GetFileName(row.Table ?? "")) + "\r\n" +
                "Source / 参照元: " + Clean(row.TableSource) + "\r\n" +
                (row.IsGrooveTable ? "Remaining / 残厚: " + Format(row.ValueMm) + " mm   " : "Normal bend / 通常曲げ   ") +
                "Mark / 加工印: " + ChoiceForKind(row.Kind) + "\r\n" +
                "Property / 保存値: " + (string.IsNullOrWhiteSpace(raw) ? "—" : raw) + " mm";
            if (!string.IsNullOrWhiteSpace(row.TableError)) detail += "\r\n" + row.TableError;
            lblDetail.Text = detail;
            toolTip.SetToolTip(lblDetail, detail + "\r\n" + row.Table);
            bool ng = selected.ErrorText.StartsWith("NG", StringComparison.Ordinal);
            lblStatus.Text = ng ? selected.ErrorText : HasUnsavedChanges ? "Unsaved changes — Save to apply / 未保存・保存で適用" :
                savedSinceRefresh ? "Saved in model — save the Part file / モデルに保存済み・部品ファイルも保存" : selected.ErrorText;
            lblStatus.ForeColor = ng ? Color.FromArgb(185, 28, 28) : HasUnsavedChanges ? Color.FromArgb(160, 90, 10) :
                selected.ErrorText.StartsWith("OK", StringComparison.Ordinal) ? Color.FromArgb(16, 120, 65) : Color.FromArgb(90, 105, 125);
            toolTip.SetToolTip(lblStatus, lblStatus.Text);
        }
        private static string Clean(string text) { return (text ?? "").Normalize(NormalizationForm.FormKC); }
        private static string FeatureName(VCutPropertySetupCommand.BendRow row)
        {
            return Clean(!string.IsNullOrEmpty(row.FeatureName) ? row.FeatureName : row.Feature == null ? "—" : row.Feature.Name);
        }
        private static string Format(double? value)
        {
            return value.HasValue && !double.IsNaN(value.Value) && !double.IsInfinity(value.Value)
                ? value.Value.ToString("0.###", CultureInfo.InvariantCulture) : "?";
        }
        private static bool TryPropertyValue(string text, out double value)
        {
            return double.TryParse((text ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
                value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
        }
        private static string ChoiceForKind(string kind)
        { return kind == "V溝1" ? "V溝1 ○" : kind == "V溝2" ? "V溝2 ◎" : kind == "C溝" ? "C溝 ●" : Unassigned; }
        private static string KindForChoice(string choice)
        { return (choice ?? "").StartsWith("V溝1") ? "V溝1" : (choice ?? "").StartsWith("V溝2") ? "V溝2" : (choice ?? "").StartsWith("C溝") ? "C溝" : ""; }
        private void ConfigureAssignmentCell(DataGridViewRow viewRow)
        {
            var row = viewRow.Tag as VCutPropertySetupCommand.BendRow;
            var cell = viewRow.Cells[colKind.Index] as DataGridViewComboBoxCell;
            if (row == null || cell == null) return;
            bool canAssign = row.IsGrooveTable && !row.WrongBendTable && string.IsNullOrWhiteSpace(row.TableError) &&
                row.ValueMm.HasValue && row.ValueMm.Value > 0 && !double.IsNaN(row.ValueMm.Value) && !double.IsInfinity(row.ValueMm.Value);
            cell.ReadOnly = !canAssign && string.IsNullOrEmpty(row.Kind);
            cell.DisplayStyle = cell.ReadOnly ? DataGridViewComboBoxDisplayStyle.Nothing : DataGridViewComboBoxDisplayStyle.DropDownButton;
            cell.Style.BackColor = cell.ReadOnly ? Color.FromArgb(235, 239, 244) : !canAssign ? Color.FromArgb(255, 245, 230) : Color.White;
            if (!canAssign && !string.IsNullOrEmpty(row.Kind))
            {
                cell.Items.Clear(); cell.Items.Add(Unassigned); cell.Items.Add(ChoiceForKind(row.Kind));
            }
            toolTip.SetToolTip(gridBends, "Click Bend to highlight / 曲げをクリックしてハイライト");
        }
        private void UpdateStatus(DataGridViewRow viewRow)
        {
            var row = viewRow.Tag as VCutPropertySetupCommand.BendRow;
            if (row == null) return;
            VCutBendIssue issue = VCutPropertySetupCommand.GetDraftIssue(row, Rows);
            string status;
            if (issue != VCutBendIssue.None) status = "NG · " + IssueLabel(issue);
            else if (!string.Equals(row.Kind, row.OriginalKind, StringComparison.Ordinal)) status = "Pending save / 保存待ち";
            else if (string.IsNullOrEmpty(row.Kind)) status = "Normal bend / 通常曲げ";
            else
            {
                string raw; double value;
                if (properties == null || !properties.TryGetValue(row.Kind, out raw) || !TryPropertyValue(raw, out value))
                    status = "NG · Property unset / プロパティ未設定";
                else if (Math.Abs(value - row.ValueMm.Value) > VCutPropertySetupCommand.ToleranceMm)
                    status = "NG · Property mismatch / プロパティ不一致";
                else status = "OK · Saved and matched / 保存済み・一致";
            }
            viewRow.ErrorText = status;
            bool ok = status.StartsWith("OK", StringComparison.Ordinal);
            bool ng = status.StartsWith("NG", StringComparison.Ordinal);
            bool pending = status.StartsWith("Pending", StringComparison.Ordinal);
            var statusCell = viewRow.Cells[colStatus.Index];
            statusCell.Value = ok ? "✓" : pending ? "…" : ng ? "✕" : "—";
            statusCell.Style.ForeColor = ok ? Color.FromArgb(16, 145, 76) : pending ? Color.FromArgb(175, 95, 10) : ng ? Color.FromArgb(211, 35, 35) : Color.FromArgb(125, 139, 153);
            statusCell.Style.SelectionForeColor = statusCell.Style.ForeColor;
            statusCell.ToolTipText = status;
            viewRow.Cells[colKind.Index].ToolTipText = status;
            viewRow.DefaultCellStyle.ForeColor = ng ? Color.FromArgb(185, 28, 28) : Color.FromArgb(25, 35, 50);
            viewRow.DefaultCellStyle.SelectionForeColor = viewRow.DefaultCellStyle.ForeColor;
            viewRow.DefaultCellStyle.BackColor = ng ? Color.FromArgb(255, 243, 243) : pending ? Color.FromArgb(255, 251, 238) : viewRow.Index % 2 == 0 ? Color.White : Color.FromArgb(247, 250, 254);
        }
        private static string IssueLabel(VCutBendIssue issue)
        {
            switch (issue)
            {
                case VCutBendIssue.WrongBendTable: return "Wrong bend table / 曲げ表不一致";
                case VCutBendIssue.TableUnreadable: return "Bend table unavailable / 曲げ表を確認";
                case VCutBendIssue.MissingValue: return "Remaining value missing / 残厚なし";
                case VCutBendIssue.InvalidValue: return "Invalid remaining value / 残厚が無効";
                case VCutBendIssue.MarkRequired: return "Mark unset / 加工印未設定";
                case VCutBendIssue.MarkNotAllowed: return "Normal bend: clear mark / 通常曲げ・印を解除";
                case VCutBendIssue.InvalidMark: return "Invalid mark / 加工印が無効";
                case VCutBendIssue.RemainingNotBelowThickness: return "Remaining must be below stock / 残厚は板厚未満";
                case VCutBendIssue.KindValuesDiffer: return "Same mark has different values / 同じ印の値が不一致";
                case VCutBendIssue.VValuesEqual: return "V溝1 and V溝2 must differ / 異なる残厚が必要";
                default: return "Check bend data / 曲げを確認";
            }
        }
        private void UpdateResponsiveColumns()
        {
            if (colType == null || gridBends.ClientSize.Width <= 0) return;
            int width = gridBends.ClientSize.Width;
            colType.Visible = width >= 540;
            colNo.Width = width < 350 ? 22 : 26;
            colStatus.Width = 50;
            colValue.Width = width < 350 ? 48 : 54;
            colKind.Width = width < 350 ? 88 : 106;
        }
        private void ApplyUnifiedGridStyle()
        {
            gridBends.ColumnHeadersDefaultCellStyle.Font = new Font("Meiryo UI", 8F, FontStyle.Bold);
            gridBends.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            gridBends.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.True;
            gridBends.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(237, 243, 251);
            gridBends.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(30, 58, 110);
            gridBends.ColumnHeadersDefaultCellStyle.Padding = Padding.Empty;
            gridBends.DefaultCellStyle.Font = new Font("Meiryo UI", 8.5F);
            gridBends.DefaultCellStyle.Padding = new Padding(3, 0, 3, 0);
            gridBends.DefaultCellStyle.SelectionBackColor = Color.FromArgb(223, 235, 252);
            gridBends.GridColor = Color.FromArgb(221, 230, 241);
            colNo.DefaultCellStyle.Alignment = colStatus.DefaultCellStyle.Alignment = colValue.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            UpdateResponsiveColumns();
        }
        private static void EnableDoubleBuffer(DataGridView grid)
        {
            try
            {
                typeof(DataGridView).InvokeMember(
                    "DoubleBuffered",
                    BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.SetProperty,
                    null,
                    grid,
                    new object[] { true });
            }
            catch { }
        }

        private void GridBends_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0) return;

            if (e.ColumnIndex == colStatus.Index)
            {
                e.PaintBackground(e.CellBounds, true);

                string val = Convert.ToString(e.Value) ?? "";
                bool isOk = val == "✓" || val.IndexOf("OK", StringComparison.OrdinalIgnoreCase) >= 0;
                bool isNg = val == "✕" || val.IndexOf("NG", StringComparison.OrdinalIgnoreCase) >= 0;
                bool isPending = val == "…";

                int cx = e.CellBounds.X + e.CellBounds.Width / 2;
                int cy = e.CellBounds.Y + e.CellBounds.Height / 2;
                int size = 18;
                var badgeRect = new Rectangle(cx - size / 2, cy - size / 2, size, size);

                var g = e.Graphics;
                var oldSmoothing = g.SmoothingMode;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                if (isOk)
                {
                    using (var brush = new SolidBrush(Color.FromArgb(236, 253, 245)))
                        g.FillEllipse(brush, badgeRect);
                    using (var pen = new Pen(Color.FromArgb(110, 231, 183), 1f))
                        g.DrawEllipse(pen, badgeRect);

                    using (var pen = new Pen(Color.FromArgb(5, 150, 105), 2.2f))
                    {
                        pen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                        pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                        pen.LineJoin = System.Drawing.Drawing2D.LineJoin.Round;
                        g.DrawLines(pen, new PointF[] {
                            new PointF(cx - 4.5f, cy + 0.2f),
                            new PointF(cx - 1.5f, cy + 3.8f),
                            new PointF(cx + 4.5f, cy - 3.2f)
                        });
                    }
                }
                else if (isNg)
                {
                    using (var brush = new SolidBrush(Color.FromArgb(254, 242, 242)))
                        g.FillEllipse(brush, badgeRect);
                    using (var pen = new Pen(Color.FromArgb(252, 165, 165), 1f))
                        g.DrawEllipse(pen, badgeRect);

                    using (var pen = new Pen(Color.FromArgb(225, 29, 72), 2.0f))
                    {
                        pen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                        pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                        float r = 3.6f;
                        g.DrawLine(pen, cx - r, cy - r, cx + r, cy + r);
                        g.DrawLine(pen, cx - r, cy + r, cx + r, cy - r);
                    }
                }
                else if (isPending)
                {
                    using (var brush = new SolidBrush(Color.FromArgb(175, 95, 10)))
                        for (int offset = -5; offset <= 5; offset += 5)
                            g.FillEllipse(brush, cx + offset - 1.5f, cy - 1.5f, 3, 3);
                }
                else
                {
                    using (var pen = new Pen(Color.FromArgb(148, 163, 184), 2.0f))
                    {
                        pen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                        pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                        g.DrawLine(pen, cx - 4f, cy, cx + 4f, cy);
                    }
                }

                g.SmoothingMode = oldSmoothing;
                e.Handled = true;
            }
        }
    }
}
