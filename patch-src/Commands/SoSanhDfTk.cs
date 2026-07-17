using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands
{
    public class ChaySoSanhDfTk
    {
        private readonly ISldWorks swApp;
        private readonly DataGridView gridBom;

        public ChaySoSanhDfTk(ISldWorks app, DataGridView grid)
        {
            swApp = app;
            gridBom = grid;
        }

        public KetQuaSoSanhDfTk Run(
            Action<int> progressStarted,
            Action<int, int> progressChanged,
            Func<bool> isCancellationRequested)
        {
            DfTkCheckerFromCustomBOM checker = new DfTkCheckerFromCustomBOM(swApp);
            KetQuaSoSanhDfTk runResult = new KetQuaSoSanhDfTk();

            bool oldCommandInProgress = swApp.CommandInProgress;
            ModelView activeView = null;

            try
            {
                ModelDoc2 activeModel = swApp.ActiveDoc as ModelDoc2;
                activeView = activeModel?.ActiveView as ModelView;
                if (activeView != null)
                    activeView.EnableGraphicsUpdate = false;

                swApp.CommandInProgress = true;
                ResolveActiveAssemblyLightweight(activeModel);
                ResolveBomAssemblyLightweight();

                HashSet<string> selectedBuhinNos = GetSelectedBuhinNos();
                HashSet<string> selectedFileNames = GetSelectedFileNames();
                runResult.CheckedCount = CountCheckedRows();
                progressStarted?.Invoke(runResult.CheckedCount);
                if (IsCanceled(isCancellationRequested, runResult))
                    return runResult;

                AssemblyDoc assemblyForCheck = GetAssemblyForCheck(activeModel);
                if (assemblyForCheck != null)
                    AddUniqueResults(runResult.DiffResults, checker.CheckAssemblySelected(assemblyForCheck, selectedBuhinNos, selectedFileNames, isCancellationRequested));

                if (IsCanceled(isCancellationRequested, runResult))
                    return runResult;

                int processedCount = 0;
                int debugIndex = 0;
                foreach (DataGridViewRow row in gridBom.Rows)
                {
                    if (IsCanceled(isCancellationRequested, runResult))
                        break;

                    if (row.IsNewRow || !Convert.ToBoolean(row.Cells[0].Value ?? false))
                        continue;

                    List<DfTkResult> rowResults = CheckBomRow(row, checker, runResult.CheckLogs, ref debugIndex);
                    processedCount++;
                    runResult.ProcessedCount = processedCount;
                    progressChanged?.Invoke(processedCount, runResult.CheckedCount);

                    if (rowResults.Count == 0)
                    {
                        if (row.Tag == null)
                            runResult.SkippedCount++;

                        continue;
                    }

                    AddUniqueResults(runResult.DiffResults, rowResults);
                    runResult.HighlightRowIndexes.Add(row.Index);
                }
            }
            finally
            {
                swApp.CommandInProgress = oldCommandInProgress;
                if (activeView != null)
                    activeView.EnableGraphicsUpdate = true;
            }

            return runResult;
        }

        private bool IsCanceled(Func<bool> isCancellationRequested, KetQuaSoSanhDfTk runResult)
        {
            if (isCancellationRequested == null || !isCancellationRequested())
                return false;

            runResult.Canceled = true;
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

        private HashSet<string> GetSelectedBuhinNos()
        {
            return GetSelectedColumnValues(1);
        }

        private HashSet<string> GetSelectedFileNames()
        {
            return GetSelectedColumnValues(5);
        }

        private HashSet<string> GetSelectedColumnValues(int columnIndex)
        {
            HashSet<string> values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (DataGridViewRow row in gridBom.Rows)
            {
                if (row.IsNewRow || !Convert.ToBoolean(row.Cells[0].Value ?? false))
                    continue;

                string value = NormalizeKey(Convert.ToString(row.Cells[columnIndex].Value ?? ""));
                if (!string.IsNullOrWhiteSpace(value))
                    values.Add(value);
            }

            return values;
        }

        private AssemblyDoc GetAssemblyForCheck(ModelDoc2 activeModel)
        {
            AssemblyDoc activeAssembly = activeModel as AssemblyDoc;
            if (activeAssembly != null)
                return activeAssembly;

            foreach (DataGridViewRow row in gridBom.Rows)
            {
                if (row.IsNewRow)
                    continue;

                Component2 component = GetFirstComponentFromRow(row);
                AssemblyDoc assembly = GetOwningAssembly(component);
                if (assembly != null)
                    return assembly;
            }

            return null;
        }

        private List<DfTkResult> CheckBomRow(
            DataGridViewRow row,
            DfTkCheckerFromCustomBOM checker,
            List<string> checkLogs,
            ref int debugIndex)
        {
            List<DfTkResult> results = new List<DfTkResult>();
            HashSet<string> checkedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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

                    DfTkResult result = checker.CheckComponent(component);
                    WriteCheckDfTkDebug(++debugIndex, row, component, result, checker);
                    if (result != null)
                        results.Add(result);
                    else
                        AddCheckLog(checkLogs, row, component, checker.LastSkipReason);
                }

                return results;
            }

            Component2 singleComponent = row.Tag as Component2;
            if (singleComponent != null)
            {
                DfTkResult result = checker.CheckComponent(singleComponent);
                WriteCheckDfTkDebug(++debugIndex, row, singleComponent, result, checker);
                if (result != null)
                    results.Add(result);
                else
                    AddCheckLog(checkLogs, row, singleComponent, checker.LastSkipReason);

                return results;
            }

            string partPath = row.Tag as string;
            if (!string.IsNullOrWhiteSpace(partPath))
            {
                DfTkResult result = checker.CheckPart(partPath);
                WriteCheckDfTkDebug(++debugIndex, row, partPath, result, checker);
                if (result != null)
                    results.Add(result);
                else
                    AddCheckLog(checkLogs, row, partPath, checker.LastSkipReason);
            }

            return results;
        }

        private void WriteCheckDfTkDebug(
            int index,
            DataGridViewRow row,
            object source,
            DfTkResult result,
            DfTkCheckerFromCustomBOM checker)
        {
            DfTkResult debugResult = result ?? checker.LastCheckedResult;
            string status = result != null
                ? "NG"
                : string.Equals(checker.LastSkipReason, "Khong khac nhau", StringComparison.OrdinalIgnoreCase)
                    ? "OK"
                    : "SKIP";

            string component = debugResult != null && !string.IsNullOrWhiteSpace(debugResult.Component)
                ? debugResult.Component
                : GetCheckSourceText(source);
            string path = debugResult != null && !string.IsNullOrWhiteSpace(debugResult.PartPath)
                ? debugResult.PartPath
                : GetCheckSourcePath(source);
            string buhinNo = debugResult != null && !string.IsNullOrWhiteSpace(debugResult.BuhinNo)
                ? debugResult.BuhinNo
                : Convert.ToString(row.Cells[1].Value ?? "");
            string outerDf = debugResult == null ? "" : debugResult.OuterDf;
            string outerTk = debugResult == null ? "" : debugResult.OuterTk;
            string innerDf = debugResult == null ? "" : debugResult.InnerDf;
            string innerTk = debugResult == null ? "" : debugResult.InnerTk;
            string areaDf = debugResult == null ? "" : debugResult.AreaDf.ToString("0.0");
            string areaTk = debugResult == null ? "" : debugResult.AreaTk.ToString("0.0");
            string reason = result == null && !string.IsNullOrWhiteSpace(checker.LastSkipReason)
                ? " | Reason=" + checker.LastSkipReason
                : "";

            System.Diagnostics.Debug.WriteLine("[CHECK DF/TK] #" + index + " Component=" + component);
            System.Diagnostics.Debug.WriteLine("Path=" + path);
            System.Diagnostics.Debug.WriteLine("BuhinNo=" + buhinNo);
            System.Diagnostics.Debug.WriteLine("Outer DF=" + outerDf + " | TK=" + outerTk);
            System.Diagnostics.Debug.WriteLine("Inner DF=" + innerDf + " | TK=" + innerTk);
            System.Diagnostics.Debug.WriteLine("Area DF=" + areaDf + " | TK=" + areaTk);
            System.Diagnostics.Debug.WriteLine("Result=" + status + reason);
        }

        private void AddCheckLog(List<string> checkLogs, DataGridViewRow row, object source, string reason)
        {
            if (string.Equals(reason, "Khong khac nhau", StringComparison.OrdinalIgnoreCase))
                return;

            checkLogs.Add(GetBomRowInfo(row) + " | " + GetCheckSourceText(source) + " | " + reason);
        }

        private string GetBomRowInfo(DataGridViewRow row)
        {
            string buhinNo = Convert.ToString(row.Cells[1].Value ?? "");
            string fileName = Convert.ToString(row.Cells[5].Value ?? "");
            return "GridRow=" + (row.Index + 1) + ", BuhinNo=" + buhinNo + ", FileName=" + fileName;
        }

        private string GetCheckSourceText(object source)
        {
            Component2 component = source as Component2;
            if (component != null)
            {
                try
                {
                    return "Component=" + component.Name2 + ", Path=" + component.GetPathName();
                }
                catch
                {
                    return "Component";
                }
            }

            return Convert.ToString(source ?? "");
        }

        private string GetCheckSourcePath(object source)
        {
            Component2 component = source as Component2;
            if (component != null)
            {
                try
                {
                    return component.GetPathName();
                }
                catch
                {
                    return "";
                }
            }

            return Convert.ToString(source ?? "");
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
                    assembly.ResolveAllLightWeightComponents(true);
            }
            catch
            {
            }
        }

        private void ResolveBomAssemblyLightweight()
        {
            foreach (DataGridViewRow row in gridBom.Rows)
            {
                if (row.IsNewRow)
                    continue;

                Component2 component = GetFirstComponentFromRow(row);
                if (ResolveOwningAssemblyLightweight(component))
                    return;
            }
        }

        private Component2 GetFirstComponentFromRow(DataGridViewRow row)
        {
            object[] components = row.Tag as object[];
            if (components != null)
            {
                foreach (object item in components)
                {
                    Component2 component = item as Component2;
                    if (component != null)
                        return component;
                }
            }

            return row.Tag as Component2;
        }

        private bool ResolveOwningAssemblyLightweight(Component2 component)
        {
            AssemblyDoc assembly = GetOwningAssembly(component);
            if (assembly == null)
                return false;

            try
            {
                assembly.ResolveAllLightWeightComponents(true);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private AssemblyDoc GetOwningAssembly(Component2 component)
        {
            if (component == null)
                return null;

            try
            {
                Component2 root = component;
                Component2 parent = component.GetParent();

                while (parent != null)
                {
                    root = parent;
                    parent = parent.GetParent();
                }

                return root.GetModelDoc2() as AssemblyDoc;
            }
            catch
            {
                return null;
            }
        }

        private void AddUniqueResults(List<DfTkResult> target, IEnumerable<DfTkResult> source)
        {
            if (source == null)
                return;

            HashSet<string> keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (DfTkResult existing in target)
                keys.Add(GetResultKey(existing));

            foreach (DfTkResult result in source)
            {
                string key = GetResultKey(result);
                if (keys.Contains(key))
                    continue;

                keys.Add(key);
                target.Add(result);
            }
        }

        private string GetResultKey(DfTkResult result)
        {
            if (result == null)
                return "";

            return NormalizeKey(result.BuhinNo)
                + "|" + NormalizeKey(result.OuterDf)
                + "|" + NormalizeKey(result.OuterTk)
                + "|" + NormalizeKey(result.InnerDf)
                + "|" + NormalizeKey(result.InnerTk)
                + "|" + result.AreaDf.ToString("0.0")
                + "|" + result.AreaTk.ToString("0.0")
                + "|" + NormalizeKey(result.DiffText);
        }

        private string NormalizeKey(string value)
        {
            return (value ?? "").Trim().ToUpperInvariant();
        }
    }

    public class KetQuaSoSanhDfTk
    {
        public KetQuaSoSanhDfTk()
        {
            DiffResults = new List<DfTkResult>();
            CheckLogs = new List<string>();
            HighlightRowIndexes = new HashSet<int>();
        }

        public int CheckedCount { get; set; }
        public int ProcessedCount { get; set; }
        public int SkippedCount { get; set; }
        public bool Canceled { get; set; }
        public List<DfTkResult> DiffResults { get; private set; }
        public List<string> CheckLogs { get; private set; }
        public HashSet<int> HighlightRowIndexes { get; private set; }
    }

    public class DfTkCheckerFromCustomBOM
    {
        private readonly ISldWorks swApp;
        public string LastSkipReason { get; private set; }
        public DfTkResult LastCheckedResult { get; private set; }

        public DfTkCheckerFromCustomBOM(ISldWorks app)
        {
            swApp = app;
        }

        public DfTkResult CheckComponent(Component2 component)
        {
            return CheckComponent(component, null);
        }

        public DfTkResult CheckComponent(Component2 component, string componentName)
        {
            LastSkipReason = "";
            LastCheckedResult = null;

            if (component == null)
            {
                LastSkipReason = "Component null";
                return null;
            }

            ModelDoc2 model = component.GetModelDoc2() as ModelDoc2;
            if (model != null)
                return CheckModel(model, false, componentName ?? component.Name2);

            string path = component.GetPathName();
            if (string.IsNullOrWhiteSpace(path))
            {
                LastSkipReason = "Khong lay duoc path tu component";
                return null;
            }

            return CheckPart(path);
        }

        public List<DfTkResult> CheckAssemblySelected(
            AssemblyDoc assembly,
            HashSet<string> selectedBuhinNos,
            HashSet<string> selectedFileNames,
            Func<bool> isCancellationRequested)
        {
            List<DfTkResult> results = new List<DfTkResult>();
            HashSet<int> processedModels = new HashSet<int>();

            TraverseAssembly(assembly, selectedBuhinNos, selectedFileNames, processedModels, results, isCancellationRequested);

            return results;
        }

        private void TraverseAssembly(
            AssemblyDoc assembly,
            HashSet<string> selectedBuhinNos,
            HashSet<string> selectedFileNames,
            HashSet<int> processedModels,
            List<DfTkResult> results,
            Func<bool> isCancellationRequested)
        {
            if (assembly == null)
                return;

            object[] components = assembly.GetComponents(true) as object[];
            if (components == null)
                return;

            foreach (object item in components)
            {
                Application.DoEvents();
                if (isCancellationRequested != null && isCancellationRequested())
                    return;

                Component2 component = item as Component2;
                if (component == null)
                    continue;

                try
                {
                    if (component.IsEnvelope())
                        continue;

                    if (component.ExcludeFromBOM)
                        continue;

                    if (component.IsSuppressed())
                        continue;

                    if (component.IsHidden(false))
                        continue;
                }
                catch
                {
                    continue;
                }

                ModelDoc2 model = component.GetModelDoc2() as ModelDoc2;
                if (model == null)
                {
                    DfTkResult openedResult = CheckPartIfSelected(component, selectedFileNames);
                    if (openedResult != null)
                        results.Add(openedResult);

                    continue;
                }

                if (model.GetType() == (int)swDocumentTypes_e.swDocASSEMBLY)
                {
                    TraverseAssembly(model as AssemblyDoc, selectedBuhinNos, selectedFileNames, processedModels, results, isCancellationRequested);
                    continue;
                }

                if (model.GetType() != (int)swDocumentTypes_e.swDocPART)
                    continue;

                if (!IsSelectedComponent(component, model, selectedBuhinNos, selectedFileNames))
                    continue;

                int modelKey = RuntimeHelpers.GetHashCode(model);
                if (processedModels.Contains(modelKey))
                    continue;

                processedModels.Add(modelKey);

                DfTkResult result = CheckModel(model, false, component.Name2);
                if (result != null)
                    results.Add(result);
            }
        }

        private DfTkResult CheckPartIfSelected(Component2 component, HashSet<string> selectedFileNames)
        {
            string path = component.GetPathName();
            if (string.IsNullOrWhiteSpace(path))
                return null;

            if (!IsSelectedByFileName(path, component.Name2, selectedFileNames))
                return null;

            DfTkResult result = CheckPart(path);
            if (result != null)
                result.Component = component.Name2;

            return result;
        }

        private bool IsSelectedComponent(
            Component2 component,
            ModelDoc2 model,
            HashSet<string> selectedBuhinNos,
            HashSet<string> selectedFileNames)
        {
            string buhinNo = GetCustomProperty(model, "", "部品番号");
            if (!string.IsNullOrWhiteSpace(buhinNo) && selectedBuhinNos.Contains(NormalizeKey(buhinNo)))
                return true;

            return IsSelectedByFileName(component.GetPathName(), component.Name2, selectedFileNames);
        }

        private bool IsSelectedByFileName(string path, string componentName, HashSet<string> selectedFileNames)
        {
            string fileName = "";
            if (!string.IsNullOrWhiteSpace(path))
                fileName = Path.GetFileNameWithoutExtension(path);

            if (!string.IsNullOrWhiteSpace(fileName) && selectedFileNames.Contains(NormalizeKey(fileName)))
                return true;

            if (!string.IsNullOrWhiteSpace(componentName) && selectedFileNames.Contains(NormalizeKey(componentName)))
                return true;

            return false;
        }

        private string NormalizeKey(string value)
        {
            return (value ?? "").Trim().ToUpperInvariant();
        }

        public DfTkResult CheckPart(string partPath)
        {
            LastSkipReason = "";
            LastCheckedResult = null;

            if (string.IsNullOrWhiteSpace(partPath))
            {
                LastSkipReason = "Part path rong";
                return null;
            }

            int errors = 0;
            int warnings = 0;
            bool openedByChecker = false;
            bool restorePartVisibility = false;

            ModelDoc2 swPart = swApp.GetOpenDocumentByName(partPath) as ModelDoc2;

            if (swPart == null)
            {
                swApp.DocumentVisible(false, (int)swDocumentTypes_e.swDocPART);
                restorePartVisibility = true;

                swPart = swApp.OpenDoc6(
                    partPath,
                    (int)swDocumentTypes_e.swDocPART,
                    (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
                    "",
                    ref errors,
                    ref warnings
                ) as ModelDoc2;

                openedByChecker = swPart != null;
            }

            if (restorePartVisibility)
                swApp.DocumentVisible(true, (int)swDocumentTypes_e.swDocPART);

            if (swPart == null)
            {
                LastSkipReason = "Khong mo duoc part. errors=" + errors + ", warnings=" + warnings;
                return null;
            }

            return CheckModel(swPart, openedByChecker, null);
        }

        private DfTkResult CheckModel(ModelDoc2 swPart, bool closeAfterCheck, string componentName)
        {
            string originalConfig = swPart.ConfigurationManager.ActiveConfiguration.Name;

            try
            {
                string[] confNames = swPart.GetConfigurationNames() as string[];
                if (confNames == null || confNames.Length == 0)
                {
                    LastSkipReason = "Khong co configuration";
                    return null;
                }

                if (FindFlatPatternFeature(swPart) == null)
                {
                    LastSkipReason = "Khong tim thay FlatPattern";
                    return null;
                }

                string confDef = confNames[0];
                string confFlat = confDef + "SM-FLAT-PATTERN";

                string buhinNo = GetCustomProperty(swPart, "", "部品番号");

                swPart.ShowConfiguration2(confDef);
                swPart.EditRebuild3();

                double areaDf = Math.Round(GetFlatAreaFromDefault(swPart, confDef) * 1000000.0, 1);

                string outerDf;
                string innerDf;
                GetCutListValues(swPart, out outerDf, out innerDf);

                double areaTk = 0;
                string outerTk = "";
                string innerTk = "";

                if (HasConfiguration(swPart, confFlat))
                {
                    swPart.ShowConfiguration2(confFlat);
                    swPart.EditRebuild3();

                    areaTk = Math.Round(GetFlatAreaFromFlatConfig(swPart, confFlat) * 1000000.0, 1);
                    GetCutListValues(swPart, out outerTk, out innerTk);
                }
                else
                {
                    LastSkipReason = "Khong co flat config: " + confFlat;
                }

                bool diffOuter = outerDf != outerTk;
                bool diffInner = innerDf != innerTk;
                bool diffArea = areaDf != areaTk;

                DfTkResult checkedResult = new DfTkResult
                {
                    Component = string.IsNullOrWhiteSpace(componentName) ? swPart.GetTitle() : componentName,
                    PartPath = swPart.GetPathName(),
                    BuhinNo = buhinNo,
                    OuterDf = outerDf,
                    OuterTk = outerTk,
                    InnerDf = innerDf,
                    InnerTk = innerTk,
                    AreaDf = areaDf,
                    AreaTk = areaTk,
                    DiffOuter = diffOuter,
                    DiffInner = diffInner,
                    DiffArea = diffArea
                };

                LastCheckedResult = checkedResult;

                if (!diffOuter && !diffInner && !diffArea)
                {
                    LastSkipReason = "Khong khac nhau";
                    return null;
                }

                string diffText = "";

                if (diffOuter)
                    diffText = "外側";

                if (diffInner)
                    diffText += diffText == "" ? "内側" : ", 内側";

                if (diffArea)
                    diffText += diffText == "" ? "表面積" : ", 表面積";

                checkedResult.DiffText = diffText;
                return checkedResult;
            }
            finally
            {
                try
                {
                    swPart.ShowConfiguration2(originalConfig);
                    swPart.EditRebuild3();
                }
                catch
                {
                }

                if (closeAfterCheck)
                {
                    try
                    {
                        swApp.CloseDoc(swPart.GetTitle());
                    }
                    catch
                    {
                    }
                }
            }
        }

        private bool HasConfiguration(ModelDoc2 swPart, string confName)
        {
            string[] confs = swPart.GetConfigurationNames() as string[];
            if (confs == null)
                return false;

            foreach (string conf in confs)
            {
                if (string.Equals(conf, confName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private string GetCustomProperty(ModelDoc2 model, string configName, string propName)
        {
            CustomPropertyManager propMgr = model.Extension.get_CustomPropertyManager(configName);

            string valOut;
            string resolvedVal;
            bool wasResolved;
            bool linkToProp;

            propMgr.Get6(propName, true, out valOut, out resolvedVal, out wasResolved, out linkToProp);

            return resolvedVal;
        }

        private void GetCutListValues(ModelDoc2 swPart, out string outer, out string inner)
        {
            outer = "";
            inner = "";

            Feature feat = swPart.FirstFeature() as Feature;

            while (feat != null)
            {
                if (feat.GetTypeName2() == "CutListFolder")
                {
                    BodyFolder bodyFolder = feat.GetSpecificFeature2() as BodyFolder;
                    if (bodyFolder != null)
                        bodyFolder.UpdateCutList();

                    CustomPropertyManager propMgr = feat.CustomPropertyManager;

                    outer = GetCutListProperty(propMgr, "ｶｯﾄ ｱｳﾄ長さ-外側");
                    inner = GetCutListProperty(propMgr, "ｶｯﾄ ｱｳﾄ長さ-内側");

                    return;
                }

                feat = feat.GetNextFeature() as Feature;
            }
        }

        private string GetCutListProperty(CustomPropertyManager propMgr, string propName)
        {
            string valOut;
            string resolvedVal;
            bool wasResolved;
            bool linkToProp;

            propMgr.Get6(propName, true, out valOut, out resolvedVal, out wasResolved, out linkToProp);

            return resolvedVal;
        }

        private Feature FindFlatPatternFeature(ModelDoc2 swPart)
        {
            Feature feat = swPart.FirstFeature() as Feature;

            while (feat != null)
            {
                if (feat.GetTypeName2() == "FlatPattern")
                    return feat;

                feat = feat.GetNextFeature() as Feature;
            }

            return null;
        }

        private double GetSurfaceArea(ModelDoc2 swPart)
        {
            ModelDocExtension ext = swPart.Extension;
            MassProperty mass = ext.CreateMassProperty();

            PartDoc part = swPart as PartDoc;
            if (part == null)
                return 0;

            object bodies = part.GetBodies2((int)swBodyType_e.swSolidBody, true);
            if (bodies == null)
                return 0;

            mass.AddBodies(bodies);

            return mass.SurfaceArea;
        }

        private double GetFlatAreaFromDefault(ModelDoc2 swPart, string confDef)
        {
            Feature flatFeat = FindFlatPatternFeature(swPart);
            if (flatFeat == null)
                return 0;

            swPart.ShowConfiguration2(confDef);

            flatFeat.SetSuppression2(
                (int)swFeatureSuppressionAction_e.swUnSuppressFeature,
                (int)swInConfigurationOpts_e.swThisConfiguration,
                null
            );

            swPart.ForceRebuild3(false);
            swPart.EditRebuild3();

            double area = GetSurfaceArea(swPart);

            flatFeat.SetSuppression2(
                (int)swFeatureSuppressionAction_e.swSuppressFeature,
                (int)swInConfigurationOpts_e.swThisConfiguration,
                null
            );

            swPart.ForceRebuild3(false);
            swPart.EditRebuild3();

            return area;
        }

        private double GetFlatAreaFromFlatConfig(ModelDoc2 swPart, string confFlat)
        {
            Feature flatFeat = FindFlatPatternFeature(swPart);
            if (flatFeat == null)
                return 0;

            swPart.ShowConfiguration2(confFlat);

            flatFeat.SetSuppression2(
                (int)swFeatureSuppressionAction_e.swUnSuppressFeature,
                (int)swInConfigurationOpts_e.swThisConfiguration,
                null
            );

            swPart.ForceRebuild3(false);
            swPart.EditRebuild3();

            return GetSurfaceArea(swPart);
        }
    }

    public static class ExcelDfTkExporter
    {
        public static void Export(List<DfTkResult> results, List<string> checkLogs)
        {
            try
            {
                Type excelType = Type.GetTypeFromProgID("Excel.Application");
                if (excelType == null)
                {
                    MessageBox.Show("Khong tim thay Microsoft Excel.", "Xuat ket qua", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                dynamic xlApp = Activator.CreateInstance(excelType);
                dynamic xlWB = xlApp.Workbooks.Add();
                dynamic xlWS = xlWB.Sheets[1];

                WriteResultHeader(xlWS);
                WriteResults(xlWS, results);

                int lastRow = results.Count + 1;
                if (lastRow > 1)
                    TrySortExcelByBuhinNo(xlWS, lastRow);

                xlWS.Columns.AutoFit();
                WriteCheckLog(xlWB, xlWS, checkLogs);

                xlApp.Visible = true;
                MessageBox.Show("Da so sanh xong. Co chi tiet khac nhau.", "Ket qua kiem tra", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Loi xuat Excel: " + ex.Message, "Xuat ket qua", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void WriteResultHeader(dynamic xlWS)
        {
            xlWS.Cells[1, 1].Value = "部品番号";
            xlWS.Cells[1, 2].Value = "Component";
            xlWS.Cells[1, 3].Value = "外側-DF";
            xlWS.Cells[1, 4].Value = "外側-TK";
            xlWS.Cells[1, 5].Value = "内側-DF";
            xlWS.Cells[1, 6].Value = "内側-TK";
            xlWS.Cells[1, 7].Value = "表面積 DF (mm2)";
            xlWS.Cells[1, 8].Value = "表面積 TK (mm2)";
            xlWS.Cells[1, 9].Value = "Status";
            xlWS.Cells[1, 10].Value = "Note";
        }

        private static void WriteResults(dynamic xlWS, List<DfTkResult> results)
        {
            int xlRow = 2;
            foreach (DfTkResult result in results)
            {
                xlWS.Cells[xlRow, 1].Value = result.BuhinNo;
                xlWS.Cells[xlRow, 2].Value = result.Component;
                xlWS.Cells[xlRow, 3].Value = result.OuterDf;
                xlWS.Cells[xlRow, 4].Value = result.OuterTk;
                xlWS.Cells[xlRow, 5].Value = result.InnerDf;
                xlWS.Cells[xlRow, 6].Value = result.InnerTk;
                xlWS.Cells[xlRow, 7].Value = result.AreaDf;
                xlWS.Cells[xlRow, 8].Value = result.AreaTk;
                xlWS.Cells[xlRow, 9].Value = "NG";
                xlWS.Cells[xlRow, 10].Value = result.DiffText;

                int yellow = Rgb(255, 255, 153);

                if (result.DiffOuter)
                {
                    xlWS.Cells[xlRow, 3].Interior.Color = yellow;
                    xlWS.Cells[xlRow, 4].Interior.Color = yellow;
                }

                if (result.DiffInner)
                {
                    xlWS.Cells[xlRow, 5].Interior.Color = yellow;
                    xlWS.Cells[xlRow, 6].Interior.Color = yellow;
                }

                if (result.DiffArea)
                {
                    xlWS.Cells[xlRow, 7].Interior.Color = yellow;
                    xlWS.Cells[xlRow, 8].Interior.Color = yellow;
                }

                xlRow++;
            }
        }

        private static void WriteCheckLog(dynamic xlWB, dynamic xlWS, List<string> checkLogs)
        {
            if (checkLogs == null || checkLogs.Count == 0)
                return;

            dynamic logSheet = xlWB.Sheets.Add(After: xlWS);
            logSheet.Name = "Check Log";
            logSheet.Cells[1, 1].Value = "Log";

            for (int i = 0; i < checkLogs.Count; i++)
            {
                logSheet.Cells[i + 2, 1].Value = checkLogs[i];
            }

            logSheet.Columns.AutoFit();
        }

        private static void TrySortExcelByBuhinNo(dynamic xlWS, int lastRow)
        {
            try
            {
                dynamic sort = xlWS.Sort;
                sort.SortFields.Clear();
                sort.SortFields.Add(xlWS.Range["A2:A" + lastRow], 0, 1);
                sort.SetRange(xlWS.Range["A1:J" + lastRow]);
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

    public class DfTkResult
    {
        public string Component { get; set; }
        public string PartPath { get; set; }
        public string BuhinNo { get; set; }

        public string OuterDf { get; set; }
        public string OuterTk { get; set; }

        public string InnerDf { get; set; }
        public string InnerTk { get; set; }

        public double AreaDf { get; set; }
        public double AreaTk { get; set; }

        public string DiffText { get; set; }

        public bool DiffOuter { get; set; }
        public bool DiffInner { get; set; }
        public bool DiffArea { get; set; }
    }
}
