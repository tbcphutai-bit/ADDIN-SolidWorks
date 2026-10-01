using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands.MirrorV7.MirrorInPlace
{
    public static class MirrorInPlaceOrchestratorV7
    {
        public const string Version = "V7-PARAMETRIC-PREFLIGHT-66-NATIVE-PROFILE-REFERENCES";

        // Reuse the original cache/diagnose/replay pipeline without inventing an
        // assembly component or changing the user's reflection plane.
        public static MirrorInPlacePreparationResultV7 ExecuteLegacyPart(ISldWorks app, ModelDoc2 source,
            CanonicalPartMirrorPlaneV7 plane, string requestedOutputPath)
        {
            if (app == null) throw new ArgumentNullException("app");
            if (source == null || source.GetType() != (int)swDocumentTypes_e.swDocPART)
                throw new InvalidOperationException("Mirror requires a saved Part.");
            var baseline = MirrorSourceBaselineV7.Capture(source);
            requestedOutputPath = Path.GetFullPath(requestedOutputPath);
            if (!string.Equals(Path.GetExtension(requestedOutputPath), ".SLDPRT", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("File ket qua phai co duoi .SLDPRT: " + requestedOutputPath);
            if (string.Equals(requestedOutputPath, baseline.SourcePath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Khong duoc ghi de Part goc. Hay chon ten file mirror moi.");
            if (File.Exists(requestedOutputPath))
                throw new InvalidOperationException("File ket qua da ton tai. Hay chon ten moi: " + requestedOutputPath);

            string runDirectory = Path.Combine(Path.GetDirectoryName(requestedOutputPath),
                "Mirror_Diagnostics", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(runDirectory);
            string candidatePath = Path.Combine(runDirectory, "Mirror_Candidate.SLDPRT");
            string reportPath = Path.Combine(runDirectory, "MirrorVerification.xml");
            bool unsafeNativeFailure66 = false;
            try
            {
                MirrorV7Diagnostics.Log("[INPLACE][START] version=" + Version +
                    " mode=STANDALONE_LEGACY_CORE plane=" + plane);
                var graph = FeatureTreeScannerV7.Scan(source);
                foreach (var node in graph.Nodes)
                    if (node != null && node.Role != MirrorV7FeatureRole.System)
                        node.PersistentReference = PersistentReferenceServiceV7.Capture(
                            source, node.Feature, node.Name, "INPLACE_SOURCE_FEATURE");
                var context = new MirrorInPlaceExecutionContextV7
                {
                    SwApp = app, SourceDocument = source, WorkingDocument = source,
                    SourceBaseline = baseline, SourceGraph = graph,
                    Reflection = PartReflectionTransformV7.CreateCanonical(plane),
                    Plane = plane, RequestedOutputPath = requestedOutputPath
                };
                var oracle = BaseSketchMutationEngineV7.ReflectCurrentSolids(context);
                var result = SheetMetalMirrorServiceV6.ExecuteV6MirrorPipeline(app, null, null,
                    new ExplicitSavePathProvider(candidatePath), source,
                    new PlaneData { Origin = context.Reflection.Origin, Normal = context.Reflection.Normal },
                    copy =>
                    {
                        context.WorkingDocument = copy;
                        BaseSketchMutationEngineV7.VerifyCurrentSolids(context, oracle);
                        ValidateCompleteFeatureTree(context, copy);
                        baseline.AssertUnchanged(source);
                    }, (replacement, copy) => RegisterNativeReplacement(context, replacement, copy),
                    replay => ImportFlangeProfileMappings50(context, replay));
                if (!result.Success)
                    throw new InvalidOperationException(result.Message ?? "Legacy feature replay did not produce a verified Part.");
                context.WorkingPath = result.MirrorPartPath;
                VerifyPublishedOutput(app, context, oracle);
                baseline.AssertUnchanged(source);
                new XDocument(new XElement("MirrorVerification",
                    new XAttribute("version", Version), new XAttribute("status", "VERIFIED_BEFORE_PUBLICATION"),
                    new XElement("Source", baseline.SourcePath), new XElement("Output", requestedOutputPath),
                    new XElement("Plane", plane), new XElement("Geometry", "PASS"),
                    new XElement("FeatureIdentity", "PASS"), new XElement("Independence", "PASS")))
                    .Save(reportPath);
                File.Copy(candidatePath, requestedOutputPath, false);
                MirrorV7Diagnostics.Log("[INPLACE][OUTPUT_PUBLISHED] path=" + requestedOutputPath);
                return new MirrorInPlacePreparationResultV7
                {
                    WorkingPath = candidatePath, ReportPath = reportPath, RequestedOutputPath = requestedOutputPath,
                    FeatureCount = graph.Nodes.Count, SourceUnchanged = true, MutationExecuted = true,
                    BaseGeometryVerified = true, OutputPublished = true
                };
            }
            catch (Exception ex)
            {
                unsafeNativeFailure66 = RollbackReplayEngineV7.IsUnsafeNativeFailure66(ex);
                MirrorV7Diagnostics.Log("[INPLACE][LEGACY_CORE_FAILED] outputNotPublished=True reason=" + ex.Message +
                    " diagnostics=" + runDirectory);
                throw;
            }
            finally
            {
                if (unsafeNativeFailure66)
                    MirrorV7Diagnostics.Log("[INPLACE66][NATIVE_STOP] cleanupCadCalls=0 sourceLiveAuditSkipped=True sourceFileWrites=0");
                else
                {
                    Activate(app, source);
                    baseline.AssertUnchanged(source);
                }
            }
        }

        public static MirrorInPlacePreparationResultV7 Prepare(ISldWorks app, ModelDoc2 source,
            CanonicalPartMirrorPlaneV7 plane, string requestedOutputPath)
        {
            if (app == null) throw new ArgumentNullException("app");
            if (source == null || source.GetType() != (int)swDocumentTypes_e.swDocPART)
                throw new InvalidOperationException("In-place mirror requires a saved Part document.");

            MirrorSourceBaselineV7 baseline = MirrorSourceBaselineV7.Capture(source);
            requestedOutputPath = Path.GetFullPath(requestedOutputPath);
            if (!string.Equals(Path.GetExtension(requestedOutputPath), ".SLDPRT", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(requestedOutputPath, baseline.SourcePath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Output must be a new .SLDPRT path and cannot overwrite the source Part.");
            if (File.Exists(requestedOutputPath))
                throw new InvalidOperationException("Output already exists. Choose a new file name.");

            MirrorWorkingCopyV7 working = null;
            ModelDoc2 copy = null;
            string reportPath = null;
            bool outputCreatedByRun = false;
            bool unsafeNativeFailure66 = false;
            try
            {
                MirrorV7Diagnostics.Log("[INPLACE][START] version=" + Version + " source=\"" + baseline.SourcePath +
                    "\" plane=" + plane + " mirrorPartApi=False mirrorBodyApi=False");
                MirrorV7ModelGraph sourceGraph = FeatureTreeScannerV7.Scan(source);
                // The scanner deliberately does not retain document-scoped identity data.
                // Freeze it now, while every node still belongs to the source document, so
                // the byte-for-byte staging copy can be matched without using localized names.
                foreach (MirrorV7FeatureNode node in sourceGraph.Nodes)
                {
                    if (node == null || node.Role == MirrorV7FeatureRole.System) continue;
                    node.PersistentReference = PersistentReferenceServiceV7.Capture(
                        source, node.Feature, node.Name, "INPLACE_SOURCE_FEATURE");
                }
                BaseFeaturePrescriptionV7 prescription = BaseFeatureDiagnosticEngineV7.Analyze(sourceGraph);
                working = MirrorWorkingCopyV7.Create(app, baseline);
                copy = working.Document;
                Activate(app, copy);
                MirrorV7ModelGraph workingGraph = FeatureTreeScannerV7.Scan(copy);
                CrossDocumentReferenceResultV7 identity = CrossDocumentReferenceAuditV7.Run(sourceGraph, copy);
                if (!identity.Success)
                    throw new InvalidOperationException("The staging Part does not preserve source feature identities.");

                MirrorInPlaceExecutionContextV7 context = new MirrorInPlaceExecutionContextV7
                {
                    SwApp = app,
                    SourceDocument = source,
                    WorkingDocument = copy,
                    SourceBaseline = baseline,
                    SourceGraph = sourceGraph,
                    WorkingGraph = workingGraph,
                    Reflection = PartReflectionTransformV7.CreateCanonical(plane),
                    Plane = plane,
                    WorkingPath = working.WorkingPath,
                    RequestedOutputPath = requestedOutputPath,
                    BasePrescription = prescription
                };
                // Capture the complete source result before moving the rollback bar.  This is
                // the authoritative publication oracle: downstream features are allowed to
                // follow their reflected parents naturally, but a file is never published
                // unless the complete rebuilt body set is the true opposite hand.
                List<Body2> finalBodyOracle = BaseSketchMutationEngineV7.ReflectCurrentSolids(context);
                var sequence = RollbackReplayEngineV7.CaptureAll(context);
                if (!prescription.SupportedBaseType)
                    throw new InvalidOperationException("Base feature is not supported for editable in-place reflection: " +
                        prescription.FeatureName + " [" + prescription.FeatureType + "].");
                context.BaseMutation = BaseSketchMutationEngineV7.Execute(context);
                if (context.BaseMutation == null || !context.BaseMutation.BaseGeometryVerified)
                    throw new InvalidOperationException("Reflected Base feature did not pass its geometry oracle.");

                FeatureReplayJournalV7 journal = RollbackReplayEngineV7.ExecuteAll(context, sequence);
                SemanticValidatorV7.AssertPublishable(journal);
                foreach (FeatureReplayCheckpointV7 item in sequence.Where(x => x.WasNativeReplacement))
                    RegisterNativeReplacement(context, item.Feature, context.WorkingDocument);
                foreach (var item in context.Dependencies64.Recipes.Values.Where(x => x.WasNativeReplacement && !sequence.Contains(x)))
                    RegisterNativeReplacement(context, item.Feature, context.WorkingDocument);

                RestoreRollbackToEndAndRebuild(context);
                BaseSketchMutationEngineV7.VerifyCurrentSolids(context, finalBodyOracle);
                ValidateCompleteFeatureTree(context, context.WorkingDocument);
                reportPath = WriteReport(context, journal, working.RunId);
                baseline.AssertUnchanged(source);

                SaveWorkingDocument(context.WorkingDocument);
                app.CloseDoc(context.WorkingDocument.GetTitle());
                copy = null;
                VerifyPublishedOutput(app, context, finalBodyOracle);
                baseline.AssertUnchanged(source);
                File.Copy(working.WorkingPath, requestedOutputPath, false);
                outputCreatedByRun = true;
                MirrorV7Diagnostics.Log("[INPLACE][OUTPUT_PUBLISHED] path=" + requestedOutputPath);

                int planned = journal.Entries.Count(x => x.Status == MirrorV7ReplayStatus.NotProcessed);
                int unsupported = journal.Entries.Count(x => x.Status == MirrorV7ReplayStatus.Unsupported);
                MirrorV7Diagnostics.Log("[INPLACE][PREPARED] working=\"" + working.WorkingPath +
                    "\" report=\"" + reportPath + "\" features=" + journal.Entries.Count +
                    " planned=" + planned + " unsupported=" + unsupported +
                    " mutationExecuted=" + (context.BaseMutation != null) + " outputPublished=True output=\"" +
                    requestedOutputPath + "\"");
                return new MirrorInPlacePreparationResultV7
                {
                    WorkingPath = working.WorkingPath,
                    ReportPath = reportPath,
                    RequestedOutputPath = requestedOutputPath,
                    FeatureCount = context.SourceGraph.Count,
                    PlannedHandlerCount = planned,
                    UnsupportedCount = unsupported,
                    SourceUnchanged = true,
                    MutationExecuted = context.BaseMutation != null,
                    BaseGeometryVerified = context.BaseMutation != null && context.BaseMutation.BaseGeometryVerified,
                    OutputPublished = true
                };
            }
            catch (Exception ex)
            {
                unsafeNativeFailure66 = RollbackReplayEngineV7.IsUnsafeNativeFailure66(ex);
                if (outputCreatedByRun && File.Exists(requestedOutputPath))
                {
                    try { File.Delete(requestedOutputPath); }
                    catch (Exception cleanupError)
                    {
                        MirrorV7Diagnostics.Log("[INPLACE][OUTPUT_CLEANUP_WARNING] path=\"" +
                            requestedOutputPath + "\" message=" + cleanupError.Message);
                    }
                }
                throw;
            }
            finally
            {
                if (unsafeNativeFailure66)
                    MirrorV7Diagnostics.Log("[INPLACE66][NATIVE_STOP] cleanupCadCalls=0 closeSkipped=True sourceLiveAuditSkipped=True sourceFileWrites=0");
                else
                {
                    if (copy != null)
                    {
                        try { app.CloseDoc(copy.GetTitle()); }
                        catch (Exception ex) { MirrorV7Diagnostics.Log("[INPLACE][CLOSE_WARNING] " + ex.Message); }
                    }
                    baseline.AssertUnchanged(source);
                    Activate(app, source);
                }
            }
        }

        private static string WriteReport(MirrorInPlaceExecutionContextV7 context,
            FeatureReplayJournalV7 journal, string runId)
        {
            XElement root = new XElement("MirrorInPlacePreparation",
                new XAttribute("version", Version),
                new XAttribute("runId", runId),
                new XAttribute("status", "GEOMETRY_VERIFIED_PENDING_SAVE_AND_REOPEN"),
                new XAttribute("mirrorPartApi", false),
                new XAttribute("mirrorFeatureApi", false),
                new XAttribute("mirrorBodyApi", false),
                new XAttribute("source", context.SourceBaseline.SourcePath),
                new XAttribute("working", context.WorkingPath),
                new XAttribute("requestedOutput", context.RequestedOutputPath),
                new XAttribute("plane", context.Plane));
            BaseFeaturePrescriptionV7 p = context.BasePrescription;
            root.Add(new XElement("BasePrescription",
                new XAttribute("name", p.FeatureName ?? string.Empty),
                new XAttribute("type", p.FeatureType ?? string.Empty),
                new XAttribute("treeOrder", p.TreeOrder),
                new XAttribute("sheetMetal", p.IsSheetMetal),
                new XAttribute("supportedBaseType", p.SupportedBaseType),
                new XAttribute("needSketchMutation", p.NeedSketchMutation),
                new XAttribute("needDirectionDiagnosis", p.NeedDirectionDiagnosis),
                new XAttribute("needThicknessSideDiagnosis", p.NeedThicknessSideDiagnosis),
                new XElement("Reason", p.Reason ?? string.Empty)));
            if (context.BaseMutation != null)
                root.Add(new XElement("BaseMutation",
                    new XAttribute("base", context.BaseMutation.BaseFeatureName ?? string.Empty),
                    new XAttribute("sketch", context.BaseMutation.SketchFeatureName ?? string.Empty),
                    new XAttribute("points", context.BaseMutation.PointCount),
                    new XAttribute("movingPoints", context.BaseMutation.MovingPointCount),
                    new XAttribute("strategy", context.BaseMutation.Strategy ?? string.Empty),
                    new XAttribute("setCoordsRejected", context.BaseMutation.SetCoordsRejectedCount),
                    new XAttribute("maximumPointErrorSI", context.BaseMutation.MaximumPointErrorMetres),
                    new XAttribute("sketchVerified", context.BaseMutation.SketchVerified),
                    new XAttribute("geometryVerified", context.BaseMutation.BaseGeometryVerified),
                    new XAttribute("saved", context.BaseMutation.Saved)));
            XElement features = new XElement("FeatureDispatchPlan");
            root.Add(features);
            foreach (MirrorV7FeatureResult entry in journal.Entries)
                features.Add(new XElement("Feature",
                    new XAttribute("name", entry.FeatureName ?? string.Empty),
                    new XAttribute("type", entry.FeatureType ?? string.Empty),
                    new XAttribute("status", entry.Status),
                    new XElement("Message", entry.Message ?? string.Empty)));
            XElement mapping = new XElement("FeatureIdentityMap", new XAttribute("configuration", context.SourceBaseline.ConfigurationName));
            foreach (var node in context.SourceGraph.Nodes.Where(n => n.PersistentReference != null))
            {
                int state;
                PersistReferenceV7 replacement;
                bool recreated = context.ReplacementReferences.TryGetValue(node.TreeOrder, out replacement);
                var target = PersistentReferenceServiceV7.Resolve(context.WorkingDocument,
                    recreated ? replacement : node.PersistentReference, out state) as Feature;
                if (target == null) throw new InvalidOperationException("Output feature identity lost: " + node.Name);
                var targetRef = PersistentReferenceServiceV7.Capture(context.WorkingDocument, target, target.Name, "OUTPUT_MAP");
                if (targetRef == null) throw new InvalidOperationException("Output reference cannot be captured: " + node.Name);
                mapping.Add(new XElement("Feature", new XAttribute("sourceName", node.Name),
                    new XAttribute("targetName", target.Name), new XAttribute("type", node.TypeName),
                    new XAttribute("nativeReplacement", recreated),
                    new XElement("SourceReference", Convert.ToBase64String(node.PersistentReference.Data)),
                    new XElement("TargetReference", Convert.ToBase64String(targetRef.Data))));
            }
            root.Add(mapping);
            root.Add(new XElement("PublicationGate", "PASS_COMPLETE_BODY_ORACLE_AND_FEATURE_TREE"));
            string path = Path.Combine(Path.GetDirectoryName(context.WorkingPath), "MirrorInPlacePlan.xml");
            new XDocument(root).Save(path);
            return path;
        }

        private static void Activate(ISldWorks app, ModelDoc2 model)
        {
            if (model == null) return;
            int error = 0;
            ModelDoc2 active = app.ActivateDoc3(model.GetTitle(), false,
                (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, ref error) as ModelDoc2;
            if (active == null || error != 0)
                throw new InvalidOperationException("Cannot activate document: " + model.GetTitle() + ", error=" + error);
        }

        private static void RestoreRollbackToEndAndRebuild(MirrorInPlaceExecutionContextV7 context)
        {
            FeatureManager manager = context.WorkingDocument.FeatureManager;
            if (manager == null || !manager.EditRollback(
                (int)swMoveRollbackBarTo_e.swMoveRollbackBarToEnd, string.Empty))
                throw new InvalidOperationException("Cannot restore the complete feature tree after reflection.");
            bool rebuilt = context.WorkingDocument.EditRebuild3();
            MirrorV7Diagnostics.Log("[INPLACE][FULL_TREE_REBUILD] success=" + rebuilt);
            // EditRebuild3 can return false because of a document-level warning.  The feature
            // audit and the bidirectional body oracle below are the authoritative checks.
        }

        private static void RegisterNativeReplacement(MirrorInPlaceExecutionContextV7 context,
            MirrorV7FeatureNode replacement, ModelDoc2 target)
        {
            if (context == null || replacement == null || replacement.Feature == null || target == null)
                throw new InvalidOperationException("Native replacement registration requires a live feature and document.");
            MirrorV7FeatureNode source = context.SourceGraph.Nodes.FirstOrDefault(n =>
                n.TreeOrder == replacement.TreeOrder);
            if (source == null || source.Role == MirrorV7FeatureRole.System ||
                source.Depth != replacement.Depth ||
                !string.Equals(source.Name, replacement.Name, StringComparison.Ordinal) ||
                !string.Equals(source.TypeName, replacement.TypeName, StringComparison.Ordinal) ||
                context.ReplacementReferences.ContainsKey(source.TreeOrder))
                throw new InvalidOperationException("Native replacement does not match one unique source node: " +
                    replacement.Name + " order=" + replacement.TreeOrder);
            PersistReferenceV7 reference = PersistentReferenceServiceV7.Capture(target,
                replacement.Feature, source.Name, "NATIVE_REPLACEMENT");
            int state;
            Feature resolved = reference == null ? null :
                PersistentReferenceServiceV7.Resolve(target, reference, out state) as Feature;
            if (resolved == null || !SingleSketchTargetBuilderV7.SameComObject(resolved, replacement.Feature))
                throw new InvalidOperationException("Native replacement identity cannot be captured: " + source.Name);
            context.ReplacementReferences.Add(source.TreeOrder, reference);
            MirrorV7Diagnostics.Log("[FEATURE42][REPLACEMENT_REGISTERED] name=" + source.Name +
                " type=" + source.TypeName + " order=" + source.TreeOrder);
        }

        private static void ValidateCompleteFeatureTree(MirrorInPlaceExecutionContextV7 context, ModelDoc2 target)
        {
            MirrorV7ModelGraph sourceGraph = context.SourceGraph;
            MirrorV7ModelGraph targetGraph = FeatureTreeScannerV7.Scan(target);
            if (sourceGraph.Count != targetGraph.Count)
                throw new InvalidOperationException("Feature tree count changed. source=" + sourceGraph.Count +
                    " target=" + targetGraph.Count);
            var mapped = new Dictionary<int, MirrorV7FeatureNode>();
            var used = new HashSet<int>();
            for (int i = 0; i < sourceGraph.Count; i++)
            {
                MirrorV7FeatureNode a = sourceGraph.Nodes[i];
                if (a.Role == MirrorV7FeatureRole.System) continue;
                if (a.PersistentReference == null)
                    throw new InvalidOperationException("Source feature identity unavailable: " + a.Name);
                int state;
                PersistReferenceV7 replacement;
                bool recreated = context.ReplacementReferences.TryGetValue(a.TreeOrder, out replacement);
                Feature resolved = PersistentReferenceServiceV7.Resolve(target,
                    recreated ? replacement : a.PersistentReference, out state) as Feature;
                MirrorV7FeatureNode b = resolved == null ? null : targetGraph.Nodes.FirstOrDefault(n =>
                    SingleSketchTargetBuilderV7.SameComObject(n.Feature, resolved));
                if (b == null) throw new InvalidOperationException("Feature identity missing in output: " + a.Name +
                    " nativeReplacement=" + recreated + " state=" + state);
                if (!used.Add(b.TreeOrder))
                    throw new InvalidOperationException("Multiple source identities resolve to output feature: " + b.Name);
                mapped.Add(a.TreeOrder, b);
                string expectedName;
                if (context.ReplacementTargetNames.TryGetValue(a.TreeOrder, out expectedName))
                {
                    if (!recreated || a.Role != MirrorV7FeatureRole.Sketch || string.IsNullOrWhiteSpace(expectedName))
                        throw new InvalidOperationException("FLANGE51 invalid replacement name registration: " + a.Name);
                }
                else expectedName = a.Name;
                if (!string.Equals(a.TypeName, b.TypeName, StringComparison.Ordinal) ||
                    a.IsSuppressed != b.IsSuppressed ||
                    (recreated && !string.Equals(expectedName, b.Name, StringComparison.Ordinal)))
                    throw new InvalidOperationException("Feature tree identity changed at order " + i +
                        ": source=" + a.Name + " [" + a.TypeName + "] target=" +
                        b.Name + " [" + b.TypeName + "].");
                if (recreated)
                    MirrorV7Diagnostics.Log("[FEATURE42][REPLACEMENT_RESOLVED] name=" + a.Name +
                        " order=" + a.TreeOrder + " state=" + state);
                if (b.IsSuppressed || b.Role == MirrorV7FeatureRole.System) continue;
                bool warning;
                int error = b.Feature.GetErrorCode2(out warning);
                if (error != 0 && !warning)
                    throw new InvalidOperationException("Active feature has rebuild error: " + b.Name +
                        " [" + b.TypeName + "] error=" + error);
            }
            if (used.Count != targetGraph.Nodes.Count(n => n.Role != MirrorV7FeatureRole.System))
                throw new InvalidOperationException("Output contains unmapped non-system features.");
            foreach (var pair in context.GeneratedBendOwners)
            {
                var bend = mapped[pair.Key];
                var owner = mapped[pair.Value];
                if (!(bend.Feature.GetParents() as object[] ?? new object[0]).OfType<Feature>()
                    .Any(p => SingleSketchTargetBuilderV7.SameComObject(p, owner.Feature)))
                    throw new InvalidOperationException("FLANGE52 regenerated bend lost its registered parent: " + bend.Name);
            }
            FeatureTreeLayout50.Verify(sourceGraph, targetGraph, mapped, context.ExternalFlangeProfiles,
                context.ReplacementTargetNames);
            MirrorV7Diagnostics.Log("[TREE50][LAYOUT_PASS] mapped=" + mapped.Count +
                " registeredExternalProfiles=" + context.ExternalFlangeProfiles.Count);
            MirrorV7Diagnostics.Log("[INPLACE][FULL_TREE_VERIFY] featureCount=" + targetGraph.Count +
                " result=PASS");
        }

        private static void ImportFlangeProfileMappings50(MirrorInPlaceExecutionContextV7 context,
            MirrorInPlaceExecutionContextV7 replay)
        {
            foreach (var replacement in replay.ReplacementReferences)
            {
                var source = context.SourceGraph.Nodes.Single(n => n.TreeOrder == replacement.Key);
                var captured = replay.SourceGraph.Nodes.Single(n => n.TreeOrder == replacement.Key);
                if (source.Name != captured.Name || source.TypeName != captured.TypeName ||
                    source.Depth != captured.Depth || source.IsSuppressed != captured.IsSuppressed ||
                    context.ReplacementReferences.ContainsKey(replacement.Key))
                    throw new InvalidOperationException("FLANGE50 replay/publication identity mismatch: " + source.Name);
                int state;
                var feature = PersistentReferenceServiceV7.Resolve(replay.WorkingDocument,
                    replacement.Value, out state) as Feature;
                string expectedName;
                bool profile = replay.ReplacementTargetNames.TryGetValue(replacement.Key, out expectedName);
                bool bend = source.TypeName == "OneBend" && replay.GeneratedBendOwners.ContainsKey(replacement.Key);
                if (!profile) expectedName = source.Name;
                if ((!profile && !bend) || (profile && source.Role != MirrorV7FeatureRole.Sketch) ||
                    string.IsNullOrWhiteSpace(expectedName) ||
                    feature == null || feature.Name != expectedName ||
                    FeatureTypeHelperV7.GetEffectiveType(feature) != source.TypeName)
                    throw new InvalidOperationException("FLANGE50 profile replacement cannot be resolved: " + source.Name);
                context.ReplacementReferences.Add(replacement.Key, replacement.Value);
                if (profile) context.ReplacementTargetNames.Add(replacement.Key, expectedName);
            }
            foreach (var pair in replay.ExternalFlangeProfiles)
            {
                var owner = context.SourceGraph.Nodes.Single(n => n.TreeOrder == pair.Value);
                var capturedOwner = replay.SourceGraph.Nodes.Single(n => n.TreeOrder == pair.Value);
                if (owner.Name != capturedOwner.Name || owner.TypeName != "EdgeFlange" ||
                    !context.ReplacementReferences.ContainsKey(pair.Key))
                    throw new InvalidOperationException("FLANGE50 owner registration is invalid.");
                context.ExternalFlangeProfiles.Add(pair.Key, pair.Value);
            }
            foreach (var pair in replay.GeneratedBendOwners)
            {
                var owner = context.SourceGraph.Nodes.Single(n => n.TreeOrder == pair.Value);
                if (owner.TypeName != "EdgeFlange" || !context.ReplacementReferences.ContainsKey(pair.Key))
                    throw new InvalidOperationException("FLANGE52 invalid bend owner registration.");
                context.GeneratedBendOwners.Add(pair.Key, pair.Value);
            }
            RegisterFlatBends52(context, replay.WorkingDocument);
            MirrorV7Diagnostics.Log("[FLANGE52][LEDGER_IMPORTED] replacements=" + replay.ReplacementReferences.Count +
                " external=" + replay.ExternalFlangeProfiles.Count);
        }

        private static void RegisterFlatBends52(MirrorInPlaceExecutionContextV7 context, ModelDoc2 model)
        {
            if (context.GeneratedBendOwners.Count == 0) return;
            var source = context.SourceGraph;
            var target = FeatureTreeScannerV7.Scan(model);
            foreach (var pair in context.GeneratedBendOwners.ToArray())
            {
                var bend = source.Nodes.Single(n => n.TreeOrder == pair.Key);
                if (bend.TypeName != "OneBend") continue;
                int state;
                var mappedBend = PersistentReferenceServiceV7.Resolve(model,
                    context.ReplacementReferences[pair.Key], out state) as Feature;
                if (mappedBend == null) throw new InvalidOperationException("FLANGE52 regenerated bend disappeared.");
                foreach (var flat in source.Nodes.Where(n => n.TypeName == "UiBend" && n.ParentFeatureNames.Contains(bend.Name)))
                {
                    var flatOwner = source.Nodes.Take(flat.TreeOrder).LastOrDefault(n => n.Depth < flat.Depth);
                    if (flatOwner == null || flatOwner.TypeName != "FlatPattern" || flatOwner.PersistentReference == null)
                        throw new InvalidOperationException("FLANGE52 source flat bend owner unavailable.");
                    var resolvedOwner = PersistentReferenceServiceV7.Resolve(model, flatOwner.PersistentReference, out state) as Feature;
                    var outputOwner = target.Nodes.SingleOrDefault(n =>
                        SingleSketchTargetBuilderV7.SameComObject(n.Feature, resolvedOwner));
                    if (outputOwner == null || outputOwner.TypeName != "FlatPattern")
                        throw new InvalidOperationException("FLANGE52 target flat pattern identity unavailable.");
                    var candidates = target.Nodes.Skip(outputOwner.TreeOrder + 1)
                        .TakeWhile(n => n.Depth > outputOwner.Depth)
                        .Where(n => n.TypeName == "UiBend" && n.Depth == outputOwner.Depth + 1 &&
                            (n.Feature.GetParents() as object[] ?? new object[0]).OfType<Feature>()
                                .Any(p => SingleSketchTargetBuilderV7.SameComObject(p, mappedBend))).ToList();
                    if (candidates.Count != 1 || candidates[0].Name != flat.Name ||
                        candidates[0].IsSuppressed != flat.IsSuppressed)
                        throw new InvalidOperationException("FLANGE52 flat bend mapping missing/ambiguous: " + flat.Name +
                            " candidates=" + candidates.Count);
                    var reference = PersistentReferenceServiceV7.Capture(model, candidates[0].Feature,
                        flat.Name, "REGENERATED_FLAT_BEND");
                    if (reference == null || !SingleSketchTargetBuilderV7.SameComObject(candidates[0].Feature,
                        PersistentReferenceServiceV7.Resolve(model, reference, out state)))
                        throw new InvalidOperationException("FLANGE52 flat bend identity cannot be retained.");
                    context.ReplacementReferences.Add(flat.TreeOrder, reference);
                    context.GeneratedBendOwners.Add(flat.TreeOrder, bend.TreeOrder);
                    MirrorV7Diagnostics.Log("[FLANGE52][FLAT_BEND_REGISTERED] source=" + flat.Name +
                        " owner=" + bend.Name + " evidence=MAPPED_BEND_AND_FLAT_PATTERN");
                }
            }
        }

        private static void SaveWorkingDocument(ModelDoc2 model)
        {
            int errors = 0, warnings = 0;
            if (!model.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref errors, ref warnings) || errors != 0)
                throw new InvalidOperationException("Cannot save verified working Part. errors=" + errors +
                    " warnings=" + warnings);
            MirrorV7Diagnostics.Log("[INPLACE][STAGING_SAVED] errors=" + errors + " warnings=" + warnings);
        }

        private static void VerifyPublishedOutput(ISldWorks app, MirrorInPlaceExecutionContextV7 context,
            List<Body2> finalBodyOracle)
        {
            // A single bounded repair on the private candidate. Pass 2 is read/verify only:
            // do not publish merely because the unsaved in-memory dependency list is empty.
            for (int pass = 0; pass < 2; pass++)
            {
                if (pass == 1 && app.GetOpenDocumentByName(context.WorkingPath) != null)
                    throw new InvalidOperationException("Candidate is still loaded after CloseDoc; disk reopen cannot be certified.");
                int errors = 0, warnings = 0;
                ModelDoc2 published = app.OpenDoc6(context.WorkingPath,
                    (int)swDocumentTypes_e.swDocPART, (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
                    context.SourceBaseline.ConfigurationName, ref errors, ref warnings);
                if (published == null)
                    throw new InvalidOperationException("Cannot reopen published Part. errors=" + errors +
                        " warnings=" + warnings);
                try
                {
                    Activate(app, published);
                    if (errors != 0) throw new InvalidOperationException("Reopen error=" + errors);
                    if (!string.Equals(Path.GetFullPath(published.GetPathName()),
                        Path.GetFullPath(context.WorkingPath), StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Reopened document is not the private mirror candidate.");
                    context.WorkingDocument = published;
                    if (!published.EditRebuild3())
                        throw new InvalidOperationException("Reopened candidate failed to rebuild.");
                    BaseSketchMutationEngineV7.VerifyCurrentSolids(context, finalBodyOracle);
                    ValidateCompleteFeatureTree(context, published);
                    if (!CrossDocumentReferenceAuditV7.Run(context.SourceGraph, published,
                        context.ReplacementReferences, context.ReplacementTargetNames).Success)
                        throw new InvalidOperationException("Feature identity mapping failed after reopening.");
                    ExternalDependencyAuditResultV7 dependencyAudit = ExternalDependencyAuditV7.Run(published);
                    if (!dependencyAudit.Success && pass == 0)
                    {
                        MirrorV7Diagnostics.Log("[FEATURE43][SOURCE_DEPENDENCY_BASELINE] readOnly=True");
                        ExternalDependencyAuditV7.Run(context.SourceDocument);
                        ExternalReferenceBreakResultV7 breakResult = ExternalReferenceBreakServiceV7.Run(
                            published, context.SourceDocument);
                        if (breakResult.Executed)
                        {
                            BaseSketchMutationEngineV7.VerifyCurrentSolids(context, finalBodyOracle);
                            ValidateCompleteFeatureTree(context, published);
                            if (!CrossDocumentReferenceAuditV7.Run(context.SourceGraph, published,
                                context.ReplacementReferences, context.ReplacementTargetNames).Success)
                                throw new InvalidOperationException("Feature identity mapping changed after breaking in-context references.");
                            SaveWorkingDocument(published);
                            context.SourceBaseline.AssertUnchanged(context.SourceDocument);
                            MirrorV7Diagnostics.Log("[FEATURE43][DETACH_SAVED] geometry=True featureTree=True identity=True next=CLOSE_REOPEN");
                            continue; // finally closes the candidate before the second pass.
                        }
                    }
                    if (!dependencyAudit.Success)
                    {
                        MirrorV7Diagnostics.Log("[FEATURE43][DETACH_REOPEN_FAILED] forbidden=" + dependencyAudit.Forbidden +
                            " outputNotPublished=True see=REFERENCE_OWNER/AUXILIARY_REFERENCE");
                        throw new InvalidOperationException("Result retains forbidden external file references; independence not verified. forbidden=" +
                            dependencyAudit.Forbidden + ". See [PHASE6B][DEPENDENCY_FORBIDDEN] in MirrorPartDebug.log.");
                    }
                    if (pass == 1)
                        MirrorV7Diagnostics.Log("[FEATURE43][DETACH_REOPEN_PASS] geometry=True featureTree=True identity=True dependencies=PASS");
                    MirrorV7Diagnostics.Log("[INPLACE][STAGING_REOPEN_PASS] reopen=True geometry=True featureTree=True independent=True");
                    return;
                }
                finally
                {
                    app.CloseDoc(published.GetTitle());
                    context.WorkingDocument = null;
                }
            }
            throw new InvalidOperationException("Candidate verification did not finish; output is not published.");
        }
    }
}
