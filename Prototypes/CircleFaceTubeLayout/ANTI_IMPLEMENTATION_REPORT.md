# ANTI IMPLEMENTATION REPORT — Circle Along Face V2 (Custom Equal-Pitch Both Ends)

- **Ngày thực hiện:** 2026-11-17
- **Repository:** `https://github.com/tbcphutai-bit/ADDIN-SolidWorks`
- **Branch:** `feature/circle-face-custom-equal-both-ends-20261117`
- **Pull Request:** #64 (Draft, stacked trên #63 `feature/circle-face-tube-layout-prototype-20261110`)
- **Commit SHA (code + tests):** `eb9d9e6f80b16fa9246e51865e01e09a3f8864bf` (`eb9d9e6`)
- **Môi trường kiểm chứng:** Windows x64, .NET Framework 4.8 (`C# 7.3`), SOLIDWORKS 2024 SP5.0 (`rev 32.5.0`)

---

## 1. Mục tiêu và logic đã hoàn thành

Command **Circle Along Face** nhận đầu vào là **1 mặt tấm cong hoặc xoắn** (`Face2` trên Part hoặc top-level Part trong Assembly), tự nhận diện hai biên cung ngắn/dài đối diện và tính chiều dài cung thực bằng SolidWorks `ICurve.GetLength3`.

Hai chế độ bố trí được hỗ trợ và tách biệt rõ ràng:

1. **Mode 1 — `TangentContact` (giữ nguyên mặc định & Auto count):**
   - Đầu nhỏ: đặt các ống φD tiếp xúc gần nhau trong 3D (`TargetGap = 0.005 mm`, `ContactTolerance = 0.05 mm`), có thể lồi mép đầu nhỏ (*small-end overhang* cho phép).
   - Đầu lớn: chia đều theo chiều dài cung thực và hiệu chỉnh khoảng dư mép hai bên cân đối bằng `CapClearance` (`SymmetryTolerance = 0.02 mm`).
   - Hỗ trợ **Auto count** (tìm từ `200` xuống số lượng lớn nhất đạt kiểm tra 3D + B-rep) và **Custom count** (kèm **Add +1**). Khi bật `Auto count`, bộ chọn mode bị khóa và hệ thống luôn ép mode hiệu lực về `TangentContact`.

2. **Mode 2 — `EqualPitch` (chia đều hai đầu cho Custom Quantity):**
   - Chỉ kích hoạt khi tắt `Auto count` và chọn `"Equal pitch at both ends"`.
   - Khởi tạo từ công thức danh nghĩa `first = last = D/2 + M`, `pitch = (L - first - last) / (N - 1)` riêng biệt cho đầu nhỏ (`Ls`) và đầu lớn (`Ll`), rồi hiệu chỉnh lặp (`SolveCore` + `SolveEqualStations`) theo hình chiếu đĩa đầu ống thực 3D (`CapClearance`) để cân đối khoảng dư trái/phải của **cả hai đầu** về đúng `margin ± 0.02 mm`.
   - Kiểm tra điều kiện chặn sớm `smallLength > diameter + 2 * margin` và `largeLength > diameter + 2 * margin` cho `EqualPitch`.
   - **Không** kiểm tra dung sai tiếp xúc đầu nhỏ (`ContactTolerance`); khe hở đầu nhỏ dương là hợp lệ.
   - **Luôn** chạy `VerifyExact` trên đường cong CAD chính xác, kiểm tra khoảng cách 3D toàn chiều dài mọi cặp ống (`TubeDistance.Between` bằng GJK trên hình trụ hữu hạn phẳng đầu) và kiểm tra va chạm B-rep với mặt/tấm (`SwFaceLayout.CheckSheet` dùng `Operations2(SWBODYINTERSECT)`). Bất kỳ cặp nào có `Lower <= TubeDistance.Resolution` (`0.0002 mm`, kể cả trường hợp bất định `Lower == 0`) hoặc B-rep `Intersecting`/`Inconclusive` đều bị đánh dấu `INFEASIBLE` và **khóa nút `Create 3D Sketch`**.

3. **Cải tiến UI, Preview, 3D Sketch và Debug Log:**
   - Khi thay đổi bất kỳ thông số nào (`diameter`, `quantity`, `margin`, `Auto count`, `Add +1`, `Flip Side`, `smallMode`), form lập tức hủy chấp nhận (`Accepted = null`), khóa nút `Create`, **xóa ngay đồ họa preview cũ trên mô hình** (`clearPreview`), và chạy lại xác thực qua timer.
   - Chặn tái nhập timer và truy cập sau dispose trong `CircleFaceTubeLayoutForm.Preview()`.
   - Báo cáo Preview hiển thị đầy đủ trên `TextBox` cuộn dọc (không bị cắt chữ): chiều dài cung đầu nhỏ/lớn, mode, `SmallPitch` (chỉ ở `EqualPitch`, không còn hiển thị `0.000 mm` gây hiểu nhầm ở `TangentContact`), `LargePitch`, khoảng dư cap trái/phải của cả hai đầu, khe hở đầu nhỏ liền kề, khe hở 3D toàn chiều dài nhỏ nhất kèm cặp `(a-b)`, số cặp va chạm nếu có, khoảng hở mặt tấm nhỏ nhất + kết quả SolidWorks B-rep, `lift`, và lý do lỗi cụ thể.
   - Preview chỉ dùng wire body tạm màu xanh lá (`LimeGreen` khi hợp lệ) hoặc đỏ cam (`OrangeRed` khi bất khả thi); `Cancel` xóa sạch không lưu Feature nào; chỉ khi bấm `Create 3D Sketch` mới tạo đúng 1 `3D Sketch` chứa N đường tâm dựng hình (`ConstructionGeometry = true`), hỗ trợ `Undo Sketch`.
   - Sửa lỗi biên giới `BinarySearch` và tăng giới hạn hội tụ đảo chiều dài cung (`56` bước với bước chia đôi định kỳ) trong `SwFaceLayout.ReadPiece`.

---

## 2. Các file và class đã chỉnh sửa

Phạm vi chỉnh sửa **chỉ nằm trong `CircleFaceTubeLayout`**, không chạm các command khác, không chạm Task Pane hay `ADDIN.csproj`, không merge vào `main`:

- `Commands/CircleFaceTubeLayout/TubeLayoutSolver.cs` (`TubeLayout`, `TubeLayoutSolver`)
  - Khởi tạo `MinimumSheetEstimate = double.NaN`, thêm các trường chẩn đoán `MinimumGapPairA/B`, `InterferingPairCount`, `FirstInterferingPairA/B`, `SheetClashCount`, `SheetInconclusiveCount`.
  - Thêm guard `smallLength > diameter + 2 * margin` cho `EqualPitch`; sửa `VerifyExact` dùng `layout.SmallRailLength / 100`; thêm `AddIssue` để không ghi đè mất lỗi trước đó; ghi `large_s_mm` vào từng dòng `AXIS` và log `INTERFERING_PAIR`.
- `Commands/CircleFaceTubeLayout/SwFaceLayout.cs` (`SwFaceLayout`)
  - Lưu `SheetClashCount` / `SheetInconclusiveCount` lên `TubeLayout`, bảo toàn lỗi trước đó qua `AddIssue`, giải thích rõ trường hợp `Flip Side` đâm vào vật liệu solid (`body_type = 0`).
  - Bảo vệ biên và tăng hội tụ `ReadPiece` (`At(station)`).
- `Commands/CircleFaceTubeLayout/TubeGeometry.cs` (`TubeDistance`)
  - Tăng số vòng lặp GJK tối đa từ `96` lên `128`.
- `Commands/CircleFaceTubeLayout/CircleFaceTubeLayoutForm.cs` (`CircleFaceTubeLayoutForm`)
  - Hỗ trợ `clearPreview` và `logLine` tùy chọn (giữ tương thích chữ ký constructor).
  - Khắc phục kẹt/tràn layout bằng `ClientSize(540, 425)`, tỷ lệ cột `54% / 46%`, `TextBox` báo cáo đầy đủ có thanh cuộn dọc, sửa lỗi hiển thị `Small pitch 0.000 mm` ở chế độ tiếp xúc, thêm guard chống tái nhập `previewInProgress` và kiểm tra `IsDisposed`.
- `Commands/CircleFaceTubeLayout/CircleFaceTubeLayoutCommand.cs` (`CircleFaceTubeLayoutCommand`)
  - Truyền `ClearPreview` và `Log` vào form; ghi thêm dòng `PREVIEW_SUMMARY` đầy đủ tất cả chỉ số trước `PREVIEW_END`.
- `Tests/CircleFaceTubeLayout/GeometryTests.cs` (`GeometryTests`)
  - Điều chỉnh và mở rộng `TestEqualPitchCustom()` (phân tách rõ trường hợp tuyến tính góc nghiêng thấp nơi `N=40` đạt danh nghĩa lẫn 3D, với trường hợp cung tròn/xoắn `R3247 -> R4323.16` nơi `N=20/30` đạt còn `N=40/41` thực sự va chạm 3D ở mép nghiêng ~9°; bổ sung kiểm tra `M=0 mm`, nhịp dài `5.5 m`, `Flip Side` trong `EqualPitch`, kiểm tra chiều dài tối thiểu `D + 2M`, kiểm tra `VerifyExact`, cặp trụ nghiêng 10° và lưới biên 3/5 cạnh).
- `Prototypes/CircleFaceTubeLayout/ANTI_IMPLEMENTATION_REPORT.md` (báo cáo này).

---

## 3. Kết quả Build và Test thực tế

### 3.1. Pure Geometry Tests (`/warnaserror`, C# 7.3 x64)
- **Lệnh build & chạy:**
  ```powershell
  csc.exe /nologo /langversion:7.3 /platform:x64 /warnaserror /out:bin\CircleFace\GeometryTests.exe Tests\CircleFaceTubeLayout\GeometryTests.cs Commands\CircleFaceTubeLayout\TubeGeometry.cs Commands\CircleFaceTubeLayout\TubeLayoutSolver.cs Commands\CircleFaceTubeLayout\BoundaryRails.cs
  .\bin\CircleFace\GeometryTests.exe
  ```
- **Kết quả:** Biên dịch **0 warning, 0 error**, thực thi thoát mã `0` (`PASS all geometry tests`).
  - Ghi nhận quan trọng: Trước khi sửa test, `TestEqualPitchCustom()` gốc của PR #64 bị lỗi ngay ở `Custom 30` (`FAIL Independent pitches for Custom 30`) vì fixture cũ dùng hình thang lệch `(s, 0, 0) -> (s, 1, 0)` có góc nghiêng cạnh phải `31.4°` làm dịch trạm cuối `1.47 mm` bởi hiệu chỉnh cap-projection, nhưng lại assert công thức danh nghĩa không hiệu chỉnh trong sai số `0.02 mm`.

### 3.2. Build Add-in x64 (.NET Framework 4.8 + SolidWorks 2024 Interop)
- **Lệnh Debug|x64:**
  ```powershell
  MSBuild.exe ADDIN.csproj /t:Build /p:Configuration=Debug /p:Platform=x64 /p:PostBuildEvent= /p:RegisterForComInterop=false /p:OutputPath="bin\CircleFace\Debug\\" /p:ReferencePath="C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\redist" /v:minimal
  ```
- **Lệnh Release|x64:**
  ```powershell
  MSBuild.exe ADDIN.csproj /t:Build /p:Configuration=Release /p:Platform=x64 /p:PostBuildEvent= /p:RegisterForComInterop=false /p:OutputPath="bin\CircleFace\Release\\" /p:ReferencePath="C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\api\redist" /v:minimal
  ```
- **Kết quả:** Cả `Debug|x64` (`bin\CircleFace\Debug\ADDIN.dll`) và `Release|x64` (`bin\CircleFace\Release\ADDIN.dll`) build thành công (mã thoát `0`, 0 lỗi, 0 cảnh báo trong `Commands/CircleFaceTubeLayout/*`). Không ghi đè `C:\Tao moi\ADDIN.dll` khi chưa yêu cầu cài đặt.

### 3.3. Kiểm thử tích hợp thực tế trên SolidWorks 2024 SP5.0 (`rev 32.5.0`)
Thực hiện trên **bản sao tạm trong scratch** (không sửa file gốc) của hai file Part thực tế:
1. `4313-04-HU-H1.SLDPRT` (`body_type = 0` — tấm solid dày, bề mặt xoắn bất đối xứng: đầu nhỏ `816.5458 mm`, đầu lớn `1428.8246 mm`, hai cạnh bên `2177.3361 mm` & `2256.3671 mm`).
2. `6866-04-円錐架台-上型-3d 133333.SLDPRT` (`body_type = 1` — sheet surface body xoắn: đầu nhỏ `817.5470 mm`, đầu lớn `1430.1414 mm`, hai cạnh bên `2177.3361 mm` & `2256.3671 mm`).

Kiểm tra trực tiếp qua `CircleFaceTubeLayoutForm` + `CircleFaceTubeLayoutCommand` + `SwFaceLayout` (`FullIntegrationTest.exe`) đạt 100%:
- Chuyển trạng thái UI (`Auto count` khóa/mở `smallMode`, `Add +1` cộng đúng 1, thay đổi giá trị lập tức khóa `Create` và xóa preview cũ, bật lại `Auto count` ép `effective_mode = TangentContact`).
- `Preview` + `Cancel` giữ nguyên số lượng feature trên Part.
- `Create 3D Sketch` tạo đúng feature `TUBE_AXES_3D_30` chứa 30 `SketchSegment` 3D với `ConstructionGeometry = true`, và `UndoSketch` xóa sạch feature khỏi cây thiết kế.

---

## 4. Bảng các trường hợp kiểm thử (OK / FAIL)

### 4.1. Ví dụ chuẩn (đầu nhỏ `811.35 mm`, đầu lớn `1421.97 mm`, `OD = 20 mm`, `M = 5 mm`) — Pure Geometry

| Trường hợp | Mode | N | Small pitch (mm) | Large pitch (mm) | Small cap L/R (mm) | Large cap L/R (mm) | Min full 3D gap (mm) | Trạng thái | Lý do / Ghi chú |
|---|---|---:|---:|---:|---|---|---:|---|---|
| Mặt cong phẳng `R3247 -> R4323.16` | `EqualPitch` | 20 | 41.1368 | 73.2689 | 4.9998 / 4.9998 | 5.0001 / 5.0001 | 20.7436 | **OK** | Tách đều 2 đầu, margin cân đối |
| Mặt cong phẳng `R3247 -> R4323.16` | `EqualPitch` | 30 | 26.9517 | 48.0038 | 4.9998 / 4.9998 | 5.0001 / 5.0001 | 6.6623 | **OK** | Tách đều 2 đầu, margin cân đối |
| Mặt cong phẳng `R3247 -> R4323.16` | `EqualPitch` | 40 | 20.0410 | 35.6951 | 4.9998 / 4.9998 | 5.0001 / 5.0001 | 0.0000 | **FAIL (Chặn)** | Ống ngoài nghiêng ~9° so với pháp tuyến cung nhỏ -> `20.041 * cos(9°) ≈ 19.79 mm < 20 mm`, va chạm 3D ở mép |
| Mặt cong phẳng `R3247 -> R4323.16` | `EqualPitch` | 41 | 19.5400 | 34.8027 | 4.9998 / 4.9998 | 5.0001 / 5.0001 | 0.0000 | **FAIL (Chặn)** | Bước chia danh nghĩa `19.534 mm < 20 mm`, va chạm 3D |
| Mặt xoắn (`twist=0.07`), `Flip=false/true` | `EqualPitch` | 20 / 30 | 41.1365 / 26.9515 | 73.2690 / 48.0038 | 4.9998 / 4.9999 | 5.0001 / 5.0001 | 20.7466 / 6.6645 | **OK** | `Flip Side` đảo chiều pháp tuyến chính xác |
| Mặt xoắn (`twist=0.07`) | `EqualPitch` | 40 / 41 | 20.0409 / 19.5398 | 35.6952 / 34.8028 | 4.9998 / 4.9999 | 5.0001 / 5.0001 | 0.0000 | **FAIL (Chặn)** | Phát hiện giao thoa 3D ở cả hai đầu nghiêng |
| Mặt phẳng góc mở nhỏ (`span=5.5 m`) | `EqualPitch` | 40 | ~20.035 | ~35.692 | 5.000 / 5.000 | 5.000 / 5.000 | > 0.0002 | **OK** | Khi góc nghiêng < 3.3°, N=40 không chạm (N=41 vẫn FAIL) |
| Mặt xoắn (`twist=0.07`), `M=0 mm` | `EqualPitch` | 40 | ~20.292 | ~36.208 | 0.000 / 0.000 | 0.000 / 0.000 | > 0.0002 | **OK** | Giảm `M` về 0 mm giúp N=40 đủ khe 3D (N=41 vẫn FAIL) |
| Mặt cong/xoắn `R3247 -> R4323.16` | `TangentContact` (Auto & Custom) | 40 / 41 | (tiếp xúc) | 35.69 / 34.799 | (cho phép lồi mép) | 5.000 / 5.000 | > 0.005 | **OK** | Giữ nguyên hành vi cũ |

### 4.2. Kiểm thử trên Part thực tế trong SolidWorks 2024 SP5.0 (`OD = 20 mm`)

| Model | Cấu hình | Mode | N | M (mm) | Flip | Lift (mm) | Min full gap (mm) | BREP sheet | Trạng thái |
|---|---|---|---:|---:|---|---:|---:|---|---|
| `4313-04-HU-H1.SLDPRT` (`solid`, `816.55/1428.82`) | `Auto` | `TangentContact` | 41 | 5 | False | 0.1497 | 0.0050 (34-35) | Clear | **OK** |
| `4313-04-HU-H1.SLDPRT` | `Custom` | `TangentContact` | 40 / 41 | 5 | False | 0.0600 / 0.1497 | 0.0050 / 0.0050 | Clear | **OK** |
| `4313-04-HU-H1.SLDPRT` | `Custom` | `EqualPitch` | 20 | 5 | False | 0.0600 | 20.4300 (1-2) | Clear | **OK** |
| `4313-04-HU-H1.SLDPRT` | `Custom` | `EqualPitch` | 30 | 5 | False | 0.0600 | 6.4657 (1-2) | Clear | **OK** (+ tạo & Undo 3D Sketch OK) |
| `4313-04-HU-H1.SLDPRT` | `Custom` | `EqualPitch` | 39 | 5 | False | 0.0600 | 0.1896 (1-2) | Clear | **OK** |
| `4313-04-HU-H1.SLDPRT` | `Custom` | `EqualPitch` | 40 | 5 | False | 0.0600 | 0.0000 (21 cặp, đầu tiên 1-2) | Clear | **FAIL (Chặn)** — B-rep SolidWorks `Operations2` xác nhận giao thoa thật `-0.3289 mm` |
| `4313-04-HU-H1.SLDPRT` | `Custom` | `EqualPitch` | 40 | 0 | False | 0.0600 | 0.0000 (4 cặp, đầu tiên 1-2) | Clear | **FAIL (Chặn)** |
| `4313-04-HU-H1.SLDPRT` | `Custom` | `EqualPitch` | 41 | 5 | False | 0.0600 | 0.0000 (40 cặp, đầu tiên 1-2) | Clear | **FAIL (Chặn)** |
| `4313-04-HU-H1.SLDPRT` | `Auto/EqualPitch` | Cả 2 | 20..41 | 5 | True | 1.253..3.321 | — | Clashes > 0 | **FAIL (Chặn)** — Flip trên solid đâm vào vật liệu thép của tấm |
| `6866-04-...-3d 133333.SLDPRT` (`surface`, `817.55/1430.14`) | `Auto / Custom` | `TangentContact` | 40 / 41 | 5 | False & True | 0.0600..0.2488 | 0.0050 | Clear | **OK** cả 2 mặt |
| `6866-04-...-3d 133333.SLDPRT` | `Custom` | `EqualPitch` | 20 / 30 / 39 | 5 | False & True | 0.0600..0.1978 | 20.74 / 6.67 / 0.34 | Clear | **OK** cả 2 mặt (+ tạo & Undo 3D Sketch ở `Flip=True, N=30` OK) |
| `6866-04-...-3d 133333.SLDPRT` | `Custom` | `EqualPitch` | 40 | 0 | False | 0.1420 | 0.0731 (39-40) | Clear | **OK** (với `Flip=True` vẫn FAIL 3 cặp ở mép kia) |
| `6866-04-...-3d 133333.SLDPRT` | `Custom` | `EqualPitch` | 40 / 41 | 5 | False & True | 0.0600..0.1978 | 0.0000 (19..40 cặp) | Clear | **FAIL (Chặn)** |

---

## 5. Đường dẫn Debug Log thực tế

Tất cả log được ghi tại `%LOCALAPPDATA%\TAI_TOOL\CircleFaceTubeLayout\`:
- `C:\Users\SGN26\AppData\Local\TAI_TOOL\CircleFaceTubeLayout\TUBE_FACE_INTEGRATION_4313_20261117_170050_427.log`
- `C:\Users\SGN26\AppData\Local\TAI_TOOL\CircleFaceTubeLayout\TUBE_FACE_INTEGRATION_6866_20261117_170209_319.log`

---

## 6. Lỗi / Giới hạn còn tồn tại và nguyên nhân

1. **Giới hạn hình học của `N = 40` với `D = 20 mm, M = 5 mm` ở chế độ `EqualPitch`:**
   - Với đầu nhỏ `811.35 mm` (`R3247`) đến đầu lớn `1421.97 mm` (`R4323.16`), hoặc trên `4313-04-HU-H1.SLDPRT` (`816.55 -> 1428.82 mm`, cạnh nghiêng bất đối xứng `2256 mm` vs `2177 mm`), bước chia cung đầu nhỏ chỉ là `20.04 mm` – `20.17 mm`. Tuy lớn hơn `20.00 mm` trên đường cong, các ống gần mép nghiêng góc `~9°–10.4°` so với pháp tuyến cung nhỏ, nên khoảng cách vuông góc thực 3D giữa hai hình trụ là `pitch * cos(theta) ≈ 19.67..19.79 mm < 20.00 mm`.
   - Đối chiếu bằng `CollisionAudit` (khoảng cách đoạn trục + SolidWorks B-rep `Operations2`) trên `4313` chứng minh cặp `(1, 2)` thực sự giao thoa **`-0.3289 mm`** (`21` cặp giao thoa từ `1-2` đến `21-22`). Đây là ràng buộc vật lý 3D thật chứ không phải lỗi solver; giải pháp hợp lệ cho người dùng là: giảm `N` (tối đa `39` ở `M=5 mm`), giảm `D`, giảm `M`, hoặc dùng Mode 1 `TangentContact` (cho phép lồi mép đầu nhỏ và bước cung không đều).
2. **`Flip Side` trên solid body dày (`body_type = 0` như `4313`):**
   - Khi chọn mặt ngoài của tấm thép solid có độ dày lớn hơn bước nâng khả dĩ trong 9 lượt (`lift <= 3.32 mm`), đảo pháp tuyến sẽ hướng thẳng vào trong khối đặc của tấm và bị chặn đúng đắn. Người dùng muốn đặt ống ở mặt bên kia của tấm solid cần chọn trực tiếp mặt bên kia (hoặc dùng mặt surface `body_type = 1` như `6866`).
3. **Các kiểm thử chưa chạy trực tiếp trong phiên này:**
   - Thao tác bấm chuột tương tác trực tiếp trên Task Pane (`BomTaskPaneControl`) bên trong cửa sổ SolidWorks GUI đang chạy của người dùng (đã kiểm tra qua test tích hợp gọi trực tiếp `CircleFaceTubeLayoutForm` + `CircleFaceTubeLayoutCommand` trên bản sao Part trong SolidWorks 2024 COM).
   - Assembly context (`Component2.Transform2`, `GetCorrespondingEntity2`) chỉ kiểm chứng logic đọc mã nguồn và biên dịch trong phiên này; chưa chạy runtime mới trên một file `.SLDASM` ở branch V2 (trước đó PR #63 đã có probe trên `CircleFace-4316-validation.SLDASM`).

---

## 7. Đánh giá tính chính xác trên mặt xoắn

- **Chiều dài cung & phân bố hai đầu:** Đo trực tiếp bằng `Curve.GetLength3` và đảo ngược tham số `u` theo chiều dài cung thực (không nội suy đều theo tham số `u`, không dùng khoảng cách thẳng hay góc xoay đơn), bảo đảm chính xác ngay cả với cạnh spline (`type = 3006`).
- **Hướng trục ống 3D trên mặt xoắn:** Tại từng trạm đã tính, `VerifyExact` đánh giá lại điểm và pháp tuyến bề mặt thực (`EvaluateAtPoint` + fallback UV ở biên cắt tỉa), bù chung một lượng nâng `lift` theo pháp tuyến cục bộ cho tới khi mọi ống vượt qua cả ước lượng khoảng hở lấy mẫu (`EstimateFaceClearance`) lẫn phép giao cắt khối chính xác của nhân SolidWorks (`Operations2(SWBODYINTERSECT)`). Trên `4313` và `6866`, `lift` tự hội tụ ở `0.060–0.198 mm` với 0 va chạm mặt tấm.
- **Độ tin cậy kiểm tra va chạm 3D:** Đối chiếu trên 39 cặp liền kề của `4313` (`N=40 EqualPitch`), kết quả GJK (`TubeDistance.Between`) trùng khớp 100% với phép giao B-rep SolidWorks trên tất cả các cặp va chạm (`1-2`..`21-22`) và các cặp hở (`22-23`..`39-40`).

---

## 8. Những công việc còn cần Codex hoặc ChatGPT kiểm tra

1. Review diff commit `eb9d9e6` trên nhánh `feature/circle-face-custom-equal-both-ends-20261117` so với nhánh cơ sở `feature/circle-face-tube-layout-prototype-20261110`.
2. Xác nhận chính sách sản phẩm cho trường hợp `EqualPitch N=40` khi người dùng thật muốn ép đủ 40 ống ở `R3247 -> R4323.16` (giữ nguyên chặn vật lý như hiện tại vì giao thoa `-0.33 mm`, hay cân nhắc thêm tùy chọn overhang trong một PR sau).
3. Nếu cần test thêm môi trường Assembly thực tế (`Component2` xoay trong `.SLDASM`) với `EqualPitch` và chụp ảnh màn hình UI/Preview thực tế từ Task Pane trên SolidWorks đang mở của user trước khi bỏ trạng thái `[DRAFT]` của PR #64.
