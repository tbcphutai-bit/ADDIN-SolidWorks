using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADDIN.Commands;
using ADDIN.Commands.BendAssembly;
using ADDIN.Commands.BendTools;
using SolidWorks.Interop.sldworks;

namespace ADDIN.UI
{
    internal sealed class BendAssemblyBatchLiveBackend : IBendAssemblyBatchBackend, IBendBatchLocator
    {
        private readonly BendCheckSession session;
        private readonly ISldWorks uxV4PreviewApp; // borrowed from existing Add-in
        internal ISldWorks UxV4PreviewApp => uxV4PreviewApp;
        private readonly BendToolCatalogProvider minimum;
        internal bool HasSelection => session.HasSelection;
        internal BendAssemblyBatchLiveBackend(ISldWorks app, ModelDoc2 host)
            : this(app, host, new BendToolCatalogProvider()) { }
        internal BendAssemblyBatchLiveBackend(ISldWorks app, ModelDoc2 host, BendToolCatalogProvider minimum)
        {
            this.minimum = minimum;
            uxV4PreviewApp = app;
            // Separate session: Batch never resets the legacy Check Min window.
            session = new BendCheckSession(app, host, null);
        }
        public Task<BendToolCatalogReadResult> ReadMinimumAsync(CancellationToken token) =>
            Task.Run(() => minimum.ReadForCheck(token), token);
        public void CaptureSelection() => session.CaptureSelection();
        public void Invalidate() => session.Invalidate();
        public IBendAssemblyBatchOperation BeginAssemblyBatch(AssemblyCheckScope scope, BendToolCatalogReadResult source,
            CancellationToken token, IProgress<AssemblyBatchProgress> progress)
        {
            BendCheckRunDiagnostics.Write("LiveBackend.Begin",new {session.IsAssembly,Scope=scope.ToString()},token:token);
            return session.IsAssembly ? (IBendAssemblyBatchOperation)new NativeOperation(session.BeginAssemblyBatch(scope, source, new AssemblyBatchOptions(), token, progress)) :
                new PartOperation(session.BeginPartBatch(source, token, progress));
        }
        public bool Locate(AssemblyBatchSnapshot snapshot, string source, int feature) => session.LocateBatchBend(snapshot, source, feature);

        // Read-only saved-file thumbnail lookup for the CURRENT UI part. Uses the
        // inventory already captured by native batch, never a name/hash-to-path guess.
        // Must run on the existing SOLIDWORKS STA (called from WinForms UI).
        // Returned file is NOT the current assembly-cut/unsaved manufacturing geometry.
        internal string UxV4ResolveSavedPartPath(BatchPartViewModel part)
        {
            if (part == null || part.RawPartResult?.Current != true || !session.IsHostActive || session.IsEditing)
                return null;
            var raw = part.RawPartResult;
            BendCheckItem[] matches;
            if (session.IsAssembly)
            {
                // Exact native occurrence objects also drive LocateBatchBend.
                // Never reverse SourceIdentity/SourceKey: assembly keys are hashes.
                matches = session.Items.Where(item => item.Current &&
                    item.AssemblyOccurrences.Any(o => raw.Occurrences.Any(r => Object.ReferenceEquals(o, r))))
                    .ToArray();
            }
            else
            {
                matches = session.Items.Where(item => item.Current &&
                    String.Equals(item.Path, session.Host.GetPathName(), StringComparison.OrdinalIgnoreCase))
                    .ToArray();
            }
            // Ambiguity is resolved by showing no preview, not an unrelated Part.
            if (matches.Length != 1) return null;
            var itemFound = matches[0];
            if (itemFound.AssemblyBodyModified ||
                raw.Occurrences.Any(o => o.Modified || o.Virtual || o.Mirrored || o.Flexible)) return null;
            if (!String.Equals(part.Configuration, itemFound.Configuration, StringComparison.OrdinalIgnoreCase))
                return null;
            var path = itemFound.Path;
            if (String.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path) ||
                !path.EndsWith(".sldprt", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
                return null;
            return path;
        }
        public void Dispose()
        {
            bool trace=System.Environment.GetEnvironmentVariable("ADDIN_BEND_CLOSE_TRACE")=="1";
            if(trace)System.Diagnostics.Trace.WriteLine("[BEND CLOSE BACKEND] session.Dispose.begin");
            session.Dispose();
            if(trace)System.Diagnostics.Trace.WriteLine("[BEND CLOSE BACKEND] session.Dispose.end");
        }
        private sealed class NativeOperation : IBendAssemblyBatchOperation
        {
            private readonly BendCheckSession.AssemblyNativeBatchOperation operation;
            internal NativeOperation(BendCheckSession.AssemblyNativeBatchOperation operation) { this.operation = operation; }
            public Task<AssemblyBatchSnapshot> Completion => operation.Completion;
            public void Pump() => operation.Pump(maximumCaptures: 1);
            public void Cancel() => operation.Cancel();
        }
        private sealed class PartOperation : IBendAssemblyBatchOperation
        {
            private readonly BendCheckSession.PartNativeBatchOperation operation;
            internal PartOperation(BendCheckSession.PartNativeBatchOperation operation) { this.operation = operation; }
            public Task<AssemblyBatchSnapshot> Completion => operation.Completion;
            public void Pump() => operation.Pump();
            public void Cancel() => operation.Cancel();
        }
    }
}
