# BÁO CÁO KỸ THUẬT VÀ NGUYÊN NHÂN SỬA ĐỔI — CHECK SAME PART V4

**Mã tham chiếu:** `CHECK_SAME_PART_V4_REVIEWED_20261107`  
**Git Baseline:** `dc7cc875961cbcfd2357879d68dc0cb7c56680bd`  
**Nhánh triển khai:** `fix/check-same-part-v4-review-20261107`  
**Các file mã nguồn được phép sửa đổi:**
- `Commands/CheckSamePart.cs`
- `UI/SamePartCheckReviewForm.cs`

---

## 1. Mục đích và Bối cảnh sửa đổi

Phiên bản CHECK SAME PART V4 tập trung xử lý dứt điểm các nguyên nhân gốc về thuật toán phân loại và trải nghiệm người dùng, đảm bảo tính an toàn toán học cao nhất và loại bỏ hoàn toàn các suy đoán thiếu chứng cứ hình học:

1. **Ưu tiên Direct Match trước Mirror:**
   - Trong các nhóm phôi trải tương đương, thuật toán luôn kiểm chứng phép xoay và tịnh tiến vật rắn ($SO(3) \times \mathbb{R}^3$) cho trạng thái Folded trước.
   - Chỉ khi phép khớp trực tiếp thất bại mới chuyển sang kiểm tra phản chiếu gương (Reflection).
   - Với các chi tiết có tính đối xứng nội tại (ví dụ tấm phẳng đối xứng có lỗ ở tâm), cả phép quay và phản chiếu đều khớp; thuật toán bắt buộc ưu tiên nhóm trùng trực tiếp, tuyệt đối không gán nhãn `CHECK MIRROR`.

2. **Tiêu chuẩn nghiêm ngặt cho `SAME FULL` (Trùng hoàn toàn):**
   - Không còn chấp nhận mã hash, chữ ký tổng quát hay độ lệch nhỏ đơn thuần để kết luận "Trùng 100%".
   - `SAME FULL` chỉ được áp dụng khi thỏa mãn đồng thời:
     - Cùng file Part gốc (`SameOriginalReference == true`).
     - Cùng configuration yêu cầu và cùng flat configuration.
     - Sai lệch đo thực tế của cả trạng thái Flat và Folded $\le 0.0001$ mm.
   - Hai file Part độc lập dù biên dạng hình học khớp hoàn toàn trong dung sai đều được xếp vào `SAME TOLERANCE` để người dùng chủ động kiểm tra bản vẽ gia công.

3. **Căn chỉnh hình học thất bại $\neq$ Chứng minh khác nhau (`INCONCLUSIVE`):**
   - Khi bộ so khớp B-Rep không tìm thấy ma trận xoay/tịnh tiến do giới hạn số vòng lặp, số điểm neo hoặc chia mặt/cạnh topology, thuật toán phân loại an toàn là `INCONCLUSIVE` (Chưa thể kết luận), không được tự suy diễn thành `VERIFIED_DIFFERENT`.
   - Chỉ khi xác minh được số lượng solid body khác nhau (`Verified distinct solid-body count`) mới được kết luận chắc chắn là `VERIFIED_DIFFERENT`.

4. **An toàn cấu hình Flat-Pattern và Folded:**
   - Tuyệt đối không lấy nhầm Flat-Pattern từ configuration khác làm phương án dự phòng.
   - Kiểm tra xác nhận cấu hình đang thực sự active trong SolidWorks sau lệnh `ShowConfiguration2`.

5. **Kiểm tra All-Pairs và Thống kê Đầy đủ:**
   - Mọi cặp trong nhóm ứng viên đều được đo đạc độc lập; sai lệch nhóm là giá trị lớn nhất trong tất cả các cặp ($\max \Delta$).
   - Số lượng chi tiết Chưa xác minh (`NotVerifiedCount`) trong Snapshot được tính bằng tổng số chi tiết (`Sum(x => x.ItemCount)`), không tính theo số dòng báo cáo.

6. **Thông báo Thân thiện Dễ hiểu (Friendly UI):**
   - Loại bỏ các mã enum kỹ thuật nội bộ (`[ VERIFIED_SAME ]`, `[ INCONCLUSIVE ]`, `[ ERROR ]`) khỏi thanh tiêu đề chính.
   - Hiển thị thông báo tiếng Việt rõ ràng với 3 nội dung: 💡 Ý nghĩa, 🔍 Chi tiết so sánh, 📋 Khuyến nghị.
   - Footer hiển thị ngôn ngữ tự nhiên: Giới hạn dung sai, sai lệch đo thực tế (hoặc N/A nếu không đo được), trạng thái phôi trải và sau chấn.
   - Giữ nguyên thông tin B-Rep chi tiết trong nút **Chi tiết kỹ thuật**.

7. **Bảo toàn Hợp đồng Giao diện & Excel (Report-First):**
   - Quét BOM xong chỉ mở form báo cáo `SamePartCheckReviewForm`.
   - Không tự động xuất Excel ra đĩa.
   - Chỉ xuất Excel khi người dùng bấm nút "Xuất Excel...".
   - Snapshot xuất Excel sử dụng cùng dữ liệu và `ReportId` với UI, không tính toán lại hình học.

---

## 2. Bảng Đối chiếu Thay đổi (Before vs. After)

| Thành phần | Trước khi sửa (V3 / Baseline) | Sau khi sửa (V4 Patch) |
| :--- | :--- | :--- |
| **Ưu tiên Mirror** | Nếu hình học đối xứng, có thể bị đánh dấu Mirror trước khi thử hết các phép quay vật rắn. | Kiểm tra Direct trước. Nếu Direct khớp $\rightarrow$ tắt cờ Mirror (`allMirrorVerified = false`), ưu tiên nhóm trùng trực tiếp. |
| **Điều kiện SAME FULL** | Gán `SAME FULL` dựa trên chữ ký và kiểm tra trực tiếp giữa seed và member. | Bắt buộc `SameOriginalReference` + cùng config + độ lệch Flat & Folded $\le 0.0001$ mm. Khác file part $\rightarrow$ `SAME TOLERANCE`. |
| **Phân loại Thất bại** | Capped/exhausted search hoặc B-Rep mismatch thường rơi vào `VERIFIED_DIFFERENT`. | Hàm `ClassifyGeometryVerificationFailure`: Chỉ khác số solid body mới là `VERIFIED_DIFFERENT`; còn lại là `INCONCLUSIVE`. |
| **Kiểm tra cặp trong nhóm** | So sánh giữa chi tiết hạt giống (seed) với từng member. | So sánh All-Pairs (mọi cặp $i < j$ trong nhóm), ghi nhận max deviation của toàn nhóm. |
| **Bộ đếm UNVERIFIED** | Đếm theo số dòng `Rows.Count(x => x.StatusCode == "UNVERIFIED")`. | Đếm theo tổng số Part `Rows.Where(...).Sum(x => x.ItemCount)`. |
| **Bộ lọc UI UNVERIFIED** | Bỏ sót các nhóm so sánh có trạng thái UNVERIFIED khi lọc. | Thêm nhánh `allGroups.Where(g => g.Status == "UNVERIFIED")` vào bộ lọc. |
| **Tiêu đề Callout Card** | Chứa mã enum kỹ thuật: `[ VERIFIED_SAME ]`, `[ INCONCLUSIVE ]`. | Tiêu đề thân thiện tiếng Việt, biểu tượng trực quan, loại bỏ mã enum khỏi màn hình chính. |
| **Chân trang Callout Card** | Hiển thị mã thô: `Trạng thái: VERIFIED_SAME · Dung sai: ... · Flat: VERIFIED_SAME`. | Ngôn ngữ tự nhiên: `Đã xác minh theo mức dung sai · Giới hạn: ... · Sai lệch đo: ... · Phôi trải: Đã xác minh · Sau chấn: ...`. |

---

## 3. Bản vá Mã nguồn đã Áp dụng

Bản vá được áp dụng tự động thông qua script `apply_check_same_part_v4.py` (phiên bản Fixed Anchors do ChatGPT cung cấp), vượt qua 100% các điều kiện an toàn:
1. Worktree sạch tại đúng commit `dc7cc875961cbcfd2357879d68dc0cb7c56680bd`.
2. Kiểm tra `apply_check_same_part_v4.py --check` đạt trạng thái `DRY RUN COMPLETE: NO FILES WRITTEN`.
3. Áp dụng `--apply` chỉ tác động duy nhất vào 2 file:
   - `Commands/CheckSamePart.cs`
   - `UI/SamePartCheckReviewForm.cs`
4. `git diff --check` sạch hoàn toàn, không có lỗi khoảng trắng thừa.

---

## 4. Chuẩn hóa Thuật ngữ Cơ khí & Tối ưu hóa UX Chuyên nghiệp (UX Refinement)

Nhằm giúp kỹ sư chế tạo, công nghệ dập uốn tôn tấm và lắp ráp máy dễ dàng nắm bắt kết quả mà không bị nhầm lẫn bởi các thuật ngữ toán học hoặc từ ngữ dịch thô, toàn bộ thuật ngữ trên giao diện đã được rà soát và nâng cấp chuyên nghiệp:

1. **Khái niệm "Đối xứng gương" (Mirror) $\rightarrow$ "Đối xứng Trái / Phải (L/R)":**
   - *Bản chất kỹ thuật:* Hai chi tiết có kích thước và hình học 3D đối xứng nghịch chiều qua mặt phẳng (như bàn tay Trái và bàn tay Phải).
   - *Ý nghĩa sản xuất:* **TUYỆT ĐỐI KHÔNG LẮP LẪN** cho nhau được! Trong gia công uốn chấn, hai chi tiết này phải dùng chương trình uốn ngược cữ/ngược hướng.
   - *Loại bỏ thuật ngữ hàn lâm:* Đã bỏ hoàn toàn cụm từ khó hiểu `"chirality đảo ngược"` khỏi phần giải thích chi tiết; thay bằng `"ngược chiều uốn / hướng lắp ráp (vế Trái - vế Phải)"`.
   - *Nhãn nút bấm / Bảng:* Đổi từ `Mirror` / `"Đối xứng gương (Mirror)"` thành **`"Đối xứng (L/R)"`** và **`"◆ CẶP CHI TIẾT ĐỐI XỨNG TRÁI / PHẢI (VẾ L / R)"`**.

2. **Khái niệm "Dung sai" (SAME TOLERANCE) $\rightarrow$ "Trong dung sai":**
   - Đổi nhãn cộc lốc `"Dung sai"` thành **`"Trong dung sai"`** / **`"✔ TRÙNG KHỚP TRONG DUNG SAI CHO PHÉP"`**, làm rõ rằng chi tiết trùng khớp hình học trong sai số cho phép ($< 0.05$ mm).

3. **Khái niệm "Trùng 3D" (SAME GEOMETRY) $\rightarrow$ "Khác vật liệu":**
   - Bản chất là hình học 3D giống nhau nhưng khác vật tư hoặc khác bề dày tôn.
   - Đổi nhãn thành **`"Khác vật liệu"`** / **`"● TRÙNG HÌNH HỌC 3D (KHÁC VẬT LIỆU / ĐỘ DÀY)"`** để cảnh báo kỹ sư tuyệt đối không dùng chung phôi tấm.

4. **Khái niệm "Trùng Phôi" (SAME FLAT) $\rightarrow$ "Trùng phôi cắt":**
   - Bản chất là các chi tiết dùng chung một phôi trải phẳng (có thể cắt Laser hoặc đột CNC chung), nhưng các bước uốn 3D khác nhau.
   - Đổi nhãn thành **`"Trùng phôi cắt"`** / **`"▲ DÙNG CHUNG PHÔI TRẢI (KHÁC BƯỚC CHẤN UỐN)"`**.

5. **Khái niệm "Chưa tìm thấy nhóm trùng trong lần quét này; chưa chứng minh khác nhau từng cặp" (NO_MATCH) $\rightarrow$ "Không trùng":**
   - Loại bỏ câu văn logic toán học phức tạp `"chưa chứng minh khác nhau từng cặp"`.
   - Đổi thành **`"Không trùng"`** (Pill gọn gàng) và **`"○ CHI TIẾT ĐỘC LẬP — KHÔNG CÓ PART TRÙNG LẶP"`**.

6. **Loại bỏ chữ tiếng Nhật trong giao diện tiếng Việt:**
   - Đổi tiêu đề cột `BOM (部品番号)` thành **`Vị trí BOM (Item No)`**.

7. **Chuẩn hóa Banner danh sách:**
   - Đổi `"NHÓM CHI TIẾT ĐỒNG DẠNG"` (từ toán học dễ hiểu nhầm là phóng to thu nhỏ) thành **`"DANH SÁCH NHÓM CHI TIẾT TRÙNG LẶP"`**.
