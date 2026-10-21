# PR #37 — điều tra riêng crash khi đóng Check Chấn đang Run

**Cập nhật lượt kế tiếp từ baseline `68049e1`:** [nghiệm thu sau sửa log harness](POST_LOGFIX_NATIVE_ACCEPTANCE_20261020.md). XL-02 đã qua idle/running close → Disconnect → ExitApp, exit 0. Phiên bốn ca tiếp theo vẫn heap crash, có dump đúng PID 24660, tại independent audit XL-19 sau khi XL-17 hoàn tất chức năng. Không thay kết luận lịch sử của PID 73032 bằng dump PID mới. Registry trước/sau hai phiên mới không khác; mismatch cũ dưới đây vẫn chưa xác định. **Heap chưa được chứng minh đã sửa.**

**BLOCKED: chưa xác định nguyên nhân heap corruption; chưa sửa COM lifetime của sản phẩm.** Tiếp tục từ `e0adf89195f8d2daf15984814270aa226b0d307a`. Không tạo PR/Phase, không merge, không chạy 33 Part hoặc Assembly 120/25. [Kết quả máy đọc được](ConsolidatedResults/close-crash-20261020.json).

## Bằng chứng đúng PID và giới hạn một lượt

PID **73032** còn ownership log, lifecycle checkpoint, Windows Event 1000 và WER report xác nhận `0xc0000374`. Fatal dump tại đường dẫn WER tạm không còn. **Không sử dụng dump PID 11408 hay PID khác để kết luận nguyên nhân của 73032.** Log cũ chỉ xác định giai đoạn đóng trong Run, chưa có stack chỉ ra lệnh/lifetime gây heap corruption.

Đã thực hiện **đúng một** lượt đối chiếu được cấp phép, PID mới **16928**, XL-02 có sẵn, Release/x64, SOLIDWORKS 2024 SP5 `32.5.0.48`. Mục đích là đóng sau Run hoàn tất, mở lại, rồi đóng trong Run. Dùng đúng DLL có trace, cùng điều kiện owned session/CommandInProgress/XToolPro session-only isolation đã ghi; không thay CAD.

Run đầu nhận đúng FeatureId **53/59**, Min **2/2 đạt**, khớp evaluator cũ; mỗi nếp giữ đủ 18 static/process/Planner candidates và UNKNOWN. Numeric công ty **0**. Đã ghi 52 assertions trước khi diagnostic dừng.

**Lượt đối chiếu bị FAIL bởi harness**, chưa tới hành động đóng chủ động: `MarkCloseProbe(IDLE_CLOSE_BEGIN) → AcceptanceAddin.Write → File.WriteAllText` ném `IOException` sharing violation vì parent đang `File.ReadAllText` cùng file trạng thái. Không có heap dump mới, không tái hiện được `0xc0000374`. Không biến lỗi file này thành giải thích cho heap corruption cũ.

Cleanup sau lỗi đã thực sự đi qua đóng Form idle → DisposeBackend/session → target Disconnect → ExitApp: target detach/borrowed host checks **PASS**, exit **0**. Đây chỉ là quan sát cleanup; **không phải** paired idle/running-close PASS. Bước đóng đang Run chưa chạy. Không khởi động lại native sau giới hạn một lần.

## Rà luồng và các thay đổi chính xác

- `UI/BendAssemblyBatchController.cs:48`: trace opt-in `ADDIN_BEND_CLOSE_TRACE=1` tại FormClosing, Cancel, RunAsync continuation/finally, CloseWhenSafe callback và DisposeBackend. Không thay điều kiện chờ, timer interval, thứ tự hủy/dispose, timeout hoặc STA dispatch.
- `UI/BendAssemblyBatchLiveBackend.cs:32`, `Commands/BendCheckSession.cs:267`: trace ranh giới Dispose và các bước cleanup, giữ nguyên thứ tự. Trace native cleanup cho thấy XL-02 `copy=False`; không có CloseDoc của working copy tại checkpoint đó. Không coi điều này là chứng minh toàn bộ COM lifetime đúng.
- `Tests/BendToolProfiles/BendAssemblyUiTests.cs`: ba test phân biệt idle, đang chờ ReadMinimum với operation null, và Close/reentrant completion trong native Pump. Xác minh backend chưa bị dispose khi read/Pump còn hoạt động, được dispose đúng một lần sau continuation trên STA. Managed test không thay native proof.
- `Tests/BendToolProfiles/NativeRepresentativeAcceptance.cs:24`: checkpoint managed flags trước/sau close; chế độ probe chỉ dùng XL-02, không tự chạy bốn ca còn lại.
- `Tests/BendToolProfiles/NativeTestProcess.cs:57`: opt-in thu **một heap dump đúng owned PID** tại C0000374, tránh hết quota vì first-chance AV khác. Không tăng deadline.
- `Tests/BendToolProfiles/AcceptanceEventFile.cs:14`: sửa riêng lỗi thu chứng cứ đã có stack — writer/reader phối hợp bằng mutex theo đường dẫn; chỉ giữ gate khi đọc/ghi log, không qua COM call. Parent/bridge cùng dùng protocol. Gate có giới hạn 5 giây, lỗi tiếp tục FAIL, không retry/nuốt exception. Final implementation được managed concurrency test, **chưa chạy lại native**. Hai prototype atomic-replace bị loại sau khi managed tests FAIL; không phải code bàn giao.
- `Tests/BendToolProfiles/Run-OwnedInprocessAcceptance.ps1`: giữ snapshot registry trước/sau để lần thu chứng cứ sau xác định được key khác nhau. **Chưa native-test** phần ghi snapshot mới.

Rà source: FormClosing giữ Form khi `running || pumping`; Cancel rút kết quả và yêu cầu dừng; RunAsync trở về owning STA; CloseWhenSafe chỉ post Close sau khi hai cờ đã hạ; DisposeBackend có cổng một lần. Native operation completions dùng asynchronous continuations; workers dùng managed DTO. Chưa có bằng chứng từ PID 73032 chứng minh một nhánh cụ thể trong chuỗi này làm hỏng heap. Vì vậy **không sửa theo suy đoán, không thêm catch/GC.Collect/ReleaseComObject để che lỗi**. TaskPane và UI layout không thay đổi.

## Kiểm thử cuối

| Hạng mục | Kết quả |
| --- | --- |
| Build sản phẩm Debug/Release x64 | **PASS** |
| Ba close boundary tests | **PASS** Debug 3 nhóm/18 assertions; Release 3/18 |
| Targeted controller regression | **PASS** Debug 7/78; Release 7/79 |
| Final coordinated event protocol | **PASS** 250 publications / 344 complete reads / 348 assertions, gồm kiểm tra writer chờ reader giữ gate |
| Input guards / wrapper result gate | **PASS** 11 assertions / 3 ca |
| XL-02 | **FAIL diagnostic**; Run checkpoint đúng; running-close **NOT RUN** |
| XL-17 / XL-19 / XL-32 / Assembly nhỏ | **NOT RUN**, vì XL-02 chưa qua cổng mở–Run–Cancel/Close–Disconnect–ExitApp đầy đủ |
| Shutdown PID 16928 | **PASS**, exit 0 trong cleanup; không sửa kết quả FAIL của PID 73032 |

DLL SHA256 (sản phẩm chỉ bổ sung trace, chưa có root-cause fix):

```text
Debug:   a677b3b752da62ac653c540b19c7094e4796ab0c0af62da3cc539e366743e936
Release: 94e1b6099090abfa4c5e34bfb9a65a5585f5c2ca534e8c1628ec14f585b01aa0
```

Release được xác nhận trong PID 16928, MVID `0fa55476-65f6-429e-801d-c452a5a9bd12`. Harness sửa sau đó không thay DLL sản phẩm; không gọi đó là native validation của bản sửa harness.

15 file CAD/Excel/fixture/cấu hình được so SHA, **0 thay đổi**; temporary COM registration đã xóa. Guard startup registry trả **mismatch**, nên không tuyên bố startup settings giữ nguyên: wrapper cũ không giữ hai snapshot, chưa xác định key/cause; không khôi phục registry bằng suy đoán. Không truy cập phiên người dùng. Không upload raw log/dump, CAD/Excel, tọa độ thiết kế hay khóa.

Kết thúc tại đây theo giới hạn đã giao. Còn thiếu **fatal stack đúng PID và đối chiếu native idle/running hoàn chỉnh** để sửa nguyên nhân có chứng minh. Check Min math, 18 Punch–Holder, thuật toán và UNKNOWN giữ nguyên; không có manufacturing PASS hoặc nghiệm thu native mới.
