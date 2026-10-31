# Giới hạn

- Đây là kiểm thử presentation bằng candidate trong tiến trình SOLIDWORKS, không phải smoke test DLL sản phẩm sau người dùng rebuild. Không tạo native Cut trong lượt R64.
- Nhìn dọc đúng pháp tuyến có thể làm bản gốc và Preview chồng hình chiếu dù tách nhau trong 3D. Không đổi vị trí World để tạo khoảng cách giả theo màn hình; người dùng có thể xoay view.
- Fit ban đầu cần orthographic view; perspective chưa được qualify. Camera chỉ phục vụ thu vừa hình, không chọn hướng/độ nâng.
- Chứng cứ API về Body/transform độc lập với bitmap; Display3=0 không đủ chứng minh mọi pixel nhìn thấy. Bitmap CAD giữ riêng, không upload.
- Các giới hạn R63 về PMP lần hai/rollback và heap reuse của R62 vẫn còn; sửa vị trí Preview không được coi là đã sửa các lỗi đó.
- Log mới của cặp 4332 fr5 × fr1a có lỗi native khác target và rollback chưa xác minh. Log kết thúc process exit 0; chưa đủ bằng chứng gọi đây là crash. R64 không thay đổi native solver/writer và không nhận đã khắc phục lỗi này.
- DLL trên đĩa đã được người dùng rebuild sang MVID khác log cũ. Reflection-only xác nhận ba overload CreateFrameNormalPresentation của R64; marker không đủ chứng minh mọi byte source khớp. Các PASS ở đây là candidate/API harness, không phải thao tác Cut trong DLL sản phẩm mới.
