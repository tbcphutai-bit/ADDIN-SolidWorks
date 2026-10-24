# R53 — nghiên cứu mẫu W23: U có mép bẻ và khác bề rộng

Ngày 2026-10-23. Dữ liệu lấy bằng SOLIDWORKS API trên bản sao ReadOnly của mẫu người dùng cung cấp. **Đây là nghiên cứu metadata và đường Sketch; chưa phải xác nhận thuật toán cắt W23.**

## Dữ liệu thực đọc được

| Part | Cut | Đang hoạt động | Suppressed | t (mm) | Bend R (mm) |
| --- | ---: | ---: | ---: | ---: | ---: |
| fr2.SLDPRT | 12 | 10 | 2 | 2 | 0.1 |
| fr3.SLDPRT | 6 | 6 | 0 | 2 | 0.1 |
| fr4.SLDPRT | 5 | 4 | 1 | 2 | 0.1 |

- **23 Cut API_VERIFIED**, gồm **20 active / 3 suppressed**; không tính các Feature CurvePattern/LibraryFeature/Boss là Cut-Extrude riêng.
- End Condition hướng 1: **Blind 11, Through All 6, Up To Vertex 2, Through All Both 4**. Trong 20 Cut active: Blind 8, Through All 6, Up To Vertex 2, Through All Both 4.
- NormalCut bật trên **17/23**; FlipSideToCut bật **4/23**; ReverseDirection bật **3/23**.
- **23/23 Sketch Cut gắn Face2**; 22 fully constrained, một underconstrained: fr3 Cut 957 / Sketch 946.
- Ba Cut suppressed: fr2 **1483, 1624**, fr4 **885**. Cut 885 có Blind 2,5 mm nhưng **không đang cắt vật liệu trong configuration đã đọc**.
- Tám Blind active đều có tham số sâu **2,00 mm**, bằng t. Mẫu này không chứng minh mọi Blind dùng t+0,10 mm. Phân biệt `SheetCutOverrun=0,10 mm` và `AssemblyFitGap=0,10 mm`; chưa đo khoảng hở B-rep tổng thể trong nghiên cứu này.

## Mẫu khác bề rộng: vẫn có đường phân giác và bậc bù

Pose Assembly xác nhận fr2×fr3 có trục vuông góc. fr2 có hai strip có thể bị thuật toán cũ nhận nhầm là Web (65 và 230 mm). Strip **65 mm** có phương bề rộng nằm trong mặt phẳng hai trục Frame và song song trục fr3; strip 230 mm vuông góc mặt phẳng đó. Vì thế chọn Web 65 từ quan hệ cặp Frame, không chọn theo strip dài nhất.

Web tương ứng của fr3 là **39 mm**. Đường xiên fr2 Cut **586** / Sketch **574** và fr3 Cut **578** / Sketch **572** cùng đường 45° trong mặt phẳng giao; residual tính toán dưới **1,1e−12 mm** và đoạn chiếu trùng nhau **49,214632 mm**. Hai Sketch nằm trên các mặt cách nhau 35 mm theo chiều sâu; đây là đối chiếu đường chiếu, không phải khẳng định hai Sketch 3D trùng nhau.

Phần khác bề rộng được xử lý bằng đường gấp khúc: fr3 có vai **26,1 mm**, fr2 có vai tương ứng **25,9 mm**. Khoảng cách giữa hai đường vai là **0,1 mm**; một cặp cạnh bậc vuông góc khác cũng cách **0,1 mm**. Quan hệ số `26,1 = 65 − 39 + 0,1` được xác nhận từ tọa độ Sketch và pose đã đọc. **INFERRED:** đây là quy ước bù chiều rộng tại vùng giao; vùng KEEP/CUT cuối cùng vẫn cần before/after B-rep.

Kết quả này bác bỏ việc tự chuyển tiết diện U có mép bẻ sang quy tắc BUTT chỉ vì có thêm nếp gấp. Cần giữ đường giao xiên và xử lý riêng các đoạn nối/Wall/Lip theo section graph.

## Góc fr3×fr4 có thêm đường trim nghiêng

Hai trục Frame vẫn vuông góc, nhưng đường Cut tương ứng có góc chiếu **59,130328°**. Các đường trim fr3 Cut **957** và fr4 Cut **906** đồng phẳng với mặt phẳng local X=0 của component ft1 (sai số số học dưới **3,5e−13 mm**). **INFERRED:** liên hệ với biên ghép của ft1; chưa đọc Face2 của ft1 để xác nhận đó là mặt vật lý. Không lấy góc này làm quy tắc khác bề rộng chung.

## Mức bằng chứng và giới hạn

- `API_VERIFIED`: Feature/Cut definition, trạng thái suppressed, Sketch/relations/support, Body hiện tại và pose Component đã đọc thật.
- Chưa có Cut W23 nào `GEOMETRY_VERIFIED` bằng **before/after B-rep**. Body hiện tại có Check2=0 không chứng minh vùng cắt đúng thiết kế.
- `INFERRED`: vai bù chiều rộng, ý nghĩa chế tạo của từng Cut, quan hệ mặt vật lý ft1 và vùng vật liệu KEEP/CUT.
- **0 before/after Cut, 0 native Apply W23, 0 kiểm thử W23 trong Add-in, 0 xác nhận Preview nhìn thấy.** Không có CAD nguồn nào được sửa, Save, Rebuild hoặc Pack and Go trong các lượt đọc này.
- Các relation Distance/Angle được kiểm kê theo RelationManager; chưa kiểm kê toàn bộ DisplayDimension và chưa xác định owner của mọi entity được tham chiếu. Không ghi “0 dimension”.

## File báo cáo

- [FEATURE_CUT_INVENTORY.csv](FEATURE_CUT_INVENTORY.csv): 23 Cut và các thông số chính.
- [FEATURE_CUT_INVENTORY.json](FEATURE_CUT_INVENTORY.json): metadata chi tiết, thống kê, Sketch và phân loại có đánh dấu INFERRED.
- [JOINT_PRIMITIVE_CHECKS.csv](JOINT_PRIMITIVE_CHECKS.csv): phép đối chiếu hình học tương đối, không chứa tọa độ tuyệt đối.
- [PROVENANCE.json](PROVENANCE.json): nguồn bằng chứng, scope API và kiểm tra bảo toàn file.
- [SCOPE_LIMITS.md](SCOPE_LIMITS.md): phạm vi solver/writer, số liệu lịch sử riêng và các trường hợp chưa được phép ghi PASS.

Không kèm CAD, tọa độ tuyệt đối, ma trận transform, đường dẫn máy, persistence token, DLL hoặc PDB.
