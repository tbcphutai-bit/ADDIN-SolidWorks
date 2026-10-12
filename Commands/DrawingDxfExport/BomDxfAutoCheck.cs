using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ADDIN.Commands.DxfChecking;

namespace ADDIN.Commands.DrawingDxfExporting
{
    // Check exactly the files published by this export. Existing DXFs in the
    // target folder are not rescanned. The existing Check DXF rules/exporter
    // own all geometric checks, annotations and the Excel report.
    internal sealed class BomDxfAutoCheck
    {
        internal readonly DxfBatchResult Batch;
        private readonly string cutLayer;
        private readonly Stopwatch timer = Stopwatch.StartNew();

        internal BomDxfAutoCheck(string sourceFolder, string runId, DxfManufacturingLayerSettings layers, string outputRoot = null)
        {
            cutLayer = DxfManufacturingLayerSettings.Normalize(layers).Cut.Name;
            Batch = new DxfBatchResult {
                SourceFolder = Path.GetFullPath(sourceFolder), Options = DxfCheckOptions.Default,
                OutputFolder = Path.Combine(outputRoot ?? sourceFolder, DxfCheckEngine.OutputDirectoryName, runId)
            };
        }

        internal DxfFileResult Check(BomDxfFileResult item, Func<bool> cancel)
        {
            string path = Path.GetFullPath(item.OutputPath);
            string prefix = Batch.SourceFolder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("DXF vừa xuất nằm ngoài thư mục của lượt chạy.");
            string relative = path.Substring(prefix.Length);
            using (var cancellation = new CancellationTokenSource()) {
                if (cancel?.Invoke() == true) cancellation.Cancel();
                var worker = Task.Run(() => CheckFile(path, relative, Batch.OutputFolder, cutLayer, cancellation.Token), cancellation.Token);
                // Geometry is managed code only. Pump the task-pane UI while it
                // runs so Stop/scroll/select stay usable without COM on a worker.
                while (!worker.IsCompleted) {
                    Application.DoEvents();
                    if (cancel?.Invoke() == true) cancellation.Cancel();
                    Thread.Sleep(20);
                }
                var result = worker.GetAwaiter().GetResult();
                Batch.Files.Add(result);
                return result;
            }
        }

        internal static DxfFileResult CheckFile(string path, string relative, string outputFolder, string cutLayer, CancellationToken token)
        {
            var result = new DxfFileResult { SourcePath = path, RelativeName = relative, Options = DxfCheckOptions.Default, CutLayer = cutLayer };
            var watch = Stopwatch.StartNew();
            try {
                token.ThrowIfCancellationRequested();
                var document = DxfRawDocument.Read(path);
                document.Extract(result);
                // BEND and laser strokes intentionally remain open. Check the
                // exported cutting layer for cut quality, then restore all
                // geometry for the existing yellow-layer statistics/report.
                DxfCheckEngine.AnalyzeFile(result, token);
                token.ThrowIfCancellationRequested();
                if (result.Status == "NG") {
                    result.OutputPath = Path.Combine(outputFolder, relative);
                    DxfCheckExport.WriteAnnotated(document, result);
                }
                token.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { result.Error = ex.Message; result.OutputPath = null; }
            result.Milliseconds = watch.Elapsed.TotalMilliseconds;
            return result;
        }

        internal void Finish(bool canceled)
        {
            Batch.Cancelled = canceled; Batch.Seconds = timer.Elapsed.TotalSeconds;
            if (Batch.Files.Count == 0) return;
            Directory.CreateDirectory(Batch.OutputFolder);
            Batch.ExcelPath = Path.Combine(Batch.OutputFolder, "CHECK_DXF.xlsx");
            DxfCheckExport.WriteExcel(Batch);
        }

        internal static void Apply(BomDxfFileResult item, DxfFileResult file)
        {
            item.CheckMilliseconds = file.Milliseconds;
            item.CheckStatus = file.Status; item.CheckIssueCount = file.Issues.Count;
            item.CheckOutputPath = file.OutputPath;
            item.CheckMessage = file.Error ?? (file.Status == "OK" ? "Biên CUT đạt các điều kiện Check DXF mặc định." :
                file.Issues.Count + " lỗi: " + string.Join("; ", file.Issues.Select(i => i.Description).Distinct()));
            item.CheckIssues.Clear();
            item.CheckIssues.AddRange(file.Issues.Select(i => "#" + i.Id + " · " + i.Code + " · " + i.Description + " · layer " + i.Layer));
            DxfTrace.Info("Check DXF " + item.PartNumber + ": " + item.CheckStatus + " · " + item.CheckMessage);
        }

        internal static void Reuse(BomDxfFileResult item, BomDxfFileResult previous)
        {
            item.CheckStatus = previous.CheckStatus; item.CheckMessage = previous.CheckMessage;
            item.CheckIssueCount = previous.CheckIssueCount; item.CheckOutputPath = previous.CheckOutputPath;
            item.CheckMilliseconds = previous.CheckMilliseconds;
            item.CheckIssues.Clear();
            item.CheckIssues.AddRange(previous.CheckIssues);
        }
    }

    // One managed worker; all item/UI updates stay on the SOLIDWORKS thread.
    internal sealed class BomDxfCheckQueue : IDisposable
    {
        private sealed class Job
        {
            internal BomDxfFileResult Item;
            internal string Path, Relative;
            internal readonly ManualResetEventSlim Ready = new ManualResetEventSlim();
        }
        private sealed class Update
        {
            internal Job Job;
            internal bool Started, Canceled;
            internal DxfFileResult Result;
            internal string Error;
        }
        private readonly BomDxfAutoCheck checker;
        private readonly string cutLayer;
        private readonly Func<bool> cancel;
        private readonly Action<BomDxfFileResult> changed;
        private readonly BlockingCollection<Job> jobs = new BlockingCollection<Job>();
        private readonly ConcurrentQueue<Update> updates = new ConcurrentQueue<Update>();
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private Task worker;
        private bool drained;
        internal bool Canceled { get { return cancellation.IsCancellationRequested; } }

        internal BomDxfCheckQueue(BomDxfAutoCheck owner, DxfManufacturingLayerSettings layers,
            Func<bool> cancellationRequested, Action<BomDxfFileResult> itemChanged)
        {
            checker = owner; cutLayer = DxfManufacturingLayerSettings.Normalize(layers).Cut.Name;
            cancel = cancellationRequested; changed = itemChanged;
        }

        internal void Enqueue(BomDxfFileResult item)
        {
            string path = Path.GetFullPath(item.OutputPath);
            string prefix = checker.Batch.SourceFolder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("DXF vừa xuất nằm ngoài thư mục của lượt chạy.");
            item.CheckStatus = "QUEUED"; item.CheckMessage = "Chờ Check DXF.";
            jobs.Add(new Job { Item = item, Path = path, Relative = path.Substring(prefix.Length) });
            changed?.Invoke(item);
        }

        internal void Start()
        {
            if (worker != null) return;
            worker = Task.Run(() => {
                foreach (var job in jobs.GetConsumingEnumerable()) {
                    var update = new Update { Job = job };
                    try {
                        cancellation.Token.ThrowIfCancellationRequested();
                        updates.Enqueue(new Update { Job = job, Started = true });
                        // Let the UI show CHECKING (and service Stop) before
                        // reading the file; no UI or COM calls on this worker.
                        job.Ready.Wait(cancellation.Token);
                        update.Result = BomDxfAutoCheck.CheckFile(job.Path, job.Relative,
                            checker.Batch.OutputFolder, cutLayer, cancellation.Token);
                    }
                    catch (OperationCanceledException) { update.Canceled = true; }
                    catch (Exception ex) { update.Error = ex.Message; }
                    finally { updates.Enqueue(update); }
                }
            });
        }

        internal void Poll()
        {
            if (cancel?.Invoke() == true) cancellation.Cancel();
            while (updates.TryDequeue(out Update update)) {
                var item = update.Job.Item;
                if (update.Started) {
                    item.CheckStatus = "CHECKING"; item.CheckMessage = "Đang kiểm tra biên CUT.";
                    try {
                        changed?.Invoke(item);
                        if (cancel?.Invoke() == true) cancellation.Cancel();
                    }
                    finally { update.Job.Ready.Set(); }
                    continue;
                }
                if (update.Canceled) {
                    item.CheckStatus = "CANCEL"; item.CheckMessage = "DXF đã xuất; đã dừng bước Check DXF.";
                }
                else if (update.Error != null) { item.CheckStatus = "ERROR"; item.CheckMessage = update.Error; }
                else { checker.Batch.Files.Add(update.Result); BomDxfAutoCheck.Apply(item, update.Result); }
                update.Job.Ready.Dispose();
                changed?.Invoke(item);
            }
        }

        internal void Drain()
        {
            if (drained) return;
            jobs.CompleteAdding(); Start();
            while (!worker.IsCompleted) {
                Poll(); Application.DoEvents();
                if (cancel?.Invoke() == true) cancellation.Cancel();
                Thread.Sleep(10);
            }
            worker.GetAwaiter().GetResult(); Poll(); drained = true;
        }

        public void Dispose()
        {
            if (!drained) { cancellation.Cancel(); Drain(); }
            jobs.Dispose(); cancellation.Dispose();
        }
    }
}
