# CHECK CHẤN — MỘT ĐỢT SỬA ĐỒNG BỘ (hướng V4.6)

**Baseline:** Draft PR #38 (kế thừa PR #37). **Đích local:** `C:\Tao moi\ADDIN\ADDIN.csproj`. **Không tự Build/Rebuild**, không chạy native, không thay DLL, không merge main; chỉ sửa trực tiếp class hiện hữu, không tạo class UI .UxV46.cs.

## A. Điều user thực sự muốn

Một phần mềm Check Chấn mà người dùng **chọn Part → chọn nếp B1..Bn → thấy mặt cắt thật và một kết luận chính** (đạt giới hạn đã xác minh / có cạnh vi phạm được chứng minh / chưa thể kết luận). Nếu có va chạm: tô đỏ **đúng cạnh Part gây đụng** và phân biệt chày/đầu kẹp. Tự chọn dao và trình tự chấn sao cho **ít thay dao nhất trong các chuỗi đã được chứng minh khả thi**, không nhóm dao bằng heuristic bỏ qua trình tự phôi.

Màn hình chính chỉ 3 vùng: trái danh sách Part/nếp, giữa mặt cắt lớn, phải 1 kết luận và tối đa 4 dòng Min/MAX/va chạm/trạng thái nguồn dao. 18 mã và các giải thích kỹ thuật dài chỉ mở theo yêu cầu trong tab `Bộ dao`/`Chi tiết kỹ thuật`/`Giới hạn Excel`/`Thứ tự chấn`. Không ghi SHA/FeatureId/FrameId/log trên màn hình sản xuất.

## B. Sự thật nguồn đã đối chiếu

1. Excel `深曲げ限界(4).xlsx`: tham chiếu Sash 6000 mm; Gooseneck HDS 4200 mm; Special Gooseneck HDS 4200 mm; Straight HDS 4200 mm; Gooseneck HD 6000 mm. Đây là **chiều dài tham chiếu họ khuôn**, không phải chiều cao cánh MAX. Các mặt cắt C/U/Z/L, vùng W/L và cảnh báo `深曲げ限界!N47` rằng không được va chạm thiết bị/khuôn là điều kiện áp dụng; pixel ảnh không phải tọa độ CAD. Không tự gán 5 họ khuôn vào 18 ToolId.
2. `Commands/BendPlacement/BendLimitWorkbookReader.cs` đã có reader cho workbook, ô/drawing/sha và manifest; các rule draft hiện có `UNVERIFIED_TOOL_FAMILY_MAPPING` và case ảnh tham khảo chưa được duyệt. `UI/BendExcelReferenceV4.cs` chỉ là UI transcription, KHÔNG PHẢI engine Check Max.
3. Các cặp công ty là **Punch + Holder đầu kẹp**, không phải V-die. `ClampVerified=0` ở các kết quả trước; CAD INSERT không xác nhận tọa độ gá thật.
4. Log Debug ngày 2026-10-21: `ToolCount=18`, `DataCount=0`, `MatchedPairs=0`, `ClampVerified=0`, `NumericQueries=0`; phiên SOLIDWORKS không thấy file settings trong LocalAppData, `Overrides` null, `UseMasterPairsAsLibrary=false`. Đây là lý do phiên này chưa có collision numeric. Bài test managed trong môi trường khác từng đọc 18/18, nên phải đối chiếu **effective runtime config**, không đổ lỗi ngay cho CAD hay chế tạo lại file.
5. `ToolProfileLibrarySession.Poll` đang gọi `Refresh` chu kỳ ~2 giây; cần kiểm soát đọc/log lặp nếu input/fingerprint không đổi, mà vẫn bảo đảm thay nguồn sẽ invalidate kết quả cũ.
6. Check Min FeatureId 524,530 = `MeetsMinimum`; KHÔNG được suy ra MAX đạt. Native COM/heap `0xC0000374` trước đây còn chưa xác minh root cause; không sửa COM lifetime/ReleaseComObject/GetFeature bằng suy đoán.

## C. Hợp đồng phân loại Excel-FIRST (bắt buộc)

Tại **từng nếp và tool candidate**, phân loại:
- `VERIFIED_MATCH`: chứng minh bằng rule numeric/shape đã phê duyệt, nguồn workbook+sheet/cell/sha, vật liệu, thickness, góc, W/L, tư thế, chiều dài, ánh xạ tool. Kiểm tra giới hạn Excel trước; chỉ kết luận phần rule thực sự chứng minh, không tự coi là clearance dọc toàn hành trình.
- `VERIFIED_NO_MATCH`: đã thử đủ điều kiện và xác nhận không có trường hợp Excel tương ứng → chỉ lúc này chạy CAD geometric/process fallback khi có đủ nguồn hình học và tọa độ gá.
- `UNRESOLVED`: ảnh nhìn giống, mapping/tool/đơn vị/tư thế chưa duyệt hoặc dữ liệu stale → `UNKNOWN`; KHÔNG tùy tiện dùng CAD fallback để né Excel.

Thiết lập kết quả riêng: `CheckMin`, `ExcelApplicability`, `ExcelLimit`, `StaticCollision`, `ProcessCollision`, `ManufacturingReadiness` và Evidence/SourceVersion. Không gộp Check Min PASS với MAX/COLLISION PASS. `0/18 geometry` phải hiển thị `Chưa kiểm tra va chạm`. `CLEAR` chỉ khi số phép numeric/coverage/position/states đủ; `COLLISION` chỉ khi engine thực có chứng cứ và phạm vi, không đoán từ 2 đường gần nhau.

## D. Danh sách sửa cùng lúc theo dependency

**D1. Source/caching** — `Commands/BendToolProfiles/ToolProfileLibrary.cs`, `ToolProfileLibrarySession.cs`, `Commands/BendPlacement/BendPlacementSources.cs`, `CompanyCadPairs.cs`, `UI/BendAssemblyBatchLiveBackend.cs`. Xác minh settings path + overrides của đúng process; phân biệt ToolCount với DataCount/PunchLoaded/HolderLoaded/MatchedPairs/ClampVerified. Không tạo hay sửa config private khi thiếu evidence. Không lặp hàng chục lần đọc cùng input; nhưng khi file/config đổi phải invalidate snapshot ngay, không re-publish CLEAR stale. Có unit tests thuần managed không trigger build và synthetic files để kiểm tra invalidation, cancellation, fingerprint.

**D2. Excel matcher** — `BendLimitWorkbookReader.cs`, `BendPlacementSources.cs`, models nguồn và UI. Giữ workbook SHA, sheet/locator, rule condition, vùng W/L; chỉ tạo trạng thái verified khi có mapping/giới hạn đã được duyệt. Không đo hình pixel hay tự tạo Lmax. Nếu chưa có approved manifest, giữ UNKNOWN và nêu đúng dữ liệu thiếu. Excel test matrix 33 case là input thử, **không phải approval**.

**D3. Geometry & collision witness** — `BendEnvelope/`, `BendPlacement/`, `BendCollision/`, `UI/BendAssemblyBatchForm.UxV43.cs`, `UI/BendSection2DCanvas.cs`. Mặt cắt Part từ CAD real capture, theo đúng Part/config/FeatureId/snapshot. **Final folded section ≠ intermediate process state**. Ghép Punch + Holder vào cùng canvas chỉ với transforms đã được chứng minh; nếu thiếu thì hiện Part và `Chưa có vị trí gá`. Tô đỏ đúng đoạn Part/tool chỉ khi có contact witness; không nearest segment giả. Bao gồm mặt cắt chữ C gập vào, nhiều hướng B1..Bn, cả cánh đối diện nếp.

**D4. Sequence / tool change** — `Commands/BendPlanning/PartSequencePlanner.cs`, `SequencePlanningModels.cs`, `UI/BendSequencePlanningControl.cs`, `UI/BendVerifiedToolChangePlanner.cs`. Search feasible transitions first, minimize tool changes/setup changes second; preserve constraints due previously bent flanges. Nếu state graph còn thiếu thì chưa đưa recommendation như sản xuất; giữ `Chưa xác minh thứ tự chấn`.

**D5. Result-first UI** — `UI/BendAssemblyBatchForm.UxV4.cs`, `UI/BendAssemblyBatchForm.UxV43.cs`, `UI/BendSection2DCanvas.cs`, model/view converter. Giữ V4.5 làm baseline thay vì thiết kế màn mới. Main: chọn Part/nếp | một hình 2D lớn | trạng thái 1 câu; bảng 18 dao ở tab khác. Hiện các trạng thái thật `Đã kiểm tra không đụng trong phạm vi...`, `Vi phạm giới hạn Excel ...`, `Va chạm chày/đầu kẹp ...`, `Chưa thể kết luận ...`; ngắn, không đánh lừa. DPI/resize, selection, Refresh, Cancel, Close không làm stale UI hoặc lộ chế độ mockup.

**D6. Controller/state integrity** — `UI/BendAssemblyBatchController.cs`, `Commands/BendCheckSession.*`, snapshot/view converter. Chỉ chỉnh nếu có bằng chứng; bảo vệ existing STA COM ownership, branch Main code, progress cancellation, invalidate. Không gọi native COM từ worker UI.

## E. Test cases/acceptance chung (trước khi gửi báo cáo)

1. Missing config → 18 IDs, 0 geometry, zero numeric → `CHƯA KIỂM TRA`, không có dao recommended; không spam polling/log.
2. Valid DXF/18 pairs (synthetic/offline) → counts đúng; ClampVerified=0 vẫn UNKNOWN.
3. Excel case verified match theo nguồn/rule → kết quả đúng và có cell ref; 4200/6000 không thành Lmax.
4. Case tương tự nhưng thiếu approved mapping → UNRESOLVED/UNKNOWN; **không** CAD fallback.
5. Verified no Excel case → CAD fallback, chỉ khi có geometry/pose/states.
6. CAD missing tool pose → section Part-only, không overlay/tô đỏ giả.
7. Verified contact witness → highlight đúng cạnh Part + chày hoặc Holder; detail xác nhận.
8. Candidate CLEAR không tự suy ra entire Part/manufacturing PASS.
9. Part C/L/U/Z + 4+ bend directions → B1..Bn đúng FeatureId/config và cánh đối diện, không fixed 4.
10. Optimization feasible order trước, minimize tool changes sau; thiếu state graph → UNKNOWN.
11. Run/Refresh/Cancel/Close, selection stale, resize/DPI, không thay Check Min parity 524/530.
12. Git diff giới hạn Check Chấn, no other commands, no private CAD/Excel/config upload, no build artifact.

## F. Một lần triển khai/kiểm tra, không Rebuild tự động

1. **Read-only preflight:** đối chiếu local worktree/branch/PR #38/dependency, Excel/18 CAD gốc có quyền truy cập. Liệt kê một bảng `vấn đề - root cause chứng minh - file - fix - test - blocker`. Không chạy thêm 33-Part test.
2. **One integration patch:** sửa trực tiếp các class có sẵn, dùng đúng model hiện tại; chỉ sửa khi có evidence. Không tạo UI version class mới, không tự gán mapping/tool limit/holder geometry giả. Giữ nguyên Check Min, COM ownership, non-CheckChan commands.
3. **Static/offline only:** `git diff --check`, kiểm tra cấu trúc namespace/compile links/callers, kiểm tra unit tests không trigger Build. **Nghiêm cấm:** Build/Rebuild/msbuild/dotnet build/csc/native SOLIDWORKS/auto DLL-PDB.
4. **GitHub:** commit changes liên quan trên branch Check Chấn, cập nhật Draft PR #38 khi an toàn, không force-push, không merge main, không upload private Excel/DXF/user settings.
5. **Một báo cáo cuối:** danh sách đã sửa thực, source SHA, kết quả test tĩnh, UI preview được đánh dấu minh họa, kết quả nào được chứng minh, phần nào BLOCKED do data, URL PR, xác nhận Build/Native NOT RUN; rồi DỪNG. Người dùng tự Rebuild Debug/x64 một lần, test XL-02 + 1 assembly nhỏ rồi gửi log/ảnh.

## PROMPT DUY NHẤT CHO CODEX

> Triển khai **một đợt sửa Check Chấn đồng bộ**, theo toàn bộ tài liệu `Docs/BendCheck/CHECK_CHAN_UNIFIED_DELIVERY_V46.md` của Draft PR #38. Đọc source ở đúng branch, local changes và dữ liệu Excel/DXF đã được giao trước đó. Tập trung sửa nguyên nhân dữ liệu, Excel-first matcher có chứng cứ, geometry/contact witness, optimizer trên feasible sequences, và UI chỉ hiện kết quả, không sửa kiểu chắp vá nữa. Ghi trực tiếp class hiện hữu, không tạo class UI phiên bản, bảo vệ Check Min và COM. Không tự quyết định giá trị, không tự nhận đủ mapping/gá khi evidence thiếu, không biến UNKNOWN→CLEAR; phần nào thiếu thì nêu blocker trong báo cáo, không quảng cáo đã hoàn thiện. **KHÔNG tự BUILD/REBUILD/native tests.** Chỉ static/offline non-build tests. Nếu không có conflict, push code đã sửa lên Draft PR #38; không merge. Gửi **một báo cáo** bao gồm root cause, file/diff/commit, acceptance từng nhóm và blockers rồi dừng.

**Tài liệu này là yêu cầu triển khai tổng hợp, KHÔNG phải tuyên bố đã code xong hoặc đã giải quyết hoàn toàn các điều kiện sản xuất.**
