using System;
using SolidWorks.Interop.sldworks;
namespace ADDIN.Commands.MirrorV7
{
    public static class FeatureTypeHelperV7
    {
        public static string Normalize(string type2, string type1)
        {
            if (string.IsNullOrWhiteSpace(type2)) return type1 ?? "";
            return string.Equals(type2, "ICE", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(type1) ? type1 : type2;
        }
        public static string GetEffectiveType(Feature feature)
        {
            string type2, type1;
            return GetEffectiveType(feature, out type2, out type1);
        }
        public static string GetEffectiveType(Feature feature, out string type2, out string type1)
        {
            if (feature == null) throw new ArgumentNullException("feature");
            type2 = ""; type1 = "";
            try { type2 = feature.GetTypeName2(); } catch (Exception ex) { MirrorV7Diagnostics.Log("[TYPE_READ_WARNING] GetTypeName2: " + ex.Message); }
            try { type1 = feature.GetTypeName(); } catch (Exception ex) { MirrorV7Diagnostics.Log("[TYPE_READ_WARNING] GetTypeName: " + ex.Message); }
            return Normalize(type2, type1);
        }
    }
}
