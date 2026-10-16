using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands
{
    internal enum BendLiveState { Ready, Updating, Editing, WaitingForRebuild, Inactive, Unavailable }

    // All event callbacks merely mark work pending. Native geometry is read from
    // the UI timer after editing/rebuild finishes and the events have settled.
    internal sealed class BendLiveScheduler
    {
        private int observedStamp;
        private bool dirty, rollback;
        private DateTime quietSince;
        internal BendLiveState State { get; private set; } = BendLiveState.Ready;
        internal BendLiveScheduler(int stamp) { observedStamp = stamp; }
        internal void Signal(DateTime now) { dirty = true; quietSince = now; State = BendLiveState.Updating; }
        internal void Rebuilt(bool rolledBack, DateTime now) { rollback = rolledBack; Signal(now); }
        internal void Observe(bool active, bool sketch, bool needsRebuild, int stamp, DateTime now)
        {
            if (State == BendLiveState.Unavailable) return;
            if (!active) { if (State != BendLiveState.Inactive) Signal(now); State = BendLiveState.Inactive; return; }
            if (stamp != observedStamp) { observedStamp = stamp; Signal(now); }
            if (sketch) { if (State != BendLiveState.Editing) Signal(now); State = BendLiveState.Editing; return; }
            if (needsRebuild || rollback) { State = BendLiveState.WaitingForRebuild; return; }
            if (State == BendLiveState.Editing || State == BendLiveState.WaitingForRebuild || State == BendLiveState.Inactive) Signal(now);
            State = dirty ? BendLiveState.Updating : BendLiveState.Ready;
        }
        internal bool Due(DateTime now) => State == BendLiveState.Updating && dirty && (now - quietSince).TotalMilliseconds >= 600;
        internal void Complete(int stamp) { observedStamp = stamp; dirty = false; rollback = false; State = BendLiveState.Ready; }
        internal void Unavailable() { dirty = false; State = BendLiveState.Unavailable; }
    }

    internal sealed class BendMinimumLiveUpdate
    {
        internal List<VCutPropertySetupCommand.BendRow> Rows;
        internal Dictionary<string, string> Properties;
        internal string Title, Configuration, Notice;
    }

    internal sealed class BendMinimumLiveMonitor : IDisposable
    {
        private readonly VCutPropertySetupCommand command;
        private readonly ModelDoc2 model;
        private DPartDocEvents_Event events;
        private readonly BendLiveScheduler scheduler;
        private bool disposed, failed;
        private int failedStamp;
        private string configuration;
        private readonly Dictionary<string, List<VCutPropertySetupCommand.BendRow>> drafts = new Dictionary<string, List<VCutPropertySetupCommand.BendRow>>(StringComparer.OrdinalIgnoreCase);
        private readonly List<VCutPropertySetupCommand.BendRow> detached = new List<VCutPropertySetupCommand.BendRow>();
        internal int RefreshCount { get; private set; }
        internal BendLiveState State => scheduler.State;
        internal bool HasDetachedDraft => detached.Count > 0;
        internal bool HasUnshownDraft => detached.Count > 0 || drafts.Any(p => !string.Equals(p.Key, configuration, StringComparison.OrdinalIgnoreCase) && p.Value.Any(r => r.Kind != r.OriginalKind));
        internal string Error { get; private set; }
        internal string StateText => failed ? Error : State == BendLiveState.Editing ? "Đang sửa sketch…" :
            State == BendLiveState.WaitingForRebuild ? "Chờ rebuild hoàn tất…" : State == BendLiveState.Inactive ? "Đang mở tài liệu khác" :
            State == BendLiveState.Unavailable ? "Tài liệu đã đóng" : State == BendLiveState.Updating ? "Đang cập nhật kết quả…" : null;

        internal BendMinimumLiveMonitor(VCutPropertySetupCommand owner, ModelDoc2 part)
        {
            command = owner; model = part; configuration = owner.LoadedConfiguration;
            scheduler = new BendLiveScheduler(model.GetUpdateStamp());
            events = model as DPartDocEvents_Event;
            if (events == null) return; // Update-stamp observation still works.
            events.RegenNotify += OnChanging;
            events.RegenPostNotify2 += OnRebuilt;
            events.FeatureSketchEditPreNotify += OnSketchEdit;
            events.SketchSolveNotify += OnSketchSolve;
            events.DimensionChangeNotify += OnDimensionChange;
            events.UndoPostNotify += OnChanging;
            events.RedoPostNotify += OnChanging;
            events.ActiveConfigChangePostNotify += OnChanging;
            events.DestroyNotify2 += OnDestroyed;
        }
        internal void RequestRefresh() { OnChanging(); }
        private int OnChanging() { if (!disposed) { failed = false; Error = null; scheduler.Signal(DateTime.UtcNow); } return 0; }
        private int OnRebuilt(object stopFeature) { if (!disposed) { failed = false; Error = null; scheduler.Rebuilt(stopFeature != null, DateTime.UtcNow); } return 0; }
        private int OnSketchEdit(object feature, object definition) { return OnChanging(); }
        private int OnSketchSolve(string name) { return OnChanging(); }
        private int OnDimensionChange(object dimension) { return OnChanging(); }
        private int OnDestroyed(int kind)
        {
            if (kind == (int)swDestroyNotifyType_e.swDestroyNotifyDestroy) { scheduler.Unavailable(); Unhook(); }
            return 0;
        }
        private static string Key(VCutPropertySetupCommand.BendRow row) => !string.IsNullOrEmpty(row.PersistentId)
            ? row.PersistentId + "/" + row.FeatureType : "id:" + row.FeatureId + "/" + row.FeatureType;

        internal static List<VCutPropertySetupCommand.BendRow> MergeDrafts(
            IEnumerable<VCutPropertySetupCommand.BendRow> old, List<VCutPropertySetupCommand.BendRow> fresh)
        {
            var dirty = old.Where(r => r.Kind != r.OriginalKind).GroupBy(Key).ToDictionary(g => g.Key, g => g.Last());
            foreach (var row in fresh)
            {
                VCutPropertySetupCommand.BendRow prior;
                if (!dirty.TryGetValue(Key(row), out prior)) continue;
                // Without a persistent reference, do not transfer a draft to a
                // replacement feature merely because its integer ID matches.
                if (string.IsNullOrEmpty(row.PersistentId) && (row.Feature == null || !ReferenceEquals(row.Feature, prior.Feature))) continue;
                row.Kind = prior.Kind;
                if (row.OriginalKind != prior.OriginalKind)
                    row.DraftConflict = "Mark trong Part đã thay đổi từ bên ngoài. Hãy chọn lại Mark để xác nhận bản nháp.";
                else row.DraftConflict = prior.DraftConflict;
                dirty.Remove(Key(row));
            }
            return dirty.Values.ToList(); // Retained until Undo restores it or explicit Refresh discards it.
        }

        internal BendMinimumLiveUpdate Tick(List<VCutPropertySetupCommand.BendRow> current)
        {
            if (disposed || State == BendLiveState.Unavailable) return null;
            try
            {
                bool active = command.IsMinimumDocumentActive(model);
                int stamp = active ? model.GetUpdateStamp() : 0;
                if (failed && active && stamp != failedStamp) { failed = false; Error = null; scheduler.Signal(DateTime.UtcNow); }
                bool sketch = active && model.GetActiveSketch2() != null;
                bool needsRebuild = active && model.Extension.NeedsRebuild2 != 0;
                scheduler.Observe(active, sketch, needsRebuild, stamp, DateTime.UtcNow);
                if (State != BendLiveState.Ready) foreach (var row in current) row.MinimumContext?.Invalidate();
                if (failed || !scheduler.Due(DateTime.UtcNow)) return null;
                string nextConfig = model.ConfigurationManager.ActiveConfiguration.Name;
                drafts[configuration] = current.Where(r => r.Kind != r.OriginalKind).Concat(detached).ToList();
                List<VCutPropertySetupCommand.BendRow> rows; Dictionary<string, string> properties; string message;
                if (!command.Load(out rows, out properties, out message)) { failed = true; failedStamp = stamp; Error = message; return null; }
                // Loading is read-only. If a reentrant model event changed the
                // geometry, discard this capture and retry after it settles.
                if (model.GetUpdateStamp() != stamp || model.GetActiveSketch2() != null || model.Extension.NeedsRebuild2 != 0)
                { foreach (var row in rows) row.MinimumContext?.Invalidate(); scheduler.Signal(DateTime.UtcNow); return null; }
                var previousContext = current.Select(r => r.MinimumContext).FirstOrDefault(c => c != null);
                var context = rows.Select(r => r.MinimumContext).FirstOrDefault(c => c != null);
                if (context != null && previousContext != null)
                    context.SetSource(new BendToolCatalogReadResult(previousContext.Catalog, previousContext.SourceError));
                List<VCutPropertySetupCommand.BendRow> prior;
                if (!drafts.TryGetValue(nextConfig, out prior)) prior = new List<VCutPropertySetupCommand.BendRow>();
                detached.Clear(); detached.AddRange(MergeDrafts(prior, rows));
                configuration = nextConfig;
                scheduler.Complete(stamp); RefreshCount++;
                return new BendMinimumLiveUpdate { Rows = rows, Properties = properties, Title = command.LoadedPartTitle,
                    Configuration = nextConfig, Notice = detached.Count == 0 ? null : detached.Count + " Mark chưa lưu thuộc nếp đã xóa/thay thế. Undo để khôi phục hoặc Refresh để bỏ bản nháp đó." };
            }
            catch (Exception ex) { failed = true; try { failedStamp = model.GetUpdateStamp(); } catch { } Error = "Chưa cập nhật được: " + ex.Message; foreach (var row in current) row.MinimumContext?.Invalidate(); return null; }
        }
        private void Unhook()
        {
            var source = events; events = null; if (source == null) return;
            try {
                source.RegenNotify -= OnChanging; source.RegenPostNotify2 -= OnRebuilt;
                source.FeatureSketchEditPreNotify -= OnSketchEdit; source.SketchSolveNotify -= OnSketchSolve;
                source.DimensionChangeNotify -= OnDimensionChange; source.UndoPostNotify -= OnChanging;
                source.RedoPostNotify -= OnChanging; source.ActiveConfigChangePostNotify -= OnChanging; source.DestroyNotify2 -= OnDestroyed;
            } catch (System.Runtime.InteropServices.COMException) { } catch (System.Runtime.InteropServices.InvalidComObjectException) { }
        }
        public void Dispose() { if (disposed) return; disposed = true; Unhook(); }
    }
}
