# Đưa source R53 vào DLL bằng quy trình hiện có

Agent đã cập nhật source chính và giữ nguyên DLL/PDB. Các bước Build/Deploy dưới đây do người dùng thực hiện thủ công. Project/output path và post-build copy hiện có được giữ nguyên.

## 1. Xác nhận đúng source

File cần có trong project đang Build:

`Commands/AutoFrameJoint/AutoFrameJointGhostPreview.cs`

SHA-256 của source giao, sau khi bỏ một dòng trắng cuối file; code tương đương bản đã chạy hai ca native harness (xem [EOF_EQUIVALENCE.json](EOF_EQUIVALENCE.json)):

`cc7fc64ab6b5a6d51cbf5e5ef6b59bd1d15f509d29af262d2e173a52cb92b68c`

File chứa các helper internal cùng namespace, vì vậy không cần thêm Compile Include hoặc sửa `.csproj`. Những thay đổi này đã được áp vào source chính trong phiên làm việc; Code Owner dùng checkout khác cần áp đúng diff của file này theo baseline của checkout đó.

## 2. Build và nạp thủ công

1. Đóng phiên SOLIDWORKS đang nạp Add-in và dừng Debug bằng thao tác của bạn.
2. Mở solution/project Add-in hiện tại trong Visual Studio. Giữ cấu hình x64, output và post-build copy đang sử dụng.
3. Build/Rebuild project bằng quy trình thường dùng. Kiểm tra kết quả Build và post-build copy đến DLL mà SOLIDWORKS đăng ký nạp.
4. Mở SOLIDWORKS và nạp Add-in. Kiểm tra đường dẫn DLL/MVID trong log cùng marker source R53 bên dưới.

DLL baseline chưa Build R53 có MVID `420dfcfc-a6ee-4861-ae82-f54d916e32e4`. Nếu vẫn nạp đúng binary baseline đó thì source R53 chưa nằm trong DLL đang chạy. Hai MVID của EXE harness độc lập không phải MVID cần đối chiếu với Add-in.

## 3. Marker cần thấy trong log

Khi tạo session Preview:

```text
[AFJ-R53] SOURCE=UNIFIED_U_ADJUSTED_BISECTOR_FACE_SKETCH plane_fallback=DISABLED MVID=...
```

Khi Apply qua OK:

```text
[AFJ-R53] APPLY_ENTRY writer=EXISTING_FACE_LINKED_SKETCH plane_fallback=DISABLED
```

Marker Web cho biết số cặp skin tương thích và Web đã chọn:

```text
[AFJ-R53] JOINT_WEB_CANDIDATES ... compatible_pairs=...
[AFJ-R53] JOINT_WEB_SELECTED ... evidence=NATIVE_AXIS_COPLANAR_SKINS_0_T
```

## 4. Kiểm tra ngắn sau khi nạp

Trên bản sao CAD, dùng A/B selection và nút Preview hiện có; sau khi quan sát đúng vùng giữ/cắt, dùng OK. Kiểm tra Feature Tree: các Sketch trên mặt có sẵn, trạng thái đủ ràng buộc, không có RefPlane AFJ mới. Với ca U đã hỗ trợ, dự kiến 4 Sketch và 4 Cut; số quan hệ 80 ở ca cơ sở hoặc 84 ở ca unequal đã kiểm thử. Các số này không áp dụng cho profile khác chưa có recipe.

Các trường hợp bị từ chối cần giữ nguyên thông báo `UNVERIFIED_RULE`/baseline-invalid. Production không chuẩn hóa 13 holder metadata như fixture harness. Khi log báo `ROLLBACK_UNVERIFIED`, giữ phiên/bản sao để kiểm tra và không Save kết quả chưa xác nhận.

## 5. Phạm vi chưa được xác nhận

Chưa có kết quả chạy trong DLL Add-in cho source R53 ở lượt bàn giao này. Display3/Preview nhìn thấy, độ mượt, mở Part sau Apply và hoàn tác metadata Assembly vẫn cần kiểm tra trong môi trường Add-in. W23 có nhiều Return/EdgeFlange chưa được native Apply; xem [JOINT_RULES.md](JOINT_RULES.md).
