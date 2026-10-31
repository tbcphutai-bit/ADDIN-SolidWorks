# Source và DLL

- Class thay đổi: `Commands/AutoFrameJoint/AutoFrameJointGhostPreview.cs`.
- Ghost source cuối: `BA65262E381E7409A9D667235ABB28A591FC8D42CE7769BF9ADE83FD3A8F598D`.
- Ghost baseline trước R62: `8C4218B6205309407565109C96B89E328449FABFE39D2567CA8B26D3228BE195`.
- Trạng thái chép source chính: `COPIED_HASH_VERIFIED`.
- DLL người dùng đã build trước lượt kiểm tra: SHA256 `47304C13BECFB755EC34D5BB6128BB7AA8B9C0CB3AF12A81930387BB09BBA470`, MVID `bd7b57a9-041e-42fd-94c7-a7a67d11c873`.
- R62 trong DLL đang cài: **chưa triển khai**. Không dùng MVID của harness để thay cho MVID Add-in.

Người dùng tự rebuild project đang dùng sau khi nhận source. Không cần tạo command mới hay thay cơ chế A/B. Khi thử, xác minh đường dẫn/MVID DLL được SOLIDWORKS load và log R62; không nạp diagnostic harness như DLL sản phẩm. Không có output path, post-build copy, project hoặc DLL/PDB nào được sửa bởi R62.


Đã compile độc lập lại chính sáu file ở source chính sau khi chép, không dùng file candidate thay thế. Chỉ Ghost thay đổi; năm file AFJ còn lại, project, DLL và PDB giữ nguyên hash trong thao tác chép. Bản Ghost trước R62 đã được sao lưu riêng trên máy.
