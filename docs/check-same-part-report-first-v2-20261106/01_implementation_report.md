# BÁO CÁO THIẾT KẾ & HOÀN THIỆN GỐC RỄ: CHECK SAME PART V2 (SAFE GEOMETRY & INVARIANCE)

## 1. Thông tin chung
* **Dự án:** ADDIN-SolidWorks
* **Nhánh làm việc:** `fix/check-same-part-report-first-v2-20261106`
* **Commit cơ sở review:** `e0a703eb8b0a342ac153bc13e2a168fae62b4d90`
* **Môi trường:** .NET Framework 4.8 / Visual Studio 2022 Community MSBuild / SOLIDWORKS Add-in x64
* **Output DLL:** `C:\Tao moi\ADDIN.dll` (Debug x64)
* **Dataset kiểm chứng thực tế:** `D:\CG1-ISOLATED-TEST-COPIES\4365`

---

## 2. Xử lý triệt để nguyên nhân gốc (Root Cause Architecture)

Thay vì viết các điều kiện chắp vá cho từng cặp chi tiết riêng lẻ, toàn bộ thuật toán hình học đã được chuẩn hóa theo các nguyên lý toán học và cơ học giải tích vững chắc:

### 2.1. Đảm bảo tính bất biến với phép tịnh tiến và quay 3D (Stage 2)
* **Vấn đề trước đây:** Kiểm tra độ lệch trọng tâm `comDelta` dựa trên tọa độ tuyệt đối $(COM_x, COM_y, COM_z)$ trong `AreMetricsWithinTolerance`. Do tọa độ trọng tâm phụ thuộc vào gốc tọa độ mô hình (origin placement), các chi tiết giống hệt nhau về hình dạng nhưng đặt lệch gốc tọa độ hoặc xoay hướng sẽ bị loại bỏ sai.
* **Giải pháp gốc rễ:**
  - Loại bỏ hoàn toàn điều kiện kiểm tra tọa độ trọng tâm tuyệt đối khỏi `AreMetricsWithinTolerance`.
  - Bộ lọc chỉ số tổng quát chỉ sử dụng các đại lượng vô hướng bất biến 100% dưới mọi phép quay và tịnh tiến $SE(3)$:
    1. Thể tích tổng thể $V$ (Volume - scalar invariant).
    2. Diện tích bề mặt tổng thể $A$ (Surface Area - scalar invariant).
    3. Tổng chiều dài các cạnh $L$ (Edge Length - scalar invariant).
    4. Số lượng Solid Body (Body Count - integer invariant).
    5. Ba mô-men quán tính chính $I_1 \le I_2 \le I_3$ (Eigenvalues của ten-xơ quán tính quanh trọng tâm - scalar invariants).
  - Phép so sánh chi tiết hình học B-Rep trong `SamePartGeometryVerifier.TryMatch` tìm kiếm ma trận biến đổi vật rắn tự do $(R, t) \in SE(3)$ thông qua việc căn chỉnh hệ khung tam giác neo (anchor triads), tự động khớp mọi chi tiết dù chúng bị dịch chuyển hay xoay hướng bất kỳ trong không gian 3D.

### 2.2. Kiểm chứng đối xứng phản chiếu hình học thực tế (True Mirror Verification - Stage 3)
* **Vấn đề trước đây:** Nhận diện Mirror dựa vào tính chất chirality (POS/NEG) hoặc tọa độ trọng tâm đối xứng qua $Y=0$. Cách làm này mang tính chắp vá, không xử lý được trường hợp mặt phẳng đối xứng là mặt phẳng nghiêng bất kỳ hoặc chi tiết bị tịnh tiến/xoay sau khi mirror.
* **Giải pháp gốc rễ:**
  - Bổ sung hàm `CreateReflectedSnapshot` và `TryMatchMirror` trong `SamePartGeometryVerifier`.
  - Về mặt đại số hình học, mọi phép phản chiếu qua mặt phẳng bất kỳ trong $\mathbb{R}^3$ đều biểu diễn được dưới dạng $x \mapsto R \cdot M_x(x) + t$, trong đó $M_x = \text{diag}(-1, 1, 1)$ là phép phản chiếu qua $X=0$ và $R \in SO(3)$ là phép quay vật rắn chuẩn tắc ($\det(R) = +1$).
  - Thuật toán tạo ảnh phản chiếu của chi tiết $A$ qua $X=0$, sau đó dùng bộ so khớp vật rắn `TryMatch` để tìm kiếm phép quay $R$ và tịnh tiến $t$ khớp với chi tiết $B$.
  - Nếu tìm thấy biến đổi khớp trong dung sai: Chi tiết $B$ được chứng minh toán học là đối xứng gương của $A$ với độ lệch sai số thực tế `ActualDeviationMm` được đo đạc chính xác.
  - Tuyệt đối không xác nhận Mirror chỉ dựa vào tên feature ("MirrorPattern") hay lịch sử dựng hình.
  - Nếu thuật toán so khớp không đủ anchor neo (inconclusive), hệ thống ghi nhận `VerificationStatus = INCONCLUSIVE`, `ActualDeviationMm = N/A` và ghi chú rõ: *"Có dấu hiệu đối xứng gương (chirality hoặc feature), nhưng chưa đủ dữ liệu hình học để tự động xác minh; cần kiểm tra thủ công."*

### 2.3. Chuẩn hóa kết luận SAME FLAT và SAME FULL (Stage 4)
* **An toàn khi phân loại SAME FLAT:**
  - Khi phôi phẳng Flat-Pattern đạt `FlatCheckStatus = VERIFIED_SAME`, hệ thống thực hiện kiểm tra trạng thái uốn gập Folded:
    - Nếu trạng thái Folded chứng minh khác nhau (`FoldedCheckStatus = VERIFIED_DIFFERENT`): Phân loại `SAME FLAT` với `VerificationStatus = VERIFIED_SAME`.
    - Nếu trạng thái Folded chưa thể kết luận (`FoldedCheckStatus = INCONCLUSIVE` do thiếu anchor hoặc mặt uốn cong tự do): Hệ thống **không được tự tiện coi là DIFFERENT**, mà phân loại với `VerificationStatus = INCONCLUSIVE`, ghi chú: *"Biên dạng phôi phẳng (Flat) đã xác minh giống nhau; trạng thái uốn gấp 3D chưa đủ dữ liệu để tự động kiểm chứng, cần kiểm tra trực tiếp mô hình 3D."*
* **Phân định rõ SAME FULL và SAME TOLERANCE:**
  - Nhãn "Trùng 100%" (`SAME FULL`) chỉ được cấp cho các chi tiết hoàn toàn trùng khớp tuyệt đối (sai lệch đo đạc $\le 0.0001$ mm hoặc cùng file chi tiết).
  - Các chi tiết có sai lệch đo đạc $> 0.0001$ mm nằm trong giới hạn dung sai được xếp chính xác vào `SAME TOLERANCE` ("Trùng trong dung sai"), hiển thị rõ sai lệch thực tế đo được (ví dụ: `0.0125 mm`) và dung sai áp dụng.

### 2.4. Sửa thứ tự nạp dữ liệu nhóm và thông điệp thân thiện (Stage 6)
* **Khắc phục lỗi thứ tự khởi tạo:** Đảm bảo gọi `group.Items.AddRange(groupItems)` TRƯỚC KHI gọi `PopulateGroupExplanations(group)` ở mọi luồng xử lý. Nhờ đó, các thông tin về Vật liệu và Chiều dày tôn luôn được trích xuất chính xác 100% từ danh sách chi tiết thực tế của nhóm thay vì hiển thị giá trị mặc định.
* **Cấu trúc thông báo 3 phần dễ hiểu:**
  1. 💡 **Ý nghĩa kết quả:** Giải thích ngắn gọn ý nghĩa sản xuất.
  2. 🔍 **Chi tiết so sánh:** Liệt kê rõ điểm giống nhau (vật liệu, chiều dày, phôi phẳng) và điểm khác nhau.
  3. 📋 **Khuyến nghị tiếp theo:** Hướng dẫn hành động cụ thể cho kỹ sư.
  - Tuyệt đối loại bỏ các mã nội bộ (VERIFIED_SAME, B-Rep, hash, exception, anchor) khỏi thông báo chính. Mọi thông số kỹ thuật chuyên sâu được bảo toàn bên trong nút **"Chi tiết kỹ thuật"** (`btnTechnicalDetails`).
* **Bố cục thanh lọc responsive:** Thanh lọc `filterBar` được kích hoạt `AutoScroll = true` và bổ sung bộ điều chỉnh chiều cao động (`updateFilterHeight`). Khi cửa sổ form bị thu nhỏ xuống kích thước tối thiểu ($1020 \times 640$), chiều cao dòng Ribbon tự động dãn từ 42px lên tương ứng và khung lọc cho phép cuộn mượt mà, không bao giờ để bất kỳ nút lọc nào bị che khuất hay tràn mép.

---

## 3. Bảo vệ Dự án & Đồng bộ Báo cáo (Stage 7 & 9)
* **Xuất Excel On-Demand:** Hoàn toàn giữ nguyên nguyên tắc không tự động xuất Excel. Chỉ xuất khi người dùng chủ động bấm "Xuất Excel..." và bấm Save. File Excel sử dụng chung snapshot và `ReportId` với UI, bảo toàn trung thực kết quả kiểm chứng mà không chạy lại so khớp hình học.
* **Bảo vệ mã nguồn ngoài phạm vi:** Không đụng chạm hay thay đổi các file thuộc tính năng CheckBalloon (`Commands/CheckBalloon.cs`, `Commands/ExcelBalloonCheckExporter.cs`, `UI/BalloonCheckReviewForm.cs`, ...) và `BomTaskPaneControl.cs`.
* **Giữ nguyên môi trường:** Giữ nguyên OutputPath `C:\Tao moi\ADDIN.dll`, không sửa file CAD gốc, không merge vào nhánh `main`.
