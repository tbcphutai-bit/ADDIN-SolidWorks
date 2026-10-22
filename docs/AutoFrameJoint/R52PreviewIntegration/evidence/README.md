# Bằng chứng R52

- `INSTALLED_BINARY_AUDIT.json`: MVID, danh sách AFJ method, member calls, string markers và hash IL chuẩn hóa. Đọc file, không load Add-in để thực thi.
- `INSTALLED_IL_COMPARISON.json`: tóm tắt 251/251 method tương ứng R51 harness; không là PDB source checksum.
- `PDB_SOURCE_VERIFICATION.json`: CodeView và native PDB GUID/age, SHA DLL/PDB; hash source từ PDB vẫn NOT_AVAILABLE.
- `PROJECT_OUTPUT_AUDIT.json`, `REGISTRATION_AUDIT.json`: snapshot read-only; output máy được thay bằng ký hiệu nhưng giữ thông tin trùng target.
- `SOURCE_BINARY_BASELINE.json`, `SOURCE_PATCH_AUDIT.json`, `SOURCE_PROTECTION.json`: hash trước, proposal và sau áp dụng; bốn file source local, project/DLL/PDB được bảo vệ.
- `SOURCE_INVARIANTS.json`: kiểm tra giữ R50 UI protection và các helper hình học R51; không là native test.
- `APPLICATION_EVENTS.json`: Application Error/WER đã loại đường dẫn. Causal callback/process-exit mapping còn UNVERIFIED.
- `LOADED_MODULE_AUDIT.json`: chỉ snapshot danh sách process modules; không phải managed assembly inventory đầy đủ.
- `LOCAL_MANAGED_RESULTS.json`, `BRANCH_MANAGED_RESULTS.json`, `*_managed_flow.log`: kiểm tra mô phỏng managed, không gọi SOLIDWORKS. Log ghi nhãn rõ từ dòng đầu.
- `DETACHED_COMPILE.json`: source/executable hashes và exit status cho hai explicit-source compile, không main Add-in build.
- `BinaryAudit.cs.txt`, `audit_pdb_source.py.txt`, `ManagedUiFlowHarness.cs.txt`: công cụ độc lập, binary/dependency không được upload. Script PDB cần thay placeholder input bằng đường dẫn private phù hợp trước khi dùng.
- `local_*.patch`: zero-context patch theo baseline local, đã áp dụng; Git cần `--unidiff-zero`. Chỉ review trước khi tái sử dụng. Không áp dụng lên baseline khác chỉ vì tên class giống nhau.

File manifest dùng SHA256 UTF-8 chuẩn hóa LF, bỏ BOM; không hash chính manifest. Không CAD, DLL/PDB, executable, dump hoặc đường dẫn cá nhân được publish. `USER_LOG_SUMMARY.json` do transcript cung cấp, không giả mạo thành raw log native.
