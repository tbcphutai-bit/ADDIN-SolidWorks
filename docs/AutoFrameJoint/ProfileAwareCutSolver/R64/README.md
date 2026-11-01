# R64 — Preview nâng khỏi mặt phẳng khung

Theo xác nhận của người dùng, bản sao Preview phải được nâng vuông góc khỏi mặt phẳng chứa hai trục Frame, giữ nguyên vị trí dọc/ngang, hướng, tỷ lệ và hình học giao góc. Vị trí 3D không phụ thuộc camera. R64 sửa cùng class Ghost đang dùng; không thêm command, không đổi A/B selection hoặc thuật toán Cut.

Pháp tuyến được dựng từ hai trục Frame đọc qua BaseFlange Sketch/transform và bằng chứng Bend B-rep; Cut Preview kiểm thêm với hướng Depth của Cut Plan đã xác minh. Chọn dấu ổn định bằng thành phần World có trị tuyệt đối lớn nhất dương, để đổi A/B hoặc đảo trục extrusion không lật hướng nâng. A/B, KEEP và CUT cùng dùng một phép tịnh tiến cứng. Không lấy hướng từ ảnh hoặc bounding box.

Độ nâng bằng span thực của hai Body đo qua GetExtremePoint theo pháp tuyến, cộng khoảng hở trình bày `max(5 mm, 10% span)`. Khoảng này chỉ phục vụ hiển thị, tách khỏi AssemblyFitGap và SheetCutOverrun 0,10 mm. Chỉ dùng bounding points cho thao tác fit camera ban đầu.

API trên bản sao 4332 và 2354 đã xác minh: không lệch trong mặt phẳng; Body temporary thực nằm trên envelope Body gốc; đổi A/B không đổi vị trí nâng; xoay camera thật 20°, zoom và P1/P2 giữ nguyên Delta và body KEEP. Cùng candidate sau orbit không Solve/Hide/Display/refit lại. Cleanup giữ camera người dùng nếu họ đã đổi view. Hash CAD nguồn/bản sao và toàn bộ Feature giữ nguyên.

Lượt bổ sung 2354 kiểm tra camera xoay 20° và zoom 1,075 với thao tác pan chẩn đoán để giữ tâm hình chiếu. Body và Delta không đổi; không gọi ghost refit hoặc solver. Bitmap trước/sau đã được kiểm tra thấy bản gốc và Preview. Pan này chỉ thuộc harness, không thêm vào code sản phẩm. Không khẳng định mọi mép vẫn trong viewport sau người dùng đổi camera.

Log người dùng gửi sau đó thuộc DLL cũ MVID `01b2c548-9d78-455b-bc89-4ea63c59071d`, báo Cut thật của cặp 4332 fr5 × fr1a khác target 110,1007 mm³ tại một lát FAR; hoàn tác `ROLLBACK_UNVERIFIED`. R64 chỉ sửa presentation và không sửa lỗi này. Công việc native được kiểm riêng; không dùng PASS Preview để kết luận Cut thật thành công.

Xem [test matrix](TEST_MATRIX.json), [bằng chứng API](API_EVIDENCE.json), [source/DLL](SOURCE_VERIFICATION.json) và [giới hạn](LIMITATIONS.md). Source đã chép vào class chính để người dùng tự rebuild; không tự Build/Deploy hoặc Merge. Mã solver/Cut Plan/native writer/history từ operation models trở xuống giữ nguyên hoàn toàn, không dùng lượt Preview này để ghi thêm PASS native Cut.
