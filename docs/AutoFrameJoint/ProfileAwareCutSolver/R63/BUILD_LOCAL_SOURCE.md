# Dùng source R63

Đã compile độc lập lại đúng sáu source ở thư mục chính sau khi chép. Phép đặt Preview phía trên được giữ cùng công thức trong class Ghost, không cần thêm Compile entry. File helper local cũ, project, DLL/PDB và năm class AFJ khác giữ nguyên hash trong thao tác chép.

- Class hiện tại: `Commands/AutoFrameJoint/AutoFrameJointGhostPreview.cs`. SHA256 source đã kiểm: `E5921AACEB73F7A064013C04420A4E96E8D55F3E4FCB95D4F3E5D56D1B7D17E6`.
- Source local trước R63 có Preview `ABOVE_SELECTED_PAIR`; DLL trong log còn Preview `SIDE_BY_SIDE`. DLL cũ đã có guard R62 nhưng chưa có source R63 hoặc thay đổi Preview local mới.
- Chỉ chép class đã qualify vào source chính. Giữ nguyên project, output path, DLL/PDB, A/B selection và các command hiện có. File helper local cũ vẫn giữ nguyên; Ghost không cần dependency Compile mới.
- Người dùng tự rebuild cấu hình Add-in đang sử dụng và xác minh DLL/MVID được SOLIDWORKS load. Không dùng DLL diagnostic harness làm Add-in sản phẩm.
- Không coi compile độc lập là native test. Kết quả API và scope thực nằm riêng trong test matrix.
