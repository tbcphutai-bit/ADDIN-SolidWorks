using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using System.Xml;
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
            bool oldGraphicsUpdate = true;

            Debug.WriteLine("[CHECK KEGAKI] ===== RUN START =====");
            try
            {
                ModelDoc2 activeModel = swApp.ActiveDoc as ModelDoc2;
                activeView = activeModel == null ? null : activeModel.ActiveView as ModelView;
                if (activeView != null)
                {
                    oldGraphicsUpdate = activeView.EnableGraphicsUpdate;
                    activeView.EnableGraphicsUpdate = false;
                }

                swApp.CommandInProgress = true;

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
                    activeView.EnableGraphicsUpdate = oldGraphicsUpdate;
                checker.Dispose();

                Debug.WriteLine("[CHECK KEGAKI] ===== RUN END ===== results="
                    + result.Results.Count + ", canceled=" + result.Canceled);
            }

            List<string> stockWarnings = new List<string>();
            int stockWarningCount = 0;
            foreach (KegakiBendResult row in result.Results)
            {
                if (!row.IsPurchasedStockBendWarning)
                    continue;
                stockWarningCount++;
                if (stockWarnings.Count < 10)
                    stockWarnings.Add((string.IsNullOrWhiteSpace(row.Component) ? row.PartPath : row.Component)
                        + ": " + row.BendName);
            }
            if (stockWarningCount > 0)
            {
                MessageBox.Show("Phát hiện " + stockWarningCount
                    + " chi tiết định hình/mua sẵn có bend line.\n"
                    + "Hãy kiểm tra lại vật liệu hoặc feature bend line.\n\n"
                    + string.Join("\n", stockWarnings.ToArray())
                    + (stockWarningCount > stockWarnings.Count ? "\nXem đầy đủ trong kết quả CHECK KEGAKI." : ""),
                    "CHECK KEGAKI - Cảnh báo chi tiết định hình",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
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

    public sealed partial class KegakiChecker : IDisposable
    {
        private const double BendTableToleranceMm = 0.001;
        private const string XToolBendTableDirectory =
            @"C:\Program Files\XTool\XTool\SwTemplete\2024\BendTable";
        private const string StandardBendTableDirectory =
            @"C:\sw共有ライブラリ\テーブル\菊川ベンドテーブル_170905";

        private readonly ISldWorks swApp;
        private readonly BendTableDeepReader bendTableReader;

        public KegakiChecker(ISldWorks app)
        {
            swApp = app;
            bendTableReader = new BendTableDeepReader();
        }

        public void Dispose()
        {
            bendTableReader.Dispose();
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

            return SingleStatus(
                "CHECK", buhinNo, bomFileName, componentName, path,
                "Component chua co file part da luu; chua the kiem tra tren ban sao.");
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

            try
            {
                using (CheckPartWorkingCopy copy = CheckPartWorkingCopy.Open(swApp, partPath, configuration))
                {
                    List<KegakiBendResult> results = CheckModel(copy.Model, buhinNo, bomFileName,
                        string.IsNullOrWhiteSpace(componentName) ? Path.GetFileNameWithoutExtension(partPath) : componentName,
                        copy.SourcePath);
                    copy.ValidateSource();
                    foreach (KegakiBendResult row in results)
                        row.Note = "Kiem tra tren ban sao part da luu; " + row.Note;
                    return results;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[CHECK KEGAKI] CheckPart ERROR: " + ex);
                return SingleStatus("CHECK", buhinNo, bomFileName, componentName, partPath, ex.Message);
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

            CheckPartWorkingCopy.RequireWorkingCopy(model);
            List<Feature> features = CollectFeatures(model);
            string materialDatabase;
            string materialCategory;
            string materialName = ReadComponentMaterialName(model, out materialDatabase, out materialCategory);
            if (IsPurchasedStockMaterial(materialName))
                return CheckPurchasedStock(features, materialName, materialCategory,
                    buhinNo, bomFileName, componentName, partPath);

            List<BendAllowanceInfo> defaults = new List<BendAllowanceInfo>();
            List<Feature> bends = new List<Feature>();
            List<Feature> curvedFeatures = new List<Feature>();
            List<double> sheetThicknesses = new List<double>();

            foreach (Feature feature in features)
            {
                string typeName = SafeFeatureType(feature);
                if (string.Equals(typeName, "SheetMetal", StringComparison.OrdinalIgnoreCase))
                {
                    BendAllowanceInfo capturedDefault = null;
                    try
                    {
                        ISheetMetalFeatureData data = feature.GetDefinition() as ISheetMetalFeatureData;
                        if (data != null)
                        {
                            capturedDefault = BendAllowanceInfo.Capture(data.GetCustomBendAllowance());
                            try
                            {
                                double thickness = Math.Abs(data.Thickness);
                                if (thickness > 0.0 && !ContainsNear(sheetThicknesses, thickness, 0.000001))
                                    sheetThicknesses.Add(thickness);
                            }
                            catch { }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("[CHECK KEGAKI] SheetMetal definition ERROR: " + ex.Message);
                    }
                    // Keep an unreadable default in the collection. Dropping it
                    // could attribute another body's scalar setting to this bend.
                    defaults.Add(capturedDefault);
                }
                else if (IsBendFeatureType(typeName))
                {
                    bends.Add(feature);
                }
                else if (IsFoldUnfoldFeatureType(typeName))
                {
                    // RuledBend and UiFreeformBend are the Fold/Unfold pair
                    // generated for the same geometry. They are not separate
                    // production bends and must not be exported or counted.
                    continue;
                }
                else if (IsCurvedSheetMetalType(typeName) && !IsFeatureSuppressed(feature))
                {
                    curvedFeatures.Add(feature);
                }
            }

            bool hasCurvedGeometry = false;
            double curvedRadiusMm = 0.0;
            if (defaults.Count > 0
                && bends.Count == 0
                && curvedFeatures.Count == 0)
                hasCurvedGeometry = HasPairedCurvedMainFaces(model, sheetThicknesses, out curvedRadiusMm);

            if (defaults.Count == 0
                && bends.Count == 0
                && curvedFeatures.Count == 0
                && !hasCurvedGeometry)
                return SingleStatus("SKIP", buhinNo, bomFileName, componentName, partPath, "Khong phai sheet metal");

            string defaultSummary = JoinDefaultSummaries(defaults);
            MaterialTableCheck materialCheck = CheckMaterialAgainstDefaultBendTables(
                materialName,
                materialDatabase,
                materialCategory,
                defaults,
                sheetThicknesses,
                bends.Count == 0 && curvedFeatures.Count == 0 && !hasCurvedGeometry);
            if (bends.Count == 0 && curvedFeatures.Count == 0)
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
                summaryRow.RadiusMm = curvedRadiusMm;
                if (hasCurvedGeometry && IsLargeRadius(curvedRadiusMm))
                {
                    CurvedAllowanceCheck curvedCheck = EvaluateCurvedAllowance(defaults);
                    summaryRow.BendName = "Curved geometry confirmed";
                    summaryRow.Status = curvedCheck.Status;
                    summaryRow.Note = "Cung R=" + curvedRadiusMm.ToString("0.###")
                        + "mm > 0.2mm, khong can bend line; " + curvedCheck.Note;
                    CopyMaterialIdentity(summaryRow, materialCheck);
                    return new List<KegakiBendResult> { summaryRow };
                }

                summaryRow.Status = HasUsableBendTable(defaults) ? "OK" : "CHECK";
                summaryRow.Note = HasUsableBendTable(defaults)
                    ? "Da xuat Bend Table mac dinh; chua tim thay bend rieng"
                    : "Khong tim thay bend rieng va khong doc duoc Bend Table mac dinh";
                ApplyMaterialCheck(summaryRow, materialCheck);
                return new List<KegakiBendResult> { summaryRow };
            }

            List<KegakiBendResult> results = new List<KegakiBendResult>();
            foreach (Feature bendFeature in bends)
            {
                KegakiBendResult bendResult = CheckBend(
                    bendFeature,
                    defaults,
                    defaultSummary,
                    materialCheck,
                    buhinNo,
                    bomFileName,
                    componentName,
                    partPath);
                if (bendResult.IsLargeRadiusBend)
                {
                    // This bend is validated against K-Factor=0.5, not the
                    // Sheet-Metal default Bend Table used by ordinary bends.
                    CopyMaterialIdentity(bendResult, materialCheck);
                    if (string.IsNullOrWhiteSpace(bendResult.MaterialName)
                        || string.IsNullOrWhiteSpace(bendResult.MaterialGroup))
                    {
                        bendResult.Status = MergeStatus(bendResult.Status, "CHECK");
                        bendResult.Note = AppendNote(bendResult.Note,
                            "Khong xac dinh duoc nhom vat lieu");
                    }
                }
                else if (IsAlpolicMaterial(materialCheck.MaterialName))
                {
                    if (bendResult.IsOverride && !string.IsNullOrWhiteSpace(bendResult.OverrideBendTablePath))
                        ApplyBendTableIdentityCheck(bendResult, materialCheck);
                    else
                        ApplyMaterialCheck(bendResult, materialCheck);
                }
                else
                {
                    CopyMaterialIdentity(bendResult, materialCheck);
                    bendResult.Status = MergeStatus(bendResult.Status, materialCheck.Status);
                    if (materialCheck.Status == "CHECK")
                        bendResult.Note = AppendNote(bendResult.Note, materialCheck.Note);
                    if (IsGrooveRadius(bendResult.RadiusMm))
                    {
                        bendResult.BendTableGroup = "溝加工";
                        CaptureIntendedGrooveLevel(model, bendFeature, bendResult);
                    }
                }
                results.Add(bendResult);
            }

            Dictionary<int, FlatBendMeasurement> flatMeasurements = KegakiFlatPatternReader.Measure(
                model, features, bends, results);
            for (int i = 0; i < results.Count; i++)
            {
                FlatBendMeasurement measurement;
                flatMeasurements.TryGetValue(i, out measurement);
                ApplyPerBendValueCheck(results[i], sheetThicknesses, measurement);
            }

            foreach (Feature curvedFeature in curvedFeatures)
            {
                KegakiBendResult curvedResult = CheckCurvedFeature(
                    curvedFeature,
                    defaults,
                    defaultSummary,
                    materialCheck,
                    buhinNo,
                    bomFileName,
                    componentName,
                    partPath);
                results.Add(curvedResult);
            }

            return results;
        }

        private static KegakiBendResult CheckCurvedFeature(
            Feature feature,
            List<BendAllowanceInfo> defaults,
            string defaultSummary,
            MaterialTableCheck materialCheck,
            string buhinNo,
            string bomFileName,
            string componentName,
            string partPath)
        {
            KegakiBendResult result = NewResult(
                buhinNo,
                bomFileName,
                componentName,
                partPath);
            result.BendName = SafeFeatureName(feature) + " [" + SafeFeatureType(feature) + "]";
            result.DefaultSetting = defaultSummary;
            result.BendSetting = defaultSummary;
            CopyMaterialIdentity(result, materialCheck);

            CurvedAllowanceCheck allowanceCheck = EvaluateCurvedAllowance(defaults);
            result.Status = allowanceCheck.Status;
            result.Note = allowanceCheck.Note;
            Debug.WriteLine("[CHECK KEGAKI] curvedFeature=" + result.BendName
                + ", setting=" + defaultSummary
                + ", status=" + result.Status
                + ", note=" + result.Note);
            return result;
        }

        private static CurvedAllowanceCheck EvaluateCurvedAllowance(
            List<BendAllowanceInfo> defaults)
        {
            CurvedAllowanceCheck result = new CurvedAllowanceCheck();
            if (defaults == null || defaults.Count == 0)
            {
                result.Status = "CHECK";
                result.Note = "Feature cong nhung khong doc duoc Sheet-Metal setting";
                return result;
            }

            List<double> kFactors = new List<double>();
            int otherSettingCount = 0;
            foreach (BendAllowanceInfo allowance in defaults)
            {
                if (allowance == null || string.IsNullOrWhiteSpace(allowance.TypeName)
                    || allowance.TypeName.StartsWith("Type ", StringComparison.OrdinalIgnoreCase)
                    || (allowance.IsKFactorSetting() && (!allowance.HasKFactor
                        || !BendValueMath.IsFinite(allowance.KFactor))))
                {
                    result.Status = "CHECK";
                    result.Note = "Chua doc duoc day du Sheet-Metal setting cho feature cong.";
                    return result;
                }
                if (allowance.IsKFactorSetting())
                    kFactors.Add(allowance.KFactor);
                else
                    otherSettingCount++;
            }

            if (kFactors.Count == 0)
            {
                result.Status = "NG";
                result.Note = "Feature cong khong dung K-Factor=0.5";
                return result;
            }

            if (otherSettingCount > 0)
            {
                result.Status = "CHECK";
                result.Note = "Co nhieu Sheet-Metal setting; chua anh xa duoc K-Factor cho feature cong";
                return result;
            }

            List<string> values = new List<string>();
            bool allCorrect = true;
            foreach (double kFactor in kFactors)
            {
                string value = kFactor.ToString("0.####");
                if (!values.Contains(value))
                    values.Add(value);
                if (Math.Abs(kFactor - 0.5) > 0.001)
                    allCorrect = false;
            }

            string joinedValues = string.Join(" | ", values.ToArray());
            result.Status = allCorrect ? "OK" : "NG";
            result.Note = allCorrect
                ? "Feature cong dung K-Factor=0.5"
                : "Feature cong co K-Factor=" + joinedValues + "; yeu cau 0.5";
            return result;
        }

        private MaterialTableCheck CheckMaterialAgainstDefaultBendTables(
            string materialName,
            string materialDatabase,
            string materialCategory,
            List<BendAllowanceInfo> defaults,
            List<double> sheetThicknesses,
            bool isFlatSheet)
        {
            MaterialTableCheck result = new MaterialTableCheck
            {
                MaterialName = materialName,
                MaterialDatabase = materialDatabase,
                MaterialCategory = materialCategory,
                MaterialGroup = ResolveMaterialGroup(materialName, materialCategory),
                Status = "OK"
            };
            if (isFlatSheet && IsAlpolicMaterial(materialName)) result.MaterialGroup = "AL";
            if (sheetThicknesses != null && sheetThicknesses.Count == 1)
                result.SheetThicknessMm = sheetThicknesses[0] * 1000.0;
            if (!isFlatSheet && !IsAlpolicMaterial(materialName))
            {
                var storedNames = new List<string>();
                if (defaults != null) foreach (BendAllowanceInfo allowance in defaults)
                {
                    if (allowance == null || !allowance.HasBendTableFile()) continue;
                    string storedName = allowance.GetBendTableFileName();
                    if (!storedNames.Contains(storedName)) storedNames.Add(storedName);
                    if (string.IsNullOrWhiteSpace(result.BendTablePath))
                        result.BendTablePath = allowance.GetBendTableFilePath();
                }
                result.BendTableName = string.Join(" | ", storedNames.ToArray());
                result.StandardBendTablePath = ResolveStandardBendTablePathForMaterialGroup(result.MaterialGroup);
                result.StandardBendTableName = SafeFileName(result.StandardBendTablePath);
                result.Status = string.IsNullOrWhiteSpace(result.MaterialGroup)
                    || result.SheetThicknessMm <= 0 ? "CHECK" : "OK";
                result.Note = result.Status == "OK"
                    ? "He so bend duoc doi chieu tu du lieu trong model; ten bang chi de hien thi."
                    : "Thieu nhom vat lieu hoac mot do day duy nhat de doi chieu he so bend.";
                return result;
            }
            if (defaults == null || defaults.Count == 0)
            {
                result.Status = "CHECK";
                result.Note = "Khong doc duoc Bend Table mac dinh.";
                return result;
            }
            var names = new List<string>();
            string deepStatus = "OK";
            foreach (BendAllowanceInfo allowance in defaults)
            {
                if (allowance == null || !allowance.HasBendTableFile())
                {
                    result.Status = MergeStatus(result.Status, "CHECK");
                    deepStatus = MergeStatus(deepStatus, "CHECK");
                    result.Note = AppendNote(result.Note, "Khong doc duoc Bend Table mac dinh.");
                    continue;
                }
                string name = allowance.GetBendTableFileName();
                if (!names.Contains(name)) names.Add(name);
                BendTableContentAssessment assessment = AssessTableContent(allowance, materialName,
                    result.MaterialGroup, result.SheetThicknessMm, isFlatSheet);
                CopyContentAssessment(result, assessment);
                result.Status = MergeStatus(result.Status, assessment.Status);
                deepStatus = MergeStatus(deepStatus, assessment.Status);
                result.Note = AppendNote(result.Note, assessment.Detail);
            }
            result.BendTableName = string.Join(" | ", names.ToArray());
            result.DeepStatus = deepStatus;
            result.DeepNote = result.Note;
            result.Note = (isFlatSheet ? "Tam phang; " : "") + result.Note;
            Debug.WriteLine("[CHECK KEGAKI] material=" + result.MaterialName
                + ", materialGroup=" + result.MaterialGroup
                + ", bendTable=" + result.BendTableName
                + ", contentStatus=" + result.Status + ", " + result.Note);
            return result;
        }

        private static string ResolveActualBendTablePath(
            string requestedPath,
            string requestedName)
        {
            if (!string.IsNullOrWhiteSpace(requestedPath) && File.Exists(requestedPath))
                return requestedPath;

            // A missing explicit path is unavailable evidence; a same-name
            // file in another directory cannot establish its actual content.
            if (!string.IsNullOrWhiteSpace(requestedPath) && Path.IsPathRooted(requestedPath))
                return null;
            string path = FindFileIgnoreCase(XToolBendTableDirectory, requestedName);
            if (!string.IsNullOrWhiteSpace(path))
                return path;
            return FindFileIgnoreCase(StandardBendTableDirectory, requestedName);
        }

        private static string ResolveStandardBendTablePath(string requestedName)
        {
            string standardName = MapStandardBendTableName(requestedName);
            string path = FindFileIgnoreCase(StandardBendTableDirectory, standardName);
            if (!string.IsNullOrWhiteSpace(path))
                return path;
            return FindFileIgnoreCase(StandardBendTableDirectory, requestedName);
        }

        private static string MapStandardBendTableName(string requestedName)
        {
            string fileName = SafeFileName(requestedName);
            string normalized = NormalizeLookupText(fileName);
            if (normalized == "BD_KIKU_ST.XLSX")
                return "BD_Kiku_ST_2020.xlsx";
            if (normalized == "BD_KIKU_AL.XLSX")
                return "BD_Kiku_AL_2020.xlsx";
            if (normalized == "BD_KIKU_SUS.XLSX")
                return "BD_Kiku_SUS_2020.xlsx";
            if (normalized == "BD_KIKU_RBS,BS,CU.XLSX")
                return "BD_Kiku_RBs,Bs,Cu_2020.xlsx";
            return fileName;
        }

        private static string FindFileIgnoreCase(string directory, string fileName)
        {
            fileName = SafeFileName(fileName);
            if (string.IsNullOrWhiteSpace(directory)
                || string.IsNullOrWhiteSpace(fileName)
                || !Directory.Exists(directory))
            {
                return "";
            }

            string direct = Path.Combine(directory, fileName);
            if (File.Exists(direct))
                return direct;

            try
            {
                foreach (string path in Directory.GetFiles(directory, "*.xlsx"))
                {
                    if (string.Equals(
                        Path.GetFileName(path),
                        fileName,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        return path;
                    }
                }
            }
            catch { }
            return "";
        }

        private static string SafeFileName(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return "";
            try { return Path.GetFileName(path.Trim()); }
            catch { return path.Trim(); }
        }

        private static string FormatNullable(double? value)
        {
            return value.HasValue ? value.Value.ToString("0.###") : "<khong doc duoc>";
        }

        private KegakiBendResult CheckBend(
            Feature feature,
            List<BendAllowanceInfo> defaults,
            string defaultSummary,
            MaterialTableCheck materialCheck,
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
                object defObj = null;
                try { defObj = feature.GetDefinition(); } catch { }

                double bendAngle = 0.0;
                double bendRadius = 0.0;
                bool bendDown = false;
                bool useDefault = true;
                object customAllowanceObj = null;
                bool dataReadSuccess = false;
                bool angleRead = false;
                bool radiusRead = false;
                bool defaultRead = false;

                IOneBendFeatureData oneBend = defObj as IOneBendFeatureData;
                if (oneBend != null)
                {
                    dataReadSuccess = true;
                    try { bendAngle = oneBend.BendAngle; angleRead = true; } catch { }
                    try { bendRadius = oneBend.BendRadius; radiusRead = true; } catch { }
                    try { bendDown = oneBend.BendDown; } catch { }
                    try { useDefault = oneBend.UseDefaultBendAllowance; defaultRead = true; } catch { }
                    try { customAllowanceObj = oneBend.GetCustomBendAllowance(); } catch { }
                }
                else
                {
                    IEdgeFlangeFeatureData edgeFlange = defObj as IEdgeFlangeFeatureData;
                    if (edgeFlange != null)
                    {
                        dataReadSuccess = true;
                        try { bendAngle = edgeFlange.BendAngle; angleRead = true; } catch { }
                        try { bendRadius = edgeFlange.BendRadius; radiusRead = true; } catch { }
                        try { useDefault = edgeFlange.UseDefaultBendAllowance; defaultRead = true; } catch { }
                        try { customAllowanceObj = edgeFlange.GetCustomBendAllowance(); } catch { }
                    }
                }

                if (!dataReadSuccess && defObj != null)
                {
                    try
                    {
                        dynamic dynamicData = defObj;
                        try { bendAngle = Convert.ToDouble(dynamicData.BendAngle); angleRead = true; } catch { }
                        try { bendRadius = Convert.ToDouble(dynamicData.BendRadius); radiusRead = true; } catch { }
                        try { useDefault = Convert.ToBoolean(dynamicData.UseDefaultBendAllowance); defaultRead = true; } catch { }
                        try { customAllowanceObj = dynamicData.GetCustomBendAllowance(); } catch { }
                        dataReadSuccess = true;
                    }
                    catch { }
                }

                if (!dataReadSuccess)
                {
                    result.Status = "CHECK";
                    result.Note = "Khong doc duoc FeatureData cua " + SafeFeatureType(feature);
                    return result;
                }

                result.AngleDeg = Math.Abs(bendAngle * 180.0 / Math.PI);
                result.RadiusMm = Math.Abs(bendRadius * 1000.0);
                result.BendDown = bendDown;
                result.HasBendGeometry = angleRead && radiusRead && defaultRead;
                result.IsOverride = !useDefault;

                if (!result.HasBendGeometry)
                {
                    result.Status = "CHECK";
                    result.Note = "Khong doc duoc goc/R hoac cach ke thua setting cua bend.";
                    return result;
                }

                BendAllowanceInfo bendAllowance = BendAllowanceInfo.Capture(customAllowanceObj);
                result.EffectiveAllowance = useDefault
                    ? ResolveUniqueStoredDefaultAllowance(defaults) : bendAllowance;
                result.EffectiveBendSetting = result.EffectiveAllowance == null
                    ? "Khong xac dinh" : result.EffectiveAllowance.Summary();

                bool isAlpolic = materialCheck != null
                    && IsAlpolicMaterial(materialCheck.MaterialName);

                const double radiusThresholdMm = 0.2;
                if (IsLargeRadius(result.RadiusMm))
                {
                    result.IsLargeRadiusBend = true;
                    bool isKFactor05 = result.EffectiveAllowance != null
                        && result.EffectiveAllowance.IsKFactorSetting()
                        && result.EffectiveAllowance.HasKFactor
                        && Math.Abs(result.EffectiveAllowance.KFactor - 0.5) <= 0.005;
                    result.IsOverride = true;
                    if (result.EffectiveAllowance == null
                        || string.IsNullOrWhiteSpace(result.EffectiveAllowance.TypeName)
                        || result.EffectiveAllowance.TypeName.StartsWith("Type ", StringComparison.OrdinalIgnoreCase)
                        || (result.EffectiveAllowance.IsKFactorSetting() && !result.EffectiveAllowance.HasKFactor))
                    {
                        result.Status = "CHECK";
                        result.BendSetting = result.EffectiveBendSetting;
                        result.Note = "R > 0.2mm: chua doc/anh xa duoc setting K-factor dang co hieu luc.";
                        return result;
                    }

                    if (isKFactor05)
                    {
                        result.Status = "OK";
                        result.BendSetting = "KFactor=0.5";
                        result.Note = "Cung luon lon (R="
                            + result.RadiusMm.ToString("0.###")
                            + " > " + radiusThresholdMm.ToString("0.###")
                            + "mm) dung K-Factor=0.5 hop le";
                    }
                    else
                    {
                        result.Status = "NG";
                        result.BendSetting = result.EffectiveBendSetting;
                        result.Note = "Cung luon lon (R="
                            + result.RadiusMm.ToString("0.###")
                            + " > " + radiusThresholdMm.ToString("0.###")
                            + "mm) bat buoc dung K-Factor=0.5 (Hien tai: "
                            + result.BendSetting + ")";
                    }
                    return result;
                }

                result.IsOverride = !useDefault;
                result.BendSetting = useDefault ? "Default Sheet-Metal" : result.EffectiveBendSetting;
                if (!useDefault && bendAllowance != null && bendAllowance.HasBendTableFile())
                    result.OverrideBendTablePath = bendAllowance.GetBendTableFilePath();

                // Alpolic retains its existing .8 table check and inverse skip.
                if (isAlpolic)
                {
                    BendTableContentAssessment alpolicTable = AssessTableContent(result.EffectiveAllowance,
                        materialCheck.MaterialName, materialCheck.MaterialGroup,
                        materialCheck.SheetThicknessMm, false);
                    result.Status = alpolicTable.Status;
                    result.Note = alpolicTable.Detail;
                    return result;
                }

                bool grooveRadius = IsGrooveRadius(result.RadiusMm);
                if (!grooveRadius && Math.Abs(result.RadiusMm - 0.1) > 0.03)
                {
                    result.Status = "NG";
                    result.Note = "Canh be thuong phai R=0.1mm; canh bao phai R=0.2mm (Hien tai R="
                        + result.RadiusMm.ToString("0.###") + "mm).";
                    return result;
                }
                result.BendTableGroup = grooveRadius ? "溝加工" : materialCheck == null ? "" : materialCheck.MaterialGroup;
                result.Status = "OK";
                result.Note = grooveRadius
                    ? "R=0.2mm: cho doi chieu he so thuc te voi muc bao da chi dinh."
                    : "R=0.1mm: cho doi chieu he so thuc te voi database theo vat lieu.";
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



        private void ApplyBendTableIdentityCheck(KegakiBendResult row, MaterialTableCheck materialCheck)
        {
            CopyMaterialIdentity(row, materialCheck);
            BendTableContentAssessment assessment = AssessTableContent(row.EffectiveAllowance,
                row.MaterialName, row.MaterialGroup, row.SheetThicknessMm, false);
            row.BendTableName = SafeFileName(assessment.ActualPath);
            row.BendTableGroup = assessment.TableGroup;
            row.StandardBendTableName = SafeFileName(assessment.StandardPath);
            row.BendCoefficientMm = assessment.ActualCoefficientMm;
            row.StandardBendCoefficientMm = assessment.StandardCoefficientMm;
            row.BendTableDeepStatus = assessment.Status;
            row.BendTableDeepNote = assessment.Detail;
            row.Status = MergeStatus(row.Status, assessment.Status);
            row.Note = AppendNote(row.Note, assessment.Detail);
        }

        private static void CompletePerBendCheck(KegakiBendResult row, string status, string note)
        {
            row.BendValueStatus = status;
            row.BendValueNote = note;
            row.Status = MergeStatus(row.Status, status);
            row.Note = AppendNote(row.Note, "So he so bend: " + note);
            Debug.WriteLine("[CHECK KEGAKI] bend=" + row.BendName + ", valueStatus=" + status
                + ", source=" + row.BendValueSource + ", " + note);
        }

        private static string ResolveStandardBendTablePathForMaterialGroup(
            string materialGroup)
        {
            string standardName = "";
            if (string.Equals(materialGroup, "SUS", StringComparison.OrdinalIgnoreCase))
                standardName = "BD_Kiku_SUS_2020.xlsx";
            else if (string.Equals(materialGroup, "ST", StringComparison.OrdinalIgnoreCase))
                standardName = "BD_Kiku_ST_2020.xlsx";
            else if (string.Equals(materialGroup, "AL", StringComparison.OrdinalIgnoreCase))
                standardName = "BD_Kiku_AL_2020.xlsx";
            else if (string.Equals(materialGroup, "CU", StringComparison.OrdinalIgnoreCase))
                standardName = "BD_Kiku_RBs,Bs,Cu_2020.xlsx";

            return FindFileIgnoreCase(StandardBendTableDirectory, standardName);
        }

        private static void ApplyMaterialCheck(
            KegakiBendResult row,
            MaterialTableCheck check)
        {
            if (row == null || check == null)
                return;

            row.MaterialName = check.MaterialName ?? "";
            row.MaterialGroup = check.MaterialGroup ?? "";
            row.BendTableGroup = check.BendTableGroup ?? "";
            row.SheetThicknessMm = check.SheetThicknessMm;
            row.BendTableName = string.IsNullOrWhiteSpace(check.BendTableName)
                ? SafeFileName(check.BendTablePath) : check.BendTableName;
            row.StandardBendTableName = check.StandardBendTableName ?? "";
            row.BendCoefficientMm = check.ActualCoefficientMm;
            row.StandardBendCoefficientMm = check.StandardCoefficientMm;
            row.BendTableDeepStatus = check.DeepStatus ?? "";
            row.BendTableDeepNote = check.DeepNote ?? "";
            row.Status = MergeStatus(row.Status, check.Status);
            row.Note = AppendNote(row.Note, check.Note);
        }

        private static void CopyMaterialIdentity(
            KegakiBendResult row,
            MaterialTableCheck check)
        {
            if (row == null || check == null)
                return;

            row.MaterialName = check.MaterialName ?? "";
            row.MaterialGroup = check.MaterialGroup ?? "";
            row.BendTableGroup = check.BendTableGroup ?? "";
            row.SheetThicknessMm = check.SheetThicknessMm;
            row.BendTableName = string.IsNullOrWhiteSpace(check.BendTableName)
                ? SafeFileName(check.BendTablePath) : check.BendTableName;
            row.StandardBendTableName = check.StandardBendTableName ?? "";
            row.BendCoefficientMm = check.ActualCoefficientMm;
            row.StandardBendCoefficientMm = check.StandardCoefficientMm;
            row.BendTableDeepStatus = check.DeepStatus ?? "";
            row.BendTableDeepNote = check.DeepNote ?? "";
        }



        private static string MergeStatus(string current, string added)
        {
            if (string.Equals(current, "NG", StringComparison.OrdinalIgnoreCase)
                || string.Equals(added, "NG", StringComparison.OrdinalIgnoreCase))
                return "NG";

            if (string.Equals(current, "CHECK", StringComparison.OrdinalIgnoreCase)
                || string.Equals(added, "CHECK", StringComparison.OrdinalIgnoreCase))
                return "CHECK";

            if (string.Equals(current, "SKIP", StringComparison.OrdinalIgnoreCase))
                return "SKIP";

            return string.IsNullOrWhiteSpace(current) ? (added ?? "") : current;
        }

        private static string AppendNote(string current, string added)
        {
            current = (current ?? "").Trim();
            added = (added ?? "").Trim();
            if (string.IsNullOrWhiteSpace(added)
                || current.IndexOf(added, StringComparison.OrdinalIgnoreCase) >= 0)
                return current;
            if (string.IsNullOrWhiteSpace(current))
                return added;
            return current + "; " + added;
        }

        private string ReadComponentMaterialName(
            ModelDoc2 model,
            out string materialDatabase,
            out string materialCategory)
        {
            materialDatabase = "";
            materialCategory = "";
            if (model == null)
                return "";

            string configuration = "";
            try { configuration = model.ConfigurationManager.ActiveConfiguration.Name; } catch { }

            try
            {
                string database = "";
                IPartDoc part = model as IPartDoc;
                string material = part == null
                    ? ""
                    : part.GetMaterialPropertyName2(configuration, out database);
                materialDatabase = (database ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(material))
                {
                    material = material.Trim();
                    materialCategory = ReadMaterialCategory(
                        model,
                        materialDatabase,
                        material);
                    return material;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[CHECK KEGAKI] GetMaterialPropertyName2 ERROR: " + ex.Message);
            }

            string[] propertyNames = { "\u6750\u8cea", "Material", "MATERIAL" };
            string[] configurations = { configuration, "" };
            foreach (string config in configurations)
            {
                CustomPropertyManager manager = null;
                try { manager = model.Extension.get_CustomPropertyManager(config ?? ""); }
                catch { }
                if (manager == null)
                    continue;

                foreach (string propertyName in propertyNames)
                {
                    string value = ReadCustomProperty(manager, propertyName);
                    if (!string.IsNullOrWhiteSpace(value))
                        return value.Trim();
                }
            }

            return "";
        }

        private string ReadMaterialCategory(
            ModelDoc2 model,
            string database,
            string materialName)
        {
            if (string.IsNullOrWhiteSpace(database)
                || string.IsNullOrWhiteSpace(materialName))
                return "";

            string databasePath = ResolveMaterialDatabasePath(model, database);
            if (string.IsNullOrWhiteSpace(databasePath))
            {
                Debug.WriteLine("[CHECK KEGAKI] Material database not found: " + database);
                return "";
            }

            try
            {
                XmlDocument document = new XmlDocument();
                document.XmlResolver = null;
                document.Load(databasePath);

                XmlNodeList materialNodes = document.SelectNodes(
                    "//*[translate(local-name(), 'MATERIAL', 'material')='material']");
                if (materialNodes == null)
                    return "";

                string wanted = NormalizeMaterialIdentity(materialName);
                foreach (XmlNode node in materialNodes)
                {
                    string nodeName = ReadXmlName(node);
                    if (!string.Equals(
                        NormalizeMaterialIdentity(nodeName),
                        wanted,
                        StringComparison.OrdinalIgnoreCase))
                        continue;

                    string nearestCategory = "";
                    XmlNode parent = node.ParentNode;
                    while (parent != null && parent.NodeType != XmlNodeType.Document)
                    {
                        string parentName = ReadXmlName(parent);
                        if (!string.IsNullOrWhiteSpace(parentName))
                        {
                            if (string.IsNullOrWhiteSpace(nearestCategory))
                                nearestCategory = parentName;
                            if (!string.IsNullOrWhiteSpace(NormalizeMaterialGroup(parentName)))
                            {
                                Debug.WriteLine("[CHECK KEGAKI] Material category found: "
                                    + parentName + " in " + databasePath);
                                return parentName;
                            }
                        }
                        parent = parent.ParentNode;
                    }

                    return nearestCategory;
                }

                Debug.WriteLine("[CHECK KEGAKI] Material node not found: "
                    + materialName + " in " + databasePath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[CHECK KEGAKI] Read material database ERROR: " + ex.Message);
            }

            return "";
        }

        private string ResolveMaterialDatabasePath(ModelDoc2 model, string database)
        {
            List<string> candidates = new List<string>();
            AddPathCandidate(candidates, database);
            if (!string.Equals(Path.GetExtension(database), ".sldmat", StringComparison.OrdinalIgnoreCase))
                AddPathCandidate(candidates, database + ".sldmat");

            string modelFolder = "";
            try
            {
                string modelPath = CheckPartWorkingCopy.GetSourcePath(model);
                if (!string.IsNullOrWhiteSpace(modelPath))
                    modelFolder = Path.GetDirectoryName(modelPath);
            }
            catch { }

            if (!string.IsNullOrWhiteSpace(modelFolder))
            {
                AddPathCandidate(candidates, Path.Combine(modelFolder, database));
                if (!string.Equals(Path.GetExtension(database), ".sldmat", StringComparison.OrdinalIgnoreCase))
                    AddPathCandidate(candidates, Path.Combine(modelFolder, database + ".sldmat"));
            }

            string configuredLocations = "";
            try
            {
                configuredLocations = swApp.GetUserPreferenceStringValue(
                    (int)swUserPreferenceStringValue_e.swFileLocationsMaterialDatabases);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[CHECK KEGAKI] Read material locations ERROR: " + ex.Message);
            }

            AddDatabaseCandidatesFromLocations(candidates, configuredLocations, database);
            AddDatabaseCandidatesFromLocations(
                candidates,
                @"C:\sw共有ライブラリ\材料ﾃﾞｰﾀﾍﾞｰｽ",
                database);

            foreach (string candidate in candidates)
            {
                try
                {
                    if (File.Exists(candidate))
                        return Path.GetFullPath(candidate);
                }
                catch { }
            }

            return "";
        }

        private static void AddDatabaseCandidatesFromLocations(
            List<string> candidates,
            string locations,
            string database)
        {
            if (candidates == null
                || string.IsNullOrWhiteSpace(locations)
                || string.IsNullOrWhiteSpace(database))
                return;

            string fileName = database.Trim();
            if (!string.Equals(Path.GetExtension(fileName), ".sldmat", StringComparison.OrdinalIgnoreCase))
                fileName += ".sldmat";

            char[] separators = { ';', '|', '\r', '\n' };
            foreach (string rawLocation in locations.Split(
                separators,
                StringSplitOptions.RemoveEmptyEntries))
            {
                string location = rawLocation.Trim().Trim('"');
                if (string.IsNullOrWhiteSpace(location))
                    continue;

                try
                {
                    if (Directory.Exists(location))
                    {
                        AddPathCandidate(candidates, Path.Combine(location, fileName));
                    }
                    else if (File.Exists(location))
                    {
                        string locationName = Path.GetFileNameWithoutExtension(location);
                        string databaseName = Path.GetFileNameWithoutExtension(fileName);
                        if (string.Equals(
                            locationName,
                            databaseName,
                            StringComparison.OrdinalIgnoreCase))
                            AddPathCandidate(candidates, location);
                    }
                }
                catch { }
            }
        }

        private static void AddPathCandidate(List<string> candidates, string value)
        {
            if (candidates == null || string.IsNullOrWhiteSpace(value))
                return;

            value = value.Trim().Trim('"');
            if (!candidates.Contains(value))
                candidates.Add(value);
        }

        private static string ReadXmlName(XmlNode node)
        {
            if (node == null || node.Attributes == null)
                return "";

            foreach (XmlAttribute attribute in node.Attributes)
            {
                if (string.Equals(attribute.LocalName, "name", StringComparison.OrdinalIgnoreCase))
                    return (attribute.Value ?? "").Trim();
            }

            return "";
        }

        private static bool IsPurchasedStockMaterial(string materialName)
        {
            string identity = NormalizeMaterialIdentity(materialName);
            return !string.IsNullOrWhiteSpace(identity)
                && !identity.EndsWith("-", StringComparison.Ordinal);
        }

        private static List<KegakiBendResult> CheckPurchasedStock(
            List<Feature> features, string materialName, string materialCategory,
            string buhinNo, string bomFileName, string componentName, string partPath)
        {
            List<string> bendLineFeatures = new List<string>();
            bool scanIncomplete = false;
            foreach (Feature feature in features)
            {
                string type = SafeFeatureType(feature);
                if (!string.Equals(type, "ProfileFeature", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(type, "3DProfileFeature", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    Sketch sketch = feature.GetSpecificFeature2() as Sketch;
                    if (sketch == null)
                    {
                        scanIncomplete = true;
                        continue;
                    }
                    Array segments = sketch.GetSketchSegments() as Array;
                    if (segments == null)
                        continue;
                    foreach (object item in segments)
                    {
                        SketchSegment segment = item as SketchSegment;
                        if (segment != null && segment.IsBendLine())
                        {
                            bendLineFeatures.Add(SafeFeatureName(feature));
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    scanIncomplete = true;
                    Debug.WriteLine("[CHECK KEGAKI] Purchased-stock bend-line scan ERROR: "
                        + SafeFeatureName(feature) + ": " + ex.Message);
                }
            }

            KegakiBendResult row = NewResult(buhinNo, bomFileName, componentName, partPath);
            row.MaterialName = materialName;
            row.MaterialGroup = ResolveMaterialGroup(materialName, materialCategory);
            row.IsPurchasedStockBendWarning = bendLineFeatures.Count > 0;
            row.BendName = string.Join(" | ", bendLineFeatures.ToArray());
            row.Status = row.IsPurchasedStockBendWarning || scanIncomplete ? "CHECK" : "SKIP";
            row.Note = row.IsPurchasedStockBendWarning
                ? "CẢNH BÁO: Vật liệu định hình/mua sẵn " + materialName
                    + " không có dấu '-' cuối tên nhưng phát hiện bend line tại " + row.BendName
                : (scanIncomplete
                    ? "Chi tiết định hình/mua sẵn: chưa đọc đủ sketch để xác nhận không có bend line"
                    : "Chi tiết định hình/mua sẵn; không có bend line; bỏ qua kiểm tra Bend Table/K-Factor");
            Debug.WriteLine("[CHECK KEGAKI] purchasedStock=" + materialName
                + ", status=" + row.Status + ", bendLineFeatures=" + row.BendName);
            return new List<KegakiBendResult> { row };
        }

        private static string NormalizeMaterialIdentity(string value)
        {
            value = (value ?? "")
                .Normalize(System.Text.NormalizationForm.FormKC)
                .Trim();
            int separator = value.LastIndexOf("::", StringComparison.Ordinal);
            if (separator >= 0 && separator + 2 < value.Length)
                value = value.Substring(separator + 2);
            return value.Trim().ToUpperInvariant();
        }

        private static string ReadCustomProperty(
            CustomPropertyManager manager,
            string propertyName)
        {
            if (manager == null || string.IsNullOrWhiteSpace(propertyName))
                return "";

            try
            {
                string value;
                string resolved;
                bool wasResolved;
                bool linked;
                manager.Get6(propertyName, false, out value, out resolved, out wasResolved, out linked);
                return !string.IsNullOrWhiteSpace(resolved) ? resolved : (value ?? "");
            }
            catch
            {
                return "";
            }
        }

        // Material names confirmed by the user from the St材, SUS材, AL材 and Cu材 libraries.
        // Keep exact aliases here so unknown names are not guessed from the table.
        private static readonly Dictionary<string, string> MaterialGroupAliases =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "ST", "ST" },
                { "PEN", "ST" },
                { "BON", "ST" },
                { "STKU", "ST" },
                { "STMI", "ST" },
                { "ZAM", "ST" },
                { "NSD", "ST" },
                { "STSA", "ST" },
                { "SUSNO.4", "SUS" },
                { "SUSPHL", "SUS" },
                { "SUSM400", "SUS" },
                { "NSS445", "SUS" },
                { "SUS", "SUS" },
                { "SUS316HL", "SUS" },
                { "SUS3162B", "SUS" },
                { "SUS2D", "SUS" },
                { "SUSNO.1", "SUS" },
                { "SUSBA", "SUS" },
                { "SUSM800", "SUS" },
                { "SUSHL", "SUS" },
                { "SUS2B", "SUS" },
                { "SUSVB.BK.FG3", "SUS" },
                { "SUSM800.BK", "SUS" },
                { "SUSHL.BK", "SUS" },
                { "SUSVB.BK", "SUS" },
                { "AL", "AL" },
                { "A1100", "AL" },
                { "A3003", "AL" },
                { "A5052", "AL" },
                { "ALSKY", "AL" },
                { "アルマイト", "AL" },
                { "CU", "CU" },
                { "BS", "CU" },
                { "RBS", "CU" }
            };

        private static string ResolveMaterialGroup(string materialName, string materialCategory)
        {
            string group;
            string identity = NormalizeMaterialIdentity(materialName).TrimEnd('-').TrimEnd();
            if (MaterialGroupAliases.TryGetValue(identity, out group))
                return group;

            group = NormalizeMaterialGroup(materialCategory);
            return !string.IsNullOrWhiteSpace(group) ? group : NormalizeMaterialGroup(materialName);
        }

        private static string NormalizeMaterialGroup(string materialName)
        {
            string knownGroup;
            if (MaterialGroupAliases.TryGetValue(
                NormalizeMaterialIdentity(materialName).TrimEnd('-').TrimEnd(), out knownGroup))
                return knownGroup;

            string value = NormalizeLookupText(materialName);
            if (string.IsNullOrWhiteSpace(value))
                return "";

            if (value.Contains("SUS") || value.Contains("\u30B9\u30C6\u30F3\u30EC\u30B9"))
                return "SUS";

            if (value.Contains("\u30A2\u30EB\u30DF")
                || value.Contains("\u30A2\u30EB\u30DE\u30A4\u30C8")
                || value.StartsWith("AL", StringComparison.Ordinal)
                || value.StartsWith("A1", StringComparison.Ordinal)
                || value.StartsWith("A2", StringComparison.Ordinal)
                || value.StartsWith("A3", StringComparison.Ordinal)
                || value.StartsWith("A5", StringComparison.Ordinal)
                || value.StartsWith("A6", StringComparison.Ordinal)
                || value.StartsWith("A7", StringComparison.Ordinal))
                return "AL";

            if (value.Contains("COPPER")
                || value.Contains("\u9285")
                || value.Contains("\u9EC4\u9285")
                || value.StartsWith("CU", StringComparison.Ordinal)
                || value.StartsWith("C1", StringComparison.Ordinal)
                || value.StartsWith("C2", StringComparison.Ordinal)
                || value.StartsWith("C3", StringComparison.Ordinal)
                || value.StartsWith("C4", StringComparison.Ordinal)
                || value.StartsWith("C5", StringComparison.Ordinal)
                || value.StartsWith("C6", StringComparison.Ordinal)
                || value.StartsWith("BS", StringComparison.Ordinal)
                || value.StartsWith("RBS", StringComparison.Ordinal))
                return "CU";

            if (value.Contains("STEEL")
                || value.Contains("\u30B9\u30C1\u30FC\u30EB")
                || value.StartsWith("ST", StringComparison.Ordinal)
                || value.StartsWith("SPC", StringComparison.Ordinal)
                || value.StartsWith("SECC", StringComparison.Ordinal)
                || value.StartsWith("SGCC", StringComparison.Ordinal)
                || value.StartsWith("SS", StringComparison.Ordinal)
                || value.StartsWith("NSD", StringComparison.Ordinal)
                || value.StartsWith("ZAM", StringComparison.Ordinal))
                return "ST";

            if (value.Contains("TITANIUM")
                || value.Contains("\u30C1\u30BF\u30F3")
                || value.StartsWith("TI", StringComparison.Ordinal))
                return "TI";

            return "";
        }

        internal static bool IsAlpolicMaterial(string materialName)
        {
            return (materialName ?? "").IndexOf("アルポリック",
                StringComparison.OrdinalIgnoreCase) >= 0;
        }



        private static string NormalizeLookupText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            return value.Normalize(System.Text.NormalizationForm.FormKC)
                .Trim()
                .Replace('\uFF3F', '_')
                .Replace('\uFF0D', '-')
                .ToUpperInvariant();
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
                Feature nextChild = null;
                try { nextChild = child.GetNextSubFeature() as Feature; } catch { }

                AddFeatureAndChildren(child, result, seen, depth + 1);
                child = nextChild;
            }
        }

        private static string JoinDefaultSummaries(List<BendAllowanceInfo> defaults)
        {
            if (defaults == null || defaults.Count == 0)
                return "Khong doc duoc";

            List<string> values = new List<string>();
            foreach (BendAllowanceInfo item in defaults)
            {
                string summary = item == null ? "Khong doc duoc Sheet-Metal setting" : item.Summary();
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

        private static bool IsBendFeatureType(string typeName)
        {
            return string.Equals(typeName, "OneBend", StringComparison.OrdinalIgnoreCase)
                || string.Equals(typeName, "SketchBend", StringComparison.OrdinalIgnoreCase)
                || string.Equals(typeName, "ToroidalBend", StringComparison.OrdinalIgnoreCase)
                || string.Equals(typeName, "ProfileBend", StringComparison.OrdinalIgnoreCase)
                || string.Equals(typeName, "SM3dBend", StringComparison.OrdinalIgnoreCase)
                || string.Equals(typeName, "SMMiteredBend", StringComparison.OrdinalIgnoreCase)
                || string.Equals(typeName, "SweptFlange", StringComparison.OrdinalIgnoreCase)
                || string.Equals(typeName, "EdgeFlange", StringComparison.OrdinalIgnoreCase)
                || string.Equals(typeName, "MiterFlange", StringComparison.OrdinalIgnoreCase)
                || string.Equals(typeName, "Hem", StringComparison.OrdinalIgnoreCase)
                || string.Equals(typeName, "Jog", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsCurvedSheetMetalType(string typeName)
        {
            return string.Equals(typeName, "LoftedBend", StringComparison.OrdinalIgnoreCase)
                || string.Equals(typeName, "FreeformBend", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsFoldUnfoldFeatureType(string typeName)
        {
            return string.Equals(typeName, "RuledBend", StringComparison.OrdinalIgnoreCase)
                || string.Equals(typeName, "UiFreeformBend", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsFeatureSuppressed(Feature feature)
        {
            if (feature == null)
                return true;
            try { return feature.IsSuppressed(); }
            catch { return false; }
        }

        private static bool IsLargeRadius(double radiusMm)
        {
            return radiusMm > 0.2 + 0.001;
        }

        private static bool HasPairedCurvedMainFaces(
            ModelDoc2 model,
            List<double> sheetThicknesses,
            out double innerRadiusMm)
        {
            innerRadiusMm = 0.0;
            if (sheetThicknesses == null || sheetThicknesses.Count == 0)
                return false;

            try
            {
                PartDoc part = model as PartDoc;
                object bodiesObject = part == null
                    ? null
                    : part.GetBodies2((int)swBodyType_e.swSolidBody, true);
                object[] bodies = bodiesObject as object[];
                if (bodies == null && bodiesObject != null)
                    bodies = new[] { bodiesObject };
                if (bodies == null)
                    return false;

                List<CylinderFaceInfo> cylinders = new List<CylinderFaceInfo>();
                double maximumFaceArea = 0.0;
                foreach (object bodyObject in bodies)
                {
                    Body2 body = bodyObject as Body2;
                    if (body == null)
                        continue;
                    object facesObject = body.GetFaces();
                    object[] faces = facesObject as object[];
                    if (faces == null && facesObject != null)
                        faces = new[] { facesObject };
                    if (faces == null)
                        continue;

                    foreach (object faceObject in faces)
                    {
                        Face2 face = faceObject as Face2;
                        if (face == null)
                            continue;

                        double faceArea = 0.0;
                        try { faceArea = Math.Abs(face.GetArea()); } catch { }
                        if (faceArea > maximumFaceArea)
                            maximumFaceArea = faceArea;

                        CylinderFaceInfo cylinder;
                        if (TryReadCylinderFace(face, faceArea, out cylinder))
                            cylinders.Add(cylinder);
                    }
                }

                if (cylinders.Count < 2 || maximumFaceArea <= 0.0)
                {
                    Debug.WriteLine("[CHECK KEGAKI] curvedMainFacePair=False, cylinders="
                        + cylinders.Count);
                    return false;
                }

                double minimumMainArea = maximumFaceArea * 0.03;
                for (int i = 0; i < cylinders.Count - 1; i++)
                {
                    CylinderFaceInfo first = cylinders[i];
                    if (first.Area < minimumMainArea)
                        continue;

                    for (int j = i + 1; j < cylinders.Count; j++)
                    {
                        CylinderFaceInfo second = cylinders[j];
                        if (second.Area < minimumMainArea
                            || !AreParallel(first.Axis, second.Axis, 0.9995))
                            continue;

                        foreach (double thickness in sheetThicknesses)
                        {
                            double radiusTolerance = Math.Max(thickness * 0.05, 0.00001);
                            if (Math.Abs(Math.Abs(first.Radius - second.Radius) - thickness)
                                > radiusTolerance)
                                continue;

                            double axisTolerance = Math.Max(thickness * 0.20, 0.00005);
                            if (DistanceBetweenParallelAxes(first, second) > axisTolerance)
                                continue;

                            Debug.WriteLine("[CHECK KEGAKI] curvedMainFacePair=True"
                                + ", thicknessMm=" + (thickness * 1000.0).ToString("0.###")
                                + ", radius1Mm=" + (first.Radius * 1000.0).ToString("0.###")
                                + ", radius2Mm=" + (second.Radius * 1000.0).ToString("0.###")
                                + ", area1=" + first.Area.ToString("0.######")
                                + ", area2=" + second.Area.ToString("0.######"));
                            innerRadiusMm = Math.Max(innerRadiusMm,
                                Math.Min(first.Radius, second.Radius) * 1000.0);
                        }
                    }
                }

                if (innerRadiusMm > 0.0)
                    return true;

                Debug.WriteLine("[CHECK KEGAKI] curvedMainFacePair=False, cylinders="
                    + cylinders.Count + ", maxArea=" + maximumFaceArea.ToString("0.######"));
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[CHECK KEGAKI] Geometry scan ERROR: " + ex.Message);
                return false;
            }
        }

        private static bool TryReadCylinderFace(
            Face2 face,
            double area,
            out CylinderFaceInfo info)
        {
            info = null;
            try
            {
                Surface surface = face.GetSurface() as Surface;
                if (surface == null || !surface.IsCylinder())
                    return false;

                object parametersObject = surface.CylinderParams;
                double[] parameters = parametersObject as double[];
                if (parameters == null && parametersObject is Array)
                {
                    Array values = (Array)parametersObject;
                    parameters = new double[values.Length];
                    for (int i = 0; i < values.Length; i++)
                        parameters[i] = Convert.ToDouble(values.GetValue(i));
                }

                if (parameters == null || parameters.Length < 7)
                    return false;

                double axisLength = Math.Sqrt(
                    parameters[3] * parameters[3]
                    + parameters[4] * parameters[4]
                    + parameters[5] * parameters[5]);
                if (axisLength <= 0.000000001)
                    return false;

                info = new CylinderFaceInfo
                {
                    Origin = new[] { parameters[0], parameters[1], parameters[2] },
                    Axis = new[]
                    {
                        parameters[3] / axisLength,
                        parameters[4] / axisLength,
                        parameters[5] / axisLength
                    },
                    Radius = Math.Abs(parameters[6]),
                    Area = area
                };
                return info.Radius > 0.0;
            }
            catch
            {
                return false;
            }
        }

        private static bool AreParallel(double[] first, double[] second, double minimumDot)
        {
            if (first == null || second == null || first.Length < 3 || second.Length < 3)
                return false;
            double dot = Math.Abs(
                first[0] * second[0]
                + first[1] * second[1]
                + first[2] * second[2]);
            return dot >= minimumDot;
        }

        private static double DistanceBetweenParallelAxes(
            CylinderFaceInfo first,
            CylinderFaceInfo second)
        {
            double dx = second.Origin[0] - first.Origin[0];
            double dy = second.Origin[1] - first.Origin[1];
            double dz = second.Origin[2] - first.Origin[2];
            double projection = dx * first.Axis[0]
                + dy * first.Axis[1]
                + dz * first.Axis[2];
            double px = dx - projection * first.Axis[0];
            double py = dy - projection * first.Axis[1];
            double pz = dz - projection * first.Axis[2];
            return Math.Sqrt(px * px + py * py + pz * pz);
        }

        private static bool ContainsNear(
            List<double> values,
            double expected,
            double tolerance)
        {
            if (values == null)
                return false;
            foreach (double value in values)
            {
                if (Math.Abs(value - expected) <= tolerance)
                    return true;
            }
            return false;
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
        public bool HasKFactor { get; private set; }
        public bool HasBendAllowance { get; private set; }
        public bool HasBendDeduction { get; private set; }
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
            try { info.KFactor = Convert.ToDouble(allowance.KFactor); info.HasKFactor = BendValueMath.IsFinite(info.KFactor); } catch { }
            try { info.BendAllowance = Convert.ToDouble(allowance.BendAllowance); info.HasBendAllowance = BendValueMath.IsFinite(info.BendAllowance); } catch { }
            try { info.BendDeduction = Convert.ToDouble(allowance.BendDeduction); info.HasBendDeduction = BendValueMath.IsFinite(info.BendDeduction); } catch { }
            try { info.BendTableFile = Convert.ToString(allowance.BendTableFile ?? ""); } catch { info.BendTableFile = ""; }
            // BendCalculationTableFile does not exist in older SOLIDWORKS
            // interop assemblies. Do not access it dynamically here because
            // that produces a RuntimeBinderException for every checked part.
            info.BendCalculationTableFile = "";

            return info;
        }

        public bool TryGetDeduction(double thicknessMm, double angleDeg, double radiusMm,
            out double deductionMm, out string detail)
        {
            BendTableValueKind kind = BendTableValueKind.Unknown;
            double value = 0;
            if (Type == (int)swBendAllowanceTypes_e.swBendAllowanceKFactor && HasKFactor)
            { kind = BendTableValueKind.KFactor; value = KFactor; }
            else if (Type == (int)swBendAllowanceTypes_e.swBendAllowanceDirect && HasBendAllowance)
            { kind = BendTableValueKind.Allowance; value = BendAllowance * 1000.0; }
            else if (Type == (int)swBendAllowanceTypes_e.swBendAllowanceDeduction && HasBendDeduction)
            { kind = BendTableValueKind.Deduction; value = BendDeduction * 1000.0; }
            return BendValueMath.TryToDeduction(kind, value, thicknessMm, angleDeg, radiusMm,
                out deductionMm, out detail);
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

        public bool IsKFactorSetting()
        {
            string type = (TypeName ?? "").ToLowerInvariant();
            return type.Contains("kfactor") || type.Contains("k_factor");
        }

        public string GetBendTableFileName()
        {
            return ShortPath(BendTableFile);
        }

        public string GetBendTableFilePath()
        {
            return BendTableFile ?? "";
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

    internal sealed class MaterialTableCheck
    {
        public string MaterialName { get; set; }
        public string MaterialDatabase { get; set; }
        public string MaterialCategory { get; set; }
        public string MaterialGroup { get; set; }
        public string BendTableName { get; set; }
        public string BendTableGroup { get; set; }
        public double SheetThicknessMm { get; set; }
        public string BendTablePath { get; set; }
        public string StandardBendTablePath { get; set; }
        public string StandardBendTableName { get; set; }
        public double? ActualCoefficientMm { get; set; }
        public double? StandardCoefficientMm { get; set; }
        public string DeepStatus { get; set; }
        public string DeepNote { get; set; }
        public BendTableBlockData ActualBlock { get; set; }
        public BendTableBlockData StandardBlock { get; set; }
        public string Status { get; set; }
        public string Note { get; set; }
    }

    internal sealed class CurvedAllowanceCheck
    {
        public string Status { get; set; }
        public string Note { get; set; }
    }

    internal sealed class CylinderFaceInfo
    {
        public double[] Origin { get; set; }
        public double[] Axis { get; set; }
        public double Radius { get; set; }
        public double Area { get; set; }
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
        internal bool HasBendGeometry { get; set; }
        internal BendAllowanceInfo EffectiveAllowance { get; set; }
        public string IntendedGroovePropertyName { get; set; }
        public double? IntendedGrooveCoefficientMm { get; set; }
        public string IntendedGrooveError { get; set; }
        public string EffectiveBendSetting { get; set; }
        public string BendValueSource { get; set; }
        public string BendValueStandardTable { get; set; }
        public double? ActualBendDeductionMm { get; set; }
        public double? MeasuredBendAllowanceMm { get; set; }
        public double? MeasuredFlatLengthMm { get; set; }
        public double? MeasuredOutsideLengthMm { get; set; }
        public double? StandardBendDeductionMm { get; set; }
        public double? BendDeductionDifferenceMm { get; set; }
        public double? ActualEdgeCoefficientMm { get; set; }
        public double? StandardEdgeCoefficientMm { get; set; }
        public string BendValueStatus { get; set; }
        public string BendValueNote { get; set; }
        public bool BendDown { get; set; }
        public bool IsOverride { get; set; }
        public bool IsLargeRadiusBend { get; set; }
        public bool IsPurchasedStockBendWarning { get; set; }
        public string DefaultSetting { get; set; }
        public string BendSetting { get; set; }
        public string OverrideBendTablePath { get; set; }
        public string MaterialName { get; set; }
        public string MaterialGroup { get; set; }
        public string BendTableGroup { get; set; }
        public double SheetThicknessMm { get; set; }
        public string BendTableName { get; set; }
        public string StandardBendTableName { get; set; }
        public double? BendCoefficientMm { get; set; }
        public double? StandardBendCoefficientMm { get; set; }
        public string BendTableDeepStatus { get; set; }
        public string BendTableDeepNote { get; set; }
        public string Note { get; set; }
        public string PartPath { get; set; }
    }

    public static class ExcelKegakiExporter
    {
        public static void Export(List<KegakiBendResult> results)
        {
            try
            {
                List<List<KegakiBendResult>> groupedResults = GroupResults(results);
                if (groupedResults.Count == 0)
                {
                    MessageBox.Show(
                        "Khong co du lieu CHECK KEGAKI.",
                        "CHECK KEGAKI",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                int ngGroupCount = 0;
                int checkGroupCount = 0;
                int okGroupCount = 0;
                foreach (List<KegakiBendResult> group in groupedResults)
                {
                    string status = GetGroupStatus(group);
                    if (status == "NG")
                        ngGroupCount++;
                    else if (status == "CHECK")
                        checkGroupCount++;
                    else if (status == "OK")
                        okGroupCount++;
                }

                Type excelType = Type.GetTypeFromProgID("Excel.Application");
                if (excelType == null)
                {
                    MessageBox.Show("Khong tim thay Microsoft Excel.", "CHECK KEGAKI", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                dynamic xlApp = Activator.CreateInstance(excelType);
                dynamic xlWB = xlApp.Workbooks.Add();
                using (var exportScope = new ExcelCheckExportScope(xlApp, "KEGAKI"))
                {
                    dynamic xlWS = xlWB.Sheets[1];
                    xlWS.Name = "CHECK KEGAKI";

                    WriteHeader(xlWS);
                    WriteRows(xlWS, groupedResults);
                    int lastRow = groupedResults.Count + 1;
                    if (lastRow > 1)
                    {
                        TrySort(xlWS, lastRow);
                        xlWS.Range["B2:N" + lastRow].WrapText = true;
                        xlWS.Range["A2:N" + lastRow].VerticalAlignment = -4160;
                        SetReadableColumnWidths(xlWS);
                        AutoFitNoteColumn(xlWS, lastRow, 14);
                    }

                    dynamic detailSheet = xlWB.Sheets.Add(After: xlWS);
                    detailSheet.Name = "HE SO TUNG BEND";
                    WritePerBendRows(detailSheet, results);
                    xlWS.Activate();
                }
                xlApp.Visible = true;
                MessageBox.Show(
                    "\u0110\u00E3 xu\u1EA5t " + groupedResults.Count
                    + " chi ti\u1EBFt.\r\nOK: " + okGroupCount
                    + "\r\nNG: " + ngGroupCount
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

        private static void WritePerBendRows(dynamic sheet, List<KegakiBendResult> results)
        {
            string[] headers = {
                "部品番号", "Vat lieu", "Bend", "板厚 (mm)", "Goc (deg)", "R (mm)",
                "Setting ap dung", "He so canh quy doi (mm)", "He so bang quy doi (mm)",
                "BD canh (mm)", "BendTable chuan", "BD bang (mm)", "Lech BD (mm)",
                "So he so", "Status tong", "Nguon gia tri", "Note",
                "Tong canh ngoai tai mat cat (mm)", "Chieu dai trai tai mat cat (mm)", "Vung bend trai / BA (mm)"
            };
            ExcelCheckReportWriter.WriteHeaders(sheet, headers);
            sheet.Cells[1, 8].AddComment("Tinh nguoc tu hinh hoc Flat-Pattern: BD = tong canh ngoai - chieu dai trai; he so = BD / (2 * tan(goc bend / 2)). Tai 90 deg: he so = BD / 2.");
            sheet.Cells[1, 9].AddComment("Quy doi tu o gia tri BendTable tai do day, goc va R cua bend. Co noi suy giua cac moc trong bang.");
            sheet.Cells[1, 14].AddComment("So sanh BD tinh nguoc tu hinh hoc model voi BD bang chuan hien tai; dung sai 0.001 mm. Ten bang giong nhau van co the NG. R > 0.2 mm dung quy tac K=0.5 rieng.");
            var reportRows = new List<KegakiBendResult>();
            foreach (KegakiBendResult item in results)
                if (!string.IsNullOrWhiteSpace(item.BendValueStatus)) reportRows.Add(item);
            var data = new object[Math.Max(1, reportRows.Count), 20];
            var fills = new ExcelCheckFillBatch(1, 20);
            int r = 2;
            foreach (KegakiBendResult row in reportRows)
            {
                if (string.IsNullOrWhiteSpace(row.BendValueStatus)) continue;
                data[r - 2, 0] = row.BuhinNo;
                data[r - 2, 1] = row.MaterialName;
                data[r - 2, 2] = row.BendName;
                data[r - 2, 3] = row.SheetThicknessMm > 0 ? (object)row.SheetThicknessMm : "";
                data[r - 2, 4] = row.HasBendGeometry ? (object)row.AngleDeg : "";
                data[r - 2, 5] = row.HasBendGeometry ? (object)row.RadiusMm : "";
                data[r - 2, 6] = row.EffectiveBendSetting;
                data[r - 2, 7] = row.ActualEdgeCoefficientMm.HasValue ? (object)row.ActualEdgeCoefficientMm.Value : "";
                data[r - 2, 8] = row.StandardEdgeCoefficientMm.HasValue ? (object)row.StandardEdgeCoefficientMm.Value : "";
                data[r - 2, 9] = row.ActualBendDeductionMm.HasValue ? (object)row.ActualBendDeductionMm.Value : "";
                data[r - 2, 10] = row.BendValueStandardTable;
                data[r - 2, 11] = row.StandardBendDeductionMm.HasValue ? (object)row.StandardBendDeductionMm.Value : "";
                data[r - 2, 12] = row.BendDeductionDifferenceMm.HasValue ? (object)row.BendDeductionDifferenceMm.Value : "";
                data[r - 2, 13] = row.BendValueStatus;
                data[r - 2, 14] = row.Status;
                data[r - 2, 15] = row.BendValueSource;
                data[r - 2, 16] = row.BendValueNote;
                data[r - 2, 17] = row.MeasuredOutsideLengthMm.HasValue ? (object)row.MeasuredOutsideLengthMm.Value : "";
                data[r - 2, 18] = row.MeasuredFlatLengthMm.HasValue ? (object)row.MeasuredFlatLengthMm.Value : "";
                data[r - 2, 19] = row.MeasuredBendAllowanceMm.HasValue ? (object)row.MeasuredBendAllowanceMm.Value : "";
                if (row.BendValueStatus == "NG") fills.Add(r, Rgb(255, 199, 206));
                else if (row.BendValueStatus == "CHECK") fills.Add(r, Rgb(255, 235, 156));
                r++;
            }
            if (r == 2) data[0, 16] = "Khong co bend can doi chieu he so (tam phang/chi tiet dinh hinh).";
            ExcelCheckReportWriter.WriteBlock(sheet, 2, 1, data);
            fills.Apply(sheet);
            int lastRow = Math.Max(2, r - 1);
            sheet.Range["A1:T" + lastRow].Font.Name = "Calibri";
            sheet.Range["A1:T" + lastRow].Font.Size = 11;
            sheet.Range["A1:T1"].Font.Bold = true;
            sheet.Range["A1:T1"].Interior.Color = Rgb(31, 78, 120);
            sheet.Range["A1:T1"].Font.Color = Rgb(255, 255, 255);
            sheet.Range["A1:T" + lastRow].WrapText = true;
            sheet.Range["A1:T" + lastRow].VerticalAlignment = -4160;
            sheet.Range["D2:M" + lastRow].NumberFormat = "0.0000";
            sheet.Range["R2:T" + lastRow].NumberFormat = "0.0000";
            double[] widths = { 12, 20, 22, 12, 12, 10, 28, 18, 18, 16, 28, 16, 16, 12, 12, 48, 64, 22, 22, 22 };
            for (int c = 0; c < widths.Length; c++) sheet.Columns[c + 1].ColumnWidth = widths[c];
            sheet.Range["A1:T" + lastRow].Rows.AutoFit();
            sheet.Range["A1:T" + lastRow].AutoFilter();
            ExcelCheckReportWriter.FreezeTopRow(sheet);
        }

        private static void WriteHeader(dynamic sheet)
        {
            string[] headers =
            {
                "\u90E8\u54C1\u756A\u53F7",
                "Vat lieu",
                "板厚 (mm)",
                "BendTable thuc te",
                "He so thuc te (mm)",
                "BendTable chuan",
                "He so chuan (mm)",
                "Chenh lech (mm)",
                "Setting chung",
                "Bend d\u00F9ng setting chung",
                "Bend setting ri\u00EAng",
                "T\u00EAn setting ri\u00EAng",
                "Status",
                "Note"
            };

            ExcelCheckReportWriter.WriteHeaders(sheet, headers);
        }

        private static void WriteRows(dynamic sheet, List<List<KegakiBendResult>> groups)
        {
            var data = new object[groups.Count, 14];
            var fills = new ExcelCheckFillBatch(1, 16);
            int excelRow = 2;
            foreach (List<KegakiBendResult> group in groups)
            {
                KegakiBendResult first = group[0];
                string groupStatus = GetGroupStatus(group);

                data[excelRow - 2, 0] = first.BuhinNo;
                data[excelRow - 2, 1] = JoinMaterial(group);
                data[excelRow - 2, 2] =
                    first.SheetThicknessMm > 0.0 ? (object)first.SheetThicknessMm : "";
                data[excelRow - 2, 3] = JoinDistinct(
                    group,
                    delegate(KegakiBendResult row) { return row.BendTableName; });
                data[excelRow - 2, 4] =
                    first.BendCoefficientMm.HasValue ? (object)first.BendCoefficientMm.Value : "";
                data[excelRow - 2, 5] = JoinDistinct(
                    group,
                    delegate(KegakiBendResult row) { return row.StandardBendTableName; });
                data[excelRow - 2, 6] =
                    first.StandardBendCoefficientMm.HasValue
                        ? (object)first.StandardBendCoefficientMm.Value
                        : "";
                data[excelRow - 2, 7] =
                    first.BendCoefficientMm.HasValue
                    && first.StandardBendCoefficientMm.HasValue
                        ? (object)Math.Abs(
                            first.BendCoefficientMm.Value
                            - first.StandardBendCoefficientMm.Value)
                        : "";
                data[excelRow - 2, 8] = CleanSettingName(
                    JoinDistinct(group, delegate(KegakiBendResult row) { return row.DefaultSetting; }));
                data[excelRow - 2, 9] = JoinBendNames(group, false);
                data[excelRow - 2, 10] = JoinBendNames(group, true);
                data[excelRow - 2, 11] = JoinOverrideSettings(group);
                data[excelRow - 2, 12] = groupStatus;
                data[excelRow - 2, 13] = BuildClearNote(group);

                if (groupStatus == "NG")
                    fills.Add(excelRow, Rgb(255, 199, 206));
                else if (groupStatus == "CHECK")
                    fills.Add(excelRow, Rgb(255, 235, 156));

                excelRow++;
            }
            ExcelCheckReportWriter.WriteBlock(sheet, 2, 1, data);
            fills.Apply(sheet);
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
                if (row.Status != "NG" && row.Status != "CHECK")
                    continue;

                string note = row.Note ?? "";
                if (!string.IsNullOrWhiteSpace(row.BendName))
                    note = row.BendName + ": " + note;
                AddDistinct(notes, note);
            }

            if (notes.Count == 0)
                return "Tat ca bend dat yeu cau; xem sheet HE SO TUNG BEND de doi chieu.";

            return string.Join("\n", notes.ToArray());
        }

        private static string JoinMaterial(List<KegakiBendResult> group)
        {
            List<string> values = new List<string>();
            foreach (KegakiBendResult row in group)
            {
                string value = (row.MaterialName ?? "").Trim();
                string materialGroup = (row.MaterialGroup ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(materialGroup))
                    value = string.IsNullOrWhiteSpace(value)
                        ? materialGroup
                        : value + " -> " + materialGroup;
                AddDistinct(values, value);
            }
            return string.Join("\n", values.ToArray());
        }

        private static void AddDistinct(List<string> values, string value)
        {
            value = (value ?? "").Trim();
            if (string.IsNullOrWhiteSpace(value))
                return;

            foreach (string current in values)
            {
                if (string.Equals(current, value, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            values.Add(value);
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
                sort.SortFields.Add(sheet.Range["A2:A" + lastRow], 0, 1);
                sort.SetRange(sheet.Range["A1:N" + lastRow]);
                sort.Header = 1;
                sort.Apply();
            }
            catch
            {
            }
        }

        private static void SetReadableColumnWidths(dynamic sheet)
        {
            sheet.Columns[1].ColumnWidth = 11;
            sheet.Columns[2].ColumnWidth = 22;
            sheet.Columns[3].ColumnWidth = 11;
            sheet.Columns[4].ColumnWidth = 24;
            sheet.Columns[5].ColumnWidth = 14;
            sheet.Columns[6].ColumnWidth = 24;
            sheet.Columns[7].ColumnWidth = 14;
            sheet.Columns[8].ColumnWidth = 14;
            sheet.Columns[9].ColumnWidth = 28;
            sheet.Columns[10].ColumnWidth = 24;
            sheet.Columns[11].ColumnWidth = 24;
            sheet.Columns[12].ColumnWidth = 28;
            sheet.Columns[13].ColumnWidth = 9;
            sheet.Columns[14].ColumnWidth = 58;
        }

        private static void AutoFitNoteColumn(dynamic sheet, int lastRow, int noteColumn)
        {
            try
            {
                dynamic noteRange = sheet.Range[
                    sheet.Cells[1, noteColumn],
                    sheet.Cells[Math.Max(1, lastRow), noteColumn]];
                dynamic excelColumn = sheet.Columns[noteColumn];
                noteRange.WrapText = false;
                excelColumn.AutoFit();
                double width = Convert.ToDouble(excelColumn.ColumnWidth);
                excelColumn.ColumnWidth = Math.Max(18.0, Math.Min(80.0, width));
                noteRange.WrapText = true;
                noteRange.Rows.AutoFit();
            }
            catch
            {
            }
        }

        private static int Rgb(int red, int green, int blue)
        {
            return red + (green << 8) + (blue << 16);
        }
    }
}
