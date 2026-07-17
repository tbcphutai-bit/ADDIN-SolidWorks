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
            List<Feature> curvedFeatures = new List<Feature>();
            List<double> sheetThicknesses = new List<double>();

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
                }
                else if (IsOneBendType(typeName))
                {
                    bends.Add(feature);
                }
                else if (IsCurvedSheetMetalType(typeName) && !IsFeatureSuppressed(feature))
                {
                    curvedFeatures.Add(feature);
                }
            }

            bool hasCurvedGeometry = false;
            if (defaults.Count > 0 && bends.Count == 0 && curvedFeatures.Count == 0)
                hasCurvedGeometry = HasPairedCurvedMainFaces(model, sheetThicknesses);

            if (defaults.Count == 0
                && bends.Count == 0
                && curvedFeatures.Count == 0
                && !hasCurvedGeometry)
                return SingleStatus("SKIP", buhinNo, bomFileName, componentName, partPath, "Khong phai sheet metal");

            string defaultSummary = JoinDefaultSummaries(defaults);
            MaterialTableCheck materialCheck = CheckMaterialAgainstDefaultBendTables(model, defaults);
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
                if (hasCurvedGeometry)
                {
                    CurvedAllowanceCheck curvedCheck = EvaluateCurvedAllowance(defaults);
                    summaryRow.BendName = "Curved geometry confirmed";
                    summaryRow.Status = curvedCheck.Status;
                    summaryRow.Note = "Xac nhan cap mat cong theo be day; " + curvedCheck.Note;
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
                    buhinNo,
                    bomFileName,
                    componentName,
                    partPath);
                ApplyMaterialCheck(bendResult, materialCheck);
                ApplyOverrideMaterialCheck(bendResult, materialCheck);
                results.Add(bendResult);
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
                if (allowance == null)
                    continue;
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
            ModelDoc2 model,
            List<BendAllowanceInfo> defaults)
        {
            MaterialTableCheck result = new MaterialTableCheck();
            string materialDatabase;
            string materialCategory;
            result.MaterialName = ReadComponentMaterialName(
                model,
                out materialDatabase,
                out materialCategory);
            result.MaterialDatabase = materialDatabase;
            result.MaterialCategory = materialCategory;
            result.MaterialGroup = NormalizeMaterialGroup(materialCategory);
            if (string.IsNullOrWhiteSpace(result.MaterialGroup))
                result.MaterialGroup = NormalizeMaterialGroup(result.MaterialName);

            List<string> tableNames = new List<string>();
            List<string> tableGroups = new List<string>();
            if (defaults != null)
            {
                foreach (BendAllowanceInfo allowance in defaults)
                {
                    if (allowance == null || !allowance.HasBendTableFile())
                        continue;

                    string tableName = allowance.GetBendTableFileName();
                    string tableGroup = NormalizeBendTableGroup(tableName);
                    if (!string.IsNullOrWhiteSpace(tableName) && !tableNames.Contains(tableName))
                        tableNames.Add(tableName);
                    if (!string.IsNullOrWhiteSpace(tableGroup) && !tableGroups.Contains(tableGroup))
                        tableGroups.Add(tableGroup);
                }
            }

            result.BendTableName = string.Join(" | ", tableNames.ToArray());
            result.BendTableGroup = string.Join(" | ", tableGroups.ToArray());

            if (string.IsNullOrWhiteSpace(result.MaterialName))
            {
                result.Status = "CHECK";
                result.Note = "Khong doc duoc vat lieu cua component";
            }
            else if (string.IsNullOrWhiteSpace(result.MaterialGroup))
            {
                result.Status = "CHECK";
                result.Note = "Chua nhan dang nhom vat lieu: " + result.MaterialName;
            }
            else if (tableNames.Count == 0)
            {
                result.Status = "CHECK";
                result.Note = "Vat lieu " + result.MaterialName + " -> " + result.MaterialGroup
                    + "; khong doc duoc Bend Table mac dinh";
            }
            else if (tableGroups.Count == 0)
            {
                result.Status = "CHECK";
                result.Note = "Khong nhan dang duoc nhom tu Bend Table: " + result.BendTableName;
            }
            else if (tableGroups.Count != 1
                || !string.Equals(result.MaterialGroup, tableGroups[0], StringComparison.OrdinalIgnoreCase))
            {
                result.Status = "NG";
                result.Note = "Vat lieu " + result.MaterialName + " -> " + result.MaterialGroup
                    + "; Bend Table " + result.BendTableName + " -> "
                    + (string.IsNullOrWhiteSpace(result.BendTableGroup) ? "khong ro" : result.BendTableGroup)
                    + ": KHONG KHOP";
            }
            else
            {
                result.Status = "OK";
                result.Note = "Vat lieu " + result.MaterialName + " -> " + result.MaterialGroup
                    + "; Bend Table " + result.BendTableName + " -> " + result.BendTableGroup
                    + ": khop";
            }

            Debug.WriteLine("[CHECK KEGAKI] material=" + result.MaterialName
                + ", database=" + result.MaterialDatabase
                + ", category=" + result.MaterialCategory
                + ", materialGroup=" + result.MaterialGroup
                + ", bendTable=" + result.BendTableName
                + ", tableGroup=" + result.BendTableGroup
                + ", status=" + result.Status);
            return result;
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

        private static void ApplyMaterialCheck(
            KegakiBendResult row,
            MaterialTableCheck check)
        {
            if (row == null || check == null)
                return;

            row.MaterialName = check.MaterialName ?? "";
            row.MaterialGroup = check.MaterialGroup ?? "";
            row.BendTableGroup = check.BendTableGroup ?? "";
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
        }

        private static void ApplyOverrideMaterialCheck(
            KegakiBendResult row,
            MaterialTableCheck defaultCheck)
        {
            if (row == null || defaultCheck == null || !row.IsOverride)
                return;

            string overrideGroup = NormalizeBendTableGroup(row.BendSetting);
            if (string.IsNullOrWhiteSpace(overrideGroup))
                return;

            if (string.IsNullOrWhiteSpace(defaultCheck.MaterialGroup))
            {
                row.Status = MergeStatus(row.Status, "CHECK");
                row.Note = AppendNote(row.Note,
                    "Khong co nhom vat lieu de so sanh Bend Table rieng " + row.BendSetting);
                return;
            }

            if (!string.Equals(
                defaultCheck.MaterialGroup,
                overrideGroup,
                StringComparison.OrdinalIgnoreCase))
            {
                row.Status = "NG";
                row.Note = AppendNote(row.Note,
                    "Bend Table rieng " + row.BendSetting + " -> " + overrideGroup
                    + ", khong khop vat lieu " + defaultCheck.MaterialGroup);
            }
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
                string modelPath = model == null ? "" : model.GetPathName();
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

        private static string NormalizeMaterialGroup(string materialName)
        {
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

        private static string NormalizeBendTableGroup(string bendTableName)
        {
            string value = NormalizeLookupText(bendTableName);
            if (string.IsNullOrWhiteSpace(value))
                return "";

            if (value.Contains("SUS"))
                return "SUS";
            if (value.Contains("_AL_") || value.Contains("-AL-") || value.Contains(" AL "))
                return "AL";
            if (value.Contains("_ST_") || value.Contains("-ST-") || value.Contains(" ST "))
                return "ST";
            if (value.Contains("_CU_") || value.Contains("-CU-")
                || value.Contains("_BS_") || value.Contains("_RBS_"))
                return "CU";
            if (value.Contains("_TI_") || value.Contains("-TI-"))
                return "TI";
            return "";
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

        private static bool IsCurvedSheetMetalType(string typeName)
        {
            return string.Equals(typeName, "LoftedBend", StringComparison.OrdinalIgnoreCase)
                || string.Equals(typeName, "RuledBend", StringComparison.OrdinalIgnoreCase)
                || string.Equals(typeName, "UiFreeformBend", StringComparison.OrdinalIgnoreCase)
                || string.Equals(typeName, "FreeformBend", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsFeatureSuppressed(Feature feature)
        {
            if (feature == null)
                return true;
            try { return feature.IsSuppressed(); }
            catch { return false; }
        }

        private static bool HasPairedCurvedMainFaces(
            ModelDoc2 model,
            List<double> sheetThicknesses)
        {
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
                            return true;
                        }
                    }
                }

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

        public bool IsKFactorSetting()
        {
            string type = (TypeName ?? "").ToLowerInvariant();
            return type.Contains("kfactor") || type.Contains("k_factor");
        }

        public string GetBendTableFileName()
        {
            return ShortPath(BendTableFile);
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
        public bool BendDown { get; set; }
        public bool IsOverride { get; set; }
        public string DefaultSetting { get; set; }
        public string BendSetting { get; set; }
        public string MaterialName { get; set; }
        public string MaterialGroup { get; set; }
        public string BendTableGroup { get; set; }
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
                    xlWS.Range["B2:H" + lastRow].WrapText = true;
                    xlWS.Range["A2:H" + lastRow].VerticalAlignment = -4160;
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
                "\u90E8\u54C1\u756A\u53F7",
                "Vat lieu",
                "Setting chung",
                "Bend d\u00F9ng setting chung",
                "Bend setting ri\u00EAng",
                "T\u00EAn setting ri\u00EAng",
                "Status",
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

                sheet.Cells[excelRow, 1].Value = first.BuhinNo;
                sheet.Cells[excelRow, 2].Value = JoinMaterial(group);
                sheet.Cells[excelRow, 3].Value = CleanSettingName(
                    JoinDistinct(group, delegate(KegakiBendResult row) { return row.DefaultSetting; }));
                sheet.Cells[excelRow, 4].Value = JoinBendNames(group, false);
                sheet.Cells[excelRow, 5].Value = JoinBendNames(group, true);
                sheet.Cells[excelRow, 6].Value = JoinOverrideSettings(group);
                sheet.Cells[excelRow, 7].Value = groupStatus;
                sheet.Cells[excelRow, 8].Value = BuildClearNote(group);

                if (groupStatus == "NG")
                    sheet.Range["A" + excelRow + ":H" + excelRow].Interior.Color = Rgb(255, 199, 206);
                else if (groupStatus == "CHECK")
                    sheet.Range["A" + excelRow + ":H" + excelRow].Interior.Color = Rgb(255, 235, 156);
                else if (groupStatus == "SKIP")
                    sheet.Range["A" + excelRow + ":H" + excelRow].Interior.Color = Rgb(217, 217, 217);

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
                if (row.Status != "NG" && row.Status != "CHECK")
                    continue;

                string note = row.Note ?? "";
                if (row.IsOverride && !string.IsNullOrWhiteSpace(row.BendName))
                    note = row.BendName + ": " + note;
                AddDistinct(notes, note);
            }

            if (notes.Count == 0)
                return "T\u1EA5t c\u1EA3 Bend d\u00F9ng setting chung";

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
                sort.SetRange(sheet.Range["A1:H" + lastRow]);
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
            sheet.Columns[3].ColumnWidth = 30;
            sheet.Columns[4].ColumnWidth = 25;
            sheet.Columns[5].ColumnWidth = 25;
            sheet.Columns[6].ColumnWidth = 32;
            sheet.Columns[7].ColumnWidth = 9;
            sheet.Columns[8].ColumnWidth = 58;
        }

        private static int Rgb(int red, int green, int blue)
        {
            return red + (green << 8) + (blue << 16);
        }
    }
}
