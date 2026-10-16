using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Xml.Linq;
using ADDIN.Commands.DxfChecking;
using ADDIN.UI;

internal static class DrawingDxfTopologyReviewTests
{
    static int passed, failed;
    static string output;
    static readonly List<object> actualFiles = new List<object>();
    static DxfEdge Line(double x, double y, double u, double v) { return DxfGeometry.Line(new DxfPoint(x, y), new DxfPoint(u, v)); }
    static List<DxfEdge> Rect(double gap = 0, double x = 0, double y = 0)
    { return new List<DxfEdge> { Line(x + gap, y, x + 100, y), Line(x + 100, y, x + 100, y + 30), Line(x + 100, y + 30, x, y + 30), Line(x, y + 30, x, y) }; }
    static DxfCheckResult Check(IEnumerable<DxfEdge> a, IEnumerable<DxfEdge> b, double tolerance = .2)
    { return ContourMatcher.Check(a, b, new DrawingDxfCheckOptions { ToleranceMm = tolerance }); }
    static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Case(string name, Action action)
    { try { action(); passed++; Console.WriteLine("PASS " + name); } catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + ": " + error); } }
    static string Snapshot(IEnumerable<DxfEdge> edges)
    { return string.Join("|", edges.Select(e => string.Join(",", new[] { e.Type, e.Start.X.ToString("R"), e.Start.Y.ToString("R"), e.End.X.ToString("R"), e.End.Y.ToString("R"), e.Length.ToString("R"), e.At(.4).X.ToString("R"), e.At(.4).Y.ToString("R") }))); }
    static string Hash(string path) { using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))); }

    [STAThread]
    static int Main(string[] args)
    {
        output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
        // Keep the full previous geometry/parser regression suite in this run.
        typeof(DrawingDxfTests).GetMethod("Main", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { new[] { Path.Combine(output, "fixtures") } });
        if (Environment.ExitCode != 0) failed++;
        Case("closed geometry remains clean PASS", () => Assert(Check(Rect(), Rect()).Status == "PASS", "closed rectangle"));
        Case("DXF tiny gap is joined and precisely located as CHECK", () => {
            var r = Check(Rect(), Rect(.001956479)); var m = r.Mismatches.Single(mismatch => mismatch.ReviewIssue);
            Assert(r.GeometryPassed && r.Status == "CHECK" && !r.Passed && r.ReviewRequired, "CHECK must not be a clean PASS");
            Assert(m.HasDxfPosition && !m.HasDrawingPosition && Math.Abs(m.Deviation - .001956479) < 1e-12, "actual gap measurement");
            Assert(m.DxfPosition.Distance(r.Translation.Apply(new DxfPoint(.001956479 / 2, 0))) < 1e-8, "join midpoint in aligned coordinates");
            Assert(!r.Mismatches.Any(mismatch => mismatch.Type == "Open DXF geometry"), "whole chain incorrectly reported open");
            Assert(r.MaximumDeviation < m.Deviation, "join gap must not overwrite shape deviation"); });
        Case("Drawing tiny gap is independently CHECK", () => {
            var r = Check(Rect(.005), Rect()); Assert(r.GeometryPassed && r.Status == "CHECK", "Drawing join blocks comparison");
            Assert(r.Mismatches.Single(m => m.ReviewIssue).HasDrawingPosition, "Drawing location lost"); });
        Case("review coordinates use the same X/Y translation as preview", () => {
            var r = Check(Rect(), Rect(.006, 500, 300));
            Assert(r.AlignmentFound && r.Status == "CHECK", "translation failed");
            Assert(r.Mismatches.Single(m => m.ReviewIssue).DxfPosition.Distance(r.Translation.Apply(new DxfPoint(500.003, 300))) < 1e-7, "unaligned marker"); });
        Case("near-overlap single edge is CHECK", () => {
            var b = Rect(); b.Add(Line(0, .007, 100, .007)); var r = Check(Rect(), b);
            Assert(r.Status == "CHECK" && r.GeometryPassed && r.Mismatches.Any(m => m.Type == "Near-overlap"), "near-overlap branch"); });
        Case("near-overlap split polyline is CHECK", () => {
            var b = Rect(); b.Add(Line(0, .006, 40, .006)); b.Add(Line(40, .006, 100, .006));
            Assert(Check(Rect(), b).Status == "CHECK", "segmented near-duplicate chain"); });
        Case("alternate path with a real bulge remains FAIL", () => {
            var b = Rect(); b.Add(Line(0, 0, 50, 1)); b.Add(Line(50, 1, 100, 0));
            var r = Check(Rect(), b); Assert(r.Status == "FAIL" && !r.GeometryPassed, "real branch was discarded"); });
        Case("gap beyond 0.01 mm remains FAIL", () => Assert(Check(Rect(), Rect(.02)).Status == "FAIL", "large open cut was bridged"));
        Case("repair respects a tighter comparison tolerance", () => Assert(Check(Rect(), Rect(.005), .002).Status == "FAIL", "user tolerance bypassed"));
        Case("ambiguous T junction remains FAIL", () => {
            var b = Rect(.006); b.Add(Line(.003, 0, .003, 1)); Assert(Check(Rect(), b).Status == "FAIL", "ambiguous branch paired arbitrarily"); });
        Case("joined gap never hides moved hole", () => {
            var a = Rect(); a.Add(DxfGeometry.Circle(new DxfPoint(30, 10), 2, 0, DxfGeometry.Tau));
            var b = Rect(.005); b.Add(DxfGeometry.Circle(new DxfPoint(30.8, 10), 2, 0, DxfGeometry.Tau));
            var r = Check(a, b); Assert(r.Status == "FAIL" && r.ReviewRequired && r.FailedInsideContours == 1, "CHECK masked a real hole mismatch"); });
        Case("joined gap never hides a new hole", () => {
            var b = Rect(.005); b.Add(DxfGeometry.Circle(new DxfPoint(30, 10), 2, 0, DxfGeometry.Tau));
            Assert(Check(Rect(), b).Status == "FAIL", "additional cut was hidden"); });
        Case("repair leaves a separate valid narrow slot intact", () => {
            var slot = new[] { Line(30, 10, 30.005, 10), Line(30.005, 10, 30.005, 20), Line(30.005, 20, 30, 20), Line(30, 20, 30, 10) };
            var a = Rect(); a.AddRange(slot); var b = Rect(.005); b.AddRange(slot);
            var r = Check(a, b); Assert(r.Status == "CHECK" && r.DrawingInsideContourCount == 1 && r.MatchedInsideContours == 1, "unrelated small slot altered"); });
        Case("repair does not mutate source edge objects or lists", () => {
            var b = Rect(.006); b.Add(Line(0, .007, 100, .007)); string before = Snapshot(b); int count = b.Count;
            Check(Rect(), b); Assert(Snapshot(b) == before && b.Count == count, "source geometry mutated"); });
        Case("comparison extraction keeps raw gap while export default is unchanged", () => {
            var buffer = new List<double>(); foreach (var edge in Rect(.005)) buffer.AddRange(new[] { 0.0, 0, 0, 0, 0, 0, 0, 0, 2, edge.Start.X / 1000, edge.Start.Y / 1000, 0, edge.End.X / 1000, edge.End.Y / 1000, 0 });
            DrawingNormalizationReport report; var raw = DrawingViewCurveBuffer.Read(buffer.ToArray(), 1, out report, false);
            Assert(Math.Abs(raw[0].Start.X - .005) < 1e-10 && Check(Rect(), raw).Status == "CHECK", "comparison silently snapped endpoints");
            Assert(GeometryLoopBuilder.Build(DrawingViewCurveBuffer.Read(buffer.ToArray(), 1)).Outer != null, "legacy export default changed"); });

        foreach (string filename in args.Skip(1)) Case("actual saved DXF " + Path.GetFileName(filename), () => {
            string hash = Hash(filename); int ignored; var edges = new DxfGeometryReader().Read(filename, "BEND;BENDLINES;BEND_UP;BEND_DOWN", out ignored);
            string before = Snapshot(edges); var clock = Stopwatch.StartNew(); var repaired = ComparisonTopologyRepair.Build(edges, .2); clock.Stop();
            Assert(repaired.Outer != null && repaired.Unclosed.Count == 0, "outer=" + (repaired.Outer != null) + ", open=" + repaired.Unclosed.Count);
            Assert(repaired.Reviews.Count > 0, "no CHECK evidence");
            var canonical = repaired.Outer.Entities.Concat(repaired.Inside.SelectMany(loop => loop.Entities)).ToList();
            var result = Check(canonical, edges); Assert(result.Status == "CHECK" && result.GeometryPassed, "comparison status=" + result.Status);
            Assert(Snapshot(edges) == before && Hash(filename) == hash, "saved DXF or raw edges modified");
            var record = new { File = filename, Status = result.Status, TopologyMs = clock.Elapsed.TotalMilliseconds, OuterArea = repaired.Outer.Area,
                Inside = repaired.Inside.Count, Unclosed = repaired.Unclosed.Count, Reviews = repaired.Reviews };
            actualFiles.Add(record); Console.WriteLine("  topology " + clock.Elapsed.TotalMilliseconds.ToString("0.0") + " ms, " + repaired.Reviews.Count + " CHECK locations");
        });
        Case("batch preview and reports preserve CHECK labels, paths and positions", () => {
            var raw = Rect(.005); var result = Check(Rect(), raw);
            var item = new DxfBomCheckItem { ItemNumber = "13", PartNumber = "TEST", DrawingPath = @"C:\test\part.SLDDRW", DxfPath = @"C:\selected\4204-13.dxf", Sheet = "Sheet1", View = "Flat", Status = result.Status, Result = result,
                Message = "Hình học khớp; kiểm tra CHECK", PreviewDrawingEdges = Rect(), PreviewDxfEdges = raw };
            var batch = new DxfBomBatchResult { BomPath = @"C:\test\BOM.SLDDRW", ReportPath = Path.Combine(output, "check-review-" + Guid.NewGuid().ToString("N") + ".xlsx"), Items = new List<DxfBomCheckItem> { item }, DxfSourceMode = "FOLDER", DxfSourcePath = @"C:\selected" };
            Assert(batch.Checks == 1 && batch.Passed == 0 && batch.Failed == 0, "batch count drops CHECK");
            DxfCheckExport.WriteBomDrawingExcel(batch, .2);
            using (var file = File.OpenRead(batch.ReportPath)) using (var zip = new ZipArchive(file, ZipArchiveMode.Read)) using (var reader = new StreamReader(zip.GetEntry("xl/worksheets/sheet2.xml").Open())) {
                string text = reader.ReadToEnd(); var ns = XNamespace.Get("http://schemas.openxmlformats.org/spreadsheetml/2006/main");
                double expected = result.Mismatches.Single(m => m.ReviewIssue).DxfPosition.X;
                bool coordinate = XDocument.Parse(text).Descendants(ns + "c").Where(c => ((string)c.Attribute("r") ?? "").StartsWith("M", StringComparison.Ordinal))
                    .Any(c => { double value; return double.TryParse((string)c.Element(ns + "v"), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && Math.Abs(value - expected) < 1e-8; });
                Assert(text.Contains("CHECK-01") && text.Contains("DRAWING X") && text.Contains("DXF X") && coordinate, "report misses aligned location"); }
            using (var form = new DxfBomDrawingReviewForm(batch)) {
                form.Size = new Size(1500, 850); form.CreateControl();
                var fields = BindingFlags.NonPublic | BindingFlags.Instance;
                var grid = (DataGridView)typeof(DxfBomDrawingReviewForm).GetField("issues", fields).GetValue(form);
                Assert(grid.Rows.Count == 1 && Convert.ToString(grid.Rows[0].Cells[0].Value) == "CHECK-01", "issue label");
                var preview = (DxfPreview)typeof(DxfBomDrawingReviewForm).GetField("preview", fields).GetValue(form);
                Assert(preview.ComparisonMarkerIsCheck && preview.ComparisonMarkerLabel == "CHECK-01", "preview marker label");
                // Render the custom preview directly; no visible window or UI automation.
                preview.Dock = DockStyle.None; preview.Size = new Size(820, 500);
                using (var bitmap = new Bitmap(preview.Width, preview.Height)) {
                    preview.DrawToBitmap(bitmap, preview.ClientRectangle);
                    int amber = 0; for (int y = 0; y < bitmap.Height; y++) for (int x = 0; x < bitmap.Width; x++) {
                        var color = bitmap.GetPixel(x, y); if (color.R > 140 && color.G > 60 && color.G < 180 && color.B < 60) amber++;
                    }
                    Assert(amber > 20, "CHECK marker was not painted"); bitmap.Save(Path.Combine(output, "check-review-preview.png"));
                }
            }
        });
        File.WriteAllText(Path.Combine(output, "actual-dxf-repair.json"), new JavaScriptSerializer { MaxJsonLength = 10000000 }.Serialize(actualFiles));
        Console.WriteLine("TOPOLOGY REVIEW RESULT " + passed + " passed; " + failed + " failed");
        return failed == 0 ? 0 : 1;
    }
}
