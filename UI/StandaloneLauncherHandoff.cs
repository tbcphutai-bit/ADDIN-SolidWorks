using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using ADDIN.Commands;
using SolidWorks.Interop.sldworks;

namespace ADDIN
{
    // Only a launcher/data bridge. All checking and conversion stay in the macro DLL.
    public partial class BomTaskPaneControl
    {
        // Optional COM/CLI entry point. A normal button click uses the current loaded BOM.
        public void OpenStandaloneCurveMacro(string selectedPartNumbers)
        {
            if (!string.IsNullOrWhiteSpace(selectedPartNumbers))
            {
                btnLoadBom_Click(btnLoadBom, EventArgs.Empty);
                if (actions == null || actions.LoadedBomContext != BomCommandContext.Detail)
                    throw new InvalidOperationException("Chọn đúng BOM chi tiết trên Drawing trước khi chạy lệnh.");
                var requested = new HashSet<string>(selectedPartNumbers.Split(';').Select(p => p.Trim()).Where(p => p.Length > 0), StringComparer.Ordinal);
                var rows = dgvModelBom.Rows.Cast<DataGridViewRow>().Where(r => !r.IsNewRow).ToList();
                foreach (string part in requested)
                    if (rows.Count(r => Convert.ToString(r.Cells[1].Value).Trim() == part) != 1)
                        throw new InvalidOperationException("Số chi tiết không duy nhất trong BOM: " + part);
                foreach (var row in rows) row.Cells[0].Value = requested.Contains(Convert.ToString(row.Cells[1].Value).Trim());
            }
            RunStandaloneCurveMacro();
        }

        public string GetStandaloneCurveMacroState()
        {
            return new JavaScriptSerializer().Serialize(new {
                DrawingCheckRuntime = "silent-current-session-20261012",
                AddinModuleVersion = typeof(BomTaskPaneControl).Assembly.ManifestModule.ModuleVersionId.ToString(),
                OldButton = btnCheckDxf == null ? null : btnCheckDxf.Text,
                NewButton = btnStandaloneCurveMacro == null ? null : btnStandaloneCurveMacro.Text,
                Parent = btnStandaloneCurveMacro == null || btnStandaloneCurveMacro.Parent == null ? null : btnStandaloneCurveMacro.Parent.Name,
                Rows = dgvModelBom.Rows.Cast<DataGridViewRow>().Count(r => !r.IsNewRow),
                SelectedParts = dgvModelBom.Rows.Cast<DataGridViewRow>().Where(r => !r.IsNewRow && Convert.ToBoolean(r.Cells[0].Value ?? false)).Select(r => Convert.ToString(r.Cells[1].Value)).ToArray(),
                BomPath = loadedDxfBomPath
            });
        }

        private sealed class StandaloneLaunchRequest
        {
            public int Schema = 1;
            public string RequestId, CreatedUtc, BomPath;
            public int SwProcessId;
            public List<StandaloneLaunchRow> Rows = new List<StandaloneLaunchRow>();
        }
        private sealed class StandaloneLaunchRow
        {
            public string ItemNumber, PartNumber, FileName, ModelPath, Configuration, DrawingPath, Error;
            public bool SourceAmbiguous;
        }

        private void RunStandaloneCurveMacro()
        {
            if (swApp == null) { lblStatus.Text = "Chưa kết nối SOLIDWORKS."; return; }
            if (drawingBomCommandInProgress) return;
            // Leave native input available for choosing source sketches in the macro.
            RunDrawingBomCommand(() =>
            {
                string requestPath = null, writtenRequest = null;
                try
                {
                    string dll = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments),
                        "addin", "Macros", "CheckDrwDxfStandalone_20261009", "bin", "CheckDrwDxfStandalone.UI2.dll");
                    if (!File.Exists(dll)) throw new FileNotFoundException("Không tìm thấy macro rời: " + dll);
                    var request = new StandaloneLaunchRequest {
                        RequestId = Guid.NewGuid().ToString("N"), SwProcessId = swApp.GetProcessID(),
                        CreatedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
                    };
                    bool detailLoaded = actions != null && actions.LoadedBomContext == BomCommandContext.Detail;
                    if (detailLoaded)
                    {
                        if (string.IsNullOrWhiteSpace(loadedDxfBomPath)) throw new InvalidOperationException("BOM chưa có đường dẫn Drawing. Hãy Cập nhật BOM lại.");
                        var bom = swApp.GetOpenDocumentByName(loadedDxfBomPath) as ModelDoc2;
                        if (bom == null || !StandaloneSamePath(bom.GetPathName(), loadedDxfBomPath))
                            throw new InvalidOperationException("Drawing chứa BOM đã đóng hoặc đổi. Hãy mở và Cập nhật BOM lại.");
                        if (dgvModelBom.IsCurrentCellDirty) dgvModelBom.CommitEdit(DataGridViewDataErrorContexts.Commit);
                        dgvModelBom.EndEdit();
                        var selected = dgvModelBom.Rows.Cast<DataGridViewRow>().Where(r => !r.IsNewRow && Convert.ToBoolean(r.Cells[0].Value ?? false)).ToList();
                        if (selected.Count == 0) throw new InvalidOperationException("Tick ít nhất một dòng BOM để mở macro kiểm tra.");
                        request.BomPath = Path.GetFullPath(loadedDxfBomPath);
                        var savedFiles = StandaloneSavedFiles(Path.GetDirectoryName(request.BomPath));
                        var resolver = drawingBomChecker ?? new CheckDrawingBom(swApp, dgvModelBom);
                        foreach (DataGridViewRow row in selected) request.Rows.Add(CaptureStandaloneRow(row, bom, resolver, savedFiles));
                    }
                    string folder = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "TAI_TOOL", "STANDALONE_CURVE_MACRO");
                    Directory.CreateDirectory(folder);
                    requestPath = Path.Combine(folder, "launch-" + request.SwProcessId.ToString(CultureInfo.InvariantCulture) + ".json");
                    // Write a complete request before RunMacro2; no dynamic environment inheritance is required.
                    string temporary = requestPath + "." + request.RequestId + ".tmp";
                    writtenRequest = new JavaScriptSerializer { MaxJsonLength = 10000000 }.Serialize(request);
                    File.WriteAllText(temporary, writtenRequest, new UTF8Encoding(true));
                    if (File.Exists(requestPath)) File.Replace(temporary, requestPath, null); else File.Move(temporary, requestPath);
                    int error;
                    bool started = swApp.RunMacro2(dll, "", "Main", 0, out error);
                    if (!started) throw new InvalidOperationException("Không chạy được macro rời; RunMacro2 error=" + error + ".");
                    lblStatus.Text = request.Rows.Count == 0 ? "Đã mở macro rời; chọn Drawing/Part để làm thủ công." : "Đã mở macro rời với " + request.Rows.Count + " dòng BOM đã tick.";
                }
                catch (Exception ex) { lblStatus.Text = "Macro rời: " + ex.Message; }
                finally
                {
                    // The macro consumes the request on entry. Clean up any unconsumed request when it returns.
                    if (requestPath != null) try { if (File.Exists(requestPath) && File.ReadAllText(requestPath) == writtenRequest) File.Delete(requestPath); } catch { }
                }
            }, false, true);
        }

        private StandaloneLaunchRow CaptureStandaloneRow(DataGridViewRow row, ModelDoc2 bom, CheckDrawingBom resolver, List<string> files)
        {
            var numbers = row.Cells[1].Tag as BomNumberValues;
            var result = new StandaloneLaunchRow {
                ItemNumber = numbers == null ? "" : numbers.ItemNumber,
                PartNumber = Convert.ToString(row.Cells[1].Value ?? "").Trim(),
                FileName = Convert.ToString(row.Cells[5].Value ?? "").Trim()
            };
            try
            {
                object[] components = row.Tag as object[];
                if (components != null && components.Length > 0)
                {
                    var typed = components.OfType<Component2>().ToList();
                    if (typed.Count != components.Length) { result.SourceAmbiguous = true; throw new InvalidDataException("Dòng BOM chứa đối tượng nguồn chưa xác định."); }
                    var sources = typed.Select(c => new { Path = c.GetPathName(), Config = c.ReferencedConfiguration ?? "" }).ToList();
                    if (sources.Any(p => string.IsNullOrWhiteSpace(p.Path))) { result.SourceAmbiguous = true; throw new InvalidDataException("Một component của dòng BOM chưa có đường dẫn nguồn."); }
                    var pairs = sources.GroupBy(p => Path.GetFullPath(p.Path).ToUpperInvariant() + "|" + p.Config, StringComparer.Ordinal).Select(g => g.First()).ToList();
                    if (pairs.Count != 1) { result.SourceAmbiguous = true; throw new InvalidDataException("Dòng BOM gộp nhiều model/configuration hoặc chưa xác định nguồn; cần tách dòng."); }
                    result.ModelPath = Path.GetFullPath(pairs[0].Path); result.Configuration = pairs[0].Config;
                }
                else if (row.Tag is string && !string.IsNullOrWhiteSpace((string)row.Tag)) result.ModelPath = Path.GetFullPath((string)row.Tag);
                else
                {
                    string filename = result.FileName.Trim('"');
                    if (Path.IsPathRooted(filename) && File.Exists(filename)) result.ModelPath = Path.GetFullPath(filename);
                    else
                    {
                        var matches = StandaloneExactFiles(files, Path.GetFileNameWithoutExtension(filename), ".sldprt", ".sldasm");
                        if (matches.Count != 1) { result.SourceAmbiguous = matches.Count > 1; throw new InvalidDataException(matches.Count == 0 ? "Không tìm thấy model đúng tên file BOM." : "Có nhiều model cùng tên file BOM; chưa chọn đường dẫn nguồn."); }
                        result.ModelPath = matches[0];
                    }
                    // General/cached BOM has no configuration in the current grid. Keep it unknown.
                }
                if (!File.Exists(result.ModelPath) || Path.GetFileName(result.ModelPath).StartsWith("~$", StringComparison.Ordinal) ||
                    !(Path.GetExtension(result.ModelPath).Equals(".sldprt", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(result.ModelPath).Equals(".sldasm", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("Đường dẫn model nguồn chưa hợp lệ hoặc file chưa lưu.");
                string resolvedPart;
                string resolvedDrawing = resolver.ResolveBomDrawingPath(row, bom, out resolvedPart);
                if (!string.IsNullOrWhiteSpace(resolvedPart) && !StandaloneSamePath(resolvedPart, result.ModelPath))
                { result.SourceAmbiguous = true; throw new InvalidDataException("Nguồn model từ BOM khác nguồn bộ tìm Drawing; chưa tự chọn một nguồn."); }
                string adjacent = Path.ChangeExtension(result.ModelPath, ".SLDDRW");
                if (File.Exists(adjacent))
                {
                    if (!string.IsNullOrWhiteSpace(resolvedDrawing) && !StandaloneSamePath(resolvedDrawing, adjacent))
                    { result.SourceAmbiguous = true; throw new InvalidDataException("Drawing tìm được khác Drawing cạnh model nguồn; cần chọn đúng nguồn."); }
                    result.DrawingPath = adjacent;
                }
                else
                {
                    var matches = StandaloneExactFiles(files, Path.GetFileNameWithoutExtension(result.ModelPath), ".slddrw");
                    if (matches.Count != 1) { result.SourceAmbiguous = matches.Count > 1; throw new InvalidDataException(matches.Count == 0 ? "Không tìm thấy Drawing đúng model nguồn." : "Có nhiều Drawing cùng tên; cần chọn đường dẫn chính xác."); }
                    if (!string.IsNullOrWhiteSpace(resolvedDrawing) && !StandaloneSamePath(resolvedDrawing, matches[0]))
                    { result.SourceAmbiguous = true; throw new InvalidDataException("Đường dẫn Drawing không khớp nguồn duy nhất đã xác định."); }
                    result.DrawingPath = matches[0];
                }
            }
            catch (Exception ex) { result.Error = ex.Message; }
            return result;
        }
        private static List<string> StandaloneExactFiles(List<string> files, string stem, params string[] extensions)
        {
            return files.Where(p => extensions.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase) && Path.GetFileNameWithoutExtension(p).Equals(stem, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
        private static bool StandaloneSamePath(string a, string b)
        {
            return !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) && Path.GetFullPath(a).Equals(Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
        private static List<string> StandaloneSavedFiles(string root)
        {
            var files = new List<string>(); var pending = new Queue<KeyValuePair<string, int>>(); pending.Enqueue(new KeyValuePair<string, int>(root, 0)); int count = 0;
            while (pending.Count > 0)
            {
                if (++count > 5000) throw new InvalidDataException("Thư mục BOM quá rộng; chưa xác nhận được nguồn duy nhất.");
                var next = pending.Dequeue();
                foreach (string file in Directory.EnumerateFiles(next.Key, "*.*", SearchOption.TopDirectoryOnly))
                    if (!Path.GetFileName(file).StartsWith("~$", StringComparison.Ordinal) && new[] { ".sldprt", ".sldasm", ".slddrw" }.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)) files.Add(Path.GetFullPath(file));
                foreach (string dir in Directory.EnumerateDirectories(next.Key))
                {
                    string name = Path.GetFileName(dir).ToUpperInvariant();
                    if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0 || name.StartsWith("CHECK_") || name.StartsWith("BACKUP") || name.StartsWith("_BACKUP") || name == "_CODEX_BACKUP" || name == "MIRROR_DIAGNOSTICS") continue;
                    if (next.Value >= 6) throw new InvalidDataException("Thư mục BOM quá sâu; chưa xác nhận được nguồn duy nhất.");
                    pending.Enqueue(new KeyValuePair<string, int>(dir, next.Value + 1));
                }
            }
            return files;
        }
    }
}
