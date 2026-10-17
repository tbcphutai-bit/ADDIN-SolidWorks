# Check chấn Phase 3A — kết quả kiểm thử

Ngày: **2026-10-17**, Windows x64. Nhánh
`feature/check-chan-phase3a-geometry` tạo trực tiếp từ PR #6,
`feature/check-chan-dxf-library`, commit
`f7e9abc110ca367f97cdc7de6452c5de9d0ae51c`.
PR #6 đang draft/open, chưa merge tại thời điểm xác nhận baseline.

## Đã thực hiện

- Thêm `ToolRegionValidator.cs`: kiểm tra vùng độc lập với observer kín/hở cũ.
  Kiểm tra liên thông, degree/vòng, suy biến, tự giao/tiếp xúc, trùng/chồng lấn,
  diện tích và nhiều vòng rời/lồng. Giữ nguyên analytic LINE/ARC và nguồn.
- Thêm `ToolDatumEvidence.cs`: mũi, điểm gá, hướng làm việc, local frame và trạng
  thái bằng chứng riêng. Không suy đoán từ hình học. File bằng chứng độc lập,
  bind SHA256 nguồn + phần geometry được chọn + payload đã duyệt + artifact.
- Tích hợp vào provider/models hiện có và source configuration tùy chọn; không
  tạo command, không sửa UI. Bằng chứng thiếu/lỗi không làm mất DXF đọc được.
- Snapshot cũ và các đối tượng datum đang được giữ cùng hết hiệu lực. Hash thay
  đổi dù size/mtime giữ nguyên, chọn outline khác trong cùng DXF, sửa số tọa độ,
  đổi artifact hoặc hết hạn đều không được dùng lại như bằng chứng còn hiệu lực.
- Hash hình học được ghi theo luồng; không tạo thêm JSON tọa độ lớn để chỉ tính
  hash. Profile vượt budget vùng vẫn có identity nguồn, không tự dựng vùng.

Chi tiết hợp đồng, tolerance và cấu hình:
[Phase3A-GeometryContract.md](Phase3A-GeometryContract.md).

## Build và bảo vệ phạm vi

MSBuild **18.10.1**, .NET Framework **4.8**, C# **7.3**, SOLIDWORKS 2024 PIAs.

| Cấu hình | Kết quả |
| --- | --- |
| Debug/x64 | PASS — 0 errors, 16 warnings có sẵn |
| Release/x64 | PASS — 0 errors, 16 warnings có sẵn |
| Managed test / native harness projects | PASS |

Các warning CS0219/CS0649 ở 8 vị trí cũ, lặp qua hai pha build WPF; không có
warning mới từ module. Build vào output/obj riêng ngoài checkout; tắt kill SW,
pre/post-build, COM registration/copy. **Không thay DLL/PDB đã cài.**

```powershell
& $MSBuild ADDIN.csproj /t:Rebuild /p:Configuration=Debug /p:Platform=x64 `
  /p:UseSharedCompilation=false /p:KillSolidWorksOnBuild=false `
  /p:PreBuildEvent= /p:PostBuildEvent= /p:RunPostBuildEvent=Never `
  "/p:OutputPath=$PrivateOut/product-debug/" `
  "/p:BaseIntermediateOutputPath=$PrivateOut/obj-debug/"
```

Release dùng cùng flags, `Configuration=Release`, output/obj khác. Hai Compile
Include là thay đổi duy nhất trong project add-in. Diff so với baseline xác nhận
không đổi `BendCheckSession`, `BendCheckPlanningDraft`, Check Min/context/graph/
section/cache, UI, WinForms Designer, event handlers, `SWAddin`, V溝, Excel/Data
hoặc các lệnh khác. `ToolDxfReader` và `ToolProfileGeometry` cũng giữ nguyên.
Không có COM/control/model được truyền vào worker; code mới chỉ có managed
geometry, file read/hash và DTO.

## Kết quả test

| Bộ kiểm thử | Kết quả |
| --- | --- |
| DXF + region + datum, C# thuần | **96 tests / 1.069 assertions PASS** |
| Bend Planning kế thừa | **24 tests / 609 assertions PASS** |
| 18 DIM gốc + 2 DXF tổng hợp | **38 assertions PASS** |
| Manifest riêng với 2 AMTS III reference | **34 assertions PASS** |
| Đối chiếu reader với ezdxf 1.4.5 | **6.975 curves PASS**, max error 4,55×10⁻¹³ mm |
| Check chấn production native/STA | **587 assertions PASS** |

C# tests dùng geometry tổng hợp, không chứa CAD công ty. Có polygon thuận/ngược/
lõm, LINE đảo thứ tự, full circle, cung lớn/âm, capsule tiếp tuyến hợp lệ, lens,
bowtie/tự giao có diện tích khác 0, cung giao/tiếp xúc, đường/cung trùng, vòng rời/
lồng/hỗn hợp, zero edge/zero area/vùng quá mỏng, joint gap hai phía tolerance,
cluster tolerance không bắc cầu, numeric/curve budget và cancellation. Có thêm
**250 polygon và 100 capsule xoay/phản chiếu/scale** với diện tích độc lập.

Tests bằng chứng kiểm tra tọa độ đơn thuần không verified, tip/gá độc lập,
nguồn/selection/payload/artifact SHA, sửa số sau duyệt, repin nguồn không duyệt
lại, expiry không đổi file, metadata thiếu/sai, hướng/frame sai, tọa độ thiếu/
không finite, path traversal, ID trùng, thiếu file, lỗi parse, multi-source chưa
hỗ trợ, cache/freshness và held objects không giữ verified sau invalidate.

## Kết quả 18 dao trên máy

| Quan sát | Số lượng |
| --- | ---: |
| Profile đọc đầy đủ | **18/18** |
| Biên dạng gốc kín | **17** |
| Vùng đơn hợp lệ theo `AnalyticRegionMmV1` | **17** |
| Biên dạng gốc hở | **1 — KIK-7181328001** |
| Profile thiếu bằng chứng mũi/gá/hướng/frame | **18** |
| Clamp được xác minh vật lý | **0** |

KIK vẫn là polyline hở; không thêm cạnh hoặc đổi flag. 17 vùng hợp lệ chỉ chứng
minh tính hợp lệ hình học trong tolerance đã định nghĩa, không chứng minh dao
được lắp đúng hoặc chi tiết chấn được. Hai reference AMTS III vẫn là reference;
geometry unsupported không được xấp xỉ hoặc tự hoàn thiện. Hash toàn bộ nguồn
DXF trước/sau giữ nguyên. Không ghi file bên cạnh nguồn công ty.

Lượt đọc DIM cuối tại máy: khoảng **93 ms** lần đầu, **12 ms** refresh không đổi;
manifest có master/reference: khoảng **327/15 ms**. Đây là số quan sát cục bộ,
không phải cam kết hiệu năng trên máy khác. Công việc chạy ở managed I/O worker.

## Hồi quy native

Harness mở production `BendCheckForm` bằng API trên một phiên SOLIDWORKS sở hữu
riêng, STA, Part sao chép. Nhận **6 nếp native / 6 rows**, 18 profiles, 17 vùng
hợp lệ và 18 bộ mốc thiếu bằng chứng. Snapshot Min/native/draft/giới hạn/phủ bì/
UI columns khớp **toàn bộ** baseline Phase 2 đã lưu. 24 ứng viên vẫn
`CHUA_XAC_MINH`.

Đổi nguồn thiếu/manifest lỗi, inject lỗi diagnostic, file bằng chứng hỏng rồi
phục hồi đều không đổi Min/planning; item/context dùng lại, geometry read count
vẫn **1** khi chỉ đổi nguồn/thông tin dao. Rebuild Part vẫn dùng auto-rescan có
sẵn. Source library không đổi model stamp/config/dirty. Hash Part gốc giữ nguyên.
Không thao tác UI bằng Computer Use. Không đổi add-in đang được cài.

Regression Assembly/layout mở rộng của Phase 2 không chạy lại trong Phase 3A;
UI/event handlers không đổi và native snapshot/cột vẫn khớp baseline. Cảnh báo
layout stress 1.000 px có sẵn trong báo cáo Phase 2 vẫn ngoài phạm vi nhiệm vụ.

## Chưa thực hiện / chưa có bằng chứng

- Chưa va chạm chi tiết–dao, chuyển động 2D, Check Max PASS/NG hay thứ tự khả thi.
- Chưa xác minh đo đạc thực tế mũi/gá, hướng, frame, holder hoặc độ khớp kẹp.
  `VerifiedRecord` trong test chỉ là fixture attestation tổng hợp; không có record
  nào của 18 dao công ty được tự nâng lên verified.
- Nhiều vùng rời/hole/island chưa có fill rule: giữ `Unsupported`. Các giới hạn
  parser Phase 2, numeric domain và budget vẫn được báo rõ.
- Approval file không phải chữ ký số xác thực reviewer hoặc kiểm định vật lý.
  Chỉ kiểm tra hợp đồng bằng chứng được cung cấp và các hash còn khớp.

Toàn bộ kết luận khả năng chấn và physical flags giữ `CHUA_XAC_MINH`/false.
Không có DXF, tọa độ thiết kế, SLDPRT, manifest/evidence công ty, ảnh hoặc raw
geometry JSON trong commit. Evidence kiểm thử chi tiết chỉ lưu ngoài checkout.
Không sửa main hoặc merge PR #3/#6; Draft PR Phase 3A target nhánh Phase 2.
