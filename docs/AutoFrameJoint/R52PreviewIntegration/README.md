# R52 — Kiểm tra tích hợp Preview vào Add-in

Ngày kiểm tra: 2026-10-22, múi giờ Asia/Bangkok. Tiếp nối [R51 / PR #42](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/pull/42), [R50 / PR #41](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/pull/41).

**Cập nhật sau log native mới của người dùng:** R52 đã chạy trong Add-in, nhận A/B, bắt nút 107/108 và gọi solver/Display3 thành công ở mức trạng thái API. Sau lần P2 thứ hai, process AccessViolation tại `Label.Caption`. Bản follow-up bỏ runtime label setter, dùng vùng Thông báo của chính page. **Bản sửa Caption chưa được kiểm tra native và chưa xác minh Preview nhìn thấy.** Xem [bằng chứng mới và bản sửa](07_NATIVE_LOG_AND_CAPTION_FIX.md). Các snapshot đầu R52 được giữ riêng; trạng thái NOT_RUN ban đầu không còn mô tả lần chạy người dùng vừa cung cấp.

## Trả lời các câu hỏi bắt buộc

| Câu hỏi | Bằng chứng / kết luận |
| --- | --- |
| R51/R52 đã nằm trong DLL chưa? | **Có.** DLL/log mới có MVID `ae27a276-be1d-4959-8391-1b3d60f3b997`; 265/265 method AFJ khớp IL chuẩn hóa harness R52 trước sửa Caption. Snapshot đầu R52 là MVID khác và vẫn được giữ. |
| Có nhận Frame B không? | **Có trong log native mới:** submit B accepted và COM mark 2 count=1, resolved đúng fr03. |
| Có bắt nút Preview không? | **Có:** 107 một lần, 108 hai lần. |
| Có gọi solver không? | **Có:** ba `SOLVER_END`, mỗi lần keptA=1 / keptB=1, removed volume dương trên cả hai. |
| Có tạo Body2 thực không? | Log Add-in ghi temporary=True, kept và removed volumes. Đây là bằng chứng log native người dùng; không là snapshot B-rep mới do Codex trích xuất. |
| Display3 có chạy không? | **Có:** 14 call trả status=0; 8 Hide trước các lần thay Preview có END. Cleanup cuối sau crash chưa xác minh. |
| Preview có thể nhìn thấy không? | **UNVERIFIED.** Có viewport estimates nhưng chưa ảnh CAD hoặc xác nhận người dùng; một phần box nằm ngoài vùng view. Offset Z +120 mm giữ nguyên. |
| Kiểm thử trong Add-in hay harness? | R52 trước sửa Caption: **log Add-in thực do người dùng cung cấp**. Bản sửa Caption: explicit-source compile và **12/12 managed controls mỗi biến thể**, chưa native. |

DLL người dùng đã Build chứa R52 cũ; **bản sửa Caption mới chỉ nằm trong source**, DLL/PDB giữ nguyên tại bước áp dụng. Codex không Build/Deploy Add-in. Không nhầm bản vá mới với module đã load.

## Phạm vi sửa

- `AutoFrameJointSelectionPage`: chỉ kiểm tra kiểu/dispatch và trạng thái managed trong submit; resolve owner, đọc marks và xác minh occurrence sau callback. Log nhận/từ chối, focus, list, queue, ready, action, delegate, epoch và thread.
- `AutoFrameJointCommandEntry`: thêm log Open và ranh giới `onPreviewCut` → SessionManager. Không tạo command khác.
- `AutoFrameJointGhostSessionManager`: thêm log token/state/owner/Assembly gates và lời gọi Ghost. Không đổi session policy.
- `AutoFrameJointGhostPreview`: thêm log solver/display/redraw/Hide, transform và ước lượng viewport. Không đổi cutter, Boolean/validation, offset, màu hay cơ chế cleanup.

Follow-up từ ảnh/log AccessViolation chỉ sửa thêm `AutoFrameJointSelectionPage`: cache trạng thái managed, không ghi `Label.Caption`; publish kết quả/geometry bằng `page.SetMessage3` sau action, ngoài selection callback. Các label dựng trước Show giữ văn bản chỉ dẫn tĩnh. Xem giới hạn về nguyên nhân gốc trong báo cáo mới.

Bốn file đã được áp dụng bằng SHA guard vào source local, từng method; baseline local và branch có khác biệt được bảo toàn. `.csproj`, output path, pre/post-build event, DLL/PDB và các command khác không được sửa. Branch/HEAD tích hợp người dùng không đổi. Không CAD gốc hoặc binary được upload.

## Đọc báo cáo

1. [Source / DLL / PDB / output](01_SOURCE_DLL_VERIFICATION.md)
2. [Sơ đồ và toàn bộ luồng Preview](02_PREVIEW_FLOW.md)
3. [Frame B và các điểm log](03_SELECTION_DIAGNOSTICS.md)
4. [Display3, transform và viewport](04_DISPLAY_VISIBILITY.md)
5. [Test matrix và giới hạn](05_TESTS_AND_LIMITS.md), [CSV](TEST_MATRIX.csv), [JSON](TEST_MATRIX.json)
6. [Tích hợp thủ công và kiểm tra trong Add-in](06_MANUAL_INTEGRATION.md)
7. [Danh mục bằng chứng](evidence/README.md), [manifest](REPORT_MANIFEST.csv)
8. **[Log native mới và sửa Caption](07_NATIVE_LOG_AND_CAPTION_FIX.md)**, [matrix follow-up](CAPTION_FOLLOWUP_MATRIX.csv), [summary](CAPTION_FOLLOWUP_SUMMARY.json)

`BINARY_VERIFIED` / `SOURCE_VERIFIED` là đọc file/IL thực, `USER_LOG` là phần log được người dùng mô tả, `PASS_MANAGED_CONTRACT` là kiểm tra với đối tượng mô phỏng, `NOT_RUN` / `UNVERIFIED` giữ nguyên nghĩa. Không đổi kết quả harness thành PASS hiển thị thực.
