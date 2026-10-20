using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADDIN.Commands.BendAssembly;
using ADDIN.Commands.BendPlacement;
using ADDIN.Commands.BendTools;

namespace ADDIN.Commands
{
    internal sealed partial class BendCheckSession
    {
        private PartNativeBatchOperation partOperation;
        private static IReadOnlyList<AssemblyBendMetadata> ReadMinimumMetadata(BendCheckItem item)
        {
            var output = new List<AssemblyBendMetadata>();
            if (!item.Current || item.Context?.Current != true) return output.AsReadOnly();
            foreach (var row in item.Rows)
            {
                var check = item.Context.Evaluate(row);
                output.Add(new AssemblyBendMetadata(row.FeatureId, row.PersistentId, row.FeatureName, row.BendAngleDeg, row.BendRadiusMm,
                    check.Status.ToString(), check.Summary, check.Details, check.Sides.Where(s => s.Measure != null).Select(s =>
                        new AssemblyBendMinimumSide(s.Measure.Shape, s.Measure.Dimension, s.Measure.ValueMm, s.LimitMm))));
            }
            return output.AsReadOnly();
        }
        // Same managed coordinator and same native capture as Assembly. No
        // temporary Assembly, configuration switch, or edited original Part.
        internal PartNativeBatchOperation BeginPartBatch(BendToolCatalogReadResult minimum, CancellationToken token,
            IProgress<AssemblyBatchProgress> progress)
        {
            RequireAssemblySta();
            if (IsAssembly) throw new InvalidOperationException("Part batch requires a Part");
            if (Items.Count != 1 || !CheckHostStamp() || !Items[0].Current || !CheckItem(Items[0])) BuildInventory(false);
            partOperation?.Invalidate();
            return partOperation = new PartNativeBatchOperation(this, minimum, token, progress);
        }
        internal bool LocateBatchBend(AssemblyBatchSnapshot snapshot, string source, int feature)
        {
            RequireAssemblySta();
            if (snapshot?.Current != true || !IsHostActive || IsEditing) return false;
            var result = snapshot.Parts.SingleOrDefault(p => p.OccurrenceSetKey == source);
            if (result == null)
            { var legacy = snapshot.Parts.Where(p => p.SourceKey == source).ToArray(); if (legacy.Length == 1) result = legacy[0]; }
            if (result?.Current != true || result.Reason != null) return false;
            var item = Items.FirstOrDefault(i => i.Current && i.PlacementCapture != null &&
                (IsAssembly ? i.AssemblyOccurrences.Any(o => result.Occurrences.Contains(o) && AssemblyOccurrenceEligibility.Reason(o) == null) : true));
            if (item == null || !CheckItem(item) || item.Context?.CheckCurrent() != true || item.Copy != null) return false;
            var row = item.Rows.SingleOrDefault(r => r.FeatureId == feature);
            if (row == null || string.IsNullOrWhiteSpace(row.PersistentId)) return false;
            int error;
            var resolved = item.Model.Extension.GetObjectByPersistReference3(Convert.FromBase64String(row.PersistentId), out error);
            if (error != 0 || resolved == null || app.IsSame(resolved, row.Feature) != (int)SolidWorks.Interop.swconst.swObjectEquality.swObjectSame) return false;
            using (var inspection = item.Context.InspectionFactory?.Invoke()) return inspection?.Select(row, -2) == true;
        }
        internal sealed class PartNativeBatchOperation
        {
            private readonly BendCheckSession owner;
            private readonly BendToolCatalogReadResult minimum;
            private readonly CancellationToken token;
            private readonly IProgress<AssemblyBatchProgress> progress;
            private readonly PlacementLease lease = new PlacementLease();
            private readonly Stopwatch clock = Stopwatch.StartNew();
            private readonly TaskCompletionSource<AssemblyBatchSnapshot> completion = new TaskCompletionSource<AssemblyBatchSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly string[] paths;
            private Task<Dictionary<string, string>> files;
            private Dictionary<string, string> revisions;
            private Task<ToolProfileLibrarySnapshot> tools;
            private Task processVerification;
            private Task<AssemblyProgramData> program;
            private PlacementModelCapture model;
            private bool reused;
            private Task<AssemblyBatchSnapshot> work;
            private bool captured, publication;
            private double captureMs;
            private DateTime nextAudit = DateTime.MinValue;
            internal Task<AssemblyBatchSnapshot> Completion => completion.Task;
            internal PartNativeBatchOperation(BendCheckSession owner, BendToolCatalogReadResult minimum, CancellationToken token, IProgress<AssemblyBatchProgress> progress)
            {
                this.owner = owner; this.minimum = minimum; this.token = token; this.progress = progress;
                paths = new[] { owner.Host.GetPathName(), minimum?.Snapshot?.SourcePath }.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                files = AssemblyNativeBatchOperation.ReadFiles(paths);
            }
            internal void Cancel() { lease.Invalidate(); owner.assemblyCoordinator.Cancel(); }
            internal void Invalidate() => lease.Invalidate();
            internal void Pump()
            {
                owner.RequireAssemblySta();
                try
                {
                    owner.toolLibrary.Poll();
                    owner.processPrograms.Poll();
                    if (token.IsCancellationRequested || !owner.IsHostActive || owner.IsEditing ||
                        owner.Host.GetUpdateStamp() != owner.hostStamp || captured && !owner.CheckItem(owner.Items[0])) lease.Invalidate();
                    if (!completion.Task.IsCompleted && clock.ElapsedMilliseconds >= 300000) { lease.Invalidate(); throw new TimeoutException("PART_NATIVE_TIME_BUDGET_EXCEEDED"); }
                    if (completion.Task.IsCompleted)
                    {
                        if (files != null && files.IsCompleted) { CheckFiles(files.GetAwaiter().GetResult()); files = null; nextAudit = DateTime.UtcNow.AddSeconds(2); }
                        if (files == null && DateTime.UtcNow >= nextAudit) files = AssemblyNativeBatchOperation.ReadFiles(paths);
                        return;
                    }
                    token.ThrowIfCancellationRequested();
                    if (!lease.Current) throw new InvalidOperationException("STALE_PART_OR_CONFIGURATION");
                    if (revisions == null)
                    {
                        if (!files.IsCompleted) return;
                        revisions = files.GetAwaiter().GetResult(); files = null;
                        string hash;
                        if (minimum?.Snapshot?.SourcePath != null && (!revisions.TryGetValue(minimum.Snapshot.SourcePath, out hash) || hash != minimum.Snapshot.SourceSha256))
                            throw new InvalidOperationException("STALE_MINIMUM_WORKBOOK");
                    }
                    if (owner.ToolLibrary.Error == "DXF source read pending.") return;
                    if (!captured)
                    {
                        reused = owner.Items[0].Done && owner.Items[0].PlacementCapture != null;
                        var capture = Stopwatch.StartNew(); owner.Capture(owner.Items[0], minimum); owner.Items[0].Context?.SetSource(minimum);
                        captureMs = capture.Elapsed.TotalMilliseconds; captured = true;
                        var item = owner.Items[0]; model = item.PlacementCapture;
                        // A 0-bend/non-sheet Part has no inferred bending data.
                        if (model == null && item.Rows.Count == 0) model = new PlacementModelCapture(item.Key, item.Stamp,
                            item.Configuration, new PlacementBodySnapshot[0], new PlacementBendSnapshot[0], false);
                        program = model == null ? Task.FromResult(new AssemblyProgramData()) : ReadProcessProgramAsync(owner.processPrograms, item.Key, item.Path, model, owner.ToolLibrary, token);
                        return;
                    }
                    if (work == null)
                    {
                        if (!program.IsCompleted) return;
                        var data=program.GetAwaiter().GetResult(); var item=owner.Items[0];
                        var occurrence = new AssemblyOccurrence("PART:" + item.Key, null, item.Name, item.Path, item.Configuration,
                            new double[] { 1,0,0,0,1,0,0,0,1,0,0,0,1,0,0,0 }, AssemblyOccurrenceKind.SheetMetal, true);
                        var source = new AssemblySourceInput(item.Key, model, new[] { occurrence }, data.Envelopes, data.Setups, data.Plans, data.Use,
                            item.Error, data.ConditionsRevision, data.Sequence, ReadMinimumMetadata(item)) {ReadinessDiagnostics=data.ReadinessDiagnostics};
                        var request = new AssemblyBatchRequest(item.Key, item.Configuration, AssemblyCheckScope.EntireAssembly, lease, owner.ToolLibrary,
                            new[] { source }, new[] { occurrence }, 0, captureMs, model == null || reused ? 0 : 1, reused ? 1 : 0);
                        work = owner.assemblyCoordinator.RunAsync(request, token: token, progress: progress); return;
                    }
                    if (!work.IsCompleted) return;
                    if (!publication) { publication = true; files = AssemblyNativeBatchOperation.ReadFiles(paths); tools = owner.toolLibrary.VerifyForPublicationAsync(); processVerification=owner.processPrograms.VerifyForPublicationAsync(); return; }
                    if (!files.IsCompleted || !tools.IsCompleted || !processVerification.IsCompleted) return;
                    processVerification.GetAwaiter().GetResult();
                    CheckFiles(files.GetAwaiter().GetResult()); files = null;
                    if (!ReferenceEquals(tools.GetAwaiter().GetResult(), owner.ToolLibrary) || !owner.ToolLibrary.Current) lease.Invalidate();
                    completion.TrySetResult(work.GetAwaiter().GetResult());
                }
                catch (Exception ex) { lease.Invalidate(); owner.assemblyCoordinator.Cancel(); completion.TrySetException(ex); }
            }
            private void CheckFiles(Dictionary<string, string> now)
            { if (revisions.Any(p => !now.ContainsKey(p.Key) || now[p.Key] != p.Value)) lease.Invalidate(); }
        }
    }
}
