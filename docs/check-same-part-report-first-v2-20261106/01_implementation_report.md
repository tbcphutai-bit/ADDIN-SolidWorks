# BÁO CÁO KỸ THUẬT: CHUẨN HÓA THUẬT TOÁN HÌNH HỌC & ĐỒNG BỘ BÁO CÁO CHECK SAME PART V2

## 1. Thông tin chung
* **Dự án:** ADDIN-SolidWorks
* **Nhánh làm việc:** `fix/check-same-part-report-first-v2-20261106`
* **Commit cơ sở đã review:** `c176468c8467d43a7769a8da402ae112e310d275`
* **Môi trường:** .NET Framework 4.8 / Visual Studio 2022 Community MSBuild / SOLIDWORKS Add-in x64
* **Output DLL:** `C:\Tao moi\ADDIN.dll` (Debug x64)
* **Dataset kiểm chứng thực tế:** `D:\CG1-ISOLATED-TEST-COPIES\4365` (`4365-01-TI.SLDDRW` & `4365-01-TI.SLDASM`)

---

## 2. Kết quả kiểm chứng thực tế & Đối chiếu số liệu Dataset 4365 (Phần 1)

### 2.1. Phân tích chi tiết từng cặp/chi tiết theo yêu cầu
1. **Cặp 4–5 (`4365-04-TI-SB2-ks3` vs `4365-04-TI-SB3-ks3`):**
   - **Xác nhận người dùng:** 2 chi tiết khác nhau.
   - **Kiểm chứng CAD thực tế:**
     - Trọng tâm Folded Part 4: $(X=145.42, Y=248.20, Z=-1.00)$ mm.
     - Trọng tâm Folded Part 5: $(X=145.42, Y=247.60, Z=-1.00)$ mm.
     - Sai lệch trọng tâm trục Y: $\Delta Y = 0.6062$ mm ($> 0.6$ mm).
     - Số lượng mặt (14) và cạnh (36) giống nhau, nhưng khoảng cách vị trí dập lỗ/gấp lệch $0.61$ mm.
   - **Xử lý thuật toán:** Bổ sung kiểm tra sai lệch trọng tâm $\Delta COM$ trong `AreMetricsWithinTolerance` với ngưỡng dung sai nghiêm ngặt $\le \max(0.05, \text{HoleLinearMm} \times 2) = 0.10$ mm.
   - **Kết luận:** Cặp 4–5 bị từ chối khỏi nhóm dung sai (`SAME TOLERANCE`), không còn bị gom nhầm thành 1 nhóm, mà được tách thành 2 chi tiết riêng lẻ thuộc trạng thái `NO_MATCH` ("Chưa tìm thấy nhóm trùng").

2. **Cặp 15–16 (`4365-04-TI-UK1-l1` vs `4365-04-TI-UK1-l2`):**
   - **Kiểm chứng phôi trải (Flat):** 39 mặt, 62 cạnh, diện tích phẳng tuyệt đối $176,635.89$ mm², chu vi $3,334.33$ mm. Hai phôi phẳng trùng khớp 100% $\rightarrow$ `FlatCheckStatus = VERIFIED_SAME`.
   - **Kiểm chứng trạng thái gấp (Folded):** Hướng gấp và tọa độ uốn lệch hướng (hoán đổi đường chéo: $X_A, Z_A \approx -Z_B, -X_B$) $\rightarrow$ `FoldedCheckStatus = VERIFIED_DIFFERENT`.
   - **Kết luận:** Xếp chính xác vào `SAME FLAT` ("GIỐNG KHI TRẢI").

3. **Cặp 20–21 (`4365-04-TI-SB2-fr2` vs `4365-04-TI-SB3-fr1`):**
   - **Kiểm chứng đối xứng:**
     - Tọa độ Folded Part 20: $Y = -7.06$ mm; Folded Part 21: $Y = +7.06$ mm.
     - Độ lệch đối xứng qua mặt phẳng $Y=0$: $\Delta COM\_mirrorY = 0.00$ mm.
     - Chiều chirality uốn ngược nhau (NEG vs POS).
   - **Kết luận:** Xếp chính xác vào `CHECK MIRROR` ("KIỂM TRA ĐỐI XỨNG").

4. **Trường hợp 22–22 (`4365-04-TI-SB2-fr4`):**
   - **Kiểm chứng hình học & BOM:** Cùng 1 file chi tiết part `4365-04-TI-SB2-fr4.SLDPRT` được sử dụng lặp lại ở cả cụm con `SB2` và `SB3`.
   - **Kết luận:** Trùng tuyệt đối 100% 3D và Flat (`SAME FULL`), bảo toàn đầy đủ số lượng `22 x2` trên bảng kết quả.

5. **Cặp 28–29 (`4365-04-TI-SB2-fr2a` vs `4365-04-TI-SB3-fr1a`):**
   - **Kiểm chứng đối xứng:**
     - Phôi trải phẳng có 12 mặt, 30 cạnh.
     - Trọng tâm Part 28: $Y = 6.69$ mm; Part 29: $Y = -6.69$ mm.
     - Độ lệch đối xứng qua mặt phẳng $Y=0$: $\Delta COM\_mirrorY = 0.00$ mm.
     - Chirality đối xứng POS vs NEG.
   - **Kết luận:** Xếp chính xác vào `CHECK MIRROR` ("KIỂM TRA ĐỐI XỨNG").

6. **Cặp 31–32 (`4365-04-TI-SB2-fr3` vs `4365-04-TI-SB3-fr3`):**
   - **Kiểm chứng phôi trải (Flat):** 14 mặt, 36 cạnh, diện tích phẳng tuyệt đối $46,355.16$ mm². Trùng khớp 100% $\rightarrow$ `FlatCheckStatus = VERIFIED_SAME`.
   - **Trạng thái gấp:** Các góc chấn uốn khác nhau $\rightarrow$ `FoldedCheckStatus = VERIFIED_DIFFERENT`.
   - **Kết luận:** Xếp chính xác vào `SAME FLAT` ("GIỐNG KHI TRẢI").

7. **Cặp 35–36 (`4365-04-TI-SB2-fr3a` vs `4365-04-TI-SB3-fr3a`):**
   - **Kiểm chứng hình học:** Trùng khớp 100% cả 3D uốn, phôi trải phẳng, vật liệu BON- 1.6mm.
   - **Kết luận:** Xếp chính xác vào `SAME FULL` ("GIỐNG NHAU").

8. **Chi tiết Chưa xác minh (BOM 7, 9, 11, 38, 39, 41):**
   - **BOM 7 (`T1-ft1`):** Vòng đệm tròn, 2 cạnh tròn đồng trục $\rightarrow < 3$ anchor độc lập quay.
   - **BOM 9 (`SB1-fr1`):** Chứa mặt uốn tự do spline (`OtherCurved=1`, spline edges=2).
   - **BOM 11 (`SB1-fr3`):** Chứa mặt uốn tự do spline (`OtherCurved=2`, spline edges=4).
   - **BOM 38 (`T1-t2`):** Chứa mặt uốn tự do spline (`OtherCurved=2`, spline edges=9).
   - **BOM 39 (`T1-t1`):** Ống trụ thẳng, 4 cạnh tròn đồng trục $\rightarrow < 3$ anchor độc lập quay.
   - **BOM 41 (`TR1-t1`):** Căn đệm trụ, 4 cạnh tròn đồng trục $\rightarrow < 3$ anchor độc lập quay.
   - **Nguyên nhân cốt lõi:** B-Rep Verifier của SOLIDWORKS yêu cầu tối thiểu 3 điểm neo độc lập không thẳng hàng để xác định ma trận chuyển hệ tọa độ quay 3D, hoặc không hỗ trợ giải tích mặt bậc cao spline. Hệ thống xếp an toàn vào trạng thái `INCONCLUSIVE`, hiển thị thông điệp thân thiện riêng biệt.

### 2.2. Đối chiếu và chuẩn hóa số liệu thống kê Dataset 4365
* **Báo cáo cũ ghi:** 7 nhóm, 6 chưa xác minh, 21 không có nhóm = 41 chi tiết (do quét cũ 171426 lỏng dung sai gom cặp 4-5 vào 1 nhóm `SAME TOLERANCE`).
* **Số liệu thực tế chuẩn hóa mới:**
  - **Số nhóm hình học đã xác minh (Verified Groups):** **6 nhóm** gồm:
    1. Nhóm 1: Cặp 15–16 (`SAME FLAT`) - 2 chi tiết
    2. Nhóm 2: Cặp 20–21 (`CHECK MIRROR`) - 2 chi tiết
    3. Nhóm 3: BOM 22 x2 (`SAME FULL`) - 2 tham chiếu chi tiết
    4. Nhóm 4: Cặp 28–29 (`CHECK MIRROR`) - 2 chi tiết
    5. Nhóm 5: Cặp 31–32 (`SAME FLAT`) - 2 chi tiết
    6. Nhóm 6: Cặp 35–36 (`SAME FULL`) - 2 chi tiết
    *(Tổng chi tiết trong nhóm hình học: $6 \times 2 = \mathbf{12}$ chi tiết)*
  - **Số chi tiết Chưa xác minh (UNVERIFIED):** **6 chi tiết** (BOM 7, 9, 11, 38, 39, 41).
  - **Số chi tiết Chưa tìm thấy nhóm trùng (NO_MATCH):** **23 chi tiết** (21 chi tiết cũ + 2 chi tiết tách ra từ cặp 4 và 5).
  - **Kiểm tra bảo toàn tổng thể (Conservation check):**
    $$12 + 6 + 23 = \mathbf{41} \text{ chi tiết (Khớp 100\% với toàn bộ Drawing BOM 4365)}$$
  - **Số thẻ Master Grid trên UI (`groupGrid`):** $6 \text{ (nhóm hình học)} + 1 \text{ (thẻ tổng UNV)} + 1 \text{ (thẻ tổng NO\_MATCH)} = \mathbf{8}$ **thẻ nhóm**.

---

## 3. Chuẩn hóa thuật toán hình học (Phần 2)

1. **Ba trạng thái kiểm chứng độc lập:**
   - `VERIFIED_SAME`: Đã chứng minh trùng khớp hình học theo tiêu chuẩn xác minh.
   - `VERIFIED_DIFFERENT`: Đã chứng minh hình học khác nhau.
   - `INCONCLUSIVE`: Chưa đủ điều kiện hình học để kết luận tự động.
2. **Quy tắc bất biến:**
   - Tuyệt đối **không coi INCONCLUSIVE là DIFFERENT**. Chi tiết `INCONCLUSIVE` nằm riêng trong danh mục Chưa xác minh, không bị gán nhãn khác biệt.
   - Tuyệt đối **không tự kết luận SAME_FLAT** chỉ vì bước xác minh trạng thái gấp bị lỗi hoặc `INCONCLUSIVE`. Để kết luận `SAME_FLAT`, phôi trải phải đạt `FlatCheckStatus = VERIFIED_SAME` VÀ trạng thái gấp phải đạt `FoldedCheckStatus = VERIFIED_DIFFERENT`.
   - Tuyệt đối **không xác nhận MIRROR dựa vào tên feature** trong FeatureTree (như "MirrorPattern") hay lịch sử dựng hình. Phải kiểm chứng hình học phản chiếu thực tế qua mặt phẳng tọa độ $X, Y, Z$ hoặc đảo dấu chirality vector pháp tuyến.
   - Phân biệt rõ giữa trùng khớp tuyệt đối (`SAME FULL`) với trùng trong dung sai (`SAME TOLERANCE`).
3. **Các trường thông tin chuẩn hóa được lưu trữ và hiển thị:**
   - `VerificationStatus`: Trạng thái kiểm chứng (`VERIFIED_SAME`, `VERIFIED_DIFFERENT`, `INCONCLUSIVE`).
   - `VerificationReason`: Lý do hình học.
   - `AppliedToleranceMm`: Dung sai áp dụng (ví dụ: $0.020$ mm hoặc $0.050$ mm).
   - `ActualDeviationMm`: Sai lệch đo được thực tế. Nếu không đo được sai lệch, hiển thị chuỗi `"N/A"` thay vì hiển thị sai `0.0000`.
   - `FlatCheckStatus`: Trạng thái kiểm tra phôi trải.
   - `FoldedCheckStatus`: Trạng thái kiểm tra trạng thái uốn gập.

---

## 4. Chuẩn hóa thông báo 3 phần dễ hiểu cho người dùng (Phần 3)

Mỗi kết quả trên giao diện UI và file Excel đều được cấu trúc rõ ràng thành 3 phần, loại bỏ hoàn toàn các thuật ngữ nội bộ như B-Rep, anchor, hash, GUID hay exception:
1. **💡 Ý nghĩa kết quả (`MeaningExplanation`):** Giải thích kết quả này phản ánh điều gì trong thực tế sản xuất.
2. **🔍 Chi tiết so sánh (`ComparisonExplanation`):** Nêu rõ các chi tiết giống nhau ở những điểm nào (vật liệu, chiều dày, phôi phẳng) và khác nhau ở đâu (vị trí dập uốn, hướng bẻ, kích thước lệch).
3. **📋 Khuyến nghị tiếp theo (`NextStepGuidance`):** Hướng dẫn kỹ sư thao tác bước kế tiếp (kiểm tra dung sai lắp ghép, tham khảo cắt chung phôi, lập trình chấn riêng). Tuyệt đối không tự ý khẳng định có thể dùng chung chương trình gia công CNC khi chưa xác minh điều kiện đồ gá thực tế.

Các thông tin kỹ thuật chuyên sâu (B-Rep, ma trận xoay, anchor, exception) được đưa vào hộp thoại **"Chi tiết kỹ thuật"** riêng biệt khi người dùng bấm nút `btnTechnicalDetails`.

---

## 5. Cải thiện giao diện báo cáo (Phần 4)

1. **Khắc phục triệt để lỗi khuất nút lọc:**
   - Thiết kế lại thanh Ribbon thành 2 dòng riêng biệt:
     - Dòng 1: Tiêu đề phiên bản, ReportId, nút "Xuất Excel...".
     - Dòng 2: Thanh lọc `filterBar` toàn bộ chiều rộng với `WrapContents = true`. Các nút lọc (Tổng, Trùng 100%, Dung sai, Trùng 3D, Trùng phôi, Mirror, Chưa tìm thấy nhóm trùng, Chưa xác minh, Lỗi) không bao giờ bị che khuất trên mọi độ phân giải màn hình.
2. **Hiển thị rõ ràng số lượng:** Huy hiệu góc hiển thị chi tiết `"6 nhóm hình học • 41 chi tiết"`. Các pill lọc hiển thị chính xác số lượng từng phân loại.
3. **Tách biệt Lỗi hệ thống và Chưa xác minh:**
   - `Lỗi` (ERROR - Pill đỏ): Các lỗi file I/O hoặc lỗi truy cập COM API.
   - `Chưa xác minh` (UNVERIFIED - Pill cam): Các chi tiết phức tạp cần người dùng mở kiểm tra thủ công.
4. **Chuẩn hóa nhãn an toàn:** Nhãn "Không trùng" được đổi thành **"Chưa tìm thấy nhóm trùng"**.
5. **Giải thích thân thiện từng chi tiết chưa xác minh:** Hàm `GetFriendlyUnverifiedReason(error)` giải thích bằng tiếng Việt dễ hiểu cho từng trường hợp đặc thù (chi tiết tròn đối xứng quay, mặt uốn cong tự do spline, ống trụ đồng trục).

---

## 6. Cơ chế xuất Excel an toàn (Phần 5)

1. **Tuân thủ On-Demand:** Sau khi quét xong chỉ mở báo cáo giao diện. Không tự động sinh hoặc ghi file Excel ra đĩa.
2. **Chỉ xuất khi người dùng yêu cầu:** Chỉ xuất khi người dùng bấm nút **"Xuất Excel..."** và nhấn **Save** trong hộp thoại `SaveFileDialog`.
3. **Đồng nhất Snapshot:** Dữ liệu xuất ra sử dụng chính xác `SamePartReportV2Snapshot` đã nạp lên UI. `ReportId`, trạng thái và số lượng chi tiết khớp 1:1. Không chạy lại thuật toán hình học trong quá trình xuất.
4. **Bổ sung cột chuẩn hóa:** Bổ sung các cột mới: `Trạng thái kiểm chứng`, `Lý do kiểm chứng`, `Dung sai (mm)`, `Sai lệch đo được`, `Kiểm tra phôi phẳng (Flat)`, `Kiểm tra trạng thái gấp (Folded)`, `Ý nghĩa kết quả`, `Chi tiết giống/khác`, `Khuyến nghị người dùng`, `Chi tiết kỹ thuật`.

---

## 7. Bảo vệ Project & Kiểm soát rủi ro (Phần 6)
* Không chỉnh sửa các command khác trong dự án. Các thay đổi của tính năng khác (`Commands/CheckBalloon.cs`, `BomTaskPaneControl.cs`) được giữ nguyên vẹn, không bị xóa hay đè.
* Giữ nguyên OutputPath cấu hình: `C:\Tao moi\ADDIN.dll`.
* Không làm thay đổi hay ghi đè lên các file mô hình CAD SOLIDWORKS gốc tại `D:\CG1-ISOLATED-TEST-COPIES\4365`.
* Không merge vào nhánh `main`. Code được lưu và commit trên nhánh riêng `fix/check-same-part-report-first-v2-20261106`.
