# PR #37 — điều tra COM/heap, giới hạn hai đối chứng

**Kết luận: CHƯA XÁC ĐỊNH bên gây lỗi.** Đã thu hẹp cơ chế ở dump PID 24660 tới native Feature wrapper bị delete khi heap block đã ở trạng thái free. Chưa có lịch sử allocation/free đầu tiên để quy lỗi cho harness, COM lifecycle của Add-in hoặc SOLIDWORKS API. Hai đối chứng mới đều hoàn tất nhưng không tái hiện fatal heap; **không có bản sửa sản phẩm hoặc tuyên bố crash đã hết**.

Baseline `c67d2d7f61a43fe735bc2b3209b86911a68f1c5c`, tiếp tục cùng PR #37. [Kết quả có cấu trúc](ConsolidatedResults/root-cause-com-20261021.json). Không merge, không 33 Part/Assembly 120–25, không tạo CAD thử mới. Native dừng sau đúng hai đối chứng đã giao.

## 1. Dump gốc đúng PID 24660

Đọc lại đúng full-memory dump SHA256 `9428349f6906dfa2669ea8426f5f8e6f78260e6f8bbab36f2d344cbe38ee0853`, 3,982,759,335 bytes. Không dùng dump PID khác để gán nguyên nhân cho 24660. Release/x64 product SHA `ef78be68e8bc66294f1e794562ccf18ced48ebac4b55d6df1fda6aae04b80722`, MVID `4dff6174-11ad-4576-ae03-d6e10b0f22f6`, SOLIDWORKS 2024 SP5 / 32.5.0.48.

Stack **fault context** của PID 24660, thứ tự từ caller tới điểm phát hiện:

```text
AcceptanceAddin.RepresentativeTick
  AuditRepresentativePart
    IndependentBendIds + 0x1bf
      clr!JIT_ChkCastAny
      clr!ObjIsInstanceOf
      clr!ComObject::SupportsInterface
      clr!SafeRelease
      slduiu!auFacet_c::GetThisClass + 0x244
      mfc140u!CCmdTarget::InternalRelease + 0x43
      sldappu!auCmdTarget_c::~auCmdTarget_c + 0x94
      ucrtbase!free_base
      ntdll!RtlFreeHeap / RtlReportFatalFailure (0xC0000374)
```

JIT disassembly xác nhận cast `(Feature)face.GetFeature()` ở dòng **76** của `NativeRepresentativeAcceptance.cs` baseline; chưa đến `feature.GetDefinition()` của lần gọi đó. `!analyze -v` phân loại `HEAP_CORRUPTION_ACTIONABLE_BlockNotBusy_DOUBLE_FREE_c0000374_ucrtbase.dll!free_base`. Phải dùng fault-context stack trên; pseudo-stack tổng hợp của `!analyze` chỉ còn `free_base`, không đại diện đầy đủ caller.

Các địa chỉ sau là **bộ nhớ của dump**, không phải tọa độ CAD:

| Đối tượng | Managed object / RCW | Native identity | Quan sát |
| --- | --- | --- | --- |
| XL-19 ModelDoc | `0x6b2eaab0` / `0x2cf6ef80` | `0x3262c228` | Managed RCW ref count 9; ModelDoc2/PartDoc cached |
| Face index 11 trong mảng 22 mặt | `0x6b32a998` / `0x5a407630` | `0x5a38f6d8` | Managed RCW ref count 1; IFace2 cached |
| Giá trị trả về GetFeature | `0x6b30d498` / `0x5a4030d0` | `0x5a491f80` | Managed RCW ref count 2; Feature/IFeature cached; vtable thuộc `slduiu!auFeature_c` |

Cả ba RCW được tạo bởi CLR Thread 1 / OSID `0x2a44`, apartment **STA**, cùng COM context `0x33bfcd8`; không có disconnected flag được SOS hiển thị. GetFeature RCW được giữ bởi hai GC/helper-frame roots trên thread hiện tại. Các số ref count này là **managed RCW count**, không phải số AddRef native. Chúng không chứng minh native object còn sống.

`!heap -x` tìm thấy Feature identity bên trong block user `0x5a491ed0`, size `0x140`, trạng thái **LFH;free**. Unwind **frame 6** (deleting destructor) khôi phục `rbx=0x5a491ed0`; disassembly chuyển chính `rbx` vào đường free. Interface pointer `0x5a491f80` nằm tại offset `+0xb0` trong block đó. Đây là bằng chứng native wrapper đang bị delete lại ở điểm heap phát hiện lỗi. **Không có stack của lần free đầu tiên**; snapshot không xác định nó xảy ra trước GetFeature, trong QI/release hay do một nhánh khác. `!gflag` trong dump trả NtGlobalFlag/NtGlobalFlag2 bằng 0; không có allocation-history instrumentation được chứng minh ở lượt cũ. Không bật thêm flag hoặc thay Registry trong lượt này, không suy diễn allocation history từ một dump sau lỗi.

## 2. GetFeature, IGetFeature và source sản phẩm

Reflection trên interop thực tế **32.5.0.48** (SHA256 `fa85c4e9b26d0ba996c53362e95afe42ab563c96f27d87cd6809d262f3d3c4cb`):

| IFace2 method | Return trong .NET | MarshalAs | DispId |
| --- | --- | --- | ---: |
| GetFeature() | System.Object | IDispatch | 6 |
| IGetFeature() | SolidWorks.Interop.sldworks.Feature | Interface | 27 |

Cả hai được SOLIDWORKS mô tả là lấy feature sở hữu mặt; mặt có thể thuộc nhiều feature và API trả oldest owner. Chữ ký trả về khác nhau không phải bằng chứng phương thức nào làm double-free. [GetFeature API](https://help.solidworks.com/2019/english/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.IFace2~GetFeature.html), [IGetFeature API](https://help.solidworks.com/2021/English/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.IFace2~IGetFeature.html).

Source sản phẩm cũng dùng `face.GetFeature() as Feature`: `Commands/BendPanelGeometry.cs:81`, `BendMinimumScan.cs:358`, `BendMinimumManufacturing.cs:187`. Oracle của harness dùng explicit cast, cùng API trả IDispatch. Cả `as` và cast đều cần kiểm tra COM interface; đổi sang `as` không có bằng chứng sẽ chữa heap corruption. Không thay sản phẩm sang IGetFeature hoặc bỏ mặt lỗi để ép PASS. Không thay `Feature.GetID()` bằng `Face.GetFeatureId()` (order number khác hợp đồng).

## 3. Vòng đời qua XL-17 → XL-19

Dump gốc: `representativeIndex=2`, stage 0, item XL-19. XL-17 đã hoàn tất các bước chức năng/đóng-mở lại, sau đó `CloseDoc(XL-17)`; XL-19 đã được mở và audit. Trace cho thấy RunAsync finally → CloseWhenSafe → FormClosing idle → DisposeBackend/session → callback return. Controller được mở lại rồi đóng idle cũng hoàn tất.

Controller cuối `0x6b25f178`: `disposed=true`, `backendDisposed=true`, `running=false`, `pumping=false`, `operation=null`; Timer disabled, `timerWindow=null`. Session `0x6b1e2c70` disposed, `partOperation=null`, Items count 0. Harness vẫn giữ `representativeController`, vì vậy root còn giữ backend/session/readonly Host XL-17 (`0x6804fb80`). Host này **khác** ModelDoc XL-19 đang audit. Sản phẩm xóa các field TaskPane window/controller/host qua FormClosed; đường root được SOS tìm thấy ở đây đi qua **harness**.

Việc giữ RCW của document đã đóng cần được tính vào lifetime review, nhưng không đủ chứng minh nó gây native double-free: timer đã tắt, không có call vào Host cũ trên fatal stack, và đối chứng B vẫn giữ cùng kiểu root mà không fatal. Không tự xóa root, gọi GC/ReleaseComObject hoặc sửa Dispose của sản phẩm để coi như đã xác định nguyên nhân. Stack nằm trong harness cũng không loại trừ lỗi xảy ra trước đó trong Add-in.

## 4. Đúng hai phép thử A/B

Cả hai chạy native bằng bridge chẩn đoán chung trên owning STA, cùng process-ownership/dump collector/CommandInProgress và XToolPro session-only isolation. **A không nạp sản phẩm ADDIN.dll**; vẫn có bridge test chung để giữ cùng apartment/call context với B. Không gọi đây là thử nghiệm SOLIDWORKS hoàn toàn không có bất kỳ test host/plugin nào. Không chỉnh startup Registry hoặc dùng phiên người dùng.

| Đối chứng | PID / thời điểm bắt đầu Bangkok | Kiểm tra thực sự | Kết quả |
| --- | --- | --- | --- |
| A — XL-19 độc lập, không sản phẩm | **57388**, 21/10/2026 06:43:09 +07 | Original GetFeature oracle trước, sau đó IGetFeature; 4 mặt trụ; native identity bằng IsSame, IDs 53/59 và 4 persistent roundtrips | **PASS**, 37 assertions; product absent cả trước/sau/Disconnect; ExitApp 0 |
| B — XL-17 có Add-in → đóng Check Chấn → XL-19 | **17156**, 21/10/2026 06:44:00 +07 | XL-17 Run/warm/Refresh/Cancel/running-close/mở-đóng lại, đóng document; rồi cùng original oracle và typed comparison XL-19 | **PASS luồng yêu cầu**, 201 assertions; target Disconnect/detach và borrowed-host check PASS; ExitApp 0 |

**B vẫn có 3 first-chance `0xC0000005` lúc 06:44:09.** Process tiếp tục hoàn tất; không có C0000374 hoặc heap dump mới. Collector chỉ ghi dump cho C0000374 nên ba AV không có stack riêng, **chưa xác định nguyên nhân**. Không gọi chúng vô hại, không gán stack từ PID khác. PASS trong bảng chỉ là luồng đối chứng hoàn tất, không chứng nhận memory safety hoặc stability.

Giữ nguyên toàn bộ method `IndependentBendIds` và assertions baseline (so source exact), native FeatureId/PersistentId không bị bỏ hoặc nới. Chạy audit GetFeature **trước** IGetFeature để typed API không prime cache trước phép cast đang điều tra. Typed comparison sau đó có thể dùng cache đã có; không phải chứng minh IGetFeature cold-call an toàn hơn. Đây không phải đối chứng factorial riêng cho mọi khác biệt marshal/call-order.

B dùng **nguyên product Release SHA/MVID của PID 24660**, không rebuild hoặc sửa. B chỉ kiểm tra API/geometry oracle của XL-19, **không Run Check Chấn hoặc nghiệm thu Min NG cho XL-19**; A cũng không có sản phẩm. Không ghi đè FAIL/NOT RUN của báo cáo năm ca cũ bằng các PASS chẩn đoán này. Min XL-17 vẫn 2/2 MeetsMinimum, 18 candidates mỗi nếp và UNKNOWN; company numeric queries 0.

## 5. Source và kiểm thử bàn giao

Chỉ bổ sung harness opt-in `ADDIN_ACCEPTANCE_COM_CONTROL=A|B`: `NativeComFeatureControl.cs`, selector/guard trong các acceptance files, project reference swconst và `Run-ComFeatureControl.ps1`. Wrapper có claim file một lần cho mỗi arm; giữ snapshot Registry và SHA nguồn; không retry sau lỗi. Không thay original oracle hoặc bất kỳ file sản phẩm nào.

- Runner và bridge **Release/x64 build PASS**. Product DLL giữ nguyên byte; Debug/product rebuild và full regression **NOT RUN**, không có product change.
- Mode guards managed **PASS 7 assertions**; wrapper parse PASS. Native A/B đã chạy đúng binary runner SHA `d6c72ce81d7846f047104d45472c23f9690a0a94ad760965604bae4534008649`, bridge SHA `7d69f5b0d321f8f81d5bb082ecbd3a4dd9d65f6ac2d82012af0d7282f21a2f76`.
- Mỗi arm: 57 startup keys / 88 values, snapshot trước/sau trùng; 15 protected CAD/Excel/fixture/config files SHA không đổi; private COM registration được xóa. Hai owned process đã exit 0. Raw logs/dump/CAD giữ riêng.
- Tổng thời gian audit XL-19 A 2788.59 ms; B 1074.63 ms, sau XL-17 7206.37 ms. Không phải benchmark collision; không so tăng tốc từ zero queries.

**Phân loại cuối vẫn UNKNOWN:** harness — chưa chứng minh; Add-in COM lifetime — chưa chứng minh; SOLIDWORKS API/interop — chưa chứng minh. Cần bằng chứng allocation/free đầu tiên của đúng native Feature wrapper hoặc native symbols/ownership trace để quy trách nhiệm. Chưa có chứng cứ đó trong dump hiện có và hai đối chứng. Dừng ở giới hạn hai lượt, không thêm bản sửa suy đoán, giữ PR Draft và kết luận nghiệm thu native/sản xuất BLOCKED.
