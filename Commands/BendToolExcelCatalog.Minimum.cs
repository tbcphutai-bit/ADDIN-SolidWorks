using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace ADDIN.Commands
{
    internal sealed class BendMinimumRule
    {
        private readonly BendToolCatalogRecord row;
        internal BendMinimumRule(BendToolCatalogRecord source)
        {
            row = source;
            ApplicableMaterials = new ReadOnlyCollection<string>(BendToolExcelCatalog.Json()
                .Deserialize<string[]>(row.Text("applicable_material_codes_json")).ToList());
        }
        internal string Id => row.Id;
        internal string MaterialGroup => row.Text("material_group");
        internal ReadOnlyCollection<string> ApplicableMaterials { get; private set; }
        internal double ThicknessMm => row.Number("thickness_mm").Value;
        internal double? VOpeningMm => row.Number("v_opening_mm");
        internal double? DieOutsideWidthMm => row.Number("die_outside_width_mm");
        internal double? InsideRadiusMm => row.Number("inside_radius_mm");
        internal double? MinimumA => row.Number("min_a_mm");
        internal double? MinimumB => row.Number("min_b_mm");
        internal double? MinimumC => row.Number("min_c_mm");
        internal string SourceSheet => row.Text("source_sheet");
        internal int? SourceRow => row.Number("source_row") is double n ? (int?)n : null;
        internal string Notes => row.Text("notes");
    }

    // This evaluates the minimum dimension table only. The caller must supply a
    // classified local section and OUTSIDE dimensions; no CAD geometry is guessed.
    internal enum BendMinimumStatus { MeetsMinimum, BelowMinimum, InsufficientData, Conditional, GroovedAccepted }

    internal sealed class BendMinimumCheckResult
    {
        internal BendMinimumStatus Status { get; private set; }
        internal BendMinimumRule Rule { get; private set; }
        internal string Message { get; private set; }
        internal BendMinimumCheckResult(BendMinimumStatus status, string message, BendMinimumRule rule = null)
        { Status = status; Message = message; Rule = rule; }
    }

    internal static class BendMinimumCheck
    {
        // Numerical roundoff only, not a manufacturing tolerance or thickness range.
        private const double RoundoffMm = 0.0000001;
        internal static bool NearlyEqual(double a, double b) => Math.Abs(a - b) <= RoundoffMm;
        private static bool Positive(double? n) => n.HasValue && n.Value > 0 && !double.IsInfinity(n.Value) && !double.IsNaN(n.Value);
        internal static BendMinimumCheckResult Evaluate(BendToolCatalogSnapshot catalog, string materialCode,
            double thicknessMm, string sectionShape, double? outsideA, double? outsideB, double? outsideC, double bendAngleDeg)
        {
            string shape = (sectionShape ?? "").Trim().ToUpperInvariant();
            if (shape == "U") shape = "C";
            if (!new[] { "L", "Z", "C" }.Contains(shape))
                return new BendMinimumCheckResult(BendMinimumStatus.InsufficientData, "Cần xác định kích thước thuộc biên dạng L, Z hoặc C trước khi so sánh.");
            if (double.IsNaN(bendAngleDeg) || double.IsInfinity(bendAngleDeg) || Math.Abs(bendAngleDeg - 90) > 0.000001)
                return new BendMinimumCheckResult(BendMinimumStatus.InsufficientData, "Bảng tối thiểu này áp dụng cho góc chấn 90 độ; chưa có dữ liệu cho góc này.");
            if (!Positive(thicknessMm))
                return new BendMinimumCheckResult(BendMinimumStatus.InsufficientData, "Chưa có chiều dày vật liệu hợp lệ.");
            var candidates = catalog.FindMinimumRules(materialCode, thicknessMm);
            if (candidates.Count != 1)
                return new BendMinimumCheckResult(BendMinimumStatus.InsufficientData, candidates.Count == 0
                    ? "Không có dữ liệu tối thiểu cho vật liệu và chiều dày này; không nội suy."
                    : "Có nhiều quy tắc phù hợp; cần chọn điều kiện áp dụng trước.");
            var rule = candidates[0];
            // The user clarified the manufacturing check with actual sections:
            // terminal L flanges use A, Z webs use B, C webs use C independently.
            // Neighboring A values cannot hide or gate a failing B/C dimension.
            double? limit = shape == "L" ? rule.MinimumA : shape == "Z" ? rule.MinimumB : rule.MinimumC;
            double? measured = shape == "L" ? outsideA : shape == "Z" ? outsideB : outsideC;
            string dimension = shape == "L" ? "A" : shape == "Z" ? "B" : "C";
            if (!Positive(limit) || !Positive(measured))
                return new BendMinimumCheckResult(BendMinimumStatus.InsufficientData, "Thiếu " + dimension + " phủ bì hoặc giới hạn " + dimension + " trong Excel.", rule);
            string comparison = dimension + " phủ bì = " + measured.Value.ToString("0.###", CultureInfo.InvariantCulture) + " mm";
            string minimum = dimension + " tối thiểu " + limit.Value.ToString("0.###", CultureInfo.InvariantCulture) + " mm trong Excel (biên dạng " + shape + ").";
            if (measured.Value < limit.Value - RoundoffMm)
                return new BendMinimumCheckResult(BendMinimumStatus.BelowMinimum, comparison + " nhỏ hơn " + minimum, rule);
            return new BendMinimumCheckResult(BendMinimumStatus.MeetsMinimum, comparison + " đạt " + minimum, rule);
        }
    }

    internal static partial class BendToolExcelCatalog
    {
        private const string MinimumSheetName = "01_Be_toi_thieu";

        private static BendToolCatalogSnapshot LoadMinimumSheet(Workbook book, string path, string sourceHash, CancellationToken cancellation)
        {
            Sheet sheet = book.Sheet(MinimumSheetName);
            if (sheet.LastRow > 40000) throw new InvalidDataException("Sheet tối thiểu vượt 40000 dòng.");
            int headerRow = 0;
            for (int row = 1; row <= sheet.LastRow; row++)
                if (!sheet.IsMergeFollower("C" + row) && Equals(sheet.At("C" + row), "Dày t\n(mm)") && Equals(sheet.At("D" + row), "AL"))
                {
                    if (headerRow != 0) throw new InvalidDataException("Sheet tối thiểu có hai khung dữ liệu.");
                    headerRow = row;
                }
            if (headerRow == 0) throw new InvalidDataException("Không tìm thấy tiêu đề khung tối thiểu. Dùng sheet 01_Be_toi_thieu với các cột B:S.");
            var headers = new Dictionary<string, string> {
                { "B", "*" }, { "C", "Dày t\n(mm)" }, { "D", "AL" }, { "E", "SUS" }, { "F", "ST" },
                { "G", "Cu / Bs / RBs" }, { "H", "V\n(mm)" }, { "I", "Rộng ngoài\ncối (mm)" }, { "J", "ir\n(mm)" },
                { "K", "AL / Cu / Bs / RBs" }, { "N", "SUS" }, { "Q", "ST" }
            };
            foreach (var header in headers)
                if (!Equals(sheet.AtMapped(header.Key + headerRow), header.Value))
                    throw new InvalidDataException("Tiêu đề " + MinimumSheetName + "!" + header.Key + headerRow + " không đúng: " + header.Value.Replace('\n', ' '));
            for (int index = 0; index < 9; index++)
                if (!Equals(sheet.At(((char)('K' + index)).ToString() + (headerRow + 1)), new[] { "A", "B", "C" }[index % 3]))
                    throw new InvalidDataException("Khung tối thiểu cần ba cột A/B/C cho từng nhóm vật liệu.");

            int firstRow = headerRow + 2, lastRow = firstRow - 1;
            while (lastRow + 1 <= sheet.LastRow && !sheet.IsMergeFollower("C" + (lastRow + 1)) && MinimumPresent(sheet.At("C" + (lastRow + 1)))) lastRow++;
            if (lastRow < firstRow) throw new InvalidDataException("Khung tối thiểu chưa có dòng chiều dày.");
            for (int row = lastRow + 1; row <= sheet.LastRow; row++)
                if (!sheet.IsMergeFollower("C" + row) && MinimumPresent(sheet.At("C" + row)))
                    throw new InvalidDataException("Khung tối thiểu có dòng rỗng xen giữa hoặc dữ liệu thừa tại C" + row + ". Các dòng chiều dày phải liên tiếp.");

            var tables = Editable.Keys.ToDictionary(k => k, k => new List<Dictionary<string, object>>(), StringComparer.Ordinal);
            tables.Add("minimum_source_rows", new List<Dictionary<string, object>>());
            var thicknesses = new List<double>();
            int bindings = 0;
            var minimumRules = tables["minimum_rules"];
            for (int row = firstRow; row <= lastRow; row++)
            {
                cancellation.ThrowIfCancellationRequested();
                var numbers = new Dictionary<char, double?>();
                for (char column = 'C'; column <= 'S'; column++)
                {
                    string address = column.ToString() + row;
                    object value = column >= 'H' && column <= 'J'
                        ? sheet.AtMinimumShared(address, firstRow, lastRow) : sheet.AtMinimumUnmerged(address);
                    double? number = MinimumNumber(value, address, column >= 'D' && column <= 'G' || column == 'J');
                    numbers.Add(column, number);
                    if (number.HasValue) bindings++;
                }
                if (!numbers['C'].HasValue) throw new InvalidDataException("Thiếu chiều dày tại C" + row + ".");
                double thickness = numbers['C'].Value;
                if (thicknesses.Any(t => BendMinimumCheck.NearlyEqual(t, thickness)))
                    throw new InvalidDataException("Chiều dày trùng trong sheet tối thiểu tại C" + row + ".");
                thicknesses.Add(thickness);
                object marker = sheet.AtMinimumUnmerged("B" + row);
                if (MinimumPresent(marker) && !Equals(marker, "*")) throw new InvalidDataException("Cột B chỉ dùng dấu * hoặc để trống.");
                // Retain all source parameters, including t12, which currently has
                // a die opening but no minimum rule. A blank limit never becomes zero.
                var sourceRow = new Dictionary<string, object>(StringComparer.Ordinal) {
                    { "id", "minimum-source-row-" + (row - firstRow + 1) }, { "revision", 1 }, { "review_status", "needs_review" },
                    { "source_sheet", MinimumSheetName }, { "source_row", row }, { "thickness_mm", thickness },
                    { "material_column_al_mm", numbers['D'] }, { "material_column_sus_mm", numbers['E'] },
                    { "material_column_st_mm", numbers['F'] }, { "material_column_cu_bs_rbs_mm", numbers['G'] },
                    { "v_opening_mm", numbers['H'] }, { "die_outside_width_mm", numbers['I'] }, { "inside_radius_mm", numbers['J'] },
                    { "source_validation_star", Equals(marker, "*") }
                };
                for (int group = 0; group < 3; group++)
                    for (int dimension = 0; dimension < 3; dimension++)
                        sourceRow.Add(new[] { "al_cu_bs_rbs", "sus", "st" }[group] + "_min_" + ((char)('a' + dimension)) + "_mm", numbers[(char)('K' + 3 * group + dimension)]);
                tables["minimum_source_rows"].Add(sourceRow);
                for (int group = 0; group < 3; group++)
                {
                    char a = (char)('K' + 3 * group), b = (char)(a + 1), c = (char)(a + 2);
                    if (!numbers[a].HasValue && !numbers[b].HasValue && !numbers[c].HasValue) continue;
                    string materialGroup = new[] { "AL_CU_BS_RBS", "SUS", "ST" }[group];
                    var members = new List<string>();
                    if (group == 0)
                    {
                        // The shared AL/Cu/Bs/RBs block has restricted source rows.
                        // Nonblank D/G selects its material scope, never a B formula.
                        if (numbers['D'].HasValue) members.Add("AL");
                        if (numbers['G'].HasValue) members.AddRange(new[] { "CU", "BS", "RBS" });
                        if (members.Count == 0) throw new InvalidDataException("Dòng " + row + " có giới hạn AL/Cu/Bs/RBs nhưng D và G đều trống; chưa xác định vật liệu áp dụng.");
                    }
                    else members.Add(materialGroup);
                    minimumRules.Add(new Dictionary<string, object>(StringComparer.Ordinal) {
                        { "id", "minimum-bend-" + materialGroup.ToLowerInvariant().Replace('_', '-') + "-t" + thickness.ToString("R", CultureInfo.InvariantCulture).Replace('.', 'p') },
                        { "revision", 1 }, { "review_status", "needs_review" }, { "material_group", materialGroup },
                        { "applicable_material_codes_json", Serialize(members) }, { "thickness_mm", thickness },
                        { "v_opening_mm", numbers['H'] }, { "die_outside_width_mm", numbers['I'] }, { "inside_radius_mm", numbers['J'] },
                        { "min_a_mm", numbers[a] }, { "min_b_mm", numbers[b] }, { "min_c_mm", numbers[c] }, { "machine_code", null },
                        { "source_sheet", MinimumSheetName }, { "source_row", row },
                        { "source_cells_json", Serialize(new { thickness = "C" + row, a = a.ToString() + row, b = b.ToString() + row, c = c.ToString() + row }) },
                        { "material_columns_mm_json", Serialize(new { AL = numbers['D'], SUS = numbers['E'], ST = numbers['F'], CU_BS_RBS = numbers['G'] }) },
                        { "source_validation_star", Equals(marker, "*") },
                        { "notes", "A/B/C là kích thước phủ bì. Kiểm riêng từng kích thước: L dùng cột A; Z dùng cột B; C (U) dùng cột C theo cách người dùng đã xác nhận. Đọc số trực tiếp, không tính lại bằng lượng trừ." }
                    });
                }
            }
            if (minimumRules.Count == 0) throw new InvalidDataException("Sheet tối thiểu chưa có giới hạn A/B/C nào.");
            var contracts = new Dictionary<string, string>(StringComparer.Ordinal) {
                { "source_policy", Serialize(new { layout = "minimum_sheet_v1", sheet = MinimumSheetName, capabilities = new[] { "minimum_dimensions" }, values = "direct_numeric_cells", blank = "unknown_not_zero", material_scope_shared_block = "D_nonblank_AL_G_nonblank_CU_BS_RBS", material_column_interpretation = "raw_source_values_not_used_to_recalculate_limits" }) },
                { "minimum_geometry_contract", Serialize(new { dimension_frame = "outside_envelope_local_section", angle_deg = 90, L = new[] { "A" }, Z = new[] { "B" }, C = new[] { "C" }, U = new[] { "C" }, bc_condition = "compare_each_dimension_to_its_own_column", classification = "free_terminal_L_A_interior_opposite_turns_Z_B_same_turns_C_C", thickness_match = "exact_nominal_no_interpolation" }) },
                { "maximum_geometry_contract", Serialize(new { automatic_check_enabled = false, availability = "not_in_this_workbook" }) },
                { "units", Serialize(new { length = "mm", blank = "unknown_not_zero" }) }
            };
            string catalogHash = Hash(Encoding.UTF8.GetBytes(Serialize(new { tables, contracts })));
            return new BendToolCatalogSnapshot(path, sourceHash, catalogHash, "minimum_bend_excel", 3,
                tables, contracts, 0, bindings, "minimum_sheet_v1");
        }

        private static bool MinimumPresent(object value) => value != null && !Equals(value, "");
        private static double? MinimumNumber(object value, string address, bool nonnegative)
        {
            if (!MinimumPresent(value)) return null;
            string label = MinimumSheetName + "!" + address;
            double n = Number(value, label);
            if (n < 0 || !nonnegative && n == 0 || n > 1000000000)
                throw new InvalidDataException(label + (nonnegative ? ": số phải từ 0 đến 1.000.000.000 mm." : ": số phải lớn hơn 0 và không quá 1.000.000.000 mm."));
            return n;
        }

        private sealed partial class Sheet
        {
            internal object AtMinimumUnmerged(string address)
            {
                int[] target = Position(address);
                foreach (string merge in merges)
                {
                    string[] ends = (merge ?? "").ToUpperInvariant().Split(':');
                    if (ends.Length != 2) throw new InvalidDataException("Vùng merge Excel không hợp lệ.");
                    ValidateAddress(ends[0]); ValidateAddress(ends[1]);
                    int[] first = Position(ends[0]), last = Position(ends[1]);
                    if (target[0] >= first[0] && target[0] <= last[0] && target[1] >= first[1] && target[1] <= last[1])
                        throw new InvalidDataException("Ô " + address + " cần đứng riêng; chỉ các thông số cối H/I/J được gộp dọc trong khung tối thiểu.");
                }
                return At(address);
            }
            internal bool IsMergeFollower(string address)
            {
                int[] target = Position(address);
                foreach (string merge in merges)
                {
                    string[] ends = (merge ?? "").ToUpperInvariant().Split(':');
                    if (ends.Length != 2) throw new InvalidDataException("Vùng merge Excel không hợp lệ.");
                    ValidateAddress(ends[0]); ValidateAddress(ends[1]);
                    int[] first = Position(ends[0]), last = Position(ends[1]);
                    if (target[0] >= first[0] && target[0] <= last[0] && target[1] >= first[1] && target[1] <= last[1] && address != ends[0]) return true;
                }
                return false;
            }
            // Only vertical shared die cells may inherit their top-left value.
            // Other mapped cells retain the legacy strict merge validation.
            internal object AtMinimumShared(string address, int firstRow, int lastRow)
            {
                int[] target = Position(address);
                string anchor = null;
                foreach (string merge in merges)
                {
                    string[] ends = (merge ?? "").ToUpperInvariant().Split(':');
                    if (ends.Length != 2) throw new InvalidDataException("Vùng merge Excel không hợp lệ.");
                    ValidateAddress(ends[0]); ValidateAddress(ends[1]);
                    int[] first = Position(ends[0]), last = Position(ends[1]);
                    if (first[0] > last[0] || first[1] > last[1]) throw new InvalidDataException("Vùng merge Excel bị đảo chiều.");
                    if (target[0] < first[0] || target[0] > last[0] || target[1] < first[1] || target[1] > last[1]) continue;
                    if (anchor != null || first[0] != last[0] || first[1] < firstRow || last[1] > lastRow)
                        throw new InvalidDataException("Thông số cối tại " + address + " chỉ được gộp dọc trong khung dữ liệu.");
                    anchor = ends[0];
                }
                if (anchor == null) return At(address);
                // Exporters can retain duplicated numeric values under a merge.
                // Excel displays the anchor, so stale follower values are ignored.
                // Still reject formulas/errors at addresses involved in the table.
                if (anchor != address) At(address);
                return At(anchor);
            }
        }
    }
}
