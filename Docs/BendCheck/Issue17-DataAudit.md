# Issue #17 — audit dữ liệu 18 Punch–Holder

Giải quyết phần công cụ và bàn giao của [Issue #17](https://github.com/tbcphutai-bit/ADDIN-SolidWorks/issues/17).
Baseline Phase 3D: `283a7a77a3280dda6cc36b82412608cf518cb58b` (PR #15).
Branch riêng: `feature/check-chan-issue17-data-audit`; Draft PR base là
`feature/check-chan-phase3d-sheet-envelope`. Không thay source runtime, UI,
Check Min, collision solver hoặc branch Phase 3D.1.

`tools/BendToolAudit/BendToolAudit.csproj` tạo CLI .NET Framework 4.8 độc lập,
không cần SOLIDWORKS/interop. Project link trực tiếp các source managed Phase
3B.1–3D; không chép lại thuật toán hình học, chữ ký hoặc solver.

## Chạy và giữ dữ liệu riêng

```powershell
& $MSBuild tools/BendToolAudit/BendToolAudit.csproj /t:Rebuild `
  /p:UseSharedCompilation=false /p:OutputPath=$PrivateBin /v:minimal

& "$PrivateBin/BendToolAudit.exe" `
  --placement $PrivatePlacementConfig `
  --dim-directory $CompanyDimDirectory `
  --output $NewPrivateOutputDirectory `
  --code-revision $CodeCommit

# Sau khi người kiểm định hoàn tất các hồ sơ bên ngoài:
& "$PrivateBin/BendToolAudit.exe" `
  --placement $PrivatePlacementConfig `
  --dim-directory $CompanyDimDirectory `
  --intake $PrivateIntake `
  --authorities $AdministeredPublicAuthorityFile `
  --output $AnotherNewPrivateOutputDirectory `
  --code-revision $CodeCommit
```

Nếu DIM dùng manifest riêng, thêm `--dim-manifest $PrivateDimManifest`.
Authority phải được chỉ định tường minh và trùng `ReviewAuthoritiesPath` của
placement config do công ty quản trị. Không tự tin cậy file khóa nằm cạnh intake.
File authority chỉ chứa public keys; private key luôn thuộc quy trình ký bên ngoài.

Exit `0`: đủ bằng chứng cho **cả 18** ở saved context đã kiểm tra. Exit `2`:
đã tạo báo cáo có blocker; đây là kết quả audit bình thường. Exit `1`: lỗi lệnh,
output không hợp lệ hoặc input đổi trong lúc audit; đọc diagnostic và chạy lại.
Một dao đủ điều kiện không làm các dao khác được duyệt.

Output phải là thư mục mới/rỗng, ngoài mọi Git checkout và ngoài thư mục CAD,
DIM hoặc workbook nguồn. CLI từ chối ghi đè, output qua junction/symlink và output
trong checkout. Nó chỉ đọc input; tạo file mới trong output đã chỉ định. Ledger
SHA đọc lại config, source, manifests, authority, evidence, artifact và model;
provider refresh cũng được kiểm tra trước khi xuất. Báo cáo là quan sát tại thời
điểm đọc file, cần chạy lại sau mọi thay đổi; không có live CAD lease ngoài app.

## File bàn giao

| File local | Công dụng |
| --- | --- |
| `PRIVATE-audit.json` | Đủ 18 dòng, source path/SHA và claimed revision, exact selection keys, per-field diagnostics, Excel references và evidence observations. |
| `18-tool-status.csv` | Bảng 18 dòng để lọc/sắp xếp; UNKNOWN giữ nguyên, không đổi NULL thành PASS. |
| `PRIVATE-operator-checklist.md` | Phiếu kiểm định riêng cho từng dao, đầy đủ các mục còn thiếu và chỗ điền artifact/đo/reviewer. |
| `PRIVATE-four-mappings.json` | Theo dõi riêng AMD-STD-004/008/117/452, kể cả sau khi giải quyết mapping. |
| `PRIVATE-selection-inventory.json` | Exact label/root INSERT/block, instance/entity keys, source closure, supported status và inventory topology. Không suy ra vai trò kim loại. |
| `PRIVATE-*-UNFILLED.json` | Mẫu intake, datum, mapping/material, seating, axial và limit applicability; đo đạc/approval/signature để trống. |
| `PRIVATE-status-summary.json` / `.md` | Bảng trạng thái 18 dòng chỉ giữ ID/enum/reason codes. Bản rút gọn này vẫn là dữ liệu công ty và cũng phải giữ ngoài GitHub. |

Toàn bộ kết quả chạy với nguồn công ty đều giữ ngoài GitHub, kể cả bảng trạng
thái rút gọn. Raw CAD/Excel, source/manifests, report, checklist, signed evidence,
keys và thông tin người vận hành không commit. Các mẫu đã xuất là tài liệu riêng;
không tự gắn vào config và không tự ký. PR chỉ chứa công cụ, hợp đồng, hướng dẫn
và kiểm thử tổng hợp; không có bảng trạng thái thực hoặc baseline công ty.

## Đóng từng nhóm bằng chứng theo thứ tự

1. **Revision nguồn.** Lấy đúng revision theo yêu cầu, kể cả hậu tố `(2)/(3)`
   khi có. Hash file unsuffixed và một tên gần giống chưa chứng minh revision.
   `ExpectedRevisions` ghi nhãn yêu cầu do người vận hành đối chiếu; không lấy từ
   basename/mtime. Mỗi `SourceRevisionRecord` cần đúng Key (`Master`, `Workbook`,
   `DIM:<ToolId>`), RequestedRevision, SourceSha256, SelectionManifestSha256 và
   chữ ký purpose `ToolSourceRevision`. DIM preset dùng hash của chuỗi
   `CompanyDimensionedV1`; nếu dùng manifest, hash bytes manifest đó.
2. **Danh tính và mapping.** Đối chiếu TEXT label/handle, Punch root INSERT/block,
   Holder root INSERT/block, entity instance paths và vai trò bản vẽ với dao thật.
   Bốn mã đặc biệt phải có quyết định độc lập. Sửa manifest riêng rồi lập lại các
   hash/bằng chứng phụ thuộc. Không chọn gần nhất, theo vị trí giấy hoặc INSERT order.
   Lựa chọn tham chiếu chưa duyệt có `MAPPING_SELECTION_UNREVIEWED`; chưa chọn
   được có `MAPPING_UNRESOLVED`. Đủ proof `ToolHolderMapping` mới đóng mục review.
3. **Vật liệu Holder.** Phân loại toàn bộ selected entity keys đúng một lần:
   contour `Solid`/`Hole`/`Island` có parent, hoặc exclusion
   `Decorative`/`Centerline`/`Reference`/`Annotation`. Kiểm tra lỗ thật, đảo và
   mọi component trên chi tiết. Giữ nguyên bounded SPLINE và mọi boundary band;
   SPLINE đọc được không đồng nghĩa biên kim loại đã duyệt. Ký
   `HolderMaterialRule` cho definition đúng binding và policy.
4. **Datum.** Đo tip Punch, mount Punch và mount Holder riêng biệt; xác lập
   local frame, working direction và đơn vị mm trong source frame đã pin.
   Hoàn tất `ToolDatumEvidence` theo hợp đồng Phase 3A đã có, gồm artifact SHA,
   field payload, người duyệt, thời điểm và expiry. Không lấy bbox/điểm thấp nhất/origin.
5. **Seating và kiểm định vật lý.** Đo HolderSourceToPunchSource, mặt tựa và
   hướng; transform phải đưa holder mount tới punch mount trong tolerance
   `1e-5 mm` hiện có. Xác nhận đúng cặp dao, revision và gá trên máy thật, lưu
   biên bản/ảnh/phương pháp/dụng cụ/sai số. Người được quyền physical inspection
   mới đặt `PhysicalInspectionConfirmed` và ký `ToolPairSeating` với intent external.
   Chữ ký mô phỏng không làm cặp dao vật lý được chấp nhận.
6. **Dài và vị trí theo trục chấn.** Đo từng đoạn Punch và từng đoạn Holder độc
   lập: LocalStartMm/LocalEndMm, ToolAxialOriginMm, ToolAxisSign `+1/-1`,
   AxialUncertaintyMm và mọi gap giữa các đoạn. Xác nhận
   `UniformProfileOverListedSegments`; taper/thay đổi profile cần contract khác.
   Dải lắp là `origin + sign * [start,end]`, giữ đúng frame và uncertainty.
   Không dùng chiều dài vô hạn, không chép chiều dài Punch sang Holder, không
   dùng workbook để lấp chiều dài thiếu. Giới hạn uncertainty hiện có `0..0.1 mm`
   là điều kiện validator, không phải số liệu đo mặc định.
7. **Context và XY thực.** Xuất full `PlacementModelCapture` managed từ pipeline
   Phase 3D, gồm bodies, native topology và bends; giữ nguyên SnapshotId,
   UpdateStamp, configuration, body/feature/work frames. Lưu capture và Part
   ngoài Git; intake ghi ModelCapturePath, ModelSourcePath, ModelSourceSha256.
   External `OfflineModelCapture` proof pin hash **bytes capture + Part bytes +
   MachineId + SetupId**. CLI rehydrate DTO và chạy lại nguyên
   `PrismaticEnvelopeCompiler`; không tin certificate/ready flags trong JSON.
   Với mỗi bend, hoàn tất hai `ToolAxialSetupRecord` cùng contact/approach/mirror,
   pin đúng envelope/profile/material/seating/frame/model; ký riêng
   `PunchAxialSetup` và `HolderAxialSetup`. Các record axial Phase 3D đã chứa XY
   contact/approach approval; không thay bằng một điểm tiếp xúc tự suy đoán.
   Capture đã lưu cần ký lại và audit lại nếu Part/config/setup thay đổi.
8. **Excel áp dụng.** Report giữ các record nguồn để kiểm định tra cứu, gồm
   sheet/locator/object SHA/kind/unit/value; không tự gán family cho 18 mã.
   Với giới hạn max bend length đã được kỹ thuật duyệt cho đúng dao/máy/setup,
   điền `LimitApplicationRecord`, pin workbook/object/value/unit/seating payload,
   ký `ToolLimitApplicability`. Reader cũ vẫn giữ `ReferenceOnly`; wrapper audit
   ghi approval riêng, không thay các flag hoặc thuật toán cũ.
   `DeepBendClearanceExample` luôn chỉ là tham khảo; cần engineering contract
   khác để làm machine limit. Không dùng 80/105 mm hoặc 4200/6000 mm làm default.

Mọi proof dùng `ToolReviewProof`, RSA >=2048 / PKCS#1 SHA256,
`ToolReviewVerifier.SignatureContent`, authority purpose/expiry và artifact bytes
như Phase 3B.1. Các payload mới ở `AuditContracts.cs` và
`OfflineModelReader.Payload`. Quy trình review/ ký bên ngoài phải gọi đúng
canonical helper; `ToolEvidenceCanonical` length-prefix và invariant round-trip
numbers, không phải SHA của JSON tự serialize. CLI không sinh khóa, chữ ký,
reviewer, ngày duyệt hoặc phép đo. Các numeric template để NULL.

## Ý nghĩa trạng thái

| Trạng thái | Điều kiện và giới hạn |
| --- | --- |
| `UNKNOWN` | Còn thiếu/không khớp source, geometry hoặc reviewed pair; xem toàn bộ Issues, không chỉ một lỗi đầu. |
| `READY_GEOMETRY_ONLY` | Master/DIM revision records được chấp nhận, Punch region và cặp geometry theo hợp đồng cũ đủ cho kiểm tra hình học. Có thể là mô phỏng; không cho phép bỏ blocker vật lý/setup. |
| `READY_PRODUCTION_STATIC_CHECK` | Đủ source external reviews, mapping/material/datum/physical pair, signed saved model context + continuous envelope, independent finite Punch/Holder setups với cùng XY cho mọi bend, và verified machine/setup limit applicability. Đây là readiness để bắt đầu static check, chưa chạy collision query. |

Source parsed ≠ region valid ≠ physical pair attested ≠ static collision clear ≠
full process feasible. Before/during/after, V-die, process motion và thứ tự chấn
chưa được xác minh bởi công cụ này. `VerifiedNative3D` không được tự cấp. Những
native shapes ngoài subset Phase 3D giữ `CODE_UNSUPPORTED` / UNKNOWN.

Thay source/manifest/geometry/material/pose, trùng instance hoặc record, sai purpose,
artifact đổi, signer không còn quyền hoặc hết hạn đều không được nâng readiness.
Mọi missing issue có `Code`, `Field`, `Diagnostic`, `RequiredEvidence` trong report
riêng. Sửa record rồi chạy lại; không sửa kết quả report để đánh dấu hoàn tất.

## Xác minh

```powershell
& $MSBuild Tests/BendToolProfiles/ToolProfiles.Tests.csproj /t:Rebuild `
  /p:UseSharedCompilation=false /p:OutputPath=$PrivateTestBin /v:minimal
& "$PrivateTestBin/ToolProfiles.Tests.exe"
python Tests/BendToolProfiles/audit_company_report.py $PrivateOutput $PrivateExpectedBaseline
```

Suite kế thừa toàn bộ Phase 3D, thêm fixture synthetic về transition đầy đủ,
simulation/production, sai source/revision/manifest/profile/frame/pose, missing
length, duplicate, source artifact freshness, signing purpose/expiry, Excel
scope và output privacy. Test signer chỉ tồn tại trong harness synthetic, không
phải chức năng CLI hoặc approval cho dao công ty. Company regression kiểm tra
18 ID thực theo contract và baseline riêng do công ty cung cấp; không nhúng dữ
liệu/trạng thái công ty vào test. Baseline JSON có `Version: 1`, `Tools` là 18
object theo thứ tự `CompanyToolIds.All`, mỗi object chứa ToolId cùng các field
trạng thái kỳ vọng; `SeparateMappingFollowups` là danh sách cần theo dõi riêng.
Giữ baseline ngoài checkout. Xem [xác minh công cụ](Validation-Issue17.md).
