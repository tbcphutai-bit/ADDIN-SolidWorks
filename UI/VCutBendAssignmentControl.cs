using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using ADDIN.Commands;

namespace ADDIN.UI
{
    // Behavior for the designer-owned Bend Feature editor in Model > Macro.
    public partial class VCutBendAssignmentControl : UserControl
    {
        private const string Unassigned = "未設定";
        private IDictionary<string, string> properties;
        private bool loadingRows;
        private bool saveAllowed;
        private bool savedSinceRefresh;

        private bool HasUnsavedChanges
        {
            get { return Rows != null && Rows.Exists(row => row.Kind != row.OriginalKind); }
        }

        internal bool ConfirmDiscardChanges(string action)
        {
            if (gridBends.IsCurrentCellDirty)
                gridBends.CommitEdit(DataGridViewDataErrorContexts.Commit);
            gridBends.EndEdit();
            return !HasUnsavedChanges || VCutNoticeDialog.Confirm(this,
                "Thông tin bào chưa được lưu",
                "Bạn đã sửa Mark nhưng chưa bấm Save.\r\n\r\n" +
                "Nếu " + action + ", các thay đổi chưa lưu sẽ bị bỏ.\r\n" +
                "Bạn có muốn tiếp tục không?") == DialogResult.Yes;
        }

        private void UpdateSaveState()
        {
            btnSave.Visible = true;
            btnSave.Enabled = saveAllowed && (!savedSinceRefresh || HasUnsavedChanges);
        }

        internal List<VCutPropertySetupCommand.BendRow> Rows { get; private set; }
        internal IDictionary<string, string> Properties { get { return properties; } }
        internal event EventHandler SaveRequested;
        internal event EventHandler RefreshRequested;
        internal event EventHandler CloseRequested;
        internal event Action<VCutPropertySetupCommand.BendRow> BendSelected;
        internal string CurrentFeatureName
        {
            get
            {
                DataGridViewRow selected = gridBends.CurrentRow;
                return selected == null ? "" :
                    Convert.ToString(selected.Cells[colFeature.Index].Value) ?? "";
            }
        }

        public VCutBendAssignmentControl()
        {
            InitializeComponent();
            EnableDoubleBuffer(gridBends);
            ApplyUnifiedGridStyle();
            gridBends.CellPainting += GridBends_CellPainting;
            gridBends.CurrentCellDirtyStateChanged += (sender, args) =>
            {
                if (gridBends.IsCurrentCellDirty)
                    gridBends.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            gridBends.CellValueChanged += (sender, args) =>
            {
                if (!loadingRows && properties != null && args.RowIndex >= 0 &&
                    args.ColumnIndex == colKind.Index)
                {
                    // Commit the changed kind before validating every related row.
                    UpdateStatus(gridBends.Rows[args.RowIndex]);
                    saveAllowed = true;
                    foreach (DataGridViewRow viewRow in gridBends.Rows)
                        UpdateStatus(viewRow);
                    UpdateSaveState();
                    ShowSelectedDetail();
                }
            };
            gridBends.CellEndEdit += (sender, args) =>
            {
                if (args.RowIndex >= 0 && args.ColumnIndex == colKind.Index)
                    ConfigureAssignmentCell(gridBends.Rows[args.RowIndex]);
            };
            gridBends.SelectionChanged += (sender, args) => ShowSelectedDetail();
            gridBends.CellClick += (sender, args) =>
            {
                if (args.RowIndex >= 0 && args.ColumnIndex == colFeature.Index)
                    HighlightCurrentRow();
            };
            gridBends.DataError += (sender, args) => { args.ThrowException = false; };
            btnSave.Click += (sender, args) =>
            {
                if (!saveAllowed || (savedSinceRefresh && !HasUnsavedChanges)) return;
                if (gridBends.IsCurrentCellDirty)
                    gridBends.CommitEdit(DataGridViewDataErrorContexts.Commit);
                SaveRequested?.Invoke(this, EventArgs.Empty);
            };
            btnRefresh.Click += (sender, args) =>
            {
                if (!ConfirmDiscardChanges("Refresh")) return;
                // Only a successful LoadRows from Refresh enables Save again.
                saveAllowed = false;
                UpdateSaveState();
                RefreshRequested?.Invoke(this, EventArgs.Empty);
            };
            btnClose.Click += (sender, args) =>
            {
                if (ConfirmDiscardChanges("đóng bảng"))
                    CloseRequested?.Invoke(this, EventArgs.Empty);
            };
        }

        internal void LoadRows(List<VCutPropertySetupCommand.BendRow> rows,
            IDictionary<string, string> propertyValues, string partTitle,
            string configuration, string preferredFeatureName = null, bool refreshed = false)
        {
            loadingRows = true;
            saveAllowed = refreshed;
            savedSinceRefresh = false;
            Rows = rows;
            properties = propertyValues;
            string cleanTitle = (partTitle ?? "").Normalize(NormalizationForm.FormKC);
            string cleanConfig = (configuration ?? "").Normalize(NormalizationForm.FormKC);
            lblDocument.Text = "Part / 部品: " + cleanTitle +
                "    Config / 設定: " + cleanConfig;
            lblDocument.AutoEllipsis = true;
            lblProperties.Text = "○ V溝1: " + Display("V溝1") + " mm     " +
                "◎ V溝2: " + Display("V溝2") + " mm\r\n" +
                "● C溝:   " + Display("C溝") + " mm";
            gridBends.Rows.Clear();
            foreach (VCutPropertySetupCommand.BendRow row in rows)
            {
                string choice = ChoiceForKind(row.Kind);
                string value = row.ValueMm.HasValue
                    ? row.ValueMm.Value.ToString("0.###", CultureInfo.InvariantCulture)
                    : row.IsGrooveTable || !string.IsNullOrWhiteSpace(row.TableError)
                        ? "?" : "—";
                string cleanName = (row.Feature.Name ?? "").Normalize(NormalizationForm.FormKC);
                string cleanType = (row.FeatureType ?? "").Normalize(NormalizationForm.FormKC);
                int index = gridBends.Rows.Add(gridBends.Rows.Count + 1,
                    cleanName, "", cleanType, value, choice);
                DataGridViewRow viewRow = gridBends.Rows[index];
                viewRow.Tag = row;
                viewRow.Cells[colFeature.Index].ToolTipText = cleanName +
                    " [" + cleanType + "]";
                viewRow.Cells[colType.Index].ToolTipText = cleanType;
                viewRow.Cells[colValue.Index].ToolTipText =
                    row.TableSource + ": " + row.Table;
                ConfigureAssignmentCell(viewRow);
                UpdateStatus(viewRow);
            }
            if (gridBends.Rows.Count > 0)
            {
                DataGridViewRow selected = null;
                foreach (DataGridViewRow viewRow in gridBends.Rows)
                    if (string.Equals(Convert.ToString(viewRow.Cells[colFeature.Index].Value),
                        preferredFeatureName, StringComparison.OrdinalIgnoreCase))
                    {
                        selected = viewRow;
                        break;
                    }
                gridBends.CurrentCell = (selected ?? gridBends.Rows[0]).Cells[colFeature.Index];
            }
            foreach (DataGridViewRow viewRow in gridBends.Rows) UpdateStatus(viewRow);
            loadingRows = false;
            UpdateSaveState();
            ShowSelectedDetail();
        }

        internal void HighlightCurrentRow()
        {
            VCutPropertySetupCommand.BendRow row = gridBends.CurrentRow == null
                ? null : gridBends.CurrentRow.Tag as VCutPropertySetupCommand.BendRow;
            if (row != null) BendSelected?.Invoke(row);
        }

        internal void RefreshAfterSave()
        {
            savedSinceRefresh = true;
            UpdateSaveState();
            if (properties == null) return;
            lblProperties.Text = "○ V溝1: " + Display("V溝1") + " mm     " +
                "◎ V溝2: " + Display("V溝2") + " mm\r\n" +
                "● C溝:   " + Display("C溝") + " mm";
            foreach (DataGridViewRow row in gridBends.Rows)
                UpdateStatus(row);
            ShowSelectedDetail();
        }

        private void ShowSelectedDetail()
        {
            DataGridViewRow selected = gridBends.CurrentRow;
            VCutPropertySetupCommand.BendRow row = selected == null ? null :
                selected.Tag as VCutPropertySetupCommand.BendRow;
            if (row == null)
            {
                lblDetail.Text = "";
                lblStatus.Text = "Select a bend / 曲げを選択";
                lblStatus.ForeColor = Color.FromArgb(143, 92, 22);
                return;
            }
            string tableName = Path.GetFileName(row.Table ?? "").Normalize(NormalizationForm.FormKC);
            string angleInfo = "角度: " + Format(row.BendAngleDeg) + "°   R: " +
                Format(row.BendRadiusMm) + " mm   板厚: " + Format(row.TableThicknessMm) + " mm";
            string vcutInfo = !string.IsNullOrWhiteSpace(row.TableError)
                ? "V cut unknown / 溝未確認"
                : row.IsGrooveTable ? "V溝: " + Format(row.ValueMm) + " mm" : "溝なし";
            string cleanName = (row.Feature.Name ?? "").Normalize(NormalizationForm.FormKC);
            string cleanType = (row.FeatureType ?? "").Normalize(NormalizationForm.FormKC);
            lblDetail.Text = cleanName + " (" + cleanType + ")\r\n" +
                angleInfo + "\r\n" +
                "曲げ表: " + tableName + "\r\n" +
                vcutInfo + " · " + row.TableSource;
            lblStatus.Text = selected.ErrorText.StartsWith("NG") ? selected.ErrorText : HasUnsavedChanges
                ? "Unsaved changes / 未保存" : selected.ErrorText;
            lblStatus.ForeColor = selected.ErrorText.StartsWith("NG") ? Color.FromArgb(195, 20, 20) : HasUnsavedChanges
                ? Color.FromArgb(175, 95, 10) : selected.ErrorText.StartsWith("OK")
                ? Color.FromArgb(16, 120, 65) :
                selected.ErrorText.StartsWith("NG")
                    ? Color.FromArgb(195, 20, 20) : Color.FromArgb(150, 95, 20);
            if (!loadingRows) BendSelected?.Invoke(row);
        }

        private static string Format(double? value)
        {
            return value.HasValue ? value.Value.ToString("0.###",
                CultureInfo.InvariantCulture) : "?";
        }

        private string Display(string name)
        {
            string value;
            return properties.TryGetValue(name, out value) &&
                !string.IsNullOrWhiteSpace(value) ? value : "—";
        }

        private static string ChoiceForKind(string kind)
        {
            return kind == "V溝1" ? "V溝1 ○" :
                kind == "V溝2" ? "V溝2 ◎" :
                kind == "C溝" ? "C溝 ●" : Unassigned;
        }

        private void ConfigureAssignmentCell(DataGridViewRow viewRow)
        {
            VCutPropertySetupCommand.BendRow row =
                viewRow.Tag as VCutPropertySetupCommand.BendRow;
            DataGridViewComboBoxCell cell =
                viewRow.Cells[colKind.Index] as DataGridViewComboBoxCell;
            if (row == null || cell == null) return;

            bool canAssign = row.IsGrooveTable &&
                string.IsNullOrWhiteSpace(row.TableError) &&
                row.ValueMm.HasValue && row.ValueMm.Value > 0 &&
                !double.IsInfinity(row.ValueMm.Value);
            if (canAssign)
            {
                cell.ReadOnly = false;
                cell.DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton;
                cell.Style.BackColor = Color.White;
                cell.ToolTipText = "Select the drawing mark / 図面の加工印を選択";
                return;
            }

            if (row.Kind.Length > 0)
            {
                // A previously saved mark remains removable if its Bend Table
                // was later changed to an ordinary bend table.
                string choice = ChoiceForKind(row.Kind);
                cell.Items.Clear();
                cell.Items.Add(Unassigned);
                cell.Items.Add(choice);
                cell.ReadOnly = false;
                cell.DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton;
                cell.Style.BackColor = Color.FromArgb(255, 245, 235);
                cell.ToolTipText = "Normal bend: clear the old mark / 通常曲げ: 古い加工印を解除";
                return;
            }

            cell.ReadOnly = true;
            cell.DisplayStyle = DataGridViewComboBoxDisplayStyle.Nothing;
            cell.Style.BackColor = Color.FromArgb(239, 242, 244);
            cell.ToolTipText = string.IsNullOrWhiteSpace(row.TableError)
                ? "Normal bend: no V cut mark / 通常曲げ: 溝加工印なし"
                : "Bend table unavailable / ベンドテーブルを読込めません";
        }

        private string AssignmentConflict(VCutPropertySetupCommand.BendRow row)
        {
            if (Rows == null || row.Kind.Length == 0 || !row.ValueMm.HasValue) return "";
            foreach (var other in Rows)
            {
                if (ReferenceEquals(row, other) || !other.IsGrooveTable ||
                    !string.IsNullOrWhiteSpace(other.TableError) || !other.ValueMm.HasValue) continue;
                double delta = Math.Abs(row.ValueMm.Value - other.ValueMm.Value);
                if (row.Kind == other.Kind && delta > VCutPropertySetupCommand.ToleranceMm)
                    return "NG · " + row.Kind + " values differ / 同じ加工印の値が不一致";
                if (((row.Kind == "V溝1" && other.Kind == "V溝2") ||
                    (row.Kind == "V溝2" && other.Kind == "V溝1")) &&
                    delta <= VCutPropertySetupCommand.ToleranceMm)
                    return "NG · V溝1 = V溝2 / 異なる値が必要";
            }
            return "";
        }

        private void UpdateStatus(DataGridViewRow viewRow)
        {
            VCutPropertySetupCommand.BendRow row =
                viewRow.Tag as VCutPropertySetupCommand.BendRow;
            if (row == null) return;
            string choice = Convert.ToString(viewRow.Cells[colKind.Index].Value) ?? "";
            row.Kind = choice.StartsWith("V溝1") ? "V溝1" :
                choice.StartsWith("V溝2") ? "V溝2" :
                choice.StartsWith("C溝") ? "C溝" : "";
            string conflict = AssignmentConflict(row);
            string status;
            if (row.WrongBendTable)
                status = "NG · Wrong bend table / 曲げテーブル不一致";
            else if (row.Kind.Length == 0)
                status = !string.IsNullOrWhiteSpace(row.TableError)
                    ? "NG · Bend table error / テーブル読込エラー" :
                    row.IsGrooveTable && !row.ValueMm.HasValue
                        ? "NG · V cut value missing / 溝値なし" :
                    !row.IsGrooveTable ? "Normal bend / 通常曲げ" :
                    "NG · Mark unset / 加工印未設定";
            else if (!string.IsNullOrWhiteSpace(row.TableError))
                status = "NG · Bend table error / テーブル読込エラー";
            else if (!row.IsGrooveTable)
                status = "NG · Normal bend: clear mark / 通常曲げ: 印を解除";
            else if (!row.ValueMm.HasValue || row.ValueMm.Value <= 0 ||
                double.IsNaN(row.ValueMm.Value) || double.IsInfinity(row.ValueMm.Value))
                status = "NG · V cut value missing / 溝値なし";
            else if (conflict.Length > 0)
                status = conflict;
            else if (!string.Equals(row.Kind, row.OriginalKind, StringComparison.Ordinal))
                status = "Pending save / 保存待ち";
            else
            {
                string raw;
                double prop;
                if (!row.ValueMm.HasValue)
                    status = "NG · V cut value missing / 溝値なし";
                else if (!properties.TryGetValue(row.Kind, out raw) ||
                    !double.TryParse((raw ?? "").Trim().Replace(',', '.'),
                        NumberStyles.Float, CultureInfo.InvariantCulture, out prop) || prop <= 0)
                    status = "NG · " + row.Kind + " unset / 未設定 → " +
                        Format(row.ValueMm) + " mm";
                else if (Math.Abs(prop - row.ValueMm.Value) >
                    VCutPropertySetupCommand.ToleranceMm)
                    status = "NG · " + row.Kind + " mismatch / 不一致 → " +
                        Format(row.ValueMm) + " mm";
                else status = "OK · Matched / 一致";
            }
            if (status.StartsWith("OK", StringComparison.Ordinal) &&
                !string.Equals(row.Kind, row.OriginalKind, StringComparison.Ordinal))
                status = "Pending save / 保存待ち";
            viewRow.ErrorText = status;
            viewRow.Cells[colKind.Index].ToolTipText = status;
            DataGridViewCell statusCell = viewRow.Cells[colStatus.Index];
            bool savedMatch = status.StartsWith("OK", StringComparison.Ordinal) &&
                string.Equals(row.Kind, row.OriginalKind, StringComparison.Ordinal);
            bool needsVcutSetup = row.IsGrooveTable || row.Kind.Length > 0 ||
                !string.IsNullOrWhiteSpace(row.TableError);
            bool pending = status.StartsWith("Pending", StringComparison.Ordinal);
            statusCell.Value = savedMatch ? "✓" : pending ? "…" : needsVcutSetup ? "✕" : "—";
            statusCell.Style.ForeColor = savedMatch
                ? Color.FromArgb(16, 145, 76) : pending ? Color.FromArgb(175, 95, 10) : needsVcutSetup
                    ? Color.FromArgb(211, 35, 35) : Color.FromArgb(125, 139, 153);
            statusCell.Style.SelectionForeColor = statusCell.Style.ForeColor;
            statusCell.ToolTipText = status.StartsWith("NG") || pending ? status : savedMatch
                ? "Saved and matched / 保存済み・一致" : needsVcutSetup
                    ? "Set the mark and save / 加工印を設定して保存" :
                    "Normal bend / 通常曲げ";
            bool ng = status.StartsWith("NG");
            viewRow.DefaultCellStyle.ForeColor = ng ? Color.FromArgb(190, 20, 20) : Color.FromArgb(25, 30, 36);
            viewRow.DefaultCellStyle.SelectionForeColor = ng ? Color.FromArgb(190, 20, 20) : Color.FromArgb(10, 50, 110);
            if (ng)
            {
                viewRow.DefaultCellStyle.BackColor = Color.FromArgb(255, 248, 248);
            }
            else
            {
                viewRow.DefaultCellStyle.BackColor = (viewRow.Index % 2 == 1)
                    ? Color.FromArgb(248, 250, 253) : Color.White;
            }
            if (!loadingRows) UpdateSaveState();
            if (gridBends.CurrentRow == viewRow) ShowSelectedDetail();
        }

        private void ApplyUnifiedGridStyle()
        {
            var headerFont = new Font("Meiryo UI", 8.5F, FontStyle.Bold);
            var cellFont = new Font("Meiryo UI", 8.5F, FontStyle.Bold);

            gridBends.ColumnHeadersHeight = 38;
            gridBends.ColumnHeadersDefaultCellStyle.Font = headerFont;
            gridBends.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(30, 58, 138); // Deep Navy Bold
            gridBends.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(241, 245, 249); // Soft Slate 100
            gridBends.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(241, 245, 249);
            gridBends.ColumnHeadersDefaultCellStyle.SelectionForeColor = Color.FromArgb(30, 58, 138);
            gridBends.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            gridBends.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.True;

            gridBends.DefaultCellStyle.Font = cellFont;
            gridBends.DefaultCellStyle.ForeColor = Color.FromArgb(25, 30, 36);
            gridBends.DefaultCellStyle.BackColor = Color.White;
            gridBends.DefaultCellStyle.SelectionBackColor = Color.FromArgb(224, 242, 254); // Soft Sky 100
            gridBends.DefaultCellStyle.SelectionForeColor = Color.FromArgb(3, 105, 161);
            gridBends.DefaultCellStyle.Padding = new Padding(2, 0, 2, 0);

            gridBends.AlternatingRowsDefaultCellStyle.Font = cellFont;
            gridBends.AlternatingRowsDefaultCellStyle.ForeColor = Color.FromArgb(25, 30, 36);
            gridBends.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(234, 242, 250);
            gridBends.AlternatingRowsDefaultCellStyle.Padding = new Padding(2, 0, 2, 0);

            gridBends.BackgroundColor = Color.FromArgb(242, 245, 248);
            gridBends.GridColor = Color.FromArgb(215, 222, 230);

            colNo.Width = 22;
            colFeature.HeaderText = "Bend\n曲げ";
            colFeature.MinimumWidth = 45;
            colFeature.FillWeight = 28F;

            colStatus.Width = 28;
            colStatus.HeaderText = "✔";

            colType.HeaderText = "Type\n種類";
            colType.MinimumWidth = 50;
            colType.FillWeight = 32F;

            colValue.Width = 34;
            colValue.HeaderText = "V\ncut";

            colKind.HeaderText = "Mark\n加工印";
            colKind.MinimumWidth = 65;
            colKind.FillWeight = 40F;

            // Harmonize typography across top and bottom panels
            var uiFontBold = new Font("Meiryo UI", 9.5F, FontStyle.Bold);
            var uiFontMed = new Font("Meiryo UI", 8.5F, FontStyle.Bold);
            var uiFontNorm = new Font("Meiryo UI", 8.5F, FontStyle.Regular);

            lblTitle.Font = uiFontBold;
            lblTitle.ForeColor = Color.FromArgb(15, 23, 42);
            lblTitle.BackColor = Color.FromArgb(237, 242, 247);

            lblDocument.Font = uiFontNorm;
            lblDocument.ForeColor = Color.FromArgb(51, 65, 85);

            lblProperties.Font = uiFontNorm;
            lblProperties.ForeColor = Color.FromArgb(15, 23, 42);

            btnRefresh.Font = uiFontMed;
            btnRefresh.ForeColor = Color.FromArgb(15, 23, 42);

            lblDetail.Font = uiFontNorm;
            lblDetail.ForeColor = Color.FromArgb(15, 23, 42);
            lblDetail.BackColor = Color.FromArgb(244, 247, 251);

            lblStatus.Font = uiFontMed;

            btnSave.Font = uiFontMed;
            btnClose.Font = uiFontMed;
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
