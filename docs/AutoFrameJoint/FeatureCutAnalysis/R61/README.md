# R61 — cắt bỏ phần dư ngoài đầu giao góc

Ngày kiểm thử: 2026-10-28. Bản này bổ sung thao tác xén các nếp không giao nhưng kéo dài ra ngoài biên kết thúc góc, đồng thời giữ vật liệu thuộc thân Frame. Bản source đã kiểm thử được chép trực tiếp vào class chính dưới hash guard và có backup. Chỉ thay class `AutoFrameJointGhostPreview.cs`; luồng A/B, writer trong Component edit và hàng rào native idle của R60 được giữ nguyên.

Source cuối được kiểm thử: Ghost SHA256 `8C4218B6205309407565109C96B89E328449FABFE39D2567CA8B26D3228BE195`, SelectionPage SHA256 `27BE94F7A1D12FB25EB1C90733068259E9326BF6BEAC1340F41C1D0A5F13914E`.

## Kết quả đã xác minh

| Fixture | Phạm vi | Cut mới | Sketch cuối | Vật liệu dư phẳng / Bend R |
|---|---|---:|---:|---|
| 2354 hiện tại do người dùng cung cấp | Bốn góc tuần tự trên cùng bản sao Assembly | 35: 5 + 7 + 11 + 12 | 51 | 5 / 1 witness dương ban đầu, không còn dư sau cắt |
| 4332 đảo A/B | API trực tiếp, bản candidate trước sửa lỗi reference/lookup ở 2354 | 12, gồm 4 Cut xén dư | 16 | 4 / 2 witness dương ban đầu, không còn dư sau cắt |
| 2354 lịch sử phục hồi | Bốn góc tuần tự trên cùng bản sao Assembly | 38: 8 + 7 + 11 + 12 | 54 | 5 / 1 witness dương ban đầu, không còn dư sau cắt |
| 4332 native PMP với source cuối | Chọn A/B native, đóng trang, idle thật rồi tạo Cut | 12, gồm 4 Cut xén dư | 16 | 4 / 2 witness dương ban đầu, không còn dư sau cắt |

Chi tiết fixture hiện tại: [2354 current08](2354_current08/README.md); fixture lịch sử: [2354 historical01](2354_historical01/README.md); native PMP của source cuối: [4332 PMP01](4332_pmp01/README.md). Ca 4332 trực tiếp trước đó: [4332 native02](4332_native02_v3/README.md). Các phép thử này được giữ riêng theo source/fixture, không dùng kết quả của source cũ để thay cho source cuối.

35 Cut của 2354 gồm 5 Cut xén phần dư ở vùng không giao. Cả 51 Sketch cuối đều được kiểm tra bằng native API: đầy đủ ràng buộc, dangling=0, overdefining=0, support Face2 thuộc Feature gốc; không thêm RefPlane, không Fixed/Convert. Các Cut dùng NormalCut=true, Blind=t API+0,10 mm và OptimizeGeometry=false; `AssemblyFitGap=0,10 mm` tách riêng `SheetCutOverrun=0,10 mm`. Sau cả bốn góc, Cut thật khớp Preview, gap/overlap và lịch sử gốc đạt kiểm tra; source và bản sao CAD trên đĩa không đổi hash, không Save/Rebuild/Pack and Go.

## Cách xử lý phần dư

Không giao trực tiếp không đồng nghĩa giữ nguyên cả chiều dài phôi. Planner truyền datum kết thúc đã được xác minh từ cặp skin 0/t của Frame đối diện sang nếp liên tục cùng nhánh, chọn khoảng tiếp xúc thực gần đầu cạnh nối về phía nếp cần xén. Chỉ phần vượt biên mới được xén; phần thuộc thân giữ lại. Nếu không tìm được datum duy nhất thì dừng `UNVERIFIED_RULE`.

Mỗi vùng dư vẫn dùng Cut riêng theo chiều dày của một lớp tôn. Sketch gắn mặt chi tiết, ràng buộc tới datum hình học thực; không dựng mặt phẳng mới, không gom thành Through All. Xem [quy tắc và nguồn hình học](R61_RULE.md).

Native writer giữ cơ chế tạo Cut trong Assembly của R60. R61 xử lý thêm datum cao độ có điểm Sketch bị merge trùng cap thật; điểm merge chỉ được dùng khi chứng minh đúng identity hình học. Writer cắt các vùng giao của cả hai Frame trước rồi mới xén phần dư, giữ nguyên chuỗi mục tiêu B-rep của từng Part. Các lát xén dùng Sketch biên dạng gốc và mặt 0/t đã xác minh làm nguồn tham chiếu bền vững. Tra cứu báo cáo Sketch kết hợp Part, ID, vai trò và tên Sketch chính xác để tránh ID giống nhau giữa hai Part.

## Kiểm chứng ý đồ ngoài việc khớp Preview

R61 có phép đo riêng từ vật liệu **trước Cut**. Từng mask ngoài biên phải chứa thể tích dương ban đầu, sau Preview và native Cut phải không còn vật liệu trong tolerance 1e-12 m³. Vật liệu phía thân trong biên và các vùng KEEP được kiểm tra giữ nguyên. Cylinder R/R+t và cạnh tiếp tuyến thật tạo witness Bend riêng để phát hiện mép cong còn sót. Tất cả mask ngoài biên được kiểm tra lại sau góc cuối, vì Native=Preview có thể cùng sai nếu chỉ so sánh hai kết quả với nhau.

## Dữ liệu đầu vào và mức độ bằng chứng

File 2354 `fr04` hiện tại có SHA256 `5195027BE9E4B75BF1D0A1BD425B5B148968DC36CCF71FCAF400FFF3423F7D05`; dữ liệu lịch sử có hash khác. Timestamp nguồn đổi trước lượt R61; chưa xác định tác giả/nguyên nhân thay đổi. Phép thử ban đầu từ chối đúng kiểm tra hash trước mở CAD, sau đó dùng manifest hiện tại được đóng băng rõ ràng. Bộ lịch sử phục hồi nằm trong fixture riêng; không ghi đè file người dùng hoặc báo cáo cũ. Xem [biến thể nguồn và các lượt chưa đạt](INPUT_VARIANTS_AND_FAILED_ATTEMPTS.json).

- `API_VERIFIED`: Cut, End Condition, chiều dày, Normal/depth, Feature identity, Face2 và quan hệ Sketch đọc trực tiếp.
- `GEOMETRY_VERIFIED`: vật liệu dư ngoài biên mất hết, phía thân được giữ, Bend R, Body/gap/interference và so sánh native/Preview bằng B-rep.
- `UNVERIFIED`: ca chưa chạy, native rollback chưa chứng minh, nguồn thay đổi hoặc rule không có datum duy nhất; không tự thay bằng kết quả suy luận.

Native PMP 4332 dùng cùng source cuối với hai ca 2354. Native Close đã trả về trước Apply, SOLIDWORKS phát OnIdleNotify thật, rồi model work chạy trên owner STA; không giả lập idle hoặc lồng Application.DoEvents. DLL đang cài SHA256 `35D8B1BB7E63CD1CA46244DE47F6E22C15B71313124CEA332EDC1F9B261E6B3A`, MVID `8c96c27a-617f-4ece-ad7a-71a9d6c49a1d` được nạp sau khi mở bản sao CAD để giữ TaskPane và event subscribers như môi trường thực. AFJ đang thử vẫn là code candidate; không tuyên bố đã chạy CommandEntry/SessionManager hoặc nút Preview của DLL R61 được rebuild.

Compile độc lập sáu source chính đã PASS; không Build/Deploy Add-in chính, không đổi DLL/PDB/project/UI. Người dùng tự rebuild; xem [hướng dẫn](MANUAL_REBUILD.md) và [source verification](SOURCE_VERIFICATION.json). Thử candidate API/PMP không tương đương xác nhận command trong DLL mới sau rebuild hoặc khả năng nhìn thấy Preview. Rollback khi lỗi giữa chừng vẫn `UNVERIFIED`; một host từng lỗi native/rollback chưa xác minh được nghỉ dùng. Các lần lỗi và crash trước mở CAD được giữ riêng, không tính là PASS. Source cuối tạo 35 + 38 + 12 = 85 sự kiện Cut ở ba fixture/phép thử riêng; ca 4332 trước đó tạo 12 sự kiện bằng candidate trước. Không cộng các sự kiện này vào bộ 43 Cut mẫu hoặc 42 before/after lịch sử.
