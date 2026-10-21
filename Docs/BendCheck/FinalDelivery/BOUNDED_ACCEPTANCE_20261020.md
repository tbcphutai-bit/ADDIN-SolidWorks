# Check Chấn — lượt hoàn thiện và kiểm thử có giới hạn, PR #37

## Kết luận: BLOCKED

Build và regression phần giao diện đã sửa **PASS**. Native cuối **FAIL**: SOLIDWORKS bị heap corruption `0xc0000374` khi đóng Form trong lúc Run. Không có đủ cơ sở nghiệm thu ổn định native hoặc sản xuất. Không mở thêm phiên tìm PASS sau lỗi này.

Lượt này tiếp tục từ `54ccb90729f3a227261a107e4114e1c29572814f` trên cùng PR #37. Không tạo PR/Phase mới, không merge. Chỉ chọn 5 fixture đã lưu; không tạo Part mới, không chạy lại 33 Part hoặc Assembly 120/25. [Dữ liệu kết quả đã loại thông tin riêng tư](ConsolidatedResults/bounded-20261020.json) là bằng chứng của lượt này; các báo cáo cũ không được dùng thay những ca NOT RUN.

## Code đã hoàn thành

- `UI/BendAssemblyBatchController.cs`: Refresh, đổi phạm vi và xem minh họa dùng cùng biên xử lý lỗi invalidation. Rút kết quả cũ trước khi gọi backend; nếu lỗi thì hiển thị tiếng Việt, giữ exception trong chẩn đoán và cho phép thử lại. Regression tiêm `COMException` chứng minh exception không thoát khỏi callback và không công bố kết quả cũ. Đây **không phải** bản sửa gốc của heap corruption.
- `BendAssemblyUiTests.cs`: regression lỗi invalidation/recovery và selector `--controller-tests`, chỉ chạy các kiểm thử liên quan controller/Form.
- `NativeAcceptanceAddin.cs`, `NativeRepresentativeAcceptance.cs`: bridge kiểm thử gọi lifecycle add-in thật và nút Check Chấn hiện có trên STA trong PID sở hữu riêng. Dùng lại Part/Assembly từ manifest riêng; kiểm tra FeatureId/persistent ID bằng mặt trụ native độc lập, so Check Min với evaluator cũ, kiểm tra 18 ứng viên backend static/process và Planner; Run/warm/Refresh/Cancel/đóng-mở Form, Selected/Entire. Không truyền COM sang worker.
- `RepresentativeAcceptanceInput.cs`: khóa đúng 5 đầu vào có sẵn và SHA nguồn; từ chối thiếu/stale/sai danh tính. Dùng lại `MatrixInput.SameJsonFacts`, so chính xác mọi giá trị nhưng không so thứ tự thuộc tính object JSON. Không nới tolerance hình học hoặc thay đổi kết quả engine.
- `NativeConsolidatedAudit.cs` và các test project: hỗ trợ manifest giới hạn. Sau khi thấy server chết, bổ sung dừng sớm theo **Process handle sở hữu** thay vì đợi hết các ca chưa chạy. Nhánh dừng sớm mới đã build, **chưa replay native**; DLL sản phẩm không đổi sau native cuối.
- `Run-OwnedInprocessAcceptance.ps1`, `Test-OwnedInprocessResult.ps1`: truyền manifest và kiểm tra SHA bằng .NET ở cổng kết quả. Sửa lỗi kiểm thử Windows PowerShell con không tự tìm được `Get-FileHash` từ môi trường PowerShell 7; lỗi thiếu cmdlet trước đó có thể trả exit 0. Ba ca cổng kết quả hiện PASS, fail closed.

Không sửa Check Min math, bộ 18 Punch–Holder, collision/process/planner algorithms, WinForms Designer, command khác hay CAD/Excel gốc.

## Build và managed regression

| Hạng mục | Kết quả thực chạy |
| --- | --- |
| Product Debug / x64 | PASS |
| Product Release / x64 | PASS |
| Controller/Form Debug | PASS: 7 nhóm, 78 assertions, 0 lỗi |
| Controller/Form Release | PASS: 7 nhóm, 80 assertions, 0 lỗi |
| Manifest/stale/exact JSON comparison | PASS: 11 assertions; không tạo/mở CAD |
| Cổng kết quả wrapper | PASS: 3 ca startup fail / SHA sai / SHA đúng; không gọi SOLIDWORKS |
| Test bridge Release và runner Release cuối | BUILD PASS |
| Full managed / 33 Part / 120–25 / Debug native / legacy stress | NOT RUN trong lượt này, giữ phạm vi yêu cầu |

Số assertions controller khác nhau do số timer Pump thực thi, không phải bỏ test. Một lần rebuild runner đang chạy gặp khóa EXE; build runner cuối vào thư mục riêng đã PASS. Không thay DLL đang chạy.

DLL SHA256:

```text
Debug:   237c923d601f78f88f823a7a44372b5a502f816a9470dfcbbda0cd265e0b63e0
Release: e92205b52856caf53a305f2f7396ea01f72551d9527f303756c6e332faf3ceae
```

Native cuối xác nhận đúng đường dẫn Release riêng và MVID `16a49831-ecc0-4ea3-b683-adc5d05feb47` **bên trong SOLIDWORKS**, không chỉ đọc file trên đĩa. Các thay đổi sau lượt native chỉ thuộc runner/báo cáo, không thay source sản phẩm hoặc DLL này.

## Năm ca native — kết quả cuối

| Ca có sẵn | Kết quả toàn ca | Quan sát trong lượt cuối |
| --- | --- | --- |
| XL-02 — U, 4.200 mm | **FAIL** | Đúng FeatureId 53/59, persistent ID và kích thước native; Check Min 2/2 đạt, khớp evaluator cũ. Mỗi nếp có 18 static + 18 process và 18 Planner candidates. Punch/Holder/process UNKNOWN đúng. Cold/warm, Refresh, Cancel và chạy lại đã tới checkpoint hợp lệ; crash khi đóng trong Run, chưa hoàn tất mở/đóng lại Form. |
| XL-17 — U, 6.000 mm | **NOT RUN** | Dừng sau native crash; không lấy kết quả 33-ca cũ làm PASS mới. |
| XL-19 — Z, cần kiểm tra Min NG | **NOT RUN** | Kỳ vọng NG từ baseline; chưa được nghiệm thu bằng DLL cuối. |
| PART-3 — fixture XL-32 đã có, 3 nếp | **NOT RUN** | Không tạo fixture thay thế. |
| ASM-SMALL — Assembly đã có 3 component, Selected/Entire | **NOT RUN** | Hai phạm vi chưa chạy trong lượt cuối. |

Không có ca native nào hoàn tất toàn bộ tiêu chí; không diễn đạt các checkpoint riêng của XL-02 thành PASS toàn ca.

XL-02, đúng Release cuối; số đo đến checkpoint hiển thị/quan sát của harness:

| Lần chạy | Tổng (ms) | Native capture (ms) | Captures / hits | Numeric queries |
| --- | ---: | ---: | ---: | ---: |
| Cold | 2.725,94 | 429,20 | 1 / 0 | 0 |
| Warm | 1.021,29 | 0,0047 | 0 / 1 | 0 |
| Refresh | 1.211,00 | 97,67 | 1 / 0 | 0 |
| Sau Cancel | 1.086,21 | 0,0067 | 0 / 1 | 0 |

Đây là thời gian đọc/đánh giá thiếu chứng cứ/hiển thị, **không phải benchmark numeric collision**. Không suy ra mức tăng tốc engine từ 0 queries.

## Native: bằng chứng và giới hạn

Điều kiện: Windows, SOLIDWORKS 2024 SP5 `32.5.0.48`, phiên mới do harness tạo, ràng buộc đúng PID và thời điểm tạo; không fallback ROT sang phiên người dùng. Chờ `StartupProcessCompleted`; không tăng deadline 60 giây cho từng ca hoặc ngân sách solver. `CommandInProgress=true` cho automation sở hữu. Chỉ unload XToolPro trong PID thử; không đổi startup settings hằng ngày. Target được Connect qua bridge taskpane riêng, test cookie 0; đây không phải nghiệm thu đăng ký/triển khai production COM.

Có hai lần khởi chạy hữu hạn:

1. PID **11408**, lượt chẩn đoán: XL-02 dừng do so sánh chuỗi JSON của harness; các facts ghi ra trùng chính xác khi so theo cấu trúc. Sửa harness bằng bộ so sánh chính xác đã có, kiểm thử thứ tự thuộc tính và xác nhận vẫn từ chối thay đổi một giá trị numeric/reason/thứ tự array. Lượt này Disconnect/ExitApp **PASS**, exit 0. Thu được hai dump **first-chance** `0xc0000005`, stack `clr!SafeReleasePreemp → RCW::ReleaseAllInterfaces → RCW cleanup`. Không coi first-chance này là crash shutdown hoặc bằng chứng xác định chủ sở hữu COM gây lỗi. Thời gian có pause ghi dump không dùng làm benchmark.
2. PID **73032**, lượt cuối sau sửa harness, cùng DLL sản phẩm: qua checkpoint sau Cancel rồi chết trong bước đóng Form khi Run đang hoạt động. Windows Event 1000 lúc **20:30:10 +07:00, 20/10/2026**, fault PID **0x11D48**, exception **0xc0000374**, `ntdll.dll +0x118095`; Process handle trả **-1073740940**. `UnloadAddIn` và khôi phục `CommandInProgress` sau đó báo `0x800706BA` vì server đã chết; không gán RPC hậu quả này làm nguyên nhân gốc ở OpenDoc6/Name2. WER report được giữ riêng; dump heap của đúng PID cuối không còn tại đường dẫn WER tạm, không tráo dump PID khác vào làm bằng chứng.

Lifecycle cuối: **Connect PASS → Run checkpoint PASS → Close FAIL → Disconnect NOT RUN → ExitApp NOT RUN**; shutdown/overall **FAIL**. 160 assertions đã được ghi trước native failure; không phải 160 assertions của một bài nghiệm thu native hoàn tất. Log “TARGET_DISCONNECTED” của lần chẩn đoán không áp sang lượt cuối.

Lỗi native chưa sửa tận gốc. Việc XToolPro đã unload mà vẫn heap crash cho thấy điều kiện cô lập từng PASS trước đây **không đủ bảo đảm ổn định**. Không thay đổi ReleaseComObject/GC một cách suy đoán, không tự nhận đã chữa RPC/shutdown. Kết thúc native sau lượt cuối; không thử thêm để tìm PASS. Runner cuối đã có cổng phát hiện server chết sớm; không khẳng định đã native-test cổng mới này.

## Bảo vệ dữ liệu và 18 Holder

15 file nguồn/fixture/cấu hình đã so SHA trước/sau, **0 thay đổi**; startup settings giữ nguyên và COM registration riêng đã xóa. Không can thiệp phiên người dùng. Việc xác minh đầy đủ dirty/config/stamp sau đóng Form của lượt cuối **chưa hoàn tất**, vì server chết trước checkpoint đó; không thay bằng kiểm tra SHA để tuyên bố tất cả state invariants PASS. CAD, Excel, tọa độ thiết kế, handle, khóa xác minh và raw dump/log riêng không được commit.

Thư viện giữ nguyên 18/18 cặp. Inventory baseline vẫn là 5.572 Holder entities / 160 SPLINE; không chạy lại inventory khi source/library không đổi. **0/18 Holder có material roles được kỹ thuật xác minh; 0/18 ClampVerified; chưa có usable axial span/process được xác minh.** Punch KIK mở giữ nguyên, không tự đóng hoặc gán vùng vật liệu hợp lệ. Các đầu kẹp tham chiếu vẫn là CAD reference.

Company static/process numeric thực chạy: **0 queries**; Phase 4B không chọn dao sản xuất, không suy ra chuyển động từ Part cuối. Các UNKNOWN được giữ nguyên; chưa đủ dữ liệu để đánh giá một dao chung cho toàn Part. Đây không phải nghiệm thu 33 Part hoặc chứng nhận sản xuất.

Những xác minh thực tế cần người dùng/kỹ thuật cung cấp: phân loại đường bao vật liệu/lỗ/đường tham khảo của Holder; mốc mũi và mốc gá riêng cùng bằng chứng seating; chiều dài hữu dụng và mapping máy/khuôn; trạng thái phôi trước/sau và chuyển động/quy trình đã xác minh, gắn đúng revision/hash. Không đề xuất thêm Phase trong lượt bàn giao này.
