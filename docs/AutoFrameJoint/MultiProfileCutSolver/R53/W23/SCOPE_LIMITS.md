# Phạm vi R53 và những phần chưa được xác minh

## Phạm vi quy tắc và writer

- Quy tắc nguồn R53 được giới hạn ở cặp trục **90°**, Web cùng mặt phẳng giao và có kiểm tra các skin vật lý của tôn. Đây là điều kiện chấp nhận hình học, không phải chứng nhận chạy được mọi Frame U.
- Frame U có thêm mép bẻ vẫn thuộc cùng họ tiết diện. Số nếp gấp không phải lý do đổi sang kiểu giao BUTT.
- Writer native có bằng chứng kiểm thử hiện tại chỉ được giới hạn ở **U đơn giản** trong bộ mẫu đã kiểm thử. Các Return bổ sung, nhiều nếp gấp và các EdgeFlange tạo thêm như W23 **chưa được xác nhận native Apply**.
- Đối chiếu đường Sketch W23 giúp chọn đúng Web và nhận ra vai bù chiều rộng. Nó chưa xác nhận thuật toán dựng Cutter hoặc Sketch/Cut mới đã tái tạo đúng toàn bộ mẫu đó.

## Không gộp số liệu khác loại

| Bộ bằng chứng | Số lượng | Ý nghĩa |
| --- | ---: | --- |
| W23 R53: Part có Feature Tree đọc thật | 3 | fr2, fr3, fr4. |
| W23 R53: Cut definition đọc thật | 23 | 20 active và 3 suppressed trong configuration đã đọc. |
| W23 R53: Cut có before/after B-rep | **0** | Không suppress/rollback hay dựng lại các trạng thái lịch sử. |
| W23 R53: native Apply hoặc kiểm thử trong Add-in | **0** | Chỉ kiểm kê ReadOnly trên bản sao. |
| Số 43 Cut / 42 before-after của báo cáo trước | Báo cáo lịch sử riêng | Không được cộng 23 Cut R53 vào số before-after; lượt R53 không kiểm định lại toàn bộ kết quả lịch sử. |

Ba Cut suppressed là fr2 **1483**, fr2 **1624**, fr4 **885**. Các định nghĩa này vẫn được kiểm kê API, nhưng không coi chúng là vật liệu đang được loại bỏ trong configuration hiện tại.

## Hai chính sách 0,10 mm độc lập

- `AssemblyFitGap = 0,10 mm`: khe hở lắp do người dùng quy định.
- `SheetCutOverrun = 0,10 mm`: lượng vượt chiều dày cho thao tác cắt xuyên một lớp tôn phù hợp. Nó không tự xác định khe hở lắp hay toàn bộ kích thước Cutter.

Trong mẫu W23, **8 Cut Blind active** có tham số hướng 1 bằng **t = 2,00 mm**. Chưa có before/after để kết luận cả 8 là Cut giao góc hay chỉ xuyên một lớp tôn. Không áp dụng `t+0,10` hàng loạt chỉ từ End Condition Blind. Giá trị 2,50 mm của fr4 Cut 885 thuộc Feature **suppressed**.

Hai cặp đường bậc của góc fr2×fr3 cách nhau 0,10 mm theo phép tính từ Sketch và pose. Đây không phải phép đo khoảng hở B-rep nhỏ nhất của toàn Assembly. Các đường xiên có thể cùng một đường chiếu nhưng nằm trên các support khác chiều sâu; không diễn giải thành xác nhận không giao thoa.

## Những kết luận chưa được phép ghi PASS

Chưa xác nhận KEEP/CUT cuối cùng, Bend R sạch hoàn toàn, native Apply W23, Preview hiển thị, độ mượt Preview hoặc luồng OK trong DLL đang dùng. Kiểm tra Body hiện tại `Check2=0`, khớp Cut Plan, biên dịch độc lập hoặc harness không thay thế được kiểm thử trong Add-in.

Không có Build/Deploy Add-in chính, thay DLL/PDB, Merge PR, Save/Rebuild CAD hoặc Pack and Go trong các lượt kiểm kê W23 được dẫn ở đây.
