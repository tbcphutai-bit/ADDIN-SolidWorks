using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using ADDIN.HoleManagement;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands
{
    internal class PaintHoleSummaryCommand
    {
        private readonly ISldWorks swApp;
        private const string PaintToken = "\u5857\u88C5";
        private const string PhiToken = "\u03C6";

        public PaintHoleSummaryCommand(ISldWorks app)
        {
            swApp = app;
        }

        public void Run()
        {
            ModelDoc2 activeModel = swApp?.ActiveDoc as ModelDoc2;
            if (activeModel == null)
            {
                MessageBox.Show("Hay mo Part hoac Assembly truoc.", "Dem hole", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            bool oldCommandInProgress = false;
            try
            {
                oldCommandInProgress = swApp.CommandInProgress;
                swApp.CommandInProgress = true;
                if (!ResolveLightweightComponents(activeModel))
                    return;
                HoleMetadataService.Initialize(swApp);

                PaintHoleScanResult result = Scan(activeModel);
                if (result.TotalFeatureRows == 0)
                {
                    MessageBox.Show("Khong tim thay ho lo trong model hien tai.", "Dem hole", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                ExportToExcel(result);
                MessageBox.Show(
                    "Da thong ke hole.\nLoai hole: " + result.Summary.Count.ToString(CultureInfo.InvariantCulture) +
                    "\nTong so luong: " + result.TotalQuantity.ToString(CultureInfo.InvariantCulture) +
                    "\nChi tiet da mo bang Excel.",
                    "Dem hole",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Loi thong ke hole: " + ex.Message, "Dem hole", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                try
                {
                    swApp.CommandInProgress = oldCommandInProgress;
                }
                catch
                {
                }
            }
        }

        private bool ResolveLightweightComponents(ModelDoc2 activeModel)
        {
            if (activeModel.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
                return true;

            AssemblyDoc assembly = activeModel as AssemblyDoc;
            if (assembly == null)
                return true;

            try
            {
                int before = assembly.GetLightWeightComponentCount();
                int unavailableBefore = CountUnavailableComponentModels(assembly);
                if (before <= 0 && unavailableBefore == 0)
                    return true;

                Debug.WriteLine("[PAINT HOLE] Resolve before scan. lightweight=" + before +
                    ", unavailableModels=" + unavailableBefore);
                assembly.ResolveAllLightWeightComponents(false);

                int after = assembly.GetLightWeightComponentCount();
                int unavailableAfter = CountUnavailableComponentModels(assembly);
                Debug.WriteLine("[PAINT HOLE] Resolve result. lightweight=" + after +
                    ", unavailableModels=" + unavailableAfter);
                if (after > 0)
                {
                    MessageBox.Show(
                        "Con component Lightweight. Hay Resolve chung roi chay lai DEM HOLE.",
                        "Dem hole",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[PAINT HOLE] Resolve lightweight failed: " + ex.Message);
                MessageBox.Show(
                    "Khong the bo che do Lightweight: " + ex.Message,
                    "Dem hole",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return false;
            }
        }

        private int CountUnavailableComponentModels(AssemblyDoc assembly)
        {
            int count = 0;
            object[] components = assembly.GetComponents(false) as object[];
            if (components == null) return count;
            foreach (object item in components)
            {
                Component2 component = item as Component2;
                if (component == null) continue;
                try
                {
                    if (!component.IsSuppressed() && component.GetModelDoc2() == null) count++;
                }
                catch { count++; }
            }
            return count;
        }

        private PaintHoleScanResult Scan(ModelDoc2 activeModel)
        {
            PaintHoleScanResult result = new PaintHoleScanResult();
            Dictionary<string, List<HoleRecord>> cache = new Dictionary<string, List<HoleRecord>>(StringComparer.OrdinalIgnoreCase);

            int docType = activeModel.GetType();
            if (docType == (int)swDocumentTypes_e.swDocPART)
            {
                foreach (HoleRecord record in ScanPart(activeModel, "", 1, GetActiveConfigurationName(activeModel)))
                    AddRecord(result, record);
                return result;
            }

            if (docType != (int)swDocumentTypes_e.swDocASSEMBLY)
                return result;

            AssemblyDoc assembly = activeModel as AssemblyDoc;
            object[] components = assembly?.GetComponents(false) as object[];
            if (components == null)
                return result;

            Debug.WriteLine("[PAINT HOLE] Assembly scan all levels. componentOccurrences=" + components.Length);
            int scannedPartOccurrences = 0;
            int unavailableOccurrences = 0;
            foreach (object item in components)
            {
                Component2 component = item as Component2;
                if (component == null || ShouldSkipComponent(component))
                    continue;

                ModelDoc2 model = component.GetModelDoc2() as ModelDoc2;
                if (model == null)
                    model = TryOpenComponentModel(component);

                if (model == null)
                {
                    unavailableOccurrences++;
                    Debug.WriteLine("[PAINT HOLE] Component model unavailable: " + component.Name2);
                    continue;
                }
                if (model.GetType() != (int)swDocumentTypes_e.swDocPART)
                    continue;

                scannedPartOccurrences++;
                string cacheKey = GetComponentCacheKey(component, model);
                List<HoleRecord> records;
                if (!cache.TryGetValue(cacheKey, out records))
                {
                    records = ScanPart(model, component.Name2, 1, component.ReferencedConfiguration);
                    cache[cacheKey] = records;
                }

                foreach (HoleRecord record in records)
                {
                    HoleRecord copy = record.Clone();
                    copy.ComponentName = component.Name2;
                    AddRecord(result, copy);
                }
            }

            if (unavailableOccurrences > 0)
                throw new InvalidOperationException("Khong doc duoc " + unavailableOccurrences.ToString(CultureInfo.InvariantCulture) +
                    " component. Hay Resolve Lightweight va mo lai assembly truoc khi dem hole.");
            Debug.WriteLine("[PAINT HOLE] Assembly scan done. partOccurrences=" + scannedPartOccurrences + ", featureRows=" + result.TotalFeatureRows + ", totalQuantity=" + result.TotalQuantity);
            return result;
        }

        private bool ShouldSkipComponent(Component2 component)
        {
            try
            {
                return component.IsEnvelope() || component.ExcludeFromBOM || component.IsSuppressed() || component.IsHidden(false);
            }
            catch
            {
                return true;
            }
        }

        private ModelDoc2 TryOpenComponentModel(Component2 component)
        {
            try
            {
                string path = component.GetPathName();
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    return null;

                int errors = 0;
                int warnings = 0;
                int docType = path.EndsWith(".sldasm", StringComparison.OrdinalIgnoreCase)
                    ? (int)swDocumentTypes_e.swDocASSEMBLY
                    : (int)swDocumentTypes_e.swDocPART;
                return swApp.OpenDoc6(path, docType, (int)swOpenDocOptions_e.swOpenDocOptions_Silent, component.ReferencedConfiguration, ref errors, ref warnings) as ModelDoc2;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[PAINT HOLE] Open component failed: " + ex.Message);
                return null;
            }
        }

        private string GetComponentCacheKey(Component2 component, ModelDoc2 model)
        {
            try
            {
                string path = component.GetPathName();
                if (string.IsNullOrWhiteSpace(path))
                    path = model.GetPathName();
                return path + "|" + component.ReferencedConfiguration;
            }
            catch
            {
                return Guid.NewGuid().ToString("N");
            }
        }

        private List<HoleRecord> ScanPart(ModelDoc2 model, string componentName, int multiplier, string configurationName)
        {
            var records = new List<HoleRecord>();
            string originalConfiguration = GetActiveConfigurationName(model);
            List<byte[]> selection = CaptureSelection(model);
            try
            {
                if (!string.IsNullOrWhiteSpace(configurationName) &&
                    !string.Equals(originalConfiguration, configurationName, StringComparison.OrdinalIgnoreCase) &&
                    !model.ShowConfiguration2(configurationName))
                    throw new InvalidOperationException("Khong mo duoc configuration " + configurationName);

                string foldedConfiguration = GetActiveConfigurationName(model);
                if (foldedConfiguration.IndexOf("flat", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Configuration flat = model.GetConfigurationByName(foldedConfiguration) as Configuration;
                    Configuration parent = flat == null ? null : flat.GetParent();
                    if (parent != null && model.ShowConfiguration2(parent.Name))
                        foldedConfiguration = parent.Name;
                }
                Debug.WriteLine("[HOLE SCAN] Part=" + SafePath(model) + ", Config=" + foldedConfiguration);
                var foldedIdentities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                records.AddRange(ScanConfiguration(model, componentName, multiplier,
                    foldedConfiguration, "Folded", foldedIdentities, null));

                string flatConfiguration = FindFlatPatternConfiguration(model, foldedConfiguration);
                if (!string.IsNullOrWhiteSpace(flatConfiguration) && model.ShowConfiguration2(flatConfiguration))
                {
                    Debug.WriteLine("[HOLE SCAN] Flat config=" + flatConfiguration);
                    records.AddRange(ScanConfiguration(model, componentName, multiplier,
                        flatConfiguration, "FlatPatternOnly", null, foldedIdentities));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[HOLE SCAN] Scan part failed: " + ex.Message);
            }
            finally
            {
                if (!string.Equals(GetActiveConfigurationName(model), originalConfiguration,
                    StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(originalConfiguration))
                {
                    try { model.ShowConfiguration2(originalConfiguration); }
                    catch (Exception ex) { Debug.WriteLine("[HOLE SCAN] Restore config failed: " + ex.Message); }
                }
                RestoreSelection(model, selection);
            }
            return records;
        }

        private List<byte[]> CaptureSelection(ModelDoc2 model)
        {
            var references = new List<byte[]>();
            try
            {
                SelectionMgr selection = model.SelectionManager as SelectionMgr;
                if (selection == null) return references;
                for (int index = 1; index <= selection.GetSelectedObjectCount2(-1); index++)
                {
                    object selected = selection.GetSelectedObject6(index, -1);
                    byte[] reference = model.Extension.GetPersistReference3(selected) as byte[];
                    if (reference != null) references.Add(reference);
                }
            }
            catch (Exception ex) { Debug.WriteLine("[HOLE SCAN] Capture selection failed: " + ex.Message); }
            return references;
        }

        private void RestoreSelection(ModelDoc2 model, List<byte[]> references)
        {
            if (references == null || references.Count == 0) return;
            try
            {
                var objects = new List<object>();
                foreach (byte[] reference in references)
                {
                    int error = 0;
                    object item = model.Extension.GetObjectByPersistReference3(reference, out error);
                    if (item != null) objects.Add(item);
                }
                if (objects.Count > 0)
                {
                    model.ClearSelection2(true);
                    model.Extension.MultiSelect2(objects.ToArray(), false, null);
                }
            }
            catch (Exception ex) { Debug.WriteLine("[HOLE SCAN] Restore selection failed: " + ex.Message); }
        }

        private List<HoleRecord> ScanConfiguration(ModelDoc2 model, string componentName, int multiplier,
            string configuration, string source, HashSet<string> collectIdentities, HashSet<string> foldedIdentities)
        {
            var records = new List<HoleRecord>();
            var resolver = new HoleFamilyResolver(model);
            string partNumber = GetPartNumber(model, configuration);
            foreach (HoleFamily family in resolver.Families)
            {
                bool inherited = false;
                foreach (Feature feature in family.Features)
                {
                    string id = GetConfigurationFeatureKey(model, feature);
                    if (collectIdentities != null) collectIdentities.Add(id);
                    if (foldedIdentities != null && foldedIdentities.Contains(id)) inherited = true;
                }
                if (foldedIdentities != null && inherited)
                {
                    Debug.WriteLine("[HOLE SCAN] Skip inherited flat family: " +
                        SafeFeatureName(family.Roots.Count > 0 ? family.Roots[0] : family.Features[0]));
                    continue;
                }
                Feature representative = family.Roots.Count > 0 ? family.Roots[0] : family.Features[0];
                string label = family.Label ?? "";
                if (label.Length == 0 && SafeFeatureName(representative).Contains(PaintToken)) label = PaintToken;
                string holeKey = family.DiameterMm.HasValue
                    ? PhiToken + family.DiameterMm.Value.ToString("0.###", CultureInfo.InvariantCulture)
                    : "?";
                var record = new HoleRecord
                {
                    HoleKey = holeKey,
                    HoleLabel = label,
                    FamilyId = family.FamilyId ?? "",
                    DiameterMm = family.DiameterMm,
                    Source = source,
                    Configuration = configuration,
                    FeatureName = SafeFeatureName(representative),
                    FeatureType = SafeFeatureType(representative),
                    PartPath = SafePath(model),
                    PartNumber = partNumber,
                    ComponentName = componentName,
                    Quantity = family.PhysicalHoleCount * Math.Max(1, multiplier),
                    IsPattern = family.Patterns.Count > 0
                };
                records.Add(record);
                Debug.WriteLine("[HOLE SCAN] Feature=" + record.FeatureName + ", Type=" + record.FeatureType +
                    ", Label=" + label + ", Quantity=" + record.Quantity + ", Source=" + source);
            }
            return records;
        }

        private string GetConfigurationFeatureKey(ModelDoc2 model, Feature feature)
        {
            // A COM pointer can change when ShowConfiguration2 recreates feature wrappers.
            // Feature names are unique within a Part and stable across its configurations.
            string name = SafeFeatureName(feature);
            if (!string.IsNullOrWhiteSpace(name)) return "Name:" + name;
            try
            {
                byte[] reference = model.Extension.GetPersistReference3(feature) as byte[];
                if (reference != null) return "Ref:" + Convert.ToBase64String(reference);
            }
            catch (Exception ex) { Debug.WriteLine("[HOLE SCAN] Feature reference failed: " + ex.Message); }
            return "COM:" + HoleFeatureClassifier.Identity(feature).ToString(CultureInfo.InvariantCulture);
        }

        private string FindFlatPatternConfiguration(ModelDoc2 model, string folded)
        {
            try
            {
                var names = model.GetConfigurationNames() as Array;
                if (names == null) return null;
                foreach (object item in names)
                {
                    string name = item as string;
                    if (string.IsNullOrWhiteSpace(name) ||
                        name.IndexOf("flat", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    Configuration configuration = model.GetConfigurationByName(name) as Configuration;
                    for (Configuration parent = configuration == null ? null : configuration.GetParent();
                        parent != null; parent = parent.GetParent())
                        if (string.Equals(parent.Name, folded, StringComparison.OrdinalIgnoreCase)) return name;
                }
            }
            catch (Exception ex) { Debug.WriteLine("[HOLE SCAN] Flat config lookup failed: " + ex.Message); }
            return null;
        }

        private void AddRecord(PaintHoleScanResult result, HoleRecord record)
        {
            if (result == null || record == null || string.IsNullOrWhiteSpace(record.HoleKey))
                return;

            result.Records.Add(record);
            result.TotalFeatureRows++;
            result.TotalQuantity += record.Quantity;

            string summaryKey = record.HoleKey + "\u001F" + (record.HoleLabel ?? "");
            HoleSummary summary;
            if (!result.Summary.TryGetValue(summaryKey, out summary))
            {
                summary = new HoleSummary { HoleKey = record.HoleKey, HoleLabel = record.HoleLabel };
                result.Summary.Add(summaryKey, summary);
            }
            summary.Quantity += record.Quantity;
        }

        private string SafeFeatureName(Feature feature)
        {
            try
            {
                return feature?.Name ?? "";
            }
            catch
            {
                return "";
            }
        }

        private string SafeFeatureType(Feature feature)
        {
            try
            {
                return feature?.GetTypeName2() ?? "";
            }
            catch
            {
                return "";
            }
        }

        private string SafePath(ModelDoc2 model)
        {
            try
            {
                return model.GetPathName() ?? "";
            }
            catch
            {
                return "";
            }
        }

        private string GetActiveConfigurationName(ModelDoc2 model)
        {
            try
            {
                Configuration configuration = model?.ConfigurationManager?.ActiveConfiguration;
                return configuration?.Name ?? "";
            }
            catch
            {
                return "";
            }
        }

        private string GetPartNumber(ModelDoc2 model, string configurationName)
        {
            string value = ReadCustomProperty(model, configurationName, "\u90E8\u54C1\u756A\u53F7");
            if (string.IsNullOrWhiteSpace(value) && !string.IsNullOrWhiteSpace(configurationName))
                value = ReadCustomProperty(model, "", "\u90E8\u54C1\u756A\u53F7");
            return (value ?? "").Trim();
        }

        private string ReadCustomProperty(ModelDoc2 model, string configurationName, string propertyName)
        {
            try
            {
                CustomPropertyManager manager =
                    model?.Extension?.get_CustomPropertyManager(configurationName ?? "");
                if (manager == null)
                    return "";

                string raw;
                string resolved;
                bool wasResolved;
                bool linked;
                manager.Get6(propertyName, false, out raw, out resolved, out wasResolved, out linked);
                return string.IsNullOrWhiteSpace(resolved) ? (raw ?? "") : resolved;
            }
            catch
            {
                return "";
            }
        }

        private void ExportToExcel(PaintHoleScanResult result)
        {
            Type excelType = Type.GetTypeFromProgID("Excel.Application");
            if (excelType == null)
            {
                MessageBox.Show("Khong tim thay Microsoft Excel.", "Dem hole", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            dynamic xlApp = Activator.CreateInstance(excelType);
            dynamic xlWB = xlApp.Workbooks.Add();
            dynamic summarySheet = xlWB.Sheets[1];
            summarySheet.Name = "Thong ke lo";

            summarySheet.Cells[1, 1] = "Hole";
            summarySheet.Cells[1, 2] = "Hole Label";
            summarySheet.Cells[1, 3] = "Total Quantity";

            int row = 2;
            foreach (HoleSummary summary in result.GetSortedSummary())
            {
                summarySheet.Cells[row, 1] = summary.HoleKey;
                summarySheet.Cells[row, 2] = summary.HoleLabel;
                summarySheet.Cells[row, 3] = summary.Quantity;
                row++;
            }
            summarySheet.Cells[row, 1] = "TONG";
            summarySheet.Cells[row, 3] = result.TotalQuantity;
            try
            {
                dynamic totalRange = summarySheet.Range[summarySheet.Cells[row, 1], summarySheet.Cells[row, 3]];
                totalRange.Font.Bold = true;
                totalRange.Interior.Color = 13434879;
            }
            catch
            {
            }

            summarySheet.Columns.AutoFit();

            dynamic detailSheet = xlWB.Sheets.Add(Type.Missing, summarySheet);
            detailSheet.Name = "Chi tiet";
            string[] headers = { "\u90E8\u54C1\u756A\u53F7", "Component", "Configuration", "Feature",
                "Feature Type", "Hole", "Hole Label", "Family ID", "Quantity", "Pattern", "Source" };
            for (int column = 0; column < headers.Length; column++)
                detailSheet.Cells[1, column + 1] = headers[column];

            row = 2;
            foreach (HoleRecord record in result.Records)
            {
                detailSheet.Cells[row, 1] = record.PartNumber;
                detailSheet.Cells[row, 2] = GetComponentDisplayName(record);
                detailSheet.Cells[row, 3] = record.Configuration;
                detailSheet.Cells[row, 4] = record.FeatureName;
                detailSheet.Cells[row, 5] = record.FeatureType;
                detailSheet.Cells[row, 6] = record.HoleKey;
                detailSheet.Cells[row, 7] = record.HoleLabel;
                detailSheet.Cells[row, 8] = record.FamilyId;
                detailSheet.Cells[row, 9] = record.Quantity;
                detailSheet.Cells[row, 10] = record.IsPattern ? "Yes" : "";
                detailSheet.Cells[row, 11] = record.Source;
                row++;
            }
            detailSheet.Columns.AutoFit();
            try
            {
                summarySheet.Activate();
                summarySheet.Range["A1"].Select();
            }
            catch
            {
            }
            xlApp.Visible = true;
        }

        private string GetComponentDisplayName(HoleRecord record)
        {
            if (!string.IsNullOrWhiteSpace(record.ComponentName))
                return record.ComponentName;
            try
            {
                string fileName = Path.GetFileNameWithoutExtension(record.PartPath ?? "");
                if (!string.IsNullOrWhiteSpace(fileName))
                    return fileName;
            }
            catch
            {
            }

            string componentName = record.ComponentName ?? "";
            int separator = Math.Max(componentName.LastIndexOf('/'), componentName.LastIndexOf('\\'));
            return separator >= 0 ? componentName.Substring(separator + 1) : componentName;
        }

        private class PaintHoleScanResult
        {
            public readonly Dictionary<string, HoleSummary> Summary = new Dictionary<string, HoleSummary>(StringComparer.OrdinalIgnoreCase);
            public readonly List<HoleRecord> Records = new List<HoleRecord>();
            public int TotalFeatureRows;
            public int TotalQuantity;

            public List<HoleSummary> GetSortedSummary()
            {
                List<HoleSummary> list = new List<HoleSummary>(Summary.Values);
                list.Sort((a, b) =>
                {
                    int bySize = string.Compare(a.HoleKey, b.HoleKey, StringComparison.OrdinalIgnoreCase);
                    return bySize != 0 ? bySize : string.Compare(a.HoleLabel, b.HoleLabel,
                        StringComparison.OrdinalIgnoreCase);
                });
                return list;
            }
        }

        private class HoleSummary
        {
            public string HoleKey;
            public string HoleLabel;
            public int Quantity;
        }

        private class HoleRecord
        {
            public string HoleKey;
            public string HoleLabel;
            public string FamilyId;
            public double? DiameterMm;
            public string Configuration;
            public string Source;
            public int Quantity;
            public bool IsPattern;
            public string FeatureName;
            public string FeatureType;
            public string PartNumber;
            public string ComponentName;
            public string PartPath;

            public HoleRecord Clone()
            {
                return (HoleRecord)MemberwiseClone();
            }
        }
    }
}
