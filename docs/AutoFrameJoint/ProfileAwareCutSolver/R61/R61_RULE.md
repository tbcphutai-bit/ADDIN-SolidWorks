# R61 — xén phần dư ngoài đầu giao góc

## Quy tắc hình học

Điều kiện **có giao với Frame kia** quyết định vùng cần tạo đường giao hoặc vát góc. Điều kiện đó không có nghĩa toàn bộ nếp không giao phải được giữ nguyên tới đầu phôi.

Mỗi nếp được chia theo hai chiều độc lập:

- Theo bề ngang nếp: vùng có tiếp xúc và vùng không có tiếp xúc trực tiếp.
- Theo trục Frame: phía thuộc thân cần giữ và phía vượt quá biên kết thúc góc cần loại bỏ.

Vùng có giao dùng quy tắc giao hiện có. Với vùng không giao, phần nằm trong biên kết thúc được giữ; phần kéo dài ra ngoài biên phải được xén. Quy tắc này áp dụng cả nếp hoàn toàn không giao và phần không giao còn lại của một nếp có giao một phần.

## Nguồn xác định biên kết thúc

Biên xén lấy từ mặt phẳng tôn 0/t thực của Frame đối diện, đã được một lát giao trực tiếp xác minh. Planner dùng giới hạn dọc trục của lát đó làm datum cho các nếp kề liên tục trên cùng nhánh tiết diện.

Trước khi lan truyền datum, planner kiểm tra:

1. Chuỗi cạnh tiết diện lấy từ dữ liệu API, các cạnh thực sự nối nhau.
2. Datum gần nhất thuộc cùng phía của Web; không truyền xuyên qua Web sang nhánh đối diện.
3. Mặt chuẩn vuông góc với trục Frame thực. Tính lại vị trí giới hạn từ cặp skin 0/t và khe hở, rồi đối chiếu với giới hạn của lát đã xác minh.
4. Nếu các datum gần tương đương cho giới hạn khác nhau, dừng với `UNVERIFIED_RULE`; không đoán một mặt cắt ở giữa.

Một cạnh làm datum có thể gồm nhiều khoảng tiếp xúc ở các cao độ khác nhau. Khi truyền giới hạn sang nếp kế tiếp, chọn khoảng tiếp xúc gần **đầu cạnh thực đang nối về phía nếp đó**; không chỉ chọn theo chỉ số cạnh và không chọn giới hạn cắt nhỏ nhất/lớn nhất. Với vùng không giao nằm trên chính cạnh làm datum, dùng khoảng tiếp xúc kề vùng đó. Nếu khoảng cách hình học bằng nhau nhưng hai mặt kết thúc khác nhau, vẫn dừng để tránh cắt tùy ý.

Không lấy bounding box hoặc tên Feature để quyết định đường cắt. Các tọa độ trong báo cáo là số đo của từng fixture, không phải hằng số mẫu đưa vào thuật toán.

## Cách tạo Cut

Mỗi vùng dư được tạo thành một operation riêng trên đúng lớp tôn: **Normal Cut, Blind = chiều dày API + 0,10 mm**. `SheetCutOverrun` vẫn độc lập với `AssemblyFitGap`.

Vùng không giao được xén tới mặt kết thúc đã xác minh, không tự sinh thêm đường vát chéo. Writer dùng Face2 có sẵn của chi tiết, giữ các ràng buộc hình học và ngữ cảnh sửa Component trong Assembly. Không cần tạo RefPlane mới hoặc thay bằng một Cut xuyên toàn bộ tiết diện. Luồng đóng PMP/chờ native idle của R60 được giữ nguyên.

## Chứng minh không còn phần dư

Việc Cut thật khớp Preview vẫn cần thiết, nhưng chưa đủ: hai bên có thể cùng giữ nhầm một phần dư. R61 bổ sung các chứng cứ độc lập từ **vật liệu ban đầu**:

- Mặt tôn phẳng: giao của các skin hữu hạn 0/t, Body gốc và cửa sổ ngoài biên kết thúc. Phải có thể tích ban đầu dương và thể tích còn sót sau Cut bằng 0 trong tolerance.
- Phía thân: mask vật liệu gốc nằm trong biên; kiểm tra không mất vật liệu được yêu cầu giữ.
- Vùng cong: xác minh các cylinder R/R+t và các cạnh tiếp tuyến thật; dùng sector vật liệu cong ban đầu ngoài biên làm witness riêng. Kiểm tra này phát hiện cả phần R còn thừa mà mask mặt phẳng không phủ tới.

Các witness ngoài biên tiếp tục được kiểm tra sau các góc kế tiếp. Witness phía thân được kiểm tra tại góc hiện hành; không yêu cầu giữ nguyên đầu đối diện khi một góc khác hợp lệ cần xén nó.

## Bằng chứng đã có

Ca 4332 đảo A/B đúng trường hợp người dùng gửi đã chạy trên bản sao bằng API: **12 Cut, 16 Sketch**, gồm bốn vùng dư phẳng và hai vùng cong R/R+t có vật liệu dương ban đầu. Sau native Cut, cả sáu witness đều không còn vật liệu dư; các witness phía thân giữ nguyên. Cut thật khớp Preview, không RefPlane mới, hash CAD nguồn/bản sao không đổi và phiên trở lại trống.

2354 hiện tại do người dùng cung cấp đã qua đủ bốn góc trên cùng bản sao Assembly: **35 Cut (5+7+11+12), 51 Sketch**, với năm witness dư phẳng và một witness Bend R có vật liệu dương ban đầu; sau Cut và sau cả bốn góc, tất cả không còn vật liệu dư. Bộ 2354 lịch sử phục hồi cũng đã qua bốn góc riêng: **38 Cut (8+7+11+12), 54 Sketch**, cùng năm witness phẳng/một witness Bend. Hai bộ dùng cùng source cuối nhưng trạng thái đầu vào khác nhau, được đóng băng và báo cáo riêng.

File `fr04` người dùng vừa chỉ định có hash khác fixture lịch sử từ trước lượt R61; không gán PASS của dữ liệu cũ cho dữ liệu mới và không tự quy nguyên nhân thay đổi cho người dùng hay API.

- `API_VERIFIED`: Feature, chiều dày, Normal Cut/Blind/depth, Face2, quan hệ Sketch được đọc thật bằng API.
- `GEOMETRY_VERIFIED`: vật liệu ngoài biên bị loại bỏ, vật liệu phía thân giữ lại, Body/gap/interference và so sánh Cut thật với Preview được đo bằng B-rep.
- `UNVERIFIED`: trường hợp chưa chạy, nguồn thay đổi, quy tắc không có datum duy nhất hoặc hành vi DLL sau rebuild chưa được kiểm tra.

Source cuối đã qua lại 4332 bằng SelectionBox native/Close/AfterClose/OnIdle thật: **12 Cut, 16 Sketch**, trong đó bốn Cut xén dư. Phần dư phẳng và Bend R đều hết, phía thân giữ nguyên. DLL hiện tại và TaskPane/event subscribers được nạp làm nền; code AFJ được thử vẫn là candidate, chưa phải command trong DLL R61 sau rebuild.

Các Cut tạo trong những phép thử này là sự kiện riêng theo fixture/source. Không cộng chúng vào kiểm kê **43 Cut mẫu** hoặc **42 Cut before/after** lịch sử. Thành công Apply không tự xác minh hoàn tác khi lỗi giữa chừng.
