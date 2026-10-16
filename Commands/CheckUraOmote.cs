using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands
{
    public class CheckUraOmoteRunner
    {
        private readonly ISldWorks swApp;
        private readonly DataGridView gridBom;

        public CheckUraOmoteRunner(ISldWorks app, DataGridView grid)
        {
            swApp = app;
            gridBom = grid;
        }

        public UraOmoteCheckResult Run(
            Action<int> progressStarted,
            Action<int, int> progressChanged,
            Func<bool> isCancellationRequested)
        {
            UraOmoteCheckResult result = new UraOmoteCheckResult();
            UraOmoteChecker checker = new UraOmoteChecker(swApp);
            HashSet<string> checkedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            Debug.WriteLine("[URA OMOTE] ===== RUN START =====");
            Debug.WriteLine("[URA OMOTE] Grid rows=" + gridBom.Rows.Count);

            bool oldCommandInProgress = swApp.CommandInProgress;
            ModelView activeView = null;
            bool oldGraphicsUpdate = true;

            try
            {
                ModelDoc2 activeModel = swApp.ActiveDoc as ModelDoc2;
                Debug.WriteLine("[URA OMOTE] Active document=" + SafeModelTitle(activeModel)
                    + ", type=" + SafeModelType(activeModel));
                activeView = activeModel?.ActiveView as ModelView;
                if (activeView != null)
                {
                    oldGraphicsUpdate = activeView.EnableGraphicsUpdate;
                    activeView.EnableGraphicsUpdate = false;
                }

                swApp.CommandInProgress = true;

                result.CheckedCount = CountCheckedRows();
                progressStarted?.Invoke(result.CheckedCount);

                int processed = 0;
                foreach (DataGridViewRow row in gridBom.Rows)
                {
                    if (IsCanceled(isCancellationRequested, result))
                        break;

                    if (row.IsNewRow || !Convert.ToBoolean(row.Cells[0].Value ?? false))
                        continue;

                    Debug.WriteLine("[URA OMOTE] Row start. index=" + row.Index
                        + ", buhinNo=" + GetBomBuhinNo(row)
                        + ", file=" + GetBomFileName(row)
                        + ", tag=" + (row.Tag == null ? "null" : row.Tag.GetType().FullName));

                    List<UraOmoteRowResult> rowResults = CheckRow(row, checker, checkedKeys);
                    processed++;
                    result.ProcessedCount = processed;
                    progressChanged?.Invoke(processed, result.CheckedCount);

                    if (rowResults.Count == 0)
                    {
                        result.SkippedCount++;
                        Debug.WriteLine("[URA OMOTE] Row end. index=" + row.Index + ", no result");
                        continue;
                    }

                    foreach (UraOmoteRowResult rowResult in rowResults)
                    {
                        result.Results.Add(rowResult);
                        if (rowResult.Status == "NG" || rowResult.Status == "CHECK")
                            result.HighlightRowIndexes.Add(row.Index);

                        Debug.WriteLine("[URA OMOTE] Row result. index=" + row.Index
                            + ", status=" + rowResult.Status
                            + ", defaultPink=" + rowResult.DefaultPinkFaceCount
                            + ", flatPink=" + rowResult.FlatPinkFaceCount
                            + ", note=" + rowResult.Note
                            + ", path=" + rowResult.PartPath);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[URA OMOTE] RUN ERROR: " + ex);
                throw;
            }
            finally
            {
                swApp.CommandInProgress = oldCommandInProgress;
                if (activeView != null)
                    activeView.EnableGraphicsUpdate = oldGraphicsUpdate;

                Debug.WriteLine("[URA OMOTE] ===== RUN END ===== processed=" + result.ProcessedCount
                    + ", results=" + result.Results.Count
                    + ", skipped=" + result.SkippedCount
                    + ", canceled=" + result.Canceled);
            }

            return result;
        }

        private List<UraOmoteRowResult> CheckRow(
            DataGridViewRow row,
            UraOmoteChecker checker,
            HashSet<string> checkedKeys)
        {
            List<UraOmoteRowResult> results = new List<UraOmoteRowResult>();
            object[] components = row.Tag as object[];
            if (components != null)
            {
                foreach (object item in components)
                {
                    Component2 component = item as Component2;
                    if (component == null)
                        continue;

                    string key = GetComponentCheckKey(component);
                    if (!string.IsNullOrWhiteSpace(key) && checkedKeys.Contains(key))
                        continue;

                    if (!string.IsNullOrWhiteSpace(key))
                        checkedKeys.Add(key);

                    UraOmoteRowResult check = checker.CheckComponent(component, GetBomBuhinNo(row), GetBomFileName(row));
                    if (check != null)
                        results.Add(check);
                }

                return results;
            }

            Component2 singleComponent = row.Tag as Component2;
            if (singleComponent != null)
            {
                string key = GetComponentCheckKey(singleComponent);
                if (string.IsNullOrWhiteSpace(key) || !checkedKeys.Contains(key))
                {
                    if (!string.IsNullOrWhiteSpace(key))
                        checkedKeys.Add(key);

                    UraOmoteRowResult check = checker.CheckComponent(singleComponent, GetBomBuhinNo(row), GetBomFileName(row));
                    if (check != null)
                        results.Add(check);
                }

                return results;
            }

            string partPath = row.Tag as string;
            if (!string.IsNullOrWhiteSpace(partPath))
            {
                string key = partPath;
                if (!checkedKeys.Contains(key))
                {
                    checkedKeys.Add(key);
                    UraOmoteRowResult check = checker.CheckPart(partPath, GetBomBuhinNo(row), GetBomFileName(row), "");
                    if (check != null)
                        results.Add(check);
                }
            }

            return results;
        }

        private bool IsCanceled(Func<bool> isCancellationRequested, UraOmoteCheckResult result)
        {
            if (isCancellationRequested == null || !isCancellationRequested())
                return false;

            result.Canceled = true;
            return true;
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

        private string GetBomBuhinNo(DataGridViewRow row)
        {
            return Convert.ToString(row.Cells[1].Value ?? "");
        }

        private string GetBomFileName(DataGridViewRow row)
        {
            return Convert.ToString(row.Cells[5].Value ?? "");
        }

        private string GetComponentCheckKey(Component2 component)
        {
            try
            {
                return component.GetPathName() + "|" + component.ReferencedConfiguration;
            }
            catch
            {
                return "";
            }
        }

        private void ResolveActiveAssemblyLightweight(ModelDoc2 activeModel)
        {
            try
            {
                AssemblyDoc assembly = activeModel as AssemblyDoc;
                if (assembly != null)
                {
                    int status = assembly.ResolveAllLightWeightComponents(false);
                    Debug.WriteLine("[URA OMOTE] Resolve active assembly lightweight. status=" + status
                        + ", title=" + SafeModelTitle(activeModel));
                }
                else
                {
                    Debug.WriteLine("[URA OMOTE] Resolve active assembly skipped. Active document is not an Assembly.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[URA OMOTE] Resolve active assembly ERROR: " + ex.Message);
            }
        }

        private string SafeModelTitle(ModelDoc2 model)
        {
            try { return model == null ? "null" : model.GetTitle(); }
            catch { return "<title error>"; }
        }

        private string SafeModelType(ModelDoc2 model)
        {
            try { return model == null ? "null" : model.GetType().ToString(); }
            catch { return "<type error>"; }
        }
    }

    public class UraOmoteChecker
    {
        private readonly ISldWorks swApp;

        public UraOmoteChecker(ISldWorks app)
        {
            swApp = app;
        }

        private string SafeComponentName(Component2 component)
        {
            try { return component == null ? "null" : component.Name2; }
            catch { return "<name error>"; }
        }

        private string SafeComponentPath(Component2 component)
        {
            try { return component == null ? "" : component.GetPathName(); }
            catch { return "<path error>"; }
        }

        private string SafeComponentConfiguration(Component2 component)
        {
            try { return component == null ? "" : component.ReferencedConfiguration; }
            catch { return "<config error>"; }
        }

        private string SafeComponentSuppression(Component2 component)
        {
            try { return component == null ? "null" : Convert.ToString(((dynamic)component).GetSuppression()); }
            catch { return "<suppression error>"; }
        }

        private string SafeModelTitle(ModelDoc2 model)
        {
            try { return model == null ? "null" : model.GetTitle(); }
            catch { return "<title error>"; }
        }

        private string SafeModelPath(ModelDoc2 model)
        {
            try { return model == null ? "" : model.GetPathName(); }
            catch { return "<path error>"; }
        }

        private string SafeActiveConfiguration(ModelDoc2 model)
        {
            try
            {
                return model?.ConfigurationManager?.ActiveConfiguration == null
                    ? "<none>"
                    : model.ConfigurationManager.ActiveConfiguration.Name;
            }
            catch { return "<config error>"; }
        }

        private string SafeActiveDisplayState(ModelDoc2 model)
        {
            try
            {
                Configuration configuration = model?.ConfigurationManager?.ActiveConfiguration;
                Array states = configuration?.GetDisplayStates() as Array;
                return states != null && states.Length > 0
                    ? Convert.ToString(states.GetValue(0))
                    : "<none>";
            }
            catch { return "<display-state error>"; }
        }

        private string SafeFeatureName(Feature feature)
        {
            try { return feature == null ? "null" : feature.Name; }
            catch { return "<feature error>"; }
        }

        private string NullableBoolText(bool? value)
        {
            return value.HasValue ? value.Value.ToString() : "null";
        }

        public UraOmoteRowResult CheckComponent(Component2 component, string bomBuhinNo, string bomFileName)
        {
            if (component == null)
                return null;

            Debug.WriteLine("[URA OMOTE] Component check start. name=" + SafeComponentName(component)
                + ", path=" + SafeComponentPath(component)
                + ", referencedConfig=" + SafeComponentConfiguration(component)
                + ", suppression=" + SafeComponentSuppression(component));

            try
            {
                // Hidden, suppressed, or Exclude-from-BOM components can still be
                // checked from their part file. Only envelope components are not
                // treated as production parts for this check.
                if (component.IsEnvelope())
                    return CreateSkippedResult(bomBuhinNo, bomFileName, "", "", "Component l\u00E0 Envelope");
            }
            catch
            {
            }

            string path = "";
            try
            {
                path = component.GetPathName();
            }
            catch
            {
            }

            if (!string.IsNullOrWhiteSpace(path))
            {
                Debug.WriteLine("[URA OMOTE] Component check uses part path directly to avoid lightweight/reference cache.");
                return CheckPart(path, bomBuhinNo, bomFileName, component.Name2, SafeComponentConfiguration(component));
            }

            return CreateCheckResult(bomBuhinNo, bomFileName, component.Name2, "",
                "Component chua co file part da luu; chua the kiem tra tren ban sao.");
        }

        public UraOmoteRowResult CheckPart(string partPath, string bomBuhinNo, string bomFileName, string componentName, string configuration = "")
        {
            Debug.WriteLine("[URA OMOTE] Part check start. path=" + partPath + ", component=" + componentName);
            if (string.IsNullOrWhiteSpace(partPath))
                return CreateSkippedResult(bomBuhinNo, bomFileName, componentName, "", "Kh\u00F4ng c\u00F3 \u0111\u01B0\u1EDDng d\u1EABn part");

            if (!string.Equals(Path.GetExtension(partPath), ".SLDPRT", StringComparison.OrdinalIgnoreCase))
                return CreateSkippedResult(bomBuhinNo, bomFileName, componentName, partPath, "Kh\u00F4ng ph\u1EA3i part");

            try
            {
                using (CheckPartWorkingCopy copy = CheckPartWorkingCopy.Open(swApp, partPath, configuration))
                {
                    UraOmoteRowResult result = CheckModel(copy.Model, bomBuhinNo, bomFileName,
                        string.IsNullOrWhiteSpace(componentName) ? Path.GetFileNameWithoutExtension(partPath) : componentName);
                    copy.ValidateSource();
                    result.PartPath = copy.SourcePath;
                    result.Note = "Kiem tra tren ban sao part da luu; " + result.Note;
                    return result;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[URA OMOTE] Part check ERROR: " + ex);
                return CreateCheckResult(bomBuhinNo, bomFileName, componentName, partPath, ex.Message);
            }
        }

        private UraOmoteRowResult CreateCheckResult(string buhinNo, string fileName,
            string componentName, string partPath, string note)
        {
            UraOmoteRowResult result = CreateSkippedResult(buhinNo, fileName, componentName, partPath, note);
            result.Status = "CHECK";
            return result;
        }

        private UraOmoteRowResult CheckModel(ModelDoc2 part, string bomBuhinNo, string bomFileName, string componentName)
        {
            CheckPartWorkingCopy.RequireWorkingCopy(part);
            string originalConfig = "";
            string defaultConfig = "";
            string flatConfig = "";
            Feature flatPatternFeature = null;
            bool? originalDefaultFlatPatternSuppressed = null;
            bool? originalFlatConfigPatternSuppressed = null;
            try
            {
                originalConfig = part.ConfigurationManager.ActiveConfiguration.Name;
                Debug.WriteLine("[URA OMOTE] Model check start. title=" + SafeModelTitle(part)
                    + ", path=" + SafeModelPath(part)
                    + ", originalConfig=" + originalConfig);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[URA OMOTE] Cannot read original configuration: " + ex.Message);
            }

            try
            {
                if (part.GetType() != (int)swDocumentTypes_e.swDocPART)
                    return CreateSkippedResult(bomBuhinNo, bomFileName, componentName, part.GetPathName(), "Kh\u00F4ng ph\u1EA3i part");

                string[] configNames = part.GetConfigurationNames() as string[];
                if (configNames == null || configNames.Length == 0)
                    return CreateSkippedResult(bomBuhinNo, bomFileName, componentName, part.GetPathName(), "Kh\u00F4ng c\u00F3 configuration");

                defaultConfig = FindDefaultConfigurationName(configNames);
                flatConfig = FindFlatConfigurationName(configNames, defaultConfig);
                bool hasFlatConfig = !string.IsNullOrWhiteSpace(flatConfig);
                Debug.WriteLine("[URA OMOTE] Configurations. count=" + configNames.Length
                    + ", default=" + defaultConfig
                    + ", flat=" + (hasFlatConfig ? flatConfig : "<missing>"));
                bool defaultShown = ActivateConfiguration(part, defaultConfig);
                if (!defaultShown) throw new InvalidOperationException("Khong mo duoc configuration: " + defaultConfig);
                Debug.WriteLine("[URA OMOTE] Show default configuration. ok=" + defaultShown
                    + ", active=" + SafeActiveConfiguration(part));

                flatPatternFeature = FindFlatPatternFeature(part);
                bool hasFlatPattern = flatPatternFeature != null;
                originalDefaultFlatPatternSuppressed = GetSuppressionState(flatPatternFeature);
                Debug.WriteLine("[URA OMOTE] Default FlatPattern. found=" + hasFlatPattern
                    + ", feature=" + SafeFeatureName(flatPatternFeature)
                    + ", originallySuppressed=" + NullableBoolText(originalDefaultFlatPatternSuppressed));

                bool defaultFlatReady = hasFlatPattern
                    && SetFlatPatternSuppressed(part, flatPatternFeature, false);
                PaintFaceSummary defaultSummary = defaultFlatReady
                    ? CheckConfig(part, defaultConfig, true)
                    : new PaintFaceSummary();
                Debug.WriteLine("[URA OMOTE] Default summary. ready=" + defaultFlatReady
                    + ", pinkFaces=" + defaultSummary.PinkFaceCount
                    + ", pinkAreaMm2=" + defaultSummary.PinkAreaMm2
                    + ", colors=" + defaultSummary.PinkColorsText
                    + ", references=" + defaultSummary.FaceReferences.Count);

                if (hasFlatConfig)
                {
                    bool flatShown = ActivateConfiguration(part, flatConfig);
                    if (!flatShown) throw new InvalidOperationException("Khong mo duoc configuration: " + flatConfig);
                    originalFlatConfigPatternSuppressed = GetSuppressionState(flatPatternFeature);
                    Debug.WriteLine("[URA OMOTE] Show flat configuration. ok=" + flatShown
                        + ", active=" + SafeActiveConfiguration(part)
                        + ", originallySuppressed=" + NullableBoolText(originalFlatConfigPatternSuppressed));
                }

                bool generatedFlatReady = hasFlatPattern && hasFlatConfig
                    && SetFlatPatternSuppressed(part, flatPatternFeature, false);
                PaintFaceSummary flatSummary = generatedFlatReady
                    ? CheckConfig(part, flatConfig, false)
                    : new PaintFaceSummary();
                Debug.WriteLine("[URA OMOTE] Flat summary. ready=" + generatedFlatReady
                    + ", pinkFaces=" + flatSummary.PinkFaceCount
                    + ", pinkAreaMm2=" + flatSummary.PinkAreaMm2
                    + ", colors=" + flatSummary.PinkColorsText);

                bool? pinkPositionMatches = defaultFlatReady && generatedFlatReady
                    ? CheckPinkFacePositionAcrossConfigurations(part, flatConfig, defaultSummary)
                    : (bool?)null;
                if (!pinkPositionMatches.HasValue && defaultFlatReady && generatedFlatReady)
                {
                    Debug.WriteLine("[URA OMOTE] Persistent-reference mapping inconclusive; trying planar-normal fallback.");
                    pinkPositionMatches = ComparePinkSideByPlanarNormal(defaultSummary, flatSummary);
                }
                Debug.WriteLine("[URA OMOTE] Pink position comparison=" + NullableBoolText(pinkPositionMatches));

                string partBuhinNo = GetCustomProperty(part, "", "部品番号");
                string buhinNo = string.IsNullOrWhiteSpace(partBuhinNo) ? bomBuhinNo : partBuhinNo;
                string path = part.GetPathName();
                string component = string.IsNullOrWhiteSpace(componentName) ? part.GetTitle() : componentName;

                UraOmoteRowResult result = new UraOmoteRowResult
                {
                    Component = component,
                    BuhinNo = buhinNo,
                    BomFileName = bomFileName,
                    PartPath = path,
                    HasSheetMetal = hasFlatPattern,
                    HasFlatConfig = hasFlatConfig,
                    DefaultPinkFaceCount = defaultSummary.PinkFaceCount,
                    DefaultPinkAreaMm2 = defaultSummary.PinkAreaMm2,
                    FlatPinkFaceCount = flatSummary.PinkFaceCount,
                    FlatPinkAreaMm2 = flatSummary.PinkAreaMm2,
                    Note = ""
                };

                if (!hasFlatPattern)
                {
                    result.Status = "SKIP";
                    result.Note = "Kh\u00F4ng t\u00ECm th\u1EA5y FlatPattern";
                }
                else if (!hasFlatConfig)
                {
                    result.Status = "CHECK";
                    result.Note = "Kh\u00F4ng t\u00ECm th\u1EA5y configuration " + defaultConfig + "SM-FLAT-PATTERN";
                }
                else if (!defaultFlatReady || !generatedFlatReady)
                {
                    result.Status = "CHECK";
                    result.Note = "Kh\u00F4ng m\u1EDF \u0111\u01B0\u1EE3c Flat-Pattern trong Default ho\u1EB7c " + flatConfig;
                }
                else if (defaultSummary.PinkFaceCount == 0 && flatSummary.PinkFaceCount == 0)
                {
                    result.Status = "CHECK";
                    result.Note = "C\u1EA3 hai Flat-Pattern trong Default v\u00E0 " + flatConfig + " \u0111\u1EC1u kh\u00F4ng c\u00F3 m\u1EB7t h\u1ED3ng";
                }
                else if (defaultSummary.PinkFaceCount == 0)
                {
                    result.Status = "NG";
                    result.Note = "Flat-Pattern trong Default kh\u00F4ng c\u00F3 m\u1EB7t h\u1ED3ng, " + flatConfig + " c\u00F3 m\u1EB7t h\u1ED3ng " + flatSummary.PinkColorsText;
                }
                else if (flatSummary.PinkFaceCount == 0)
                {
                    result.Status = "NG";
                    result.Note = "Flat-Pattern trong Default c\u00F3 m\u1EB7t h\u1ED3ng " + defaultSummary.PinkColorsText + ", " + flatConfig + " kh\u00F4ng c\u00F3 m\u1EB7t h\u1ED3ng";
                }
                else if (pinkPositionMatches == false)
                {
                    result.Status = "NG";
                    result.Note = "M\u1EB7t h\u1ED3ng kh\u00F4ng tr\u00F9ng v\u1ECB tr\u00ED gi\u1EEFa Flat-Pattern trong Default v\u00E0 " + flatConfig + " (b\u1ECB \u0111\u1EA3o m\u1EB7t)";
                }
                else if (!pinkPositionMatches.HasValue)
                {
                    result.Status = "CHECK";
                    result.Note = "Kh\u00F4ng \u00E1nh x\u1EA1 \u0111\u01B0\u1EE3c m\u1EB7t h\u1ED3ng gi\u1EEFa Flat-Pattern trong Default v\u00E0 " + flatConfig;
                }
                else if (Math.Abs(defaultSummary.PinkAreaMm2 - flatSummary.PinkAreaMm2) > Math.Max(5.0, defaultSummary.PinkAreaMm2 * 0.03))
                {
                    result.Status = "CHECK";
                    result.Note = "Di\u1EC7n t\u00EDch m\u1EB7t h\u1ED3ng gi\u1EEFa Default v\u00E0 Flat-Pattern l\u1EC7ch nhi\u1EC1u";
                }
                else
                {
                    result.Status = "OK";
                    result.Note = "M\u1EB7t h\u1ED3ng tr\u00F9ng \u0111\u00FAng v\u1ECB tr\u00ED gi\u1EEFa Flat-Pattern trong Default v\u00E0 " + flatConfig;
                }

                Debug.WriteLine("[URA OMOTE] Model result. buhinNo=" + result.BuhinNo
                    + ", status=" + result.Status
                    + ", defaultPink=" + result.DefaultPinkFaceCount
                    + ", flatPink=" + result.FlatPinkFaceCount
                    + ", note=" + result.Note);

                return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[URA OMOTE] Model check ERROR. title=" + SafeModelTitle(part) + ", error=" + ex);
                throw;
            }
            finally
            {
                // The working copy is discarded by CheckPart; no restore rebuild is needed.
                Debug.WriteLine("[URA OMOTE] Model check end. title=" + SafeModelTitle(part));
            }
        }

        private string FindDefaultConfigurationName(string[] configNames)
        {
            if (configNames == null || configNames.Length == 0)
                return "";

            foreach (string configName in configNames)
            {
                if (string.Equals(configName, "Default", StringComparison.OrdinalIgnoreCase))
                    return configName;
            }

            return configNames[0];
        }

        private string FindFlatConfigurationName(string[] configNames, string defaultConfig)
        {
            if (configNames == null)
                return "";

            string expectedName = defaultConfig + "SM-FLAT-PATTERN";
            foreach (string configName in configNames)
            {
                if (string.Equals(configName, expectedName, StringComparison.OrdinalIgnoreCase))
                    return configName;
            }

            return "";
        }

        private bool ActivateConfiguration(ModelDoc2 part, string configName)
        {
            if (part == null || string.IsNullOrWhiteSpace(configName))
                return false;

            try
            {
                if (string.Equals(SafeActiveConfiguration(part), configName, StringComparison.OrdinalIgnoreCase))
                    return true;

                CheckPartWorkingCopy.RequireWorkingCopy(part);
                if (!part.ShowConfiguration2(configName)) return false;
                return string.Equals(SafeActiveConfiguration(part), configName, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[URA OMOTE] Activate configuration ERROR. requested=" + configName
                    + ", error=" + ex.Message);
                return false;
            }
        }

        private bool? GetSuppressionState(Feature feature)
        {
            if (feature == null)
                return null;

            try
            {
                object states = feature.IsSuppressed2(
                    (int)swInConfigurationOpts_e.swThisConfiguration,
                    null);

                if (states is bool)
                    return (bool)states;

                Array stateArray = states as Array;
                if (stateArray != null && stateArray.Length > 0)
                    return Convert.ToBoolean(stateArray.GetValue(0));
            }
            catch
            {
            }

            try
            {
                return feature.IsSuppressed();
            }
            catch
            {
                return null;
            }
        }

        private bool SetFlatPatternSuppressed(ModelDoc2 part, Feature flatPatternFeature, bool suppressed)
        {
            if (part == null || flatPatternFeature == null)
                return false;

            bool? currentState = GetSuppressionState(flatPatternFeature);
            if (currentState.HasValue && currentState.Value == suppressed)
            {
                Debug.WriteLine("[URA OMOTE] FlatPattern suppression unchanged. config=" + SafeActiveConfiguration(part)
                    + ", suppressed=" + suppressed);
                return true;
            }

            try
            {
                CheckPartWorkingCopy.RequireWorkingCopy(part);
                bool changed = flatPatternFeature.SetSuppression2(
                    suppressed
                        ? (int)swFeatureSuppressionAction_e.swSuppressFeature
                        : (int)swFeatureSuppressionAction_e.swUnSuppressFeature,
                    (int)swInConfigurationOpts_e.swThisConfiguration,
                    null);

                if (!changed || !part.EditRebuild3() || GetSuppressionState(flatPatternFeature) != suppressed)
                    return false;
                Debug.WriteLine("[URA OMOTE] Set FlatPattern suppression. config=" + SafeActiveConfiguration(part)
                    + ", requested=" + suppressed
                    + ", changed=" + changed
                    + ", actual=" + NullableBoolText(GetSuppressionState(flatPatternFeature)));
                return changed;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[URA OMOTE] Set FlatPattern suppression ERROR. config=" + SafeActiveConfiguration(part)
                    + ", requested=" + suppressed + ", error=" + ex.Message);
                return false;
            }
        }

        public static bool IsFlatDrawingView(SolidWorks.Interop.sldworks.View view)
        {
            if (view == null)
                return false;
            try { if (view.IsFlatPatternView()) return true; } catch { }
            try
            {
                ModelDoc2 model = view.ReferencedDocument as ModelDoc2;
                if (!(model is PartDoc)) return false;
                string config = view.ReferencedConfiguration;
                if (string.IsNullOrWhiteSpace(config)) return false;
                Feature feature = model.FirstFeature() as Feature;
                while (feature != null)
                {
                    if (feature.GetTypeName2() == "FlatPattern")
                    {
                        object state = feature.IsSuppressed2(
                            (int)swInConfigurationOpts_e.swSpecifyConfiguration, new[] { config });
                        Array states = state as Array;
                        if ((state is bool && !(bool)state)
                            || (states != null && states.Length > 0 && !Convert.ToBoolean(states.GetValue(0))))
                            return true;
                    }
                    feature = feature.GetNextFeature() as Feature;
                }
            }
            catch { }
            return false;
        }

        public FlatDrawingViewSideResult CheckFlatDrawingView(SolidWorks.Interop.sldworks.View view)
        {
            var result = new FlatDrawingViewSideResult();
            ModelDoc2 model = null;
            CheckPartWorkingCopy workingCopy = null;
            try
            {
                if (!IsFlatDrawingView(view))
                {
                    result.Message = "View không ở trạng thái 展開.";
                    return result;
                }
                result.ViewName = view.Name ?? "";
                ModelDoc2 referencedModel = view.ReferencedDocument as ModelDoc2;
                string sourcePath = referencedModel == null ? "" : referencedModel.GetPathName();
                if (string.IsNullOrWhiteSpace(sourcePath))
                {
                    result.Message = "Khong lay duoc file part da luu cua view 展開.";
                    return result;
                }
                string config = view.ReferencedConfiguration ?? "";
                result.Configuration = config;
                if (string.IsNullOrWhiteSpace(config))
                {
                    result.Message = "Khong doc duoc configuration cua view 展開.";
                    return result;
                }
                workingCopy = CheckPartWorkingCopy.Open(swApp, sourcePath, config);
                model = workingCopy.Model;
                PartDoc part = model as PartDoc;
                if (part == null)
                {
                    result.Message = "Khong mo duoc ban sao part cua view 展開.";
                    return result;
                }
                Feature flat = FindFlatPatternFeature(model);
                if (flat == null || GetSuppressionState(flat) != false)
                {
                    result.Message = "Flat-Pattern chưa mở trong configuration của view.";
                    return result;
                }
                string displayState = "";
                try { displayState = view.DisplayState ?? ""; } catch { }
                DisplayStateFaceMaterialSet appearances = ReadDrawingFaceMaterials(model, displayState);
                result.DisplayState = appearances.DisplayStateName;
                if (!appearances.ReadSucceeded)
                {
                    result.Message = "Không đọc được màu mặt trong display state mà view 展開 tham chiếu: " + displayState;
                    return result;
                }
                MathTransform transform = view.ModelToViewTransform;
                MathUtility math = swApp.GetMathUtility() as MathUtility;
                if (transform == null || math == null)
                {
                    result.Message = "Không đọc được hướng nhìn của view 展開.";
                    return result;
                }
                Array bodies = part.GetBodies2((int)swBodyType_e.swSolidBody, true) as Array;
                if (bodies == null || bodies.Length != 1)
                {
                    result.Message = "View 展開 cần xác định được một body tấm duy nhất.";
                    return result;
                }
                double frontArea = 0, backArea = 0, frontPink = 0, backPink = 0;
                foreach (object bodyObject in bodies)
                {
                    Body2 body = bodyObject as Body2;
                    Array faces = body?.GetFaces() as Array;
                    if (faces == null) continue;
                    foreach (object faceObject in faces)
                    {
                        Face2 face = faceObject as Face2;
                        double[] normal = face?.Normal as double[];
                        if (normal == null || normal.Length < 3) continue;
                        MathVector vector = math.CreateVector(normal) as MathVector;
                        vector = vector?.MultiplyTransform(transform) as MathVector;
                        double[] inView = vector?.ArrayData as double[];
                        if (inView == null || inView.Length < 3) continue;
                        double length = Math.Sqrt(inView[0]*inView[0]+inView[1]*inView[1]+inView[2]*inView[2]);
                        if (length < 1e-12) continue;
                        double z = inView[2] / length;
                        // Exclude thickness edges and non-face-on views.
                        if (Math.Abs(z) < 0.98) continue;
                        double area = face.GetArea();
                        double[] material;
                        bool hasAppearance = appearances.TryGet(face, out material);
                        bool pink = hasAppearance ? IsPinkMaterial(material)
                            : !appearances.Authoritative && TryGetPinkFaceMaterial(face, out material);
                        if (z > 0) { frontArea += area; if (pink) frontPink += area; }
                        else { backArea += area; if (pink) backPink += area; }
                    }
                }
                workingCopy.ValidateSource();
                result.FrontPinkAreaMm2 = frontPink * 1000000.0;
                result.BackPinkAreaMm2 = backPink * 1000000.0;
                result.PinkFacingViewer = ClassifyFlatPinkSide(frontArea, backArea, frontPink, backPink);
                result.Message = result.PinkFacingViewer.HasValue
                    ? (result.PinkFacingViewer.Value ? "Mặt đang nhìn của view 展開 có màu hồng (ウラ)." : "Mặt đang nhìn của view 展開 là mặt màu (表); mặt đối diện có màu hồng.")
                    : frontArea <= 0 || backArea <= 0
                        ? "View 展開 không nhìn thẳng hai mặt tấm."
                        : frontPink <= 0 && backPink <= 0
                            ? "Cả hai mặt không có màu hồng trong configuration '" + config + "', display state '" + result.DisplayState + "' của view 展開; chưa xác định được 表/ウラ."
                            : "Diện tích màu hồng không đủ để xác định mặt tấm.";
                Debug.WriteLine("[DRAWING FLAT SIDE] view=" + result.ViewName + " config=" + config
                    + " displayState=" + result.DisplayState + " pinkFacingViewer=" + NullableBoolText(result.PinkFacingViewer)
                    + " frontPinkMm2=" + result.FrontPinkAreaMm2 + " backPinkMm2=" + result.BackPinkAreaMm2);
            }
            catch (Exception ex)
            {
                result.PinkFacingViewer = null;
                result.Message = "Không kiểm tra được mặt của view 展開: " + ex.Message;
            }
            finally
            {
                if (workingCopy != null) workingCopy.Dispose();
            }
            return result;
        }

        internal static bool? ClassifyFlatPinkSide(double frontArea, double backArea, double frontPink, double backPink)
        {
            if (frontArea <= 1e-12 || backArea <= 1e-12) return null;
            const double fraction = 0.01;
            bool front = frontPink > frontArea * fraction;
            bool back = backPink > backArea * fraction;
            // The visible face determines ウラ/表. With no pink face on
            // either side, leave the result unknown so the report warns.
            if (!front && !back) return null;
            return front;
        }

        private PaintFaceSummary CheckConfig(ModelDoc2 part, string configName, bool collectFaceReferences)
        {
            PaintFaceSummary summary = new PaintFaceSummary();
            try
            {
                // Configuration va FlatPattern da duoc chuan bi o CheckModel.
                // Khong rebuild lai tai day vi SOLIDWORKS co the dung khi rebuild
                // lien tiep tren mot so part co Flat-Pattern phuc tap.
                string activeBeforeScan = SafeActiveConfiguration(part);
                bool shown = string.Equals(
                    activeBeforeScan,
                    configName,
                    StringComparison.OrdinalIgnoreCase);

                Debug.WriteLine("[URA OMOTE] Scan config prepare. requested=" + configName
                    + ", activeBefore=" + activeBeforeScan
                    + ", alreadyActive=" + shown);

                if (!shown)
                {
                    Debug.WriteLine("[URA OMOTE] Scan config activate start. requested=" + configName);
                    shown = ActivateConfiguration(part, configName);
                    Debug.WriteLine("[URA OMOTE] Scan config activate end. requested=" + configName
                        + ", shown=" + shown
                        + ", activeAfter=" + SafeActiveConfiguration(part));
                }

                Debug.WriteLine("[URA OMOTE] Scan config start. requested=" + configName
                    + ", shown=" + shown
                    + ", active=" + SafeActiveConfiguration(part)
                    + ", activeDisplayState=" + SafeActiveDisplayState(part)
                    + ", collectReferences=" + collectFaceReferences);

                PartDoc partDoc = part as PartDoc;
                object bodiesObj = partDoc?.GetBodies2((int)swBodyType_e.swSolidBody, true);
                object[] bodies = bodiesObj as object[];
                if (bodies == null && bodiesObj != null)
                    bodies = new[] { bodiesObj };

                if (bodies == null)
                {
                    Debug.WriteLine("[URA OMOTE] Scan config has no visible solid bodies. config=" + configName);
                    return summary;
                }

                Debug.WriteLine("[URA OMOTE] Scan config bodies=" + bodies.Length + ", config=" + configName);

                int totalFaces = 0;
                DisplayStateFaceMaterialSet displayStateMaterials = ReadActiveDisplayStateFaceMaterials(part);

                foreach (object bodyObj in bodies)
                {
                    Body2 body = bodyObj as Body2;
                    if (body == null)
                        continue;

                    object facesObj = body.GetFaces();
                    object[] faces = facesObj as object[];
                    if (faces == null && facesObj != null)
                        faces = new[] { facesObj };

                    if (faces == null)
                        continue;

                    totalFaces += faces.Length;

                    foreach (object faceObj in faces)
                    {
                        Face2 face = faceObj as Face2;
                        if (face == null)
                            continue;

                        double[] material;
                        bool hasDisplayStateMaterial = displayStateMaterials.TryGet(face, out material);
                        if (hasDisplayStateMaterial)
                        {
                            if (!IsPinkMaterial(material))
                                continue;
                        }
                        else
                        {
                            // When display states are linked to configurations, the
                            // active display state is authoritative. Do not fall back
                            // to stale configuration-level face appearance values.
                            if (displayStateMaterials.Authoritative)
                                continue;

                            if (!TryGetPinkFaceMaterial(face, out material))
                                continue;
                        }

                        summary.PinkFaceCount++;
                        summary.AddPinkColor(material);
                        summary.AddPinkFaceGeometry(face);
                        if (collectFaceReferences)
                            summary.AddFaceReference(part.Extension.GetPersistReference3(face));
                        try
                        {
                            summary.PinkAreaMm2 += face.GetArea() * 1000000.0;
                        }
                        catch
                        {
                        }
                    }
                }

                summary.PinkAreaMm2 = Math.Round(summary.PinkAreaMm2, 1);
                Debug.WriteLine("[URA OMOTE] Scan config end. config=" + configName
                    + ", totalFaces=" + totalFaces
                    + ", pinkFaces=" + summary.PinkFaceCount
                    + ", pinkAreaMm2=" + summary.PinkAreaMm2
                    + ", colors=" + summary.PinkColorsText
                    + ", references=" + summary.FaceReferences.Count);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[URA OMOTE] Scan config ERROR. config=" + configName + ", error=" + ex);
            }

            return summary;
        }

        private sealed class DisplayStateFaceMaterialSet
        {
            public bool Authoritative { get; set; }
            public bool ReadSucceeded { get; set; }
            public string DisplayStateName { get; set; }
            public readonly List<Face2> Faces = new List<Face2>();
            public readonly List<double[]> Materials = new List<double[]>();

            public bool TryGet(Face2 target, out double[] material)
            {
                material = null;
                if (target == null)
                    return false;

                for (int i = 0; i < Faces.Count && i < Materials.Count; i++)
                {
                    try
                    {
                        if (Faces[i] != null && target.IsSame(Faces[i]))
                        {
                            material = Materials[i];
                            return material != null && material.Length >= 3;
                        }
                    }
                    catch
                    {
                    }
                }

                return false;
            }
        }

        private sealed class DrawingFaceAppearance
        {
            public object Entity;
            public double[] Material;
            public int Priority;
        }

        // Render appearances are separate from legacy MaterialPropertyValues.
        // Evaluate only the configuration/display state used by the Drawing,
        // on the disposable CHECK copy. Never switch the user's source part.
        private DisplayStateFaceMaterialSet ReadDrawingFaceMaterials(ModelDoc2 model, string requestedState)
        {
            var result = new DisplayStateFaceMaterialSet { Authoritative = true };
            try
            {
                CheckPartWorkingCopy.RequireWorkingCopy(model);
                Configuration configuration = model.ConfigurationManager.ActiveConfiguration;
                Array states = configuration.GetDisplayStates() as Array;
                if (states == null || states.Length == 0) return result;
                string state = string.IsNullOrWhiteSpace(requestedState)
                    ? Convert.ToString(states.GetValue(states.GetLowerBound(0))) : requestedState;
                bool found = false;
                foreach (object value in states)
                    if (string.Equals(Convert.ToString(value), state, StringComparison.OrdinalIgnoreCase)) found = true;
                if (!found || !configuration.ApplyDisplayState(state)) return result;
                result.DisplayStateName = state;

                var legacy = ReadActiveDisplayStateFaceMaterials(model, state);
                object names = new[] { state };
                int option = (int)swDisplayStateOpts_e.swSpecifyDisplayState;
                int count = model.Extension.GetRenderMaterialsCount2(option, names);
                Array materials = model.Extension.GetRenderMaterials2(option, names) as Array;
                if (count < 0 || (count > 0 && (materials == null || materials.Length != count))) return result;
                var appearances = new List<DrawingFaceAppearance>();
                if (materials != null)
                    foreach (object value in materials)
                    {
                        RenderMaterial appearance = value as RenderMaterial;
                        if (appearance == null) return result;
                        Array entities = appearance.GetEntities() as Array;
                        if (appearance.GetEntitiesCount() > 0 && entities == null) return result;
                        // A patterned appearance has no single face RGB value.
                        // Keep it unknown instead of assuming its primary color.
                        int color = appearance.PrimaryColor;
                        double[] rgb = color >= 0 && appearance.SecondaryColor == color
                            ? new[] { (color & 255) / 255.0, ((color >> 8) & 255) / 255.0, ((color >> 16) & 255) / 255.0 } : null;
                        if (entities == null) continue;
                        foreach (object entity in entities)
                        {
                            int priority = entity is Face2 ? 4 : entity is Feature ? 3 : entity is Body2 ? 2 : entity is ModelDoc2 ? 1 : 0;
                            if (priority > 0) appearances.Add(new DrawingFaceAppearance { Entity = entity, Priority = priority, Material = rgb });
                        }
                    }

                Array bodies = ((PartDoc)model).GetBodies2((int)swBodyType_e.swSolidBody, true) as Array;
                if (bodies == null) return result;
                foreach (Body2 body in bodies)
                {
                    Array faces = body.GetFaces() as Array;
                    if (faces == null) return result;
                    foreach (Face2 face in faces)
                    {
                        DrawingFaceAppearance effective = null;
                        foreach (DrawingFaceAppearance appearance in appearances)
                        {
                            bool matches = appearance.Entity is Face2
                                ? face.IsSame((Face2)appearance.Entity)
                                : appearance.Entity is Feature
                                    ? swApp.IsSame(face.GetFeature(), appearance.Entity) == (int)swObjectEquality.swObjectSame
                                    : appearance.Entity is Body2
                                        ? swApp.IsSame(body, appearance.Entity) == (int)swObjectEquality.swObjectSame
                                        : swApp.IsSame(model, appearance.Entity) == (int)swObjectEquality.swObjectSame;
                            if (!matches) continue;
                            if (effective != null && effective.Priority == appearance.Priority &&
                                !SameDrawingColor(effective.Material, appearance.Material)) return result;
                            if (effective == null || appearance.Priority > effective.Priority) effective = appearance;
                        }
                        double[] material;
                        if (effective != null && effective.Priority == 4)
                            material = effective.Material;
                        else if (!legacy.TryGet(face, out material) || !ValidDrawingColor(material))
                        {
                            // Legacy face colors still take precedence over a
                            // feature/body/model appearance in the same state.
                            material = face.GetMaterialPropertyValues2((int)swInConfigurationOpts_e.swThisConfiguration, null) as double[];
                            if (!ValidDrawingColor(material)) material = effective?.Material;
                        }
                        if (effective != null && effective.Material == null && !ValidDrawingColor(material)) return result;
                        if (!ValidDrawingColor(material)) continue;
                        result.Faces.Add(face);
                        result.Materials.Add(material);
                    }
                }
                result.ReadSucceeded = true;
                Debug.WriteLine("[URA OMOTE] Drawing appearances. config=" + configuration.Name + ", state=" + state
                    + ", renderMaterials=" + count + ", coloredFaces=" + result.Faces.Count);
            }
            catch (Exception error)
            {
                Debug.WriteLine("[URA OMOTE] Drawing appearance ERROR: " + error);
            }
            return result;
        }

        private static bool ValidDrawingColor(double[] material)
        {
            return material != null && material.Length >= 3 &&
                material[0] >= 0 && material[0] <= 1 && material[1] >= 0 && material[1] <= 1 && material[2] >= 0 && material[2] <= 1;
        }

        private static bool SameDrawingColor(double[] first, double[] second)
        {
            if (first == null || second == null) return first == second;
            for (int i = 0; i < 3; i++) if (Math.Abs(first[i] - second[i]) > 1e-6) return false;
            return true;
        }

        private DisplayStateFaceMaterialSet ReadActiveDisplayStateFaceMaterials(ModelDoc2 part, string requestedDisplayState = "")
        {
            DisplayStateFaceMaterialSet result = new DisplayStateFaceMaterialSet();
            try
            {
                ConfigurationManager manager = part?.ConfigurationManager;
                Configuration configuration = manager?.ActiveConfiguration;
                Array displayStates = configuration?.GetDisplayStates() as Array;
                if (configuration == null || displayStates == null || displayStates.Length == 0)
                {
                    Debug.WriteLine("[URA OMOTE] Display-state face scan skipped: no active display state.");
                    return result;
                }

                if (!string.IsNullOrWhiteSpace(requestedDisplayState))
                {
                    bool found = false;
                    foreach (object state in displayStates)
                        if (string.Equals(Convert.ToString(state), requestedDisplayState, StringComparison.OrdinalIgnoreCase))
                            found = true;
                    if (!found) return result;
                }

                result.DisplayStateName = string.IsNullOrWhiteSpace(requestedDisplayState)
                    ? Convert.ToString(displayStates.GetValue(0)) : requestedDisplayState;
                bool linked = false;
                try
                {
                    linked = manager.LinkDisplayStatesToConfigurations;
                }
                catch
                {
                }

                object rawFaces;
                object rawProperties = configuration.GetDisplayStateFaceProperties(
                    result.DisplayStateName,
                    out rawFaces);
                Array faces = rawFaces as Array;
                Array properties = rawProperties as Array;

                // Linked active states and an explicitly requested drawing-view
                // state are authoritative, including an empty appearance list.
                result.ReadSucceeded = faces != null && properties != null;
                result.Authoritative = faces != null && (linked || !string.IsNullOrWhiteSpace(requestedDisplayState));

                if (faces != null && properties != null)
                {
                    for (int i = 0; i < faces.Length; i++)
                    {
                        Face2 face = faces.GetValue(i) as Face2;
                        double[] material = ReadDisplayStateMaterial(properties, i);
                        if (face == null || material == null)
                            continue;

                        result.Faces.Add(face);
                        result.Materials.Add(material);
                    }
                }

                Debug.WriteLine("[URA OMOTE] Display-state face appearances. state=" + result.DisplayStateName
                    + ", linkedToConfig=" + linked
                    + ", authoritative=" + result.Authoritative
                    + ", rawFaces=" + (faces == null ? 0 : faces.Length)
                    + ", rawProperties=" + (properties == null ? 0 : properties.Length)
                    + ", parsed=" + result.Faces.Count);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[URA OMOTE] Display-state face appearance ERROR: " + ex);
            }

            return result;
        }

        private double[] ReadDisplayStateMaterial(Array properties, int faceIndex)
        {
            if (properties == null || faceIndex < 0)
                return null;

            try
            {
                object value = faceIndex < properties.Length ? properties.GetValue(faceIndex) : null;
                Array nested = value as Array;
                if (nested != null && nested.Length >= 9)
                {
                    double[] material = new double[9];
                    for (int i = 0; i < 9; i++)
                        material[i] = Convert.ToDouble(nested.GetValue(i));
                    return HasAssignedMaterial(material) ? material : null;
                }
            }
            catch
            {
            }

            int offset = faceIndex * 9;
            if (properties.Length < offset + 9)
                return null;

            try
            {
                double[] material = new double[9];
                for (int i = 0; i < 9; i++)
                    material[i] = Convert.ToDouble(properties.GetValue(offset + i));
                return HasAssignedMaterial(material) ? material : null;
            }
            catch
            {
                return null;
            }
        }

        private bool HasAssignedMaterial(double[] material)
        {
            if (material == null || material.Length < 3)
                return false;

            for (int i = 0; i < material.Length; i++)
            {
                if (material[i] >= 0.0)
                    return true;
            }

            return false;
        }

        private bool? ComparePinkSideByPlanarNormal(PaintFaceSummary source, PaintFaceSummary target)
        {
            double[] sourceNormal;
            double[] targetNormal;
            if (source == null || target == null
                || !source.TryGetDominantPinkNormal(out sourceNormal)
                || !target.TryGetDominantPinkNormal(out targetNormal))
                return null;

            double dot = sourceNormal[0] * targetNormal[0]
                + sourceNormal[1] * targetNormal[1]
                + sourceNormal[2] * targetNormal[2];

            Debug.WriteLine("[URA OMOTE] Planar-normal comparison. dot=" + dot.ToString("0.######")
                + ", source=(" + string.Join(",", sourceNormal) + ")"
                + ", target=(" + string.Join(",", targetNormal) + ")");

            if (Math.Abs(dot) < 0.8)
                return null;

            return dot > 0.0;
        }

        private bool? CheckPinkFacePositionAcrossConfigurations(ModelDoc2 part, string targetConfig, PaintFaceSummary sourceSummary)
        {
            if (part == null || sourceSummary == null || sourceSummary.PinkFaceCount == 0)
                return null;

            if (sourceSummary.FaceReferences.Count == 0)
                return null;

            try
            {
                if (!ActivateConfiguration(part, targetConfig))
                {
                    Debug.WriteLine("[URA OMOTE] Persistent mapping cannot show target config=" + targetConfig);
                    return null;
                }

                DisplayStateFaceMaterialSet targetDisplayStateMaterials =
                    ReadActiveDisplayStateFaceMaterials(part);
                int resolvedReferenceCount = 0;
                int referenceIndex = 0;

                foreach (object persistReference in sourceSummary.FaceReferences)
                {
                    referenceIndex++;
                    int errorCode;
                    object mappedObject = part.Extension.GetObjectByPersistReference3(
                        persistReference,
                        out errorCode);

                    Face2 mappedFace = mappedObject as Face2;
                    if (mappedFace == null)
                    {
                        Debug.WriteLine("[URA OMOTE] Persistent mapping failed. index=" + referenceIndex
                            + ", errorCode=" + errorCode
                            + ", object=" + (mappedObject == null ? "null" : mappedObject.GetType().FullName));
                        continue;
                    }

                    resolvedReferenceCount++;
                    double[] material;
                    bool hasDisplayStateMaterial = targetDisplayStateMaterials.TryGet(mappedFace, out material);
                    bool mappedFaceIsPink = hasDisplayStateMaterial
                        ? IsPinkMaterial(material)
                        : !targetDisplayStateMaterials.Authoritative
                            && TryGetPinkFaceMaterial(mappedFace, out material);

                    if (!mappedFaceIsPink)
                    {
                        // A persistent face ID can resolve to another sheet face after
                        // FlatPattern rebuilds the topology. This is not sufficient proof
                        // that the painted side is reversed. Mark the mapping inconclusive
                        // so the planar-normal comparison can decide the physical side.
                        Debug.WriteLine("[URA OMOTE] Persistent mapping inconclusive: mapped face is not pink in active appearance. index="
                            + referenceIndex + ", errorCode=" + errorCode
                            + ", displayState=" + targetDisplayStateMaterials.DisplayStateName
                            + ", authoritative=" + targetDisplayStateMaterials.Authoritative);
                        return null;
                    }

                    Debug.WriteLine("[URA OMOTE] Persistent mapping resolved pink face from "
                        + (hasDisplayStateMaterial ? "display state" : "face/config appearance")
                        + ". index=" + referenceIndex + ", errorCode=" + errorCode);
                }

                Debug.WriteLine("[URA OMOTE] Persistent mapping summary. resolved=" + resolvedReferenceCount
                    + ", expected=" + sourceSummary.FaceReferences.Count);

                return resolvedReferenceCount == sourceSummary.FaceReferences.Count
                    ? (bool?)true
                    : null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[URA OMOTE] Persistent mapping ERROR: " + ex);
                return null;
            }
        }

        private bool TryGetPinkFaceMaterial(Face2 face, out double[] material)
        {
            material = null;
            try
            {
                material = face.GetMaterialPropertyValues2(
                    (int)swInConfigurationOpts_e.swThisConfiguration,
                    null) as double[];

                if (material == null || material.Length < 3)
                    material = face.MaterialPropertyValues as double[];
            }
            catch
            {
            }

            if (material == null || material.Length < 3)
                return false;

            return IsPinkMaterial(material);
        }

        private bool IsPinkMaterial(double[] material)
        {
            if (material == null || material.Length < 3)
                return false;

            double red = material[0];
            double green = material[1];
            double blue = material[2];
            double max = Math.Max(red, Math.Max(green, blue));
            double min = Math.Min(red, Math.Min(green, blue));

            return red > 0.45
                && red >= green + 0.08
                && blue >= green - 0.05
                && max - min > 0.08;
        }

        private Feature FindFlatPatternFeature(ModelDoc2 part)
        {
            Feature feat = part.FirstFeature() as Feature;
            while (feat != null)
            {
                if (feat.GetTypeName2() == "FlatPattern")
                    return feat;

                feat = feat.GetNextFeature() as Feature;
            }

            return null;
        }

        private bool HasConfiguration(ModelDoc2 part, string configName)
        {
            string[] configs = part.GetConfigurationNames() as string[];
            if (configs == null)
                return false;

            foreach (string config in configs)
            {
                if (string.Equals(config, configName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private string GetCustomProperty(ModelDoc2 model, string configName, string propName)
        {
            try
            {
                CustomPropertyManager propMgr = model.Extension.get_CustomPropertyManager(configName);
                string valOut;
                string resolvedVal;
                bool wasResolved;
                bool linkToProp;
                propMgr.Get6(propName, true, out valOut, out resolvedVal, out wasResolved, out linkToProp);
                return resolvedVal;
            }
            catch
            {
                return "";
            }
        }

        private UraOmoteRowResult CreateSkippedResult(string buhinNo, string bomFileName, string component, string path, string note)
        {
            return new UraOmoteRowResult
            {
                Component = component,
                BuhinNo = buhinNo,
                BomFileName = bomFileName,
                PartPath = path,
                Status = "SKIP",
                Note = note
            };
        }

        private class PaintFaceSummary
        {
            private readonly HashSet<string> pinkColors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            private readonly List<object> faceReferences = new List<object>();
            private readonly List<PinkFaceGeometry> pinkFaceGeometries = new List<PinkFaceGeometry>();

            public int PinkFaceCount { get; set; }
            public double PinkAreaMm2 { get; set; }
            public IList<object> FaceReferences { get { return faceReferences; } }

            public string PinkColorsText
            {
                get
                {
                    if (pinkColors.Count == 0)
                        return "(kh\u00F4ng c\u00F3)";

                    List<string> colors = new List<string>(pinkColors);
                    colors.Sort(StringComparer.OrdinalIgnoreCase);
                    return string.Join(", ", colors);
                }
            }

            public void AddPinkColor(double[] material)
            {
                if (material == null || material.Length < 3)
                    return;

                int red = ToRgbByte(material[0]);
                int green = ToRgbByte(material[1]);
                int blue = ToRgbByte(material[2]);
                pinkColors.Add("RGB(" + red + "," + green + "," + blue + ")");
            }

            public void AddFaceReference(object persistReference)
            {
                if (persistReference != null)
                    faceReferences.Add(persistReference);
            }

            public void AddPinkFaceGeometry(Face2 face)
            {
                if (face == null)
                    return;

                try
                {
                    double[] normal = face.Normal as double[];
                    if (normal == null || normal.Length < 3)
                        return;

                    double length = Math.Sqrt(normal[0] * normal[0] + normal[1] * normal[1] + normal[2] * normal[2]);
                    if (length < 0.9)
                        return;

                    pinkFaceGeometries.Add(new PinkFaceGeometry
                    {
                        Area = face.GetArea(),
                        Normal = new[] { normal[0] / length, normal[1] / length, normal[2] / length }
                    });
                }
                catch
                {
                }
            }

            public bool TryGetDominantPinkNormal(out double[] normal)
            {
                normal = null;
                PinkFaceGeometry largest = null;
                foreach (PinkFaceGeometry geometry in pinkFaceGeometries)
                {
                    if (largest == null || geometry.Area > largest.Area)
                        largest = geometry;
                }

                if (largest == null)
                    return false;

                normal = largest.Normal;
                return normal != null && normal.Length >= 3;
            }

            public bool HasSamePinkColors(PaintFaceSummary other)
            {
                return other != null && pinkColors.SetEquals(other.pinkColors);
            }

            private static int ToRgbByte(double value)
            {
                return Math.Max(0, Math.Min(255, (int)Math.Round(value * 255.0)));
            }
        }

        private class PinkFaceGeometry
        {
            public double Area { get; set; }
            public double[] Normal { get; set; }
        }
    }

    public class UraOmoteCheckResult
    {
        public UraOmoteCheckResult()
        {
            Results = new List<UraOmoteRowResult>();
            HighlightRowIndexes = new HashSet<int>();
        }

        public int CheckedCount { get; set; }
        public int ProcessedCount { get; set; }
        public int SkippedCount { get; set; }
        public bool Canceled { get; set; }
        public List<UraOmoteRowResult> Results { get; private set; }
        public HashSet<int> HighlightRowIndexes { get; private set; }
    }

    public sealed class FlatDrawingViewSideResult
    {
        public string ViewName { get; set; } = "";
        public string Configuration { get; set; } = "";
        public string DisplayState { get; set; } = "";
        public bool? PinkFacingViewer { get; set; }
        public double FrontPinkAreaMm2 { get; set; }
        public double BackPinkAreaMm2 { get; set; }
        public string Message { get; set; } = "";
    }

    public class UraOmoteRowResult
    {
        public string Component { get; set; }
        public string BuhinNo { get; set; }
        public string BomFileName { get; set; }
        public string PartPath { get; set; }
        public bool HasSheetMetal { get; set; }
        public bool HasFlatConfig { get; set; }
        public int DefaultPinkFaceCount { get; set; }
        public double DefaultPinkAreaMm2 { get; set; }
        public int FlatPinkFaceCount { get; set; }
        public double FlatPinkAreaMm2 { get; set; }
        public string Status { get; set; }
        public string Note { get; set; }
    }

    public static class ExcelUraOmoteExporter
    {
        public static void Export(List<UraOmoteRowResult> results)
        {
            try
            {
                List<UraOmoteRowResult> reportRows = new List<UraOmoteRowResult>();
                if (results != null)
                {
                    foreach (UraOmoteRowResult result in results)
                    {
                        if (result == null)
                            continue;

                        // Excel chi bao cao chi tiet co sai khac hoac can kiem tra lai.
                        if (result.Status == "NG" || result.Status == "CHECK")
                            reportRows.Add(result);
                    }
                }

                Debug.WriteLine("[URA OMOTE] Excel filter. all="
                    + (results == null ? 0 : results.Count)
                    + ", report=" + reportRows.Count);

                if (reportRows.Count == 0)
                {
                    MessageBox.Show(
                        "Khong tim thay chi tiet co su khac biet. Khong xuat Excel.",
                        "Check Ura Omote",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                Type excelType = Type.GetTypeFromProgID("Excel.Application");
                if (excelType == null)
                {
                    MessageBox.Show("Kh\u00F4ng t\u00ECm th\u1EA5y Microsoft Excel.", "Check Ura Omote", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                dynamic xlApp = Activator.CreateInstance(excelType);
                dynamic xlWB = xlApp.Workbooks.Add();
                using (var exportScope = new ExcelCheckExportScope(xlApp, "URA OMOTE"))
                {
                    dynamic xlWS = xlWB.Sheets[1];
                    xlWS.Name = "Check Ura Omote";

                    WriteHeader(xlWS);
                    WriteRows(xlWS, reportRows);

                    int lastRow = reportRows.Count + 1;
                    if (lastRow > 1)
                        TrySortExcelByBuhinNo(xlWS, lastRow);

                    xlWS.Range["A1:E" + lastRow].Columns.AutoFit();
                    AutoFitNoteColumn(xlWS, lastRow, 5);
                }
                xlApp.Visible = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show("L\u1ED7i xu\u1EA5t Excel: " + ex.Message, "Check Ura Omote", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void WriteHeader(dynamic xlWS)
        {
            ExcelCheckReportWriter.WriteHeaders(xlWS, new[] {
                "\u90E8\u54C1\u756A\u53F7",
                "S\u1ED1 m\u1EB7t h\u1ED3ng trong Default",
                "S\u1ED1 m\u1EB7t h\u1ED3ng trong Flat-Pattern",
                "Status",
                "Note"
            });
        }

        private static void WriteRows(dynamic xlWS, List<UraOmoteRowResult> results)
        {
            var data = new object[results.Count, 5];
            var fills = new ExcelCheckFillBatch(1, 5);
            int row = 2;
            foreach (UraOmoteRowResult result in results)
            {
                data[row - 2, 0] = result.BuhinNo;
                data[row - 2, 1] = result.DefaultPinkFaceCount;
                data[row - 2, 2] = result.FlatPinkFaceCount;
                data[row - 2, 3] = result.Status;
                data[row - 2, 4] = result.Note;

                if (result.Status == "NG")
                    fills.Add(row, Rgb(255, 199, 206));
                else if (result.Status == "CHECK")
                    fills.Add(row, Rgb(255, 235, 156));
                else if (result.Status == "SKIP")
                    fills.Add(row, Rgb(217, 217, 217));

                row++;
            }
            ExcelCheckReportWriter.WriteBlock(xlWS, 2, 1, data);
            fills.Apply(xlWS);
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

        private static void TrySortExcelByBuhinNo(dynamic xlWS, int lastRow)
        {
            try
            {
                dynamic sort = xlWS.Sort;
                sort.SortFields.Clear();
                sort.SortFields.Add(xlWS.Range["A2:A" + lastRow], 0, 1);
                sort.SetRange(xlWS.Range["A1:E" + lastRow]);
                sort.Header = 1;
                sort.Apply();
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
