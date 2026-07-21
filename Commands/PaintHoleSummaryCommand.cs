using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
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

            if (!ResolveLightweightComponents(activeModel))
                return;

            bool oldCommandInProgress = false;
            try
            {
                oldCommandInProgress = swApp.CommandInProgress;
                swApp.CommandInProgress = true;

                PaintHoleScanResult result = Scan(activeModel);
                if (result.TotalFeatureRows == 0)
                {
                    MessageBox.Show("Khong tim thay feature ten dang phi hoac son trong model hien tai.", "Dem hole", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
                if (before <= 0 && assembly.LightweightAllResolved())
                    return true;

                Debug.WriteLine("[PAINT HOLE] Resolve lightweight before scan. before=" + before);
                assembly.ResolveAllLightWeightComponents(false);
                Application.DoEvents();

                int after = assembly.GetLightWeightComponentCount();
                bool resolved = after <= 0 || assembly.LightweightAllResolved();
                if (!resolved)
                {
                    assembly.ResolveAllLightweight();
                    Application.DoEvents();
                    after = assembly.GetLightWeightComponentCount();
                    resolved = after <= 0 || assembly.LightweightAllResolved();
                }

                Debug.WriteLine("[PAINT HOLE] Resolve lightweight result. after=" + after + ", resolved=" + resolved);
                if (!resolved)
                {
                    MessageBox.Show(
                        "Khong the bo het che do Lightweight. Hay Resolve component roi chay lai DEM LO.",
                        "Dem hole",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return false;
                }

                activeModel.ForceRebuild3(false);
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
            foreach (object item in components)
            {
                Application.DoEvents();
                Component2 component = item as Component2;
                if (component == null || ShouldSkipComponent(component))
                    continue;

                ModelDoc2 model = component.GetModelDoc2() as ModelDoc2;
                if (model == null)
                    model = TryOpenComponentModel(component);

                if (model == null || model.GetType() != (int)swDocumentTypes_e.swDocPART)
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
            List<HoleRecord> records = new List<HoleRecord>();
            HashSet<string> keysWithPattern = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> baseKeysWithPattern = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string partNumber = GetPartNumber(model, configurationName);

            try
            {
                for (Feature feature = model.FirstFeature() as Feature; feature != null; feature = feature.GetNextFeature() as Feature)
                {
                    if (!IsUsableFeature(feature))
                        continue;

                    string featureName = SafeFeatureName(feature);
                    string holeKey = ParseHoleKey(featureName);
                    if (string.IsNullOrWhiteSpace(holeKey))
                        continue;

                    bool pattern = IsPatternFeature(feature);
                    if (pattern && !IsUsablePatternFeature(feature))
                    {
                        Debug.WriteLine("[PAINT HOLE] Skip unusable pattern feature. name=" + featureName + ", type=" + SafeFeatureType(feature));
                        continue;
                    }

                    int quantity = pattern
                        ? GetPatternInstanceCount(feature) * GetPatternSeedHoleCount(feature)
                        : GetDirectHoleCount(feature);
                    if (quantity < 1)
                        quantity = 1;

                    HoleRecord record = new HoleRecord
                    {
                        HoleKey = holeKey,
                        FeatureName = featureName,
                        FeatureType = SafeFeatureType(feature),
                        PartPath = SafePath(model),
                        PartNumber = partNumber,
                        ComponentName = componentName,
                        Quantity = quantity * Math.Max(1, multiplier),
                        IsPaint = featureName.IndexOf(PaintToken, StringComparison.OrdinalIgnoreCase) >= 0,
                        IsPattern = pattern
                    };
                    records.Add(record);
                    if (pattern)
                    {
                        keysWithPattern.Add(holeKey);
                        baseKeysWithPattern.Add(GetBaseHoleKey(holeKey));
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[PAINT HOLE] Scan part failed: " + ex.Message);
            }

            if (keysWithPattern.Count == 0)
                return records;

            List<HoleRecord> filtered = new List<HoleRecord>();
            foreach (HoleRecord record in records)
            {
                string baseKey = GetBaseHoleKey(record.HoleKey);
                if (!record.IsPattern && (keysWithPattern.Contains(record.HoleKey) || baseKeysWithPattern.Contains(baseKey)))
                {
                    Debug.WriteLine("[PAINT HOLE] Skip seed to avoid pattern double count. feature=" + record.FeatureName + ", key=" + record.HoleKey);
                    continue;
                }
                filtered.Add(record);
            }
            return filtered;
        }

        private void AddRecord(PaintHoleScanResult result, HoleRecord record)
        {
            if (result == null || record == null || string.IsNullOrWhiteSpace(record.HoleKey))
                return;

            result.Records.Add(record);
            result.TotalFeatureRows++;
            result.TotalQuantity += record.Quantity;

            HoleSummary summary;
            if (!result.Summary.TryGetValue(record.HoleKey, out summary))
            {
                summary = new HoleSummary { HoleKey = record.HoleKey, IsPaint = record.IsPaint };
                result.Summary.Add(record.HoleKey, summary);
            }
            summary.Quantity += record.Quantity;
            if (record.IsPattern)
                summary.PatternFeatureCount++;
            else
                summary.DirectFeatureCount++;
        }

        private string ParseHoleKey(string featureName)
        {
            featureName = (featureName ?? "").Trim();
            if (featureName.Length == 0)
                return null;

            bool paint = featureName.IndexOf(PaintToken, StringComparison.OrdinalIgnoreCase) >= 0;
            int index = IndexOfPhi(featureName);
            if (index < 0)
            {
                if (!paint)
                    return null;

                int paintIndex = featureName.IndexOf(PaintToken, StringComparison.OrdinalIgnoreCase);
                string beforePaint = featureName.Substring(0, paintIndex).Trim();
                if (string.IsNullOrWhiteSpace(beforePaint))
                    return null;
                return beforePaint + " " + PaintToken;
            }

            string size = "";
            for (int i = index + 1; i < featureName.Length; i++)
            {
                char c = featureName[i];
                if (char.IsDigit(c) || c == '.' || c == ',' || c == 'x' || c == 'X' || c == '\u00D7')
                {
                    size += c == ',' ? '.' : (c == 'X' || c == '\u00D7' ? 'x' : c);
                    continue;
                }
                break;
            }

            if (string.IsNullOrWhiteSpace(size))
                return null;

            return PhiToken + size + (paint ? " " + PaintToken : "");
        }

        private string GetBaseHoleKey(string holeKey)
        {
            holeKey = (holeKey ?? "").Trim();
            if (holeKey.Length == 0)
                return "";

            int paintIndex = holeKey.IndexOf(PaintToken, StringComparison.OrdinalIgnoreCase);
            if (paintIndex >= 0)
                holeKey = holeKey.Substring(0, paintIndex).Trim();

            int patternIndex = holeKey.IndexOf("PATTERN", StringComparison.OrdinalIgnoreCase);
            if (patternIndex >= 0)
                holeKey = holeKey.Substring(0, patternIndex).Trim();

            return holeKey;
        }

        private int IndexOfPhi(string text)
        {
            if (string.IsNullOrEmpty(text))
                return -1;
            char[] chars = { '\u03C6', '\u03A6', '\u2300', '\u00D8', '\u00F8' };
            foreach (char c in chars)
            {
                int index = text.IndexOf(c);
                if (index >= 0)
                    return index;
            }
            return -1;
        }

        private bool IsPatternFeature(Feature feature)
        {
            string type = SafeFeatureType(feature);
            string name = SafeFeatureName(feature);
            return type.IndexOf("Pattern", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   type.IndexOf("Curve", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("PATTERN", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("Pattern", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool IsUsablePatternFeature(Feature feature)
        {
            if (!IsUsableFeature(feature))
                return false;

            if (HasFeatureWarning(feature))
                return false;

            int count = GetPatternInstanceCount(feature);
            if (count <= 1)
                return false;

            return true;
        }

        private bool IsUsableFeature(Feature feature)
        {
            if (feature == null)
                return false;

            if (IsFeatureSuppressed(feature))
                return false;

            if (HasFeatureError(feature))
                return false;

            return true;
        }

        private bool IsFeatureSuppressed(Feature feature)
        {
            try
            {
                bool suppressed = feature.IsSuppressed();
                if (suppressed)
                    Debug.WriteLine("[PAINT HOLE] Skip suppressed feature. name=" + SafeFeatureName(feature));
                return suppressed;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[PAINT HOLE] IsSuppressed failed, use current-configuration fallback. name=" +
                    SafeFeatureName(feature) + ", error=" + ex.Message);
            }

            try
            {
                object value = feature.IsSuppressed2(
                    (int)swInConfigurationOpts_e.swThisConfiguration,
                    null);
                if (value is bool)
                {
                    bool suppressed = (bool)value;
                    if (suppressed)
                        Debug.WriteLine("[PAINT HOLE] Skip suppressed feature (fallback). name=" + SafeFeatureName(feature));
                    return suppressed;
                }
                object[] values = value as object[];
                if (values != null)
                {
                    foreach (object item in values)
                    {
                        if (item is bool && (bool)item)
                        {
                            Debug.WriteLine("[PAINT HOLE] Skip suppressed feature (fallback array). name=" + SafeFeatureName(feature));
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[PAINT HOLE] IsSuppressed2 failed. name=" +
                    SafeFeatureName(feature) + ", error=" + ex.Message);
            }

            return false;
        }

        private bool HasFeatureError(Feature feature)
        {
            try
            {
                int warning = 0;
                object value = ((dynamic)feature).GetErrorCode2(ref warning);
                int errorCode = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                if (errorCode != 0)
                {
                    Debug.WriteLine("[PAINT HOLE] Feature has rebuild error. name=" + SafeFeatureName(feature) + ", error=" + errorCode + ", warning=" + warning);
                    return true;
                }
            }
            catch
            {
            }

            try
            {
                object value = ((dynamic)feature).GetErrorCode();
                int errorCode = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                if (errorCode != 0)
                {
                    Debug.WriteLine("[PAINT HOLE] Feature has error. name=" + SafeFeatureName(feature) + ", error=" + errorCode);
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private bool HasPatternWarningOrSkippedInstances(Feature feature)
        {
            if (HasFeatureWarning(feature))
                return true;

            if (HasPatternSkippedInstances(feature))
                return true;

            return false;
        }

        private bool HasFeatureWarning(Feature feature)
        {
            try
            {
                int warning = 0;
                object value = ((dynamic)feature).GetErrorCode2(ref warning);
                int errorCode = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                if (errorCode != 0 || warning != 0)
                {
                    Debug.WriteLine("[PAINT HOLE] Skip pattern with warning/error. name=" + SafeFeatureName(feature) + ", error=" + errorCode + ", warning=" + warning);
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private bool HasPatternSkippedInstances(Feature feature)
        {
            try
            {
                dynamic definition = feature.GetDefinition();
                if (definition == null)
                    return false;

                int skipped = TryGetDynamicInt(definition, "SkippedItemCount");
                if (skipped <= 0) skipped = TryGetDynamicInt(definition, "SkippedItemsCount");
                if (skipped <= 0) skipped = TryGetDynamicInt(definition, "SkippedInstanceCount");
                if (skipped <= 0) skipped = TryGetDynamicInt(definition, "SkippedInstancesCount");
                if (skipped <= 0) skipped = TryGetDynamicInt(definition, "SkipCount");
                if (skipped > 0)
                {
                    Debug.WriteLine("[PAINT HOLE] Skip pattern with skipped instances. name=" + SafeFeatureName(feature) + ", skipped=" + skipped);
                    return true;
                }

                object skippedArray = TryGetDynamicObject(definition, "SkippedItemArray");
                if (skippedArray == null) skippedArray = TryGetDynamicObject(definition, "SkippedItems");
                if (skippedArray == null) skippedArray = TryGetDynamicObject(definition, "SkippedInstances");
                Array array = skippedArray as Array;
                if (array != null && array.Length > 0)
                {
                    Debug.WriteLine("[PAINT HOLE] Skip pattern with skipped item array. name=" + SafeFeatureName(feature) + ", skipped=" + array.Length);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[PAINT HOLE] Read skipped pattern info failed: " + ex.Message);
            }

            return false;
        }

        private object TryGetDynamicObject(dynamic obj, string propertyName)
        {
            try
            {
                return obj.GetType().InvokeMember(propertyName, System.Reflection.BindingFlags.GetProperty, null, obj, null);
            }
            catch
            {
                return null;
            }
        }
        private int GetPatternInstanceCount(Feature feature)
        {
            try
            {
                object definition = feature.GetDefinition();
                LinearPatternFeatureData linear = definition as LinearPatternFeatureData;
                if (linear != null)
                {
                    int count = GetTwoDirectionPatternCount(linear.D1TotalInstances, linear.D2TotalInstances, linear.D2PatternSeedOnly);
                    return Math.Max(1, count - linear.GetSkippedItemCount());
                }
                CurveDrivenPatternFeatureData curve = definition as CurveDrivenPatternFeatureData;
                if (curve != null)
                {
                    int count = GetTwoDirectionPatternCount(curve.D1InstanceCount, curve.D2InstanceCount, curve.D2PatternSeedOnly);
                    return Math.Max(1, count - curve.GetSkippedItemCount());
                }
                CircularPatternFeatureData circular = definition as CircularPatternFeatureData;
                if (circular != null)
                {
                    int count = circular.TotalInstances2 > 0 ? circular.TotalInstances2 : circular.TotalInstances;
                    return Math.Max(1, count - circular.GetSkippedItemCount());
                }
                FillPatternFeatureData fill = definition as FillPatternFeatureData;
                if (fill != null && fill.NoOfInstances > 0)
                    return fill.NoOfInstances;
                TablePatternFeatureData table = definition as TablePatternFeatureData;
                if (table != null)
                    return Math.Max(1, table.GetPointCount() - table.GetSkippedItemCount());
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[PAINT HOLE] Read typed pattern count failed. feature=" + SafeFeatureName(feature) + ", error=" + ex.Message);
            }

            int value = TryGetFeatureDimensionInt(feature, "D1");
            if (value > 1)
                return value;

            try
            {
                dynamic definition = feature.GetDefinition();
                if (definition != null)
                {
                    value = TryGetDynamicInt(definition, "D1TotalInstances");
                    if (value > 1) return value;
                    value = TryGetDynamicInt(definition, "TotalInstances");
                    if (value > 1) return value;
                    value = TryGetDynamicInt(definition, "InstanceCount");
                    if (value > 1) return value;
                    value = TryGetDynamicInt(definition, "NumberOfInstances");
                    if (value > 1) return value;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[PAINT HOLE] Read pattern definition failed: " + ex.Message);
            }
            return 1;
        }

        private int GetTwoDirectionPatternCount(int direction1, int direction2, bool secondDirectionSeedOnly)
        {
            direction1 = Math.Max(1, direction1);
            direction2 = Math.Max(1, direction2);
            if (direction2 <= 1)
                return direction1;
            return secondDirectionSeedOnly ? direction1 + direction2 - 1 : direction1 * direction2;
        }

        private int GetPatternSeedHoleCount(Feature patternFeature)
        {
            try
            {
                object definition = patternFeature.GetDefinition();
                object seedValue = null;
                LinearPatternFeatureData linear = definition as LinearPatternFeatureData;
                if (linear != null) seedValue = linear.PatternFeatureArray;
                CurveDrivenPatternFeatureData curve = definition as CurveDrivenPatternFeatureData;
                if (curve != null) seedValue = curve.PatternFeatureArray;
                CircularPatternFeatureData circular = definition as CircularPatternFeatureData;
                if (circular != null) seedValue = circular.PatternFeatureArray;
                FillPatternFeatureData fill = definition as FillPatternFeatureData;
                if (fill != null) seedValue = fill.PatternFeatureArray;
                TablePatternFeatureData table = definition as TablePatternFeatureData;
                if (table != null) seedValue = table.PatternFeatureArray;

                Array seeds = seedValue as Array;
                if (seeds == null || seeds.Length == 0)
                    return 1;
                int holeCount = 0;
                foreach (object item in seeds)
                {
                    Feature seed = item as Feature;
                    if (seed == null || IsPatternFeature(seed) || !IsUsableFeature(seed))
                        continue;
                    holeCount += GetDirectHoleCount(seed);
                }
                return Math.Max(1, holeCount);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[PAINT HOLE] Read pattern seed failed. feature=" + SafeFeatureName(patternFeature) + ", error=" + ex.Message);
                return 1;
            }
        }

        private int TryGetFeatureDimensionInt(Feature feature, string dimensionName)
        {
            try
            {
                Dimension dimension = feature.Parameter(dimensionName) as Dimension;
                if (dimension == null)
                    return 0;
                double value = dimension.SystemValue;
                if (value > 0.0 && value < 100000.0)
                    return Math.Max(1, (int)Math.Round(value));
            }
            catch
            {
            }
            return 0;
        }

        private int TryGetDynamicInt(dynamic obj, string propertyName)
        {
            try
            {
                object value = obj.GetType().InvokeMember(propertyName, System.Reflection.BindingFlags.GetProperty, null, obj, null);
                if (value == null)
                    return 0;
                return Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return 0;
            }
        }

        private int GetDirectHoleCount(Feature feature)
        {
            try
            {
                WizardHoleFeatureData2 holeData = feature.GetDefinition() as WizardHoleFeatureData2;
                if (holeData != null)
                {
                    int positionCount = holeData.GetSketchPointCount();
                    if (positionCount > 0)
                    {
                        Debug.WriteLine("[PAINT HOLE] Hole Wizard positions. feature=" + SafeFeatureName(feature) + ", count=" + positionCount);
                        return positionCount;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[PAINT HOLE] Read Hole Wizard positions failed. feature=" + SafeFeatureName(feature) + ", error=" + ex.Message);
            }

            int contourCount = 0;
            try
            {
                for (Feature sub = feature.GetFirstSubFeature() as Feature; sub != null; sub = sub.GetNextSubFeature() as Feature)
                {
                    Sketch sketch = null;
                    try
                    {
                        sketch = sub.GetSpecificFeature2() as Sketch;
                    }
                    catch
                    {
                    }

                    if (sketch == null)
                        continue;

                    object[] contours = sketch.GetSketchContours() as object[];
                    if (contours == null)
                        continue;
                    foreach (object item in contours)
                    {
                        SketchContour contour = item as SketchContour;
                        if (contour != null && contour.IsClosed())
                            contourCount++;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[PAINT HOLE] Count closed hole contours failed: " + ex.Message);
            }
            if (contourCount > 0)
            {
                Debug.WriteLine("[PAINT HOLE] Direct feature closed contours. feature=" + SafeFeatureName(feature) + ", count=" + contourCount);
                return contourCount;
            }
            Debug.WriteLine("[PAINT HOLE] Direct feature fallback to one physical hole. feature=" + SafeFeatureName(feature));
            return 1;
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

            summarySheet.Cells[1, 1] = "Loai lo";
            summarySheet.Cells[1, 2] = "Tong so lo";
            summarySheet.Cells[1, 3] = "\u7A74\u5857\u88C5";
            summarySheet.Cells[1, 4] = "So feature pattern";
            summarySheet.Cells[1, 5] = "So feature truc tiep";
            summarySheet.Cells[1, 6] = "Loai lo";
            summarySheet.Cells[1, 7] = "\u90E8\u54C1\u756A\u53F7";
            summarySheet.Cells[1, 8] = "Ten component";
            summarySheet.Cells[1, 9] = "Tong so lo";
            summarySheet.Cells[1, 10] = "So feature";

            int row = 2;
            foreach (HoleSummary summary in result.GetSortedSummary())
            {
                summarySheet.Cells[row, 1] = summary.HoleKey;
                summarySheet.Cells[row, 2] = summary.Quantity;
                summarySheet.Cells[row, 3] = summary.IsPaint ? "\u7A74\u5857\u88C5" : "";
                summarySheet.Cells[row, 4] = summary.PatternFeatureCount;
                summarySheet.Cells[row, 5] = summary.DirectFeatureCount;
                row++;
            }
            summarySheet.Cells[row, 1] = "TONG";
            summarySheet.Cells[row, 2] = result.TotalQuantity;
            try
            {
                dynamic totalRange = summarySheet.Range[summarySheet.Cells[row, 1], summarySheet.Cells[row, 5]];
                totalRange.Font.Bold = true;
                totalRange.Interior.Color = 13434879;
            }
            catch
            {
            }
            int featureRow = 2;
            foreach (HolePartSummary partSummary in BuildHolePartSummaries(result))
            {
                summarySheet.Cells[featureRow, 6] = partSummary.HoleKey;
                summarySheet.Cells[featureRow, 7] = partSummary.PartNumber;
                summarySheet.Cells[featureRow, 8] = partSummary.ComponentName;
                summarySheet.Cells[featureRow, 9] = partSummary.Quantity;
                summarySheet.Cells[featureRow, 10] = partSummary.FeatureCount;
                featureRow++;
            }
            summarySheet.Columns.AutoFit();

            dynamic detailSheet = xlWB.Sheets.Add(Type.Missing, summarySheet);
            detailSheet.Name = "Chi tiet";
            detailSheet.Cells[1, 1] = "Loai lo";
            detailSheet.Cells[1, 2] = "So lo";
            detailSheet.Cells[1, 3] = "Feature";
            detailSheet.Cells[1, 4] = "Kieu feature";
            detailSheet.Cells[1, 5] = "La pattern";
            detailSheet.Cells[1, 6] = "\u90E8\u54C1\u756A\u53F7";
            detailSheet.Cells[1, 7] = "Ten component";

            row = 2;
            foreach (HoleRecord record in result.Records)
            {
                detailSheet.Cells[row, 1] = record.HoleKey;
                detailSheet.Cells[row, 2] = record.Quantity;
                detailSheet.Cells[row, 3] = record.FeatureName;
                detailSheet.Cells[row, 4] = record.FeatureType;
                detailSheet.Cells[row, 5] = record.IsPattern ? "Co" : "";
                detailSheet.Cells[row, 6] = record.PartNumber;
                detailSheet.Cells[row, 7] = GetComponentDisplayName(record);
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

        private List<HolePartSummary> BuildHolePartSummaries(PaintHoleScanResult result)
        {
            Dictionary<string, HolePartSummary> grouped =
                new Dictionary<string, HolePartSummary>(StringComparer.OrdinalIgnoreCase);

            foreach (HoleRecord record in result.Records)
            {
                string componentName = GetComponentDisplayName(record);
                string key = (record.HoleKey ?? "") + "\u001F" +
                    (record.PartNumber ?? "") + "\u001F" + componentName;

                HolePartSummary summary;
                if (!grouped.TryGetValue(key, out summary))
                {
                    summary = new HolePartSummary
                    {
                        HoleKey = record.HoleKey,
                        PartNumber = record.PartNumber,
                        ComponentName = componentName
                    };
                    grouped.Add(key, summary);
                }

                summary.Quantity += record.Quantity;
                summary.FeatureCount++;
            }

            List<HolePartSummary> list = new List<HolePartSummary>(grouped.Values);
            list.Sort((left, right) =>
            {
                int compare = string.Compare(left.HoleKey, right.HoleKey, StringComparison.OrdinalIgnoreCase);
                if (compare != 0)
                    return compare;
                compare = string.Compare(left.PartNumber, right.PartNumber, StringComparison.OrdinalIgnoreCase);
                if (compare != 0)
                    return compare;
                return string.Compare(left.ComponentName, right.ComponentName, StringComparison.OrdinalIgnoreCase);
            });
            return list;
        }

        private string GetComponentDisplayName(HoleRecord record)
        {
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
                list.Sort((a, b) => string.Compare(a.HoleKey, b.HoleKey, StringComparison.OrdinalIgnoreCase));
                return list;
            }
        }

        private class HoleSummary
        {
            public string HoleKey;
            public int Quantity;
            public bool IsPaint;
            public int PatternFeatureCount;
            public int DirectFeatureCount;
        }

        private class HolePartSummary
        {
            public string HoleKey;
            public string PartNumber;
            public string ComponentName;
            public int Quantity;
            public int FeatureCount;
        }

        private class HoleRecord
        {
            public string HoleKey;
            public int Quantity;
            public bool IsPaint;
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
