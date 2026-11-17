# Kadai — Kế hoạch kỹ thuật đầy đủ
Nền tảng: SolidWorks 2024 SP5.0; Windows 11; .NET Framework 4.8; x64; C# 7.3; Add-in hiện tại tbcphutai-bit/ADDIN-SolidWorks.

## 1. Yêu cầu đầy đủ
1. User chọn mặt/bề mặt panel làm master; tùy ý chọn N thanh dọc và M thanh ngang, phương bố trí, bước/lề/khu vực giới hạn và trong giai đoạn sau có vị trí riêng.
2. Mỗi thanh có hình dạng riêng theo panel tại vị trí đó; người dùng chọn những mép/đáy/đầu nào cố định hay được phép thay đổi. Không chỉ Trim.
3. Các thanh làm từ Sheet Metal theo vật liệu/chiều dày trong file SLDPRT mẫu, khi các thuộc tính đó thực sự tồn tại.
4. Rãnh giữa thanh dọc/ngang bằng Part-level **Cut-Extrude**, cần kiểm tra Normal Cut và hướng cắt; có **độ hở lắp** khác nhau ở hai phương, xác định chiều sâu và hướng đưa vào theo mô hình gốc.
5. Có Preview 3D không sửa Part thật; chỉ Apply sau kiểm chứng và xác nhận. Cập nhật số lượng, hướng, hình dạng panel mà giữ Feature/gán Part/2D liên quan nếu có thể.
6. Sản xuất: Part native, Flat Pattern hợp lệ, Assembly/BOM, Drawing SLDDRW và DXF 1:1.

## 2. Mẫu và giới hạn
CAD mẫu: C:\Users\SGN26\Desktop\4315\4315
KDA1.SLDPRT phải được mở bằng SolidWorks. Hai STEP chỉ chứng minh hình học: KDA1 7x4, KF3 5x2; dày dạng tấm 1.6 mm; một số rãnh 1.8 hoặc 1.6 mm. Nền dạng tấm khoảng 2.3 mm.
28 và 10 là số vị trí giao dự kiến; tối đa 56 và 20 vùng notch nếu cả hai thanh đều cắt tại mỗi giao. Thực tế phải kiểm tra số Cut và cut depth, không suy từ số giao.
Một rãnh rộng hơn đối tượng ghép 0.2 mm là **total geometric width clearance**, chưa được xác minh là dung sai gia công áp dụng chung.
KHÔNG xác định chắc chắn master panel source từ hai STEP; khi thiếu, bắt buộc lựa chọn Face/Surface thật ở UI.

## 3. Kiến trúc dự kiến (Codex có trách nhiệm phản biện)
- SourceResolver: Face/Surface/Component reference có persistent IDs và configuration; chuyển đúng transform nếu ở Assembly, lưu hệ tọa độ U/V/N.
- DistributionSolver: N/M, equal-by-span, pitch cố định, manual coordinates; lề, vùng cấm, chiều quay, Flip; gắn ID ổn định.
- PanelSectionSolver: giao của từng mặt phẳng thanh với panel (có thể gồm nhiều face), lấy đúng nhánh, offset theo normal và clipping trong footprint. Giữ datum đáy/đầu và các feature được lock; contour 2D hợp lệ, không tự giao.
- JointSolver: tính giao vị trí thực giữa mỗi cặp; một mặt cắt rãnh trên thanh dọc và một trên thanh ngang theo hình học mẫu, cách đưa thanh và độ sâu. Độ rộng: với giao vuông góc, width_long = thickness_cross + extra_long và width_cross = thickness_long + extra_cross. Với góc xiên phải xét projected thickness, không dùng công thức vuông góc.
- NativeSheetMetalWriter: dựng Sketch/Ref Plane và native Sheet Metal (Base Flange/Tab hay workflow phù hợp đã kiểm chứng), tạo Part-level Cut-Extrude / Normal Cut với Feature Scope và Flat Pattern; **không** thay bằng imported solid đơn thuần.
- PreviewManager: hiện temporary body chỉ tạm, clear/hide đúng khi cancel, switch doc, rebuild, close; trước Apply không thêm Feature hay dirty source.
- RegenerationManager: mapping ID theo rib/part/config/source, đối chiếu old/new với preview diff, giữ user-added features nếu còn hợp lệ, cảnh báo deletion/dangling reference và xử lý rollback.
- ManufacturingWriter: Part, Assembly, BOM, Drawing, DXF từ FlatPattern chính xác, có báo cáo OK/WARN/FAIL từng Part và không overwrite bất ngờ.

## 4. Thông tin phải đọc thật từ SLDPRT
Mở KDA1 native và KF3 native nếu tìm thấy; enumerate Feature Tree + subfeatures, sketch/dimensions, SheetMetal definitions/thickness/material/K-factor/bend allowance/bend radius/cấu hình, FlatPattern, Cut-Extrude Sketch widths/depth/direction/normal-cut/feature-scope; CustomProperties và relations. Kiểm tra ít nhất 2 giao điển hình mỗi phương. So sánh với STEP, ghi lỗi nếu không khớp. Xem Assembly hoặc Drawing gốc nếu có.
**Không ghi giả vào báo cáo tên Feature/vật liệu khi chưa đọc SLDPRT.**

## 5. UI production
Model > Macro > Kadai. Giữ theme hiện tại; ưu tiên native SolidWorks PropertyManagerPage theo cơ chế của UI/NativeCommandPropertyManagerPage.cs, lập trình COM cẩn thận:
- Source: chọn face/surface panel, diện tích, hướng, bottom datum và Lock boundaries.
- Grid: N/M, pitch/end margins, manual positions.
- Sheet Metal: material/template mẫu, thickness hiện hành, gap đến panel, bend settings (nếu có).
- Joints: extra clearance mỗi phương, entry direction, depth, Normal Cut toggle, warnings.
- Review: overlay preview 3D/old-vs-new; status từng thanh, số giao, thay đổi Cut.
- Actions: Analyze → Preview 3D → Apply to new working copy → Update → Drawing/DXF → Cancel.
P0 hiện mới chỉ là WinForms layout preview (2D), Apply bị khóa.

## 6. Roadmap
P0: nút Kadai, Form số thanh, layout math, cảnh báo; hoàn tất mã nguồn khởi đầu, chưa build SW.
P1: Codex mở native files, báo cáo feature/SheetMetal/cut/material, đánh giá hai hay nhiều hướng kiến trúc, tìm master panel.
P2: Face selection + real 3D temporary-body Preview 3 long / 1 cross, cancel không đổi source.
P3: Một rib native Sheet Metal, một rồi nhiều Cut-Extrude lắp ghép, valid FlatPattern; dựng 3+1 trên working copy.
P4: N×M, các mẫu 7x4 và 5x2, thay số 7x4 → 10x6 → 7x4, giữ dữ liệu người dùng.
P5: tạo các Part sản xuất, Assembly, SLDDRW, DXF; check file link/đơn vị/FlatPattern.
P6: UI/UX và regression, kiểm thử x64 Debug/Release, 30+ chu kỳ mở/đóng, COM/HWND, xử lý crash.

## 7. Tiêu chuẩn nghiệm thu
- Kadai xuất hiện đúng tab Macro và không ảnh hưởng lệnh Check Same Part, Check Drawing BOM, AutoSpline, Mirror Part, VCut, Check Hole/DXF.
- Shape thay đổi theo từng vị trí trên panel, không Pattern các shape như nhau.
- Cut thật nằm ở từng Part Feature Tree, không chỉ Assembly-level; khe hở theo từng phương kiểm chứng bằng đo thực và hướng lắp.
- Native Sheet Metal thickness/material/FlatPattern đúng mẫu (khi mẫu chứa thông tin), Drawing và DXF chuẩn đơn vị mm tỉ lệ 1:1.
- Cancel không tạo Feature/không lưu model; Apply chỉ ra thư mục output mới, không đè lên CAD mẫu.
- Đổi số N/M cập nhật các thanh/slot/drawing với bản báo cáo diff, không mất các cut do user thêm một cách âm thầm.
- Không thể trải phẳng / không xác định panel / cut không đúng / reference mất => WARN/FAIL và dừng xuất gia công; không giả định thành công.

## 8. Quy tắc kỹ thuật
SolidWorks API nhập chiều dài bằng **meters**, UI nhập mm; mọi đổi đơn vị phải tường minh. Phải kiểm tra chữ ký Interop SW2024 thực tại máy, đặc biệt FeatureCut4/NormalCut/BaseFlange và temp-body Hide/Release. CAD COM phải thao tác đúng thread, tránh final release shared RCW. Sử dụng cách đóng native page trước khi thay đổi model theo convention của UI/NativeCommandPropertyManagerPage.cs.
Repo public: KHÔNG đẩy SLDPRT/STEP/SLDDRW/DXF/mẫu proprietary hay ảnh nội bộ; chỉ code và báo cáo có kiểm duyệt. Không merge main khi chưa có preview duyệt.
