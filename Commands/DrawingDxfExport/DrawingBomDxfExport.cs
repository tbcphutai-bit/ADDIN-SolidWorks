using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using ADDIN.Commands.DxfChecking;
using SolidWorks.Interop.sldworks;

namespace ADDIN.Commands.DrawingDxfExporting
{
    internal sealed class BomDxfBatchResult
    {
        public string BomPath, TargetFolder, ReportPath, TracePath, Message, CheckReportPath, CheckOutputFolder;
        public int SelectedRows, ProcessedRows;
        public bool Canceled;
        public bool CheckWhileExporting;
        public double TotalMilliseconds;
        public readonly List<BomDxfFileResult> Items = new List<BomDxfFileResult>();
        public int Exported { get { return Items.Count(i => i.Status == "OK" || i.Status == "WARN"); } }
        public int Errors { get { return Items.Count(i => i.Status == "ERROR"); } }
        public int Reused { get { return Items.Count(i => i.Status == "REUSED"); } }
        internal DxfBatchResult CheckBatch;
        public int CheckedFiles { get { return CheckPassed + CheckNg + CheckErrors; } }
        public int CheckPassed { get { return Items.Count(i => i.Status != "REUSED" && i.CheckStatus == "OK"); } }
        public int CheckNg { get { return Items.Count(i => i.Status != "REUSED" && i.CheckStatus == "NG"); } }
        public int CheckErrors { get { return Items.Count(i => i.Status != "REUSED" && i.CheckStatus == "ERROR"); } }
    }

    internal static class DxfSourceIdentity
    {
        internal static bool SamePath(string first, string second)
        {
            if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second)) return false;
            try { return string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase); }
            catch (ArgumentException) { return false; }
        }
        internal static bool ConfigurationMatches(string actual, string expected)
        {
            if (string.IsNullOrWhiteSpace(expected)) return true;
            if (string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)) return true;
            const string suffix = "SM-FLAT-PATTERN";
            if (actual == null || !actual.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return false;
            string parent = actual.Substring(0, actual.Length - suffix.Length).TrimEnd('-', '_', ' ');
            return string.Equals(parent, expected, StringComparison.OrdinalIgnoreCase);
        }
        internal static string Key(BomDxfPart part)
        { return Path.GetFullPath(part.ComponentPath) + "|" + (part.ConfigurationName ?? ""); }
    }

    internal sealed class DrawingBomDxfExport
    {
        private readonly ISldWorks app;
        private readonly CheckDrawingBom resolver;
        internal DrawingBomDxfExport(ISldWorks application, DataGridView grid)
        { app = application; resolver = new CheckDrawingBom(application, grid); }

        private sealed class WorkItem
        {
            internal BomDxfPart Part;
            internal BomDxfFileResult Result;
        }

        internal BomDxfBatchResult Run(DataGridView grid, string bomPath, BomDxfExportOptions options,
            Action<int> begin, Action<int, int> progress, Action finish, Func<bool> cancel,
            Action<BomDxfFileResult, int, int> onItem = null)
        {
            if (app == null || grid == null) throw new InvalidOperationException("Chưa kết nối SolidWorks/BOM.");
            if (string.IsNullOrWhiteSpace(bomPath)) throw new InvalidOperationException("Bấm Cập nhật để load BOM chi tiết trước khi xuất DXF.");
            if (options == null || string.IsNullOrWhiteSpace(options.TargetFolder))
                throw new InvalidOperationException("Chưa chọn thư mục xuất DXF.");
            var bom = app.GetOpenDocumentByName(bomPath) as ModelDoc2;
            if (bom == null || !DxfSourceIdentity.SamePath(bom.GetPathName(), bomPath))
                throw new InvalidOperationException("Drawing chứa BOM đã đóng hoặc đổi tên. Hãy Load BOM lại.");
            if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            grid.EndEdit();
            var rows = grid.Rows.Cast<DataGridViewRow>()
                .Where(r => !r.IsNewRow && Convert.ToBoolean(r.Cells[0].Value ?? false)).ToList();
            if (rows.Count == 0) throw new InvalidOperationException("Tick ít nhất một dòng BOM cần xuất DXF.");
            var timer = Stopwatch.StartNew();
            var batch = new BomDxfBatchResult { BomPath = bomPath, SelectedRows = rows.Count, CheckWhileExporting = options.CheckWhileExporting,
                TargetFolder = Path.GetFullPath(options.TargetFolder) };
            Directory.CreateDirectory(batch.TargetFolder);
            string runId = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N").Substring(0, 6);
            string reportRoot = string.IsNullOrWhiteSpace(options.ReportFolder) ? null : Path.GetFullPath(options.ReportFolder);
            var work = new List<WorkItem>();
            var outputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var completed = new Dictionary<string, BomDxfFileResult>(StringComparer.OrdinalIgnoreCase);
            var checker = new BomDxfAutoCheck(batch.TargetFolder, runId, options.Layers, reportRoot);
            batch.ReportPath = Path.Combine(reportRoot == null ? batch.TargetFolder : checker.Batch.OutputFolder, "DXF_EXPORT_" + runId + ".csv");
            var aliases = new Dictionary<BomDxfFileResult, List<BomDxfFileResult>>();
            var checkQueue = new BomDxfCheckQueue(checker, options.Layers, cancel, item => {
                onItem?.Invoke(item, batch.ProcessedRows, rows.Count);
                if (aliases.TryGetValue(item, out List<BomDxfFileResult> linked))
                    foreach (var alias in linked) { BomDxfAutoCheck.Reuse(alias, item); onItem?.Invoke(alias, batch.ProcessedRows, rows.Count); }
            });
            batch.CheckBatch = checker.Batch; batch.CheckOutputFolder = checker.Batch.OutputFolder;
            begin?.Invoke(rows.Count);
            try {
                if (options.CheckWhileExporting) checkQueue.Start();
                using (DxfExportContext.Enter(app, options.Layers))
                using (DxfOutPut.BeginTrace(app, "drawing-bom", bomPath)) {
                    batch.TracePath = DxfTrace.CurrentPath;
                    DxfExportContext.CancellationRequested = cancel;
                    DxfExportContext.PhaseChanged = phase => {
                        checkQueue.Poll();
                        if (DxfExportContext.CurrentFile != null)
                            onItem?.Invoke(DxfExportContext.CurrentFile, batch.ProcessedRows, rows.Count);
                    };
                    foreach (var row in rows) {
                        var numbers = row.Cells[1].Tag as BomNumberValues;
                        var result = new BomDxfFileResult { BomRow = row.Index, ItemNumber = numbers?.ItemNumber ?? "",
                            PartNumber = Cell(row, 1), DrawingPath = "", ComponentPath = "", Configuration = "" };
                        batch.Items.Add(result);
                        if (cancel?.Invoke() == true) { batch.Canceled = true; result.Status = "CANCEL"; result.Message = "Đã hủy trước khi xuất."; onItem?.Invoke(result, batch.ProcessedRows, rows.Count); continue; }
                        try { work.Add(new WorkItem { Part = Capture(row, bom, result), Result = result }); result.Status = "READY"; }
                        catch (Exception ex) { result.Message = ex.Message; }
                        onItem?.Invoke(result, batch.ProcessedRows, rows.Count);
                    }
                    // Repeated BOM rows share a physical file; its PART_INFO gets
                    // the total selected quantity for that same Part/configuration.
                    var quantities = work.GroupBy(w => DxfSourceIdentity.Key(w.Part), StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => g.Sum(w => w.Part.PartQty), StringComparer.OrdinalIgnoreCase);
                    foreach (var result in batch.Items) {
                        checkQueue.Poll();
                        if (cancel?.Invoke() == true || batch.Canceled) { batch.Canceled = true; result.Status = "CANCEL"; result.Message = "Đã hủy trước khi xuất."; onItem?.Invoke(result, batch.ProcessedRows, rows.Count); continue; }
                        var item = work.FirstOrDefault(w => ReferenceEquals(w.Result, result));
                        if (item != null) {
                            string key = DxfSourceIdentity.Key(item.Part);
                            if (completed.TryGetValue(key, out BomDxfFileResult previous)) {
                                result.Status = "REUSED"; result.OutputPath = previous.OutputPath;
                                result.ViewName = previous.ViewName; result.SheetName = previous.SheetName;
                                result.Message = "Cùng Part/config với dòng đã xuất; dùng chung DXF.";
                                BomDxfAutoCheck.Reuse(result, previous);
                                if (!aliases.TryGetValue(previous, out List<BomDxfFileResult> linked)) aliases.Add(previous, linked = new List<BomDxfFileResult>());
                                linked.Add(result);
                            }
                            else {
                                DxfExportContext.CurrentFile = result;
                                result.Status = "RUNNING"; result.Phase = "Tìm view Flat Pattern đúng Part/config";
                                onItem?.Invoke(result, batch.ProcessedRows, rows.Count);
                                item.Part.PartQty = quantities[key];
                                var exportTimer = Stopwatch.StartNew();
                                bool ok = DxfOutPut.Export(item.Part, batch.TargetFolder,
                                    options.IncludeBendLines ? 1 : 0, options.IncludeLaserMarks ? 1 : 0, "dxf",
                                    options.RecognizeFeatureMarks, options.KeepDrawingMarkSketches, outputs);
                                result.ExportMilliseconds = exportTimer.Elapsed.TotalMilliseconds;
                                result.Status = ok ? result.WarningCount > 0 ? "WARN" : "OK" : "ERROR";
                                result.Message = ok ? string.Join(" | ", result.Notices) :
                                    result.Notices.LastOrDefault() ?? "Không xuất được DXF; xem nhật ký.";
                                if (ok) {
                                    completed[key] = result;
                                    try { checkQueue.Enqueue(result); }
                                    catch (Exception ex) { result.CheckStatus = "ERROR"; result.CheckMessage = ex.Message; }
                                }
                                if (!ok && cancel?.Invoke() == true) {
                                    batch.Canceled = true; result.Status = "CANCEL"; result.Message = "Đã dừng; DXF của dòng này chưa được phát hành.";
                                }
                                DxfExportContext.CurrentFile = null;
                            }
                        }
                        batch.ProcessedRows++;
                        onItem?.Invoke(result, batch.ProcessedRows, rows.Count);
                        progress?.Invoke(batch.ProcessedRows, rows.Count);
                        checkQueue.Poll();
                    }
                    checkQueue.Drain(); batch.Canceled |= checkQueue.Canceled;
                }
            }
            finally {
                string checkReportWarning = "";
                try { checkQueue.Drain(); batch.Canceled |= checkQueue.Canceled; }
                finally { checkQueue.Dispose(); }
                try { checker.Finish(batch.Canceled); batch.CheckReportPath = checker.Batch.ExcelPath; }
                catch (Exception ex) { checkReportWarning = " Chưa ghi được báo cáo Check DXF: " + ex.Message; }
                batch.Message = (batch.Canceled ? "Đã dừng. " : "") + "Xuất DXF: " + batch.Exported + " file, " +
                    batch.Reused + " dòng dùng chung, " + batch.Errors + " lỗi / " + batch.SelectedRows + " dòng chọn. " +
                    "Check DXF: " + batch.CheckPassed + " OK, " + batch.CheckNg + " NG, " + batch.CheckErrors + " lỗi." + checkReportWarning;
                batch.TotalMilliseconds = timer.Elapsed.TotalMilliseconds;
                try { WriteReport(batch); }
                finally { finish?.Invoke(); }
            }
            return batch;
        }

        private BomDxfPart Capture(DataGridViewRow row, ModelDoc2 bom, BomDxfFileResult result)
        {
            string modelPath;
            string drawingPath = resolver.ResolveBomDrawingPath(row, bom, out modelPath);
            string configuration = "";
            var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (row.Tag is object[] components) {
                foreach (object value in components) {
                    var component = value as Component2;
                    if (component == null) continue;
                    string path = component.GetPathName();
                    string config = component.ReferencedConfiguration ?? "";
                    if (string.IsNullOrWhiteSpace(path)) continue;
                    sources.Add(Path.GetFullPath(path) + "|" + config);
                    modelPath = path; configuration = config;
                }
                if (sources.Count > 1) throw new InvalidOperationException("Dòng BOM chứa nhiều Part/config khác nhau; hãy tách dòng trước khi xuất.");
            }
            result.ComponentPath = modelPath ?? ""; result.DrawingPath = drawingPath ?? ""; result.Configuration = configuration;
            if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath))
                throw new FileNotFoundException("Không tìm thấy Part của dòng BOM: " + Cell(row, 5));
            if (!string.Equals(Path.GetExtension(modelPath), ".SLDPRT", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Xuất khai triển DXF chỉ áp dụng cho Part (.SLDPRT).");
            if (string.IsNullOrWhiteSpace(drawingPath) || !File.Exists(drawingPath))
                throw new FileNotFoundException("Không tìm thấy Drawing (.SLDDRW) của Part: " + modelPath);
            int.TryParse(Cell(row, 4), NumberStyles.Integer, CultureInfo.InvariantCulture, out int quantity);
            double.TryParse(Cell(row, 3).Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double thickness);
            return new BomDxfPart { ComponentPath = modelPath, DrawingPath = drawingPath, ConfigurationName = configuration,
                PartNumber = result.PartNumber, PartName = Cell(row, 5), PMaterial = Cell(row, 2), Thickness = thickness,
                PartQty = Math.Max(1, quantity), UnitName = (row.Cells[1].Tag as BomNumberValues)?.Goban ?? "",
                ExportSplinesAsSplines = SplineChoice(row) };
        }

        internal const string SplineColumnName = "colDxfSpline";
        // Ticked: SPLINE. Unticked: POLYLINE. No such column (other callers): leave the machine setting alone.
        private static bool? SplineChoice(DataGridViewRow row)
        {
            DataGridView grid = row.DataGridView;
            if (grid == null || !grid.Columns.Contains(SplineColumnName)) return null;
            return Convert.ToBoolean(row.Cells[SplineColumnName].Value ?? false);
        }
        private static string Cell(DataGridViewRow row, int index)
        { return Convert.ToString(row.Cells[index].Value ?? "").Trim(); }
        private static string Csv(string value) { return "\"" + (value ?? "").Replace("\"", "\"\"") + "\""; }
        internal static void WriteReport(BomDxfBatchResult batch)
        {
            var text = new StringBuilder("INo BOM,部品番号,XUẤT DXF,PART,CONFIG,DRAWING,SHEET,FLAT VIEW,DXF,GHI CHÚ XUẤT,CHECK DXF,SỐ LỖI CHECK,DXF ĐÁNH DẤU,GHI CHÚ CHECK\r\n");
            foreach (var item in batch.Items)
                text.AppendLine(string.Join(",", new[] { item.ItemNumber, item.PartNumber, item.Status, item.ComponentPath,
                    item.Configuration, item.DrawingPath, item.SheetName, item.ViewName, item.OutputPath, item.Message,
                    item.CheckStatus, item.CheckIssueCount.ToString(CultureInfo.InvariantCulture), item.CheckOutputPath, item.CheckMessage }.Select(Csv)));
            Directory.CreateDirectory(Path.GetDirectoryName(batch.ReportPath));
            File.WriteAllText(batch.ReportPath, text.ToString(), new UTF8Encoding(true));
            File.WriteAllText(Path.ChangeExtension(batch.ReportPath, ".json"),
                new JavaScriptSerializer { MaxJsonLength = 50000000 }.Serialize(batch), new UTF8Encoding(true));
        }
    }
}
