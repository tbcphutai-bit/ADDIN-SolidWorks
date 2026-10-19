using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ADDIN.Commands;
using SolidWorks.Interop.sldworks;
using Timer = System.Windows.Forms.Timer;

namespace ADDIN.UI
{
    internal sealed class BendCheckForm : Form
    {
        private readonly BendCheckSession session;
        private BendAssemblyBatchForm batchForm;
        private BendAssemblyBatchController batchController;
        private readonly DataGridView parts;
        private readonly ComboBox scope;
        private readonly TextBox search;
        private readonly CheckBox ngOnly, expandBends;
        private readonly Label totals, state;
        private readonly ModernButton refresh, stop;
        private readonly Panel detailHost;
        private readonly SplitContainer split;
        private readonly Timer timer;
        private readonly BendToolCatalogProvider provider = new BendToolCatalogProvider();
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private readonly Stopwatch elapsed = new Stopwatch();
        private Task<BendToolCatalogReadResult> sourceRead;
        private BendToolCatalogReadResult source;
        private DateTime nextSource = DateTime.MinValue, quietSince = DateTime.MinValue;
        private BendMinimumDetailsForm detail;
        private BendCheckItem selectedItem;
        private readonly Dictionary<string, string> bendSelections = new Dictionary<string, string>();
        internal bool IsForActiveModel => session.IsHostActive;
        private string detailSignature, sourceSignature, tableSignature;
        private bool populating, scanning, capturing, pendingStart, stale, disposed, closingPending;
        private int nextItem, freshnessIndex, debounceStamp;

        internal BendCheckForm(ISldWorks app, ModelDoc2 model, Func<List<VCutPropertySetupCommand.BendRow>> sharedRows)
        {
            session = new BendCheckSession(app, model, sharedRows);
            Name = "BendCheckForm"; Text = "TAI TOOL — KIỂM TRA KÍCH THƯỚC CHẤN · " + model.GetTitle(); Font = new Font("Segoe UI", 10F);
            AutoScaleMode = AutoScaleMode.Dpi; BackColor = TaiTheme.Page; Size = new Size(1300, 920); MinimumSize = new Size(1020, 720);
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Padding = new Padding(12, 8, 12, 8) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            var title = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 1, ColumnCount = 3, Margin = Padding.Empty };
            title.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
            title.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
            title.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            title.Controls.Add(new Label { Text = "KIỂM TRA KÍCH THƯỚC CHẤN", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 13F, FontStyle.Bold), ForeColor = TaiTheme.Accent }, 0, 0);
            {
                var btnBatchUI = new ModernButton { Text = "Check chấn", Dock = DockStyle.Fill, Kind = ModernButtonKind.Subtle, Tone = ModernButtonTone.Accent, Margin = new Padding(4, 4, 4, 4) };
                btnBatchUI.Click += (s, e) => {
                    if (batchForm != null && !batchForm.IsDisposed) { batchForm.Activate(); return; }
                    if (capturing) return;
                    batchForm = new BendAssemblyBatchForm(model.GetTitle(), model.ConfigurationManager?.ActiveConfiguration?.Name);
                    batchController = new BendAssemblyBatchController(batchForm, new BendAssemblyBatchLiveBackend(app, model));
                    // Both windows use the same SOLIDWORKS STA. Pause the Min
                    // timer while Batch owns native capture; resume its normal
                    // freshness checks after the child closes.
                    timer.Stop();
                    batchForm.FormClosed += (sender, args) => { batchForm = null; batchController = null; if (!disposed) timer.Start(); };
                    batchForm.Show(this);
                };
                title.Controls.Add(btnBatchUI, 1, 0);
            }
            totals = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, ForeColor = TaiTheme.Text, Font = new Font("Segoe UI", 10F, FontStyle.Bold) }; title.Controls.Add(totals, 2, 0); root.Controls.Add(title, 0, 0);
            var toolbar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 7, RowCount = 1, Margin = Padding.Empty };
            foreach (int width in new[] { 170, 0, 135, 175, 96, 76, 76 }) toolbar.ColumnStyles.Add(new ColumnStyle(width == 0 ? SizeType.Percent : SizeType.Absolute, width == 0 ? 100 : width));
            scope = new ComboBox { Name = "bendCheckScope", Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, Enabled = session.IsAssembly, Font = new Font("Segoe UI", 10F) };
            scope.Items.AddRange(session.IsAssembly ? new object[] { "Chi tiết đang chọn", "Toàn bộ cụm (Assembly)" } : new object[] { "Part hiện tại" });
            scope.SelectedIndex = session.IsAssembly && !session.HasSelection ? 1 : 0;
            search = new TextBox { Name = "bendCheckSearch", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10F) }; search.TextChanged += (s, e) => Populate();
            ngOnly = new CheckBox { Text = "Chỉ hiện lỗi (NG)", AutoSize = true, Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10F) }; ngOnly.CheckedChanged += (s, e) => Populate();
            expandBends = new CheckBox { Text = "Hiện chi tiết nếp chấn", Checked = true, AutoSize = true, Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10F) };
            expandBends.CheckedChanged += (s, e) => { split.Panel2Collapsed = !expandBends.Checked; if (expandBends.Checked) ShowPart(true); else ClearDetail(); };
            refresh = new ModernButton { Text = "Quét lại", Dock = DockStyle.Fill, Kind = ModernButtonKind.Standard, Tone = ModernButtonTone.Accent, Font = new Font("Segoe UI", 10F, FontStyle.Bold) };
            stop = new ModernButton { Text = "Dừng", Dock = DockStyle.Fill, Kind = ModernButtonKind.Standard, Enabled = false, Font = new Font("Segoe UI", 10F) };
            var close = new ModernButton { Text = "Đóng", Dock = DockStyle.Fill, Kind = ModernButtonKind.Standard, Font = new Font("Segoe UI", 10F) }; close.Click += (s, e) => Close();
            toolbar.Controls.Add(scope, 0, 0); toolbar.Controls.Add(search, 1, 0); toolbar.Controls.Add(ngOnly, 2, 0); toolbar.Controls.Add(expandBends, 3, 0);
            toolbar.Controls.Add(refresh, 4, 0); toolbar.Controls.Add(stop, 5, 0); toolbar.Controls.Add(close, 6, 0); root.Controls.Add(toolbar, 0, 1);
            var tip = new ToolTip(); componentsTip = tip; tip.SetToolTip(search, "Tìm Part / configuration"); tip.SetToolTip(scope, "Component đã chọn; chọn subassembly sẽ gồm các Part bên trong.");
            split = new SplitContainer { Name = "bendCheckSplit", Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterWidth = 8,
                Size = new Size(1240, 750), SplitterDistance = 200, Panel1MinSize = 70, Panel2MinSize = 350, BackColor = TaiTheme.Page };
            parts = BendMinimumDetailsForm.CreateGrid("bendCheckParts");
            BendMinimumDetailsForm.AddColumn(parts, "part", "図番 (Part)", 270, 190, true);
            BendMinimumDetailsForm.AddColumn(parts, "config", "コンフィグ (Config)", 150, 110, true);
            BendMinimumDetailsForm.AddColumn(parts, "qty", "数量", 55, 45, false);
            BendMinimumDetailsForm.AddColumn(parts, "material", "材質", 110, 85, true);
            BendMinimumDetailsForm.AddColumn(parts, "t", "板厚 t (mm)", 95, 80, false);
            BendMinimumDetailsForm.AddColumn(parts, "bends", "曲げ数", 75, 65, false);
            BendMinimumDetailsForm.AddColumn(parts, "min", "Check Min", 120, 100, false);
            BendMinimumDetailsForm.AddColumn(parts, "max", "Check Max", 85, 75, false);
            parts.Columns[5].ToolTipText = "曲げ数: Tổng số đường chấn (nếp uốn) trên chi tiết";
            parts.Columns[6].ToolTipText = "Check Min: Kiểm tra kích thước bẻ tối thiểu theo dao cối";
            parts.Columns[7].ToolTipText = "Check Max: Kiểm tra kích thước chấn tối đa (chưa áp dụng)";
            parts.CurrentCellChanged += (s, e) => { if (!populating) ShowPart(true); };
            parts.RowHeightChanged += (s, e) => FitSinglePartTable();
            split.Panel1.Controls.Add(parts); detailHost = new Panel { Dock = DockStyle.Fill, BackColor = TaiTheme.Surface }; split.Panel2.Controls.Add(detailHost);
            root.Controls.Add(split, 0, 2); state = new Label { Dock = DockStyle.Fill, ForeColor = TaiTheme.TextMuted, Font = new Font("Segoe UI", 9.5F), TextAlign = ContentAlignment.MiddleLeft }; root.Controls.Add(state, 0, 3); Controls.Add(root);
            refresh.Click += (s, e) => RequestScan();
            stop.Click += (s, e) => { scanning = pendingStart = false; stop.Enabled = false; refresh.Enabled = true; state.Text = "Đã dừng · Các dòng Chờ chưa được kiểm tra"; Populate(); };
            scope.SelectionChangeCommitted += (s, e) => { if (capturing) return; if (scope.SelectedIndex == 0) { session.CaptureSelection(); if (!session.HasSelection) scope.SelectedIndex = 1; } RequestScan(); };
            timer = new Timer { Interval = 150 }; timer.Tick += (s, e) => Tick();
            Shown += (s, e) => { RequestScan(); timer.Start(); };
        }
        private readonly ToolTip componentsTip;
        private void ClearDetail()
        {
            if (detail != null && selectedItem != null && !string.IsNullOrEmpty(detail.SelectedBendIdentity))
                bendSelections[selectedItem.Key] = detail.SelectedBendIdentity;
            detailSignature = null; if (detail != null) { detail.Dispose(); detail = null; } detailHost.Controls.Clear();
        }
        private void RequestScan()
        {
            if (capturing || disposed) return;
            ClearDetail(); selectedItem = null; scanning = pendingStart = false;
            try
            {
                session.BuildInventory(session.IsAssembly && scope.SelectedIndex == 0);
                stale = false; quietSince = DateTime.MinValue; nextItem = freshnessIndex = 0;
                // One source snapshot for the entire run; no per-component Excel I/O.
                pendingStart = true; sourceRead = sourceRead ?? Task.Run(() => provider.ReadForCheck(cancellation.Token));
                state.Text = "Đọc bảng tra…"; refresh.Enabled = false; stop.Enabled = true; scope.Enabled = expandBends.Enabled = false;
                split.SplitterDistance = Math.Max(split.Panel1MinSize, Math.Min(session.Items.Count == 1 ? 85 : 200, split.Height - split.Panel2MinSize - split.SplitterWidth));
                elapsed.Restart(); Populate();
            }
            catch (Exception ex) { state.Text = ex.Message; refresh.Enabled = true; stop.Enabled = false; Populate(); }
        }
        private void Tick()
        {
            if (disposed || capturing) return;
            try
            {
                if (sourceRead != null && sourceRead.IsCompleted)
                {
                    var task = sourceRead; sourceRead = null;
                    if (!task.IsCanceled)
                    {
                        source = task.IsFaulted ? new BendToolCatalogReadResult(null, task.Exception.GetBaseException().Message) : task.Result;
                        string signature = source.Snapshot?.SourceSha256 + "|" + source.Snapshot?.SourcePath + "|" + source.Error;
                        if (signature != sourceSignature)
                        {
                            sourceSignature = signature; foreach (var item in session.Items) item.Context?.SetSource(source);
                            detailSignature = null; Populate();
                        }
                    }
                    nextSource = DateTime.UtcNow.AddSeconds(2);
                    if (pendingStart) { pendingStart = false; scanning = true; }
                }
                if (!session.IsHostActive || session.IsEditing || !session.CheckHostStamp())
                {
                    if (!stale) { session.Invalidate(); stale = true; ClearDetail(); Populate(); }
                    scanning = pendingStart = false;
                    if (!session.IsHostActive) { quietSince = DateTime.MinValue; state.Text = "Quay lại model / configuration để cập nhật"; }
                    else if (session.IsEditing) { quietSince = DateTime.MinValue; state.Text = "Chờ thoát sketch / rebuild"; }
                    else {
                        int stamp = session.Host.GetUpdateStamp();
                        if (quietSince == DateTime.MinValue || stamp != debounceStamp) { debounceStamp = stamp; quietSince = DateTime.UtcNow; }
                        state.Text = "Chờ model ổn định…";
                    }
                    refresh.Enabled = true; stop.Enabled = false;
                }
                // A stale host stamp remains unequal until the next inventory.
                // Debounce only while the document is active and rebuilt.
                if (stale && session.IsHostActive && !session.IsEditing)
                {
                    if (quietSince == DateTime.MinValue) quietSince = DateTime.UtcNow;
                    if ((DateTime.UtcNow - quietSince).TotalMilliseconds >= 650) { RequestScan(); return; }
                }
                if (scanning)
                {
                    if (nextItem < session.Items.Count)
                    {
                        capturing = true; try { session.Capture(session.Items[nextItem++], source); } finally { capturing = false; }
                        state.Text = nextItem + " / " + session.Items.Count + " Part · " + elapsed.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s";
                        Populate();
                    }
                    if (nextItem >= session.Items.Count)
                    {
                        scanning = false; elapsed.Stop(); refresh.Enabled = true; stop.Enabled = false;
                        state.Text = session.Items.Count == 0 ? "Không có Part trong phạm vi" : session.Items.Count + " Part · " + session.Items.Sum(i => i.Quantity) + " component · " + elapsed.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s";
                        if (session.SuppressedCount > 0) state.Text += " · " + session.SuppressedCount + " suppressed bỏ qua";
                        Populate();
                    }
                }
                else if (!stale && !pendingStart && session.Items.Count > 0)
                {
                    var item = session.Items[freshnessIndex++ % session.Items.Count];
                    if (!session.CheckItem(item))
                    { session.Invalidate(); stale = true; quietSince = DateTime.UtcNow; ClearDetail(); Populate(); }
                    else { UpdatePartValues(); ShowPart(false); }
                }
                if (!scanning && !pendingStart && sourceRead == null && DateTime.UtcNow >= nextSource)
                    sourceRead = Task.Run(() => provider.ReadForCheck(cancellation.Token));
            }
            catch (Exception ex) { scanning = pendingStart = false; state.Text = ex.Message; refresh.Enabled = true; stop.Enabled = false; }
            finally
            {
                scope.Enabled = session.IsAssembly && !scanning && !pendingStart && !capturing;
                expandBends.Enabled = !scanning && !pendingStart && !capturing;
                if (closingPending && !capturing) { closingPending = false; BeginInvoke(new Action(Close)); }
            }
        }
        private void Populate()
        {
            if (disposed) return; string key = (parts.CurrentRow?.Tag as BendCheckItem)?.Key;
            var visible = session.Items.Where(i => (string.IsNullOrWhiteSpace(search.Text) || (i.Name + " " + i.Configuration).IndexOf(search.Text.Trim(), StringComparison.CurrentCultureIgnoreCase) >= 0) &&
                (!ngOnly.Checked || i.Done && i.Current && i.Error == null && i.Summary.Status == BendMinimumStatus.BelowMinimum)).ToList();
            populating = true;
            try
            {
                while (parts.Rows.Count > visible.Count) parts.Rows.RemoveAt(parts.Rows.Count - 1);
                while (parts.Rows.Count < visible.Count) parts.Rows.Add();
                for (int i = 0; i < visible.Count; i++) parts.Rows[i].Tag = visible[i];
                UpdatePartValues();
                var selected = parts.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => ((BendCheckItem)r.Tag).Key == key) ?? parts.Rows.Cast<DataGridViewRow>().FirstOrDefault();
                if (selected != null && parts.CurrentRow != selected) parts.CurrentCell = selected.Cells[0];
            }
            finally { populating = false; }
            ShowPart(false);
        }
        private void UpdatePartValues()
        {
            string signature = string.Join("|", session.Items.Select(i => i.Key + ":" + i.Done + ":" + i.Current + ":" + i.Error + ":" +
                i.Context?.Current + ":" + i.Context?.Catalog?.SourceSha256 + ":" + i.Context?.SourceError + ":" + string.Join(",", i.Rows.Select(r => r.Kind)))) +
                "#" + string.Join("|", parts.Rows.Cast<DataGridViewRow>().Select(r => ((BendCheckItem)r.Tag).Key));
            if (signature == tableSignature) return; tableSignature = signature;
            foreach (DataGridViewRow row in parts.Rows)
            {
                var i = (BendCheckItem)row.Tag;
                row.SetValues(i.Name, i.Configuration, i.Quantity, i.Material ?? "—", i.ThicknessMm?.ToString("0.###", CultureInfo.InvariantCulture) ?? "—", i.Done ? i.Rows.Count.ToString() : "—", i.MinimumText, "—");
                row.Cells[6].Style.ForeColor = i.MinimumText == "NG" ? TaiTheme.Danger : i.MinimumText == "Đạt" ? TaiTheme.Success : TaiTheme.Warning;
                row.Cells[6].Style.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
                row.Cells[6].ToolTipText = i.Error ?? i.Summary.Counts; row.Cells[0].ToolTipText = i.Path;
            }
            int pass = session.Items.Count(i => i.MinimumText == "Đạt"), ng = session.Items.Count(i => i.MinimumText == "NG");
            int pending = session.Items.Count - pass - ng;
            totals.Text = "Check Min: Đạt " + pass + " · Lỗi NG " + ng + (pending > 0 ? " · Chờ " + pending : "") + "    |    Check Max: —";
            FitSinglePartTable();
        }
        private void FitSinglePartTable()
        {
            if (split == null || parts == null || session.Items.Count != 1 || parts.Rows.Count != 1 || split.Panel2Collapsed) return;
            int height = parts.ColumnHeadersHeight + parts.Rows[0].Height + SystemInformation.HorizontalScrollBarHeight + 6;
            int limit = split.Height - split.Panel2MinSize - split.SplitterWidth;
            if (limit < split.Panel1MinSize) return;
            int desired = Math.Max(split.Panel1MinSize, Math.Min(height, limit));
            if (split.SplitterDistance != desired) split.SplitterDistance = desired;
        }
        private void ShowPart(bool locateComponent)
        {
            if (!expandBends.Checked || disposed || capturing) return;
            var item = parts.CurrentRow?.Tag as BendCheckItem;
            if (locateComponent) session.SelectRepresentative(item);
            string signature = item?.Key + "|" + item?.MinimumText + "|" + item?.Context?.Catalog?.SourceSha256 + "|" +
                (item == null ? "" : string.Join(",", item.Rows.Select(r => r.Kind + ":" + r.MinimumContext?.Evaluate(r).Summary)));
            if (signature == detailSignature) return;
            detailSignature = signature;
            if (item != selectedItem) { ClearDetail(); selectedItem = item; detailSignature = signature; }
            if (item == null || !item.Done || !item.Current || item.Error != null)
            {
                ClearDetail(); detailSignature = signature;
                detailHost.Controls.Add(new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = TaiTheme.TextMuted,
                    Text = item == null ? "Chọn Part" : !item.Done ? "Chờ kiểm tra" : item.Error ?? "Chờ cập nhật" }); return;
            }
            if (detail == null)
            {
                detailHost.Controls.Clear(); detail = new BendMinimumDetailsForm(null, null, true);
                string prior; if (bendSelections.TryGetValue(item.Key, out prior)) detail.PreferSelection(prior);
                detailHost.Controls.Add(detail); detail.Show();
            }
            var summary = item.Summary; detail.UpdateResults(item.Rows, item.Name, summary.Text, summary.Counts, item.Error, item.Current);
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        { if (capturing) { e.Cancel = true; closingPending = true; return; } base.OnFormClosing(e); }
        protected override void Dispose(bool disposing)
        {
            if (disposing && !disposed)
            {
                disposed = true; timer?.Stop(); timer?.Dispose(); cancellation.Cancel(); ClearDetail(); session.Dispose(); componentsTip?.Dispose();
                // Observe a cancellation/fault even when the window closes during an Excel read.
                sourceRead?.ContinueWith(t => { var ignored = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted); cancellation.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
