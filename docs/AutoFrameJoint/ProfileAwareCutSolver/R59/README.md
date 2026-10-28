# R59 — Cut thật theo từng lớp tôn, khớp Preview

Ngày kiểm thử: 2026-10-28. Source GhostPreview SHA256: `036844470F9C779F1A0392EC0AB25ECF6D3BBAFA2B366645C460DD39064E9525`.

Đã chạy đủ bốn góc 2354 tuần tự trên **cùng một bản sao Assembly**, tạo 33 Cut-Extrude thật; kiểm tra thêm cặp nhiều nếp gấp 4332 tạo 8 Cut. Mỗi bước so sánh B-rep Cut thật với Body tạm của Preview; kiểm tra lại các Part và toàn bộ Sketch sau góc cuối. Đây là thử nghiệm API trực tiếp trong SOLIDWORKS bằng công cụ biên dịch độc lập từ sáu source hiện hành, **chưa phải kiểm thử lại DLL Add-in đang cài hoặc thao tác nút PMP**.

| Assembly | Cặp Frame | Cut thật | Sketch tích lũy sau góc | Kết quả API |
|---|---|---:|---:|---|
| 2354 | fr01 × fr04 | 7 | 11 | PASS |
| 2354 | fr04 × fr03 | 6 | 21 | PASS |
| 2354 | fr03 × fr02 | 10 | 35 | PASS |
| 2354 | fr02 × fr01 | 10 | 49 | PASS |
| 4332 | fr3a-4 × fr5-3 | 8 | 12 | PASS |

Trong 49 Sketch của 2354 có 33 Sketch dùng cho Cut và 16 Sketch guide có ràng buộc; không tính guide thành Cut. 4332 có 8 Sketch Cut và 4 guide. API xác nhận các Sketch ràng buộc đầy đủ, dangling=0, overdefining=0, không Fixed/Convert, support là Face2 có sẵn; không tạo RefPlane mới. Kiểm tra tích lũy bao gồm Sketch ở Part của các góc trước, kể cả Part không được chọn ở góc hiện tại.

## Phần code thay đổi

Chỉ sửa `Commands/AutoFrameJoint/AutoFrameJointGhostPreview.cs`. Giữ nguyên entry point `TryShowCutCandidate`, A/B selection, CommandEntry, SessionManager và SelectionPage của source chính. Không thêm command, không thay project hoặc output/deployment.

- `AfjPerFoldSheetPlanner.Apply`: phân tích chuỗi lớp tôn phẳng và nếp cong từ mặt/cạnh B-rep thật; dựng vùng tiếp xúc hữu hạn riêng cho từng lớp, đồng thời giữ các vùng không giao.
- `AfjSheetCutPolicy`: bắt buộc mỗi operation có đúng một lát cắt, Normal Cut và Blind theo chiều dày API + 0,10 mm. Writer từ chối plan chưa được xác minh; không quay về lệnh cắt sâu qua cả biên dạng.
- `AfjPerFoldSheetPlanner.CreateNormalFlatPreviewTools`: dự đoán phần Bend do Normal Cut bằng biên radial theo cylinder R/R+t thật, kết hợp phần phẳng; tránh coi Normal Cut là một prism thẳng đơn thuần.
- `AfjNativeUTransaction`: tạo Cut trên từng lớp theo đúng plan Preview; kiểm tra contour trước/sau FeatureCut4, tham số đọc lại và B-rep từng bước/final.
- `AfjUParametricSketchWriter`: Sketch trên Face2 của chi tiết, Coincident/H/V/Parallel/SameLength tới Sketch gốc và datum tôn đã đo. Điểm liên kết cao độ/chiều dày lấy từ biên dạng gốc còn tồn tại, thay cho đỉnh đầu Frame bị các Cut sau xóa mất.
- Journal ownership: ghi nhận đúng Feature được tạo và theo thứ tự tạo. Không xóa ép các Feature tôn gốc khi hoàn tác chưa chứng minh được.

## Quy ước và giới hạn hình học

`SheetCutOverrun = 0,10 mm` là phần tăng chiều sâu xuyên **một** lớp tôn. `AssemblyFitGap = 0,10 mm` là khoảng hở giữa hai Frame. Hai tham số có cùng giá trị nhưng khác vai trò. Ví dụ đo t=2,50 mm thì Cut sâu 2,60 mm; không cộng thêm khe hở lắp vào chiều sâu lần nữa.

Luôn bật Normal Cut; các lần tạo mới dùng OptimizeGeometry=false, có kiểm tra đọc lại. Thử nghiệm cho thấy OptimizeGeometry=true có thể làm trơn đường Web có bước nhỏ dù Sketch vẫn đúng; không được đánh đồng OptimizeGeometry với Normal Cut.

Frame ngắn được ưu tiên che. Bề ngang khác nhau được xử lý theo giao thực của từng cặp mặt tôn/nếp gấp; không ép mọi lớp theo một đường 45 độ hoặc một cutter xuyên hết. Vùng không giao được giữ bằng mask hữu hạn và kiểm tra thể tích mất. Ở 4332, cả bốn vùng KEEP đo được đều có thể tích bị loại bỏ bằng 0. Các phần mép chưa che hết hoặc chưa đo được vẫn phải được báo riêng.

Nguồn ràng buộc hình học là mặt/Sketch gốc của Frame; không Convert hoặc Fixed để giả ràng buộc. Một số đoạn dựng dùng kích thước tham số có ý nghĩa: khe hở lắp, hoặc chiều dày tôn API khi hai skin cách nhau t. Không dùng kích thước để khóa tùy ý toàn bộ contour.

## Bằng chứng và phạm vi

- `API_VERIFIED`: Feature Cut được tạo thật, NormalCut/end condition/depth/thickness đọc lại, Sketch relations và support được kiểm tra bằng API.
- `GEOMETRY_VERIFIED`: symmetric difference, body health, gap/overlap và vật liệu KEEP được đo bằng B-rep. Tolerance symmetric difference là 1e-12 m³; không nới tolerance để đạt PASS.
- `INFERRED`/`UNVERIFIED`: sự ổn định với mọi loại Frame, hình học không thuộc miền rule hiện tại, flatten/unflatten, Preview nhìn thấy trong giao diện, và luồng PMP của DLL sau khi người dùng rebuild chưa được xác minh trong lượt này.

2354 sau cả bốn góc có khe hở 0,10 mm, giao thoa 0; lịch sử gốc còn nguyên. Nguồn CAD và bản sao trên đĩa giữ hash, không gọi Save/Rebuild/Pack and Go. Source chính đã được chép đúng hash và compile độc lập; main Add-in không được Build/Deploy, DLL/PDB không đổi.

**Hoàn tác khi lỗi còn UNVERIFIED:** các lần lỗi trước có derivative Sketch/FlatPattern hoặc context holder do SOLIDWORKS sinh tự động chưa thể xóa an toàn. Lượt thành công này không chứng minh rollback. Writer báo rõ khi rollback không được xác minh; không lưu Part trong trường hợp đó. Không tuyên bố bản này đã khắc phục mọi crash lịch sử.

Không chạy lại 1618/3615 trong lượt qualification này; giữ nguyên kết quả và giới hạn của các báo cáo lịch sử. 41 Cut mới tạo trong hai fixture không thay thế kiểm kê 43 Cut mẫu hoặc 42 Cut before/after trước đó.

Xem [kết quả 2354](2354/README.md), [kết quả 4332](4332/README.md), [cách rebuild thủ công](MANUAL_REBUILD.md).
