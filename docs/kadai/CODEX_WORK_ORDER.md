# PROMPT CHÍNH THỨC CHO CODEX — KADAI (copy vào Codex)

Bạn là kỹ sư SolidWorks 2024 SP5.0 và lập trình viên Add-in C# SOLIDWORKS API. Hãy **làm việc trực tiếp** trong repo hiện có, và **mở native SLDPRT thật** để tự đánh giá Feature Tree, không dựa duy nhất vào STEP và mô tả ChatGPT.

Repo: https://github.com/tbcphutai-bit/ADDIN-SolidWorks
Branch Kadai đã được tạo và push: feature/kadai-parametric-panel-ribs-20261117
Thư mục các model gốc trên máy Windows:
C:\Users\SGN26\Desktop\4315\4315

### A. Bắt đầu an toàn
1. git fetch, kiểm tra status/branch/commit; local repo có thể khác main trên GitHub. KHÔNG git reset --hard, không ghi đè uncommitted work. Checkout hoặc tạo local branch tracking branch Kadai; backup thay đổi trước khi rebase/merge nếu có xung đột.
2. Đọc các file docs/kadai/README.md, KADAI_IMPLEMENTATION_PLAN.md và code trong Commands/Kadai/*, UI/*Kadai*.cs. Xác nhận P0 chỉ có bố trí số học/preview sơ đồ 2D và Apply 3D bị khóa. Xem lại mã 4 file integration trước khi build.
3. Build Debug x64, Release x64 theo ADDIN.csproj (.NET 4.8 / C# 7.3) với SolidWorks 2024 Interop. Sửa các lỗi tích hợp Kadai trước, không đổi logic lệnh khác. Chạy numeric tests.
4. Không đưa private CAD/screenshot lên public GitHub. Toàn bộ thao tác CAD thực hiện trên bản sao trong thư mục LOCAL mới, ví dụ C:\Users\SGN26\Desktop\4315\KADAI_OUTPUT_TEST.

### B. Phải MỞ file gốc trong SolidWorks và ĐỌC Feature thật
Tìm và mở 4315-02-KDA1.SLDPRT trong SolidWorks 2024. Duyệt thư mục để xác định có KF3.SLDPRT, panel source, SLDASM, SLDDRW và các file liên kết khác không; nếu thiếu phải báo thiếu, không tự nhận là đã mở.
1. Liệt kê toàn bộ Feature Tree (kể cả subfeatures): tên, GetTypeName2, suppression/config, Parent/Child, body membership, sketch, dimension, reference, original feature order.
2. Đọc các Feature Sheet Metal: thickness, material theo configuration/custom properties, bend radius/K-factor/bend allowance/bend table nếu có, Flat Pattern và tính hợp lệ. So sánh với trị hình học STEP (~1.6 mm rib, ~2.3 mm base).
3. Đọc và kiểm tra đúng các **Cut-Extrude** trên mẫu: sketch profile, width/depth, cut side/direction, Normal Cut, Feature Scope, chọn ra ít nhất 2 vị trí/rãnh đại diện ở MỖI phương. Kiểm tra rãnh đối ứng và việc lắp thanh. So sánh 1.8 mm và 1.6 mm từ STEP, tính total geometric clearance riêng từng phương; nhận xét nếu có rãnh zero clearance.
4. Chọn/khảo sát mặt panel SOURCE. Chưa chắc STEP chứa panel master, nên nếu không có phải minh bạch và thiết kế UI để người dùng chọn đúng Face/Surface, local datum, footprint. Không suy diễn một mặt cong từ các thanh đã bị cắt.
5. Kiểm tra các Part/Body có external references, feature patterns, split/save bodies, drawing relink hoặc imported features gì không. Báo chính xác các điều kiện ràng buộc.
6. Lưu báo cáo kỹ thuật + screenshot Feature Tree, PropertyManager Sheet Metal và Cut trong output LOCAL. Công bố trong repo chỉ tóm tắt số liệu không nhạy cảm đã được người dùng đồng ý.

### C. Phản biện thuật toán ChatGPT trước khi code
Bắt buộc so sánh có lập luận:
(a) master multibody Sheet Metal Part → Save Bodies/derived parts,
(b) master reference Part + từng rib Part native/Assembly,
(c) face intersection + profile sketch/native Sheet Metal + Cut-Extrude,
(d) alternative Surface Cut/Replace Face nếu phù hợp.
Phân tích việc giữ reference 3D và Feature khi tăng/giảm N/M và khi panel thay đổi; đảm bảo tạo bản vẽ 2D và Flat Pattern. Ưu tiên giải pháp dễ gia công và kiểm soát hơn giải pháp mô hình phức tạp nhưng không trải phẳng được.

### D. TRIỂN KHAI TỪNG MỐC CÓ TEST THẬT
P1 — audit xong, build sạch, cập nhật báo cáo.
P2 — cho chọn panel Face/Surface thật, chọn local axes/datum, tính các mặt phẳng thanh và giao tuyến với bề mặt panel, tạo Closed Sketch hợp lệ; dựng 3 thanh dọc + 1 thanh ngang dạng Preview **temporary-body 3D thật**, Cancel không làm dirty source và giải phóng COM.
P3 — thực sự tạo 1 native Sheet Metal Part, sau đó 3+1; dùng **Part-level Extruded Cut/Cut-Extrude** hoặc Normal Cut phù hợp, tạo rãnh hai hướng với clearance đúng dựa trên cut gốc, đo width/depth tại Sketch và kiểm tra hướng lắp / residual geometry / FlatPattern. Thử mở Part đó độc lập.
P4 — mở rộng toàn bộ N×M, test KDA1 7x4 và KF3 5x2, rồi thay 7x4 → 10x6 → 7x4. Đồng bộ stable rib IDs, reference/rebuild, preserve user-added features nếu còn hợp lệ. Nếu phải xóa/recreate phải có preview-diff và xác nhận.
P5 — xuất từng Sheet Metal Part, Assembly, BOM, SLDDRW và DXF Flat Pattern 1:1; validate orientation, đơn vị, lỗ/rãnh, dimension, không overwrite model gốc.
P6 — UI native theo style Macro/PropertyManager, ổn định COM/HWND, thử đóng mở >30 lần, build cả x64 Debug/Release, smoke test AutoSpline, Mirror, Check Hole, Check Same Part, Drawing BOM, V-Cut, DXF.

### E. Điều cấm / điều kiện STOP
- Không tác động file gốc, không Save trực tiếp, không ghi đè sản phẩm trước khi người dùng xác nhận.
- Không tạo Fake "3D preview" dựa trên line 2D, không thông báo Feature Cut đã có khi chỉ đặt contract/interface.
- Không coi STEP là Feature Tree. Không coi "không giao thể tích" là đủ lắp.
- Không hardcode khe hở 0.2 mm như quy chuẩn công ty: chỉ là trị đo minh họa, phải kiểm chứng Native Model và người dùng có quyền chỉnh.
- Không dùng Imported Bodies thay cho native editable Sheet Metal làm deliverable sản xuất.
- Không force push, không merge main. Chỉ commit/push branch Kadai khi code đã build/test đúng. Nếu thiếu panel master hoặc dữ liệu sản xuất, báo STOP với bằng chứng và để Apply disabled.
- Không chỉnh sửa các command hiện có ngoài các điểm nút Kadai đã được tích hợp. Tuân thủ nguyên tắc COM chỉ chạy trên thread SolidWorks UI; tránh lỗi heap corruption trước đây.

### F. Đầu ra bắt buộc
1. Báo cáo KADAI_IMPLEMENTATION_REPORT.md theo docs/kadai/CODEX_REPORT_TEMPLATE.md.
2. Audit Feature Tree thật từ các SLDPRT với tên và thông số chính xác.
3. So sánh hướng triển khai, quyết định giải pháp cuối cùng và lý do.
4. Mã hoàn chỉnh theo scope đã test, danh sách file đổi, SHA commit, link branch/PR nếu có.
5. Preview chụp thực tế trong SolidWorks cùng đúng tính chất Sheet Metal, Cut-Extrude và Flat Pattern.
6. Test matrix: P0 math, SW build, 3D preview/cancel, cut clearance, flat, DXF, Drawings, 7x4/5x2/10x6, old commands, COM lifecycle.
7. Liệt kê điều gì chưa hoàn thành; đừng hứa đã xong phần chưa kiểm thử.

Hãy tự nghiên cứu, đánh giá phản biện và triển khai thay vì chỉ trả một đề xuất trên giấy. Không hỏi lại những thông số đã có trong file; đọc chúng qua SolidWorks API. Khi file gốc thực sự không chứa một thông tin thiết yếu, ghi lại rõ và tạo lựa chọn user-input thay vì tự đoán.
