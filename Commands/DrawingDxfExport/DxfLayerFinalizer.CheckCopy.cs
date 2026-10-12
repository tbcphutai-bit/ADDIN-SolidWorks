// Adapted from user-supplied DXF.zip; see SOURCE.md.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ADDIN.Commands.DrawingDxfExporting
{
    internal static partial class DxfLayerFinalizer
    {
        /// <summary>
        /// Bản sao DXF chỉ dùng cho DXF Check: bỏ vòng LASER_MARK là MIỆNG lỗ côn/bậc — vòng khắc có
        /// lỗ xuyên CUT đồng tâm, nhỏ hơn bên trong. FlatPattern (hình mẫu của DXF Check) chỉ có lỗ
        /// xuyên, nên vòng miệng khắc thêm bị tính là contour thừa (4115 chi tiết 27: 16 vòng Ø20 quanh
        /// lỗ Ø12, lệch 1005 mm). Vòng MARK thay lỗ xuyên (lòng đã xoá, không có CUT bên trong) không bị
        /// đụng — phép thay ấy đã có bước bù riêng. File xuất thật không đổi. Không có vòng nào thì trả
        /// lại đúng <paramref name="filePath"/>.
        /// </summary>
        internal static string CreateCheckCopyWithoutMouthRings(
            string filePath,
            DxfManufacturingLayerSettings layerSettings,
            out int removed)
        {
            removed = 0;
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return filePath;
            DxfManufacturingLayerSettings settings = DxfManufacturingLayerSettings.Normalize(layerSettings);
            Encoding encoding = Encoding.GetEncoding(932);
            List<string> lines = File.ReadAllLines(filePath, encoding).ToList();
            if (lines.Count < 2 || lines.Count % 2 != 0) return filePath;

            var circles = new List<(DxfRecord Record, string Layer, double X, double Y, double R)>();
            foreach (DxfRecord record in CollectEntityRecords(lines))
            {
                if (!IsDxfType(record, "CIRCLE")) continue;
                if (!TryGetDoubleGroupValue(lines, record.Start, record.EndExclusive, "10", out double x) ||
                    !TryGetDoubleGroupValue(lines, record.Start, record.EndExclusive, "20", out double y) ||
                    !TryGetDoubleGroupValue(lines, record.Start, record.EndExclusive, "40", out double r))
                    continue;
                if (GetNormalZ(lines, record.Start, record.EndExclusive) < 0) x = -x;   // OCS lật X
                string layer = GetGroupValue(lines, record.Start, record.EndExclusive, "8") ?? "0";
                circles.Add((record, layer, x, y, r));
            }

            var doomed = circles
                .Where(mark => IsLayer(mark.Layer, settings.LaserMark.Name) &&
                               circles.Any(bore => IsLayer(bore.Layer, settings.Cut.Name) &&
                                                   bore.R < mark.R - 0.01 &&
                                                   Math.Abs(bore.X - mark.X) <= 0.05 &&
                                                   Math.Abs(bore.Y - mark.Y) <= 0.05))
                .Select(mark => mark.Record)
                .ToList();
            if (doomed.Count == 0) return filePath;

            foreach (DxfRecord record in doomed.OrderByDescending(r => r.Start))
                lines.RemoveRange(record.Start, record.EndExclusive - record.Start);
            string checkPath = Path.Combine(
                Path.GetDirectoryName(filePath) ?? "",
                Path.GetFileNameWithoutExtension(filePath) + ".check.dxf");
            File.WriteAllLines(checkPath, lines, encoding);
            removed = doomed.Count;
            return checkPath;
        }
    }
}
