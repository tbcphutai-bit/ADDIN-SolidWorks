# Display3 và khả năng nhìn thấy Preview

## Trạng thái kiểm tra

**R52 chưa gọi solver/Body2/Display3 trong SOLIDWORKS thực và chưa đo viewport thực.** Người dùng chọn tiếp tục source/DLL trước. Không tự khởi tạo SOLIDWORKS, tự load Add-in hoặc dùng computer use. Các kết quả R51 ở PR #42 là lần chạy harness trước, không được đổi nhãn thành R52 PASS.

Vì log mới chưa có B commit hoặc nút Preview, chưa có bằng chứng hai Body đã được Display3 rồi nằm ngoài vùng nhìn trong lần chạy đó. Khả năng offset làm khó quan sát chỉ là giả thuyết cần đo khi backend thực sự được gọi.

## Component và transform

Chuỗi tọa độ R51 giữ nguyên:

1. Solver giữ Body trong tọa độ Part; lấy transform occurrence đã chọn từ Assembly.
2. `World(result, transform)` tạo bản sao world.
3. `DisplayCandidateBodies` copy world, apply world shift **Z +0.120 m**.
4. Apply inverse transform occurrence để về Part-local.
5. `Display3(selectedComponent, color, swTempBodySelectOptionNone)` đặt body qua đúng Component context.

Không apply thêm transform Component sau bước 5. Body phải temporary; original Part không bị Cut/Save/Hide. Selected Component top-level, visibility/suppression, Part document, bound active Assembly và transform vẫn dùng các gate R51. `shown` đăng ký reference trước khi vào Display3 để cleanup còn giữ được object khi API lỗi. `IsShowing` là có tracked body, **không phải body được chứng minh nhìn thấy**.

R52 thêm `DISPLAY_CONTEXT` và group `A_KEEP`, `B_KEEP`, `A_CUT`, `B_CUT` để đối chiếu transform/Part/candidate. `DISPLAY3_END status=0` chỉ là trạng thái API; lỗi vẫn fail như trước. `REDRAW` không activate document khác; cleanup `Hide(part)` tiếp tục giữ reference nếu Hide lỗi.

## Chẩn đoán viewport được thêm

Sau shift world, trước inverse Part, đọc body box và `ActiveView.Transform`, `GetVisibleBox`, Scale2, EnableGraphicsUpdate. Tám góc box được project để ghi `OVERLAPS_WINDOW_ESTIMATE` hoặc `OUTSIDE_WINDOW_ESTIMATE`. Body box chỉ dùng chẩn đoán hiển thị; không tham gia xác định vùng cắt. Perspective/thiếu dữ liệu/lỗi diagnostic cho `UNVERIFIED` và không làm thay đổi cutter hoặc nuốt lỗi Display3.

Các giới hạn: body box là bao ngoài, không phải footprint solid chính xác; coordinate viewport/PMP/HiDPI chưa được hiệu chuẩn trong phiên native; pixel overlap không kiểm tra occlusion, clipping hoặc transparency. Mọi log viewport vẫn có `actual_visibility=UNVERIFIED`. Test managed chỉ kiểm tra phép so rectangle, không xác minh getter native hoặc ảnh.

## Offset và nhận biết Preview

Giữ nguyên vị trí +120 mm và màu xanh/cam KEEP, hồng/tím CUT. Không tự zoom, dịch camera, đổi offset hoặc thêm command thứ hai khi chưa có kiểm chứng native. Với hướng nhìn gần trục Z, dịch Z có thể ít thay đổi tọa độ màn hình; với view khác có thể dịch ra khỏi vùng zoom. Đây là nhận định hình học tổng quát, **không phải đo được trong lần chạy người dùng**.

Sau Build thủ công, cần kiểm tra group/Component, `Display3` status và view log rồi xác nhận người dùng nhìn thấy hai Frame Preview khác với Frame gốc. Nếu chỉ có status=0/tracked body thì chưa đạt tiêu chí nhìn thấy.

Tham khảo chính thức: [ModelView.Transform](https://help.solidworks.com/2019/english/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.IModelView~Transform.html), [GetVisibleBox](https://help.solidworks.com/2019/english/api/sldworksapi/solidworks.interop.sldworks~solidworks.interop.sldworks.imodelview~getvisiblebox.html).
