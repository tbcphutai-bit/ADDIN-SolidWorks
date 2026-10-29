# R63 — Profile hệ thống có trạng thái suppression khác nhau

Ngày 2026-10-29. Lỗi trong log mới được xác định ở guard lịch sử ngay sau native Cut đầu của cặp **4332 fr1a × fr2**. DLL đã có R62; đây không phải kết luận solver không tạo được Cut. [Nguyên nhân và sửa](CAUSE_AND_FIX.md).

Source R63 đủ gate trong **3/3 lượt**, **7 cặp**, **69 Cut mới**. Số lượt/cặp có thể gồm regression lặp trên các bản sao riêng; không cộng vào 43 Feature Cut mẫu ban đầu. Đây là kiểm thử candidate chạy trong SOLIDWORKS, gồm scope native PMP/idle nếu test matrix ghi nhận; chưa phải xác nhận DLL người dùng sau rebuild.

| Lượt | Cặp | Cut | Sketch cuối | Kết quả |
|---|---:|---:|---:|---|
| NEW_PAIR_PMP | 1 | 12 | 16 | PASS_QUALIFIED_CANDIDATE_API_ONLY |
| SEQUENTIAL_DIRECT | 2 | 22 | 30 | PASS_QUALIFIED_CANDIDATE_API_ONLY |
| FOUR_CORNERS_2354 | 4 | 35 | 51 | PASS_QUALIFIED_CANDIDATE_API_ONLY |

Xem [test matrix](TEST_MATRIX.json), [dữ liệu Cut API](NATIVE_CUTS.csv), [Sketch mới](NATIVE_SKETCHES.csv), [bằng chứng trạng thái](STATE_EVIDENCE.json), [cách dùng source](BUILD_LOCAL_SOURCE.md) và [giới hạn](LIMITATIONS.md). Trạng thái chép source: **COPIED_HASH_VERIFIED**. Không tự Build/Deploy, không sửa project, không lưu CAD gốc và không Merge.
