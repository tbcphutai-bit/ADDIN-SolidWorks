# R61 — source chính và rebuild thủ công

Đã chép trực tiếp đúng file `Commands/AutoFrameJoint/AutoFrameJointGhostPreview.cs` đã qua kiểm thử vào source chính, có hash guard trước/sau và backup. SHA256 mới: `8C4218B6205309407565109C96B89E328449FABFE39D2567CA8B26D3228BE195`.

Không cần thêm file vào project. SelectionPage giữ bản R60 idle barrier SHA256 `27BE94F7A1D12FB25EB1C90733068259E9326BF6BEAC1340F41C1D0A5F13914E`; CommandEntry, SessionManager, lựa chọn A/B và các command khác không đổi. Không Build/Rebuild/F5/Deploy Add-in chính, không sửa project/output path/DLL/PDB.

DLL hiện tại có MVID `8c96c27a-617f-4ece-ad7a-71a9d6c49a1d`, SHA256 `35D8B1BB7E63CD1CA46244DE47F6E22C15B71313124CEA332EDC1F9B261E6B3A`; đây là DLL trước R61. Candidate API/PMP được thử trong một DLL chẩn đoán riêng, không thay DLL chính.

1. Kết thúc lệnh và đóng phiên SOLIDWORKS đang nạp DLL trước R61 trước khi tự rebuild.
2. Mở solution đang dùng, kiểm tra GhostPreview chứa `TerminalOverhangTrim` và `TerminalTrimWitnesses`; tự rebuild theo cấu hình hiện có, không đổi output/deployment.
3. Mở bản sao Assembly, chọn đúng A/B, Preview rồi OK. Không dùng file gốc để kiểm tra lần đầu.
4. Xác nhận các mép dư ngoài góc được xén, phần thuộc thân giữ nguyên; từng Cut vẫn Normal/Blind=t+0,10 mm, Sketch trên Face2 và ràng buộc hình học.

Hai bộ 2354 khác trạng thái đầu vào đã được thử riêng đủ bốn góc; 4332 được thử qua native PMP/idle thật cùng DLL/TaskPane nền. Kết quả này chưa phải xác nhận command AFJ nằm trong DLL R61 sau khi người dùng rebuild, chưa chứng minh visible Preview hoặc flatten/unflatten. Khi báo `ROLLBACK_UNVERIFIED`, không lưu CAD; kiểm tra Undo trên bản sao. Không coi phép thử Apply thành công là chứng minh rollback khi lỗi.
