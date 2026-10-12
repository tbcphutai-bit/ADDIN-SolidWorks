using System;
using System.Collections.Generic;
using System.Globalization;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands
{
    public sealed partial class KegakiChecker
    {
        private static bool IsGrooveRadius(double radiusMm)
        { return BendValueMath.IsFinite(radiusMm) && Math.Abs(radiusMm - 0.2) <= 0.03; }

        private void CaptureIntendedGrooveLevel(ModelDoc2 model, Feature bend, KegakiBendResult row)
        {
            row.IntendedGroovePropertyName = "";
            row.IntendedGrooveCoefficientMm = null;
            row.IntendedGrooveError = "Chua co muc bao da chi dinh cho bend nay (V溝1/V溝2/C溝).";
            try
            {
                // Only the document-owned saved tag is read. Do not create or
                // migrate attributes while checking a saved snapshot.
                var tags = new Dictionary<int, string>();
                var malformed = new HashSet<int>();
                for (Feature feature = model.FirstFeature() as Feature;
                    feature != null; feature = feature.GetNextFeature() as Feature)
                {
                    const string prefix = "ADDIN_VCut_Bend_";
                    if (!(feature.Name ?? "").StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                    int id;
                    if (!int.TryParse(feature.Name.Substring(prefix.Length), out id) || id <= 0) continue;
                    var attribute = feature.GetSpecificFeature2() as SolidWorks.Interop.sldworks.Attribute;
                    Parameter parameter = attribute == null ? null : attribute.GetParameter("PropertyName") as Parameter;
                    string kind = parameter == null ? null : parameter.GetStringValue();
                    kind = kind == null ? "" : kind.Trim().Normalize(System.Text.NormalizationForm.FormKC);
                    if (parameter == null || (kind != "" && kind != "V溝1" && kind != "V溝2" && kind != "C溝")
                        || tags.ContainsKey(id)) malformed.Add(id);
                    else tags.Add(id, kind);
                }
                string property;
                string error;
                if (!TryReadAssignedKind(bend, tags, malformed, out property, out error))
                { row.IntendedGrooveError = error; return; }
                row.IntendedGroovePropertyName = property;
                string configuration = model.ConfigurationManager.ActiveConfiguration.Name;
                double coefficient;
                if (!TryReadAssignedCoefficient(model, configuration, property, out coefficient, out error))
                { row.IntendedGrooveError = error; return; }
                // Set Bend line currently writes the thickness block's C into
                // these properties. C and the manufacturing residual differ in
                // some approved tables (e.g. C2.3 / residual2.5).
                row.IntendedGrooveCoefficientMm = coefficient;
                row.IntendedGrooveError = "";
            }
            catch (Exception ex)
            { row.IntendedGrooveError = "Khong doc duoc muc bao da chi dinh: " + ex.Message; }
        }

        private static bool TryReadAssignedKind(Feature bend, Dictionary<int, string> tags,
            HashSet<int> malformed, out string kind, out string detail)
        {
            kind = "";
            detail = "Chua gan V溝1/V溝2/C溝 cho bend nay; CHECK khong suy doan tu bang dang nap.";
            int id = bend.GetID();
            if (malformed.Contains(id))
            { detail = "Tag muc bao cua bend bi trung/khong hop le."; return false; }
            if (tags.TryGetValue(id, out kind)) return !string.IsNullOrWhiteSpace(kind);
            // A child bend can inherit its explicit feature parent's assignment.
            // Stop at the nearest tagged ancestor; unrelated Sheet-Metal defaults
            // and enumeration order do not assign a manufacturing level.
            var seen = new HashSet<int>();
            var pending = new Queue<Feature>();
            pending.Enqueue(bend);
            var found = new HashSet<string>(StringComparer.Ordinal);
            while (pending.Count > 0)
            {
                Feature feature = pending.Dequeue();
                int currentId = feature.GetID();
                if (currentId <= 0 || !seen.Add(currentId)) continue;
                if (seen.Count > 128)
                { detail = "Qua nhieu feature cha de anh xa muc bao."; return false; }
                if (currentId != id && malformed.Contains(currentId))
                { detail = "Tag muc bao cua feature cha khong hop le."; return false; }
                string parentKind;
                if (currentId != id && tags.TryGetValue(currentId, out parentKind))
                {
                    if (string.IsNullOrWhiteSpace(parentKind))
                    { detail = "Feature cha chua co muc bao da chi dinh."; return false; }
                    found.Add(parentKind);
                    continue;
                }
                Array parents = feature.GetParents() as Array;
                if (parents == null) continue;
                foreach (object item in parents)
                {
                    Feature parent = item as Feature;
                    if (parent == null) continue;
                    string type = parent.GetTypeName2();
                    if (string.Equals(type, "EdgeFlange", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(type, "OneBend", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(type, "SMBend", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(type, "SketchBend", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(type, "Hem", StringComparison.OrdinalIgnoreCase)) pending.Enqueue(parent);
                }
            }
            if (found.Count != 1)
            { if (found.Count > 1) detail = "Co nhieu muc bao tren cac feature cha; chua xac dinh duoc muc cho bend nay."; return false; }
            foreach (string item in found) kind = item;
            return true;
        }

        private static bool TryReadAssignedCoefficient(ModelDoc2 model, string configuration,
            string property, out double coefficient, out string detail)
        {
            coefficient = 0;
            detail = "Khong co gia tri he so da chi dinh trong " + property + ".";
            var scopes = new List<string> { configuration ?? "" };
            if (!scopes.Contains("")) scopes.Add("");
            foreach (string scope in scopes)
            {
                CustomPropertyManager manager = model.Extension.get_CustomPropertyManager(scope);
                if (manager == null) continue;
                string raw, resolved;
                bool wasResolved, linked;
                // Cached reads never activate another configuration to evaluate
                // expressions. The assignment command stores literal numbers.
                int readStatus = manager.Get6(property, true, out raw, out resolved, out wasResolved, out linked);
                if (readStatus == (int)swCustomInfoGetResult_e.swCustomInfoGetResult_NotPresent) continue;
                if (readStatus != (int)swCustomInfoGetResult_e.swCustomInfoGetResult_CachedValue
                    && readStatus != (int)swCustomInfoGetResult_e.swCustomInfoGetResult_ResolvedValue)
                { detail = "Khong xac nhan duoc trang thai doc " + property + "."; return false; }
                if (linked)
                { detail = property + " la thuoc tinh lien ket; CHECK khong tinh lai gia tri chi dinh."; return false; }
                if (string.IsNullOrWhiteSpace(raw))
                {
                    if (!string.IsNullOrWhiteSpace(resolved))
                    { detail = property + " chi co gia tri cache, chua doc duoc so da chi dinh."; return false; }
                    detail = property + " dang trong tai configuration nay; chua co muc bao da chi dinh.";
                    return false;
                }
                if (TryParseAssignedCoefficient(raw, out coefficient))
                {
                    double cachedCoefficient;
                    if (!string.IsNullOrWhiteSpace(resolved)
                        && (!TryParseAssignedCoefficient(resolved, out cachedCoefficient)
                            || Math.Abs(cachedCoefficient - coefficient) > 1e-9))
                    { detail = property + " co so da luu va gia tri cache khong thong nhat."; return false; }
                    return true;
                }
                detail = property + " khong phai mot he so so duong da luu; CHECK khong tinh lai cong thuc.";
                return false;
            }
            return false;
        }

        internal static bool TryParseAssignedCoefficient(string raw, out double coefficient)
        {
            coefficient = 0;
            string value = (raw ?? "").Trim().Normalize(System.Text.NormalizationForm.FormKC).Replace(',', '.');
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out coefficient)
                && BendValueMath.IsFinite(coefficient) && coefficient > 0;
        }
    }
}
