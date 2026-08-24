using System;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands
{
    public enum BomCommandContext
    {
        None,
        Detail,
        Unit
    }

    public class BomLoader
    {
        private readonly ISldWorks swApp;
        private readonly HashSet<string> silentlyOpenedDocumentPaths =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> componentPropertyCache =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> assemblyPathCache =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private Func<bool> cancellationRequested;
        private BomCommandContext activeBomContext = BomCommandContext.Detail;

        public BomLoader(ISldWorks app)
        {
            swApp = app;
        }

        public ITableAnnotation GetCustomBomTable()
        {
            ModelDoc2 model = swApp?.ActiveDoc as ModelDoc2;
            if (model == null)
                return null;

            ITableAnnotation selectedTable = GetSelectedTable(model);
            if (selectedTable != null)
                return selectedTable;

            return GetFirstTable(model);
        }

        public BomCommandContext GetBomCommandContext(ITableAnnotation table)
        {
            if (table == null)
                return BomCommandContext.None;

            IBomTableAnnotation bomTable = table as IBomTableAnnotation;
            BomFeature bomFeature = null;
            try
            {
                bomFeature = bomTable?.BomFeature as BomFeature;
            }
            catch
            {
                bomFeature = null;
            }

            string identity = "";
            try
            {
                identity += " " + Convert.ToString(table.Title ?? "");
            }
            catch
            {
            }
            try
            {
                identity += " " + Convert.ToString(bomFeature?.Name ?? "");
            }
            catch
            {
            }

            string normalized = identity.Replace(" ", "").Replace("_", "").Replace("-", "").ToUpperInvariant();
            if (normalized.Contains("BOMUNIT") || normalized.Contains("UNITBOM"))
                return BomCommandContext.Unit;
            if (normalized.Contains("DETAIL") || normalized.Contains("COMPONENT") || normalized.Contains("CHITIET"))
                return BomCommandContext.Detail;

            try
            {
                if (bomFeature != null)
                {
                    int tableType = bomFeature.TableType;
                    if (tableType == (int)swBomType_e.swBomType_TopLevelOnly)
                        return BomCommandContext.Unit;
                    if (tableType == (int)swBomType_e.swBomType_PartsOnly ||
                        tableType == (int)swBomType_e.swBomType_Indented)
                        return BomCommandContext.Detail;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[BOM LOAD] cannot read BOM table type: " + ex.Message);
            }

            // A non-BOM/custom table is treated as a detail list. This keeps the
            // component checks available while reserving XEP UNIT for a top-level BOM.
            return BomCommandContext.Detail;
        }

        private ITableAnnotation GetSelectedTable(ModelDoc2 model)
        {
            SelectionMgr selMgr = model.SelectionManager as SelectionMgr;
            if (selMgr == null)
                return null;

            int count = selMgr.GetSelectedObjectCount2(-1);

            for (int i = 1; i <= count; i++)
            {
                int type = selMgr.GetSelectedObjectType3(i, -1);

                if (type != (int)swSelectType_e.swSelANNOTATIONTABLES)
                    continue;

                object selectedObject = selMgr.GetSelectedObject6(i, -1);

                ITableAnnotation table = selectedObject as ITableAnnotation;
                if (table != null)
                    return table;

                Annotation annotation = selectedObject as Annotation;
                table = annotation?.GetSpecificAnnotation() as ITableAnnotation;

                if (table != null)
                    return table;
            }

            return null;
        }

        private ITableAnnotation GetFirstTable(ModelDoc2 model)
        {
            DrawingDoc drawing = model as DrawingDoc;

            if (drawing != null)
            {
                SolidWorks.Interop.sldworks.View view =
                    drawing.GetFirstView() as SolidWorks.Interop.sldworks.View;

                while (view != null)
                {
                    ITableAnnotation table =
                        view.GetFirstTableAnnotation() as ITableAnnotation;

                    if (table != null)
                        return table;

                    view = view.GetNextView() as SolidWorks.Interop.sldworks.View;
                }
            }

            Annotation annotation = model.GetFirstAnnotation2() as Annotation;

            while (annotation != null)
            {
                ITableAnnotation table =
                    annotation.GetSpecificAnnotation() as ITableAnnotation;

                if (table != null)
                    return table;

                annotation = annotation.GetNext3() as Annotation;
            }

            return null;
        }

        public void LoadBOMTableToGrid(DataGridView gridBom, ITableAnnotation swTable,
            Func<bool> isCancellationRequested = null)
        {
            if (gridBom == null || swTable == null)
                return;

            CloseSilentlyOpenedDocuments();
            componentPropertyCache.Clear();
            assemblyPathCache.Clear();
            ModelDoc2 originalDocument = swApp == null ? null : swApp.ActiveDoc as ModelDoc2;
            Func<bool> previousCancellation = cancellationRequested;
            cancellationRequested = isCancellationRequested;
            activeBomContext = GetBomCommandContext(swTable);

            try
            {

            gridBom.Rows.Clear();
            gridBom.AllowUserToAddRows = false;
            Debug.WriteLine("[BOM LOAD] start table rows=" + swTable.RowCount);

            int buhinNoCol = FindColumnIndex(swTable, "部品番号");
            int materialCol = FindColumnIndex(swTable, "材質");
            int thicknessCol = FindColumnIndex(swTable, "板厚");
            int gobanCol = FindColumnIndex(swTable, "合番");
            int qtyCol = FindColumnIndex(swTable, "数量");
            int fileNameCol = FindColumnIndex(swTable, "部品ﾌｧｲﾙ名");
            if (fileNameCol < 0)
                fileNameCol = FindColumnIndex(swTable, "部品ファイル名");

            List<string> bomAssemblyPathsToScan = new List<string>();
            List<string> searchDirectories = GetAssemblySearchDirectories(swTable);
            HashSet<string> rowAssemblyPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int r = 1; r < swTable.RowCount; r++)
            {
                if ((r % 10) == 0)
                    Application.DoEvents();
                if (IsCancellationRequested())
                    break;
                string fileName = GetCellText(swTable, r, fileNameCol);
                string bomBuhinNo = GetCellText(swTable, r, buhinNoCol);

                int rowIndex = gridBom.Rows.Add(
                    false,
                    bomBuhinNo,
                    activeBomContext == BomCommandContext.Unit
                        ? GetCellText(swTable, r, gobanCol)
                        : GetCellText(swTable, r, materialCol),
                    activeBomContext == BomCommandContext.Unit
                        ? ""
                        : GetCellText(swTable, r, thicknessCol),
                    GetCellText(swTable, r, qtyCol),
                    fileName
                );

                object[] rowComponents = SaveComponentPathToRowTag(
                    gridBom,
                    swTable,
                    r,
                    rowIndex,
                    fileName,
                    searchDirectories);
                // The BOM value is already resolved by SOLIDWORKS. Opening every
                // component again only to read the same property is very costly.
                if (string.IsNullOrWhiteSpace(bomBuhinNo))
                {
                    string componentBuhinNo = GetComponentCustomProperty(rowComponents, "部品番号");
                    if (!string.IsNullOrWhiteSpace(componentBuhinNo))
                        gridBom.Rows[rowIndex].Cells[1].Value = componentBuhinNo;
                }

                if (activeBomContext == BomCommandContext.Unit
                    && string.IsNullOrWhiteSpace(Convert.ToString(gridBom.Rows[rowIndex].Cells[2].Value)))
                {
                    string componentGoban = GetComponentCustomProperty(rowComponents, "合番");
                    if (!string.IsNullOrWhiteSpace(componentGoban))
                        gridBom.Rows[rowIndex].Cells[2].Value = componentGoban;
                }

                // Only BOM UNIT needs recursive assembly enrichment. A detail BOM
                // already contains its component rows, so this scan is redundant.
                if (activeBomContext != BomCommandContext.Unit)
                    continue;

                string assemblyPath = FindAssemblyPathByFileName(fileName, searchDirectories);
                if (!IsAssemblyPath(assemblyPath))
                    continue;

                rowAssemblyPaths.Add(assemblyPath);
                if (IsDash02Assembly(assemblyPath))
                    AddUniquePath(bomAssemblyPathsToScan, assemblyPath);
            }

            HashSet<string> traversedAssemblyPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string assemblyPath in bomAssemblyPathsToScan)
            {
                Application.DoEvents();
                if (IsCancellationRequested())
                    break;
                AddSubAssemblyRowsFromBomAssembly(gridBom, assemblyPath, rowAssemblyPaths, traversedAssemblyPaths);
            }

            Debug.WriteLine("[BOM LOAD] final grid rows=" + gridBom.Rows.Count + ", bomAssembliesToScan=" + bomAssemblyPathsToScan.Count);
            }
            finally
            {
                CloseSilentlyOpenedDocuments();
                if (originalDocument != null)
                {
                    try
                    {
                        int activateErrors = 0;
                        swApp.ActivateDoc3(originalDocument.GetTitle(), false, 0, ref activateErrors);
                    }
                    catch { }
                }
                cancellationRequested = previousCancellation;
            }
        }

        private int FindColumnIndex(ITableAnnotation table, string headerName)
        {
            for (int c = 0; c < table.ColumnCount; c++)
            {
                string header = NormalizeHeaderText(table.get_Text(0, c));

                if (string.Equals(header, NormalizeHeaderText(headerName), StringComparison.OrdinalIgnoreCase))
                    return c;
            }

            return -1;
        }

        private string GetCellText(ITableAnnotation table, int row, int col)
        {
            if (col < 0)
                return "";

            return table.get_Text(row, col);
        }

        private object[] SaveComponentPathToRowTag(
            DataGridView gridBom,
            ITableAnnotation swTable,
            int tableRow,
            int gridRow,
            string fileName,
            List<string> searchDirectories)
        {
            IBomTableAnnotation bomTable = swTable as IBomTableAnnotation;
            if (bomTable == null)
                return null;

            try
            {
                object[] comps = bomTable.GetComponents2(tableRow, "") as object[];

                if (comps != null && comps.Length > 0)
                {
                    gridBom.Rows[gridRow].Tag = comps;
                    return comps;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    "[BOM LOAD] GetComponents2 failed row=" + tableRow
                    + ", error=" + ex.Message);
            }

            string fallbackPartPath = GetBomRowPartPath(bomTable, tableRow);
            string fallbackSource = "model path";
            if (string.IsNullOrWhiteSpace(fallbackPartPath))
            {
                fallbackPartPath = FindPartPathByFileName(fileName, searchDirectories);
                fallbackSource = "file name";
            }

            if (!string.IsNullOrWhiteSpace(fallbackPartPath))
            {
                gridBom.Rows[gridRow].Tag = fallbackPartPath;
                Debug.WriteLine(
                    "[BOM LOAD] use " + fallbackSource + " fallback row=" + tableRow
                    + ", path=" + fallbackPartPath);
            }
            else
            {
                Debug.WriteLine(
                    "[BOM LOAD] no component or model path row=" + tableRow);
            }

            return null;
        }

        private string GetBomRowPartPath(
            IBomTableAnnotation bomTable,
            int tableRow)
        {
            try
            {
                string itemNumber;
                string partNumber;
                Array paths = bomTable.GetModelPathNames(
                    tableRow,
                    out itemNumber,
                    out partNumber) as Array;

                if (paths == null)
                    return "";

                foreach (object pathObject in paths)
                {
                    string path = Convert.ToString(pathObject ?? "");
                    if (string.Equals(
                        Path.GetExtension(path),
                        ".SLDPRT",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        return path;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    "[BOM LOAD] GetModelPathNames failed row=" + tableRow
                    + ", error=" + ex.Message);
            }

            return "";
        }

        private List<string> GetAssemblySearchDirectories(ITableAnnotation swTable)
        {
            List<string> directories = new List<string>();

            ModelDoc2 activeModel = swApp?.ActiveDoc as ModelDoc2;
            AddDirectoryFromPath(directories, activeModel?.GetPathName());

            IBomTableAnnotation bomTable = swTable as IBomTableAnnotation;
            try
            {
                BomFeature bomFeature = bomTable?.BomFeature as BomFeature;
                AddDirectoryFromPath(directories, bomFeature?.GetReferencedModelName());
            }
            catch
            {
            }

            DrawingDoc drawing = activeModel as DrawingDoc;
            if (drawing != null)
            {
                SolidWorks.Interop.sldworks.View view =
                    drawing.GetFirstView() as SolidWorks.Interop.sldworks.View;

                while (view != null)
                {
                    ModelDoc2 referencedModel = view.ReferencedDocument as ModelDoc2;
                    AddDirectoryFromPath(directories, referencedModel?.GetPathName());
                    view = view.GetNextView() as SolidWorks.Interop.sldworks.View;
                }
            }

            Debug.WriteLine("[BOM LOAD] assembly search directories=" + directories.Count);
            return directories;
        }

        private void AddDirectoryFromPath(List<string> directories, string path)
        {
            if (directories == null || string.IsNullOrWhiteSpace(path))
                return;

            try
            {
                string directory = Path.GetDirectoryName(path);
                if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                    return;

                foreach (string existing in directories)
                {
                    if (string.Equals(existing, directory, StringComparison.OrdinalIgnoreCase))
                        return;
                }

                directories.Add(directory);
            }
            catch
            {
            }
        }

        private string FindAssemblyPathByFileName(string fileName, List<string> searchDirectories)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return "";

            string cleanedName = fileName.Trim();
            if (IsAssemblyPath(cleanedName) && Path.IsPathRooted(cleanedName) && File.Exists(cleanedName))
                return Path.GetFullPath(cleanedName);

            string baseName = Path.GetFileNameWithoutExtension(cleanedName);
            if (string.IsNullOrWhiteSpace(baseName))
                baseName = cleanedName;

            string assemblyFileName = baseName + ".SLDASM";
            string cachedPath;
            if (assemblyPathCache.TryGetValue(assemblyFileName, out cachedPath))
                return cachedPath;

            if (searchDirectories != null)
            {
                foreach (string directory in searchDirectories)
                {
                    string directPath = Path.Combine(directory, assemblyFileName);
                    if (File.Exists(directPath))
                    {
                        string path = Path.GetFullPath(directPath);
                        Debug.WriteLine("[BOM LOAD] BOM assembly resolved=" + path);
                        assemblyPathCache[assemblyFileName] = path;
                        return path;
                    }
                }
            }

            assemblyPathCache[assemblyFileName] = "";
            return "";
        }

        private string FindPartPathByFileName(string fileName, List<string> searchDirectories)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return "";

            string cleanedName = fileName.Trim();
            if (string.Equals(
                    Path.GetExtension(cleanedName),
                    ".SLDPRT",
                    StringComparison.OrdinalIgnoreCase)
                && Path.IsPathRooted(cleanedName)
                && File.Exists(cleanedName))
            {
                return Path.GetFullPath(cleanedName);
            }

            string baseName = Path.GetFileNameWithoutExtension(cleanedName);
            if (string.IsNullOrWhiteSpace(baseName))
                baseName = cleanedName;

            string partFileName = baseName + ".SLDPRT";
            if (searchDirectories == null)
                return "";

            foreach (string directory in searchDirectories)
            {
                try
                {
                    string directPath = Path.Combine(directory, partFileName);
                    if (!File.Exists(directPath))
                        continue;

                    string path = Path.GetFullPath(directPath);
                    Debug.WriteLine("[BOM LOAD] BOM part resolved by file name=" + path);
                    return path;
                }
                catch
                {
                }
            }

            return "";
        }

        private bool IsDash02Assembly(string path)
        {
            if (!IsAssemblyPath(path))
                return false;

            string name = Path.GetFileNameWithoutExtension(path) ?? "";
            return name.IndexOf("-02-", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void AddUniquePath(List<string> paths, string path)
        {
            if (paths == null || string.IsNullOrWhiteSpace(path))
                return;

            foreach (string existing in paths)
            {
                if (string.Equals(existing, path, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            Debug.WriteLine("[BOM LOAD] scan seed -02- assembly=" + path);
            paths.Add(path);
        }

        private void AddSubAssemblyRowsFromBomAssembly(
            DataGridView gridBom,
            string assemblyPath,
            HashSet<string> rowAssemblyPaths,
            HashSet<string> traversedAssemblyPaths)
        {
            if (!IsAssemblyPath(assemblyPath))
                return;

            ModelDoc2 model = swApp.GetOpenDocumentByName(assemblyPath) as ModelDoc2;
            if (model == null)
                model = OpenAssembly(assemblyPath);

            if (model == null)
            {
                Debug.WriteLine("[BOM LOAD] cannot open -02- assembly=" + assemblyPath);
                return;
            }

            Debug.WriteLine("[BOM LOAD] scan -02- assembly=" + assemblyPath);
            AddSubAssemblyRowsFromModel(gridBom, model, assemblyPath, rowAssemblyPaths, traversedAssemblyPaths);
        }

        private void AddSubAssemblyRowsFromModel(
            DataGridView gridBom,
            ModelDoc2 model,
            string assemblyPath,
            HashSet<string> rowAssemblyPaths,
            HashSet<string> traversedAssemblyPaths)
        {
            if (model == null || !IsAssemblyPath(assemblyPath) || !traversedAssemblyPaths.Add(assemblyPath))
                return;

            object[] children = GetTopLevelAssemblyComponents(model, assemblyPath);
            if (children == null)
                return;

            int childIndex = 0;
            foreach (object item in children)
            {
                Application.DoEvents();
                if (IsCancellationRequested())
                    return;
                childIndex++;
                Component2 child = item as Component2;
                if (child == null)
                {
                    DebugBomVerbose("[BOM LOAD][DBG] child[" + childIndex + "] parent=" + Path.GetFileNameWithoutExtension(assemblyPath) + ", not Component2");
                    continue;
                }

                bool suppressed = IsSuppressed(child);
                string childName = GetComponentName(child);
                string childPath = "";
                try
                {
                    childPath = child.GetPathName();
                }
                catch (Exception ex)
                {
                    DebugBomVerbose("[BOM LOAD][DBG] child[" + childIndex + "] parent=" + Path.GetFileNameWithoutExtension(assemblyPath) + ", name=" + childName + ", GetPathName failed=" + ex.Message);
                }

                bool isAssembly = IsAssemblyPath(childPath);
                bool isEnvelope = IsEnvelopeComponent(child);
                bool excludeFromBom = IsExcludeFromBomComponent(child);
                DebugBomVerbose("[BOM LOAD][DBG] child[" + childIndex + "] parent=" + Path.GetFileNameWithoutExtension(assemblyPath) + ", name=" + childName + ", suppressed=" + suppressed + ", envelope=" + isEnvelope + ", excludeFromBom=" + excludeFromBom + ", isAssembly=" + isAssembly + ", path=" + childPath);

                if (suppressed || isEnvelope || excludeFromBom)
                {
                    Debug.WriteLine("[BOM LOAD] skip suppressed/envelope/exclude BOM component="
                        + childName + ", suppressed=" + suppressed + ", path=" + childPath);
                    continue;
                }

                if (!isAssembly)
                    continue;

                ModelDoc2 childModel = child.GetModelDoc2() as ModelDoc2;
                if (childModel == null)
                    childModel = OpenAssembly(childPath);
                if (childModel == null)
                    DebugBomVerbose("[BOM LOAD][DBG] child assembly model null path=" + childPath);

                if (!rowAssemblyPaths.Contains(childPath))
                {
                    AddComponentRow(gridBom, child, childModel, childPath, rowAssemblyPaths);
                    Debug.WriteLine("[BOM LOAD] add sub assembly row path=" + childPath);
                }

                AddSubAssemblyRowsFromModel(gridBom, childModel, childPath, rowAssemblyPaths, traversedAssemblyPaths);
            }
        }

        private object[] GetTopLevelAssemblyComponents(ModelDoc2 model, string path)
        {
            AssemblyDoc assembly = model as AssemblyDoc;
            if (assembly == null)
            {
                Debug.WriteLine("[BOM LOAD] no AssemblyDoc for path=" + path);
                return null;
            }

            object[] children = null;
            try
            {
                ConfigurationManager manager = model.ConfigurationManager as ConfigurationManager;
                Configuration configuration = manager == null
                    ? null : manager.ActiveConfiguration as Configuration;
                Component2 root = configuration == null
                    ? null : configuration.GetRootComponent3(true) as Component2;
                children = root == null ? null : root.GetChildren() as object[];
                Debug.WriteLine("[BOM LOAD] configuration tree="
                    + (configuration == null ? "" : configuration.Name)
                    + ", path=" + path + ", count=" + (children == null ? 0 : children.Length));
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[BOM LOAD] configuration tree failed path=" + path
                    + ", error=" + ex.Message);
            }
            if (children == null)
                children = assembly.GetComponents(true) as object[];
            Debug.WriteLine("[BOM LOAD] GetComponents(true top) path=" + path + ", count=" + (children == null ? 0 : children.Length));

            if (children == null || children.Length == 0)
            {
                ResolveAssemblyLightweight(assembly, path);
                children = assembly.GetComponents(true) as object[];
                Debug.WriteLine("[BOM LOAD] GetComponents(true retry) path=" + path
                    + ", count=" + (children == null ? 0 : children.Length));
            }

            return children;
        }

        private void AddSeedComponents(
            object[] components,
            List<Component2> seedComponents,
            HashSet<string> rowModelPaths)
        {
            if (components == null)
                return;

            foreach (object item in components)
            {
                Component2 component = item as Component2;
                if (component == null)
                    continue;

                seedComponents.Add(component);
                string path = component.GetPathName();
                if (IsModelPath(path))
                    rowModelPaths.Add(path);
            }
        }

        private void AddRootAssemblySubAssemblyRows(
            DataGridView gridBom,
            ITableAnnotation swTable,
            HashSet<string> rowModelPaths,
            HashSet<string> traversedAssemblyPaths)
        {
            IBomTableAnnotation bomTable = swTable as IBomTableAnnotation;
            ModelDoc2 rootModel = GetRootAssemblyFromBom(bomTable);
            if (rootModel == null)
                rootModel = GetRootAssemblyFromActiveDrawing();

            string modelPath = rootModel?.GetPathName();
            if (!IsAssemblyPath(modelPath))
            {
                Debug.WriteLine("[BOM LOAD] skip root assembly: modelPath=" + modelPath);
                return;
            }

            Debug.WriteLine("[BOM LOAD] root assembly=" + modelPath);
            AddAssemblyChildrenFromModel(gridBom, rootModel, modelPath, rowModelPaths, traversedAssemblyPaths);
        }

        private ModelDoc2 GetRootAssemblyFromBom(IBomTableAnnotation bomTable)
        {
            if (bomTable == null)
                return null;

            try
            {
                BomFeature bomFeature = bomTable.BomFeature as BomFeature;
                string modelPath = bomFeature?.GetReferencedModelName();
                if (!IsAssemblyPath(modelPath))
                    return null;

                ModelDoc2 rootModel = swApp.GetOpenDocumentByName(modelPath) as ModelDoc2;
                return rootModel ?? OpenAssembly(modelPath);
            }
            catch
            {
                return null;
            }
        }

        private ModelDoc2 GetRootAssemblyFromActiveDrawing()
        {
            ModelDoc2 activeModel = swApp?.ActiveDoc as ModelDoc2;
            DrawingDoc drawing = activeModel as DrawingDoc;
            if (drawing == null)
                return null;

            SolidWorks.Interop.sldworks.View view =
                drawing.GetFirstView() as SolidWorks.Interop.sldworks.View;

            while (view != null)
            {
                ModelDoc2 referencedModel = view.ReferencedDocument as ModelDoc2;
                string modelPath = referencedModel?.GetPathName();
                if (IsAssemblyPath(modelPath))
                {
                    Debug.WriteLine("[BOM LOAD] root from drawing view=" + view.Name + ", path=" + modelPath);
                    return referencedModel;
                }

                view = view.GetNextView() as SolidWorks.Interop.sldworks.View;
            }

            return null;
        }

        private void AddAssemblyChildrenFromModel(
            DataGridView gridBom,
            ModelDoc2 model,
            string path,
            HashSet<string> rowModelPaths,
            HashSet<string> traversedAssemblyPaths)
        {
            if (model == null || !IsAssemblyPath(path) || !traversedAssemblyPaths.Add(path))
                return;

            if (!rowModelPaths.Contains(path))
                AddComponentRow(gridBom, null, model, path, rowModelPaths);

            object[] children = GetAssemblyComponents(model, path);
            if (children == null)
                return;

            foreach (object item in children)
            {
                Component2 child = item as Component2;
                if (child != null)
                    AddComponentRowsRecursive(gridBom, child, rowModelPaths, traversedAssemblyPaths);
            }
        }

        private void AddComponentRowsRecursive(
            DataGridView gridBom,
            Component2 component,
            HashSet<string> rowModelPaths,
            HashSet<string> traversedAssemblyPaths)
        {
            if (gridBom == null || component == null)
                return;
            Application.DoEvents();
            if (IsCancellationRequested())
                return;

            if (IsSuppressed(component))
            {
                Debug.WriteLine("[BOM LOAD] skip suppressed component=" + GetComponentName(component));
                return;
            }

            string path = component.GetPathName();
            if (!IsModelPath(path))
            {
                Debug.WriteLine("[BOM LOAD] skip non model component=" + GetComponentName(component) + ", path=" + path);
                return;
            }

            ModelDoc2 model = component.GetModelDoc2() as ModelDoc2;
            if (model == null && IsAssemblyPath(path))
                model = OpenAssembly(path);
            if (model == null && IsPartPath(path))
                model = OpenPart(path);

            if (!rowModelPaths.Contains(path))
            {
                AddComponentRow(gridBom, component, model, path, rowModelPaths);
                Debug.WriteLine("[BOM LOAD] add child row path=" + path);
            }

            if (!IsAssemblyPath(path) || !traversedAssemblyPaths.Add(path))
                return;

            object[] children = GetAssemblyComponents(model, path);
            if (children == null)
                return;

            foreach (object item in children)
            {
                Component2 child = item as Component2;
                if (child != null)
                    AddComponentRowsRecursive(gridBom, child, rowModelPaths, traversedAssemblyPaths);
            }
        }

        private void AddComponentRow(
            DataGridView gridBom,
            Component2 component,
            ModelDoc2 model,
            string path,
            HashSet<string> rowModelPaths)
        {
            string secondaryValue = activeBomContext == BomCommandContext.Unit
                ? GetComponentCustomProperty(component, model, "合番")
                : GetCustomProperty(model, "材質");
            string thicknessValue = activeBomContext == BomCommandContext.Unit
                ? ""
                : GetCustomProperty(model, "板厚");

            int rowIndex = gridBom.Rows.Add(
                true,
                GetComponentCustomProperty(component, model, "部品番号"),
                secondaryValue,
                thicknessValue,
                "1",
                Path.GetFileNameWithoutExtension(path)
            );

            if (component != null)
                gridBom.Rows[rowIndex].Tag = new object[] { component };
            rowModelPaths.Add(path);
        }

        private object[] GetAssemblyComponents(ModelDoc2 model, string path)
        {
            AssemblyDoc assembly = model as AssemblyDoc;
            if (assembly == null)
            {
                Debug.WriteLine("[BOM LOAD] no AssemblyDoc for path=" + path);
                return null;
            }

            object[] children = assembly.GetComponents(false) as object[];
            Debug.WriteLine("[BOM LOAD] GetComponents(false) path=" + path + ", count=" + (children == null ? 0 : children.Length));
            if (children != null && children.Length > 0)
                return children;

            children = assembly.GetComponents(true) as object[];
            Debug.WriteLine("[BOM LOAD] GetComponents(true) path=" + path + ", count=" + (children == null ? 0 : children.Length));
            if (children != null && children.Length > 0)
                return children;

            ResolveAssemblyLightweight(assembly, path);
            children = assembly.GetComponents(false) as object[];
            Debug.WriteLine("[BOM LOAD] GetComponents(false retry) path=" + path + ", count=" + (children == null ? 0 : children.Length));
            return children;
        }

        private void ResolveAssemblyLightweight(AssemblyDoc assembly, string path)
        {
            try
            {
                assembly.ResolveAllLightWeightComponents(true);
                Debug.WriteLine("[BOM LOAD] resolved lightweight path=" + path);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[BOM LOAD] resolve lightweight failed path=" + path + ", error=" + ex.Message);
            }
        }

        private bool IsSuppressed(Component2 component)
        {
            if (component == null)
                return true;
            try
            {
                return component.GetSuppression2()
                    == (int)swComponentSuppressionState_e.swComponentSuppressed;
            }
            catch { }
            try
            {
                return component.GetSuppression()
                    == (int)swComponentSuppressionState_e.swComponentSuppressed;
            }
            catch { return false; }
        }

        private bool IsEnvelopeComponent(Component2 component)
        {
            if (component == null)
                return false;

            try
            {
                return component.IsEnvelope();
            }
            catch
            {
                return false;
            }
        }

        private bool IsExcludeFromBomComponent(Component2 component)
        {
            if (component == null)
                return false;

            try
            {
                return component.ExcludeFromBOM;
            }
            catch
            {
                return false;
            }
        }

        private string GetComponentName(Component2 component)
        {
            try
            {
                return component?.Name2 ?? "";
            }
            catch
            {
                return "";
            }
        }

        private bool IsAssemblyPath(string path)
        {
            return !string.IsNullOrWhiteSpace(path) &&
                string.Equals(Path.GetExtension(path), ".SLDASM", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsModelPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            string extension = Path.GetExtension(path);
            return string.Equals(extension, ".SLDASM", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".SLDPRT", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsPartPath(string path)
        {
            return !string.IsNullOrWhiteSpace(path) &&
                string.Equals(Path.GetExtension(path), ".SLDPRT", StringComparison.OrdinalIgnoreCase);
        }

        private ModelDoc2 OpenAssembly(string assemblyPath)
        {
            ModelDoc2 openDoc = swApp.GetOpenDocumentByName(assemblyPath) as ModelDoc2;
            if (openDoc != null)
                return openDoc;

            int errors = 0;
            int warnings = 0;
            bool restoreVisibility = false;
            try
            {
                swApp.DocumentVisible(false, (int)swDocumentTypes_e.swDocASSEMBLY);
                restoreVisibility = true;

                ModelDoc2 opened = swApp.OpenDoc6(
                    assemblyPath,
                    (int)swDocumentTypes_e.swDocASSEMBLY,
                    (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
                    "",
                    ref errors,
                    ref warnings) as ModelDoc2;
                if (opened != null)
                    silentlyOpenedDocumentPaths.Add(assemblyPath);
                return opened;
            }
            finally
            {
                if (restoreVisibility)
                    swApp.DocumentVisible(true, (int)swDocumentTypes_e.swDocASSEMBLY);
            }
        }

        private ModelDoc2 OpenPart(string partPath)
        {
            ModelDoc2 openDoc = swApp.GetOpenDocumentByName(partPath) as ModelDoc2;
            if (openDoc != null)
                return openDoc;

            int errors = 0;
            int warnings = 0;
            bool restoreVisibility = false;
            try
            {
                swApp.DocumentVisible(false, (int)swDocumentTypes_e.swDocPART);
                restoreVisibility = true;

                ModelDoc2 opened = swApp.OpenDoc6(
                    partPath,
                    (int)swDocumentTypes_e.swDocPART,
                    (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
                    "",
                    ref errors,
                    ref warnings) as ModelDoc2;
                if (opened != null)
                    silentlyOpenedDocumentPaths.Add(partPath);
                return opened;
            }
            finally
            {
                if (restoreVisibility)
                    swApp.DocumentVisible(true, (int)swDocumentTypes_e.swDocPART);
            }
        }

        private string GetCustomProperty(ModelDoc2 model, string propertyName)
        {
            return GetCustomProperty(model, "", propertyName);
        }

        private string GetCustomProperty(ModelDoc2 model, string configurationName, string propertyName)
        {
            if (model == null || string.IsNullOrWhiteSpace(propertyName))
                return "";

            try
            {
                CustomPropertyManager propMgr = model.Extension.get_CustomPropertyManager(configurationName ?? "");
                string valOut;
                string resolvedValOut;
                bool wasResolved;
                bool linkToProperty;
                propMgr.Get6(propertyName, false, out valOut, out resolvedValOut, out wasResolved, out linkToProperty);
                return !string.IsNullOrWhiteSpace(resolvedValOut) ? resolvedValOut : (valOut ?? "");
            }
            catch
            {
                return "";
            }
        }

        private string GetComponentCustomProperty(object[] components, string propertyName)
        {
            if (components == null)
                return "";

            foreach (object item in components)
            {
                if (IsCancellationRequested())
                    return "";
                Component2 component = item as Component2;
                if (component == null)
                    continue;

                string value = GetComponentCustomProperty(component, null, propertyName);
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }

            return "";
        }

        private string GetComponentCustomProperty(Component2 component, ModelDoc2 knownModel, string propertyName)
        {
            ModelDoc2 model = knownModel;
            string referencedConfiguration = "";
            string path = "";

            try
            {
                if (component != null)
                {
                    referencedConfiguration = component.ReferencedConfiguration ?? "";
                    path = component.GetPathName();
                }

                string cacheKey = (path ?? "").Trim().ToUpperInvariant() + "|"
                    + (referencedConfiguration ?? "").Trim().ToUpperInvariant() + "|"
                    + (propertyName ?? "").Trim().ToUpperInvariant();
                string cachedValue;
                if (!string.IsNullOrWhiteSpace(path)
                    && componentPropertyCache.TryGetValue(cacheKey, out cachedValue))
                    return cachedValue;

                if (component != null && model == null)
                    model = component.GetModelDoc2() as ModelDoc2;

                if (model == null && !string.IsNullOrWhiteSpace(path))
                {
                    model = swApp.GetOpenDocumentByName(path) as ModelDoc2;
                    if (model == null)
                    {
                        model = IsAssemblyPath(path) ? OpenAssembly(path) : OpenPart(path);
                    }
                }

                string value = GetCustomProperty(model, referencedConfiguration, propertyName);
                if (string.IsNullOrWhiteSpace(value))
                    value = GetCustomProperty(model, "", propertyName);

                if (!string.IsNullOrWhiteSpace(path))
                    componentPropertyCache[cacheKey] = value ?? "";

                return value;
            }
            catch
            {
                return "";
            }
        }

        [Conditional("BOM_LOAD_VERBOSE")]
        private static void DebugBomVerbose(string message)
        {
            Debug.WriteLine(message);
        }

        private void CloseSilentlyOpenedDocuments()
        {
            if (swApp == null || silentlyOpenedDocumentPaths.Count == 0)
                return;

            List<string> paths = new List<string>(silentlyOpenedDocumentPaths);
            paths.Reverse();
            foreach (string path in paths)
            {
                try
                {
                    ModelDoc2 document = swApp.GetOpenDocumentByName(path) as ModelDoc2;
                    if (document != null)
                        swApp.CloseDoc(document.GetTitle());
                }
                catch { }
            }
            silentlyOpenedDocumentPaths.Clear();
        }

        private bool IsCancellationRequested()
        {
            try
            {
                return cancellationRequested != null && cancellationRequested();
            }
            catch
            {
                return false;
            }
        }

        private string NormalizeHeaderText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            string text = value.Trim();
            while (true)
            {
                int start = text.IndexOf('<');
                int end = text.IndexOf('>');
                if (start < 0 || end <= start)
                    break;

                text = text.Remove(start, end - start + 1);
            }

            return text.Trim();
        }

        public void ClearBomGrid(DataGridView gridBom)
        {
            if (gridBom == null)
                return;

            gridBom.Rows.Clear();
        }
    }
}
