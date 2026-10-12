using System;
using System.Collections.Generic;
using System.Globalization;

namespace ADDIN.Commands
{
    // Identity is the numerical content of the thickness in use. Filenames and
    // material labels are deliberately not consulted: either may be renamed.
    internal static class BendTableContentComparer
    {
        internal static BendTableContentComparison Compare(
            BendTableWorkbookData actual, BendTableWorkbookData standard,
            double thicknessMm, double toleranceMm)
        {
            var result = new BendTableContentComparison { Status = "CHECK" };
            if (!BendValueMath.IsFinite(thicknessMm) || thicknessMm <= 0
                || !BendValueMath.IsFinite(toleranceMm) || toleranceMm < 0)
                return Set(result, "CHECK", "Do day/dung sai so sanh BendTable khong hop le.");
            if (actual == null || !actual.IsValid)
                return Set(result, "CHECK", "Khong doc duoc noi dung BendTable thuc te. "
                    + (actual == null ? "" : actual.Error));
            if (standard == null || !standard.IsValid)
                return Set(result, "CHECK", "Khong doc duoc noi dung BendTable chuan. "
                    + (standard == null ? "" : standard.Error));
            if (!actual.IsMillimeters || !standard.IsMillimeters
                || !KnownKind(actual.ValueKind) || !KnownKind(standard.ValueKind))
                return Set(result, "CHECK", "Chua xac dinh duoc loai gia tri/don vi mm cua BendTable.");
            if (actual.ValueKind != standard.ValueKind)
                return Set(result, "NG", "Loai gia tri BendTable thuc te khac bang chuan ("
                    + actual.ValueKind + " / " + standard.ValueKind + ").");

            string detail;
            result.ActualBlock = FindThickness(actual, thicknessMm, out detail);
            if (result.ActualBlock == null)
                return Set(result, "CHECK", "BendTable thuc te: " + detail);
            result.StandardBlock = FindThickness(standard, thicknessMm, out detail);
            if (result.StandardBlock == null)
                return Set(result, "CHECK", "BendTable chuan: " + detail);
            BendTableBlockData actualBlock = result.ActualBlock;
            BendTableBlockData standardBlock = result.StandardBlock;
            if (actualBlock.IsGroove != standardBlock.IsGroove)
                return Set(result, "NG", "Loai bang bao ranh/be thuong khac bang chuan.");
            if (!actualBlock.HasCoefficient || !standardBlock.HasCoefficient
                || !BendValueMath.IsFinite(actualBlock.CoefficientMm)
                || !BendValueMath.IsFinite(standardBlock.CoefficientMm))
                return Set(result, "CHECK", "Khong doc duoc he so goc hop le cua block BendTable.");
            if (!ValidateGrid(actualBlock, out detail))
                return Set(result, "CHECK", "BendTable thuc te: " + detail);
            if (!ValidateGrid(standardBlock, out detail))
                return Set(result, "CHECK", "BendTable chuan: " + detail);

            result.MaxDifferenceMm = Math.Abs(actualBlock.CoefficientMm - standardBlock.CoefficientMm);
            string differenceDetail = "He so goc lech " + Number(result.MaxDifferenceMm) + " mm";
            // Both grids define piecewise bilinear surfaces. Checking the union
            // of their breakpoints catches altered extra actual rows/columns
            // that sampling only the standard's original nodes would miss.
            // Extra actual domain outside the approved standard is not compared.
            var angles = new SortedSet<double>();
            var standardAngles = new List<double>();
            var actualAngles = new List<double>();
            foreach (BendTableAngleData row in standardBlock.Angles)
            { angles.Add(row.AngleDeg); standardAngles.Add(row.AngleDeg); }
            double minimumAngle = standardBlock.Angles[0].AngleDeg;
            double maximumAngle = standardBlock.Angles[standardBlock.Angles.Count - 1].AngleDeg;
            foreach (BendTableAngleData row in actualBlock.Angles)
            {
                actualAngles.Add(row.AngleDeg);
                if (row.AngleDeg >= minimumAngle && row.AngleDeg <= maximumAngle)
                    angles.Add(row.AngleDeg);
            }
            var radii = new SortedSet<double>(standardBlock.RadiiMm);
            double minimumRadius = standardBlock.RadiiMm[0];
            double maximumRadius = standardBlock.RadiiMm[standardBlock.RadiiMm.Count - 1];
            foreach (double radius in actualBlock.RadiiMm)
                if (radius >= minimumRadius && radius <= maximumRadius) radii.Add(radius);
            foreach (double angle in angles)
            {
                foreach (double radius in radii)
                {
                    double actualValue;
                    double standardValue;
                    string lookupDetail;
                    if (!TryGetContentValue(actualBlock, actualAngles, angle, radius,
                        out actualValue, out lookupDetail) || !BendValueMath.IsFinite(actualValue))
                        return Set(result, "CHECK", "Khong du du lieu BendTable thuc te de doi chieu: "
                            + lookupDetail);
                    if (!TryGetContentValue(standardBlock, standardAngles, angle, radius,
                        out standardValue, out lookupDetail) || !BendValueMath.IsFinite(standardValue))
                        return Set(result, "CHECK", "Khong du du lieu BendTable chuan de doi chieu: "
                            + lookupDetail);
                    double difference = Math.Abs(actualValue - standardValue);
                    if (!BendValueMath.IsFinite(difference))
                        return Set(result, "CHECK", "Chenh lech gia tri BendTable khong hop le.");
                    if (difference > result.MaxDifferenceMm)
                    {
                        result.MaxDifferenceMm = difference;
                        differenceDetail = "Lech lon nhat tai goc " + Number(angle)
                            + " deg, R" + Number(radius)
                            + ": " + Number(difference) + " mm";
                    }
                }
            }
            return result.MaxDifferenceMm > toleranceMm
                ? Set(result, "NG", differenceDetail)
                : Set(result, "OK", "Noi dung BendTable khop bang chuan; ten file khong anh huong ket qua.");
        }

        private static bool KnownKind(BendTableValueKind kind)
        {
            return kind == BendTableValueKind.Deduction || kind == BendTableValueKind.Allowance
                || kind == BendTableValueKind.KFactor;
        }

        private static bool TryGetContentValue(BendTableBlockData block, IList<double> angles,
            double angle, double radius, out double value, out string detail)
        {
            value = 0;
            detail = "Goc/ban kinh nam ngoai pham vi BendTable.";
            int angleLow, angleHigh, radiusLow, radiusHigh;
            double angleFraction, radiusFraction;
            if (!FindContentBracket(angles, angle, out angleLow, out angleHigh, out angleFraction)
                || !FindContentBracket(block.RadiiMm, radius, out radiusLow, out radiusHigh,
                    out radiusFraction)) return false;
            double low = Interpolate(block.Angles[angleLow].ValuesMm[radiusLow].Value,
                block.Angles[angleLow].ValuesMm[radiusHigh].Value, radiusFraction);
            double high = Interpolate(block.Angles[angleHigh].ValuesMm[radiusLow].Value,
                block.Angles[angleHigh].ValuesMm[radiusHigh].Value, radiusFraction);
            value = Interpolate(low, high, angleFraction);
            detail = "";
            return BendValueMath.IsFinite(value);
        }

        private static bool FindContentBracket(IList<double> axis, double target,
            out int low, out int high, out double fraction)
        {
            low = high = 0;
            fraction = 0;
            if (target < axis[0] || target > axis[axis.Count - 1]) return false;
            // Content identity uses the stored nodes exactly. The .01 snapping
            // used when measuring a bend must not hide a different nearby node.
            for (int index = 0; index < axis.Count; index++)
            {
                if (target == axis[index]) { low = high = index; return true; }
                if (target < axis[index])
                {
                    low = index - 1;
                    high = index;
                    fraction = (target - axis[low]) / (axis[high] - axis[low]);
                    return true;
                }
            }
            return false;
        }

        private static double Interpolate(double low, double high, double fraction)
        { return low + (high - low) * fraction; }

        private static BendTableBlockData FindThickness(BendTableWorkbookData workbook,
            double thicknessMm, out string detail)
        {
            BendTableBlockData selected = null;
            double smallestDifference = double.MaxValue;
            int matches = 0;
            foreach (BendTableBlockData block in workbook.Blocks)
            {
                if (block == null || !BendValueMath.IsFinite(block.ThicknessMm) || block.ThicknessMm <= 0)
                    continue;
                double difference = Math.Abs(block.ThicknessMm - thicknessMm);
                if (difference > 0.01) continue;
                matches++;
                if (difference < smallestDifference) { selected = block; smallestDifference = difference; }
            }
            detail = matches > 1 ? "Co nhieu block cung do day can tra; khong xac dinh duoc block."
                : "Khong co block do day " + Number(thicknessMm) + " mm.";
            return matches == 1 ? selected : null;
        }

        private static bool ValidateGrid(BendTableBlockData block, out string detail)
        {
            detail = "Block BendTable khong co du lieu goc/ban kinh hop le.";
            if (block.RadiiMm.Count == 0 || block.Angles.Count == 0) return false;
            for (int index = 0; index < block.RadiiMm.Count; index++)
            {
                double radius = block.RadiiMm[index];
                if (!BendValueMath.IsFinite(radius) || radius < 0) return false;
                if (index > 0 && radius <= block.RadiiMm[index - 1])
                { detail = "Cot ban kinh BendTable khong tang dan hoac bi trung."; return false; }
            }
            for (int index = 0; index < block.Angles.Count; index++)
            {
                BendTableAngleData row = block.Angles[index];
                if (row == null || !BendValueMath.IsFinite(row.AngleDeg)
                    || row.AngleDeg < 0 || row.AngleDeg > 180) return false;
                if (index > 0 && row.AngleDeg <= block.Angles[index - 1].AngleDeg)
                { detail = "Hang goc BendTable khong tang dan hoac bi trung."; return false; }
                if (row.ValuesMm.Count != block.RadiiMm.Count)
                { detail = "So cot gia tri BendTable khong khop so cot ban kinh."; return false; }
                foreach (double? value in row.ValuesMm)
                {
                    if (!value.HasValue || !BendValueMath.IsFinite(value.Value))
                    { detail = "BendTable co o trong/gia tri khong hop le tai goc "
                        + Number(row.AngleDeg) + " deg."; return false; }
                }
            }
            detail = "";
            return true;
        }

        private static BendTableContentComparison Set(BendTableContentComparison result,
            string status, string detail)
        { result.Status = status; result.Detail = detail.Trim(); return result; }

        private static string Number(double value)
        { return value.ToString("0.######", CultureInfo.InvariantCulture); }
    }

    internal sealed class BendTableContentComparison
    {
        internal string Status { get; set; }
        internal string Detail { get; set; }
        internal BendTableBlockData ActualBlock { get; set; }
        internal BendTableBlockData StandardBlock { get; set; }
        internal double MaxDifferenceMm { get; set; }
    }
}
