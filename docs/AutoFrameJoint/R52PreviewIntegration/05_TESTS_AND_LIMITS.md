# Test matrix và giới hạn

## Đã thực hiện

- Đọc source local và project XML, MVID/IL DLL, PE CodeView/PDB MSF, COM registration và Windows events. Không thực thi DLL khi đọc IL.
- So 251 method AFJ với IL chuẩn hóa của R51 harness: 251 khớp. Snapshot source mới được so SHA riêng; không giả định native PDB source checksum đã có.
- Biên dịch x64 C# 7.3 với TRACE: harness độc lập chứa **sáu class AFJ hiện có**, cả biến thể local và branch. Chỉ explicit source/reference, không `.csproj`, MSBuild, main Add-in output hoặc post-build.
- Mỗi biến thể chạy chín managed controls: A→B focus nhưng B chưa tự selected; B commit giả lập và auto Analyze; nút 107; nút 108; reason từ chối; owner/duplicate deferred; foreign thread; rectangle viewport; drop queued Preview sau close/epoch.
- **9/9 `PASS_MANAGED_CONTRACT` cho mỗi biến thể.** Hai delegate Preview được gọi; không solver/body/Display3. Proxy ghi **0 method/property interface invocations trong selection callbacks**. Cast QueryInterface của dispatch vẫn được phép; test này không đo chi phí COM native.
- Bất biến source: R50 BuildControls/marks/filters/SingleEntityOnly, SetCaption và owner/epoch gates giữ nguyên; readiness/deep identity kiểm tra cũ giữ nguyên ngoài guard deferred. Mọi helper từ `AfjOperationKind` trở xuống của Ghost R51 giống nguyên văn baseline; không đổi Boolean, Cut Plan, rule hoặc validation.
- Áp dụng source local bằng before/after SHA: đúng bốn `.cs`; project/DLL/PDB và hai class math/probe không đổi; branch/HEAD tích hợp không đổi.

Xem [TEST_MATRIX.csv](TEST_MATRIX.csv), [JSON](TEST_MATRIX.json), log và provenance trong evidence. Log managed bắt đầu bằng `R52-MANAGED-SIMULATION`; không được dùng chúng làm log native.

## Chưa thực hiện

| Hạng mục | Trạng thái |
| --- | --- |
| Build/Deploy Add-in chính R52 | `NOT_RUN` theo giới hạn nhiệm vụ |
| Native PMP nhận Frame B trong DLL R52 | `NOT_RUN` |
| Nút Preview thực trong Add-in R52 | `NOT_RUN` |
| R52 solver tạo temporary Body2 thực | `NOT_RUN` |
| R52 Display3 / Hide thực | `NOT_RUN` |
| Hiệu chuẩn projection/viewport dưới PMP/HiDPI | `NOT_RUN` |
| Người dùng nhìn thấy hai Frame Preview | `UNVERIFIED` |
| Nguyên nhân chính xác B không commit / heap corruption | `UNVERIFIED` |

Người dùng chọn tiếp tục kiểm tra source/DLL trước. Khi phát hiện có PID giao diện mới sau đó, chỉ đọc module list của process; không chạy API selection/Preview trên phiên đó. Không lấy hidden/Embedding process làm phiên kiểm thử thay thế.

## Các kết quả cũ không thay đổi

R51 từng đo Body2/Boolean/gap/interference và Display3 trên bản sao 2354, trước-Cut 3615; hai cặp 1618 vẫn UNVERIFIED_RULE. Không chạy lại những API này trong R52 và không công bố một PASS mới dựa trên compile hoặc proxy. Cũng chưa giải thích hai residual native 2354 hoặc xác minh manufacturing intent; giới hạn PR #42 tiếp tục áp dụng.

Các báo cáo thành công chỉ chứng minh từng mức bằng chứng nêu rõ. Còn cần một lượt kiểm tra native sau Build thủ công để kết luận lỗi người dùng đã được giải quyết.
