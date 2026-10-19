# 03 — Profile topology recognition (profile-agnostic)

## Mục tiêu
Nhận diện đặc điểm tiết diện **từ B-rep Solid thực tế**, không dựa trên `ProfileCode`, tên file, tên feature hay nhìn từ camera. Các nhãn Web/Wall/Lip là **vai trò suy luận**, không phải nhãn CAD chắc chắn tồn tại.

## Quy trình
1. **Transform & units:** đưa body vào hệ Assembly; ghi nguồn Part, instance, configuration, transform và đơn vị.
2. **Detect candidate axis:** dùng các cặp cạnh/mặt đồng hướng, extrusion-like invariance và section-stability trên nhiều trạm. Không lấy axis X/Y/Z toàn cục.
3. **Find stable stations:** lấy lát cắt giữa thanh và thêm ít nhất hai trạm gần đó; tránh vùng miter, holes, slots, welding features, studs và notch.
4. **Section loops:** chuyển giao tuyến mặt phẳng với B-rep thành wire/closed loops (khi phù hợp), ghi rõ open section / thinned surfaces / multi-body. Đặt tolerance theo quy mô thiết kế.
5. **Region graph:** node = đoạn thẳng/cung/cạnh cong của section, edge = quan hệ kề nhau, tiếp tuyến, góc chuyển hướng, bán kính, chiều dày.
6. **Region roles:** đề xuất `WebCandidate`, `WallCandidate`, `LipCandidate`, `BendCandidate`, `Other`, mỗi tag có confidence và reference geometry.
7. **Cross-station validation:** kiểm tra roles tồn tại ổn định trên nhiều station; nếu tiết diện biến thiên (taper, locally welded, cutout) thì giảm confidence hoặc `UNSUPPORTED`.

## Hình học cần giữ
- Mặt trung hòa tham khảo / mặt ngoài / mặt trong và thickness phải được **xác minh**, không đoán từ bounding box.
- R bo không phải cạnh sắc: không xem arc chuyển tiếp như một flange rời.
- Lip/return có thể xuất hiện cả bên trong lẫn ngoài; lựa chọn cắt phụ thuộc joint constraints, **không** phụ thuộc nhãn Lip đơn độc.
- Đầu frame đã cắt không phải là mẫu tiết diện ổn định; phục dựng phôi trước cắt chỉ được phép khi xác định có đoạn tiết diện không đổi.
- Với sheet metal có nhiều sheet-metal bodies hoặc composite solids: reject/ask selection thay vì đoán.

## Bảng dữ liệu trích xuất đề xuất
```text
SectionTopology:
  componentOccurrenceKey
  axisOrigin, axisDirection, sectionStation
  normal, insideDirection, outsideDirection
  loops[{segments[{curveType, length, tangent, curvature, adjacentFaces}]}]
  regions[{label, segmentRefs, faceRefs, thicknessEstimate, confidence}]
  diagnostics[{code, severity, evidence}]
```

## Các trạng thái lỗi cần hỗ trợ
- `AXIS_NOT_UNIQUE`
- `NO_STABLE_CROSS_SECTION`
- `MULTI_BODY_AMBIGUOUS`
- `INSIDE_OUTSIDE_UNKNOWN`
- `PROFILE_SELF_INTERSECTION_OR_INVALID_BREP`
- `GEOMETRY_TOLERANCE_UNDEFINED`
- `REGION_LABEL_AMBIGUOUS`

## Phương pháp kiểm thử
Chọn ba station cách nhau đủ xa; dùng phép đăng ký/so sánh mặt cắt theo tọa độ cục bộ rồi kiểm tra sai lệch biên dạng, không chỉ diện tích. Phải kiểm thử ca quay, mirror, lỗ dọc, gờ lệch và profile có R; kết quả mỗi station có confidence và sai lệch.
