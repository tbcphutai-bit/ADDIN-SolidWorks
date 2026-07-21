using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands
{
    public class XepUnitDrawing
    {
        private readonly ISldWorks swApp;

        public XepUnitDrawing(ISldWorks app)
        {
            swApp = app;
        }

        public void Run(
            DataGridView gridBom,
            Action<int> beginProgress,
            Action<int, int> updateProgress,
            Action finishProgress,
            Func<bool> isCancellationRequested = null)
        {
            ModelDoc2 activeModel = swApp?.ActiveDoc as ModelDoc2;
            if (activeModel == null ||
                activeModel.GetType() != (int)swDocumentTypes_e.swDocDRAWING)
            {
                MessageBox.Show("Hay mo drawing va chon drawing view assembly.", "XEP UNIT", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            IBomTableAnnotation selectedBomTable = GetSelectedBomTable(activeModel) ?? GetFirstBomTable(activeModel);

            int activeSortedTableCount = 0;
            bool gridSorted = SortGridByBuhinNo(gridBom);

            List<string> drawingPaths = new List<string>();
            int checkedRowCount = GetCheckedRowCount(gridBom);
            beginProgress?.Invoke(Math.Max(1, checkedRowCount));
            try
            {
                CollectDrawingPathsFromCheckedRows(gridBom, selectedBomTable, activeModel, drawingPaths);
            }
            finally
            {
                finishProgress?.Invoke();
            }
            if (IsCancellationRequested(isCancellationRequested))
                return;

            if (drawingPaths.Count == 0)
            {
                if (activeSortedTableCount > 0 || gridSorted)
                {
                    MessageBox.Show(
                        "Da sap xep bang hien tai." + System.Environment.NewLine +
                        "Da sap xep bang: " + activeSortedTableCount + System.Environment.NewLine +
                        "Grid da sap xep: " + (gridSorted ? "Yes" : "No") + System.Environment.NewLine +
                        "Khong co dong nao duoc tick nen khong mo drawing con.",
                        "XEP UNIT",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                MessageBox.Show("Khong tim thay drawing tu cac dong da tick. Kiem tra cot file name hoac drawing cung thu muc.", "XEP UNIT", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            int openedDrawingCount = 0;
            int updatedDrawingCount = 0;
            int sortedTableCount = 0;
            int savedDrawingCount = 0;
            int closedDrawingCount = 0;
            int skippedDrawingCount = 0;

            int totalCount = drawingPaths.Count;
            int currentCount = 0;
            beginProgress?.Invoke(totalCount);

            try
            {
                foreach (string drawingPath in drawingPaths)
                {
                    currentCount++;
                    updateProgress?.Invoke(currentCount, totalCount);
                    Application.DoEvents();
                    if (IsCancellationRequested(isCancellationRequested))
                        break;

                    Debug.WriteLine("[XEP UNIT] Drawing=" + drawingPath);
                    if (string.IsNullOrWhiteSpace(drawingPath) || !File.Exists(drawingPath))
                    {
                        Debug.WriteLine("[XEP UNIT] Skip: drawing not found");
                        skippedDrawingCount++;
                        continue;
                    }

                    bool openedByCommand;
                    ModelDoc2 drawingModel = OpenDrawing(drawingPath, out openedByCommand);
                    if (drawingModel == null)
                    {
                        Debug.WriteLine("[XEP UNIT] Skip: cannot open drawing");
                        skippedDrawingCount++;
                        continue;
                    }

                    openedDrawingCount++;
                    int sortedTables = SortBuhinNoInDrawing(drawingModel);
                    Debug.WriteLine("[XEP UNIT] Sorted tables=" + sortedTables);
                    if (sortedTables > 0)
                    {
                        updatedDrawingCount++;
                        sortedTableCount += sortedTables;
                        RebuildDrawing(drawingModel);
                        drawingModel.GraphicsRedraw2();
                        if (SaveDrawing(drawingModel))
                            savedDrawingCount++;
                    }

                    if (openedByCommand && CloseDrawing(drawingModel))
                        closedDrawingCount++;
                }
            }
            finally
            {
                finishProgress?.Invoke();
            }

            if (IsCancellationRequested(isCancellationRequested))
                return;

            MessageBox.Show(
                "XEP UNIT xong." + System.Environment.NewLine +
                "Da sap xep bang hien tai: " + activeSortedTableCount + System.Environment.NewLine +
                "Grid da sap xep: " + (gridSorted ? "Yes" : "No") + System.Environment.NewLine +
                "Da mo drawing: " + openedDrawingCount + System.Environment.NewLine +
                "Da sap xep drawing: " + updatedDrawingCount + System.Environment.NewLine +
                "Da sap xep bang: " + sortedTableCount + System.Environment.NewLine +
                "Da save drawing: " + savedDrawingCount + System.Environment.NewLine +
                "Da dong drawing: " + closedDrawingCount + System.Environment.NewLine +
                "Bo qua: " + skippedDrawingCount,
                "XEP UNIT",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private static bool IsCancellationRequested(Func<bool> callback)
        {
            try
            {
                return callback != null && callback();
            }
            catch
            {
                return false;
            }
        }

        private IBomTableAnnotation GetSelectedBomTable(ModelDoc2 drawingModel)
        {
            SelectionMgr selMgr = drawingModel.SelectionManager as SelectionMgr;
            if (selMgr == null)
                return null;

            int count = selMgr.GetSelectedObjectCount2(-1);
            for (int i = 1; i <= count; i++)
            {
                int type = selMgr.GetSelectedObjectType3(i, -1);
                if (type != (int)swSelectType_e.swSelANNOTATIONTABLES)
                    continue;

                object selected = selMgr.GetSelectedObject6(i, -1);
                IBomTableAnnotation bomTable = selected as IBomTableAnnotation;
                if (bomTable != null)
                    return bomTable;

                Annotation annotation = selected as Annotation;
                bomTable = annotation?.GetSpecificAnnotation() as IBomTableAnnotation;
                if (bomTable != null)
                    return bomTable;

                ITableAnnotation table = selected as ITableAnnotation;
                bomTable = table as IBomTableAnnotation;
                if (bomTable != null)
                    return bomTable;
            }

            return null;
        }

        private IBomTableAnnotation GetFirstBomTable(ModelDoc2 drawingModel)
        {
            DrawingDoc drawing = drawingModel as DrawingDoc;
            if (drawing == null)
                return null;

            SolidWorks.Interop.sldworks.View view =
                drawing.GetFirstView() as SolidWorks.Interop.sldworks.View;

            while (view != null)
            {
                ITableAnnotation table = view.GetFirstTableAnnotation() as ITableAnnotation;
                while (table != null)
                {
                    IBomTableAnnotation bomTable = table as IBomTableAnnotation;
                    if (bomTable != null)
                        return bomTable;

                    table = table.GetNext() as ITableAnnotation;
                }

                view = view.GetNextView() as SolidWorks.Interop.sldworks.View;
            }

            return null;
        }

        private void CollectAssemblyPathsFromCheckedRows(
            DataGridView gridBom,
            IBomTableAnnotation selectedBomTable,
            ModelDoc2 activeDrawing,
            List<string> assemblyPaths,
            HashSet<string> visited,
            Action<int, int> updateProgress)
        {
            if (gridBom == null)
                return;

            int totalCount = Math.Max(1, GetCheckedRowCount(gridBom));
            int currentCount = 0;
            List<string> searchDirectories = GetDrawingSearchDirectories(selectedBomTable, activeDrawing);

            foreach (DataGridViewRow row in gridBom.Rows)
            {
                if (row.IsNewRow || !Convert.ToBoolean(row.Cells[0].Value ?? false))
                    continue;

                currentCount++;
                updateProgress?.Invoke(currentCount, totalCount);
                Application.DoEvents();

                if (CollectAssemblyPathsFromRowTag(row, assemblyPaths, visited))
                    continue;

                string fileName = GetGridCellText(row, 5);
                string assemblyPath = FindAssemblyPathByFileName(fileName, searchDirectories);
                if (string.IsNullOrWhiteSpace(assemblyPath))
                {
                    Debug.WriteLine("[XEP UNIT] Row assembly fallback failed. file=" + fileName);
                    continue;
                }

                ModelDoc2 assemblyModel = swApp.GetOpenDocumentByName(assemblyPath) as ModelDoc2;
                if (assemblyModel == null)
                    assemblyModel = OpenAssembly(assemblyPath);

                Debug.WriteLine("[XEP UNIT] Row assembly fallback path=" + assemblyPath);
                CollectAssemblyPaths(assemblyModel, assemblyPaths, visited);
            }
        }

        private int GetCheckedRowCount(DataGridView gridBom)
        {
            if (gridBom == null)
                return 0;

            int count = 0;
            foreach (DataGridViewRow row in gridBom.Rows)
            {
                if (!row.IsNewRow && Convert.ToBoolean(row.Cells[0].Value ?? false))
                    count++;
            }

            return count;
        }

        private bool SortGridByBuhinNo(DataGridView gridBom)
        {
            if (gridBom == null || gridBom.Rows.Count <= 1 || gridBom.Columns.Count <= 1)
                return false;

            try
            {
                if (gridBom.IsCurrentCellDirty)
                    gridBom.CommitEdit(DataGridViewDataErrorContexts.Commit);

                gridBom.Sort(new BomGridBuhinNoComparer(1));
                Debug.WriteLine("[XEP UNIT] Grid sorted by BuhinNo");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[XEP UNIT] Grid sort error: " + ex.Message);
                return false;
            }
        }

        private bool CollectAssemblyPathsFromRowTag(
            DataGridViewRow row,
            List<string> assemblyPaths,
            HashSet<string> visited)
        {
            object[] components = row.Tag as object[];
            if (components == null)
            {
                Debug.WriteLine("[XEP UNIT] Row skip: row.Tag has no component array");
                return false;
            }

            bool collected = false;
            foreach (object item in components)
            {
                Component2 component = item as Component2;
                if (component == null)
                    continue;

                if (CollectAssemblyPathsFromComponent(component, assemblyPaths, visited))
                    collected = true;
            }

            return collected;
        }

        private void CollectDrawingPathsFromCheckedRows(
            DataGridView gridBom,
            IBomTableAnnotation selectedBomTable,
            ModelDoc2 activeDrawing,
            List<string> drawingPaths)
        {
            if (gridBom == null)
                return;

            List<string> searchDirectories = GetDrawingSearchDirectories(selectedBomTable, activeDrawing);
            foreach (DataGridViewRow row in gridBom.Rows)
            {
                if (row.IsNewRow || !Convert.ToBoolean(row.Cells[0].Value ?? false))
                    continue;

                string fileName = GetGridCellText(row, 5);
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    Debug.WriteLine("[XEP UNIT] Row skip: file name is empty");
                    continue;
                }

                string drawingPath = FindDrawingPathByFileName(fileName, searchDirectories);
                if (string.IsNullOrWhiteSpace(drawingPath))
                {
                    Debug.WriteLine("[XEP UNIT] Row skip: drawing not found for file=" + fileName);
                    continue;
                }

                AddDrawingPathIfExists(drawingPath, drawingPaths);
            }
        }

        private string GetGridCellText(DataGridViewRow row, int columnIndex)
        {
            if (row == null || columnIndex < 0 || columnIndex >= row.Cells.Count)
                return "";

            return Convert.ToString(row.Cells[columnIndex].Value ?? "").Trim();
        }

        private List<string> GetDrawingSearchDirectories(IBomTableAnnotation selectedBomTable, ModelDoc2 activeDrawing)
        {
            List<string> directories = new List<string>();

            AddDirectoryFromPath(activeDrawing?.GetPathName(), directories);

            try
            {
                BomFeature bomFeature = selectedBomTable?.BomFeature as BomFeature;
                AddDirectoryFromPath(bomFeature?.GetReferencedModelName(), directories);
            }
            catch
            {
            }

            return directories;
        }

        private void AddDirectoryFromPath(string path, List<string> directories)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

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

        private string FindDrawingPathByFileName(string fileName, List<string> searchDirectories)
        {
            string baseName = Path.GetFileNameWithoutExtension(fileName ?? "");
            if (string.IsNullOrWhiteSpace(baseName))
                return "";

            foreach (string directory in searchDirectories)
            {
                string upperPath = Path.Combine(directory, baseName + ".SLDDRW");
                if (File.Exists(upperPath))
                    return upperPath;

                string lowerPath = Path.Combine(directory, baseName + ".slddrw");
                if (File.Exists(lowerPath))
                    return lowerPath;
            }

            return "";
        }

        private string FindAssemblyPathByFileName(string fileName, List<string> searchDirectories)
        {
            string baseName = Path.GetFileNameWithoutExtension(fileName ?? "");
            if (string.IsNullOrWhiteSpace(baseName))
                return "";

            foreach (string directory in searchDirectories)
            {
                string upperPath = Path.Combine(directory, baseName + ".SLDASM");
                if (File.Exists(upperPath))
                    return upperPath;

                string lowerPath = Path.Combine(directory, baseName + ".sldasm");
                if (File.Exists(lowerPath))
                    return lowerPath;
            }

            return "";
        }

        private void AddDrawingPathsFromAssemblies(List<string> assemblyPaths, List<string> drawingPaths)
        {
            foreach (string assemblyPath in assemblyPaths)
            {
                string drawingPath = GetDrawingPathFromModelPath(assemblyPath);
                AddDrawingPathIfExists(drawingPath, drawingPaths);
            }
        }

        private void AddDrawingPathIfExists(string drawingPath, List<string> drawingPaths)
        {
            if (string.IsNullOrWhiteSpace(drawingPath) || !File.Exists(drawingPath))
                return;

            foreach (string existing in drawingPaths)
            {
                if (string.Equals(existing, drawingPath, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            drawingPaths.Add(drawingPath);
        }

        private ModelDoc2 GetRootAssemblyModelFromBom(IBomTableAnnotation bomTable)
        {
            if (bomTable == null)
                return null;

            try
            {
                BomFeature bomFeature = bomTable.BomFeature as BomFeature;
                string modelPath = bomFeature?.GetReferencedModelName();
                if (string.IsNullOrWhiteSpace(modelPath))
                    return null;

                if (!string.Equals(Path.GetExtension(modelPath), ".SLDASM", StringComparison.OrdinalIgnoreCase))
                    return null;

                ModelDoc2 assemblyModel = swApp.GetOpenDocumentByName(modelPath) as ModelDoc2;
                if (assemblyModel == null)
                    assemblyModel = OpenAssembly(modelPath);

                return assemblyModel;
            }
            catch
            {
                return null;
            }
        }

        private void BuildAssemblyFileNameMap(
            ModelDoc2 model,
            Dictionary<string, ModelDoc2> assemblyByFileName,
            HashSet<string> visited)
        {
            if (model == null ||
                model.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
                return;

            string path = model.GetPathName();
            if (string.IsNullOrWhiteSpace(path) || !visited.Add(path))
                return;

            string key = NormalizeFileName(Path.GetFileNameWithoutExtension(path));
            if (!string.IsNullOrWhiteSpace(key) && !assemblyByFileName.ContainsKey(key))
                assemblyByFileName.Add(key, model);

            AssemblyDoc assembly = model as AssemblyDoc;
            object[] components = assembly?.GetComponents(false) as object[];
            if (components == null)
                return;

            foreach (object item in components)
            {
                Component2 component = item as Component2;
                if (component == null)
                    continue;

                string childPath = component.GetPathName();
                if (string.IsNullOrWhiteSpace(childPath) ||
                    !string.Equals(Path.GetExtension(childPath), ".SLDASM", StringComparison.OrdinalIgnoreCase))
                    continue;

                ModelDoc2 childModel = component.GetModelDoc2() as ModelDoc2;
                if (childModel == null)
                    childModel = OpenAssembly(childPath);

                BuildAssemblyFileNameMap(childModel, assemblyByFileName, visited);
            }
        }

        private ModelDoc2 FindAssemblyByFileName(string fileName, Dictionary<string, ModelDoc2> assemblyByFileName)
        {
            string key = NormalizeFileName(Path.GetFileNameWithoutExtension(fileName ?? ""));
            if (string.IsNullOrWhiteSpace(key))
                return null;

            ModelDoc2 exactModel;
            if (assemblyByFileName.TryGetValue(key, out exactModel))
                return exactModel;

            foreach (KeyValuePair<string, ModelDoc2> item in assemblyByFileName)
            {
                if (item.Key.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    key.IndexOf(item.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                    return item.Value;
            }

            return null;
        }

        private string NormalizeFileName(string value)
        {
            return (value ?? "").Trim().ToUpperInvariant();
        }

        private void CollectAssemblyPathsFromBom(
            IBomTableAnnotation bomTable,
            List<string> assemblyPaths,
            HashSet<string> visited)
        {
            ITableAnnotation table = bomTable as ITableAnnotation;
            if (table == null)
                return;

            for (int row = 1; row < table.RowCount; row++)
            {
                object[] components = bomTable.GetComponents2(row, "") as object[];
                if (components == null)
                    continue;

                foreach (object item in components)
                {
                    Component2 component = item as Component2;
                    if (component == null)
                        continue;

                    CollectAssemblyPathsFromComponent(component, assemblyPaths, visited);
                }
            }
        }

        private void CollectRootAssemblyPathFromBom(
            IBomTableAnnotation bomTable,
            List<string> assemblyPaths,
            HashSet<string> visited)
        {
            try
            {
                BomFeature bomFeature = bomTable.BomFeature as BomFeature;
                string modelPath = bomFeature?.GetReferencedModelName();
                if (string.IsNullOrWhiteSpace(modelPath))
                    return;

                if (!string.Equals(Path.GetExtension(modelPath), ".SLDASM", StringComparison.OrdinalIgnoreCase))
                    return;

                ModelDoc2 assemblyModel = swApp.GetOpenDocumentByName(modelPath) as ModelDoc2;
                if (assemblyModel == null)
                    assemblyModel = OpenAssembly(modelPath);

                CollectAssemblyPaths(assemblyModel, assemblyPaths, visited);
            }
            catch
            {
            }
        }

        private bool CollectAssemblyPathsFromComponent(
            Component2 component,
            List<string> assemblyPaths,
            HashSet<string> visited)
        {
            try
            {
                if (component.IsSuppressed() || component.ExcludeFromBOM)
                    return false;
            }
            catch
            {
                return false;
            }

            string path = component.GetPathName();
            Debug.WriteLine("[XEP UNIT] Checked component path=" + path);
            if (string.IsNullOrWhiteSpace(path) ||
                !string.Equals(Path.GetExtension(path), ".SLDASM", StringComparison.OrdinalIgnoreCase))
            {
                Debug.WriteLine("[XEP UNIT] Skip component: not assembly");
                return false;
            }

            ModelDoc2 model = component.GetModelDoc2() as ModelDoc2;
            if (model == null)
                model = OpenAssembly(path);

            CollectAssemblyPaths(model, assemblyPaths, visited);
            return true;
        }

        private void CollectAssemblyPaths(ModelDoc2 model, List<string> assemblyPaths, HashSet<string> visited)
        {
            if (model == null ||
                model.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
                return;

            string path = model.GetPathName();
            if (string.IsNullOrWhiteSpace(path) || !visited.Add(path))
                return;

            assemblyPaths.Add(path);

            AssemblyDoc assembly = model as AssemblyDoc;
            object[] components = assembly?.GetComponents(false) as object[];
            if (components == null)
                return;

            foreach (object item in components)
            {
                Component2 component = item as Component2;
                if (component == null)
                    continue;

                try
                {
                    if (component.IsSuppressed() || component.ExcludeFromBOM)
                        continue;
                }
                catch
                {
                    continue;
                }

                string childPath = component.GetPathName();
                ModelDoc2 childModel = component.GetModelDoc2() as ModelDoc2;
                if (childModel == null && !string.IsNullOrWhiteSpace(childPath))
                    childModel = OpenAssembly(childPath);

                if (childModel != null && childModel.GetType() == (int)swDocumentTypes_e.swDocASSEMBLY)
                    CollectAssemblyPaths(childModel, assemblyPaths, visited);
            }
        }

        private string GetDrawingPathFromModelPath(string modelPath)
        {
            if (string.IsNullOrWhiteSpace(modelPath))
                return "";

            return Path.ChangeExtension(modelPath, ".SLDDRW");
        }

        private ModelDoc2 OpenDrawing(string drawingPath, out bool openedByCommand)
        {
            openedByCommand = false;

            ModelDoc2 openDoc = swApp.GetOpenDocumentByName(drawingPath) as ModelDoc2;
            if (openDoc != null)
                return openDoc;

            int errors = 0;
            int warnings = 0;
            ModelDoc2 openedDoc = swApp.OpenDoc6(
                drawingPath,
                (int)swDocumentTypes_e.swDocDRAWING,
                (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
                "",
                ref errors,
                ref warnings) as ModelDoc2;

            openedByCommand = openedDoc != null;
            return openedDoc;
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

                return swApp.OpenDoc6(
                    assemblyPath,
                    (int)swDocumentTypes_e.swDocASSEMBLY,
                    (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
                    "",
                    ref errors,
                    ref warnings) as ModelDoc2;
            }
            finally
            {
                if (restoreVisibility)
                    swApp.DocumentVisible(true, (int)swDocumentTypes_e.swDocASSEMBLY);
            }
        }

        private int SortBuhinNoInDrawing(ModelDoc2 drawingModel)
        {
            int sortedTables = 0;

            foreach (ITableAnnotation table in GetTables(drawingModel))
            {
                int buhinNoCol = FindBuhinNoSortColumnIndex(table);
                if (buhinNoCol < 0)
                {
                    Debug.WriteLine("[XEP UNIT] Skip table: cannot find BuhinNo column");
                    continue;
                }

                IBomTableAnnotation bomTable = table as IBomTableAnnotation;
                if (bomTable == null)
                {
                    Debug.WriteLine("[XEP UNIT] Skip table: not BOM table");
                    continue;
                }

                if (SortBomTableByBuhinNo(bomTable, buhinNoCol))
                    sortedTables++;
            }

            return sortedTables;
        }

        private bool SortBomTableByBuhinNo(IBomTableAnnotation bomTable, int buhinNoCol)
        {
            try
            {
                BomTableSortData sortData = bomTable.GetBomTableSortData();
                if (sortData == null)
                    return false;

                sortData.set_ColumnIndex(0, buhinNoCol);
                sortData.set_Ascending(0, true);
                sortData.set_ColumnIndex(1, -1);
                sortData.set_Ascending(1, true);
                sortData.set_ColumnIndex(2, -1);
                sortData.set_Ascending(2, true);
                sortData.SortMethod = (int)swBomTableSortMethod_e.swBomTableSortMethod_Numeric;
                sortData.DoNotChangeItemNumber = false;
                sortData.SaveCurrentSortParameters = false;
                sortData.ItemGroups = new int[0];
                TryForceRenumberItemNumbers(sortData);

                ITableAnnotation table = bomTable as ITableAnnotation;
                Debug.WriteLine("[XEP UNIT] Sort before. column=" + buhinNoCol + ", preview=" + GetBomSortPreview(table, buhinNoCol));
                bool sorted = bomTable.Sort(sortData);
                bool manualSorted = SortTableRowsManually(table, buhinNoCol);

                Debug.WriteLine("[XEP UNIT] Sort result="
                    + sorted
                    + ", manualSorted="
                    + manualSorted
                    + ", column="
                    + buhinNoCol
                    + ", previewAfter="
                    + GetBomSortPreview(table, buhinNoCol));
                return sorted || manualSorted;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[XEP UNIT] Sort error: " + ex.Message);
                return false;
            }
        }

        private bool SortTableRowsManually(ITableAnnotation table, int sortColumn)
        {
            if (table == null || sortColumn < 0 || table.RowCount <= 2)
                return false;

            try
            {
                List<string> desiredKeys = new List<string>();
                for (int row = 1; row < table.RowCount; row++)
                    desiredKeys.Add((table.get_Text(row, sortColumn) ?? "").Trim());

                List<string> sortedKeys = new List<string>(desiredKeys);
                sortedKeys.Sort(CompareNaturalValues);

                bool alreadySorted = true;
                for (int i = 0; i < desiredKeys.Count; i++)
                {
                    if (!string.Equals(desiredKeys[i], sortedKeys[i], StringComparison.Ordinal))
                    {
                        alreadySorted = false;
                        break;
                    }
                }

                if (alreadySorted)
                {
                    Debug.WriteLine("[XEP UNIT] Manual row sort skip: already sorted.");
                    return false;
                }

                bool movedAny = false;
                for (int targetRow = 1; targetRow < table.RowCount; targetRow++)
                {
                    string targetKey = sortedKeys[targetRow - 1];
                    int currentRow = FindCurrentRowBySortKey(table, sortColumn, targetKey, targetRow);
                    if (currentRow < 0 || currentRow == targetRow)
                        continue;

                    bool moved = table.MoveRow(
                        currentRow,
                        (int)swMoveLocation_e.swMoveBefore,
                        targetRow);

                    Debug.WriteLine("[XEP UNIT] Manual row move. key="
                        + targetKey
                        + ", from="
                        + currentRow
                        + ", to="
                        + targetRow
                        + ", moved="
                        + moved);

                    movedAny |= moved;
                }

                return movedAny;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[XEP UNIT] Manual row sort failed: " + ex.Message);
                return false;
            }
        }

        private int FindCurrentRowBySortKey(ITableAnnotation table, int sortColumn, string targetKey, int startRow)
        {
            for (int row = startRow; row < table.RowCount; row++)
            {
                string key = (table.get_Text(row, sortColumn) ?? "").Trim();
                if (string.Equals(key, targetKey, StringComparison.Ordinal))
                    return row;
            }

            return -1;
        }

        private int CompareNaturalValues(string left, string right)
        {
            bool emptyLeft = string.IsNullOrWhiteSpace(left);
            bool emptyRight = string.IsNullOrWhiteSpace(right);
            if (emptyLeft && emptyRight)
                return 0;
            if (emptyLeft)
                return 1;
            if (emptyRight)
                return -1;

            int i = 0;
            int j = 0;

            while (i < left.Length && j < right.Length)
            {
                char a = left[i];
                char b = right[j];
                if (char.IsDigit(a) && char.IsDigit(b))
                {
                    int numberCompare = CompareNumberToken(left, ref i, right, ref j);
                    if (numberCompare != 0)
                        return numberCompare;
                    continue;
                }

                int charCompare = char.ToUpperInvariant(a).CompareTo(char.ToUpperInvariant(b));
                if (charCompare != 0)
                    return charCompare;

                i++;
                j++;
            }

            return left.Length.CompareTo(right.Length);
        }

        private int CompareNumberToken(string left, ref int i, string right, ref int j)
        {
            int leftStart = i;
            int rightStart = j;

            while (i < left.Length && char.IsDigit(left[i]))
                i++;
            while (j < right.Length && char.IsDigit(right[j]))
                j++;

            string leftNumber = left.Substring(leftStart, i - leftStart).TrimStart('0');
            string rightNumber = right.Substring(rightStart, j - rightStart).TrimStart('0');
            if (leftNumber.Length == 0)
                leftNumber = "0";
            if (rightNumber.Length == 0)
                rightNumber = "0";

            int lengthCompare = leftNumber.Length.CompareTo(rightNumber.Length);
            if (lengthCompare != 0)
                return lengthCompare;

            return string.Compare(leftNumber, rightNumber, StringComparison.Ordinal);
        }

        private void TryForceRenumberItemNumbers(BomTableSortData sortData)
        {
            if (sortData == null)
                return;

            try
            {
                dynamic data = sortData;
                data.DoNotChangeItemNumber = false;
            }
            catch
            {
            }

            try
            {
                dynamic data = sortData;
                data.RenumberItems = true;
                Debug.WriteLine("[XEP UNIT] Sort option RenumberItems=True");
            }
            catch
            {
            }
        }

        private string GetBomSortPreview(ITableAnnotation table, int sortColumn)
        {
            if (table == null || sortColumn < 0)
                return "";

            try
            {
                List<string> values = new List<string>();
                int maxRow = Math.Min(table.RowCount, 6);
                for (int row = 1; row < maxRow; row++)
                {
                    string itemNo = table.ColumnCount > 0 ? table.get_Text(row, 0) : "";
                    string sortValue = table.get_Text(row, sortColumn);
                    values.Add((itemNo ?? "").Trim() + ":" + (sortValue ?? "").Trim());
                }

                return string.Join(" | ", values.ToArray());
            }
            catch (Exception ex)
            {
                return "preview failed: " + ex.Message;
            }
        }

        private void RebuildDrawing(ModelDoc2 drawingModel)
        {
            if (drawingModel == null)
                return;

            try
            {
                drawingModel.EditRebuild3();
            }
            catch
            {
            }

            try
            {
                drawingModel.ForceRebuild3(false);
            }
            catch
            {
            }
        }

        private bool SaveDrawing(ModelDoc2 drawingModel)
        {
            if (drawingModel == null)
                return false;

            try
            {
                int errors = 0;
                int warnings = 0;
                return drawingModel.Save3(
                    (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
                    ref errors,
                    ref warnings);
            }
            catch
            {
                return false;
            }
        }

        private bool CloseDrawing(ModelDoc2 drawingModel)
        {
            if (drawingModel == null)
                return false;

            try
            {
                string title = drawingModel.GetTitle();
                if (string.IsNullOrWhiteSpace(title))
                    return false;

                swApp.CloseDoc(title);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private List<ITableAnnotation> GetTables(ModelDoc2 drawingModel)
        {
            List<ITableAnnotation> tables = new List<ITableAnnotation>();
            DrawingDoc drawing = drawingModel as DrawingDoc;
            if (drawing == null)
                return tables;

            SolidWorks.Interop.sldworks.View view =
                drawing.GetFirstView() as SolidWorks.Interop.sldworks.View;

            while (view != null)
            {
                ITableAnnotation table = view.GetFirstTableAnnotation() as ITableAnnotation;
                while (table != null)
                {
                    tables.Add(table);
                    table = table.GetNext() as ITableAnnotation;
                }

                view = view.GetNextView() as SolidWorks.Interop.sldworks.View;
            }

            return tables;
        }

        private int FindBuhinNoSortColumnIndex(ITableAnnotation table)
        {
            const string rawBuhinNoHeader = "<FONT size=12PTS>\u90e8\u54c1\u756a\u53f7";
            const string buhinNoHeader = "\u90e8\u54c1\u756a\u53f7";

            int rawIndex = FindColumnIndex(table, rawBuhinNoHeader, true);
            if (rawIndex >= 0)
                return rawIndex;

            return FindColumnIndex(table, buhinNoHeader, false);
        }

        private int FindColumnIndex(ITableAnnotation table, string headerName, bool exactText)
        {
            string normalizedHeaderName = NormalizeHeaderText(headerName);
            for (int col = 0; col < table.ColumnCount; col++)
            {
                string header = table.get_Text(0, col);
                if (exactText &&
                    string.Equals((header ?? "").Trim(), headerName, StringComparison.OrdinalIgnoreCase))
                    return col;

                if (!exactText &&
                    string.Equals(NormalizeHeaderText(header), normalizedHeaderName, StringComparison.OrdinalIgnoreCase))
                    return col;
            }

            return -1;
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

        private class BomGridBuhinNoComparer : IComparer
        {
            private readonly int columnIndex;

            public BomGridBuhinNoComparer(int columnIndex)
            {
                this.columnIndex = columnIndex;
            }

            public int Compare(object x, object y)
            {
                DataGridViewRow rowX = x as DataGridViewRow;
                DataGridViewRow rowY = y as DataGridViewRow;
                string valueX = GetCellText(rowX, columnIndex);
                string valueY = GetCellText(rowY, columnIndex);

                bool emptyX = string.IsNullOrWhiteSpace(valueX);
                bool emptyY = string.IsNullOrWhiteSpace(valueY);
                if (emptyX && emptyY)
                    return 0;
                if (emptyX)
                    return 1;
                if (emptyY)
                    return -1;

                int natural = CompareNatural(valueX, valueY);
                if (natural != 0)
                    return natural;

                return (rowX?.Index ?? 0).CompareTo(rowY?.Index ?? 0);
            }

            private static string GetCellText(DataGridViewRow row, int columnIndex)
            {
                if (row == null || columnIndex < 0 || columnIndex >= row.Cells.Count)
                    return "";

                return Convert.ToString(row.Cells[columnIndex].Value ?? "").Trim();
            }

            private static int CompareNatural(string left, string right)
            {
                int i = 0;
                int j = 0;

                while (i < left.Length && j < right.Length)
                {
                    char a = left[i];
                    char b = right[j];
                    if (char.IsDigit(a) && char.IsDigit(b))
                    {
                        int numberCompare = CompareNumberToken(left, ref i, right, ref j);
                        if (numberCompare != 0)
                            return numberCompare;
                        continue;
                    }

                    int charCompare = char.ToUpperInvariant(a).CompareTo(char.ToUpperInvariant(b));
                    if (charCompare != 0)
                        return charCompare;

                    i++;
                    j++;
                }

                return left.Length.CompareTo(right.Length);
            }

            private static int CompareNumberToken(string left, ref int i, string right, ref int j)
            {
                int leftStart = i;
                int rightStart = j;

                while (i < left.Length && char.IsDigit(left[i]))
                    i++;
                while (j < right.Length && char.IsDigit(right[j]))
                    j++;

                string leftNumber = left.Substring(leftStart, i - leftStart).TrimStart('0');
                string rightNumber = right.Substring(rightStart, j - rightStart).TrimStart('0');
                if (leftNumber.Length == 0)
                    leftNumber = "0";
                if (rightNumber.Length == 0)
                    rightNumber = "0";

                int lengthCompare = leftNumber.Length.CompareTo(rightNumber.Length);
                if (lengthCompare != 0)
                    return lengthCompare;

                int valueCompare = string.Compare(leftNumber, rightNumber, StringComparison.Ordinal);
                if (valueCompare != 0)
                    return valueCompare;

                return (i - leftStart).CompareTo(j - rightStart);
            }
        }
    }
}
