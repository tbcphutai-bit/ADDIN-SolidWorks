# Source chính và rebuild thủ công

Đã chép trực tiếp **duy nhất** `Commands/AutoFrameJoint/AutoFrameJointGhostPreview.cs` vào source chính, sau khi kiểm tra hash baseline và sao lưu bản cũ. SHA256 bản mới: `036844470F9C779F1A0392EC0AB25ECF6D3BBAFA2B366645C460DD39064E9525`.

Không cần thêm class vào project: các helper internal nằm trong file GhostPreview hiện có. A/B selection và ba class điều phối/UI còn lại của source chính không bị sửa. Branch PR giữ nguyên UI sẵn có của branch; các khác biệt comment/nhãn và cache message so với source chính không nằm trong thay đổi R59.

Source chính và source PR đều compile thành công bằng compiler độc lập từ sáu file Auto Frame Joint. Đây không phải Build project Add-in. Công cụ thử API nạp DLL chẩn đoán riêng, không nạp thay ADDIN.dll.

DLL đang cài **chưa có R59**: SHA256 `F22D57943B23D6FF17B6A17EB68E95424A604EAB9F082C180B8162A034FB44C7`, MVID `472fc9a6-5298-4ceb-a696-86bc9a0658fe`. DLL, PDB, project và đường dẫn deploy giữ nguyên.

1. Kết thúc lệnh hiện tại; đóng phiên SOLIDWORKS đang nạp DLL cũ trước khi tự rebuild.
2. Mở solution đang dùng; kiểm tra file GhostPreview có `ORTHOGONAL_U_PER_FOLD_NORMAL_BLIND` và `AfjPerFoldSheetPlanner`.
3. Tự rebuild theo cấu hình/đường dẫn deploy hiện có. Không thay output path hoặc copy thêm DLL chẩn đoán vào Add-in.
4. Mở bản sao Assembly để chạy Preview rồi OK tạo Cut. Kiểm tra log DLL/MVID mới, Normal Cut, Blind=t+0,10 mm và Sketch gắn mặt có ràng buộc.

Kết quả API bốn góc đã đạt với đúng source hash trên. Việc nút PMP, hiển thị Preview và DLL do người dùng rebuild hoạt động trong phiên sử dụng thực tế vẫn cần đối chiếu sau rebuild. Không ghi PASS cho các bước giao diện chưa chạy. Các phép thử native Apply đo khoảng 16,6–32,6 giây/góc ở 2354; đây không phải bằng chứng đã giải quyết toàn bộ vấn đề chậm/đơ.

Nếu writer báo `ROLLBACK_UNVERIFIED`, không lưu CAD; kiểm tra Undo/bản sao. Không dùng kết quả thành công để suy ra hoàn tác khi lỗi đã được xác minh.
