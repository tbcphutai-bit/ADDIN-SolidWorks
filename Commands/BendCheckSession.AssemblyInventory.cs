using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using ADDIN.Commands.BendAssembly;
using ADDIN.Commands.BendPlacement;
using ADDIN.Commands.BendTools;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands
{
    internal sealed partial class BendCheckSession
    {
        internal readonly List<AssemblyOccurrence> InventoryOccurrences = new List<AssemblyOccurrence>();
        internal int BodyMetadataReads { get; private set; }
        internal int SelectiveResolveCount => 0;
        private bool? assemblyInventorySelectedOnly;
        private readonly List<Component2> nativeInventoryReferences = new List<Component2>();
        private readonly Dictionary<Component2,string> assemblyComponentAudit = new Dictionary<Component2,string>();
        private void RequireAssemblySta()
        {if(Thread.CurrentThread.GetApartmentState()!=ApartmentState.STA)throw new InvalidOperationException("Assembly inventory/capture requires STA");}
        private void ResetAssemblyInventory()
        {RequireAssemblySta();InvalidateAssemblyBackend();InventoryOccurrences.Clear();nativeInventoryReferences.Clear();assemblyComponentAudit.Clear();assemblyInventorySelectedOnly=null;BodyMetadataReads=0;}
        private string ComponentIdentity(Component2 component)
        {
            var chain=new List<string>();var current=component;int depth=0;
            while(current!=null&&depth++<128)
            {
                chain.Add(current.GetID().ToString(System.Globalization.CultureInfo.InvariantCulture)+":"+current.Name2+":"+current.ReferencedConfiguration);
                current=current.GetParent() as Component2;
            }
            if(depth>=128)throw new InvalidOperationException("Component parent chain is not bounded");
            chain.Reverse();string persistent=null;
            try{var bytes=Host.Extension.GetPersistReference3(component) as byte[];if(bytes?.Length>0)persistent=Convert.ToBase64String(bytes);}catch{ }
            return ToolEvidenceCanonical.Hash("AssemblyOccurrenceV1",Host.GetPathName(),hostConfiguration,string.Join("/",chain),persistent);
        }
        private static double[] ComponentTransform(Component2 component)
        {return (component.GetTotalTransform(false)?.ArrayData as double[])??new double[0];}
        private static bool Rigid(double[] t) => AssemblyOccurrence.RigidTransform(t);
        private static bool HasFlexibleAncestor(Component2 component)
        {
            // Selected roots may be descendants, so recursion alone cannot
            // establish their inherited assembly context.
            var parent=component.GetParent() as Component2;int depth=0;
            while(parent!=null)
            {
                if(++depth>128)throw new InvalidOperationException("Unbounded assembly ancestry");
                if(parent.Solving==(int)swComponentSolvingOption_e.swComponentFlexibleSolving)return true;
                parent=parent.GetParent() as Component2;
            }
            return false;
        }
        private static bool HasLiveExternalGeometry(ModelDoc2 model)
        {
            int count=model.Extension.ListExternalFileReferencesCount();if(count==0)return false;
            object models,components,features,types,status,entities,featCom,configOptions,configNames;
            model.Extension.ListExternalFileReferences2(out models,out components,out features,out types,out status,out entities,out featCom,out configOptions,out configNames);
            var states=status as Array;
            // Broken references are explicitly disconnected by SOLIDWORKS;
            // locked/out-of-context/dangling references still cannot establish
            // a context-independent source. Never break references here.
            return states==null||states.Length!=count||states.Cast<object>().Any(s=>Convert.ToInt32(s)!=(int)swExternalReferenceStatus_e.swExternalReferenceBroken);
        }
        // Diagnostics describe the COM return shape, never source geometry.
        // A scalar/null metadata result is not evidence that a body is normal.
        private static object AssemblyArrayShape(object value)
        {
            var array=value as Array;
            return new {ClrType=value?.GetType().FullName??"null",Rank=array?.Rank,
                Length=array?.Length,LowerBounds=array==null?null:Enumerable.Range(0,array.Rank).Select(array.GetLowerBound).ToArray(),
                Dimensions=array==null?null:Enumerable.Range(0,array.Rank).Select(array.GetLength).ToArray()};
        }
        private void VisitAssemblyInventory(Component2 component,HashSet<string> visited,bool flexibleParent)
        {
            BendNativeCaptureDiagnostics.Stage("Assembly.Inventory.visit." + visited.Count);
            RequireAssemblySta();string id=ComponentIdentity(component);if(!visited.Add(id))return;
            nativeInventoryReferences.Add(component);
            string name=component.Name2,path=component.GetPathName(),config=component.ReferencedConfiguration;
            var parent=component.GetParent() as Component2;string parentId=parent==null?null:ComponentIdentity(parent);
            double[] transform=ComponentTransform(component);int suppression=component.GetSuppression2();
            bool mirrored=component.IsMirrored(),virtualPart=component.IsVirtual,flexible=flexibleParent||component.Solving==(int)swComponentSolvingOption_e.swComponentFlexibleSolving||HasFlexibleAncestor(component);
            string source=ToolEvidenceCanonical.Hash("AssemblySourcePathConfigV1",string.IsNullOrWhiteSpace(path)?id:path.ToUpperInvariant(),config);
            bool subassembly=string.Equals(Path.GetExtension(path),".SLDASM",StringComparison.OrdinalIgnoreCase);
            Func<AssemblyOccurrenceKind,bool,bool,string,AssemblyOccurrence> record=(entryKind,share,entryModified,why)=>new AssemblyOccurrence(id,parentId,name,source,config,transform,entryKind,share,mirrored,entryModified,virtualPart,flexible,why,subassembly);
            if(suppression==(int)swComponentSuppressionState_e.swComponentSuppressed)
            {SuppressedCount++;InventoryOccurrences.Add(record(AssemblyOccurrenceKind.Suppressed,false,false,"SUPPRESSED_COMPONENT"));return;}
            if(suppression==(int)swComponentSuppressionState_e.swComponentLightweight||suppression==(int)swComponentSuppressionState_e.swComponentFullyLightweight)
            {
                // No SetSuppression2 / resolve-all / model/config activation.
                // Resolving changes document state; exact rollback is not a
                // read-only guarantee. A caller may resolve needed components
                // explicitly and run again. Never use saved Part as instance proof.
                var entry=record(AssemblyOccurrenceKind.Lightweight,false,false,subassembly?"LIGHTWEIGHT_SUBASSEMBLY_SCOPE_UNRESOLVED":"LIGHTWEIGHT_NEEDS_EXPLICIT_USER_RESOLVE");
                BendCheckRunDiagnostics.Write("Assembly.Inventory.skipped",new {OccurrenceId=id,Component=name,SourceFile=Path.GetFileName(path),
                    ReferencedConfiguration=config,Suppression=suppression,Reason=entry.Reason});
                InventoryOccurrences.Add(entry);if(!subassembly)AddAssemblyItem(component,null,path,config,id,entry,false,entry.Reason);return;
            }
            if(suppression!=(int)swComponentSuppressionState_e.swComponentFullyResolved&&suppression!=(int)swComponentSuppressionState_e.swComponentResolved)
            {var entry=record(AssemblyOccurrenceKind.Unloaded,false,false,"UNRESOLVED_SUPPRESSION_OR_ID_STATE");InventoryOccurrences.Add(entry);AddAssemblyItem(component,null,path,config,id,entry,false,entry.Reason);return;}
            var model=component.GetModelDoc2() as ModelDoc2;
            if(model!=null)subassembly=model.GetType()==(int)swDocumentTypes_e.swDocASSEMBLY;
            if(subassembly)
            {
                var children=component.GetChildren() as object[];
                InventoryOccurrences.Add(record(AssemblyOccurrenceKind.Subassembly,false,false,children==null?"SUBASSEMBLY_CHILD_INVENTORY_UNVERIFIED":flexible?"FLEXIBLE_CONTEXT_ISOLATED":null));
                if(children==null)return;
                foreach(var child in children.OfType<Component2>())VisitAssemblyInventory(child,visited,flexible);return;
            }
            bool modified=false,shareable=false;string error=null,minimumError=null;var kind=AssemblyOccurrenceKind.SheetMetal;
            if(model==null){error="UNLOADED_COMPONENT";kind=AssemblyOccurrenceKind.Unloaded;}
            else
            {
                object rawBodies=null,info=null;
                string metadataIssue=null;
                try
                {
                    // Diagnostics use the captured component configuration; no
                    // extra native configuration query can veto body eligibility.
                    BendCheckRunDiagnostics.Write("Assembly.Inventory.body.begin",new {OccurrenceId=id,Component=name,SourceFile=Path.GetFileName(path),
                        ReferencedConfiguration=config,ModelConfigurationQuery="DeferredToNativeCapture",Suppression=suppression});
                    rawBodies=component.GetBodies3((int)swBodyType_e.swSolidBody,out info);BodyMetadataReads++;
                    // SAFEARRAY lower bounds need not be zero. Enumerate a
                    // documented one-dimensional array without discarding any
                    // unrecognized element before matching its metadata.
                    var nativeBodies=rawBodies as Array;
                    var entries=nativeBodies!=null&&nativeBodies.Rank==1?nativeBodies.Cast<object>().ToArray():new object[0];
                    var bodies=entries.OfType<Body2>().ToArray();
                    var flags=info as Array;
                    bool bodyArrayKnown=nativeBodies!=null&&nativeBodies.Rank==1&&entries.Length==bodies.Length;
                    bool hasUserBody;
                    string flagProblem=AssemblyOccurrenceEligibility.BodyMetadataProblem(info,bodies.Length,
                        (int)swBodyInfo_e.swNormalBody_e,(int)swBodyInfo_e.swUserBody_e,out hasUserBody);
                    object[] flagEntries=flags!=null&&flags.Rank==1?flags.Cast<object>().ToArray():new object[0];
                    if(nativeBodies==null)metadataIssue=rawBodies==null?"BODY_RETURN_NULL":"BODY_RETURN_NOT_ARRAY";
                    else if(nativeBodies.Rank!=1)metadataIssue="BODY_ARRAY_RANK_UNSUPPORTED";
                    else if(entries.Length!=bodies.Length)metadataIssue="BODY_ARRAY_CONTAINS_UNRECOGNIZED_ENTRIES";
                    else if(bodies.Length==0)metadataIssue="EMPTY_SOLID_BODY_ARRAY";
                    else metadataIssue=flagProblem;
                    bool? singleBodySheetMetal=bodies.Length==1?(bool?)bodies[0].IsSheetMetal():null;
                    BendCheckRunDiagnostics.Write("Assembly.Inventory.body.result",new {OccurrenceId=id,Component=name,SourceFile=Path.GetFileName(path),
                        ReferencedConfiguration=config,ModelConfigurationQuery="DeferredToNativeCapture",Suppression=suppression,
                        Bodies=AssemblyArrayShape(rawBodies),BodyInfo=AssemblyArrayShape(info),ReturnedEntries=entries.Length,
                        Body2Count=bodies.Length,DiscardedEntries=entries.Length-bodies.Length,
                        BodyEntryClrTypes=entries.Select(v=>v?.GetType().FullName??"null").ToArray(),
                        BodyInfoElementTypes=flagEntries.Select(v=>v?.GetType().FullName??"null").ToArray(),
                        BodyInfoValues=flagProblem==null?flagEntries.Select(v=>Convert.ToInt32(v)).ToArray():null,
                        SingleBodySheetMetal=singleBodySheetMetal,MetadataIssue=metadataIssue});
                    if(flags==null)minimumError="Chưa đọc được body trong Assembly.";
                    if(!bodyArrayKnown||flagProblem!=null||bodies.Length==0)error="UNKNOWN_ASSEMBLY_BODY_METADATA";
                    else modified=hasUserBody;
                    if(bodies.Length>1)error="MULTIBODY_INSTANCE_UNSUPPORTED";
                    else if(singleBodySheetMetal==false){kind=AssemblyOccurrenceKind.NonSheetMetal;error="NON_SHEET_METAL_PART";}
                    if(modified){error="ASSEMBLY_MODIFIED_BODY_REQUIRES_INSTANCE_GEOMETRY";kind=AssemblyOccurrenceKind.Unsupported;minimumError="Body có sửa trong Assembly; cần kiểm tra hình học instance.";}
                    else if(mirrored||!Rigid(transform))error="MIRRORED_OR_UNVERIFIED_INSTANCE_TRANSFORM";
                    else if(flexible)error="FLEXIBLE_ASSEMBLY_CONTEXT_UNVERIFIED";
                    else if(HasLiveExternalGeometry(model))error="IN_CONTEXT_OR_DERIVED_SOURCE_UNVERIFIED";
                    if(error!=null&&kind==AssemblyOccurrenceKind.SheetMetal)kind=AssemblyOccurrenceKind.Unsupported;
                    // An open unsaved source still has a reliable native stamp;
                    // Capture checks that stamp/config and its lease. A dirty
                    // source never permits the protected alternate-config copy.
                    shareable=error==null&&!virtualPart&&!string.IsNullOrWhiteSpace(path);
                }
                catch(Exception ex)
                {
                    BendCheckRunDiagnostics.Write("Assembly.Inventory.body.error",new {OccurrenceId=id,Component=name,SourceFile=Path.GetFileName(path),
                        ReferencedConfiguration=config,ModelConfigurationQuery="DeferredToNativeCapture",Suppression=suppression,
                        Bodies=AssemblyArrayShape(rawBodies),BodyInfo=AssemblyArrayShape(info),MetadataIssue=metadataIssue},ex);
                    error="ASSEMBLY_BODY_PROOF_FAILED:"+ex.GetType().Name;kind=AssemblyOccurrenceKind.Unsupported;minimumError="Chưa đối chiếu body Assembly: "+ex.Message;
                }
            }
            BendCheckRunDiagnostics.Write("Assembly.Inventory.classified",new {OccurrenceId=id,Component=name,SourceFile=Path.GetFileName(path),
                ReferencedConfiguration=config,Suppression=suppression,Kind=kind.ToString(),Modified=modified,
                Mirrored=mirrored,Flexible=flexible,Shareable=shareable,Reason=error,MinimumError=minimumError});
            BendNativeCaptureDiagnostics.Stage("Assembly.Inventory.proof-complete." + visited.Count);
            var occurrence=record(kind,shareable,modified,error);InventoryOccurrences.Add(occurrence);
            AddAssemblyItem(component,model,path,config,id,occurrence,modified,error,minimumError);
        }
        private void AddAssemblyItem(Component2 component,ModelDoc2 model,string path,string config,string id,AssemblyOccurrence occurrence,bool modified,string error,string minimumError=null)
        {
            if(model==null)minimumError=minimumError??error;
            // Preserve the existing Min grouping/error contract. A new Max/
            // process restriction must not turn a successful Min result into
            // Error or change the Min UI's three file/config rows into four.
            string key=BendCheckItem.GroupKey(path,config,id,modified||minimumError!=null);
            var item=Items.FirstOrDefault(i=>i.Key==key);
            if(item==null)
            {
                item=new BendCheckItem {Key=key,Path=path,Name=string.IsNullOrEmpty(path)?occurrence.DisplayPath:Path.GetFileNameWithoutExtension(path),
                    Configuration=config,Model=model,AssemblyBodyModified=modified,Error=minimumError,AssemblyBatchReason=error};Items.Add(item);
            }
            item.Instances.Add(component);item.AssemblyOccurrences.Add(occurrence);item.Quantity++;
            // This field is only a group diagnostic, not an occurrence veto.
            item.AssemblyBatchReason=AssemblyOccurrenceEligibility.GroupReason(item.AssemblyOccurrences);
        }
        private string AssemblyOccurrenceState()
        {
            RequireAssemblySta();var values=new List<string>();
            foreach(var component in nativeInventoryReferences)
            {string state=AssemblyComponentState(component);values.Add(state);assemblyComponentAudit[component]=state;}
            return ToolEvidenceCanonical.Hash(values.OrderBy(x=>x,StringComparer.Ordinal).ToArray());
        }
        private string AssemblyComponentState(Component2 component) => ToolEvidenceCanonical.Hash(ComponentIdentity(component),component.GetSuppression2().ToString(),component.ReferencedConfiguration,
            ToolEvidenceCanonical.Hash(ComponentTransform(component).Select(ToolEvidenceCanonical.Number).ToArray()),
            component.IsMirrored().ToString(),component.IsVirtual.ToString(),component.Solving.ToString(),HasFlexibleAncestor(component).ToString());
    }
}
