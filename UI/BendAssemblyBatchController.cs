using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using ADDIN.Commands;
using ADDIN.Commands.BendAssembly;
using ADDIN.Commands.BendTools;

namespace ADDIN.UI
{
    // Narrow boundary also used by Form lifecycle tests. Implementations own
    // native state; the controller calls Begin/Pump/Dispose only on the UI STA.
    internal interface IBendAssemblyBatchOperation
    {
        Task<AssemblyBatchSnapshot> Completion { get; }
        void Pump();
        void Cancel();
    }
    internal interface IBendAssemblyBatchBackend : IDisposable
    {
        Task<BendToolCatalogReadResult> ReadMinimumAsync(CancellationToken token);
        IBendAssemblyBatchOperation BeginAssemblyBatch(AssemblyCheckScope scope, BendToolCatalogReadResult minimum,
            CancellationToken token, IProgress<AssemblyBatchProgress> progress);
        void CaptureSelection();
        void Invalidate();
    }
    internal interface IBendBatchLocator
    { bool Locate(AssemblyBatchSnapshot snapshot, string source, int feature); }

    internal sealed class BendAssemblyBatchController : IDisposable
    {
        private readonly BendAssemblyBatchForm form;
        private readonly IBendAssemblyBatchBackend backend;
        private readonly System.Windows.Forms.Timer timer;
        private readonly Control dispatcher = new Control();
        private readonly Stopwatch elapsed = new Stopwatch();
        private readonly TaskCompletionSource<bool> disposedSignal = new TaskCompletionSource<bool>();
        private readonly int staThread = Thread.CurrentThread.ManagedThreadId;
        private CancellationTokenSource cancellation;
        private IBendAssemblyBatchOperation operation;
        private AssemblyBatchSnapshot displayed;
        private bool running, pumping, closing, disposed, backendDisposed;
        private bool pumpReported;
        private int generation;
        internal bool Running => running;
        internal BendAssemblyBatchTimings LastRunTimings { get; private set; }
        private readonly bool traceClose = Environment.GetEnvironmentVariable("ADDIN_BEND_CLOSE_TRACE") == "1";
        private void TraceClose(string stage)
        {
            if (traceClose) Trace.WriteLine("[BEND CLOSE] " + stage + " controller=" + GetHashCode() +
                " thread=" + Thread.CurrentThread.ManagedThreadId + " generation=" + generation +
                " running=" + running + " pumping=" + pumping + " closing=" + closing +
                " disposed=" + disposed + " backendDisposed=" + backendDisposed +
                " formDisposed=" + form.IsDisposed + " operation=" + (operation == null ? "null" : operation.Completion.Status.ToString()));
        }

        internal BendAssemblyBatchController(BendAssemblyBatchForm form, IBendAssemblyBatchBackend backend)
        {
            if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA) throw new InvalidOperationException("Batch UI requires STA");
            this.form = form; this.backend = backend;
            // One path for BOTH the main TaskPane command and legacy launcher.
            // No COM work here; only install borrowed host and read-only resolver.
            var livePreviewBackend = backend as BendAssemblyBatchLiveBackend;
            if (livePreviewBackend != null)
            {
                form.UxV4SetSolidWorksApp(livePreviewBackend.UxV4PreviewApp);
                form.UxV4SetSavedPartPathResolver(livePreviewBackend.UxV4ResolveSavedPartPath);
            }
            // SOLIDWORKS/COM message pumping may replace the ambient context.
            // Keep our own STA handle instead of trusting await's context.
            var handle = dispatcher.Handle;
            form.RunBatchRequested += RunRequested;
            form.CancelBatchRequested += CancelRequested;
            form.RefreshRequested += RefreshRequested;
            form.PreviewRequested += PreviewRequested;
            form.ScopeChanged += ScopeChanged;
            form.BendLocateRequested += LocateRequested;
            form.FormClosing += FormClosing;
            form.Disposed += FormDisposed;
            timer = new System.Windows.Forms.Timer { Interval = 50 };
            timer.Tick += Tick;
            timer.Start();
            BendCheckRunDiagnostics.Write("Controller.connected",new {Controller=GetHashCode(),Form=form.GetHashCode(),StaThread=staThread});
        }
        private void RequireSta()
        {
            if (Thread.CurrentThread.ManagedThreadId != staThread) throw new InvalidOperationException("Batch native calls must stay on the owning STA");
        }
        private void LocateRequested(object sender, Tuple<string, int> target)
        {
            RequireSta();
            if (running || displayed?.Current != true || form.IsPreview) return;
            try { (backend as IBendBatchLocator)?.Locate(displayed, target.Item1, target.Item2); }
            catch (Exception ex) { form.RecordDiagnostic("Lỗi làm sáng vị trí trên CAD: " + ex.GetType().Name + " — " + ex.Message); }
        }
        private async void RunRequested(object sender, AssemblyCheckScope scope)
        { BendCheckRunDiagnostics.Write("RunBatchRequested",new {Controller=GetHashCode(),Scope=scope.ToString()});await RunAsync(scope); }
        private async void RefreshRequested(object sender, EventArgs args)
        {
            if (running || disposed) return;
            if (!TryInvalidate()) return;
            await RunAsync(form.Scope);
        }
        private bool TryInvalidate()
        {
            RequireSta();
            // Guard the backend boundary before RunAsync's exception handler.
            // Withdraw the previous result even when that read fails.
            displayed = null;
            form.ClearLive("Đang cập nhật nguồn CAD; chưa có kết quả hiện hành.");
            try { backend.Invalidate(); return true; }
            catch (Exception ex)
            {
                form.ClearLive("Không cập nhật được nguồn CAD — " + ex.Message + ". Mở lại chi tiết rồi Quét lại.");
                form.RecordDiagnostic(ex.GetType().Name + " — " + ex.Message);
                return false;
            }
        }
        internal async Task RunAsync(AssemblyCheckScope scope)
        {
            RequireSta();
            if (running || disposed || closing) { BendCheckRunDiagnostics.Write("RunAsync.ignored",new {running,disposed,closing});return; }
            BendCheckRunDiagnostics.Write("RunAsync.begin",new {Controller=GetHashCode(),Scope=scope.ToString()});
            int run = ++generation;
            running = true; displayed = null; operation = null; pumpReported=false;
            LastRunTimings = null;
            cancellation = new CancellationTokenSource();
            var token = cancellation.Token;
            elapsed.Restart();
            TraceClose("RunAsync.begin");
            form.ClearLive("Đọc nguồn dữ liệu; chưa có kết quả CAD.");
            form.SetRunningState(true);
            try
            {
                BendCheckRunDiagnostics.Write("ReadMinimumAsync.begin",token:token);
                var minimum = await backend.ReadMinimumAsync(token).ConfigureAwait(false);
                BendCheckRunDiagnostics.Write("ReadMinimumAsync.end",new {Returned=minimum!=null},token:token);
                await OnSta();
                RequireSta();
                TraceClose("RunAsync.minimum.ready");
                token.ThrowIfCancellationRequested();
                if (disposed) return;
                // Construct on STA. Late progress from previous generations is
                // ignored, including queued callbacks after Cancel/Refresh.
                var progress = new UiProgress(this, p =>
                {
                    if (!disposed && running && run == generation && !token.IsCancellationRequested)
                        form.UpdateProgress(p, elapsed.Elapsed.TotalSeconds);
                });
                BendCheckRunDiagnostics.Write("BeginAssemblyBatch.begin",token:token);
                operation = backend.BeginAssemblyBatch(scope, minimum, token, progress);
                BendCheckRunDiagnostics.Write("BeginAssemblyBatch.end",new {Completion=operation.Completion.Status.ToString()},token:token);
                var completion = operation.Completion;
                // Direct Dispose is also supported; never wait synchronously
                // for native work while closing the owner window.
                var ended = await Task.WhenAny(completion, disposedSignal.Task).ConfigureAwait(false);
                await OnSta();
                TraceClose("RunAsync.completion.resumed");
                if (ended != completion)
                {
                    ObserveFault(completion);
                    return;
                }
                var snapshot = await completion;
                BendCheckRunDiagnostics.Write("Completion",new {Current=snapshot?.Current,Parts=snapshot?.Parts.Count,Reason=snapshot?.InvalidReason},token:token);
                if (disposed) return;
                if (token.IsCancellationRequested)
                    form.ShowStopped("Đã dừng kiểm tra. Không có kết quả hiện hành.", elapsed.Elapsed.TotalSeconds, snapshot);
                else if (snapshot == null || !snapshot.Current)
                    form.ShowStopped("Kết quả không còn hiệu lực — " + snapshot?.InvalidReason, elapsed.Elapsed.TotalSeconds, snapshot);
                else
                {
                    displayed = snapshot;
                    form.BindSnapshot(snapshot, elapsed.Elapsed.TotalSeconds);
                    var audit = Stopwatch.StartNew(); bool current = snapshot.Current;
                    if (!current)
                    {
                        displayed = null;
                        form.ShowStopped("Kết quả đổi trong khi hiển thị; cần Quét lại.", elapsed.Elapsed.TotalSeconds, snapshot);
                    }
                    else
                    {
                        LastRunTimings = form.LastBindingTimings;
                        if (LastRunTimings != null) LastRunTimings.SnapshotValidationMilliseconds += audit.Elapsed.TotalMilliseconds;
                        form.RecordVisibleTime(elapsed.Elapsed.TotalMilliseconds);
                    }
                }
            }
            catch (OperationCanceledException ex)
            {
                BendCheckRunDiagnostics.Write("RunAsync.cancelled",error:ex,token:token);
                await OnSta();
                if (!disposed) form.ShowStopped("Đã hủy tác vụ. Không có kết quả hiện hành.", elapsed.Elapsed.TotalSeconds);
            }
            catch (Exception ex)
            {
                BendCheckRunDiagnostics.Write("RunAsync.error",error:ex,token:token);
                await OnSta();
                operation?.Cancel();
                if (!disposed)
                {
                    form.ShowStopped("Không hoàn tất kiểm tra — " + ex.Message, elapsed.Elapsed.TotalSeconds);
                    form.RecordDiagnostic(ex.ToString()+"\r\nLog: "+BendCheckRunDiagnostics.LogPath);
                }
            }
            finally
            {
                await OnSta();
                TraceClose("RunAsync.finally.begin");
                BendCheckRunDiagnostics.Write("RunAsync.end",new {ElapsedMs=elapsed.Elapsed.TotalMilliseconds,Completion=operation?.Completion.Status.ToString()},token:token);
                elapsed.Stop(); running = false;
                cancellation.Dispose(); cancellation = null;
                if (disposed && !pumping) dispatcher.Dispose();
                if (!disposed)
                {
                    form.SetRunningState(false);
                    CloseWhenSafe();
                }
                TraceClose("RunAsync.finally.end");
            }
        }
        private StaContinuation OnSta() => new StaContinuation(this);
        private struct StaContinuation : INotifyCompletion
        {
            private readonly BendAssemblyBatchController owner;
            internal StaContinuation(BendAssemblyBatchController owner) { this.owner = owner; }
            public StaContinuation GetAwaiter() => this;
            public bool IsCompleted => Thread.CurrentThread.ManagedThreadId == owner.staThread;
            public void OnCompleted(Action continuation) => owner.dispatcher.BeginInvoke(continuation);
            public void GetResult() => owner.RequireSta();
        }
        private sealed class UiProgress : IProgress<AssemblyBatchProgress>
        {
            private readonly BendAssemblyBatchController owner;
            private readonly Action<AssemblyBatchProgress> receive;
            internal UiProgress(BendAssemblyBatchController owner, Action<AssemblyBatchProgress> receive)
            { this.owner = owner; this.receive = receive; }
            public void Report(AssemblyBatchProgress value)
            {
                if (owner.disposed) return;
                try { owner.dispatcher.BeginInvoke(new Action(() => { if (!owner.disposed) receive(value); })); }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { if (!owner.disposed) throw; }
            }
        }
        private static void ObserveFault(Task task)
        { task.ContinueWith(t => { var ignored = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted); }
        private void CancelRequested(object sender, EventArgs args) { Cancel(); }
        internal void Cancel()
        {
            RequireSta();
            TraceClose("Cancel.begin");
            if (disposed) return;
            cancellation?.Cancel();
            operation?.Cancel();
            TraceClose("Cancel.operation.cancelled");
            displayed = null;
            form.ShowStopped("Đang hủy; chờ xác nhận dừng...", elapsed.Elapsed.TotalSeconds);
            if (running) form.SetRunMessage("Đang hủy; chờ xác nhận dừng...", true);
            TraceClose("Cancel.end");
        }
        private void PreviewRequested(object sender, EventArgs args)
        {
            if (running || disposed) return;
            operation?.Cancel(); operation = null; displayed = null;
            if (!TryInvalidate()) return;
            form.LoadMockupData();
        }
        private void ScopeChanged(object sender, AssemblyCheckScope scope)
        {
            if (running || disposed) return;
            operation?.Cancel(); operation = null; displayed = null;
            if (!TryInvalidate()) return;
            try { if (scope == AssemblyCheckScope.SelectedComponents) backend.CaptureSelection(); }
            catch (Exception ex) { form.ClearLive("Không đọc được phạm vi chọn — " + ex.Message); return; }
            form.ClearLive("Phạm vi đã đổi. Bấm Bắt đầu kiểm tra; Quét lại giữ nguyên phạm vi đã chọn.");
        }
        private void Tick(object sender, EventArgs args)
        {
            if (disposed || pumping) return;
            RequireSta(); timer.Stop(); pumping = true;
            try
            {
                if(operation!=null&&!pumpReported){pumpReported=true;BendCheckRunDiagnostics.Write("Pump.begin",new {Controller=GetHashCode()},token:cancellation?.Token??default(CancellationToken));}
                operation?.Pump();
                if (displayed != null && !displayed.Current)
                {
                    string reason = displayed.InvalidReason;
                    var previous = displayed;
                    displayed = null;
                    form.ShowStopped("Kết quả đã cũ — " + reason + ". Cần Quét lại.", elapsed.Elapsed.TotalSeconds, previous);
                }
            }
            catch (Exception ex)
            {
                BendCheckRunDiagnostics.Write("Pump.error",error:ex,token:cancellation?.Token??default(CancellationToken));
                cancellation?.Cancel(); operation?.Cancel(); displayed = null;
                form.ClearLive("Không kiểm tra được hiệu lực dữ liệu — " + ex.Message);
                if (running) form.SetRunMessage("Đang dừng sau lỗi kiểm tra; chờ xác nhận...", true);
            }
            finally
            {
                pumping = false;
                if (disposed) { DisposeBackend(); if (!running) dispatcher.Dispose(); }
                else
                {
                    // Schedule relative to the end of an atomic COM capture.
                    // An overdue WM_TIMER must not immediately start another
                    // capture/audit and monopolize the host message queue.
                    CloseWhenSafe(); timer.Start();
                }
            }
        }
        private void FormClosing(object sender, FormClosingEventArgs args)
        {
            TraceClose("FormClosing.begin");
            if (running || pumping)
            {
                args.Cancel = true; closing = true;
                Cancel();
            }
            TraceClose("FormClosing.end cancel=" + args.Cancel);
        }
        private void CloseWhenSafe()
        {
            if (closing && !running && !pumping && !form.IsDisposed)
            { closing = false; TraceClose("CloseWhenSafe.post"); form.BeginInvoke(new Action(() => { TraceClose("CloseWhenSafe.callback"); form.Close(); TraceClose("CloseWhenSafe.callback.return"); })); }
        }
        private void FormDisposed(object sender, EventArgs args) { Dispose(); }
        private void DisposeBackend()
        { if (!backendDisposed) { TraceClose("DisposeBackend.begin"); backendDisposed = true; backend.Dispose(); TraceClose("DisposeBackend.end"); } }
        public void Dispose()
        {
            RequireSta();
            TraceClose("Dispose.begin");
            if (disposed) return;
            disposed = true; ++generation;
            cancellation?.Cancel(); operation?.Cancel();
            disposedSignal.TrySetResult(true);
            timer.Stop(); timer.Dispose();
            form.RunBatchRequested -= RunRequested;
            form.CancelBatchRequested -= CancelRequested;
            form.RefreshRequested -= RefreshRequested;
            form.PreviewRequested -= PreviewRequested;
            form.ScopeChanged -= ScopeChanged;
            form.BendLocateRequested -= LocateRequested;
            form.FormClosing -= FormClosing;
            form.Disposed -= FormDisposed;
            if (!pumping) { DisposeBackend(); if (!running) dispatcher.Dispose(); }
            TraceClose("Dispose.end");
        }
    }
}
