# Issue #5 — Phase 2 DXF company tool data

Ngày kiểm thử: 2026-10-17. Nhánh `feature/check-chan-dxf-library` tạo từ commit
PR #3 đã kiểm thử: `d1fa2c11924cfa691e029e2e5c4ff0813cc6f0fd`.
Lúc bắt đầu, PR #3 còn draft/open, main là
`549a03c5e2d79fec289327c0bfeef3ac7e8fd757`. Không merge hay sửa các commit đó.

## Phần hoàn thành

- Năm file `Commands/BendToolProfiles/`: geometry analytic, DTO/provenance,
  bounded DXF reader, source configuration/catalog/cache và managed I/O session.
- Đọc 18 mã công ty qua thư mục DXF read-only. Có preset DIM export và manifest
  selector riêng cho handle/layer/block/INSERT instance/type; không đoán file,
  chọn polyline lớn nhất, thay company bằng AMADA hay dựng lại dao từ catalogue.
- LINE / ARC / CIRCLE / LWPOLYLINE bulge / POLYLINE 2D / INSERT; giữ R, nguồn,
  flags hở/kín, tọa độ và phép biến đổi. Đổi đơn vị được khai báo sang mm.
- Tách punch / reference / annotations; verified holder là trạng thái chưa có
  bằng chứng. Tip origin, frame và clamp fit luôn chưa xác minh.
- SHA256 của file + config/manifest; cache theo bytes và invalidate snapshot cũ.
  Lỗi/thiếu nguồn có record riêng, không dùng last-good geometry như dữ liệu mới.
- `BendCheckSession` dùng lại capture/BendRow/MinimumContext/PlanningDraft đã có;
  chỉ liên kết thêm snapshot dao. Worker không nhận COM/model/control. Poll dùng
  điểm kiểm tra freshness hiện hữu, không sửa UI hoặc event handler WinForms.
- Debug `[BEND TOOL LIBRARY]` riêng chỉ có provenance/count/status, không có tọa
  độ CAD. Lỗi adapter/file/debug không biến thành lỗi Check Min.

Không đổi `SWAddin.cs`, UI/Designer, Check Min, graph/mặt cắt/cache cũ,
Excel/bend tables, V溝 hoặc các lệnh khác. `ADDIN.csproj` chỉ thêm 5 Compile Include.
Hướng dẫn cấu hình, contract và lệnh tái lập:
[`Tests/BendToolProfiles/README.md`](../../Tests/BendToolProfiles/README.md).

## Build

MSBuild 18.10.1 / .NET Framework 4.8 / C# 7.3, SOLIDWORKS 2024 PIAs.
**Debug/x64 và Release/x64 PASS, 0 error, 16 warning có sẵn**
(8 vị trí CS0219/CS0649 lặp qua hai pha WPF; không có warning mới từ module).
Các project test managed/native cũng build PASS. Mỗi cấu hình dùng output/obj
riêng, không kill SOLIDWORKS, đăng ký COM hoặc thay DLL/PDB đã cài.

```powershell
& $MSBuild ADDIN.csproj /t:Rebuild /p:Configuration=Debug /p:Platform=x64 `
  /p:UseSharedCompilation=false /p:KillSolidWorksOnBuild=false `
  /p:PreBuildEvent= /p:PostBuildEvent= /p:RunPostBuildEvent=Never `
  "/p:OutputPath=$Out/product-debug/" "/p:BaseIntermediateOutputPath=$Out/obj-debug/"
```

Release dùng cùng flags với `Configuration=Release` và output/obj khác.

## Kết quả kiểm thử

| Bộ kiểm thử | Kết quả |
| --- | --- |
| DXF managed, không cần SW API | 40 test / 200 assertions PASS |
| Bend Planning kế thừa PR #3 | 24 test / 609 assertions PASS |
| 18 file DIM + index/master gốc | 32 assertions PASS; 18/18 đọc được, 17 kín, KIK hở |
| Manifest riêng, thêm 2 AMTS III references | 28 assertions PASS; 18/18 punch, 2 reference, 0 clamp verified |
| Đối chiếu độc lập với ezdxf 1.4.5 | 6.975 curves PASS; sai số tọa độ/R/midpoint lớn nhất 4,55×10⁻¹³ mm |
| Check chấn native, thư viện thật và lỗi nguồn | 441 assertions PASS |
| Part/Assembly regression hiện có | 1.514 assertions: 1.513 PASS, 1 cảnh báo layout có sẵn |

Managed tests bao gồm cung R/sweep âm/cung lớn/bulge cuối polyline hở, units
thiếu/khác mm, OCS -Z, INSERT base point/scale/rotation/mirror/nesting/array,
nonuniform circular transform, xref/cycle/unsupported/malformed input,
annotations/role overlap, nguồn thiếu, SHA thay đổi dù length/mtime giữ nguyên,
manifest thay đổi và worker/diagnostic exception.

Company integration chỉ đọc nguồn gốc. Hash file trước/sau giữ nguyên. Polyline
`KIK-7181328001` vẫn hở; không tự thêm cạnh hoặc đổi flag. 18 mã có dữ liệu rõ
ràng, nhưng **0 mã được xác minh mốc mũi/frame/clamp**. AMTS III chỉ là reference,
không phải bằng chứng CAD gá chính xác. Không lấy nhãn nhóm hoặc 4.200/6.000 mm
làm năng lực máy.

Independent QA dùng một thư viện khác để đọc lại nguồn, so endpoint/center/R và
midpoint cung sau OCS/INSERT. Hai DXF tổng hợp cũng được đối chiếu, gồm ARC normal
-Z. SPLINE không được xấp xỉ thành cung/đường và luôn giữ unsupported metadata.

Native harness chạy production `BendCheckForm` qua API/STA, trên Part sao chép.
Nhận đủ **6 nếp native / 6 rows**, vẫn 24 ứng viên `CHUA_XAC_MINH` như PR #3;
Min metadata/phủ bì/giới hạn/trạng thái khớp baseline toàn bộ. Khi đổi nguồn DXF
sang thiếu file/manifest lỗi rồi phục hồi, hoặc inject lỗi ghi debug, Min không
đổi và geometry read count vẫn 1. Snapshot dao cũ bị invalidate; rebuild Part
vẫn dùng cơ chế quét lại hiện có. Hash Part gốc giữ nguyên.

Regression Assembly kiểm tra scope component/toàn cụm, gom số lượng theo
file/configuration, working copy cấu hình khác, highlight/đánh số/lọc NG và sửa
driving sketch dimension/rebuild/tự cập nhật. Kết quả Min khớp PR #3. Cảnh báo
layout với row giả tên dài tại 1.000 px đã có trên main/PR #3: ô cao 57 px trong
row 53 px. Giữ nguyên UI theo yêu cầu; không tuyên bố toàn bộ test layout PASS.

## Chưa xác minh / giới hạn

- Chưa có mốc mũi dao, tọa độ gá hoặc bằng chứng khớp kẹp được kiểm chứng vật lý.
- Chưa Check Max, chọn dao, tạo vùng va chạm, mô phỏng chuyển động hay thứ tự bẻ
  khả thi. Tất cả trạng thái thư viện/validation vẫn `CHUA_XAC_MINH`.
- Reader V0 giới hạn ở ASCII/text DXF, XY z=0, OCS +/-Z; không âm thầm flatten
  3D, xử lý fitted/width polyline, spline/ellipse hoặc xref. Các trường hợp đó
  có reason/unsupported state rõ ràng. Closure chỉ quan sát nguồn/endpoints,
  chưa chứng minh vùng kín không tự cắt hoặc vùng va chạm hợp lệ.
- Cấu hình qua companion JSON/environment/injection; không thêm Settings UI.

Không có CAD, geometry fixtures công ty, manifest riêng, ảnh hay raw JSON trong
commit. Chỉ code/test tổng hợp và báo cáo đã loại dữ liệu thiết kế. Evidence chi
tiết nằm ngoài checkout trên máy kiểm thử. PR mới là draft, không merge main.
