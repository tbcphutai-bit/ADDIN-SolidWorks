# PR #37 — nghiệm thu native sau sửa log harness

**BLOCKED — lỗi heap chưa hết.** Tiếp tục từ `68049e1e3ed420954bba7aa693ecba910fd6733b`, cùng branch/PR #37. Không sửa sản phẩm, Check Min, 18 dao, Collision Engine hoặc UI; không merge. [Kết quả máy đọc được](ConsolidatedResults/post-logfix-native-20261020.json).

## Kết quả năm trường hợp

| Trường hợp | Thực thi chức năng | Disconnect / ExitApp | Bằng chứng và giới hạn |
| --- | --- | --- | --- |
| XL-02, U 4200 | **PASS** | **PASS**, exit 0 | Một lượt riêng, PID 34396; idle-close rồi running-close, mở lại, Run/Refresh/Cancel, 168 assertions |
| XL-17, U 6000 | **PASS chức năng** | **FAIL ở cấp phiên chung** | Run/Refresh/Cancel, running-close và mở/đóng lại hoàn tất; trạng thái PASS và 7111.65 ms được khôi phục từ dump PID 24660 |
| XL-19, Z / Min NG | **FAIL native** | **FAIL ở cấp phiên chung** | Heap `0xc0000374` trong independent native audit, trước khi mở Check Chấn/Run cho XL-19; kết quả Min NG của ca này **NOT RUN** |
| XL-32, Part 3 nếp | **NOT RUN** | **NOT RUN** | Dừng sau fatal heap, không chạy tiếp |
| Assembly nhỏ, Selected/Entire | **NOT RUN** | **NOT RUN** | Dừng sau fatal heap, không chạy tiếp |

XL-02 đã qua toàn bộ cổng trước khi bắt đầu một phiên có điều kiện cho bốn ca còn lại. Không chạy lại XL-02 trong phiên đó. Tổng cộng hai phiên owned theo thứ tự này, không retry sau fatal heap. Không chạy 33 Part hoặc Assembly 120/25, không tạo fixture mới.

XL-17 PASS chức năng không đồng nghĩa cả phiên shutdown PASS. File progress cuối trên đĩa còn `Execution=NOT_RUN`: runner chỉ publish ở các checkpoint, chưa publish lại sau kết thúc XL-17 trước khi chuyển sang ca kế. Không sửa file chứng cứ này thành PASS. Đọc ngoại tuyến **chính dump 24660** bằng SOS xác nhận `representativeIndex=2`, `representativeStage=0`, tham số `item.CaseId=XL-19`; dictionary của XL-17 đã có `Execution`, `Cancel`, `CloseDuringRun`, `OpenClose` đều PASS. Số assertions trong dump là **167**; checkpoint trên đĩa trước đó là **160**. Đây là hai thời điểm của cùng phiên, không cộng chúng thành số test hoàn tất.

## DLL, harness và điều kiện thử

Sản phẩm Release/x64 được rebuild từ baseline trên; **cùng DLL** được xác nhận nạp trong cả hai PID:

```text
ADDIN.dll SHA256: ef78be68e8bc66294f1e794562ccf18ced48ebac4b55d6df1fda6aae04b80722
MVID:             4dff6174-11ad-4576-ae03-d6e10b0f22f6
SOLIDWORKS:       2024 SP5, 32.5.0.48
```

Harness/bridge dùng `AcceptanceEventFile` của baseline: reader và writer cùng phối hợp bằng mutex cho file log. Không gặp lại sharing violation đã chặn lượt trước. Bridge gọi target lifecycle và command Check Chấn thật trong process/STA, có TaskPane và Form thật, test cookie 0; đây **không phải nghiệm thu production COM deployment**. Giữ điều kiện `CommandInProgress` và unload XToolPro chỉ trong PID owned theo runner hiện có; không tự thay cấu hình startup trên máy. Temporary private COM registration được xóa sau cả hai phiên.

Running-close thực sự đi qua `running=true`, nhưng trace tại thời điểm close có `operation=null`: chưa có bằng chứng native cho việc đóng đúng lúc một `Pump` đang thực thi. Không dùng kết quả này thay cho mọi thời điểm có thể đóng trong quá trình capture.

Để tuân thủ không chạy lại XL-02, chỉ bổ sung gate chọn test trong bốn file harness: `RepresentativeAcceptanceInput.cs`, `NativeRepresentativeAcceptance.cs`, `NativeConsolidatedAudit.cs`, `NativeAssemblyBatchValidation.cs`. Gate `ADDIN_ACCEPTANCE_PASSED_XL02_AUDIT` yêu cầu audit XL-02 có functional/shutdown/close/cancel PASS và SHA DLL trùng; chỉ chọn tiếp index 1, không cung cấp bằng chứng sản xuất. Gate không thay đường đọc hình học hoặc lệnh sản phẩm.

| Build / managed test lượt này | Kết quả thực chạy |
| --- | --- |
| Product Release/x64, native runner và bridge Release/x64 | **PASS**; product còn các warning CS0649 có sẵn |
| Product Debug/x64 / native Debug | **NOT RUN** trong lượt nghiệm thu Release này; kết quả Debug cũ là lịch sử |
| Gate chọn bốn ca sau XL-02 | **PASS**, 7 assertions, không native/CAD call |
| AcceptanceEventFile hiện tại | **PASS**, 250 publications, 250 complete reads, 254 assertions, gồm exclusion khi reader giữ gate |
| Broad managed regression / 33 Part / 120–25 | **NOT RUN**, ngoài phạm vi lượt này |

Build harness ban đầu cho XL-02 và build có gate cho bốn ca sau khác nhau; sản phẩm không đổi:

| Binary | XL-02 SHA256 | Bốn ca sau SHA256 |
| --- | --- | --- |
| NativeAssemblyBatch.Validation.exe | `a77881390beae2d6758d3e5a331eb23d188c88779fe2c2c12dac290ba724a9f5` | `6ba28011c9f7df94a5c72350ea2ff37500273a48e7fdf04363c5aa1f33c57400` |
| CheckChan.NativeAcceptanceHost.dll | `99bcb068aa8ceb43372e02e261ff527d1d0049d378a51ce26aca8db560b7441b` | `13748ad8f4a951094a23f5d0eb5980cafcadb4f7326289d2ea82f2abf2566114` |

## Fatal heap đúng PID

Phiên thất bại: PID **24660** (`0x6054`), creation UTC **2026-10-20T15:18:25.0858814Z**. ProcDump gắn đúng owned PID, lọc `C0000374`, tối đa một dump. Exception lúc **2026-10-20 22:18:39 +07:00**; process exit **-1073740940**. Dump full-memory giữ riêng tại máy, **3,982,759,335 bytes**, SHA256:

```text
9428349f6906dfa2669ea8426f5f8e6f78260e6f8bbab36f2d344cbe38ee0853
```

Stack của chính dump này, lược bỏ địa chỉ:

```text
ntdll!RtlReportFatalFailure / RtlFreeHeap
ucrtbase!free_base
sldappu!auCmdTarget_c::~auCmdTarget_c
mfc140u!CCmdTarget::InternalRelease
slduiu!auFacet_c::GetThisClass
clr!SafeRelease / ComObject::SupportsInterface / ObjIsInstanceOf
clr!JIT_ChkCastAny
AcceptanceAddin.IndependentBendIds(ModelDoc2) + 0x1bf
AcceptanceAddin.AuditRepresentativePart(RepresentativeCase)
AcceptanceAddin.RepresentativeTick()
WinForms Timer callback, owning STA
```

JIT disassembly ánh xạ return offset `+0x1bf` tới **cast đối tượng trả về `face.GetFeature()` sang `SolidWorks.Interop.sldworks.Feature`**, tại `Tests/BendToolProfiles/NativeRepresentativeAcceptance.cs:76`. Lệnh gọi `GetDefinition()` nằm **sau** return point này; không quy lỗi cho các phép `is IOneBendFeatureData` ở dòng 77. Tại thời điểm fatal, harness đang audit XL-19 trước `OpenRepresentativeWindow()`/Run của ca này (dòng 167–168).

Đây là **điểm phát hiện heap corruption**, chưa phải chứng minh chỗ ghi hỏng heap đầu tiên. Close trace trước đó cho XL-17 cho thấy Cancel → RunAsync finally → CloseWhenSafe callback → FormClosing idle → DisposeBackend/session → callback return với Form disposed; mở lại/đóng idle cũng hoàn tất. Không có `FormClosing` hoặc `DisposeBackend` trên fatal stack hiện tại. Chưa thể khẳng định sản phẩm vô can hoặc quy crash cho riêng việc đóng Form từ một stack phát hiện muộn.

RPC `0x800706BE` ở UnloadAddIn và `0x800706BA` ở cleanup xảy ra sau server đã chết, không được báo là nguyên nhân gốc. Không thêm catch, GC, COM release, tăng timeout hoặc sửa thuật toán theo suy đoán. Không sử dụng dump PID khác để giải thích PID 73032 cũ; dump mới chỉ kết luận cho PID 24660. Nếu điều tra tiếp cần allocation/free history hoặc chẩn đoán native COM lifetime quanh Feature QI/release với symbol phù hợp; lượt này dừng tại giới hạn đã giao.

## Registry, dữ liệu và mức sử dụng

Hai snapshot trước/sau của **mỗi phiên** so sánh toàn bộ các root do guard hiện tại theo dõi: `HKCU\Software\SolidWorks\AddInsStartup` và `HKLM\Software\SolidWorks\Addins` trong tiến trình 64-bit. **57 keys / 88 values, 0 key thêm/xóa, 0 giá trị khác**, SHA trước/sau đều:

```text
f7e4b4093f4a112a26ec2cc78fda154a32b84b74e641076b1386e1d141a18887
```

Không có khác biệt Registry startup trong hai phiên này. Mismatch của PID 16928 cũ vẫn **chưa xác định key/cause** vì không giữ snapshot cũ; không suy luận lại hoặc tự restore cấu hình máy. Không tuyên bố đã so mọi key Registry ngoài hai root trên.

15 file CAD/Excel/fixture/cấu hình được so SHA, **0 thay đổi**. XL-02 và XL-17 nhận FeatureIds **53/59**, Min **2/2 MeetsMinimum**, đối chiếu evaluator hiện có; mỗi nếp giữ đủ **18** Punch/Holder và process candidates, cùng 18 Planner candidates. Các kết luận Punch/Holder/process vẫn UNKNOWN đúng dữ liệu thiếu, company numeric queries **0**. Không coi thời gian dưới đây là benchmark numeric collision:

| Ca | Cold ms | Warm ms | Cold native capture ms | Warm capture hits | Tổng ca ms |
| --- | ---: | ---: | ---: | ---: | ---: |
| XL-02 | 1557.73 | 966.66 | 295.09 | 0 | 7654.94 |
| XL-17 | 1616.96 | 594.74 | 299.90 | 1 | 7111.65 |

Không xác minh bổ sung hồ sơ của 18 Holder trong lượt này: material roles, datum/seating, ClampVerified, usable span và process evidence còn thiếu; KIK mở giữ nguyên. Kết quả UNKNOWN không được nâng thành CLEAR hoặc manufacturing PASS.

**Chưa ổn định để nghiệm thu hoặc thử nghiệm thông thường trên Part/Assembly.** XL-02 đã chứng minh trọn chu kỳ đóng/Disconnect/ExitApp trong một lượt, nhưng phiên kế tiếp vẫn fatal heap. Chỉ có bằng chứng kiểm thử cô lập nêu trên; không nghiệm thu đủ năm ca hay 33 Part. Source và báo cáo được giao trên cùng PR #37, giữ Draft, không merge.
