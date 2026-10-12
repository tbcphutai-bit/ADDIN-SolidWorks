using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace ADDIN.Commands
{
    public sealed partial class KegakiChecker
    {
        // The actual value belongs to the model. A referenced external Excel
        // file is never evidence of the value currently used by this bend.
        private void ApplyPerBendValueCheck(KegakiBendResult row, List<double> sheetThicknesses,
            FlatBendMeasurement measurement)
        {
            if (row.IsLargeRadiusBend)
            {
                row.BendValueStatus = "SKIP";
                row.BendValueNote = "R > 0.2mm: kiem tra K-factor=0.5 theo quy tac rieng.";
                return;
            }
            if (IsAlpolicMaterial(row.MaterialName))
            {
                row.BendValueStatus = "SKIP";
                row.BendValueNote = "Bo qua do nguoc he so Alpolic theo yeu cau; van kiem tra bang va setting.";
                row.Note = AppendNote(row.Note, row.BendValueNote);
                return;
            }
            if (!row.HasBendGeometry)
            {
                CompletePerBendCheck(row, "CHECK", "Khong doc duoc thong tin goc/R cua bend.");
                return;
            }
            if (sheetThicknesses == null || sheetThicknesses.Count != 1)
            {
                CompletePerBendCheck(row, "CHECK", "Thieu do day hoac co nhieu do day; chua anh xa duoc do day cho bend nay.");
                return;
            }
            row.SheetThicknessMm = sheetThicknesses[0] * 1000.0;
            if (!BendValueMath.IsFinite(row.SheetThicknessMm) || row.SheetThicknessMm <= 0)
            {
                CompletePerBendCheck(row, "CHECK", "Do day tam cua bend khong hop le.");
                return;
            }

            double actual;
            string detail;
            if (!TryReadActualBendDeduction(row, measurement, out actual, out detail))
            {
                CompletePerBendCheck(row, "CHECK", detail);
                return;
            }
            row.ActualBendDeductionMm = actual;
            row.ActualEdgeCoefficientMm = BendValueMath.NormalizedCoefficient(actual, row.AngleDeg);
            row.BendCoefficientMm = row.ActualEdgeCoefficientMm;

            bool isGroove = Math.Abs(row.RadiusMm - 0.2) <= 0.03;
            BendStandardValueSelection selection = isGroove
                ? ReadIntendedGrooveValue(row, actual)
                : ReadOrdinaryStandardValue(row.MaterialGroup, row.SheetThicknessMm,
                    row.AngleDeg, row.RadiusMm, actual);
            row.BendValueStandardTable = SafeFileName(selection.StandardPath);
            row.StandardBendTableName = row.BendValueStandardTable;
            if (selection.ExpectedDeductionMm.HasValue)
            {
                double expected = selection.ExpectedDeductionMm.Value;
                row.StandardBendDeductionMm = expected;
                row.StandardEdgeCoefficientMm = BendValueMath.NormalizedCoefficient(expected, row.AngleDeg);
                row.StandardBendCoefficientMm = row.StandardEdgeCoefficientMm;
                row.BendDeductionDifferenceMm = Math.Abs(actual - expected);
                detail = "BD canh=" + actual.ToString("0.####") + " mm; BD bang="
                    + expected.ToString("0.####") + " mm; lech="
                    + row.BendDeductionDifferenceMm.Value.ToString("0.####")
                    + " mm (dung sai " + BendTableToleranceMm.ToString("0.###") + " mm). ";
            }
            else detail = "BD canh=" + actual.ToString("0.####") + " mm. ";
            CompletePerBendCheck(row, selection.Status, detail + selection.Detail);
        }

        internal static bool TryReadActualBendDeduction(KegakiBendResult row,
            FlatBendMeasurement measurement, out double deductionMm, out string detail)
        {
            deductionMm = 0;
            detail = "";
            if (measurement != null && !measurement.GeometryUnavailable)
            {
                row.BendValueSource = "Do nguoc tu hinh hoc Flat-Pattern trong model";
                if (!string.IsNullOrWhiteSpace(measurement.Error)
                    || !measurement.AllowanceMm.HasValue || !measurement.AngleDeg.HasValue
                    || !measurement.RadiusMm.HasValue)
                {
                    detail = !string.IsNullOrWhiteSpace(measurement.Error) ? measurement.Error
                        : "Thieu mau do hinh hoc cua bend; khong thay bang gia tri setting.";
                    return false;
                }
                if (!BendValueMath.IsFinite(measurement.AllowanceMm.Value)
                    || measurement.AllowanceMm.Value <= 0)
                {
                    detail = "Gia tri do tu Flat-Pattern khong hop le; khong thay bang gia tri setting.";
                    return false;
                }
                if (!BendValueMath.TryToDeduction(BendTableValueKind.Allowance,
                    measurement.AllowanceMm.Value, row.SheetThicknessMm, measurement.AngleDeg.Value,
                    measurement.RadiusMm.Value, out deductionMm, out detail)) return false;
                row.MeasuredBendAllowanceMm = measurement.AllowanceMm;
                row.AngleDeg = measurement.AngleDeg.Value;
                row.RadiusMm = measurement.RadiusMm.Value;
                row.MeasuredFlatLengthMm = measurement.FlatLengthMm;
                if (measurement.StraightLengthMm.HasValue)
                    row.MeasuredOutsideLengthMm = measurement.StraightLengthMm.Value
                        + 2.0 * (row.RadiusMm + row.SheetThicknessMm)
                        * Math.Tan(row.AngleDeg * Math.PI / 360.0);
                row.BendValueSource = (string.IsNullOrWhiteSpace(measurement.Source)
                    ? "Hinh hoc Flat-Pattern trong model" : measurement.Source)
                    + "; " + measurement.Samples + " mau do";
                return true;
            }
            // Unavailability is distinct from a failed/inconsistent measurement.
            // A partial sample can never be overridden by the setting value.
            if (measurement != null && (measurement.Samples > 0 || measurement.AllowanceMm.HasValue))
            {
                detail = "Co mau hinh hoc nhung chua xac nhan duoc gia tri; khong thay bang setting.";
                return false;
            }
            BendAllowanceInfo setting = row.EffectiveAllowance;
            if (setting == null)
            {
                detail = "Khong do duoc tam trai va khong doc/anh xa duoc setting cua bend.";
                return false;
            }
            // TryGetDeduction selects the active numeric type only. Inactive
            // K/BA/BD fields of a table setting must not supply an actual value.
            if (!setting.TryGetDeduction(row.SheetThicknessMm, row.AngleDeg, row.RadiusMm,
                out deductionMm, out detail))
            {
                detail = "Khong do duoc tam trai va setting hieu luc khong co gia tri so doc duoc. " + detail;
                return false;
            }
            row.BendValueSource = "Gia tri so luu trong setting model (" + setting.Summary()
                + "); khong phai gia tri do tu tam trai";
            return true;
        }

        private BendStandardValueSelection ReadOrdinaryStandardValue(string materialGroup,
            double thicknessMm, double angleDeg, double radiusMm, double actualDeductionMm)
        {
            var result = new BendStandardValueSelection { Status = "CHECK" };
            result.StandardPath = ResolveStandardBendTablePathForMaterialGroup(materialGroup);
            if (string.IsNullOrWhiteSpace(result.StandardPath))
            {
                result.Detail = "Khong tim thay bang chuan theo vat lieu cua chi tiet.";
                return result;
            }
            double expected;
            string detail;
            if (!bendTableReader.ReadStandard(result.StandardPath).TryGetDeduction(
                thicknessMm, angleDeg, radiusMm, out expected, out detail))
            {
                result.Detail = "Bang chuan: " + detail;
                return result;
            }
            result.ExpectedDeductionMm = expected;
            result.Status = Math.Abs(actualDeductionMm - expected) > BendTableToleranceMm ? "NG" : "OK";
            result.Detail = result.Status == "OK" ? "Gia tri cua bend khop chuan vat lieu; ten file khong anh huong."
                : "Gia tri cua bend khong khop chuan vat lieu hien tai.";
            return result;
        }

        private BendStandardValueSelection ReadIntendedGrooveValue(KegakiBendResult row,
            double actualDeductionMm)
        {
            if (!string.IsNullOrWhiteSpace(row.IntendedGrooveError))
                return new BendStandardValueSelection { Status = "CHECK", Detail = row.IntendedGrooveError };
            if (string.IsNullOrWhiteSpace(row.IntendedGroovePropertyName)
                || !row.IntendedGrooveCoefficientMm.HasValue
                || !BendValueMath.IsFinite(row.IntendedGrooveCoefficientMm.Value)
                || row.IntendedGrooveCoefficientMm.Value <= 0)
                return new BendStandardValueSelection { Status = "CHECK",
                    Detail = "Chua co muc bao du kien doc lap cua bend (V溝1 / V溝2 / C溝)." };
            double intendedCoefficientMm = row.IntendedGrooveCoefficientMm.Value;
            var candidates = new List<GrooveBendValueCandidate>();
            bool incomplete = false;
            string firstError = "";
            try
            {
                foreach (string path in bendTableReader.GetGrooveStandardPaths())
                {
                    BendTableWorkbookData standard = bendTableReader.ReadStandard(path);
                    if (!standard.IsValid)
                    {
                        incomplete = true;
                        if (firstError.Length == 0) firstError = SafeFileName(path) + ": " + standard.Error;
                        continue;
                    }
                    BendTableBlockData block = standard.FindThickness(row.SheetThicknessMm);
                    if (block == null) continue;
                    if (!block.HasCoefficient || !BendValueMath.IsFinite(block.CoefficientMm))
                    {
                        incomplete = true;
                        if (firstError.Length == 0) firstError = SafeFileName(path) + ": khong doc duoc he so block.";
                        continue;
                    }
                    if (Math.Abs(block.CoefficientMm - intendedCoefficientMm) > BendTableToleranceMm) continue;
                    double? remaining = ReadCanonicalGrooveRemainingThickness(path);
                    double deduction;
                    string detail;
                    if (!block.IsGroove || !remaining.HasValue
                        || !standard.TryGetDeduction(row.SheetThicknessMm, row.AngleDeg, row.RadiusMm,
                            out deduction, out detail))
                    {
                        incomplete = true;
                        if (firstError.Length == 0)
                            firstError = SafeFileName(path) + ": thieu du lieu bao ranh tai do day/goc/R can tra.";
                        continue;
                    }
                    candidates.Add(new GrooveBendValueCandidate
                    { StandardPath = path, DeductionMm = deduction,
                        CoefficientMm = block.CoefficientMm, RemainingThicknessMm = remaining.Value });
                }
            }
            catch (Exception ex)
            {
                incomplete = true;
                if (firstError.Length == 0) firstError = ex.Message;
            }
            BendStandardValueSelection selected = SelectIntendedGrooveValue(candidates, actualDeductionMm,
                row.SheetThicknessMm, intendedCoefficientMm, BendTableToleranceMm, incomplete, firstError);
            selected.Detail = "Muc bao du kien " + row.IntendedGroovePropertyName + "="
                + intendedCoefficientMm.ToString("0.###") + " mm; " + selected.Detail;
            return selected;
        }

        internal static double? ReadCanonicalGrooveRemainingThickness(string standardPath)
        {
            string name;
            try { name = Path.GetFileName(standardPath).Normalize(NormalizationForm.FormKC); }
            catch { return null; }
            Match match = Regex.Match(name, @"溝\s*(\d+(?:\.\d+)?)\s*残");
            double value;
            if (!match.Success || !double.TryParse(match.Groups[1].Value, NumberStyles.Float,
                CultureInfo.InvariantCulture, out value) || !BendValueMath.IsFinite(value) || value <= 0)
                return null;
            return value;
        }

        // The independent model property stores Kiku's C coefficient. The
        // actual measured coefficient must never choose the manufacturing key.
        internal static BendStandardValueSelection SelectIntendedGrooveValue(
            IList<GrooveBendValueCandidate> candidates, double actualDeductionMm,
            double thicknessMm, double intendedCoefficientMm, double toleranceMm,
            bool incomplete, string firstError)
        {
            var result = new BendStandardValueSelection { Status = "CHECK" };
            if (!BendValueMath.IsFinite(actualDeductionMm) || !BendValueMath.IsFinite(thicknessMm)
                || thicknessMm <= 0 || !BendValueMath.IsFinite(intendedCoefficientMm)
                || intendedCoefficientMm <= 0 || !BendValueMath.IsFinite(toleranceMm) || toleranceMm < 0)
            { result.Detail = "Gia tri bend/do day/dung sai bao ranh khong hop le."; return result; }
            GrooveBendValueCandidate nearest = null;
            double nearestDifference = double.MaxValue;
            double minimumDeduction = double.MaxValue;
            double maximumDeduction = double.MinValue;
            var intendedResiduals = new SortedSet<double>();
            if (candidates != null)
            {
                foreach (GrooveBendValueCandidate candidate in candidates)
                {
                    if (candidate == null || !BendValueMath.IsFinite(candidate.DeductionMm)
                        || !BendValueMath.IsFinite(candidate.CoefficientMm) || candidate.CoefficientMm <= 0
                        || !BendValueMath.IsFinite(candidate.RemainingThicknessMm)
                        || candidate.RemainingThicknessMm <= 0)
                    { incomplete = true; continue; }
                    if (Math.Abs(candidate.CoefficientMm - intendedCoefficientMm) > toleranceMm) continue;
                    double difference = Math.Abs(actualDeductionMm - candidate.DeductionMm);
                    intendedResiduals.Add(candidate.RemainingThicknessMm);
                    minimumDeduction = Math.Min(minimumDeduction, candidate.DeductionMm);
                    maximumDeduction = Math.Max(maximumDeduction, candidate.DeductionMm);
                    if (difference < nearestDifference)
                    { nearest = candidate; nearestDifference = difference; }
                }
            }
            if (incomplete)
            {
                result.Detail = "Chua du du lieu bang bao ranh chuan tai do day/goc/R can tra. " + firstError;
                return result;
            }
            if (intendedResiduals.Count > 1)
            {
                result.Detail = "He so bao du kien ung voi nhieu be day con lai trong database; chua chon duoc chuan duy nhat.";
                return result;
            }
            if (nearest == null)
            {
                result.Detail = "Database khong co bang bao ranh cho he so du kien tai do day/goc/R nay.";
                return result;
            }
            if (maximumDeduction - minimumDeduction > toleranceMm)
            {
                result.Detail = "Nhieu bang cung he so bao du kien nhung gia tri tai goc/R khong thong nhat; chua chon duoc chuan.";
                return result;
            }
            result.StandardPath = nearest.StandardPath;
            result.ExpectedDeductionMm = nearest.DeductionMm;
            if (nearest.RemainingThicknessMm >= thicknessMm)
            {
                result.Status = "NG";
                result.Detail = "Tam day " + thicknessMm.ToString("0.###") + " mm <= be day con lai sau bao "
                    + nearest.RemainingThicknessMm.ToString("0.###") + " mm.";
                return result;
            }
            result.Status = nearestDifference > toleranceMm ? "NG" : "OK";
            result.Detail = (result.Status == "OK" ? "Gia tri bend khop muc bao du kien. "
                : "Gia tri bend KHONG KHOP muc bao du kien. ")
                + "Chuan " + SafeFileName(nearest.StandardPath) + "; be day con lai="
                + nearest.RemainingThicknessMm.ToString("0.###", CultureInfo.InvariantCulture) + " mm.";
            return result;
        }
    }

    internal sealed class GrooveBendValueCandidate
    {
        internal string StandardPath { get; set; }
        internal double DeductionMm { get; set; }
        internal double CoefficientMm { get; set; }
        internal double RemainingThicknessMm { get; set; }
    }

    internal sealed class BendStandardValueSelection
    {
        internal string Status { get; set; }
        internal string Detail { get; set; }
        internal string StandardPath { get; set; }
        internal double? ExpectedDeductionMm { get; set; }
    }
}
