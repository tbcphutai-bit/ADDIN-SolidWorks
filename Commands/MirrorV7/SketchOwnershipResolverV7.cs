using System;
using SolidWorks.Interop.sldworks;
namespace ADDIN.Commands.MirrorV7
{
    public static class SketchOwnershipResolverV7
    {
        // Deliberately limited to independent sketches for the first executor experiment.
        public static SketchOwnershipEvidenceV7 Resolve(MirrorV7FeatureNode node, SketchSnapshotV7 snapshot)
        {
            string evidence = "MissingFeatureOrSnapshot";
            var result = SketchOwnershipEvidenceV7.Unresolved;
            try
            {
                if (node == null || node.Feature == null || snapshot == null || !snapshot.CaptureSucceeded) return result;
                Feature feature = node.Feature;
                Feature owner = feature.GetOwnerFeature();
                object[] parents = feature.GetParents() as object[];
                object[] children = feature.GetChildren() as object[];
                bool bend = false;
                foreach (var entity in snapshot.Entities) if (entity.IsBendLine) bend = true;
                evidence = "owner=" + (owner == null ? "<none>" : FeatureTypeHelperV7.GetEffectiveType(owner)) +
                    ";depth=" + node.Depth + ";children=" + (children == null ? 0 : children.Length) + ";bend=" + bend;
                bool onlyReferenceParents = true;
                if (parents != null) foreach (object raw in parents)
                {
                    Feature parent = raw as Feature;
                    string type = parent == null ? "" : FeatureTypeHelperV7.GetEffectiveType(parent);
                    if (type != "RefPlane" && type != "OriginProfileFeature") onlyReferenceParents = false;
                }
                if (!bend && owner == null && node.Depth == 0 && (children == null || children.Length == 0) &&
                    onlyReferenceParents && !snapshot.IsDerived && !snapshot.IsShared && !snapshot.IsSuppressed &&
                    FeatureTypeHelperV7.GetEffectiveType(feature) == "ProfileFeature")
                {
                    result = SketchOwnershipEvidenceV7.VerifiedUserInput;
                    evidence += ";scope=Independent2DSketch;referenceParentsOnly=True";
                }
                else if (owner != null)
                {
                    string ownerType = FeatureTypeHelperV7.GetEffectiveType(owner);
                    result = string.Equals(ownerType, "FlatPattern", StringComparison.OrdinalIgnoreCase)
                        ? SketchOwnershipEvidenceV7.VerifiedGenerated
                        : SketchOwnershipEvidenceV7.FeatureOwned;
                    evidence += ";ownershipEvidence=GetOwnerFeature;mutationHandlerSupported=False";
                }
                else evidence += ";requiresOwnerSpecificStrategy=True";
                return result;
            }
            catch (Exception ex) { evidence = "OwnershipApiFailure:" + ex.Message; return SketchOwnershipEvidenceV7.Unresolved; }
            finally { MirrorV7Diagnostics.Log("[PHASE6C1][OWNERSHIP] name=\"" + (node == null ? "" : node.Name) + "\" result=" + result + " evidence=" + evidence); }
        }
    }
}
