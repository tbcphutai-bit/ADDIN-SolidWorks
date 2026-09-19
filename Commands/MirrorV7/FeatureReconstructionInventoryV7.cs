using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands.MirrorV7
{
    public static class FeatureReconstructionInventoryV7
    {
        public const string Version = "V7-FEATURE-DEFINITION-1";

        // Produces a diagnostic package, not an output Part. Definition selection access is
        // confined to a disposable byte-for-byte copy to protect production documents.
        public static string Capture(ISldWorks app, ModelDoc2 source, CanonicalPartMirrorPlaneV7 plane)
        {
            var baseline = MirrorSourceBaselineV7.Capture(source);
            ModelDoc2 copy = null;
            string reportPath = null;
            var report = new XElement("FeatureReconstructionInventory",
                new XAttribute("version", Version), new XAttribute("source", baseline.SourcePath),
                new XAttribute("configuration", baseline.ConfigurationName), new XAttribute("plane", plane),
                new XAttribute("status", "INCOMPLETE"), new XAttribute("mirroredPartCreated", false),
                new XAttribute("referenceScope", "UNMIRRORED_WORKING_COPY"),
                new XElement("Blocker", "FeatureReconstructionEngineNotImplemented"));
            MirrorV7Diagnostics.Log("[FEATURE_RECONSTRUCTION] START version=" + Version);
            try
            {
                var graph = FeatureTreeScannerV7.Scan(source);
                // Freeze source identities before any AccessSelections call on the copy.
                foreach (var node in graph.Nodes)
                    node.PersistentReference = PersistentReferenceServiceV7.Capture(source, node.Feature, node.Name, "SourceFeature");
                var working = MirrorWorkingCopyV7.Create(app, baseline);
                copy = working.Document;
                reportPath = Path.Combine(Path.GetDirectoryName(working.WorkingPath), "FeatureDefinitions.xml");
                report.Add(new XAttribute("workingCopy", working.WorkingPath));
                int activationError = 0;
                ModelDoc2 active = app.ActivateDoc3(copy.GetTitle(), false,
                    (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, ref activationError) as ModelDoc2;
                if (active == null || activationError != 0 || active.GetPathName() != copy.GetPathName())
                    throw new InvalidOperationException("Cannot activate disposable working Part.");

                foreach (var node in graph.Nodes)
                {
                    var entry = new XElement("Feature", new XAttribute("order", node.TreeOrder),
                        new XAttribute("name", node.Name), new XAttribute("type", node.TypeName),
                        new XAttribute("role", node.Role), new XAttribute("suppressed", node.IsSuppressed));
                    report.Add(entry);
                    if (node.Role == MirrorV7FeatureRole.System) { entry.Add(new XElement("Skipped", "SystemFeature")); continue; }
                    if (node.PersistentReference == null)
                    {
                        entry.Add(new XElement("Blocker", "SourceFeatureIdentityUnavailable")); continue;
                    }
                    string sourceId = Convert.ToBase64String(node.PersistentReference.Data);
                    entry.Add(new XElement("SourceId", sourceId));
                    int state;
                    Feature counterpart = PersistentReferenceServiceV7.Resolve(copy, node.PersistentReference, out state) as Feature;
                    // Never substitute a same-name feature when identity resolution fails.
                    if (counterpart == null || (state != 0 && !(node.IsSuppressed && state == 2)) ||
                        FeatureTypeHelperV7.GetEffectiveType(counterpart) != node.TypeName)
                    {
                        entry.Add(new XElement("Blocker", "WorkingCopyFeatureIdentityMismatch: " + state)); continue;
                    }
                    byte[] copyId = copy.Extension.GetPersistReference3(counterpart) as byte[];
                    if (copyId == null || copyId.Length == 0)
                    {
                        entry.Add(new XElement("Blocker", "WorkingCopyPersistentIdUnavailable")); continue;
                    }
                    entry.Add(new XElement("CopyId", Convert.ToBase64String(copyId)));
                    var parents = new XElement("Parents"); entry.Add(parents);
                    object[] parentObjects = counterpart.GetParents() as object[];
                    if (parentObjects != null) foreach (object parent in parentObjects)
                    {
                        byte[] parentId = copy.Extension.GetPersistReference3(parent) as byte[];
                        parents.Add(parentId == null || parentId.Length == 0
                            ? new XElement("UnresolvedReference") : new XElement("CopyId", Convert.ToBase64String(parentId)));
                    }
                    if (node.IsSuppressed)
                    {
                        entry.Add(new XElement("Blocker", "SuppressedFeatureRequiresConfigurationSpecificCapture")); continue;
                    }
                    if (node.Role == MirrorV7FeatureRole.Reference || node.Role == MirrorV7FeatureRole.Sketch)
                    {
                        entry.Add(new XElement("Blocker", "ReferenceOrSketchSemanticReplayNotImplemented")); continue;
                    }
                    XElement definition = FeatureDefinitionCaptureV7.Capture(copy, counterpart);
                    entry.Add(definition);
                    MirrorV7Diagnostics.Log("[FEATURE_DEFINITION] name=" + node.Name + " type=" + node.TypeName +
                        " options=" + definition.Elements("Option").Count() + " errors=" + definition.Descendants("CaptureError").Count() +
                        " unresolvedReferences=" + definition.Descendants("UnresolvedReference").Count() + " replayImplemented=False");
                    // Re-resolve after each selection-access scope, not via stale transient geometry.
                }
                baseline.AssertUnchanged(source);
                report.SetAttributeValue("status", "CAPTURE_FINISHED_REPLAY_NOT_IMPLEMENTED");
                report.Add(new XAttribute("sourceUnchanged", true));
                new XDocument(report).Save(reportPath);
                MirrorV7Diagnostics.Log("[FEATURE_RECONSTRUCTION] INCOMPLETE report=" + reportPath + " outputPartCreated=False");
                return reportPath;
            }
            catch (Exception ex)
            {
                report.Add(new XElement("FatalError", ex.ToString()));
                if (reportPath != null) new XDocument(report).Save(reportPath);
                throw;
            }
            finally
            {
                try { if (copy != null) app.CloseDoc(copy.GetTitle()); }
                finally { baseline.AssertUnchanged(source); }
            }
        }
    }
}
