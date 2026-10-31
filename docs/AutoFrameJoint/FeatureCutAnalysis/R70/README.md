# R70 — 4458 reference for inward/angled folds

Nghiên cứu tương ứng trong Draft PR45: [commit 68119cd](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/commit/68119cd412be55d9c439ef7c75131b882a7600a7). Cả hai cập nhật R70 chỉ thêm tài liệu, không sửa source solver.

Đã đọc Assembly `4458-02-TI-b2.SLDASM` và các dependency bằng SOLIDWORKS API trong một **process mới, không nạp Add-in chính**, trên các bản sao Read-Only. Đây là nghiên cứu quy tắc; **chưa sửa solver/class chính, chưa tạo Cut mới**.

## Bằng chứng đã có

- **24 file sao chép**, gồm 1 Assembly và 23 Part; đọc chi tiết **7 Frame Part**, trong đó có `b2-framegoc` và hai Part **`b1-fr2/fr3` được dùng lại trong Assembly b2**.
- FeatureManager union có **93 Cut**: 79 active, 14 suppressed. Tất cả trả `NormalCut=true`; 10 có `FlipSideToCut=true`. Đây là số lệnh tồn tại, không phải 93 Cut giao góc hoặc 93 before/after.
- Native base profile của `b1-fr2/fr3` có đoạn **144 mm × 5,154411621 mm**, nghiêng **2,05°**. Các skin B-rep tương ứng xác nhận cùng hướng nghiêng và cách nhau 2 mm. Không được ép toàn bộ nếp về 90°.
- Trục dọc các Frame trong Assembly vẫn song song hoặc vuông góc. **2,05° là độ nghiêng của đoạn biên dạng/mặt ghép**, không phải góc giao hai trục Frame.

| Chi tiết và Cut gốc | Chi tiết có skin ghép nghiêng | Offset giữa mặt phẳng Cut và hai skin |
|---|---|---|
| `b2-fr1`, Cut710, Sketch702 | `b1-fr2` | 0,1000000000 / 2,1000000000 mm |
| `b2-fr4`, Cut1024, Sketch1015 | `b1-fr3` | 0,1000000003 / 2,1000000003 mm |

Hai Sketch nêu trên là Sketch2D một đường thẳng, gắn **Face2 có sẵn** và fully constrained. Góc đường Sketch, normal mặt đầu do Cut sở hữu và normal skin ghép đều thống nhất 2,05°. Các khoảng cách trong bảng là **analytic planar support offsets**, chưa chứng minh khoảng hở hữu hạn/closest-distance cho toàn bộ Body.

Mẫu lịch sử dùng Through All Both ở Cut710 và Through All ở Cut1024. **Không chép các End Condition đó thành quy tắc mới.** Mục đích hình học cần học là mặt đầu đi theo mặt ghép nghiêng; quy tắc sản xuất mới vẫn là từng lớp tôn, Normal/Blind `t + 0,10 mm`.

## Phương pháp đề xuất

1. Đọc toàn bộ chuỗi biên dạng và cặp skin thật, giữ hướng nghiêng/gập vào của từng đoạn; xác định phía vật liệu từ B-rep, kể cả ReverseThickness.
2. Ghép từng lớp tôn với mặt đối ứng hữu hạn. Dựng ranh giới cắt theo giao tuyến với mặt đó; không dùng một độ sâu dọc trục chung cho mọi nếp, và không ghép chỉ theo nhãn Near/Far.
3. Giữ khe lắp 0,10 mm theo pháp tuyến mặt ghép. Với góc nghiêng, quy đổi dịch dọc trục theo góc thực; không đánh đồng với phần dư chiều sâu Cut.
4. Tạo từng cutter theo pháp tuyến vật liệu của chính lớp đó, Blind `t + 0,10 mm`, Normal Cut. Dùng hình học Bend R/R+t đo được để kiểm tra phần cong; chưa coi chiều sâu đơn lẻ là bằng chứng đã hết Bend.
5. Vẽ Sketch trên Face2 của chi tiết, dùng quan hệ Coincident/Parallel và guide có nguồn tham chiếu rõ ràng; hạn chế dim, không Convert Entities và không thêm Plane.
6. Kiểm tra vùng bỏ theo từng lớp và giữ nguyên phần thân/đầu đối diện. Không bỏ điều kiện bảo vệ chỉ để vượt lỗi `SelectedEnd`.

Xem [đặc tả phương pháp](PER_STRIP_ANGLED_METHOD.md), [Rule Design JSON](PROPOSED_RULE.json), [đối chiếu hình học](REFERENCE_GEOMETRY.json), [hình học profile/Sketch](PROFILE_CAP_GEOMETRY.json), [kiểm tra toán học](METHOD_MATH_CHECKS.json), [93 Cut CSV](FEATURE_CUT_INVENTORY.csv) và [JSON](FEATURE_CUT_INVENTORY.json).

## Phạm vi xác minh

`API_VERIFIED`: Feature/Sketch/Cut scalar, reference Face2, constraint status, profile points, sheet thickness/R, occurrence transforms. `GEOMETRY_VERIFIED`: góc từ dữ liệu đó, normal B-rep, khoảng cách các mặt phẳng và phép kiểm tra affine. `INFERRED`: quy tắc mới tách Cut lịch sử thành các cutter Normal/Blind theo từng lớp.

**Chưa chạy** Cut before/after, Boolean mới, native Cut mới, Preview mới hoặc matrix bốn góc cho quy tắc này. Các role end-trim/notch/hole trong inventory có trạng thái suy luận riêng; không dùng tên Feature để xác nhận vai trò.

Source/copy SHA-256 của cả 24 file không đổi. Không Save/Rebuild CAD, không Pack and Go. Host đã trở về trống rồi được yêu cầu thoát bằng API; sau đó process không còn chạy. Không nạp/build/deploy Add-in chính, không đổi source, DLL/PDB hay project. CAD gốc, ảnh B-rep và log đầy đủ chỉ lưu cục bộ; không upload GitHub. Corpus 15 Part/43 Cut cũ được giữ nguyên, không cộng gộp 93 Cut của nghiên cứu mới vào đó.
