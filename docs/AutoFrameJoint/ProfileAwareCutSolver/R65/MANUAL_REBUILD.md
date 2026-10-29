# Đưa source vào Add-in đang dùng

Class `Commands/AutoFrameJoint/AutoFrameJointGhostPreview.cs` đã được chép trực tiếp vào source chính. SHA256 cần có là `9CC782AC11E8C17E641514B7B7538A28E5F1C77DEA0F3D09CC0387FAE7284B89`. Không cần thêm command, helper, Compile item hoặc thay output path.

1. Lưu công việc đang làm rồi đóng phiên SOLIDWORKS đang nạp Add-in.
2. Trong Visual Studio, người dùng tự rebuild project hiện tại bằng cấu hình đang dùng; giữ nguyên output/post-build/deployment của project.
3. Mở lại SOLIDWORKS và kiểm DLL load đúng đường dẫn đang dùng. MVID phải khác DLL trước R65 (`bc0de01f-891f-4f01-aa42-96c84a32172f`). MVID đổi riêng nó không chứng minh mọi source khớp; đối chiếu hash class trước build.
4. Thử trên bản sao: chọn đúng A/B, xem Preview P1/P2 rồi OK. Đối chiếu `NATIVE_CUT_VERIFIED`, `APPLY_VERIFIED`, số Cut và gap; chưa ghi PASS nếu chỉ hiện Preview. Các trace `[AFJ-R65]` mô tả contour/Bend khi quy tắc tương ứng được dùng.

R65 đã được compile độc lập và thử direct API bằng class thật; chưa chạy DLL sản phẩm sau rebuild của người dùng. Các phiên API sở hữu đã đóng bản sao không lưu và thoát. Không Merge PR.
