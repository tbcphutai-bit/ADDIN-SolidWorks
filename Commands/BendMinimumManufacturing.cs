using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands
{
    internal sealed class BendMinimumMaterialInfo
    {
        internal string Name, Code, Category, DatabaseName, DatabasePath, Note;
    }

    // Resolve the material actually assigned to the body/configuration. Read the
    // classification in its sldmat, rather than guessing a grade from its prefix.
    // One cache belongs to one scan; Quet lai rereads edited material libraries.
    internal sealed class BendMinimumMaterialLibraries
    {
        internal static readonly string LibraryDirectory = @"C:\sw共有ライブラリ\材料ﾃﾞｰﾀﾍﾞｰｽ";
        internal static readonly string[] LibraryNames = { "kiku_板材_材料 2019.sldmat", "kiku_棒材_材料 2019.sldmat" };
        private readonly Dictionary<string, List<BendMinimumMaterialInfo>> cache = new Dictionary<string, List<BendMinimumMaterialInfo>>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> locations;
        internal BendMinimumMaterialLibraries(IEnumerable<string> directories)
        { locations = (directories ?? Enumerable.Empty<string>()).Concat(new[] { LibraryDirectory }).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(); }

        internal BendMinimumMaterialInfo Read(ISldWorks app, ModelDoc2 model, Body2 body, string configuration)
        {
            string database;
            string name = BendMinimumMaterial.Read(model, body, configuration, out database);
            string folder = Path.GetDirectoryName(model.GetPathName() ?? "");
            return Resolve(name, database, folder);
        }
        internal BendMinimumMaterialInfo Resolve(string name, string database, string modelFolder)
        {
            var info = new BendMinimumMaterialInfo { Name = name ?? "", DatabaseName = database ?? "" };
            if (string.IsNullOrWhiteSpace(name)) { info.Note = "Model chưa có vật liệu."; return info; }
            var paths = new List<string>();
            if (!string.IsNullOrWhiteSpace(database))
            {
                string file = database.Trim();
                if (!file.EndsWith(".sldmat", StringComparison.OrdinalIgnoreCase)) file += ".sldmat";
                if (Path.IsPathRooted(file)) AddPath(paths, file);
                if (!string.IsNullOrWhiteSpace(modelFolder)) AddPath(paths, Path.Combine(modelFolder, Path.GetFileName(file)));
                foreach (string location in locations)
                    if (Directory.Exists(location)) AddPath(paths, Path.Combine(location, Path.GetFileName(file)));
                    else if (string.Equals(Path.GetFileName(location), Path.GetFileName(file), StringComparison.OrdinalIgnoreCase)) AddPath(paths, location);
                // A saved database reference has priority over other libraries.
                paths = paths.Take(1).ToList();
            }
            else foreach (string location in locations)
            {
                if (Directory.Exists(location)) foreach (string file in LibraryNames) AddPath(paths, Path.Combine(location, file));
                else if (location.EndsWith(".sldmat", StringComparison.OrdinalIgnoreCase)) AddPath(paths, location);
            }
            var matches = new List<BendMinimumMaterialInfo>();
            try
            {
                foreach (string path in paths)
                {
                    List<BendMinimumMaterialInfo> values;
                    if (!cache.TryGetValue(path, out values)) { values = ReadLibrary(path); cache.Add(path, values); }
                    matches.AddRange(values.Where(v => Identity(v.Name) == Identity(name)));
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is XmlException || ex is InvalidDataException)
            { info.Note = "Không đọc được thư viện vật liệu: " + ex.Message; return info; }
            if (matches.Count > 0 && matches.Select(v => (v.Code ?? "") + "/" + v.Category).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1)
            {
                var found = matches[0]; info.Code = found.Code; info.Category = found.Category; info.DatabasePath = found.DatabasePath;
                info.Note = string.IsNullOrEmpty(found.Code) ? "Nhóm vật liệu trong thư viện chưa có ánh xạ sang nhóm của bảng tối thiểu." : "Nhóm vật liệu lấy từ classification trong thư viện SolidWorks.";
            }
            else if (matches.Count > 0) info.Note = "Tên vật liệu trùng ở các nhóm khác nhau; chưa xác định được nhóm kiểm tra.";
            else
            {
                // Exact workbook codes are also usable on imported Parts with a
                // literal material property. Arbitrary grade prefixes are not.
                info.Code = BendMinimumMaterial.Code(name);
                info.Note = paths.Count == 0 ? "Không tìm thấy thư viện vật liệu đã gán." : "Không tìm thấy đúng tên vật liệu trong thư viện đã gán.";
            }
            return info;
        }
        private static void AddPath(List<string> values, string path)
        { if (File.Exists(path)) { string full = Path.GetFullPath(path); if (!values.Contains(full, StringComparer.OrdinalIgnoreCase)) values.Add(full); } }
        internal static string Identity(string value)
        {
            string text = (value ?? "").Normalize(NormalizationForm.FormKC).Trim().ToUpperInvariant();
            int split = text.LastIndexOf("::", StringComparison.Ordinal); return split < 0 ? text : text.Substring(split + 2).Trim();
        }
        internal static string Group(string category, string name)
        {
            switch (Identity(category))
            {
                case "ST材": case "スチール": return "ST";
                case "SUS材": case "ステンレス": return "SUS";
                case "AL材": case "アルミ": return "AL";
                case "CU材": case "銅":
                    string metal = Identity(name).TrimEnd('-').TrimEnd();
                    return metal == "BS" ? "BS" : metal == "RBS" ? "RBS" : "CU";
                case "TI材": return "TI";
                default: return null;
            }
        }
        internal static List<BendMinimumMaterialInfo> ReadLibrary(string path)
        {
            byte[] bytes;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length > 16 * 1024 * 1024) throw new InvalidDataException("Thư viện vật liệu vượt 16 MB.");
                using (var memory = new MemoryStream()) { stream.CopyTo(memory); bytes = memory.ToArray(); }
            }
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 32 * 1024 * 1024 };
            var document = new XmlDocument { XmlResolver = null };
            using (var memory = new MemoryStream(bytes)) using (var reader = XmlReader.Create(memory, settings)) document.Load(reader);
            var result = new List<BendMinimumMaterialInfo>();
            foreach (XmlNode node in document.SelectNodes("//*[local-name()='material']"))
            {
                string name = node.Attributes?["name"]?.Value;
                if (string.IsNullOrWhiteSpace(name)) continue;
                string category = null, group = null;
                for (XmlNode parent = node.ParentNode; parent != null; parent = parent.ParentNode)
                {
                    if (parent.LocalName != "classification") continue;
                    string value = parent.Attributes?["name"]?.Value;
                    if (category == null) category = value;
                    string mapped = Group(value, name);
                    if (mapped != null) { category = value; group = mapped; break; }
                }
                result.Add(new BendMinimumMaterialInfo { Name = name, Code = group, Category = category, DatabasePath = Path.GetFullPath(path) });
            }
            return result;
        }
    }

    internal enum BendMinimumGrooveState { None, Grooved, Unknown }
    internal sealed class BendMinimumGrooveInfo
    {
        internal BendMinimumGrooveState State;
        internal string Kind, FeatureName, Note;
    }
    internal sealed class BendMinimumFeatureLink
    {
        internal int Id;
        internal string Name;
        internal bool ParentsKnown = true;
        internal readonly List<BendMinimumFeatureLink> Parents = new List<BendMinimumFeatureLink>();
    }

    // Read document-owned Set Bend line tags. Never create/migrate attributes,
    // infer all bends from a Part-level V property, or infer groove from radius.
    internal sealed class BendMinimumGrooveAssignments
    {
        internal readonly Dictionary<int, string> Tags = new Dictionary<int, string>();
        internal readonly HashSet<int> Malformed = new HashSet<int>();
        private string readError;
        private static readonly string[] ParentTypes = { "EdgeFlange", "OneBend", "SMBend", "SketchBend", "Hem", "SMMiteredFlange", "SMBaseFlange" };
        internal BendMinimumGrooveAssignments() { }
        internal BendMinimumGrooveAssignments(ModelDoc2 model)
        {
            try
            {
                var seen = new HashSet<string>(StringComparer.Ordinal); int count = 0;
                for (Feature feature = model.FirstFeature() as Feature; feature != null; feature = feature.GetNextFeature() as Feature)
                {
                    if (++count > 10000 || !seen.Add(feature.Name + "/" + feature.GetTypeName2())) { readError = "Không đọc hết được thông tin gán bào trong Part."; break; }
                    const string prefix = "ADDIN_VCut_Bend_";
                    if (!(feature.Name ?? "").StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                    int id;
                    if (!int.TryParse(feature.Name.Substring(prefix.Length), out id) || id <= 0) continue;
                    var attribute = feature.GetSpecificFeature2() as SolidWorks.Interop.sldworks.Attribute;
                    var parameter = attribute?.GetParameter("PropertyName") as Parameter;
                    string raw = parameter?.GetStringValue();
                    string kind = raw == null ? null : raw.Normalize(NormalizationForm.FormKC).Trim();
                    if (kind == null || kind != "" && kind != "V溝1" && kind != "V溝2" && kind != "C溝" || Tags.ContainsKey(id)) Malformed.Add(id);
                    else Tags.Add(id, kind);
                }
            }
            catch (Exception ex) { readError = "Không đọc được thông tin gán bào: " + ex.Message; }
        }
        internal BendMinimumGrooveInfo Read(BendMinimumSite site)
        {
            try
            {
                var features = site.Faces.Select(f => f.GetFeature() as Feature).ToList();
                if (features.Count == 0 || features.Any(f => f == null)) return Unknown("Không xác định được feature của đường chấn.");
                var results = features.GroupBy(f => f.GetID()).Select(g => Read(g.First())).ToList();
                if (results.Any(r => r.State == BendMinimumGrooveState.Unknown)) return results.First(r => r.State == BendMinimumGrooveState.Unknown);
                if (results.Any(r => r.State == BendMinimumGrooveState.Grooved) && results.Any(r => r.State == BendMinimumGrooveState.None)) return Unknown("Các mặt của đường chấn có thông tin bào khác nhau.");
                return new BendMinimumGrooveInfo { State = results.All(r => r.State == BendMinimumGrooveState.Grooved) ? BendMinimumGrooveState.Grooved : BendMinimumGrooveState.None,
                    Kind = string.Join(" / ", results.Select(r => r.Kind).Where(s => !string.IsNullOrEmpty(s)).Distinct()), FeatureName = string.Join(" / ", results.Select(r => r.FeatureName).Distinct()),
                    Note = "Đọc gán bào của feature sở hữu mặt chấn." };
            }
            catch (Exception ex) { return Unknown("Không đọc được feature/gán bào của đường chấn: " + ex.Message); }
        }
        internal BendMinimumGrooveInfo Read(Feature feature)
        {
            if (readError != null) return Unknown(readError);
            try
            {
                var links = new Dictionary<int, BendMinimumFeatureLink>();
                var queue = new Queue<Feature>(); queue.Enqueue(feature);
                BendMinimumFeatureLink root = null;
                while (queue.Count > 0)
                {
                    Feature native = queue.Dequeue(); int id = native.GetID();
                    if (id <= 0) return Unknown("Feature chấn không có ID để đọc gán bào.");
                    BendMinimumFeatureLink link;
                    if (!links.TryGetValue(id, out link)) { link = new BendMinimumFeatureLink { Id = id, Name = native.Name }; links.Add(id, link); }
                    if (root == null) root = link;
                    if (links.Count > 128) return Unknown("Quá nhiều feature cha để đọc gán bào.");
                    if (Tags.ContainsKey(id) || Malformed.Contains(id)) continue;
                    var parents = native.GetParents() as Array;
                    if (parents == null) continue;
                    foreach (object item in parents)
                    {
                        var parent = item as Feature;
                        if (parent == null || !ParentTypes.Contains(parent.GetTypeName2())) continue;
                        int parentId = parent.GetID(); BendMinimumFeatureLink ancestor;
                        if (!links.TryGetValue(parentId, out ancestor)) { ancestor = new BendMinimumFeatureLink { Id = parentId, Name = parent.Name }; links.Add(parentId, ancestor); queue.Enqueue(parent); }
                        link.Parents.Add(ancestor);
                    }
                }
                return Resolve(root, Tags, Malformed);
            }
            catch (Exception ex) { return Unknown("Không đọc được gán bào của feature: " + ex.Message); }
        }
        internal static BendMinimumGrooveInfo Resolve(BendMinimumFeatureLink feature, IDictionary<int, string> tags, ISet<int> malformed)
        {
            if (feature == null) return Unknown("Chưa xác định được feature chấn.");
            var queue = new Queue<BendMinimumFeatureLink>(); queue.Enqueue(feature);
            var seen = new HashSet<int>(); var kinds = new HashSet<string>(StringComparer.Ordinal); bool normal = false;
            while (queue.Count > 0)
            {
                var current = queue.Dequeue(); if (!seen.Add(current.Id)) continue;
                if (current.Id <= 0 || seen.Count > 128 || !current.ParentsKnown) return Unknown("Chưa đọc đủ feature cha để xác định gán bào.");
                if (malformed.Contains(current.Id)) return Unknown("Tag gán bào bị trùng hoặc không hợp lệ.");
                string kind;
                if (tags.TryGetValue(current.Id, out kind))
                {
                    if (string.IsNullOrEmpty(kind)) normal = true;
                    else if (kind == "V溝1" || kind == "V溝2" || kind == "C溝") kinds.Add(kind);
                    else return Unknown("Loại bào đã gán không hợp lệ.");
                    continue;
                }
                foreach (var parent in current.Parents) queue.Enqueue(parent);
            }
            if (normal && kinds.Count > 0) return Unknown("Các feature cha có gán bào và không bào khác nhau.");
            return new BendMinimumGrooveInfo { State = kinds.Count > 0 ? BendMinimumGrooveState.Grooved : BendMinimumGrooveState.None, Kind = string.Join(" / ", kinds.OrderBy(s => s)), FeatureName = feature.Name };
        }
        private static BendMinimumGrooveInfo Unknown(string note)
        { return new BendMinimumGrooveInfo { State = BendMinimumGrooveState.Unknown, Note = note }; }
    }
}
