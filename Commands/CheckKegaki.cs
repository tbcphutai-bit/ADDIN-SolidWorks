using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands
{
    public sealed class CheckKegakiRunner
    {
        private readonly ISldWorks swApp;
        private readonly DataGridView gridBom;

        public CheckKegakiRunner(ISldWorks app, DataGridView grid)
        {
            swApp = app;
            gridBom = grid;
        }

        public KegakiCheckResult Run(
            Action<int> progressStarted,
            Action<int, int> progressChanged,
            Func<bool> isCancellationRequested)
        {
            KegakiCheckResult result = new KegakiCheckResult();
            KegakiChecker checker = new KegakiChecker(swApp);
            HashSet<string> checkedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool oldCommandInProgress = swApp.CommandInProgress;
            ModelView activeView = null;

            Debug.WriteLine("[CHECK KEGAKI] ===== RUN START =====");
            try
            {
                ModelDoc2 activeModel = swApp.ActiveDoc as ModelDoc2;
                activeView = activeModel == null ? null : activeModel.ActiveView as ModelView;
                if (activeView != null)
                    activeView.EnableGraphicsUpdate = false;

                swApp.CommandInProgress = true;
                ResolveActiveAssemblyLightweight(activeModel);

                result.CheckedCount = CountCheckedRows();
                if (progressStarted != null)
                    progressStarted(result.CheckedCount);

                int processed = 0;
                foreach (DataGridViewRow row in gridBom.Rows)
                {
                    if (isCancellationRequested != null && isCancellationRequested())
                    {
                        result.Canceled = true;
                        break;
                    }

                    if (row.IsNewRow || !Convert.ToBoolean(row.Cells[0].Value ?? false))
                        continue;

                    List<KegakiBendResult> rowResults = CheckRow(row, checker, checkedKeys);
                    processed++;
                    result.ProcessedCount = processed;
                    if (progressChanged != null)
                        progressChanged(processed, result.CheckedCount);

                    bool hasResult = false;
                    foreach (KegakiBendResult rowResult in rowResults)
                    {
                        hasResult = true;
                        result.Results.Add(rowResult);
                        if (rowResult.Status == "NG" || rowResult.Status == "CHECK")
                            result.HighlightRowIndexes.Add(row.Index);
                    }

                    if (!hasResult || AllSkipped(rowResults))
                        result.SkippedCount++;
                }
            }
            finally
            {
                swApp.CommandInProgress = oldCommandInProgress;
                if (activeView != null)
                    activeView.EnableGraphicsUpdate = true;

                Debug.WriteLine("[CHECK KEGAKI] ===== RUN END ===== results="
                    + result.Results.Count + ", canceled=" + result.Canceled);
            }

            return result;
        }

        private List<KegakiBendResult> CheckRow(
            DataGridViewRow row,
            KegakiChecker checker,
            HashSet<string> checkedKeys)
        {
            List<KegakiBendResult> results = new List<KegakiBendResult>();
            string buhinNo = Convert.ToString(row.Cells[1].Value ?? "");
            string bomFileName = Convert.ToString(row.Cells[5].Value ?? "");

            object[] componentArray = row.Tag as object[];
            if (componentArray != null)
            {
                foreach (object item in componentArray)
                {
                    Component2 component = item as Component2;
                    if (component == null)
                        continue;

                    AddComponentResults(component, buhinNo, bomFileName, checker, checkedKeys, results);
                }

                return results;
            }

            Component2 singleComponent = row.Tag as Component2;
            if (singleComponent != null)
            {
                AddComponentResults(singleComponent, buhinNo, bomFileName, checker, checkedKeys, results);
                return results;
            }

            string partPath = row.Tag as string;
            if (!string.IsNullOrWhiteSpace(partPath) && checkedKeys.Add(partPath))
                results.AddRange(checker.CheckPart(partPath, "", buhinNo, bomFileName, ""));

            return results;
        }

        private static void AddComponentResults(
            Component2 component,
            string buhinNo,
            string bomFileName,
            KegakiChecker checker,
            HashSet<string> checkedKeys,
            List<KegakiBendResult> results)
        {
            string path = "";
            string configuration = "";
            try { path = component.GetPathName(); } catch { }
            try { configuration = component.ReferencedConfiguration; } catch { }

            string key = path + "|" + configuration;
            if (!string.IsNullOrWhiteSpace(key) && !checkedKeys.Add(key))
                return;

            results.AddRange(checker.CheckComponent(component, buhinNo, bomFileName));
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

        private static bool AllSkipped(List<KegakiBendResult> rows)
        {
            if (rows == null || rows.Count == 0)
                return true;

            foreach (KegakiBendResult row in rows)
            {
                if (row.Status != "SKIP")
                    return false;
            }

            return true;
        }

        private void ResolveActiveAssemblyLightweight(ModelDoc2 activeModel)
        {
            try
            {
                AssemblyDoc assembly = activeModel as AssemblyDoc;
                if (assembly != null)
                    assembly.ResolveAllLightWeightComponents(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[CHECK KEGAKI] Resolve lightweight ERROR: " + ex.Message);
            }
        }
    }

    public sealed class KegakiChecker
    {
        private readonly ISldWorks swApp;

        public KegakiChecker(ISldWorks app)
        {
            swApp = app;
        }

        public List<KegakiBendResult> CheckComponent(
            Component2 component,
            string buhinNo,
            string bomFileName)
        {
            if (component == null)
                return new List<KegakiBendResult>();

            string path = "";
            string configuration = "";
            string componentName = "";
            try { path = component.GetPathName(); } catch { }
            try { configuration = component.ReferencedConfiguration; } catch { }
            try { componentName = component.Name2; } catch { }

            if (!string.IsNullOrWhiteSpace(path))
                return CheckPart(path, configuration, buhinNo, bomFileName, componentName);

            ModelDoc2 model = null;
            try { model = component.GetModelDoc2() as ModelDoc2; } catch { }
            if (model != null)
                return CheckModel(model, buhinNo, bomFileName, componentName, "");

            return SingleStatus(
                "CHECK", buhinNo, bomFileName, componentName, path,
                "Khong lay duoc model cua component");
        }

        public List<KegakiBendResult> CheckPart(
            string partPath,
            string configuration,
            string buhinNo,
            string bomFileName,
            string componentName)
        {
            if (string.IsNullOrWhiteSpace(partPath))
                return SingleStatus("CHECK", buhinNo, bomFileName, componentName, partPath, "Khong co path part");

            if (!string.Equals(Path.GetExtension(partPath), ".SLDPRT", StringComparison.OrdinalIgnoreCase))
                return SingleStatus("SKIP", buhinNo, bomFileName, componentName, partPath, "Khong phai part");

            int errors = 0;
            int warnings = 0;
            bool openedByChecker = false;
            bool restorePartVisibility = false;
            ModelDoc2 part = swApp.GetOpenDocumentByName(partPath) as ModelDoc2;

            try
            {
                if (part == null)
                {
                    swApp.DocumentVisible(false, (int)swDocumentTypes_e.swDocPART);
                    restorePartVisibility = true;
                    part = swApp.OpenDoc6(
                        partPath,
                        (int)swDocumentTypes_e.swDocPART,
                        (int)(swOpenDocOptions_e.swOpenDocOptions_Silent | swOpenDocOptions_e.swOpenDocOptions_ReadOnly),
                        "",
                        ref errors,
                        ref warnings) as ModelDoc2;
                    openedByChecker = part != null;
                }

                if (part == null)
                    return SingleStatus("CHECK", buhinNo, bomFileName, componentName, partPath, "Khong mo duoc part");

                string oldConfiguration = "";
                try { oldConfiguration = part.ConfigurationManager.ActiveConfiguration.Name; } catch { }

                try
                {
                    if (!string.IsNullOrWhiteSpace(configuration)
                        && !string.Equals(oldConfiguration, configuration, StringComparison.OrdinalIgnoreCase))
                    {
                        part.ShowConfiguration2(configuration);
                    }

                    return CheckModel(part, buhinNo, bomFileName, componentName, partPath);
                }
                finally
                {
                    if (!openedByChecker
                        && !string.IsNullOrWhiteSpace(oldConfiguration)
                        && !string.Equals(oldConfiguration, configuration, StringComparison.OrdinalIgnoreCase))
                    {
                        try { part.ShowConfiguration2(oldConfiguration); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[CHECK KEGAKI] CheckPart ERROR: " + ex);
                return SingleStatus("CHECK", buhinNo, bomFileName, componentName, partPath, ex.Message);
            }
            finally
            {
                if (openedByChecker && part != null)
                {
                    try { swApp.CloseDoc(part.GetTitle()); } catch { }
                }

                if (restorePartVisibility)
                {
                    try { swApp.DocumentVisible(true, (int)swDocumentTypes_e.swDocPART); } catch { }
                }
            }
        }

        private List<KegakiBendResult> CheckModel(
            ModelDoc2 model,
            string buhinNo,
            string bomFileName,
            string componentName,
            string partPath)
        {
            if (model == null)
                return SingleStatus("CHECK", buhinNo, bomFileName, componentName, partPath, "Model null");

            if (model.GetType() != (int)swDocumentTypes_e.swDocPART)
                return SingleStatus("SKIP", buhinNo, bomFileName, componentName, partPath, "Khong phai part");

            if (string.IsNullOrWhiteSpace(partPath))
            {
                try { partPath = model.GetPathName(); } catch { }
            }

            List<Feature> features = CollectFeatures(model);
            List<BendAllowanceInfo> defaults = new List<BendAllowanceInfo>();
            List<Feature> bends = new List<Feature>();

            foreach (Feature feature in features)
            {
                string typeName = SafeFeatureType(feature);
                if (string.Equals(typeName, "SheetMetal", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        ISheetMetalFeatureData data = feature.GetDefinition() as ISheetMetalFeatureData;
                        if (data != null)
                        {
                            BendAllowanceInfo allowance = BendAllowanceInfo.Capture(data.GetCustomBendAllowance());
                            if (allowance != null)
                                defaults.Add(allowance);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("[CHECK KEGAKI] SheetMetal definition ERROR: " + ex.Message);
                    }
                }
                else if (IsOneBendType(typeName))
                {
                    bends.Add(feature);
                }
            }

            if (defaults.Count == 0 && bends.Count == 0)
                return SingleStatus("SKIP", buhinNo, bomFileName, componentName, partPath, "Khong phai sheet metal");

            string defaultSummary = JoinDefaultSummaries(defaults);
            if (bends.Count == 0)
            {
                foreach (Feature feature in features)
                {
                    Debug.WriteLine("[CHECK KEGAKI] Feature " + SafeFeatureName(feature)
                        + " [" + SafeFeatureType(feature) + "]");
                }

                KegakiBendResult summaryRow = NewResult(
                    buhinNo, bomFileName, componentName, partPath);
                summaryRow.BendName = "Sheet-Metal default";
                summaryRow.DefaultSetting = defaultSummary;
                summaryRow.BendSetting = defaultSummary;
                summaryRow.Status = HasUsableBendTable(defaults) ? "OK" : "CHECK";
                summaryRow.Note = HasUsableBendTable(defaults)
                    ? "Da xuat Bend Table mac dinh; chua tim thay bend rieng"
                    : "Khong tim thay bend rieng va khong doc duoc Bend Table mac dinh";
                return new List<KegakiBendResult> { summaryRow };
            }

            List<KegakiBendResult> results = new List<KegakiBendResult>();
            foreach (Feature bendFeature in bends)
            {
                results.Add(CheckBend(
                    bendFeature,
                    defaults,
                    defaultSummary,
                    buhinNo,
                    bomFileName,
                    componentName,
                    partPath));
            }

            return results;
        }

        private KegakiBendResult CheckBend(
            Feature feature,
            List<BendAllowanceInfo> defaults,
            string defaultSummary,
            string buhinNo,
            string bomFileName,
            string componentName,
            string partPath)
        {
            KegakiBendResult result = NewResult(buhinNo, bomFileName, componentName, partPath);
            result.BendName = SafeFeatureName(feature);
            result.DefaultSetting = defaultSummary;

            try
            {
                IOneBendFeatureData data = feature.GetDefinition() as IOneBendFeatureData;
                if (data == null)
                {
                    result.Status = "CHECK";
                    result.Note = "Khong doc duoc OneBendFeatureData";
                    return result;
                }

                try { result.AngleDeg = Math.Abs(data.BendAngle * 180.0 / Math.PI); } catch { }
                try { result.RadiusMm = Math.Abs(data.BendRadius * 1000.0); } catch { }
                try { result.BendDown = data.BendDown; } catch { }

                bool useDefault = true;
                try { useDefault = data.UseDefaultBendAllowance; } catch { }
                result.IsOverride = !useDefault;

                if (useDefault)
                {
                    result.Status = "OK";
                    result.BendSetting = "Default Sheet-Metal";
                    result.Note = "Dung he so mac dinh";
                    return result;
                }

                BendAllowanceInfo bendAllowance = BendAllowanceInfo.Capture(data.GetCustomBendAllowance());
                result.BendSetting = bendAllowance == null ? "Khong doc duoc" : bendAllowance.Summary();
                if (bendAllowance == null)
                {
                    result.Status = "CHECK";
                    result.Note = "Bend dang override nhung khong doc duoc he so";
                    return result;
                }

                if (defaults.Count == 0)
                {
                    result.Status = "CHECK";
                    result.Note = "Bend dang override; khong doc duoc chuan Sheet-Metal de so sanh";
                    return result;
                }

                foreach (BendAllowanceInfo defaultAllowance in defaults)
                {
                    if (bendAllowance.IsEquivalentTo(defaultAllowance))
                    {
                        result.Status = "OK";
                        result.Note = "Override nhung giong thiet lap Sheet-Metal";
                        return result;
                    }
                }

                result.Status = "NG";
                result.Note = "Bend override khac thiet lap Sheet-Metal";
                return result;
            }
            catch (Exception ex)
            {
                result.Status = "CHECK";
                result.Note = ex.Message;
                Debug.WriteLine("[CHECK KEGAKI] Bend ERROR " + result.BendName + ": " + ex);
                return result;
            }
        }

        private static List<Feature> CollectFeatures(ModelDoc2 model)
        {
            List<Feature> result = new List<Feature>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Feature feature = model.FirstFeature() as Feature;
            while (feature != null)
            {
                AddFeatureAndChildren(feature, result, seen, 0);
                feature = feature.GetNextFeature() as Feature;
            }

            return result;
        }

        private static void AddFeatureAndChildren(
            Feature feature,
            List<Feature> result,
            HashSet<string> seen,
            int depth)
        {
            if (feature == null || depth > 12)
                return;

            string key = SafeFeatureType(feature) + "|" + SafeFeatureName(feature);
            if (!seen.Add(key))
                return;

            result.Add(feature);
            Feature child = null;
            try { child = feature.GetFirstSubFeature() as Feature; } catch { }
            while (child != null)
            {
                AddFeatureAndChildren(child, result, seen, depth + 1);
                try { child = child.GetNextSubFeature() as Feature; }
                catch { child = null; }
            }
        }

        private static string JoinDefaultSummaries(List<BendAllowanceInfo> defaults)
        {
            if (defaults == null || defaults.Count == 0)
                return "Khong doc duoc";

            List<string> values = new List<string>();
            foreach (BendAllowanceInfo item in defaults)
            {
                string summary = item.Summary();
                if (!values.Contains(summary))
                    values.Add(summary);
            }

            return string.Join(" | ", values.ToArray());
        }

        private static bool HasUsableBendTable(List<BendAllowanceInfo> defaults)
        {
            if (defaults == null)
                return false;

            foreach (BendAllowanceInfo item in defaults)
            {
                if (item != null && item.HasBendTableFile())
                    return true;
            }

            return false;
        }

        private static bool IsOneBendType(string typeName)
        {
            return string.Equals(typeName, "OneBend", StringComparison.OrdinalIgnoreCase)
                || string.Equals(typeName, "SketchBend", StringComparison.OrdinalIgnoreCase)
                || string.Equals(typeName, "ToroidalBend", StringComparison.OrdinalIgnoreCase);
        }

        private static List<KegakiBendResult> SingleStatus(
            string status,
            string buhinNo,
            string bomFileName,
            string componentName,
            string partPath,
            string note)
        {
            KegakiBendResult row = NewResult(buhinNo, bomFileName, componentName, partPath);
            row.Status = status;
            row.Note = note;
            return new List<KegakiBendResult> { row };
        }

        private static KegakiBendResult NewResult(
            string buhinNo,
            string bomFileName,
            string componentName,
            string partPath)
        {
            return new KegakiBendResult
            {
                BuhinNo = buhinNo ?? "",
                BomFileName = bomFileName ?? "",
                Component = componentName ?? "",
                PartPath = partPath ?? ""
            };
        }

        private static string SafeFeatureType(Feature feature)
        {
            if (feature == null)
                return "";

            try
            {
                string typeName = feature.GetTypeName();
                if (!string.IsNullOrWhiteSpace(typeName))
                    return typeName;
            }
            catch
            {
            }

            try { return feature.GetTypeName2(); }
            catch { return ""; }
        }

        private static string SafeFeatureName(Feature feature)
        {
            try { return feature == null ? "" : feature.Name; }
            catch { return ""; }
        }
    }

    internal sealed class BendAllowanceInfo
    {
        public int Type { get; private set; }
        public string TypeName { get; private set; }
        public double KFactor { get; private set; }
        public double BendAllowance { get; private set; }
        public double BendDeduction { get; private set; }
        public string BendTableFile { get; private set; }
        public string BendCalculationTableFile { get; private set; }

        public static BendAllowanceInfo Capture(object allowanceObject)
        {
            if (allowanceObject == null)
                return null;

            BendAllowanceInfo info = new BendAllowanceInfo();
            dynamic allowance = allowanceObject;

            try { info.Type = Convert.ToInt32(allowance.Type); } catch { info.Type = int.MinValue; }
            try { info.TypeName = Enum.GetName(typeof(swBendAllowanceTypes_e), info.Type) ?? ("Type " + info.Type); }
            catch { info.TypeName = "Type " + info.Type; }
            try { info.KFactor = Convert.ToDouble(allowance.KFactor); } catch { }
            try { info.BendAllowance = Convert.ToDouble(allowance.BendAllowance); } catch { }
            try { info.BendDeduction = Convert.ToDouble(allowance.BendDeduction); } catch { }
            try { info.BendTableFile = Convert.ToString(allowance.BendTableFile ?? ""); } catch { info.BendTableFile = ""; }
            // BendCalculationTableFile does not exist in older SOLIDWORKS
            // interop assemblies. Do not access it dynamically here because
            // that produces a RuntimeBinderException for every checked part.
            info.BendCalculationTableFile = "";

            return info;
        }

        public bool IsEquivalentTo(BendAllowanceInfo other)
        {
            if (other == null || Type != other.Type)
                return false;

            string type = (TypeName ?? "").ToLowerInvariant();
            if (type.Contains("calculation") && type.Contains("table"))
                return SamePath(BendCalculationTableFile, other.BendCalculationTableFile);
            if (type.Contains("bendtable") || (type.Contains("bend") && type.Contains("table")))
                return SamePath(BendTableFile, other.BendTableFile);
            if (type.Contains("kfactor") || type.Contains("k_factor"))
                return NearlyEqual(KFactor, other.KFactor, 0.0000001);
            if (type.Contains("deduction"))
                return NearlyEqual(BendDeduction, other.BendDeduction, 0.0000001);
            if (type.Contains("direct") || type.Contains("allowance"))
                return NearlyEqual(BendAllowance, other.BendAllowance, 0.0000001);

            return NearlyEqual(KFactor, other.KFactor, 0.0000001)
                && NearlyEqual(BendAllowance, other.BendAllowance, 0.0000001)
                && NearlyEqual(BendDeduction, other.BendDeduction, 0.0000001)
                && SamePath(BendTableFile, other.BendTableFile)
                && SamePath(BendCalculationTableFile, other.BendCalculationTableFile);
        }

        public string Summary()
        {
            string type = (TypeName ?? "").ToLowerInvariant();
            if (type.Contains("calculation") && type.Contains("table"))
                return ShortTypeName() + ": " + ShortPath(BendCalculationTableFile);
            if (type.Contains("bendtable") || (type.Contains("bend") && type.Contains("table")))
                return ShortTypeName() + ": " + ShortPath(BendTableFile);
            if (type.Contains("kfactor") || type.Contains("k_factor"))
                return ShortTypeName() + "=" + KFactor.ToString("0.####");
            if (type.Contains("deduction"))
                return ShortTypeName() + "=" + (BendDeduction * 1000.0).ToString("0.###") + " mm";
            if (type.Contains("direct") || type.Contains("allowance"))
                return ShortTypeName() + "=" + (BendAllowance * 1000.0).ToString("0.###") + " mm";

            return ShortTypeName();
        }

        public bool HasBendTableFile()
        {
            string type = (TypeName ?? "").ToLowerInvariant();
            return (type.Contains("bendtable") || (type.Contains("bend") && type.Contains("table")))
                && !string.IsNullOrWhiteSpace(BendTableFile);
        }

        private string ShortTypeName()
        {
            return string.IsNullOrWhiteSpace(TypeName)
                ? ("Type " + Type)
                : TypeName.Replace("swBendAllowance", "");
        }

        private static string ShortPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return "<trong>";

            try { return Path.GetFileName(path); }
            catch { return path; }
        }

        private static bool SamePath(string left, string right)
        {
            left = NormalizePath(left);
            right = NormalizePath(right);
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return "";

            try { return Path.GetFullPath(path.Trim()).TrimEnd('\\'); }
            catch { return path.Trim().TrimEnd('\\'); }
        }

        private static bool NearlyEqual(double left, double right, double tolerance)
        {
            return Math.Abs(left - right) <= tolerance;
        }
    }

    public sealed class KegakiCheckResult
    {
        public KegakiCheckResult()
        {
            Results = new List<KegakiBendResult>();
            HighlightRowIndexes = new HashSet<int>();
        }

        public int CheckedCount { get; set; }
        public int ProcessedCount { get; set; }
        public int SkippedCount { get; set; }
        public bool Canceled { get; set; }
        public List<KegakiBendResult> Results { get; private set; }
        public HashSet<int> HighlightRowIndexes { get; private set; }
    }

    public sealed class KegakiBendResult
    {
        public string Status { get; set; }
        public string BuhinNo { get; set; }
        public string BomFileName { get; set; }
        public string Component { get; set; }
        public string BendName { get; set; }
        public double AngleDeg { get; set; }
        public double RadiusMm { get; set; }
        public bool BendDown { get; set; }
        public bool IsOverride { get; set; }
        public string DefaultSetting { get; set; }
        public string BendSetting { get; set; }
        public string Note { get; set; }
        public string PartPath { get; set; }
    }

    public static class ExcelKegakiExporter
    {
        public static void Export(List<KegakiBendResult> results)
        {
            try
            {
                List<List<KegakiBendResult>> groupedResults = GetDifferentGroups(GroupResults(results));
                if (groupedResults.Count == 0)
                {
                    MessageBox.Show(
                        "Khong co chi tiet NG hoac CHECK.",
                        "CHECK KEGAKI",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                int ngGroupCount = 0;
                int checkGroupCount = 0;
                foreach (List<KegakiBendResult> group in groupedResults)
                {
                    string status = GetGroupStatus(group);
                    if (status == "NG")
                        ngGroupCount++;
                    else if (status == "CHECK")
                        checkGroupCount++;
                }

                Type excelType = Type.GetTypeFromProgID("Excel.Application");
                if (excelType == null)
                {
                    MessageBox.Show("Khong tim thay Microsoft Excel.", "CHECK KEGAKI", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                dynamic xlApp = Activator.CreateInstance(excelType);
                dynamic xlWB = xlApp.Workbooks.Add();
                dynamic xlWS = xlWB.Sheets[1];
                xlWS.Name = "CHECK KEGAKI";

                WriteHeader(xlWS);
                WriteRows(xlWS, groupedResults);
                int lastRow = groupedResults.Count + 1;
                if (lastRow > 1)
                {
                    TrySort(xlWS, lastRow);
                    xlWS.Range["C2:G" + lastRow].WrapText = true;
                    xlWS.Range["A2:G" + lastRow].VerticalAlignment = -4160;
                    SetReadableColumnWidths(xlWS);
                    xlWS.Rows.AutoFit();
                }

                xlApp.Visible = true;
                MessageBox.Show(
                    "T\u00ECm th\u1EA5y " + groupedResults.Count
                    + " chi ti\u1EBFt c\u1EA7n ki\u1EC3m tra.\r\nNG: " + ngGroupCount
                    + "\r\nCHECK: " + checkGroupCount,
                    "CHECK KEGAKI",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Loi xuat Excel: " + ex.Message, "CHECK KEGAKI", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void WriteHeader(dynamic sheet)
        {
            string[] headers =
            {
                "Status",
                "\u90E8\u54C1\u756A\u53F7",
                "Setting chung",
                "Bend d\u00F9ng setting chung",
                "Bend setting ri\u00EAng",
                "T\u00EAn setting ri\u00EAng",
                "Note"
            };

            for (int i = 0; i < headers.Length; i++)
                sheet.Cells[1, i + 1].Value = headers[i];
        }

        private static void WriteRows(dynamic sheet, List<List<KegakiBendResult>> groups)
        {
            int excelRow = 2;
            foreach (List<KegakiBendResult> group in groups)
            {
                KegakiBendResult first = group[0];
                string groupStatus = GetGroupStatus(group);

                sheet.Cells[excelRow, 1].Value = groupStatus;
                sheet.Cells[excelRow, 2].Value = first.BuhinNo;
                sheet.Cells[excelRow, 3].Value = CleanSettingName(
                    JoinDistinct(group, delegate(KegakiBendResult row) { return row.DefaultSetting; }));
                sheet.Cells[excelRow, 4].Value = JoinBendNames(group, false);
                sheet.Cells[excelRow, 5].Value = JoinBendNames(group, true);
                sheet.Cells[excelRow, 6].Value = JoinOverrideSettings(group);
                sheet.Cells[excelRow, 7].Value = BuildClearNote(group);

                if (groupStatus == "NG")
                    sheet.Range["A" + excelRow + ":G" + excelRow].Interior.Color = Rgb(255, 199, 206);
                else if (groupStatus == "CHECK")
                    sheet.Range["A" + excelRow + ":G" + excelRow].Interior.Color = Rgb(255, 235, 156);
                else if (groupStatus == "SKIP")
                    sheet.Range["A" + excelRow + ":G" + excelRow].Interior.Color = Rgb(217, 217, 217);

                excelRow++;
            }
        }

        private static List<List<KegakiBendResult>> GroupResults(List<KegakiBendResult> results)
        {
            List<List<KegakiBendResult>> groups = new List<List<KegakiBendResult>>();
            Dictionary<string, int> groupIndexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (KegakiBendResult result in results)
            {
                string key = (result.BuhinNo ?? "") + "\u001F"
                    + (result.Component ?? "") + "\u001F"
                    + (result.PartPath ?? "");

                int groupIndex;
                if (!groupIndexes.TryGetValue(key, out groupIndex))
                {
                    groupIndex = groups.Count;
                    groupIndexes.Add(key, groupIndex);
                    groups.Add(new List<KegakiBendResult>());
                }

                groups[groupIndex].Add(result);
            }

            return groups;
        }

        private static List<List<KegakiBendResult>> GetDifferentGroups(
            List<List<KegakiBendResult>> groups)
        {
            List<List<KegakiBendResult>> differentGroups = new List<List<KegakiBendResult>>();
            foreach (List<KegakiBendResult> group in groups)
            {
                string status = GetGroupStatus(group);
                if (status == "NG" || status == "CHECK")
                    differentGroups.Add(group);
            }

            return differentGroups;
        }

        private static string GetGroupStatus(List<KegakiBendResult> group)
        {
            bool hasCheck = false;
            bool allSkip = true;
            foreach (KegakiBendResult row in group)
            {
                if (row.Status == "NG")
                    return "NG";
                if (row.Status == "CHECK")
                    hasCheck = true;
                if (row.Status != "SKIP")
                    allSkip = false;
            }

            if (hasCheck)
                return "CHECK";
            return allSkip ? "SKIP" : "OK";
        }

        private static string JoinBendNames(List<KegakiBendResult> group, bool overridesOnly)
        {
            List<string> values = new List<string>();
            foreach (KegakiBendResult row in group)
            {
                if (IsDefaultSummary(row) || row.IsOverride != overridesOnly)
                    continue;

                if (!string.IsNullOrWhiteSpace(row.BendName))
                    values.Add(row.BendName);
            }

            return string.Join("\n", values.ToArray());
        }

        private static string JoinOverrideSettings(List<KegakiBendResult> group)
        {
            List<string> values = new List<string>();
            foreach (KegakiBendResult row in group)
            {
                if (!row.IsOverride)
                    continue;

                values.Add(CleanSettingName(row.BendSetting));
            }

            return string.Join("\n", values.ToArray());
        }

        private static string BuildClearNote(List<KegakiBendResult> group)
        {
            if (group.Count == 1 && IsDefaultSummary(group[0]))
                return group[0].Note ?? "";

            List<string> notes = new List<string>();
            foreach (KegakiBendResult row in group)
            {
                if (!row.IsOverride)
                    continue;

                string setting = CleanSettingName(row.BendSetting);
                if (row.Status == "NG")
                    notes.Add(row.BendName + ": d\u00F9ng " + setting + ", kh\u00E1c setting chung");
                else if (row.Status == "OK")
                    notes.Add(row.BendName + ": setting ri\u00EAng gi\u1ED1ng setting chung");
                else
                    notes.Add(row.BendName + ": c\u1EA7n ki\u1EC3m tra " + setting);
            }

            if (notes.Count == 0)
                return "T\u1EA5t c\u1EA3 Bend d\u00F9ng setting chung";

            return string.Join("\n", notes.ToArray());
        }

        private static string CleanSettingName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            return value.Replace("BendTable: ", "").Trim();
        }

        private static string JoinDistinct(
            List<KegakiBendResult> group,
            Func<KegakiBendResult, string> selector)
        {
            List<string> values = new List<string>();
            foreach (KegakiBendResult row in group)
            {
                string value = selector(row) ?? "";
                if (!string.IsNullOrWhiteSpace(value) && !values.Contains(value))
                    values.Add(value);
            }
            return string.Join("\n", values.ToArray());
        }

        private static bool IsDefaultSummary(KegakiBendResult row)
        {
            return string.Equals(
                row.BendName,
                "Sheet-Metal default",
                StringComparison.OrdinalIgnoreCase);
        }

        private static void TrySort(dynamic sheet, int lastRow)
        {
            try
            {
                dynamic sort = sheet.Sort;
                sort.SortFields.Clear();
                sort.SortFields.Add(sheet.Range["B2:B" + lastRow], 0, 1);
                sort.SetRange(sheet.Range["A1:G" + lastRow]);
                sort.Header = 1;
                sort.Apply();
            }
            catch
            {
            }
        }

        private static void SetReadableColumnWidths(dynamic sheet)
        {
            sheet.Columns[1].ColumnWidth = 9;
            sheet.Columns[2].ColumnWidth = 11;
            sheet.Columns[3].ColumnWidth = 30;
            sheet.Columns[4].ColumnWidth = 25;
            sheet.Columns[5].ColumnWidth = 25;
            sheet.Columns[6].ColumnWidth = 32;
            sheet.Columns[7].ColumnWidth = 48;
        }

        private static int Rgb(int red, int green, int blue)
        {
            return red + (green << 8) + (blue << 16);
        }
    }
}
