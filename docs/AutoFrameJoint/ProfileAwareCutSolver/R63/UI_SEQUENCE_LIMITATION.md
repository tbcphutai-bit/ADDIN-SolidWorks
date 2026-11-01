# Giới hạn riêng của lượt mở PMP lần hai

Lượt `NEW_PAIR_PMP` đã xác minh cặp fr1a × fr2 qua native selection A/B, Close, AfterClose và native idle rồi tạo 12 Cut thật. Điều này không được dùng để tuyên bố mọi lần mở lại giao diện đều đã đạt.

Một lượt khác thử mở PMP liên tiếp cho hai góc trên cùng Assembly đã qua góc đầu, nhưng dừng với timeout của harness ở trang thứ hai. Log có sự kiện activation; chưa đủ bằng chứng kết luận trang không được kích hoạt hoặc xác định lỗi production UI. State machine của harness có thể ghi đè lỗi đầu tiên bằng timeout khi chờ cleanup, nên nguyên nhân ban đầu của lượt này còn `UNVERIFIED`.

Kết quả `SEQUENTIAL_DIRECT` xác minh hai lần cắt liên tiếp, Body và toàn bộ Sketch/Cut của góc trước bằng API trực tiếp. Nó không thay thế phạm vi kiểm thử mở lại PMP. Lượt PMP thứ hai không được cộng vào các lượt PASS hoặc 69 sự kiện Cut đã báo cáo.

Các file CAD nguồn và bản sao giữ nguyên hash, tài liệu của host đã đóng không Save sau lượt thử. Không có crash heap hay AccessViolation được ghi trong lượt log mới người dùng gửi; lỗi heap của R62 vẫn là giới hạn riêng đã được công bố trước đó.
