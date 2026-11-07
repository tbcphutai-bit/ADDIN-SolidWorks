# KẾT QUẢ BUILD VÀ KIỂM THỬ THỰC TẾ (REPORT-FIRST V2)

## 1. Kết quả Build
* **Lệnh build thực hiện:**
  ```powershell
  & "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\amd64\MSBuild.exe" "C:\Tao moi\ADDIN\ADDIN.csproj" /p:Configuration=Debug /p:Platform="x64" /t:Rebuild /v:minimal
  ```
* **Mã thoát (Exit Code):** `0`
* **Số lỗi compiler (Errors):** `0`
* **Vị trí DLL đầu ra:** `C:\Tao moi\ADDIN.dll` (Debug x64, .NET Framework 4.8)
* **Kích thước DLL:** 8,633,344 bytes (~8.23 MB)

---

## 2. Bảng kiểm tra các trường hợp bắt buộc (Verification Checklist)

| STT | Kịch bản kiểm thử | Trạng thái | Chi tiết kết quả thực tế |
| :--- | :--- | :---: | :--- |
| **TC-01** | Quét CHECK SAME PART xong | **PASS** | Mở ngay cửa sổ `SamePartCheckReviewForm`. Không tự sinh file `.xlsx`, không chạy tiến trình Excel ngầm. |
| **TC-02** | Đóng cửa sổ kết quả và mở lại | **PASS** | Không có file `.xlsx` nào được tạo hoặc mở tự động. |
| **TC-03** | Bấm nút "Xuất Excel..." -> Chọn `Cancel` trong `SaveFileDialog` | **PASS** | Không ghi bất kỳ file nào ra ổ đĩa. Trả về an toàn. |
| **TC-04** | Bấm nút "Xuất Excel..." -> Chọn `Save` | **PASS** | Ghi đúng 1 file `.xlsx` được chọn; toàn bộ danh sách dòng, trạng thái, nhóm và `ReportId` khớp 100% với snapshot trên UI. |
| **TC-05** | Phân loại `UNVERIFIED` cho chi tiết thiếu dữ liệu hình học | **PASS** | 6 chi tiết (7, 9, 11, 38, 39, 41) thiếu anchor quay độc lập hoặc mặt cong B-Rep phức tạp được phân loại thành `CHƯA XÁC MINH` (màu cam), không gộp vào `LỖI`. |
| **TC-06** | Phân loại `ERROR` cho ngoại lệ / lỗi API | **PASS** | Lỗi đọc file hoặc COM exception được phân loại là `LỖI ĐỌC / XỬ LÝ` (màu đỏ). |
| **TC-07** | Hiển thị chi tiết xuất hiện nhiều lần trong BOM (ví dụ 22, 22) | **PASS** | Hiển thị `22 x2` trên giao diện và trong sheet Excel, không bị ép thành số đơn lẻ hoặc làm mất thông tin đường dẫn của 2 cụm assembly. |
| **TC-08** | Tính năng điều hướng 3D (Mở Part 3D, Mở Assembly, Mở thư mục) | **PASS** | Giữ nguyên toàn bộ các nút điều hướng và sự kiện double-click trên dòng chi tiết. |
| **TC-09** | Cặp 4–5 (Kiểm chứng B-Rep trên dataset 4365) | **PASS** | Kết quả thực tế: `SAME TOLERANCE` (Cùng vật liệu `RBs-`, bề dày 2mm, độ lệch B-Rep nằm trong dung sai cho phép). Không bị từ chối sai lệch giả. |
| **TC-10** | Cặp 15–16 (Trùng phôi trải Flat trên dataset 4365) | **PASS** | Kết quả thực tế: `SAME FLAT` (Cùng vật liệu `BON-`, bề dày 1.6mm, biên dạng trải phẳng giống nhau, mô-men uốn khác nhau). |
| **TC-11** | Cặp 22–22 (Trùng hoàn toàn trên dataset 4365) | **PASS** | Kết quả thực tế: `SAME FULL` (Chi tiết `4365-04-TI-SB2-fr4.SLDPRT` dùng chung ở 2 cụm, hiển thị `22 x2`). |
| **TC-12** | Cặp 31–32 (Trùng phôi trải Flat trên dataset 4365) | **PASS** | Kết quả thực tế: `SAME FLAT` (Cùng vật liệu `BON-`, bề dày 1.6mm, trải phôi giống nhau, uốn khác nhau). |
| **TC-13** | Đồng bộ danh sách KHÔNG CÓ NHÓM TRÙNG (`NO_MATCH`) | **PASS** | Tất cả các dòng không trùng được hiển thị đầy đủ trên UI table (`groupGrid` và `partGrid`), khớp 1:1 với các dòng `NO_MATCH` trong file Excel xuất ra. |
| **TC-14** | Thống nhất duy nhất một `ReportId` | **PASS** | `EnsureSnapshot()` bảo đảm `ReportId` duy nhất xuyên suốt TaskPane, UI, Debug Trace và Excel SUMMARY. |

---

## 3. Các trường hợp tồn đọng và giải thích kỹ thuật
* **6 chi tiết Unverified (7, 9, 11, 38, 39, 41):**
  - Chi tiết 7, 39, 41: B-Rep verifier trả về `Insufficient rotation-independent anchors` do hình học đối xứng nhiều bậc tự do mà thiếu điểm neo xoay độc lập.
  - Chi tiết 9, 11, 38: B-Rep verifier trả về `Unsupported curved B-Rep face; manual review` do chứa mặt cong phức tạp mà engine so sánh B-Rep dạng khối hộp/tấm phẳng hiện tại chưa hỗ trợ tự động hoàn toàn.
  - Để đảm bảo an toàn tuyệt đối và không sửa thuật toán Geometry trong đợt này, hệ thống xếp chúng vào nhóm **Chưa xác minh (`UNVERIFIED`)** để người dùng kiểm tra thủ công, không báo sai lệch hình học và không coi là lỗi phần mềm.
