# R52 — Kiểm tra tích hợp Preview vào Add-in

Ngày kiểm tra: 2026-10-22, múi giờ Asia/Bangkok. Tiếp nối [R51 / PR #42](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/pull/42), [R50 / PR #41](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/pull/41).

**R51 có trong DLL người dùng đã Build. Chưa xác minh được Preview nhìn thấy trong Add-in.** R52 bổ sung chẩn đoán và bỏ truy vấn COM selection khỏi `OnSubmitSelection`; chưa Build/Deploy Add-in chính, chưa chạy lại lựa chọn bằng native PMP. Không tuyên bố đã sửa xong hiện tượng mất Preview.

## Trả lời các câu hỏi bắt buộc

| Câu hỏi | Bằng chứng / kết luận |
| --- | --- |
| R51 đã nằm trong DLL chưa? | **Có.** MVID `2f4e359e-bffe-4f74-9441-891bb4c77b12` khớp log. IL có `[AFJ-R51]`, solver và Display3. 251/251 method AFJ khớp IL chuẩn hóa của harness được biên dịch từ source local R51. |
| Có nhận Frame B không? | Log người dùng cung cấp kết thúc ở `A-count=1 B-count=0`; **chưa có bằng chứng nhận B trong lần chạy đó**. R52 chỉ đạt mô phỏng managed, chưa xác minh native commit. |
| Có bắt nút Preview không? | Không có `BUTTON id=107/108` trong log cung cấp. Managed controls gọi được hai callback sau khi post; **Add-in thực: NOT_RUN**. |
| Có gọi solver không? | DLL có đường gọi. Log lần chạy mới chưa tới `BACKEND BEGIN Preview` / `[AFJ-R51]`; **chưa có bằng chứng solver được gọi trong lần đó**. |
| Có tạo Body2 thực không? | **R52: NOT_RUN**. R51 đã tạo và đo Body2 qua API harness, giữ nguyên provenance cũ; không chuyển thành PASS cho R52. |
| Display3 có chạy không? | **R52/Add-in mới: chưa có bằng chứng**. R51 harness từng đạt trạng thái API; không chứng minh lần chạy mới đã hiển thị. |
| Preview có thể nhìn thấy không? | **UNVERIFIED.** Chưa đo viewport thực, chưa kiểm tra hình ảnh hoặc được người dùng xác nhận. Offset Z +120 mm giữ nguyên. |
| Kiểm thử trong Add-in hay harness? | Read-only DLL/IL/PDB/project/registry audit; biên dịch độc lập sáu class AFJ; **9/9 managed controls cho mỗi biến thể source**. Không native Add-in/PMP, solver, Body2 hay Display3 mới. |

Sau khi áp dụng source R52, DLL vẫn giữ MVID trên: **R51 có trong DLL; sửa đổi R52 chưa nằm trong DLL** vì nhiệm vụ không Build/Deploy. Không nhầm source mới với module đã được load.

## Phạm vi sửa

- `AutoFrameJointSelectionPage`: chỉ kiểm tra kiểu/dispatch và trạng thái managed trong submit; resolve owner, đọc marks và xác minh occurrence sau callback. Log nhận/từ chối, focus, list, queue, ready, action, delegate, epoch và thread.
- `AutoFrameJointCommandEntry`: thêm log Open và ranh giới `onPreviewCut` → SessionManager. Không tạo command khác.
- `AutoFrameJointGhostSessionManager`: thêm log token/state/owner/Assembly gates và lời gọi Ghost. Không đổi session policy.
- `AutoFrameJointGhostPreview`: thêm log solver/display/redraw/Hide, transform và ước lượng viewport. Không đổi cutter, Boolean/validation, offset, màu hay cơ chế cleanup.

Bốn file đã được áp dụng bằng SHA guard vào source local, từng method; baseline local và branch có khác biệt được bảo toàn. `.csproj`, output path, pre/post-build event, DLL/PDB và các command khác không được sửa. Branch/HEAD tích hợp người dùng không đổi. Không CAD gốc hoặc binary được upload.

## Đọc báo cáo

1. [Source / DLL / PDB / output](01_SOURCE_DLL_VERIFICATION.md)
2. [Sơ đồ và toàn bộ luồng Preview](02_PREVIEW_FLOW.md)
3. [Frame B và các điểm log](03_SELECTION_DIAGNOSTICS.md)
4. [Display3, transform và viewport](04_DISPLAY_VISIBILITY.md)
5. [Test matrix và giới hạn](05_TESTS_AND_LIMITS.md), [CSV](TEST_MATRIX.csv), [JSON](TEST_MATRIX.json)
6. [Tích hợp thủ công và kiểm tra trong Add-in](06_MANUAL_INTEGRATION.md)
7. [Danh mục bằng chứng](evidence/README.md), [manifest](REPORT_MANIFEST.csv)

`BINARY_VERIFIED` / `SOURCE_VERIFIED` là đọc file/IL thực, `USER_LOG` là phần log được người dùng mô tả, `PASS_MANAGED_CONTRACT` là kiểm tra với đối tượng mô phỏng, `NOT_RUN` / `UNVERIFIED` giữ nguyên nghĩa. Không đổi kết quả harness thành PASS hiển thị thực.
