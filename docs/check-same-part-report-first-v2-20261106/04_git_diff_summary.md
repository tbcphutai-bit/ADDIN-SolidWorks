# TỔNG HỢP GIT DIFF VÀ CÁC THAY ĐỔI MÃ NGUỒN (REPORT-FIRST V2 - UPDATE 2026-11-07)

## 1. Mục tiêu đợt cập nhật
Rà soát và hoàn thiện 3 nội dung theo phản hồi nghiệm thu từ ChatGPT:
1. **Chuẩn hóa nhãn `NO_MATCH`:** Đổi từ "Không trùng" / "KHÔNG CÓ NHÓM TRÙNG" thành **"Chưa tìm thấy nhóm trùng"** / **"CHƯA TÌM THẤY NHÓM TRÙNG"** (do chưa chứng minh khác biệt hình học từng cặp). Giữ nguyên `StatusCode = "NO_MATCH"` và giữ nguyên thuật toán so sánh hình học.
2. **Xác minh số lượng nhóm:** Đính chính và giải thích rõ quy tắc đếm: **7 nhóm hình học + 1 thẻ Chưa xác minh (UNV) + 1 thẻ Chưa tìm thấy nhóm trùng (-) = 9 nhóm (cards) trên UI Master Grid**.
3. **Bổ sung bằng chứng kiểm thử thực tế:** Cung cấp log gốc cho 7 cặp (4–5, 15–16, 22–22, 31–32, 20–21, 28–29, 35–36), phân tách 4 danh mục dữ liệu rõ ràng (Actual runtime, Automated verification, Synthesized metrics, Unverified cases).

---

## 2. Git diff --stat (So với commit `c176468`)
```text
 Commands/CheckSamePart.cs                          |   6 +-
 Commands/ThaoTacBomTaskPane.cs                     |   2 +-
 UI/SamePartCheckReviewForm.cs                      |  24 ++--
 .../01_implementation_report.md                    | 102 ++++++++--------
 .../02_build_test_results.md                       | 119 +++++++++++++++----
 .../03_debug_evidence.txt                          | 129 +++++++++++++++------
 .../04_git_diff_summary.md                         | 105 +++++++++--------
 7 files changed, 308 insertions(+), 179 deletions(-)
```

---

## 3. Chi tiết các thay đổi trong source code

### 3.1. `Commands/CheckSamePart.cs`
- **Line 3500 (`CreateRow` cho `NO_MATCH`):**
  - Sửa ghi chú an toàn: `"Chưa tìm thấy nhóm trùng trong lần quét này; chưa chứng minh khác nhau từng cặp."`
- **Line 3535 (`DisplayStatus`):**
  - Đổi trả về từ `"KHÔNG CÓ NHÓM TRÙNG"` thành `"CHƯA TÌM THẤY NHÓM TRÙNG"`.
- **Line 3757 (`SamePartReportV2Form` - Fallback UI):**
  - Cập nhật chuỗi tổng kết: `+ "  |  CHƯA TÌM THẤY NHÓM TRÙNG: " + snapshot.NoMatchCount;`

### 3.2. `Commands/ThaoTacBomTaskPane.cs`
- **Line 1047 (`ShowSamePartResult`):**
  - Cập nhật thông báo trạng thái trên TaskPane: `+ (snapshot.NoMatchCount > 0 ? ", chưa tìm thấy nhóm trùng: " + snapshot.NoMatchCount : "")`.

### 3.3. `UI/SamePartCheckReviewForm.cs`
- **Line 140 (Filter Pills):**
  - Khởi tạo `pillNoMatch = new FilterPillButton("Chưa tìm thấy nhóm trùng", countNoMatch, "NO_MATCH", ...)`.
- **Line 396 (`groupGrid` Layout):**
  - Mở rộng độ rộng cột 0 (`groupGrid.Columns[0].Width`) từ 110 lên **175 px** để hiển thị trọn vẹn nhãn "Chưa tìm thấy nhóm trùng" không bị cắt chữ.
- **Line 548 & 552 (`DisplayGroups`):**
  - Gán ghi chú an toàn cho `fakeNoMatchGroup.Note` và thêm dòng hiển thị với tên `"Chưa tìm thấy nhóm trùng"`.
- **Line 607 (`DisplaySelectedGroup`):**
  - Sửa tiêu đề nhóm chọn khi là NO_MATCH: `"Chưa tìm thấy nhóm trùng (" + group.Items.Count + " chi tiết)"`.
- **Line 710 (`DisplaySelectedGroup` Callout Card):**
  - Sửa `calloutHeader.Text` thành `"○ CHI TIẾT CHƯA TÌM THẤY NHÓM TRÙNG (NO MATCH)"`.
  - Sửa `calloutBody.Text` nhấn mạnh: *"Chưa tìm thấy nhóm trùng trong lần quét này; chưa chứng minh khác nhau từng cặp."*
- **Line 783 & 799 (`FormatStatusLabel` & `FormatStatusDescription`):**
  - Ánh xạ `NO_MATCH` thành `"Chưa tìm thấy nhóm trùng"`.
- **Line 876 (`DrawDetailBanner` Badge):**
  - Mở rộng hình chữ nhật vẽ badge: `Rectangle badgeRect = new Rectangle(detailBanner.Width - 180, (detailBanner.Height - 28) / 2, 168, 28);` để chứa trọn chuỗi text tiếng Việt.
- **Line 966 (`PaintGroupCell`):**
  - Mở rộng giới hạn độ rộng pill trong cell của `groupGrid`: `int pillW = Math.Min(165, e.CellBounds.Width - 10);`.

### 3.4. Cập nhật tài liệu kiểm chứng
- **`01_implementation_report.md`:** Cập nhật mục 2.1 (chuẩn hóa nhãn `NO_MATCH`), mục 2.2 (phép đếm chính xác 9 nhóm $7 + 1 + 1$).
- **`02_build_test_results.md`:** Bổ sung phân loại 4 nhóm dữ liệu (Thực tế, Tự động, Tổng hợp, Unverified) và 14 test scenarios chi tiết.
- **`03_debug_evidence.txt`:** Cung cấp log gốc cho cả 7 cặp chi tiết, 6 chi tiết chưa xác minh với exception nguyên bản, 21 chi tiết chưa tìm thấy nhóm trùng, xác nhận 9 thẻ UI và luồng xuất Excel theo yêu cầu.
- **`04_git_diff_summary.md`:** Thống kê git diff và chi tiết từng file thay đổi.
