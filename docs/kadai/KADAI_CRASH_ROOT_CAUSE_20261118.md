# Kadai — điều tra native heap corruption, 18/11/2026

## Kết luận

Đã phân biệt **hai đường native heap corruption** bằng dump và đối chứng:

1. Khi thoát SolidWorks trống: vòng đời **ICommandManager của XToolProParallelTest**. Không cần mở CAD hoặc nạp code Kadai. Tắt riêng XTool làm phiên đối chứng thoát bình thường; bật lại XTool làm lỗi tái hiện. Tắt riêng TAI TOOL vẫn lỗi.
2. Khi audit/mở Part, XTool đã tắt: **TAI TOOL `MarkHighlightManager.RefreshNow → Describe` chạy chen vào `OpenDoc6`**, lỗi native release khi CLR kiểm tra COM interface của một Feature. Đây không phải stack shutdown của CommandManager.

Đây là kết luận cho signature heap/COM shutdown đã phân tích. Không gộp access violation `0xc0000005` ở thử nghiệm Modeler ban đầu, mọi lỗi hình học, hoặc mọi crash trước đây thành cùng một nguyên nhân.

Không sửa hoặc thay DLL XTool, TAI TOOL hay KIKUKAWA CAD. Không thay logic lệnh khác. Production Apply/Export của Kadai vẫn khóa.

## Môi trường và binary

- SolidWorks Professional 2024 SP5.0; executable/interop cài đặt `32.5.0.48`.
- Kadai branch: `feature/kadai-parametric-panel-ribs-20261117`; code nền đã kiểm thử: `7139a704d0e9082fa4f245a5ae696c8f8119a03f`.
- Commit công cụ điều tra và kết quả đối chứng: `ce3aea441e1240f105fcd91e16c50b0b6dc25756`. Không deploy commit này vào DLL production.
- Module liên quan: `%LOCALAPPDATA%\XTool\XToolPro-Parallel-Test\XToolProParallelTest.dll`.
- Assembly: `XToolProParallelTest, Version=3.3.46.222, Culture=neutral, PublicKeyToken=null`.
- MVID: `50f769b6-9e8c-48a2-8c7b-ba82855121b5`.
- SHA256: `F4487AAEAB428BB462AE3BE711947BC493CF1295C8A6AED2C574FD8D4DA83A55`.
- GUID khởi động XTool: `{A9E338DE-6599-4F46-B7E0-64654FBA27F8}`. Máy còn một key thừa dấu `}`; hai giá trị được sao lưu và thay đổi cùng nhau trong phép thử, sau đó hoàn nguyên.

Danh sách assembly được đọc trực tiếp bằng SOS `!dumpdomain`, không suy từ biểu tượng Task Pane hoặc chỉ từ registry. TAI TOOL và KIKUKAWA CAD đều hiện diện trong đối chứng tắt XTool.

## Bằng chứng debugger

### Dump có sẵn lúc 06:32

Đã bảo toàn và phân tích mini dump PID 27820, process uptime 5 giờ 32 phút 55 giây. Module TAI TOOL đang dùng là DLL triển khai trên máy; không thấy module host Kadai trong dump. Sau khi nạp đúng symbol Microsoft, debugger nhận diện:

```text
HEAP_CORRUPTION_ACTIONABLE_BlockNotBusy_DOUBLE_FREE_c0000374_ucrtbase.dll!free_base
```

Stack đi từ `CAmApp::ExitInstance`, `CoEEShutDownCOM`, `RCWCache::ReleaseWrappersWorker`, `RCW::ReleaseAllInterfaces`, qua native SolidWorks/MFC và `ucrtbase!free_base`, tới `ntdll!RtlReportFatalFailure`.

Một dump lưu từ tháng 10 cũng có cùng chuỗi shutdown và bucket double free. Sự tương đồng này cho thấy signature đã tồn tại trước Kadai; không chứng minh binary XTool tháng 10 giống binary hiện tại.

### Full dump tái hiện mới, PID 45908

Mở SolidWorks bằng UI, không mở tài liệu và không gọi audit/preview/generator. Dùng ProcDump bắt first-chance `C0000374` khi đóng cửa sổ bằng Alt+F4. Full dump khoảng 2.2 GB chứa được dữ liệu RCW mà mini dump trước đó thiếu.

SOS `!DumpRCW` tại địa chỉ RCW lấy từ stack cho kết quả:

```text
COM interface: SolidWorks.Interop.sldworks.ICommandManager
Managed ref count: 2
Flags: IsDisconnected
Captured IUnknown vtable: sldappu!auCommandManager::classauCommandManager+0xa0
```

Đối tượng đang cleanup là CommandManager. Những tên export gần nhất như `swTaskpaneView_c::getAuDispatch` trên native stack không đủ để kết luận Task Pane của Kadai gây lỗi; SolidWorks không cung cấp private PDB trong phép điều tra này.

### Ảnh bộ nhớ trước khi thoát, PID 30268

Đọc full dump của phiên TAI TOOL đã tắt nhưng XTool còn bật:

- Không có instance `ADDIN.SwAddin`.
- Có một `SolidWorks.Interop.sldworks.CommandManagerClass`.
- `XToolPro.XCommandManager.CommandManagerHandler._cmdMgr` trỏ vào đối tượng này; `_addinID = 14`.
- `!gcroot` tìm được đường giữ đối tượng qua event sink `DestroyNotify` → `XToolPro.ShutDown.SolidWorksShutdownManager` → callback → `XToolPro.Integration` → `_cmdHandler` → `_cmdMgr`.

Đối chiếu hai dump **cùng PID 30268**: managed CommandManager trước khi thoát có RCW `0x2d245c50`; chính địa chỉ RCW `0x2d245c50` xuất hiện trong `RCW::ReleaseAllInterfaces` trên stack crash sau đó. Đây là liên kết trực tiếp giữa đối tượng do XTool giữ và RCW đang native-release khi lỗi xảy ra, không chỉ so sánh tên method ở hai process khác nhau.

Hai mini dump đối chứng dương PID 30268 và 43972 đều có cùng bucket double free và chuỗi `ExitInstance → CLR RCW cleanup` như full dump PID 45908.

Đường GC root còn đi qua một control KIKUKAWA và application RCW dùng chung. Vì vậy kết luận được giới hạn ở vòng đời CommandManager XTool trong cấu hình hiện tại; chưa thử XTool độc lập trên một máy sạch.

## Đối chứng thực tế

Các phiên được phân biệt theo PID. Chỉ debugger/monitor đúng PID do lượt kiểm tra mở. Không dùng `taskkill`, không mở CAD gốc, không Save. Bảng bật/tắt là trạng thái module đã xác nhận trong phiên, không chỉ trạng thái checkbox.

| Phiên | TAI TOOL | XTool Parallel Test | KIKUKAWA CAD | Thao tác | Kết quả |
|---|---|---|---|---|---|
| 45908 | Bật | Bật | Bật | Mở SW trống → Alt+F4 | `C0000374`; full dump; RCW ICommandManager |
| 30268 | Tắt | Bật | Bật | Mở SW trống → Alt+F4 | `C0000374`; mini dump; trước đó ghi full dump để truy GC root |
| 46692 | Bật | Tắt | Bật | Mở SW trống → Alt+F4 | Process exit `0x00000000`; không có heap dump |
| 43972 | Bật | Bật lại | Bật | Mở SW trống → Alt+F4 | `C0000374` tái hiện; mini dump |
| 3904 | Bật | Tắt | Bật | Audit native bộ 22 Part bản sao | FAIL: hoàn tất 6/22, crash khi mở Part tiếp theo; stack MARK refresh |
| 39800 | Tắt | Tắt | Bật | Cùng audit 22 Part, rồi `ExitApp` qua API | PASS 22/22; dirty 0; open error 0; process exit `0x00000000` |
| 2292 | Bật, chỉ tách MARK observer trong process test | Tắt | Bật | Cùng audit 22 Part, rồi `ExitApp` qua API | PASS 22/22; dirty 0; open error 0; process exit `0x00000000` |

Thay đổi duy nhất giữa hai lượt đối chứng dương khi thoát và lượt thoát sạch là việc nạp XTool. Lỗi shutdown không cần panel, Cut, Flat Pattern, preview, Drawing hay code Kadai để tái hiện. Tuy nhiên tắt XTool **không đủ** để bảo đảm audit an toàn: lượt PID 3904 phát hiện đường lỗi thứ hai.

## Đường lỗi thứ hai: refresh MARK trong lúc mở tài liệu

Lượt PID 3904 dùng executable audit compile trực tiếp từ code đã commit, chỉ đọc các native Part `KADAI_TEST__` trong thư mục local. Không có host/candidate Kadai được nạp trong SolidWorks. XTool không có trong `!dumpdomain`; TAI TOOL và KIKUKAWA CAD có mặt.

ProcDump bắt `C0000374` lúc 07:01:53. Dù bucket double free giống nhau, stack thực tế khác hoàn toàn:

```text
ISldWorks.OpenDoc6 / native apiOpenDoc
  su_APIWaitForSingleObject / DispatchMessage
    WinForms InvokeMarshaledCallbacks
      ADDIN.Helpers.MarkHighlightManager.RefreshNow
        ADDIN.Helpers.MarkHighlightManager.Describe
          CLR JIT_IsInstanceOfAny / ComObject::SupportsInterface / SafeRelease
            native auFacet / auCmdTarget destructor / free_base
              RtlReportFatalFailure
```

Audit client nhận `0x800706BE` (RPC failed) sau khi SolidWorks chết; mã RPC này là hậu quả. Log xác nhận hoàn tất 6 Part rồi fail ở `OpenDoc6` tiếp theo. Không gán đây là lỗi Cut hoặc Sheet Metal chỉ vì file có các Feature đó.

Code MARK đang triển khai dùng callback được đưa vào hàng đợi WinForms; source tương ứng trong repo dùng `dispatcher.BeginInvoke(RefreshNow)` và duyệt Feature/COM trong `Describe`. Stack chứng minh callback có thể chạy trong message pump lồng của API mở tài liệu. Cùng thread UI vẫn có thể bị **reentrancy**; `BeginInvoke` không bảo đảm đã ra khỏi `OpenDoc6`.

Đã chạy hai đối chứng API tiếp theo, giữ nguyên hình học và nội dung audit:

- PID 39800: tắt cả TAI TOOL và XTool; đủ 22/22 Part, không dirty, không open error; `ExitApp` và process exit `0x00000000`.
- PID 2292: bật lại TAI TOOL, XTool vẫn tắt; host chẩn đoán có GUID riêng gọi đúng `MarkHighlightManager.Detach()` trong process test chưa có tài liệu, kiểm tra static `current` thành null. Không thay file DLL, không đổi lệnh TAI TOOL khác. Đủ 22/22 Part, không dirty, không open error; `ExitApp` và process exit `0x00000000`.

Như vậy có bằng chứng đối chứng hẹp cho MARK observer, không chỉ việc tắt toàn bộ TAI TOOL. Chưa có first-free stack để khẳng định chính callback này đã gây lần ghi hỏng bộ nhớ đầu tiên. Cần sửa và kiểm thử gate chống reentrancy theo vòng đời document; không xem `catch(COMException)` là biện pháp chữa native heap corruption. Mỗi cấu hình audit đối chứng mới chạy một lượt 22 Part, chưa phải stress test dài hạn.

## Cơ chế cleanup quan sát từ binary

Phân tích IL bằng Mono.Cecil chỉ đọc binary, không thực thi hoặc patch:

1. `Integration` tạo `CommandManagerHandler`, giữ application, add-in cookie và command manager/group.
2. Callback `DestroyNotify` gọi `ShutdownCore("DestroyNotify")`.
3. Nhánh này gọi `CommandManagerHandler.Cleanup(false)`.
4. `Cleanup(false)` đặt `_cmdGroup` và `_cmdMgr` thành null, rồi return; không vào nhánh `RemoveCommandGroup(2010)` và không có lời gọi release RCW trong method này.
5. Full dump chứng minh một RCW ICommandManager vẫn được CLR cleanup trong `CoEEShutDownCOM`; native release lúc này phát hiện double free.

Điểm cần sửa nằm ở quyền sở hữu và thứ tự teardown CommandManager/callback/group của XTool trước khi SolidWorks hủy native command infrastructure. Chỉ đặt field thành null không chứng minh native COM đã được giải phóng đúng thời điểm. Ngược lại, thêm `FinalReleaseComObject` vào application hoặc RCW dùng chung cũng chưa phải bản sửa đã được kiểm chứng.

**Giới hạn:** chưa có allocation/first-free stack hoặc private SolidWorks symbols để chỉ ra câu lệnh đã free block lần đầu. Chưa tạo hoặc kiểm thử bản sửa XTool. Vì vậy không khẳng định một lời gọi `ReleaseComObject` bổ sung đơn lẻ sẽ sửa triệt để lỗi native. Bằng chứng hiện tại xác nhận module/đối tượng/vòng đời và điều kiện tái hiện, chưa xác nhận patch.

## Ảnh hưởng đến Kadai và hướng xử lý

- Không sửa thuật toán Cut/Sheet Metal để xử lý signature shutdown này: đối chứng không cần chạy thuật toán vẫn lỗi.
- Có thể dùng phiên kiểm thử Kadai không nạp XTool để tiếp tục xác minh CAD; đây là cấu hình đối chứng, chưa phải chứng nhận toàn bộ add-in hoạt động ổn định cùng nhau.
- Một bản sửa XTool cần được làm và kiểm thử riêng: nhận diện owner của CommandManager/group/callback; teardown trên thread SolidWorks đúng thời điểm; thử unload/reload, đóng SW trống, đóng sau CAD, và lặp nhiều chu kỳ. Giữ app/RCW dùng chung an toàn.
- MARK cần chặn duyệt COM khi đang open/close/rebuild tài liệu, hủy callback cũ theo vòng đời document và chỉ đọc lại khi model đã ổn định. Chạy cùng thread UI hoặc dùng `BeginInvoke` riêng lẻ không đáp ứng điều kiện này. Chưa thay logic MARK của DLL đang triển khai.
- P4, native PMP, regression các lệnh cũ và các gate sản xuất khác trong báo cáo chính vẫn chưa hoàn tất. Không tự bật Apply khi chỉ hết signature crash này.

## Bằng chứng riêng và bảo toàn dữ liệu

Toàn bộ dump, IL, module inventory, GC roots, log ProcDump và audit CAD nằm local trong `kadai-local/diagnostics/20261118-root-cause/`, ngoài Git. Không đưa dump, CAD, ảnh nội bộ hoặc vendor DLL lên public repository.

Đã sao lưu và xác minh SHA256 13 AutoRecover trước khi đóng Welcome. SolidWorks tự dọn các recovery đang hoạt động sau lượt thoát sạch; sau cùng đã phục hồi đủ **13/13** từ bản sao và kiểm tra hash. Kết quả kiểm tra 110 CAD gốc trong lượt điều tra: **110 so sánh, 0 thay đổi**.

## Kết quả xác nhận cuối

- Compile các công cụ chẩn đoán với Interop SW2024: PASS.
- Guard PID của audit và control: PASS, PID sai bị từ chối trước khi mở tài liệu/thoát app.
- Numeric planner: PASS 30 behavioral checks; build Debug/Release x64: PASS, không deploy hoặc COM-register production DLL.
- Hai lượt audit API 22 Part đối chứng: PASS; audit baseline MARK bật: FAIL 6/22, giữ nguyên log và dump.
- Hoàn nguyên 3 startup values về đúng giá trị **và kiểu DWORD** ban đầu; gỡ CLSID host chẩn đoán: PASS.
- AutoRecover phục hồi và đối chiếu hash: 13/13 PASS. Không overwrite recovery khác nội dung.
- Các process test cuối đã thoát qua `ExitApp`. Không đóng hoặc kill process nền không thuộc lượt kiểm tra.
- Không sửa runtime production; không bật Apply/Export; không merge main.

Các file code thay đổi chỉ phục vụ chẩn đoán Kadai: `tools/KadaiNativeAudit.cs` (PID guard và log trước OpenDoc), `tools/KadaiSwControl.cs` (identity/exit có kiểm tra PID và không cho exit khi còn document), `tools/KadaiMarkIsolationHost.cs` (đối chứng observer trong process test). Host không đăng ký startup, không tự chạy khi dùng add-in bình thường. Hướng dẫn vận hành cần thiết nằm trong phần dưới.

## Dùng lại công cụ chẩn đoán

Build các file tool bằng C# compiler .NET Framework với Interop SW2024 cài trên máy; chúng không nằm trong production add-in. Lưu binary, CAD và output ngoài Git. Trước khi chạy audit, đặt `KADAI_EXPECTED_SW_PID` bằng PID của phiên test và chỉ truyền đường dẫn bản sao.

`KadaiSwControl identity <PID>` đọc identity/số document; `exit-isolated <PID>` chỉ gọi `ExitApp` nếu PID khớp và không có document mở. Nó không gọi Save hoặc force-close tài liệu. Người điều tra vẫn phải chọn đúng process test.

`KadaiMarkIsolationHost` chỉ dành cho chẩn đoán, có GUID riêng `{498861B9-CC08-4EDC-8F89-AAB32448DE79}`. Đặt `expected-pid.txt` cạnh host DLL trước khi gọi `LoadAddIn`. Host từ chối nếu PID khác, có document mở, hoặc không tìm thấy observer MARK đang tồn tại. Lượt kiểm tra đã dùng đăng ký COM HKCU tạm rồi xóa; không chạy RegAsm cho production ADDIN. Tách observer chỉ có hiệu lực trong process test đó; phiên mới dùng DLL nguyên vẹn và hoạt động observer ban đầu.
