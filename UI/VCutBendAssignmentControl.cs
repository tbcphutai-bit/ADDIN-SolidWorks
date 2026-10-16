using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
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
        private static readonly Font LabelBoldFont = new Font("Segoe UI", 9.5F, FontStyle.Bold);
        private static readonly Font ValueBoldFont = new Font("Segoe UI", 10F, FontStyle.Bold);
        private string headerMaterialText = "—";
        private string headerStockText = "t = —";
        private string headerPartText = "—";
        private string headerConfigText = "—";
        private string detailFeatureText = "";
        private string detailTypeText = "";
        private string detailAngleText = "";
        private string detailRadiusText = "";
        private string detailGrooveText = "";
        private string detailTableText = "";
        private string detailIssueText = "";
        private int summaryOkCount, summaryPendingCount, summaryNgCount, summaryNormalCount;

        private sealed class PropertyChipState
        {
            public string Title;
            public string Value;
            public bool Invalid;
            public bool Pending;
        }

        internal List<VCutPropertySetupCommand.BendRow> Rows { get; private set; }
        internal IDictionary<string, string> Properties { get { return properties; } }
        internal event EventHandler DraftChanged, SaveRequested, RefreshRequested, CloseRequested;
        internal event Action<VCutPropertySetupCommand.BendRow> BendSelected;
        private bool HasUnsavedChanges { get { return Rows != null && Rows.Any(row => row.Kind != row.OriginalKind) || minimumLive?.HasUnshownDraft == true; } }
        private bool HasBlockingIssues { get { return Rows != null && Rows.Any(row => VCutPropertySetupCommand.GetDraftIssue(row, Rows) != VCutBendIssue.None); } }
        internal string CurrentFeatureName
        {
            get { return gridBends.CurrentRow == null ? "" : Convert.ToString(gridBends.CurrentRow.Cells[colFeature.Index].Value) ?? ""; }
        }

        public VCutBendAssignmentControl()
        {
            InitializeComponent();
            InitializeMinimum();
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            EnableDoubleBuffer(gridBends);
            EnableDoubleBuffer(panelHeader);
            EnableDoubleBuffer(panelFooter);
            EnableDoubleBuffer(lblDocument);
            EnableDoubleBuffer(lblDetail);
            EnableDoubleBuffer(lblSummary);
            ApplyUnifiedGridStyle();

            lblTitle.Text = "";
            lblTitle.Paint += LblTitle_Paint;
            lblDocument.Text = "";
            lblDocument.Paint += LblDocument_Paint;
            lblDetail.Text = "";
            lblDetail.Paint += LblDetail_Paint;
            lblSummary.Text = "";
            lblSummary.Paint += LblSummary_Paint;
            panelHeader.Paint += PanelHeader_Paint;
            panelFooter.Paint += PanelFooter_Paint;

            foreach (Label chip in new[] { lblPropertyV1, lblPropertyV2, lblPropertyC })
            {
                chip.Text = "";
                chip.Paint += PropertyChip_Paint;
            }

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
                    if (row != null) { row.Kind = KindForChoice(Convert.ToString(viewRow.Cells[colKind.Index].Value)); if (viewRow.Index == args.RowIndex) row.DraftConflict = null; }
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
                minimumContext?.CheckCurrent();
                UpdateMinimumRows();
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
                if (ConfirmDiscardChanges("Close")) CloseRequested?.Invoke(this, EventArgs.Empty);
            };
            gridBends.SizeChanged += (sender, args) => UpdateResponsiveColumns();
            toolTip.SetToolTip(txtSearch, "Find bend, mark (V溝1 / V溝2 / C溝) or bend table");
            toolTip.SetToolTip(btnRefresh, "Read external feature and bend table changes");
            toolTip.SetToolTip(btnClose, "Close; unsaved changes require confirmation");
            UpdateSaveState();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen borderPen = new Pen(TaiTheme.Border, 1f))
            {
                e.Graphics.DrawRectangle(borderPen, 0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
            }
        }

        private void PanelHeader_Paint(object sender, PaintEventArgs e)
        {
            using (Pen linePen = new Pen(Color.FromArgb(226, 232, 240), 1f))
            {
                e.Graphics.DrawLine(linePen, 0, panelHeader.Height - 1, panelHeader.Width, panelHeader.Height - 1);
            }
        }

        private void PanelFooter_Paint(object sender, PaintEventArgs e)
        {
            using (Pen linePen = new Pen(Color.FromArgb(226, 232, 240), 1f))
            {
                e.Graphics.DrawLine(linePen, 0, 0, panelFooter.Width, 0);
                e.Graphics.DrawLine(linePen, 0, panelFooter.Height - 1, panelFooter.Width, panelFooter.Height - 1);
            }
        }

        private void LblTitle_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            TaiTheme.Prepare(g);

            int h = lblTitle.Height;
            int w = lblTitle.Width;
            int padX = TaiTheme.Px(this, 10);

            // 1. Blue vertical accent bar on the left
            int barW = TaiTheme.Px(this, 4);
            int barH = TaiTheme.Px(this, 16);
            int barY = (h - barH) / 2 + 1;
            TaiTheme.FillRound(g, new Rectangle(padX, barY, barW, barH), 2, TaiTheme.Accent, Color.Empty);

            int titleX = padX + barW + TaiTheme.Px(this, 7);
            int titleNaturalW = TextRenderer.MeasureText(g, "SET BEND LINE", lblTitle.Font, Size.Empty,
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;
            int minTitleRight = titleX + Math.Min(titleNaturalW + TaiTheme.Px(this, 6), Math.Max(70, w / 2));

            // 2. Right-side Badges (Material & Stock Thickness)
            Font badgeFont = LabelBoldFont;
            int pillH = TaiTheme.Px(this, 23);
            int pillY = (h - pillH) / 2 + 1;
            int rightX = w - padX;

            if (!string.IsNullOrEmpty(headerStockText))
            {
                int sw = TextRenderer.MeasureText(g, headerStockText, badgeFont, Size.Empty,
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width + TaiTheme.Px(this, 12);
                if (rightX - sw >= minTitleRight)
                {
                    Rectangle stockRect = new Rectangle(rightX - sw, pillY, sw, pillH);
                    TaiTheme.FillRound(g, stockRect, pillH / 2, Color.FromArgb(219, 234, 254), Color.FromArgb(147, 197, 253));
                    TextRenderer.DrawText(g, headerStockText, badgeFont, stockRect, Color.FromArgb(30, 64, 175),
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                    rightX = stockRect.X - TaiTheme.Px(this, 6);
                }
            }

            if (!string.IsNullOrEmpty(headerMaterialText) && rightX - minTitleRight >= TaiTheme.Px(this, 38))
            {
                int mw = TextRenderer.MeasureText(g, headerMaterialText, badgeFont, Size.Empty,
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width + TaiTheme.Px(this, 12);
                int drawMw = Math.Min(mw, rightX - minTitleRight);
                Rectangle matRect = new Rectangle(rightX - drawMw, pillY, drawMw, pillH);
                TaiTheme.FillRound(g, matRect, pillH / 2, Color.FromArgb(226, 232, 240), Color.FromArgb(203, 213, 225));
                Rectangle matTextRect = new Rectangle(matRect.X + 4, matRect.Y, Math.Max(1, matRect.Width - 8), matRect.Height);
                TextRenderer.DrawText(g, headerMaterialText, badgeFont, matTextRect, Color.FromArgb(30, 41, 59),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                rightX = matRect.X - TaiTheme.Px(this, 8);
            }

            // 3. Single Card Title
            Rectangle titleRect = new Rectangle(titleX, 1, Math.Max(40, rightX - titleX), h - 1);
            TextRenderer.DrawText(g, "SET BEND LINE", lblTitle.Font, titleRect, TaiTheme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }

        private void LblDocument_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            TaiTheme.Prepare(g);

            int h = lblDocument.Height;
            int w = lblDocument.Width;
            int x = TaiTheme.Px(this, 10);
            int rightLimit = Math.Max(x + 20, w - TaiTheme.Px(this, 10));

            Font labelFont = LabelBoldFont;
            Font valueFont = ValueBoldFont;
            Color labelColor = Color.FromArgb(71, 85, 105);
            Color valueColor = Color.FromArgb(15, 23, 42);

            int configReserve = Math.Min(TaiTheme.Px(this, 118), Math.Max(TaiTheme.Px(this, 84), (rightLimit - x) * 40 / 100));
            x = DrawKeyValueSegment(g, "Part:", headerPartText, labelFont, valueFont, labelColor, valueColor, x, 0, h, rightLimit - configReserve);

            if (x + TaiTheme.Px(this, 24) < rightLimit)
            {
                int sepX = x + TaiTheme.Px(this, 6);
                using (Pen sepPen = new Pen(Color.FromArgb(203, 213, 225), 1.2f))
                {
                    g.DrawLine(sepPen, sepX, 5, sepX, h - 5);
                }
                x = sepX + TaiTheme.Px(this, 6);
                DrawKeyValueSegment(g, "Config:", headerConfigText, labelFont, valueFont, labelColor, valueColor, x, 0, h, rightLimit);
            }
        }

        private int DrawKeyValueSegment(Graphics g, string label, string value, Font labelFont, Font valueFont,
            Color labelColor, Color valueColor, int x, int y, int h, int maxRight)
        {
            if (x >= maxRight) return x;
            int lw = TextRenderer.MeasureText(g, label, labelFont, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;
            Rectangle lRect = new Rectangle(x, y, Math.Min(lw + 2, maxRight - x), h);
            TextRenderer.DrawText(g, label, labelFont, lRect, labelColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            x += lw + TaiTheme.Px(this, 5);
            if (x >= maxRight) return x;

            int vw = TextRenderer.MeasureText(g, value, valueFont, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;
            int drawW = Math.Min(vw + 2, maxRight - x);
            Rectangle vRect = new Rectangle(x, y, drawW, h);
            TextRenderer.DrawText(g, value, valueFont, vRect, valueColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            return x + drawW;
        }

        private void PropertyChip_Paint(object sender, PaintEventArgs e)
        {
            Label chip = sender as Label;
            if (chip == null) return;
            Graphics g = e.Graphics;
            g.Clear(panelHeader.BackColor);
            TaiTheme.Prepare(g);

            PropertyChipState state = chip.Tag as PropertyChipState;
            string title = state != null ? state.Title : "";
            string val = state != null ? state.Value : "—";
            bool invalid = state != null && state.Invalid;
            bool pending = state != null && state.Pending;
            bool hasValue = !string.IsNullOrEmpty(val) && val != "—";

            Color fill = invalid
                ? Color.FromArgb(254, 242, 242)
                : pending
                    ? Color.FromArgb(239, 246, 255)
                    : Color.White;
            Color border = invalid
                ? Color.FromArgb(252, 165, 165)
                : pending
                    ? Color.FromArgb(96, 165, 250)
                    : hasValue
                        ? Color.FromArgb(96, 165, 250)
                        : Color.FromArgb(203, 213, 225);
            Color titleColor = invalid
                ? Color.FromArgb(185, 28, 28)
                : pending
                    ? Color.FromArgb(29, 78, 216)
                    : Color.FromArgb(51, 65, 85);
            Color valueColor = invalid
                ? Color.FromArgb(220, 38, 38)
                : pending
                    ? Color.FromArgb(29, 78, 216)
                    : hasValue
                        ? Color.FromArgb(37, 99, 235)
                        : Color.FromArgb(148, 163, 184);

            Rectangle box = new Rectangle(0, 0, Math.Max(1, chip.Width - 1), Math.Max(1, chip.Height - 1));
            TaiTheme.FillRound(g, box, TaiTheme.Px(this, 6), fill, border);

            int pad = TaiTheme.Px(this, 6);
            int valW = TextRenderer.MeasureText(g, val, ValueBoldFont, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;
            int titleW = TextRenderer.MeasureText(g, title, LabelBoldFont, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;

            if (pad * 2 + titleW + valW + TaiTheme.Px(this, 8) > chip.Width && title.Length > 1 &&
                (title[0] == '○' || title[0] == '◎' || title[0] == '●'))
            {
                title = title.Substring(1).TrimStart();
                titleW = TextRenderer.MeasureText(g, title, LabelBoldFont, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;
            }

            int sepX = pad + titleW + TaiTheme.Px(this, 4);
            if (sepX < chip.Width - valW - pad - TaiTheme.Px(this, 3))
            {
                using (Pen divPen = new Pen(Color.FromArgb(226, 232, 240), 1f))
                {
                    g.DrawLine(divPen, sepX, 5, sepX, chip.Height - 6);
                }
            }

            Rectangle titleRect = new Rectangle(pad, 0, Math.Max(1, Math.Min(titleW + 2, chip.Width - pad * 2)), chip.Height);
            TextRenderer.DrawText(g, title, LabelBoldFont, titleRect, titleColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

            int valLeft = Math.Min(chip.Width - pad, pad + titleW + TaiTheme.Px(this, 5));
            Rectangle valRect = new Rectangle(valLeft, 0, Math.Max(1, chip.Width - pad - valLeft), chip.Height);
            TextRenderer.DrawText(g, val, ValueBoldFont, valRect, valueColor,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
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
            btnSave.Enabled = properties != null && saveAllowed && MinimumModelReady && !HasBlockingIssues && (!savedSinceRefresh || HasUnsavedChanges);
            toolTip.SetToolTip(btnSave, !MinimumModelReady ? "Chờ model cập nhật hoặc giải quyết Mark nháp chưa khớp. " + liveNotice : HasBlockingIssues
                ? "Resolve NG rows before saving"
                : savedSinceRefresh && !HasUnsavedChanges ? "Saved"
                : !saveAllowed ? "Refresh external bend data first"
                : "Write marks and update V溝1 / V溝2 / C溝 properties");
            btnSave.Invalidate();
        }
        internal void ConfigureForNativeCommand()
        {
            nativeCommand = true;
            btnRefresh.Visible = btnSave.Visible = btnClose.Visible = true;
            UpdateSaveState();
        }
        internal void ReleaseNativeCommand()
        {
            ReleaseMinimum();
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
                headerPartText = string.IsNullOrWhiteSpace(partTitle) ? "—" : Clean(partTitle);
                headerConfigText = string.IsNullOrWhiteSpace(configuration) ? "—" : Clean(configuration);
                lblDocument.Text = "";
                toolTip.SetToolTip(lblDocument, "Part: " + headerPartText + "\r\nConfig: " + headerConfigText);
                lblDocument.Invalidate();

                string material = string.Join(", ", Rows.Select(r => r.Material).Where(m => !string.IsNullOrWhiteSpace(m)).Distinct());
                var thicknesses = Rows.Where(r => r.ModelThicknessMm.HasValue).Select(r => r.ModelThicknessMm.Value).Distinct().ToList();
                string stock = thicknesses.Count == 1 ? Format(thicknesses[0]) + " mm" : thicknesses.Count > 1 ? "Mixed" : "?";
                headerMaterialText = material.Length == 0 ? "?" : material;
                headerStockText = "t = " + stock;
                toolTip.SetToolTip(lblTitle, "Material (材質): " + headerMaterialText + "\r\nThickness (板厚): " + stock);
                lblTitle.Invalidate();

                bool showTools = Rows.Count >= 5 || !string.IsNullOrWhiteSpace(txtSearch.Text) || chkIssuesOnly.Checked;
                layoutTools.Visible = showTools;
                layoutRoot.RowStyles[1].Height = showTools ? 34F : 0F;

                gridBends.Rows.Clear();
                foreach (var row in Rows)
                {
                    string value = row.ValueMm.HasValue ? Format(row.ValueMm) : row.IsGrooveTable || !string.IsNullOrWhiteSpace(row.TableError) ? "?" : "—";
                    int index = gridBends.Rows.Add(gridBends.Rows.Count + 1, FeatureName(row), Clean(row.FeatureType), value, "", ChoiceForKind(row.Kind));
                    var viewRow = gridBends.Rows[index];
                    viewRow.Tag = row;
                    viewRow.Cells[colFeature.Index].ToolTipText = FeatureName(row) + " [" + row.FeatureType + "]";
                    viewRow.Cells[colType.Index].ToolTipText = row.FeatureType;
                    viewRow.Cells[colValue.Index].ToolTipText = "V-Cut (残厚): " + value + " mm\r\n" + row.TableSource + ": " + row.Table;
                    ConfigureAssignmentCell(viewRow);
                    UpdateStatus(viewRow);
                }
                DataGridViewRow preferred = gridBends.Rows.Cast<DataGridViewRow>().FirstOrDefault(v =>
                    string.Equals(Convert.ToString(v.Cells[colFeature.Index].Value), Clean(preferredFeatureName), StringComparison.OrdinalIgnoreCase));
                if (gridBends.Rows.Count > 0) gridBends.CurrentCell = (preferred ?? gridBends.Rows[0]).Cells[colFeature.Index];
            }
            finally { loadingRows = false; }
            BindMinimum();
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
            minimumContext?.CheckCurrent();
            foreach (DataGridViewRow viewRow in gridBends.Rows) UpdateStatus(viewRow);
            UpdateMinimumRows();
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
                    string text = row == null ? "" : FeatureName(row) + " " + row.FeatureType + " " + row.Kind + " " + row.Table + " " + viewRow.ErrorText + " " + MinimumResult(row).Summary;
                    viewRow.Visible = (!chkIssuesOnly.Checked || viewRow.ErrorText.StartsWith("NG", StringComparison.Ordinal) || MinimumBelow(row)) &&
                        (query.Length == 0 || Clean(text).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
                }
                var selected = previous != null && previous.Visible ? previous : gridBends.Rows.Cast<DataGridViewRow>().FirstOrDefault(v => v.Visible);
                if (selected != null) gridBends.CurrentCell = selected.Cells[colFeature.Index];
                lblEmpty.Visible = selected == null;
                lblEmpty.Text = Rows == null || Rows.Count == 0 ? "No bend features" : "No matching bends";
                if (lblEmpty.Visible) lblEmpty.BringToFront();
            }
            finally { applyingFilters = false; }
            UpdateSummary();
            ShowSelectedDetail();
        }
        private void UpdateSummary()
        {
            var viewRows = gridBends.Rows.Cast<DataGridViewRow>().ToList();
            summaryOkCount = viewRows.Count(r => Convert.ToString(r.Cells[colStatus.Index].Value) == "✓");
            summaryNgCount = viewRows.Count(r => r.ErrorText.StartsWith("NG", StringComparison.Ordinal));
            summaryPendingCount = viewRows.Count(r => Convert.ToString(r.Cells[colStatus.Index].Value) == "…");
            summaryNormalCount = viewRows.Count - summaryOkCount - summaryNgCount - summaryPendingCount;
            lblSummary.Text = "";
            lblSummary.Invalidate();
            toolTip.SetToolTip(lblSummary, "✓ Saved   ·   … Pending   ·   ✕ NG   ·   — Normal bend");
        }

        private void LblSummary_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            TaiTheme.Prepare(g);

            int h = lblSummary.Height;
            int w = lblSummary.Width;
            int x = 0;
            Font font = w < 255 ? LabelBoldFont : ValueBoldFont;
            string pendingText = w < 235 ? ("… " + summaryPendingCount + " Pend") : ("… " + summaryPendingCount + " Pending");

            x = DrawSummaryCounter(g, "✓ " + summaryOkCount + " OK", font, Color.FromArgb(4, 120, 87), x, h, w);
            x = DrawSummaryDot(g, x, h, w);
            x = DrawSummaryCounter(g, pendingText, font,
                summaryPendingCount > 0 ? Color.FromArgb(180, 83, 9) : Color.FromArgb(71, 85, 105), x, h, w);
            x = DrawSummaryDot(g, x, h, w);
            x = DrawSummaryCounter(g, "✕ " + summaryNgCount + " NG", font,
                summaryNgCount > 0 ? Color.FromArgb(185, 28, 28) : Color.FromArgb(71, 85, 105), x, h, w);
            x = DrawSummaryDot(g, x, h, w);
            DrawSummaryCounter(g, "— " + summaryNormalCount, font, Color.FromArgb(30, 41, 59), x, h, w);
        }

        private int DrawSummaryCounter(Graphics g, string text, Font font, Color color, int x, int h, int maxRight)
        {
            if (x >= maxRight) return x;
            int tw = TextRenderer.MeasureText(g, text, font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;
            Rectangle rect = new Rectangle(x, 0, Math.Min(tw + 2, maxRight - x), h);
            TextRenderer.DrawText(g, text, font, rect, color,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            return x + tw + 1;
        }

        private int DrawSummaryDot(Graphics g, int x, int h, int maxRight)
        {
            int gap = TaiTheme.Px(this, 5);
            if (x + gap * 2 >= maxRight) return x;
            int sepX = x + gap;
            using (Pen p = new Pen(Color.FromArgb(203, 213, 225), 1.2f))
            {
                g.DrawLine(p, sepX, 6, sepX, h - 6);
            }
            return sepX + gap;
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

                string valueText = matched
                    ? saved
                    : (saved + "→" + next);

                cards[i].Tag = new PropertyChipState
                {
                    Title = symbols[i] + names[i],
                    Value = valueText,
                    Invalid = invalid,
                    Pending = !matched && !invalid
                };
                cards[i].Invalidate();
                toolTip.SetToolTip(cards[i], names[i] + " (" + symbols[i] + ")" +
                    "\r\nSaved: " + (saved == "—" ? "—" : saved + " mm") +
                    "\r\nNext: " + (assigned.Count == 0 ? "Clear" : next + " mm"));
            }
        }
        private void ShowSelectedDetail()
        {
            if (nativeReleased || IsDisposed || Disposing) return;
            var selected = gridBends.CurrentRow;
            var row = selected == null ? null : selected.Tag as VCutPropertySetupCommand.BendRow;
            if (row == null)
            {
                layoutRoot.RowStyles[3].Height = 82F;
                panelFooter.BackColor = Color.FromArgb(248, 250, 252);
                detailFeatureText = "";
                detailTypeText = "";
                detailAngleText = "";
                detailRadiusText = "";
                detailGrooveText = "";
                detailTableText = "";
                detailIssueText = "";
                lblDetail.Invalidate();
                ShowMinimumDetail(null);
                lblStatus.Text = "Select a bend";
                lblStatus.ForeColor = Color.FromArgb(71, 85, 105);
                return;
            }
            string raw = "";
            if (properties != null && !string.IsNullOrEmpty(row.Kind)) properties.TryGetValue(row.Kind, out raw);
            string tableName = Clean(Path.GetFileName(row.Table ?? ""));
            if (string.IsNullOrEmpty(tableName)) tableName = "—";

            bool ng = selected.ErrorText.StartsWith("NG", StringComparison.Ordinal);
            bool hasTableError = !string.IsNullOrWhiteSpace(row.TableError);

            detailFeatureText = FeatureName(row);
            detailTypeText = Clean(row.FeatureType);
            detailAngleText = Format(row.BendAngleDeg) + "°";
            detailRadiusText = Format(row.BendRadiusMm);
            detailGrooveText = row.ValueMm.HasValue ? Format(row.ValueMm) + " mm" : "";
            detailTableText = tableName;
            detailIssueText = (ng || hasTableError) ? (hasTableError ? row.TableError : selected.ErrorText) : "";

            string fullTooltip = detailFeatureText + "  [" + detailTypeText + "]\r\n" +
                "Angle (角度): " + detailAngleText + "   R: " + detailRadiusText + " mm\r\n" +
                "Thickness (板厚): " + Format(row.ModelThicknessMm) + " mm   Table (表): " + Format(row.TableThicknessMm) + " mm\r\n" +
                "Bend table: " + tableName + " (" + Clean(row.TableSource) + ")\r\n" +
                (row.IsGrooveTable ? "V-Cut (残厚): " + Format(row.ValueMm) + " mm   " : "Normal bend   ") +
                "Mark: " + ChoiceForKind(row.Kind) + "\r\n" +
                "Saved property: " + (string.IsNullOrWhiteSpace(raw) ? "—" : raw + " mm") +
                (!string.IsNullOrWhiteSpace(row.Table) ? "\r\nPath: " + row.Table : "");

            if (ng || hasTableError)
            {
                layoutRoot.RowStyles[3].Height = 104F;
                panelFooter.BackColor = Color.FromArgb(254, 242, 242);
            }
            else
            {
                layoutRoot.RowStyles[3].Height = 82F;
                panelFooter.BackColor = Color.FromArgb(248, 250, 252);
            }
            lblDetail.Invalidate();

            ShowMinimumDetail(row);
            toolTip.SetToolTip(lblDetail, fullTooltip);
            lblStatus.Text = ng ? selected.ErrorText : HasUnsavedChanges ? "Unsaved" :
                savedSinceRefresh ? "Saved" : "Ready";
            lblStatus.ForeColor = ng ? Color.FromArgb(185, 28, 28) : HasUnsavedChanges ? Color.FromArgb(180, 83, 9) :
                savedSinceRefresh || selected.ErrorText.StartsWith("OK", StringComparison.Ordinal) ? Color.FromArgb(4, 120, 87) : Color.FromArgb(30, 41, 59);
            toolTip.SetToolTip(lblStatus, lblStatus.Text);
        }

        private void LblDetail_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            TaiTheme.Prepare(g);

            int w = lblDetail.Width;
            int h = lblDetail.Height;
            Font labelFont = LabelBoldFont;
            Font valueFont = ValueBoldFont;
            Color labelColor = Color.FromArgb(71, 85, 105);
            Color valueColor = Color.FromArgb(15, 23, 42);
            Color accentValueColor = Color.FromArgb(29, 78, 216);

            if (string.IsNullOrEmpty(detailFeatureText))
            {
                TextRenderer.DrawText(g, "Select a bend to inspect its parameters and bend table", labelFont,
                    new Rectangle(0, 0, w, h), labelColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                return;
            }

            int rowH = TaiTheme.Px(this, 22);
            int y1 = 1;
            int x = 0;

            // 1. Feature Name (Bold)
            int fw = TextRenderer.MeasureText(g, detailFeatureText, valueFont, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;
            TextRenderer.DrawText(g, detailFeatureText, valueFont, new Rectangle(x, y1, Math.Min(fw + 2, w), rowH), valueColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            x += fw + TaiTheme.Px(this, 6);

            // 2. Type Badge Pill
            if (!string.IsNullOrEmpty(detailTypeText) && x + 40 < w)
            {
                int tw = TextRenderer.MeasureText(g, detailTypeText, LabelBoldFont, Size.Empty, TextFormatFlags.SingleLine).Width + TaiTheme.Px(this, 10);
                int pillH = TaiTheme.Px(this, 20);
                Rectangle pillRect = new Rectangle(x, y1 + (rowH - pillH) / 2, tw, pillH);
                TaiTheme.FillRound(g, pillRect, 4, Color.FromArgb(226, 232, 240), Color.FromArgb(203, 213, 225));
                TextRenderer.DrawText(g, detailTypeText, LabelBoldFont, pillRect, Color.FromArgb(51, 65, 85),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                x += tw + TaiTheme.Px(this, 8);
            }

            // 3. Separator + Angle / R / 残厚 Key-Value pairs
            if (x + 30 < w)
            {
                using (Pen sepPen = new Pen(Color.FromArgb(203, 213, 225), 1.2f))
                {
                    g.DrawLine(sepPen, x, y1 + 4, x, y1 + rowH - 4);
                }
                x += TaiTheme.Px(this, 8);
                x = DrawKeyValueSegment(g, "Angle:", detailAngleText, labelFont, valueFont, labelColor, valueColor, x, y1, rowH, w);
                x += TaiTheme.Px(this, 10);
                x = DrawKeyValueSegment(g, "R:", detailRadiusText, labelFont, valueFont, labelColor, valueColor, x, y1, rowH, w);
                if (!string.IsNullOrEmpty(detailGrooveText))
                {
                    x += TaiTheme.Px(this, 10);
                    x = DrawKeyValueSegment(g, "残厚:", detailGrooveText, labelFont, valueFont, labelColor, accentValueColor, x, y1, rowH, w);
                }
            }

            // Row 2: Table (Label : Value)
            int y2 = y1 + rowH + 2;
            DrawKeyValueSegment(g, "Table:", detailTableText, labelFont, valueFont, labelColor, valueColor, 0, y2, rowH, w);

            // Row 3 (optional): Warning / Issue
            if (!string.IsNullOrEmpty(detailIssueText))
            {
                int y3 = y2 + rowH + 1;
                TextRenderer.DrawText(g, "⚠ " + detailIssueText, valueFont, new Rectangle(0, y3, w, rowH), Color.FromArgb(185, 28, 28),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            }
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
            cell.Style.BackColor = !canAssign && !string.IsNullOrEmpty(row.Kind) ? Color.FromArgb(255, 245, 230) : Color.White;
            cell.Style.SelectionBackColor = Color.FromArgb(37, 99, 235);
            cell.Style.SelectionForeColor = Color.White;
            if (!canAssign && !string.IsNullOrEmpty(row.Kind))
            {
                cell.Items.Clear(); cell.Items.Add(Unassigned); cell.Items.Add(ChoiceForKind(row.Kind));
            }
            toolTip.SetToolTip(gridBends, "Click Bend to highlight in 3D");
        }
        private void UpdateStatus(DataGridViewRow viewRow)
        {
            var row = viewRow.Tag as VCutPropertySetupCommand.BendRow;
            if (row == null) return;
            VCutBendIssue issue = VCutPropertySetupCommand.GetDraftIssue(row, Rows);
            string status;
            if (issue != VCutBendIssue.None) status = "NG · " + IssueLabel(issue);
            else if (!string.Equals(row.Kind, row.OriginalKind, StringComparison.Ordinal)) status = "Pending save";
            else if (string.IsNullOrEmpty(row.Kind)) status = "Normal bend";
            else
            {
                string raw; double value;
                if (properties == null || !properties.TryGetValue(row.Kind, out raw) || !TryPropertyValue(raw, out value))
                    status = "NG · Property unset (" + row.Kind + ")";
                else if (Math.Abs(value - row.ValueMm.Value) > VCutPropertySetupCommand.ToleranceMm)
                    status = "NG · Property mismatch (" + row.Kind + ")";
                else status = "OK · Saved and matched";
            }
            viewRow.ErrorText = status;
            bool ok = status.StartsWith("OK", StringComparison.Ordinal);
            bool ng = status.StartsWith("NG", StringComparison.Ordinal);
            bool pending = status.StartsWith("Pending", StringComparison.Ordinal);
            var statusCell = viewRow.Cells[colStatus.Index];
            statusCell.Value = ok ? "✓" : pending ? "…" : ng ? "✕" : "—";
            statusCell.Style.ForeColor = ok ? Color.FromArgb(16, 145, 76) : pending ? Color.FromArgb(175, 95, 10) : ng ? Color.FromArgb(211, 35, 35) : Color.FromArgb(125, 139, 153);
            statusCell.Style.SelectionForeColor = Color.White;
            statusCell.ToolTipText = status;
            viewRow.Cells[colKind.Index].ToolTipText = status;
            viewRow.DefaultCellStyle.ForeColor = ng ? Color.FromArgb(185, 28, 28) : Color.FromArgb(23, 32, 46);
            viewRow.DefaultCellStyle.SelectionBackColor = Color.FromArgb(37, 99, 235);
            viewRow.DefaultCellStyle.SelectionForeColor = Color.White;
            viewRow.DefaultCellStyle.BackColor = ng ? Color.FromArgb(254, 242, 242) : pending ? Color.FromArgb(255, 251, 235) : Color.White;
        }
        private static string IssueLabel(VCutBendIssue issue)
        {
            switch (issue)
            {
                case VCutBendIssue.WrongBendTable: return "Wrong bend table";
                case VCutBendIssue.TableUnreadable: return "Bend table unavailable";
                case VCutBendIssue.MissingValue: return "Missing 残厚 value";
                case VCutBendIssue.InvalidValue: return "Invalid 残厚 value";
                case VCutBendIssue.MarkRequired: return "Mark unset (V溝1 / V溝2 / C溝)";
                case VCutBendIssue.MarkNotAllowed: return "Normal bend: clear mark";
                case VCutBendIssue.InvalidMark: return "Invalid mark";
                case VCutBendIssue.RemainingNotBelowThickness: return "残厚 must be below t";
                case VCutBendIssue.KindValuesDiffer: return "Same mark has different 残厚";
                case VCutBendIssue.VValuesEqual: return "V溝1 and V溝2 must have different 残厚";
                default: return "Check bend data";
            }
        }
        private void UpdateResponsiveColumns()
        {
            if (colType == null || gridBends.ClientSize.Width <= 0) return;
            int vScroll = gridBends.Controls.OfType<VScrollBar>().Any(v => v.Visible)
                ? SystemInformation.VerticalScrollBarWidth
                : 0;
            int width = Math.Max(200, gridBends.ClientSize.Width - vScroll - 2);
            colType.Visible = width >= 440;
            colFeature.MinimumWidth = 56;
            if (width < 310)
            {
                colNo.Width = 24;
                colValue.HeaderText = "残厚";
                colValue.Width = 54;
                colStatus.Width = 40;
                colKind.MinimumWidth = 64;
                colKind.Width = 68;
            }
            else
            {
                colNo.Width = 28;
                colValue.HeaderText = "V-Cut (残厚)";
                colValue.Width = width < 360 ? 80 : 90;
                colStatus.Width = 44;
                colKind.MinimumWidth = 70;
                colKind.Width = width < 360 ? 74 : 86;
            }
        }
        private void ApplyUnifiedGridStyle()
        {
            gridBends.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            gridBends.ColumnHeadersDefaultCellStyle.Font = LabelBoldFont;
            gridBends.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            gridBends.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
            gridBends.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(226, 232, 240);
            gridBends.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(15, 23, 42);
            gridBends.ColumnHeadersDefaultCellStyle.Padding = Padding.Empty;
            colNo.HeaderCell.Style.Padding = Padding.Empty;
            colFeature.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleLeft;
            colFeature.HeaderCell.Style.Padding = new Padding(8, 0, 0, 0);
            gridBends.DefaultCellStyle.Font = LabelBoldFont;
            gridBends.DefaultCellStyle.Padding = new Padding(4, 0, 4, 0);
            colNo.DefaultCellStyle.Padding = Padding.Empty;
            colNo.DefaultCellStyle.ForeColor = Color.FromArgb(71, 85, 105);
            colNo.DefaultCellStyle.SelectionForeColor = Color.FromArgb(219, 234, 254);
            colFeature.DefaultCellStyle.Font = ValueBoldFont;
            colFeature.DefaultCellStyle.SelectionForeColor = Color.White;
            colType.DefaultCellStyle.ForeColor = Color.FromArgb(71, 85, 105);
            colType.DefaultCellStyle.SelectionForeColor = Color.FromArgb(219, 234, 254);
            gridBends.DefaultCellStyle.SelectionBackColor = Color.FromArgb(37, 99, 235);
            gridBends.DefaultCellStyle.SelectionForeColor = Color.White;
            gridBends.GridColor = Color.FromArgb(226, 232, 240);
            colNo.DefaultCellStyle.Alignment = colStatus.DefaultCellStyle.Alignment = colValue.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            UpdateResponsiveColumns();
        }
        private static void EnableDoubleBuffer(Control control)
        {
            try
            {
                typeof(Control).InvokeMember(
                    "DoubleBuffered",
                    BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.SetProperty,
                    null,
                    control,
                    new object[] { true });
            }
            catch { }
        }

        private void GridBends_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex == -1 && e.ColumnIndex >= 0)
            {
                using (SolidBrush bg = new SolidBrush(Color.FromArgb(226, 232, 240)))
                {
                    e.Graphics.FillRectangle(bg, e.CellBounds);
                }
                using (Pen bottomPen = new Pen(Color.FromArgb(148, 163, 184), 2f))
                {
                    e.Graphics.DrawLine(bottomPen, e.CellBounds.Left, e.CellBounds.Bottom - 1, e.CellBounds.Right, e.CellBounds.Bottom - 1);
                }
                if (e.ColumnIndex > 0)
                {
                    using (Pen divPen = new Pen(Color.FromArgb(203, 213, 225), 1f))
                    {
                        e.Graphics.DrawLine(divPen, e.CellBounds.Left, e.CellBounds.Top + 7, e.CellBounds.Left, e.CellBounds.Bottom - 8);
                    }
                }
                string headerText = Convert.ToString(e.FormattedValue) ?? "";
                TextFormatFlags flags = e.ColumnIndex == colFeature.Index
                    ? (TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding)
                    : (TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                Rectangle textRect = e.ColumnIndex == colFeature.Index
                    ? new Rectangle(e.CellBounds.X + 8, e.CellBounds.Y, Math.Max(1, e.CellBounds.Width - 10), e.CellBounds.Height)
                    : e.CellBounds;
                TextRenderer.DrawText(e.Graphics, headerText, LabelBoldFont,
                    textRect, Color.FromArgb(15, 23, 42), flags);
                e.Handled = true;
                return;
            }

            if (e.RowIndex < 0) return;
            bool selected = (e.State & DataGridViewElementStates.Selected) != 0;

            if (e.ColumnIndex == colNo.Index)
            {
                e.PaintBackground(e.CellBounds, true);
                if (selected)
                {
                    using (SolidBrush barBrush = new SolidBrush(Color.FromArgb(30, 64, 175)))
                    {
                        e.Graphics.FillRectangle(barBrush, e.CellBounds.X, e.CellBounds.Y, 4, e.CellBounds.Height);
                    }
                }
                string noText = Convert.ToString(e.FormattedValue) ?? "";
                Color noColor = selected ? Color.FromArgb(219, 234, 254) : Color.FromArgb(71, 85, 105);
                TextRenderer.DrawText(e.Graphics, noText, LabelBoldFont, e.CellBounds, noColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                e.Paint(e.CellBounds, DataGridViewPaintParts.Border);
                e.Handled = true;
                return;
            }

            if (e.ColumnIndex == colValue.Index)
            {
                e.PaintBackground(e.CellBounds, true);
                string vText = Convert.ToString(e.FormattedValue) ?? "—";
                bool hasNum = vText != "—" && vText != "?";
                Color vColor = selected
                    ? (hasNum ? Color.White : Color.FromArgb(219, 234, 254))
                    : (hasNum ? Color.FromArgb(29, 78, 216) : Color.FromArgb(148, 163, 184));
                Font vFont = hasNum ? ValueBoldFont : LabelBoldFont;
                TextRenderer.DrawText(e.Graphics, vText, vFont, e.CellBounds, vColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                e.Paint(e.CellBounds, DataGridViewPaintParts.Border);
                e.Handled = true;
                return;
            }

            if (e.ColumnIndex == colKind.Index)
            {
                bool isEditingThisCell = gridBends.EditingControl != null &&
                    gridBends.CurrentCell != null &&
                    gridBends.CurrentCell.RowIndex == e.RowIndex &&
                    gridBends.CurrentCell.ColumnIndex == colKind.Index;
                if (!isEditingThisCell)
                {
                    e.PaintBackground(e.CellBounds, true);
                    var cell = gridBends.Rows[e.RowIndex].Cells[colKind.Index] as DataGridViewComboBoxCell;
                    bool editable = cell != null && !cell.ReadOnly;
                    string kText = Convert.ToString(e.FormattedValue) ?? Unassigned;
                    bool assigned = kText != Unassigned;
                    Color kColor = selected
                        ? Color.White
                        : (assigned ? Color.FromArgb(15, 23, 42) : Color.FromArgb(71, 85, 105));
                    Font kFont = assigned || selected ? ValueBoldFont : LabelBoldFont;

                    Rectangle textRect = editable
                        ? new Rectangle(e.CellBounds.X + 6, e.CellBounds.Y, Math.Max(1, e.CellBounds.Width - 24), e.CellBounds.Height)
                        : new Rectangle(e.CellBounds.X + 4, e.CellBounds.Y, Math.Max(1, e.CellBounds.Width - 8), e.CellBounds.Height);
                    TextRenderer.DrawText(e.Graphics, kText, kFont, textRect, kColor,
                        (editable ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter) |
                        TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

                    if (editable)
                    {
                        int cx = e.CellBounds.Right - 13;
                        int cy = e.CellBounds.Y + e.CellBounds.Height / 2;
                        Color arrowColor = selected ? Color.White : Color.FromArgb(71, 85, 105);
                        var oldMode = e.Graphics.SmoothingMode;
                        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                        using (Pen arrowPen = new Pen(arrowColor, 1.8f))
                        {
                            arrowPen.StartCap = LineCap.Round;
                            arrowPen.EndCap = LineCap.Round;
                            e.Graphics.DrawLines(arrowPen, new PointF[]
                            {
                                new PointF(cx - 3.5f, cy - 1.5f),
                                new PointF(cx, cy + 2f),
                                new PointF(cx + 3.5f, cy - 1.5f)
                            });
                        }
                        e.Graphics.SmoothingMode = oldMode;
                    }
                    e.Paint(e.CellBounds, DataGridViewPaintParts.Border);
                    e.Handled = true;
                    return;
                }
            }

            if (e.ColumnIndex == colFeature.Index && (colType == null || !colType.Visible))
            {
                e.PaintBackground(e.CellBounds, true);
                var row = gridBends.Rows[e.RowIndex].Tag as VCutPropertySetupCommand.BendRow;
                string name = Convert.ToString(e.Value) ?? "";
                string typeText = row != null ? Clean(row.FeatureType) : "";
                Color fore = selected
                    ? Color.White
                    : e.CellStyle.ForeColor;

                Rectangle textBounds = new Rectangle(e.CellBounds.X + 6, e.CellBounds.Y, Math.Max(1, e.CellBounds.Width - 10), e.CellBounds.Height);
                int nameW = TextRenderer.MeasureText(e.Graphics, name, ValueBoldFont, Size.Empty,
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;
                TextRenderer.DrawText(e.Graphics, name, ValueBoldFont, textBounds, fore,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

                if (!string.IsNullOrEmpty(typeText) && nameW + 58 < textBounds.Width)
                {
                    Rectangle subRect = new Rectangle(textBounds.X + nameW + 8, e.CellBounds.Y, textBounds.Width - nameW - 8, e.CellBounds.Height);
                    Color subColor = selected ? Color.FromArgb(219, 234, 254) : Color.FromArgb(100, 116, 139);
                    TextRenderer.DrawText(e.Graphics, typeText, LabelBoldFont, subRect, subColor,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                }
                e.Paint(e.CellBounds, DataGridViewPaintParts.Border);
                e.Handled = true;
                return;
            }

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
                g.SmoothingMode = SmoothingMode.AntiAlias;

                if (isOk)
                {
                    using (var brush = new SolidBrush(Color.FromArgb(236, 253, 245)))
                        g.FillEllipse(brush, badgeRect);
                    using (var pen = new Pen(Color.FromArgb(110, 231, 183), 1f))
                        g.DrawEllipse(pen, badgeRect);

                    using (var pen = new Pen(Color.FromArgb(5, 150, 105), 2.2f))
                    {
                        pen.StartCap = LineCap.Round;
                        pen.EndCap = LineCap.Round;
                        pen.LineJoin = LineJoin.Round;
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
                        pen.StartCap = LineCap.Round;
                        pen.EndCap = LineCap.Round;
                        float r = 3.6f;
                        g.DrawLine(pen, cx - r, cy - r, cx + r, cy + r);
                        g.DrawLine(pen, cx - r, cy + r, cx + r, cy - r);
                    }
                }
                else if (isPending)
                {
                    using (var brush = new SolidBrush(selected ? Color.White : Color.FromArgb(175, 95, 10)))
                        for (int offset = -5; offset <= 5; offset += 5)
                            g.FillEllipse(brush, cx + offset - 1.5f, cy - 1.5f, 3, 3);
                }
                else
                {
                    using (var pen = new Pen(selected ? Color.FromArgb(219, 234, 254) : Color.FromArgb(148, 163, 184), 2.0f))
                    {
                        pen.StartCap = LineCap.Round;
                        pen.EndCap = LineCap.Round;
                        g.DrawLine(pen, cx - 4f, cy, cx + 4f, cy);
                    }
                }

                g.SmoothingMode = oldSmoothing;
                e.Paint(e.CellBounds, DataGridViewPaintParts.Border);
                e.Handled = true;
            }
        }
    }
}
