using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using ADDIN.Commands.BendAssembly;
using ADDIN.Commands.BendEnvelope;
using ADDIN.Commands.BendPlacement;
using ADDIN.Commands.BendProcess;
using ADDIN.Commands.BendPlanning;
using ADDIN.Commands.BendTools;

namespace ADDIN.Commands
{
    // Managed program supplied by a reviewed-data provider. The optional factory
    // runs ONLY during Pump on STA; no callback/owner/COM enters the coordinator.
    internal sealed class AssemblyProgramData
    {
        internal IReadOnlyList<string> ReadinessDiagnostics {get;set;} = PlacementLists.Copy<string>(null);
        internal IEnumerable<SheetEnvelopeCapture> Envelopes { get; }
        internal IEnumerable<ToolAxialSetupEvidence> Setups { get; }
        internal IEnumerable<BendProcessPlan> Plans { get; }
        internal AssemblyProgramUse Use { get; }
        internal string ConditionsRevision { get; }
        internal PartSequenceSpace Sequence { get; }
        internal AssemblyProgramData(IEnumerable<SheetEnvelopeCapture> envelopes, IEnumerable<ToolAxialSetupEvidence> setups,
            IEnumerable<BendProcessPlan> plans, AssemblyProgramUse use, string revision) : this(envelopes, setups, plans, use, revision, null) { }
        internal AssemblyProgramData(IEnumerable<SheetEnvelopeCapture> envelopes=null, IEnumerable<ToolAxialSetupEvidence> setups=null,
            IEnumerable<BendProcessPlan> plans=null, AssemblyProgramUse use=null, string revision=null, PartSequenceSpace sequence=null)
        { Envelopes=PlacementLists.Copy(envelopes); Setups=PlacementLists.Copy(setups); Plans=PlacementLists.Copy(plans); Use=use; ConditionsRevision=revision; Sequence=sequence; }
    }

    internal sealed partial class BendCheckSession
    {
        private readonly AssemblyBatchCoordinator assemblyCoordinator = new AssemblyBatchCoordinator();
        private readonly ProcessProgramProvider processPrograms = new ProcessProgramProvider();
        private static async Task<AssemblyProgramData> ReadProcessProgramAsync(ProcessProgramProvider provider, string source, string path, PlacementModelCapture model,
            ToolProfileLibrarySnapshot library, CancellationToken token)
        {
            var imported = await provider.ReadAsync(source, path, model, library, token).ConfigureAwait(false);
            var finalStatic = await provider.ReadFinalStaticAsync(source,path,model,library,token).ConfigureAwait(false);
            // Alternative process states belong to the sequence graph. They
            // are not the one observed final sheet envelope or a single plan
            // for the legacy per-bend static/process comparison.
            return new AssemblyProgramData(envelopes:finalStatic.Envelopes,setups:finalStatic.Setups,use: imported.Use,
                // Preserve the existing signed process-only PartLocal payload
                // when no final-static setup was supplied. Adding a new setup
                // deliberately cannot borrow approval of the narrower program.
                revision:finalStatic.Setups.Count==0?imported.Revision:ToolEvidenceCanonical.Hash(imported.Revision,finalStatic.Revision),sequence:imported.Sequence)
                {ReadinessDiagnostics=finalStatic.Reasons};
        }
        private AssemblyNativeBatchOperation assemblyOperation;
        private PlacementLease assemblySceneLease;
        private string assemblyPoseState;
        private DateTime nextAssemblyAudit=DateTime.MinValue;
        private int assemblyAuditCursor,assemblyPartAuditCursor;
        private readonly Dictionary<string,string> assemblyFileRevisions = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> assemblySourcesRequireReload = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        internal AssemblyBatchSnapshot AssemblyBatchResult => assemblyOperation?.Result;
        private bool IsAssemblyBackendEditing
        {
            get
            {
                if(IsEditing)return true;
                var target=((SolidWorks.Interop.sldworks.AssemblyDoc)Host).GetEditTarget() as SolidWorks.Interop.sldworks.ModelDoc2;
                return target!=null&&app.IsSame(target,Host)!=(int)SolidWorks.Interop.swconst.swObjectEquality.swObjectSame;
            }
        }

        // Caller invokes Pump on the existing STA message loop until Completion
        // finishes, and keeps calling PollAssemblyBackend while displaying data.
        // No controls/events changed; this is the native-tested UI entry point.
        internal AssemblyNativeBatchOperation BeginAssemblyBatch(AssemblyCheckScope scope, BendToolCatalogReadResult minimumSource,
            AssemblyBatchOptions options = null, CancellationToken token = default(CancellationToken),
            IProgress<AssemblyBatchProgress> progress = null, Func<string,PlacementModelCapture,AssemblyProgramData> program = null)
        {
            RequireAssemblySta();
            BendNativeCaptureDiagnostics.Stage("Assembly.Begin.entry");
            if (!IsAssembly) throw new InvalidOperationException("Assembly batch requires an Assembly document");
            BendCheckRunDiagnostics.Write("Assembly.Preflight",new {HostActive=IsHostActive,Editing=IsEditing,
                HasActiveSketch=Host.GetActiveSketch2()!=null,NeedsRebuild=Host.Extension.NeedsRebuild2,
                ComponentEdit=IsAssemblyBackendEditing},token:token);
            if (!IsHostActive) throw new InvalidOperationException("Quay lại đúng Assembly và configuration đã mở Check chấn.");
            if (IsEditing) throw new InvalidOperationException("Assembly hoặc chi tiết đang chỉnh sửa cần thoát sketch và Rebuild trước khi kiểm tra.");
            if (IsAssemblyBackendEditing) throw new InvalidOperationException("Thoát chế độ chỉnh sửa component trong Assembly rồi kiểm tra lại.");
            PollAssemblyBackendCore(true);
            BendNativeCaptureDiagnostics.Stage("Assembly.Begin.polled");
            bool selectedOnly = scope == AssemblyCheckScope.SelectedComponents;
            if (selectedOnly && !HasSelection) throw new InvalidOperationException("No selected components in the captured scope");
            var timer = Stopwatch.StartNew();
            bool reuse = assemblyInventorySelectedOnly == selectedOnly && CapturedAssemblyInputsCurrent();
            if (!reuse) BuildInventory(selectedOnly);
            BendNativeCaptureDiagnostics.Stage("Assembly.Begin.inventory-complete");
            InvalidateAssemblyBackend();
            assemblySceneLease = new PlacementLease(); assemblyPoseState = AssemblyOccurrenceState();
            BendNativeCaptureDiagnostics.Stage("Assembly.Begin.pose-audit-complete");
            assemblyOperation = new AssemblyNativeBatchOperation(this, minimumSource, scope, options ?? new AssemblyBatchOptions(),
                assemblySceneLease, timer.Elapsed.TotalMilliseconds, token, progress, program);
            return assemblyOperation;
        }
        private bool CapturedAssemblyInputsCurrent()
        {
            if(!IsHostActive||IsAssemblyBackendEditing||Host.GetUpdateStamp()!=hostStamp)return false;
            foreach(var component in nativeInventoryReferences)
            {string old;if(!assemblyComponentAudit.TryGetValue(component,out old)||old!=AssemblyComponentState(component))return false;}
            return Items.All(i=>i.Current&&(i.PlacementCapture==null||i.PlacementCapture.Lease.Current&&i.Context?.Current==true&&i.Model!=null&&
                i.Model.GetUpdateStamp()==i.Stamp&&ConfigurationOf(i.Model)==i.Configuration&&i.Model.GetActiveSketch2()==null&&i.Model.Extension.NeedsRebuild2==0));
        }
        internal void PollAssemblyBackend()
        { PollAssemblyBackendCore(false); }
        private void PollAssemblyBackendCore(bool full)
        {
            RequireAssemblySta();
            if (assemblySceneLease == null || !assemblySceneLease.Current) return;
            try
            {
                bool changed = !IsHostActive || IsAssemblyBackendEditing || Host.GetUpdateStamp() != hostStamp;
                bool audit=full||DateTime.UtcNow>=nextAssemblyAudit;
                if(!changed&&audit)
                {
                    int limit=full?nativeInventoryReferences.Count:Math.Min(4,nativeInventoryReferences.Count);
                    for(int n=0;n<limit;n++)
                    {
                        var component=nativeInventoryReferences[assemblyAuditCursor++%nativeInventoryReferences.Count];string old;
                        if(!assemblyComponentAudit.TryGetValue(component,out old)||old!=AssemblyComponentState(component)){changed=true;break;}
                    }
                    nextAssemblyAudit=DateTime.UtcNow.AddMilliseconds(250);
                }
                var checkedItems=full?Items:!audit||Items.Count==0?new List<BendCheckItem>():new List<BendCheckItem>{Items[assemblyPartAuditCursor++%Items.Count]};
                if (!changed&&audit) foreach (var item in checkedItems.Where(i => i.Done && i.Model != null && i.PlacementCapture != null))
                {
                    if (!item.Current || item.Context?.Current == false || !item.PlacementCapture.Lease.Current ||
                        item.Model.GetUpdateStamp() != item.Stamp || ConfigurationOf(item.Model) != item.Configuration ||
                        item.Model.GetActiveSketch2() != null || item.Model.Extension.NeedsRebuild2 != 0)
                    { changed = true; break; }
                }
                if (changed) InvalidateAssemblyBackend(); else assemblyOperation?.PollFiles();
            }
            catch { InvalidateAssemblyBackend(); }
        }
        private void InvalidateAssemblyBackend()
        { assemblySceneLease?.Invalidate(); assemblyOperation?.Invalidate(); partOperation?.Invalidate(); }
        private void DisposeAssemblyBackend()
        { InvalidateAssemblyBackend(); assemblyCoordinator.Dispose(); processPrograms.Dispose(); nativeInventoryReferences.Clear(); }

        internal sealed class AssemblyNativeBatchOperation
        {
            private readonly BendCheckSession owner;
            private readonly BendToolCatalogReadResult minimum;
            private readonly AssemblyCheckScope scope;
            private readonly AssemblyBatchOptions options;
            private readonly PlacementLease lease;
            private readonly CancellationToken token;
            private readonly IProgress<AssemblyBatchProgress> progress;
            private readonly Func<string,PlacementModelCapture,AssemblyProgramData> programs;
            private readonly TaskCompletionSource<AssemblyBatchSnapshot> completion = new TaskCompletionSource<AssemblyBatchSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly double inventoryMs;
            private readonly string[] paths;
            private readonly List<AssemblySourceInput> sources = new List<AssemblySourceInput>();
            private Dictionary<string,string> revisions;
            private Task<Dictionary<string,string>> files;
            private Task<AssemblyBatchSnapshot> work;
            private Task<ToolProfileLibrarySnapshot> toolVerification;
            private Task processVerification;
            private Task<AssemblyProgramData> pendingProgram;
            private string pendingSourceKey;
            private int preparedIndex = -1;
            private DateTime nextFiles;
            private int index, captures, hits;
            private volatile bool nativeCaptureActive;
            internal bool NativeCaptureActive => nativeCaptureActive;
            private double captureMs;
            private bool validatingPublication;
            private readonly Stopwatch operationClock=Stopwatch.StartNew();
            public Task<AssemblyBatchSnapshot> Completion => completion.Task;
            public AssemblyBatchSnapshot Result { get; private set; }
            public double MaximumStaCaptureMilliseconds { get; private set; }
            public string Diagnostic { get; private set; }

            internal AssemblyNativeBatchOperation(BendCheckSession owner, BendToolCatalogReadResult minimum, AssemblyCheckScope scope,
                AssemblyBatchOptions options, PlacementLease lease, double inventoryMs, CancellationToken token,
                IProgress<AssemblyBatchProgress> progress, Func<string,PlacementModelCapture,AssemblyProgramData> programs)
            {
                this.owner=owner; this.minimum=minimum; this.scope=scope; this.options=options; this.lease=lease; this.inventoryMs=inventoryMs;
                this.token=token; this.progress=progress; this.programs=programs;
                // Only captured, eligible geometry is a file proof dependency.
                // A virtual/unsupported definition can materialize a temporary
                // side file while SOLIDWORKS changes activation. It already has
                // an independent UNKNOWN result and must not invalidate normal
                // siblings. Assembly stamp/pose and required source guards stay.
                paths=owner.Items.Where(i=>i.AssemblyOccurrences.Any(o=>AssemblyOccurrenceEligibility.Reason(o)==null))
                    .Select(i=>i.Path).Concat(new[]{owner.Host.GetPathName(),minimum?.Snapshot?.SourcePath})
                    .Where(p=>!string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                files=ReadFiles(paths); nextFiles=DateTime.UtcNow.AddSeconds(2);
            }
            // One existing atomic Min/native capture per Pump, yielding between
            // Parts. It is not a promise that a single COM capture takes <50ms.
            internal void Pump(int maximumCaptures = 1)
            {
                owner.RequireAssemblySta();
                if (maximumCaptures < 1) throw new ArgumentOutOfRangeException(nameof(maximumCaptures));
                owner.processPrograms.Poll();
                if (completion.Task.IsCompleted) { owner.toolLibrary.Poll();owner.PollAssemblyBackend(); return; }
                try
                {
                    owner.PollAssemblyBackend(); owner.toolLibrary.Poll();
                    if (token.IsCancellationRequested) { lease.Invalidate(); Diagnostic="CANCELLED"; }
                    if(operationClock.ElapsedMilliseconds>=options.Milliseconds){lease.Invalidate();Diagnostic="ASSEMBLY_NATIVE_TIME_BUDGET_EXCEEDED";}
                    if(!lease.Current&&revisions==null)revisions=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
                    if (revisions==null)
                    {
                        if (!files.IsCompleted) return;
                        revisions=files.GetAwaiter().GetResult(); files=null;
                        string expected=minimum?.Snapshot?.SourceSha256, path=minimum?.Snapshot?.SourcePath;
                        if (path!=null && (!revisions.ContainsKey(path)||revisions[path]!=expected))
                        { Diagnostic="STALE_MINIMUM_WORKBOOK"; lease.Invalidate(); }
                        foreach (var item in owner.Items)
                        {
                            string current,old;
                            if (item.Path!=null && revisions.TryGetValue(item.Path,out current))
                            {
                                if (owner.assemblyFileRevisions.TryGetValue(item.Path,out old)&&old!=current)
                                { owner.assemblySourcesRequireReload.Add(item.Path); }
                                if(owner.assemblySourcesRequireReload.Contains(item.Path))
                                { item.Invalidate(); Diagnostic="PART_BYTES_CHANGED_RELOAD_SOURCE_REQUIRED"; lease.Invalidate(); }
                                owner.assemblyFileRevisions[item.Path]=current;
                            }
                        }
                    }
                    if (work==null)
                    {
                        // Wait for the one shared tool read, not one parse per Part.
                        if (lease.Current&&owner.ToolLibrary.Error=="DXF source read pending.") return;
                        int captured=0;
                        while(index<owner.Items.Count && captured<maximumCaptures)
                        {
                            var item=owner.Items[index];
                            bool hasEligible=item.AssemblyOccurrences.Any(o=>AssemblyOccurrenceEligibility.Reason(o)==null);
                            if (preparedIndex != index)
                            {
                                var clock=Stopwatch.StartNew(); bool existing=item.Done;
                                if (lease.Current && !token.IsCancellationRequested && item.Error==null&&hasEligible)
                                {
                                    nativeCaptureActive=true;
                                    try { owner.Capture(item,minimum); }
                                    finally { nativeCaptureActive=false; }
                                }
                                if(existing&&lease.Current)item.Context?.SetSource(minimum);
                                if (existing && item.PlacementCapture!=null) hits++; else if (item.PlacementCapture!=null) captures++;
                                clock.Stop(); captureMs+=clock.Elapsed.TotalMilliseconds; MaximumStaCaptureMilliseconds=Math.Max(MaximumStaCaptureMilliseconds,clock.Elapsed.TotalMilliseconds);
                                string revision; revisions.TryGetValue(item.Path??"",out revision);
                                pendingSourceKey=ToolEvidenceCanonical.Hash("NativeAssemblySourceV1",item.Key,revision,item.Stamp.ToString(),minimum?.Snapshot?.RuntimeCatalogSha256);
                                preparedIndex=index;
                                pendingProgram=item.PlacementCapture==null||!lease.Current||!hasEligible?Task.FromResult<AssemblyProgramData>(null):
                                    programs!=null?Task.FromResult(programs(pendingSourceKey,item.PlacementCapture)):
                                    ReadProcessProgramAsync(owner.processPrograms,pendingSourceKey,item.Path,item.PlacementCapture,owner.ToolLibrary,token);
                            }
                            if (!pendingProgram.IsCompleted) return;
                            var model=item.PlacementCapture; var data=pendingProgram.GetAwaiter().GetResult(); string key=pendingSourceKey;
                            string sourceReason=item.Error??(model==null&&hasEligible?item.PlacementDiagnosticError??"MISSING_NATIVE_CAPTURE":null);
                            // Min preserves its file/config grouping; Batch separates
                            // healthy occurrences and individual unsupported instances.
                            var partition=AssemblyOccurrenceEligibility.Partition(key,model,item.AssemblyOccurrences,
                                data?.Envelopes,data?.Setups,data?.Plans,data?.Use,sourceReason,data?.ConditionsRevision,data?.Sequence,ReadMinimumMetadata(item));
                            foreach(var input in partition)input.ReadinessDiagnostics=data?.ReadinessDiagnostics??PlacementLists.Copy<string>(null);
                            sources.AddRange(partition);
                            BendCheckRunDiagnostics.Write("Assembly.Pump.capture.end",new {index,Rows=item.Rows.Count,item.Error,item.PlacementDiagnosticError,sourceReason},token:token);
                            index++; captured++;
                        }
                        if (index<owner.Items.Count) return;
                        owner.PollAssemblyBackendCore(true);
                        var request=new AssemblyBatchRequest(ToolEvidenceCanonical.Hash(owner.Host.GetPathName(),owner.hostConfiguration,owner.hostStamp.ToString(),owner.assemblyPoseState),
                            owner.hostConfiguration,scope,lease,owner.ToolLibrary,sources,owner.InventoryOccurrences,inventoryMs,captureMs,captures,hits);
                        BendCheckRunDiagnostics.Write("Assembly.Pump.worker.begin",new {Parts=sources.Count},token:token);
                        work=owner.assemblyCoordinator.RunAsync(request,options,token,progress); return;
                    }
                    if (!work.IsCompleted) return;
                    if(!lease.Current){Result=work.GetAwaiter().GetResult();completion.TrySetResult(Result);return;}
                    if (!validatingPublication) { files=ReadFiles(paths);toolVerification=owner.toolLibrary.VerifyForPublicationAsync();processVerification=owner.processPrograms.VerifyForPublicationAsync();validatingPublication=true; return; }
                    if (!files.IsCompleted || !toolVerification.IsCompleted || !processVerification.IsCompleted) return;
                    processVerification.GetAwaiter().GetResult();
                    CheckRevisions(files.GetAwaiter().GetResult()); files=null;
                    var verifiedLibrary=toolVerification.GetAwaiter().GetResult();
                    if(!ReferenceEquals(verifiedLibrary,owner.ToolLibrary)||!verifiedLibrary.Current)
                    {lease.Invalidate();Diagnostic="TOOL_SOURCES_CHANGED_BEFORE_PUBLICATION";}
                    owner.PollAssemblyBackendCore(true); Result=work.GetAwaiter().GetResult(); completion.TrySetResult(Result);
                }
                catch(Exception ex) { BendCheckRunDiagnostics.Write("Assembly.Pump.error",error:ex,token:token);BendNativeCaptureDiagnostics.Failure(ex,"Assembly.NativePump"); lease.Invalidate(); Diagnostic="ASSEMBLY_NATIVE_CAPTURE_FAILED:"+ex.GetType().Name; completion.TrySetException(ex); }
            }
            internal void Invalidate() { lease.Invalidate(); }
            internal void Cancel() { lease.Invalidate();owner.assemblyCoordinator.Cancel();Diagnostic="CANCELLED"; }
            internal void PollFiles()
            {
                if (revisions==null || validatingPublication && !completion.Task.IsCompleted) return;
                if (files!=null && files.IsCompleted)
                {
                    try { CheckRevisions(files.GetAwaiter().GetResult()); } catch { lease.Invalidate(); Diagnostic="SOURCE_FRESHNESS_READ_FAILED"; }
                    files=null; nextFiles=DateTime.UtcNow.AddSeconds(2);
                }
                if (files==null && DateTime.UtcNow>=nextFiles) files=ReadFiles(paths);
            }
            private void CheckRevisions(Dictionary<string,string> current)
            {
                if (revisions.Any(p=>!current.ContainsKey(p.Key)||current[p.Key]!=p.Value))
                { lease.Invalidate(); Diagnostic="SOURCE_FILE_CHANGED_DURING_OR_AFTER_BATCH"; }
            }
            internal static Task<Dictionary<string,string>> ReadFiles(string[] paths)
            {
                // This closure owns strings only, never owner/operation/COM.
                var task=Task.Run(()=> {
                    var result=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
                    foreach(string path in paths)
                    {
                        if(!File.Exists(path)){result.Add(path,"MISSING_SOURCE_FILE");continue;}
                        var before=new FileInfo(path);long length=before.Length,ticks=before.LastWriteTimeUtc.Ticks;
                        using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
                        using(var sha=SHA256.Create()) result.Add(path,BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant());
                        var after=new FileInfo(path);
                        if(after.Length!=length||after.LastWriteTimeUtc.Ticks!=ticks)throw new IOException("SOURCE_CHANGED_DURING_HASH");
                    }
                    return result;
                });
                task.ContinueWith(t=>{var observed=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);
                return task;
            }
        }
    }
}
