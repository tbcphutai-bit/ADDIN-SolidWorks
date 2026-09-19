# Quy tắc DIM mặt cắt – 20260910-convert-R-preserve-old-dim-v7

## Convert cung và giữ DIM cũ (v7)

- Nhánh cung chính giữ danh sách DIM và construction cũ, không xóa trước khi thử bộ DIM mới. Nhánh L/U/Z thông thường giữ hành vi hiện có.
- Kiểm tra trạng thái sketch trước khi tạo bất cứ hình phụ nào. Nếu sketch đã mâu thuẫn từ trước, dừng và ghi rõ `baseline before creating anything`; không tự xóa ràng buộc của người dùng.
- Chỉ chọn cung chính của mặt đang đo và gọi `SketchUseEdge3(false, false)`. So sánh trước/sau để xác định đúng một cung mới, kiểm tra sketch đích, tâm, bán kính và relation nguồn. Không thêm CORADIAL vào cung vừa convert.
- Thử di chuyển hai đầu cung mới đến các mốc phủ bì bằng SetCoords rồi rebuild. Chỉ chấp nhận nếu tọa độ, tâm, bán kính, relation và trạng thái solver vẫn đạt. Không ghi đè DIM.
- Nếu bị khóa hoặc sai hình học: bỏ đúng cung thử mới, convert lại một cung tham chiếu nguyên vẹn; dựng cung phụ có hai mốc phủ bì và thêm một CORADIAL với cung tham chiếu này. Không xóa relation của model hoặc entity cũ.
- Dựng đường/cung phụ với AddToDB trong try/finally để hạn chế snap/tự thêm relation, khôi phục setting ngay sau tạo. Ghi log index đoạn/mối nối và trạng thái trước/sau từng relation. Không thêm COINCIDENT giữa cùng một COM point.
- DIM mới phải đúng loại, giá trị và không dangling sau rebuild. Chỉ khi cả bộ đạt mới xóa các DIM thuộc snapshot cũ và construction cũ đã đánh dấu. Có bảo vệ nếu API trả về DIM cũ thay vì tạo DIM mới.
- Nếu lỗi trước bước thay thế, xóa các entity/DIM vừa tạo, giữ DIM cũ. Nếu lỗi ngay trong bước xóa bộ cũ, báo số đã xóa và giữ bộ mới để kiểm tra; không hứa rollback nguyên tử khi API xóa đã chạy. Drawing không tự lưu.

Kiểm chứng: compile toàn add-in; chạy lại 795 kiểm thử hình học không đổi. Chưa kiểm chứng Convert/SetCoords/solver trên drawing thật; log `[DIM MAT CAT CONVERT]` phân biệt convert, thử mở rộng, fallback, constraint và replacement. Cần thử lần đầu/rerun, đổi mặt, thay model và rebuild trên bản sao drawing.

Tham chiếu API: [SketchUseEdge3](https://help.solidworks.com/2016/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.ISketchManager~SketchUseEdge3.html).

## Quản lý sketch Drawing (v6)

- Chấp nhận ActiveSketch là sketch nền của sheet/view trên sheet hiện tại; không dùng điều kiện khác null để kết luận đang sửa sketch không phù hợp.
- Nếu đang Edit Sheet Format hoặc sketch/block không thuộc các sketch nền đó thì dừng trước khi xóa DIM.
- ActivateView, lấy View.GetSketch và kiểm tra COM identity với ActiveSketch trước khi xóa DIM/dựng hình. Mỗi construction mới phải thuộc đúng sketch view.
- Không gọi InsertSketch2 để bật/tắt sketch của drawing view. Sau khi chạy hoặc lỗi, khôi phục view/sheet trước đó và preference nhập DIM.
- Ghi log `[DIM MAT CAT SKETCH]` cho context được chấp nhận, kết quả đối chiếu sketch và khôi phục view. Các phép tính R/Z/phủ bì của v5 không đổi.

Tham chiếu: [IView.GetSketch](https://help.solidworks.com/2026/English/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.IView~GetSketch.html), [IDrawingDoc.ActivateView](https://help.solidworks.com/2023/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IDrawingDoc~ActivateView.html).

## Cung chính có cạnh bẻ (v5)

- Một cung tạo hình chính trên mỗi mặt, nối với các cạnh bẻ qua bo nhỏ hoặc giao trực tiếp. Duyệt biên kín từ cạnh/cung click, ghép hai mặt theo liên kết, đồng tâm và chiều dày; không phân nhánh theo vùng trái/phải của màn hình.
- Giao đường đỡ cạnh bẻ với đường tròn chính cho mốc phủ bì. Chọn nghiệm tại đúng đầu nối; từ chối nghiệm mơ hồ. Các cạnh bẻ tiếp theo dùng giao line–line. Cung đo được mở rộng tới hai mốc này, chiều dài là R nhân góc quét, không phải dây cung hoặc chiều dài cung vật lý đã bị bo cắt ngắn.
- Dựng cung/line construction trong sketch 2D của view theo cách code tham chiếu 20260908. Ràng buộc CORADIAL với cung gốc, COLINEAR với line gốc, COINCIDENT tại các mối nối và mép đầu. Kiểm tra tọa độ sau ràng buộc; đo driven, không ghi đè số hiển thị.
- DIM R lấy cung trên mặt được chọn. Góc giữa các tiếp tuyến vật lý của hai đoạn đỡ tại chỗ bẻ: 90° trong dung sai 0.1° không ghi; khác 90° dùng DIM góc và xử lý góc bù giống L nghiêng. Với đoạn đỡ là cung, dựng line tiếp tuyến có ràng buộc vào vertex và cung gốc, không lấy góc của dây cung.
- Đánh dấu construction bằng layer dành riêng `TAI_SECTION_R_V5_<view hash>`. Khi chạy lại chỉ xóa construction đã đánh dấu trong sketch của view đó; không xóa toàn bộ sketch/view. Không dùng layer này cho hình học thủ công.
- v7 thay đổi vòng đời của nhóm DIM cong: giữ DIM cũ tới khi bộ mới đạt; chi tiết về lỗi trong bước thay thế ở mục v7 phía trên. Drawing không tự lưu; nên thử trên bản sao và Undo nếu cần.

API ràng buộc: [SOLIDWORKS SketchAddConstraints](https://help.solidworks.com/2024/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IModelDoc2~SketchAddConstraints.html).

## Hành vi đã tổng hợp

| Trường hợp | Quy tắc |
| --- | --- |
| Chọn cạnh thẳng | Cạnh được click xác định một mặt vật lý của tiết diện. Theo liên kết line/cung trên mặt đó đến hai mép đầu. |
| Chọn cung | Chọn mặt chứa cung, đồng thời yêu cầu đo riêng cung đã click, kể cả R nhỏ. |
| L | Hai đoạn, một lượt bẻ. |
| U | Ba đoạn, hai lượt bẻ cùng chiều. |
| Z | Ba đoạn, hai lượt bẻ đổi chiều. |
| Bậc/lip/nhiều đoạn | Xét chuỗi lượt bẻ cục bộ; không ép toàn bộ thành một L/U/Z đơn. |
| Góc bo nhỏ | Đo tới giao điểm hai đường thẳng đỡ cạnh của mặt đã chọn. |
| Mép cuối của Z | Lấy giao điểm với mặt tiếp giáp của chính contour đã chọn đến đầu tự do. Không lấy cực trị qua chiều dày. |
| Đoạn Z nối vào mép bẻ cuối, hai góc 90°, không đo R riêng | Giữ mốc phía bậc; ở phía mép cuối chọn mặt xa nhất theo hướng đo của đoạn. Xét hai mặt của đúng mép cuối, không xét các cạnh khác trong view. Không thay mốc của DIM mép cuối. |
| R cần ghi riêng | Đo các line tới điểm tiếp tuyến vật lý; bổ sung bán kính và chiều dài cung trên mặt đã chọn. |
| R lớn nối với góc bo nhỏ qua line | Mỗi đầu line xét độc lập: một đầu có thể là điểm tiếp tuyến, đầu kia là giao điểm lý thuyết. |
| Góc không 90° | DIM chiều dài theo hướng đoạn và thêm góc trong; kiểm tra góc bù trước khi chấp nhận. |
| Chạy lại | Sau khi nhận dạng hợp lệ, xóa DIM cũ trong view đang chọn, đọc lại hình học rồi tạo bộ mới. |

Đối với dạng Z, một mặt vật lý liên tục có thể nằm ngoài ở góc này và nằm trong ở góc đảo chiều kế tiếp. Code giữ liên kết đó, trừ ngoại lệ cục bộ đoạn nối vào mép cuối nêu trên. Mẫu bậc của người dùng phải ra 32.2 cho đoạn ngang nối mép bẻ lên, còn DIM mép đứng giữ 15.5; các bậc 12 vẫn giữ nguyên. Mốc mới là giao đường đỡ đoạn đo với mặt xa của mép cuối, không cộng chiều dày vào giá trị DIM. Quy tắc xét cả hai chiều duyệt và không phụ thuộc trục ngang/dọc của view. Z có mép cuối ở cả hai đầu đoạn thì xét từng đầu; U, các bậc nội bộ, R đo tiếp tuyến và góc xiên không dùng ngoại lệ này.

## Chính sách nhận dạng R

Khi chọn line: chỉ tách R để đo riêng nếu bán kính **trong** của cặp cung vượt chiều dày + 0.1 mm. Đây là chính sách suy ra từ logic tham chiếu cũ, không phải quy luật chế tạo phổ quát. Dùng bán kính trong giúp việc đổi mặt click không làm cùng một góc bị phân loại khác nhau. Chọn trực tiếp cung yêu cầu đo riêng cung đó.

Cặp cung phải đồng tâm, tiếp tuyến với line kề và có chênh lệch bán kính bằng chiều dày. Dung sai góc để phân loại 90° là 0.1°. Các dung sai chiều dài được chuyển theo tỉ lệ view; chiều dài DIM không phụ thuộc tỉ lệ hay vị trí đặt view.

Không có số 44/48/12/29.9/15.5/33.8/... trong thuật toán để quyết định kích thước. Chúng chỉ xuất hiện trong fixture kiểm thử. Bán kính hiển thị có thể đã làm tròn; chiều dài cung lấy từ hình học đầy đủ, không ép số để giống ảnh.

## Cấu trúc

- `Commands/SectionProfilePlanner.cs`: biên kín, hai mặt, hai mép đầu, phân loại từng góc, chọn mốc, lập kế hoạch DIM. Không gọi COM.
- `Commands/LenhDimCanhSongSong.Envelope.cs`: entry point `Run`, lựa chọn cạnh/cung, xóa DIM cũ, đọc lại hình học, phát DIM line/góc/R/chiều dài cung và kiểm tra sau rebuild.
- `Commands/LenhDimCanhSongSong.cs`: các helper SOLIDWORKS cũ được tái sử dụng; `RunLegacy` không nằm trong luồng nút bấm.

DIM tạo ra phải đúng loại, giá trị và không dangling trước/sau rebuild. Không override text hoặc giá trị DIM để che sai tham chiếu. Nếu không tạo được, báo riêng từng đoạn. Preference nhập giá trị khi tạo DIM được khôi phục khi thoát lệnh. Drawing không tự lưu.

## Kiểm tra

795 tổ hợp kiểm tra hình học, dùng seed thật như luồng nút bấm: giữ các ca L/U/Z/bậc-lip/R tiếp tuyến v4; thêm cung chính R44200/R18000, chiều dài cung mở rộng 500 và các cạnh 30/174.1/161.1; chọn cả hai mặt, đổi tỉ lệ/xoay/phản chiếu/chiều dày; góc 90°/110.9°/75° tại cung–line; từ chối biên bị mất bo nối. Đây là dữ liệu mô phỏng, không phải drawing SOLIDWORKS thực; ràng buộc COM và liên kết sau sửa model cần kiểm chứng trên drawing thật.

Chạy từ project:

```powershell
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /out:Tests\SectionProfile\SectionProfilePlanner.Tests.exe Commands\SectionProfilePlanner.cs Tests\SectionProfile\SectionProfilePlanner.Tests.cs
& '.\Tests\SectionProfile\SectionProfilePlanner.Tests.exe'
```

Kiểm tra trên bản sao drawing: click cạnh từng mặt rồi bấm DIM mặt cắt; so sánh bộ DIM và log `[DIM MAT CAT PLAN]`, đổi mặt rồi chạy lại; kiểm tra DIM cũ đã được thay thế. Với cung cần đo riêng, kiểm tra R/chiều dài cung/line tiếp tuyến và liên kết sau sửa model/rebuild.

## Giới hạn hiện tại

- Phải có biên tiết diện tôn đầy đủ trong dữ liệu cạnh/cung hiển thị, hai mặt cùng chiều dày và hai mép đầu thẳng vuông góc. View đứt, chồng hình hoặc mất cạnh sẽ báo lý do.
- Nhánh cong v5 chỉ nhận một cung chính trên mỗi mặt. Bo phụ phải tiếp tuyến và bán kính không vượt 2 lần chiều dày + 0.1 mm; cung chính phải lớn hơn ngưỡng đó. Đây là giới hạn nhận dạng bảo thủ, không phải quy luật chế tạo. Nhiều cung chính, cung bị chia thành nhiều cung đồng bán kính, hem, spline, đầu vát và cung >180° vẫn bị từ chối, không nối đoán.
- Chưa kiểm thử COM trên file drawing thật của người dùng; các trường hợp assembly/component transform và ẩn tiếp tuyến cần kiểm tra khi có dữ liệu thực.
- Vị trí chữ dùng offset theo từng đoạn; chưa có bộ tránh va chạm chữ tổng quát.

API dùng để tạo DIM và xử lý góc bù: [SOLIDWORKS AddDimension2](https://help.solidworks.com/2019/english/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.IModelDoc2~AddDimension2.html).
