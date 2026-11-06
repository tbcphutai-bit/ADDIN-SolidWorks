# Giới hạn: lỗi khi dùng lại host kiểm thử

Sau lượt 4332 đã xác minh 10 Cut thật, Preview được dọn và các bản sao đã đóng. Khi nạp lượt chẩn đoán khác vào cùng tiến trình rồi mở fixture 2354, SOLIDWORKS thoát trong `OpenDoc6`, trước khi bắt đầu Preview/Cut của lượt mới.

Application Error và Windows Error Reporting xác nhận mã `0xC0000374` tại `ntdll.dll`. Hash CAD nguồn và bản sao giữ nguyên. Không có native stack dump để xác định nơi gây hỏng heap. Module báo lỗi không chứng minh Part hỏng hoặc xác định thành phần gây lỗi.

Lượt dùng lại host là **FAIL/UNVERIFIED về độ ổn định tiến trình**. Kiểm tra B-rep, Sketch, khe hở và lịch sử thành công không chứng minh heap an toàn khi mở dữ liệu kế tiếp.

Harness giữ witness Part/Body trong static list, đồng thời host có Add-in đang cài và nhiều diagnostic assembly đã nạp. Đây là các khác biệt vòng đời cần cô lập, **chưa phải nguyên nhân được xác minh**. Mã hiện tại đã Hide các body được theo dõi trước native Apply; trace có 20 Display và 20 Hide hoàn tất. Chưa có bằng chứng đủ để sửa sản phẩm bằng explicit COM release hay ép GC.

Chạy riêng từng fixture trong phiên mới chỉ xác minh các lượt hình học đó. Đây **không phải bằng chứng đã sửa lỗi dùng lại host**, hoặc bảo đảm mọi trường hợp ổn định. Lỗi này vẫn còn mở để phân tích native dump trong một thử nghiệm tiếp theo.
