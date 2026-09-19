using System;
using SolidWorks.Interop.sldworks;
namespace ADDIN.Commands.MirrorV7
{
    public static class PersistentReferenceAuditV7
    {
        public static PersistentReferenceAuditResultV7 RunOrThrow(MirrorV7Context context)
        {
            if (context == null) throw new ArgumentNullException("context");
            if (context.PartDoc == null) throw new InvalidOperationException("PHASE4 requires PartDoc.");
            if (context.Graph == null) throw new InvalidOperationException("PHASE4 requires the Phase 2 graph.");
            PersistentReferenceAuditResultV7 result = new PersistentReferenceAuditResultV7();
            foreach (MirrorV7FeatureNode node in context.Graph.Nodes)
            {
                if (node != null && node.Role == MirrorV7FeatureRole.System)
                {
                    result.SystemSkipped++;
                    MirrorV7Diagnostics.Log("[PHASE4][PERSIST_SKIP_SYSTEM] name=\"" + (node.Name ?? "") + "\" type=\"" + (node.TypeName ?? "") + "\"");
                    continue;
                }
                result.Attempted++;
                if (node == null || node.Feature == null) { result.UnsupportedOrUnavailable++; continue; }
                PersistReferenceV7 persist = PersistentReferenceServiceV7.Capture(context.PartDoc, node.Feature, node.Name, "FEATURE");
                if (persist == null) { result.UnsupportedOrUnavailable++; MirrorV7Diagnostics.Log("[PHASE4][PERSIST_UNAVAILABLE] name=\"" + (node.Name ?? "") + "\" type=\"" + (node.TypeName ?? "") + "\" role=" + node.Role); continue; }
                node.PersistentReference = persist; result.Captured++;
                int state; object resolved = PersistentReferenceServiceV7.Resolve(context.PartDoc, persist, out state);
                if (resolved == null) { MirrorV7Diagnostics.Log("[PHASE4][PERSIST_RESOLVE_FAIL] name=\"" + (node.Name ?? "") + "\" bytes=" + persist.ByteCount + " state=" + state); continue; }
                Feature resolvedFeature = resolved as Feature;
                if (resolvedFeature == null) { result.Mismatched++; MirrorV7Diagnostics.Log("[PHASE4][PERSIST_TYPE_MISMATCH] name=\"" + (node.Name ?? "") + "\" state=" + state); continue; }
                string actual = ""; try { actual = resolvedFeature.Name ?? ""; } catch { }
                if (!string.Equals(node.Name ?? "", actual, StringComparison.Ordinal)) { result.Mismatched++; MirrorV7Diagnostics.Log("[PHASE4][PERSIST_NAME_MISMATCH] expected=\"" + (node.Name ?? "") + "\" actual=" + actual + " state=" + state); continue; }
                result.Resolved++; MirrorV7Diagnostics.Log("[PHASE4][PERSIST_OK] name=\"" + (node.Name ?? "") + "\" type=\"" + (node.TypeName ?? "") + "\" role=" + node.Role + " bytes=" + persist.ByteCount + " state=" + state);
            }
            MirrorV7Diagnostics.Log("[PHASE4][SUMMARY] attempted=" + result.Attempted + " captured=" + result.Captured + " resolved=" + result.Resolved + " unsupported=" + result.UnsupportedOrUnavailable + " mismatched=" + result.Mismatched + " systemSkipped=" + result.SystemSkipped);
            if (!result.Success) throw new InvalidOperationException("PHASE4 persistent reference audit failed. captured=" + result.Captured + " resolved=" + result.Resolved + " mismatched=" + result.Mismatched);
            return result;
        }
    }
}
