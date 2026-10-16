using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands
{
    internal enum VCutBendIssue
    {
        None, WrongBendTable, TableUnreadable, MissingValue, InvalidValue,
        MarkRequired, MarkNotAllowed, InvalidMark, RemainingNotBelowThickness,
        KindValuesDiffer, VValuesEqual, ExternalMarkChanged
    }

    // Assigns a manufacturing groove kind to each Bend Feature. On save, the
    // confirmed groove value in its Bend Table updates the Part properties.
    internal sealed partial class VCutPropertySetupCommand
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
        private List<BendRow> loadedRows = new List<BendRow>();
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
            string defaultTable;
            double modelThicknessMm;
            List<Feature> features;
            try { features = CollectActiveBends(part, out defaultTable, out modelThicknessMm); }
            catch (Exception ex)
            {
                message = "Không đọc được Bend Feature: " + ex.Message;
                return false;
            }

            if (features.Count == 0)
            {
                message = "Part không có Bend Feature được hỗ trợ.";
                return false;
            }

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
            LoadMinimum(rows, part, config);
            loadedRows = rows.Select(SnapshotRow).ToList();
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
                (ReferenceEquals(loadedPart, active) || app.IsSame(loadedPart, active) == (int)swObjectEquality.swObjectSame) &&
                active.GetType() == (int)swDocumentTypes_e.swDocPART &&
                string.Equals(active.GetTitle(), loadedPartTitle,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(active.GetPathName(), loadedPartPath,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(active.ConfigurationManager.ActiveConfiguration.Name,
                    loadedConfiguration, StringComparison.OrdinalIgnoreCase);
        }

        // Read-only validation for the native page; Save repeats it before mutation.
        internal bool ValidateRows(IEnumerable<BendRow> rows, out string message)
        {
            ModelDoc2 active = app == null ? null : app.ActiveDoc as ModelDoc2;
            if (!IsLoadedPart(active)) { message = "Part hoặc cấu hình đã thay đổi; hãy mở lại Set Bend line."; return false; }
            List<BendRow> current;
            if (!TryReadCurrentDraft(rows, out current, out message)) return false;
            Dictionary<string, double> values;
            var errors = Validate(current, out values);
            message = errors.Count == 0 ? "Sẵn sàng. ✓ lưu loại bào và Custom Property; ✕ bỏ thay đổi." : string.Join("\n", errors.Take(4));
            return errors.Count == 0;
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
            List<BendRow> current;
            if (!TryReadCurrentDraft(bendRows, out current, out message)) return false;
            Dictionary<string, double> grooveValues;
            List<string> errors = Validate(current, out grooveValues);
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
            foreach (BendRow row in current)
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
                    message = "NG — không gán được " + row.FeatureName + ": " + error +
                        (restored ? "" : "\r\nKhông khôi phục đủ Bend Feature: " + restoreError);
                    return false;
                }
                if (tags.Read(row.Feature) != row.Kind)
                {
                    changedRows.Add(row);
                    string restoreError;
                    bool restored = RestoreBendTags(tags, changedRows, out restoreError);
                    message = "NG — không đọc lại được loại bào vừa gán cho " +
                        row.FeatureName + ". Chưa cập nhật Custom Property." +
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
            foreach (BendRow row in bendRows) row.OriginalKind = row.Kind ?? "";
            // Advance only the immutable tag baseline after a successful commit.
            // It lets a second Save validate against the latest document state.
            foreach (BendRow row in current) row.OriginalKind = row.Kind;
            loadedRows = current.Select(SnapshotRow).ToList();
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
                    failures.Add(row.FeatureName + ": " + writeError);
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

        private bool TryReadCurrentDraft(IEnumerable<BendRow> draftRows,
            out List<BendRow> currentRows, out string message)
        {
            currentRows = null;
            message = "";
            try
            {
                string defaultTable;
                double modelThicknessMm;
                List<Feature> features = CollectActiveBends(loadedPart,
                    out defaultTable, out modelThicknessMm);
                var tags = new VCutFeatureTagStore(app, loadedPart);
                string material = ReadMaterial(loadedPart, loadedConfiguration);
                var freshRows = features.Select(feature => CreateRow(feature,
                    defaultTable, modelThicknessMm, tags, material)).ToList();
                string reason = !string.Equals(defaultTable, loadedDefaultTable,
                    StringComparison.OrdinalIgnoreCase) ? "Bend Table của Sheet Metal đã thay đổi" :
                    Math.Abs(modelThicknessMm - loadedModelThicknessMm) > 0.000001
                        ? "Bề dày hoặc nhóm Sheet Metal đã thay đổi" :
                        GetRefreshReason(loadedRows, freshRows);
                if (reason.Length > 0)
                {
                    message = "Chưa lưu; dữ liệu ngoài bảng đã thay đổi.\r\n\r\n" + reason +
                        ".\r\nHãy bấm Refresh để cập nhật Bend Feature trước khi lưu.";
                    return false;
                }

                var draftById = new Dictionary<int, BendRow>();
                foreach (BendRow row in draftRows ?? Enumerable.Empty<BendRow>())
                {
                    if (row == null || row.Feature == null)
                        throw new InvalidOperationException("Bend Feature trong bảng không hợp lệ.");
                    int id = row.Feature.GetID();
                    if (id <= 0 || (row.FeatureId > 0 && row.FeatureId != id) ||
                        draftById.ContainsKey(id))
                        throw new InvalidOperationException("Bend Feature trong bảng bị trùng hoặc đã thay đổi.");
                    draftById.Add(id, row);
                }
                if (draftById.Count != freshRows.Count ||
                    freshRows.Any(row => !draftById.ContainsKey(row.FeatureId)))
                    throw new InvalidOperationException("Danh sách Bend Feature không còn đầy đủ.");
                foreach (BendRow row in freshRows) { row.Kind = draftById[row.FeatureId].Kind ?? ""; row.DraftConflict = draftById[row.FeatureId].DraftConflict; }
                currentRows = freshRows;
                return true;
            }
            catch (Exception ex)
            {
                message = "Chưa lưu; không xác nhận được Bend Feature hiện tại.\r\n\r\n" +
                    ex.Message + "\r\nHãy bấm Refresh rồi thử lại.";
                return false;
            }
        }

        private static List<Feature> CollectActiveBends(ModelDoc2 part,
            out string defaultTable, out double modelThicknessMm)
        {
            var features = new List<Feature>();
            var defaults = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var thicknesses = new HashSet<double>();
            for (Feature root = part.FirstFeature() as Feature; root != null;
                root = root.GetNextFeature() as Feature)
                Collect(root, features, defaults, thicknesses);
            defaultTable = defaults.Count == 1 ? defaults.First() : "";
            modelThicknessMm = thicknesses.Count == 1 ? thicknesses.First() : 0;
            // A subfeature can also be exposed by the top-level iterator. Its
            // document feature ID remains stable across rename/save/reopen.
            var unique = new Dictionary<int, Feature>();
            foreach (Feature feature in features)
            {
                int id = feature.GetID();
                if (id <= 0) throw new InvalidOperationException("Bend Feature không có ID ổn định.");
                if (!unique.ContainsKey(id)) unique.Add(id, feature);
            }
            return unique.Values.ToList();
        }

        // Compare cached values only. This is also used by regression checks;
        // it never calls SOLIDWORKS or mutates the user's Mark draft.
        internal static string GetRefreshReason(IEnumerable<BendRow> loaded,
            IEnumerable<BendRow> current)
        {
            var oldRows = (loaded ?? Enumerable.Empty<BendRow>()).ToList();
            var newRows = (current ?? Enumerable.Empty<BendRow>()).ToList();
            if (oldRows.Any(row => row == null || row.FeatureId <= 0) ||
                newRows.Any(row => row == null || row.FeatureId <= 0) ||
                oldRows.GroupBy(row => row.FeatureId).Any(group => group.Count() > 1) ||
                newRows.GroupBy(row => row.FeatureId).Any(group => group.Count() > 1))
                return "Không xác nhận được ID của Bend Feature";
            var byId = oldRows.ToDictionary(row => row.FeatureId);
            if (byId.Count != newRows.Count || newRows.Any(row => !byId.ContainsKey(row.FeatureId)))
                return "Danh sách Bend Feature đã thêm, xóa hoặc thay đổi trạng thái";
            foreach (BendRow row in newRows)
            {
                BendRow old = byId[row.FeatureId];
                bool same = EqualText(old.FeatureName, row.FeatureName) &&
                    EqualText(old.FeatureType, row.FeatureType) &&
                    EqualText(old.Material, row.Material) &&
                    EqualText(old.Table, row.Table) &&
                    EqualText(old.TableSource, row.TableSource) &&
                    EqualText(old.TableRevision, row.TableRevision) &&
                    EqualText(old.TableError, row.TableError) &&
                    old.WrongBendTable == row.WrongBendTable && old.IsGrooveTable == row.IsGrooveTable &&
                    EqualNumber(old.ModelThicknessMm, row.ModelThicknessMm) &&
                    EqualNumber(old.TableThicknessMm, row.TableThicknessMm) &&
                    EqualNumber(old.TableValueMm, row.TableValueMm) &&
                    EqualNumber(old.ValueMm, row.ValueMm) &&
                    EqualNumber(old.BendAngleDeg, row.BendAngleDeg) &&
                    EqualNumber(old.BendRadiusMm, row.BendRadiusMm) &&
                    string.Equals(old.OriginalKind, row.OriginalKind, StringComparison.Ordinal);
                if (!same)
                    return (string.IsNullOrWhiteSpace(row.FeatureName) ? "Bend Feature" : row.FeatureName) +
                        ": thông số, vật liệu, Bend Table hoặc Mark đã thay đổi";
            }
            return "";
        }

        private static bool EqualText(string left, string right)
        { return string.Equals(left ?? "", right ?? "", StringComparison.OrdinalIgnoreCase); }
        private static bool EqualNumber(double? left, double? right)
        {
            if (left.HasValue != right.HasValue) return false;
            if (!left.HasValue) return true;
            return IsFinite(left.Value) && IsFinite(right.Value) &&
                Math.Abs(left.Value - right.Value) <= 0.000001;
        }

        private static BendRow SnapshotRow(BendRow row)
        {
            return new BendRow { Feature = row.Feature, FeatureId = row.FeatureId,
                FeatureName = row.FeatureName, FeatureType = row.FeatureType,
                Material = row.Material, ModelThicknessMm = row.ModelThicknessMm,
                BendAngleDeg = row.BendAngleDeg, BendRadiusMm = row.BendRadiusMm,
                Table = row.Table, TableSource = row.TableSource, TableRevision = row.TableRevision,
                TableThicknessMm = row.TableThicknessMm, TableValueMm = row.TableValueMm,
                TableError = row.TableError, WrongBendTable = row.WrongBendTable,
                IsGrooveTable = row.IsGrooveTable, ValueMm = row.ValueMm,
                OriginalKind = row.OriginalKind, Kind = row.Kind, MinimumContext = row.MinimumContext, PersistentId = row.PersistentId, DraftConflict = row.DraftConflict };
        }

        private static List<string> Validate(IEnumerable<BendRow> rows,
            out Dictionary<string, double> grooveValues)
        {
            List<string> errors = new List<string>();
            grooveValues = new Dictionary<string, double>();
            var allRows = (rows ?? Enumerable.Empty<BendRow>()).ToList();
            foreach (BendRow row in allRows)
            {
                if (row == null || row.Feature == null)
                {
                    errors.Add("NG — Bend Feature không hợp lệ. Hãy Refresh bảng.");
                    continue;
                }
                VCutBendIssue issue = GetDraftIssue(row, allRows);
                if (issue != VCutBendIssue.None)
                {
                    errors.Add(row.FeatureName + ": " + GetDraftIssueMessage(row, issue));
                    continue;
                }
                if (!string.IsNullOrEmpty(row.Kind)) grooveValues[row.Kind] = row.ValueMm.Value;
            }
            return errors;
        }

        internal static VCutBendIssue GetDraftIssue(BendRow row, IEnumerable<BendRow> rows)
        {
            VCutBendIssue local = GetLocalDraftIssue(row);
            if (local != VCutBendIssue.None || row == null || string.IsNullOrEmpty(row.Kind)) return local;
            foreach (BendRow other in rows ?? Enumerable.Empty<BendRow>())
            {
                if (ReferenceEquals(row, other) || GetLocalDraftIssue(other) != VCutBendIssue.None ||
                    other == null || string.IsNullOrEmpty(other.Kind)) continue;
                double difference = Math.Abs(row.ValueMm.Value - other.ValueMm.Value);
                if (row.Kind == other.Kind && difference > ToleranceMm) return VCutBendIssue.KindValuesDiffer;
                if (((row.Kind == "V溝1" && other.Kind == "V溝2") ||
                    (row.Kind == "V溝2" && other.Kind == "V溝1")) && difference <= ToleranceMm)
                    return VCutBendIssue.VValuesEqual;
            }
            return VCutBendIssue.None;
        }

        private static VCutBendIssue GetLocalDraftIssue(BendRow row)
        {
            if (row == null) return VCutBendIssue.InvalidMark;
            if (!string.IsNullOrEmpty(row.DraftConflict)) return VCutBendIssue.ExternalMarkChanged;
            string kind = row.Kind ?? "";
            if (VCutFeatureTagStore.Normalize(kind) != kind) return VCutBendIssue.InvalidMark;
            if (row.WrongBendTable) return VCutBendIssue.WrongBendTable;
            // Unknown table data is not an ordinary bend. It must never permit
            // a successful Save that silently clears the previous properties.
            if (!string.IsNullOrWhiteSpace(row.TableError)) return VCutBendIssue.TableUnreadable;
            if (!row.IsGrooveTable) return kind.Length == 0 ? VCutBendIssue.None : VCutBendIssue.MarkNotAllowed;
            if (!row.ValueMm.HasValue) return VCutBendIssue.MissingValue;
            if (!IsPositiveFinite(row.ValueMm.Value)) return VCutBendIssue.InvalidValue;
            if ((row.ModelThicknessMm.HasValue && !IsPositiveFinite(row.ModelThicknessMm.Value)) ||
                (row.TableThicknessMm.HasValue && !IsPositiveFinite(row.TableThicknessMm.Value)))
                return VCutBendIssue.InvalidValue;
            // The table stores remaining stock (残厚), not machining depth.
            // A real groove needs 0 < remaining < the measured stock thickness.
            if (row.ModelThicknessMm.HasValue && IsPositiveFinite(row.ModelThicknessMm.Value) &&
                row.ValueMm.Value >= row.ModelThicknessMm.Value)
                return VCutBendIssue.RemainingNotBelowThickness;
            return kind.Length == 0 ? VCutBendIssue.MarkRequired : VCutBendIssue.None;
        }

        internal static string GetDraftIssueMessage(BendRow row, VCutBendIssue issue)
        {
            switch (issue)
            {
                case VCutBendIssue.WrongBendTable: return "NG — " + (row == null ? "Sai Bend Table." : row.TableError);
                case VCutBendIssue.TableUnreadable: return "NG — " + (row == null ? "Không đọc được Bend Table." : row.TableError);
                case VCutBendIssue.MissingValue: return "NG — không đọc được bề dày còn lại từ Bend Table 溝.";
                case VCutBendIssue.InvalidValue: return "NG — bề dày còn lại phải là số hữu hạn lớn hơn 0.";
                case VCutBendIssue.MarkRequired: return "NG — chưa gán loại bào (Bend Table 溝=" +
                    Format(row.ValueMm.Value) + " mm). Hãy chọn V溝1, V溝2 hoặc C溝 rồi lưu lại.";
                case VCutBendIssue.MarkNotAllowed: return "NG — Bend Table thường không xác nhận 溝; hãy bỏ Mark cũ.";
                case VCutBendIssue.ExternalMarkChanged: return "NG — " + row.DraftConflict;
                case VCutBendIssue.InvalidMark: return "NG — loại bào không hợp lệ.";
                case VCutBendIssue.RemainingNotBelowThickness: return "NG — bề dày còn lại " + Format(row.ValueMm.Value) +
                    " mm phải nhỏ hơn bề dày vật liệu " + Format(row.ModelThicknessMm.Value) + " mm.";
                case VCutBendIssue.KindValuesDiffer: return "NG — " + row.Kind +
                    " đang được gán cho các giá trị khác nhau. Mỗi Mark phải dùng một giá trị thống nhất.";
                case VCutBendIssue.VValuesEqual: return "NG — V溝1 và V溝2 phải có giá trị khác nhau; C溝 được phép trùng với V溝.";
                default: return "";
            }
        }

        private static bool IsFinite(double value)
        { return !double.IsNaN(value) && !double.IsInfinity(value); }
        private static bool IsPositiveFinite(double value)
        { return value > 0 && IsFinite(value); }

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
            row.FeatureId = feature.GetID();
            row.FeatureName = (feature.Name ?? "").Normalize(System.Text.NormalizationForm.FormKC);
            row.FeatureType = feature.GetTypeName2() ?? "";
            row.Material = material ?? "";
            row.ModelThicknessMm = IsPositiveFinite(modelThicknessMm)
                ? (double?)modelThicknessMm : null;
            row.OriginalKind = tags.Read(feature);
            row.Kind = row.OriginalKind;
            try
            {
                object definition = feature.GetDefinition();
                IOneBendFeatureData one = definition as IOneBendFeatureData;
                IEdgeFlangeFeatureData flange = definition as IEdgeFlangeFeatureData;
                ISketchedBendFeatureData sketch = definition as ISketchedBendFeatureData;
                if (one == null && flange == null && sketch == null)
                {
                    row.TableError = "Chưa hỗ trợ đọc thông số Bend Feature loại " + row.FeatureType;
                    return row;
                }
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
                var tableFile = new System.IO.FileInfo(resolved);
                row.TableRevision = tableFile.Length.ToString(CultureInfo.InvariantCulture) + ":" +
                    tableFile.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture);
                row.TableThicknessMm = thickness;
                row.TableValueMm = tableValue;
                row.IsGrooveTable = isGroove;
                if (row.IsGrooveTable)
                    row.ValueMm = tableValue;
            }
            catch (Exception ex) { row.TableError = "Không đọc được thông số Bend Feature: " + ex.Message; }
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
            internal BendMinimumContext MinimumContext;
            public string PersistentId = "";
            public string DraftConflict;
            public Feature Feature;
            public int FeatureId;
            public string FeatureName = "";
            public string FeatureType = "";
            public string Material = "";
            public double? ModelThicknessMm;
            public double? BendAngleDeg;
            public double? BendRadiusMm;
            public string Table = "";
            public string TableSource = "";
            public string TableRevision = "";
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
