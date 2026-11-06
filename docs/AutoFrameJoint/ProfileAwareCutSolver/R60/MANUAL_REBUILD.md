# Source R60 và rebuild thủ công

Đã chép trực tiếp bản đã thử vào source chính, có backup và hash guard. Thay đổi chỉ nằm trong hai file hiện có:

- `Commands/AutoFrameJoint/AutoFrameJointGhostPreview.cs`: native Cut trong đúng occurrence edit context, Assembly luôn active; không ActivateDoc3 sang Part độc lập ở từng Cut.
- `UI/AutoFrameJoint/AutoFrameJointSelectionPage.cs`: chờ native OnIdleNotify rồi post Apply và session cleanup ngoài callback native; giữ lifetime của owner dispatcher và cặp Frame đã chốt.

Không cần thêm helper vào project, không tạo command mới. CommandEntry và SessionManager hiện tại được dùng tiếp. Kết quả compile độc lập/API phải đối chiếu với đúng hash source trong báo cáo; compile độc lập không thay ADDIN.dll đang dùng.

DLL gửi trong log crash có MVID `9423a9df-74e5-4b35-befc-c68db4d08afd`, SHA256 `61463528AC731C2921F033C4088904F02C0DA417C8F148928D8013B8A9E774AB`. DLL này là bản R59, chưa chứa R60 cho tới khi người dùng tự rebuild/deploy theo cấu hình đang dùng.

1. Đóng phiên SOLIDWORKS đang nạp DLL cũ.
2. Tự rebuild solution/cấu hình hiện có, không thay output path hoặc chép DLL chẩn đoán vào Add-in.
3. Mở bản sao Assembly, chọn A/B, Preview rồi OK tạo Cut. Đối chiếu MVID DLL mới trong log.
4. Log mới cần có `AFTER_CLOSE_RETURN`, rồi `AFJ_R60_IDLE_GATE` xác nhận native idle trước `CLOSED_ACTION_BEGIN` và `APPLY_CALLBACK_BEGIN`; từng Cut có `FEATURECUT4_IN_ASSEMBLY_PART_EDIT`, sau đó `RESTORE_ASSEMBLY_EDIT`.

Đã qua API trực tiếp đủ bốn góc 2354 và ca native PMP/idle 4332 với DLL nền đầy đủ; managed callback vẫn được tách riêng. Không coi đó là bằng chứng người dùng đã thấy Preview hoặc DLL sau rebuild chắc chắn chạy qua PMP. Nếu writer báo `ROLLBACK_UNVERIFIED`, không lưu Part và kiểm tra Undo trên bản sao.
