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

## 8. Vật liệu custom và bảng uốn ST

Mẫu native t7 và d1 dùng `StSA-` trong `kiku_板材_材料 2019.sldmat`. Thư viện cũ cũng có tên vật liệu này. Writer thử nhận đường dẫn thư viện cụ thể, tên vật liệu và đường dẫn bảng uốn từ các tùy chọn `--material-db`, `--material` và `--bend-table`. Bộ thử dùng bảng `BD_Kiku_ST_2020.xlsx` đã được chấp thuận. Workbook có dữ liệu bend deduction theo mm và mục chiều dày 1,6 mm.

Trên SolidWorks 2024 SP5.0, truyền workbook Excel vào custom allowance khi tạo Base Flange làm mất đường dẫn. API nhập workbook trực tiếp cũng trả lỗi. Ghi mode Bend Table và đường dẫn trực tiếp lên feature Sheet Metal của body, sau đó gọi `ModifyDefinition`, cho phép đọc lại đúng bằng `GetCustomBendAllowance`. Writer kiểm tra chiều dày, mode, đường dẫn bảng và vật liệu của configuration trước khi lưu Part. Nó kiểm lại các giá trị này khi mở Part đã lưu để dựng Assembly thử. [API đọc vật liệu theo configuration](https://help.solidworks.com/2024/english/api/sldworksapi/solidworks.interop.sldworks~solidworks.interop.sldworks.ipartdoc~getmaterialpropertyname2.html) · [API Sheet Metal](https://help.solidworks.com/2024/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.ISheetMetalFeatureData_members.html)

Bộ KDA1 7×4 đạt kiểm tra trên 11 Part Sheet Metal, 11 Drawing và 11 DXF. Validator đo đúng 56 rãnh, đơn vị mm và tỉ lệ 1:1. Assembly thử có 11 component và không có giao thể tích giữa các thanh. Build Debug/Release x64 đạt. SHA256 của bản sao mặt panel, thư viện vật liệu và workbook ST giữ nguyên. Log API và package ở `kadai-assembly-6866/st-native-04`.

Thay đổi nằm trong `Commands/Kadai/KadaiNativePrototype.cs` và `tools/KadaiCadTests.cs`. **Apply vẫn khóa.** Các thanh của bộ thử là tấm phẳng. Phép thử lưu và mở lại bảng ST, Flat Pattern và DXF của các thanh này không đo độ bù cho một chi tiết thực sự có nếp uốn.

## 9. Chọn mặt panel và mặt phẳng đế trong Assembly

Kadai đưa **Chọn mặt panel** và **Chọn mặt đế** lên đầu cửa sổ. Người dùng có thể bấm nút rồi chọn một mặt trong vùng đồ họa, hoặc chọn mặt trước rồi bấm nút. Cửa sổ hiện tên component đã chọn. Khi chọn đủ hai mặt, Kadai quy điểm và pháp tuyến của mặt đế về hệ Part panel, suy ra trục cao, chiều từ đế về phía panel và cao độ đáy. Đổi bên đặt thanh bằng cách chọn mặt đế ở phía tương ứng. Các thông số tọa độ, lề và khe hở nằm trong mục **Thông số nâng cao**.

Preview thử lề đang nhập trước. Với tùy chọn tự tìm lề, các lượt thử thu dần vùng bố trí và chỉ hiện lưới khi kiểm tra đủ rãnh. Báo cáo Preview hiện cặp lề thực dùng. Người dùng có thể bỏ tùy chọn để thử đúng lề nhập. Mặt đế cong, nghiêng so với cả ba trục của Part panel hoặc cắt qua panel bị từ chối. Hỗ trợ chọn component cấp trên cùng, một mặt panel liên tục và một mặt phẳng đế. [Cách đọc điểm và pháp tuyến từ mặt phẳng bằng API](https://help.solidworks.com/2023/English/api/sldworksapi/Get_Normal_of_Planar_Surface_Example_VB.htm)

| Lượt thử bằng API trên SolidWorks 2024 SP5.0 | Kết quả |
|---|---|
| Bản sao 4315-KDA1, mặt panel 3d và mặt đế bs1 | Hướng +Y, cao độ 0 mm trong Part panel. 7×4 rồi 5×3, lần lượt 56 và 30 rãnh. |
| Bản sao 6866, mặt panel côn và mặt đế bs-new | Hướng +Z, cao độ khoảng −306,269 mm trong Part panel. 7×4 rồi 5×3, lần lượt 56 và 30 rãnh. Component đế được resolve trong bộ nhớ của phiên thử đọc riêng. |
| Đối chứng | UI từ chối mặt cong làm đế. Test số kiểm tra hướng cao âm và rãnh đối ứng. Save flag của Assembly giữ nguyên trong thao tác chọn mặt và Preview. |

Build Debug/Release x64 trên nhánh và source TAI TOOL đạt. Đối chiếu 47.298 mục IL/metadata/tài nguyên ngoài Kadai cho 0 sai khác. Các file trên nhánh gồm `KadaiCadContracts.cs`, `KadaiPlanner.cs`, `KadaiJointGeometry.cs`, `KadaiPreviewBackend.cs`, `UI/KadaiForm.cs` và `tools/KadaiJointGeometryTests.cs`. Log kiểm thử nằm trong thư mục local `kadai-assembly-6866/face-pick-cad-02`. **Apply vẫn khóa.**

TAI TOOL đã được cập nhật khi SolidWorks đóng. DLL cài có MVID `103a06d5-5a5a-4ca1-86b9-127315f818e1` và SHA256 `72677A85918D84100A2C6261F252C15FC3A74C1AA26E789C4C1D7828F339BFDF`, khớp bản ứng viên đã kiểm thử. Cùng đợt này bản cài nhận code writer thử ST từ mục 8. Đối chiếu 31 file nguồn 6866 cho 0 sai khác và t7 khớp bản sao tham chiếu.

## 10. Bố trí theo mép base và chọn phương bám panel

Giao diện giữ thao tác chọn hai mặt, số thanh dọc/ngang, hai khoảng chừa áp dụng chung và chọn **Dọc** hoặc **Ngang** bám panel. Sơ đồ dùng chiều dài các thanh preview thực tế. Trạng thái ngắn thay khung log. Các preset, mục thông số nâng cao, ô chiều dày riêng và nút tính layout 2D đã bỏ khỏi form.

Mỗi phương đo khoảng chừa từ biên của mặt base đã chọn đến tim thanh đầu và cuối. Pitch bằng chiều dài đo theo phương bố trí trừ hai khoảng chừa, chia cho số khoảng giữa các thanh. Một thanh đặt ở giữa phạm vi sau khi trừ hai khoảng chừa. Kadai lấy cực trị hình học của mặt base trong hệ Part panel, rồi giới hạn các thanh theo biên mặt thật. Thanh bám panel lấy tiết diện từ panel và mở rãnh từ đáy. Thanh vuông góc có đỉnh thẳng dưới panel và mở rãnh từ trên. Lưới giữ nguyên khoảng chừa người dùng nhập. Vị trí ngoài biên thanh không sinh rãnh. Vị trí giao sát đầu thanh và thiếu chỗ tạo rãnh dừng Preview, báo cặp thanh cụ thể. [API đo điểm trên mặt SolidWorks](https://help.solidworks.com/2024/english/api/sldworksapi/solidworks.interop.sldworks~solidworks.interop.sldworks.iface2~getclosestpointon.html)

Preview dùng chiều dày thanh chung 1,6 mm và độ hở rãnh tổng 0,2 mm theo mẫu 4315. Phần đỉnh thẳng lấy mốc 170 mm và giảm khi cần để chừa ít nhất 1 mm dưới điểm thấp nhất của panel. Cao độ ghép lấy mốc 100 mm hoặc điểm giữa khi thanh đỡ thấp hơn. Đây là quy tắc đang dùng cho Preview. Đọc lại vật liệu custom dùng cặp tên vật liệu và thư viện gán cho configuration của base. Cả hai base bản sao trả về `kiku_板材_材料 2019 / StSA-`.

| Assembly bản sao, 11 dọc × 6 ngang | Kết quả trên SOLIDWORKS 2024 SP5.0 |
|---|---|
| 4315-KDA1, mặt bs1 | Chừa 50/80 mm. Hai phương bám panel đều đạt. Pitch 212 mm và 126 mm. Có 38 vị trí giao thực tế trên 66 giao điểm lưới. |
| 6866, mặt bs-new | Chừa 50/50 mm. Hai phương đều đạt. Pitch 215 mm và 270 mm. Có 48 vị trí giao thực tế trên 66 giao điểm lưới. |
| 6866, đối chứng 50/80 mm | Preview báo rãnh L-010 / C-006 sát đầu thanh. Khoảng chừa nhập giữ nguyên. |

Đo tim thanh trong hình học preview xác nhận hai khoảng chừa và pitch. Kiểm tra SolidWorks Modeler không phát hiện giao thể tích giữa hai nhóm thanh xẻ rãnh ở các lượt đạt. Test form xác nhận bốn ô số, một lựa chọn phương bám panel và Apply bị khóa. Save flag và update stamp không đổi qua lượt Preview. Build Debug/Release x64 đạt. Log local nằm trong `kadai-assembly-6866/base-layout-cad-03`. Các file sửa thuộc Kadai và hai test planner/joint. **Apply vẫn khóa.**

Bản cài TAI TOOL có MVID `ac27e86c-189c-41c9-ab0e-0bf7e638230a` và SHA256 `50B4A9587AC368337117208BD20220C95898ECDC3E88B673DABD25A85CA348D0`. Đối chiếu DLL đang dùng trước đợt này, bản dựng kiểm thử và DLL cài không thấy sai khác qua 47.274 mục IL/metadata/tài nguyên ngoài Kadai. Đã sao lưu DLL/PDB và đúng năm file nguồn Kadai trước khi cài.

Lượt cuối nạp đúng DLL từ đường dẫn cài đặt và chạy lại trên bản sao mới của file 6866 đã lưu ngày 19/11/2026. Kết quả hình học và UI trùng bảng trên. 17 file CAD nguồn của bộ 6866 khớp SHA256 trước và sau lượt thử. Log local bắt đầu ở `kadai-assembly-6866/base-layout-cad-04/run.log`.

## 11. Thanh chạy hết base, chiều cao cố định và Apply

Chiều dài của mỗi thanh lấy từ giao giữa vị trí thanh và biên mặt base. Với nhóm bám panel, tiết diện trên đi theo panel trong vùng phủ. Đoạn ngoài vùng phủ nối ngang từ cao độ tại mép panel gần nhất ra hết base. Thanh nằm hoàn toàn ngoài vùng phủ dùng chiều cao cố định. UI dùng một ô **Chiều cao thanh cố định (mm)** chung, đo từ mặt base và mặc định 170 mm. Giá trị quá thấp để ghép rãnh hoặc quá gần panel bị báo lỗi, giữ nguyên giá trị nhập.

Apply chỉ mở khi có Preview hợp lệ. Đổi tham số, đổi mặt hoặc mất nguồn CAD làm mất hiệu lực Preview. Apply chọn thư mục cha và tạo một gói mới có tên riêng. Gói chứa Part Sheet Metal, Drawing trải phẳng, DXF và Assembly các thanh theo đúng hệ tọa độ nguồn. Vật liệu lấy từ cấu hình Part base, bảng uốn lấy `BD_Kiku_ST_2020.xlsx`. Ghi file tạo mới và đánh dấu **INCOMPLETE** trong thời gian xuất. Bộ tạo thử cũ vẫn hỗ trợ các đầu vào kiểm chứng trước đó. Phần mép cong được lấy mẫu với sai số dây cung giữa mẫu 0,05 mm. [API xuất DXF từ Part Sheet Metal](https://help.solidworks.com/2024/english/api/sldworksapi/solidworks.interop.sldworks~solidworks.interop.sldworks.ipartdoc_methods.html)

Trên 4315-KDA1 (mặt base bs2) và 6866 (mặt bs-new), test 11 dọc × 6 ngang tại 50/50 mm và chiều cao cố định 140 mm đạt cho cả hai phương chọn. Kết quả trên 6866 cho đủ 66 giao điểm và tất cả thanh chạm biên base ở hai đầu theo phép đo. Modeler không báo giao thể tích giữa các thanh đã tạo rãnh. UI từ chối chiều cao 1 mm và 1.000 mm trong bộ mẫu và khóa lại Apply khi đổi số lượng. Debug/Release x64 và kiểm tra planner/joint đạt. Đối chiếu 47.266 mục DLL so với bản đang dùng chỉ thấy một điểm ngoài namespace Kadai đổi nội dung tooltip nút Kadai.

Apply đã được gọi từ form trên bản sao 6866 với 7 dọc × 4 ngang, chừa 50/50 mm, chiều cao cố định 140 mm và phương dọc bám panel. Gói có 11 Part Sheet Metal, 11 Drawing, 11 DXF và 1 Assembly chứa 11 component. Mở lại Assembly xác nhận mỗi Part lấy vật liệu `StSA-` và mọi component tham chiếu file trong thư mục mới. Kiểm tra DXF đo được chiều dài toàn base 1.450/2.250 mm theo phương, chiều cao nhóm đỡ 140 mm và 56 rãnh 1,8 mm ở đúng vị trí. Trạng thái lưu và update stamp của CAD nguồn thử không đổi. SolidWorks tiếp tục ghi gói sau khi bộ điều phối chạm giới hạn 150 giây. Kết quả cuối của host là `EXIT 0` và gói đã mở lại được. Đã tăng giới hạn bộ điều phối cho lượt thử sau. Log local ở `kadai-assembly-6866/full-rib-cad-02`.

Đã cài khi các tiến trình SolidWorks đóng và xác nhận DLL cài khớp DLL thử. MVID `b98130e3-7484-48ea-851e-9b9159b8e2f1`, SHA256 `937524544A1609D767D18115BC0507B044DB8A9052FA8A8A2289A5BA4F6874E8`. 31 file nguồn trong thư mục 6866 khớp mã băm của bản ghi trước. Các file phần mềm đã chỉnh là ba file `Commands/Kadai/KadaiPlanner.cs`, `KadaiPreviewBackend.cs`, `KadaiNativePrototype.cs` và hai file `UI/KadaiForm.cs`, `UI/BomTaskPaneControl.Kadai.cs`.

## 12. Crash khi Apply tạo Assembly, 22/11/2026

Ảnh debugger dừng tại `KadaiNativePrototype.CreateAssembly → ISldWorks.OpenDoc6` với ngoại lệ native `0xC0000005` trong `clr.dll`, đọc địa chỉ `0x10`. Cuối `Output-Debug.txt` ghi cùng ngoại lệ và tiến trình SolidWorks thoát. Thư mục `Kadai-20261122-001010-fe316d` còn 12 Part, 12 Drawing, 12 DXF và 64 dòng rãnh trong `JointPlan.csv`. Thư mục chỉ có dấu `INCOMPLETE.txt` và thiếu Assembly. Bằng chứng khoanh vùng được lần mở lại Part sau khi xuất. Cơ chế gây ngoại lệ native của lần này chưa được xác định bằng dump.

`Commands/Kadai/KadaiNativePrototype.cs` tạo Assembly đích trước vòng lặp xuất. Với mỗi thanh, Apply xuất Part, Drawing và DXF, sau đó chèn Part đã nạp bằng `AddComponent5` rồi đóng cửa sổ Part. Bước ghi Assembly kiểm tra số component, phép đặt, trạng thái cố định và giao thể tích. Apply chỉ đánh dấu hoàn tất sau khi lưu Assembly và BOM. Công cụ chẩn đoán ghép từ Part có sẵn vẫn giữ đường nạp riêng. [SOLIDWORKS API AddComponent5](https://help.solidworks.com/2024/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IAssemblyDoc~AddComponent5.html)

Build TAI TOOL Debug/Release x64 đạt. Đối chiếu bản Debug với DLL đang dùng trước khi sửa cho 0 sai khác tại 47.641 mục IL, metadata và tài nguyên ngoài Kadai. Bản sửa phần mềm gồm một file `Commands/Kadai/KadaiNativePrototype.cs`.

Kiểm thử API trong SolidWorks 2024 SP5.0 bật bộ theo dõi MARK của TAI TOOL. Preview trên bản sao 4315 và 6866 đạt ở cả hai phương bám panel. Apply trên bản sao 6866 với 8 dọc × 4 ngang, chừa 30/30 mm và thanh cố định cao 140 mm, tạo đủ 12 Part Sheet Metal, 12 Drawing, 12 DXF, BOM và Assembly. Mở lại Assembly xác nhận 12 component có vật liệu `StSA-` và tham chiếu Part trong gói mới. Đo DXF xác nhận biên khép kín, chiều dài theo base và 64 rãnh rộng 1,8 mm. Trạng thái lưu của mô hình thử và SHA256 của 17 file CAD nguồn 6866 giữ nguyên. Phiên thử không nạp các add-in bên thứ ba. Log nằm trong thư mục local `kadai-assembly-6866/apply-crash-cad-01`.

Đã sao lưu và cài đúng DLL thử khi SolidWorks đóng. MVID `11d7a721-2cbe-48e3-9c93-8f90500c1b41`, SHA256 `FE070BD3929FD838BB91D16E06ECD2D075846E933DBD34A7E8F14F87A9B2F29F`. Đối chiếu DLL sau cài cho 0 sai khác ngoài Kadai. Chức năng đặt tên component đang ở giai đoạn trao đổi.

## 13. Chọn template, vật liệu, tên Part và thư mục khi Apply

Apply hỏi theo thứ tự **Template Part → Vật liệu → Tên Part → Template Drawing → Thư mục lưu**. Hộp thoại **New SOLIDWORKS Document** lấy danh sách từ **File Locations → Document Templates** của phiên chạy. Kadai đọc đường dẫn đã chọn từ `FileNewPreNotify` và trả về 1 để dừng việc tạo tài liệu tại bước chọn. Hộp thoại **Material** gốc chạy trên một Part tạm từ template đã chọn, đề xuất vật liệu của base nếu tìm thấy trong thư viện. Part tạm được đóng mà không lưu. Người dùng đặt tên theo hai dãy số và sửa từng tên trong danh sách. Các file Part, Drawing và DXF dùng tên đã xác nhận. Thư mục lưu khởi đầu ở thư mục của Assembly nguồn. [SOLIDWORKS API RunCommand](https://help.solidworks.com/2023/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.ISldWorks~RunCommand.html)

Template Drawing do người dùng chọn giữ khổ giấy và khung tên. Hình chiếu Flat Pattern giảm tỉ lệ nếu cần để vừa tờ. Kiểm tra `板材_バラ図枠_2024(D).DRWDOT` cho khổ A4 và khung `板材_Ver.05.slddrt`, trùng khung của Drawing t7. Tỉ lệ tờ trong template là 1:1, khác tỉ lệ 1:5 trong bản vẽ mẫu đã lưu.

`ExportToDWG2` làm tiến trình thử thoát sau khi ghi file với Part tạo từ `Part_板材_2024(D).PRTDOT`. Sự cố lặp lại khi dùng tên ASCII, tắt bộ theo dõi MARK và xuất từ Part bản sao đã mở lại. Tạo Drawing trực tiếp từ cùng Part đạt. Với Apply, DXF cho thanh phẳng được ghi ở kích thước thực từ cạnh của mặt Part đã cắt. SolidWorks 2024 SP5.0 nhập được định dạng ASCII DXF R12 thử mà phần header khai báo mm. Đường xuất native cũ phục vụ thử nghiệm prototype. Khi gói dừng giữa chừng, `INCOMPLETE.txt` cho biết thao tác và tên file cuối cùng mà Kadai đã bắt đầu. [Mã nhóm DXF LINE](https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-DXF/files/GUID-FCEF5726-53AE-4C43-B4EA-C84EB8686A66.htm)

Với template công ty, một lượt thử cho thấy Part mất kết nối COM sau khi đóng Drawing. Apply chèn Part vào Assembly khi tài liệu Part còn mở, rồi tạo hình chiếu từ đường dẫn Part đã lưu. Phép thử trên bản sao 6866 với 7 dọc × 4 ngang dùng hai template công ty, chọn `St-` và đặt tên có tiếng Nhật, tiếng Việt và dấu phẩy. Gói hoàn chỉnh có 11 Part, 11 Drawing, 11 DXF và một Assembly. Mở lại Assembly xác nhận 11 component Sheet Metal cố định với vật liệu đã chọn. Kiểm tra tham chiếu của cả 11 Drawing xác nhận đúng Part. Kiểm tra tờ mẫu xác nhận khổ A4, đúng khung tên và có hình chiếu Flat Pattern. SolidWorks mở lại DXF đầu tiên. Đối chiếu 11 biên DXF khép kín với gói native cùng hình học cho kết quả trùng tọa độ ở 1:1, gồm 56 rãnh. Log local ở `kadai-assembly-6866/apply-options-cad-08` và `kadai-assembly-6866/apply-options-readback-01`.

Các bước mở và hủy hộp thoại **New SOLIDWORKS Document** và **Material** đã được kiểm tra riêng trong tiến trình thử. Lượt kiểm tra gói đầy đủ truyền các lựa chọn vào điểm bắt đầu ghi của form bằng API. Vị trí tab ban đầu do hộp thoại SolidWorks quản lý. Build Debug/Release x64 đạt. SHA256 của 17 file CAD nguồn 6866 giữ nguyên. So DLL ứng viên với DLL TAI TOOL đang dùng có 0 sai khác tại 47.645 mục IL, metadata và tài nguyên ngoài Kadai. MVID ứng viên `77c145df-3065-4952-92b6-b45067430f89`, SHA256 `24BA4B24B9D98F87EE5F5AE65E570791CF30F77BC5962DB912F8F93F4A42DCF7`.

Các file code sửa gồm `Commands/Kadai/KadaiCadContracts.cs`, `Commands/Kadai/KadaiNativePrototype.cs`, `Commands/Kadai/KadaiPreviewBackend.cs` và `UI/KadaiForm.cs`.

## 14. Apply dừng trước khi tạo gói, 23/11/2026

Log Debug ghi nhiều `InvalidOperationException` trong `ADDIN.dll` nhưng thiếu thông báo và stack trace. Phiên ghi log thoát với mã 0. Chưa tìm được gói mới trong các thư mục đã rà soát để lấy bước cuối từ `INCOMPLETE.txt`. Chưa xác định được ngoại lệ cụ thể của lần chạy đó.

Phát hiện một lỗi trong luồng chọn mẫu của `UI/KadaiForm.cs`. `FileNewPreNotify` có thể trả đuôi file thay vì đường dẫn. Bản trước luôn hủy thao tác tạo ngay ở sự kiện này. Bản sửa tiếp tục khi nhận dữ liệu sơ bộ và lấy đường dẫn từ sự kiện trước hoặc sau tạo tài liệu. Nếu xác nhận rồi mà API thiếu đường dẫn, Kadai mở bộ chọn file theo cấu hình Document Templates. Bước hủy vẫn trả người dùng về Preview. Khi Apply lỗi, form hiện bước lỗi và ghi stack trace vào Output Debug. [SOLIDWORKS FileNewPreNotify](https://help.solidworks.com/2025/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.DSldWorksEvents_FileNewPreNotifyEventHandler.html)

Sáu trường hợp xử lý lựa chọn đều đạt với DLL Debug đã build, gồm thông báo đuôi file rồi hủy, đường dẫn đầy đủ, đường dẫn UNC nhận sau khi tạo tài liệu và yêu cầu chọn lại khi thiếu đường dẫn. Debug/Release x64 đạt. So sánh 47.645 mục IL, metadata và tài nguyên ngoài Kadai cho 0 sai khác. SHA256 của 17 CAD nguồn 6866 giữ nguyên. Lượt chạy SolidWorks 2024 sau khi phiên làm việc đóng được ghi ở mục 15.

Apply có hai lượt chọn mẫu cho cả gói. Trước khi mở hộp thoại gốc, Kadai hiện thông báo riêng cho **1/5 · Template Part** với tab **Part** và mẫu `.prtdot`, hoặc **4/5 · Template bản vẽ** với tab **Draw** và mẫu `.drwdot`. Người dùng có thể hủy từ thông báo để trở về Preview. Nếu chọn nhầm loại file, hộp thoại chọn lại nhắc đúng tab cần dùng.

## 15. Giữ tỷ lệ sơ đồ 2D khi đổi kích thước Preview

`UI/KadaiForm.cs` vẽ lại sơ đồ khi vùng Preview đổi kích thước. Hai trục dùng cùng tỷ lệ mm sang pixel. Khung tấm và các thanh cùng căn giữa theo kích thước khả dụng. Lượt thử render từ bản sao 6866 giữ đúng tỷ lệ tấm 2.250 × 1.450 mm ở bốn khung 910 × 453, 475 × 64, 445 × 393 và 1.050 × 188 pixel, và Apply vẫn mở sau mỗi lần đổi kích thước.

Kiểm thử API trên SolidWorks 2024 SP5.0 xác nhận nhận được đường dẫn hai template công ty khi SolidWorks phát sự kiện tạo tài liệu. Apply trên 6866 tạo 11 Part Sheet Metal, 11 Drawing, 11 DXF và mở lại được Assembly. Drawing kiểm tra có hình chiếu Flat Pattern và giữ khung A4 đã chọn. Phép thử dùng API để truyền các lựa chọn vào form. Log local tại `kadai-assembly-6866/preview-resize-cad-01/run.log`. Build Debug/Release x64 đạt. MVID DLL kiểm thử `e8ef3b4a-4e55-44a4-8ce3-3e624877a367`, SHA256 `469FAD7BD9E7A8E52AC3799AAE94D95CC31D6EB5FE1C28A7D3F70CCC78103F5D`.

Đối chiếu 11 DXF với bộ native tham chiếu cho kết quả trùng biên dạng 1:1 và đủ 56 rãnh. 17 file CAD nguồn 6866 giữ nguyên mã băm. Bản đã thử được cài vào TAI TOOL khi SolidWorks đóng. File phần mềm cập nhật là `UI/KadaiForm.cs`, DLL và PDB. Đối chiếu DLL đã cài cho 0 sai khác tại 47.645 mục ngoài Kadai.

## 16. Nhận diện thanh trong 3D khi đặt tên Part

Ở bước **Tên Part**, Kadai khôi phục các body Preview sau khi quay lại từ Part tạm dùng để chọn vật liệu. Bảng tên nằm ở mép màn hình và cửa sổ Kadai chính tạm ẩn để chừa chỗ xem mô hình. Dòng hiện tại trong bảng làm thanh tương ứng nổi màu hồng. Liên kết dùng mã thanh cố định, nên sửa tên Part không đổi vị trí được tô. Chuyển dòng hoặc đóng bảng khôi phục màu nhóm ban đầu. Hai file sửa là `UI/KadaiForm.cs` và `Commands/Kadai/KadaiPreviewBackend.cs`. [SOLIDWORKS Display3](https://help.solidworks.com/2024/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IBody2~Display3.html)

Log Debug thực tế ngày 23/11 xác nhận SolidWorks trả đúng đường dẫn hai template đã chọn. Gói người dùng xuất lúc 11:59, `Kadai-20261123-115949-252858`, có 11 Part, 11 Drawing, 11 DXF, một Assembly và `PASS.txt`. Hai ngoại lệ chung trong log xuất hiện trước khi chọn template. Không có lỗi Apply được ghi sau đó.

Trên bản sao 6866 trong SolidWorks 2024 SP5.0, phép thử đổi dòng `L-001` sang `C-004` và sửa tên trong bảng thực. Hai ảnh đồ họa xác nhận vùng màu hồng chuyển sang vị trí khác. Hủy bảng khôi phục Preview, bật lại cửa sổ Kadai chính và giữ nguyên cờ lưu cùng update stamp của Assembly. Ảnh và log ở `kadai-assembly-6866/naming-highlight-cad-01`.

Apply sau lượt thử tô sáng xuất đủ 11 Part, 11 Drawing và 11 DXF. Assembly mở lại được. 11 DXF trùng biên dạng 1:1 với mẫu native, đủ 56 rãnh. 17 file CAD nguồn giữ nguyên mã băm. Build Debug/Release x64 đạt. Bản thử được cài sau khi SolidWorks đóng và đối chiếu cho 0 sai khác tại 47.742 mục ngoài Kadai. MVID `b71a9f43-f2bb-42e9-b190-add2b0ec65b5`, SHA256 `B57A76D4F74BCAFCC84F4D1E5CC1D1B8382310C973E749AACB421ED90C4C62AE`.

## 17. Chèn thanh Kadai trực tiếp vào Assembly đang mở

Apply dùng chính tài liệu Assembly đã chọn mặt panel và mặt đế, tại cấp trên cùng. Mỗi thanh được lưu thành Part Sheet Metal, chèn vào Assembly khi Part còn được nạp, đặt theo hệ tọa độ của panel và cố định. Kadai tiếp tục tạo Drawing Flat Pattern theo template đã chọn. Apply bỏ bước xuất DXF và tạo Assembly riêng. Assembly đang mở có thay đổi chờ lưu sau Apply. Người dùng bấm **Ctrl+S** khi muốn lưu.

Kadai kiểm tra giao thể tích giữa hai nhóm thanh dọc và ngang vừa chèn. Bộ kiểm tra sao chép các body Sheet Metal thực, đặt bản sao theo transform của từng component trong hệ tọa độ Assembly rồi dùng Modeler kiểm tra giao thể tích. Nếu một bước lỗi sau khi đã chèn, Kadai thử gỡ những component do lần chạy đó thêm và giữ các file đã tạo trong thư mục có dấu `INCOMPLETE.txt`. Phạm vi sửa gồm `Commands/Kadai/KadaiNativePrototype.cs`, `Commands/Kadai/KadaiPreviewBackend.cs` và `UI/KadaiForm.cs`. [SOLIDWORKS API IModeler](https://help.solidworks.com/2024/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IModeler_members.html)

Build TAI TOOL Debug/Release x64, 36 kiểm tra bộ bố trí và kiểm tra hình học rãnh đạt. So DLL Debug với bản đang cài cho 0 sai khác ở 47.742 mục ngoài Kadai. SHA256 của 17 file CAD nguồn 6866 giữ nguyên. Bản đầu của ba class đã được đồng bộ vào project đang dùng để người dùng tự rebuild. Phiên kiểm thử ở mục 18 xác nhận Apply chèn trực tiếp vào Assembly trong bộ mã mới.

## 18. Chọn mặt ウラ và ghi W/L trên bản vẽ 展開

Bảng đặt tên Part cho phép chọn một trong hai mặt rộng làm **ウラ**, đảo mặt từng thanh hoặc áp dụng cho các thanh cùng phương. Lựa chọn bám theo mã thanh khi người dùng sửa tên Part. Preview dùng một mặt tạm màu hồng để chỉ phía ウラ của dòng đang chọn. Kadai yêu cầu xác nhận đủ các thanh trước khi tiếp tục.

Khi tạo Part, Kadai đổi lựa chọn từ hệ tọa độ Preview sang pháp tuyến theo hệ tọa độ Part. Mã tạo Part gán màu hồng cấp mặt trong cấu hình thường và Flat Pattern. Kadai kiểm tra pháp tuyến theo hướng nhìn của hình chiếu và lật view nếu cần để hiển thị **表**. Drawing giữ khổ và khung tên của template, dùng tên dạng `図面ビュー<n>` và thêm ký hiệu **表** khoanh tròn. [SOLIDWORKS API tạo và lật Flat Pattern view](https://help.solidworks.com/2024/english/api/sldworksapi/Create_and_Flip_Flat-Pattern_View_of_Sheet_Metal_Part_Example_VB.htm)

Kadai chọn đúng Drawing View và chạy điểm gọi `RunNative` của **DIM Kegaki** với khoảng dời 10 mm. Lệnh sẵn có hiện sketch Bounding Box và tạo kích thước. Kadai kiểm tra dữ liệu hiển thị của biên và đủ hai kích thước trước khi lưu Drawing. [SOLIDWORKS API đọc Bounding Box của Flat Pattern view](https://help.solidworks.com/2022/english/api/sldworksapi/Get_Number_of_Lines_Flat-pattern_Drawing_View_Boundary-box_Sketch_Example_CSharp.htm)

Build TAI TOOL Debug/Release x64 đạt. Kiểm thử control WinForms thực xác nhận từ chối mặt chưa chọn, đảo mặt, áp dụng theo nhóm, sửa riêng từng thanh và giữ tên đã sửa. Đối chiếu DLL Debug cho 0 sai khác tại 47.742 mục ngoài Kadai. MVID DLL kiểm thử `0872a226-d0a0-4cd0-b20f-09c29c44eaff`, SHA256 `F1C47BEF8534023C3713C4153ED4B6CCE92295CB1AB013476DC073AFEF9B0A01`.

Các file code cập nhật gồm `Commands/Kadai/KadaiCadContracts.cs`, `Commands/Kadai/KadaiNativePrototype.cs`, `Commands/Kadai/KadaiPreviewBackend.cs` và `UI/KadaiForm.cs`.

Lượt thử đầu tiên nạp đúng DLL trong một PID riêng của SolidWorks 2024 SP5.0 và tạo đủ 11 Part cùng 11 Drawing. Tiến trình thử thoát ở bước kiểm tra Assembly với lỗi native `0xc0000374`. Mã ghi nhận bước cuối đang gọi kiểm tra giao thể tích. Phiên SolidWorks người dùng vẫn chạy. Sau khi đổi sang kiểm tra bản sao body qua Modeler, Apply hoàn tất, xác nhận đủ 11 component được chèn và ghi dấu hoàn tất trong gói.

Lượt thử cuối qua COM mở bản sao 6866 trong một PID riêng và tạo **11 Part Sheet Metal cùng 11 Drawing**. Thử với cả hai lựa chọn mặt ウラ xen kẽ. Mở lại các Part kiểm tra màu ở cấu hình thường và Flat Pattern. Mở lại toàn bộ 11 Drawing xác nhận view `図面ビュー1` nhìn mặt **表**, có ký hiệu **表** khoanh tròn, giữ khổ và khung A4 đã chọn, Bounding Box có bốn cạnh và hai giá trị DIM khớp với hình học của biên trong sai số kiểm thử 0,05 mm. Thanh thử có cặp kích thước đại diện 1.450 × 302,735 mm và 2.250 × 170 mm.

Apply giữ nguyên component có sẵn và SHA256 của Assembly trên đĩa trước khi người kiểm thử chủ động lưu bản sao. Gói thành công có 0 DXF và 0 Assembly mới. Khi chèn giả lập một lỗi sau thanh đầu tiên của lượt thử riêng, chương trình gỡ đúng thanh đó. Lưu thủ công và mở lại bản sao xác nhận tên và đường dẫn tham chiếu đến toàn bộ component được giữ. SHA256 của 17 file CAD nguồn giữ nguyên. Phiên SolidWorks người dùng và DLL đang dùng giữ nguyên. Log kiểm thử local nằm ở `kadai-assembly-6866/ura-drawing-separate-06/run.log`.

## 19. Bảng chọn mặt modeless và kiểm tra cấu hình 展開

Log Debug của người dùng ghi phiên SolidWorks nạp `C:\Tao moi\ADDIN.dll`. Project đang dùng lúc đó mới nhận bản vá kiểm tra giao thể tích và vẫn tạo hình chiếu phẳng theo luồng cũ. Bản cập nhật đồng bộ luồng chọn mặt, tạo bản vẽ 展開 và gọi DIM Kegaki.

Bảng Tên Part và mặt ウラ là cửa sổ modeless do cửa sổ SolidWorks sở hữu. Khi bảng mở, viewport giữ trạng thái cho phép tương tác. **Xem rõ thanh** đưa thanh vào khung nhìn, **Xem mặt đối diện** đổi hướng camera và giữ lựa chọn mặt, còn **Chọn mặt đang xem làm ウラ** và **Đảo mặt ウラ** cập nhật mặt màu hồng. Tùy chọn **Ẩn thanh Preview khác** tác động lên body tạm của Kadai. Kadai trả góc nhìn trước đó khi đóng bảng sau khi dùng các nút đổi góc. Nếu tài liệu đang mở đã thay đổi, người dùng cần tạo Preview mới. [SolidWorks API IModelView](https://help.solidworks.com/2024/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IModelView.html)

Kadai kiểm tra view 展開 tham chiếu đúng cấu hình phái sinh, feature Flat Pattern được mở và rebuild không có lỗi. Cấu hình ban đầu của Part được khôi phục để dùng trong Assembly. Trước khi lưu Drawing, Kadai gọi DIM Kegaki và kiểm tra Bounding Box cùng hai DIM W/L, giữ quy ước **ウラ** màu hồng và view nhìn mặt **表**.

Màu chuẩn đọc qua COM từ bản sao chỉ đọc của `4315-04-KDA1-t7.SLDPRT` và `4315-04-KDA1-d1.SLDPRT`. Cả hai mẫu lưu màu ở cấp mặt với RGB **255, 192, 192** (`#FFC0C0`) trong cấu hình thường và 展開. Các hệ số hiển thị đi cùng màu được giữ theo mẫu. Kadai dùng chung RGB này cho Preview, gán lại màu đúng mặt sau khi quay về cấu hình thường rồi lưu Part. Log đọc mẫu ở `kadai-assembly-6866/ura-reference-color-02/run.log`.

Template `板材_バラ図枠_2024(D).DRWDOT` có sẵn block **表** cỡ **24 pt**, hệ số 1, và một block **ウラ** riêng. Kadai dùng lại block 表 và đặt cạnh view 展開, giữ định dạng gốc. Trên Drawing mới, add-in gỡ các block và note dùng riêng làm ký hiệu ウラ. Các ghi chú gia công dài hơn và file template gốc được giữ. [SolidWorks API đọc note của block](https://help.solidworks.com/2024/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.ISketchBlockDefinition~GetNotes.html)

### View số 13 trong template công ty

View 12 của template là hình chiếu cạnh phải trống. View 13 là hình chiếu đẳng trục trống ở vị trí khoảng **(155,335 mm, 92,239 mm)** với tỉ lệ **1:10**. Phép thử trên Drawing tạm cho thấy `InsertModelInPredefinedView` gán đúng Part, còn việc đổi `ReferencedConfiguration` sang `SM-FLAT-PATTERN` vẫn giữ hướng đẳng trục, `IsFlatPatternView() = false` và thiếu Bounding Box. `ChangeRefConfigurationOfFlatPatternView` trả về false trên view đó. Log đọc thử ở `kadai-assembly-6866/predefined-flat-13-probe-01/run.log`. [SolidWorks API gán Part vào predefined view](https://help.solidworks.com/2018/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IDrawingDoc~InsertModelInPredefinedView.html)

Khi template có view 13 trống, Kadai lấy tên, vị trí và tỉ lệ đó, gỡ view 12 trống rồi đặt Flat Pattern thật tại vị trí 13 trên Drawing mới. Sheet dùng view 13 cho các ô liên kết thuộc tính của Part. Kadai giữ vị trí và tỉ lệ mẫu khi thanh nằm vừa khổ giấy. Các template thiếu view 13 trống tiếp tục dùng luồng đặt Flat Pattern hiện có.

Build Debug/Release x64 đạt. So sánh bản Debug với bản build nguyên trạng từ project TAI TOOL cho 0 thay đổi trong 47.738 mục IL và metadata ngoài Kadai. MVID kiểm thử `2da20461-eb89-4836-a60c-7551ceaebcfa`, SHA256 `05D0189155BC75F616C7DFC2BCE23D3C1E12E47D29FC6AEC087563063C714412`.

Trên bản sao 6866, kiểm thử SolidWorks 2024 SP5.0 xác nhận cả cửa sổ SolidWorks và viewport vẫn được bật khi bảng modeless mở. Các nút đổi camera hoạt động, lựa chọn mặt độc lập với góc nhìn và góc nhìn ban đầu được khôi phục. Kiểm thử này điều khiển camera qua API. Sau Apply, mở lại 11 Part và 11 Drawing xác nhận đúng màu cấp mặt theo mẫu trong cả hai cấu hình, Flat Pattern phái sinh đã mở, Drawing chỉ có view 13 tại vị trí mẫu và tỉ lệ 1:10, sheet liên kết thuộc tính vào view 13, block 表 đúng 24 pt, ký hiệu ウラ đã gỡ, ghi chú gia công vẫn còn, Bounding Box hiển thị và DIM Kegaki tạo đủ W/L. Kiểm thử gỡ một thanh khi chèn lỗi và lưu rồi mở lại Assembly bản sao đều đạt.

File thay đổi gồm `Commands/Kadai/KadaiCadContracts.cs`, `Commands/Kadai/KadaiNativePrototype.cs`, `Commands/Kadai/KadaiPreviewBackend.cs` và `UI/KadaiForm.cs`. Mã nguồn được đồng bộ vào project đang dùng để người dùng tự rebuild. DLL đang cài giữ nguyên. SHA256 của các CAD nguồn kiểm tra giữ nguyên. Log ở `kadai-assembly-6866/slot13-template-cad-03/run.log`.
