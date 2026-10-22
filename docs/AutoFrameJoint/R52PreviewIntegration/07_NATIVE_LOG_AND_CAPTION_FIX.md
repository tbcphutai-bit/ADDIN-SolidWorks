# Log native R52 và sửa Caption sau Preview

## Nguồn mới

Người dùng cung cấp ảnh Visual Studio dừng tại `label.Caption = text` trong `SetCaption`, exception `System.AccessViolationException`, và toàn bộ Output của PID 39072. Log được lọc lấy AFJ/error theo đúng thứ tự dòng, loại đường dẫn máy và đính kèm trong `evidence/caption_followup/`. Đây là log thực của người dùng, không phải phiên API do Codex tự chạy.

DLL hiện tại có MVID **`ae27a276-be1d-4959-8391-1b3d60f3b997`**, khớp log. 265/265 method AFJ khớp normalized IL của detached R52 local harness trước sửa Caption. Nó khác MVID `2f4e...` ở snapshot đầu R52 vì người dùng đã Build; không gộp hai snapshot.

## Luồng đã quan sát trong Add-in

1. Page đầu nhận A fr04 và B fr03: submit accepted, marks 1/2 đều count=1, auto Analyze hoàn tất. Page đóng sạch khi chưa có Preview.
2. Page thứ hai dùng lại selections đã có: callback counts cache vẫn -1/-1, nhưng COM selection reads thực là 1/1. Cache -1 không có nghĩa mất Frame B.
3. Nút **107 → P1**: solver END, keptA=1, keptB=1; hai Display3 KEEP trả 0. Caption 104 sau P1 vẫn cập nhật thành công.
4. Nút **108 → P2**: Hide hai body cũ có END, solver END và sáu Display3 KEEP/CUT trả 0. Caption sau P2 đầu vẫn thành công.
5. Nhấn **108 lần nữa**: sáu Hide có END, solver END, sáu Display3 trả 0, redraw và callback/backend END. Ready đọc lại đúng hai Frame. Ngay sau `CAPTION BEGIN id=104` có AccessViolation, không có CAPTION END. Process kết thúc `0xffffffff`.

Tổng cộng ba solve, 14 Display3 status=0 và 8 Hide kết thúc giữa các Preview. Mỗi solve bỏ khoảng **71.753,398 mm³ A** và **68.665,694 mm³ B**; rule `ORTHOGONAL_U_TALLER_COVER`, Frame ngắn A che, log solver báo gap 0,1 mm. Không xuất B-rep native mới hoặc bộ Check2/Check3 chi tiết từ phiên này; không thay log bằng dữ liệu R51 harness.

**Lỗi đã quan sát ở cập nhật nhãn sau Preview, không phải thiếu lệnh gọi Preview.** Nhãn đã hoạt động nhiều lần trước đó. Chưa biết label RCW/native lifetime hay hỏng bộ nhớ từ thao tác khác là nguyên nhân gốc; không quy kết Boolean, PIA version hoặc chiều dài text khi chưa có dump/call stack native.

## Bản sửa một class

Chỉ sửa `AutoFrameJointSelectionPage.cs` trong follow-up:

- Bỏ `SetCaption` ghi COM property, thay bằng `CacheStatusText` chỉ lưu managed dictionary và log. Không có `Label.Caption` hoặc `Control.Enabled` setter trong source/IL đã biên dịch.
- Label dựng trong `BuildControls` có chỉ dẫn tĩnh xem vùng Thông báo; marks, filters, các button ID, submit/list/focus/action/epoch và các helper backend không đổi.
- `PublishStatusMessage` dùng **`PropertyManagerPage2.SetMessage3`** của page hiện có, từ lượt ready được post sau action; không đọc/ghi native label RCW, không thêm cửa sổ hay command.
- Message chứa đầy đủ trạng thái và năm dòng geometry đã tính, giữ Unicode/multiline. Chuỗi không đổi được coalesce. Log `STATUS_CACHE`, `MESSAGE_BEGIN/END` và marker `status_channel=PAGE_MESSAGE` cho biết code mới.
- Nếu API message trả false hoặc ném **exception managed/COM thông thường**, chỉ tắt kênh message tới khi mở page mới; không block nút/solver, không fallback label hoặc retry. Không thêm cơ chế bắt/tiếp tục AccessViolation/hỏng heap native.

`SetMessage3` là API đặt vùng message của PropertyManager, có trạng thái visibility/expanded và giá trị trả về boolean theo [tài liệu SOLIDWORKS](https://help.solidworks.com/2020/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IPropertyManagerPage2~SetMessage3.html). Tài liệu không chứng minh nó an toàn nếu process đã bị hỏng bộ nhớ. Việc loại call site bị lỗi là bản sửa đường cập nhật UI; **chưa chứng minh đã loại nguyên nhân gốc heap corruption**.

## Kiểm tra và giới hạn

Hai biến thể source compile x64 độc lập cùng sáu class AFJ. **12/12 managed controls mỗi biến thể**, gồm toàn bộ chín kiểm tra cũ cộng chuỗi Unicode dài/geometry, message false và COM exception. Proxy label cố ý ném lỗi nếu bị gọi; thực tế có **0 label method/property invocations**. Preview delegate vẫn được gọi khi message bị tắt. Compiled IL có 0 setter Caption/Enabled và một call SetMessage3.

Source local được áp dụng bằng SHA guard, chỉ UI class thay đổi; project, DLL/PDB và năm class AFJ khác giữ nguyên. Không Build/Deploy, không điều khiển SOLIDWORKS đang debug, không Merge. Bản sửa mới **chưa chạy trong Add-in native**.

Viewport log mới chỉ cho bounding rectangle giao vùng view; A/B KEEP có phần nằm ngoài bounds. Điều này không chứng minh toàn bộ solid nhìn thấy, không đo occlusion và chưa hiệu chuẩn PMP/HiDPI. Chưa có ảnh CAD xác nhận hai Frame Preview. Không tự thay +120 mm hoặc zoom khi chưa kiểm chứng.

Sau Build thủ công cần thấy `status_channel=PAGE_MESSAGE`, không còn CAPTION BEGIN/END runtime, kiểm tra P1/P2 lặp lại, message kết quả và cleanup. Nếu vẫn có native lỗi bộ nhớ, cần giữ vị trí lỗi mới/dump; không đổi lỗi thành PASS.
