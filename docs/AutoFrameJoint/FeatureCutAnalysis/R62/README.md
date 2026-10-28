# R62 — lịch sử Cut, tốc độ và Preview tách cạnh

Ngày: 2026-10-28. R62 sửa ngay class `AutoFrameJointGhostPreview.cs`, giữ cơ chế chọn A/B và luồng đóng trang → native idle → Apply hiện tại. Không Build/Deploy Add-in chính, không Merge và không lưu CAD gốc.

Các lượt source cuối đạt đủ gate trong báo cáo: **3/3 lượt, 9 cặp, 83 sự kiện Cut mới**. Đây là kiểm thử candidate chạy trong tiến trình SOLIDWORKS; chưa phải xác nhận command trong DLL người dùng vừa rebuild.

| Lượt | Cặp | Cut | Sketch cuối | Kết quả |
|---|---:|---:|---:|---|
| R62_4332_HISTORY | 1 | 10 | 14 | PASS_QUALIFIED_CANDIDATE_API_ONLY |
| R62_2354_HISTORICAL | 4 | 38 | 54 | PASS_QUALIFIED_CANDIDATE_API_ONLY |
| R62_2354_CURRENT | 4 | 35 | 51 | PASS_QUALIFIED_CANDIDATE_API_ONLY |

R62 nhận diện các Profile bị suppress do Flat Pattern sinh tự động bằng cấu trúc native, owner/parent và persistent reference, không dùng tiền tố tên để bỏ kiểm tra. Những Sketch/Cut nguồn và hình học thân vẫn được kiểm tra. Preview dùng một phép tịnh tiến cứng chung A+B theo màn hình, đặt bản sao bên phải bản gốc; vùng nhìn được fit theo pixel đã quy đổi về cùng hệ tọa độ. Cleanup chỉ khôi phục camera nếu người dùng chưa đổi khung nhìn.

Tăng tốc bằng cách bỏ các full invariant trùng giữa hai bước không có thay đổi mô hình và giảm log/COM chỉ phục vụ chẩn đoán. Mọi kiểm tra sau Sketch/Cut, trước Commit, B-rep và đồ thị Sketch cuối vẫn giữ. Xem [thời gian](TIMINGS.md), [test matrix](TEST_MATRIX.json), [thay đổi](CHANGES.md), [source/DLL](SOURCE_DLL_VERIFICATION.md) và [giới hạn](KNOWN_LIMITATIONS.md).

Các quy ước giữ nguyên: NormalCut=true; Blind=t đọc bằng API+0,10 mm cho từng lớp tôn; AssemblyFitGap=0,10 mm tách riêng SheetCutOverrun=0,10 mm; không Through All, RefPlane mới, Fixed hay Convert. Kết quả cũ 43 Cut mẫu/42 before-after không được cộng với các sự kiện Cut mới của R62.


Source cuối đã chép vào class chính và compile độc lập sáu source thành công. Người dùng tự rebuild Add-in theo cấu hình hiện có. [83 Cut mới](NATIVE_CUTS.csv) và [119 Sketch mới](NATIVE_SKETCHES.csv) có dữ liệu API chi tiết; gồm cả Sketch guide và Sketch dùng cho Cut, không cộng lẫn với 43 Cut mẫu nghiên cứu cũ.

**Giới hạn quan trọng:** một host dùng lại sau lượt 4332 đã thoát với lỗi heap khi mở fixture tiếp theo, trước khi Cut. Chạy thành công trong các host mới không chứng minh lỗi này đã được sửa. Xem [giới hạn dùng lại host](CRASH_REUSE_LIMITATION.md). Không tuyên bố xử lý mọi biên dạng hoặc toàn bộ installed Add-in đã ổn định.
