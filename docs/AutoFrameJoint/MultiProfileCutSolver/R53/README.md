# R53 — tích hợp Sketch gắn mặt và giao U khác bề rộng

Ngày 2026-10-23. Source chính đã được cập nhật trong **`Commands/AutoFrameJoint/AutoFrameJointGhostPreview.cs`**. SHA-256 của file cuối:

`cc7fc64ab6b5a6d51cbf5e5ef6b59bd1d15f509d29af262d2e173a52cb92b68c`

Chỉ file source này được ghi. Các entry point, A/B selection, R50 UI protection, class UI/CommandEntry/SessionManager, project, branch và HEAD hiện tại được giữ nguyên. Không Build/Deploy Add-in chính, thay DLL/PDB hay Merge.

## Đã thay đổi trong class hiện tại

| Thành phần | Thay đổi |
| --- | --- |
| `AfjParametricRules.SelectJointWebs` | Chọn cặp Web bằng trục Component, mặt phẳng chung và hai skin B-rep 0/t. Không chọn strip dài nhất khi có nhiều ứng viên. |
| Quy tắc U và Cut Plan | Giữ họ U khi có mép bẻ; không tự đổi thành BUTT vì khác bề rộng hoặc có thêm nếp gấp. Trường hợp thiếu bằng chứng trả `UNVERIFIED_RULE`. |
| `AfjNativeCutCommit.TryApply` | Đường Apply U đã nối sang writer gắn Face2 trong chính file source đang dùng. Không rơi về writer tạo Plane tự do. |
| `AfjUParametricSketchWriter` | Dựng Sketch trên mặt Web có sẵn, dùng ràng buộc trực tiếp với datum Frame và Sketch liên quan, không Convert/FIX. Hỗ trợ recipe U đơn giản 8/9 đỉnh đã kiểm thử. |
| `AfjNativeUTransaction` | Dùng đúng intent đã Preview, tạo các Cut tuần tự, kiểm tra Body/gap/placement và quản lý hoàn tác bằng journal toàn cặp. |

Luồng hiện tại vẫn là: A/B selection → solver → Cut Plan → Boolean tạm → Preview → OK → `TryApply` → Sketch/Cut native. Không tạo command hoặc hệ thống Preview thứ hai.

## Kết quả native của cùng code được giao

Hai lượt dưới gọi **entry point `AfjNativeCutCommit.TryApply` thật trong harness độc lập**, trên bản sao CAD. Hai ca dùng cùng code nguồn; sau kiểm thử chỉ bỏ một dòng trắng cuối file để qua git diff --check. Xem [EOF_EQUIVALENCE.json](EOF_EQUIVALENCE.json) cho hash bản chạy API và bản giao. Chúng không chạy bên trong DLL Add-in đã cài và không xác nhận Display3/Preview nhìn thấy.

| Ca | Cut / Sketch | Quan hệ / dimension | Plane mới | B-rep so với Cut Plan A/B | Gap / overlap |
| --- | --- | --- | ---: | --- | --- |
| `native_apply_14`: cặp U cơ sở | 4 / 4 | 80 / 2 | 0 | 0 / 0 m³ | 0,10 mm / 0 m³ |
| `native_unequal_03`: Frame ngắn B rộng 100 mm, Frame dài A rộng 70 mm | 4 / 4; main Sketch B/A có 9/8 đỉnh | 84 / 2 | 0 | 0 / 0 m³ | 0,10 mm / 0 m³ |

Tất cả bốn Sketch của mỗi ca đủ ràng buộc, không dangling/overdefining; support là Face2 gốc. Các Part giữ Feature được bảo vệ và thứ tự gốc. Phiên harness trở về trống, hash file nguồn/bản sao không đổi; không gọi Save.

Hai dimension là **khe lắp chung** và **margin đóng contour ngoài miền vật liệu**. Chúng không phải kích thước tọa độ để định vị các đỉnh đường cắt. Những đỉnh biên cắt được liên kết bằng quan hệ hình học với Frame/Sketch liên quan.

Kết quả B-rep bằng 0 chứng minh Cut native khớp Cut Plan đã tính trong các ca này. Nó không tự chứng minh quy tắc KEEP/CUT phù hợp mọi thiết kế hoặc mọi biên dạng.

## Điều kiện fixture và hoàn tác còn giới hạn

Cả hai ca positive dùng fixture sao chép đã xử lý riêng **13 Assembly in-context holder có persistence token không hợp lệ**. Việc chuẩn bị fixture chỉ chạy trong harness trước baseline transaction; Part history/B-rep và Assembly history ngoài holder được kiểm tra giữ nguyên. **Không có bước chuẩn hóa này trong production.** Baseline không hợp lệ sẽ bị từ chối.

Hoàn tác toàn cặp đã có journal, nhưng **khôi phục đầy đủ metadata Assembly vẫn `UNVERIFIED`**. Hai ca positive không phải kiểm thử rollback khi lỗi. Không diễn giải trường `RollbackVerified=false` của ca positive thành một phép thử rollback đã chạy thành công hoặc thất bại.

## W23 và U có nhiều mép bẻ

Mẫu 2216 W23 cung cấp bằng chứng ReadOnly: 3 Part, **23 Cut definition = 20 active + 3 suppressed**; góc Web 65/39 mm giữ đường xiên 45° và có vai bù 26,1/25,9 mm. Đây là cùng họ U với section graph khác nhau.

**Writer native hiện được kiểm thử chỉ cho U đơn giản.** Các Return bổ sung, nhiều nếp gấp, EdgeFlange tạo thêm và trường hợp W23 có trim liên quan component thứ ba chưa được xác nhận native Apply. Mẫu W23 có **0 before/after Cut, 0 native Apply, 0 kiểm thử trong Add-in** trong lượt nghiên cứu này. Không cộng 23 definition này vào số 43/42 của báo cáo lịch sử.

## Trạng thái DLL

Source mới đã nằm trong file chính; DLL/PDB giữ nguyên. DLL baseline đã kiểm kê có MVID **`420dfcfc-a6ee-4861-ae82-f54d916e32e4`**. Vì chưa Build/Deploy Add-in chính, DLL baseline này **chưa chứa source R53**.

Xem [MANUAL_REBUILD.md](MANUAL_REBUILD.md) để đưa source vào DLL bằng quy trình Build hiện có của người dùng; [JOINT_RULES.md](JOINT_RULES.md) mô tả chính sách cắt và những kết luận chưa được phép ghi PASS.

## Báo cáo và dữ liệu kèm theo

- [TEST_MATRIX.csv](TEST_MATRIX.csv): phạm vi và kết quả từng ca.
- [NATIVE_API_RESULTS.json](NATIVE_API_RESULTS.json): kết quả API đã loại thông tin riêng tư.
- [NATIVE_CUTS.csv](NATIVE_CUTS.csv), [NATIVE_CUTS.json](NATIVE_CUTS.json): thông số Cut native mới.
- [NATIVE_SKETCHES.csv](NATIVE_SKETCHES.csv): support, ràng buộc, dimension và Plane.
- [ROLLBACK_LIMITS.md](ROLLBACK_LIMITS.md), [DIAGNOSTIC_HISTORY.csv](DIAGNOSTIC_HISTORY.csv): các lượt lỗi và giới hạn hoàn tác.
- [COMPILATION.json](COMPILATION.json): kiểm tra biên dịch độc lập; không phải Build Add-in chính.
- [SOURCE_MANIFEST.json](SOURCE_MANIFEST.json), [MAIN_SOURCE_UPDATE.json](MAIN_SOURCE_UPDATE.json): source cuối và việc cập nhật file chính.
- [Nghiên cứu W23](W23/README.md): 23 Cut definition, pose Assembly và các giới hạn before/after.
