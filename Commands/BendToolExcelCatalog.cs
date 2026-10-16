using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Xml;
using System.Xml.Linq;

namespace ADDIN.Commands
{
    // Immutable record access. Nested JSON is returned as text, never as a mutable dictionary.
    internal sealed class BendToolCatalogRecord
    {
        private readonly Dictionary<string, object> values;
        internal string Table { get; private set; }
        internal string Id { get { return Text("id"); } }
        internal int Revision { get { return Convert.ToInt32(values["revision"], CultureInfo.InvariantCulture); } }
        internal string ReviewStatus { get { return Text("review_status"); } }
        internal BendToolCatalogRecord(string table, Dictionary<string, object> source)
        { Table = table; values = BendToolExcelCatalog.Object(BendToolExcelCatalog.Json().DeserializeObject(BendToolExcelCatalog.Serialize(source))); }
        internal string Text(string field)
        {
            object value;
            if (!values.TryGetValue(field, out value) || value == null) return null;
            return value as string ?? BendToolExcelCatalog.Serialize(value);
        }
        internal double? Number(string field)
        {
            object value;
            if (!values.TryGetValue(field, out value) || value == null) return null;
            return BendToolExcelCatalog.Number(value, Table + "/" + Id + "/" + field);
        }
        internal string ToJson() { return BendToolExcelCatalog.Serialize(values); }
    }

    internal sealed class BendToolDefinition
    {
        private readonly BendToolCatalogRecord row;
        internal BendToolDefinition(BendToolCatalogRecord record) { row = record; }
        internal string Id => row.Id;
        internal string Code => row.Text("code");
        internal string Manufacturer => row.Text("manufacturer");
        internal string DisplayName => row.Text("display_name");
        internal string ToolType => row.Text("tool_type");
        internal double? NominalAngleDeg => row.Number("nominal_angle_deg");
        internal double? TipRadiusMm => row.Number("tip_radius_mm");
        internal double? LoadCapacityKnM => row.Number("load_capacity_kn_m");
        internal int Revision => row.Revision;
        internal string ReviewStatus => row.ReviewStatus;
        internal string Notes => row.Text("notes");
    }

    internal sealed class BendToolCatalogSnapshot
    {
        private readonly Dictionary<string, ReadOnlyCollection<BendToolCatalogRecord>> tables;
        private readonly ReadOnlyDictionary<string, string> contracts;
        internal string SourcePath { get; private set; }
        internal string SourceSha256 { get; private set; }
        internal string RuntimeCatalogSha256 { get; private set; }
        internal string DatabaseId { get; private set; }
        internal int SchemaVersion { get; private set; }
        internal DateTime LoadedUtc { get; private set; }
        internal int ChangedRecordCount { get; private set; }
        internal int BindingCount { get; private set; }
        internal string SourceLayout { get; private set; }
        internal bool AutomaticMaximumCheckEnabled => false;
        internal IReadOnlyDictionary<string, string> Contracts => contracts;
        internal ReadOnlyCollection<BendToolDefinition> Tools { get; private set; }
        internal ReadOnlyCollection<BendMinimumRule> MinimumRules { get; private set; }
        internal ReadOnlyCollection<BendToolCatalogRecord> MinimumSourceRows { get; private set; }
        internal BendToolCatalogSnapshot(string path, string sourceHash, string catalogHash, string databaseId,
            int schemaVersion, Dictionary<string, List<Dictionary<string, object>>> source,
            Dictionary<string, string> contractData, int changedCount, int bindingCount,
            string sourceLayout = "illustrated_cells_v2")
        {
            SourcePath = path; SourceSha256 = sourceHash; RuntimeCatalogSha256 = catalogHash;
            DatabaseId = databaseId; SchemaVersion = schemaVersion; LoadedUtc = DateTime.UtcNow;
            ChangedRecordCount = changedCount; BindingCount = bindingCount;
            SourceLayout = sourceLayout;
            tables = source.ToDictionary(p => p.Key, p => new ReadOnlyCollection<BendToolCatalogRecord>(
                p.Value.Select(r => new BendToolCatalogRecord(p.Key, r)).ToList()), StringComparer.Ordinal);
            contracts = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(contractData, StringComparer.Ordinal));
            Tools = new ReadOnlyCollection<BendToolDefinition>(Records("tools").Select(r => new BendToolDefinition(r)).ToList());
            MinimumRules = new ReadOnlyCollection<BendMinimumRule>(Records("minimum_rules").Select(r => new BendMinimumRule(r)).ToList());
            ReadOnlyCollection<BendToolCatalogRecord> sourceRows;
            MinimumSourceRows = tables.TryGetValue("minimum_source_rows", out sourceRows) ? sourceRows
                : new ReadOnlyCollection<BendToolCatalogRecord>(new List<BendToolCatalogRecord>());
        }
        internal ReadOnlyCollection<BendToolCatalogRecord> Records(string table)
        {
            ReadOnlyCollection<BendToolCatalogRecord> result;
            if (!tables.TryGetValue(table, out result)) throw new ArgumentException("Bảng dao không hỗ trợ: " + table);
            return result;
        }
        internal BendToolCatalogRecord Find(string table, string id) { return Records(table).FirstOrDefault(r => r.Id == id); }
        internal BendToolDefinition FindTool(string id) { return Tools.FirstOrDefault(t => t.Id == id); }
        internal ReadOnlyCollection<BendMinimumRule> FindMinimumRules(string materialCode, double thicknessMm)
        {
            string material = (materialCode ?? "").Trim().ToUpperInvariant();
            return new ReadOnlyCollection<BendMinimumRule>(MinimumRules.Where(r =>
                r.ApplicableMaterials.Contains(material) && BendMinimumCheck.NearlyEqual(r.ThicknessMm, thicknessMm)).ToList());
        }
        internal BendMinimumCheckResult CheckMinimum(string materialCode, double thicknessMm,
            string sectionShape, double? outsideA, double? outsideB, double? outsideC, double bendAngleDeg)
        { return BendMinimumCheck.Evaluate(this, materialCode, thicknessMm, sectionShape, outsideA, outsideB, outsideC, bendAngleDeg); }
    }

    internal sealed class BendToolCatalogReadResult
    {
        internal BendToolCatalogSnapshot Snapshot { get; private set; }
        internal string Error { get; private set; }
        internal BendToolCatalogReadResult(BendToolCatalogSnapshot snapshot, string error)
        { Snapshot = snapshot; Error = error; }
    }

    // Checks must read/hash the saved source via ReadForCheck or CaptureForCheck.
    // LastValid is a cache only; missing/bad source never returns it as a check result.
    internal sealed class BendToolCatalogProvider
    {
        private readonly BendToolStorageSettings settings;
        private readonly object gate = new object();
        private BendToolCatalogSnapshot lastValid;
        private string lastError;
        internal BendToolCatalogProvider() : this(new BendToolStorageSettings()) { }
        internal BendToolCatalogProvider(BendToolStorageSettings sourceSettings) { settings = sourceSettings; }
        internal BendToolCatalogSnapshot LastValid { get { lock (gate) return lastValid; } }
        internal string LastError { get { lock (gate) return lastError; } }
        // Missing or invalid configuration is a normal UI state. Keep it out of
        // faulted worker Tasks so Just My Code does not stop before the UI reports it.
        internal BendToolCatalogReadResult ReadForCheck(CancellationToken cancellation)
        {
            try
            {
                cancellation.ThrowIfCancellationRequested();
                string path = settings.LoadPath(cancellation);
                if (path == null)
                    return ReadFailure("Chưa chọn file Excel dao chấn trong Settings. Vào Settings > DAO CHẤN (BEND TOOLS), Browse file .xlsx rồi Lưu.");
                return new BendToolCatalogReadResult(CapturePath(path, cancellation), null);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { return ReadFailure(ex.Message); }
        }
        private BendToolCatalogReadResult ReadFailure(string message)
        {
            lock (gate) lastError = message;
            return new BendToolCatalogReadResult(null, message);
        }
        private BendToolCatalogSnapshot CapturePath(string path, CancellationToken cancellation)
        {
            BendToolCatalogSnapshot current = BendToolExcelCatalog.Load(path, cancellation, LastValid);
            lock (gate) { lastValid = current; lastError = null; }
            return current;
        }
        internal BendToolCatalogSnapshot CaptureForCheck(CancellationToken cancellation)
        {
            try
            {
                string path = settings.LoadPath(cancellation);
                if (path == null) throw new InvalidDataException("Chưa chọn file Excel dao chấn trong Settings.");
                return CapturePath(path, cancellation);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { lock (gate) lastError = ex.Message; throw; }
        }
    }

    internal static partial class BendToolExcelCatalog
    {
        private static readonly XNamespace Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        private static readonly XNamespace DocRelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static readonly Regex Address = new Regex(@"^([A-Z]{1,3})([1-9][0-9]{0,6})$", RegexOptions.CultureInvariant);
        private static readonly Dictionary<string, HashSet<string>> Editable = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal) {
            { "tools", Set("code manufacturer tool_type display_name nominal_angle_deg tip_radius_mm load_capacity_kn_m notes") },
            { "context_rules", Set("tool_group tool_id machine_code material_group thickness_mm thickness_scope max_bend_length_mm condition_note notes") },
            { "minimum_rules", Set("material_group applicable_material_codes_json thickness_mm v_opening_mm die_outside_width_mm inside_radius_mm min_a_mm min_b_mm min_c_mm machine_code notes") },
            { "maximum_curves", Set("tool_id display_name source_label section_shape source_uncertainty_mm notes") },
            { "maximum_curve_points", Set("curve_id seq w_mm l_mm notes") },
            { "maximum_examples", Set("tool_id display_name section_shape geometry_json source_dimensions_json source_uncertainty_mm notes") }
        };
        private static readonly HashSet<string> Numeric = Set("nominal_angle_deg tip_radius_mm load_capacity_kn_m thickness_mm max_bend_length_mm v_opening_mm die_outside_width_mm inside_radius_mm min_a_mm min_b_mm min_c_mm source_uncertainty_mm w_mm l_mm seq");
        private static readonly HashSet<string> Nonnegative = Set("tip_radius_mm inside_radius_mm source_uncertainty_mm seq");
        private static readonly HashSet<string> Required = Set("code manufacturer tool_type display_name tool_group material_group thickness_scope applicable_material_codes_json section_shape curve_id geometry_json source_dimensions_json source_label");
        private static readonly HashSet<string> LeafExamples = Set("deep-rotated-source-case-85-40 kik-source-case-105-70");
        private static readonly string[] ContractIds = { "source_policy", "minimum_geometry_contract", "maximum_geometry_contract", "bend_evaluation_contract", "dimension_definitions", "units", "catalog_identity", "manufacturing_conditions" };
        private static HashSet<string> Set(string text) { return new HashSet<string>(text.Split(' '), StringComparer.Ordinal); }
        internal static JavaScriptSerializer Json() { return new JavaScriptSerializer { MaxJsonLength = 64000000, RecursionLimit = 128 }; }
        internal static string Serialize(object value) { return Json().Serialize(value); }
        internal static Dictionary<string, object> Object(object value)
        { var result = value as Dictionary<string, object>; if (result == null) throw new InvalidDataException("Cần một JSON object trong dữ liệu gốc."); return result; }
        private static IEnumerable<object> Array(object value)
        { var array = value as IEnumerable; if (array == null || value is string || value is IDictionary) throw new InvalidDataException("Cần JSON array trong dữ liệu gốc."); return array.Cast<object>(); }
        private static string Text(Dictionary<string, object> data, string key)
        { object value; if (!data.TryGetValue(key, out value) || !(value is string)) throw new InvalidDataException("Thiếu chuỗi " + key + "."); return (string)value; }
        internal static double Number(object value, string label)
        {
            if (value == null || value is bool || value is string || !(value is IConvertible)) throw new InvalidDataException(label + ": cần số trực tiếp.");
            double number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            if (double.IsNaN(number) || double.IsInfinity(number)) throw new InvalidDataException(label + ": số phải hữu hạn.");
            return number;
        }
        private static int Integer(object value, string label, int minimum)
        { double n = Number(value, label); if (n < minimum || n > int.MaxValue || n != Math.Truncate(n)) throw new InvalidDataException(label + ": cần số nguyên hợp lệ."); return (int)n; }
        private static string Hash(byte[] bytes)
        { using (var algorithm = SHA256.Create()) return BitConverter.ToString(algorithm.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }

        internal static BendToolCatalogSnapshot Load(string path, CancellationToken cancellation, BendToolCatalogSnapshot cached = null)
        {
            path = BendToolStorageSettings.NormalizePath(path);
            byte[] bytes = StableRead(path, cancellation);
            string sourceHash = Hash(bytes);
            if (cached != null && string.Equals(cached.SourcePath, path, StringComparison.OrdinalIgnoreCase) && cached.SourceSha256 == sourceHash) return cached;
            using (var memory = new MemoryStream(bytes, false))
            using (var zip = new ZipArchive(memory, ZipArchiveMode.Read))
            {
                var book = new Workbook(zip);
                // Keep the legacy catalog contract intact. A malformed legacy workbook
                // cannot fall back to a second sheet and hide its validation failure.
                if (!book.Contains("THONG_TIN") && book.Contains(MinimumSheetName))
                    return LoadMinimumSheet(book, path, sourceHash, cancellation);
                Sheet info = book.Sheet("THONG_TIN");
                info.Headers(new[] { "key", "value" });
                var meta = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (object[] cells in info.Rows(6, 2))
                {
                    if (cells.All(v => v == null || Equals(v, ""))) continue;
                    string key = cells[0] as string;
                    if (string.IsNullOrWhiteSpace(key) || meta.ContainsKey(key)) throw new InvalidDataException("THONG_TIN có key trùng/rỗng.");
                    meta.Add(key, cells[1]);
                }
                if (!Equals(Value(meta, "workbook_layout"), "illustrated_cells_v2") || !Equals(Value(meta, "runtime_contract"), "bend_tool_excel_v1"))
                    throw new InvalidDataException("Excel cần định dạng illustrated_cells_v2 / bend_tool_excel_v1. Hãy dùng file dao chấn mới.");
                string databaseId = Value(meta, "database_id") as string;
                if (string.IsNullOrWhiteSpace(databaseId)) throw new InvalidDataException("THONG_TIN thiếu database_id.");
                int schemaVersion = Integer(Value(meta, "schema_version"), "schema_version", 1);
                if (schemaVersion != 3) throw new InvalidDataException("Phiên bản schema dao chưa hỗ trợ: " + schemaVersion);
                string catalogHash = Value(meta, "runtime_catalog_sha256") as string;
                string sourceToken = Value(meta, "content_token") as string;
                if (catalogHash == null || sourceToken == null || !Regex.IsMatch(catalogHash, "^[0-9a-f]{64}$") || !Regex.IsMatch(sourceToken, "^[0-9a-f]{64}$"))
                    throw new InvalidDataException("THONG_TIN cần SHA-256 hợp lệ cho catalog/content_token.");
                Sheet originals = book.Sheet("GOC_DU_LIEU");
                originals.Headers(new[] { "kind", "id", "json" });
                var raw = new List<string>();
                var baseline = Editable.Keys.ToDictionary(k => k, k => new List<Dictionary<string, object>>(), StringComparer.Ordinal);
                var columns = new Dictionary<string, Dictionary<string, Dictionary<string, object>>>(StringComparer.Ordinal);
                var contracts = new Dictionary<string, string>(StringComparer.Ordinal);
                var identities = new HashSet<string>(StringComparer.Ordinal);
                foreach (object[] cells in originals.Rows(6, 3))
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (cells.All(v => v == null || Equals(v, ""))) continue;
                    string kind = cells[0] as string, id = cells[1] as string, json = cells[2] as string;
                    if (string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(id) || json == null || !identities.Add(kind + "/" + id))
                        throw new InvalidDataException("GOC_DU_LIEU có dòng trùng/không hợp lệ.");
                    raw.Add(kind + "\t" + id + "\t" + json);
                    object decoded = Json().DeserializeObject(json);
                    if (kind == "columns")
                    {
                        var definition = Object(decoded); string table = Text(definition, "table");
                        if (table != id || !Editable.ContainsKey(table) || columns.ContainsKey(table)) throw new InvalidDataException("Định nghĩa columns không hợp lệ.");
                        var fields = new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);
                        foreach (object value in Array(Value(definition, "columns")))
                        {
                            var field = Object(value); string key = Text(field, "key");
                            if (fields.ContainsKey(key) || !(Value(field, "editable") is bool) || (bool)field["editable"] != Editable[table].Contains(key))
                                throw new InvalidDataException("Quyền chỉnh sửa cột gốc không hợp lệ: " + table + "/" + key);
                            string type = Text(field, "type");
                            if (type != "text" && type != "number") throw new InvalidDataException("Kiểu cột gốc không hợp lệ.");
                            fields.Add(key, field);
                        }
                        if (!Editable[table].IsSubsetOf(fields.Keys)) throw new InvalidDataException("Thiếu cột chỉnh sửa trong dữ liệu gốc.");
                        columns.Add(table, fields);
                    }
                    else if (kind == "record")
                    {
                        var data = Object(decoded); string table = Text(data, "table"); var row = Object(Value(data, "row"));
                        if (!baseline.ContainsKey(table) || id != table + "/" + Text(row, "id")) throw new InvalidDataException("ID record gốc không nhất quán.");
                        baseline[table].Add(row);
                    }
                    else if (kind == "contract")
                    {
                        if (!(decoded is IDictionary) && !(decoded is IEnumerable && !(decoded is string))) throw new InvalidDataException("Contract cần JSON object/array: " + id);
                        contracts.Add(id, json);
                    }
                    else if (kind == "geometry")
                    {
                        var data = Object(decoded); string table = Text(data, "table"); var row = Object(Value(data, "row"));
                        string expected = table == "profiles" ? "profiles/" + Text(row, "id") : table == "profile_vertices" ? "profile_vertices/" + Text(row, "profile_id") + "/" + Integer(Value(row, "seq"), "profile seq", 0) : null;
                        if (expected != id) throw new InvalidDataException("Geometry nguồn không hợp lệ.");
                        contracts.Add("geometry/" + id, json);
                    }
                    else throw new InvalidDataException("Loại dữ liệu gốc không hỗ trợ: " + kind);
                }
                if (catalogHash != Hash(Encoding.UTF8.GetBytes(string.Join("\n", raw)))) throw new InvalidDataException("Hash GOC_DU_LIEU không khớp; không dùng dữ liệu nguồn bị thay đổi.");
                if (columns.Count != Editable.Count || ContractIds.Any(id => !contracts.ContainsKey(id))) throw new InvalidDataException("Dữ liệu gốc thiếu bảng/contract.");
                ValidateContracts(contracts, meta);
                var rows = baseline.ToDictionary(p => p.Key, p => p.Value.ToDictionary(r => Text(r, "id"), StringComparer.Ordinal), StringComparer.Ordinal);
                foreach (var table in baseline)
                    foreach (var row in table.Value)
                        if (!columns[table.Key].Keys.All(k => row.ContainsKey(k)) || row.Keys.Any(k => !columns[table.Key].ContainsKey(k)))
                            throw new InvalidDataException("Record gốc không khớp columns: " + table.Key + "/" + Text(row, "id"));
                ValidateCatalog(baseline, baseline, contracts);
                var working = baseline.ToDictionary(p => p.Key, p => p.Value.Select(r => Object(Json().DeserializeObject(Serialize(r)))).ToList(), StringComparer.Ordinal);
                var edited = working.ToDictionary(p => p.Key, p => p.Value.ToDictionary(r => Text(r, "id"), StringComparer.Ordinal), StringComparer.Ordinal);
                Sheet map = book.Sheet("VI_TRI_O");
                map.Headers(new[] { "table", "record_id", "revision", "field", "sheet", "cell", "encoding", "json_pointer" });
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int bindingCount = 0;
                foreach (object[] cells in map.Rows(6, 8))
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (cells.All(v => v == null || Equals(v, ""))) continue;
                    string table = cells[0] as string, id = cells[1] as string, field = cells[3] as string,
                        sheetName = cells[4] as string, cellName = (cells[5] as string ?? "").ToUpperInvariant(), encoding = cells[6] as string;
                    string pointer = cells[7] as string ?? "";
                    if (table == null || !rows.ContainsKey(table) || id == null || !rows[table].ContainsKey(id) || field == null || !Editable[table].Contains(field))
                        throw new InvalidDataException("VI_TRI_O trỏ record/cột không hợp lệ.");
                    if (Integer(cells[2], "revision ánh xạ", 1) != Integer(Value(rows[table][id], "revision"), "revision gốc", 1))
                        throw new InvalidDataException("Revision ánh xạ không khớp dữ liệu gốc: " + table + "/" + id);
                    if (sheetName == null || new[] { "THONG_TIN", "VI_TRI_O", "GOC_DU_LIEU" }.Contains(sheetName) || !book.Contains(sheetName))
                        throw new InvalidDataException("Sheet ánh xạ không hợp lệ.");
                    ValidateAddress(cellName);
                    bool leaf = table == "maximum_examples" && LeafExamples.Contains(id) && field == "source_dimensions_json";
                    if (leaf ? pointer != "/source_labeled_dimensions_mm/W" && pointer != "/source_labeled_dimensions_mm/L" : pointer != "")
                        throw new InvalidDataException("JSON pointer không được phép cho field này.");
                    string fieldKey = table + "/" + id + "/" + field + pointer;
                    if (!seen.Add(fieldKey) || !targets.Add(sheetName + "/" + cellName)) throw new InvalidDataException("Ánh xạ field/ô bị trùng hoặc chồng nhau.");
                    string expectedEncoding = field == "applicable_material_codes_json" ? "material_codes" : field == "tool_type" ? "tool_type" : field == "thickness_scope" ? "thickness_scope" : "direct";
                    if (encoding != expectedEncoding) throw new InvalidDataException("Encoding không đúng: " + fieldKey);
                    object value = book.Sheet(sheetName).AtMapped(cellName);
                    if (leaf)
                    {
                        double number = Number(value, fieldKey);
                        if (number <= 0) throw new InvalidDataException("W/L minh họa phải > 0 mm.");
                        var json = JsonObjectField(Value(edited[table][id], field), fieldKey);
                        var dimensions = Object(Value(json, "source_labeled_dimensions_mm"));
                        string key = pointer.EndsWith("/W", StringComparison.Ordinal) ? "W" : "L";
                        if (!dimensions.ContainsKey(key)) throw new InvalidDataException("Dữ liệu gốc thiếu nhãn W/L.");
                        dimensions[key] = number;
                        edited[table][id][field] = json;
                    }
                    else edited[table][id][field] = Decode(value, encoding, fieldKey);
                    bindingCount++;
                }
                foreach (var table in rows)
                    foreach (var row in table.Value)
                        foreach (string field in Editable[table.Key])
                        {
                            string key = table.Key + "/" + row.Key + "/" + field;
                            string[] pointers = table.Key == "maximum_examples" && LeafExamples.Contains(row.Key) && field == "source_dimensions_json"
                                ? new[] { "/source_labeled_dimensions_mm/W", "/source_labeled_dimensions_mm/L" } : new[] { "" };
                            if (pointers.Any(p => !seen.Contains(key + p))) throw new InvalidDataException("VI_TRI_O thiếu field: " + key);
                        }
                int changed = 0;
                var affectedCurves = new HashSet<string>(StringComparer.Ordinal);
                foreach (var table in edited)
                    foreach (var pair in table.Value)
                    {
                        var original = rows[table.Key][pair.Key];
                        string[] changedFields = Editable[table.Key].Where(f => !EquivalentField(f, Value(original, f), Value(pair.Value, f))).ToArray();
                        if (changedFields.Length > 0)
                        {
                            changed++;
                            if (changedFields.Any(f => f != "notes"))
                            {
                                pair.Value["review_status"] = "needs_review";
                                ResetMaximumQa(table.Key, pair.Value);
                                if (table.Key == "maximum_curve_points")
                                { affectedCurves.Add(Text(original, "curve_id")); affectedCurves.Add(Text(pair.Value, "curve_id")); }
                            }
                        }
                    }
                foreach (string curveId in affectedCurves)
                    if (edited["maximum_curves"].ContainsKey(curveId)) ResetMaximumQa("maximum_curves", edited["maximum_curves"][curveId]);
                ValidateCatalog(working, baseline, contracts);
                ValidateGeometry(contracts, new HashSet<string>(edited["tools"].Keys, StringComparer.Ordinal));
                return new BendToolCatalogSnapshot(path, sourceHash, catalogHash, databaseId, schemaVersion, working, contracts, changed, bindingCount);
            }
        }

        private static object Value(Dictionary<string, object> data, string key)
        { object value; if (!data.TryGetValue(key, out value)) throw new InvalidDataException("Thiếu dữ liệu " + key + "."); return value; }
        private static bool Equivalent(object a, object b)
        {
            if ((a == null || Equals(a, "")) && (b == null || Equals(b, ""))) return true;
            if (a != null && b != null && !(a is string) && !(b is string) && !(a is IDictionary) && !(b is IDictionary) && !(a is IEnumerable) && !(b is IEnumerable) && !(a is bool) && !(b is bool))
                return Number(a, "giá trị gốc") == Number(b, "giá trị Excel");
            if (a is Dictionary<string, object> || b is Dictionary<string, object>)
                return Canonical(JsonObjectField(a, "JSON gốc")) == Canonical(JsonObjectField(b, "JSON Excel"));
            return Equals(a, b);
        }
        private static bool EquivalentField(string field, object a, object b)
        {
            if (field == "geometry_json" || field == "source_dimensions_json")
                return Canonical(JsonObjectField(a, field)) == Canonical(JsonObjectField(b, field));
            if (field == "applicable_material_codes_json")
                return Canonical(Array(Json().DeserializeObject((string)a)).Cast<string>().OrderBy(s => s, StringComparer.Ordinal).ToArray())
                    == Canonical(Array(Json().DeserializeObject((string)b)).Cast<string>().OrderBy(s => s, StringComparer.Ordinal).ToArray());
            return Equivalent(a, b);
        }
        private static void ResetMaximumQa(string table, Dictionary<string, object> row)
        {
            if (table == "maximum_curves")
            { row["review_status"] = "needs_review"; row["source_review_status"] = "unreviewed"; row["automatic_check_enabled"] = 0; row["datum_registration_status"] = "unregistered"; }
            else if (table == "maximum_examples") row["source_review_status"] = "unreviewed";
        }
        private static string Canonical(object value)
        {
            if (value is Dictionary<string, object>) return "{" + string.Join(",", Object(value).OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => Serialize(p.Key) + ":" + Canonical(p.Value))) + "}";
            if (value is IEnumerable && !(value is string)) return "[" + string.Join(",", Array(value).Select(Canonical)) + "]";
            return Serialize(value);
        }
        private static Dictionary<string, object> JsonObjectField(object value, string label)
        {
            try { return Object(value is string ? Json().DeserializeObject((string)value) : value); }
            catch (Exception ex) { throw new InvalidDataException(label + ": JSON object không hợp lệ.", ex); }
        }
        private static object Decode(object value, string encoding, string label)
        {
            if (encoding == "direct") return value;
            string text = value as string;
            if (text == null) throw new InvalidDataException(label + ": cần văn bản trực tiếp.");
            if (encoding == "material_codes")
            {
                string[] codes = text.Split(';').Select(s => s.Trim()).ToArray();
                if (codes.Length == 0 || codes.Any(string.IsNullOrWhiteSpace) || codes.Distinct(StringComparer.Ordinal).Count() != codes.Length) throw new InvalidDataException(label + ": mã vật liệu không rỗng/trùng.");
                return Serialize(codes);
            }
            if (encoding == "tool_type")
            {
                if (text == "Dao trên") return "punch";
                if (text == "Cối dưới") return "die";
                if (text == "Chưa xác định") return "unknown";
            }
            if (encoding == "thickness_scope")
            {
                if (text == "Đúng chiều dày") return "exact";
                if (text == "Chiều dày tối đa") return "maximum";
            }
            throw new InvalidDataException(label + ": lựa chọn không hợp lệ.");
        }

        private static void ValidateCatalog(Dictionary<string, List<Dictionary<string, object>>> tables,
            Dictionary<string, List<Dictionary<string, object>>> baseline, Dictionary<string, string> contracts)
        {
            var toolIds = new HashSet<string>(tables["tools"].Select(r => Text(r, "id")), StringComparer.Ordinal);
            var curveIds = new HashSet<string>(tables["maximum_curves"].Select(r => Text(r, "id")), StringComparer.Ordinal);
            var conditions = Object(Json().DeserializeObject(contracts["manufacturing_conditions"]));
            var materialGroups = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (object value in Array(Value(conditions, "material_groups")))
            {
                var group = Object(value); string code = Text(group, "code");
                string[] members = Array(Json().DeserializeObject(Text(group, "members_json"))).Cast<string>().ToArray();
                if (materialGroups.ContainsKey(code) || members.Length == 0 || members.Any(string.IsNullOrWhiteSpace) || members.Distinct(StringComparer.Ordinal).Count() != members.Length)
                    throw new InvalidDataException("Danh mục nhóm vật liệu nguồn không hợp lệ.");
                materialGroups.Add(code, new HashSet<string>(members, StringComparer.Ordinal));
            }
            string[] machineCodes = Array(Value(conditions, "machines")).Select(value => Text(Object(value), "code")).ToArray();
            if (machineCodes.Distinct(StringComparer.Ordinal).Count() != machineCodes.Length) throw new InvalidDataException("Danh mục máy nguồn bị trùng.");
            var machines = new HashSet<string>(machineCodes, StringComparer.Ordinal);
            var curves = tables["maximum_curves"].ToDictionary(r => Text(r, "id"), StringComparer.Ordinal);
            var codes = new HashSet<string>(StringComparer.Ordinal);
            var seqs = new HashSet<string>(StringComparer.Ordinal);
            foreach (var table in tables)
                foreach (var row in table.Value)
                {
                    string label = table.Key + "/" + Text(row, "id");
                    Integer(Value(row, "revision"), label + "/revision", 1);
                    if (!new[] { "needs_review", "verified", "retired" }.Contains(Text(row, "review_status"))) throw new InvalidDataException(label + ": review_status không hợp lệ.");
                    foreach (string field in Editable[table.Key])
                    {
                        object value = Value(row, field);
                        if (value is string && field != "notes" && field != "condition_note")
                        {
                            value = ((string)value).Trim(); row[field] = value;
                            if (Equals(value, "") && (field == "tool_id" || field == "machine_code")) { row[field] = null; value = null; }
                        }
                        if (Numeric.Contains(field))
                        {
                            bool required = table.Key == "maximum_curve_points" || field == "source_uncertainty_mm" || table.Key == "minimum_rules" && (field == "thickness_mm" || field == "v_opening_mm");
                            if (value == null || Equals(value, "")) { if (required) throw new InvalidDataException(label + "/" + field + ": cần số."); row[field] = null; continue; }
                            double number = Number(value, label + "/" + field);
                            if (field == "w_mm" || field == "l_mm") { if (number <= -1e9 || number >= 1e9) throw new InvalidDataException(label + ": tọa độ nguồn ngoài phạm vi."); }
                            else if (Nonnegative.Contains(field) ? number < 0 : number <= 0) throw new InvalidDataException(label + "/" + field + ": ngoài giới hạn.");
                            if (field == "nominal_angle_deg" && number >= 180) throw new InvalidDataException(label + ": góc cần < 180°.");
                            if (field == "seq") Integer(value, label + "/seq", 0);
                        }
                        else if (field == "geometry_json" || field == "source_dimensions_json")
                        { var json = JsonObjectField(value, label + "/" + field); ValidateFiniteTree(json, label + "/" + field); row[field] = json; }
                        else if (value != null && !(value is string)) throw new InvalidDataException(label + "/" + field + ": cần văn bản.");
                        if (Required.Contains(field) && (value == null || value is string && string.IsNullOrWhiteSpace((string)value))) throw new InvalidDataException(label + "/" + field + ": không được rỗng.");
                    }
                    if (row.ContainsKey("tool_id") && Value(row, "tool_id") != null && !Equals(Value(row, "tool_id"), "") && !toolIds.Contains(Text(row, "tool_id"))) throw new InvalidDataException(label + ": tool_id không tồn tại.");
                    if (row.ContainsKey("material_group") && !materialGroups.ContainsKey(Text(row, "material_group"))) throw new InvalidDataException(label + ": nhóm vật liệu không tồn tại.");
                    if (row.ContainsKey("machine_code") && Value(row, "machine_code") != null && !Equals(Value(row, "machine_code"), "") && !machines.Contains(Text(row, "machine_code"))) throw new InvalidDataException(label + ": máy không tồn tại.");
                    if (table.Key == "tools")
                    {
                        if (!new[] { "punch", "die", "unknown" }.Contains(Text(row, "tool_type"))) throw new InvalidDataException(label + ": loại dao không hợp lệ.");
                        if (!codes.Add(Text(row, "manufacturer") + "\t" + Text(row, "code"))) throw new InvalidDataException("Mã dao/nhà sản xuất bị trùng.");
                        foreach (var source in baseline["tools"].Where(r => Text(r, "id") != Text(row, "id")))
                            foreach (object aliasValue in Array(Json().DeserializeObject(Text(source, "aliases"))))
                            { var alias = Object(aliasValue); if (Text(alias, "manufacturer") == Text(row, "manufacturer") && Text(alias, "alias") == Text(row, "code")) throw new InvalidDataException(label + ": mã dao trùng alias nguồn của dao khác."); }
                    }
                    if (table.Key == "context_rules" && !new[] { "exact", "maximum" }.Contains(Text(row, "thickness_scope"))) throw new InvalidDataException(label + ": phạm vi chiều dày không hợp lệ.");
                    if (table.Key == "minimum_rules")
                    {
                        object decoded = Json().DeserializeObject(Text(row, "applicable_material_codes_json"));
                        string[] members = Array(decoded).Select(v => v as string).ToArray();
                        var valid = materialGroups[Text(row, "material_group")];
                        if (members.Length == 0 || members.Any(m => m == null || !valid.Contains(m)) || members.Distinct(StringComparer.Ordinal).Count() != members.Length) throw new InvalidDataException(label + ": mã vật liệu không thuộc nhóm.");
                    }
                    if (row.ContainsKey("section_shape") && !new[] { "U", "compound_U", "unknown" }.Contains(Text(row, "section_shape"))) throw new InvalidDataException(label + ": hình dạng tiết diện không hợp lệ.");
                    if (table.Key == "maximum_curve_points")
                    {
                        string curve = Text(row, "curve_id"); if (!curveIds.Contains(curve)) throw new InvalidDataException(label + ": curve_id không tồn tại.");
                        if (!seqs.Add(curve + "/" + Integer(Value(row, "seq"), label + "/seq", 0))) throw new InvalidDataException("Điểm biên trùng curve/seq.");
                        var parent = curves[curve];
                        if ((Text(parent, "curve_kind") == "source_probe_endpoints" || new[] { "probe_endpoint", "maximum_admissible_depth" }.Contains(Text(parent, "boundary_semantics")))
                            && (Number(Value(row, "w_mm"), label) < 0 || Number(Value(row, "l_mm"), label) < 0))
                            throw new InvalidDataException(label + ": điểm giới hạn/probe W/L không được âm.");
                    }
                    if (table.Key == "maximum_curves")
                    {
                        if (Integer(Value(row, "automatic_check_enabled"), label + "/automatic", 0) != 0) throw new InvalidDataException("Nguồn max chưa đăng ký datum; automatic_check_enabled phải bằng 0.");
                        if (Text(row, "units") != "mm" || !new[] { "tool_contour", "source_probe_endpoints", "user_defined_curve" }.Contains(Text(row, "curve_kind"))
                            || !new[] { "collision_contour", "probe_endpoint", "maximum_admissible_depth", "unclassified" }.Contains(Text(row, "boundary_semantics"))
                            || !new[] { "unregistered", "verified" }.Contains(Text(row, "datum_registration_status"))
                            || !new[] { "none", "piecewise_linear" }.Contains(Text(row, "interpolation_policy"))) throw new InvalidDataException(label + ": scope/gating max không hợp lệ.");
                        ValidateFiniteTree(JsonObjectField(Value(row, "source_coordinate_frame_json"), label), label);
                        ValidateFiniteTree(JsonObjectField(Value(row, "source_extent_json"), label), label);
                    }
                    if ((table.Key == "maximum_curves" || table.Key == "maximum_examples") && !new[] { "unreviewed", "digitization_checked" }.Contains(Text(row, "source_review_status")))
                        throw new InvalidDataException(label + ": trạng thái số hóa nguồn không hợp lệ.");
                    if (table.Key == "maximum_examples" && !new[] { "unclassified", "source_declared_limit", "illustration" }.Contains(Text(row, "source_outcome")))
                        throw new InvalidDataException(label + ": kết luận nguồn không hợp lệ.");
                }
            // Retain source contracts verbatim. Neither cell edits nor a loaded snapshot qualify max for approval.
            if (!contracts.ContainsKey("maximum_geometry_contract")) throw new InvalidDataException("Thiếu hợp đồng hình học max.");
        }
        private static void ValidateContracts(Dictionary<string, string> contracts, Dictionary<string, object> meta)
        {
            var policy = Object(Json().DeserializeObject(contracts["source_policy"]));
            string[] active = Array(Value(policy, "allowed_excel_sheets")).Cast<string>().ToArray();
            string[] excluded = Array(Value(policy, "excluded_excel_sheets")).Cast<string>().ToArray();
            if (Text(policy, "minimum_primary_source_id") != "src-minimum-bend-pdf-kcc20019" || excluded.Length != 1 || excluded[0] != "最小曲げ表"
                || active.Length != 4 || !Set("金型限界表 深曲げ限界_(2) 深曲げ限界 特型").SetEquals(active.Select(s => s.Replace(' ', '_'))))
                throw new InvalidDataException("Source policy phải dùng PDF minimum và đúng bốn sheet nguồn max; loại bảng Excel cũ.");
            var identity = Object(Json().DeserializeObject(contracts["catalog_identity"]));
            if (identity.Count != 4 || Text(identity, "database_id") != (string)Value(meta, "database_id")
                || Integer(Value(identity, "schema_version"), "identity schema", 1) != 3
                || Text(identity, "source_content_token") != (string)Value(meta, "content_token") || Text(identity, "runtime_contract") != "bend_tool_excel_v1")
                throw new InvalidDataException("Catalog identity không khớp THONG_TIN.");
            var units = Object(Json().DeserializeObject(contracts["units"]));
            if (Text(units, "length") != "mm" || Text(units, "angle") != "degree" || Text(units, "tool_load") != "kN/m" || Text(units, "blank") != "unknown_not_zero")
                throw new InvalidDataException("Đơn vị nguồn không đúng mm/degree/kN/m/unknown_not_zero.");
            foreach (string name in new[] { "minimum_geometry_contract", "maximum_geometry_contract", "bend_evaluation_contract" })
                ValidateFiniteTree(Object(Json().DeserializeObject(contracts[name])), name);
        }
        private static void ValidateGeometry(Dictionary<string, string> contracts, HashSet<string> toolIds)
        {
            var profiles = new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);
            var vertices = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
            foreach (var entry in contracts.Where(p => p.Key.StartsWith("geometry/profiles/", StringComparison.Ordinal)))
            {
                var row = Object(Value(Object(Json().DeserializeObject(entry.Value)), "row")); string id = Text(row, "id");
                if (!toolIds.Contains(Text(row, "tool_id")) || Text(row, "units") != "mm") throw new InvalidDataException("Profile nguồn thiếu dao/mm.");
                Integer(Value(row, "vertex_count"), "profile vertex_count", 1);
                profiles.Add(id, row); vertices.Add(id, new HashSet<int>());
            }
            foreach (var entry in contracts.Where(p => p.Key.StartsWith("geometry/profile_vertices/", StringComparison.Ordinal)))
            {
                var row = Object(Value(Object(Json().DeserializeObject(entry.Value)), "row")); string id = Text(row, "profile_id");
                int seq = Integer(Value(row, "seq"), "profile seq", 0);
                if (!vertices.ContainsKey(id) || !vertices[id].Add(seq)) throw new InvalidDataException("Vertex nguồn thiếu profile/seq bị trùng.");
                foreach (string field in new[] { "x", "y", "bulge" }) if (Math.Abs(Number(Value(row, field), "profile " + field)) >= 1e9) throw new InvalidDataException("Vertex nguồn ngoài phạm vi.");
            }
            foreach (var pair in profiles) if (Integer(Value(pair.Value, "vertex_count"), "profile count", 1) != vertices[pair.Key].Count) throw new InvalidDataException("Profile vertex_count không khớp nguồn.");
        }
        private static void ValidateFiniteTree(object value, string label)
        {
            if (value is IDictionary) foreach (object child in Object(value).Values) ValidateFiniteTree(child, label);
            else if (value is IEnumerable && !(value is string)) foreach (object child in Array(value)) ValidateFiniteTree(child, label);
            else if (value != null && !(value is string) && !(value is bool)) Number(value, label);
        }
        private static byte[] StableRead(string path, CancellationToken cancellation)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                cancellation.ThrowIfCancellationRequested();
                byte[] first = ReadBytes(path);
                if (cancellation.WaitHandle.WaitOne(40)) cancellation.ThrowIfCancellationRequested();
                byte[] second = ReadBytes(path);
                if (first.Length == second.Length && Hash(first) == Hash(second)) return second;
            }
            throw new IOException("Excel đang được lưu/thay file; hãy đợi lưu xong rồi đọc lại.");
        }
        private static byte[] ReadBytes(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (stream.Length > 64000000) throw new InvalidDataException("Excel dao chấn quá lớn (tối đa 64 MB).");
                using (var memory = new MemoryStream())
                {
                    var buffer = new byte[65536]; int read;
                    while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                    { if (memory.Length + read > 64000000) throw new InvalidDataException("Excel dao quá lớn."); memory.Write(buffer, 0, read); }
                    return memory.ToArray();
                }
            }
        }
        private static void ValidateAddress(string address)
        {
            Match match = Address.Match(address);
            int column = 0;
            if (!match.Success) throw new InvalidDataException("Địa chỉ ô không hợp lệ: " + address);
            foreach (char letter in match.Groups[1].Value) column = column * 26 + letter - 'A' + 1;
            if (column > 16384 || int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) > 1048576) throw new InvalidDataException("Ô vượt giới hạn Excel.");
        }

        private sealed class Cell
        { internal object Value; internal bool Formula; }
        private sealed partial class Sheet
        {
            private readonly Dictionary<string, Cell> cells = new Dictionary<string, Cell>(StringComparer.Ordinal);
            private readonly List<string> merges = new List<string>();
            internal int LastRow { get; private set; }
            internal Sheet(XDocument xml, List<string> strings)
            {
                foreach (XElement element in xml.Descendants(Ns + "c"))
                {
                    string address = ((string)element.Attribute("r") ?? "").ToUpperInvariant(); ValidateAddress(address);
                    if (cells.ContainsKey(address)) throw new InvalidDataException("Ô Excel bị trùng: " + address);
                    int row = int.Parse(Address.Match(address).Groups[2].Value, CultureInfo.InvariantCulture);
                    LastRow = Math.Max(LastRow, row);
                    if (cells.Count > 300000) throw new InvalidDataException("Sheet dao quá lớn.");
                    string type = (string)element.Attribute("t"); string raw = (string)element.Element(Ns + "v");
                    object value = null;
                    if (type == "inlineStr") value = string.Concat(element.Element(Ns + "is")?.Descendants(Ns + "t").Select(t => t.Value) ?? Enumerable.Empty<string>());
                    else if (type == "s") { int index; if (!int.TryParse(raw, out index) || index < 0 || index >= strings.Count) throw new InvalidDataException("Shared string không hợp lệ."); value = strings[index]; }
                    else if (type == "b") value = raw == "1";
                    else if (type == "str" || type == "e") value = raw;
                    else if (raw != null) { double n; if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out n) || double.IsNaN(n) || double.IsInfinity(n)) throw new InvalidDataException("Số ô Excel không hợp lệ: " + address); value = n; }
                    cells.Add(address, new Cell { Value = value, Formula = element.Element(Ns + "f") != null || type == "e" });
                }
                foreach (XElement merge in xml.Descendants(Ns + "mergeCell")) merges.Add((string)merge.Attribute("ref"));
            }
            internal object At(string address)
            {
                address = address.ToUpperInvariant(); Cell cell;
                if (cells.TryGetValue(address, out cell))
                { if (cell.Formula) throw new InvalidDataException("Ô " + address + " chứa công thức/lỗi; hãy nhập giá trị trực tiếp."); return cell.Value; }
                return null;
            }
            internal object AtMapped(string address)
            {
                foreach (string merge in merges)
                {
                    string[] ends = (merge ?? "").ToUpperInvariant().Split(':');
                    if (ends.Length != 2) throw new InvalidDataException("Vùng merge Excel không hợp lệ.");
                    ValidateAddress(ends[0]); ValidateAddress(ends[1]);
                    int[] first = Position(ends[0]), last = Position(ends[1]), target = Position(address);
                    if (target[0] >= first[0] && target[0] <= last[0] && target[1] >= first[1] && target[1] <= last[1] && address != ends[0])
                        throw new InvalidDataException("Ô ánh xạ nằm trong vùng merge nhưng không phải ô đầu: " + address);
                }
                return At(address);
            }
            private static int[] Position(string address)
            {
                Match match = Address.Match(address); int column = 0;
                foreach (char c in match.Groups[1].Value) column = column * 26 + c - 'A' + 1;
                return new[] { column, int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) };
            }
            internal void Headers(string[] expected)
            {
                for (int i = 0; i < expected.Length; i++) if (!Equals(At(Column(i + 1) + "5"), expected[i])) throw new InvalidDataException("Header hàng 5 không đúng hợp đồng: " + expected[i]);
                foreach (var cell in cells.Where(p => Address.Match(p.Key).Groups[2].Value == "5" && Position(p.Key)[0] > expected.Length))
                    if (cell.Value.Value != null && !Equals(cell.Value.Value, "")) throw new InvalidDataException("Header metadata thừa cột.");
            }
            internal IEnumerable<object[]> Rows(int start, int columns)
            {
                if (LastRow > 40000) throw new InvalidDataException("Metadata dao vượt 40000 dòng.");
                for (int row = start; row <= LastRow; row++) yield return Enumerable.Range(1, columns).Select(c => At(Column(c) + row)).ToArray();
            }
            private static string Column(int index)
            { string result = ""; while (index > 0) { index--; result = (char)('A' + index % 26) + result; index /= 26; } return result; }
        }
        private sealed class Workbook
        {
            private readonly ZipArchive zip;
            private readonly Dictionary<string, string> paths = new Dictionary<string, string>(StringComparer.Ordinal);
            private readonly Dictionary<string, Sheet> loaded = new Dictionary<string, Sheet>(StringComparer.Ordinal);
            private readonly List<string> strings;
            internal Workbook(ZipArchive source)
            {
                zip = source;
                if (zip.Entries.GroupBy(e => e.FullName, StringComparer.Ordinal).Any(g => g.Count() > 1)) throw new InvalidDataException("Excel có ZIP entry trùng.");
                XDocument shared = Xml("xl/sharedStrings.xml", false);
                strings = shared == null ? new List<string>() : shared.Descendants(Ns + "si").Select(si => string.Concat(si.Descendants(Ns + "t").Select(t => t.Value))).ToList();
                var relationships = Xml("xl/_rels/workbook.xml.rels", true).Descendants(RelNs + "Relationship").ToDictionary(r => (string)r.Attribute("Id"), StringComparer.Ordinal);
                foreach (XElement sheet in Xml("xl/workbook.xml", true).Descendants(Ns + "sheet"))
                {
                    string name = (string)sheet.Attribute("name"), id = (string)sheet.Attribute(DocRelNs + "id");
                    XElement relation;
                    if (name == null || paths.ContainsKey(name) || id == null || !relationships.TryGetValue(id, out relation) || (string)relation.Attribute("TargetMode") == "External") throw new InvalidDataException("Sheet/relationship Excel không hợp lệ.");
                    string target = (string)relation.Attribute("Target");
                    if (target == null || target.Contains("..") || target.Contains('\\')) throw new InvalidDataException("Đường dẫn sheet không hợp lệ.");
                    paths.Add(name, target.StartsWith("/", StringComparison.Ordinal) ? target.TrimStart('/') : "xl/" + target);
                }
            }
            internal bool Contains(string name) { return paths.ContainsKey(name); }
            internal Sheet Sheet(string name)
            {
                Sheet result;
                if (!paths.ContainsKey(name)) throw new InvalidDataException("Excel thiếu sheet " + name + ".");
                if (!loaded.TryGetValue(name, out result)) { result = new Sheet(Xml(paths[name], true), strings); loaded.Add(name, result); }
                return result;
            }
            private XDocument Xml(string path, bool required)
            {
                ZipArchiveEntry entry = zip.GetEntry(path);
                if (entry == null) { if (required) throw new InvalidDataException("Excel thiếu XML " + path); return null; }
                if (entry.Length > 64000000) throw new InvalidDataException("XML Excel quá lớn.");
                using (Stream stream = entry.Open())
                using (XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 64000000 }))
                    return XDocument.Load(reader, LoadOptions.None);
            }
        }
    }
}
