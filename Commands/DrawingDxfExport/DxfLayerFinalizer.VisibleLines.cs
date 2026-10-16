using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ADDIN.Commands.DxfChecking;

namespace ADDIN.Commands.DrawingDxfExporting
{
    internal static partial class DxfLayerFinalizer
    {
        // Positioning uses the complete immutable source buffer. Selection uses
        // its continuous subset, so removing a hidden extreme never shifts the frame.
        private static List<DxfEdge> VisibleDrawingReference(DxfSemanticPlan plan, out double paperX, out double paperY)
        {
            if (plan.ReferenceCurveBuffer == null || plan.ReferenceCurveBuffer.Length == 0)
                throw new InvalidDataException("Thiếu curve tham chiếu của Drawing.");
            var all = DrawingViewCurveBuffer.Read(plan.ReferenceCurveBuffer, 1.0);
            var box = DxfBox.From(all.SelectMany(e => DxfGeometry.Sample(e, .001)));
            double[] paper = BoundsOf(plan.VisibleEdges.SelectMany(e => e.Points).Select(p => new DxfPoint2D { X = p[0], Y = p[1] }));
            if (paper == null) throw new InvalidDataException("Không đọc được hệ tọa độ cạnh thấy Drawing.");
            paperX = paper[0] - box.MinX * plan.ViewScale;
            paperY = paper[1] - box.MinY * plan.ViewScale;
            return DrawingViewCurveBuffer.Read(plan.ContinuousReferenceCurveBuffer ?? plan.ReferenceCurveBuffer, 1.0);
        }

        private static List<DxfSemanticItem> VisibleDrawingItems(DxfSemanticPlan plan)
        {
            var curves = VisibleDrawingReference(plan, out double x, out double y);
            var items = new List<DxfSemanticItem>();
            foreach (var curve in curves)
            {
                var item = new DxfSemanticItem { Kind = "POLYLINE", Source = "Drawing visible continuous edge" };
                foreach (var point in GateCurveSamples(curve))
                    item.Points.Add(new[] { point.X * plan.ViewScale + x, point.Y * plan.ViewScale + y });
                items.Add(item);
            }
            return items;
        }

        private sealed class RawDrawingLineVisibility
        {
            readonly Dictionary<string, string> layerTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            readonly HashSet<string> hiddenLayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            readonly Dictionary<string, bool> solidTypes = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            internal RawDrawingLineVisibility(List<string> lines)
            {
                solidTypes["CONTINUOUS"] = true;
                foreach (var record in LayoutRecordsInSection(lines, "TABLES", "LTYPE"))
                {
                    string name = GetGroupValue(lines, record.Start, record.EndExclusive, "2");
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    var lengths = new List<double>();
                    for (int i = record.Start; i + 1 < record.EndExclusive; i += 2)
                        if (lines[i].Trim() == "49")
                        {
                            if (!double.TryParse(lines[i+1], NumberStyles.Float, CultureInfo.InvariantCulture, out double length) || double.IsNaN(length) || double.IsInfinity(length))
                                throw new InvalidDataException("Mẫu LTYPE DXF không hợp lệ: " + name);
                            lengths.Add(length);
                        }
                    solidTypes[name] = lengths.All(v => v > 0);
                }
                foreach (var record in LayoutRecordsInSection(lines, "TABLES", "LAYER"))
                {
                    string name = GetGroupValue(lines, record.Start, record.EndExclusive, "2");
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    layerTypes[name] = GetGroupValue(lines, record.Start, record.EndExclusive, "6") ?? "CONTINUOUS";
                    if (LayoutNumber(lines, record, "62", 7) < 0 || (((int)LayoutNumber(lines, record, "70", 0)) & 1) != 0)
                        hiddenLayers.Add(name);
                }
            }
            internal bool IsVisibleContinuous(List<string> lines, DxfRecord record)
            {
                string layer = GetGroupValue(lines, record.Start, record.EndExclusive, "8") ?? "0";
                if (hiddenLayers.Contains(layer) || LayoutNumber(lines, record, "60", 0) != 0 || LayoutNumber(lines, record, "67", 0) != 0) return false;
                string type = GetGroupValue(lines, record.Start, record.EndExclusive, "6") ?? "BYLAYER";
                if (type.Equals("BYLAYER", StringComparison.OrdinalIgnoreCase))
                    type = layerTypes.TryGetValue(layer, out string inherited) ? inherited : "CONTINUOUS";
                // A top-level BYBLOCK entity uses the default continuous pattern;
                // INSERT/block decoration is removed before publication.
                if (type.Equals("BYBLOCK", StringComparison.OrdinalIgnoreCase)) type = "CONTINUOUS";
                return solidTypes.TryGetValue(type, out bool solid) && solid;
            }
        }

        private static bool NormalizeVisibleLinePatterns(List<string> lines, DxfManufacturingLayerSettings settings)
        {
            bool changed = false;
            foreach (var record in CollectEntityRecords(lines).OrderByDescending(r => r.Start))
            {
                string layer = GetGroupValue(lines, record.Start, record.EndExclusive, "8");
                if (!IsLayer(layer, settings.Cut.Name) && !IsLayer(layer, settings.LaserMark.Name)) continue;
                if (!GeometryTypes.Contains(record.Type) && !IsDxfType(record, "VERTEX") && !IsDxfType(record, "SEQEND")) continue;
                changed |= SetLayoutGroup(lines, record, "6", "CONTINUOUS");
                if (GetGroupValue(lines, record.Start, record.EndExclusive, "60") != null)
                    changed |= SetLayoutGroup(lines, record, "60", "0");
            }
            return changed;
        }
    }
}
