# CODEX REVIEW REPORT — Circle Along Face V2 (PR #64)

- **Ngày đánh giá:** 2026-11-17
- **Repository:** `https://github.com/tbcphutai-bit/ADDIN-SolidWorks`
- **Branch:** `feature/circle-face-custom-equal-both-ends-20261117`
- **Pull Request:** #64 (`[DRAFT]`, stacked trên PR #63)
- **Commits được kiểm tra & bổ sung:**
  - Anti code + tests: `eb9d9e6f80b16fa9246e51865e01e09a3f8864bf` (`eb9d9e6`)
  - Anti report: `22413396a23264d240aafa142eeffbc36e3c6c7e` (`2241339`)
  - Review handoff: `f51a89b` (`CODEX_REVIEW_AFTER_ANTI_V2.md`)
  - Codex hardening fixes (sau kiểm tra độc lập): `4c4a7d64916530888b8643fe4ed3c06c7acd0cde` (`4c4a7d6`)
- **Môi trường kiểm chứng độc lập:** Windows x64, .NET Framework 4.8 (`C# 7.3`), SOLIDWORKS 2024 SP5.0 (`rev 32.5.0`)

---

## 1. Kết luận tổng thể và Các phát hiện theo mức độ nghiêm trọng

### Verdict: **Sẵn sàng xét duyệt cho PR #64 (sau bản vá `4c4a7d6`); đề xuất tách tùy chọn *Symmetric Overhang* thành bản V3 riêng biệt (đã chứng minh tính khả thi 3D đầy đủ bên dưới)**

### Phát hiện xếp hạng theo mức độ

1. **[HIGH — ĐÃ SỬA ở `4c4a7d6`] Lỗi tham số hóa `Edge` vs `Curve` trong `SwFaceLayout.ReadPiece` làm crash mặt phẳng có cung tròn / đoạn thẳng (`CIRCLE_TYPE = 3002`, `LINE_TYPE = 3001`)**
   - **Bằng chứng tái hiện:** Khi kiểm tra thực tế trên một chi tiết tấm phẳng (`surface_type = 4001`) tạo từ Sketch có 2 cung tròn nhỏ + 1 cung tròn lớn + 2 đoạn thẳng (`R3247 L=811.35 mm`, `R4323.16 L=1421.97 mm`), `SwFaceLayout.ReadPiece` văng `LayoutException: Coedge 2 does not match its evaluated curve endpoints.` trước cả khi mở UI.
   - **Đối chiếu log cũ của người dùng:** Chính lỗi này đã xuất hiện trong `TUBE_FACE_20261116_115713_770.log` (`Coedge 1 does not match its evaluated curve endpoints.` trên `6866-04-...3d.SLDPRT`) khi `edge.GetCurveParams3()` trả về `[UMinValue, UMaxValue]` theo hệ quy chiếu tham số của `Edge`/`CoEdge`, khác với hệ tham số thô của `edge.GetCurve()` (`curve.Evaluate2` cho ra điểm lệch `~1.19 m` hoặc ngược vòng tròn `180°`).
   - **Khắc phục (`SwFaceLayout.cs`):** Giữ đường nhanh `CURVE_DOMAIN` cho B-spline như trước; nếu không khớp, tự động chuyển sang `EDGE_DOMAIN_FALLBACK` gọi trực tiếp `edge.Evaluate2(u, ...)` trên `[UMinValue, UMaxValue]` và tích phân chiều dài cung bằng cầu phương Gauss-Legendre 5 điểm trên 32×4 đoạn (`EdgeSegmentLength`). Đã tái kiểm chứng thành công trên SolidWorks 2024 (`PlanarSplit EqualPitch N=20/30/40/41` và `Auto N=41`).

2. **[MEDIUM — ĐÃ SỬA ở `4c4a7d6`] Rủi ro race trạng thái UI khi sửa text `NumericUpDown` hoặc timer/COM message pump trong `CircleFaceTubeLayoutForm`**
   - **Vấn đề:** `NumericUpDown` chỉ bắn `ValueChanged` khi mất focus/bấm nút tăng giảm, còn khi gõ số mới trực tiếp chưa rời ô nhập liệu (`TextChanged`) hoặc khi sự kiện phát sinh trong lúc `analyze(...)` bơm thông điệp COM STA, kết quả cũ có thể giữ hoặc bật lại nút `Create 3D Sketch`.
   - **Khắc phục (`CircleFaceTubeLayoutForm.cs`):** Thêm bộ đếm đơn điệu `previewVersion` để loại bỏ kết quả preview lỗi thời và gắn `TextChanged -> InvalidatePendingText()` để hủy `Accepted`, khóa ngay `Create 3D Sketch`, và xóa đồ họa preview cũ ngay khi người dùng bắt đầu sửa số.

3. **[VERIFIED ACCURATE — KHÔNG PHẢI LỖI] Chặn `N=40` và `N=41` trong `EqualPitch` (`OD=20 mm, M=5 mm`) trên `4313`, `6866` và `R3247->R4323.16`**
   - Anti báo cáo đúng sự thật: tuy bước cung đầu nhỏ danh nghĩa là `20.041–20.172 mm > 20.000 mm` ở `N=40`, góc nghiêng mép ngoài `~9°–12.8°` làm khoảng cách vuông góc 3D thực tế giảm xuống `< 20.000 mm`. Đây là va chạm khối 3D thật chứ không phải lỗi GJK bảo thủ.

---

## 2. Kiểm tra độc lập thuật toán, va chạm 3D và đối chiếu báo cáo của Anti

### 2.1. Đường tâm 3D trên mặt xoắn (`VerifyExact`, `EstimateFaceClearance`, `CheckSheet`)
- **Bản chất hình học:** Mỗi trục ống nối thẳng giữa điểm offset pháp tuyến tại trạm đầu nhỏ `A_i = P_s(s_i) + n_s(s_i)*(R + lift)` và điểm offset pháp tuyến tại trạm đầu lớn `B_i = P_l(l_i) + n_l(l_i)*(R + lift)`. Đây **không phải** tiếp tuyến liên tục toàn chiều dài với mặt xoắn bậc hai, mà là **trục thẳng dựng từ pháp tuyến bề mặt tại hai đầu kết hợp nâng chung `lift`** cho tới khi toàn thân ống không cắt tấm.
- **Đo kiểm chứng độ võng / khe hở giữa thân ống với mặt tấm (`DeepAuditProbe.exe`, `N=30 EqualPitch`, 33 điểm dọc mỗi ống):**
  - `4313-04-HU-H1.SLDPRT` (`solid`, `lift = 0.0600 mm`): khe hở thân ống–tấm nằm trong `[0.0370, 0.2170] mm` (nhỏ nhất tại ống 23), `CheckSheet` B-rep `Clear = 30/30`.
  - `6866-04-円錐架台-上型-3d 133333.SLDPRT` (`surface`, `Flip=false`, `lift = 0.1978 mm`): khe hở nằm trong `[0.0198, 0.2191] mm` (nhỏ nhất tại ống 30), `Clear = 30/30`.
  - `6866` (`Flip=true`, `lift = 0.0600 mm`): khe hở nằm trong `[0.0374, 0.2204] mm` (nhỏ nhất tại ống 8), `Clear = 30/30`.
- **Nhận xét:** Cách xử lý này an toàn tuyệt đối về va chạm tấm vì `CheckSheet` tạo cylinder B-rep đủ chiều dài và giao với `ownerBody.ICopy()` (với solid) hoặc `CreateSheetFromFaces` (với surface); mọi trạng thái `Intersecting` hoặc `Inconclusive` đều tiếp tục tăng `lift` (tối đa `2*D = 40 mm`, 9 bước) hoặc khóa `Create`.

### 2.2. Kiểm tra GJK (`TubeDistance.Between`) vs SolidWorks B-rep (`Operations2`)
- **Kiểm tra mã nguồn (`TubeGeometry.cs`):** `TubeAxis.Support` tính support chính xác cho hình trụ **phẳng đầu** (hai đĩa đầu + thành trụ). GJK duy trì `lower` từ supporting hyperplane và `upper` từ simplex giảm tối ưu trên toàn bộ tập con (`Reduce`), dừng khi `upper - lower <= Resolution (0.0002 mm)`. Mọi cặp có `lower <= Resolution` (bao gồm trường hợp chưa chứng minh được tách rời `lower = 0`) đều bị chặn an toàn.
- **Đối chiếu cận chạm 3D trực tiếp trên SolidWorks 2024 (`AssemblyAndBrepAudit.exe`):**

| Cặp kiểm thử cận chạm (`D = 20 mm`) | GJK `[lower, upper]` (mm) | GJK chặn (`<= 0.0002`) | SolidWorks `Operations2` va chạm? | Kết luận |
|---|---:|---|---|---|
| Song song hở `+0.005 mm` | `[0.005000, 0.005000]` | False | False (`err=0`) | Khớp 100% |
| Song song hở `+0.001 mm` | `[0.001000, 0.001000]` | False | False (`err=0`) | Khớp 100% |
| Song song lấn `-0.001 mm` | `[0.000000, 0.000000]` | True | True (`err=0`) | Khớp 100% |
| Nghiêng `5°`, hở `+0.002 mm` | `[0.001961, 0.002039]` | False | False (`err=0`) | Khớp 100% |
| Nghiêng `5°`, lấn `-0.002 mm` | `[0.000000, 0.000000]` | True | True (`err=0`) | Khớp 100% |
| Đồng trục đầu-đầu hở `+2.000 mm` | `[2.000000, 2.000000]` | False | False (`err=0`) | Khớp 100% |
| Đồng trục đầu-đầu lấn `-0.002 mm` | `[0.000000, 0.000000]` | True | True (`err=0`) | Khớp 100% |

- **Đối chiếu toàn bộ 39 cặp liền kề của `4313` (`EqualPitch N=40, M=5`):** Các cặp `(1,2)..(21,22)` có `segGap` từ `-0.3289 mm` đến `-0.0062 mm` đều cho `swBrep = count=1` và `gjk = 0.0000`; các cặp `(22,23)..(39,40)` có `segGap` từ `+0.0049 mm` đến `+0.1083 mm` đều cho `swBrep = count=0` và GJK dương khớp tới `0.0002 mm`.

### 2.3. Kiểm tra tính chính xác của `EqualPitch` và ngữ cảnh Assembly (`.SLDASM`)
- **EqualPitch:** Không gọi `ContactTolerance` ở đầu nhỏ; `SolveCore` + `SolveEqualStations` hiệu chỉnh độc lập `first/last` hai đầu sao cho `CapClearance` trái/phải của cả đầu nhỏ lẫn đầu lớn hội tụ về `margin ± 0.02 mm`; `VerifyExact` đánh giá lại trên CAD thực và giữ bước cung đều nhau hoàn toàn.
- **Assembly `.SLDASM` end-to-end (bổ sung kiểm chứng còn thiếu trong báo cáo Anti):** Chạy trên bản sao `CircleFace-4316-validation.SLDASM` chứa component `4313-04-HU-H1-1` đặt xoay + tịnh tiến (`Plate transform: [-1,0,0; 0,0,-1; 0,-1,0]`, `t=[0.02, 0.30, 0]`, `scale=1`). `FaceInPartCoordinates()` ánh xạ đúng `asmFace -> partFace`, giải `EqualPitch N=30` (`sp=27.1274 mm, lp=48.2417 mm, lift=0.0600 mm`), tạo đúng feature `TUBE_AXES_3D_30` gồm 30 `SketchSegment` 3D với tọa độ khớp `ToDocument(...)` tới `< 1e-6 m`, và `UndoSketch` xóa sạch.
- **Đánh giá các tuyên bố trong `ANTI_IMPLEMENTATION_REPORT.md`:**
  - *Đã tái kiểm chứng trực tiếp đạt:* Pure `GeometryTests` (`/warnaserror`), `MSBuild Debug|x64 & Release|x64`, `4313` (`N=20/30/39/40/41`, `Auto`, `TangentContact`, `Flip`), `6866-133333` (`Flip=false/true`, `Create/Undo`), và thêm mới trong đợt review này: **Assembly `.SLDASM` end-to-end**, **mặt phẳng chia 2 cung tròn (`surface_type=4001`)**, và **7 cặp GJK vs B-rep cận chạm**.
  - *Chưa kiểm chứng tự động:* Bấm nút trực tiếp trên Task Pane (`BomTaskPaneControl`) bên trong giao diện GUI tương tác của SolidWorks (đã kiểm chứng cấp Form/Command qua COM); file gốc `6866-04-円錐架台-上型-3d.SLDPRT` ngày `2026-11-16` không có trên ổ đĩa (nhưng lỗi `ReadPiece` của nó đã được tái hiện nguyên nhân và vá sạch ở `4c4a7d6`).

---

## 3. Chứng minh hình học 3D & Phương án cho tùy chọn **Symmetric Overhang** (Đề xuất V3)

### 3.1. Phân tích 3D thực nghiệm bằng `OverhangProbe.exe` và `DeepAuditProbe.exe`

Khi đặt `overhang = H` (tương đương mục tiêu `CapClearance = -H`, tức mép ngoài của đĩa đầu ống φ20 được phép nhô cân đối ra ngoài hai góc biên trái/phải `H mm` với `first/last` đối xứng), có hai ngưỡng hình học rất rõ ràng:
- **Mức 1 — Tâm vẫn nằm trên biên cung cắt tỉa (`0 <= H <= R = 10 mm`, `s ∈ [0, L]`):** không cần ngoại suy đường cong hay bề mặt ngoài mặt đã chọn; mọi điểm đặt và pháp tuyến lấy chính xác từ SolidWorks.
- **Mức 2 — Tâm vượt nhẹ qua hai đầu biên (`H > R = 10 mm`, `s < 0` hoặc `s > L`):** phải ngoại suy tiếp tuyến đầu mút biên và giữ pháp tuyến đầu mút, đồng thời bắt buộc kiểm tra B-rep với `ownerBody` để không đâm vào gân/flange kế bên.

**Kết quả đo kiểm chứng 3D thực tế (`OD = 20 mm`, không nới lỏng bất kỳ kiểm tra va chạm nào):**

| Mô hình | Chế độ Overhang thử nghiệm | N | `H_min` đạt 3D (mm) | Center ngoài biên mỗi bên (mm) | `smallPitch` (mm) | `largePitch` (mm) | Cap L/R nhỏ (mm) | Cap L/R lớn (mm) | Min 3D gap (mm) | SolidWorks B-rep Sheet Clashes | Kết luận khả thi |
|---|---|---:|---:|---:|---:|---:|---|---|---:|---:|---|
| `Arc twist=0` (`811.35/1421.97`) | `SmallOnlyOverhang` (lớn `+5`) | 40 | **0.0** | 0.00 | 20.2970 | 35.6949 | 0.00 / 0.00 | +5.00 / +5.00 | **0.0844** | N/A | Khả thi trên biên |
| `Arc twist=0` (`811.35/1421.97`) | `BothEndsSymOverhang` | 40 | **0.0** | 0.00 | 20.2974 | 35.9515 | 0.00 / 0.00 | 0.00 / 0.00 | **0.0718** | N/A | Khả thi trên biên |
| `Arc twist=0` (`811.35/1421.97`) | `SmallOnlyOverhang` (lớn `+5`) | 41 | **8.0** | 0.00 | 20.1891 | 34.8021 | -8.00 / -8.00 | +5.00 / +5.00 | **0.0031** | N/A | Khả thi trên biên |
| `Arc twist=0` (`811.35/1421.97`) | `BothEndsSymOverhang` | 41 | **9.0** | 0.00 | 20.2400 | 35.5027 | -9.00 / -9.00 | -9.00 / -9.00 | **0.0221** | N/A | Khả thi trên biên |
| `Arc twist=0.07` (`811.35/1421.97`) | `BothEndsSymOverhang` | 40 / 41 | **0.0 / 9.0** | 0.00 | 20.2973 / 20.2398 | 35.9516 / 35.5028 | 0.00 / -9.00 | 0.00 / -9.00 | **0.0736 / 0.0242** | N/A | Khả thi trên biên |
| `6866` (`surface`, `817.55/1430.14`) | `BothEndsSymOverhang` | 40 / 41 | **0.0 / 9.0** | 0.00 | 20.4574 / 20.3946 | 36.1650 / 35.7098 | 0.00 / -9.00 | 0.00 / -9.00 | **0.0738 / 0.0149** | **0** | Khả thi trên biên |
| `4313` (`solid`, `816.55/1428.82`) | `SmallOnlyOverhang` (lớn `+5`) | 40 | **1.5** | 0.00 | 20.5055 | 35.8716 | -1.50 / -1.50 | +5.00 / +5.00 | **0.0106** | **0** | Khả thi trên biên |
| `4313` (`solid`, `816.55/1428.82`) | `BothEndsSymOverhang` | 40 | **2.0** | 0.00 | 20.5314 | 36.2316 | -2.00 / -2.00 | -2.00 / -2.00 | **0.0248** | **0** | Khả thi trên biên |
| **`4313` (`solid`, `816.55/1428.82`)** | **`SmallOnlyOverhang` (lớn `+5`)** | **41** | **11.5** | **1.50** | 20.4940 | 34.9746 | -11.50 / -11.50 | +5.00 / +5.00 | 0.0247 | **14 CLASHES (!)** | **KHÔNG ĐẠT với tấm** |
| **`4313` (`solid`, `816.55/1428.82`)** | **`BothEndsSymOverhang`** | **41** | **12.0** | **2.00** | 20.5193 | 35.8267 | -12.00 / -12.00 | -12.00 / -12.00 | **0.0230** | **0** | **Đạt khi ngoại suy `2.0 mm`** |
| **`6866_flip` (`surface`)** | **`SmallOnlyOverhang` (lớn `+5`)** | **41** | **11.0** | **1.00** | 20.4940 | 35.0075 | -11.00 / -11.00 | +5.00 / +5.00 | 0.0217 | **14 CLASHES (!)** | **KHÔNG ĐẠT với tấm** |
| **`6866_flip` (`surface`)** | **`BothEndsSymOverhang`** | **41** | **11.5** | **1.50** | 20.5194 | 35.8346 | -11.50 / -11.50 | -11.50 / -11.50 | **0.0209** | **0** | **Đạt khi ngoại suy `1.5 mm`** |

### 3.2. Phát hiện hình học then chốt từ thực nghiệm trên `4313` và `6866`

1. **Tại sao `BothEndsSymOverhang` thắng tuyệt đối `SmallOnlyOverhang` trên mặt xoắn lõm?**
   Khi chỉ cho đầu nhỏ lồi mép (`-11.5 mm`) mà vẫn giữ đầu lớn thụt vào (`+5 mm`), các ống ở hai cánh bị xoay lệch khỏi đường sinh tự nhiên của mặt nón xoắn, làm bụng ống cong võng xuống cắt mặt tấm (`14 clashes` SolidWorks B-rep trên cả `4313` lẫn `6866_flip` ở `lift = 0.06 mm`). Ngược lại, **Symmetric Overhang đều ở cả hai đầu (`BothEndsSymOverhang`)** giữ hướng ống bám sát đường sinh của mặt xoắn nên **0 va chạm tấm (`swSheetClashes = 0`)**!
2. **Tại sao `4313` (`N=41`) cần `H = 12.0 mm` (tâm vượt biên `2.0 mm`) trong khi `6866` (`Flip=false`) chỉ cần `H = 9.0 mm` (tâm nằm trong biên)?**
   Trên phía lõm của mặt nón (`4313 Flip=false` và `6866 Flip=true`), phép offset theo pháp tuyến vào tâm cong (`R_tube + lift ≈ 10.2 mm`) thu hẹp bán kính hiệu dụng; kết hợp góc loe bất đối xứng `~10.4°–12.8°` ở cạnh 2 (`2256.37 mm`), ngay cả khi tâm đặt tại đúng hai góc biên (`s=0, s=L`, `H=10 mm`), bước cung `20.414 mm` vẫn giao thoa nhẹ ở 4–5 cặp ngoài. Chỉ cần mở rộng đều hai đầu thêm `1.5–2.0 mm` (`H = 11.5–12.0 mm`), toàn bộ 41 ống tách nhau sạch sẽ (`minFull >= 0.021 mm`, `swSheetClashes = 0`).
3. **Tại sao không tự ý bật vào PR #64?**
   Theo `CODEX_REVIEW_AFTER_ANTI_V2.md`, Symmetric Overhang (đặc biệt khi `H > D/2` cần ngoại suy khỏi biên mặt cắt tỉa) phải là tùy chọn tường minh được phê duyệt riêng, không âm thầm kích hoạt làm thay đổi ý nghĩa của `Both-end edge margin` trong PR #64.

### 3.3. Phương án thiết kế cuối cùng cho V3 (đề xuất triển khai ngay sau khi ChatGPT/User duyệt)

- **UI tường minh (opt-in, mặc định TẮT):**
  - Trong `Custom` + `Equal pitch at both ends`, thêm checkbox `"Allow symmetric overhang"` (mặc định `false`) và ô `"Max cap overhang (mm)"` (mặc định `12.0 mm` hoặc `10.0 mm` nếu muốn giới hạn tâm nằm trên biên).
  - Nếu `Allow symmetric overhang == false` (mặc định của PR #64): giữ nguyên chặn `margin >= 0` và `smallLength > D + 2*margin`.
  - Nếu `Allow symmetric overhang == true`: cho phép solver tăng dần `overhang H` từ `max(0, -margin)` lên tới `maxOverhang` (hoặc dùng đúng `overhang` đối xứng chỉ khi cần giải quyết va chạm 3D, luôn ưu tiên `H` nhỏ nhất đạt mọi kiểm tra va chạm 3D + B-rep).
- **Quy tắc an toàn hình học khi `H > D/2` (tâm vượt biên cắt tỉa):**
  - Chiếu tiếp tuyến đầu mút `s < 0` / `s > L` bằng `p0 + inward * s`, giữ pháp tuyến đầu mút và **giới hạn cứng** độ vượt tâm `center_ext <= 0.5 * D` (`H <= D`, ví dụ `<= 15–20 mm`).
  - **Không** chạy `EstimateFaceClearance` theo kiểu kẹp góc giả; bắt buộc chạy SolidWorks B-rep `CheckSheet` trên `ownerBody.ICopy()` (để bắt mọi va chạm với thành gập/flange bên cạnh nếu có) và gắn cảnh báo rõ ràng trong Preview + log `OVERHANG_EXTRAPOLATED center_ext_mm=...`.
  - Các tùy chọn thay thế không cần vượt biên cho người dùng vẫn được giữ nguyên: giảm `N <= 39` (`M=5 mm`), chọn `N=40` với `H = 2.0 mm` (nằm trong biên), giảm `OD <= 19 mm`, hoặc chuyển sang `TangentContact`.

---

## 4. Kết quả Build & Test cập nhật sau commit `4c4a7d6`

| Hạng mục kiểm tra | Lệnh / Công cụ | Kết quả | Ghi chú / Bằng chứng |
|---|---|---|---|
| Pure Geometry Tests (`/warnaserror`) | `csc.exe /langversion:7.3 /platform:x64 /warnaserror ...` + `GeometryTests.exe` | **PASS** (0 warning, 0 error) | Đủ `N=20/30/40/41`, phẳng/cong/xoắn, `Flip`, `VerifyExact`, trụ nghiêng `10°`, biên `3/5` cạnh |
| Add-in Build `Debug\|x64` | `MSBuild ADDIN.csproj /p:Configuration=Debug /p:Platform=x64` | **PASS** (0 error) | `bin\CircleFace\Debug\ADDIN.dll` |
| Add-in Build `Release\|x64` | `MSBuild ADDIN.csproj /p:Configuration=Release /p:Platform=x64` | **PASS** (0 error) | `bin\CircleFace\Release\ADDIN.dll` |
| GJK vs SolidWorks B-rep cận chạm | `AssemblyAndBrepAudit.exe` (`rev 32.5.0`) | **PASS** (7/7 trường hợp) | Khớp 100% ở `±0.001 mm` |
| Assembly `.SLDASM` end-to-end | `AssemblyAndBrepAudit.exe` trên bản sao `CircleFace-4316-validation.SLDASM` | **PASS** | `4313-04-HU-H1-1` transformed -> `TUBE_AXES_3D_30` + `UndoSketch` |
| Mặt phẳng chia 2 cung tròn (`4001/3002`) | `AssemblyAndBrepAudit.exe` trên Part phẳng `811.35/1421.97` | **PASS** | Vá xong lỗi `ReadPiece`; `N=20/30 OK`, `N=40/41 INFEASIBLE`, `Auto=41 OK` |
| SolidWorks 2024 Part Integration + UI | `FullIntegrationTest.exe` trên bản sao `4313` & `6866-133333` | **PASS** | UI, Preview, `Cancel`, `Create 3D Sketch`, `UndoSketch`, Logs |

**Debug Logs mới nhất sau `4c4a7d6`:**
- `C:\Users\SGN26\AppData\Local\TAI_TOOL\CircleFaceTubeLayout\TUBE_FACE_INTEGRATION_4313_20261117_185411_236.log`
- `C:\Users\SGN26\AppData\Local\TAI_TOOL\CircleFaceTubeLayout\TUBE_FACE_INTEGRATION_6866_20261117_185522_512.log`

---

## 5. Checklist đề xuất cho ChatGPT đánh giá & phê duyệt

- [x] Bảo toàn 100% mode `TangentContact` (Auto & Custom), không thay đổi behavior cũ.
- [x] Tính chiều dài cung thực chính xác cho cả B-spline (`3006`), cung tròn (`3002`), và đoạn thẳng (`3001`) kể cả khi tham số `Edge` lệch hệ quy chiếu `Curve`.
- [x] `EqualPitch` chia đều độc lập hai đầu, hiệu chỉnh `CapClearance` cân đối trái/phải, khóa `Create` khi có va chạm 3D hoặc va chạm tấm B-rep.
- [x] Xác nhận kiểm chứng trên Part xoắn solid (`4313`), surface (`6866`), mặt phẳng chia đoạn (`4001`), và Assembly (`.SLDASM`) trong SolidWorks 2024 SP5.0.
- [x] Không chỉnh sửa command khác, không ghi đè file gốc, không merge vào `main`.
- [ ] **Quyết định cần ChatGPT/User chốt:**
  1. Phê duyệt merge PR #64 (`EqualPitch` chuẩn không overhang + bản vá `ReadPiece` + bản vá Form) sau khi test bấm trực tiếp Task Pane một lần trên giao diện SolidWorks của user; và
  2. Phê duyệt triển khai tiếp V3 cho tùy chọn tường minh **`Allow symmetric overhang`** theo thông số chứng minh ở Mục 3 (`H = 2.0 mm` cho `N=40`, `H = 9.0–12.0 mm` cho `N=41` trên `4313`/`6866`).
