# Giới hạn và mức độ bằng chứng

- Một lượt mở PMP lần hai trong harness bị timeout và chưa xác minh nguyên nhân. [Phạm vi kiểm thử UI](UI_SEQUENCE_LIMITATION.md) được tách khỏi kết quả cắt liên tiếp bằng API trực tiếp; không ghi lượt timeout là PASS.

- `API_VERIFIED`: cấu trúc Profile trước mutation, native Cut parameters, thickness, Sketch relations/support, Feature history và persistent reference trong các lượt đạt gate.
- `GEOMETRY_VERIFIED`: native/Preview symmetric difference, Body validity, gap/interference, material KEEP/CUT và Bend witnesses của fixture đã chạy.
- `UNVERIFIED`: mọi trường hợp chưa chạy, mép hở chưa được đo đủ, toàn bộ command trong installed DLL sau rebuild và rollback cho mọi lịch sử. Không tuyên bố hỗ trợ mọi Frame.
- Log lỗi ban đầu kết thúc process code 0; các WinRT empty-name errors xuất hiện sau lỗi AFJ, chưa chứng minh là nguyên nhân. Không ghi đây là AccessViolation hoặc heap crash trong chính log mới này.
- Lỗi heap `0xC0000374` của lượt R62 dùng lại host để mở fixture khác vẫn chưa xác định nguyên nhân. Thành công trong host riêng hoặc regression hiện tại không chứng minh đã sửa lỗi đó.
- Rollback exact vẫn cần phục hồi đầy đủ baseline IDs. Nếu metadata hệ thống không trở lại, không ghi rollback PASS. Lượt thành công không qualify mọi đường rollback thất bại.
- Ảnh CAD, file CAD gốc/bản sao, raw logs, đường dẫn máy và persistent token payload không được xuất bản.
