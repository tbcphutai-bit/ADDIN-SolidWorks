# 02 — Joint rules: điều đã biết vs giả thuyết

## 1. Tiêu chuẩn KCC 26003 (STANDARD; phạm vi bắt buộc)
Tài liệu *パネルコーナー部立ち上がり板厚勝ち負け方向の統一* định nghĩa:
- **勝ち (Winner):** phía che **mặt cắt bề dày (小口)** của thành đối diện.
- **負け (Loser):** phía có mặt cắt bề dày được thành đối diện che.
- Với **panel chấn bốn phía** và **panel dạng tấm cắt**, ở **phần thành dựng góc**: **hướng cạnh ngắn thắng**, **hướng cạnh dài thua**.
- Hình vuông: thống nhất chọn một phía.
- Nếu Drawing có chỉ dẫn thắng/thua: **Drawing ưu tiên cao hơn mặc định**.
- Ngoài phạm vi đã quy định: **tham vấn người phụ trách trước/sau công đoạn**, không suy rộng.

**Quan trọng:** văn bản 26003 quy định *hướng che mép chiều dày thành dựng*, **không** chứng minh toàn bộ phương pháp cắt Web/Lip/Bend. Nó **không tự áp dụng** cho mọi frame kết cấu hoặc góc tam giác.

## 2. Bảng rule sơ bộ
| ID | Loại chứng cứ | Điều kiện | Quyết định | Khi không đủ dữ kiện |
|---|---|---|---|---|
| R01 | Proposed geometry method | 2 frame có axis đủ tin cậy | so chiều dài theo axis | trả AMBIGUOUS |
| R02 | STANDARD KCC26003 | panel có phạm vi phù hợp, không Drawing override | Short-direction thành WIN, Long-direction thành LOSE | không được tự áp dụng |
| R03 | STANDARD KCC26003 | Drawing ghi thắng/thua | ưu tiên Drawing | cần nguồn Drawing hợp lệ |
| R04 | STANDARD KCC26003 | Panel vuông | chọn thống nhất một phía | cần convention hoặc user xác nhận |
| R05 | OBSERVED, revalidate | nhiều góc 90° trên mẫu | Web có vùng giao xiên, thường gần 45° | không coi 45° là mặc định |
| R06 | OBSERVED, revalidate | mặt cắt thay đổi cao độ | Wall kết thúc khác Web | không ép cùng một cut plane |
| R07 | OBSERVED, revalidate | có lip/overhang | lip có thể giữ dài hơn hoặc cắt ngắn | cần rule quyết định, không tự suy |
| R08 | OBSERVED, revalidate | chi tiết 2354 47/48 | tồn tại quan hệ phản chiếu đầu cắt theo phân tích trước | kiểm chứng DXF geometry + topology |
| R09 | HYPOTHESIS | có panel inside/outside rõ | ưu tiên liên tục một số bề mặt/mép chuẩn | cần người thiết kế phê chuẩn |
| R10 | UNRESOLVED | góc không vuông / giao chữ T / thanh giữa | winner/loser chưa có quy định chung | NEEDS_REVIEW |

## 3. Quy trình quyết định
```text
if validDrawingOverride:
    WinnerLoser = Drawing
else if Kcc26003Applicable == TRUE:
    if ShortDirection != LongDirection by defined measure:
        WinnerLoser = ShortDirection wins
    else:
        WinnerLoser = StandardizedSiteConventionOrNeedsReview
else:
    WinnerLoser = NeedsReview

for each region in [Web, Wall, Lip, Bend, Other]:
    candidates = GenerateGeometryCandidates(region, corner, actualFaces)
    candidates = FilterByProvenConstraints(candidates, WinnerLoser, collision)
    if unique, validated, evidence-supported candidate:
        mark Proposed
    else:
        mark UNDETERMINED // never silently choose a cut
```

## 4. Vì sao không dùng 'Short Frame is always fully preserved'
Winner/Loser **chỉ xác định bên che mặt đầu thành** tại vùng tương ứng. Long Frame vẫn có thể giữ nguyên một phần gờ; Short Frame vẫn có thể cần vát Web hoặc notch. Không được dịch Winner/Loser thành Boolean subtract cả Part hoặc điều kiện `KeepAsIs` cho mọi vùng.

## 5. Các trường hợp cần giữ riêng
- **Đầu trái/đầu phải và mirror:** phải transform joint frame; một vết cắt có thể sinh biến thể đối xứng, nhưng orientation vẫn phải kiểm.
- **Lỗ và stud:** chỉ nhận diện để bảo vệ và tránh bị coi là đặc trưng tiết diện.
- **Gờ chìa:** không được khẳng định 'mọi gờ ngoài đều giữ', 'mọi gờ trong đều cắt'.
- **Góc tam giác / 30-60-90:** không áp dụng trực tiếp quy tắc panel chữ nhật từ KCC 26003.
- **Cạnh dài/ngắn:** đo theo **hướng panel** khi dùng tiêu chuẩn; không đơn giản là bounding box đối tượng.

## 6. Câu hỏi nghiên cứu quan trọng nhất
Tìm một **ràng buộc hình học có thể kiểm chứng** quyết định giữ/cắt gờ khi nhiều kết quả đều không overlap. Nếu ràng buộc đó chưa có, bộ giải được phép đưa ra *các ứng viên* nhưng bắt buộc gắn trạng thái `NEEDS_REVIEW`.
