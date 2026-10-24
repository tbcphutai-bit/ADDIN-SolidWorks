# R54 — sửa lỗi đóng lệnh khi mới chọn Frame A

Đã sửa trực tiếp `UI/AutoFrameJoint/AutoFrameJointSelectionPage.cs` trong source chính để người dùng tự Rebuild. Không đổi solver/Cut Plan, không Build/Deploy Add-in, không thay DLL/PDB và không tác động CAD.

## Lỗi xác minh từ log và source

DLL đã có R53, MVID `71279ae1-72e7-4f90-9a36-d8c03e0e0ddd`. Log có A-count1/B-count0; Submit+List tăng selectionRevision lên2, nhưng TryReadSelection chỉ gán committedSelectionRevision khi đủ cặp hợp lệ, nên giá trị vẫn0. OnClose hiểu sai2≠0 là công việc chưa xong, post finish rồi ném COMException HRESULT1. Chưa có callback Preview/solver/Apply trong lượt này. Không có B submit/list trong log; không kết luận người dùng đã thử click B hay chưa.

Exception đúng thông điệp đã được tái hiện bằng class UI cũ trong chương trình độc lập. Sau exception, log ghi SOLIDWORKS kết thúc0xffffffff; chưa đủ dữ liệu phân biệt debugger Stop với native crash.

## Thay đổi cụ thể

- `TryReadSelection`: tách revision của snapshot đã đọc ổn định khỏi revision cặp hợp lệ. A-only là trạng thái đã xử lý, không phải chờ backend.
- Submit/List chỉ vô hiệu readiness bằng biến managed; không thêm đọc COM hoặc native control trong selection callback.
- `OnClose`: chụp snapshot managed, đánh dấu closing, hủy epoch cũ, cho đóng bình thường; không còn nhánh ném close-veto exception.
- Chỉ chuyển yêu cầu Apply sang AfterClose khi OK, đủ A/B đã xác minh, revision hiện hành và backend rảnh. Backend vẫn kiểm tra ticket Preview thật trước khi tạo Cut.
- `AfterClose`: thiếu/stale/busy/Cancel thì bỏ Apply, vẫn cleanup/onClosed. Đóng lúc Preview bận dùng cơ chế PendingCleanup hiện có của SessionManager.
- Nút Finish riêng vẫn đọc/kiểm tra lựa chọn qua dispatcher trước khi gọi Close. Giữ nguyên R50 native UI protection và selection A/B.

OnClose có thể dùng HRESULT1 để chủ động veto theo [tài liệu SOLIDWORKS](https://help.solidworks.com/2016/english/api/swpublishedapi/SOLIDWORKS.Interop.swpublished~SOLIDWORKS.Interop.swpublished.IPropertyManagerPage2Handler9~OnClose.html). Đây không phải API bị cấm; lỗi ở điều kiện dùng veto cho A-only. R54 chọn cho teardown và chặn Apply không hợp lệ, không dùng exception để chờ Preview.

## Kiểm tra đã chạy

11 ca managed trên source local và11 ca trên baseline branch đều đạt: rỗng, A-only, pair stale/invalid, ba loại backend busy, queued action, pair hợp lệ, Cancel và callback lặp. Không delegate nào chạy trong OnClose; Apply chỉ chạy sau AfterClose cho ca đủ điều kiện; cleanup/onClosed đúng một lần; không gọi interface Component hoặc SOLIDWORKS API. Biên dịch sáu source AFJ độc lập đạt.

Đây là kiểm tra callback độc lập, **chưa phải kiểm thử native PMP trong DLL SOLIDWORKS**. Không chạy lại CAD hoặc cộng các ca managed này vào kết quả Feature Cut. Sau Rebuild cần kiểm tra trong log thực.

## Source và DLL

Baseline UI local và branch vốn khác caption/comment/cách xóa thông báo cũ; cùng bản sửa close được áp riêng, không chép đè toàn class từ branch vào source local. Hai biến thể được kiểm tra riêng; các hash trong SOURCE_PATCH_MANIFEST và MAIN_SOURCE_UPDATE. Solver R53 được giữ nguyên.

DLL/PDB giữ đúng bản người dùng vừa Build. R54 chỉ có trong DLL sau khi người dùng Rebuild. Marker khi mở lệnh:

```text
[AFJ-R54] CLOSE_POLICY=ALLOW_TEARDOWN_VALID_PAIR_ONLY MVID=...
```

Khi chỉ có A và bấm OK, dự kiến `CLOSE ... apply_eligible=False`, `APPLY_SKIPPED ... INCOMPLETE_OR_INVALID_PAIR`, sau đó cleanup; không còn thông điệp exception cũ. Khi thiếu B sẽ không tạo Cut.

## Bằng chứng

- [LOG_SUMMARY.json](LOG_SUMMARY.json), [LOG_EVIDENCE.md](LOG_EVIDENCE.md).
- [MANAGED_CLOSE_local.json](MANAGED_CLOSE_local.json), [MANAGED_CLOSE_git.json](MANAGED_CLOSE_git.json), [MANAGED_CLOSE_old.json](MANAGED_CLOSE_old.json).
- [COMPILATION.json](COMPILATION.json), [SOURCE_PATCH_MANIFEST.json](SOURCE_PATCH_MANIFEST.json), [MAIN_SOURCE_UPDATE.json](MAIN_SOURCE_UPDATE.json).

Không kèm log thô chứa đường dẫn máy, CAD, DLL/PDB hoặc persistence payload. Các giới hạn hình học và rollback Assembly của R53 vẫn giữ nguyên; R54 không tuyên bố đã sửa chúng.
