# Code handoff: CHECK DRAWING — báo cáo dễ hiểu và hiện ký tự Property

Baseline: PR #58, branch diagnostic/goban-property-view-and-bom-column-probe-20261106. Áp dụng **vào các class cục bộ mới nhất**, không thay cả file. Bản này chưa build/kiểm thử SOLIDWORKS.

Mục tiêu: ưu tiên ký tự lấy từ Drawing; nếu Drawing API trả rỗng nhưng Raw liên kết hợp lệ và Property xác minh được thì hiển thị ký tự từ Property như ※単品搬入, GHI RÕ "Property (tham chiếu)" và giữ DisplayStatus=Warning. Chữ nhập tay thay công thức thì Link=NG. Không gán Property thành dữ liệu BOM.

## A — Commands/CheckDrawingBom.cs: thêm 2 property

Trong class DrawingPropertyLinkReportRow, thêm:

~~~csharp
public string DisplayOrReferenceValue { get; set; } = "";
public string DisplayOrReferenceSource { get; set; } = "";
~~~

## B — Commands/CheckDrawingBom.cs: helper chọn View nguồn chính xác

Thêm vào cùng class với TryGetVerifiedSheetGobanProperty, ở bên ngoài method:

~~~csharp
private static bool TryLocateGobanPropertyView(
    DrawingDoc drawing,
    Sheet intendedSheet,
    string intendedSheetName,
    string propertyView,
    out SolidWorks.Interop.sldworks.View selected,
    out string reason)
{
    selected = null;
    reason = "VIEW_SOURCE_UNVERIFIED";
    if (drawing == null || intendedSheet == null)
    {
        reason = "DRAWING_OR_SHEET_NULL";
        return false;
    }

    string intendedName, currentName;
    try
    {
        intendedName = (intendedSheet.GetName() ?? "").Trim();
        Sheet activeSheet = drawing.GetCurrentSheet() as Sheet;
        currentName = activeSheet == null ? "" : (activeSheet.GetName() ?? "").Trim();
    }
    catch
    {
        reason = "SHEET_NAME_API_ERROR";
        return false;
    }
    if (intendedName.Length == 0 || currentName.Length == 0 ||
        !string.Equals(intendedName, currentName,
            StringComparison.OrdinalIgnoreCase) ||
        (!string.IsNullOrWhiteSpace(intendedSheetName) &&
         !string.Equals(intendedName, intendedSheetName.Trim(),
             StringComparison.OrdinalIgnoreCase)))
    {
        reason = "SHEET_CONTEXT_MISMATCH";
        return false;
    }

    bool isDefault = string.Equals((propertyView ?? "").Trim(),
        "Default", StringComparison.OrdinalIgnoreCase);

    try
    {
        SolidWorks.Interop.sldworks.View sheetView =
            drawing.GetFirstView() as SolidWorks.Interop.sldworks.View;
        if (sheetView == null ||
            sheetView.Type != (int)swDrawingViewTypes_e.swDrawingSheet)
        {
            reason = "SHEET_VIEW_NOT_FOUND";
            return false;
        }

        // The first view is the sheet container. Scan only views on current sheet.
        SolidWorks.Interop.sldworks.View view =
            sheetView.GetNextView() as SolidWorks.Interop.sldworks.View;
        while (view != null)
        {
            if (view.Type != (int)swDrawingViewTypes_e.swDrawingSheet)
            {
                // Strict verification; IsViewOnSheet is permissive and must
                // not be used as sole source verification.
                Sheet ownerSheet = view.Sheet as Sheet;
                if (ownerSheet == null ||
                    !string.Equals((ownerSheet.GetName() ?? "").Trim(),
                        intendedName, StringComparison.OrdinalIgnoreCase))
                {
                    reason = "VIEW_SHEET_UNVERIFIED";
                    return false;
                }

                if (isDefault)
                {
                    // First model DRAWING VIEW is the default candidate.
                    // Never skip this view when ReferencedDocument is null.
                    selected = view;
                    reason = "DEFAULT_FIRST_VIEW";
                    return true;
                }

                if (string.Equals((view.GetName2() ?? "").Trim(),
                        (propertyView ?? "").Trim(),
                        StringComparison.OrdinalIgnoreCase))
                {
                    selected = view;
                    reason = "EXPLICIT_VIEW";
                    return true;
                }
            }
            view = view.GetNextView() as SolidWorks.Interop.sldworks.View;
        }
    }
    catch (Exception ex)
    {
        reason = "VIEW_LOOKUP_API_ERROR";
        LogDebug("[GOBAN PROPERTY VIEW ERROR] " + ex.Message);
        return false;
    }

    reason = isDefault
        ? "DEFAULT_VIEW_NOT_FOUND"
        : "EXPLICIT_VIEW_NOT_FOUND";
    return false;
}
~~~

Trong TryGetVerifiedSheetGobanProperty(), chỉ thay đoạn bắt đầu từ:

SolidWorks.Interop.sldworks.View targetView = null;

cho đến hết dòng:

LogDebug("[GOBAN PROP DIAG TARGET] matched view=\"" + propertyView + "\"");

bằng code sau. **Giữ nguyên toàn bộ đoạn kiểm tra ReferencedDocument, Model Path, Configuration và Get6 phía sau**:

~~~csharp
SolidWorks.Interop.sldworks.View targetView;
string viewChoice;
if (!TryLocateGobanPropertyView(
        drawing, sheet, sheetName, propertyView,
        out targetView, out viewChoice))
{
    source = viewChoice;
    LogDebug("[GOBAN PROPERTY SOURCE] status=UNVERIFIED reason=" +
        viewChoice + " sheet=\"" + sheetName +
        "\" propertyView=\"" + propertyView + "\"");
    return false;
}
LogDebug("[GOBAN PROPERTY SOURCE] status=SELECTED method=" +
    viewChoice + " sheet=\"" + sheetName +
    "\" propertyView=\"" + propertyView +
    "\" selected=\"" + (targetView.GetName2() ?? "") + "\"");
~~~

NOTE: Khi nhiều sheet sử dụng nguồn Property chung, cần xác minh thêm Document Properties. Không được tự gán nguồn khác; chưa xác nhận thì Warning. Chỉ chấp nhận "Default" nếu đó là giá trị đã xác nhận từ log hoặc cấu hình SOLIDWORKS tương ứng.

## C — Commands/CheckDrawingBom.cs: giá trị chữ + câu kết quả dễ hiểu

Trong AddGobanPropertyLinkReportRow() tìm dòng:

row.Note = string.Join(" | ", reasons);

Thay đúng dòng đó bằng:

~~~csharp
bool displayedWasRead =
    !drawingData.GobanDisplayUnavailable &&
    !string.IsNullOrWhiteSpace(drawingData.Goban);

if (displayedWasRead)
{
    row.DisplayOrReferenceValue = drawingData.Goban;
    row.DisplayOrReferenceSource = "Drawing API";
}
else if (row.LinkStatus == DrawingBomCheckStatus.OK && gotProperty)
{
    // Reference value only; not proof of actual Drawing display.
    row.DisplayOrReferenceValue = propValue;
    row.DisplayOrReferenceSource = "Property (tham chiếu)";
}
else if (row.LinkStatus == DrawingBomCheckStatus.NG &&
         !string.IsNullOrWhiteSpace(drawingData.GobanLinkRawExpression) &&
         !drawingData.GobanLinkRawExpression.TrimStart().StartsWith(
             "$", StringComparison.Ordinal))
{
    row.DisplayOrReferenceValue = drawingData.GobanLinkRawExpression;
    row.DisplayOrReferenceSource = "Raw Text (nhập tay)";
}
else
{
    row.DisplayOrReferenceValue = "(Chưa đọc được)";
    row.DisplayOrReferenceSource = "Chưa xác minh";
}

var readable = new List<string>();
if (row.LinkStatus == DrawingBomCheckStatus.OK)
    readable.Add("Liên kết 合番 đúng công thức.");
else if (row.LinkStatus == DrawingBomCheckStatus.NG)
    readable.Add("Liên kết 合番 sai hoặc đã bị nhập tay. Công thức cần có: " +
        "$PRPSHEET:\"合番\".");
else
    readable.Add("Chưa xác minh được công thức gốc trong ô 合番.");

if (gotProperty)
    readable.Add("Đã đọc giá trị từ Property của model tham chiếu (" +
        propSource + ").");
else
    readable.Add("Chưa đọc được Property từ đúng View/Configuration (" +
        propSource + ").");

if (!hasBomValue)
    readable.Add("BOM không có cột 合番 riêng nên chưa thể đối chiếu Property với BOM.");
else if (row.PropertyStatus == DrawingBomCheckStatus.NG)
    readable.Add("Property không trùng giá trị 合番 của BOM.");
else if (row.PropertyStatus == DrawingBomCheckStatus.OK)
    readable.Add("Property trùng giá trị 合番 của BOM.");
else
    readable.Add("Chưa đủ dữ liệu để đối chiếu Property và BOM.");

if (row.DisplayStatus == DrawingBomCheckStatus.NG)
    readable.Add("Chữ đọc trực tiếp từ Drawing khác Property.");
else if (row.DisplayStatus == DrawingBomCheckStatus.OK)
    readable.Add("Đã xác minh chữ trực tiếp từ Drawing.");
else if (row.DisplayOrReferenceSource == "Property (tham chiếu)")
    readable.Add("Chữ ở cột Giá trị chữ lấy từ Property; " +
        "chưa xác minh trực tiếp chữ Drawing.");
else
    readable.Add("Chưa xác minh trực tiếp chữ đang hiển thị trên Drawing.");

row.Note = string.Join(" ", readable);
~~~

Giữ nguyên đoạn so sánh có thể chuyển comparison.Status sang NG và toàn bộ ApplyGobanTemplateLinkAudit. Không dùng giá trị Model/BOM thay vào comparison.DrawingValue ở Sheet 2.

## D — Commands/ExcelDrawingCheckExporter.cs: thay hàm xuất Sheet 3

Thay toàn bộ method WritePropertyLinkSheet hiện tại bằng 2 method dưới đây. Giữ nguyên các helper Excel sẵn có. Sheet 1/2 không sửa.

~~~csharp
private static string GobanFriendlyStatus(DrawingBomCheckStatus status)
{
    switch (status)
    {
        case DrawingBomCheckStatus.OK: return "Đúng";
        case DrawingBomCheckStatus.NG: return "Sai";
        default: return "Chưa xác minh";
    }
}

private static void WritePropertyLinkSheet(
    dynamic sheet, List<DrawingCheckItemResult> items)
{
    const int columns = 11;
    var rows = new List<object[]>();
    var statusRanges = new Dictionary<string, List<string>>();

    rows.Add(new object[] {
        "KIỂM TRA LIÊN KẾT PROPERTY — GIÁ TRỊ VÀ NGUỒN DỮ LIỆU",
        null, null, null, null, null, null, null, null, null, null
    });
    rows.Add(new object[] {
        "No.", "Hạng mục", "Giá trị chữ", "Nguồn chữ",
        "Công thức gốc Drawing", "Property (Model)", "BOM",
        "Liên kết", "Property ↔ BOM", "Chữ Drawing", "Giải thích kết quả"
    });

    int group = 0;
    if (items != null)
    {
        foreach (DrawingCheckItemResult item in items)
        {
            if (item == null || item.PropertyLinkRows == null ||
                item.PropertyLinkRows.Count == 0)
                continue;

            group++;
            string file = string.IsNullOrWhiteSpace(item.DrawingPath)
                ? "(Chưa có Drawing)" : Path.GetFileName(item.DrawingPath);

            rows.Add(new object[] {
                "COMPONENT INo " + item.BomItemNumber +
                " | " + (item.Component ?? "") + " | " + file +
                " | Kết quả tổng: " + GobanFriendlyStatus(item.Status),
                null, null, null, null, null, null, null, null, null, null
            });

            int index = 0;
            foreach (DrawingPropertyLinkReportRow field in item.PropertyLinkRows)
            {
                index++;
                int excelRow = rows.Count + 1;

                rows.Add(new object[] {
                    index, field.FieldName, field.DisplayOrReferenceValue,
                    field.DisplayOrReferenceSource, field.DrawingRaw,
                    field.PropertyValue, field.BomValue,
                    GobanFriendlyStatus(field.LinkStatus),
                    GobanFriendlyStatus(field.PropertyStatus),
                    GobanFriendlyStatus(field.DisplayStatus), field.Note
                });

                // Maintain status colors using original enum values.
                AddStatusRange(statusRanges,
                    field.LinkStatus.ToString(), "H" + excelRow);
                AddStatusRange(statusRanges,
                    field.PropertyStatus.ToString(), "I" + excelRow);
                AddStatusRange(statusRanges,
                    field.DisplayStatus.ToString(), "J" + excelRow);
            }
            rows.Add(new object[columns]);
        }
    }

    if (group == 0)
        rows.Add(new object[] {
            "Không có ô 合番 dạng 1×2 trong phạm vi kiểm tra liên kết.",
            null, null, null, null, null, null, null, null, null, null
        });

    object[,] data = new object[rows.Count, columns];
    for (int r = 0; r < rows.Count; r++)
        for (int c = 0; c < columns; c++)
            data[r, c] = rows[r][c];

    // All Japanese text and Raw expressions are literal strings.
    sheet.Range["C:G"].NumberFormat = "@";
    sheet.Range[sheet.Cells[1, 1],
        sheet.Cells[rows.Count, columns]].Value2 = data;

    dynamic title = sheet.Range["A1:K1"];
    title.Merge();
    title.Font.Bold = true;
    title.Font.Size = 14;
    title.Font.Color = Rgb(255, 255, 255);
    title.Interior.Color = Rgb(31, 78, 120);
    sheet.Rows[1].RowHeight = 28.0;

    dynamic header = sheet.Range["A2:K2"];
    header.Font.Bold = true;
    header.Font.Color = Rgb(255, 255, 255);
    header.Interior.Color = Rgb(54, 95, 145);
    sheet.Rows[2].RowHeight = 27.0;

    double[] widths = { 7, 13, 25, 29, 37, 27, 35, 13, 18, 17, 85 };
    for (int c = 1; c <= columns; c++)
        sheet.Columns[c].ColumnWidth = widths[c - 1];

    sheet.Range["A1:K" + rows.Count].VerticalAlignment = XlTop;
    sheet.Range["K1:K" + rows.Count].WrapText = true;
    sheet.Range["H1:J" + rows.Count].HorizontalAlignment = XlCenter;

    for (int r = 3; r <= rows.Count; r++)
    {
        string label = Convert.ToString(rows[r - 1][0] ?? "");
        if (!label.StartsWith("COMPONENT INo ", StringComparison.Ordinal))
            continue;
        dynamic groupTitle = sheet.Range["A" + r + ":K" + r];
        groupTitle.Merge();
        groupTitle.Font.Bold = true;
        groupTitle.Interior.Color = Rgb(221, 235, 247);
        sheet.Rows[r].RowHeight = 24.0;
    }

    ApplyBatchedStatusColors((object)sheet, statusRanges);
}
~~~

## D2 — Tên nguồn và câu chữ chính xác (2 chỉnh sửa nhỏ)

Trong TryGetVerifiedSheetGobanProperty, sau khi đã chọn targetView và trước khi đọc Property, lấy tên View thật:

~~~csharp
string verifiedSourceViewName = (targetView.GetName2() ?? "").Trim();
~~~

Sau đó chỉ đổi dòng gán source khi Get6 thành công từ

~~~csharp
source = "SHEET_VIEW:" + propertyView + "|CONFIG:" +
    (string.IsNullOrWhiteSpace(scope) ? "(DOCUMENT)" : scope);
~~~

sang:

~~~csharp
source = "SHEET_VIEW:" + verifiedSourceViewName + "|CONFIG:" +
    (string.IsNullOrWhiteSpace(scope) ? "(DOCUMENT)" : scope);
~~~

Như vậy ghi tên View thực, không báo nhầm "SHEET_VIEW:Default" như thể Default là tên View.

Trong AddGobanPropertyLinkReportRow thay các nhãn chưa rõ nghĩa (không sửa điều kiện logic):

~~~csharp
row.BomValue = hasBomValue ? bomDirect : "(BOM không có cột 合番 riêng)";
row.PropertyValue = gotProperty ? propValue : "(Chưa xác minh được Property)";
~~~

**Kiểm tra bản thân Sheet.Properties trước khi tin nguồn Default.** Chỉ ghi Property (tham chiếu) khi View/Model/Config đã được xác minh và raw link hợp lệ. Chưa xác minh thì giữ "(Chưa đọc được)".

## E — expected sample outputs (not runtime evidence)

1. Raw linked correctly; Property = ※単品搬入; Drawing display API empty; BOM has no 合番:
   - Giá trị chữ = ※単品搬入; Nguồn chữ = Property (tham chiếu)
   - Công thức gốc = $PRPSHEET:"合番"
   - Liên kết = Đúng; Property ↔ BOM = Chưa xác minh; Chữ Drawing = Chưa xác minh.
2. Manual raw text = aa; Drawing display = aa:
   - Giá trị chữ = aa; Nguồn chữ = Drawing API;
   - Liên kết = Sai and old field comparison must remain NG.
3. Valid raw; actual displayed text matches Property:
   - Nguồn chữ = Drawing API; Chữ Drawing = Đúng.
4. Valid raw; no Property and no Drawing text:
   - Giá trị chữ = (Chưa đọc được); Chữ Drawing = Chưa xác minh.

## F — Anti acceptance and safety

- Verify any local changes ahead of PR #58 first. Never overwrite full classes; copy only these sections with precise anchors.
- If multi-sheet sources do not match the selected active sheet, return Warning instead of choosing another source. In cases where ownership of the 1x2 table is unknown, do not claim the Property reproduces the rendered text.
- No Save/Rebuild/Activate/Close Drawing; no UI, csproj, Part or BOM grid mutation; preserve SafeCloseDocument and Ura/Omote.
- Keep all statuses in the old two sheets unchanged, including NG priority; only sheet 3 gets human-friendly labels.
- Run git diff --check and static consistency checks; DO NOT claim runtime success without user build.
- Push an implementation branch, create Draft PR, report SHA, diff and testing limits. Do not merge.
