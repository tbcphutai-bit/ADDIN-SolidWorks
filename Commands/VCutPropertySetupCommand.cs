using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands
{
    // Assigns a manufacturing groove kind to each Bend Feature. On save, the
    // confirmed groove value in its Bend Table updates the Part properties.
    internal sealed class VCutPropertySetupCommand
    {
        internal const double ToleranceMm = 0.0005;
        private static readonly string[] GroovePropertyNames = { "V溝1", "V溝2", "C溝" };
        private readonly ISldWorks app;
        private ModelDoc2 loadedPart;
        private string loadedPartTitle;
        private string loadedPartPath;
        private string loadedConfiguration;
        private string loadedDefaultTable;
        private double loadedModelThicknessMm;
        public string LoadedPartTitle { get { return loadedPartTitle ?? ""; } }
        public string LoadedConfiguration { get { return loadedConfiguration ?? ""; } }
        public VCutPropertySetupCommand(ISldWorks swApp) { app = swApp; }

        public bool Load(out List<BendRow> rows,
            out Dictionary<string, string> properties, out string message)
        {
            rows = null;
            properties = null;
            message = "";
            ModelDoc2 part = app == null ? null : app.ActiveDoc as ModelDoc2;
            if (part == null || part.GetType() != (int)swDocumentTypes_e.swDocPART)
            {
                message = "Hãy mở Part cần thiết lập loại bào.";
                return false;
            }

            string config = part.ConfigurationManager.ActiveConfiguration.Name;
            List<Feature> features = new List<Feature>();
            HashSet<string> defaults = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<double> thicknesses = new HashSet<double>();
            for (Feature root = part.FirstFeature() as Feature; root != null;
                root = root.GetNextFeature() as Feature)
                Collect(root, features, defaults, thicknesses);

            if (features.Count == 0)
            {
                message = "Part không có Bend Feature được hỗ trợ.";
                return false;
            }

            string defaultTable = defaults.Count == 1 ? defaults.First() : "";
            double modelThicknessMm = thicknesses.Count == 1 ? thicknesses.First() : 0;
            VCutFeatureTagStore tags = new VCutFeatureTagStore(app, part);
            string material = ReadMaterial(part, config);
            rows = features.Select(feature =>
                CreateRow(feature, defaultTable, modelThicknessMm, tags, material)).ToList();
            properties = new Dictionary<string, string>();
            foreach (string name in GroovePropertyNames)
                properties[name] = ReadProperty(part, config, name);
            loadedPart = part;
            loadedPartTitle = part.GetTitle();
            loadedPartPath = part.GetPathName();
            loadedConfiguration = config;
            loadedDefaultTable = defaultTable;
            loadedModelThicknessMm = modelThicknessMm;
            return true;
        }

        public bool SelectFeature(BendRow row)
        {
            ModelDoc2 active = app == null ? null : app.ActiveDoc as ModelDoc2;
            if (row == null || row.Feature == null || !IsLoadedPart(active))
                return false;
            try
            {
                active.ClearSelection2(true);
                bool selected = row.Feature.Select2(false, 0);
                if (selected) active.GraphicsRedraw2();
                return selected;
            }
            catch { return false; }
        }

        private bool IsLoadedPart(ModelDoc2 active)
        {
            return loadedPart != null && active != null &&
                active.GetType() == (int)swDocumentTypes_e.swDocPART &&
                string.Equals(active.GetTitle(), loadedPartTitle,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(active.GetPathName(), loadedPartPath,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(active.ConfigurationManager.ActiveConfiguration.Name,
                    loadedConfiguration, StringComparison.OrdinalIgnoreCase);
        }

        public bool Save(IEnumerable<BendRow> rows,
            IDictionary<string, string> properties, out string message)
        {
            message = "";
            ModelDoc2 active = app == null ? null : app.ActiveDoc as ModelDoc2;
            if (!IsLoadedPart(active))
            {
                message = "Part hoặc cấu hình đang mở đã thay đổi.\r\n\r\n" +
                    "Hãy bấm Refresh trong Set Bend line rồi thử lưu lại.";
                return false;
            }
            List<BendRow> bendRows = rows == null ? new List<BendRow>() : rows.ToList();
            Dictionary<string, double> grooveValues;
            List<string> errors = Validate(bendRows, loadedDefaultTable,
                loadedModelThicknessMm, new VCutFeatureTagStore(app, loadedPart),
                ReadMaterial(active, loadedConfiguration),
                out grooveValues);
            if (errors.Count > 0)
            {
                message = "Chưa lưu; Custom Property vẫn giữ nguyên.\r\n\r\n" +
                    "Các cạnh cần kiểm tra:\r\n" +
                    string.Join("\r\n", errors.Take(8).Select(error => "• " + error)) +
                    (errors.Count > 8 ? "\r\n• Và " + (errors.Count - 8) +
                        " cạnh khác…" : "");
                return false;
            }

            int saved = 0;
            VCutFeatureTagStore tags = new VCutFeatureTagStore(app, loadedPart);
            var changedRows = new List<BendRow>();
            foreach (BendRow row in bendRows)
            {
                if (IsSuppressedInCurrentConfiguration(row.Feature)) continue;
                if (row.Kind == row.OriginalKind &&
                    (row.Kind.Length == 0 || tags.HasDocumentTag(row.Feature))) continue;
                string error;
                if (!tags.Write(loadedPart, row.Feature, row.Kind, out error))
                {
                    changedRows.Add(row);
                    string restoreError;
                    bool restored = RestoreBendTags(tags, changedRows, out restoreError);
                    message = "NG — không gán được " + row.Feature.Name + ": " + error +
                        (restored ? "" : "\r\nKhông khôi phục đủ Bend Feature: " + restoreError);
                    return false;
                }
                if (tags.Read(row.Feature) != row.Kind)
                {
                    changedRows.Add(row);
                    string restoreError;
                    bool restored = RestoreBendTags(tags, changedRows, out restoreError);
                    message = "NG — không đọc lại được loại bào vừa gán cho " +
                        row.Feature.Name + ". Chưa cập nhật Custom Property." +
                        (restored ? "" : "\r\nKhông khôi phục đủ Bend Feature: " + restoreError);
                    return false;
                }
                changedRows.Add(row);
                saved++;
            }
            string propertyError;
            if (!ReplaceGrooveProperties(active, loadedConfiguration,
                grooveValues, out propertyError))
            {
                string restoreError;
                bool restored = RestoreBendTags(tags, changedRows, out restoreError);
                message = "NG — đã gán " + saved + " Bend Feature nhưng không làm mới được " +
                    "Custom Property: " + propertyError +
                    (restored ? "" : "\r\nKhông khôi phục đủ Bend Feature: " + restoreError) +
                    "\r\nHãy Refresh bảng để kiểm tra trước khi Save Part.";
                return false;
            }
            foreach (BendRow row in changedRows) row.OriginalKind = row.Kind;
            if (properties != null)
                foreach (string name in GroovePropertyNames)
                {
                    double value;
                    properties[name] = grooveValues.TryGetValue(name, out value)
                        ? value.ToString("0.########", CultureInfo.InvariantCulture) : "";
                }
            try { loadedPart.ForceRebuild3(false); } catch { }
            message = "Đã cập nhật loại bào và Custom Property.\r\n\r\n" +
                "• Bend Feature được cập nhật: " + saved + "\r\n" +
                "• Giá trị bào được ghi lại: " + grooveValues.Count +
                " (V溝1, V溝2, C溝 được làm mới)\r\n\r\n" +
                "Hãy lưu Part để giữ thiết lập.";
            return true;
        }

        private bool RestoreBendTags(VCutFeatureTagStore tags,
            IEnumerable<BendRow> changedRows, out string error)
        {
            var failures = new List<string>();
            foreach (BendRow row in changedRows.Reverse())
            {
                string writeError;
                if (!tags.Write(loadedPart, row.Feature, row.OriginalKind, out writeError) ||
                    tags.Read(row.Feature) != row.OriginalKind)
                    failures.Add(row.Feature.Name + ": " + writeError);
            }
            error = string.Join("; ", failures);
            return failures.Count == 0;
        }

        private sealed class PropertySnapshot
        {
            public string Scope;
            public string Name;
            public string RawValue;
            public int Type;
        }

        private static bool ReplaceGrooveProperties(ModelDoc2 part, string config,
            IDictionary<string, double> values, out string error)
        {
            error = "";
            var oldProperties = new List<PropertySnapshot>();
            string[] scopes = new[] { "", config ?? "" }.Distinct().ToArray();
            bool mutationStarted = false;
            try
            {
                // Take a raw-value snapshot before the first delete so a failed
                // operation can restore even a pre-existing property expression.
                foreach (string scope in scopes)
                {
                    CustomPropertyManager manager = part.Extension.get_CustomPropertyManager(scope);
                    if (manager == null) throw new InvalidOperationException(
                        "Không mở được Custom Property scope " + scope);
                    foreach (string name in GroovePropertyNames)
                    {
                        string raw, resolved;
                        bool wasResolved, linkToParent;
                        int result = manager.Get6(name, false, out raw, out resolved,
                            out wasResolved, out linkToParent);
                        if (result == (int)swCustomInfoGetResult_e.swCustomInfoGetResult_NotPresent)
                            continue;
                        oldProperties.Add(new PropertySnapshot { Scope = scope,
                            Name = name, RawValue = raw ?? "", Type = manager.GetType2(name) });
                    }
                }

                mutationStarted = true;
                DeleteGrooveProperties(part, scopes);
                foreach (KeyValuePair<string, double> pair in values)
                {
                    string writeError;
                    if (!WriteProperty(part, config, pair.Key,
                        pair.Value.ToString("0.########", CultureInfo.InvariantCulture),
                        out writeError))
                        throw new InvalidOperationException(pair.Key + ": " + writeError);
                }

                foreach (string scope in scopes)
                {
                    CustomPropertyManager manager = part.Extension.get_CustomPropertyManager(scope);
                    foreach (string name in GroovePropertyNames)
                    {
                        string raw, resolved;
                        bool wasResolved, linkToParent;
                        int result = manager.Get6(name, false, out raw, out resolved,
                            out wasResolved, out linkToParent);
                        double expected = 0, actual;
                        bool shouldExist = scope.Length == 0 &&
                            values.TryGetValue(name, out expected);
                        if (shouldExist)
                        {
                            string text = !string.IsNullOrWhiteSpace(resolved) ? resolved : raw;
                            if (result == (int)swCustomInfoGetResult_e.swCustomInfoGetResult_NotPresent ||
                                !TryParse(text, out actual) ||
                                Math.Abs(actual - expected) > ToleranceMm)
                                throw new InvalidOperationException(name + ": giá trị đọc lại không khớp.");
                        }
                        else if (result != (int)swCustomInfoGetResult_e.swCustomInfoGetResult_NotPresent)
                            throw new InvalidOperationException(name + ": chưa xóa được giá trị cũ.");
                    }
                }
                part.SetSaveFlag();
                return true;
            }
            catch (Exception ex)
            {
                if (!mutationStarted)
                {
                    error = ex.Message;
                    return false;
                }
                string restoreError;
                bool restored = RestoreGrooveProperties(part, scopes,
                    oldProperties, out restoreError);
                error = ex.Message + (restored ? " Đã khôi phục property cũ." :
                    " Không khôi phục đủ property cũ: " + restoreError);
                return false;
            }
        }

        private static void DeleteGrooveProperties(ModelDoc2 part, IEnumerable<string> scopes)
        {
            foreach (string scope in scopes)
            {
                CustomPropertyManager manager = part.Extension.get_CustomPropertyManager(scope);
                if (manager == null) throw new InvalidOperationException(
                    "Không mở được Custom Property scope " + scope);
                foreach (string name in GroovePropertyNames)
                {
                    int result = manager.Delete2(name);
                    if (result != (int)swCustomInfoDeleteResult_e.swCustomInfoDeleteResult_OK &&
                        result != (int)swCustomInfoDeleteResult_e.swCustomInfoDeleteResult_NotPresent)
                        throw new InvalidOperationException(name + ": không xóa được ở scope " +
                            (scope.Length == 0 ? "Part" : scope) + " (" + result + ").");
                }
            }
        }

        private static bool RestoreGrooveProperties(ModelDoc2 part, string[] scopes,
            IEnumerable<PropertySnapshot> oldProperties, out string error)
        {
            var failures = new List<string>();
            foreach (string scope in scopes)
            {
                foreach (string name in GroovePropertyNames)
                {
                    try
                    {
                        CustomPropertyManager manager =
                            part.Extension.get_CustomPropertyManager(scope);
                        int result = manager.Delete2(name);
                        if (result != (int)swCustomInfoDeleteResult_e.swCustomInfoDeleteResult_OK &&
                            result != (int)swCustomInfoDeleteResult_e.swCustomInfoDeleteResult_NotPresent)
                            failures.Add(scope + "/" + name + ": delete " + result);
                    }
                    catch (Exception ex) { failures.Add(scope + "/" + name + ": " + ex.Message); }
                }
            }
            foreach (PropertySnapshot old in oldProperties)
            {
                try
                {
                    CustomPropertyManager manager =
                        part.Extension.get_CustomPropertyManager(old.Scope);
                    int result = manager.Add3(old.Name, old.Type, old.RawValue,
                        (int)swCustomPropertyAddOption_e.swCustomPropertyReplaceValue);
                    if (result != (int)swCustomInfoAddResult_e.swCustomInfoAddResult_AddedOrChanged)
                        failures.Add(old.Scope + "/" + old.Name + ": add " + result);
                }
                catch (Exception ex)
                { failures.Add(old.Scope + "/" + old.Name + ": " + ex.Message); }
            }
            try { part.SetSaveFlag(); } catch (Exception ex) { failures.Add(ex.Message); }
            error = string.Join("; ", failures);
            return failures.Count == 0;
        }

        private static List<string> Validate(IEnumerable<BendRow> rows,
            string defaultTable, double modelThicknessMm,
            VCutFeatureTagStore tags, string material, out Dictionary<string, double> grooveValues)
        {
            List<string> errors = new List<string>();
            grooveValues = new Dictionary<string, double>();
            foreach (BendRow row in rows)
            {
                if (row == null || row.Feature == null)
                {
                    errors.Add("NG — Bend Feature không hợp lệ. Hãy Refresh bảng.");
                    continue;
                }
                // The table may have changed since Load. Check every active bend
                // again before changing any feature tag or Custom Property.
                if (IsSuppressedInCurrentConfiguration(row.Feature)) continue;
                string featName = (row.Feature.Name ?? "").Normalize(System.Text.NormalizationForm.FormKC);
                string kind = row.Kind ?? "";
                if (VCutFeatureTagStore.Normalize(kind) != kind)
                {
                    errors.Add(featName + ": NG — loại bào không hợp lệ.");
                    continue;
                }
                BendRow current = CreateRow(row.Feature, defaultTable,
                    modelThicknessMm, tags, material);
                if (current.WrongBendTable)
                {
                    errors.Add(featName + ": " + current.TableError);
                    continue;
                }
                if (kind.Length == 0)
                {
                    if (string.IsNullOrWhiteSpace(current.TableError) &&
                        current.IsGrooveTable && current.ValueMm.HasValue &&
                        current.ValueMm.Value > 0 &&
                        !double.IsNaN(current.ValueMm.Value) &&
                        !double.IsInfinity(current.ValueMm.Value))
                        errors.Add(featName + ": chưa gán loại bào " +
                            "(Bend Table 溝=" + Format(current.ValueMm.Value) +
                            " mm). Hãy chọn V溝1, V溝2 hoặc C溝 rồi lưu lại.");
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(current.TableError))
                {
                    errors.Add(featName + ": NG — " + current.TableError);
                    continue;
                }
                if (!current.IsGrooveTable)
                {
                    errors.Add(featName + ": NG — Bend Table không xác nhận 溝.");
                    continue;
                }
                if (!current.ValueMm.HasValue || current.ValueMm.Value <= 0 ||
                    double.IsNaN(current.ValueMm.Value) || double.IsInfinity(current.ValueMm.Value))
                {
                    errors.Add(featName + ": NG — không đọc được giá trị từ Bend Feature.");
                    continue;
                }
                double previous;
                if (grooveValues.TryGetValue(row.Kind, out previous) &&
                    Math.Abs(previous - current.ValueMm.Value) > ToleranceMm)
                    errors.Add(featName + ": NG — " + row.Kind + " được gán cho " +
                        Format(previous) + " mm và " + Format(current.ValueMm.Value) +
                        " mm. Hãy dùng loại bào khác cho hai giá trị khác nhau.");
                else
                    grooveValues[row.Kind] = current.ValueMm.Value;
            }
            if (VValuesConflict(grooveValues))
            {
                double v1 = grooveValues["V溝1"];
                errors.Add("V溝1 và V溝2 cùng " + Format(v1) +
                    " mm. Hai loại V溝 phải có giá trị khác nhau; C溝 được phép trùng giá trị với V溝.");
            }
            return errors;
        }

        internal static bool VValuesConflict(IDictionary<string, double> values)
        {
            double v1, v2;
            return values != null && values.TryGetValue("V溝1", out v1) &&
                values.TryGetValue("V溝2", out v2) &&
                Math.Abs(v1 - v2) <= ToleranceMm;
        }

        private static bool WriteProperty(ModelDoc2 part, string config,
            string name, string value, out string error)
        {
            error = "";
            try
            {
                // The operator's property dialog shows document-level properties.
                // Also update an existing configuration override so it cannot
                // shadow the new document value in drawings and later reads.
                if (!WritePropertyInScope(part, "", name, value))
                {
                    error = "không cập nhật được property của Part.";
                    return false;
                }
                if (!string.IsNullOrWhiteSpace(config) &&
                    !string.IsNullOrWhiteSpace(ReadPropertyInScope(part, config, name)) &&
                    !WritePropertyInScope(part, config, name, value))
                {
                    error = "không cập nhật được property của configuration " + config + ".";
                    return false;
                }
                string actual = ReadProperty(part, config, name);
                double actualMm, expectedMm;
                if (!TryParse(actual, out actualMm) ||
                    !TryParse(value, out expectedMm) ||
                    Math.Abs(actualMm - expectedMm) > ToleranceMm)
                {
                    error = "giá trị đọc lại không khớp (" + actual + ").";
                    return false;
                }
                part.SetSaveFlag();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static bool WritePropertyInScope(ModelDoc2 part, string scope,
            string name, string value)
        {
            CustomPropertyManager manager = part.Extension.get_CustomPropertyManager(scope);
            if (manager == null) return false;
            int result = manager.Set2(name, value);
            if (result == (int)swCustomInfoSetResult_e.swCustomInfoSetResult_NotPresent)
                return manager.Add3(name, (int)swCustomInfoType_e.swCustomInfoText,
                    value, (int)swCustomPropertyAddOption_e.swCustomPropertyReplaceValue) ==
                    (int)swCustomInfoAddResult_e.swCustomInfoAddResult_AddedOrChanged;
            return result == (int)swCustomInfoSetResult_e.swCustomInfoSetResult_OK;
        }

        private static string ReadMaterial(ModelDoc2 model, string config)
        {
            try
            {
                string database;
                IPartDoc part = model as IPartDoc;
                string material = part == null ? "" : part.GetMaterialPropertyName2(config, out database);
                if (!string.IsNullOrWhiteSpace(material)) return material.Trim();
            }
            catch { }
            foreach (string key in new[] { "材質", "Material" })
            {
                string value = ReadProperty(model, config, key);
                if (!string.IsNullOrWhiteSpace(value) && !value.StartsWith("\"SW-"))
                    return value.Trim();
            }
            return "";
        }

        internal static bool IsWrongMaterialTable(string material, string table)
        {
            var materialMatch = System.Text.RegularExpressions.Regex.Match(material ?? "",
                @"^(SUS|ST)(?=$|[\d_\s-])", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var tableMatch = System.Text.RegularExpressions.Regex.Match(
                System.IO.Path.GetFileNameWithoutExtension(table ?? ""),
                @"(?:^|[_\s-])(SUS|ST)(?=$|[\d_\s.-])", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            // Shared groove tables have no material suffix; their thickness and
            // groove values are validated by the table reader, not guessed here.
            return materialMatch.Success && tableMatch.Success &&
                !string.Equals(materialMatch.Groups[1].Value, tableMatch.Groups[1].Value,
                    StringComparison.OrdinalIgnoreCase);
        }

        private static BendRow CreateRow(Feature feature, string defaultTable,
            double modelThicknessMm,
            VCutFeatureTagStore tags, string material)
        {
            BendRow row = new BendRow();
            row.Feature = feature;
            row.FeatureType = feature.GetTypeName2() ?? "";
            row.OriginalKind = tags.Read(feature);
            row.Kind = row.OriginalKind;
            try
            {
                object definition = feature.GetDefinition();
                IOneBendFeatureData one = definition as IOneBendFeatureData;
                IEdgeFlangeFeatureData flange = definition as IEdgeFlangeFeatureData;
                ISketchedBendFeatureData sketch = definition as ISketchedBendFeatureData;
                if (one == null && flange == null && sketch == null) return row;
                try
                {
                    double angle = one != null ? one.BendAngle :
                        flange != null ? flange.BendAngle : sketch.BendAngle;
                    row.BendAngleDeg = Math.Abs(angle * 180.0 / Math.PI);
                }
                catch { }
                try
                {
                    double radius = one != null ? one.BendRadius :
                        flange != null ? flange.BendRadius : sketch.BendRadius;
                    row.BendRadiusMm = Math.Abs(radius * 1000.0);
                }
                catch { }
                bool useDefault = one != null ? one.UseDefaultBendAllowance :
                    flange != null ? flange.UseDefaultBendAllowance :
                    sketch.UseDefaultBendAllowance;
                object allowanceObject = one != null ? one.GetCustomBendAllowance() :
                    flange != null ? flange.GetCustomBendAllowance() :
                    sketch.GetCustomBendAllowance();
                BendAllowanceInfo allowance = BendAllowanceInfo.Capture(allowanceObject);
                if (one != null)
                    Debug.WriteLine("[VCUT SETUP] OneBend=" + feature.Name +
                        " bendAllowanceType=" + one.BendAllowanceType +
                        " bendTableFile=" + one.BendTableFile +
                        " bendAllowanceMm=" + Format(one.BendAllowance * 1000) +
                        " kFactor=" + Format(one.KFactor));
                if (allowance != null)
                    Debug.WriteLine("[VCUT SETUP] Feature=" + feature.Name +
                        " type=" + row.FeatureType +
                        " allowanceType=" + allowance.TypeName +
                        " table=" + allowance.BendTableFile +
                        " bendAllowanceMm=" + Format(allowance.BendAllowance * 1000) +
                        " bendDeductionMm=" + Format(allowance.BendDeduction * 1000) +
                        " kFactor=" + Format(allowance.KFactor) +
                        " useDefault=" + useDefault);
                // OneBend can report UseDefaultBendAllowance=true while its
                // own GetCustomBendAllowance still contains the table shown
                // in the Feature PropertyManager. Prefer that explicit path.
                string featureTable = allowance != null && allowance.HasBendTableFile()
                    ? allowance.GetBendTableFilePath() : "";
                row.Table = !string.IsNullOrWhiteSpace(featureTable)
                    ? featureTable : useDefault ? defaultTable : "";
                row.TableSource = !string.IsNullOrWhiteSpace(featureTable) &&
                    (!useDefault || !string.Equals(featureTable, defaultTable,
                        StringComparison.OrdinalIgnoreCase))
                    ? "Bend Feature" : "Sheet Metal";
                Debug.WriteLine("[VCUT SETUP] Effective table=" + feature.Name +
                    " source=" + row.TableSource + " path=" + row.Table);
                if (string.IsNullOrWhiteSpace(row.Table))
                {
                    row.TableError = "Feature không có Bend Table";
                    return row;
                }
                row.WrongBendTable = IsWrongMaterialTable(material, row.Table);
                if (row.WrongBendTable)
                {
                    row.TableError = "Sai Bend Table: vật liệu " + material +
                        " không phù hợp với " + System.IO.Path.GetFileName(row.Table);
                    Debug.WriteLine("[VCUT SETUP] WRONG BEND TABLE " + feature.Name + " " + row.TableError);
                    return row;
                }
                string resolved, readError;
                double thickness, tableValue;
                bool isGroove;
                if (!BendTableCellReader.TryRead(row.Table, modelThicknessMm,
                    out resolved, out thickness, out tableValue, out isGroove,
                    out readError))
                {
                    row.TableError = readError;
                    return row;
                }
                row.Table = resolved;
                row.TableThicknessMm = thickness;
                row.TableValueMm = tableValue;
                row.IsGrooveTable = isGroove;
                if (row.IsGrooveTable)
                    row.ValueMm = tableValue;
            }
            catch { }
            return row;
        }

        private static void Collect(Feature feature, List<Feature> bends,
            HashSet<string> defaults, HashSet<double> thicknesses)
        {
            // A suppressed parent also makes its subfeatures unavailable in this
            // configuration, so do not descend into that branch of the tree.
            if (IsSuppressedInCurrentConfiguration(feature)) return;

            string type = feature.GetTypeName2() ?? "";
            if (string.Equals(type, "SheetMetal", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    ISheetMetalFeatureData data = feature.GetDefinition() as ISheetMetalFeatureData;
                    if (data != null && data.Thickness > 0)
                        thicknesses.Add(Math.Round(data.Thickness * 1000, 3));
                    BendAllowanceInfo info = data == null ? null :
                        BendAllowanceInfo.Capture(data.GetCustomBendAllowance());
                    if (info != null && info.HasBendTableFile())
                        defaults.Add(info.GetBendTableFilePath());
                }
                catch { }
            }
            else if (IsBendType(type) &&
                (type == "OneBend" || !HasOneBendChild(feature, 0)))
                bends.Add(feature);

            for (Feature child = feature.GetFirstSubFeature() as Feature;
                child != null; child = child.GetNextSubFeature() as Feature)
                Collect(child, bends, defaults, thicknesses);
        }

        private static bool IsSuppressedInCurrentConfiguration(Feature feature)
        {
            try
            {
                object states = feature.IsSuppressed2(
                    (int)swInConfigurationOpts_e.swThisConfiguration, null);
                if (states is bool) return (bool)states;
                Array values = states as Array;
                if (values != null && values.Length > 0)
                    return Convert.ToBoolean(values.GetValue(0));
            }
            catch { }

            // Older SOLIDWORKS versions may not provide a usable IsSuppressed2
            // result for every feature type.
            try { return feature.IsSuppressed(); }
            catch { return false; }
        }

        private static bool HasOneBendChild(Feature feature, int depth)
        {
            if (depth >= 12) return false;
            for (Feature child = feature.GetFirstSubFeature() as Feature;
                child != null; child = child.GetNextSubFeature() as Feature)
                if (child.GetTypeName2() == "OneBend" ||
                    HasOneBendChild(child, depth + 1))
                    return true;
            return false;
        }

        private static bool IsBendType(string type)
        {
            return type == "OneBend" || type == "SketchBend" || type == "EdgeFlange" ||
                type == "ToroidalBend" || type == "ProfileBend" || type == "SM3dBend" ||
                type == "SMMiteredBend" || type == "SweptFlange" || type == "MiterFlange" ||
                type == "Hem" || type == "Jog";
        }

        private static string ReadProperty(ModelDoc2 part, string config, string name)
        {
            foreach (string scope in new[] { config ?? "", "" }.Distinct())
            {
                string value = ReadPropertyInScope(part, scope, name);
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
            return "";
        }

        private static string ReadPropertyInScope(ModelDoc2 part, string scope, string name)
        {
            try
            {
                CustomPropertyManager manager = part.Extension.get_CustomPropertyManager(scope);
                if (manager == null) return "";
                string raw, resolved;
                bool wasResolved, linkToParent;
                manager.Get6(name, false, out raw, out resolved,
                    out wasResolved, out linkToParent);
                string value = !string.IsNullOrWhiteSpace(resolved) ? resolved : raw;
                return (value ?? "").Trim();
            }
            catch { return ""; }
        }

        private static bool TryParse(string text, out double mm)
        {
            return double.TryParse((text ?? "").Trim().Replace(',', '.'),
                NumberStyles.Float, CultureInfo.InvariantCulture, out mm) &&
                mm > 0 && !double.IsNaN(mm) && !double.IsInfinity(mm);
        }

        private static string Format(double number)
        {
            return number.ToString("0.###", CultureInfo.InvariantCulture);
        }

        internal sealed class BendRow
        {
            public Feature Feature;
            public string FeatureType = "";
            public double? BendAngleDeg;
            public double? BendRadiusMm;
            public string Table = "";
            public string TableSource = "";
            public double? TableThicknessMm;
            public double? TableValueMm;
            public string TableError = "";
            public bool WrongBendTable;
            public bool IsGrooveTable;
            public double? ValueMm;
            public string OriginalKind = "";
            public string Kind = "";
        }
    }

}
