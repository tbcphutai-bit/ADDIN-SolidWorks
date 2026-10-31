# R60 — sửa crash tại ActivateDoc3 khi tạo Cut thật

Ngày: 2026-10-28. Đã chép trực tiếp bản đã qualification vào source chính, chỉ thay hai class hiện có: `AutoFrameJointGhostPreview.cs` và `AutoFrameJointSelectionPage.cs`. Giữ nguyên cơ chế A/B và command hiện tại; không Build/Deploy Add-in chính hoặc sửa project.

Source GhostPreview dùng cho API qualification: SHA256 `EF7197D1C7188ECD308076A026B3A9DA17463B732DEA380E44FCB3BDDEF00F46`. SelectionPage có native idle barrier: SHA256 `27BE94F7A1D12FB25EB1C90733068259E9326BF6BEAC1340F41C1D0A5F13914E`. Các ca API hình học trực tiếp dùng bản UI single-post trước đó (`AB456...A5CF`); không coi chúng là bằng chứng timing cho UI idle barrier mới.

## Crash người dùng gửi đã xác định được gì

DLL đã rebuild, SHA256 `61463528AC731C2921F033C4088904F02C0DA417C8F148928D8013B8A9E774AB`, MVID `9423a9df-74e5-4b35-befc-c68db4d08afd`; đây là DLL mới có source R59, không phải DLL cũ bị nạp nhầm.

Cặp người dùng chọn là A=`4332-04-TI-P-1-fr5-3`, B=`4332-04-TI-P-1-fr3a-4`, thứ tự đảo so với thử 4332 ở R59. Preview đã chạy thành công qua API. Writer tạo bốn Sketch guide và Sketch lát cắt đầu tiên, rồi crash **trước Cut đầu tiên** khi `ActivateDoc3` chuyển Assembly sang Part B. Log có `BEGIN ACTIVATE B.ACTIVATE_DOC3` nhưng không có END; exception `0xC0000005` đọc địa chỉ `0x10` trong `clr.dll`.

Source và log xác nhận Apply vẫn chạy đồng bộ bên trong callback native `AfterClose`. DLL đầy đủ còn có callback đổi ActiveDoc cập nhật TaskPane; các lỗi WPF binding gần thời điểm crash là tương quan cần lưu ý, **chưa chứng minh TaskPane là nguyên nhân duy nhất**. Không có căn cứ kết luận CAD hỏng. Xem [bằng chứng tóm tắt sạch](CRASH_DIAGNOSIS.json).

## Sửa luồng thay vì bắt AccessViolation rồi chạy tiếp

- SelectionPage chỉ bỏ tham chiếu PMP và post việc sau đóng trang trong `AfterClose`. Apply/Hide/dọn session chỉ được arm sau native `OnIdleNotify`, rồi chạy ở posted message trên đúng owner STA, giữ nguyên cặp A/B và revision đã chốt. Native idle callback chỉ arm/post; không thực hiện model API trong callback này. Session/page ownership được giữ tới khi Apply và cleanup hoàn tất. Cancel không Apply; không có fallback gọi native inline khi dispatcher không dùng được.
- GhostPreview giữ Assembly active, vào edit context của đúng Component bằng `EditPart2`, chọn đúng Sketch của occurrence rồi tạo Cut. Sau đó `EditAssembly` trả lại context. Không chuyển active document sang Part độc lập tại ranh giới đã crash.
- Cut trả về phải có native Part identity được kiểm tra ngay. Nếu `GetCorresponding2` trả null do Feature đã là object native của Part, chỉ chấp nhận khi tìm đúng ID trong Part tree và API object equality trùng khớp; không suy ownership từ tên Feature.
- Giữ nguyên Cut theo từng lớp tôn: NormalCut=true, Blind=t API +0,10 mm, OptimizeGeometry=false. Khe hở lắp 0,10 mm vẫn tách riêng với phần vượt chiều sâu. Không thêm Plane hoặc Convert/Fixed để khóa Sketch.

## Cách đọc kết quả kiểm thử

Xem thư mục [4332](4332_native02/README.md) và bộ [2354](2354_native01/README.md). Mỗi bộ có JSON, bảng test, từng Cut, kiểm tra Sketch tích lũy, vùng KEEP và thống kê activation path.

4332 đã qua đúng thứ tự A/B của người dùng: 8 Cut, 12 Sketch, cả bốn vùng không giao giữ nguyên vật liệu. Trace API có 8 lần tạo Cut trong edit context, 8 lần khôi phục Assembly và **0 lần ActivateDoc3 sang Part A/B**. Normal/Blind/depth/Optimize đọc lại; Cut thật khớp Body Preview.

2354 đã qua đủ bốn góc tuần tự trên cùng bản sao Assembly: 7+6+10+10=33 Cut; cuối cùng cả 49 Sketch vẫn ràng buộc đầy đủ, không dangling/overdefining, không Plane mới. Khe hở cuối 0,10 mm, giao thoa 0; Cut thật khớp Preview, lịch sử gốc và hash CAD nguồn/bản sao giữ nguyên. Trace có 33 lần tạo Cut trong occurrence edit context và 0 lần ActivateDoc3 sang Part A/B.

Bộ API trực tiếp chạy trong SOLIDWORKS từ source mới, không phải DLL Add-in đang cài. SelectionPage idle barrier có 12 kiểm thử managed bằng message queue owner STA và idle mô phỏng, chứng minh thứ tự deferred Apply/cleanup; **không dùng PASS managed để khẳng định crash native đã sửa**. Native PMP đã được kiểm tra riêng như phần dưới.

Tài liệu API [OnClose](https://help.solidworks.com/2025/English/api/swpublishedapi/SOLIDWORKS.Interop.swpublished~SOLIDWORKS.Interop.swpublished.IPropertyManagerPage2Handler9~OnClose.html) cho phép thực hiện các hành động được hỗ trợ trong AfterClose; không kết luận AfterClose luôn bị cấm. Idle barrier là tăng cường an toàn dựa trên phép thử timing thực tế.

Thử native PMP đầu tiên với bản chỉ BeginInvoke đã thất bại guard: callback AfterClose đã trả về, nhưng Close(true) còn trên stack và bơm posted message. Chưa chạy Cut trong phép thử đó; không tính PASS. Do đó bản mới chờ native OnIdleNotify và kiểm tra command state ngoài callback trước khi chạy model API. Bản idle barrier đã qua thử native PMP tiếp theo; xem phần kết quả riêng dưới đây.

Lần thử R60 đầu tiên đã tránh ranh giới crash nhưng bị chặn ở kiểm tra native identity vì `GetCorresponding2` null; Cut parameters/body lúc đó chưa được qualification và rollback chưa xác minh. Không tính lần này là PASS. Bản kế tiếp thêm kiểm tra object native đã trả về rồi chạy lại trên bản sao mới.

## Native PMP + DLL nền đầy đủ đã qua

Xem [bằng chứng native PMP/idle 4332](4332_native_pmp03/README.md) và [thứ tự callback/activation](4332_native_pmp03/R60_NATIVE_PMP_IDLE_PROOF.json). Trên đúng cặp đảo A/B của người dùng, bản candidate dùng SelectionBox native/mark 1 và 2, Close(true), callback AfterClose thật và **OnIdleNotify thật**. Native Close đã trả về trước Apply; không gọi idle giả hoặc lồng Application.DoEvents. Apply chạy trên đúng owner STA ở posted message sau idle, tạo 8 Cut thật, 12 Sketch đầy đủ ràng buộc; B-rep khớp Preview, bốn vùng KEEP giữ nguyên, không Plane mới, khe hở/giao thoa và hash CAD đạt.

DLL người dùng gửi (614635…4AB, MVID 9423…) được giữ nạp từ trước khi mở bản sao CAD, cùng TaskPane và event subscribers đầy đủ. Không unload hoặc nạp lại nó. **AFJ đang kiểm thử vẫn là code candidate**, chưa phải command AFJ trong DLL R60 do người dùng rebuild. Ghost ticket đã được kiểm tra trước khi mở trang chẩn đoán; không tuyên bố đã thử nút Preview, CommandEntry hoặc SessionManager của DLL đang cài.

Thời gian khoảng 37,8 giây của ca native PMP gồm cả chọn Frame, đóng trang, chờ idle và Apply; không gọi đó là thời gian riêng solver. Hai ca API trực tiếp tạo 41 Cut; ca native PMP tạo lại 8 Cut ở bản sao khác. Đếm riêng các sự kiện này, không biến thành 49 Cut mẫu hoặc thay kiểm kê 43 mẫu/42 before-after.

Hai class chính đã được chép dưới hash guard và có backup. Compile độc lập từ sáu source chính và sáu source branch đều PASS; DLL/PDB/project giữ nguyên. Branch giữ các nhãn/comment/cache UI sẵn có của nó, chỉ áp dụng vùng sửa R60. Người dùng tự rebuild; xem [hướng dẫn](MANUAL_REBUILD.md).

**Giới hạn:** rollback khi lỗi giữa chừng còn UNVERIFIED; thành công của các ca apply không chứng minh rollback. Không Save CAD gốc, không đưa CAD/log thô/đường dẫn máy/binary lên repository. Khả năng nhìn thấy Preview và hoạt động DLL/PMP sau rebuild phải được ghi đúng phạm vi đã chạy. Không tuyên bố đã xử lý mọi crash hoặc mọi biên dạng.
