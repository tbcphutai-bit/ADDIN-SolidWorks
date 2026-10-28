using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ADDIN.Commands.BendTools
{
    // Managed source I/O only. Generations cover both polling and publication.
    internal sealed class ToolProfileLibrarySession : IDisposable
    {
        private readonly ToolProfileLibraryProvider provider;
        private readonly CancellationTokenSource cancellation=new CancellationTokenSource();
        private readonly object gate=new object();
        private readonly List<Task> readers=new List<Task>();
        private Task<ToolProfileLibrarySnapshot> pending;
        private DateTime next=DateTime.MinValue;
        private string configurationStamp=ToolProfileSourceSettings.QuickConfigurationStamp();
        private string lastReport;
        private int generation,pendingGeneration;
        private bool disposed;
        internal ToolProfileLibrarySnapshot Snapshot { get; private set; } = ToolProfileLibraryProvider.Failure("DXF source read pending.");
        internal string DiagnosticError { get; private set; }
        internal ToolProfileLibrarySession() : this(new ToolProfileLibraryProvider()) { }
        internal ToolProfileLibrarySession(ToolProfileLibraryProvider provider) { this.provider=provider??throw new ArgumentNullException(nameof(provider)); }
        internal void Refresh()
        {
            lock(gate)
            {
                if(disposed)return;
                // Explicit Refresh revokes old evidence, including pending reads.
                generation++;Snapshot?.Invalidate();Snapshot=ToolProfileLibraryProvider.Failure("DXF source read pending.");next=DateTime.MinValue;
                if(pending==null)StartRead();
            }
        }
        private Task<ToolProfileLibrarySnapshot> Read(int ticket,string stamp)
        {
            var token=cancellation.Token;
            var task=Task.Run(()=>{
                lock(gate)if(disposed||ticket!=generation||token.IsCancellationRequested)
                    return ToolProfileLibraryProvider.Failure("SOURCE_READ_GENERATION_SUPERSEDED");
                var value=provider.Read(token);
                bool valid=value?.EffectiveSources?.Validate(token)!=false;
                lock(gate)
                {
                    if(disposed||ticket!=generation||token.IsCancellationRequested||!valid||
                        stamp!=ToolProfileSourceSettings.QuickConfigurationStamp())
                    {
                        // Provider snapshots are shared. An obsolete audit may
                        // have received the NEW generation's same-source value;
                        // it may reject its own result, never revoke that value.
                        if(!valid||stamp!=ToolProfileSourceSettings.QuickConfigurationStamp())value?.Invalidate();
                        return ToolProfileLibraryProvider.Failure("SOURCE_CHANGED_OR_CANCELLED_DURING_READ");
                    }
                    return value;
                }
            },token);
            readers.RemoveAll(t=>t.IsCompleted);
            readers.Add(task);
            task.ContinueWith(t=>{var observed=t.Exception;},CancellationToken.None,TaskContinuationOptions.OnlyOnFaulted,TaskScheduler.Default);
            return task;
        }
        private void StartRead()
        {
            pendingGeneration=generation;
            pending=Read(pendingGeneration,ToolProfileSourceSettings.QuickConfigurationStamp());
        }
        internal Task<ToolProfileLibrarySnapshot> VerifyForPublicationAsync()
        {
            lock(gate)
            {
                if(disposed)throw new ObjectDisposedException(nameof(ToolProfileLibrarySession));
                return Read(generation,ToolProfileSourceSettings.QuickConfigurationStamp());
            }
        }
        internal void Poll()
        {
            lock(gate)
            {
                if(disposed)return;
                string observed=ToolProfileSourceSettings.QuickConfigurationStamp();
                if(observed!=configurationStamp||Snapshot?.EffectiveSources?.Current==false)
                {
                    configurationStamp=observed;generation++;Snapshot?.Invalidate();
                    Snapshot=ToolProfileLibraryProvider.Failure("DXF source read pending.");next=DateTime.MinValue;
                }
                if(pending!=null&&pending.IsCompleted)
                {
                    var task=pending;pending=null;
                    // Recheck after completion so a late watcher revocation or
                    // Refresh cannot publish the just-completed stale result.
                    bool changed=pendingGeneration!=generation||task.IsCanceled;
                    var value=task.IsFaulted ? ToolProfileLibraryProvider.Failure(task.Exception.GetBaseException().Message) :
                        task.IsCanceled ? null : task.Result;
                    if(changed||value?.Current==false)
                    {
                        next=changed?DateTime.MinValue:DateTime.UtcNow.AddSeconds(12);
                        Snapshot=ToolProfileLibraryProvider.Failure(changed?"DXF source read pending.":value?.Error??"SOURCE_CHANGED_OR_CANCELLED_DURING_READ");
                    }
                    else if(value!=null)
                    {
                        if(!ReferenceEquals(Snapshot,value))Snapshot?.Invalidate();Snapshot=value;
                        if(lastReport!=value.Fingerprint)
                        {
                            lastReport=value.Fingerprint;
                            try { Debug.WriteLine("[BEND TOOL LIBRARY] "+value.ToDebugJson());DiagnosticError=null; }
                            catch(Exception ex) { DiagnosticError=ex.Message; }
                        }
                        next=DateTime.UtcNow.AddSeconds(value.Error=="DXF source directory is not configured."?30:value.Error!=null?12:6);
                    }
                }
                if(pending==null&&DateTime.UtcNow>=next)StartRead();
            }
        }
        internal void RecordDiagnosticFailure(Exception ex)
        {
            lock(gate){DiagnosticError=ex.Message;generation++;Snapshot?.Invalidate();
                Snapshot=ToolProfileLibraryProvider.Failure("DXF diagnostic update failed: "+ex.Message);next=DateTime.UtcNow.AddSeconds(12);}
        }
        public void Dispose()
        {
            Task[] outstanding;
            lock(gate)
            {
                if(disposed)return;disposed=true;generation++;Snapshot?.Invalidate();cancellation.Cancel();outstanding=readers.ToArray();
            }
            // Do not block the STA or dispose tokens before publication audits end.
            Task.WhenAll(outstanding).ContinueWith(t=>{
                var observed=t.Exception;provider.Dispose();cancellation.Dispose();
            },CancellationToken.None,TaskContinuationOptions.None,TaskScheduler.Default);
        }
    }
}
