# Frame B và chẩn đoán callback

> **Lịch sử snapshot đầu R52:** Báo cáo này giữ kết quả tại thời điểm trước khi người dùng Build/test. Log native mới và bản sửa Caption được đối chiếu tại [follow-up](07_NATIVE_LOG_AND_CAPTION_FIX.md); dùng follow-up để đọc trạng thái hiện tại.

## Kết luận từ log được cung cấp

Người dùng cung cấp tóm tắt: MVID đúng R51, R50 ACTIVATED/SHOW, A count=1, A→B AUTO FOCUS, READY=False A-count=1 B-count=0; không có BUTTON 107/108, BACKEND BEGIN Preview hoặc AFJ-R51. Đây là `USER_LOG`, không phải log native R52 do harness tạo.

Ranh giới cuối được xác nhận là **A đã được nhận; chưa có B commit**. Log cũ không ghi mọi lần submit/accept/reject, cũng không kiểm tra kết quả native focus. Vì vậy không phân biệt được chưa tới submit box B, submit B bị từ chối, selection không commit đúng mark hoặc tiến trình kết thúc trước commit. Không kết luận người dùng đã/không click vào B.

Trong IL/source cũ, `OnSubmitSelection` thực sự gọi `IsSameDocumentActive`, resolve owner, `Selected(otherMark)`, `DefinitelyDifferent` và `Name2` trong lúc selection native đang xử lý. Đây là một điểm cần thu hẹp theo yêu cầu R52; **chưa có bằng chứng nó là nguyên nhân duy nhất làm mất B hoặc gây heap corruption**. R52 chuyển các kiểm tra COM này sang lượt xử lý đã post và thêm reason code.

## Mốc log R52

Mỗi log UI có sequence, epoch, owner/current thread, callback depth, cached focus/counts và trạng thái shown/closing/activated/faulted. Cache chỉ là dữ liệu callback/managed; COM counts sau callback được ghi riêng.

| Mốc | Nội dung / cách đọc |
| --- | --- |
| `COMMAND_OPEN`, `SHOW` | MVID thực trong Add-in đã Build; có/không bind callback Preview. |
| `SUBMIT_ENTER` / `SUBMIT_EXIT` | box/type, accepted và reason; chưa đồng nghĩa native đã commit. |
| `REJECT_BOX_ID`, `REJECT_FILTER_TYPE`, `REJECT_NULL_DISPATCH` | Sai ID/type/dispatch ngay từ tham số. |
| `REJECT_PAGE_STATE_OR_OWNER` | Page inactive/closing/faulted, epoch hoặc owner gate; xem trường managed trong cùng log. |
| `REJECT_NO_COMPONENT_OR_ENTITY_INTERFACE` | Dispatch không có interface hỗ trợ. |
| `REJECT_DUPLICATE_CACHED_REFERENCE` | Candidate trực tiếp trùng reference occurrence đã cache của ô kia. |
| `ACCEPT_FILTERED_PENDING_DEFERRED_IDENTITY` | Chấp nhận tạm; identity/deep checks còn ở lượt deferred. |
| `FOCUS_ENTER/EXIT`, `LIST_ENTER/EXIT` | ID focus và số selection do native callback cung cấp. Không COM getter ở callback. |
| `SELECTION_REFRESH_REQUEST/MERGED/RUN/REJECT` | Có post, coalesce hay gate làm mất refresh. |
| `AUTO_FOCUS_BEGIN/END/SKIP` | Có gọi focus B, giá trị GetSelectionFocus sau đó; vẫn chờ submit/list của B. |
| `SELECTION_READ` | COM thực sau callback: mark/count, resolved owner, route và Name2. |
| `READY` | Distinct pair và lý do; so callbackCounts với deferredCounts để tìm điểm không commit. |
| `BUTTON id=107/108` | Entry callback của nút; absence không chứng minh lỗi solver. |
| `ACTION_REQUEST/RUN/REJECT/END`, `POST_REQUEST/RUN/END` | Kiểm tra action bị từ chối trước/sau posting, stale epoch hoặc UI fault. |
| `PREVIEW_SELECTION_REJECT`, `PREVIEW_CALLBACK_BEGIN/END` | Pair check trước callback hoặc callback bị ngắt. |
| `ENTRY_PREVIEW_BEGIN/END` | Chuyển tiếp qua Entry, token, candidate và status manager. |
| `SESSION_PREVIEW_ENTER`, `SESSION_REJECT`, `SESSION_GHOST_CALL` | Token/state/owner/Assembly gating trước Ghost. |
| `GHOST_ENTER`, `SOLVER_BEGIN/END` | Đã tới backend và solver; kept counts, removed volumes. |
| `DISPLAY_CONTEXT`, `DISPLAY3_BEGIN/END`, `VIEW_STATE/VIEWPORT` | Transform, Component context, trạng thái API và viewport estimate. |
| `REDRAW_BEGIN/END`, `HIDE_BEGIN/END`, `AFTER_CLOSE` | Graphics/cleanup và body tracking. |

## Cách xác định chỗ dừng sau Build thủ công

1. Có `AUTO_FOCUS_END B_focus=True`, nhưng không có `SUBMIT_ENTER box=102`: ranh giới native selection B chưa tới handler; chưa xét solver.
2. Có submit B nhưng accepted=False: dùng reason cụ thể, không dự đoán từ hình ảnh.
3. Accepted=True nhưng thiếu `LIST_ENTER box=102 count=1` hoặc `SELECTION_READ mark=2 count=1`: chưa xác minh commit/mark; lưu toàn bộ log liền mạch.
4. READY=True nhưng không có BUTTON: callback nút chưa tới handler. Có BUTTON nhưng ACTION_REJECT: xem gate. Không ép chạy backend khi pair chưa hợp lệ.
5. Có SOLVER_END nhưng không thấy hình: xem Display3 status, viewport, redraw và xác nhận hiển thị thực; không gán PASS từ body tracking.

## Thoát tiến trình

Windows có hai cặp sự kiện Application Error/WER lúc 11:28 và 11:35 ngày 2026-10-22 (+07), ghi `SLDWORKS.exe` 32.5 và `0xC0000374` (heap corruption). Các event được giữ sau khi loại đường dẫn. Không có dump/call stack liên kết một callback hoặc Boolean cụ thể với sự kiện này. Exit `0xffffffff` trong log người dùng và event heap corruption là hai quan sát; **chưa xác minh chúng thuộc chính lần chạy nào**. Không đổi thành kết luận lỗi file Part, solver hoặc UI.

Tài liệu chính thức: [OnSubmitSelection](https://help.solidworks.com/2019/english/api/swpublishedapi/solidworks.interop.swpublished~solidworks.interop.swpublished.ipropertymanagerpage2handler9~onsubmitselection.html), [selection marks / MultiSelect2](https://help.solidworks.com/2016/english/api/sldworksapi/solidworks.interop.sldworks~solidworks.interop.sldworks.imodeldocextension~multiselect2.html), [GetSelectionFocus](https://help.solidworks.com/2022/English/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.IPropertyManagerPageSelectionbox~GetSelectionFocus.html).
