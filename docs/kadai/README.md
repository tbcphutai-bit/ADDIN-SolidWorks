# Kadai — Grasshopper-like Sheet Metal Rib Grid in SolidWorks 2024

## Mục tiêu xác nhận với chủ dự án
**Kadai** là một nút MỚI tại **TAI TOOL > Model > Macro** trong Add-in hiện có, KHÔNG phải chương trình riêng. Người dùng chọn panel 3D làm tham chiếu, điều chỉnh **số thanh dọc và số thanh ngang độc lập**, khoảng cách/vị trí; Add-in tính lại **toàn bộ hình dạng từng thanh** theo panel, rãnh lắp ghép có độ hở, và tạo **các chi tiết Sheet Metal có Feature Tree native**, đặc biệt Cut-Extrude ở cấp Part, Flat Pattern, bản vẽ 2D và DXF.

Không chỉ cắt mép trên, không Pattern một thanh giống nhau, không chỉ xuất Imported Body. Ưu tiên bảo vệ các lệnh Add-in hiện có.

Repository: https://github.com/tbcphutai-bit/ADDIN-SolidWorks
Branch: feature/kadai-parametric-panel-ribs-20261117
Thư mục CAD gốc trên máy người dùng:
C:\Users\SGN26\Desktop\4315\4315

## Đường dẫn/tên file tham khảo
- 4315-02-KDA1.SLDPRT: bản Part native, cần kiểm tra Feature Tree/Sheet Metal/Cut-Extrude bằng SolidWorks 2024.
- 4315-02-KDA1.STEP: geometry only.
- 4315-02-KF3.STEP: geometry only.
- Tìm thêm trong thư mục các file KF3.SLDPRT, SLDASM, panel source SLDPRT, SLDDRW, DXF nếu có. Chưa thể khẳng định chúng tồn tại.
- Video gốc người dùng gửi để tham khảo nguyên lý: https://www.youtube.com/watch?v=mYUQv9WZ2f0 (không khẳng định đã xác minh nội dung hình ảnh video).

**Không đưa file CAD/ảnh/tài liệu vật liệu nội bộ lên GitHub PUBLIC.** Chỉ commit code, danh mục và kết quả kiểm tra không nhạy cảm. Tất cả bản sao CAD output thử nghiệm nằm trong thư mục riêng trên máy người dùng.

## Thông tin đã đo từ STEP, chưa phải Feature native
- KDA1: 13 solid; phân loại hình học gồm 7 thanh một phương, 4 thanh vuông góc, 2 tấm nền; 28 vị trí giao. Thanh dạng tấm dày 1.6 mm, nền dày 2.3 mm. Một số rãnh mẫu rộng 1.8 mm.
- KF3: 10 solid; khoảng 5 thanh + 2 thanh ngang, 1 tấm nền, 2 dải cong khác; 10 vị trí giao. Một số rãnh mẫu rộng 1.8 mm; ở phương kia có rãnh 1.6 mm.
- Rãnh rộng 1.8 với thanh dày 1.6 tương đương 0.2 mm độ hở **tổng theo bề rộng được đo**, không chứng minh là dung sai tiêu chuẩn công ty. Rãnh 1.6 tương đương 0 độ hở danh nghĩa => cảnh báo.
- Kiểm tra không chồng thể tích không có nghĩa chắc chắn lắp được; cần hướng đưa thanh và độ hở thực.
- STEP không cung cấp lịch sử Feature native, vật liệu/K-factor/bend table, External References, tên Cut hay mặt panel gốc. Cần Codex mở SLDPRT thật.

## Cấu trúc code P0 trong nhánh
- Commands/Kadai/KadaiPlanner.cs: tính toán bố trí NxM và công thức chiều rộng rãnh mẫu cho giao vuông góc, đơn vị mm.
- Commands/Kadai/KadaiCadContracts.cs: CAD backend interface chưa triển khai.
- UI/KadaiForm.cs: xem sơ đồ 2D tham số; **Apply 3D disabled**.
- UI/BomTaskPaneControl.Kadai.cs: launcher/lifecycle riêng.
- Bốn file tích hợp: BomTaskPaneControl.cs, UI/BomCommandLayout.cs, UI/BomTaskPaneTheme.cs, ADDIN.csproj.
- docs/kadai/KADAI_IMPLEMENTATION_PLAN.md: thiết kế tổng thể.
- docs/kadai/CODEX_WORK_ORDER.md: yêu cầu Codex mở SolidWorks gốc, đánh giá và thực hiện.
- docs/kadai/CODEX_REPORT_TEMPLATE.md: biểu mẫu báo cáo.
- tools/kadai_test_layout.py: kiểm tra số học độc lập; không phải build C# hay kiểm tra CAD.

## Trạng thái thành thật
Đã tiếp tục bằng SolidWorks 2024 SP5.0: build Debug/Release x64, audit native, nút Model → Macro → Kadai và temporary-solid Preview theo face thật. Có laboratory prototype 3+1 với native Sheet Metal/Cut/Flat, Drawing, DXF, Assembly và CSV BOM. Writer prototype không được nối vào nút Apply.

**Apply/Export vẫn khóa.** P4 regeneration giữ Feature chưa triển khai; P6 chưa đạt ổn định do các lần native heap corruption trong audit. Preview hiện là polyline approximation trên một face trong Part, chưa phải production contour. Không merge main hoặc dùng prototype để gia công. Xem [báo cáo thực hiện](../../KADAI_IMPLEMENTATION_REPORT.md) để phân biệt PASS, prototype và phần chưa làm.

Code mới: `Commands/Kadai/KadaiPreviewBackend.cs`, `KadaiNativePrototype.cs`; công cụ audit/CAD validation và test C# trong `tools/Kadai*`. Chạy build cô lập bằng `tools/Test-Kadai.ps1 -OutputDirectory <NEW_LOCAL_FOLDER>`; script tắt production-copy post-build. CAD/ảnh/JSON nội bộ được giữ ngoài Git trong `kadai-local/`.
