# Giới hạn

- PASS final thuộc direct SOLIDWORKS API harness trên bản sao. Chưa smoke test toàn bộ DLL Add-in hoặc Preview callback/PMP của source cuối.
- Lượt PMP dùng lại host trước đó bị CANCEL_OR_OTHER_CLOSE rồi timeout, không vào native writer. Không coi direct API là bằng chứng UI đó đã được sửa.
- Rollback thử nghiệm vẫn gặp protected-history guard. Các thay đổi thử nghiệm bị loại khỏi final; không tuyên bố rollback/crash đã sửa.
- Mô hình radial mới yêu cầu cylinder/tangent/paired skins và guide intervals xác định. Nếu thiếu chứng cứ vẫn UNVERIFIED_RULE; không coi năm cặp là bằng chứng mọi biên dạng đều chạy được.
- Hai host/camera/tracing khác nhau khiến thời gian Apply không phải benchmark tốc độ. Không tuyên bố phần trăm tăng tốc. Cache Preview và vị trí nâng theo pháp tuyến của R64 được giữ.
- Không có CAD, ảnh CAD, raw log, đường dẫn riêng, persistent token hoặc binary trong báo cáo GitHub. Kho 43 Cut mẫu và 42 before/after nghiên cứu gốc giữ nguyên.
