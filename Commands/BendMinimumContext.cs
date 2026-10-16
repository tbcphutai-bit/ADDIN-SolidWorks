using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace ADDIN.Commands
{
    internal sealed class BendMinimumRowResult
    {
        internal BendMinimumStatus Status;
        internal string Summary, Details;
        internal BendMinimumMeasure Datum;
        internal readonly List<BendMinimumSideResult> Sides = new List<BendMinimumSideResult>();
    }

    internal sealed class BendMinimumSideResult
    {
        internal BendMinimumMeasure Measure;
        internal BendMinimumCheckResult Check;
        internal double? LimitMm;
    }

    // One capture shared by all rows and both presentations of Set Bend Line.
    // Changing a draft mark or replacing Excel only re-evaluates these dimensions.
    internal sealed class BendMinimumContext
    {
        internal readonly Dictionary<int, BendMinimumFeatureGeometry> Geometry;
        private readonly Func<bool> isCurrent;
        internal readonly double CaptureSeconds;
        internal readonly int FaceCount, EdgeCount;
        internal int GeometryReadCount => 1;
        internal int KernelSectionCount => 0;
        internal BendToolCatalogSnapshot Catalog { get; private set; }
        internal string SourceError { get; private set; } = "Đang đọc Excel dao chấn…";
        internal bool Current { get; private set; } = true;
        internal string CaptureError;
        internal Func<BendMinimumLiveMonitor> LiveFactory;
        internal Func<BendMinimumInspection> InspectionFactory;
        private string lastTrace;
        internal void Invalidate() { Current = false; }

        internal BendMinimumContext(Dictionary<int, BendMinimumFeatureGeometry> geometry,
            Func<bool> current, double seconds, int faces, int edges)
        {
            Geometry = geometry;
            isCurrent = current;
            CaptureSeconds = seconds;
            FaceCount = faces;
            EdgeCount = edges;
        }

        // Called on the SolidWorks/UI thread only. Never put native API calls on
        // the Excel worker. Invalidation is sticky, including switching away/back.
        internal bool CheckCurrent()
        {
            if (Current)
            {
                try { Current = isCurrent != null && isCurrent(); }
                catch { Current = false; }
            }
            return Current;
        }
        internal bool SetSource(BendToolCatalogReadResult source)
        {
            var next = source?.Snapshot;
            string error = source?.Error ?? (next == null ? "Chưa đọc được Excel dao chấn." : null);
            bool same = string.Equals(SourceError, error, StringComparison.Ordinal) &&
                (Catalog == null && next == null || Catalog != null && next != null &&
                string.Equals(Catalog.SourcePath, next.SourcePath, StringComparison.OrdinalIgnoreCase) &&
                Catalog.SourceSha256 == next.SourceSha256);
            if (same) return false;
            Catalog = next;
            SourceError = error;
            return true;
        }
        internal void SourcePending()
        {
            // Reading is not a failed read. Keep the last confirmed snapshot
            // until the worker has a complete replacement or a confirmed error.
        }
        private static string Mm(double n) => n.ToString("0.###", CultureInfo.InvariantCulture);
        internal void TraceRows(IList<VCutPropertySetupCommand.BendRow> rows)
        {
            var values = new List<string>();
            foreach (var row in rows)
            {
                BendMinimumFeatureGeometry item; Geometry.TryGetValue(row.FeatureId, out item);
                var check = Evaluate(row);
                values.Add("[BEND MIN] feature=" + row.FeatureName + " id=" + row.FeatureId +
                    " material=" + item?.MaterialCode + " mark=" + row.Kind + " status=" + check.Status +
                    " sides=" + string.Join(" | ", check.Sides.Select(s => s.Measure.Side + " " + s.Measure.Shape + "/" + s.Measure.Dimension +
                    " outside_mm=" + Mm(s.Measure.ValueMm) + " t_mm=" + Mm(s.Measure.ThicknessMm) +
                    " minimum_mm=" + (s.LimitMm.HasValue ? Mm(s.LimitMm.Value) : "?") + " result=" + s.Check.Status +
                    " rule=" + s.Check.Rule?.Id + " sheet=" + s.Check.Rule?.SourceSheet + " row=" + s.Check.Rule?.SourceRow)) +
                    (check.Sides.Count == 0 ? " reason=" + check.Details.Replace("\r\n", " | ") : ""));
            }
            string trace = "[BEND MIN] current=" + Current + " excel=" + Catalog?.SourcePath + " sha256=" + Catalog?.SourceSha256 +
                " source_error=" + SourceError + " bends=" + rows.Count + " geometry_reads=1\r\n" + string.Join("\r\n", values);
            if (trace == lastTrace) return;
            lastTrace = trace; Debug.WriteLine(trace);
        }
        private static BendMinimumRowResult Unknown(string reason) => new BendMinimumRowResult {
            Status = BendMinimumStatus.InsufficientData, Summary = "? Chưa đủ dữ liệu", Details = reason
        };

        internal BendMinimumRowResult Evaluate(VCutPropertySetupCommand.BendRow row)
        {
            if (!Current) return Unknown("Part/cấu hình hoặc hình học đã thay đổi. Bấm Refresh để đọc lại; các Mark đang sửa vẫn được giữ trong bảng.");
            if (new[] { "V溝1", "V溝2", "C溝" }.Contains(row.Kind))
                return new BendMinimumRowResult {
                    Status = BendMinimumStatus.GroovedAccepted, Summary = "✓ Cạnh bào",
                    Details = "Tối thiểu: OK (cạnh bào " + row.Kind + ")." +
                        (row.Kind != row.OriginalKind ? " Mark đang sửa, chưa lưu vào Part." : "")
                };
            if (!string.IsNullOrEmpty(row.Kind)) return Unknown("Mark không được hỗ trợ; không tự miễn kiểm tra tối thiểu.");
            BendMinimumFeatureGeometry item;
            if (!Geometry.TryGetValue(row.FeatureId, out item)) return Unknown(CaptureError ?? "Không có hình học cho feature này.");
            if (CaptureError != null || item.Error != null) return Unknown(CaptureError ?? item.Error);
            if (item.Measures.Count != 2) return Unknown("Chưa đo được hai phía của nếp chấn.");

            var messages = new List<string>();
            var checks = new List<Tuple<BendMinimumMeasure, BendMinimumCheckResult>>();
            foreach (var measure in item.Measures)
            {
                BendMinimumCheckResult check;
                if (measure.Error != null)
                    check = new BendMinimumCheckResult(BendMinimumStatus.InsufficientData, measure.Error);
                else if (Catalog == null)
                    check = new BendMinimumCheckResult(BendMinimumStatus.InsufficientData, SourceError);
                else
                    check = Catalog.CheckMinimum(item.MaterialCode, measure.ThicknessMm, measure.Shape,
                        measure.Dimension == "A" ? (double?)measure.ValueMm : null,
                        measure.Dimension == "B" ? (double?)measure.ValueMm : null,
                        measure.Dimension == "C" ? (double?)measure.ValueMm : null, row.BendAngleDeg ?? double.NaN);
                checks.Add(Tuple.Create(measure, check));
                string label = measure.Shape == null ? "?" : measure.Shape + "/" + measure.Dimension + " = " + Mm(measure.ValueMm) + " mm phủ bì";
                double? limit = check.Rule == null ? null : measure.Dimension == "A" ? check.Rule.MinimumA : measure.Dimension == "B" ? check.Rule.MinimumB : check.Rule.MinimumC;
                messages.Add(measure.Side + " • " + label + (limit.HasValue ? " / min " + Mm(limit.Value) : "") +
                    " • " + (measure.Neighbor ?? "?") + "\r\n" + check.Message);
            }
            var worst = checks.OrderBy(c => c.Item2.Status == BendMinimumStatus.BelowMinimum ? 0 : c.Item2.Status == BendMinimumStatus.MeetsMinimum ? 2 : 1).First();
            string worstSideName = worst.Item1.Side ?? "Phía A";
            string caption = worst.Item2.Status == BendMinimumStatus.BelowMinimum
                ? string.Format("NG: {0} ({1}辺) {2} mm < Min", worstSideName, worst.Item1.Dimension, Mm(worst.Item1.ValueMm))
                : worst.Item2.Status == BendMinimumStatus.MeetsMinimum
                    ? "✓ Đạt (" + string.Join("/", checks.Select(c => c.Item1.Shape).Distinct()) + ")"
                    : "? Chưa đủ dữ liệu";
            string source = Catalog == null ? SourceError : "Excel: " + Catalog.SourcePath;
            var result = new BendMinimumRowResult {
                Status = worst.Item2.Status, Summary = caption, Datum = worst.Item1,
                Details = "TỐI THIỂU • " + item.MaterialCode + " t=" + Mm(item.Measures[0].ThicknessMm) + " mm\r\n" +
                    string.Join("\r\n", messages) + "\r\n" + source +
                    "\r\nPhía A/B là hai phía nếp chấn; L dùng A, Z dùng B, C(U) dùng C của bảng."
            };
            foreach (var pair in checks)
            {
                var rule = pair.Item2.Rule;
                result.Sides.Add(new BendMinimumSideResult { Measure = pair.Item1, Check = pair.Item2,
                    LimitMm = rule == null ? null : pair.Item1.Dimension == "A" ? rule.MinimumA : pair.Item1.Dimension == "B" ? rule.MinimumB : rule.MinimumC });
            }
            return result;
        }
    }
}
