Nguồn: `C:\Users\SGN26\Downloads\DXF.zip`, do người dùng cung cấp ngày 09/10/2026.

Các partial class `DxfOutPut`, `DxfLayerFinalizer`, `FlatPatternViewSelector` và các lớp hình học đi kèm được chuyển namespace sang `ADDIN.Commands.DrawingDxfExporting`. Luồng đang dùng lấy hình từ view Flat Pattern trong Drawing bằng bản sao `.SLDDRW` rồi SaveAs DXF, giữ logic phân layer và tỉ lệ 1:1 của mẫu.

`DxfExportContext.cs` và `DrawingBomDxfExport.cs` là phần nối mới với SolidWorks/BOM của ADDIN. Không phụ thuộc XToolPro, netDxf hay module Nesting. Bộ đọc DXF và preview dùng lại các lớp hiện có trong ADDIN. `DxfPalette.cs` chứa bảng màu ACI tương thích netDxf; giấy phép đi kèm được giữ trong `netDxf-LICENSE.txt`.

Không đưa `DrawingDxfSave` (xuất toàn bộ Drawing), `ExportDrawingViewToDXF` (lệnh chọn view riêng), NestingQuick và phần sửa/lưu Part của mẫu vào lệnh BOM này. Không lấy hình thay thế từ Part khi Drawing không có view phù hợp.

API màu dùng `swUseModelColorInDrawings`, được liệt kê trong [SOLIDWORKS API Help](https://help.solidworks.com/2026/english/api/swconst/SolidWorks.Interop.swconst~SolidWorks.Interop.swconst.swUserPreferenceToggle_e.html?id=a9f0aa13fe6e42bfa579b65823e3ca89). Giá trị toàn cục được khôi phục sau lệnh; override màu/component được xóa trên bản sao Drawing trước SaveAs.
