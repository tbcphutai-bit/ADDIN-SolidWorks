# Native Apply và giới hạn hoàn tác — R53, 2026-10-23

## Kết luận

`native_apply_14` đã chạy thành công **entrypoint thật `AfjNativeCutCommit.TryApply`** trên bản sao: 4 Cut, 4 Sketch gắn Face gốc, status fully constrained, 80 quan hệ hình học, 2 tham số chung, 0 Plane mới. Chênh lệch B-rep so với target Boolean của từng Cut và hai Body cuối bằng 0. `native_unequal_03` cũng Apply thành công trên bản sao có Frame ngắn B rộng 100 mm, Frame dài A rộng 70 mm: 4 Cut, profile chính B/A có 9/8 cạnh, 84 quan hệ, 2 tham số và 0 Plane mới; khe lắp đo được xấp xỉ 0,10 mm, overlap bằng 0.

**Đây là API harness gọi luồng Apply thật; chưa phải kiểm thử DLL Add-in đã nạp, Preview nhìn thấy trên màn hình, hoặc chứng minh đúng mọi quy tắc chế tạo.** Mẫu khác bề rộng được tạo bằng sửa tham số trên bản sao 2354 trong bộ nhớ; không được gọi là kiểm thử Apply trực tiếp Assembly 2216 của người dùng.

Hai lượt thành công không chạy nhánh rollback. Trường `RollbackVerified=false` mặc định cùng danh sách `Rollback=[]` trong báo cáo thành công nghĩa là **chưa chạy hoàn tác**, không phải hoàn tác thất bại.

## Lịch sử kiểm tra giữ nguyên kết quả

| Lượt | Bằng chứng thực tế | Hoàn tác |
|---|---|---|
| `native_rollback_10` | Chế độ FORCE_ROLLBACK nhưng guard gặp holder Assembly mới sau Cut đầu tiên; chưa tới điểm cố ý gây lỗi sau bốn Cut. | Hai Part được kiểm tra độc lập: B-rep khôi phục chính xác, Feature gốc/thứ tự giữ nguyên, không còn Feature mới. Xóa holder Assembly bằng option 0 trả false. **Part restore đạt trong lượt này; Assembly cleanup và global rollback UNVERIFIED.** |
| `native_apply_11` | Một Cut được xác minh; factory tiếp theo làm companion SheetMetal owned không còn xuất hiện trong inventory thường. | Guard dừng; journal gặp delta chưa giải thích khi xóa. Kiểm tra Body sau lỗi không đạt. **Không ghi Part restore PASS cho lượt này.** |
| `native_apply_12` | Companion không được liệt kê nhưng token vẫn resolve trạng thái 0; guard từ chối kết luận nó đã bị xóa. | Global rollback UNVERIFIED; kiểm tra Body sau lỗi không đạt. **Không ghi Part restore PASS.** |
| `native_apply_14` | Apply thành công 4 Cut/80 relations/2 parameters/0 Plane; Body khớp target và Assembly edit context được trả lại. | Không chạy rollback. |
| `native_unequal_02` | Không có Cut được xác minh; writer dừng vì không tìm được datum cạnh Web hữu hạn cho Sketch relief. | Kiểm tra độc lập xác nhận hai Part khôi phục B-rep/cây/thứ tự và không còn Feature mới. Assembly còn khác số Feature. **Global rollback UNVERIFIED.** |
| `native_unequal_03` | Apply thành công trường hợp B100/A70, 4 Cut/84 relations/2 parameters/0 Plane; gap và overlap đạt kiểm tra. | Không chạy rollback. |

Tất cả các lượt nêu trên ghi hash file nguồn và bản sao không đổi sau khi đóng không Save; phiên kiểm thử trở về trạng thái không có tài liệu mở. Điều này chỉ áp dụng cho các lượt được liệt kê, không xóa hoặc sửa kết luận của sự cố Pack and Go nguồn đã được báo trước đó.

## Quyền sở hữu Feature và metadata

- Sketch được đăng ký provisional ngay khi native trả Feature; identity Part được xác nhận lại sau khi đóng Sketch và thoát EditPart. Cut được đăng ký ngay sau factory và xác nhận identity riêng.
- Inventory dùng native top-level kết hợp flat Feature API, bổ sung parent TemplateSheetMetal thực của SheetMetal. Từng baseline ID/type/name/suppression và thứ tự top-level gốc vẫn được bảo vệ; không dùng duyệt subfeature đệ quy.
- Companion SheetMetal chỉ được nhận qua checkpoint của đúng một `FeatureCut4`, parent template có trong baseline, t/R khớp dữ liệu API gốc và token roundtrip chính xác. Companion là **passive automatic-only**: journal không Select hoặc Delete nó trực tiếp.
- Feature owned vẫn có thể tồn tại dù API inventory không liệt kê. Token đã xác minh được bổ sung chỉ cho companion passive đã biết, yêu cầu đúng native ID/type/name và template/t/R. Trong `native_apply_14`, log xác nhận token của companion 754 resolve đúng ID 754. Không được coi “không liệt kê” là “đã xóa”.
- Nhánh token trỏ sang ID mới chỉ được đối chiếu với companion mới đã xác minh của đúng factory; không có bằng chứng trong lượt Apply14 để coi nhánh thay thế đó đã được kiểm thử thành công.
- Holder Assembly chỉ được nhận khi có bằng chứng baseline MateGroup + Reference của đúng Component A/B, Sketch/Cut owned đã xác nhận và checkpoint tương ứng. Không nhận mọi delta Assembly theo tên hoặc loại Feature.

## Giới hạn còn mở

**Chưa có bằng chứng hoàn tác toàn bộ giao dịch đạt trên source cuối.** Lượt 10 và unequal02 chứng minh khôi phục Part trong phạm vi đã chạy, đồng thời cho thấy metadata Assembly chưa được dọn đầy đủ. Các lượt 11/12 còn không đạt kiểm tra Body sau lỗi. Không suy rộng kết quả Part restore của một lượt sang mọi lỗi hoặc source cuối.

Không dùng empty-Sketch lifecycle để dọn Assembly production: thao tác này có thể làm mất holder gốc. Bước chuẩn bị fixture cũ chỉ chạy trên bản sao, trước baseline giao dịch; source production không bật normalization. Baseline có holder gốc không xác minh được token phải dừng trước mutation.

Nếu Apply lỗi và báo `ROLLBACK_UNVERIFIED`, Code Owner cần phân biệt trạng thái hình học Part với metadata Assembly; không được ghi PASS hoặc tự Save vì chỉ thấy Part giống ban đầu. Kiểm thử DLL Add-in, mở Part sau Apply, hiệu năng Preview và các biên dạng U nhiều mép bẻ ngoài recipe đã chạy vẫn là hạng mục riêng.
