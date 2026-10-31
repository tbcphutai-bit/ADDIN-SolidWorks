# R66 — màu Preview phân biệt với Part thật

Preview A dùng tím RGB (155,55,255), B dùng vàng RGB (255,215,0), độ trong suốt 10%. P2 hiển thị phần CUT A đỏ (255,50,65), CUT B xanh dương (0,115,255), độ trong suốt 12%. Chú giải trong thông báo Preview khớp palette dùng cho cả Show Bodies và Cut Preview.

Một helper chung đặt màu trên Body tạm và từng Face2 của chính Body tạm để ghi đè appearance kế thừa từ Part. Cổng IsTemporaryBody kiểm trước mọi setter; không đổi Component/Part appearance gốc. Display3 vẫn nhận đúng COLORREF; selection, vị trí nâng theo pháp tuyến, cache và cleanup giữ nguyên.

Mã solver/Cut Plan/native writer/history từ operation models trở xuống giống hoàn toàn R65. Không chạy lại native Cut cho thay đổi màu. Compile độc lập sáu source PASS; class Ghost đã chép vào source chính. Chưa xác minh màu render trong SOLIDWORKS hoặc DLL sau người dùng rebuild, có log AFJ-R66-COLOR cho số mặt đổi màu và trường hợp API không hỗ trợ appearance. Không main Build/Deploy, sửa project hoặc DLL/PDB, Save CAD hay Merge.

Người dùng tự rebuild source đang dùng rồi mở lại lệnh Preview. Bộ 53 Cut/73 Sketch của R65 giữ nguyên số liệu và phạm vi; không cộng thêm kết quả kiểm CAD cho R66.
