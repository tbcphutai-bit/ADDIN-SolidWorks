# Giới hạn và mức độ bằng chứng

- `API_VERIFIED`: Feature/Cut parameters, native thickness, Face2/Sketch relations, Feature inventory và persistent-reference observations của các lượt đạt gate.
- `GEOMETRY_VERIFIED`: native/Preview symmetric difference, valid Body, gap/interference, vật liệu dư và KEEP được đo bằng B-rep trong phạm vi fixture đã chạy.
- `INFERRED` hoặc `UNVERIFIED`: trường hợp chưa chạy, hình học không có datum duy nhất, rule bị từ chối, command của DLL mới sau rebuild và thao tác tương tác đầy đủ của người dùng.
- Các lượt API harness/candidate PMP không chứng minh toàn bộ installed Add-in đã chạy đúng. Không báo PASS khả năng nhìn thấy chỉ vì Display3 trả 0; ảnh API và projected pixel checks được đánh giá riêng, ảnh CAD giữ riêng.
- Chưa chứng minh xử lý mọi dạng Frame. Quy tắc không đủ bằng chứng phải trả `UNVERIFIED_RULE` và dừng, không đoán cutter.
- Rollback giữa chừng vẫn chưa được qualify cho mọi lịch sử có metadata tự sinh. Nếu original generated IDs không trở lại, không được ghi rollback exact PASS dù hình học đã được kiểm ở mức khác.
- Số Cut trong các lượt chưa đạt chỉ là số dòng report đã xác minh tại thời điểm đó; 0 không chứng minh factory chưa tạo Feature. Lỗi có thể xảy ra sau native creation và trước khi report được ghi.
- Chưa thử stress tương tác/đóng SOLIDWORKS giữa factory Cut. Không tự Build, Deploy hay Merge.
- So tốc độ là quan sát một lượt API cùng fixture/thứ tự/scope, không phải benchmark thống kê hoặc cam kết tốc độ trong debugger của người dùng.
