# 01 — Geometry specification (Phase 1)

## 1. Bài toán
User chọn **hai Component/Part độc lập trong SOLIDWORKS Assembly**. Hệ thống phải phân tích hình học thực, xác định trục và vai trò hai Frame, tìm vùng góc và đề xuất phương án cắt theo tiết diện. Đầu ra Phase 1 là **đặc tả + phương án + kiểm thử**, tuyệt đối **không** là sửa Part.

Không được mặc định:
- mọi góc 90°, vát 45°, hoặc mọi mặt đều cắt theo một plane;
- có sẵn mã profile, profile luôn đối xứng, mọi gờ được giữ, hoặc Boolean subtract là kết quả cuối;
- Short Frame đồng nghĩa Winner trong mọi hình thức kết cấu;
- X/Y/Z Assembly thể hiện chiều dài dọc Frame.

## 2. Thuật ngữ chuẩn
- **Frame Axis**: phương tịnh tiến đặc trưng của tiết diện suốt chiều dài thanh.
- **Length**: khoảng bao có định nghĩa rõ trên Frame Axis, *khác* chiều dài danh nghĩa nếu đầu bị vát/gờ.
- **Long/Short**: so sánh hai Frame trên **cùng định nghĩa chiều dài**; cần tolerance và tie handling.
- **Winner/Loser (勝ち/負け)**: quan hệ *che mặt cắt chiều dày của thành*, không đồng nghĩa giữ nguyên toàn bộ Part.
- **Web / Wall / Lip / Bend / Overhang**: nhãn mô tả vùng hình học, không mặc định là feature tồn tại sẵn.
- **Joint Inside / Outside**: phía lòng panel / phía ngoài panel xác định từ hình học và chỉ dẫn thiết kế, không suy từ camera.
- **Reference Geometry**: hệ mặt/mép dùng làm chuẩn kích thước cắt, có nguồn gốc và hướng normal rõ.

## 3. Input contract
```text
AssemblyContext:
  documentId, configurationName, units, selectedComponentPaths[2]
  componentTransformsToAssembly[2]
  selectedBodies[2] (multi-body -> require explicit main body / fail)
  ownerPartPaths, resolved/suppressed/lightweight states
  sharedDefinitionUsage (one Part file used by multiple instances?)
GeometryContext:
  candidateFrameAxis[2], axisConfidence
  stableSectionStations[2], sections and topology confidence
  jointCornerCandidate(s), orientation, inside/outside reference
DesignContext:
  drawingOverride (optional, provenance required)
  kcc26003Applies: TRUE/FALSE/UNKNOWN
  allowedGap/fitTolerance (not silently defaulted to production value)
```

Tất cả hình học tính trong **Assembly coordinate system**: `pAsm = T_ComponentToAssembly * pPart`; ghi rõ cách inverse transform khi tạo feature về Part context. SOLIDWORKS API có hệ đơn vị nội bộ SI; adapter chuyển đổi đơn vị và dung sai rõ ràng, không hardcode mm như dữ liệu nội bộ.

## 4. Bài toán tổng quát
1. **Validate selection:** đúng hai component hữu hiệu, body truy cập được, không self-selection, không chỉ ảnh/chọn edge vô nghĩa.
2. **Identify axis:** phát hiện vùng tiết diện không đổi theo trục; lấy axis từ hình học với confidence và nguồn (cạnh dài/face parallelism/section comparison). Nếu lỗ, cắt đầu hoặc gờ làm nhiễu, bỏ vùng đầu ra khỏi mẫu đo.
3. **Measure & classify:** so chiều dài theo axis; nếu chênh lệch trong tolerance -> `EQUAL_OR_AMBIGUOUS`. Frame dài/ngắn là thông tin, không tự quyết định mọi vùng cắt.
4. **Locate joint:** tìm đầu gần giao, hai hướng trục, góc 3D, mặt phẳng panel và in/out; hỗ trợ góc không vuông, mirror và rotate.
5. **Extract topology:** mặt cắt ở ít nhất vài trạm độc lập; phân vùng Web, Wall, Lip, Bend theo vị trí tương đối và face adjacency.
6. **Resolve winner/loser:** dùng precedence trong `02_JOINT_RULES.md`. Không có rule áp dụng -> `NEEDS_REVIEW`.
7. **Generate candidates:** lập plane/surface/solid cut **candidate theo từng vùng**; phân giác chỉ là ứng viên cho Web trong điều kiện phù hợp.
8. **Check constraints:** gờ giữ được, mặt đầu che đúng yêu cầu, không cắt quá, không overlap ngoài dung sai, không ảnh hưởng các lỗ/stud hoặc component khác.
9. **Preview-only result:** danh sách cut candidate, giữ/cắt, căn cứ, mức tin cậy, diagnostics; **không tạo feature thực** trong Phase 1.

## 5. Output contract
```text
JointAnalysisResult:
  status = ANALYZED | NEEDS_REVIEW | UNSUPPORTED | ERROR
  measuredAxes, lengthDefinitions, LongShortClassification
  cornerAngle, intersection, insideOutsideState
  winnerLoserDecision + evidenceId + applicability
  perRegionCandidates[]:
    sourceComponent, regionLabel, action(CUT/PRESERVE/OFFSET/UNDETERMINED)
    cutterGeometryIntent, referenceFaces, targetEnd, tolerance, reasons
  ambiguityReasons[], warnings[], validationCases[]
  sourceModelsAndConfigurations[], geometryUnits, diagnostics
```

No `APPLY_SUCCESS` in Phase 1. `NEEDS_REVIEW` is **not** equal to `FAIL`; lack of manufacturing rule is not a geometric impossibility.

## 6. Safety for future SOLIDWORKS integration
- Nếu nhiều instance cùng trỏ về 1 `.SLDPRT`, không sửa định nghĩa dùng chung mà không có cơ chế tách component/config hoặc người dùng xác nhận.
- Dự kiến preview qua temporary bodies hoặc working copy; thử nghiệm không ghi đè file gốc.
- Tránh dùng tên face/edge ID mang tính ngẫu nhiên làm căn cứ lâu dài; dùng hình học, transform và fingerprint/topology conditions.
- Bảo vệ Feature Tree hiện có, mates, BOM, sheet-metal metadata, sketch, hole và rebuild.
- Không chọn nhầm mặt do orientation và mirrored instance; kiểm tra handedness của transform.

## 7. Tiêu chí để tiến hành Phase 2
Có specification review hoàn tất + test fixtures được gắn mã + danh sách các điều kiện dừng đã ký xác nhận. Không triển khai `Cut-Extrude` / `Cut with Surface` chỉ từ văn bản này.
