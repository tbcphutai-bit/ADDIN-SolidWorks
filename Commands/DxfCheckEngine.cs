using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace ADDIN.Commands.DxfChecking
{
    internal static partial class DxfCheckEngine
    {
        public const string OutputDirectoryName = "CHECK_DXF_OUTPUT";
        public static DxfBatchResult Run(string folder, bool recursive, string outputOverride, IProgress<DxfProgress> progress, CancellationToken token)
        { return Run(folder, recursive, outputOverride, progress, token, DxfCheckOptions.Default); }

        public static DxfBatchResult Run(string folder, bool recursive, string outputOverride, IProgress<DxfProgress> progress, CancellationToken token, DxfCheckOptions options)
        {
            DxfBatchResult batch = new DxfBatchResult { SourceFolder = folder, Options = options ?? DxfCheckOptions.Default };
            if (!batch.Options.HasAny) { batch.InputMessage = "Hãy tick ít nhất một điều kiện để kiểm tra DXF."; return batch; }
            if (string.IsNullOrWhiteSpace(folder)) { batch.InputMessage = "Chọn thư mục chứa file DXF để bắt đầu kiểm tra."; return batch; }
            folder = Path.GetFullPath(folder);
            batch.SourceFolder = folder;
            if (!Directory.Exists(folder)) { batch.InputMessage = "Không tìm thấy thư mục DXF. Hãy chọn lại thư mục."; return batch; }
            List<string> files = Enumerate(folder, recursive).OrderBy(p => p, NaturalPathComparer).ThenBy(p => p, StringComparer.OrdinalIgnoreCase).ThenBy(p => p, StringComparer.Ordinal).ToList();
            Trace.WriteLine("[CHECK DXF] Folder=\"" + folder + "\"; Recursive=" + recursive + "; Files=" + files.Count);
            if (files.Count == 0) {
                batch.InputMessage = "Không có DXF trong thư mục này. Chọn thư mục chứa file, hoặc bật Quét cả thư mục con rồi chọn lại.";
                return batch;
            }
            int relativeStart = folder.TrimEnd(Path.DirectorySeparatorChar).Length + 1;
            foreach (IGrouping<string, string> group in files.GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)) {
                if (!batch.Options.Has(DxfCheckCondition.DuplicateNames)) break;
                if (group.Select(Path.GetDirectoryName).Distinct(StringComparer.OrdinalIgnoreCase).Count() < 2) continue;
                batch.DuplicateNames.Add(new DxfDuplicateName {
                    FileName = group.Key,
                    RelativeNames = group.Select(p => p.Substring(relativeStart)).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ThenBy(p => p, StringComparer.Ordinal).ToList()
                });
            }
            Dictionary<string, int> duplicateCounts = batch.DuplicateNames.ToDictionary(d => d.FileName, d => d.RelativeNames.Count, StringComparer.OrdinalIgnoreCase);
            if (batch.DuplicateNames.Count > 0) {
                Trace.WriteLine("[CHECK DXF] DuplicateNames=" + batch.DuplicateNames.Count + "; DuplicateFiles=" + batch.DuplicateFileCount);
                foreach (DxfDuplicateName duplicate in batch.DuplicateNames) Trace.WriteLine("[CHECK DXF] Duplicate=\"" + duplicate.FileName + "\"; Paths=" + string.Join(" | ", duplicate.RelativeNames));
                if (progress != null) progress.Report(new DxfProgress { Total = files.Count, DuplicateNames = batch.DuplicateNames });
            }
            string root = outputOverride ?? Path.Combine(folder, OutputDirectoryName);
            batch.OutputFolder = Path.Combine(root, DateTime.Now.ToString("yyyyMMdd_HHmmss_fff"));
            if (Directory.Exists(batch.OutputFolder)) batch.OutputFolder += "_" + Guid.NewGuid().ToString("N").Substring(0, 6);
            Directory.CreateDirectory(batch.OutputFolder);
            Stopwatch timer = Stopwatch.StartNew();
            foreach (string path in files)
            {
                if (token.IsCancellationRequested) { batch.Cancelled = true; break; }
                string relative = path.Substring(relativeStart);
                int duplicateCount;
                duplicateCounts.TryGetValue(Path.GetFileName(path), out duplicateCount);
                DxfFileResult result = new DxfFileResult { SourcePath = path, RelativeName = relative, DuplicateNameCount = duplicateCount, Options = batch.Options };
                Stopwatch fileTimer = Stopwatch.StartNew();
                try
                {
                    DxfRawDocument document = DxfRawDocument.Read(path);
                    document.Extract(result);
                    Analyze(result, token);
                    token.ThrowIfCancellationRequested();
                    if (result.Status == "NG") {
                        result.OutputPath = Path.Combine(batch.OutputFolder, relative);
                        DxfCheckExport.WriteAnnotated(document, result);
                    }
                }
                catch (OperationCanceledException) { batch.Cancelled = true; break; }
                catch (Exception ex) {
                    result.Error = ex.Message; result.OutputPath = null;
                    Trace.WriteLine("[CHECK DXF] File failed; File=\"" + path + "\"; Error=" + ex);
                }
                result.Milliseconds = fileTimer.Elapsed.TotalMilliseconds;
                batch.Files.Add(result);
                if (progress != null) progress.Report(new DxfProgress { Done = batch.Files.Count, Total = files.Count, File = result });
            }
            batch.Seconds = timer.Elapsed.TotalSeconds;
            batch.ExcelPath = Path.Combine(batch.OutputFolder, "CHECK_DXF.xlsx");
            DxfCheckExport.WriteExcel(batch);
            Trace.WriteLine("[CHECK DXF] Completed; Files=" + batch.Files.Count + "; NG=" + batch.NgCount + "; ReadErrors=" + batch.ErrorCount + "; DuplicateNames=" + batch.DuplicateNames.Count + "; Output=\"" + batch.OutputFolder + "\"");
            return batch;
        }

        public static DxfRecheckResult Recheck(DxfBatchResult batch, DxfFileResult previous, CancellationToken token)
        { return RecheckSaved(batch, previous, token, false); }

        public static DxfRecheckResult WriteBack(DxfBatchResult batch, DxfFileResult previous, CancellationToken token)
        {
            if (batch == null || previous == null || !batch.Files.Contains(previous)) throw new ArgumentException("Không tìm thấy file trong bộ DXF đang kiểm tra.");
            if (previous.Status != "OK" || string.IsNullOrEmpty(previous.CleanPath) || string.IsNullOrEmpty(previous.OutputPath)
                || !string.Equals(Path.GetFullPath(previous.CleanPath), Path.GetFullPath(previous.OutputPath), StringComparison.OrdinalIgnoreCase))
                return new DxfRecheckResult { File = previous, Batch = batch, SourceWriteError = "Hãy lưu DXF đã sửa và bấm Kiểm tra lại DXF đã sửa để đạt OK trước khi ghi đè file gốc." };
            return RecheckSaved(batch, previous, token, true);
        }

        private static DxfRecheckResult RecheckSaved(DxfBatchResult batch, DxfFileResult previous, CancellationToken token, bool writeToSource)
        {
            if (batch == null || previous == null || !batch.Files.Contains(previous)) throw new ArgumentException("Không tìm thấy file trong bộ DXF đang kiểm tra.");
            DxfRecheckResult outcome = new DxfRecheckResult();
            if (token.IsCancellationRequested) { outcome.Cancelled = true; return outcome; }
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" + Guid.NewGuid().ToString("N").Substring(0, 6);
            DxfFileResult updated = new DxfFileResult {
                Options = batch.Options,
                CutLayer = previous.CutLayer,
                SourcePath = previous.SourcePath, RelativeName = previous.RelativeName, DuplicateNameCount = previous.DuplicateNameCount,
                CheckedPath = previous.OutputPath, OutputPath = previous.OutputPath, RecheckedAt = DateTime.Now,
                SourceUpdatedAt = previous.SourceUpdatedAt, SourceBackupPath = previous.SourceBackupPath
            };
            Stopwatch timer = Stopwatch.StartNew();
            try {
                Trace.WriteLine("[CHECK DXF] Recheck saved output=\"" + previous.OutputPath + "\"");
                DxfRawDocument doc = DxfRawDocument.ReadSavedOutput(previous.OutputPath);
                doc.Extract(updated); AnalyzeFile(updated, token); token.ThrowIfCancellationRequested();
                if (updated.Status == "NG") {
                    updated.OutputPath = Path.Combine(batch.OutputFolder, "KIEM_TRA_LAI", stamp, previous.RelativeName);
                    DxfCheckExport.WriteAnnotated(doc, updated);
                }
                else {
                    string cleanPath = CleanOutputPath(batch, previous);
                    string cleanBackup = Path.Combine(batch.OutputFolder, "LICH_SU_DXF_SACH", stamp, Path.GetFileName(cleanPath));
                    // Export the exact saved snapshot that passed verification, not a
                    // second read that might contain a later CAD save.
                    DxfCheckExport.WriteCleanDocument(doc, cleanPath, cleanBackup);
                    updated.OutputPath = updated.CleanPath = cleanPath;
                }
                token.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) { outcome.Cancelled = true; return outcome; }
            catch (Exception ex) {
                updated.Error = ex.Message; updated.OutputPath = File.Exists(previous.OutputPath) ? previous.OutputPath : null; updated.CleanPath = null;
                Trace.WriteLine("[CHECK DXF] Recheck file failed; File=\"" + previous.OutputPath + "\"; Error=" + ex);
            }
            updated.Milliseconds = timer.Elapsed.TotalMilliseconds;
            if (writeToSource && updated.Status == "OK") {
                try {
                    string backupPath = Path.Combine(batch.OutputFolder, "SAO_LUU_GOC", stamp, previous.RelativeName);
                    DxfCheckExport.WriteBack(updated.CleanPath, previous.SourcePath, backupPath, token);
                    outcome.SourceWritten = true;
                    updated.SourceUpdatedAt = DateTime.Now; updated.SourceBackupPath = backupPath;
                    Trace.WriteLine("[CHECK DXF] Original updated=\"" + previous.SourcePath + "\"; Backup=\"" + backupPath + "\"");
                }
                catch (OperationCanceledException) { outcome.Cancelled = true; return outcome; }
                catch (Exception ex) { outcome.SourceWriteError = "Chưa ghi đè được DXF gốc: " + ex.Message; }
            }
            else if (writeToSource) outcome.SourceWriteError = updated.Status == "NG" ? "Bản sạch đã thay đổi và còn NG. File gốc chưa được ghi đè; sửa tiếp trên bản đánh dấu mới." : "Không đọc được bản sạch. File gốc chưa được ghi đè.";
            DxfBatchResult next = new DxfBatchResult {
                Options = batch.Options,
                SourceFolder = batch.SourceFolder, OutputFolder = batch.OutputFolder, Cancelled = batch.Cancelled,
                Seconds = batch.Seconds + timer.Elapsed.TotalSeconds, DuplicateNames = new List<DxfDuplicateName>(batch.DuplicateNames),
                Files = batch.Files.Select(f => f == previous ? updated : f.WithoutComparisons()).ToList(),
                ExcelPath = Path.Combine(batch.OutputFolder, "CHECK_DXF_RECHECK_" + stamp + ".xlsx")
            };
            if (token.IsCancellationRequested && !outcome.SourceWritten) { outcome.Cancelled = true; return outcome; }
            try { DxfCheckExport.WriteExcel(next); }
            catch (Exception ex) { next.ExcelPath = null; outcome.ReportWarning = "Chưa xuất được Excel mới: " + ex.Message; }
            if (token.IsCancellationRequested && !outcome.SourceWritten) { outcome.Cancelled = true; return outcome; }
            outcome.File = updated; outcome.Batch = next;
            Trace.WriteLine("[CHECK DXF] Rechecked; File=\"" + updated.RelativeName + "\"; Status=" + updated.Status + "; NG=" + updated.Issues.Count + "; Clean=\"" + updated.CleanPath + "\"");
            return outcome;
        }

        private static string CleanOutputPath(DxfBatchResult batch, DxfFileResult selected)
        {
            var groups = batch.Files.GroupBy(f => Path.GetFileName(f.RelativeName), StringComparer.OrdinalIgnoreCase).ToList();
            // Keep a recognizable original stem and an explicit clean suffix.
            // Duplicate stems include the source folders rather than click-order numbers.
            HashSet<string> used = new HashSet<string>(groups.Where(g => g.Count() == 1)
                .Select(g => Path.GetFileNameWithoutExtension(g.Key) + "__SACH" + Path.GetExtension(g.Key)), StringComparer.OrdinalIgnoreCase);
            foreach (var group in groups.OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)) {
                foreach (DxfFileResult file in group.OrderBy(f => f.RelativeName, StringComparer.OrdinalIgnoreCase).ThenBy(f => f.RelativeName, StringComparer.Ordinal)) {
                    string stem = Path.GetFileNameWithoutExtension(file.RelativeName), extension = Path.GetExtension(file.RelativeName);
                    string name = stem + "__SACH" + extension;
                    if (group.Count() > 1) {
                        string folder = Path.GetDirectoryName(file.RelativeName);
                        if (string.IsNullOrEmpty(folder)) folder = "GOC";
                        string readable = string.Join("__", folder.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(p => string.Concat(p.Select(c => char.IsWhiteSpace(c) || Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim('.', '_')));
                        string qualified = stem + "__" + readable;
                        name = qualified + "__SACH" + extension;
                        if (!used.Add(name)) {
                            string id;
                            using (SHA256 hash = SHA256.Create()) id = BitConverter.ToString(hash.ComputeHash(
                                Encoding.UTF8.GetBytes(file.RelativeName.Replace('/', '\\').ToUpperInvariant()))).Replace("-", "").Substring(0, 10);
                            string candidate = qualified + "__" + id;
                            name = candidate + "__SACH" + extension;
                            int number = 0;
                            while (!used.Add(name)) name = candidate + "_" + (++number).ToString(CultureInfo.InvariantCulture) + "__SACH" + extension;
                        }
                    }
                    if (file == selected) return Path.Combine(batch.OutputFolder, "DXF_SACH", name);
                }
            }
            throw new ArgumentException("Không tìm thấy file để xuất DXF sạch.");
        }

        private static IEnumerable<string> Enumerate(string folder, bool recursive)
        {
            foreach (string path in Directory.EnumerateFiles(folder)) if (string.Equals(Path.GetExtension(path), ".dxf", StringComparison.OrdinalIgnoreCase)) yield return path;
            if (!recursive) yield break;
            foreach (string child in Directory.EnumerateDirectories(folder))
            {
                if (string.Equals(Path.GetFileName(child), OutputDirectoryName, StringComparison.OrdinalIgnoreCase) || (File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) continue;
                foreach (string path in Enumerate(child, true)) yield return path;
            }
        }

        internal static void AnalyzeFile(DxfFileResult result, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(result.CutLayer)) { Analyze(result, token); return; }
            var allEdges = result.Edges;
            result.Edges = allEdges.Where(e => string.Equals(e.Layer, result.CutLayer, StringComparison.OrdinalIgnoreCase)).ToList();
            result.GeometryCount = result.Edges.Select(e => e.Handle).Distinct().Count();
            try {
                if (result.Edges.Count == 0) throw new InvalidDataException("Không có biên dạng trên layer " + result.CutLayer + " để Check DXF.");
                Analyze(result, token);
            }
            finally { result.Edges = allEdges; }
            if (result.Options.Has(DxfCheckCondition.YellowLayers)) DxfLayerStatistics.Analyze(result, token);
        }

        public static void Analyze(DxfFileResult result, CancellationToken token)
        {
            DxfCheckOptions options = result.Options ?? DxfCheckOptions.Default;
            result.Options = options;
            if (options.Has(DxfCheckCondition.YellowLayers)) DxfLayerStatistics.Analyze(result, token);
            else { result.YellowLayers.Clear(); result.YellowShapes.Clear(); result.YellowItems.Clear(); }
            if (result.Edges.Count == 0) throw new InvalidDataException("Không tìm thấy biên dạng 2D để kiểm tra.");
            foreach (var group in result.Edges.Where(edge =>
                (options.Has(DxfCheckCondition.SplineEntity) && edge.OriginalType == "SPLINE") ||
                (options.Has(DxfCheckCondition.PolylineEntity) && (edge.OriginalType == "POLYLINE" || edge.OriginalType == "LWPOLYLINE")))
                .GroupBy(edge => new { edge.Handle, edge.Space, edge.OriginalType })) {
                token.ThrowIfCancellationRequested();
                string code = group.Key.OriginalType == "SPLINE" ? "SPLINE" : "POLYLINE";
                Add(result, code, "Đối tượng " + group.Key.OriginalType + ": NG theo điều kiện đã tick", group.ToList(), null);
            }
            if (options.Has(DxfCheckCondition.Ellipse)) foreach (IGrouping<string, DxfEdge> group in result.Edges.GroupBy(e => e.Handle))
            {
                if (group.Any(e => e.Type == "ELLIPSE")) Add(result, "ELLIPSE", "Đối tượng ELLIPSE: NG", group.ToList(), null);
            }
            if (options.Has(DxfCheckCondition.ShortEdge)) foreach (DxfEdge edge in result.Edges)
            {
                token.ThrowIfCancellationRequested();
                if (edge.Length < 0.5) Add(result, "SHORT_EDGE", "Đoạn biên dạng ngắn hơn 0,5 mm" + (edge.Segment > 0 ? " (đoạn " + edge.Segment + ")" : ""), new List<DxfEdge> { edge }, edge.Length);
            }
            HashSet<DxfEdge> fullDuplicates = CheckOverlaps(result, token);
            DxfContourCheck.Analyze(result, fullDuplicates, token);
            if (options.Has(DxfCheckCondition.DeformedHole) || options.Has(DxfCheckCondition.IntersectingHoles)) CheckHoles(result, fullDuplicates, token);
            if (options.Has(DxfCheckCondition.RoundHoleToEdge)) CheckRoundHolesAtEdge(result, token);
            if (options.Has(DxfCheckCondition.SplineRoundHole)) CheckSplineRoundHoles(result, token);
            // Contour assembly and duplicate suppression support several checks.
            // Keep those geometric dependencies, then report only selected rules.
            result.Issues.RemoveAll(issue => !options.IssueEnabled(issue.Code));
            for (int i = 0; i < result.Issues.Count; i++) result.Issues[i].Id = i + 1;
        }

        internal static void Add(DxfFileResult result, string code, string text, List<DxfEdge> edges, double? measurement, DxfBox? mark = null)
        {
            DxfBox box = mark ?? DxfBox.From(edges.SelectMany(e => e.Preview));
            result.Issues.Add(new DxfIssue { Code = code, Description = text, Handle = string.Join("; ", edges.Select(e => e.Handle + (e.Segment > 0 ? ":" + e.Segment : "")).Distinct()), Layer = string.Join("; ", edges.Select(e => e.Layer).Distinct()), EntityType = string.Join("; ", edges.Select(e => e.OriginalType).Distinct()), Space = edges[0].Space, Position = box.Center, Bounds = box, Measurement = measurement });
        }

        private static HashSet<DxfEdge> CheckOverlaps(DxfFileResult result, CancellationToken token)
        {
            HashSet<DxfEdge> full = new HashSet<DxfEdge>();
            List<DxfEdge> active = new List<DxfEdge>();
            foreach (DxfEdge e in result.Edges.OrderBy(e => e.Bounds.MinX))
            {
                token.ThrowIfCancellationRequested();
                active.RemoveAll(a => a.Bounds.MaxX < e.Bounds.MinX - DxfGeometry.Epsilon);
                foreach (DxfEdge a in active)
                {
                    if (a.Space != e.Space || !a.Bounds.Intersects(e.Bounds, DxfGeometry.Epsilon)) continue;
                    DxfBox mark; double overlap = Overlap(a, e, out mark);
                    if (overlap <= 1e-10) continue; // A single crossing point has no overlap length.
                    Add(result, "OVERLAP", "Hai đường có đoạn chồng lên nhau", new List<DxfEdge> { a, e }, overlap, mark);
                    if (Math.Abs(overlap - e.Length) <= DxfGeometry.Epsilon && Math.Abs(a.Length - e.Length) <= DxfGeometry.Epsilon) full.Add(e);
                }
                active.Add(e);
            }
            return full;
        }

        private static double Overlap(DxfEdge a, DxfEdge b, out DxfBox mark)
        {
            mark = DxfBox.From(new[] { a.At(.5), b.At(.5) });
            if (a.Type == "LINE" && b.Type == "LINE")
            {
                DxfPoint u = a.End - a.Start;
                if (u.Length <= 1e-15 || (b.End - b.Start).Length <= 1e-15) return 0;
                u = u * (1 / u.Length);
                if (Math.Abs(u.Cross(b.Start - a.Start)) > DxfGeometry.Epsilon || Math.Abs(u.Cross(b.End - a.Start)) > DxfGeometry.Epsilon) return 0;
                double p = u.Dot(b.Start - a.Start), q = u.Dot(b.End - a.Start);
                double lo = Math.Max(0, Math.Min(p, q)), hi = Math.Min(a.Length, Math.Max(p, q));
                if (hi <= lo) return 0;
                mark = DxfBox.From(new[] { a.Start + u * lo, a.Start + u * hi }); return hi - lo;
            }
            bool ac = a.Type == "ARC" || a.Type == "CIRCLE", bc = b.Type == "ARC" || b.Type == "CIRCLE";
            if (ac && bc)
            {
                if (a.Center.Distance(b.Center) > DxfGeometry.Epsilon || Math.Abs(a.Radius - b.Radius) > DxfGeometry.Epsilon) return 0;
                double sum = 0; List<DxfPoint> points = new List<DxfPoint>();
                foreach (Tuple<double, double> x in Intervals(a)) foreach (Tuple<double, double> y in Intervals(b))
                {
                    double lo = Math.Max(x.Item1, y.Item1), hi = Math.Min(x.Item2, y.Item2);
                    if (hi <= lo) continue;
                    sum += (hi - lo) * a.Radius;
                    for (int i = 0; i <= 16; i++) { double angle = lo + (hi - lo) * i / 16; points.Add(a.Center + new DxfPoint(Math.Cos(angle), Math.Sin(angle)) * a.Radius); }
                }
                if (points.Count > 0) mark = DxfBox.From(points);
                return sum;
            }
            // Recognize straight spline spans analytically before checking arbitrary curved spans.
            DxfEdge al = Straight(a), bl = Straight(b);
            if (al != null && bl != null) return Overlap(al, bl, out mark);
            if (ac && b.Type == "LINE" || bc && a.Type == "LINE") return 0;
            double found = PartialCurveOverlap(a, b, out mark);
            if (found > 0) return found;
            return PartialCurveOverlap(b, a, out mark);
        }

        private static DxfEdge Straight(DxfEdge e)
        {
            if (e.Type == "LINE") return e;
            if (e.Start.Distance(e.End) <= DxfGeometry.Epsilon) return null;
            foreach (double t in ProbeParameters(e)) if (DxfGeometry.DistanceSegment(e.At(t), e.Start, e.End) > DxfGeometry.Epsilon) return null;
            return DxfGeometry.Line(e.Start, e.End);
        }
        internal static IEnumerable<double> ProbeParameters(DxfEdge e)
        {
            for (int i = 1; i < e.Breaks.Count; i++) for (int j = 0; j < 8; j++) yield return e.Breaks[i - 1] + (e.Breaks[i] - e.Breaks[i - 1]) * j / 8;
            yield return 1;
        }
        private static double PartialCurveOverlap(DxfEdge a, DxfEdge b, out DxfBox mark)
        {
            mark = DxfBox.From(new[] { a.At(.5) });
            List<double> ts = ProbeParameters(a).Distinct().OrderBy(t => t).ToList();
            double sum = 0; List<DxfPoint> overlapPoints = new List<DxfPoint>();
            for (int i = 1; i < ts.Count; i++)
            {
                double lo = ts[i - 1], hi = ts[i];
                DxfPoint p = a.At(lo), q = a.At(hi);
                if (DxfGeometry.DistanceCurve(p, b) > DxfGeometry.Epsilon || DxfGeometry.DistanceCurve(q, b) > DxfGeometry.Epsilon) continue;
                bool coincident = true;
                for (int j = 1; j < 8; j++) if (DxfGeometry.DistanceCurve(a.At(lo + (hi - lo) * j / 8), b) > DxfGeometry.Epsilon) { coincident = false; break; }
                if (!coincident) continue;
                DxfEdge span = Subcurve(a, lo, hi); sum += DxfGeometry.Length(span);
                overlapPoints.AddRange(span.Preview);
            }
            if (overlapPoints.Count > 0) mark = DxfBox.From(overlapPoints);
            return sum;
        }
        private static IEnumerable<Tuple<double, double>> Intervals(DxfEdge e)
        {
            if (e.Closed) { yield return Tuple.Create(0.0, DxfGeometry.Tau); yield break; }
            double lo = DxfGeometry.Mod(e.Sweep < 0 ? e.StartAngle + e.Sweep : e.StartAngle), hi = lo + Math.Abs(e.Sweep);
            yield return Tuple.Create(lo, Math.Min(hi, DxfGeometry.Tau));
            if (hi > DxfGeometry.Tau) yield return Tuple.Create(0.0, hi - DxfGeometry.Tau);
        }
        private static DxfEdge Subcurve(DxfEdge e, double a, double b)
        {
            return new DxfEdge { Type = "SPLINE", Evaluate = t => e.At(a + (b - a) * t), Derivative = t => e.Derivative(a + (b - a) * t) * (b - a), Start = e.At(a), End = e.At(b), Bounds = e.Bounds, Handle = e.Handle, Layer = e.Layer, Space = e.Space, OriginalType = e.OriginalType };
        }

        private sealed class Loop
        {
            public List<DxfEdge> Edges = new List<DxfEdge>();
            public List<DxfPoint> Points = new List<DxfPoint>();
            public string Space;
            public bool Closed;
            public DxfBox Bounds;
        }
        private static void CheckHoles(DxfFileResult result, HashSet<DxfEdge> duplicates, CancellationToken token)
        {
            List<Loop> loops = BuildLoops(result.Edges.Where(e => !duplicates.Contains(e)).ToList());
            List<Loop> inner = loops.Where(loop => loop.Points.Count >= 3 && loops.Any(other => other != loop && other.Closed && other.Space == loop.Space && other.Bounds.Size > loop.Bounds.Size + DxfGeometry.Epsilon && Contains(other.Points, loop.Points[0]) && loop.Points.Where((p, i) => i % Math.Max(1, loop.Points.Count / 16) == 0).All(p => Contains(other.Points, p)))).ToList();
            if (result.Options.Has(DxfCheckCondition.IntersectingHoles)) for (int i = 0; i < inner.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                for (int j = i + 1; j < inner.Count; j++)
                {
                    if (inner[i].Space != inner[j].Space || !inner[i].Bounds.Intersects(inner[j].Bounds, DxfGeometry.Epsilon)) continue;
                    List<DxfPoint> hits = new List<DxfPoint>();
                    foreach (DxfEdge a in inner[i].Edges) foreach (DxfEdge b in inner[j].Edges) hits.AddRange(Intersections(a, b));
                    if (hits.Count > 0) Add(result, "HOLE_INTERSECTION", "Các lỗ/vùng cắt khuyết bên trong chạm hoặc cắt qua nhau", inner[i].Edges.Concat(inner[j].Edges).ToList(), null, DxfBox.From(hits));
                }
                if (inner[i].Edges.Count > 3)
                {
                    List<DxfPoint> self = new List<DxfPoint>();
                    for (int a = 0; a < inner[i].Edges.Count; a++) for (int b = a + 2; b < inner[i].Edges.Count; b++)
                    {
                        if (a == 0 && b == inner[i].Edges.Count - 1) continue;
                        self.AddRange(Intersections(inner[i].Edges[a], inner[i].Edges[b]));
                    }
                    if (self.Count > 0) Add(result, "HOLE_INTERSECTION", "Biên vùng cắt khuyết bên trong tự cắt/đan vào nhau", inner[i].Edges, null, DxfBox.From(self));
                }
            }
            if (result.Options.Has(DxfCheckCondition.DeformedHole)) foreach (Loop loop in loops)
            {
                token.ThrowIfCancellationRequested();
                if (loop.Points.Count < 3) continue;
                // Containment is computed against actual outer contours, in the same drawing space.
                int containing = loops.Count(other => other != loop && other.Closed && other.Space == loop.Space && other.Bounds.Size > loop.Bounds.Size + DxfGeometry.Epsilon && Contains(other.Points, loop.Points[0]) && loop.Points.Where((p, i) => i % Math.Max(1, loop.Points.Count / 16) == 0).All(p => Contains(other.Points, p)));
                if (containing % 2 != 1) continue;
                if (loop.Edges.All(e => e.Type == "LINE"))
                {
                    // Rectangular and intentionally angular cutouts are valid. Faceted round holes are not.
                    if (loop.Edges.Count < 8 || Math.Max(loop.Bounds.Width, loop.Bounds.Height) > 1.3 * Math.Min(loop.Bounds.Width, loop.Bounds.Height)) continue;
                }
                DxfPoint center; double radius, deviation;
                List<DxfPoint> probes = loop.Edges.SelectMany(e => ProbeParameters(e).Select(e.At)).ToList();
                bool fit = DxfGeometry.FitCircle(probes, out center, out radius, out deviation);
                if (fit && deviation <= DxfGeometry.Epsilon && loop.Closed) continue;
                if (loop.Closed && IsSlot(loop.Edges)) continue;
                Add(result, "ROUND_DEFORMED", loop.Closed ? "Lỗ bên trong có biên cong nhưng không tròn và không phải slot hợp lệ" : "Biên lỗ tròn bên trong bị méo hoặc không khép kín", loop.Edges, fit ? (double?)deviation : null);
            }
        }

        private static void CheckSplineRoundHoles(DxfFileResult file, CancellationToken token)
        {
            foreach (DxfContourResult contour in file.Contours.Where(c => c.Closed && c.Role == "Inner"
                && c.Edges.Any(e => e.OriginalType == "SPLINE")))
            {
                token.ThrowIfCancellationRequested();
                if (IsSlot(contour.Edges)) continue;
                DxfPoint center; double radius, deviation;
                List<DxfPoint> points = contour.Edges.SelectMany(e => ProbeParameters(e).Select(e.At)).ToList();
                if (!DxfGeometry.FitCircle(points, out center, out radius, out deviation) || deviation > DxfGeometry.Epsilon) continue;
                Add(file, "SPLINE_ROUND_HOLE", "C" + contour.Id + " — Lỗ bên trong dạng tròn có đối tượng SPLINE: NG; R"
                    + radius.ToString("0.#########", CultureInfo.InvariantCulture) + " mm", contour.Edges, radius);
                file.Issues[file.Issues.Count - 1].ContourId = contour.Id;
            }
        }

        private static List<Loop> BuildLoops(List<DxfEdge> edges)
        {
            List<Loop> loops = new List<Loop>();
            HashSet<DxfEdge> consumed = new HashSet<DxfEdge>();
            foreach (DxfEdge e in edges.Where(e => e.Closed))
            { loops.Add(new Loop { Edges = new List<DxfEdge> { e }, Points = e.Preview, Space = e.Space, Bounds = DxfBox.From(e.Preview), Closed = true }); consumed.Add(e); }
            foreach (DxfEdge seed in edges)
            {
                if (consumed.Contains(seed)) continue;
                Loop loop = new Loop { Space = seed.Space };
                bool joinedWithinTolerance = true;
                DxfPoint first = seed.Start, end = seed.End;
                loop.Edges.Add(seed); loop.Points.AddRange(seed.Preview); consumed.Add(seed);
                while (!DxfGeometry.Connected(end, first))
                {
                    DxfEdge next = null; bool reverse = false; double nearest = DxfGeometry.ClosureTolerance + 1e-10;
                    foreach (DxfEdge candidate in edges)
                    {
                        if (consumed.Contains(candidate) || candidate.Space != seed.Space) continue;
                        double a = candidate.Start.Distance(end), b = candidate.End.Distance(end);
                        if (a < nearest) { nearest = a; next = candidate; reverse = false; }
                        if (b < nearest) { nearest = b; next = candidate; reverse = true; }
                    }
                    if (next == null) break;
                    if (nearest > DxfGeometry.ClosureTolerance + 1e-10) joinedWithinTolerance = false;
                    consumed.Add(next); loop.Edges.Add(next);
                    List<DxfPoint> points = new List<DxfPoint>(next.Preview);
                    if (reverse) points.Reverse();
                    loop.Points.AddRange(points); end = reverse ? next.Start : next.End;
                }
                loop.Closed = joinedWithinTolerance && DxfGeometry.Connected(end, first);
                // Tiny endpoint gaps are candidates for a damaged round hole, never accepted as a circle.
                if (loop.Closed || (end.Distance(first) < .001 && loop.Edges.Count > 1))
                { loop.Bounds = DxfBox.From(loop.Points); loops.Add(loop); }
            }
            return loops;
        }

        private static bool Contains(List<DxfPoint> polygon, DxfPoint p)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                DxfPoint a = polygon[i], b = polygon[j];
                if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
            }
            return inside;
        }

        internal static bool IsSlot(List<DxfEdge> source)
        {
            return IsExactSlot(source) || IsRailSlot(source) || IsCurvedRailCutout(source);
        }

        private static bool IsCurvedRailCutout(List<DxfEdge> source)
        {
            // Annular-strip cutouts have two concentric curved rails and straight end caps.
            // This is a cutout-shape test; it does not relax round-hole geometry.
            List<DxfEdge> caps = source.Where(e => e.Type == "LINE").ToList();
            List<DxfEdge> arcs = source.Where(e => e.Type == "ARC").ToList();
            if (caps.Count != 2 || arcs.Count < 2 || caps.Count + arcs.Count != source.Count) return false;
            if (arcs.Any(e => e.Center.Distance(arcs[0].Center) > DxfGeometry.Epsilon)) return false;
            List<List<DxfEdge>> rails = new List<List<DxfEdge>>();
            foreach (DxfEdge arc in arcs)
            {
                List<DxfEdge> rail = rails.FirstOrDefault(r => Math.Abs(r[0].Radius - arc.Radius) <= DxfGeometry.Epsilon);
                if (rail == null) { rail = new List<DxfEdge>(); rails.Add(rail); }
                rail.Add(arc);
            }
            if (rails.Count != 2) return false;
            double width = Math.Abs(rails[0][0].Radius - rails[1][0].Radius);
            if (width <= DxfGeometry.ClosureTolerance || caps.Any(e => e.Length > width * 1.5)
                || rails.Any(r => r.Sum(e => e.Length) < width * 2 || r.Sum(e => Math.Abs(e.Sweep)) >= DxfGeometry.Tau)) return false;
            List<List<DxfPoint>> ends = new List<List<DxfPoint>>();
            foreach (List<DxfEdge> rail in rails)
            {
                List<DxfPoint> points = rail.SelectMany(e => new[] { e.Start, e.End }).ToList();
                List<DxfPoint> loose = new List<DxfPoint>();
                for (int i = 0; i < points.Count; i++)
                {
                    int degree = points.Where((p, j) => j != i && DxfGeometry.Connected(points[i], p)).Count();
                    if (degree > 1) return false;
                    if (degree == 0) loose.Add(points[i]);
                }
                if (loose.Count != 2) return false;
                ends.Add(loose);
            }
            HashSet<int> usedInner = new HashSet<int>(), usedOuter = new HashSet<int>();
            foreach (DxfEdge cap in caps)
            {
                int inner = ends[0].FindIndex(p => DxfGeometry.Connected(p, cap.Start));
                int outer = ends[1].FindIndex(p => DxfGeometry.Connected(p, cap.End));
                if (inner < 0 || outer < 0)
                {
                    inner = ends[0].FindIndex(p => DxfGeometry.Connected(p, cap.End));
                    outer = ends[1].FindIndex(p => DxfGeometry.Connected(p, cap.Start));
                }
                if (inner < 0 || outer < 0 || !usedInner.Add(inner) || !usedOuter.Add(outer)) return false;
            }
            return true;
        }

        private static bool IsRailSlot(List<DxfEdge> source)
        {
            // Exported slots can have spline caps and slightly tapered rails.
            // Recognize the long cutout by its two sides and two end caps;
            // this does not relax the circle test for round holes.
            List<DxfEdge> rails = source.Where(e => e.Type == "LINE").ToList();
            List<DxfEdge> caps = source.Where(e => e.Type != "LINE").ToList();
            if (rails.Count != 2 || caps.Count < 2) return false;
            DxfPoint axis = rails[0].End - rails[0].Start;
            DxfPoint second = rails[1].End - rails[1].Start;
            if (axis.Length <= DxfGeometry.Epsilon || second.Length <= DxfGeometry.Epsilon) return false;
            axis = axis * (1 / axis.Length);
            if (Math.Abs(axis.Dot(second)) / second.Length < .995) return false;
            DxfPoint origin = rails[0].Start;
            double sideA = axis.Cross((rails[0].Start + rails[0].End) * .5 - origin);
            double sideB = axis.Cross((rails[1].Start + rails[1].End) * .5 - origin);
            double width = Math.Abs(sideA - sideB);
            if (width <= DxfGeometry.Epsilon || rails.Min(e => e.Length) < width * .5) return false;
            double low = rails.Max(e => Math.Min(axis.Dot(e.Start - origin), axis.Dot(e.End - origin)));
            double high = rails.Min(e => Math.Max(axis.Dot(e.Start - origin), axis.Dot(e.End - origin)));
            if (high - low < width * .5) return false;
            bool lowCap = false, highCap = false;
            foreach (DxfEdge cap in caps)
            {
                List<DxfPoint> points = ProbeParameters(cap).Select(cap.At).ToList();
                if (points.Any(p => axis.Cross(p - origin) < Math.Min(sideA, sideB) - width * .02 || axis.Cross(p - origin) > Math.Max(sideA, sideB) + width * .02)) return false;
                if (points.All(p => axis.Dot(p - origin) <= low + width * .02)) lowCap = true;
                else if (points.All(p => axis.Dot(p - origin) >= high - width * .02)) highCap = true;
                else return false;
            }
            return lowCap && highCap;
        }

        private static bool IsExactSlot(List<DxfEdge> source)
        {
            List<DxfEdge> primitive = new List<DxfEdge>();
            foreach (DxfEdge e in source)
            {
                if (e.Type == "LINE" || e.Type == "ARC") { primitive.Add(e); continue; }
                for (int i = 1; i < e.Breaks.Count; i++)
                {
                    DxfEdge part = Subcurve(e, e.Breaks[i - 1], e.Breaks[i]);
                    DxfEdge line = Straight(part);
                    if (line != null) { primitive.Add(line); continue; }
                    DxfPoint center; double radius, deviation;
                    List<DxfPoint> p = Enumerable.Range(0, 17).Select(j => part.At(j / 16.0)).ToList();
                    if (!DxfGeometry.FitCircle(p, out center, out radius, out deviation) || deviation > DxfGeometry.Epsilon) return false;
                    double start = Math.Atan2(p[0].Y - center.Y, p[0].X - center.X), sweep = 0;
                    for (int j = 1; j < p.Count; j++) { double angle = Math.Atan2(p[j].Y - center.Y, p[j].X - center.X) - Math.Atan2(p[j - 1].Y - center.Y, p[j - 1].X - center.X); if (angle > Math.PI) angle -= DxfGeometry.Tau; if (angle < -Math.PI) angle += DxfGeometry.Tau; sweep += angle; }
                    primitive.Add(DxfGeometry.Circle(center, radius, start, sweep));
                }
            }
            List<DxfEdge> arcs = primitive.Where(e => e.Type == "ARC").ToList(), lines = primitive.Where(e => e.Type == "LINE").ToList();
            if (arcs.Count < 2 || lines.Count < 2 || primitive.Any(e => e.Type != "ARC" && e.Type != "LINE")) return false;
            List<Tuple<DxfPoint, double, double>> ends = new List<Tuple<DxfPoint, double, double>>();
            foreach (DxfEdge arc in arcs)
            {
                int i = ends.FindIndex(x => x.Item1.Distance(arc.Center) <= DxfGeometry.Epsilon && Math.Abs(x.Item2 - arc.Radius) <= DxfGeometry.Epsilon);
                if (i < 0) ends.Add(Tuple.Create(arc.Center, arc.Radius, Math.Abs(arc.Sweep)));
                else ends[i] = Tuple.Create(ends[i].Item1, ends[i].Item2, ends[i].Item3 + Math.Abs(arc.Sweep));
            }
            if (ends.Count != 2 || Math.Abs(ends[0].Item2 - ends[1].Item2) > DxfGeometry.Epsilon || ends.Any(x => Math.Abs(x.Item3 - Math.PI) * x.Item2 > DxfGeometry.Epsilon)) return false;
            DxfPoint axis = ends[1].Item1 - ends[0].Item1;
            if (axis.Length <= DxfGeometry.Epsilon) return false;
            axis = axis * (1 / axis.Length); double r = ends[0].Item2;
            bool positive = false, negative = false;
            foreach (DxfEdge line in lines)
            {
                DxfPoint d = line.End - line.Start;
                if (d.Length <= 1e-15 || Math.Abs(axis.Cross(d)) > DxfGeometry.Epsilon) return false;
                double pa = axis.Cross(line.Start - ends[0].Item1), pb = axis.Cross(line.End - ends[0].Item1);
                if (Math.Abs(Math.Abs(pa) - r) > DxfGeometry.Epsilon || Math.Abs(pa - pb) > DxfGeometry.Epsilon) return false;
                if (pa > 0) positive = true; else negative = true;
            }
            return positive && negative;
        }

        private static List<DxfPoint> Intersections(DxfEdge a, DxfEdge b)
        {
            List<DxfPoint> points = new List<DxfPoint>();
            if (!a.Bounds.Intersects(b.Bounds, DxfGeometry.Epsilon)) return points;
            bool ac = a.Type == "ARC" || a.Type == "CIRCLE", bc = b.Type == "ARC" || b.Type == "CIRCLE";
            if (a.Type == "LINE" && b.Type == "LINE")
            {
                DxfPoint u = a.End - a.Start, v = b.End - b.Start, w = b.Start - a.Start;
                double den = u.Cross(v);
                if (Math.Abs(den) > 1e-15)
                {
                    double t = w.Cross(v) / den, s = w.Cross(u) / den;
                    if (t >= -1e-12 && t <= 1 + 1e-12 && s >= -1e-12 && s <= 1 + 1e-12) points.Add(a.Start + u * t);
                }
                else if (Math.Abs(u.Cross(w)) <= DxfGeometry.Epsilon * Math.Max(1, u.Length))
                {
                    foreach (DxfPoint p in new[] { a.Start, a.End, b.Start, b.End }) if (DxfGeometry.DistanceSegment(p, a.Start, a.End) <= DxfGeometry.Epsilon && DxfGeometry.DistanceSegment(p, b.Start, b.End) <= DxfGeometry.Epsilon) points.Add(p);
                }
                return points;
            }
            if (ac && bc)
            {
                DxfPoint d = b.Center - a.Center; double distance = d.Length;
                if (distance <= DxfGeometry.Epsilon || distance > a.Radius + b.Radius + DxfGeometry.Epsilon || distance < Math.Abs(a.Radius - b.Radius) - DxfGeometry.Epsilon) return points;
                double x = (a.Radius * a.Radius - b.Radius * b.Radius + distance * distance) / (2 * distance), h2 = a.Radius * a.Radius - x * x;
                if (h2 < -DxfGeometry.Epsilon * Math.Max(1, a.Radius)) return points;
                DxfPoint u = d * (1 / distance), p = a.Center + u * x, v = new DxfPoint(-u.Y, u.X) * Math.Sqrt(Math.Max(0, h2));
                foreach (DxfPoint hit in new[] { p + v, p - v }) if (DxfGeometry.DistanceCurve(hit, a) <= DxfGeometry.Epsilon && DxfGeometry.DistanceCurve(hit, b) <= DxfGeometry.Epsilon) points.Add(hit);
                return points;
            }
            if (a.Type == "LINE" && bc || b.Type == "LINE" && ac)
            {
                DxfEdge line = a.Type == "LINE" ? a : b, circle = ac ? a : b;
                DxfPoint u = line.End - line.Start, d = line.Start - circle.Center;
                double aa = u.Dot(u), bb = 2 * d.Dot(u), cc = d.Dot(d) - circle.Radius * circle.Radius;
                double disc = bb * bb - 4 * aa * cc;
                if (aa <= 1e-20 || disc < 0) return points;
                foreach (double t in new[] { (-bb + Math.Sqrt(disc)) / (2 * aa), (-bb - Math.Sqrt(disc)) / (2 * aa) })
                { DxfPoint p = line.Start + u * t; if (t >= -1e-12 && t <= 1 + 1e-12 && DxfGeometry.DistanceCurve(p, circle) <= DxfGeometry.Epsilon) points.Add(p); }
                return points;
            }
            // Curved entities retain analytic evaluation; subdivided segments are only for intersection searches.
            List<DxfPoint> ap = a.Preview, bp = b.Preview;
            for (int i = 1; i < ap.Count; i++) for (int j = 1; j < bp.Count; j++)
            {
                DxfEdge al = DxfGeometry.Line(ap[i - 1], ap[i]), bl = DxfGeometry.Line(bp[j - 1], bp[j]);
                if (al.Bounds.Intersects(bl.Bounds, 0)) points.AddRange(Intersections(al, bl));
            }
            return points;
        }

        internal static readonly IComparer<string> NaturalPathComparer = new NaturalNameComparer();

        internal static int CompareNaturalPath(string a, string b)
        {
            if (ReferenceEquals(a, b)) return 0;
            if (a == null) return -1;
            if (b == null) return 1;
            int dir = CompareNatural(Path.GetDirectoryName(a) ?? "", Path.GetDirectoryName(b) ?? "");
            if (dir != 0) return dir;
            int stem = CompareNatural(Path.GetFileNameWithoutExtension(a) ?? "", Path.GetFileNameWithoutExtension(b) ?? "");
            return stem != 0 ? stem : CompareNatural(a, b);
        }

        internal static int CompareNatural(string a, string b)
        {
            if (ReferenceEquals(a, b)) return 0;
            if (a == null) return -1;
            if (b == null) return 1;
            int i = 0, j = 0;
            while (i < a.Length && j < b.Length)
            {
                if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
                {
                    int ai = i, bj = j;
                    while (i < a.Length && char.IsDigit(a[i])) i++;
                    while (j < b.Length && char.IsDigit(b[j])) j++;
                    string x = a.Substring(ai, i - ai).TrimStart('0'), y = b.Substring(bj, j - bj).TrimStart('0');
                    int c = x.Length.CompareTo(y.Length); if (c == 0) c = string.CompareOrdinal(x, y); if (c == 0) c = (i - ai).CompareTo(j - bj);
                    if (c != 0) return c;
                }
                else { int c = char.ToUpperInvariant(a[i++]).CompareTo(char.ToUpperInvariant(b[j++])); if (c != 0) return c; }
            }
            return a.Length.CompareTo(b.Length);
        }

        private sealed class NaturalNameComparer : IComparer<string>
        {
            public int Compare(string a, string b) { return CompareNaturalPath(a, b); }
        }
    }
}
