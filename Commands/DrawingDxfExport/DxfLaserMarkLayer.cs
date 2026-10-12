// Adapted from user-supplied DXF.zip; see SOURCE.md.
using System;
using System.Globalization;
using System.Linq;
using System.Text;

namespace ADDIN.Commands.DrawingDxfExporting
{
    /// <summary>
    /// Nhận dạng layer KHẮC (laser mark) trong một DXF bất kỳ — DXF của cơ chế xuất mới (LASER_MARK theo
    /// bảng cài đặt), DXF cũ còn layer marker tạm, hoặc DXF từ CAD/xưởng khác đặt tên layer khắc theo
    /// thói quen (MARK, LASER-MARK, LAZER, 刻印, ケガキ/ｹｶﾞｷ, 罫書, KEGAKI…). Bộ kiểm/sửa DXF (autofix)
    /// phải coi các nét này là nét KHẮC, không phải đường cắt: không bắt khép kín, không nối khe, không
    /// tính chiều dài cắt, không xoá nét ngắn. Thuần, có test.
    /// </summary>
    internal static class DxfLaserMarkLayer
    {
        private static readonly string[] Tokens =
        {
            "MARK", "LAZER", "KEGAKI", "刻印", "ケガキ", "罫書", "マーキング"
        };

        public static bool IsLaserMarkLayer(string layer, DxfManufacturingLayerSettings settings = null)
        {
            if (string.IsNullOrWhiteSpace(layer)) return false;
            string name = Normalize(layer);
            DxfManufacturingLayerSettings s = settings == null ? null : DxfManufacturingLayerSettings.Normalize(settings);
            if (s != null)
            {
                if (string.Equals(name, Normalize(s.LaserMark.Name), StringComparison.Ordinal)) return true;
                // Layer đã được cài là CẮT / UỐN / THÔNG TIN thì không bao giờ là khắc.
                if (string.Equals(name, Normalize(s.Cut.Name), StringComparison.Ordinal) ||
                    string.Equals(name, Normalize(s.Bend.Name), StringComparison.Ordinal) ||
                    string.Equals(name, Normalize(s.PartInfo.Name), StringComparison.Ordinal))
                    return false;
            }
            if (Tokens.Any(token => name.IndexOf(token, StringComparison.Ordinal) >= 0)) return true;
            // «LASER» một mình là khắc; «LASER_CUT»/«LASERCUT» là cắt.
            return name.IndexOf("LASER", StringComparison.Ordinal) >= 0 &&
                   name.IndexOf("CUT", StringComparison.Ordinal) < 0;
        }

        private static string Normalize(string text)
        {
            string value = (text ?? "").Trim();
            try { value = value.Normalize(NormalizationForm.FormKC); } catch (ArgumentException) { }
            return value.ToUpperInvariant();
        }
    }
}
