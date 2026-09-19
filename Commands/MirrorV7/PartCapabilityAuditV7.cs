using System;
namespace ADDIN.Commands.MirrorV7
{
    /// <summary>Read-only inventory. Audit coverage is not permission to mutate geometry.</summary>
    public static class PartCapabilityAuditV7
    {
        public static void Run(MirrorV7Context context)
        {
            if (context == null || context.Graph == null) throw new InvalidOperationException("Capability audit requires graph.");
            int system = 0, recognized = 0, unknown = 0, suppressed = 0, missingReference = 0;
            foreach (var node in context.Graph.Nodes)
            {
                if (node == null) { unknown++; continue; }
                if (node.Role == MirrorV7FeatureRole.System) { system++; continue; }
                bool known = node.Role != MirrorV7FeatureRole.Unknown;
                if (known) recognized++; else unknown++;
                if (node.IsSuppressed) suppressed++;
                if (node.PersistentReference == null) missingReference++;
                MirrorV7Diagnostics.Log("[CAPABILITY][FEATURE] name=\"" + node.Name + "\" type=\"" + node.TypeName + "\" role=" + node.Role +
                    " suppressed=" + node.IsSuppressed + " parents=" + node.ParentFeatureNames.Count +
                    " status=" + (known ? "NeedsImplementation" : "Unsupported") +
                    " reason=" + (known ? "No verified mutation handler registered for this feature; classification only." : "Unknown feature type requires explicit handler and verification."));
            }
            int external = 0, slots = 0, threeD = 0, missingDimensionRefs = 0;
            foreach (var sketch in context.SketchSnapshots)
            {
                external += sketch.RelationCountExternal; slots += sketch.Slots.Count;
                if (sketch.Is3D) threeD++;
                foreach (var dimension in sketch.Dimensions) if (dimension.PersistRefBytes == 0) missingDimensionRefs++;
                MirrorV7Diagnostics.Log("[CAPABILITY][SKETCH] name=\""+sketch.FeatureName+"\" is3D="+sketch.Is3D+" derived="+sketch.IsDerived+" shared="+sketch.IsShared+" suppressed="+sketch.IsSuppressed+" externalRelations="+sketch.RelationCountExternal+" slots="+sketch.Slots.Count+" action=ResolveOwnershipAndConstraintStrategyBeforeMutation");
            }
            MirrorV7Diagnostics.Log("[CAPABILITY][SUMMARY] recognized="+recognized+" unknown="+unknown+" systemSkipped="+system+" suppressed="+suppressed+" missingFeatureReference="+missingReference+" externalRelations="+external+" slots="+slots+" sketches3D="+threeD+" missingDimensionReferences="+missingDimensionRefs+" mutationReady=False reason=MutationHandlersNotImplemented");
        }
    }
}
