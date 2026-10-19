# 07 — Trao đổi Phase 1 trong Visual Studio + GitHub Copilot

## Mục đích
Tài liệu nằm trên nhánh GitHub độc lập `docs/auto-frame-joint-phase1-20261019`. Bạn có thể mở cùng repository từ Visual Studio, checkout nhánh rồi yêu cầu GitHub Copilot đọc **file Markdown trong workspace** để trao đổi/rà soát ngay tại VS.

**Lưu ý:** Copilot trong Visual Studio và ChatGPT ở hội thoại này là **hai phiên/ngữ cảnh riêng**. Không tự động đồng bộ những tin nhắn đã trao đổi; chỉ những file đã checkout hoặc được đính kèm làm context mới đáng tin để Copilot đọc.

## Cách xem từ Visual Studio
1. Trong **Git Changes / Git Repository**, `Fetch` từ origin.
2. Tìm remote branch `origin/docs/auto-frame-joint-phase1-20261019`, checkout thành local branch có tracking.
3. **Khuyến nghị:** dùng clone/worktree riêng cho Auto Frame Joint nếu Check Hole đang làm dở; **không** mất các thay đổi local chưa commit khi chuyển nhánh.
4. Mở `docs/AutoFrameJoint/README.md`, đọc các file theo thứ tự; mở **GitHub Copilot Chat (Agent)**.
5. Gắn file đang đọc vào context qua tính năng `Add context`/file reference có trên phiên bản Copilot của bạn. Chỉ khi Copilot xác nhận thấy nội dung mới tiến hành đánh giá.
6. Không yêu cầu Copilot sửa `Commands/`, `SWAddin.cs`, `ADDIN.csproj`, UI hay API ở Phase 1.

## Prompt 1 — Cho Copilot đọc đầy đủ
```text
Hãy đọc toàn bộ file Markdown trong docs/AutoFrameJoint/ (README và 01–07).
Đọc trực tiếp nội dung trong workspace, không đoán nội dung nếu file chưa được mở.
Tóm tắt: (a) STANDARD từ KCC 26003, (b) OBSERVED chưa revalidate,
(c) HYPOTHESIS, (d) UNRESOLVED.
Không viết/đổi code C#, chỉ phân tích tài liệu. Trích path và heading hỗ trợ.
```

## Prompt 2 — Review tính logic và kiến trúc
```text
Đóng vai SOLIDWORKS Add-in C# geometry reviewer.
Review docs/AutoFrameJoint/01_GEOMETRY_SPEC.md đến 06_UNRESOLVED_CASES.md.
Tìm các mâu thuẫn trong hệ tọa độ Part/Assembly, chiều dài trục frame,
Winner/Loser theo phạm vi KCC, giao góc tùy profile và cách giữ lip/overhang.
Với mỗi vấn đề: severity, file/heading, ví dụ phản chứng,
cách sửa đề nghị, test case cần thêm.
Không thay đổi source code; các giả thuyết phải được gắn nhãn.
```

## Prompt 3 — Ghi báo cáo để chuyển cho người review
```text
Chỉ tạo/đề xuất Markdown review ở docs/AutoFrameJoint/REVIEW_FROM_VS.md.
Bao gồm các mục: DONE, ISSUES, QUESTIONS, PROPOSED_FIXES,
SOURCE_EVIDENCE, TEST_CASE_UPDATES và CURRENT_BRANCH/COMMIT.
Nêu rõ tài liệu nào thực sự đã đọc. Không claim code chạy/test passed.
Không chạm backend/UI, không tự merge branch.
Sau khi hoàn tất, báo lại các file đã sửa và commit SHA (nếu có commit).
```

## Mẫu báo cáo cho người review
```md
# VS Copilot review — Auto Frame Joint Phase 1
- Branch:
- Baseline SHA:
- Files reviewed:
- Confirmed (with source):
- Incorrect assumptions:
- Open geometry questions:
- Proposed changes:
- Tests requested:
- Files changed:
- Commit SHA (if created):
```

## Quy ước phối hợp
- **ChatGPT:** tổng hợp chứng cứ, thiết kế rules/contract, kiểm tra report, đề xuất chỉnh spec.
- **Codex:** khi có yêu cầu mới, viết C# geometry analyzer/backend và tests trên nhánh code riêng.
- **Antigravity:** thiết kế WinForms **chỉ** sau khi contract ổn định; bảo vệ event handlers và logic hiện có.
- **Copilot VS:** đọc, phản biện, tạo report/tài liệu khi được yêu cầu. Có thể dùng trực tiếp qua checkout branch; không được coi trao đổi với nó là đã tự động gửi về ChatGPT.

## Cách đưa phản hồi về GitHub
Commit riêng `REVIEW_FROM_VS.md` trên branch review hoặc tạo PR/comment nếu muốn có lịch sử thảo luận. **Không push trực tiếp lên main** và không trộn với nhánh `feat/check-hole-connection-groups-cg1` đang làm chức năng khác.
