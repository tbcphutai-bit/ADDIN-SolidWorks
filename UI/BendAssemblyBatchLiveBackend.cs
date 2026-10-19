using System;
using System.Threading;
using System.Threading.Tasks;
using ADDIN.Commands;
using ADDIN.Commands.BendAssembly;
using SolidWorks.Interop.sldworks;

namespace ADDIN.UI
{
    internal sealed class BendAssemblyBatchLiveBackend : IBendAssemblyBatchBackend
    {
        private readonly BendCheckSession session;
        private readonly BendToolCatalogProvider minimum;
        internal BendAssemblyBatchLiveBackend(ISldWorks app, ModelDoc2 host)
            : this(app, host, new BendToolCatalogProvider()) { }
        internal BendAssemblyBatchLiveBackend(ISldWorks app, ModelDoc2 host, BendToolCatalogProvider minimum)
        {
            this.minimum = minimum;
            // Separate session: Batch never resets the legacy Check Min window.
            session = new BendCheckSession(app, host, null);
        }
        public Task<BendToolCatalogReadResult> ReadMinimumAsync(CancellationToken token) =>
            Task.Run(() => minimum.ReadForCheck(token), token);
        public void CaptureSelection() => session.CaptureSelection();
        public void Invalidate() => session.Invalidate();
        public IBendAssemblyBatchOperation BeginAssemblyBatch(AssemblyCheckScope scope, BendToolCatalogReadResult source,
            CancellationToken token, IProgress<AssemblyBatchProgress> progress) =>
            new NativeOperation(session.BeginAssemblyBatch(scope, source, new AssemblyBatchOptions(), token, progress));
        public void Dispose() => session.Dispose();
        private sealed class NativeOperation : IBendAssemblyBatchOperation
        {
            private readonly BendCheckSession.AssemblyNativeBatchOperation operation;
            internal NativeOperation(BendCheckSession.AssemblyNativeBatchOperation operation) { this.operation = operation; }
            public Task<AssemblyBatchSnapshot> Completion => operation.Completion;
            public void Pump() => operation.Pump(maximumCaptures: 1);
            public void Cancel() => operation.Cancel();
        }
    }
}
