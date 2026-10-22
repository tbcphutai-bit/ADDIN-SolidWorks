# Source, DLL và project output

> **Lịch sử snapshot đầu R52:** Báo cáo này giữ kết quả tại thời điểm trước khi người dùng Build/test. Log native mới và bản sửa Caption được đối chiếu tại [follow-up](07_NATIVE_LOG_AND_CAPTION_FIX.md); dùng follow-up để đọc trạng thái hiện tại.

## DLL được kiểm tra

DLL và PDB ở output mà log người dùng nêu được đọc trực tiếp bằng Mono.Cecil và parser PE/MSF, không load/thực thi Add-in, không mở project build.

| Mục | Kết quả |
| --- | --- |
| MVID DLL hiện tại | `2f4e359e-bffe-4f74-9441-891bb4c77b12` — khớp log R52 |
| SHA256 DLL | `16020db76d451ded49aa8e58d3a8a2959a83f76a045f9ee75e73da56e0ad6872` |
| SHA256 PDB | `8700870373db4a262a35bcef6fe0a60f6001b913d84fe8229b755a18d6b65ecb` |
| CodeView GUID / age | `c0f93be0-cf0e-4082-ae81-72431d3a8218` / `1` |
| PDB MSF stream GUID / age | Cùng GUID và age; **PDB khớp DLL** |
| R51 marker | Có chuỗi `[AFJ-R51] source=profile-skin-cutter` trong `TryShowCutCandidate` |
| Solver | Có call `AfjJointSolver.Solve` |
| Luồng UI/backend | Có các method/call của SelectionPage, CommandEntry, SessionManager, GhostPreview, solver, Display3 và cleanup |

251/251 method trong 49 type AFJ khớp IL chuẩn hóa với `Final2354Smoke.exe` R51, biên dịch từ sáu class local trước R52. So sánh loại NOP, chuẩn hóa branch/member/operand và không sử dụng metadata token số để kết luận. Đây là bằng chứng logic IL tương ứng; không phải kiểm tra byte DLL giống nhau hoặc chứng minh toàn bộ Add-in giống source. Chi tiết ở `evidence/INSTALLED_BINARY_AUDIT.json` và `INSTALLED_IL_COMPARISON.json`.

Parser native PDB đọc được document entries nhưng không thu được hash source từ các entry này; source checksum được đánh dấu `NOT_AVAILABLE`. Không coi checksum rỗng là mismatch. Source local Ghost trước R52 có SHA256 `2670c383134d76e77db103ad52ee70aa49d03f95287ff1c9c14305eda896070b`, đúng biến thể R51 đã áp dụng. Các class còn lại và DLL được snapshot riêng.

## Project và deployment

XML project hiện tại liệt kê đủ sáu class AFJ và launcher hiện có. `Debug|x64` ghi trực tiếp vào **output cũ trùng đường dẫn DLL trong log**. `TRACE` có trong DefineConstants. Debug/PDB là `full`. `PreBuildEvent` và `PostBuildEvent` hiện rỗng: **không có lệnh copy post-build** trong project này. AnyCPU/Release có output khác; không suy ra các cấu hình đó đang được người dùng sử dụng chỉ từ XML.

Registry HKLM của class `ADDIN.SwAddin` trỏ CodeBase tới chính DLL output đó; tên file khác hoa/thường không tạo một target Windows khác. Chỉ đọc registry, không đăng ký COM lại. Dữ liệu xuất bản dùng ký hiệu `<EXISTING_ADDIN_OUTPUT>` thay cho đường dẫn máy. Cấu hình MSBuild nhập ngoài/XML chưa được evaluate bằng Build/MSBuild; đối chiếu MVID và IL của DLL thực là bằng chứng trực tiếp hơn.

Một lần đọc module list của PID giao diện mới thấy không có `ADDIN.dll` trong danh sách trả về. Không kết luận Add-in chắc chắn chưa load từ riêng phép liệt kê đó; đây không phải PID đã cung cấp log và không phải bằng chứng thay cho managed assembly inventory. Không inject hoặc tự load Add-in để thử.

## Trước và sau R52

Trước R52, DLL tương ứng logic AFJ R51 local. Sau R52, source local đã có sửa callback/log, nhưng DLL/PDB giữ nguyên SHA/MVID. Vì vậy **source R52 mới chưa được Build vào DLL**, theo đúng giới hạn nhiệm vụ. `SOURCE_PROTECTION.json` xác nhận chỉ bốn `.cs` được sửa; project, binary và các class không liên quan không đổi. Không chép nguyên class PR #42 đè lên baseline local.

Người dùng cần dùng quy trình Build thủ công của mình sau review; kiểm tra marker `stage=COMMAND_OPEN` / `stage=SHOW source=R52` để phân biệt DLL đã chứa R52. Không tự khẳng định MVID mới sẽ là giá trị nào trước khi Build.
