# KADAI_IMPLEMENTATION_REPORT

Ngày thực hiện: 17–18/11/2026, Asia/Bangkok. **Chưa nghiệm thu sản xuất; Apply/Export trong UI vẫn khóa.**

Repository: https://github.com/tbcphutai-bit/ADDIN-SolidWorks  
Branch: `feature/kadai-parametric-panel-ribs-20261117`  
Base commit: `f71cac7c73d183694f6af347c467a07d61819954`  
Code commit: [`7139a704d0e9082fa4f245a5ae696c8f8119a03f`](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/commit/7139a704d0e9082fa4f245a5ae696c8f8119a03f)  
Native runtime: SolidWorks Professional 2024 SP5.0, API `32.5.0`, executable `32.5.0.48`; .NET Framework 4.8, C# 7.3, x64.

CAD gốc chỉ được đọc/copy. Bản sao, ảnh, JSON chứa Custom Properties và các sản phẩm thử nằm **ngoài repository**, trong thư mục local `kadai-local/`. Không đưa CAD, ảnh nội bộ, bảng vật liệu hoặc giá trị nhận dạng dự án lên GitHub. Báo cáo đầy đủ kèm ảnh là `kadai-local/KADAI_FEATURE_AUDIT_LOCAL.md`.

## 1. Feature Tree & Sheet Metal native audit

Đã mở trực tiếp **bản sao native SLDPRT**, không dùng STEP để suy ra Feature Tree. Audit ổn định cuối đã ghi 22 Part, gồm KDA1 master, các Part `3d/3da/bs1/bs2/d1/o1/o2/t1…t7`, KF3 `bs1/d1/t1…t5`. Đã đọc thêm hai nguồn hình học và ba assembly KDA1, KF3, HY. Không có `4315-02-KF3.SLDPRT` trong thư mục mẫu.

| Mẫu/config đang dùng | Feature thực tế | GetTypeName2 / legacy | Vật liệu / chiều dày | Cut / scope / flat |
|---|---|---|---|---|
| KDA1 master Part | 13 solid, 2 surface, 32 Feature cấp gốc | `BaseBody`, `RefSurface` | Master không gán material | Không chứa lịch sử Sheet Metal/Cut của từng thanh |
| KDA1 t1/t2 | `板金97`, `ﾍﾞｰｽ-ﾌﾗﾝｼﾞ1`, Cut 3/4/5/6; t2 có Cut 7 | `SheetMetal`, `SMBaseFlange`, `Cut`; một số `ICE` → `Cut`/`Boss` | Part thành phần có material; t = 1.6 mm | Cut tạo rãnh là Cut 6 + Sketch 10; có `FlatPattern` |
| KDA1 d1 | Sheet Metal, Base Flange, Cut 1, Linear Pattern | `SheetMetal`, `SMBaseFlange`, `Cut`, `LPattern` | t = 1.6 mm | Sketch 2 seed rãnh 1.8 mm; pattern pitch 358.33 mm |
| KDA1 bs1/bs2 | Sheet Metal, Base Flange, Cut | Native Sheet Metal | t = 2.3 mm | Có Flat Pattern |
| KF3 t1/t2 | Sheet Metal, Base Flange, Cut 1/2 | Native Sheet Metal/Cut | t = 1.6 mm | Hai rãnh trong sketch, rộng 1.8 mm, sâu hình học 50 mm |
| KF3 d1 | Sheet Metal, Base Flange, Cut 1, Pattern | Native Sheet Metal/Cut/Pattern | t = 1.6 mm | Sketch có hai bề rộng 1.6 mm: clearance danh nghĩa bằng 0 |

Các Part thành phần có Custom Properties liên kết material, thickness, diện tích, khối lượng, số lượng và thông tin bản vẽ. Giá trị chi tiết được giữ local. Audit ghi tên Feature/subfeature theo thứ tự, parent/child, dimension, sketch line endpoints, relation types, configuration names/properties, body/face boxes và persistent face references. Đây là snapshot configuration đang mở; **chưa kiểm tra hình học mọi configuration**.

**Sheet Metal:** radius 0.1 mm; API lưu K-factor 0.5 nhưng `BendAllowanceType=1` là chế độ bảng uốn. Không coi K-factor đang lưu là quy tắc uốn đang hoạt động. PropertyManager thật hiển thị tên bảng khác đường dẫn API trả về; đường dẫn bảng API không tồn tại tại máy. Điểm bất nhất này chưa được giải quyết và là một điều kiện khóa sản xuất.

**Cut thật:** KDA1 t1 Cut 6 là Blind liên kết thickness 1.6 mm, `NormalCut=true`, Direction 2 tắt; chiều sâu rãnh trong sketch là 100 mm. KDA1 d1 Cut 1 là Through All, `NormalCut=true`; chiều sâu rãnh trong sketch là 70.1 mm trên thanh cao 170 mm. Không nhầm chiều sâu Cut xuyên chiều dày với chiều sâu notch trong mặt thanh. Part một body; các giá trị `FeatureScope=true`, `AutoSelect=false` đã đọc, và một số scope entity được resolve trong audit đầu. Bản audit batch cuối không rollback để resolve scope; danh sách scope trống trong JSON cuối không có nghĩa là Cut không có scope.

Đã đối chiếu tối thiểu hai rãnh mỗi phương qua sketch seed/pattern và hai Part thanh. KDA1 có clearance bề rộng tổng 0.2 mm; datum rãnh đối ứng sau transform cho khoảng hở theo chiều lắp 0.1 mm. KF3 có một phương clearance bề rộng 0.2 mm, phương kia 0; hai notch 50 mm trên chiều cao 100 mm có khoảng hở chiều sâu danh nghĩa 0. Đây là số đo mẫu, **không phải tiêu chuẩn dung sai công ty**. Chưa có swept-volume/insertion test để chứng nhận lắp được.

Có external references/in-context references đến assembly, bao gồm đường dẫn cũ không còn tồn tại. Một số Feature ở KDA1 t2…t7 báo `swSketchErrorExtRefFail=51`; có warning/error khác ghi trong JSON. Flat Pattern của mẫu tồn tại, thường suppressed; chưa chứng nhận unfolding tất cả mẫu có reference lỗi. Prototype mới đã thử unsuppress/rebuild/resuppress Flat Pattern thành công.

Nguồn mặt cong KDA1 được tìm thấy trong Part `3d` và master. UI buộc người dùng chọn một Face trong Part, lưu persistent reference + configuration, chọn trục cao và datum. Không giả định các thanh đã cắt là panel master. Nguồn liên quan KF3 là frame assembly/solid; **chưa xác định được một panel surface master KF3 tương đương**.

## 2. Proposal review và quyết định kiến trúc

| Phương án | Đánh giá |
|---|---|
| Master multibody Sheet Metal → Save Bodies | Thuận tiện đối chiếu giao nhưng body index/tên dễ đổi khi tăng giảm số lượng; derived links và drawing relink phức tạp; khó giữ cut người dùng trong từng Part. Không chọn làm lõi cập nhật. |
| Master reference Part + mỗi rib là Part native + Assembly | Chọn làm kiến trúc đích: mỗi thanh có Feature Tree, Flat Pattern, metadata và Drawing độc lập; dễ kiểm tra gia công và giữ Feature thủ công. Phải quản lý station identity và version manifest rõ ràng. |
| Giao face/plane → profile native → Base Flange + Part Cut | Chọn làm cách sinh hình học. Bản sản xuất cần giữ curve chính xác và trim topology; không Pattern một rib chung cho mọi vị trí. |
| Surface Cut / Replace Face | Hữu ích khi sửa biên phôi có datum khóa, nhưng phụ thuộc reference và topology; không thay thế native part-level slot cuts, kiểm tra thickness và flat. Mẫu thực tế có các reference dễ đứt nên không dùng làm mặc định. |

**Code hiện tại:** P2 dùng giao ray–surface thật, kiểm tra thuộc trimmed face, tìm đoạn liên tục, refine polyline theo midpoint tolerance 0.05 mm rồi dựng temporary solid. Footprint khởi tạo bằng geometric extrema của sheet body, không dùng `Face.GetBox` gần đúng. Đây là preview approximation; chưa phải exact-curve manufacturing backend. Chỉ hỗ trợ một face, một outer loop, single-valued theo một trục chính trong Part; assembly transforms, nhiều face, lỗ và mặt gấp bị từ chối.

Prototype P3/P5 tạo native sketch, Base Flange, Cut-Extrude Normal Cut xuyên hai chiều tại Part, thử Flat Pattern rồi xuất. Fixture 3+1 dùng joint datum 100 mm và depth clearance 0.1 mm; không coi fixture đó là solver tổng quát hoặc cấu hình KF3. Rib ID dạng ordinal hiện chỉ là nhãn, **chưa bảo đảm stable physical identity khi đổi số lượng**.

## 3. Work performed

| Mốc | Kết quả thực hiện |
|---|---|
| P0 | Sửa lỗi build `Environment` trùng Interop; chạy planner C# thật; snapshot request; kiểm tra finite datum/axis. Nút Kadai mở form từ Model → Macro trong candidate full UI. |
| P1 | Audit 22 Part, nguồn liên quan và 3 assembly; chụp Feature Tree, Sheet Metal PropertyManager và slot Cut PropertyManager; tìm lỗi external refs và bất nhất bảng uốn. Chưa nghiệm thu mọi configuration/flat mẫu. |
| P2 | Có capture face, persistent ref/config, principal-axis datum, true face extrema, actual temporary solid bodies, clear/cancel, clear khi đổi tham số/doc/config/rebuild và khi shutdown. 3+1, 7×4→10×6→7×4, 5×2 đã qua preview trên mặt KDA1. |
| P3 | Laboratory prototype: 4 Part native Sheet Metal, part-level Normal Cut, Flat Pattern rebuild đạt; DXF đo rãnh 1.8 mm. Chưa đưa writer vào Apply. |
| P4 | Preview số lượng đã chạy; **chưa có regeneration giữ Feature người dùng, persistent station mapping, preview-diff/rollback hoặc update Drawing**. Không thực hiện xóa/recreate sản phẩm đang dùng. |
| P5 | Prototype có 4 SLDPRT + 4 SLDDRW + 4 DXF, Assembly 4 component fixed và CSV BOM. View scale 1:1, custom sheet có kiểm tra containment. Chưa có dimensioning/production BOM chuẩn, visual QA cuối toàn bộ Drawing hay certification lắp. |
| P6 | Debug/Release x64 build đạt. 35 lần mở/đóng form qua launcher, form dispose đạt trên candidate. UI vẫn WinForms, chưa native PMP; chưa chứng nhận HWND/COM leak, old-command regression hoặc crash-free operation. **FAIL/STOP về ổn định.** |

Các file code thay đổi chỉ trong `Commands/Kadai/`, UI Kadai, công cụ Kadai và các điểm tích hợp cần thiết ở `ADDIN.csproj`/`BomTaskPaneControl.cs`. Không sửa implementation của lệnh khác. Không deploy đè DLL production; candidate được nạp bằng host GUID/assembly name riêng. Host test registration đã gỡ; startup production đã khôi phục.

Danh sách file:

- `ADDIN.csproj`, `BomTaskPaneControl.cs`.
- `Commands/Kadai/KadaiCadContracts.cs`, `KadaiPlanner.cs`, `KadaiPreviewBackend.cs`, `KadaiNativePrototype.cs`.
- `UI/KadaiForm.cs`, `UI/BomTaskPaneControl.Kadai.cs`.
- `tools/KadaiNativeAudit.cs`, `KadaiCadTests.cs`, `KadaiPlannerTests.cs`, `KadaiSwControl.cs`, `KadaiValidationHost.cs`, `Test-Kadai.ps1`, `kadai_validate_dxf.py`.
- `docs/kadai/README.md`, `KADAI_IMPLEMENTATION_REPORT.md`.

## 4. Tests / observed evidence

| Kiểm tra | Kết quả / giới hạn |
|---|---|
| Debug x64 / Release x64 | PASS, SW2024 interop cài tại máy; có warning CS0219/CS0649, không phải zero-warning build |
| Planner C# thật | PASS: 30 behavioral checks; Python numeric check độc lập cũng PASS |
| UI Model → Macro → Kadai | PASS trong isolated candidate full task pane; chưa cài production |
| Capture → Preview UI | PASS: chọn face thật; 7+4 = 11 temporary solids; ảnh local |
| Preview/cancel | PASS 3+1 và matrix: feature count, save flag, update stamp không đổi |
| 7×4→10×6→7×4 / 5×2 | PASS preview trên **cùng mặt KDA1**; 5×2 không phải xác minh panel master KF3 |
| Native Sheet Metal / Cut / Flat | PASS fixture 3+1; không đại diện full 7×4 production hoặc update |
| DXF | PASS 4 file: outline extents khớp measured profiles trong 0.001 mm, mm 1:1, rãnh 1.8 mm đúng số fixture |
| Drawing | File/native flat view tạo được; API scale/containment checks PASS sau sửa khổ giấy. Visual QA toàn bộ bản cuối chưa xong |
| Assembly/BOM | PASS fixture 4 component, transform checked, CSV quantity 1 mỗi rib; cần explicit assembly template vì default template thiếu |
| Source protection | PASS: 110/110 hash CAD gốc so với manifest local không đổi; không gọi Save lên source |
| Lifecycle | PASS 35 form open/close/dispose; không suy rộng thành memory/HWND leak proof hoặc 35 preview cycles |
| AutoSpline/Mirror/Check Hole/Same Part/Drawing BOM/V-Cut/DXF regressions | Chưa chạy end-to-end; chỉ build, kiểm tra diff và nhìn thấy các nút cũ |

**Các lỗi quan sát và xử lý:** lỗi compile P0; curve array/lifetime ở temp sheet; chọn nhầm Feature cuối cây thay vì slot sketch (đã sửa bằng sketch identity); Face.GetBox quá rộng (đã dùng extrema); drawing view ngoài paper (đã custom-size/containment); thiếu default assembly template (explicit installed SW2024 template); trùng tên Part giữa package thử (full-path lookup/close và reject mismatch).

**Native crash — cập nhật điều tra 18/11/2026:** đã phân biệt hai đường lỗi bằng debugger và đối chứng thực tế. Khi thoát phiên trống, RCW `ICommandManager` do `XToolProParallelTest` giữ bị native release rồi phát hiện double free; tắt XTool thì thoát mã 0, bật lại thì tái hiện, tắt TAI TOOL riêng vẫn lỗi. Khi audit với XTool tắt nhưng TAI TOOL bật, `MarkHighlightManager.RefreshNow → Describe` chạy trong message pump lồng của `OpenDoc6`, native heap lỗi sau 6 Part. Giữ TAI TOOL bật và chỉ tách MARK observer trong process test thì cùng audit đủ 22/22 Part, dirty 0, open error 0 và `ExitApp` mã 0. Một lượt tắt cả hai add-in cũng PASS 22/22 và thoát sạch. Chi tiết binary/RCW, matrix, giới hạn first-free và evidence trong [báo cáo điều tra native crash](docs/kadai/KADAI_CRASH_ROOT_CAUSE_20261118.md). Không gộp access violation/Modeler ban đầu vào hai đường này. Chưa sửa DLL triển khai hoặc chứng nhận stress test; Apply vẫn khóa.

Evidence local: `evidence/audit-all-safe.json`, `audit-kf3-safe.json`, `audit-assemblies.json`, `preview-matrix-pass.txt`, `assembly-bom-pass.txt`, `dxf-scale-notch-final.txt`, ảnh Feature Tree/PropertyManager/Preview, build logs và `solidworks-crash-events.json`. Các audit partial/error và package thử lỗi được giữ để truy vết, không coi chúng là PASS.

## 5. Completion status

Người dùng có thể review code, build cô lập, dùng layout và thử preview theo face trong Part. Bộ prototype gia công chỉ là **bằng chứng thí nghiệm local**, không phải file đã được duyệt đưa đi gia công. Các file gốc không bị ghi đè; chín AutoRecover có trước đã được sao lưu và đối chiếu hash.

**Chưa hoàn thành yêu cầu P0–P6 toàn bộ.** Những phần còn lại: sửa và kiểm thử vòng đời CommandManager XTool/refresh MARK đã khoanh vùng; đối chiếu bảng uốn/material inheritance; exact curve/trim solver, multi-face/assembly frame; production joint/insertion validation; regeneration giữ Feature người dùng; full N×M native/flat/export/update; drawing dimensioning/BOM; native PMP và regression lệnh cũ. Cần giải quyết các điều kiện này trước khi enable Apply/Export hoặc xem xét merge. Không merge main.

Điều tra bổ sung và công cụ API được commit ở `ce3aea441e1240f105fcd91e16c50b0b6dc25756`: `tools/KadaiNativeAudit.cs`, `tools/KadaiSwControl.cs`, `tools/KadaiMarkIsolationHost.cs`, `docs/kadai/KADAI_CRASH_ROOT_CAUSE_20261118.md`. Công cụ kiểm tra đúng PID, từ chối exit khi có document và cho phép đối chứng MARK trong process test; không nằm trong production add-in. Compile tools, 30 numeric checks, Debug/Release x64 đều PASS. Sau thử nghiệm đã hoàn nguyên đúng 3 startup values/kiểu DWORD, gỡ CLSID test, phục hồi/hash 13/13 AutoRecover và kiểm tra lại 110 CAD gốc: 0 thay đổi. Các thử nghiệm sau yêu cầu chuyển sang API đều thực hiện bằng COM/debugger, không dùng Computer Use.

## 6. Tiếp tục triển khai trên TAI TOOL có CircleFace V3

Kadai bổ sung kiểm tra hình học rãnh cho toàn lưới trước khi ghi bất kỳ Part nào. Bộ kiểm tra đo vị trí theo khung của mỗi thanh, kiểm tra đủ đối ứng, khoảng cách tới biên và rãnh bên cạnh, độ hở ngang, vật liệu còn lại và độ chồng hai chiều sâu tại cùng cao độ ghép. Form báo rãnh lỗi và gợi ý lề từ các contour hiện tại để người dùng nhập lại và Preview. Form cũng báo số mặt phẳng thanh giữ nguyên, thêm và bỏ khi đổi lưới trên cùng mặt đã Capture. Khóa vị trí 0,001 mm giúp so sánh qua các lượt preview; file native đã lưu vẫn mang ID thứ tự.

Writer thí nghiệm tạo package vào thư mục mới và ghi `JointPlan.csv`, `Stations.csv`, Assembly/BOM cùng các Part. Sau khi dựng Assembly, kiểm tra số component, rebuild và giao thể tích bằng [SOLIDWORKS Interference Detection API](https://help.solidworks.com/2024/English/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.IAssemblyDoc~InterferenceDetectionManager.html). DXF validator kiểm tra kích thước và đơn vị 1:1, cạnh đáy rãnh và cạnh bên tại đúng vị trí theo joint plan. Log và CAD ở thư mục local ngoài Git.

| Lượt thử tại SW 2024 SP5.0 trên copy mặt KDA1 | Kết quả |
|---|---|
| Preview 3+1, 7×4 → 10×6 → 7×4, 5×2 | PASS số temporary bodies, Cancel và trạng thái Part nguồn |
| Native 3+1 | 4 Part Sheet Metal/Normal Cut/Flat Pattern, 4 Drawing, 4 DXF; 6 rãnh đạt checker; Assembly 4 component, không giao thể tích |
| Native 7×4 với lề 50/150,9 mm | Chặn trước khi tạo package vì một rãnh vượt đầu contour đã trim |
| Native 7×4 với lề thử 70/630 mm trên cùng mặt | 11 Part Sheet Metal/Normal Cut/Flat Pattern, 11 Drawing, 11 DXF; 56 rãnh đạt checker; Assembly 11 component, không giao thể tích |
| Native 10×6 với lề thử 70/630 mm | 16 Part Sheet Metal/Normal Cut/Flat Pattern; Assembly 16 component, không giao thể tích |
| Quay về 7×4 với cùng tham số thử | 11 Part và Assembly mới trong thư mục riêng; contour, station và joint plan khớp hash của lượt 7×4 trước |
| Offline | 32 kiểm tra planner, test mức rãnh cho 3+1/7×4/10×6/5×2 và các trường hợp từ chối; Debug/Release x64 đạt trên nhánh Kadai và source tích hợp |
| Bảo toàn dữ liệu | Đối chiếu 110 CAD gốc, 0 thay đổi; bản sao mặt dùng thử cũng giữ nguyên hash |

Tích hợp local được xây từ snapshot của source TAI TOOL đang dùng, đối chiếu với DLL trước tích hợp cho 47.249 mục IL/metadata/tài nguyên khớp. Ngoài các kiểu mới của Kadai, bản tích hợp chỉ khác ở 11 mục dùng để đăng ký, bố trí, bật nút hoặc đóng form Kadai trong Task Pane. IL của Make Hole, Fix Tỉ lệ, DIM Kegaki và CircleFace V3 khớp baseline. Điểm ghép local gồm `ADDIN.csproj`, `BomTaskPaneControl.cs` và `UI/BomCommandLayout.cs`. Các tài liệu đang mở của người dùng được tách khỏi phiên test bằng PID.

Các giá trị 70/630 mm là tham số đối chứng trên mặt được chọn, không phải preset sản xuất. Chưa xác nhận lắp đặt với panel/vật cản trong hành trình lắp, vật liệu và dung sai công ty, contour chính xác ở toàn bộ bề dày, native KF3, hoặc cập nhật Feature/Drawing tại chỗ. **Apply/Export giao diện vẫn khóa.**

Code Kadai và các lượt đối chứng nằm ở commit `34c4da15bac2ca68befe3f53852e0aee99a58f15`. DLL tích hợp đã được chép vào đường dẫn TAI TOOL sau khi SolidWorks thoát. SHA256 sau chép là `98BF509B388404E024E9E3AC924F30F0AE1023EF408417D269A0DEF997560AA7`, MVID `c578002c-bf98-4e2c-8b52-a131eb7eb487`. Đã xác nhận kiểu Kadai và CircleFace V3 trong DLL đã cài. Giữ bản sao DLL/PDB trước khi cài, log sai khác IL, và kết quả CAD tại chỗ. Kiểm tra này xác thực file cài đặt trên đĩa và các package chạy bằng host test, chưa xác thực lần nạp phiên làm việc sau cài đặt.

Đối chiếu API: [Sheet Metal 2013+ data access](https://help.solidworks.com/2024/english/api/sldworksapi/Get_Template_Sheet_Metal_Feature_Data_Example_VBNET.htm), [native Base Flange example](https://help.solidworks.com/2024/english/api/sldworksapi/insert_sheet_metal_base_flange_example_vbnet.htm), [FeatureCut4](https://help.solidworks.com/2024/english/api/sldworksapi/solidworks.interop.sldworks~solidworks.interop.sldworks.ifeaturemanager~featurecut4.html). Kết quả CAD trong báo cáo dựa trên chạy local, không dựa vào ví dụ tài liệu.

## 7. Preview trong Assembly với rãnh đối ứng theo mẫu t7

Trên bản sao native của `4315-04-KDA1-t7.SLDPRT`, `SheetMetal` có chiều dày 1,6 mm, radius 0,1 mm và vật liệu `StSA-`. Sau Base Flange, Cut 6 là feature có kiểu mới `ICE` và kiểu legacy `Cut`. Cut 6 phụ thuộc Sketch 10, Base Flange và Cut 3. Sketch 10 có bốn rãnh mở từ đáy, mỗi rãnh rộng 1,8 mm và sâu 100 mm trong mặt thanh. Thiết lập Cut 6 là Blind 1,6 mm, Normal Cut bật, Direction 2 tắt và Feature Scope bật. Scope body chưa được resolve trong snapshot. Cut 4 dùng surface tham chiếu và báo lỗi external reference 51 trên bản sao. Thanh `d1` dày 1,6 mm có rãnh đối ứng mở từ mép trên, seed rộng 1,8 mm và sâu 70,1 mm trên thanh cao 170 mm.

Kadai chạy từ Model → Macro ngay trong Assembly. Người dùng chọn mặt của component panel cấp trên cùng. Kadai lấy mặt tương ứng và lưu persistent reference trong Part, rồi kiểm tra configuration, update stamp và vị trí của component trước khi dựng. Trục cao, lề và cao độ đáy được nhập theo hệ tọa độ Part panel. Biên dạng mỗi thanh theo mặt thật và được xẻ hai phía đối ứng tại cùng cao độ ghép. Lựa chọn dùng cùng một bộ tọa độ rãnh cho Preview và prototype Cut-Extrude giúp số đếm, vị trí, bề rộng và chiều sâu nhất quán. Native writer vẫn ở chế độ thí nghiệm.

Preview dựng khối rắn tạm từ đường bao đã xẻ rãnh. Cách này cho phép bỏ toàn bộ Preview qua Cancel mà không tạo Feature hoặc component. Hình học giữ ở hệ Part khi gọi `Display3` với component. Lần thử đầu cho thấy nhân thêm `Transform2` làm hiển thị sai trên 6866. Ảnh API sau khi sửa xác nhận khối nằm dưới panel. API hiển thị áp dụng cho component cấp trên cùng, nên với panel nằm trong cụm con hãy mở cụm con. [SolidWorks API Display3](https://help.solidworks.com/2024/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IBody2~Display3.html)

| Bản sao native trên SolidWorks 2024 SP5.0 | Kết quả |
|---|---|
| Assembly 4315-02-KDA1, mặt của component 3d | 7×4 → 5×3 → 7×4. 11 → 8 → 11 khối tạm và 56 → 30 → 56 rãnh. Trục cao Y và đáy 0 mm. |
| Assembly 6866, mặt của component côn có xoay và tịnh tiến | Cùng ma trận số lượng và số rãnh. Trục cao Z trong Part. Đáy thử −162,18 mm, lề thử bằng 36% span mỗi phương. Chọn Y với cùng mặt bị chặn vì một tia có nhiều giao điểm. |
| Giao thể tích trong bộ thanh preview | Cả hai assembly qua ba lượt kiểm tra bằng Modeler. Đối chứng phôi đặc có giao thể tích, bộ thanh đã xẻ không có. |
| Trạng thái CAD và build | Save flag của Assembly và Part panel giữ nguyên qua Preview/Clear. Kiểm 31 file nguồn 6866 và SHA256 t7 so với bản sao. Planner, joint tests và Debug/Release x64 đạt. |

Các lề thử được chọn để rãnh nằm trọn trong contour của mặt cụ thể. Các lượt có lề nhỏ hơn bị chặn trước khi hiển thị. Vẫn cần kiểm tra đường đưa thanh vào, va chạm với chi tiết sẵn có, chuẩn gia công và cập nhật Part/Drawing tại chỗ. **Apply/Export vẫn khóa.** Các ảnh lấy qua API và log ở thư mục local `kadai-assembly-6866`.

Commit code và test chạy trên nhánh Kadai là `54db3935a98a4c4c99bf6d55d95740aa3b16dcf8`. Các file là `Commands/Kadai/KadaiCadContracts.cs`, `KadaiJointGeometry.cs`, `KadaiPreviewBackend.cs`, `UI/KadaiForm.cs`, `UI/BomTaskPaneControl.Kadai.cs` và `tools/KadaiJointGeometryTests.cs`. Bản dựng TAI TOOL được kiểm với DLL hiện hành qua 47.298 mục IL, metadata và tài nguyên ngoài các kiểu Kadai. Ba sai khác đều là điểm tích hợp Kadai trong Task Pane. Make Hole, Fix Tỉ lệ, DIM Kegaki và CircleFace V3 có IL trùng bản đang cài trước đợt này. Điểm bật nút trong source máy là `BomTaskPaneControl.cs`.

Đã cài DLL/PDB sau khi SolidWorks đóng và kiểm lại SHA256, IL và MVID của DLL cài. DLL mới có SHA256 `2BB8254C6415D733519D94A1D2F55AB7DB92F1EEC32D97BFAED7D333F9FE02C9` và MVID `34acaf37-a974-4e5e-8af9-4eb5e6ed2247`. Bản sao dự phòng và danh sách sáu file nguồn tích hợp nằm trong `kadai-assembly-6866/integration`. Tệp `CheckAssemblyHole.cs` được chỉnh đồng thời trên máy có SHA256 giữ nguyên trước và sau triển khai. Đối chiếu 13/13 AutoRecover sau khi đóng phiên thử đạt.

Trong một phiên SolidWorks thử nghiệm riêng, host nạp trực tiếp DLL đã cài, xác nhận cùng MVID, khởi tạo Task Pane control với bản sao Assembly 6866 đang active và gọi nút qua WinForms. Model → Macro hiển thị nút Kadai, nút đã bật, form mở được và Apply vẫn khóa. Log launcher ở thư mục local.
