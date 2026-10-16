using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ADDIN.Commands;

namespace ADDIN.UI
{
    public partial class VCutBendAssignmentControl
    {
        private Label lblMinimumResult;
        private Panel panelMinimumResult;
        private ModernButton btnMinimumDetails;
        private BendMinimumDetailsForm minimumDetails;
        private System.Windows.Forms.Timer minimumTimer;
        private BendMinimumContext minimumContext;
        private BendMinimumLiveMonitor minimumLive;
        private BendToolCatalogProvider minimumProvider;
        private CancellationTokenSource minimumCancellation;
        private Task<BendToolCatalogReadResult> minimumSourceRead;
        private DateTime nextMinimumSourceRead;
        private bool applyingLiveRefresh, minimumTickRunning;
        private string liveNotice, lastLiveState;
        internal event Action<VCutPropertySetupCommand.BendRow> MinimumSelected;
        internal bool MinimumModelReady => minimumLive == null || minimumLive.State == BendLiveState.Ready && minimumLive.Error == null && !minimumLive.HasDetachedDraft;

        private void InitializeMinimum()
        {
            panelMinimumResult = new Panel { Name = "panelMinimumResult", Dock = DockStyle.Fill, Margin = new Padding(0, 2, 0, 2), BorderStyle = BorderStyle.FixedSingle };
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 126));
            lblMinimumResult = new Label { Name = "lblMinimumResult", Dock = DockStyle.Fill, AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(7, 0, 0, 0), Font = LabelBoldFont };
            btnMinimumDetails = new ModernButton { Name = "btnMinimumDetails", Text = "Xem chi tiết", Dock = DockStyle.Fill,
                Kind = ModernButtonKind.Standard, Icon = TaiIcon.BendFlat, Margin = new Padding(4, 6, 6, 6),
                AccessibleName = "Xem chi tiết kiểm tra bẻ tối thiểu" };
            btnMinimumDetails.Click += (s, e) => OpenMinimumDetails();
            layout.Controls.Add(lblMinimumResult, 0, 0); layout.Controls.Add(btnMinimumDetails, 1, 0); panelMinimumResult.Controls.Add(layout);
            layoutDetails.RowCount = 3; layoutDetails.RowStyles.Clear();
            layoutDetails.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layoutDetails.RowStyles.Add(new RowStyle(SizeType.Absolute, 54)); layoutDetails.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            layoutDetails.SetRow(lblSummary, 2); layoutDetails.SetRow(lblStatus, 2);
            layoutDetails.Controls.Add(panelMinimumResult, 0, 1); layoutDetails.SetColumnSpan(panelMinimumResult, 2);
            minimumTimer = new System.Windows.Forms.Timer(components) { Interval = 500 };
            minimumTimer.Tick += (s, e) => MinimumTick();
            VisibleChanged += (s, e) => {
                if (!Visible || nativeReleased) { minimumTimer.Stop(); CloseMinimumDetails(); minimumLive?.Dispose(); minimumLive = null; return; }
                EnsureMinimumLive(); minimumTimer.Start(); MinimumTick();
            };
        }
        private void BindMinimum()
        {
            if (applyingLiveRefresh) { minimumContext = Rows?.Select(r => r.MinimumContext).FirstOrDefault(c => c != null); return; }
            ReleaseMinimum(); minimumContext = Rows?.Select(r => r.MinimumContext).FirstOrDefault(c => c != null);
            minimumProvider = new BendToolCatalogProvider(); minimumCancellation = new CancellationTokenSource();
            liveNotice = lastLiveState = null; nextMinimumSourceRead = DateTime.MinValue;
            EnsureMinimumLive(); BeginMinimumSourceRead(); if (Visible && !nativeReleased) minimumTimer.Start();
        }
        private void EnsureMinimumLive()
        {
            if (minimumLive != null || minimumContext?.LiveFactory == null || !Visible || nativeReleased) return;
            try { minimumLive = minimumContext.LiveFactory(); if (!minimumContext.CheckCurrent()) minimumLive.RequestRefresh(); }
            catch (Exception ex) { liveNotice = "Chưa theo dõi được thay đổi model: " + ex.Message; }
        }
        private void CloseMinimumDetails()
        {
            var window = minimumDetails; minimumDetails = null; if (window != null && !window.IsDisposed) window.Close();
        }
        private void ReleaseMinimum()
        {
            minimumTimer?.Stop(); CloseMinimumDetails(); minimumLive?.Dispose(); minimumLive = null;
            minimumCancellation?.Cancel(); minimumCancellation?.Dispose(); minimumCancellation = null;
            if (minimumSourceRead != null) minimumSourceRead.ContinueWith(t => { var ignored = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            minimumSourceRead = null; minimumContext = null;
        }
        private void BeginMinimumSourceRead()
        {
            if (minimumContext == null || minimumSourceRead != null || minimumCancellation == null) return;
            var token = minimumCancellation.Token; var provider = minimumProvider;
            minimumSourceRead = Task.Run(() => provider.ReadForCheck(token), token);
        }
        private void MinimumTick()
        {
            if (minimumTickRunning || nativeReleased || IsDisposed || Disposing || !Visible || minimumContext == null) return;
            minimumTickRunning = true;
            try {
                EnsureMinimumLive(); bool changed = false; var update = minimumLive?.Tick(Rows);
                if (update != null) {
                    CommitEditor(); var prior = gridBends.CurrentRow?.Tag as VCutPropertySetupCommand.BendRow;
                    var selected = update.Rows.FirstOrDefault(r => prior != null && (!string.IsNullOrEmpty(prior.PersistentId) ? r.PersistentId == prior.PersistentId : r.FeatureId == prior.FeatureId));
                    liveNotice = update.Notice;
                    // The native page holds this same List. Keep it synchronized.
                    var shared = Rows; shared.Clear(); shared.AddRange(update.Rows); applyingLiveRefresh = true;
                    try {
                        var sharedProperties = properties;
                        sharedProperties.Clear(); foreach (var pair in update.Properties) sharedProperties[pair.Key] = pair.Value;
                        LoadRows(shared, sharedProperties, update.Title, update.Configuration, selected?.FeatureName, true);
                    }
                    finally { applyingLiveRefresh = false; }
                    DraftChanged?.Invoke(this, EventArgs.Empty); changed = true;
                }
                if (minimumLive == null) { bool was = minimumContext.Current; minimumContext.CheckCurrent(); changed |= was != minimumContext.Current; }
                string state = minimumLive?.StateText;
                if (state != lastLiveState) { lastLiveState = state; changed = true; UpdateSaveState(); DraftChanged?.Invoke(this, EventArgs.Empty); }
                if (minimumSourceRead != null && minimumSourceRead.IsCompleted) {
                    try { changed |= minimumContext.SetSource(minimumSourceRead.GetAwaiter().GetResult()); }
                    catch (Exception ex) { changed |= minimumContext.SetSource(new BendToolCatalogReadResult(null, ex.Message)); }
                    minimumSourceRead = null; nextMinimumSourceRead = DateTime.UtcNow.AddSeconds(5);
                }
                if (minimumSourceRead == null && DateTime.UtcNow >= nextMinimumSourceRead) BeginMinimumSourceRead();
                // An unchanged read does not touch the grid, filter, or selection.
                if (changed) {
                    UpdateMinimumRows();
                    if (minimumLive == null || minimumLive.State == BendLiveState.Ready) ApplyFilters();
                }
            }
            finally { minimumTickRunning = false; }
        }
        private BendMinimumRowResult MinimumResult(VCutPropertySetupCommand.BendRow row) => row?.MinimumContext?.Evaluate(row) ?? new BendMinimumRowResult {
            Status = BendMinimumStatus.InsufficientData, Summary = "? Chưa đọc", Details = "Chưa đọc dữ liệu tối thiểu."
        };
        private bool MinimumBelow(VCutPropertySetupCommand.BendRow row) => MinimumResult(row).Status == BendMinimumStatus.BelowMinimum;
        private void UpdateMinimumRows()
        {
            minimumContext?.TraceRows(Rows);
            var summary = BendMinimumSummary.Create(Rows); bool pending = minimumLive != null && minimumLive.State != BendLiveState.Ready;
            string title = pending ? minimumLive.StateText : summary.Text;
            if (!string.IsNullOrEmpty(minimumLive?.Error)) title = "? Chưa đủ dữ liệu";
            bool unknown = pending || summary.Status == BendMinimumStatus.InsufficientData;
            lblMinimumResult.ForeColor = unknown ? Color.FromArgb(146, 64, 14) : summary.Status == BendMinimumStatus.BelowMinimum ? Color.FromArgb(185, 28, 28) : Color.FromArgb(4, 120, 87);
            panelMinimumResult.BackColor = unknown ? Color.FromArgb(255, 251, 235) : summary.Status == BendMinimumStatus.BelowMinimum ? Color.FromArgb(254, 242, 242) : Color.FromArgb(236, 253, 245);
            string text = title + "\r\n" + (pending ? "Kiểm tra bẻ tối thiểu" : summary.Counts);
            if (lblMinimumResult.Text != text) lblMinimumResult.Text = text;
            toolTip.SetToolTip(lblMinimumResult, minimumLive?.Error ?? liveNotice ?? "Kết quả kích thước bẻ tối thiểu của toàn bộ chi tiết.");
            btnMinimumDetails.Enabled = Rows != null && Rows.Count > 0;
            if (minimumDetails != null && !minimumDetails.IsDisposed) minimumDetails.UpdateResults(Rows, headerPartText, title, summary.Counts, minimumLive?.Error ?? liveNotice, !pending);
        }
        private void ShowMinimumDetail(VCutPropertySetupCommand.BendRow row)
        { layoutRoot.RowStyles[3].Height = string.IsNullOrEmpty(detailIssueText) ? 142F : 166F; }
        internal void OpenMinimumDetails()
        {
            if (nativeReleased || Rows == null) return;
            if (minimumDetails != null && !minimumDetails.IsDisposed) { minimumDetails.Activate(); return; }
            minimumDetails = new BendMinimumDetailsForm(row => { if (MinimumModelReady) BendSelected?.Invoke(row); }, row => { if (MinimumModelReady) MinimumSelected?.Invoke(row); });
            minimumDetails.FormClosed += (s, e) => minimumDetails = null;
            UpdateMinimumRows(); minimumDetails.Show(this);
        }
    }
}
