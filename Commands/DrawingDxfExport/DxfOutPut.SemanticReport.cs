// Adapted from user-supplied DXF.zip; see SOURCE.md.
using System;
using System.Globalization;
namespace ADDIN.Commands.DrawingDxfExporting
{
    internal partial class DxfOutPut
    {
        /// <summary>Báo kết quả «đặt layer bằng hình học» vào hộp thông báo xuất DXF.</summary>
        private static void ReportSemantic(
            DxfSemanticResult semantic,
            DXFPropforExport properties,
            bool bendLines,
            bool laserMarks)
        {
            DxfExportNotices.Info(
                "Layer",
                "Khớp hình DXF với view: " +
                (semantic.MatchRatio * 100).ToString("0.#", CultureInfo.InvariantCulture) + " %" +
                (Math.Abs(semantic.OffsetXMm) > 1e-6 || Math.Abs(semantic.OffsetYMm) > 1e-6
                    ? " (đã bù lệch " + semantic.OffsetXMm.ToString("0.##", CultureInfo.InvariantCulture) + ", " +
                      semantic.OffsetYMm.ToString("0.##", CultureInfo.InvariantCulture) + " mm)"
                    : "") + ".");
            if (laserMarks)
            {
                DxfExportNotices.Info(
                    "LASER_MARK",
                    "Đặt LASER_MARK bằng hình học: " + semantic.MarkEntities + " nét" +
                    (semantic.UserMarkEntitiesInjected > 0 ? ", ghi thêm " + semantic.UserMarkEntitiesInjected + " sketch MARK" : "") +
                    (semantic.ErasedEntities > 0 ? ", xoá " + semantic.ErasedEntities + " vòng" : "") + ".");
                if (semantic.MarksWithoutEntity > 0 && semantic.MarkEntitiesInjected > 0)
                {
                    // Không còn là lỗi: cạnh thiếu nét đã được ghi lại từ hình chiếu model (DXF Check vẫn kiểm).
                    string message = semantic.MarksWithoutEntity + " cạnh MARK không có nét SolidWorks vẽ — TAI TOOL bổ sung " +
                        semantic.MarkEntitiesInjected + " nét LASER_MARK từ model (" + semantic.MissingMarkDetail + ", toạ độ giấy).";
                    ErrorEvents.LogDebug($"File:{properties.FileName}-{properties.PartNumber}: [LASER_MARK] {message}");
                    DxfExportNotices.Info("LASER_MARK", message);
                }
                else if (semantic.MarksWithoutEntity > 0)
                {
                    string message = "LỖI LASER_MARK: " + semantic.MarksWithoutEntity +
                        " cạnh feature MARK không có nét nào trong DXF (SolidWorks không vẽ cạnh ấy: " +
                        semantic.MissingMarkDetail + ") — kiểm tra DXF trước khi cắt.";
                    ErrorEvents.LogError($"File:{properties.FileName}-{properties.PartNumber}: {message}");
                    DxfExportNotices.Warning("LASER_MARK", message);
                }
            }
            if (bendLines)
            {
                DxfExportNotices.Info(
                    "BEND",
                    "Đường uốn: " + semantic.BendEntitiesFound + " nét SolidWorks ghi" +
                    (semantic.BendEntitiesInjected > 0 ? ", TAI TOOL bổ sung " + semantic.BendEntitiesInjected : "") + ".");
            }
            if (semantic.StrayTextRemoved > 0)
            {
                DxfExportNotices.Info(
                    "Layer",
                    "Bỏ " + semantic.StrayTextRemoved + " chữ không thuộc PART_INFO (chú thích uốn, nhãn view).");
            }
        }
    }
}
