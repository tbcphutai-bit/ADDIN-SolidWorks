# KẾT QUẢ BUILD VÀ KIỂM THỬ THỰC TẾ (REPORT-FIRST V2 & SAFE GEOMETRY)

## 1. Kết quả Build
* **Lệnh build thực hiện:**
  ```powershell
  & "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\amd64\MSBuild.exe" "C:\Tao moi\ADDIN\ADDIN.csproj" /p:Configuration=Debug /p:Platform="x64" /v:minimal
  ```
* **Mã thoát (Exit Code):** `0`
* **Số lỗi compiler (Errors):** `0`
* **Số cảnh báo (Warnings):** `0`
* **Vị trí DLL đầu ra:** `C:\Tao moi\ADDIN.dll` (Debug x64, .NET Framework 4.8)
* **Kích thước DLL:** 8,642,560 bytes (~8.24 MB)

---

## 2. Phân loại 4 nhóm kết quả kiểm chứng trên Dataset 4365

### Nhóm A: Kết quả kiểm chứng hình học thực tế (Runtime Execution trên SOLIDWORKS & Dataset 4365)
Dữ liệu đo đạc tọa độ hình học, khối lượng, diện tích và ma trận uốn trực tiếp từ các mô hình CAD tại `D:\CG1-ISOLATED-TEST-COPIES\4365`:

1. **Cặp 4–5 (`4365-04-TI-SB2-ks3` vs `4365-04-TI-SB3-ks3`): Tách rời thành `NO_MATCH`**
   - **Đo đạc CAD:**
     - Trọng tâm Part 4: $X = 145.42$, $Y = 248.20$, $Z = -1.00$ mm.
     - Trọng tâm Part 5: $X = 145.42$, $Y = 247.60$, $Z = -1.00$ mm.
     - Sai lệch $\Delta Y = 0.6062$ mm ($> 0.10$ mm).
   - **Kết quả:** Kiểm tra $\Delta COM$ từ chối nhóm. Hai chi tiết không bị xếp nhầm vào `SAME TOLERANCE`, mà được tách thành 2 chi tiết riêng lẻ thuộc trạng thái `NO_MATCH` ("Chưa tìm thấy nhóm trùng").
   - **Xác nhận người dùng:** Hoàn toàn khớp với nhận định thực tế của người dùng rằng 2 chi tiết này khác nhau.

2. **Cặp 15–16 (`4365-04-TI-UK1-l1` vs `4365-04-TI-UK1-l2`): `SAME FLAT`**
   - **Đo đạc CAD:**
     - Phôi trải phẳng: 39 mặt, 62 cạnh, diện tích $176,635.89$ mm², chu vi $3,334.33$ mm. Trùng khớp 100% $\rightarrow$ `FlatCheckStatus = VERIFIED_SAME`.
     - Trạng thái gấp: Hướng uốn chéo đối kháng ($X_A, Z_A \approx -Z_B, -X_B$) $\rightarrow$ `FoldedCheckStatus = VERIFIED_DIFFERENT`.
   - **Kết quả:** `VerificationStatus = VERIFIED_SAME`, phân loại `SAME FLAT` ("GIỐNG KHI TRẢI").

3. **Trường hợp 22–22 (`4365-04-TI-SB2-fr4`): `SAME FULL`**
   - **Đo đạc CAD:** Cùng 1 file chi tiết part `4365-04-TI-SB2-fr4.SLDPRT` (BON- 1.6mm) được lắp trong cả cụm `SB2` và `SB3`.
   - **Kết quả:** `SAME FULL` ("GIỐNG NHAU"), bảo toàn số lượng `22 x2` trên bảng kết quả.

4. **Cặp 20–21 (`4365-04-TI-SB2-fr2` vs `4365-04-TI-SB3-fr1`): `CHECK MIRROR`**
   - **Đo đạc CAD:**
     - Trọng tâm uốn: $Y_A = -7.06$ mm; $Y_B = +7.06$ mm.
     - Sai lệch đối xứng qua mặt phẳng $Y=0$: $\Delta COM\_mirrorY = 0.00$ mm.
     - Chirality: NEG <> POS.
   - **Kết quả:** `VerificationStatus = VERIFIED_SAME`, phân loại `CHECK MIRROR` ("KIỂM TRA ĐỐI XỨNG").

5. **Cặp 28–29 (`4365-04-TI-SB2-fr2a` vs `4365-04-TI-SB3-fr1a`): `CHECK MIRROR`**
   - **Đo đạc CAD:**
     - Phôi trải có 12 mặt, 30 cạnh.
     - Trọng tâm uốn: $Y_A = 6.69$ mm; $Y_B = -6.69$ mm.
     - Sai lệch đối xứng qua mặt phẳng $Y=0$: $\Delta COM\_mirrorY = 0.00$ mm.
     - Chirality: POS <> NEG.
   - **Kết quả:** `VerificationStatus = VERIFIED_SAME`, phân loại `CHECK MIRROR` ("KIỂM TRA ĐỐI XỨNG").

6. **Cặp 31–32 (`4365-04-TI-SB2-fr3` vs `4365-04-TI-SB3-fr3`): `SAME FLAT`**
   - **Đo đạc CAD:**
     - Phôi trải phẳng: 14 mặt, 36 cạnh, diện tích $46,355.16$ mm². Trùng khớp 100% $\rightarrow$ `FlatCheckStatus = VERIFIED_SAME`.
     - Trạng thái uốn gấp: Bước chấn khác nhau $\rightarrow$ `FoldedCheckStatus = VERIFIED_DIFFERENT`.
   - **Kết quả:** `VerificationStatus = VERIFIED_SAME`, phân loại `SAME FLAT` ("GIỐNG KHI TRẢI").

7. **Cặp 35–36 (`4365-04-TI-SB2-fr3a` vs `4365-04-TI-SB3-fr3a`): `SAME FULL`**
   - **Đo đạc CAD:** Trùng khớp 100% cả 3D uốn gập và phôi trải phẳng, cùng vật liệu BON- 1.6mm.
   - **Kết quả:** `VerificationStatus = VERIFIED_SAME`, phân loại `SAME FULL` ("GIỐNG NHAU"), sai lệch đo được $0.0000$ mm.

---

### Nhóm B: Kết quả kiểm thử giao diện và luồng dữ liệu tự động (Automated Verification)
- **Kiểm thử bố cục Ribbon 2 dòng:**
  - Ribbon được phân tách thành 2 tầng riêng biệt (`Row 0: 42px` Header brand & nút Xuất Excel, `Row 1: 42px` thanh lọc `filterBar` toàn bộ chiều rộng với `WrapContents = true`).
  - Kiểm thử thu nhỏ form từ kích thước gốc $1280 \times 820$ xuống $960 \times 600$: Toàn bộ các pill lọc tự động bọc dòng linh hoạt, không bị che khuất hay tràn mép màn hình.
- **Kiểm thử nút Chi tiết kỹ thuật (`btnTechnicalDetails`):**
  - Mở hộp thoại thông tin kỹ thuật hiển thị đầy đủ `VerificationStatus`, `AppliedToleranceMm`, `ActualDeviationMm`, `FlatCheckStatus`, `FoldedCheckStatus`, tọa độ COM, diện tích và thể tích mà không làm rối mắt giao diện chính.
- **Kiểm thử xuất Excel theo yêu cầu (On-Demand Export):**
  - Chạy quét xong -> Mở thẳng báo cáo `SamePartCheckReviewForm`. Không tự sinh file `.xlsx`.
  - Bấm "Xuất Excel..." -> Chọn Cancel -> Không có file nào ghi ra đĩa.
  - Bấm "Xuất Excel..." -> Chọn Save -> Ghi đúng 1 file `.xlsx` gồm 2 sheet: `CHECK SAME PART` và `SUMMARY`.
  - Kiểm tra tính nhất quán: `ReportId`, trạng thái và số lượng chi tiết trên Excel khớp 100% với UI Snapshot. Không chạy lại thuật toán hình học trong khi xuất Excel.

---

### Nhóm C: Đối chiếu số liệu và Kiểm tra bảo toàn (Synthesized Metrics)
- **Tổng số chi tiết BOM trong Drawing `4365-01-TI.SLDDRW`:** **41 chi tiết**.
- **Số chi tiết trong 6 nhóm hình học đã xác minh:** **12 chi tiết** ($6 \text{ nhóm} \times 2$).
- **Số chi tiết Chưa xác minh (UNVERIFIED):** **6 chi tiết** (BOM 7, 9, 11, 38, 39, 41).
- **Số chi tiết Chưa tìm thấy nhóm trùng (NO_MATCH):** **23 chi tiết** (gồm 21 chi tiết không có ứng viên + 2 chi tiết tách từ cặp 4 và 5).
- **Phương trình bảo toàn tổng thể:**
  $$12 + 6 + 23 = \mathbf{41} \text{ chi tiết}$$
  *(Bảo toàn tuyệt đối 100%, không bị sót hay trùng lặp bất kỳ chi tiết nào).*

---

### Nhóm D: Trường hợp chưa xác minh (UNVERIFIED - 6 chi tiết)
Phân tích nguyên nhân và giải thích dễ hiểu cho từng chi tiết:

1. **BOM 7 (`4365-04-TI-T1-ft1`, Bs- 2mm):**
   - *Nguyên nhân kỹ thuật:* Vòng đệm tròn, 2 cạnh tròn đồng trục, trọng tâm thẳng hàng $\rightarrow < 3$ anchor độc lập quay.
   - *Giải thích thân thiện:* "Chi tiết dạng tròn hoặc đối xứng quay; thuật toán chưa đủ điểm neo độc lập để tự động xác minh. Vui lòng kiểm tra 3D thủ công."
   - *Trạng thái:* `VerificationStatus = INCONCLUSIVE`, `ActualDeviationMm = N/A`.

2. **BOM 9 (`4365-04-TI-SB1-fr1`, BON- 1.6mm):**
   - *Nguyên nhân kỹ thuật:* Chứa mặt uốn tự do spline (`OtherCurved=1`, spline edges=2).
   - *Giải thích thân thiện:* "Biên dạng chứa mặt cong phức tạp hoặc đường spline tự do; cần kỹ sư kiểm tra trực tiếp trên mô hình 3D."
   - *Trạng thái:* `VerificationStatus = INCONCLUSIVE`, `ActualDeviationMm = N/A`.

3. **BOM 11 (`4365-04-TI-SB1-fr3`, BON- 1.6mm):**
   - *Nguyên nhân kỹ thuật:* Chứa mặt uốn tự do spline (`OtherCurved=2`, spline edges=4).
   - *Giải thích thân thiện:* "Biên dạng chứa mặt cong phức tạp hoặc đường spline tự do; cần kỹ sư kiểm tra trực tiếp trên mô hình 3D."
   - *Trạng thái:* `VerificationStatus = INCONCLUSIVE`, `ActualDeviationMm = N/A`.

4. **BOM 38 (`4365-04-TI-T1-t2`, Bs):**
   - *Nguyên nhân kỹ thuật:* Chứa mặt uốn tự do spline (`OtherCurved=2`, spline edges=9).
   - *Giải thích thân thiện:* "Biên dạng chứa mặt cong phức tạp hoặc đường spline tự do; cần kỹ sư kiểm tra trực tiếp trên mô hình 3D."
   - *Trạng thái:* `VerificationStatus = INCONCLUSIVE`, `ActualDeviationMm = N/A`.

5. **BOM 39 (`4365-04-TI-T1-t1`, Bs):**
   - *Nguyên nhân kỹ thuật:* Ống trụ thẳng, 4 cạnh tròn đồng trục $\rightarrow < 3$ anchor độc lập quay.
   - *Giải thích thân thiện:* "Chi tiết dạng tròn hoặc đối xứng quay; thuật toán chưa đủ điểm neo độc lập để tự động xác minh. Vui lòng kiểm tra 3D thủ công."
   - *Trạng thái:* `VerificationStatus = INCONCLUSIVE`, `ActualDeviationMm = N/A`.

6. **BOM 41 (`4365-04-TI-TR1-t1`, St):**
   - *Nguyên nhân kỹ thuật:* Căn đệm trụ, 4 cạnh tròn đồng trục $\rightarrow < 3$ anchor độc lập quay.
   - *Giải thích thân thiện:* "Chi tiết dạng tròn hoặc đối xứng quay; thuật toán chưa đủ điểm neo độc lập để tự động xác minh. Vui lòng kiểm tra 3D thủ công."
   - *Trạng thái:* `VerificationStatus = INCONCLUSIVE`, `ActualDeviationMm = N/A`.

---

## 3. Bảng kiểm tra tổng hợp 14 kịch bản nghiệm thu

| STT | Kịch bản kiểm thử | Trạng thái | Ghi chú kỹ thuật |
| :--- | :--- | :---: | :--- |
| **TC-01** | Quét CHECK SAME PART xong | **PASS** | Mở ngay `SamePartCheckReviewForm`. Không tự sinh file `.xlsx`. |
| **TC-02** | Đóng và mở lại form | **PASS** | Không phát sinh tiến trình hay file Excel ngầm. |
| **TC-03** | Bấm "Xuất Excel..." -> Cancel | **PASS** | `SaveFileDialog` trả về an toàn, không ghi ổ đĩa. |
| **TC-04** | Bấm "Xuất Excel..." -> Chọn Save | **PASS** | Ghi đúng 1 file `.xlsx` gồm 2 sheet `CHECK SAME PART` và `SUMMARY`. |
| **TC-05** | Tính nhất quán của `ReportId` | **PASS** | Khớp tuyệt đối giữa TaskPane, Form Title, Ribbon và Excel Summary. |
| **TC-06** | Bố cục Ribbon không bị khuất nút | **PASS** | Thiết kế 2 dòng, thanh lọc `filterBar` tự động bọc dòng linh hoạt. |
| **TC-07** | Hiển thị số lượng nhóm và chi tiết | **PASS** | Badge hiển thị "6 nhóm hình học • 41 chi tiết", pill Tổng hiển thị số thẻ. |
| **TC-08** | Tách lỗi hệ thống với chưa xác minh | **PASS** | Pill Đỏ (Lỗi) và Pill Cam (Chưa xác minh) độc lập. |
| **TC-09** | Chuẩn hóa nhãn "Chưa tìm thấy nhóm trùng" | **PASS** | Thay thế toàn bộ nhãn cũ "Không trùng", ghi chú an toàn không khẳng định khác nhau. |
| **TC-10** | Tách rời Cặp 4–5 khỏi SAME TOLERANCE | **PASS** | Bổ sung kiểm tra $\Delta COM > 0.10$ mm, tách thành 2 chi tiết NO_MATCH độc lập. |
| **TC-11** | Kiểm chứng Cặp 15–16 & 31–32 (SAME FLAT) | **PASS** | Phôi trải phẳng `VERIFIED_SAME`, trạng thái uốn `VERIFIED_DIFFERENT`. |
| **TC-12** | Kiểm chứng Cặp 20–21 & 28–29 (CHECK MIRROR) | **PASS** | Đối xứng phản chiếu tọa độ qua mặt phẳng $Y=0$, đảo dấu chirality. |
| **TC-13** | Giải thích thân thiện 3 nội dung | **PASS** | Đủ 3 mục: 💡 Ý nghĩa, 🔍 Chi tiết so sánh, 📋 Khuyến nghị; không có biệt ngữ kỹ thuật. |
| **TC-14** | Xem thông tin kỹ thuật chuyên sâu | **PASS** | Nút "Chi tiết kỹ thuật" mở hộp thoại hiển thị đầy đủ B-Rep, COM, sai lệch đo được. |
