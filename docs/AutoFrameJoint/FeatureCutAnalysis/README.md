> **R60 — 2026-10-28:** Sửa đường dẫn crash `ActivateDoc3`: Cut trong đúng Component edit, Apply sau native idle. Đã qua lại 4 góc 2354 và native PMP thật của cặp 4332 đảo A/B, có DLL/TaskPane hiện tại làm nền. Hai class đã chép vào source chính; người dùng tự rebuild. Xem [kết quả và phạm vi thực tế](R60/README.md).

> **R59 — 2026-10-28:** 2354 đủ 4 góc tạo 33 Cut thật khớp Preview; 4332 thêm 8 Cut và giữ 4 vùng không giao. Tất cả Normal/Blind=t+0,10 mm, không Plane mới. Đây là API harness từ source mới, chưa phải DLL/PMP đã rebuild. Xem [kết quả và giới hạn](R59/README.md); giữ nguyên kiểm kê 43 Cut mẫu / 42 before-after lịch sử.

> **R58 — 2026-10-26:** API đã chạy qua đúng DLL đang cài và luồng PMP/OK ở đủ 4 góc trên cùng bản sao Assembly: **16 Cut, 16 Sketch 2D ràng buộc đầy đủ trên Face2 có sẵn, 0 Plane mới**, khe hở 0,10 mm, giao thoa 0. Chỉ đạt trong phép thử mở CAD trước rồi nạp Add-in; **crash khi nạp Add-in trước CAD chưa được sửa**. Xem [bằng chứng và giới hạn](R58/README.md), [JSON từng góc](R58/FOUR_CORNERS_API.json) và [commit báo cáo PR #45](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/commit/ec6d528bc87343c1491332874204b8d70d5bd50d). 16 Cut sinh khi thử được tách khỏi 43 Cut mẫu / 42 before-after; không đổi dữ liệu mẫu hoặc source Add-in trong branch tài liệu này.

> **R54 — sửa lỗi đóng trang A-only:** đã cập nhật trực tiếp class SelectionPage, xem [bản sửa và kiểm tra callback](R54/README.md). Người dùng tự Rebuild; chưa xác nhận native PMP sau Rebuild.

> **R53 — 2026-10-23:** source chính đã tích hợp writer Sketch gắn Face, không tạo Plane mới; hai ca native đã chạy với đúng source. Xem [báo cáo và giới hạn R53](R53/README.md). Các kết quả lịch sử dưới đây được giữ nguyên.

# Auto Frame Joint — Native Feature Cut Analysis

> **R51 2026-10-22:** Đã kiểm kê lại 42 Cut, xác minh NormalCut/depth và nghiên cứu ba Cut bị suppress của 2354. Cặp Debug 2354 có Preview API đạt kiểm tra hình học theo quy tắc Frame ngắn che; 1618 vẫn UNVERIFIED. Xem [bổ sung R51](12_R51_FEATURE_CUT_AUDIT/README.md) và [Draft PR #42](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/pull/42). Nội dung các lần trích xuất trước được giữ để truy vết.

> **Cập nhật nghiên cứu 2026-10-22:** Đã có 42 Cut before/after, 28 replay oracle và quy tắc tham số U đối chiếu 8 góc T1/T2. Các nhận định “chưa có before/after/mapping” bên dưới là trạng thái lịch sử của lần trích xuất API đầu tiên. Xem [nghiên cứu mới](11_PARAMETRIC_RULE_RESEARCH/README.md), [matrix](11_PARAMETRIC_RULE_RESEARCH/TEST_MATRIX.csv) và [giới hạn](11_PARAMETRIC_RULE_RESEARCH/05_TEST_RESULTS_AND_LIMITATIONS.md). Dữ liệu API đã xác minh trước được giữ nguyên; source solver nằm ở branch riêng.

Đã kết nối phiên SOLIDWORKS hiện có qua COM ngoài sandbox và đọc thành công **15/15 Part**, **503 mục top-level**, **43 Cut-Extrude thật**. Trong 43 Cut có 28 `Cut` và 15 `ICE` có legacy type `Cut`; không dùng tên hiển thị để đoán loại lệnh. Không có Part thiếu Feature Tree trong 15 nguồn này.

| Chỉ số | Kết quả |
|---|---:|
| Part mở Read-Only / có Feature Tree | 15 / 15 |
| Cut được xác minh qua API | 43 |
| Bộ scalar + Sketch hình học đã đọc | 43 |
| Cut có selection access trên bản sao | 43 |
| Up To Vertex có tọa độ reference | 3 / 3 |
| Toàn bộ tham số hoàn chỉnh theo tiêu chí nghiêm ngặt | 0 — xem giới hạn hướng/contour dưới đây |
| Cut chưa xác minh sự tồn tại | 0 trong 15 Part |

Lỗi truy cập COM trước đó xuất phát từ ngữ cảnh sandbox: cùng máy có tiến trình SOLIDWORKS và ROT truy cập được khi chạy ngoài sandbox. Nhận định trước về khác phiên đăng nhập là giả thuyết đã được thay thế. Log cũ được giữ để truy vết.

Các Cut dùng 17 Blind, 22 Through All Both, 3 Up To Vertex và 1 Through All. Số depth trả về cho Through All là giá trị lưu thô, không được dùng làm giới hạn cắt hữu hạn. Hai phía Frame đều có lịch sử Cut; chưa có drawing/Assembly mapping để gán thắng/thua cho từng góc. Một số Sketch đầu thanh gãy khúc, một Cut fr1 nằm trên mặt nghiêng riêng.

**Giới hạn còn lại:** `GetDirectionReference` trả -1/null ngay cả khi AccessSelections trên bản sao; vector mặc định suy ra từ sketch normal và quy ước API được đánh dấu `INFERRED`. `Contours` không trả lựa chọn riêng. Vì vậy 43 bộ scalar/Sketch được xác minh không đồng nghĩa 43 định nghĩa đã đủ để replay tuyệt đối. Chưa dựng chênh B-rep trước/sau hoặc mask Web/Wall/Lip/Bend; không gọi suppress/rebuild/save. Bốn Part T1 được đọc thêm folded `Default` vì trạng thái lưu là flat. `4114` đổi dirty flag trong bộ nhớ khi đọc, đã đóng không lưu và hash file không đổi.

## Đọc báo cáo

- [01 — Inventory và bằng chứng bảo toàn nguồn](01_INPUT_INVENTORY.md)
- [02 — Tree theo thứ tự API, gồm subfeatures](02_FEATURE_TREE_REPORT.md)
- [03 — Chi tiết từng Cut, Sketch, End Condition, scope](03_CUT_FEATURE_DETAILS.md)
- [04 — Web, Wall, Lip, Bend và geometry](04_FRAME_GEOMETRY_ANALYSIS.md)
- [05 — So sánh quy tắc và phạm vi KCC](05_JOINT_RULE_COMPARISON.md)
- [06 — Native metrics đối chiếu STEP](06_STEP_VS_SLDPRT.md)
- [07 — Các điểm còn UNVERIFIED](07_UNVERIFIED_FINDINGS.md)
- [08 — Khuyến nghị Code Owner](08_SOLVER_RECOMMENDATIONS.md)
- [09 — Chẩn đoán cũ và đính chính](09_API_ACCESS_DIAGNOSTICS.md)
- [10 — Phương pháp STA, bản sao và nguồn API](10_API_METHOD_AND_PROVENANCE.md)
- [CSV Cut](FEATURE_CUT_INVENTORY.csv), [JSON Cut](FEATURE_CUT_INVENTORY.json), [CSV mọi Feature](AFJ_FEATURE_INVENTORY.csv), [Case matrix](AFJ_CASE_MATRIX.csv), [Summary JSON](API_EXTRACTION_SUMMARY.json)

`API_VERIFIED` là đọc thật từ native API. `GEOMETRY_VERIFIED` là metrics B-rep cuối, không tự động chứng minh vùng bị bỏ. `INFERRED` là phân loại/logic chưa có before-after hoặc Assembly proof. Dữ liệu STEP có sẵn giữ provenance cũ và snapshot trong `history/`. Chỉ xuất tài liệu và dữ liệu trích xuất cần thiết; không chứa CAD gốc, DLL, source ZIP hoặc đường dẫn cá nhân.

REPORT_MANIFEST.csv ghi SHA256 của nội dung Git chuẩn hóa newline LF; gói ZIP báo cáo dùng cùng nội dung chuẩn hóa để kiểm tra nhất quán.


## Follow-up: 2026-10-22 folded profiles and geometric Sketch relations

The original 15-Part / 43-Cut inventory and 42-Cut before/after evidence above are retained unchanged.

See [the follow-up report](Followup20261022/README.md) for unequal-width 75/65 mm and 55/70 mm native tests, multiple bends, direct Coincident/Tangent end-Face Sketches, measured exposure, Preview cache timings, and all failed/unverified cases. The source changes are on `feat/afj-multi-profile-cut-preview-20261022`, source commit `90876693d34fbaf6b13f0570b548ba7f6b946547`. This documentation branch contains no Add-in production source changes.

The main Add-in was not built/deployed. Installed UI visibility/smoothness, the legacy U Sketch rewrite and complete cross-Frame associative Cut transactions remain unverified or incomplete as explicitly recorded in that report.

## Post-load DLL and legacy U audit (2026-10-22)

[The post-load audit](PostLoadAudit20261022/README.md) confirms that the user manually built and loaded the newer AFJ DLL (MVID `420dfcfc-a6ee-4861-ae82-f54d916e32e4`). The fr03/fr04 run selected the legacy U writer: four actual Cuts matched the target bodies, but four new planes and four unconstrained Sketches violate the requested modeling intent. Copy activation/rebuild and saved-copy Read-Only reopen completed without reproducing the reported hang; its installed cause remains unverified. Separate face/start-offset and direct seam-relation experiments are reported as bounded research, not an enabled U repair. Earlier binary/source snapshots above remain historical evidence.
