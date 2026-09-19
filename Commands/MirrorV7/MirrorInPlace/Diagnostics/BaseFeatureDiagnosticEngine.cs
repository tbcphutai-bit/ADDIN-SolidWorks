using System;
using System.Linq;

namespace ADDIN.Commands.MirrorV7.MirrorInPlace
{
    public sealed class BaseFeaturePrescriptionV7
    {
        public string FeatureName { get; internal set; }
        public string FeatureType { get; internal set; }
        public int TreeOrder { get; internal set; }
        public bool IsSheetMetal { get; internal set; }
        public bool NeedSketchMutation { get; internal set; }
        public bool NeedDirectionDiagnosis { get; internal set; }
        public bool NeedThicknessSideDiagnosis { get; internal set; }
        public bool SupportedBaseType { get; internal set; }
        public string Reason { get; internal set; }
    }

    public static class BaseFeatureDiagnosticEngineV7
    {
        public static BaseFeaturePrescriptionV7 Analyze(MirrorV7ModelGraph graph)
        {
            if (graph == null) throw new ArgumentNullException("graph");
            MirrorV7FeatureNode node = graph.Nodes.FirstOrDefault(IsBaseCandidate);
            if (node == null)
                return new BaseFeaturePrescriptionV7
                {
                    SupportedBaseType = false,
                    Reason = "No active Base Flange, Boss Extrude, Lofted Bend or Convert-to-Sheet-Metal base feature was found."
                };

            string type = Normalize(node.TypeName);
            bool sheetMetal = node.Role == MirrorV7FeatureRole.SheetMetal || IsSheetMetalType(type);
            bool supported = IsSupportedBaseType(type);
            return new BaseFeaturePrescriptionV7
            {
                FeatureName = node.Name,
                FeatureType = node.TypeName,
                TreeOrder = node.TreeOrder,
                IsSheetMetal = sheetMetal,
                NeedSketchMutation = true,
                NeedDirectionDiagnosis = true,
                NeedThicknessSideDiagnosis = sheetMetal,
                SupportedBaseType = supported,
                Reason = supported
                    ? "Base feature identified. Mutation is deliberately not executed until its absorbed profile and option snapshot are complete."
                    : "Base feature family is not implemented by the in-place mutation engine."
            };
        }

        private static bool IsBaseCandidate(MirrorV7FeatureNode node)
        {
            if (node == null || node.IsSuppressed) return false;
            string t = Normalize(node.TypeName);
            return IsSupportedBaseType(t) || t.Contains("loftedbend") ||
                   t.Contains("converttosheetmetal") || t.Contains("insertbend");
        }

        private static bool IsSupportedBaseType(string t)
        {
            return t.Contains("baseflange") || t.Contains("smbaseflange") ||
                   t == "boss" || t.Contains("bossextrude") || t.Contains("extrusion");
        }

        private static bool IsSheetMetalType(string t)
        {
            return t.Contains("sheetmetal") || t.Contains("baseflange") ||
                   t.Contains("smbaseflange") || t.Contains("loftedbend") ||
                   t.Contains("insertbend");
        }

        private static string Normalize(string value)
        {
            return (value ?? string.Empty).Replace(" ", string.Empty).Replace("-", string.Empty).ToLowerInvariant();
        }
    }
}
