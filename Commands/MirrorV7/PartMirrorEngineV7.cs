using System;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
namespace ADDIN.Commands.MirrorV7
{
    public sealed class PartMirrorEngineV7
    {
        public const string Version = "V7-PHASE-6C1";
        public MirrorV7Context Context { get; private set; }
        public PartMirrorEngineV7(MirrorV7Context context) { if (context == null) throw new ArgumentNullException("context"); Context = context; }
        public void ValidateFoundation() { MirrorV7Diagnostics.LogPhase("PHASE1", "V7 foundation active. No geometry operation executed."); }

        public MirrorV7ModelGraph AuditFeatureTree(ModelDoc2 partDoc)
        {
            if (partDoc == null) throw new ArgumentNullException("partDoc");
            if (partDoc.GetType() != (int)swDocumentTypes_e.swDocPART)
                throw new InvalidOperationException("V7 Phase 2 requires a Part document.");
            Context.PartDoc = partDoc; Context.AuditOnly = true;
            bool dirtyBefore = SafeSaveFlag(partDoc); int countBefore = SafeFeatureCount(partDoc);
            MirrorV7Diagnostics.LogPhase("PHASE2", "Audit started. featureCountBefore=" + countBefore + " saveFlagBefore=" + dirtyBefore);
            MirrorV7ModelGraph graph = FeatureTreeScannerV7.Scan(partDoc); Context.Graph = graph;
            bool dirtyAfter = SafeSaveFlag(partDoc); int countAfter = SafeFeatureCount(partDoc);
            MirrorV7Diagnostics.LogPhase("PHASE2", "Audit completed. featureCountAfter=" + countAfter + " saveFlagAfter=" + dirtyAfter + " graphNodes=" + graph.Count);
            if (countAfter != countBefore) throw new InvalidOperationException("PHASE2 SAFETY FAILURE: Feature count changed during read-only audit.");
            if (dirtyAfter != dirtyBefore) throw new InvalidOperationException("PHASE2 SAFETY FAILURE: Part dirty/save flag changed during read-only audit.");
            return graph;
        }
        private static bool SafeSaveFlag(ModelDoc2 model) { return model.GetSaveFlag(); }
        private static int SafeFeatureCount(ModelDoc2 model) { return model.GetFeatureCount(); }
        public void AuditReflectionMathematics()
        {
            MirrorV7Diagnostics.LogPhase("PHASE3", "Reflection mathematics audit started.");
            ReflectionMathSelfTestV7.RunOrThrow();
            MirrorV7Diagnostics.LogPhase("PHASE3", "Reflection mathematics audit completed.");
        }
        public PersistentReferenceAuditResultV7 AuditPersistentReferences()
        {
            if (Context.PartDoc == null) throw new InvalidOperationException("PHASE4 PartDoc is null.");
            if (Context.Graph == null) throw new InvalidOperationException("PHASE4 Graph is null.");
            int before = SafeFeatureCount(Context.PartDoc); bool dirtyBefore = SafeSaveFlag(Context.PartDoc);
            MirrorV7Diagnostics.LogPhase("PHASE4", "Persistent reference audit started.");
            PersistentReferenceAuditResultV7 result = PersistentReferenceAuditV7.RunOrThrow(Context);
            int after = SafeFeatureCount(Context.PartDoc); bool dirtyAfter = SafeSaveFlag(Context.PartDoc);
            MirrorV7Diagnostics.LogPhase("PHASE4", "Persistent reference audit completed. featureCountBefore=" + before + " featureCountAfter=" + after + " saveFlagBefore=" + dirtyBefore + " saveFlagAfter=" + dirtyAfter);
            if (before != after) throw new InvalidOperationException("PHASE4 SAFETY FAILURE: Feature count changed.");
            if (dirtyBefore != dirtyAfter) throw new InvalidOperationException("PHASE4 SAFETY FAILURE: SaveFlag changed.");
            return result;
        }

        public void AuditBodyVerification()
        {
            if (Context.PartDoc == null) throw new InvalidOperationException("Body audit requires PartDoc.");
            int before = Context.PartDoc.GetFeatureCount(); bool dirty = Context.PartDoc.GetSaveFlag();
            try
            {
                PartDoc part = Context.PartDoc as PartDoc;
                object[] bodies = part == null ? null : part.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
                if (bodies == null || bodies.Length == 0) throw new InvalidOperationException("No solid body available for verifier self-test.");
                foreach (object body in bodies) BodyMirrorVerifierV7.SelfTest(body as Body2);
                MirrorV7Diagnostics.Log("[BODY_VERIFY] PASS bodies=" + bodies.Length + " scope=MEASUREMENT_MATH_ONLY");
            }
            finally
            {
                int after = Context.PartDoc.GetFeatureCount(); bool dirtyAfter = Context.PartDoc.GetSaveFlag();
                if (before != after || dirty != dirtyAfter) throw new InvalidOperationException("BODY_VERIFY safety invariant failed.");
            }
        }

        public SketchSnapshotAuditResultV7 AuditSketchSnapshots()
        {
            if (Context.PartDoc == null || Context.Graph == null) throw new InvalidOperationException("PHASE5 requires PartDoc and Graph.");
            Context.SketchSnapshots.Clear();
            return SketchSnapshotAuditV7.RunOrThrow(Context);
        }

        public WorkingCopyEquivalenceResultV7 AuditWorkingCopy(CanonicalPartMirrorPlaneV7 selectedPlane)
        {
            if (Context.PartDoc == null || Context.Graph == null) throw new InvalidOperationException("PHASE6A requires a completed source audit.");
            ModelDoc2 source = Context.PartDoc;
            Context.SelectedPlane = selectedPlane;
            PartCapabilityAuditV7.Run(Context);
            MirrorSourceBaselineV7 baseline = MirrorSourceBaselineV7.Capture(source);
            Context.SourceBaseline = baseline;
            MirrorWorkingCopyV7 working = null;
            Exception primary = null;
            try
            {
                working = MirrorWorkingCopyV7.Create(Context.SwApp, baseline);
                MirrorV7ModelGraph copyGraph = FeatureTreeScannerV7.Scan(working.Document);
                Context.WorkingDocument = working.Document;
                Context.WorkingGraph = copyGraph;
                Context.WorkingPath = working.WorkingPath;
                Context.WorkingRunId = working.RunId;
                CrossDocumentReferenceResultV7 refs = CrossDocumentReferenceAuditV7.Run(Context.Graph, working.Document);
                if (!refs.Success) throw new InvalidOperationException("PHASE6A cross-document reference audit failed.");
                WorkingCopyEquivalenceResultV7 result = WorkingCopyEquivalenceAuditV7.Run(Context, working.Document, copyGraph);
                if (!result.Success) throw new InvalidOperationException("PHASE6A working-copy equivalence audit failed.");
                int copyFeatureCountBefore = working.Document.GetFeatureCount();
                bool copyDirtyBefore = working.Document.GetSaveFlag();
                try
                {
                    MirrorV7Context copyContext = new MirrorV7Context { SwApp = Context.SwApp, PartDoc = working.Document, Graph = copyGraph, AuditOnly = true };
                    SketchMirrorPlanV7 plan = SketchMirrorPlannerV7.Build(copyContext, selectedPlane);
                    Context.SketchMirrorPlan = plan;
                result.Plan = plan;
                result.PlanBuilt = true;
                    result.WorkingPath = working.WorkingPath;
                    result.RunId = working.RunId;
                    MirrorV7Diagnostics.Log("[PHASE6B][PLAN_BUILT] runId=" + working.RunId + " workingCopy=\"" + working.WorkingPath + "\" sketches=" + plan.Items.Count + " mutationEnabled=False");
                }
                finally
                {
                    int copyFeatureCountAfter = working.Document.GetFeatureCount();
                    bool copyDirtyAfter = working.Document.GetSaveFlag();
                    MirrorV7Diagnostics.Log("[PHASE6B][COPY_SAFETY] featureCountBefore=" + copyFeatureCountBefore + " featureCountAfter=" + copyFeatureCountAfter + " saveFlagBefore=" + copyDirtyBefore + " saveFlagAfter=" + copyDirtyAfter);
                    if (copyFeatureCountBefore != copyFeatureCountAfter || copyDirtyBefore != copyDirtyAfter)
                        throw new InvalidOperationException("PHASE6B safety failure: Working copy changed.");
                }
                MirrorV7Diagnostics.Log("[PHASE6A][PASS] runId=" + working.RunId + " workingCopy=\"" + working.WorkingPath + "\" openErrors=" + working.OpenErrors + " openWarnings=" + working.OpenWarnings);
                return result;
            }
            catch (Exception ex) { primary = ex; MirrorV7Diagnostics.LogException("PHASE6A", ex); throw; }
            finally
            {
                try { baseline.AssertUnchanged(source); }
                catch (Exception safety)
                {
                    MirrorV7Diagnostics.LogException("PHASE6A_SOURCE_SAFETY", safety);
                    if (primary == null) throw;
                    MirrorV7Diagnostics.Log("[PHASE6A][PRIMARY_AND_SAFETY_FAILURE] primary=" + primary.Message + " safety=" + safety.Message);
                }
            }
        }
    }
}
