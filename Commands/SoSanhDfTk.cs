using System;
using System.Collections.Generic;
using System.Globalization;
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

                return results;
            }

            AddCheckLog(
                checkLogs,
                row,
                "",
                "Grid khong co Component hoac Part path");

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
            if (debugResult != null)
            {
                System.Diagnostics.Debug.WriteLine(
                    "Geometry DF=" + debugResult.GeometryDfSummary
                    + " | TK=" + debugResult.GeometryTkSummary);
                System.Diagnostics.Debug.WriteLine(
                    "Geometry Compared=" + debugResult.GeometryCompared
                    + " | Different=" + debugResult.DiffGeometry
                    + " | Detail=" + debugResult.GeometryNote);
            }
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
            ModelDoc2 activeDocBeforeCheck = swApp.ActiveDoc as ModelDoc2;
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

                if (!HasConfiguration(swPart, confFlat))
                {
                    LastSkipReason = "Khong co flat config: " + confFlat;
                    return null;
                }

                // Capture the existing Flat-Pattern state first. Do not rebuild,
                // unsuppress, or update its Cut List before taking this snapshot;
                // otherwise SOLIDWORKS can refresh the stale flat geometry and
                // hide the difference that this command is intended to detect.
                string flatActivationError;
                if (!TryShowConfiguration(swPart, confFlat, out flatActivationError))
                {
                    LastSkipReason = "Khong chuyen duoc sang flat config: " + confFlat
                        + ". " + flatActivationError;
                    return null;
                }

                double areaTk = Math.Round(GetSurfaceArea(swPart) * 1000000.0, 1);
                GeometrySignature geometryTk = CaptureGeometrySignature(swPart);
                string outerTk;
                string innerTk;
                GetCutListValues(swPart, false, out outerTk, out innerTk);

                // DF is rebuilt only in a disposable copy. Never toggle or
                // rebuild FlatPattern in the original document because doing so
                // can refresh the saved TK/cut-list state that we need to check.
                GeometrySignature geometryDf;
                double areaDf;
                string outerDf;
                string innerDf;
                string dfCaptureError;
                if (!TryCaptureDfFromTemporaryCopy(
                    swPart,
                    confDef,
                    out areaDf,
                    out geometryDf,
                    out outerDf,
                    out innerDf,
                    out dfCaptureError))
                {
                    LastSkipReason = "Khong doc duoc DF tu ban sao tam. " + dfCaptureError;
                    return null;
                }

                bool diffOuter = outerDf != outerTk;
                bool diffInner = innerDf != innerTk;
                bool diffArea = areaDf != areaTk;
                GeometryComparison geometryComparison = CompareGeometry(geometryDf, geometryTk);
                bool diffGeometry = geometryComparison.Compared && geometryComparison.IsDifferent;

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
                    DiffArea = diffArea,
                    DiffGeometry = diffGeometry,
                    GeometryCompared = geometryComparison.Compared,
                    GeometryDfSummary = geometryDf.Summary,
                    GeometryTkSummary = geometryTk.Summary,
                    GeometryNote = geometryComparison.Detail
                };

                LastCheckedResult = checkedResult;

                if (!diffOuter && !diffInner && !diffArea && !diffGeometry)
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

                if (diffGeometry)
                    diffText += diffText == ""
                        ? "SAI VỊ TRÍ HÌNH HỌC"
                        : ", SAI VỊ TRÍ HÌNH HỌC";

                if (diffGeometry && !string.IsNullOrWhiteSpace(geometryComparison.Detail))
                    diffText += " - " + geometryComparison.Detail;

                checkedResult.DiffText = diffText;
                return checkedResult;
            }
            finally
            {
                if (closeAfterCheck)
                {
                    try
                    {
                        // This document was opened silently by the checker. Close
                        // it immediately without saving; restoring its display
                        // configuration first would only add another activation.
                        swApp.CloseDoc(swPart.GetTitle());
                    }
                    catch
                    {
                    }
                }
                else
                {
                    try
                    {
                        // Never close a document that was already loaded by the
                        // user or by the assembly. Return only its configuration.
                        swPart.ShowConfiguration2(originalConfig);
                    }
                    catch
                    {
                    }
                }

                RestoreActiveDocument(activeDocBeforeCheck);
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

        private bool TryShowConfiguration(
            ModelDoc2 swPart,
            string confName,
            out string failureDetail)
        {
            failureDetail = "";

            bool showResult = false;
            string firstError = "";

            try
            {
                showResult = swPart.ShowConfiguration2(confName);
            }
            catch (Exception ex)
            {
                firstError = ex.Message;
            }

            // ShowConfiguration2 can return false for a model document that is
            // loaded only through an assembly. Verify the actual active
            // configuration before treating that return value as a failure.
            if (IsConfigurationActive(swPart, confName))
                return true;

            int activateErrors = 0;

            try
            {
                string activationName = GetDocumentActivationName(swPart);
                ModelDoc2 activatedPart = swApp.ActivateDoc3(
                    activationName,
                    false,
                    (int)swRebuildOnActivation_e.swDontRebuildActiveDoc,
                    ref activateErrors
                ) as ModelDoc2;

                if (activatedPart != null)
                    swPart = activatedPart;

                showResult = swPart.ShowConfiguration2(confName);

                if (IsConfigurationActive(swPart, confName))
                    return true;
            }
            catch (Exception ex)
            {
                failureDetail = "ActivateDoc3 error=" + activateErrors + ", exception=" + ex.Message;
                return false;
            }

            string activeConfig = GetActiveConfigurationName(swPart);
            failureDetail = "ShowConfiguration2=" + showResult
                + ", ActivateDoc3 error=" + activateErrors
                + ", active=" + (string.IsNullOrWhiteSpace(activeConfig) ? "<none>" : activeConfig);

            if (!string.IsNullOrWhiteSpace(firstError))
                failureDetail += ", first exception=" + firstError;

            return false;
        }

        private bool IsConfigurationActive(ModelDoc2 swPart, string confName)
        {
            return string.Equals(
                GetActiveConfigurationName(swPart),
                confName,
                StringComparison.OrdinalIgnoreCase);
        }

        private string GetActiveConfigurationName(ModelDoc2 swPart)
        {
            try
            {
                Configuration activeConfiguration = swPart.ConfigurationManager.ActiveConfiguration;
                return activeConfiguration == null ? "" : activeConfiguration.Name;
            }
            catch
            {
                return "";
            }
        }

        private string GetDocumentActivationName(ModelDoc2 swPart)
        {
            string path = swPart.GetPathName();
            if (!string.IsNullOrWhiteSpace(path))
                return Path.GetFileName(path);

            return swPart.GetTitle();
        }

        private void RestoreActiveDocument(ModelDoc2 document)
        {
            if (document == null)
                return;

            try
            {
                ModelDoc2 current = swApp.ActiveDoc as ModelDoc2;
                if (current != null
                    && string.Equals(
                        GetDocumentActivationName(current),
                        GetDocumentActivationName(document),
                        StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                int activateErrors = 0;
                swApp.ActivateDoc3(
                    GetDocumentActivationName(document),
                    false,
                    (int)swRebuildOnActivation_e.swDontRebuildActiveDoc,
                    ref activateErrors
                );
            }
            catch
            {
            }
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

        private void GetCutListValues(
            ModelDoc2 swPart,
            bool updateCutList,
            out string outer,
            out string inner)
        {
            outer = "";
            inner = "";

            Feature feat = swPart.FirstFeature() as Feature;

            while (feat != null)
            {
                if (feat.GetTypeName2() == "CutListFolder")
                {
                    BodyFolder bodyFolder = feat.GetSpecificFeature2() as BodyFolder;
                    if (updateCutList && bodyFolder != null)
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

        private bool TryCaptureDfFromTemporaryCopy(
            ModelDoc2 sourcePart,
            string confDef,
            out double areaDf,
            out GeometrySignature geometryDf,
            out string outerDf,
            out string innerDf,
            out string failureDetail)
        {
            areaDf = 0.0;
            geometryDf = GeometrySignature.Invalid("Chua doc hinh hoc DF");
            outerDf = "";
            innerDf = "";
            failureDetail = "";

            if (sourcePart == null)
            {
                failureDetail = "Part nguon null";
                return false;
            }

            string sourcePath = sourcePart.GetPathName();
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                failureDetail = "Part chua duoc luu hoac path khong ton tai: " + sourcePath;
                return false;
            }

            string tempId = Guid.NewGuid().ToString("N");
            string tempDirectory = Path.Combine(
                Path.GetTempPath(),
                "TAI_TOOL_DFTK_" + tempId);
            string tempPartPath = Path.Combine(
                tempDirectory,
                Path.GetFileNameWithoutExtension(sourcePath)
                    + "_DFTK_" + tempId.Substring(0, 8)
                    + Path.GetExtension(sourcePath));
            ModelDoc2 tempPart = null;
            ModelDoc2 activeDocBeforeTemp = swApp.ActiveDoc as ModelDoc2;
            bool partVisibilityChanged = false;

            try
            {
                Directory.CreateDirectory(tempDirectory);
                File.Copy(sourcePath, tempPartPath, true);

                int errors = 0;
                int warnings = 0;
                swApp.DocumentVisible(false, (int)swDocumentTypes_e.swDocPART);
                partVisibilityChanged = true;
                tempPart = swApp.OpenDoc6(
                    tempPartPath,
                    (int)swDocumentTypes_e.swDocPART,
                    (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
                    "",
                    ref errors,
                    ref warnings) as ModelDoc2;

                if (tempPart == null)
                {
                    failureDetail = "Khong mo duoc ban sao tam. errors="
                        + errors + ", warnings=" + warnings;
                    return false;
                }

                if (!HasConfiguration(tempPart, confDef))
                {
                    failureDetail = "Ban sao tam khong co configuration: " + confDef;
                    return false;
                }

                string activationError;
                if (!TryShowConfiguration(tempPart, confDef, out activationError))
                {
                    failureDetail = "Khong chuyen duoc ban sao tam sang configuration "
                        + confDef + ". " + activationError;
                    return false;
                }

                Feature flatFeature = FindFlatPatternFeature(tempPart);
                if (flatFeature == null)
                {
                    failureDetail = "Ban sao tam khong co FlatPattern";
                    return false;
                }

                flatFeature.SetSuppression2(
                    (int)swFeatureSuppressionAction_e.swUnSuppressFeature,
                    (int)swInConfigurationOpts_e.swThisConfiguration,
                    null);

                // This is the only explicit rebuild in DF/TK. It runs in the
                // temporary file, which is always closed without saving.
                tempPart.EditRebuild3();

                areaDf = Math.Round(GetSurfaceArea(tempPart) * 1000000.0, 1);
                geometryDf = CaptureGeometrySignature(tempPart);
                GetCutListValues(tempPart, true, out outerDf, out innerDf);

                System.Diagnostics.Debug.WriteLine(
                    "[CHECK DF/TK] DF captured from temporary copy. Source="
                    + sourcePath + ", Temp=" + tempPartPath
                    + ", Area=" + areaDf.ToString("0.0")
                    + ", Outer=" + outerDf + ", Inner=" + innerDf);
                return true;
            }
            catch (Exception ex)
            {
                failureDetail = ex.Message;
                return false;
            }
            finally
            {
                if (partVisibilityChanged)
                {
                    try
                    {
                        swApp.DocumentVisible(true, (int)swDocumentTypes_e.swDocPART);
                    }
                    catch
                    {
                    }
                }

                if (tempPart != null)
                {
                    try
                    {
                        swApp.CloseDoc(tempPart.GetTitle());
                    }
                    catch
                    {
                    }
                }

                RestoreActiveDocument(activeDocBeforeTemp);

                try
                {
                    if (Directory.Exists(tempDirectory))
                        Directory.Delete(tempDirectory, true);
                }
                catch (Exception cleanupException)
                {
                    System.Diagnostics.Debug.WriteLine(
                        "[CHECK DF/TK] Khong xoa duoc thu muc tam: "
                        + tempDirectory + ". " + cleanupException.Message);
                }
            }
        }

        // Legacy implementation retained for quick rollback. The active logic
        // above no longer calls this method on the original document.
        private double GetFlatAreaFromDefault(
            ModelDoc2 swPart,
            string confDef,
            out GeometrySignature geometrySignature)
        {
            geometrySignature = GeometrySignature.Invalid("Chua doc hinh hoc DF");
            Feature flatFeat = FindFlatPatternFeature(swPart);
            if (flatFeat == null)
                return 0;

            swPart.ShowConfiguration2(confDef);

            flatFeat.SetSuppression2(
                (int)swFeatureSuppressionAction_e.swUnSuppressFeature,
                (int)swInConfigurationOpts_e.swThisConfiguration,
                null
            );

            swPart.EditRebuild3();

            double area = GetSurfaceArea(swPart);
            geometrySignature = CaptureGeometrySignature(swPart);

            flatFeat.SetSuppression2(
                (int)swFeatureSuppressionAction_e.swSuppressFeature,
                (int)swInConfigurationOpts_e.swThisConfiguration,
                null
            );

            swPart.EditRebuild3();

            return area;
        }

        private GeometrySignature CaptureGeometrySignature(ModelDoc2 swPart)
        {
            try
            {
                PartDoc part = swPart as PartDoc;
                if (part == null)
                    return GeometrySignature.Invalid("Khong phai Part");

                object[] bodies = part.GetBodies2(
                    (int)swBodyType_e.swSolidBody,
                    true) as object[];

                if (bodies == null || bodies.Length == 0)
                    return GeometrySignature.Invalid("Khong co solid body");

                Face2 largestPlanarFace = null;
                double largestArea = 0;

                foreach (object bodyObject in bodies)
                {
                    Body2 body = bodyObject as Body2;
                    if (body == null)
                        continue;

                    object[] faces = body.GetFaces() as object[];
                    if (faces == null)
                        continue;

                    foreach (object faceObject in faces)
                    {
                        Face2 face = faceObject as Face2;
                        if (face == null)
                            continue;

                        Surface surface = face.GetSurface() as Surface;
                        if (surface == null || !surface.IsPlane())
                            continue;

                        double area = face.GetArea();
                        if (area > largestArea)
                        {
                            largestArea = area;
                            largestPlanarFace = face;
                        }
                    }
                }

                if (largestPlanarFace == null)
                    return GeometrySignature.Invalid("Khong tim thay mat phang chinh");

                GeometrySignature signature = new GeometrySignature();
                object[] loops = largestPlanarFace.GetLoops() as object[];

                if (loops == null || loops.Length == 0)
                    return GeometrySignature.Invalid("Mat phang khong co loop");

                foreach (object loopObject in loops)
                {
                    Loop2 loop = loopObject as Loop2;
                    if (loop == null)
                        continue;

                    object[] edges = loop.GetEdges() as object[];
                    if (edges == null || edges.Length == 0)
                        continue;

                    if (loop.IsOuter())
                    {
                        AddLoopReferencePoints(edges, signature.OuterReferencePoints);
                        continue;
                    }

                    InnerLoopGeometry innerLoop = CreateInnerLoopGeometry(edges);
                    if (innerLoop != null)
                        signature.InnerLoops.Add(innerLoop);
                }

                if (signature.OuterReferencePoints.Count == 0)
                {
                    double[] box = largestPlanarFace.GetBox() as double[];
                    if (box != null && box.Length >= 6)
                    {
                        AddUniquePoint(signature.OuterReferencePoints, new Point3(box[0], box[1], box[2]));
                        AddUniquePoint(signature.OuterReferencePoints, new Point3(box[3], box[4], box[5]));
                    }
                }

                signature.BuildInvariantSeries();
                signature.Valid = true;
                signature.Summary = "InnerLoop=" + signature.InnerLoops.Count
                    + ", OuterRef=" + signature.OuterReferencePoints.Count;
                return signature;
            }
            catch (Exception ex)
            {
                return GeometrySignature.Invalid(ex.GetType().Name + ": " + ex.Message);
            }
        }

        private InnerLoopGeometry CreateInnerLoopGeometry(object[] edges)
        {
            List<Point3> edgePoints = new List<Point3>();
            List<Point3> circleCenters = new List<Point3>();
            List<double> circleRadiiMm = new List<double>();
            double perimeterMm = 0;
            bool allCircular = true;

            foreach (object edgeObject in edges)
            {
                Edge edge = edgeObject as Edge;
                if (edge == null)
                    continue;

                AddEdgeEndPoints(edge, edgePoints);

                Curve curve = edge.GetCurve() as Curve;
                if (curve == null)
                {
                    allCircular = false;
                    continue;
                }

                try
                {
                    CurveParamData parameters = edge.GetCurveParams3();
                    if (parameters != null)
                    {
                        perimeterMm += curve.GetLength3(
                            parameters.UMinValue,
                            parameters.UMaxValue) * 1000.0;
                    }
                }
                catch
                {
                }

                if (!curve.IsCircle())
                {
                    allCircular = false;
                    continue;
                }

                double[] circle = curve.CircleParams as double[];
                if (circle != null && circle.Length >= 7)
                {
                    circleCenters.Add(new Point3(circle[0], circle[1], circle[2]));
                    circleRadiiMm.Add(circle[6] * 1000.0);
                }
            }

            Point3 center;
            double radiusMm = 0;

            if (allCircular
                && circleCenters.Count > 0
                && CircleCentersAreCoincident(circleCenters))
            {
                center = AveragePoint(circleCenters);
                radiusMm = circleRadiiMm.Count == 0 ? 0 : circleRadiiMm[0];
            }
            else if (edgePoints.Count > 0)
            {
                center = AveragePoint(edgePoints);
            }
            else if (circleCenters.Count > 0)
            {
                center = AveragePoint(circleCenters);
            }
            else
            {
                return null;
            }

            return new InnerLoopGeometry
            {
                Center = center,
                PerimeterMm = perimeterMm,
                RadiusMm = radiusMm
            };
        }

        private void AddLoopReferencePoints(object[] edges, List<Point3> points)
        {
            foreach (object edgeObject in edges)
            {
                Edge edge = edgeObject as Edge;
                if (edge != null)
                    AddEdgeEndPoints(edge, points);
            }
        }

        private void AddEdgeEndPoints(Edge edge, List<Point3> points)
        {
            try
            {
                CurveParamData parameters = edge.GetCurveParams3();
                if (parameters == null)
                    return;

                AddPointArray(points, parameters.StartPoint as double[]);
                AddPointArray(points, parameters.EndPoint as double[]);
            }
            catch
            {
            }
        }

        private void AddPointArray(List<Point3> points, double[] coordinates)
        {
            if (coordinates == null || coordinates.Length < 3)
                return;

            AddUniquePoint(points, new Point3(coordinates[0], coordinates[1], coordinates[2]));
        }

        private void AddUniquePoint(List<Point3> points, Point3 point)
        {
            const double duplicateToleranceMeters = 0.000001;

            foreach (Point3 existing in points)
            {
                if (existing.DistanceTo(point) <= duplicateToleranceMeters)
                    return;
            }

            points.Add(point);
        }

        private bool CircleCentersAreCoincident(List<Point3> centers)
        {
            if (centers == null || centers.Count == 0)
                return false;

            Point3 first = centers[0];
            for (int i = 1; i < centers.Count; i++)
            {
                if (first.DistanceTo(centers[i]) > 0.00001)
                    return false;
            }

            return true;
        }

        private Point3 AveragePoint(List<Point3> points)
        {
            double x = 0;
            double y = 0;
            double z = 0;

            foreach (Point3 point in points)
            {
                x += point.X;
                y += point.Y;
                z += point.Z;
            }

            return new Point3(x / points.Count, y / points.Count, z / points.Count);
        }

        private GeometryComparison CompareGeometry(
            GeometrySignature df,
            GeometrySignature tk)
        {
            if (df == null || tk == null || !df.Valid || !tk.Valid)
            {
                string dfReason = df == null ? "null" : df.FailureReason;
                string tkReason = tk == null ? "null" : tk.FailureReason;
                return GeometryComparison.NotCompared(
                    "Khong doc duoc hinh hoc. DF=" + dfReason + ", TK=" + tkReason);
            }

            const double toleranceMm = 0.1;
            double maxDeltaMm = 0;
            string changedSeries = "";

            if (df.InnerLoops.Count != tk.InnerLoops.Count)
            {
                return GeometryComparison.Different(
                    "Số biên dạng kín bên trong DF=" + df.InnerLoops.Count
                    + ", TK=" + tk.InnerLoops.Count);
            }

            if (!CompareSortedSeries(
                df.LoopPerimetersMm,
                tk.LoopPerimetersMm,
                toleranceMm,
                ref maxDeltaMm))
            {
                changedSeries = "kích thước biên dạng kín bên trong";
            }

            double centerPairDelta = 0;
            if (!CompareSortedSeries(
                df.InnerCenterPairDistancesMm,
                tk.InnerCenterPairDistancesMm,
                toleranceMm,
                ref centerPairDelta))
            {
                changedSeries = string.IsNullOrWhiteSpace(changedSeries)
                    ? "khoảng cách giữa các biên dạng kín bên trong"
                    : changedSeries + ", khoảng cách giữa các biên dạng kín bên trong";
                maxDeltaMm = Math.Max(maxDeltaMm, centerPairDelta);
            }

            double outerReferenceDelta = 0;
            if (!CompareSortedSeries(
                df.InnerToOuterReferenceDistancesMm,
                tk.InnerToOuterReferenceDistancesMm,
                toleranceMm,
                ref outerReferenceDelta))
            {
                changedSeries = string.IsNullOrWhiteSpace(changedSeries)
                    ? "vị trí biên dạng kín bên trong so với biên ngoài"
                    : changedSeries + ", vị trí biên dạng kín bên trong so với biên ngoài";
                maxDeltaMm = Math.Max(maxDeltaMm, outerReferenceDelta);
            }

            if (!string.IsNullOrWhiteSpace(changedSeries))
            {
                return GeometryComparison.Different(
                    changedSeries + "; lệch lớn nhất "
                    + maxDeltaMm.ToString("0.###") + " mm");
            }

            return GeometryComparison.Same();
        }

        private bool CompareSortedSeries(
            List<double> first,
            List<double> second,
            double toleranceMm,
            ref double maxDeltaMm)
        {
            if (first == null || second == null || first.Count != second.Count)
            {
                maxDeltaMm = double.PositiveInfinity;
                return false;
            }

            List<double> firstSorted = new List<double>(first);
            List<double> secondSorted = new List<double>(second);
            firstSorted.Sort();
            secondSorted.Sort();

            bool same = true;
            for (int i = 0; i < firstSorted.Count; i++)
            {
                double delta = Math.Abs(firstSorted[i] - secondSorted[i]);
                maxDeltaMm = Math.Max(maxDeltaMm, delta);
                if (delta > toleranceMm)
                    same = false;
            }

            return same;
        }

        private sealed class GeometrySignature
        {
            public GeometrySignature()
            {
                InnerLoops = new List<InnerLoopGeometry>();
                OuterReferencePoints = new List<Point3>();
                LoopPerimetersMm = new List<double>();
                InnerCenterPairDistancesMm = new List<double>();
                InnerToOuterReferenceDistancesMm = new List<double>();
            }

            public bool Valid { get; set; }
            public string FailureReason { get; set; }
            public string Summary { get; set; }
            public List<InnerLoopGeometry> InnerLoops { get; private set; }
            public List<Point3> OuterReferencePoints { get; private set; }
            public List<double> LoopPerimetersMm { get; private set; }
            public List<double> InnerCenterPairDistancesMm { get; private set; }
            public List<double> InnerToOuterReferenceDistancesMm { get; private set; }

            public static GeometrySignature Invalid(string reason)
            {
                return new GeometrySignature
                {
                    Valid = false,
                    FailureReason = reason ?? ""
                };
            }

            public void BuildInvariantSeries()
            {
                foreach (InnerLoopGeometry loop in InnerLoops)
                {
                    LoopPerimetersMm.Add(loop.PerimeterMm);

                    foreach (Point3 outerPoint in OuterReferencePoints)
                    {
                        InnerToOuterReferenceDistancesMm.Add(
                            loop.Center.DistanceTo(outerPoint) * 1000.0);
                    }
                }

                for (int i = 0; i < InnerLoops.Count; i++)
                {
                    for (int j = i + 1; j < InnerLoops.Count; j++)
                    {
                        InnerCenterPairDistancesMm.Add(
                            InnerLoops[i].Center.DistanceTo(InnerLoops[j].Center) * 1000.0);
                    }
                }
            }
        }

        private sealed class InnerLoopGeometry
        {
            public Point3 Center { get; set; }
            public double PerimeterMm { get; set; }
            public double RadiusMm { get; set; }
        }

        private sealed class Point3
        {
            public Point3(double x, double y, double z)
            {
                X = x;
                Y = y;
                Z = z;
            }

            public double X { get; private set; }
            public double Y { get; private set; }
            public double Z { get; private set; }

            public double DistanceTo(Point3 other)
            {
                double dx = X - other.X;
                double dy = Y - other.Y;
                double dz = Z - other.Z;
                return Math.Sqrt(dx * dx + dy * dy + dz * dz);
            }
        }

        private sealed class GeometryComparison
        {
            public bool Compared { get; private set; }
            public bool IsDifferent { get; private set; }
            public string Detail { get; private set; }

            public static GeometryComparison NotCompared(string detail)
            {
                return new GeometryComparison { Compared = false, Detail = detail ?? "" };
            }

            public static GeometryComparison Different(string detail)
            {
                return new GeometryComparison
                {
                    Compared = true,
                    IsDifferent = true,
                    Detail = detail ?? ""
                };
            }

            public static GeometryComparison Same()
            {
                return new GeometryComparison { Compared = true, IsDifferent = false };
            }
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
                FreezeTopRow(xlWS);

                int lastRow = results.Count + 1;
                if (lastRow > 1)
                    TrySortExcelByBuhinNo(xlWS, lastRow);

                xlWS.Columns.AutoFit();
                AutoFitNoteColumn(xlWS, Math.Max(1, lastRow), 9);
                AutoFitNoteColumn(xlWS, Math.Max(1, lastRow), 11);
                WriteCheckLog(xlWB, xlWS, checkLogs);
                xlWS.Activate();
                xlWS.Range["A1"].Select();

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
            xlWS.Cells[1, 9].Value = "差分値 (DF-TK)";
            xlWS.Cells[1, 10].Value = "Status";
            xlWS.Cells[1, 11].Value = "Note";
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
                xlWS.Cells[xlRow, 9].Value = BuildDifferenceValue(result);
                xlWS.Cells[xlRow, 10].Value = "NG";
                xlWS.Cells[xlRow, 11].Value = result.DiffText;
                xlWS.Cells[xlRow, 9].WrapText = true;

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

                if (result.DiffOuter || result.DiffInner || result.DiffArea || result.DiffGeometry)
                    xlWS.Cells[xlRow, 9].Interior.Color = yellow;

                if (result.DiffGeometry)
                    xlWS.Cells[xlRow, 11].Interior.Color = yellow;

                xlRow++;
            }
        }

        private static string BuildDifferenceValue(DfTkResult result)
        {
            List<string> differences = new List<string>();

            if (result.DiffOuter)
                AddLengthDifference(differences, "外側", result.OuterDf, result.OuterTk);

            if (result.DiffInner)
                AddLengthDifference(differences, "内側", result.InnerDf, result.InnerTk);

            if (result.DiffArea)
            {
                double areaDelta = result.AreaDf - result.AreaTk;
                differences.Add("表面積: "
                    + areaDelta.ToString("0.0", CultureInfo.InvariantCulture)
                    + " mm2");
            }

            // Geometry does not have one DF/TK scalar value to subtract.
            // If this is the only mismatch, keep the geometry detail here.
            if (result.DiffGeometry && differences.Count == 0)
            {
                differences.Add("位置: "
                    + (string.IsNullOrWhiteSpace(result.GeometryNote)
                        ? "差異あり"
                        : result.GeometryNote));
            }

            return string.Join(System.Environment.NewLine, differences);
        }

        private static void AddLengthDifference(
            List<string> differences,
            string label,
            string dfText,
            string tkText)
        {
            double dfValue;
            double tkValue;

            if (TryParseMeasurement(dfText, out dfValue)
                && TryParseMeasurement(tkText, out tkValue))
            {
                double delta = dfValue - tkValue;
                differences.Add(label + ": "
                    + delta.ToString("0.###", CultureInfo.InvariantCulture)
                    + " mm");
                return;
            }

            differences.Add(label + ": DF=" + (dfText ?? "")
                + " / TK=" + (tkText ?? "")
                + " (計算不可)");
        }

        private static bool TryParseMeasurement(string text, out double value)
        {
            value = 0.0;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            string normalized = text.Trim()
                .Replace("mm²", "")
                .Replace("mm2", "")
                .Replace("mm", "")
                .Trim();

            if (double.TryParse(
                normalized,
                NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture,
                out value))
            {
                return true;
            }

            if (double.TryParse(
                normalized,
                NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.CurrentCulture,
                out value))
            {
                return true;
            }

            normalized = normalized.Replace(',', '.');
            return double.TryParse(
                normalized,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value);
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
            AutoFitNoteColumn(logSheet, checkLogs.Count + 1, 1);
            FreezeTopRow(logSheet);
        }

        private static void FreezeTopRow(dynamic sheet)
        {
            if (sheet == null)
                return;

            try
            {
                sheet.Activate();
                dynamic window = sheet.Application.ActiveWindow;
                if (window == null)
                    return;

                window.FreezePanes = false;
                window.SplitColumn = 0;
                window.SplitRow = 1;
                window.FreezePanes = true;
            }
            catch
            {
                // Freeze header is presentation only; export must still succeed.
            }
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
                sort.SetRange(xlWS.Range["A1:K" + lastRow]);
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
        public bool DiffGeometry { get; set; }
        public bool GeometryCompared { get; set; }
        public string GeometryDfSummary { get; set; }
        public string GeometryTkSummary { get; set; }
        public string GeometryNote { get; set; }
    }
}
