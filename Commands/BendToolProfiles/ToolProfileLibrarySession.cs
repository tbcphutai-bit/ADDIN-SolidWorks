using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace ADDIN.Commands.BendTools
{
    // Only managed settings/file data enters this worker. It has no reference to
    // a Part, BendRow, MinimumContext, control or any SOLIDWORKS COM object.
    internal sealed class ToolProfileLibrarySession : IDisposable
    {
        private readonly ToolProfileLibraryProvider provider;
        private readonly CancellationTokenSource cancellation=new CancellationTokenSource();
        private Task<ToolProfileLibrarySnapshot> pending;
        private DateTime next=DateTime.MinValue;
        private string lastReport;
        private bool disposed;
        internal ToolProfileLibrarySnapshot Snapshot { get; private set; } = ToolProfileLibraryProvider.Failure("DXF source read pending.");
        internal string DiagnosticError { get; private set; }
        internal ToolProfileLibrarySession() : this(new ToolProfileLibraryProvider()) { }
        internal ToolProfileLibrarySession(ToolProfileLibraryProvider provider) { this.provider=provider; }
        internal void Refresh()
        {
            if (disposed || pending!=null) return;
            pending=Task.Run(()=>provider.Read(cancellation.Token),cancellation.Token);
        }
        // Explicit end-of-batch freshness barrier, managed I/O only. A changed
        // source invalidates the old provider snapshot even before the next UI
        // poll accepts the replacement. Never publish old cached CLEAR while a
        // newly requested source validation is still pending.
        internal Task<ToolProfileLibrarySnapshot> VerifyForPublicationAsync()
        {
            if(disposed)throw new ObjectDisposedException(nameof(ToolProfileLibrarySession));
            var token=cancellation.Token;
            return Task.Run(()=>provider.Read(token),token);
        }
        internal void Poll()
        {
            if (disposed) return;
            if (pending!=null && pending.IsCompleted)
            {
                var task=pending; pending=null;
                if (!task.IsCanceled)
                {
                    var result=task.IsFaulted ? ToolProfileLibraryProvider.Failure(task.Exception.GetBaseException().Message) : task.Result;
                    if (!ReferenceEquals(Snapshot,result)) Snapshot?.Invalidate(); Snapshot=result;
                    if (lastReport!=result.Fingerprint)
                    {
                        lastReport=result.Fingerprint;
                        try { Debug.WriteLine("[BEND TOOL LIBRARY] "+result.ToDebugJson()); DiagnosticError=null; }
                        catch (Exception ex) { DiagnosticError=ex.Message; }
                    }
                }
                next=DateTime.UtcNow.AddSeconds(2);
            }
            if (pending==null && DateTime.UtcNow>=next) Refresh();
        }
        internal void RecordDiagnosticFailure(Exception ex)
        {
            DiagnosticError=ex.Message; Snapshot?.Invalidate(); Snapshot=ToolProfileLibraryProvider.Failure("DXF diagnostic update failed: "+ex.Message);
        }
        public void Dispose()
        {
            if (disposed) return; disposed=true; Snapshot?.Invalidate(); cancellation.Cancel();
            pending?.ContinueWith(t=> { var observed=t.Exception; },TaskContinuationOptions.OnlyOnFaulted);
            // A file reader may still observe this token until its task exits.
            if (pending==null || pending.IsCompleted) cancellation.Dispose();
            else pending.ContinueWith(t=>cancellation.Dispose(),TaskScheduler.Default);
        }
    }
}
