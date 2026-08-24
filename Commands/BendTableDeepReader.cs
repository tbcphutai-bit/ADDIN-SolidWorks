using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace ADDIN.Commands
{
    internal sealed class BendTableDeepReader : IDisposable
    {
        private readonly Dictionary<string, BendTableWorkbookData> cache =
            new Dictionary<string, BendTableWorkbookData>(StringComparer.OrdinalIgnoreCase);
        private object excelApplication;

        public BendTableWorkbookData Read(string path)
        {
            string normalizedPath = NormalizePath(path);
            BendTableWorkbookData cached;
            if (cache.TryGetValue(normalizedPath, out cached))
                return cached;

            BendTableWorkbookData result = new BendTableWorkbookData
            {
                FilePath = normalizedPath
            };
            cache[normalizedPath] = result;

            if (string.IsNullOrWhiteSpace(normalizedPath) || !File.Exists(normalizedPath))
            {
                result.Error = "Khong tim thay BendTable: " + normalizedPath;
                return result;
            }

            object workbooks = null;
            object workbook = null;
            object worksheet = null;
            object usedRange = null;
            try
            {
                EnsureExcelApplication();
                dynamic excel = excelApplication;
                workbooks = excel.Workbooks;
                workbook = ((dynamic)workbooks).Open(
                    normalizedPath,
                    0,
                    true);
                worksheet = ((dynamic)workbook).Worksheets[1];
                usedRange = ((dynamic)worksheet).UsedRange;
                object rawValues = ((dynamic)usedRange).Value2;
                Array values = rawValues as Array;
                if (values == null || values.Rank != 2)
                {
                    result.Error = "BendTable khong co bang du lieu 2 chieu.";
                    return result;
                }

                ParseBlocks(values, result);
                if (result.Blocks.Count == 0)
                    result.Error = "Khong tim thay block do day trong BendTable.";
            }
            catch (Exception ex)
            {
                result.Error = ex.GetType().Name + ": " + ex.Message;
            }
            finally
            {
                if (workbook != null)
                {
                    try { ((dynamic)workbook).Close(false); } catch { }
                }
                ReleaseComObject(usedRange);
                ReleaseComObject(worksheet);
                ReleaseComObject(workbook);
                ReleaseComObject(workbooks);
            }

            return result;
        }

        public void Dispose()
        {
            if (excelApplication != null)
            {
                try { ((dynamic)excelApplication).Quit(); } catch { }
                ReleaseComObject(excelApplication);
                excelApplication = null;
            }
            cache.Clear();
        }

        private void EnsureExcelApplication()
        {
            if (excelApplication != null)
                return;

            Type excelType = Type.GetTypeFromProgID("Excel.Application");
            if (excelType == null)
                throw new InvalidOperationException("Khong tim thay Microsoft Excel.");

            excelApplication = Activator.CreateInstance(excelType);
            dynamic excel = excelApplication;
            excel.Visible = false;
            excel.DisplayAlerts = false;
            excel.ScreenUpdating = false;
            excel.EnableEvents = false;
        }

        private static void ParseBlocks(Array values, BendTableWorkbookData result)
        {
            int rowMin = values.GetLowerBound(0);
            int rowMax = values.GetUpperBound(0);
            int colMin = values.GetLowerBound(1);
            int colMax = values.GetUpperBound(1);

            for (int row = rowMin; row <= rowMax; row++)
            {
                string label = NormalizeText(GetValue(values, row, colMin));
                if (!string.Equals(label, "\u539A\u307F:", StringComparison.OrdinalIgnoreCase))
                    continue;

                double thickness;
                if (!TryGetDouble(GetValue(values, row, colMin + 1), out thickness))
                    continue;

                BendTableBlockData block = new BendTableBlockData
                {
                    ThicknessMm = thickness
                };

                double coefficient;
                if (TryGetDouble(GetValue(values, row, colMin + 2), out coefficient))
                {
                    block.HasCoefficient = true;
                    block.CoefficientMm = coefficient;
                }

                int radiusRow = row + 2;
                if (radiusRow <= rowMax)
                {
                    for (int col = colMin + 1; col <= colMax; col++)
                    {
                        double radius;
                        if (!TryGetDouble(GetValue(values, radiusRow, col), out radius))
                        {
                            if (block.RadiiMm.Count > 0)
                                break;
                            continue;
                        }

                        block.RadiiMm.Add(radius);
                        block.SourceColumns.Add(col);
                    }
                }

                for (int valueRow = row + 3; valueRow <= rowMax; valueRow++)
                {
                    string nextLabel = NormalizeText(GetValue(values, valueRow, colMin));
                    if (string.Equals(nextLabel, "\u539A\u307F:", StringComparison.OrdinalIgnoreCase))
                        break;

                    double angle;
                    if (!TryGetDouble(GetValue(values, valueRow, colMin), out angle))
                    {
                        if (block.Angles.Count > 0)
                            break;
                        continue;
                    }

                    BendTableAngleData angleData = new BendTableAngleData
                    {
                        AngleDeg = angle
                    };
                    foreach (int sourceColumn in block.SourceColumns)
                    {
                        double value;
                        angleData.ValuesMm.Add(
                            TryGetDouble(GetValue(values, valueRow, sourceColumn), out value)
                                ? (double?)value
                                : null);
                    }
                    block.Angles.Add(angleData);
                }

                result.Blocks.Add(block);
            }
        }

        private static object GetValue(Array values, int row, int column)
        {
            try { return values.GetValue(row, column); }
            catch { return null; }
        }

        private static bool TryGetDouble(object value, out double number)
        {
            number = 0.0;
            if (value == null)
                return false;

            if (value is double)
            {
                number = (double)value;
                return true;
            }
            if (value is float)
            {
                number = (float)value;
                return true;
            }
            if (value is decimal)
            {
                number = (double)(decimal)value;
                return true;
            }
            if (value is int)
            {
                number = (int)value;
                return true;
            }

            string text = Convert.ToString(value, CultureInfo.InvariantCulture);
            return double.TryParse(
                text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out number)
                || double.TryParse(
                    text,
                    NumberStyles.Float,
                    CultureInfo.CurrentCulture,
                    out number);
        }

        private static string NormalizeText(object value)
        {
            string text = Convert.ToString(value ?? "").Trim();
            return text.Normalize(System.Text.NormalizationForm.FormKC);
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return "";
            try { return Path.GetFullPath(path.Trim()); }
            catch { return path.Trim(); }
        }

        private static void ReleaseComObject(object value)
        {
            if (value == null || !Marshal.IsComObject(value))
                return;
            try { Marshal.FinalReleaseComObject(value); } catch { }
        }
    }

    internal sealed class BendTableWorkbookData
    {
        public BendTableWorkbookData()
        {
            Blocks = new List<BendTableBlockData>();
        }

        public string FilePath { get; set; }
        public string Error { get; set; }
        public List<BendTableBlockData> Blocks { get; private set; }
        public bool IsValid
        {
            get { return string.IsNullOrWhiteSpace(Error) && Blocks.Count > 0; }
        }

        public BendTableBlockData FindThickness(double thicknessMm)
        {
            BendTableBlockData best = null;
            double bestDifference = double.MaxValue;
            foreach (BendTableBlockData block in Blocks)
            {
                double difference = Math.Abs(block.ThicknessMm - thicknessMm);
                if (difference < bestDifference)
                {
                    best = block;
                    bestDifference = difference;
                }
            }

            return bestDifference <= 0.01 ? best : null;
        }
    }

    internal sealed class BendTableBlockData
    {
        public BendTableBlockData()
        {
            RadiiMm = new List<double>();
            SourceColumns = new List<int>();
            Angles = new List<BendTableAngleData>();
        }

        public double ThicknessMm { get; set; }
        public bool HasCoefficient { get; set; }
        public double CoefficientMm { get; set; }
        public List<double> RadiiMm { get; private set; }
        public List<int> SourceColumns { get; private set; }
        public List<BendTableAngleData> Angles { get; private set; }

        public bool TryGetValue(
            double angleDeg,
            double radiusMm,
            out double valueMm,
            out string detail)
        {
            valueMm = 0.0;
            detail = "";
            if (RadiiMm.Count == 0 || Angles.Count == 0)
            {
                detail = "Block BendTable khong co du lieu goc/ban kinh.";
                return false;
            }

            int radiusLow;
            int radiusHigh;
            double radiusFraction;
            if (!FindBracket(RadiiMm, radiusMm, 0.01, out radiusLow, out radiusHigh, out radiusFraction))
            {
                detail = "Ban kinh " + radiusMm.ToString("0.###")
                    + " mm nam ngoai pham vi BendTable.";
                return false;
            }

            int angleLow;
            int angleHigh;
            double angleFraction;
            List<double> angles = new List<double>();
            foreach (BendTableAngleData row in Angles)
                angles.Add(row.AngleDeg);
            if (!FindBracket(angles, angleDeg, 0.01, out angleLow, out angleHigh, out angleFraction))
            {
                detail = "Goc " + angleDeg.ToString("0.###")
                    + " deg nam ngoai pham vi BendTable.";
                return false;
            }

            double lowRadiusValue;
            double highRadiusValue;
            if (!TryInterpolateRadius(
                    Angles[angleLow],
                    radiusLow,
                    radiusHigh,
                    radiusFraction,
                    out lowRadiusValue)
                || !TryInterpolateRadius(
                    Angles[angleHigh],
                    radiusLow,
                    radiusHigh,
                    radiusFraction,
                    out highRadiusValue))
            {
                detail = "BendTable co o trong tai goc/ban kinh can tra.";
                return false;
            }

            valueMm = lowRadiusValue
                + ((highRadiusValue - lowRadiusValue) * angleFraction);
            return true;
        }

        public BendTableBlockComparison CompareWith(
            BendTableBlockData standard,
            double toleranceMm)
        {
            BendTableBlockComparison comparison = new BendTableBlockComparison();
            if (standard == null)
            {
                comparison.Compared = false;
                comparison.Detail = "Khong co block BendTable chuan.";
                return comparison;
            }

            if (!HasCoefficient || !standard.HasCoefficient)
            {
                comparison.Compared = false;
                comparison.Detail = "Khong doc duoc he so goc cua block BendTable.";
                return comparison;
            }

            comparison.Compared = true;
            comparison.MaxDifferenceMm = Math.Abs(CoefficientMm - standard.CoefficientMm);
            comparison.Detail = comparison.MaxDifferenceMm > toleranceMm
                ? "He so goc lech "
                    + comparison.MaxDifferenceMm.ToString("0.###") + " mm"
                : "";

            foreach (BendTableAngleData standardAngle in standard.Angles)
            {
                for (int radiusIndex = 0; radiusIndex < standard.RadiiMm.Count; radiusIndex++)
                {
                    double? standardValue = radiusIndex < standardAngle.ValuesMm.Count
                        ? standardAngle.ValuesMm[radiusIndex]
                        : null;
                    if (!standardValue.HasValue)
                        continue;

                    double actualValue;
                    string lookupDetail;
                    if (!TryGetValue(
                            standardAngle.AngleDeg,
                            standard.RadiiMm[radiusIndex],
                            out actualValue,
                            out lookupDetail))
                    {
                        comparison.IsDifferent = true;
                        comparison.Detail = lookupDetail;
                        return comparison;
                    }

                    double difference = Math.Abs(actualValue - standardValue.Value);
                    if (difference > comparison.MaxDifferenceMm)
                    {
                        comparison.MaxDifferenceMm = difference;
                        comparison.Detail = "Lech lon nhat tai goc "
                            + standardAngle.AngleDeg.ToString("0.###")
                            + " deg, R" + standard.RadiiMm[radiusIndex].ToString("0.###")
                            + ": " + difference.ToString("0.###") + " mm";
                    }
                }
            }

            comparison.IsDifferent = comparison.MaxDifferenceMm > toleranceMm;
            if (!comparison.IsDifferent)
                comparison.Detail = "Block BendTable khop bang chuan.";
            return comparison;
        }

        private static bool TryInterpolateRadius(
            BendTableAngleData row,
            int lowIndex,
            int highIndex,
            double fraction,
            out double value)
        {
            value = 0.0;
            if (row == null
                || lowIndex < 0
                || highIndex < 0
                || lowIndex >= row.ValuesMm.Count
                || highIndex >= row.ValuesMm.Count
                || !row.ValuesMm[lowIndex].HasValue
                || !row.ValuesMm[highIndex].HasValue)
            {
                return false;
            }

            double low = row.ValuesMm[lowIndex].Value;
            double high = row.ValuesMm[highIndex].Value;
            value = low + ((high - low) * fraction);
            return true;
        }

        private static bool FindBracket(
            List<double> values,
            double target,
            double exactTolerance,
            out int lowIndex,
            out int highIndex,
            out double fraction)
        {
            lowIndex = -1;
            highIndex = -1;
            fraction = 0.0;
            if (values == null || values.Count == 0)
                return false;

            for (int i = 0; i < values.Count; i++)
            {
                if (Math.Abs(values[i] - target) <= exactTolerance)
                {
                    lowIndex = i;
                    highIndex = i;
                    return true;
                }
            }

            for (int i = 0; i < values.Count - 1; i++)
            {
                double first = values[i];
                double second = values[i + 1];
                double min = Math.Min(first, second);
                double max = Math.Max(first, second);
                if (target < min || target > max)
                    continue;

                lowIndex = i;
                highIndex = i + 1;
                fraction = Math.Abs(second - first) < 0.0000001
                    ? 0.0
                    : (target - first) / (second - first);
                return true;
            }

            return false;
        }
    }

    internal sealed class BendTableAngleData
    {
        public BendTableAngleData()
        {
            ValuesMm = new List<double?>();
        }

        public double AngleDeg { get; set; }
        public List<double?> ValuesMm { get; private set; }
    }

    internal sealed class BendTableBlockComparison
    {
        public bool Compared { get; set; }
        public bool IsDifferent { get; set; }
        public double MaxDifferenceMm { get; set; }
        public string Detail { get; set; }
    }
}
