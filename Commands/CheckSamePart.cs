using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands
{
    public sealed class SamePartCheckResult
    {
        public int CheckedCount { get; set; }
        public int ProcessedCount { get; set; }
        public int SkippedCount { get; set; }
        public bool Canceled { get; set; }
        public string DebugLogPath { get; set; }
        public List<SamePartGroupResult> Groups { get; } = new List<SamePartGroupResult>();
        public List<SamePartItemResult> Errors { get; } = new List<SamePartItemResult>();
        public HashSet<int> HighlightRowIndexes { get; } = new HashSet<int>();
    }

    public sealed class SamePartGroupResult
    {
        public int GroupNumber { get; set; }
        public string Status { get; set; }
        public string Note { get; set; }
        public List<SamePartItemResult> Items { get; } = new List<SamePartItemResult>();
    }

    public sealed class SamePartItemResult
    {
        public int BomRowIndex { get; set; }
        public string BuhinNo { get; set; }
        public string BomFileName { get; set; }
        public string ComponentName { get; set; }
        public string PartPath { get; set; }
        public string Configuration { get; set; }
        public string FlatConfiguration { get; set; }
        public string Material { get; set; }
        public string MaterialKey { get; set; }
        public string Thickness { get; set; }
        public string ThicknessKey { get; set; }
        public string FoldedSignature { get; set; }
        public string FlatSignature { get; set; }
        public string FoldedCandidateSignature { get; set; }
        public string FlatCandidateSignature { get; set; }
        public string FoldedBroadSignature { get; set; }
        public string FlatBroadSignature { get; set; }
        public string FoldedBroadSummary { get; set; }
        public string FlatBroadSummary { get; set; }
        public BroadGeometryMetrics FoldedBroadMetrics { get; set; }
        public BroadGeometryMetrics FlatBroadMetrics { get; set; }
        public FlatProfileMetrics FlatProfileMetrics { get; set; }
        public string FlatProfileSummary { get; set; }
        public string FoldedOrientationSignature { get; set; }
        public string FlatOrientationSignature { get; set; }
        public string FoldedChiralitySignature { get; set; }
        public string FlatChiralitySignature { get; set; }
        public string FeatureMirrorSignature { get; set; }
        public string FeatureOperationSignature { get; set; }
        public string FeatureTrace { get; set; }
        public string Error { get; set; }
    }

    public sealed class BroadGeometryMetrics
    {
        public int BodyCount { get; set; }
        public double AreaMm2 { get; set; }
        public double EdgeLengthMm { get; set; }
        public double VolumeMm3 { get; set; }
        public List<double> PrincipalMoments { get; } = new List<double>();
    }

    public sealed class FlatProfileMetrics
    {
        public int InnerLoopCount { get; set; }
        public List<InternalLoopMetrics> InnerLoops { get; } = new List<InternalLoopMetrics>();
        public List<double> CenterDistancesMm { get; } = new List<double>();
    }

    public sealed class SamePartToleranceOptions
    {
        public double AreaAbsoluteMm2 { get; set; } = 5.0;
        public double AreaRelativePercent { get; set; } = 0.01;
        public double EdgeLengthMm { get; set; } = 0.20;
        public double VolumeAbsoluteMm3 { get; set; } = 5.0;
        public double VolumeRelativePercent { get; set; } = 0.01;
        public double PrincipalMomentRelativePercent { get; set; } = 0.05;
        public double HoleLinearMm { get; set; } = 0.20;
        public double HoleRadiusMm { get; set; } = 0.05;

        public SamePartToleranceOptions Clone()
        {
            return (SamePartToleranceOptions)MemberwiseClone();
        }
    }

    public sealed class InternalLoopMetrics
    {
        public string TopologyKey { get; set; }
        public double PerimeterMm { get; set; }
        public List<double> EdgeLengthsMm { get; } = new List<double>();
        public List<double> RadiiMm { get; } = new List<double>();
        public List<double> OuterDistancesMm { get; } = new List<double>();
        internal double[] CenterM { get; set; }
    }

    internal sealed class SamePartCheckTarget
    {
        public int BomRowIndex { get; set; }
        public string BuhinNo { get; set; }
        public string BomFileName { get; set; }
        public string ComponentName { get; set; }
        public string PartPath { get; set; }
        public string Configuration { get; set; }
        public string Material { get; set; }
        public string Thickness { get; set; }
    }

    public sealed class CheckSamePartRunner
    {
        private readonly ISldWorks swApp;
        private readonly DataGridView gridBom;
        private readonly SamePartToleranceOptions toleranceOptions;
        private readonly List<string> debugLines = new List<string>();

        public CheckSamePartRunner(
            ISldWorks app,
            DataGridView grid,
            SamePartToleranceOptions options = null)
        {
            swApp = app;
            gridBom = grid;
            toleranceOptions = options == null ? new SamePartToleranceOptions() : options.Clone();
        }

        public SamePartCheckResult Run(
            Action<int> progressStarted,
            Action<int, int> progressChanged,
            Func<bool> isCancellationRequested)
        {
            SamePartCheckResult result = new SamePartCheckResult();
            debugLines.Clear();
            ModelDoc2 originalDocument = swApp == null ? null : swApp.ActiveDoc as ModelDoc2;
            bool oldCommandInProgress = false;
            HashSet<string> processedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<SamePartItemResult> items = new List<SamePartItemResult>();

            try
            {
                if (swApp == null || gridBom == null)
                    return result;

                oldCommandInProgress = swApp.CommandInProgress;
                swApp.CommandInProgress = true;
                result.CheckedCount = CountCheckedRows();
                progressStarted?.Invoke(result.CheckedCount);
                Log("===== RUN START ===== rows=" + result.CheckedCount);

                foreach (DataGridViewRow row in gridBom.Rows)
                {
                    if (IsCancellationRequested(isCancellationRequested))
                    {
                        result.Canceled = true;
                        break;
                    }
                    if (row.IsNewRow || !Convert.ToBoolean(row.Cells[0].Value ?? false))
                        continue;

                    List<SamePartCheckTarget> targets = GetTargets(row);
                    if (targets.Count == 0)
                    {
                        SamePartItemResult missing = CreateErrorFromRow(row, "Khong tim thay duong dan part trong BOM.");
                        result.Errors.Add(missing);
                        result.HighlightRowIndexes.Add(row.Index);
                        result.SkippedCount++;
                    }

                    foreach (SamePartCheckTarget target in targets)
                    {
                        if (IsCancellationRequested(isCancellationRequested))
                        {
                            result.Canceled = true;
                            break;
                        }

                        string key = ((target.PartPath ?? "").Trim() + "|" +
                            (target.Configuration ?? "").Trim()).ToUpperInvariant();
                        if (key == "|" || !processedTargets.Add(key))
                            continue;

                        SamePartItemResult item = CheckTarget(target, isCancellationRequested);
                        if (!string.IsNullOrWhiteSpace(item.Error))
                        {
                            result.Errors.Add(item);
                            result.HighlightRowIndexes.Add(item.BomRowIndex);
                            result.SkippedCount++;
                        }
                        else
                        {
                            items.Add(item);
                        }
                    }

                    result.ProcessedCount++;
                    progressChanged?.Invoke(result.ProcessedCount, result.CheckedCount);
                    Application.DoEvents();
                }

                if (!result.Canceled)
                    BuildGroups(items, result);
            }
            catch (OperationCanceledException)
            {
                result.Canceled = true;
            }
            finally
            {
                try { swApp.CommandInProgress = oldCommandInProgress; } catch { }
                RestoreDocument(originalDocument);
                Log("===== RUN END ===== processed=" + result.ProcessedCount
                    + ", groups=" + result.Groups.Count + ", errors=" + result.Errors.Count
                    + ", canceled=" + result.Canceled);
                result.DebugLogPath = SaveDebugLog();
            }

            return result;
        }

        private SamePartItemResult CheckTarget(
            SamePartCheckTarget target,
            Func<bool> isCancellationRequested)
        {
            SamePartItemResult item = new SamePartItemResult
            {
                BomRowIndex = target.BomRowIndex,
                BuhinNo = target.BuhinNo ?? "",
                BomFileName = target.BomFileName ?? "",
                ComponentName = target.ComponentName ?? "",
                PartPath = target.PartPath ?? "",
                Configuration = target.Configuration ?? "",
                Material = target.Material ?? "",
                MaterialKey = NormalizeMaterial(target.Material),
                Thickness = target.Thickness ?? "",
                ThicknessKey = NormalizeThickness(target.Thickness)
            };

            string partPath = (target.PartPath ?? "").Trim();
            if (partPath.Length == 0 || !File.Exists(partPath) ||
                !string.Equals(Path.GetExtension(partPath), ".SLDPRT", StringComparison.OrdinalIgnoreCase))
            {
                item.Error = "Khong tim thay file SLDPRT hop le.";
                return item;
            }

            string temporaryPartPath = "";
            ModelDoc2 part = null;
            bool visibilityChanged = false;
            int errors = 0;
            int warnings = 0;

            try
            {
                ThrowIfCanceled(isCancellationRequested);
                temporaryPartPath = CreateTemporaryPartCopy(partPath);
                if (temporaryPartPath.Length == 0)
                {
                    item.Error = "Khong tao duoc ban sao tam de kiem tra an toan.";
                    return item;
                }

                try
                {
                    swApp.DocumentVisible(false, (int)swDocumentTypes_e.swDocPART);
                    visibilityChanged = true;
                }
                catch { }

                part = swApp.OpenDoc6(
                    temporaryPartPath,
                    (int)swDocumentTypes_e.swDocPART,
                    (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
                    "",
                    ref errors,
                    ref warnings) as ModelDoc2;

                if (part == null)
                {
                    item.Error = "Khong mo duoc ban sao tam. Error=" + errors + ", Warning=" + warnings;
                    return item;
                }

                string sourceConfiguration = FindSourceConfiguration(part, target.Configuration);
                if (sourceConfiguration.Length == 0 || !TryShowConfiguration(part, sourceConfiguration))
                {
                    item.Error = "Khong tim thay configuration gap de so sanh.";
                    return item;
                }

                item.Configuration = sourceConfiguration;
                part.ForceRebuild3(false);
                ThrowIfCanceled(isCancellationRequested);
                item.FoldedSignature = GeometrySignatureBuilder.Create(part, isCancellationRequested);
                item.FoldedCandidateSignature = GeometrySignatureBuilder.CreateCandidate(part, isCancellationRequested);
                item.FoldedBroadSignature = GeometrySignatureBuilder.CreateBroad(
                    part,
                    isCancellationRequested,
                    out string foldedBroadSummary,
                    out BroadGeometryMetrics foldedBroadMetrics);
                item.FoldedBroadSummary = foldedBroadSummary;
                item.FoldedBroadMetrics = foldedBroadMetrics;
                item.FoldedOrientationSignature = GeometrySignatureBuilder.CreateOrientation(part, isCancellationRequested);
                item.FoldedChiralitySignature = GeometrySignatureBuilder.CreateChirality(part, isCancellationRequested);
                GeometrySignatureBuilder.CreateFeatureDiagnostics(
                    part,
                    out string mirrorSignature,
                    out string operationSignature,
                    out string featureTrace);
                item.FeatureMirrorSignature = mirrorSignature;
                item.FeatureOperationSignature = operationSignature;
                item.FeatureTrace = featureTrace;

                string flatConfiguration = FindFlatConfiguration(part, sourceConfiguration);
                if (flatConfiguration.Length == 0 || !TryShowConfiguration(part, flatConfiguration))
                {
                    item.Error = "Khong tim thay configuration SM-FLAT-PATTERN.";
                    return item;
                }

                item.FlatConfiguration = flatConfiguration;
                part.ForceRebuild3(false);
                ThrowIfCanceled(isCancellationRequested);
                item.FlatSignature = GeometrySignatureBuilder.Create(part, isCancellationRequested);
                item.FlatCandidateSignature = GeometrySignatureBuilder.CreateCandidate(part, isCancellationRequested);
                item.FlatBroadSignature = GeometrySignatureBuilder.CreateBroad(
                    part,
                    isCancellationRequested,
                    out string flatBroadSummary,
                    out BroadGeometryMetrics flatBroadMetrics);
                item.FlatBroadSummary = flatBroadSummary;
                item.FlatBroadMetrics = flatBroadMetrics;
                item.FlatProfileMetrics = GeometrySignatureBuilder.CreateFlatProfileMetrics(
                    part,
                    isCancellationRequested,
                    out string flatProfileExact,
                    out string flatProfileCandidate,
                    out string flatProfileSummary);
                item.FlatProfileSummary = flatProfileSummary;
                item.FlatOrientationSignature = GeometrySignatureBuilder.CreateOrientation(part, isCancellationRequested);
                item.FlatChiralitySignature = GeometrySignatureBuilder.CreateChirality(part, isCancellationRequested);

                if (item.FoldedSignature.Length == 0 || item.FlatSignature.Length == 0)
                    item.Error = "Khong tao duoc chu ky hinh hoc cua chi tiet.";
                LogItem(item);
                return item;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                item.Error = ex.GetType().Name + ": " + ex.Message;
                Log("Target ERROR: " + ex);
                return item;
            }
            finally
            {
                if (part != null)
                {
                    try { swApp.CloseDoc(part.GetTitle()); } catch { }
                }
                if (visibilityChanged)
                {
                    try { swApp.DocumentVisible(true, (int)swDocumentTypes_e.swDocPART); } catch { }
                }
                DeleteTemporaryPartCopy(temporaryPartPath);
            }
        }

        private void BuildGroups(List<SamePartItemResult> items, SamePartCheckResult result)
        {
            int groupNumber = 1;
            HashSet<SamePartItemResult> groupedItems = new HashSet<SamePartItemResult>();
            IEnumerable<IGrouping<string, SamePartItemResult>> rawFlatGroups = items
                .Where(item => !string.IsNullOrWhiteSpace(item.FlatCandidateSignature))
                .GroupBy(item => item.FlatCandidateSignature, StringComparer.Ordinal)
                .Where(group => group.Count() > 1);

            List<List<SamePartItemResult>> flatGroups = new List<List<SamePartItemResult>>();
            foreach (IGrouping<string, SamePartItemResult> rawGroup in rawFlatGroups)
            {
                List<SamePartItemResult> rawItems = rawGroup
                    .OrderBy(item => SortBuhinNo(item.BuhinNo))
                    .ThenBy(item => item.PartPath, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                HashSet<SamePartItemResult> used = new HashSet<SamePartItemResult>();
                foreach (SamePartItemResult seed in rawItems)
                {
                    if (used.Contains(seed))
                        continue;
                    List<SamePartItemResult> profileGroup = new List<SamePartItemResult> { seed };
                    foreach (SamePartItemResult candidate in rawItems)
                    {
                        if (ReferenceEquals(seed, candidate) || used.Contains(candidate))
                            continue;
                        if (profileGroup.All(existing => AreFlatProfilesWithinTolerance(
                            existing.FlatProfileMetrics,
                            candidate.FlatProfileMetrics)))
                            profileGroup.Add(candidate);
                    }
                    if (profileGroup.Count < 2)
                        continue;
                    foreach (SamePartItemResult member in profileGroup)
                        used.Add(member);
                    flatGroups.Add(profileGroup);
                }
            }

            foreach (List<SamePartItemResult> flatGroup in flatGroups
                .OrderBy(group => group.Min(item => SortBuhinNo(item.BuhinNo))))
            {
                List<SamePartItemResult> groupItems = flatGroup
                    .OrderBy(item => SortBuhinNo(item.BuhinNo))
                    .ThenBy(item => item.PartPath, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                int flatDirectionCount = groupItems
                    .Select(item => item.FlatOrientationSignature ?? "")
                    .Distinct(StringComparer.Ordinal)
                    .Count();
                int flatExactCount = groupItems
                    .Select(item => item.FlatSignature ?? "")
                    .Distinct(StringComparer.Ordinal)
                    .Count();
                int foldedInvariantCount = groupItems
                    .Select(item => item.FoldedSignature ?? "")
                    .Distinct(StringComparer.Ordinal)
                    .Count();
                int foldedCandidateCount = groupItems
                    .Select(item => item.FoldedCandidateSignature ?? "")
                    .Distinct(StringComparer.Ordinal)
                    .Count();
                int foldedDirectionCount = groupItems
                    .Select(item => item.FoldedOrientationSignature ?? "")
                    .Distinct(StringComparer.Ordinal)
                    .Count();
                int foldedChiralityCount = groupItems
                    .Select(item => item.FoldedChiralitySignature ?? "")
                    .Where(value => value.Length > 0 && value != "AMBIG" && value != "PLANAR")
                    .Distinct(StringComparer.Ordinal)
                    .Count();
                int mirrorFeatureCount = groupItems
                    .Select(item => item.FeatureMirrorSignature ?? "")
                    .Where(value => value.Length > 0 && value != "NONE")
                    .Distinct(StringComparer.Ordinal)
                    .Count();
                int featureOperationCount = groupItems
                    .Select(item => item.FeatureOperationSignature ?? "")
                    .Distinct(StringComparer.Ordinal)
                    .Count();
                bool sameFolded = foldedCandidateCount == 1;
                bool sameMaterial = groupItems.Select(item => item.MaterialKey).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1;
                bool sameThickness = groupItems.Select(item => item.ThicknessKey).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1;
                bool mirrorByDirection = foldedInvariantCount == 1 && foldedDirectionCount > 1;
                bool mirrorByChirality = foldedChiralityCount > 1;
                bool mirrorByFeature = mirrorFeatureCount > 1;
                bool mirrorByOperation = flatExactCount == 1
                    && foldedInvariantCount == 1
                    && featureOperationCount > 1;

                SamePartGroupResult group = new SamePartGroupResult
                {
                    GroupNumber = groupNumber++
                };
                group.Items.AddRange(groupItems);

                if (mirrorByDirection || mirrorByChirality || mirrorByFeature || mirrorByOperation)
                {
                    group.Status = "CHECK MIRROR";
                    List<string> mirrorReasons = new List<string>();
                    if (mirrorByDirection) mirrorReasons.Add("huong toa do");
                    if (mirrorByChirality) mirrorReasons.Add("dau thuan/nghich hinh hoc");
                    if (mirrorByFeature) mirrorReasons.Add("feature mirror/derived");
                    if (mirrorByOperation) mirrorReasons.Add("chuoi feature tao/cat khac nhau");
                    group.Note = "Bien dang co cung kich thuoc nhung khac "
                        + string.Join(", ", mirrorReasons)
                        + "; khong tinh la chi tiet giong nhau.";
                }
                else if (sameFolded && sameMaterial && sameThickness)
                {
                    group.Status = "SAME FULL";
                    group.Note = flatExactCount == 1 && foldedInvariantCount == 1
                        ? "Giong Flat-Pattern, trang thai gap, vat lieu va chieu day."
                        : "Giong hinh hoc trong dung sai so sanh, trang thai gap, vat lieu va chieu day.";
                }
                else if (sameFolded)
                {
                    group.Status = "SAME GEOMETRY";
                    List<string> differences = new List<string>();
                    if (!sameMaterial)
                        differences.Add("vat lieu");
                    if (!sameThickness)
                        differences.Add("chieu day");
                    group.Note = "Giong hinh hoc nhung khac " + string.Join(" va ", differences) + ".";
                }
                else
                {
                    group.Status = "SAME FLAT";
                    group.Note = "Giong bien dang Flat-Pattern nhung trang thai gap khac nhau.";
                }

                result.Groups.Add(group);
                Log("GROUP " + group.GroupNumber + " | " + group.Status
                    + " | Buhin=" + string.Join(",", groupItems.Select(item => item.BuhinNo))
                    + " | flatDirectionCount=" + flatDirectionCount
                    + " | flatExactCount=" + flatExactCount
                    + " | foldedInvariantCount=" + foldedInvariantCount
                    + " | foldedCandidateCount=" + foldedCandidateCount
                    + " | foldedDirectionCount=" + foldedDirectionCount
                    + " | foldedChiralityCount=" + foldedChiralityCount
                    + " | mirrorFeatureCount=" + mirrorFeatureCount
                    + " | featureOperationCount=" + featureOperationCount);
                foreach (SamePartItemResult item in groupItems)
                {
                    Log("  GROUP PART " + item.BuhinNo
                        + " | foldedChirality=" + item.FoldedChiralitySignature
                        + " | flatCandidate=" + ShortHash(item.FlatCandidateSignature)
                        + " | foldedCandidate=" + ShortHash(item.FoldedCandidateSignature)
                        + " | mirrorFeature=" + item.FeatureMirrorSignature
                        + " | operation=" + ShortHash(item.FeatureOperationSignature));
                    Log("    FEATURES: " + item.FeatureTrace);
                }
                foreach (SamePartItemResult item in groupItems)
                {
                    result.HighlightRowIndexes.Add(item.BomRowIndex);
                    groupedItems.Add(item);
                }
            }

            // Lop dung sai so sanh tung gia tri so, khong hash bang tuyet doi.
            // Moi phan tu them vao nhom phai dat dung sai voi tat ca phan tu da co.
            List<SamePartItemResult> remaining = items
                .Where(item => !groupedItems.Contains(item))
                .Where(item => item.FlatBroadMetrics != null && item.FoldedBroadMetrics != null)
                .OrderBy(item => SortBuhinNo(item.BuhinNo))
                .ThenBy(item => item.PartPath, StringComparer.OrdinalIgnoreCase)
                .ToList();
            HashSet<SamePartItemResult> toleranceUsed = new HashSet<SamePartItemResult>();

            foreach (SamePartItemResult seed in remaining)
            {
                if (toleranceUsed.Contains(seed))
                    continue;

                List<SamePartItemResult> groupItems = new List<SamePartItemResult> { seed };
                foreach (SamePartItemResult candidate in remaining)
                {
                    if (ReferenceEquals(candidate, seed) || toleranceUsed.Contains(candidate))
                        continue;
                    if (groupItems.All(existing => AreSameWithinTolerance(existing, candidate)))
                        groupItems.Add(candidate);
                }
                if (groupItems.Count < 2)
                    continue;

                SamePartGroupResult group = new SamePartGroupResult
                {
                    GroupNumber = groupNumber++,
                    Status = "SAME TOLERANCE",
                    Note = "Giong nhau trong dung sai nguoi dung da nhap."
                };
                group.Items.AddRange(groupItems);
                result.Groups.Add(group);
                Log("GROUP " + group.GroupNumber + " | SAME TOLERANCE | Buhin="
                    + string.Join(",", groupItems.Select(item => item.BuhinNo)));
                foreach (SamePartItemResult item in groupItems)
                {
                    Log("  GROUP PART " + item.BuhinNo
                        + " | flatBroad=" + item.FlatBroadSummary
                        + " | foldedBroad=" + item.FoldedBroadSummary);
                    Log("    FEATURES: " + item.FeatureTrace);
                    result.HighlightRowIndexes.Add(item.BomRowIndex);
                    groupedItems.Add(item);
                    toleranceUsed.Add(item);
                }
            }
        }

        private bool AreSameWithinTolerance(
            SamePartItemResult first,
            SamePartItemResult second)
        {
            if (first == null || second == null)
                return false;
            if (!string.Equals(first.MaterialKey ?? "", second.MaterialKey ?? "",
                StringComparison.OrdinalIgnoreCase))
                return false;
            if (!string.Equals(first.ThicknessKey ?? "", second.ThicknessKey ?? "",
                StringComparison.OrdinalIgnoreCase))
                return false;
            if (!string.Equals(first.FeatureOperationSignature ?? "",
                second.FeatureOperationSignature ?? "", StringComparison.Ordinal))
                return false;
            if (!AreChiralitiesCompatible(first.FoldedChiralitySignature,
                second.FoldedChiralitySignature))
                return false;
            return AreMetricsWithinTolerance(first.FlatBroadMetrics, second.FlatBroadMetrics)
                && AreMetricsWithinTolerance(first.FoldedBroadMetrics, second.FoldedBroadMetrics)
                && AreFlatProfilesWithinTolerance(first.FlatProfileMetrics, second.FlatProfileMetrics);
        }

        private static bool AreChiralitiesCompatible(string first, string second)
        {
            string a = first ?? "";
            string b = second ?? "";
            bool aKnown = a == "POS" || a == "NEG";
            bool bKnown = b == "POS" || b == "NEG";
            return !aKnown || !bKnown || string.Equals(a, b, StringComparison.Ordinal);
        }

        private bool AreMetricsWithinTolerance(
            BroadGeometryMetrics first,
            BroadGeometryMetrics second)
        {
            if (first == null || second == null || first.BodyCount != second.BodyCount)
                return false;
            if (!WithinAbsoluteOrRelative(first.AreaMm2, second.AreaMm2,
                toleranceOptions.AreaAbsoluteMm2,
                toleranceOptions.AreaRelativePercent / 100.0))
                return false;
            if (Math.Abs(first.EdgeLengthMm - second.EdgeLengthMm) > toleranceOptions.EdgeLengthMm)
                return false;
            if (!WithinAbsoluteOrRelative(first.VolumeMm3, second.VolumeMm3,
                toleranceOptions.VolumeAbsoluteMm3,
                toleranceOptions.VolumeRelativePercent / 100.0))
                return false;
            if (first.PrincipalMoments.Count != second.PrincipalMoments.Count)
                return false;
            for (int i = 0; i < first.PrincipalMoments.Count; i++)
            {
                if (!WithinAbsoluteOrRelative(
                    first.PrincipalMoments[i], second.PrincipalMoments[i], 1e-12,
                    toleranceOptions.PrincipalMomentRelativePercent / 100.0))
                    return false;
            }
            return true;
        }

        private static bool WithinAbsoluteOrRelative(
            double first,
            double second,
            double absoluteTolerance,
            double relativeTolerance)
        {
            double difference = Math.Abs(first - second);
            double relativeLimit = Math.Max(Math.Abs(first), Math.Abs(second)) * relativeTolerance;
            return difference <= Math.Max(absoluteTolerance, relativeLimit);
        }

        private bool AreFlatProfilesWithinTolerance(
            FlatProfileMetrics first,
            FlatProfileMetrics second)
        {
            if (first == null || second == null)
                return false;
            if (first.InnerLoopCount != second.InnerLoopCount)
                return false;
            if (!AreSortedValuesWithin(first.CenterDistancesMm, second.CenterDistancesMm,
                toleranceOptions.HoleLinearMm))
                return false;

            bool[] used = new bool[second.InnerLoops.Count];
            foreach (InternalLoopMetrics source in first.InnerLoops)
            {
                int matchIndex = -1;
                for (int i = 0; i < second.InnerLoops.Count; i++)
                {
                    if (!used[i] && AreInternalLoopsWithinTolerance(source, second.InnerLoops[i]))
                    {
                        matchIndex = i;
                        break;
                    }
                }
                if (matchIndex < 0)
                    return false;
                used[matchIndex] = true;
            }
            return true;
        }

        private bool AreInternalLoopsWithinTolerance(
            InternalLoopMetrics first,
            InternalLoopMetrics second)
        {
            if (first == null || second == null)
                return false;
            if (!string.Equals(first.TopologyKey ?? "", second.TopologyKey ?? "",
                StringComparison.Ordinal))
                return false;
            if (Math.Abs(first.PerimeterMm - second.PerimeterMm) > toleranceOptions.HoleLinearMm)
                return false;
            if (!AreSortedValuesWithin(first.EdgeLengthsMm, second.EdgeLengthsMm,
                toleranceOptions.HoleLinearMm))
                return false;
            if (!AreSortedValuesWithin(first.RadiiMm, second.RadiiMm,
                toleranceOptions.HoleRadiusMm))
                return false;
            return AreSortedValuesWithin(first.OuterDistancesMm, second.OuterDistancesMm,
                toleranceOptions.HoleLinearMm);
        }

        private static bool AreSortedValuesWithin(
            IList<double> first,
            IList<double> second,
            double tolerance)
        {
            if (first == null || second == null || first.Count != second.Count)
                return false;
            for (int i = 0; i < first.Count; i++)
            {
                if (Math.Abs(first[i] - second[i]) > tolerance)
                    return false;
            }
            return true;
        }

        private List<SamePartCheckTarget> GetTargets(DataGridViewRow row)
        {
            List<SamePartCheckTarget> targets = new List<SamePartCheckTarget>();
            AddTargets(targets, row.Tag, row);
            return targets;
        }

        private void AddTargets(List<SamePartCheckTarget> targets, object source, DataGridViewRow row)
        {
            if (source == null)
                return;
            IEnumerable enumerable = source as IEnumerable;
            if (enumerable != null && !(source is string) && !(source is Component2))
            {
                foreach (object value in enumerable)
                    AddTargets(targets, value, row);
                return;
            }

            Component2 component = source as Component2;
            if (component != null)
            {
                string path = "";
                string configuration = "";
                string name = "";
                try { path = component.GetPathName() ?? ""; } catch { }
                try { configuration = component.ReferencedConfiguration ?? ""; } catch { }
                try { name = component.Name2 ?? ""; } catch { }
                targets.Add(CreateTarget(row, path, configuration, name));
                return;
            }

            string pathText = source as string;
            if (!string.IsNullOrWhiteSpace(pathText))
                targets.Add(CreateTarget(row, pathText, "", GetCellText(row, 5)));
        }

        private SamePartCheckTarget CreateTarget(
            DataGridViewRow row,
            string path,
            string configuration,
            string componentName)
        {
            return new SamePartCheckTarget
            {
                BomRowIndex = row.Index,
                BuhinNo = GetCellText(row, 1),
                Material = GetCellText(row, 2),
                Thickness = GetCellText(row, 3),
                BomFileName = GetCellText(row, 5),
                ComponentName = componentName ?? "",
                PartPath = path ?? "",
                Configuration = configuration ?? ""
            };
        }

        private SamePartItemResult CreateErrorFromRow(DataGridViewRow row, string error)
        {
            return new SamePartItemResult
            {
                BomRowIndex = row.Index,
                BuhinNo = GetCellText(row, 1),
                Material = GetCellText(row, 2),
                Thickness = GetCellText(row, 3),
                BomFileName = GetCellText(row, 5),
                Error = error
            };
        }

        private string FindSourceConfiguration(ModelDoc2 model, string requested)
        {
            string requestedName = (requested ?? "").Trim();
            object[] names = ToObjectArray(model.GetConfigurationNames());
            if (requestedName.Length > 0 &&
                !requestedName.ToUpperInvariant().Contains("FLAT-PATTERN") &&
                names.Any(value => string.Equals(Convert.ToString(value), requestedName, StringComparison.OrdinalIgnoreCase)))
            {
                return requestedName;
            }

            foreach (object value in names)
            {
                string name = Convert.ToString(value ?? "").Trim();
                if (name.Length > 0 && !name.ToUpperInvariant().Contains("FLAT-PATTERN"))
                    return name;
            }
            return "";
        }

        private string FindFlatConfiguration(ModelDoc2 model, string sourceConfiguration)
        {
            object[] names = ToObjectArray(model.GetConfigurationNames());
            string source = (sourceConfiguration ?? "").Trim().ToUpperInvariant();
            string fallback = "";
            foreach (object value in names)
            {
                string name = Convert.ToString(value ?? "").Trim();
                string upper = name.ToUpperInvariant();
                if (!upper.Contains("FLAT-PATTERN"))
                    continue;
                if (fallback.Length == 0)
                    fallback = name;
                if (source.Length > 0 && upper.StartsWith(source, StringComparison.OrdinalIgnoreCase))
                    return name;
            }
            return fallback;
        }

        private bool TryShowConfiguration(ModelDoc2 model, string configurationName)
        {
            if (model == null || string.IsNullOrWhiteSpace(configurationName))
                return false;
            bool shown = false;
            try { shown = model.ShowConfiguration2(configurationName); } catch { }
            try
            {
                Configuration active = model.ConfigurationManager == null
                    ? null
                    : model.ConfigurationManager.ActiveConfiguration;
                return shown || (active != null && string.Equals(
                    active.Name, configurationName, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return shown;
            }
        }

        private string CreateTemporaryPartCopy(string partPath)
        {
            try
            {
                string directory = Path.Combine(Path.GetTempPath(), "ADDIN_CHECK_SAME_PART", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(directory);
                // SolidWorks khong cho mo hai document co cung title, ke ca khi
                // chung nam o hai thu muc khac nhau. Dat title rieng cho ban sao
                // tam de tranh swFileWithSameTitleAlreadyOpen (Error=65536).
                string uniqueFileName = "CHK_" +
                    Guid.NewGuid().ToString("N").Substring(0, 12) + "_" +
                    Path.GetFileName(partPath);
                string destination = Path.Combine(directory, uniqueFileName);
                File.Copy(partPath, destination, true);
                Log("Create temp: source=" + partPath + "; copy=" + destination);
                return destination;
            }
            catch (Exception ex)
            {
                Log("Create temp ERROR: " + ex.Message);
                return "";
            }
        }

        private void DeleteTemporaryPartCopy(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;
            try
            {
                string directory = Path.GetDirectoryName(path);
                if (File.Exists(path))
                    File.Delete(path);
                if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
                    Directory.Delete(directory, true);
            }
            catch (Exception ex)
            {
                Log("Delete temp ERROR: " + ex.Message);
            }
        }

        private void RestoreDocument(ModelDoc2 originalDocument)
        {
            if (swApp == null || originalDocument == null)
                return;
            try
            {
                int errors = 0;
                swApp.ActivateDoc3(originalDocument.GetTitle(), false,
                    (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, ref errors);
            }
            catch { }
        }

        private int CountCheckedRows()
        {
            int count = 0;
            foreach (DataGridViewRow row in gridBom.Rows)
            {
                if (!row.IsNewRow && Convert.ToBoolean(row.Cells[0].Value ?? false))
                    count++;
            }
            return count;
        }

        private static void ThrowIfCanceled(Func<bool> callback)
        {
            if (callback != null && callback())
                throw new OperationCanceledException();
        }

        private static bool IsCancellationRequested(Func<bool> callback)
        {
            return callback != null && callback();
        }

        private static string GetCellText(DataGridViewRow row, int columnIndex)
        {
            if (row == null || columnIndex < 0 || columnIndex >= row.Cells.Count)
                return "";
            return Convert.ToString(row.Cells[columnIndex].Value ?? "").Trim();
        }

        private static string NormalizeMaterial(string value)
        {
            string text = (value ?? "").Trim().ToUpperInvariant();
            if (text.Contains("SUS") || text.Contains("STAINLESS") || text.Contains("ステンレス"))
                return "SUS";
            if (text.Contains("AL") || text.Contains("A1100") || text.Contains("A3003") ||
                text.Contains("A5052") || text.Contains("アルミ"))
                return "AL";
            if (text.Contains("CU") || text.Contains("COPPER") || text.Contains("銅"))
                return "CU";
            if (text.Contains("ST") || text.Contains("STEEL") || text.Contains("スチール"))
                return "ST";
            return Regex.Replace(text, @"\s+", "");
        }

        private static string NormalizeThickness(string value)
        {
            Match match = Regex.Match(value ?? "", @"[-+]?\d+(?:[\.,]\d+)?");
            if (!match.Success)
                return (value ?? "").Trim().ToUpperInvariant();
            double number;
            if (!double.TryParse(match.Value.Replace(',', '.'), NumberStyles.Float,
                CultureInfo.InvariantCulture, out number))
            {
                return (value ?? "").Trim().ToUpperInvariant();
            }
            return number.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static decimal SortBuhinNo(string value)
        {
            decimal number;
            return decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out number)
                ? number
                : decimal.MaxValue;
        }

        private static object[] ToObjectArray(object value)
        {
            if (value == null)
                return new object[0];
            object[] objects = value as object[];
            if (objects != null)
                return objects;
            Array array = value as Array;
            if (array == null)
                return new object[0];
            object[] result = new object[array.Length];
            for (int i = 0; i < array.Length; i++)
                result[i] = array.GetValue(i);
            return result;
        }

        private void LogItem(SamePartItemResult item)
        {
            if (item == null)
                return;
            Log("PART: " + item.BuhinNo + " | " + item.PartPath);
            Log("  Config gap: " + item.Configuration);
            Log("  Config flat: " + item.FlatConfiguration);
            Log("  Material/Thickness: " + item.MaterialKey + " / " + item.ThicknessKey);
            Log("  Folded invariant: " + ShortHash(item.FoldedSignature));
            Log("  Folded candidate: " + ShortHash(item.FoldedCandidateSignature));
            Log("  Folded broad: " + item.FoldedBroadSummary + " | " + ShortHash(item.FoldedBroadSignature));
            Log("  Folded direction: " + ShortHash(item.FoldedOrientationSignature));
            Log("  Folded chirality: " + item.FoldedChiralitySignature);
            Log("  Flat invariant: " + ShortHash(item.FlatSignature));
            Log("  Flat candidate: " + ShortHash(item.FlatCandidateSignature));
            Log("  Flat broad: " + item.FlatBroadSummary + " | " + ShortHash(item.FlatBroadSignature));
            Log("  Flat profile: " + item.FlatProfileSummary);
            Log("  Flat direction: " + ShortHash(item.FlatOrientationSignature));
            Log("  Flat chirality: " + item.FlatChiralitySignature);
            Log("  Mirror feature: " + item.FeatureMirrorSignature);
            Log("  Feature operation: " + ShortHash(item.FeatureOperationSignature));
            if (!string.IsNullOrWhiteSpace(item.Error))
                Log("  ERROR: " + item.Error);
        }

        private void Log(string text)
        {
            string line = "[CHECK SAME PART] " + (text ?? "");
            debugLines.Add(DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + " " + line);
            Debug.WriteLine(line);
        }

        private string SaveDebugLog()
        {
            try
            {
                string desktop = System.Environment.GetFolderPath(System.Environment.SpecialFolder.DesktopDirectory);
                string path = Path.Combine(desktop,
                    "CHECK_SAME_PART_DEBUG_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
                File.WriteAllLines(path, debugLines, new UTF8Encoding(false));
                return path;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[CHECK SAME PART] Save debug ERROR: " + ex.Message);
                return "";
            }
        }

        private static string ShortHash(string value)
        {
            string text = value ?? "";
            return text.Length <= 20 ? text : text.Substring(0, 20);
        }
    }

    internal static class GeometrySignatureBuilder
    {
        private const double LengthScale = 1000000.0;
        private const double AreaScale = 100000000.0;
        // Dung sai nhom ung vien: 0.01 mm cho chieu dai va 0.1 mm2 cho dien tich.
        // Chu ky chinh xac van duoc giu lai de debug va xac nhan cap trung tuyet doi.
        private const double CandidateLengthScale = 100000.0;
        private const double CandidateAreaScale = 10000000.0;

        public static string Create(ModelDoc2 model, Func<bool> isCancellationRequested)
        {
            PartDoc part = model as PartDoc;
            if (part == null)
                return "";

            object[] bodies = ToObjectArray(part.GetBodies2((int)swBodyType_e.swSolidBody, true));
            if (bodies.Length == 0)
                return "";

            List<string> bodyTokens = new List<string>();
            foreach (object bodyObject in bodies)
            {
                ThrowIfCanceled(isCancellationRequested);
                Body2 body = bodyObject as Body2;
                if (body == null)
                    continue;
                bodyTokens.Add(CreateBodyToken(body, isCancellationRequested));
            }
            bodyTokens.Sort(StringComparer.Ordinal);
            return Hash(string.Join("||", bodyTokens));
        }

        public static string CreateCandidate(ModelDoc2 model, Func<bool> isCancellationRequested)
        {
            PartDoc part = model as PartDoc;
            if (part == null)
                return "";

            List<string> bodyTokens = new List<string>();
            foreach (object bodyObject in ToObjectArray(
                part.GetBodies2((int)swBodyType_e.swSolidBody, true)))
            {
                ThrowIfCanceled(isCancellationRequested);
                Body2 body = bodyObject as Body2;
                if (body != null)
                    bodyTokens.Add(CreateCandidateBodyToken(body, isCancellationRequested));
            }
            bodyTokens.Sort(StringComparer.Ordinal);
            return Hash(string.Join("||", bodyTokens));
        }

        public static string CreateBroad(
            ModelDoc2 model,
            Func<bool> isCancellationRequested,
            out string summary,
            out BroadGeometryMetrics metrics)
        {
            summary = "";
            metrics = new BroadGeometryMetrics();
            PartDoc part = model as PartDoc;
            if (part == null)
                return "";

            List<string> bodyTokens = new List<string>();
            foreach (object bodyObject in ToObjectArray(
                part.GetBodies2((int)swBodyType_e.swSolidBody, true)))
            {
                ThrowIfCanceled(isCancellationRequested);
                Body2 body = bodyObject as Body2;
                if (body == null)
                    continue;
                metrics.BodyCount++;

                double area = 0;
                foreach (object faceObject in ToObjectArray(body.GetFaces()))
                {
                    Face2 face = faceObject as Face2;
                    if (face == null) continue;
                    try { area += face.GetArea(); } catch { }
                }

                double edgeLength = 0;
                foreach (object edgeObject in ToObjectArray(body.GetEdges()))
                {
                    Edge edge = edgeObject as Edge;
                    if (edge == null) continue;
                    try
                    {
                        Curve curve = edge.GetCurve() as Curve;
                        CurveParamData parameters = edge.GetCurveParams3();
                        if (curve != null && parameters != null)
                            edgeLength += curve.GetLength3(parameters.UMinValue, parameters.UMaxValue);
                    }
                    catch { }
                }

                List<long> massTokens = new List<long>();
                try
                {
                    object value = body.GetMassProperties(1.0);
                    Array array = value as Array;
                    if (array != null)
                    {
                        if (array.Length > 3)
                            metrics.VolumeMm3 += Math.Abs(Convert.ToDouble(
                                array.GetValue(3), CultureInfo.InvariantCulture)) * 1000000000.0;
                        if (array.Length >= 12)
                        {
                            double ixx = Convert.ToDouble(array.GetValue(6), CultureInfo.InvariantCulture);
                            double iyy = Convert.ToDouble(array.GetValue(7), CultureInfo.InvariantCulture);
                            double izz = Convert.ToDouble(array.GetValue(8), CultureInfo.InvariantCulture);
                            double ixy = Convert.ToDouble(array.GetValue(9), CultureInfo.InvariantCulture);
                            double iyz = Convert.ToDouble(array.GetValue(10), CultureInfo.InvariantCulture);
                            double izx = Convert.ToDouble(array.GetValue(11), CultureInfo.InvariantCulture);
                            metrics.PrincipalMoments.AddRange(EigenvaluesSymmetric3x3(
                                ixx, iyy, izz, ixy, iyz, izx));
                        }
                        // Bo toa do tam khoi (0..2), chi giu volume/mass va cac
                        // moment bat bien. Sap xep 3 moment cuoi de bo huong truc.
                        List<double> raw = new List<double>();
                        for (int i = 3; i < array.Length; i++)
                            raw.Add(Convert.ToDouble(array.GetValue(i), CultureInfo.InvariantCulture));
                        if (raw.Count >= 3)
                        {
                            List<long> tail = raw.Skip(Math.Max(0, raw.Count - 3))
                                .Select(number => Quantize(Math.Abs(number), 1000000000.0))
                                .OrderBy(number => number)
                                .ToList();
                            foreach (double number in raw.Take(Math.Max(0, raw.Count - 3)))
                                massTokens.Add(Quantize(Math.Abs(number), 1000000000.0));
                            massTokens.AddRange(tail);
                        }
                        else
                        {
                            massTokens.AddRange(raw.Select(number =>
                                Quantize(Math.Abs(number), 1000000000.0)));
                        }
                    }
                }
                catch { }

                metrics.AreaMm2 += area * 1000000.0;
                metrics.EdgeLengthMm += edgeLength * 1000.0;

                string token = "A" + Quantize(area, 1000000.0)
                    + "|L" + Quantize(edgeLength, 10000.0)
                    + "|M" + string.Join(",", massTokens);
                bodyTokens.Add(token);
            }
            metrics.PrincipalMoments.Sort();
            bodyTokens.Sort(StringComparer.Ordinal);
            summary = string.Join("||", bodyTokens);
            return summary.Length == 0 ? "" : Hash(summary);
        }

        public static FlatProfileMetrics CreateFlatProfileMetrics(
            ModelDoc2 model,
            Func<bool> isCancellationRequested,
            out string exactSignature,
            out string candidateSignature,
            out string summary)
        {
            FlatProfileMetrics result = new FlatProfileMetrics();
            exactSignature = "";
            candidateSignature = "";
            summary = "";
            PartDoc part = model as PartDoc;
            if (part == null)
                return result;

            foreach (object bodyObject in ToObjectArray(
                part.GetBodies2((int)swBodyType_e.swSolidBody, true)))
            {
                ThrowIfCanceled(isCancellationRequested);
                Body2 body = bodyObject as Body2;
                Face2 mainFace = FindLargestPlanarFace(body);
                if (mainFace == null)
                    continue;

                List<double[]> outerReferencePoints = new List<double[]>();
                List<InternalLoopMetrics> bodyInnerLoops = new List<InternalLoopMetrics>();
                foreach (object loopObject in ToObjectArray(mainFace.GetLoops()))
                {
                    ThrowIfCanceled(isCancellationRequested);
                    Loop2 loop = loopObject as Loop2;
                    if (loop == null)
                        continue;

                    bool isOuter = false;
                    try { isOuter = loop.IsOuter(); } catch { }
                    List<double[]> loopReferencePoints;
                    InternalLoopMetrics metric = ReadLoopMetrics(
                        loop,
                        isCancellationRequested,
                        out loopReferencePoints);
                    if (isOuter)
                    {
                        outerReferencePoints.AddRange(loopReferencePoints);
                        continue;
                    }
                    if (metric != null)
                        bodyInnerLoops.Add(metric);
                }

                outerReferencePoints = DistinctPoints(outerReferencePoints, 1e-9);
                foreach (InternalLoopMetrics metric in bodyInnerLoops)
                {
                    foreach (double[] point in outerReferencePoints)
                        metric.OuterDistancesMm.Add(Distance(metric.CenterM, point) * 1000.0);
                    metric.OuterDistancesMm.Sort();
                    result.InnerLoops.Add(metric);
                }
            }

            result.InnerLoopCount = result.InnerLoops.Count;
            for (int i = 0; i < result.InnerLoops.Count; i++)
            {
                for (int j = i + 1; j < result.InnerLoops.Count; j++)
                {
                    result.CenterDistancesMm.Add(Distance(
                        result.InnerLoops[i].CenterM,
                        result.InnerLoops[j].CenterM) * 1000.0);
                }
            }
            result.CenterDistancesMm.Sort();
            exactSignature = BuildFlatProfileToken(result, 1000.0);
            candidateSignature = BuildFlatProfileToken(result, 100.0);
            summary = "loops=" + result.InnerLoopCount
                + ", centerDistances=" + result.CenterDistancesMm.Count;
            return result;
        }

        private static Face2 FindLargestPlanarFace(Body2 body)
        {
            if (body == null)
                return null;
            Face2 largest = null;
            double largestArea = 0;
            foreach (object faceObject in ToObjectArray(body.GetFaces()))
            {
                Face2 face = faceObject as Face2;
                if (face == null)
                    continue;
                try
                {
                    Surface surface = face.GetSurface() as Surface;
                    if (surface == null || !surface.IsPlane())
                        continue;
                    double area = face.GetArea();
                    if (area > largestArea)
                    {
                        largestArea = area;
                        largest = face;
                    }
                }
                catch { }
            }
            return largest;
        }

        private static InternalLoopMetrics ReadLoopMetrics(
            Loop2 loop,
            Func<bool> isCancellationRequested,
            out List<double[]> referencePoints)
        {
            referencePoints = new List<double[]>();
            if (loop == null)
                return null;

            InternalLoopMetrics metric = new InternalLoopMetrics();
            int lineCount = 0;
            int circleCount = 0;
            int otherCount = 0;
            object[] edges = ToObjectArray(loop.GetEdges());
            foreach (object edgeObject in edges)
            {
                ThrowIfCanceled(isCancellationRequested);
                Edge edge = edgeObject as Edge;
                if (edge == null)
                {
                    otherCount++;
                    continue;
                }

                Curve curve = null;
                try { curve = edge.GetCurve() as Curve; } catch { }
                double lengthMm = 0;
                if (curve != null)
                {
                    try
                    {
                        CurveParamData parameters = edge.GetCurveParams3();
                        if (parameters != null)
                            lengthMm = curve.GetLength3(
                                parameters.UMinValue,
                                parameters.UMaxValue) * 1000.0;
                    }
                    catch { }

                    try
                    {
                        if (curve.IsLine())
                        {
                            lineCount++;
                        }
                        else if (curve.IsCircle())
                        {
                            circleCount++;
                            double[] circle = curve.CircleParams as double[];
                            if (circle != null && circle.Length > 6)
                            {
                                metric.RadiiMm.Add(Math.Abs(circle[6]) * 1000.0);
                                referencePoints.Add(new[] { circle[0], circle[1], circle[2] });
                            }
                        }
                        else
                        {
                            otherCount++;
                        }
                    }
                    catch { otherCount++; }
                }
                else
                {
                    otherCount++;
                }

                metric.PerimeterMm += lengthMm;
                metric.EdgeLengthsMm.Add(lengthMm);
                AddEdgeVertexPoint(edge, true, referencePoints);
                AddEdgeVertexPoint(edge, false, referencePoints);
            }

            referencePoints = DistinctPoints(referencePoints, 1e-9);
            if (referencePoints.Count == 0)
                return null;
            metric.CenterM = new[]
            {
                (referencePoints.Min(point => point[0]) + referencePoints.Max(point => point[0])) * 0.5,
                (referencePoints.Min(point => point[1]) + referencePoints.Max(point => point[1])) * 0.5,
                (referencePoints.Min(point => point[2]) + referencePoints.Max(point => point[2])) * 0.5
            };
            metric.EdgeLengthsMm.Sort();
            metric.RadiiMm.Sort();
            metric.TopologyKey = "E" + edges.Length
                + "|L" + lineCount
                + "|C" + circleCount
                + "|U" + otherCount;
            return metric;
        }

        private static void AddEdgeVertexPoint(
            Edge edge,
            bool start,
            List<double[]> points)
        {
            if (edge == null || points == null)
                return;
            try
            {
                Vertex vertex = start
                    ? edge.GetStartVertex() as Vertex
                    : edge.GetEndVertex() as Vertex;
                double[] point = vertex == null ? null : vertex.GetPoint() as double[];
                if (point != null && point.Length >= 3)
                    points.Add(new[] { point[0], point[1], point[2] });
            }
            catch { }
        }

        private static List<double[]> DistinctPoints(
            IEnumerable<double[]> points,
            double tolerance)
        {
            List<double[]> result = new List<double[]>();
            foreach (double[] point in points ?? Enumerable.Empty<double[]>())
            {
                if (point == null || point.Length < 3)
                    continue;
                if (!result.Any(existing => Distance(existing, point) <= tolerance))
                    result.Add(point);
            }
            return result;
        }

        private static string BuildFlatProfileToken(
            FlatProfileMetrics metrics,
            double scalePerMm)
        {
            if (metrics == null)
                return "";
            List<string> loops = metrics.InnerLoops.Select(loop =>
                loop.TopologyKey
                + "|P" + Quantize(loop.PerimeterMm, scalePerMm)
                + "|E" + string.Join(",", loop.EdgeLengthsMm.Select(
                    value => Quantize(value, scalePerMm)))
                + "|R" + string.Join(",", loop.RadiiMm.Select(
                    value => Quantize(value, scalePerMm)))
                + "|O" + string.Join(",", loop.OuterDistancesMm.Select(
                    value => Quantize(value, scalePerMm))))
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToList();
            return Hash("H" + metrics.InnerLoopCount
                + "|L=" + string.Join(";", loops)
                + "|D=" + string.Join(",", metrics.CenterDistancesMm.Select(
                    value => Quantize(value, scalePerMm))));
        }

        private static IEnumerable<double> EigenvaluesSymmetric3x3(
            double ixx,
            double iyy,
            double izz,
            double ixy,
            double iyz,
            double izx)
        {
            double[,] matrix =
            {
                { ixx, ixy, izx },
                { ixy, iyy, iyz },
                { izx, iyz, izz }
            };
            for (int iteration = 0; iteration < 32; iteration++)
            {
                int p = 0;
                int q = 1;
                double largest = Math.Abs(matrix[0, 1]);
                if (Math.Abs(matrix[0, 2]) > largest)
                {
                    p = 0; q = 2; largest = Math.Abs(matrix[0, 2]);
                }
                if (Math.Abs(matrix[1, 2]) > largest)
                {
                    p = 1; q = 2; largest = Math.Abs(matrix[1, 2]);
                }
                if (largest < 1e-20)
                    break;

                double angle = 0.5 * Math.Atan2(
                    2.0 * matrix[p, q], matrix[q, q] - matrix[p, p]);
                double cosine = Math.Cos(angle);
                double sine = Math.Sin(angle);
                double app = matrix[p, p];
                double aqq = matrix[q, q];
                double apq = matrix[p, q];
                matrix[p, p] = cosine * cosine * app
                    - 2.0 * sine * cosine * apq + sine * sine * aqq;
                matrix[q, q] = sine * sine * app
                    + 2.0 * sine * cosine * apq + cosine * cosine * aqq;
                matrix[p, q] = matrix[q, p] = 0.0;
                for (int r = 0; r < 3; r++)
                {
                    if (r == p || r == q)
                        continue;
                    double arp = matrix[r, p];
                    double arq = matrix[r, q];
                    matrix[r, p] = matrix[p, r] = cosine * arp - sine * arq;
                    matrix[r, q] = matrix[q, r] = sine * arp + cosine * arq;
                }
            }
            return new[]
            {
                Math.Abs(matrix[0, 0]),
                Math.Abs(matrix[1, 1]),
                Math.Abs(matrix[2, 2])
            }.OrderBy(value => value);
        }

        // Chu ky thuan/nghich doc lap voi vi tri va phep xoay. No thay doi dau
        // khi hinh hoc bi mirror, nhung khong thay doi khi chi tiet chi bi xoay.
        public static string CreateChirality(ModelDoc2 model, Func<bool> isCancellationRequested)
        {
            PartDoc part = model as PartDoc;
            if (part == null)
                return "";

            List<string> tokens = new List<string>();
            foreach (object bodyObject in ToObjectArray(
                part.GetBodies2((int)swBodyType_e.swSolidBody, true)))
            {
                ThrowIfCanceled(isCancellationRequested);
                Body2 body = bodyObject as Body2;
                if (body == null)
                    continue;
                List<double[]> points = ReadVertices(body);
                string token = CreateBodyChirality(points, isCancellationRequested);
                if (token.Length > 0)
                    tokens.Add(token);
            }
            if (tokens.Count == 0)
                return "AMBIG";
            if (tokens.All(value => value == "PLANAR"))
                return "PLANAR";
            tokens.Sort(StringComparer.Ordinal);
            return string.Join("|", tokens);
        }

        public static void CreateFeatureDiagnostics(
            ModelDoc2 model,
            out string mirrorSignature,
            out string operationSignature,
            out string trace)
        {
            List<string> mirrorTokens = new List<string>();
            List<string> operationTokens = new List<string>();
            List<string> traceTokens = new List<string>();
            int count = 0;
            try
            {
                for (Feature feature = model == null ? null : model.FirstFeature() as Feature;
                    feature != null && count < 250;
                    feature = feature.GetNextFeature() as Feature)
                {
                    count++;
                    string name = "";
                    string type = "";
                    try { name = feature.Name ?? ""; } catch { }
                    try { type = feature.GetTypeName2() ?? ""; } catch { }
                    string compact = count.ToString(CultureInfo.InvariantCulture)
                        + ":" + type + ":" + name;
                    traceTokens.Add(compact);

                    if (IsOperationFeatureType(type))
                        operationTokens.Add(type.ToUpperInvariant());

                    string search = (type + " " + name).ToUpperInvariant();
                    if (search.Contains("MIRROR") ||
                        search.Contains("DERIVED") ||
                        search.Contains("STOCK") ||
                        search.Contains("MIRRORED") ||
                        search.Contains("ミラー") ||
                        search.Contains("鏡像"))
                    {
                        // Khong dua ten feature vao chu ky de viec doi ten khong lam sai ket qua.
                        mirrorTokens.Add(type.ToUpperInvariant());
                    }
                }
            }
            catch (Exception ex)
            {
                traceTokens.Add("TREE_ERROR:" + ex.GetType().Name + ":" + ex.Message);
            }

            mirrorTokens.Sort(StringComparer.Ordinal);
            operationTokens.Sort(StringComparer.Ordinal);
            mirrorSignature = mirrorTokens.Count == 0
                ? "NONE"
                : Hash(string.Join("|", mirrorTokens));
            operationSignature = operationTokens.Count == 0
                ? "NONE"
                : Hash(string.Join("|", operationTokens));
            trace = string.Join("; ", traceTokens);
        }

        private static bool IsOperationFeatureType(string type)
        {
            string value = (type ?? "").ToUpperInvariant();
            if (value.Length == 0)
                return false;
            string[] ignored =
            {
                "FOLDER", "REFPLANE", "ORIGIN", "PROFILEFEATURE",
                "CUTLIST", "FLATPATTERN", "MATERIAL", "HISTORY",
                "COMMENT", "SENSOR", "ENVFOLDER", "DETAILCABINET"
            };
            if (ignored.Any(token => value.Contains(token)))
                return false;
            return value.Contains("CUT")
                || value.Contains("EXTRU")
                || value == "ICE"
                || value.Contains("FLANGE")
                || value.Contains("BEND")
                || value.Contains("SHEETMETAL")
                || value.Contains("PATTERN")
                || value.Contains("MIRROR")
                || value.Contains("DERIVED")
                || value.Contains("STOCK")
                || value.Contains("HOLE");
        }

        // Chu ky nay co tinh den huong truc cua hinh hoc. No duoc tach khoi
        // chu ky invariant de mirror khong bi xep nham vao SAME.
        public static string CreateOrientation(ModelDoc2 model, Func<bool> isCancellationRequested)
        {
            PartDoc part = model as PartDoc;
            if (part == null)
                return "";

            List<string> bodyTokens = new List<string>();
            foreach (object bodyObject in ToObjectArray(
                part.GetBodies2((int)swBodyType_e.swSolidBody, true)))
            {
                ThrowIfCanceled(isCancellationRequested);
                Body2 body = bodyObject as Body2;
                if (body == null)
                    continue;

                List<double[]> points = new List<double[]>();
                foreach (object vertexObject in ToObjectArray(body.GetVertices()))
                {
                    Vertex vertex = vertexObject as Vertex;
                    if (vertex == null)
                        continue;
                    double[] point = null;
                    try { point = vertex.GetPoint() as double[]; } catch { }
                    if (point != null && point.Length >= 3)
                        points.Add(new[] { point[0], point[1], point[2] });
                }

                if (points.Count == 0)
                    continue;
                double cx = points.Average(point => point[0]);
                double cy = points.Average(point => point[1]);
                double cz = points.Average(point => point[2]);
                List<string> pointTokens = points.Select(point =>
                    Quantize(point[0] - cx, LengthScale) + "," +
                    Quantize(point[1] - cy, LengthScale) + "," +
                    Quantize(point[2] - cz, LengthScale))
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToList();
                bodyTokens.Add(string.Join(";", pointTokens));
            }

            bodyTokens.Sort(StringComparer.Ordinal);
            return Hash(string.Join("||", bodyTokens));
        }

        private static string CreateBodyToken(Body2 body, Func<bool> isCancellationRequested)
        {
            List<string> faces = new List<string>();
            List<string> edges = new List<string>();
            List<double[]> vertices = new List<double[]>();

            foreach (object faceObject in ToObjectArray(body.GetFaces()))
            {
                ThrowIfCanceled(isCancellationRequested);
                Face2 face = faceObject as Face2;
                if (face == null)
                    continue;
                double area = 0;
                try { area = face.GetArea(); } catch { }
                string type = "U";
                try
                {
                    Surface surface = face.GetSurface() as Surface;
                    if (surface != null)
                    {
                        if (surface.IsPlane()) type = "P";
                        else if (surface.IsCylinder()) type = "C";
                        else if (surface.IsCone()) type = "N";
                        else if (surface.IsSphere()) type = "S";
                        else if (surface.IsTorus()) type = "T";
                    }
                }
                catch { }
                faces.Add(type + Quantize(area, AreaScale));
            }

            foreach (object edgeObject in ToObjectArray(body.GetEdges()))
            {
                ThrowIfCanceled(isCancellationRequested);
                Edge edge = edgeObject as Edge;
                if (edge == null)
                    continue;
                double length = 0;
                string type = "U";
                double radius = 0;
                try
                {
                    Curve curve = edge.GetCurve() as Curve;
                    if (curve != null)
                    {
                        try
                        {
                            CurveParamData parameters = edge.GetCurveParams3();
                            if (parameters != null)
                                length = curve.GetLength3(parameters.UMinValue, parameters.UMaxValue);
                        }
                        catch { }
                        if (curve.IsLine()) type = "L";
                        else if (curve.IsCircle())
                        {
                            type = "C";
                            double[] circle = curve.CircleParams as double[];
                            if (circle != null && circle.Length > 6)
                                radius = circle[6];
                        }
                        else
                        {
                            dynamic dynamicCurve = curve;
                            try { if (dynamicCurve.IsEllipse()) type = "E"; } catch { }
                            try { if (dynamicCurve.IsSpline()) type = "S"; } catch { }
                        }
                    }
                }
                catch { }
                edges.Add(type + Quantize(length, LengthScale) + ":" + Quantize(radius, LengthScale));
            }

            foreach (object vertexObject in ToObjectArray(body.GetVertices()))
            {
                Vertex vertex = vertexObject as Vertex;
                if (vertex == null)
                    continue;
                double[] point = null;
                try { point = vertex.GetPoint() as double[]; } catch { }
                if (point != null && point.Length >= 3)
                    vertices.Add(new[] { point[0], point[1], point[2] });
            }

            faces.Sort(StringComparer.Ordinal);
            edges.Sort(StringComparer.Ordinal);
            List<long> distances = BuildInvariantDistances(vertices, isCancellationRequested);
            string box = BuildBoxToken(body);
            return "B:" + box
                + "|F:" + faces.Count + ":" + string.Join(",", faces)
                + "|E:" + edges.Count + ":" + string.Join(",", edges)
                + "|V:" + vertices.Count + ":" + string.Join(",", distances);
        }

        private static string CreateCandidateBodyToken(
            Body2 body,
            Func<bool> isCancellationRequested)
        {
            List<long> faceAreas = new List<long>();
            List<string> edges = new List<string>();
            List<double[]> vertices = ReadVertices(body);

            foreach (object faceObject in ToObjectArray(body.GetFaces()))
            {
                ThrowIfCanceled(isCancellationRequested);
                Face2 face = faceObject as Face2;
                if (face == null)
                    continue;
                try { faceAreas.Add(Quantize(face.GetArea(), CandidateAreaScale)); }
                catch { faceAreas.Add(0); }
            }

            foreach (object edgeObject in ToObjectArray(body.GetEdges()))
            {
                ThrowIfCanceled(isCancellationRequested);
                Edge edge = edgeObject as Edge;
                if (edge == null)
                    continue;
                double length = 0;
                double radius = 0;
                string type = "U";
                try
                {
                    Curve curve = edge.GetCurve() as Curve;
                    if (curve != null)
                    {
                        try
                        {
                            CurveParamData parameters = edge.GetCurveParams3();
                            if (parameters != null)
                                length = curve.GetLength3(parameters.UMinValue, parameters.UMaxValue);
                        }
                        catch { }
                        if (curve.IsLine()) type = "L";
                        else if (curve.IsCircle())
                        {
                            type = "C";
                            double[] circle = curve.CircleParams as double[];
                            if (circle != null && circle.Length > 6)
                                radius = circle[6];
                        }
                    }
                }
                catch { }
                edges.Add(type
                    + Quantize(length, CandidateLengthScale)
                    + ":" + Quantize(radius, CandidateLengthScale));
            }

            faceAreas.Sort();
            edges.Sort(StringComparer.Ordinal);
            List<long> distances = BuildInvariantDistances(
                vertices,
                isCancellationRequested,
                CandidateLengthScale);
            // Khong dua axis-aligned body box vao token ung vien. Body box thay doi
            // khi cung mot body duoc xay theo truc/huong khac, du hinh hoc trung nhau.
            return "F:" + faceAreas.Count + ":" + string.Join(",", faceAreas)
                + "|E:" + edges.Count + ":" + string.Join(",", edges)
                + "|V:" + vertices.Count + ":" + string.Join(",", distances);
        }

        private static List<double[]> ReadVertices(Body2 body)
        {
            List<double[]> points = new List<double[]>();
            if (body == null)
                return points;
            foreach (object vertexObject in ToObjectArray(body.GetVertices()))
            {
                Vertex vertex = vertexObject as Vertex;
                if (vertex == null)
                    continue;
                double[] point = null;
                try { point = vertex.GetPoint() as double[]; } catch { }
                if (point != null && point.Length >= 3)
                    points.Add(new[] { point[0], point[1], point[2] });
            }
            return points;
        }

        private static string CreateBodyChirality(
            List<double[]> points,
            Func<bool> isCancellationRequested)
        {
            if (points == null || points.Count < 4)
                return "PLANAR";

            List<Tuple<string, double[]>> labeled = new List<Tuple<string, double[]>>();
            Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (double[] point in points)
            {
                ThrowIfCanceled(isCancellationRequested);
                List<long> distances = points
                    .Where(other => !ReferenceEquals(other, point))
                    .Select(other => Quantize(Distance(point, other), 100000.0))
                    .OrderBy(value => value)
                    .ToList();
                string label = string.Join(",", distances);
                labeled.Add(Tuple.Create(label, point));
                counts[label] = counts.TryGetValue(label, out int current) ? current + 1 : 1;
            }

            List<Tuple<string, double[]>> unique = labeled
                .Where(item => counts[item.Item1] == 1)
                .OrderBy(item => item.Item1, StringComparer.Ordinal)
                .Take(24)
                .ToList();
            if (unique.Count < 4)
                return "AMBIG";

            for (int a = 0; a < unique.Count - 3; a++)
            for (int b = a + 1; b < unique.Count - 2; b++)
            for (int c = b + 1; c < unique.Count - 1; c++)
            for (int d = c + 1; d < unique.Count; d++)
            {
                ThrowIfCanceled(isCancellationRequested);
                double volume6 = SignedVolume6(
                    unique[a].Item2,
                    unique[b].Item2,
                    unique[c].Item2,
                    unique[d].Item2);
                if (Math.Abs(volume6) > 1e-15)
                    return volume6 > 0 ? "POS" : "NEG";
            }
            return "PLANAR";
        }

        private static double SignedVolume6(double[] a, double[] b, double[] c, double[] d)
        {
            double abx = b[0] - a[0];
            double aby = b[1] - a[1];
            double abz = b[2] - a[2];
            double acx = c[0] - a[0];
            double acy = c[1] - a[1];
            double acz = c[2] - a[2];
            double adx = d[0] - a[0];
            double ady = d[1] - a[1];
            double adz = d[2] - a[2];
            return abx * (acy * adz - acz * ady)
                - aby * (acx * adz - acz * adx)
                + abz * (acx * ady - acy * adx);
        }

        private static List<long> BuildInvariantDistances(
            List<double[]> vertices,
            Func<bool> isCancellationRequested)
        {
            return BuildInvariantDistances(vertices, isCancellationRequested, LengthScale);
        }

        private static List<long> BuildInvariantDistances(
            List<double[]> vertices,
            Func<bool> isCancellationRequested,
            double scale)
        {
            List<long> values = new List<long>();
            if (vertices.Count <= 350)
            {
                for (int i = 0; i < vertices.Count; i++)
                {
                    ThrowIfCanceled(isCancellationRequested);
                    for (int j = i + 1; j < vertices.Count; j++)
                        values.Add(Quantize(Distance(vertices[i], vertices[j]), scale));
                }
            }
            else
            {
                double[] center = new double[3];
                foreach (double[] point in vertices)
                {
                    center[0] += point[0];
                    center[1] += point[1];
                    center[2] += point[2];
                }
                center[0] /= vertices.Count;
                center[1] /= vertices.Count;
                center[2] /= vertices.Count;
                foreach (double[] point in vertices)
                    values.Add(Quantize(Distance(point, center), scale));
            }
            values.Sort();
            return values;
        }

        private static string BuildBoxToken(Body2 body)
        {
            return BuildBoxToken(body, LengthScale);
        }

        private static string BuildBoxToken(Body2 body, double scale)
        {
            try
            {
                dynamic dynamicBody = body;
                object value = dynamicBody.GetBodyBox();
                double[] box = value as double[];
                if (box == null && value is Array)
                {
                    Array array = (Array)value;
                    box = new double[array.Length];
                    for (int i = 0; i < array.Length; i++)
                        box[i] = Convert.ToDouble(array.GetValue(i), CultureInfo.InvariantCulture);
                }
                if (box == null || box.Length < 6)
                    return "";
                long[] dimensions =
                {
                    Quantize(Math.Abs(box[3] - box[0]), scale),
                    Quantize(Math.Abs(box[4] - box[1]), scale),
                    Quantize(Math.Abs(box[5] - box[2]), scale)
                };
                Array.Sort(dimensions);
                return string.Join(",", dimensions);
            }
            catch
            {
                return "";
            }
        }

        private static long Quantize(double value, double scale)
        {
            return (long)Math.Round(value * scale, MidpointRounding.AwayFromZero);
        }

        private static double Distance(double[] a, double[] b)
        {
            double dx = a[0] - b[0];
            double dy = a[1] - b[1];
            double dz = a[2] - b[2];
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        private static string Hash(string text)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? ""));
                StringBuilder builder = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash)
                    builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
                return builder.ToString();
            }
        }

        private static void ThrowIfCanceled(Func<bool> callback)
        {
            if (callback != null && callback())
                throw new OperationCanceledException();
        }

        private static object[] ToObjectArray(object value)
        {
            if (value == null)
                return new object[0];
            object[] objects = value as object[];
            if (objects != null)
                return objects;
            Array array = value as Array;
            if (array == null)
                return new object[0];
            object[] result = new object[array.Length];
            for (int i = 0; i < array.Length; i++)
                result[i] = array.GetValue(i);
            return result;
        }
    }

    internal sealed class SamePartToleranceDialog : Form
    {
        private static SamePartToleranceOptions lastOptions = new SamePartToleranceOptions();

        private readonly NumericUpDown numAreaAbsolute;
        private readonly NumericUpDown numAreaRelative;
        private readonly NumericUpDown numEdgeLength;
        private readonly NumericUpDown numVolumeAbsolute;
        private readonly NumericUpDown numVolumeRelative;
        private readonly NumericUpDown numPrincipalMoment;
        private readonly NumericUpDown numHoleLinear;
        private readonly NumericUpDown numHoleRadius;

        private SamePartToleranceDialog(SamePartToleranceOptions options)
        {
            Font = new Font("Meiryo UI", 9F, FontStyle.Regular, GraphicsUnit.Point, 128);
            Text = "CHECK SAME PART - DUNG SAI";
            BackColor = Color.White;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            TopMost = true;
            ClientSize = new Size(540, 500);

            Panel header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 72,
                BackColor = Color.FromArgb(42, 96, 145)
            };
            Label title = new Label
            {
                AutoSize = false,
                Location = new Point(22, 13),
                Size = new Size(495, 25),
                Font = new Font("Meiryo UI", 12F, FontStyle.Bold, GraphicsUnit.Point, 128),
                ForeColor = Color.White,
                Text = "CHECK SAME PART - NH\u1eACP DUNG SAI"
            };
            Label subtitle = new Label
            {
                AutoSize = false,
                Location = new Point(23, 40),
                Size = new Size(490, 22),
                ForeColor = Color.FromArgb(225, 238, 249),
                Text = "C\u00e1c gi\u00e1 tr\u1ecb n\u00e0y ch\u1ec9 \u00e1p d\u1ee5ng cho l\u1ea7n ki\u1ec3m tra hi\u1ec7n t\u1ea1i."
            };
            header.Controls.Add(title);
            header.Controls.Add(subtitle);
            Controls.Add(header);

            GroupBox group = new GroupBox
            {
                Text = "Dung sai so s\u00e1nh h\u00ecnh h\u1ecdc",
                Font = new Font("Meiryo UI", 9F, FontStyle.Bold, GraphicsUnit.Point, 128),
                Location = new Point(18, 87),
                Size = new Size(504, 342),
                ForeColor = Color.FromArgb(35, 55, 75)
            };
            Controls.Add(group);

            TableLayoutPanel table = new TableLayoutPanel
            {
                Location = new Point(15, 27),
                Size = new Size(474, 300),
                ColumnCount = 3,
                RowCount = 8,
                BackColor = Color.White
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62F));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105F));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64F));
            for (int row = 0; row < 8; row++)
                table.RowStyles.Add(new RowStyle(SizeType.Absolute, 37F));
            group.Controls.Add(table);

            numAreaAbsolute = AddToleranceRow(table, 0,
                "Di\u1ec7n t\u00edch - sai l\u1ec7ch tuy\u1ec7t \u0111\u1ed1i", "mm\u00b2", options.AreaAbsoluteMm2, 3, 1000000M);
            numAreaRelative = AddToleranceRow(table, 1,
                "Di\u1ec7n t\u00edch - sai l\u1ec7ch t\u01b0\u01a1ng \u0111\u1ed1i", "%", options.AreaRelativePercent, 4, 100M);
            numEdgeLength = AddToleranceRow(table, 2,
                "T\u1ed5ng chi\u1ec1u d\u00e0i c\u1ea1nh", "mm", options.EdgeLengthMm, 3, 1000M);
            numVolumeAbsolute = AddToleranceRow(table, 3,
                "Th\u1ec3 t\u00edch - sai l\u1ec7ch tuy\u1ec7t \u0111\u1ed1i", "mm\u00b3", options.VolumeAbsoluteMm3, 3, 100000000M);
            numVolumeRelative = AddToleranceRow(table, 4,
                "Th\u1ec3 t\u00edch - sai l\u1ec7ch t\u01b0\u01a1ng \u0111\u1ed1i", "%", options.VolumeRelativePercent, 4, 100M);
            numPrincipalMoment = AddToleranceRow(table, 5,
                "M\u00f4men ch\u00ednh - sai l\u1ec7ch t\u01b0\u01a1ng \u0111\u1ed1i", "%", options.PrincipalMomentRelativePercent, 4, 100M);
            numHoleLinear = AddToleranceRow(table, 6,
                "V\u1ecb tr\u00ed / chu vi / c\u1ea1nh l\u1ed7", "mm", options.HoleLinearMm, 3, 1000M);
            numHoleRadius = AddToleranceRow(table, 7,
                "B\u00e1n k\u00ednh l\u1ed7", "mm", options.HoleRadiusMm, 3, 1000M);

            Button runButton = new Button
            {
                Text = "CH\u1ea0Y KI\u1ec2M TRA",
                Font = new Font("Meiryo UI", 9F, FontStyle.Bold, GraphicsUnit.Point, 128),
                BackColor = Color.FromArgb(221, 238, 252),
                ForeColor = Color.FromArgb(28, 76, 118),
                FlatStyle = FlatStyle.Flat,
                Location = new Point(288, 447),
                Size = new Size(130, 36),
                DialogResult = DialogResult.None
            };
            runButton.FlatAppearance.BorderColor = Color.FromArgb(72, 126, 176);
            Button cancelButton = new Button
            {
                Text = "H\u1ee6Y",
                Font = new Font("Meiryo UI", 9F, FontStyle.Bold, GraphicsUnit.Point, 128),
                BackColor = Color.White,
                ForeColor = Color.FromArgb(70, 70, 70),
                FlatStyle = FlatStyle.Flat,
                Location = new Point(429, 447),
                Size = new Size(93, 36),
                DialogResult = DialogResult.None
            };
            cancelButton.FlatAppearance.BorderColor = Color.FromArgb(155, 165, 175);
            Controls.Add(runButton);
            Controls.Add(cancelButton);
            AcceptButton = runButton;
            CancelButton = cancelButton;
            runButton.Click += delegate
            {
                Debug.WriteLine("[CHECK SAME PART][TOLERANCE] Run clicked.");
                DialogResult = DialogResult.OK;
                Close();
            };
            cancelButton.Click += delegate
            {
                Debug.WriteLine("[CHECK SAME PART][TOLERANCE] Cancel clicked.");
                DialogResult = DialogResult.Cancel;
                Close();
            };
            Shown += delegate
            {
                Activate();
                numAreaAbsolute.Focus();
                numAreaAbsolute.Select(0, numAreaAbsolute.Text.Length);
            };
        }

        public static bool TryGetOptions(IWin32Window owner, out SamePartToleranceOptions options)
        {
            using (SamePartToleranceDialog dialog = new SamePartToleranceDialog(lastOptions.Clone()))
            {
                Debug.WriteLine("[CHECK SAME PART][TOLERANCE] Dialog opening.");
                DialogResult dialogResult = dialog.ShowDialog();
                Debug.WriteLine("[CHECK SAME PART][TOLERANCE] Dialog closed. Result=" + dialogResult);
                if (dialogResult != DialogResult.OK)
                {
                    options = null;
                    return false;
                }

                options = dialog.ReadOptions();
                lastOptions = options.Clone();
                return true;
            }
        }

        private SamePartToleranceOptions ReadOptions()
        {
            return new SamePartToleranceOptions
            {
                AreaAbsoluteMm2 = (double)numAreaAbsolute.Value,
                AreaRelativePercent = (double)numAreaRelative.Value,
                EdgeLengthMm = (double)numEdgeLength.Value,
                VolumeAbsoluteMm3 = (double)numVolumeAbsolute.Value,
                VolumeRelativePercent = (double)numVolumeRelative.Value,
                PrincipalMomentRelativePercent = (double)numPrincipalMoment.Value,
                HoleLinearMm = (double)numHoleLinear.Value,
                HoleRadiusMm = (double)numHoleRadius.Value
            };
        }

        private static NumericUpDown AddToleranceRow(
            TableLayoutPanel table,
            int row,
            string labelText,
            string unit,
            double value,
            int decimals,
            decimal maximum)
        {
            Label label = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Meiryo UI", 9F, FontStyle.Regular, GraphicsUnit.Point, 128),
                ForeColor = Color.FromArgb(35, 45, 55),
                Text = labelText
            };
            NumericUpDown editor = new NumericUpDown
            {
                Dock = DockStyle.Fill,
                DecimalPlaces = decimals,
                Minimum = 0M,
                Maximum = maximum,
                Increment = decimals >= 4 ? 0.001M : 0.01M,
                ThousandsSeparator = true,
                TextAlign = HorizontalAlignment.Right,
                Font = new Font("Meiryo UI", 9F, FontStyle.Regular, GraphicsUnit.Point, 128),
                Margin = new Padding(3, 6, 3, 5),
                Value = Math.Min(maximum, Math.Max(0M, Convert.ToDecimal(value, CultureInfo.InvariantCulture)))
            };
            Label unitLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Meiryo UI", 9F, FontStyle.Regular, GraphicsUnit.Point, 128),
                ForeColor = Color.FromArgb(90, 100, 110),
                Text = unit
            };
            table.Controls.Add(label, 0, row);
            table.Controls.Add(editor, 1, row);
            table.Controls.Add(unitLabel, 2, row);
            return editor;
        }
    }

    public static class ExcelSamePartExporter
    {
        public static int Export(
            SamePartCheckResult result,
            string outputDirectory,
            out string exportedPath)
        {
            exportedPath = "";
            if (result == null || (result.Groups.Count == 0 && result.Errors.Count == 0))
                return 0;

            dynamic excel = null;
            dynamic workbook = null;
            dynamic sheet = null;
            try
            {
                Type excelType = Type.GetTypeFromProgID("Excel.Application");
                if (excelType == null)
                    return 0;
                excel = Activator.CreateInstance(excelType);
                workbook = excel.Workbooks.Add();
                sheet = workbook.Worksheets[1];
                sheet.Name = "CHECK SAME PART";

                string[] headers =
                {
                    "\u90e8\u54c1\u756a\u53f7", "Nh\u00f3m", "S\u1ed1 chi ti\u1ebft", "V\u1eadt li\u1ec7u", "B\u1ec1 d\u00e0y",
                    "C\u1ea5u h\u00ecnh", "C\u1ea5u h\u00ecnh tr\u1ea3i", "Gi\u00e1 tr\u1ecb kh\u00e1c nhau",
                    "Ghi ch\u00fa", "K\u1ebft qu\u1ea3", "\u0110\u01b0\u1eddng d\u1eabn"
                };
                for (int column = 0; column < headers.Length; column++)
                    sheet.Cells[1, column + 1] = headers[column];

                int row = 2;
                foreach (SamePartGroupResult group in result.Groups)
                {
                    sheet.Cells[row, 1] = JoinDistinct(group.Items.Select(item => item.BuhinNo));
                    sheet.Cells[row, 2] = group.GroupNumber;
                    sheet.Cells[row, 3] = group.Items.Count;
                    sheet.Cells[row, 4] = JoinDistinct(group.Items.Select(item => item.Material));
                    sheet.Cells[row, 5] = JoinDistinct(group.Items.Select(item => item.Thickness));
                    sheet.Cells[row, 6] = JoinDistinct(group.Items.Select(item => item.Configuration));
                    sheet.Cells[row, 7] = JoinDistinct(group.Items.Select(item => item.FlatConfiguration));
                    sheet.Cells[row, 8] = FormatDifferenceValues(group);
                    sheet.Cells[row, 9] = FormatNoteForExcel(group);
                    sheet.Cells[row, 10] = FormatStatusForExcel(group.Status);
                    sheet.Cells[row, 11] = string.Join(System.Environment.NewLine,
                        group.Items.Select(item => item.PartPath).Distinct(StringComparer.OrdinalIgnoreCase));
                    ApplyRowColor(sheet, row, group.Status);
                    row++;
                }

                foreach (SamePartItemResult error in result.Errors)
                {
                    sheet.Cells[row, 1] = error.BuhinNo;
                    sheet.Cells[row, 2] = "-";
                    sheet.Cells[row, 3] = 1;
                    sheet.Cells[row, 4] = error.Material;
                    sheet.Cells[row, 5] = error.Thickness;
                    sheet.Cells[row, 6] = error.Configuration;
                    sheet.Cells[row, 7] = error.FlatConfiguration;
                    sheet.Cells[row, 8] = "";
                    sheet.Cells[row, 9] = FormatErrorForExcel(error.Error);
                    sheet.Cells[row, 10] = "KI\u1ec2M TRA";
                    sheet.Cells[row, 11] = error.PartPath;
                    ApplyRowColor(sheet, row, "CHECK");
                    row++;
                }

                dynamic usedRange = sheet.Range[sheet.Cells[1, 1], sheet.Cells[row - 1, headers.Length]];
                usedRange.Font.Name = "Meiryo UI";
                usedRange.Font.Size = 10;
                usedRange.VerticalAlignment = -4160;
                usedRange.Borders.LineStyle = 1;
                sheet.Rows[1].Font.Bold = true;
                sheet.Rows[1].Interior.Color = ColorToExcel(221, 235, 247);
                usedRange.Columns.AutoFit();
                usedRange.Rows.AutoFit();
                sheet.Columns[8].ColumnWidth = Math.Min(65, Math.Max(30, sheet.Columns[8].ColumnWidth));
                sheet.Columns[9].ColumnWidth = Math.Min(48, Math.Max(24, sheet.Columns[9].ColumnWidth));
                sheet.Columns[11].ColumnWidth = Math.Min(60, Math.Max(28, sheet.Columns[11].ColumnWidth));
                sheet.Columns[8].WrapText = true;
                sheet.Columns[9].WrapText = true;
                sheet.Columns[11].WrapText = true;
                sheet.Application.ActiveWindow.SplitRow = 1;
                sheet.Application.ActiveWindow.FreezePanes = true;

                string targetDirectory = ResolveOutputDirectory(outputDirectory);
                string path = Path.Combine(targetDirectory,
                    "CHECK_SAME_PART_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".xlsx");
                workbook.SaveAs(path);
                exportedPath = path;
                Debug.WriteLine("[CHECK SAME PART] Excel saved: " + path);
                excel.Visible = true;
                return row - 2;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[CHECK SAME PART] Excel ERROR: " + ex);
                try { if (workbook != null) workbook.Close(false); } catch { }
                try { if (excel != null) excel.Quit(); } catch { }
                MessageBox.Show("Khong xuat duoc Excel CHECK SAME PART:\n" + ex.Message,
                    "CHECK SAME PART", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 0;
            }
            finally
            {
                ReleaseCom(sheet);
                ReleaseCom(workbook);
                ReleaseCom(excel);
            }
        }

        private static string ResolveOutputDirectory(string outputDirectory)
        {
            if (!string.IsNullOrWhiteSpace(outputDirectory))
            {
                try
                {
                    string fullPath = Path.GetFullPath(outputDirectory);
                    if (Directory.Exists(fullPath))
                        return fullPath;
                }
                catch
                {
                }
            }

            return System.Environment.GetFolderPath(
                System.Environment.SpecialFolder.DesktopDirectory);
        }

        private static string FormatNoteForExcel(SamePartGroupResult group)
        {
            string status = group == null ? "" : (group.Status ?? "");
            switch (status)
            {
                case "SAME FULL":
                    return "C\u00e1c chi ti\u1ebft gi\u1ed1ng nhau ho\u00e0n to\u00e0n.";
                case "SAME TOLERANCE":
                    return "C\u00e1c chi ti\u1ebft gi\u1ed1ng nhau trong ph\u1ea1m vi dung sai.";
                case "CHECK MIRROR":
                    return "Bi\u00ean d\u1ea1ng t\u01b0\u01a1ng \u0111\u01b0\u01a1ng nh\u01b0ng c\u00f3 kh\u1ea3 n\u0103ng l\u00e0 chi ti\u1ebft \u0111\u1ed1i x\u1ee9ng.";
                case "SAME GEOMETRY":
                    return "Bi\u00ean d\u1ea1ng gi\u1ed1ng nhau nh\u01b0ng v\u1eadt li\u1ec7u ho\u1eb7c b\u1ec1 d\u00e0y kh\u00e1c nhau.";
                case "SAME FLAT":
                    return "Bi\u00ean d\u1ea1ng tr\u1ea3i gi\u1ed1ng nhau nh\u01b0ng tr\u1ea1ng th\u00e1i g\u1ea5p kh\u00e1c nhau.";
                default:
                    return group == null ? "" : (group.Note ?? "");
            }
        }

        private static string FormatDifferenceValues(SamePartGroupResult group)
        {
            if (group == null || group.Items == null || group.Items.Count < 2)
                return "";

            string status = group.Status ?? "";
            if (status == "SAME FULL" || status == "SAME TOLERANCE")
                return "";

            SamePartItemResult reference = group.Items[0];
            List<string> comparisons = new List<string>();
            for (int index = 1; index < group.Items.Count; index++)
            {
                SamePartItemResult candidate = group.Items[index];
                List<string> differences = BuildPairDifferences(reference, candidate, status);
                if (differences.Count == 0)
                    differences.Add(GetFallbackDifference(status));

                comparisons.Add((reference.BuhinNo ?? "?") + " <> "
                    + (candidate.BuhinNo ?? "?") + ": "
                    + string.Join("; ", differences));
            }
            return string.Join(System.Environment.NewLine, comparisons);
        }

        private static List<string> BuildPairDifferences(
            SamePartItemResult first,
            SamePartItemResult second,
            string status)
        {
            List<string> values = new List<string>();
            if (first == null || second == null)
                return values;

            if (!string.Equals(first.MaterialKey ?? "", second.MaterialKey ?? "",
                StringComparison.OrdinalIgnoreCase))
            {
                values.Add("V\u1eadt li\u1ec7u: " + DisplayValue(first.Material)
                    + " <> " + DisplayValue(second.Material));
            }
            if (!string.Equals(first.ThicknessKey ?? "", second.ThicknessKey ?? "",
                StringComparison.OrdinalIgnoreCase))
            {
                values.Add("B\u1ec1 d\u00e0y: " + DisplayValue(first.Thickness)
                    + " <> " + DisplayValue(second.Thickness));
            }

            if (status == "SAME FLAT")
                AddMetricDifferences(values, "Tr\u1ea1ng th\u00e1i g\u1ea5p",
                    first.FoldedBroadMetrics, second.FoldedBroadMetrics);

            if (status == "CHECK MIRROR")
            {
                AddReadableSignatureDifference(values, "D\u1ea5u h\u00ecnh h\u1ecdc",
                    first.FoldedChiralitySignature, second.FoldedChiralitySignature);
                AddReadableSignatureDifference(values, "Feature mirror/derived",
                    first.FeatureMirrorSignature, second.FeatureMirrorSignature);

                if (!string.Equals(first.FoldedOrientationSignature ?? "",
                    second.FoldedOrientationSignature ?? "", StringComparison.Ordinal))
                    values.Add("H\u01b0\u1edbng t\u1ecda \u0111\u1ed9 h\u00ecnh h\u1ecdc kh\u00e1c nhau");
                if (!string.Equals(first.FeatureOperationSignature ?? "",
                    second.FeatureOperationSignature ?? "", StringComparison.Ordinal))
                    values.Add("Chu\u1ed7i feature t\u1ea1o/c\u1eaft kh\u00e1c nhau");
            }

            if (status != "SAME GEOMETRY")
                AddFlatProfileDifferences(values, first.FlatProfileMetrics, second.FlatProfileMetrics);

            return values.Distinct(StringComparer.Ordinal).ToList();
        }

        private static void AddMetricDifferences(
            List<string> values,
            string label,
            BroadGeometryMetrics first,
            BroadGeometryMetrics second)
        {
            if (first == null || second == null)
            {
                values.Add(label + ": thi\u1ebfu d\u1eef li\u1ec7u h\u00ecnh h\u1ecdc");
                return;
            }
            if (first.BodyCount != second.BodyCount)
                values.Add(label + " - s\u1ed1 body: " + first.BodyCount + " <> " + second.BodyCount);
            AddNumberDifference(values, label + " - di\u1ec7n t\u00edch",
                first.AreaMm2, second.AreaMm2, "mm\u00b2");
            AddNumberDifference(values, label + " - t\u1ed5ng chi\u1ec1u d\u00e0i c\u1ea1nh",
                first.EdgeLengthMm, second.EdgeLengthMm, "mm");
            AddNumberDifference(values, label + " - th\u1ec3 t\u00edch",
                first.VolumeMm3, second.VolumeMm3, "mm\u00b3");

            if (first.PrincipalMoments.Count != second.PrincipalMoments.Count)
            {
                values.Add(label + " - s\u1ed1 m\u00f4men ch\u00ednh: "
                    + first.PrincipalMoments.Count + " <> " + second.PrincipalMoments.Count);
            }
            else
            {
                for (int index = 0; index < first.PrincipalMoments.Count; index++)
                {
                    AddNumberDifference(values, label + " - m\u00f4men ch\u00ednh " + (index + 1),
                        first.PrincipalMoments[index], second.PrincipalMoments[index], "");
                }
            }
        }

        private static void AddFlatProfileDifferences(
            List<string> values,
            FlatProfileMetrics first,
            FlatProfileMetrics second)
        {
            if (first == null || second == null)
                return;
            if (first.InnerLoopCount != second.InnerLoopCount)
                values.Add("S\u1ed1 l\u1ed7/bi\u00ean d\u1ea1ng trong: "
                    + first.InnerLoopCount + " <> " + second.InnerLoopCount);
            if (!NumericListsEqual(first.CenterDistancesMm, second.CenterDistancesMm))
                values.Add("Kho\u1ea3ng c\u00e1ch t\u00e2m l\u1ed7: "
                    + FormatNumberList(first.CenterDistancesMm, "mm") + " <> "
                    + FormatNumberList(second.CenterDistancesMm, "mm"));

            List<double> firstPerimeters = first.InnerLoops.Select(loop => loop.PerimeterMm).OrderBy(v => v).ToList();
            List<double> secondPerimeters = second.InnerLoops.Select(loop => loop.PerimeterMm).OrderBy(v => v).ToList();
            if (!NumericListsEqual(firstPerimeters, secondPerimeters))
                values.Add("Chu vi l\u1ed7/bi\u00ean d\u1ea1ng trong: "
                    + FormatNumberList(firstPerimeters, "mm") + " <> "
                    + FormatNumberList(secondPerimeters, "mm"));

            List<double> firstRadii = first.InnerLoops.SelectMany(loop => loop.RadiiMm).OrderBy(v => v).ToList();
            List<double> secondRadii = second.InnerLoops.SelectMany(loop => loop.RadiiMm).OrderBy(v => v).ToList();
            if (!NumericListsEqual(firstRadii, secondRadii))
                values.Add("B\u00e1n k\u00ednh l\u1ed7/bi\u00ean d\u1ea1ng trong: "
                    + FormatNumberList(firstRadii, "mm") + " <> "
                    + FormatNumberList(secondRadii, "mm"));
        }

        private static void AddReadableSignatureDifference(
            List<string> values,
            string label,
            string first,
            string second)
        {
            string a = first ?? "";
            string b = second ?? "";
            if (!string.Equals(a, b, StringComparison.Ordinal))
                values.Add(label + ": " + DisplayValue(a) + " <> " + DisplayValue(b));
        }

        private static void AddNumberDifference(
            List<string> values,
            string label,
            double first,
            double second,
            string unit)
        {
            if (Math.Abs(first - second) <= 1e-9)
                return;
            string suffix = string.IsNullOrEmpty(unit) ? "" : " " + unit;
            values.Add(label + ": " + FormatNumber(first) + suffix
                + " <> " + FormatNumber(second) + suffix);
        }

        private static bool NumericListsEqual(IList<double> first, IList<double> second)
        {
            if (first == null || second == null || first.Count != second.Count)
                return false;
            for (int index = 0; index < first.Count; index++)
            {
                if (Math.Abs(first[index] - second[index]) > 1e-9)
                    return false;
            }
            return true;
        }

        private static string FormatNumberList(IEnumerable<double> values, string unit)
        {
            List<string> numbers = (values ?? Enumerable.Empty<double>())
                .Select(FormatNumber)
                .ToList();
            string suffix = string.IsNullOrEmpty(unit) ? "" : " " + unit;
            return numbers.Count == 0 ? "-" : string.Join(", ", numbers) + suffix;
        }

        private static string FormatNumber(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string DisplayValue(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
        }

        private static string GetFallbackDifference(string status)
        {
            switch (status ?? "")
            {
                case "CHECK MIRROR":
                    return "Kh\u00e1c d\u1ea5u ho\u1eb7c h\u01b0\u1edbng h\u00ecnh h\u1ecdc; c\u1ea7n ki\u1ec3m tra \u0111\u1ed1i x\u1ee9ng";
                case "SAME FLAT":
                    return "Tr\u1ea1ng th\u00e1i g\u1ea5p kh\u00e1c nhau";
                case "SAME GEOMETRY":
                    return "V\u1eadt li\u1ec7u ho\u1eb7c b\u1ec1 d\u00e0y kh\u00e1c nhau";
                default:
                    return "Ph\u00e1t hi\u1ec7n kh\u00e1c bi\u1ec7t nh\u01b0ng kh\u00f4ng c\u00f3 gi\u00e1 tr\u1ecb s\u1ed1 ph\u00f9 h\u1ee3p";
            }
        }

        private static string FormatStatusForExcel(string status)
        {
            switch (status ?? "")
            {
                case "SAME FULL": return "GI\u1ed0NG NHAU";
                case "SAME TOLERANCE": return "GI\u1ed0NG NHAU (TRONG DUNG SAI)";
                case "CHECK MIRROR": return "KI\u1ec2M TRA \u0110\u1ed0I X\u1ee8NG";
                case "SAME GEOMETRY": return "GI\u1ed0NG BI\u00caN D\u1ea0NG";
                case "SAME FLAT": return "GI\u1ed0NG KHI TR\u1ea2I";
                default: return status ?? "";
            }
        }

        private static string FormatErrorForExcel(string error)
        {
            if (string.IsNullOrWhiteSpace(error))
                return "Kh\u00f4ng th\u1ec3 ki\u1ec3m tra chi ti\u1ebft.";

            string value = error.Trim();
            if (value.IndexOf("SM-FLAT-PATTERN", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Kh\u00f4ng t\u00ecm th\u1ea5y c\u1ea5u h\u00ecnh SM-FLAT-PATTERN.";
            return value;
        }

        private static string JoinDistinct(IEnumerable<string> values)
        {
            return string.Join(", ", values
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase));
        }

        private static void ApplyRowColor(dynamic sheet, int row, string status)
        {
            dynamic range = sheet.Range[sheet.Cells[row, 1], sheet.Cells[row, 10]];
            if (status == "SAME FULL")
                range.Interior.Color = ColorToExcel(226, 239, 218);
            else if (status == "CHECK SAME")
                range.Interior.Color = ColorToExcel(255, 235, 156);
            else if ((status ?? "").StartsWith("CHECK", StringComparison.OrdinalIgnoreCase))
                range.Interior.Color = ColorToExcel(255, 199, 206);
            else
                range.Interior.Color = ColorToExcel(255, 235, 156);
            ReleaseCom(range);
        }

        private static int ColorToExcel(int red, int green, int blue)
        {
            return red | (green << 8) | (blue << 16);
        }

        private static void ReleaseCom(object value)
        {
            if (value == null || !Marshal.IsComObject(value))
                return;
            try { Marshal.FinalReleaseComObject(value); } catch { }
        }
    }
}
