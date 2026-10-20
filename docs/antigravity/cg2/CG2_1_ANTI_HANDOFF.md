# CG2.1 — Quy trình mới: Anti chỉnh class trực tiếp, build thủ công đúng chỗ cũ

**QUY ĐỊNH MỚI NHẤT THEO YÊU CẦU CỦA NGƯỜI DÙNG — thay thế toàn bộ chỉ dẫn one-shot tự build cũ.**

Repository: tbcphutai-bit/ADDIN-SolidWorks  
Branch: feat/check-hole-cg2-part-relations  
PR #33: Draft / READY_FOR_MERGE=NO / không tự merge / không làm CG3.

## 1. Quyền sửa source và cách phối hợp

- Anti **được phép mở và chỉnh trực tiếp các class C# có liên quan trong source hiện tại**, bao gồm dán patch do ChatGPT cung cấp. Không tạo class trùng, không tạo bản sao DLL hay source mới để thay class gốc.
- Khi có lỗi thuật toán hoặc một patch phức tạp, gửi source/diff và log cho ChatGPT rà và viết phương án. Anti không tự mở rộng logic NG, CSV, marker hoặc các command ngoài phạm vi.
- **Không tự động build sau khi edit, save, pull, commit, chạy test script hoặc đổi tab.** Chỉ build thủ công khi người dùng yêu cầu rõ.
- Nếu IDE/agent đã bật tính năng auto build, auto compile, auto test on save, hãy tắt chức năng đó cho tác vụ này. Không tự thực hiện bước build bằng shell.
- Không tự chạy git pull/reset/checkout làm mất các thay đổi trong class. Trước khi đồng bộ branch, phải bảo đảm giữ lại phần source đang chỉnh.
- Commit/push chỉ khi được giao; giữ PR Draft.

## 2. Đường dẫn build chuẩn của ADDIN.csproj — KHÔNG ĐỔI

Project đang có sẵn các cấu hình sau:

| Configuration | Build thủ công (Build > Build/Rebuild trong Visual Studio) | DLL |
|---|---|---|
| Debug / x64 | Build chỉ khi được yêu cầu | C:\Tao moi\ADDIN.dll |
| Release / x64 | Build chỉ khi được yêu cầu | C:\Tao moi\ADDIN\bin\x64\Release\ADDIN.dll |

ADDIN.csproj có PostBuildEvent copy DLL/PDB của lần build thủ công về:

    C:\Tao moi\ADDIN.dll
    C:\Tao moi\ADDIN.pdb

**Đây là vị trí cũ, đúng yêu cầu.** Không truyền /p:OutputPath=...; không tắt PostBuildEvent khi thực sự được yêu cầu build thủ công. Không build Debug và Release liên tiếp nếu không cần thiết, vì cả hai có thể copy đè DLL cài đặt (lần build cuối thắng).

Lưu ý: vừa sửa class hoặc commit code không có nghĩa DLL đã thay đổi. Phải build thủ công mới cập nhật DLL. Kiểm tra thời gian build và SHA256 sau khi hoàn tất.

## 3. Script CG2_1_ONE_SHOT.ps1 đã đổi thành TEST-ONLY

- Không gọi MSBuild, Roslyn csc, git pull/fetch; không đổi OutputPath hoặc PostBuildEvent.
- Chỉ đọc source branch, SHA256 của DLL **đã được build thủ công**, sau đó tùy chọn chạy các chương trình test EXE **đã được biên dịch trước**.
- Không có EXE kiểm thử thì ghi NOT_TESTED, không giả PASS. Có -RunCad thì phải chỉ rõ runner EXE đã biên dịch và sandbox Pack-and-Go đã được cấp phép.
- Script không tự sửa, build, đồng bộ hoặc commit class nào.
- Vì Anti có thể sửa class trực tiếp, script chấp nhận trạng thái worktree DIRTY nhưng ghi DIRTY_PRESERVED; không xóa local changes.

Sau khi **được phép build thủ công** và xác minh đúng DLL cũ, có thể dùng script test-only:

    cd "C:\Tao moi\ADDIN"
    & ".\docs\antigravity\cg2\CG2_1_ONE_SHOT.ps1" -RepoRoot (Get-Location).Path -CandidateDll "C:\Tao moi\ADDIN.dll"

Nếu có EXE test đã được build riêng, chỉ định đường dẫn thật:

    & ".\docs\antigravity\cg2\CG2_1_ONE_SHOT.ps1" -RepoRoot (Get-Location).Path -CandidateDll "C:\Tao moi\ADDIN.dll" -OfflineHarnessExe "<actual-path>\Cg2TestHarness.exe" -RunnerExe "<actual-path>\Cg2LiveCadRunner.exe"

Không sao chép nguyên ví dụ đường dẫn <actual-path>. Dùng đường dẫn máy thực tế. Test Runner phải được biên dịch lại thủ công nếu mã kiểm thử thay đổi.

## 4. Chạy kiểm thử CAD (chỉ khi có yêu cầu và fixture an toàn)

- Dùng Assembly/Part Pack-and-Go **bản sao**, toàn bộ tham chiếu nằm trong sandbox đã xác nhận.
- Sandbox phải có file chấp thuận CG1_SANDBOX_OK.txt; không tự tạo file.
- Đóng các phiên SOLIDWORKS của người dùng theo cách bình thường trước khi chạy CAD runner; không tự taskkill.
- Test runner dùng AutoCreateMarkers=false. Không tuyên bố marker parity PASS khi chưa chạy riêng test đó.
- Chỉ dùng thực tế CG1 baseline DLL và CG2.1 candidate DLL **khác nhau**, được xác minh SHA256; chạy trên fixture đã có.
- Với kiểm thử riêng từng lỗ, giữ local manifest CG2_1_EXPECTED_SITES.tsv trong sandbox, dùng 9 cột tab cách nhau; occurrence/hole labels phải là giá trị thật.
- Nếu CSV byte khác, ghi FAIL đúng. Không ép PASS dựa vào canonical row signature.
- Các bài test chính: lỗ lệch, BREP 2/3 mm, Subassembly/rotation, Assembly lớn khi có bản sao cho phép, CG1/CG2 production verdict và raw CSV byte.

Ví dụ gọi script test-only sau khi đã biên dịch thủ công runner:

    $root = "D:\CG1-ISOLATED-TEST-COPIES"
    $cg1 = "C:\Tao moi\backup_cg1_release_snapshot\ADDIN.dll"
    $fixtures = @("$root\Fixture_OK.SLDASM", "$root\Fixture_NG_Offset.SLDASM")
    & ".\docs\antigravity\cg2\CG2_1_ONE_SHOT.ps1" -CandidateDll "C:\Tao moi\ADDIN.dll" -RunnerExe "<actual-path>\Cg2LiveCadRunner.exe" -BaselineDll $cg1 -SandboxRoot $root -Fixtures $fixtures -RunCad

Các đường dẫn fixture chỉ mang tính minh họa, kiểm tra chúng tồn tại trước khi dùng.

## 5. Kết quả và dừng

Anti ghi lại: class/dòng thực tế đã chỉnh, git diff, trạng thái built/not built, đường dẫn DLL, SHA256, từng test PASS/FAIL/NOT_TESTED và exit code thật. Giữ các log CAD gốc ở máy, chỉ đưa dữ liệu đã được phép và đã lược bỏ nội dung nhạy cảm vào report.

**Mặc định sau khi chỉnh class: CODE_UPDATED; BUILD=NOT_RUN (MANUAL ONLY); TEST=NOT_RUN.** Dừng và đợi người dùng yêu cầu Build nếu chưa có yêu cầu.

Không sửa dữ liệu sản xuất, không tự merge PR #33 và không làm CG3.
