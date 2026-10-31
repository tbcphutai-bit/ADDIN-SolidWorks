# R53 — quy tắc giao góc, Cutter và Sketch native

## 1. Phân tích một họ U có section graph

Web, Wall, Lip/Return và Bend là các vùng của cùng một tiết diện. Có thêm mép bẻ hoặc khác bề rộng không tự tạo ra một loại giao BUTT. Solver giữ nguyên kiến trúc Profile Analyzer → Joint Rule → Cut Plan riêng A/B → Boolean tạm → Validation → Ghost Preview.

Hiện phạm vi có bằng chứng native là: hai trục 90°, Web cùng mặt phẳng giao, chọn được duy nhất cặp skin vật lý 0/t và thỏa các kiểm tra Web/Wall/Bend. Frame ngắn được ưu tiên che. Khi recipe KEEP/CUT hoặc quan hệ chưa được xác minh, trả `UNVERIFIED_RULE`; không dựng vùng cắt tùy ý.

## 2. Chọn Web theo cặp Frame

Một profile có thể có nhiều strip “nằm giữa hai Wall song song”. Mẫu fr2 của 2216 có ứng viên 65 và 230 mm. Chọn Web bằng phương trục Frame đối diện, mặt phẳng hai trục và skin B-rep. Trong cặp fr2×fr3, chỉ strip 65 mm có phương bề rộng nằm đúng mặt phẳng giao và phù hợp trục fr3.

Không dùng bounding box, tên Feature hay strip dài nhất để quyết định Web. Sau khi chọn, các strip còn lại vẫn thuộc section graph cần phân tích; việc chọn đúng Web chưa chứng minh đã xử lý đủ mọi Return.

## 3. Bề rộng khác nhau

Giữ đường giao xiên giữa các vùng tương ứng, sau đó xử lý vai/bậc cho phần tiết diện khác nhau. Bằng chứng W23: Web 65/39 mm vẫn có đường giao 45°; vai 26,1 và 25,9 mm cùng các cạnh bậc cách nhau 0,1 mm. Đây là đối chiếu Sketch theo pose thật, chưa phải before/after B-rep.

Native case kiểm thử cuối dùng U đơn giản: Frame ngắn B 100 mm và Frame dài A 70 mm. Writer tạo main Sketch B 9 đỉnh, A 8 đỉnh; 4 Cut, 4 Sketch, 84 quan hệ, 2 dimension, không Plane mới, B-rep khớp plan và gap 0,1 mm. Không diễn giải thành chứng nhận mọi U nhiều mép bẻ.

Trong hệ tọa độ joint, s dọc Frame dài và q dọc Frame ngắn; sf/qf là tangent xa của cover/loser, nw/nl là tangent gần tương ứng. Nhánh khác bề rộng dùng:

```text
delta = sf - qf
qn = max(nl, qf - (sf - nw))
sn = qn + delta
s(q) = sn + (q-qn)
```

Giữ slope1 ở cặp90° rồi nối bằng vai. Không clamp qn về skin trong và kéo đổi slope để bù width. Offset loser theo pháp tuyến dùng g*sqrt(1+ds²) theo s; không đồng nhất một độ lệch0,1 theo trục với khoảng cách vuông góc0,1. Nhánh bằng bề rộng giữ clamp cũ đã kiểm thử, nên có thể hơi lệch45°; chưa sửa thành phân giác lý tưởng. Nhánh loser rộng hơn có code construction nhưng chưa native-qualified trong lượt R53 này.

Khi cover rộng hơn, endpoint phân giác nằm trong Web; cạnh định hướng cho relief phải lấy tại tangent gần native, không lấy endpoint đó làm điểm giả trên cạnh. Rectangle relief vẫn Coincident với điểm của Sketch nguồn, nên việc đổi điểm tìm cạnh định hướng không dịch vùng cắt.

## 4. Sketch gắn mặt và quan hệ

Support là mặt Web thật của Part. Dùng Coincident/Collinear/Parallel/Perpendicular/SameLength và các quan hệ cần thiết với datum Frame hoặc Sketch được tạo trước. Không Convert, FIX hoặc tạo RefPlane mới cho đường Apply U đã hỗ trợ.

Hai dimension chung có ý nghĩa:

- `AFJ_AssemblyFitGap`: khe lắp 0,10 mm.
- `AFJ_ToolClosureMargin`: đưa đoạn đóng contour ra ngoài miền vật liệu để có profile kín ổn định; không định vị đường miter.

Tọa độ seed không được coi là ràng buộc. Sau khi tạo Sketch phải đọc lại trạng thái fully constrained và số quan hệ, kiểm tra reference Face2, sau đó mới tạo Cut.

## 5. Ba đại lượng 0,10 mm

| Chính sách | Ý nghĩa |
| --- | --- |
| `AssemblyFitGap = 0,10 mm` | Khe hở lắp giữa hai Frame theo quy định người dùng. |
| `SheetCutOverrun = 0,10 mm` | Phần vượt t cho Cut đã xác minh chỉ xuyên một lớp tôn phẳng phù hợp. |
| `CutterEnvelopeOverrun = 0,10 mm` | Margin của Cutter bao profile; không phải khe lắp hoặc phép xác định chiều dày. |

Factory single-sheet yêu cầu chiều dày API và hai skin B-rep phù hợp. Một Cut qua profile đã gấp hoặc qua Bend không được mặc định sâu t+0,10 mm. Mẫu W23 có 8 Blind active sâu bằng t=2,00 mm; chưa có before/after để gọi cả 8 là Cut giao góc xuyên một lớp.

## 6. Cut native thực tạo trong hai ca cuối

**Mỗi ca tạo4 Cut; cả8 Cut mới của hai ca đều Blind và `NormalCut=false`.** `NORMAL_BEND_CAP` là tên vai trò của một bước relief trong Cut Plan; nó không có nghĩa checkbox Normal Cut đã bật.

| Thao tác | Chính sách sâu | Sâu native trong ca cơ sở |
| --- | --- | ---: |
| Long/loser main | Bao profile đã gấp | 42,2 mm |
| Long far Bend relief | Vùng relief Bend riêng | 2,8 mm |
| Short/cover main | Bao profile đã gấp | 60,2 mm |
| Short Bend cap relief | Vùng relief Bend riêng | 2,7 mm |

Các số trên là kết quả của dataset kiểm thử, không phải hằng chiều sâu áp dụng cho Frame khác. Cut Plan có tool riêng theo từng vùng và thứ tự. Writer đọc lại End Condition, depth, NormalCut, direction và so sánh Body thực với oracle Boolean sau các bước; không đổi một formed-profile operation thành t+0,10 mm chỉ vì nó là Blind.

## 7. Validation và giới hạn an toàn

- Preview sử dụng Body tạm; Apply chỉ dùng ticket/intent đã Preview còn hợp lệ.
- Kiểm tra source, configuration, Feature được bảo vệ và placement; tạo Sketch/Cut tuần tự với ownership journal.
- Hai ca positive cuối: B-rep difference A/B=0, overlap=0, gap=0,10 mm. Đây là kiểm thử API harness của đúng source cuối.
- 13 holder không hợp lệ trong fixture 2354 được chuẩn hóa **chỉ trên bản sao harness trước transaction**. Production không thực hiện thao tác này; baseline không hợp lệ bị từ chối.
- Khôi phục metadata Assembly khi rollback toàn cặp vẫn `UNVERIFIED`. Không coi positive Apply là kiểm thử rollback khi lỗi.
- U có Return/nhiều EdgeFlange như W23, hướng trục khác 90°, exposed-edge recipe chưa xác minh hoặc contact với component thứ ba không thuộc phạm vi native đã đạt.
- Chưa xác nhận Display3, Preview nhìn thấy/mượt, đóng lệnh, mở Part sau Apply hay OK trong DLL Add-in đang dùng bằng hai lượt harness này.
