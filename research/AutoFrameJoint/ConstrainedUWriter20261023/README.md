# Constrained U writer — review candidate, 2026-10-23

**Chưa phải bản sửa đã bật trong Add-in. Không chép đè class đang dùng hoặc bật define harness trong Add-in.** Giao source ngay để Code Owner kiểm tra; lần giao này không chạy thêm test native.

## Kết quả thực tế và giới hạn

Trên bản sao cặp 2354 fr03/fr04, lượt roundtrip riêng đã tạo 4 Cut và 4 Sketch fully constrained: **80 quan hệ hình học, 2 tham số chung, 0 Plane mới**. Sketch gắn Web Face có sẵn, liên kết trực tiếp Frame/Sketch bằng Coincident, Parallel, Perpendicular, Equal và quan hệ điểm. Không Convert, FIX hay dimension tọa độ từng điểm.

Kết quả đó chứng minh biểu diễn hình học của đúng mẫu đã chạy. Nó không chứng minh transaction tích hợp hoặc hoàn tác. Lượt mới chỉ tạo 2 Cut rồi gặp `0x80010105` tại `FindById/GetFirstSubFeature`; journal gặp lỗi khi đọc `GetTypeName2` trước khi xóa. **Rollback chưa đạt, không bật Apply mới.** Biên dạng có Return hoặc nhánh không đúng tám đỉnh vẫn chưa được writer này hỗ trợ. Chưa chứng minh sửa được chậm/treo trong Add-in.

## Source để đọc

- `AutoFrameJointGhostPreview.candidate.cs`: candidate tự chứa ba helper, dựa trên baseline local đã kiểm kê. Không nằm trong Compile items của project chính.
- Ba file `*.cs.txt`: helper riêng để đọc/diff; không biên dịch đồng thời với candidate đã nhúng helper.
- `INTEGRATION_ROUTE.patch.txt`: chỉ thay đổi route so với baseline local đã ghi SHA trong manifest; chưa áp dụng vào source chính.
- `CANDIDATE_MANIFEST.json`, `COMPILE_RESULT.json`: hash, phạm vi biên dịch và các gate còn thiếu.

Tên thuộc tính `private_document_path` và `private_persistent_token_base64` xuất hiện trong **mã serializer cục bộ**, không phải payload đã trích xuất. Package không chứa giá trị đường dẫn riêng tư, token, CAD, Interop DLL hay output binary. Không đưa file `.private.json` do helper tạo ra lên GitHub.

## Route được chuẩn bị

Giữ các entrypoint SelectionPage → CommandEntry → SessionManager → GhostPreview. Solver giữ lại construction intent của Preview; OK không chạy solver lần hai. Intent hiện được giữ **bằng tham chiếu mutable**, chưa phải snapshot bất biến. Transaction mới đăng ký Sketch/Cut vừa tạo vào một journal chung trước các bước tiếp theo, kiểm tra target Boolean từng bước và chỉ Commit sau kiểm tra cuối.

Gate `ConstrainedURouteQualified` mặc định **false**. Nếu áp dụng nguyên candidate vào class chính, OK của hai U rule sẽ trả `UNVERIFIED_APPLY`; không fallback sang writer Sketch tự do/Plane. Điều này **sẽ đổi hành vi Apply hiện có**, nên package hiện chỉ nằm trong thư mục research và không thay source đang chạy. Nhánh folded hiện có được giữ lại, không tuyên bố đã kiểm thử lại.

## Hướng dẫn cho Code Owner

1. Đối chiếu SHA baseline và diff class local. Baseline của candidate có thể khác HEAD PR; không thay cả class từ candidate.
2. Đọc lỗi rollback và sự cố source hash trong báo cáo liên kết bên dưới trước khi đưa route vào Add-in.
3. Kiểm tra biên dịch độc lập đã có: C#7.3/x64, gate OFF và harness define ON đều 0 lỗi, 8 warning chưa dùng biến/field. Đây không phải Build Add-in hoặc kiểm thử `TryApply` đầy đủ.
4. Không sửa `.csproj`, output path hay deployment để thử package này. Chưa có hướng dẫn Build/Deploy production vì gate hoàn tác chưa đạt.
5. Cần giải quyết vòng đời COM/traversal và kiểm thử journal trên context hợp lệ trước khi mở gate. Các vòng kiểm tra đệ quy lặp lại hiện còn tốn COM; chưa có số đo cải thiện độ mượt.

`AssemblyFitGap = 0.10 mm` và `SheetCutOverrun = 0.10 mm` độc lập. Shared ToolClosureMargin chỉ đóng cutter ngoài vùng giữ lại. Không gán `t+0.10mm` cho mọi Cut qua toàn biên dạng.

[Checkpoint và lỗi mới](../../../docs/AutoFrameJoint/MultiProfileCutSolver/PostLoadAudit20261022/FINAL_DELIVERY20261023.md), [4 Cut đã xác minh](../../../docs/AutoFrameJoint/MultiProfileCutSolver/PostLoadAudit20261022/U_FOUR_CUT_ROUNDTRIP.md), [sự cố Assembly nguồn](../../../docs/AutoFrameJoint/MultiProfileCutSolver/PostLoadAudit20261022/SOURCE_HASH_INCIDENT20261023.md).
