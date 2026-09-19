using System;
using SolidWorks.Interop.sldworks;

namespace ADDIN.Commands.MirrorV7
{
    public sealed class CrossDocumentReferenceResultV7
    {
        public int Attempted { get; set; }
        public int Resolved { get; set; }
        public int Unavailable { get; set; }
        public int Mismatched { get; set; }
        public int Ambiguous { get; set; }
        public int SystemSkipped { get; set; }
        public bool Success { get { return Attempted > 0 && Resolved == Attempted && Unavailable == 0 && Mismatched == 0 && Ambiguous == 0; } }
    }

    public static class CrossDocumentReferenceAuditV7
    {
        public static CrossDocumentReferenceResultV7 Run(MirrorV7ModelGraph sourceGraph, ModelDoc2 copy)
        {
            if (sourceGraph == null) throw new ArgumentNullException("sourceGraph");
            if (copy == null) throw new ArgumentNullException("copy");
            CrossDocumentReferenceResultV7 result = new CrossDocumentReferenceResultV7();
            foreach (MirrorV7FeatureNode node in sourceGraph.Nodes)
            {
                if (node != null && node.Role == MirrorV7FeatureRole.System) { result.SystemSkipped++; continue; }
                result.Attempted++;
                if (node == null || node.PersistentReference == null) { result.Unavailable++; MirrorV7Diagnostics.Log("[PHASE6A][SOURCE_REFERENCE_UNAVAILABLE]"); continue; }
                try
                {
                    int state;
                    object raw = PersistentReferenceServiceV7.Resolve(copy, node.PersistentReference, out state);
                    Feature feature = raw as Feature;
                    if (raw == null) { result.Unavailable++; MirrorV7Diagnostics.Log("[PHASE6A][COPY_RESOLVE_NULL] name=\"" + node.Name + "\" state=" + state); continue; }
                    if (feature == null) { result.Mismatched++; MirrorV7Diagnostics.Log("[PHASE6A][COPY_TYPE_MISMATCH] name=\"" + node.Name + "\" state=" + state); continue; }
                    string type2, type1;
                    string actualName = feature.Name ?? "", actualType = FeatureTypeHelperV7.GetEffectiveType(feature, out type2, out type1);
                    MirrorV7Diagnostics.Log("[PHASE6A][TYPE_CHECK] expectedName=\"" + node.Name + "\" actualName=\"" + actualName + "\" expectedType=\"" + node.TypeName + "\" rawType2=\"" + type2 + "\" rawType1=\"" + type1 + "\" effectiveType=\"" + actualType + "\" state=" + state);
                    if (!string.Equals(node.Name ?? "", actualName, StringComparison.Ordinal) || !string.Equals(node.TypeName ?? "", actualType, StringComparison.Ordinal))
                    { result.Mismatched++; MirrorV7Diagnostics.Log("[PHASE6A][COPY_IDENTITY_MISMATCH] expected=\"" + node.Name + "\" actual=\"" + actualName + "\" state=" + state); continue; }
                    result.Resolved++; MirrorV7Diagnostics.Log("[PHASE6A][COPY_RESOLVE_OK] name=\"" + node.Name + "\" type=\"" + actualType + "\" state=" + state);
                }
                catch (Exception ex) { result.Unavailable++; MirrorV7Diagnostics.Log("[PHASE6A][COPY_RESOLVE_EXCEPTION] name=\"" + node.Name + "\" message=" + ex.Message); }
            }
            MirrorV7Diagnostics.Log("[PHASE6A][REFERENCE_SUMMARY] attempted=" + result.Attempted + " resolved=" + result.Resolved + " unavailable=" + result.Unavailable + " mismatched=" + result.Mismatched + " ambiguous=" + result.Ambiguous + " systemSkipped=" + result.SystemSkipped);
            return result;
        }
    }
}
