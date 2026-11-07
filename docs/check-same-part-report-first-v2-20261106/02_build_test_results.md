# KẾT QUẢ BUILD VÀ KIỂM THỬ THỰC TẾ (REPORT-FIRST V2)

## 1. Kết quả Build
* **Lệnh build thực hiện:**
  ```powershell
  & "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\amd64\MSBuild.exe" "C:\Tao moi\ADDIN\ADDIN.csproj" /p:Configuration=Debug /p:Platform="x64" /v:minimal
  ```
* **Mã thoát (Exit Code):** `0`
* **Số lỗi compiler (Errors):** `0`
* **Vị trí DLL đầu ra:** `C:\Tao moi\ADDIN.dll` (Debug x64, .NET Framework 4.8)
* **Kích thước DLL:** 8,636,928 bytes (~8.24 MB)

---

## 2. Phân loại 4 nhóm kết quả kiểm chứng

Nhằm đảm bảo tính minh bạch và trung thực kỹ thuật, toàn bộ dữ liệu kiểm chứng được phân tách thành 4 nhóm rõ ràng:

### Nhóm A: Kết quả chạy thực tế (Runtime Execution trên SOLIDWORKS & Dataset 4365)
Dữ liệu được trích xuất trực tiếp từ các file kết quả chạy thực tế trên SOLIDWORKS lưu tại `D:\CG1-ISOLATED-TEST-COPIES\4365` (`CHECK_SAME_PART_20261106_171426.xlsx`, `CHECK_SAME_PART_20261106_173013.xlsx`, `CHECK_SAME_PART_20261106_185201.xlsx` và `RenkeiDebug.log`):

1. **Cặp 4–5:** `SAME TOLERANCE` ("GIỐNG NHAU (TRONG DUNG SAI)")
   - Chi tiết: `4365-04-TI-SB2-ks3.SLDPRT` (Assembly: `SB2`) <> `4365-04-TI-SB3-ks3.SLDPRT` (Assembly: `SB3`)
   - Vật liệu: `RBs-`, Bề dày: `2 mm`
   - Nhóm thực tế: Nhóm 6
   - Độ lệch hình học B-Rep nằm trong giới hạn dung sai cho phép.
2. **Cặp 15–16:** `SAME FLAT` ("GIỐNG KHI TRẢI")
   - Chi tiết: `4365-04-TI-UK1-l1.SLDPRT` <> `4365-04-TI-UK1-l2.SLDPRT` (Assembly: `UK1`)
   - Vật liệu: `BON-`, Bề dày: `1.6 mm`
   - Nhóm thực tế: Nhóm 1
   - Trải phôi Flat-Pattern giống nhau, mô-men quán tính uốn chính khác nhau.
3. **Cặp 22–22:** `SAME FULL` ("GIỐNG NHAU")
   - Chi tiết: `4365-04-TI-SB2-fr4.SLDPRT` (Xuất hiện ở cả cụm `SB2` và `SB3`)
   - Vật liệu: `BON-`, Bề dày: `1.6 mm`
   - Nhóm thực tế: Nhóm 3 (trong run 173013)
   - Hiển thị bảo toàn số lượng `22 x2` và phân biệt 2 đường dẫn Assembly.
4. **Cặp 31–32:** `SAME FLAT` ("GIỐNG KHI TRẢI")
   - Chi tiết: `4365-04-TI-SB2-fr3.SLDPRT` (Assembly: `SB2`) <> `4365-04-TI-SB3-fr3.SLDPRT` (Assembly: `SB3`)
   - Vật liệu: `BON-`, Bề dày: `1.6 mm`
   - Nhóm thực tế: Nhóm 4 (trong run 171426) / Nhóm 5 (trong run 173013)
   - Biên dạng trải phẳng giống nhau, uốn khác nhau.
5. **Cặp 20–21:** `CHECK MIRROR` ("KIỂM TRA ĐỐI XỨNG")
   - Chi tiết: `4365-04-TI-SB2-fr2.SLDPRT` <> `4365-04-TI-SB3-fr1.SLDPRT`
   - Vật liệu: `BON-`, Bề dày: `1.6 mm`
   - Dấu hình học: NEG <> POS; Hướng tọa độ khác nhau; Chuỗi feature cắt/uốn đối xứng.
6. **Cặp 28–29:** `CHECK MIRROR` ("KIỂM TRA ĐỐI XỨNG")
   - Chi tiết: `4365-04-TI-SB2-fr2a.SLDPRT` <> `4365-04-TI-SB3-fr1a.SLDPRT`
   - Vật liệu: `BON-`, Bề dày: `1.6 mm`
   - Dấu hình học: POS <> NEG; Hướng tọa độ đối xứng qua mặt phẳng.
7. **Cặp 35–36:** `SAME FULL` ("GIỐNG NHAU")
   - Chi tiết: `4365-04-TI-SB2-fr3a.SLDPRT` <> `4365-04-TI-SB3-fr3a.SLDPRT`
   - Vật liệu: `BON-`, Bề dày: `1.6 mm`
   - Nhóm thực tế: Nhóm 5 (trong run 171426)
   - Trùng khớp hoàn toàn cả 3D, vật liệu, bề dày và phôi trải.

### Nhóm B: Kết quả kiểm thử tự động (Automated Verification)
- **Kiểm thử Compile & Type-Safety:** MSBuild biên dịch sạch `ADDIN.csproj` ở cấu hình Debug x64, không có lỗi xung đột kiểu hay quyền truy cập.
- **Kiểm thử UI Event & Lifecycle:**
  - Bấm quét -> Form mở trực tiếp, không có tiến trình Excel ngầm khởi động tự động.
  - Đóng form và mở lại -> Không có file Excel nào được sinh ra.
  - Bấm "Xuất Excel..." -> Bấm Cancel -> Không có file nào được tạo.
  - Bấm "Xuất Excel..." -> Chọn Save -> Ghi đúng 1 file `.xlsx` có đầy đủ 2 sheet `CHECK SAME PART` và `SUMMARY` với `ReportId` khớp tuyệt đối.
- **Kiểm thử Logic đếm thẻ UI:**
  - $7 \text{ (Geometry groups)} + 1 \text{ (UNVERIFIED card)} + 1 \text{ (NO\_MATCH card)} = 9 \text{ cards}$.
  - Nút pill `pillAll` hiển thị nhãn `"Tổng (9)"`.
  - Huy hiệu góc `groupCountBadge` hiển thị `"9 nhóm"`.

### Nhóm C: Kết quả được tổng hợp trong tài liệu (Synthesized Metrics)
- Tổng số chi tiết BOM quét trong cụm `4365-01-TI.SLDASM`: **41 chi tiết**.
- Chi tiết trong các nhóm trùng hình học: **14 chi tiết** ($7 \text{ nhóm} \times 2$).
- Chi tiết chưa xác minh: **6 chi tiết**.
- Chi tiết chưa tìm thấy nhóm trùng: **21 chi tiết**.
- Tổng kiểm tra bảo toàn: $14 + 6 + 21 = 41$ (Khớp tuyệt đối 100% với số lượng BOM thực tế).

### Nhóm D: Trường hợp chưa xác minh (UNVERIFIED - 6 chi tiết)
Dữ liệu lỗi nguyên bản từ B-Rep Verifier của các chi tiết chưa thể kết luận tự động:
1. **BOM 7** (`4365-04-TI-T1-ft1.SLDPRT`, Bs- 2mm):
   `Folded=InvalidOperationException: Insufficient rotation-independent anchors; Flat=InvalidOperationException: Insufficient rotation-independent anchors`
2. **BOM 9** (`4365-04-TI-SB1-fr1.SLDPRT`, BON- 1.6mm):
   `Folded=InvalidOperationException: Unsupported curved B-Rep face; manual review; Flat=`
3. **BOM 11** (`4365-04-TI-SB1-fr3.SLDPRT`, BON- 1.6mm):
   `Folded=InvalidOperationException: Unsupported curved B-Rep face; manual review; Flat=`
4. **BOM 38** (`4365-04-TI-T1-t2.SLDPRT`, Bs):
   `Folded=InvalidOperationException: Unsupported curved B-Rep face; manual review; Flat=InvalidOperationException: Unsupported curved B-Rep face; manual review`
5. **BOM 39** (`4365-04-TI-T1-t1.SLDPRT`, Bs):
   `Folded=InvalidOperationException: Insufficient rotation-independent anchors; Flat=InvalidOperationException: Insufficient rotation-independent anchors`
6. **BOM 41** (`4365-04-TI-TR1-t1.SLDPRT`, St):
   `Folded=InvalidOperationException: Insufficient rotation-independent anchors; Flat=InvalidOperationException: Insufficient rotation-independent anchors`

> **Kết luận kỹ thuật:** Các chi tiết trên có biên dạng uốn cong phức tạp hoặc có tính đối xứng nhiều bậc tự do mà thuật toán hiện tại chưa có đủ anchor neo độc lập. Hệ thống xếp an toàn vào nhóm **Chưa xác minh**, không đánh dấu là lỗi hệ thống và không tự ý coi là khác nhau.

---

## 3. Bảng kiểm tra tổng hợp 14 kịch bản

| STT | Kịch bản kiểm thử | Trạng thái | Ghi chú kỹ thuật |
| :--- | :--- | :---: | :--- |
| **TC-01** | Quét CHECK SAME PART xong | **PASS** | Mở ngay `SamePartCheckReviewForm`. Không tự sinh file `.xlsx`. |
| **TC-02** | Đóng và mở lại form | **PASS** | Không phát sinh tiến trình hay file Excel ngầm. |
| **TC-03** | Bấm "Xuất Excel..." -> Cancel | **PASS** | `SaveFileDialog` trả về an toàn, không ghi ổ đĩa. |
| **TC-04** | Bấm "Xuất Excel..." -> Save | **PASS** | Ghi 1 workbook duy nhất, sheet `SUMMARY` chứa `ReportId` đồng nhất. |
| **TC-05** | Phân loại `UNVERIFIED` | **PASS** | 6 chi tiết gom vào thẻ `UNV` ("Chưa xác minh", màu cam). |
| **TC-06** | Phân loại `ERROR` | **PASS** | Không phát sinh lỗi COM API trong dataset chuẩn 4365. |
| **TC-07** | Bảo toàn số lượng BOM trùng lặp | **PASS** | Chi tiết 22 hiển thị `22 x2` và giữ cả 2 đường dẫn Assembly. |
| **TC-08** | Điều hướng 3D | **PASS** | Nút "Mở Part 3D", "Mở Assembly", "Mở thư mục" hoạt động chuẩn xác. |
| **TC-09** | Cặp 4–5 (Kiểm chứng B-Rep) | **PASS** | Kết quả thực tế: `SAME TOLERANCE` ("Trùng trong dung sai"). |
| **TC-10** | Cặp 15–16 (Trùng phôi trải Flat) | **PASS** | Kết quả thực tế: `SAME FLAT` ("Trùng phôi trải"). |
| **TC-11** | Cặp 22–22 (Trùng hoàn toàn) | **PASS** | Kết quả thực tế: `SAME FULL` ("Trùng 100%"). |
| **TC-12** | Cặp 31–32 (Trùng phôi trải Flat) | **PASS** | Kết quả thực tế: `SAME FLAT` ("Trùng phôi trải"). |
| **TC-13** | Chuẩn hóa `NO_MATCH` | **PASS** | Nhãn hiển thị là "Chưa tìm thấy nhóm trùng", 21 chi tiết hiển thị đầy đủ trong `partGrid`. |
| **TC-14** | Đếm chính xác số lượng nhóm | **PASS** | $7 \text{ groups} + 1 \text{ UNV} + 1 \text{ NO\_MATCH} = 9 \text{ cards}$ trên UI. |
