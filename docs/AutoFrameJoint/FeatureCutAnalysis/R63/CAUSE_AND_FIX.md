# Nguyên nhân và bản sửa

Log nạp DLL MVID `4b90299a-4e1c-4038-aab7-b276c9a469fb` và đã tạo Preview R62. Cặp mới là Frame dài fr1a và Frame ngắn fr2. Native Cut đầu đã trả về Feature 2032 trước khi guard ném lỗi vì Profile 2019 `a-1` biến mất. `cuts=0` chỉ là số dòng report đã xác minh; không chứng minh chưa có native mutation. Rollback trong log là `ROLLBACK_UNVERIFIED`.

API read-only trên bản sao xác nhận Profile 2019 và 2021 không bị suppress, token state 0; Profile 2018/2020 bị suppress, token state 2. Cả bốn đều là Sketch 2D dưới đúng `FoldUpDownSketchFolder` 688 bị suppress, sole parent `FlatPattern` 668 bị suppress, không có Feature con phụ thuộc và roundtrip đầy đủ token. Điều kiện cũ chỉ đăng ký Profile bị suppress nên bỏ sót hai Profile hệ thống hợp lệ.

R63 chọn bằng cấu trúc native trước mutation, rồi kiểm token khớp suppression thực: `True → state 2`, `False → state 0`. ID/type/name/suppression và đầy đủ token phải khớp. Tên chỉ được ghi lại làm bằng chứng, không dùng để cấp ngoại lệ. Invalidation chỉ được ghi nhận ngay sau đúng registered native Cut, khi token Profile cũ trả state 1/null và authority Flat Pattern/folder vẫn qua guard. Không cho phép xóa Profile gốc tùy ý hoặc bỏ kiểm tra Sketch/Cut nguồn.

Giữ nguyên Normal Cut, Blind = chiều dày API +0,10 mm cho từng lớp tôn; SheetCutOverrun tách riêng AssemblyFitGap 0,10 mm. Native Body phải khớp Preview, Sketch phải đạt kiểm tra, và không tạo RefPlane mới. Cách đặt Preview phía trên cặp Frame từ source local được giữ; phép tính thuần được đặt trong class Ghost để không cần thêm Compile entry vào project.
