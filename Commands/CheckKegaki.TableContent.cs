using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace ADDIN.Commands
{
    public sealed partial class KegakiChecker
    {
        private readonly Dictionary<string, BendTableContentAssessment> tableContentCache =
            new Dictionary<string, BendTableContentAssessment>(StringComparer.OrdinalIgnoreCase);

        private BendTableContentAssessment AssessTableContent(BendAllowanceInfo allowance,
            string materialName, string materialGroup, double thicknessMm, bool isFlatSheet)
        {
            string requested = allowance == null ? "" : allowance.GetBendTableFilePath();
            string key = requested + "|" + materialName + "|" + materialGroup + "|"
                + thicknessMm.ToString("R", CultureInfo.InvariantCulture) + "|" + isFlatSheet;
            BendTableContentAssessment result;
            if (tableContentCache.TryGetValue(key, out result)) return result;
            result = new BendTableContentAssessment { Status = "CHECK", Detail = "Chua doi chieu duoc noi dung Bend Table." };
            tableContentCache[key] = result;
            if (allowance == null || !allowance.HasBendTableFile())
            { result.Detail = "Khong doc duoc Bend Table dang dung."; return result; }
            if (!BendValueMath.IsFinite(thicknessMm) || thicknessMm <= 0)
            { result.Detail = "Chua xac dinh duoc mot do day tam de doi chieu Bend Table."; return result; }
            result.ActualPath = ResolveActualBendTablePath(requested, allowance.GetBendTableFileName());
            if (string.IsNullOrWhiteSpace(result.ActualPath))
            { result.Detail = "Khong tim thay file Bend Table thuc te: " + requested; return result; }
            BendTableWorkbookData actual = bendTableReader.Read(result.ActualPath);
            if (!actual.IsValid)
            { result.Detail = "Khong doc duoc Bend Table thuc te: " + actual.Error; return result; }
            result.ActualBlock = actual.FindThickness(thicknessMm);
            if (result.ActualBlock == null)
            { result.Detail = "Bend Table thuc te khong co block do day " + thicknessMm.ToString("0.###") + " mm."; return result; }
            result.IsGroove = result.ActualBlock.IsGroove;
            if (result.ActualBlock.HasCoefficient)
                result.ActualCoefficientMm = result.ActualBlock.CoefficientMm;
            bool alpolic = IsAlpolicMaterial(materialName);
            if (alpolic && !isFlatSheet)
            {
                result.StandardPath = ResolveStandardBendTablePath("BD_Kiku_溝0.8残シ.xlsx");
                result.TableGroup = "溝加工";
            }
            else if (result.IsGroove && !isFlatSheet)
            {
                result.TableGroup = "溝加工";
                return FindGrooveContentMatch(result, actual, thicknessMm);
            }
            else
            {
                string group = alpolic && isFlatSheet ? "AL" : materialGroup;
                result.TableGroup = group;
                result.StandardPath = ResolveStandardBendTablePathForMaterialGroup(group);
            }
            if (string.IsNullOrWhiteSpace(result.StandardPath))
            { result.Detail = "Khong xac dinh duoc bang chuan theo vat lieu: " + materialName; return result; }
            ApplyContentComparison(result, actual, bendTableReader.ReadStandard(result.StandardPath), thicknessMm);
            return result;
        }

        private BendTableContentAssessment FindGrooveContentMatch(BendTableContentAssessment result,
            BendTableWorkbookData actual, double thicknessMm)
        {
            bool incomplete = false;
            bool hasThickness = false;
            string bestDetail = "";
            double nearestCoefficient = double.MaxValue;
            try
            {
                foreach (string path in bendTableReader.GetGrooveStandardPaths())
                {
                    BendTableWorkbookData standard = bendTableReader.ReadStandard(path);
                    if (!standard.IsValid) { incomplete = true; continue; }
                    BendTableBlockData block = standard.FindThickness(thicknessMm);
                    if (block == null || !block.IsGroove) continue;
                    hasThickness = true;
                    BendTableContentComparison comparison = BendTableContentComparer.Compare(
                        actual, standard, thicknessMm, BendTableToleranceMm);
                    if (comparison.Status == "OK")
                    {
                        result.StandardPath = path;
                        ApplyContentComparison(result, actual, standard, thicknessMm);
                        return result;
                    }
                    if (comparison.Status == "CHECK") incomplete = true;
                    if (block.HasCoefficient && result.ActualCoefficientMm.HasValue)
                    {
                        double difference = Math.Abs(block.CoefficientMm - result.ActualCoefficientMm.Value);
                        if (difference < nearestCoefficient)
                        { nearestCoefficient = difference; bestDetail = comparison.Detail; }
                    }
                }
                result.Status = incomplete || !hasThickness ? "CHECK" : "NG";
                result.Detail = incomplete ? "Chua doc du bo bang bao ranh chuan de doi chieu noi dung."
                    : !hasThickness ? "Khong co bang bao ranh chuan cho do day nay."
                    : "Noi dung bang bao ranh khong khop bo chuan. " + bestDetail;
            }
            catch (Exception ex) { result.Detail = "Khong doi chieu duoc bang bao ranh chuan: " + ex.Message; }
            return result;
        }

        private void ApplyContentComparison(BendTableContentAssessment target,
            BendTableWorkbookData actual, BendTableWorkbookData standard, double thicknessMm)
        {
            BendTableContentComparison comparison = BendTableContentComparer.Compare(
                actual, standard, thicknessMm, BendTableToleranceMm);
            target.Status = comparison.Status;
            target.Detail = comparison.Detail;
            target.ActualBlock = comparison.ActualBlock ?? target.ActualBlock;
            target.StandardBlock = comparison.StandardBlock;
            if (target.ActualBlock != null && target.ActualBlock.HasCoefficient)
                target.ActualCoefficientMm = target.ActualBlock.CoefficientMm;
            if (target.StandardBlock != null && target.StandardBlock.HasCoefficient)
                target.StandardCoefficientMm = target.StandardBlock.CoefficientMm;
            if (comparison.Status == "OK" && target.IsGroove)
            {
                // Manufacturing residual is separate from the bend coefficient:
                // the approved 2.5 residual table, for example, has C=2.3.
                // Only the confirmed canonical standard can supply this label.
                target.RemainingThicknessMm = bendTableReader.GetGrooveRemainingThickness(target.StandardPath);
            }
            if (comparison.Status == "OK" && !string.Equals(SafeFileName(target.ActualPath),
                SafeFileName(target.StandardPath), StringComparison.OrdinalIgnoreCase))
                target.Detail = "Ten file khac; noi dung khop " + SafeFileName(target.StandardPath)
                    + "; he so=" + FormatNullable(target.StandardCoefficientMm) + " mm.";
        }

        private static void CopyContentAssessment(MaterialTableCheck target, BendTableContentAssessment source)
        {
            target.BendTablePath = source.ActualPath;
            target.StandardBendTablePath = source.StandardPath;
            target.StandardBendTableName = SafeFileName(source.StandardPath);
            target.BendTableGroup = source.TableGroup;
            target.ActualBlock = source.ActualBlock;
            target.StandardBlock = source.StandardBlock;
            target.ActualCoefficientMm = source.ActualCoefficientMm;
            target.StandardCoefficientMm = source.StandardCoefficientMm;
            target.DeepStatus = source.Status;
            target.DeepNote = source.Detail;
        }

        private static BendAllowanceInfo ResolveUniqueStoredDefaultAllowance(List<BendAllowanceInfo> defaults)
        {
            if (defaults == null || defaults.Count == 0 || defaults[0] == null) return null;
            foreach (BendAllowanceInfo allowance in defaults)
            {
                if (allowance == null) return null;
                string firstType = (defaults[0].TypeName ?? "").ToLowerInvariant();
                string nextType = (allowance.TypeName ?? "").ToLowerInvariant();
                bool bothTables = firstType.Contains("bendtable") && nextType.Contains("bendtable")
                    && defaults[0].Type == allowance.Type;
                // No table file read or filename comparison is needed here.
                // Table settings cannot provide a scalar numeric fallback.
                if (!bothTables && !defaults[0].IsEquivalentTo(allowance)) return null;
            }
            return defaults[0];
        }

    }

    internal sealed class BendTableContentAssessment
    {
        internal string Status { get; set; }
        internal string Detail { get; set; }
        internal string ActualPath { get; set; }
        internal string StandardPath { get; set; }
        internal string TableGroup { get; set; }
        internal bool IsGroove { get; set; }
        internal double? ActualCoefficientMm { get; set; }
        internal double? RemainingThicknessMm { get; set; }
        internal double? StandardCoefficientMm { get; set; }
        internal BendTableBlockData ActualBlock { get; set; }
        internal BendTableBlockData StandardBlock { get; set; }
    }
}
