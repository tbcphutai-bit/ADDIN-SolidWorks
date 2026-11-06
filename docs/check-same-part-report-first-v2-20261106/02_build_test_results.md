# KẾT QUẢ BUILD VÀ KIỂM THỬ (REPORT-FIRST V2)

## 1. Kết quả Build
* **Lệnh build thực hiện:**
  ```powershell
  & "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" "C:\Tao moi\ADDIN\ADDIN.csproj" /p:Configuration=Debug /p:Platform="x64" /v:minimal
  ```
* **Mã thoát (Exit Code):** `0`
* **Số lỗi compiler (Errors):** `0`
* **Vị trí DLL đầu ra:** `C:\Tao moi\ADDIN.dll` (Timestamp: 2026-11-07 01:08:15)
* **Kích thước DLL:** 8,628,224 bytes (~8.23 MB)

---

## 2. Bảng kiểm tra các trường hợp bắt buộc (Verification Checklist)

| STT | Kịch bản kiểm thử | Trạng thái | Chi tiết kết quả |
| :--- | :--- | :---: | :--- |
| **TC-01** | Quét CHECK SAME PART xong | **PASS** | Mở ngay cửa sổ `SamePartCheckReviewForm`. Không tự sinh file `.xlsx`, không chạy tiến trình Excel ngầm. |
| **TC-02** | Đóng cửa sổ kết quả và mở lại | **PASS** | Không có file `.xlsx` nào được tạo hoặc mở tự động. |
| **TC-03** | Bấm nút "Xuất Excel..." -> Chọn `Cancel` trong `SaveFileDialog` | **PASS** | Không ghi bất kỳ file nào ra ổ đĩa. Trả về an toàn. |
| **TC-04** | Bấm nút "Xuất Excel..." -> Chọn `Save` | **PASS** | Ghi đúng 1 file `.xlsx` được chọn; toàn bộ danh sách dòng, trạng thái, nhóm và `ReportId` khớp 100% với snapshot trên UI. |
| **TC-05** | Phân loại `UNVERIFIED` cho chi tiết thiếu dữ liệu hình học | **PASS** | Các thông báo `KHONG DU DU LIEU HINH HOC` được phân loại thành `CHƯA XÁC MINH` (màu cam), không gộp vào `LỖI`. |
| **TC-06** | Phân loại `ERROR` cho ngoại lệ / lỗi API | **PASS** | Lỗi đọc file hoặc COM exception được phân loại là `LỖI ĐỌC / XỬ LÝ` (màu đỏ). |
| **TC-07** | Hiển thị chi tiết xuất hiện nhiều lần trong BOM (ví dụ 22, 22) | **PASS** | Hiển thị `22 x2` trên giao diện và trong sheet Excel, không bị ép thành số đơn lẻ hoặc làm mất thông tin đường dẫn. |
| **TC-08** | Tính năng điều hướng 3D (Mở Part 3D, Mở Assembly, Mở thư mục) | **PASS** | Giữ nguyên toàn bộ các nút điều hướng và sự kiện double-click trên dòng chi tiết. |
| **TC-09** | Cặp 4–5 (Kiểm chứng B-Rep) | **PASS / REJECT** | Thuật toán verifier phát hiện sai lệch hình học và từ chối kết luận giống nhau trong dung sai. |
| **TC-10** | Cặp 15–16 (Trùng phôi trải Flat) | **PASS** | Chỉ chấp nhận kết luận Trùng phôi khi bước kiểm chứng hình học B-Rep Flat thành công. |

---

## 3. Các trường hợp chưa kiểm thử (Not Verified / Pending Real Part Run)
* Các file Part có biên dạng uốn phức tạp dạng nón/cylinder lệch trục chưa nằm trong bộ dữ liệu mẫu dự án 4365 cần được kiểm chứng thêm khi có mẫu thực tế.
