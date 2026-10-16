# CODEX TASK — Kiểm thử nền tính thứ tự bẻ trong **Check chấn** hiện tại

> **Repo:** \`tbcphutai-bit/ADDIN-SolidWorks\`  
> **Branch:** \`feature/check-chan-planning-foundation\`  
> **PR:** https://github.com/tbcphutai-bit/ADDIN-SolidWorks/pull/3  
> **Không dùng PR #2** (nhánh tạo lệnh độc lập đã lỗi thời).  
> **Mục tiêu giai đoạn này:** xác minh nguồn đọc Bend hiện có bằng Part thật, sửa vấn đề nền tảng nếu có, rồi push thay đổi để ChatGPT xem lại trên GitHub. **Chưa triển khai UI mới, Check Max, chọn dao hoặc kết luận thứ tự khả thi.**

## 1. Nguyên tắc và phạm vi

- **Phải tích hợp bên trong tính năng \`Check chấn\` hiện hữu**, không tạo command/nút/menu/form khác.
- Tận dụng dữ liệu từ \`BendCheckSession\`, \`VCutPropertySetupCommand.CaptureForCheck\`, \`BendMinimumFeatureGraph\`, \`BendMinimumSection\`, \`BendMinimumGeometryCache\`. Không viết lại cách lấy mặt cắt nếu phần có sẵn đủ dùng.
- Giữ nguyên kết quả **Check Min (cạnh tối thiểu)**, bảng Excel, phép đo phủ bì, đánh dấu V溝 và hành vi Part/Assembly.
- Kiểm thử trên **native Sheet Metal Part có Feature Tree và Flat-Pattern**, ở trạng thái đã chấn.
- \`BendCheckPlanningDraft\` hiện chỉ sinh **các thứ tự ứng viên chưa xác minh**; không được gọi chúng là thứ tự gia công hợp lệ.
- Chỉ có các trạng thái phù hợp hiện tại: \`CHUA_XAC_MINH\`, hoặc ghi lỗi cụ thể. Không trả \`PASS\`, \`OK gia công\` khi chưa có engine kiểm tra chuyển động/va chạm và dữ liệu dụng cụ đã xác minh.
- Không tự sử dụng 4.200/6.000 mm từ Excel như năng lực máy chắc chắn; không sử dụng đầu kẹp AMTS III dạng minh họa như CAD gá thật.
- SOLIDWORKS COM phải chạy trên thread phù hợp với luồng UI/STA của Add-in; không chuyển tác vụ COM sang worker Task nền.
- Không tự động sửa/suppress/unsuppress/rebuild/chuyển configuration/save model gốc để phục vụ việc kiểm tra.

## 2. Đọc source trước khi thay đổi

Kiểm tra các file dưới đây và mô tả trong PR kết nối dữ liệu:

1. \`UI/BendMinimumTaskPaneIntegration.cs\` — nút \`Check chấn\` hiện có.
2. \`UI/BendCheckForm.cs\` — bảng Part/Assembly, cột \`Check Min\` và \`Check Max\` (hiện chưa tính Max).
3. \`Commands/BendCheckSession.cs\` — quét Part/Assembly, \`CaptureForCheck\`, bảo vệ stale model; hiện đã gọi \`BendCheckPlanningDraft.Create\`.
4. \`Commands/VCutPropertySetupCommand.cs\` — \`BendRow\`: ID, góc, R, t, \`PersistentId\`.
5. \`Commands/BendMinimumFeatureGraph.cs\` / \`Commands/BendMinimumSection.cs\` — hình học và mặt cắt sẵn có.
6. \`Commands/BendCheckPlanningDraft.cs\` — danh sách Bend, danh sách thứ tự giả định, JSON Debug.
7. \`ADDIN.csproj\` — dự án kiểu cũ (explicit Compile Include), .NET Framework 4.8, C# 7.3/x64.

## 3. Công việc Codex cần thực hiện

### A. Build và sửa lỗi tích hợp (nếu có)

- Checkout nhánh trên, đối chiếu với \`main\` mới nhất; **không xóa hoặc ghi đè commit của người khác**.
- Build solution cho target phù hợp SOLIDWORKS trên máy Windows.
- Nếu có lỗi C# / missing references / explicit Compile Include / serializer / runtime, sửa **tối thiểu trong phạm vi Check chấn**.
- Không thay đổi \`SWAddin.cs\` hoặc \`BomTaskPaneControl.Designer.cs\` nếu không thật sự cần. Không đổi giao diện.

### B. Kiểm thử với Part mẫu thật

Dùng Part người dùng đã cung cấp ở **máy Windows**, tên file:

\`4114-04-HN-C07-12_New.SLDPRT\`

**Không push SLDPRT lên GitHub công khai**, không copy nội dung hình học chi tiết vào logs công khai, không đưa vào bộ fixtures của repo.

Quy trình kiểm thử:

1. Mở Part trong SOLIDWORKS, chọn cấu hình Sheet Metal đã chấn (Feature Tree và Flat-Pattern có sẵn).
2. Mở **chính lệnh \`Check chấn\` hiện có**.
3. Ghi nhận **số Bend thật do SOLIDWORKS nhận diện** (không giả định Part có 2 Bend).
4. So sánh từng Bend: tên Feature, ID, góc, R, t, \`PersistentId\` có/không và kết quả Check Min.
5. Kiểm tra đoạn \`[BEND PLAN DRAFT]\` ở Visual Studio Debug Output: BendCount, BendIds, CandidateOrderCount, \`Exhaustive\`, \`Reason\`, \`Status\`.
6. Kiểm tra sau khi chỉnh Part / rebuild thủ công / chuyển configuration: dữ liệu cũ không còn được trình bày như kết quả mới; cần quét lại.
7. Mở một Part chữ L, chữ U, chữ Z đơn giản nếu có để kiểm thử đối chiếu 1–2 Bend. Với đúng 2 Bend nhận diện, phải có 2 hoán vị đảo thứ tự và cả hai \`CHUA_XAC_MINH\`.
8. Kiểm tra lại cùng Part trên \`main\` hoặc so sánh bản build trước: **Check Min phải cho ra kết quả tương đương**.

Nếu Part thực tế có >8 Bend thì phải ghi giới hạn của bản V0; **không** gắn nhãn “không chấn được”.

### C. Làm sạch và củng cố đúng phạm vi

- Sửa những lỗi phát hiện được trong việc nối \`BendRow\` → \`BendCheckPlanningDraft\`: thiếu bend, trùng ID, trạng thái stale, lỗi JSON, null hoặc ngoại lệ.
- Không thêm logic phát hiện/đo mặt cắt mới khi chưa chứng minh engine hiện có sai.
- Có thể thêm **unit test cho phép hoán vị thuần C#** và trạng thái \`CHUA_XAC_MINH\`, miễn không cần SOLIDWORKS API trong test.
- Không thiết kế UI hoặc DB dao ở lượt này.
- Không thay đổi phép đánh giá Check Min để ép test đi qua.

## 4. Điều kiện nghiệm thu

- [ ] Build thành công hoặc báo chính xác blocker môi trường.
- [ ] Cửa sổ \`Check chấn\` hiện tại vẫn mở/chạy; không sinh thêm nút hay command.
- [ ] Check Min không hồi quy (logic/hiển thị/Part/Assembly).
- [ ] Đọc đúng số Bend và dữ liệu cho Part mẫu, hoặc báo rõ trường hợp chưa hỗ trợ.
- [ ] JSON debug hợp lệ, \`BendId\` không trùng trong một lần quét.
- [ ] Các thứ tự ứng viên đều \`CHUA_XAC_MINH\`.
- [ ] Không sửa Part gốc; không commit file Part mẫu.
- [ ] Thay đổi từ lần quét cũ được invalidated theo cơ chế hiện có.
- [ ] Các thay đổi chỉ nằm trong module của \`Check chấn\` và file test liên quan.

## 5. Codex phải bàn giao

**Push code lên branch \`feature/check-chan-planning-foundation\` (PR #3), không merge.** Sau đó viết vào comment của PR #3:

- Commit SHA và danh sách file đã sửa.
- Kết quả build: command/configuration, PASS hoặc lỗi cụ thể.
- Kết quả Part mẫu: bao nhiêu Bend thật, bao nhiêu row từ Check chấn, bao nhiêu thứ tự ứng viên, status, lỗi nếu có.
- So sánh trước/sau cho Check Min.
- Những phần chưa hoàn thành/thiếu bằng chứng; tuyệt đối không tuyên bố đã xác minh khả năng chấn.
- Không đăng hình CAD hoặc dữ liệu thiết kế nhạy cảm lên GitHub công khai; nếu cần screenshot, gửi người dùng riêng.

**Khi Codex push xong:** Người dùng báo lại trong ChatGPT để ChatGPT đọc chính commit/PR mới trên GitHub và giao việc kế tiếp: tích hợp thư viện dao DXF và Check Max mà không làm hỏng Check Min.
