# Issue #4 — kiểm thử nền Bend Planning

Thực hiện theo `CODEX_NEXT_TASK.md` trên nhánh
`feature/check-chan-planning-foundation`, PR #3. Mốc so sánh:

- `main`: `549a03c5e2d79fec289327c0bfeef3ac7e8fd757`.
- PR trước sửa: `b88d001499ec959dacc288bd49e00a76c82431a3`.
- Không merge, không sửa UI, Check Min, bảng Excel, graph/mặt cắt hoặc gán V溝.

## Luồng dữ liệu và phần sửa

Nút hiện có trong `UI/BendMinimumTaskPaneIntegration.cs` mở
`UI/BendCheckForm.cs`. Form đọc Excel ở worker; inventory/capture và mọi API
SOLIDWORKS chạy trên STA. `BendCheckSession.Capture` dùng chung row/context của
Set Bend Line khi còn current, hoặc gọi `VCutPropertySetupCommand.CaptureForCheck`.
Luồng này vẫn dùng `BendPanelGeometryReader` → `BendMinimumFeatureGraph` và cache /
preview mặt cắt hiện có. Sau khi gắn nguồn Excel, planning chỉ chụp các giá trị
managed từ chính `BendRow` đó. Không quét hình học lần hai.

Hai file production thay đổi:

- `Commands/BendCheckPlanningDraft.cs`: chặn sinh thứ tự khi row null, native ID
  không hợp lệ/trùng, PersistentId trùng hoặc context stale. `SourceRowCount`
  giúp thấy inventory không đầy đủ. NaN/Infinity trở thành JSON `null` có warning.
  Giữ metadata và kết quả Min riêng với trạng thái planning `CHUA_XAC_MINH`.
- `Commands/BendCheckSession.cs`: cách ly exception của adapter/serializer/debug
  listener khỏi `item.Error` và context Check Min. Lỗi diagnostic được lưu riêng.
  Getter planning không trả snapshot của context shared đã invalidate; Dispose
  cũng bỏ snapshot. Không thay đổi control, thuật toán Min hoặc cơ chế timer UI.

## Build

Windows x64, MSBuild 18.10.1, .NET Framework 4.8, C# 7.3; PIAs/SOLIDWORKS 2024
(native test revision `32.5.0`).

| Build | Kết quả |
| --- | --- |
| `main`, Debug/x64 | PASS, 0 error |
| PR trước sửa, Debug/x64 | PASS, 0 error |
| Sau sửa, Debug/x64 | PASS, 0 error |
| Sau sửa, Release/x64 | PASS, 0 error |
| Hai project test managed/native | PASS |

Product builds có 16 warning CS0219/CS0649 sẵn có (8 vị trí, lặp qua hai pha WPF),
không phát sinh warning từ phần sửa. Build ra thư mục riêng, không đăng ký COM,
không kill SOLIDWORKS hoặc chép đè DLL/PDB đã cài. Lệnh dùng:

```powershell
& $MSBuild ADDIN.csproj /t:Rebuild /p:Configuration=Debug /p:Platform=x64 `
  /p:UseSharedCompilation=false /p:KillSolidWorksOnBuild=false `
  /p:PreBuildEvent= /p:PostBuildEvent= /p:RunPostBuildEvent=Never `
  "/p:OutputPath=$Out/product/" "/p:BaseIntermediateOutputPath=$Out/product-obj/"
```

Release dùng cùng lệnh với `Configuration=Release` và thư mục output/obj khác.
Hướng dẫn chạy test: [`Tests/BendPlanning/README.md`](../../Tests/BendPlanning/README.md).

## Kết quả test

### Managed — không dùng SOLIDWORKS API

24 test PASS, 609 assertions. Trước sửa đã tái hiện 5 test FAIL: null row,
native ID không hợp lệ, PersistentId trùng, context stale và số không hữu hạn.
Sau sửa không còn test FAIL. Bao gồm 1–9 nếp, hai thứ tự đảo nhau, giới hạn
24 hoán vị, JSON/culture/Unicode, exception và tách trạng thái Min/planning.

### Part mẫu từ Issue #4

Harness mở chính production `BendCheckForm`, qua API trên STA, bằng DLL test riêng;
không thay DLL đã cài hoặc tạo command mới. Part native có Feature Tree và
Flat-Pattern; chỉ mở bản sao ở cấu hình đã chấn. Hash Part gốc giữ nguyên.

- **6 nếp native có mặt trụ / 6 row Check chấn / 6 bend planning**.
- Tên/ID/góc/R đối chiếu trực tiếp native feature definitions; t đối chiếu native
  Sheet Metal definition; PersistentId đối chiếu native persist reference.
- **24 ứng viên, tất cả `CHUA_XAC_MINH`, `Exhaustive=false`**.
  Reason: `Truncated at 24 orders; none checked.` BendId không trùng.
- **595 assertions PASS**; 6 bản ghi `[BEND PLAN DRAFT]` thực tế parse được JSON.
- Sau sketch edit/rebuild/đổi configuration: snapshot cũ bị invalidate, dữ liệu
  được quét lại. Shared-context invalidate cũng ẩn draft trước tick kế tiếp.
- Part mẫu không lộ driving linear dimension qua API; test sketch thêm một đoạn
  construction trên bản sao. Test đổi driving dimension thực tế nằm trong bộ
  hồi quy Part/Assembly bên dưới.
- Chủ động làm debug listener ném exception: Min vẫn giữ được kết quả; lỗi nằm
  trong diagnostic planning, không biến thành lỗi capture Min.
- `initial.minimum` đối chiếu `main` / PR trước sửa / sau sửa **khớp toàn bộ**:
  metadata, hai phía phủ bì, giới hạn và trạng thái. Giữ nguyên 5 BelowMinimum và
  1 GroovedAccepted. Không thay đổi đánh giá để ép PASS.

### L / U / Z nhân tạo

Tạo bằng native Sheet Metal base flange API, không dùng CAD khách hàng.
L nhận 1 nếp / 1 ứng viên; U và Z mỗi loại nhận 2 nếp / 2 hoán vị đảo nhau,
tất cả `CHUA_XAC_MINH`. Graph Min nhận đúng L / C(U) / Z. Fixture không gán vật
liệu, nên đây không phải bằng chứng Min đạt hay chấn được. **139 assertions PASS**.

### Hồi quy Part/Assembly

Bộ native regression cục bộ hiện có chạy trên cả `main` và DLL sau sửa:
**1.514 assertions mỗi bản: 1.513 PASS, 1 cảnh báo layout có sẵn**, kết quả
chức năng tương đương. Bao gồm:

- Component chọn / toàn cụm; 4 instance gom thành 3 file/configuration.
- Cùng file/configuration gom số lượng, configuration khác dùng working copy.
- Min, highlight trong tọa độ Assembly, đánh số/chọn nếp, lọc NG và giữ lựa chọn.
- Thay driving sketch dimension, rebuild và tự cập nhật Min trên Part sao chép.

**Cảnh báo UI có sẵn:** stress test dùng 24 row giả với tên/configuration dài tại
1.000 px có một ô vật liệu đo cao 57 px trong row 53 px. Cùng hiện tượng trên
`main` và bản sửa; 1.280/1.600 px không có cảnh báo này. Đây là kết quả test UI
chưa PASS hoàn toàn, được ghi riêng để vẫn chạy các test chức năng còn lại.
Giữ nguyên UI theo yêu cầu, không sửa lỗi layout ở lượt này.

## Giới hạn và dữ liệu riêng

Không xác minh dao, chuyển động, va chạm, thứ tự gia công, Check Max hoặc năng lực
máy. V0 chỉ liệt kê tối đa 24 ứng viên khi ≤8 nếp; >8 giữ inventory và ghi lý do
chưa liệt kê, không kết luận không chấn được. `Exhaustive` chỉ nói về hoán vị.

Không có CAD, screenshot, raw debug JSON hoặc số đo hình học Part thật trong
commit. Báo cáo chi tiết và fixtures chỉ lưu ngoài checkout. Không dùng CAD kẹp
minh họa hoặc chiều dài 4.200/6.000 mm để khẳng định khả năng chấn.
