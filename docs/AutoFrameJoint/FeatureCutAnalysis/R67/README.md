# R67 — DLL đang chạy thiếu R65/R66

Log mới nạp DLL MVID `dcc0c708-a37e-4b8a-a84c-589bf2af6aa1`. Reflection-only đọc đúng DLL trên đĩa xác nhận có 3 overload CreateFrameNormalPresentation (R64), nhưng không có các method xử lý Bend/guide R65 và không có ApplyTemporaryPreviewAppearance (R66). Đây là kiểm metadata trực tiếp, không suy từ việc thiếu log. Project hiện vẫn Compile đúng class GhostPreview nguồn.

DLL được build lúc **13:21:10 ngày 29/10/2026 (UTC+7)**, trước khi source R65 được chép lúc 13:26 và trước màu R66 được chép lúc 14:02. SOLIDWORKS mở lúc 13:56 vẫn nạp binary 13:21. Log còn chú giải xanh/cam và lỗi cũ 110,1007 mm³, maps=0 tại B:FAR:6:MATE_3. Không có bằng chứng đây là lỗi do riêng chế độ Debug; guard dừng vì Body không khớp target của binary cũ.

Log có hai lượt: fr5 × fr3a hoàn tất 12 Cut, rồi fr5 × fr1a bị chặn. Dữ liệu trên đĩa được người dùng lưu lúc 14:00, khác fixture trước đó ở Assembly và ba Part. Các file hiện tại được đóng băng hash và sao chép riêng; không khôi phục hoặc ghi đè lịch sử vừa lưu.

Source R66 hiện tại đã được thử trực tiếp bằng API trên bản sao dữ liệu **sau lần Cut đã lưu**, đúng cặp còn lỗi fr5 × fr1a: **18 Cut / 22 Sketch**, native/Preview difference **0**, gap **0.100000 mm**, interference **0**. Lát lỗi cũ cũng khớp 0; original authored history, mọi Sketch cuối lượt và KEEP/CUT/Bend được kiểm riêng. Mọi hash nguồn/bản sao giữ nguyên, đóng không lưu. Palette tím/vàng được đặt thành công lên các Face của Body tạm; chưa xác minh pixel trong DLL sản phẩm.

Không sửa thêm thuật toán hoặc class khi binary hiện tại chưa chứa sửa đã có. Source chính vẫn SHA256 `8DA46874294E61F2A61CE5921F3C28A3F1564254A6587442D8B502AE8CEC9F41`. Người dùng cần đóng SOLIDWORKS, tự Rebuild đúng project hiện tại/cấu hình x64 rồi mở lại. MVID mới phải khác `dcc0c708-a37e-4b8a-a84c-589bf2af6aa1`; log phải có AFJ-R65 và AFJ-R66-COLOR khi vào các đường tương ứng. Chỉ đổi MVID không đủ: compiled methods phía trên phải xuất hiện trong DLL mới.

Ảnh khoanh đỏ thuộc binary cũ. API này chứng minh Cut thật khớp Preview theo kế hoạch hiện tại, chưa chứng minh mọi mép nhìn thấy trong ảnh đều đúng ý thiết kế. Sau khi binary mới chạy, phần mép còn hở phải được đối chiếu riêng; không coi hình Preview hoặc body match là bằng chứng che kín mọi mặt. Rollback cũ còn UNVERIFIED, không được nhận đã sửa crash/rollback.

Xem [source/DLL](SOURCE_DLL_VERIFICATION.json), [case API](CURRENT_SAVED_CASE_API.json) và [từng Cut](CURRENT_SAVED_CASE_CUTS.csv). Không main Build/Deploy, sửa project/DLL hoặc Save CAD gốc; không upload CAD/raw log/token/ảnh riêng tư. Dữ liệu R65 và R66 giữ nguyên phạm vi, không cộng lượt này vào 43 Cut mẫu/42 before-after.
