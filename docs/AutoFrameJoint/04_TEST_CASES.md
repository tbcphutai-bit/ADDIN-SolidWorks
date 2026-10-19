# 04 — Case registry & repeatable CAD tests

## Nguyên tắc
Các bộ CAD/ảnh/PDF được gửi để **nghiên cứu mẫu cuối cùng (golden reference)**. Chưa có bộ kết quả tự sinh nào được xác nhận; mọi test mặc định **NOT_RUN**. Không suy diễn `PASS` từ việc STEP mẫu không giao thoa.

Dữ liệu gốc hiện ở ngoài repository: đăng ký nguồn thực và được phép sử dụng trước khi tiến hành tests. Không thêm dữ liệu CAD nội bộ vào repo public nếu chưa được chấp thuận.

## Registry v1

| Case ID | Nguồn theo cuộc nghiên cứu | Góc/hình học cần thử | Rule trọng tâm | Hiện trạng |
|---|---|---|---|---|
| AFJ-1618-EXT | `1618a.zip` | Các góc ngoài frame khác tiết diện | Web/Wall/Lip, overhang, orientation | NOT_RUN |
| AFJ-1618-MID | `1618a.zip` | Thanh giữa và các đầu tiếp giáp | Ngoại lệ góc T, clearance | NOT_RUN |
| AFJ-3615-T1 | `3615a.zip` T1 | Khung chữ nhật, một hướng dài | Short/Long và WIN/LOSE phạm vi phù hợp | NOT_RUN |
| AFJ-3615-T2 | `3615a.zip` T2 | Khung chữ nhật, đảo hướng dài/ngắn | Invariance khi xoay | NOT_RUN |
| AFJ-2216-W18 | `2216-02-lc-W18.STEP` / `2216a.zip` | Khung tam giác, góc khác 90° | Không tự áp dụng KCC ngoài phạm vi | NOT_RUN |
| AFJ-2216-W22-IMG | Ảnh `2216-02-lc-W22` | Góc thành ngắn được đánh dấu; đối chiếu che mặt cắt | Visual-only, cần CAD cho đo đạc | NOT_RUN |
| AFJ-2354-B5 | `2354 (4).zip` (Drawing B-5) | Khung phức tạp & các phần lắp | Tách frame riêng khỏi tổng Assembly | NOT_RUN |
| AFJ-2354-47-48 | DXF chi tiết 47 và 48 trong `2354 (4).zip` | 2 kiểu vát đảo hướng | Quan hệ mirror và coordinate transform | NOT_RUN |
| AFJ-2354-43-59-64 | DXF chi tiết 43, 59, 64 | Đầu vát ở các vùng khác nhau | Không ép cắt 1 plane | NOT_RUN |
| AFJ-KCC-26003 | PDF chuẩn KCC số 26003 | Panel chấn bốn phía / tấm cắt | Quy tắc thắng-thua có phạm vi | NOT_RUN |

**Lưu ý:** Những ghi nhận từ phân tích trước (ví dụ 47/48 gần đối xứng và các mặt đáy vát xiên) là *OBSERVED*: phải tính lại từ CAD/DXF gốc, báo định nghĩa sai số đo, không biến thành PASS.

## Bộ thử tối thiểu
1. **Axis:** cùng một Part đặt theo 3 cách xoay + mirror -> cùng length/axis local sau chuẩn hóa.
2. **Winner/Loser:** panel cạnh ngắn/dài + Drawing override + panel vuông + chưa rõ KCC scope.
3. **Profile:** tiết diện U, U có gờ, gờ bên ngoài & bên trong, profile lệch, R, holes/slots, multi-body.
4. **Cut region:** ít nhất 3 vùng cao độ (Web/Wall/Lip) và test overhang retained/removed.
5. **Topology:** 2 đầu cắt trái/phải, 47 vs 48 như bài thử phản chiếu.
6. **Safety:** same SLDPRT reused in 2 occurrences; no source mutation; rollback.
7. **Geometry comparison:** khoảng thể tích sai khác giữa model tái dựng từ phôi chưa cắt và model mẫu; không giới hạn ở silhouette.

## Workflow kiểm thử khi có CAD thật
1. Manifest từng nguồn: `sourceName, sourceHash, path, units, document/config, visible components, conversion details`.
2. Xác định **Part chính xác** và vai trò trong Assembly, giữ transform; đừng gộp Part vào một envelope duy nhất.
3. Lấy tiết diện ổn định trước đầu cắt. Chỉ **reconstruct uncut stock** khi tiết diện thật sự invariant dọc chiều dài.
4. Chạy solver thử nghiệm trên **copy** để sinh candidate geometry.
5. So sánh B-rep tại vùng góc (và ở các vùng gờ mỏng), ghi sai số, có visual overlay.
6. Lưu kết quả theo `AFJ-xxx`: `PASS | FAIL | INCONCLUSIVE | NOT_RUN`, thêm báo cáo sai khác cụ thể và chứng cứ.

## Report schema cho từng case
```yaml
case_id: AFJ-3615-T1
source_sha256: TODO
part_instances: TODO
assembly_config: TODO
station_locations: TODO
winner_loser_source: STANDARD/OVERRIDE/UNRESOLVED
solver_version: NOT_IMPLEMENTED
test_status: NOT_RUN
measured_tolerances: TODO
intersection_volume_mm3: null
symmetric_difference_mm3: null
boundary_deviation_mm: null
protected_regions: []
failure_reason: ""
evidence_images: []
```

**Không đặt tiêu chí mm cố định ở Phase 1** khi chưa có xác nhận dung sai sản xuất/model. Đặc biệt độ lệch xấp xỉ 0 không tự chứng minh gờ được giữ đúng thiết kế.
