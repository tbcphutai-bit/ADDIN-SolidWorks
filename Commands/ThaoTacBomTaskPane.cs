using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;

namespace ADDIN.Commands
{
    public class ThaoTacBomTaskPane
    {
        public BomCommandContext LoadedBomContext { get; private set; }

        private readonly ISldWorks swApp;
        private readonly BomLoader bomLoader;
        private readonly DataGridView gridBom;
        private readonly CheckBox chkSelectAll;
        private readonly Label lblStatus;
        private readonly ProgressBar progressBar;
        private readonly Control invoker;
        private HashSet<int> pendingCheckboxRows;
        private bool cancelRequested;
        private bool checkInProgress;
        private KetQuaSoSanhDfTk cachedDfTkResult;
        private string cachedDfTkSignature;

        public ThaoTacBomTaskPane(
            ISldWorks app,
            BomLoader loader,
            DataGridView grid,
            CheckBox selectAll,
            Label status,
            ProgressBar progress,
            Control invokeControl)
        {
            swApp = app;
            bomLoader = loader;
            gridBom = grid;
            chkSelectAll = selectAll;
            lblStatus = status;
            progressBar = progress;
            invoker = invokeControl;
        }

        public void ConfigureGrid()
        {
            gridBom.MultiSelect = true;
            gridBom.SelectionMode = DataGridViewSelectionMode.CellSelect;
            ConfigureGridForContext(BomCommandContext.None);
            ResetProgress();
            AutoFitBomGrid();
        }

        public void LoadBom(Func<bool> isCancellationRequested = null)
        {
            InvalidateDfTkCache();
            LoadedBomContext = BomCommandContext.None;
            ConfigureGridForContext(BomCommandContext.None);
            if (bomLoader == null)
            {
                lblStatus.Text = "Chua ket noi SOLIDWORKS.";
                return;
            }

            ITableAnnotation swTable = bomLoader.GetCustomBomTable();

            if (swTable == null)
            {
                lblStatus.Text = "Khong tim thay Custom BOM. Hay chon bang BOM trong SOLIDWORKS.";
                return;
            }

            LoadedBomContext = bomLoader.GetBomCommandContext(swTable);
            ConfigureGridForContext(LoadedBomContext);

            // Avoid measuring and repainting every row while COM data is loaded.
            gridBom.SuspendLayout();
            gridBom.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            gridBom.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            try
            {
                bomLoader.LoadBOMTableToGrid(gridBom, swTable, isCancellationRequested);
                if (isCancellationRequested != null && isCancellationRequested())
                {
                    lblStatus.Text = "Da huy CAP NHAT BOM.";
                    return;
                }

                chkSelectAll.Checked = true;
                SetAllChecked(true);
                SortGridByBuhinNoAscending();
            }
            finally
            {
                gridBom.ResumeLayout(false);
                AutoFitBomGrid();
            }

            string bomLabel = LoadedBomContext == BomCommandContext.Unit
                ? "BOM UNIT"
                : "BOM chi tiet";
            lblStatus.Text = "Da load " + bomLabel + ": " + gridBom.Rows.Count + " dong";
        }

        private void SortGridByBuhinNoAscending()
        {
            if (gridBom == null || gridBom.Columns.Count < 2 || gridBom.Rows.Count == 0)
                return;

            gridBom.SortCompare -= GridBom_SortCompareBuhinNo;
            gridBom.SortCompare += GridBom_SortCompareBuhinNo;
            gridBom.Columns[1].SortMode = DataGridViewColumnSortMode.Programmatic;
            gridBom.Sort(gridBom.Columns[1], System.ComponentModel.ListSortDirection.Ascending);
            gridBom.Columns[1].HeaderCell.SortGlyphDirection = SortOrder.Ascending;
        }

        private void GridBom_SortCompareBuhinNo(object sender, DataGridViewSortCompareEventArgs e)
        {
            if (e.Column.Index != 1)
                return;

            string left = Convert.ToString(e.CellValue1 ?? "").Trim();
            string right = Convert.ToString(e.CellValue2 ?? "").Trim();
            decimal leftNumber;
            decimal rightNumber;

            if (decimal.TryParse(left, out leftNumber) && decimal.TryParse(right, out rightNumber))
                e.SortResult = leftNumber.CompareTo(rightNumber);
            else
                e.SortResult = string.Compare(left, right, StringComparison.CurrentCultureIgnoreCase);

            if (e.SortResult == 0)
                e.SortResult = e.RowIndex1.CompareTo(e.RowIndex2);

            e.Handled = true;
        }

        public void ClearBom()
        {
            InvalidateDfTkCache();
            bomLoader?.ClearBomGrid(gridBom);
            LoadedBomContext = BomCommandContext.None;
            ConfigureGridForContext(BomCommandContext.None);
            AutoFitBomGrid();
            lblStatus.Text = "Da xoa BOM";
        }

        private void ConfigureGridForContext(BomCommandContext context)
        {
            if (gridBom == null || gridBom.Columns.Count < 6)
                return;

            bool isUnit = context == BomCommandContext.Unit;
            gridBom.Columns[1].HeaderText = "部品番号";
            gridBom.Columns[2].HeaderText = isUnit ? "合番" : "材質";
            gridBom.Columns[2].Visible = true;
            gridBom.Columns[3].HeaderText = "板厚";
            gridBom.Columns[3].Visible = !isUnit;
            gridBom.Columns[4].HeaderText = "数量";
            gridBom.Columns[5].HeaderText = "部品ファイル名";
        }

        public void SetAllChecked(bool isChecked)
        {
            foreach (DataGridViewRow row in gridBom.Rows)
            {
                if (row.IsNewRow)
                    continue;

                row.Cells[0].Value = isChecked;
            }

            gridBom.EndEdit();
        }

        public void CommitCurrentCellIfDirty()
        {
            if (gridBom.IsCurrentCellDirty)
                gridBom.CommitEdit(DataGridViewDataErrorContexts.Commit);
        }

        public void BeginCheckboxSelection(int rowIndex, int columnIndex)
        {
            if (rowIndex < 0 || columnIndex != 0)
                return;

            bool modifierSelectionRequested =
                (Control.ModifierKeys & (Keys.Control | Keys.Shift)) != Keys.None;
            bool selectedRegionExists =
                gridBom.SelectedCells.Count > 1 || gridBom.SelectedRows.Count > 1;

            // A normal click must affect only the checkbox under the mouse.
            // A highlighted region, or Ctrl/Shift, intentionally applies the
            // checkbox value to every row represented by the selection.
            pendingCheckboxRows = modifierSelectionRequested || selectedRegionExists
                ? GetSelectedRowIndexes()
                : new HashSet<int>();
            pendingCheckboxRows.Add(rowIndex);
        }

        public void ApplyCheckboxSelection(int rowIndex, int columnIndex)
        {
            if (rowIndex < 0 || columnIndex != 0)
                return;

            HashSet<int> rowIndexes = pendingCheckboxRows;
            if (rowIndexes == null || rowIndexes.Count == 0)
            {
                rowIndexes = new HashSet<int>();
                rowIndexes.Add(rowIndex);
            }

            invoker.BeginInvoke(new Action(() =>
            {
                bool newValue = Convert.ToBoolean(gridBom.Rows[rowIndex].Cells[0].Value ?? false);

                foreach (int index in rowIndexes)
                {
                    if (index < 0 || index >= gridBom.Rows.Count)
                        continue;

                    DataGridViewRow row = gridBom.Rows[index];
                    if (row.IsNewRow)
                        continue;

                    row.Cells[0].Value = newValue;
                }

                pendingCheckboxRows = null;
                gridBom.EndEdit();
            }));
        }

        public void ToggleSelectedRowsBySpace(KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Space || gridBom.SelectedCells.Count == 0)
                return;

            bool check = !AreAllSelectedRowsChecked();
            SetSelectedRowsChecked(check);
            e.Handled = true;
        }

        public void AutoFitBomGrid()
        {
            gridBom.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            gridBom.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;

            if (gridBom.Columns.Count < 6)
                return;

            gridBom.Columns[0].AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            gridBom.Columns[0].MinimumWidth = 45;
            gridBom.Columns[0].Width = 45;

            for (int i = 1; i <= 5; i++)
                gridBom.Columns[i].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

            gridBom.Columns[1].FillWeight = 120;
            gridBom.Columns[2].FillWeight = LoadedBomContext == BomCommandContext.Unit ? 100 : 80;
            gridBom.Columns[3].FillWeight = 60;
            gridBom.Columns[4].FillWeight = 60;
            gridBom.Columns[5].FillWeight = 180;
        }

        public bool CheckDfTk()
        {
            if (swApp == null)
            {
                lblStatus.Text = "Chua ket noi SOLIDWORKS.";
                return false;
            }

            cancelRequested = false;
            checkInProgress = true;
            KetQuaSoSanhDfTk result = null;
            try
            {
                ChaySoSanhDfTk runner = new ChaySoSanhDfTk(swApp, gridBom);
                result = runner.Run(BeginProgress, UpdateProgress, IsCancelRequested);
                StoreDfTkCache(result);
            }
            finally
            {
                checkInProgress = false;
                FinishProgress();
            }

            HighlightRowsForResults(result.DiffResults);
            HighlightRows(result.HighlightRowIndexes);
            AutoFitBomGrid();
            ShowCheckResult(result);
            return result != null && !result.Canceled && result.CheckedCount > 0;
        }

        public void CheckUraOmote()
        {
            if (swApp == null)
            {
                lblStatus.Text = "Chua ket noi SOLIDWORKS.";
                return;
            }

            cancelRequested = false;
            checkInProgress = true;
            UraOmoteCheckResult result = null;
            try
            {
                CheckUraOmoteRunner runner = new CheckUraOmoteRunner(swApp, gridBom);
                result = runner.Run(BeginProgress, UpdateProgress, IsCancelRequested);
            }
            finally
            {
                checkInProgress = false;
                FinishProgress();
            }

            HighlightRows(result.HighlightRowIndexes);
            AutoFitBomGrid();
            ShowUraOmoteResult(result);
        }

        public void CheckKegaki()
        {
            if (swApp == null)
            {
                lblStatus.Text = "Chua ket noi SOLIDWORKS.";
                return;
            }

            cancelRequested = false;
            checkInProgress = true;
            KegakiCheckResult result = null;
            try
            {
                CheckKegakiRunner runner = new CheckKegakiRunner(swApp, gridBom);
                result = runner.Run(BeginProgress, UpdateProgress, IsCancelRequested);
            }
            finally
            {
                checkInProgress = false;
                FinishProgress();
            }

            if (result == null)
                return;

            HighlightRows(result.HighlightRowIndexes);
            AutoFitBomGrid();
            ShowKegakiResult(result);
        }

        public void CheckRound()
        {
            if (swApp == null)
            {
                lblStatus.Text = "Chua ket noi SOLIDWORKS.";
                return;
            }

            cancelRequested = false;
            checkInProgress = true;
            RoundHoleCheckResult result = null;
            try
            {
                CheckRoundRunner runner = new CheckRoundRunner(swApp, gridBom);
                result = runner.Run(BeginProgress, UpdateProgress, IsCancelRequested);
            }
            finally
            {
                checkInProgress = false;
                FinishProgress();
            }

            if (result == null)
                return;

            int alignedPreviewCount = RoundHolePreviewDrawingAligner.AlignToActiveDrawing(
                swApp,
                result.Results);
            System.Diagnostics.Debug.WriteLine(
                "[CHECK ROUND] Preview Drawing View aligned=" + alignedPreviewCount);
            HighlightRows(result.HighlightRowIndexes);
            AutoFitBomGrid();
            ShowRoundResult(result);
        }

        public void CheckSamePart(SamePartToleranceOptions toleranceOptions)
        {
            if (swApp == null)
            {
                lblStatus.Text = "Chua ket noi SOLIDWORKS.";
                return;
            }

            if (toleranceOptions == null)
                toleranceOptions = new SamePartToleranceOptions();

            string excelOutputDirectory = GetActiveSolidWorksDocumentDirectory();
            cancelRequested = false;
            checkInProgress = true;
            SamePartCheckResult result = null;
            try
            {
                CheckSamePartRunner runner = new CheckSamePartRunner(
                    swApp,
                    gridBom,
                    toleranceOptions);
                result = runner.Run(BeginProgress, UpdateProgress, IsCancelRequested);
            }
            finally
            {
                checkInProgress = false;
                FinishProgress();
            }

            if (result == null)
                return;

            HighlightRows(result.HighlightRowIndexes);
            AutoFitBomGrid();
            ShowSamePartResult(result, excelOutputDirectory);
        }

        public void CheckAll(CombinedCheckOptions options)
        {
            if (swApp == null)
            {
                lblStatus.Text = "Chua ket noi SOLIDWORKS.";
                return;
            }
            if (options == null)
                options = CombinedCheckOptions.All();
            // DF/TK is an independent command and is never part of this combined check.
            options.CheckDfTk = false;
            if (!options.HasSelection)
                return;

            cancelRequested = false;
            checkInProgress = true;
            CombinedCheckResult combined = new CombinedCheckResult();
            int totalSteps = options.SelectedCount;
            int currentStep = 0;
            try
            {
                if (options.CheckUraOmote && !combined.Canceled && !IsCancelRequested())
                {
                    currentStep++;
                    lblStatus.Text = "CHECK URA/KEGAKI " + currentStep + "/" + totalSteps
                        + ": dang kiem tra \u30A6\u30E9\u8868...";
                    combined.UraOmote = new CheckUraOmoteRunner(swApp, gridBom)
                        .Run(BeginProgress, UpdateProgress, IsCancelRequested);
                }

                if (options.CheckKegaki && !combined.Canceled && !IsCancelRequested())
                {
                    currentStep++;
                    lblStatus.Text = "CHECK URA/KEGAKI " + currentStep + "/" + totalSteps
                        + ": dang kiem tra KEGAKI...";
                    combined.Kegaki = new CheckKegakiRunner(swApp, gridBom)
                        .Run(BeginProgress, UpdateProgress, IsCancelRequested);
                }
            }
            finally
            {
                checkInProgress = false;
                FinishProgress();
            }

            HighlightCombinedRows(combined);
            AutoFitBomGrid();

            if (combined.Canceled || IsCancelRequested())
            {
                lblStatus.Text = "Da huy CHECK URA/KEGAKI.";
                return;
            }

            int checkedCount = 0;
            if (combined.DfTk != null)
                checkedCount = Math.Max(checkedCount, combined.DfTk.CheckedCount);
            if (combined.UraOmote != null)
                checkedCount = Math.Max(checkedCount, combined.UraOmote.CheckedCount);
            if (combined.Kegaki != null)
                checkedCount = Math.Max(checkedCount, combined.Kegaki.CheckedCount);
            if (checkedCount == 0)
            {
                lblStatus.Text = "Chua chon chi tiet nao de CHECK URA/KEGAKI.";
                return;
            }

            ExcelCombinedCheckExporter.Export(combined, gridBom);
            lblStatus.Text = "CHECK URA/KEGAKI xong. Da xuat Excel gom "
                + (totalSteps + 1) + " sheet.";
        }

        public void InvalidateDfTkCache()
        {
            cachedDfTkResult = null;
            cachedDfTkSignature = null;
        }

        private void StoreDfTkCache(KetQuaSoSanhDfTk result)
        {
            if (result == null || result.Canceled || result.CheckedCount <= 0)
            {
                InvalidateDfTkCache();
                return;
            }

            cachedDfTkResult = result;
            cachedDfTkSignature = BuildDfTkCacheSignature();
            System.Diagnostics.Debug.WriteLine(
                "[CHECK DF/TK] Saved session cache. checked=" + result.CheckedCount
                + ", signature=" + cachedDfTkSignature);
        }

        private bool TryGetCachedDfTkResult(out KetQuaSoSanhDfTk result)
        {
            result = null;
            if (cachedDfTkResult == null || string.IsNullOrEmpty(cachedDfTkSignature))
                return false;

            string currentSignature = BuildDfTkCacheSignature();
            if (!string.Equals(cachedDfTkSignature, currentSignature, StringComparison.Ordinal))
            {
                System.Diagnostics.Debug.WriteLine(
                    "[CHECK DF/TK] Session cache is stale. old=" + cachedDfTkSignature
                    + ", new=" + currentSignature);
                InvalidateDfTkCache();
                return false;
            }

            result = cachedDfTkResult;
            System.Diagnostics.Debug.WriteLine(
                "[CHECK DF/TK] Reuse session cache. checked=" + result.CheckedCount);
            return true;
        }

        private string BuildDfTkCacheSignature()
        {
            StringBuilder signature = new StringBuilder();
            signature.Append((int)LoadedBomContext).Append('|');

            foreach (DataGridViewRow row in gridBom.Rows)
            {
                if (row.IsNewRow || !Convert.ToBoolean(row.Cells[0].Value ?? false))
                    continue;

                signature.Append(row.Index).Append(':');
                signature.Append(Convert.ToString(row.Cells[1].Value ?? "").Trim()).Append(':');
                signature.Append(Convert.ToString(row.Cells[5].Value ?? "").Trim()).Append(':');
                AppendDfTkSourceSignature(signature, row.Tag);
                signature.Append('|');
            }

            return signature.ToString();
        }

        private void AppendDfTkSourceSignature(StringBuilder signature, object source)
        {
            object[] sources = source as object[];
            if (sources != null)
            {
                foreach (object item in sources)
                {
                    AppendDfTkSourceSignature(signature, item);
                    signature.Append(';');
                }
                return;
            }

            Component2 component = source as Component2;
            if (component != null)
            {
                string path = "";
                string configuration = "";
                try { path = component.GetPathName() ?? ""; } catch { }
                try { configuration = component.ReferencedConfiguration ?? ""; } catch { }
                AppendDfTkPathSignature(signature, path);
                signature.Append('@').Append(configuration);
                return;
            }

            AppendDfTkPathSignature(signature, source as string);
        }

        private void AppendDfTkPathSignature(StringBuilder signature, string path)
        {
            string normalizedPath = (path ?? "").Trim().ToUpperInvariant();
            signature.Append(normalizedPath);

            if (normalizedPath.Length == 0)
                return;

            try
            {
                if (File.Exists(path))
                    signature.Append('#').Append(File.GetLastWriteTimeUtc(path).Ticks);
            }
            catch
            {
            }
        }

        private void HighlightCombinedRows(CombinedCheckResult result)
        {
            if (result == null)
                return;

            if (result.DfTk != null)
            {
                HighlightRowsForResults(result.DfTk.DiffResults);
                HighlightRows(result.DfTk.HighlightRowIndexes);
            }
            if (result.UraOmote != null)
                HighlightRows(result.UraOmote.HighlightRowIndexes);
            if (result.Kegaki != null)
                HighlightRows(result.Kegaki.HighlightRowIndexes);
        }

        public void RequestCancel()
        {
            if (!checkInProgress)
                return;

            cancelRequested = true;
            lblStatus.Text = "Dang huy lenh kiem tra...";
            Application.DoEvents();
        }

        private bool IsCancelRequested()
        {
            return cancelRequested;
        }

        private void BeginProgress(int totalCount)
        {
            if (progressBar == null)
                return;

            progressBar.Minimum = 0;
            progressBar.Maximum = Math.Max(1, totalCount);
            progressBar.Value = 0;
            progressBar.Visible = true;
            progressBar.Refresh();
            Application.DoEvents();
        }

        private void UpdateProgress(int currentCount, int totalCount)
        {
            if (progressBar == null)
                return;

            progressBar.Maximum = Math.Max(1, totalCount);
            progressBar.Value = Math.Min(progressBar.Maximum, Math.Max(progressBar.Minimum, currentCount));
            progressBar.Refresh();
            Application.DoEvents();
        }

        private void FinishProgress()
        {
            if (progressBar == null)
                return;

            if (progressBar.Visible)
                progressBar.Value = progressBar.Maximum;

            progressBar.Refresh();
            Application.DoEvents();
            ResetProgress();
        }

        private void ResetProgress()
        {
            if (progressBar == null)
                return;

            progressBar.Value = 0;
            progressBar.Visible = false;
        }

        private void ShowCheckResult(KetQuaSoSanhDfTk result)
        {
            WriteCheckDebugResult(result);

            if (result.Canceled)
            {
                lblStatus.Text = "Da huy CHECK DF/TK. Da xu ly: " + result.ProcessedCount + "/" + result.CheckedCount;
                return;
            }

            if (result.CheckedCount == 0)
            {
                lblStatus.Text = "Chua chon chi tiet nao de CHECK DF/TK.";
                return;
            }

            if (result.DiffResults.Count == 0)
            {
                string message = "Khong co chi tiet nao khac nhau.";
                if (result.SkippedCount > 0)
                    message += " Bo qua " + result.SkippedCount + " dong khong co duong dan part.";
                if (result.CheckLogs.Count > 0)
                    message += " Co " + result.CheckLogs.Count + " dong log kiem tra.";

                lblStatus.Text = message;
                MessageBox.Show(message, "Ket qua kiem tra", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            ExcelDfTkExporter.Export(result.DiffResults, result.CheckLogs);
            lblStatus.Text = "CHECK DF/TK xong. Da chon: " + result.CheckedCount + ", khac nhau: " + result.DiffResults.Count;
        }

        private void ShowUraOmoteResult(UraOmoteCheckResult result)
        {
            if (result == null)
            {
                lblStatus.Text = "Khong co ket qua Check ウラ 表.";
                return;
            }

            if (result.Canceled)
            {
                lblStatus.Text = "Da huy Check ウラ 表. Da xu ly: " + result.ProcessedCount + "/" + result.CheckedCount;
                return;
            }

            if (result.CheckedCount == 0)
            {
                lblStatus.Text = "Chua chon chi tiet nao de Check ウラ 表.";
                return;
            }

            ExcelUraOmoteExporter.Export(result.Results);

            int ngCount = 0;
            int checkCount = 0;
            foreach (UraOmoteRowResult row in result.Results)
            {
                if (row.Status == "NG")
                    ngCount++;
                else if (row.Status == "CHECK")
                    checkCount++;
            }

            lblStatus.Text = "Check ウラ 表 xong. Da chon: " + result.CheckedCount
                + ", NG: " + ngCount
                + ", CHECK: " + checkCount
                + ", bo qua: " + result.SkippedCount;
        }

        private void ShowKegakiResult(KegakiCheckResult result)
        {
            if (result == null)
            {
                lblStatus.Text = "Khong co ket qua CHECK KEGAKI.";
                return;
            }

            if (result.Canceled)
            {
                lblStatus.Text = "Da huy CHECK KEGAKI. Da xu ly: "
                    + result.ProcessedCount + "/" + result.CheckedCount;
                return;
            }

            if (result.CheckedCount == 0)
            {
                lblStatus.Text = "Chua chon chi tiet nao de CHECK KEGAKI.";
                return;
            }

            ExcelKegakiExporter.Export(result.Results);

            int ngCount = 0;
            int checkCount = 0;
            int overrideCount = 0;
            foreach (KegakiBendResult row in result.Results)
            {
                if (row.Status == "NG")
                    ngCount++;
                else if (row.Status == "CHECK")
                    checkCount++;

                if (row.IsOverride)
                    overrideCount++;
            }

            lblStatus.Text = "CHECK KEGAKI xong. Da chon: " + result.CheckedCount
                + ", bend override: " + overrideCount
                + ", NG: " + ngCount
                + ", CHECK: " + checkCount
                + ", bo qua: " + result.SkippedCount;
        }

        private void ShowRoundResult(RoundHoleCheckResult result)
        {
            if (result == null)
            {
                lblStatus.Text = "Khong co ket qua CHECK ROUND.";
                return;
            }

            if (result.Canceled)
            {
                lblStatus.Text = "Da huy CHECK ROUND. Da xu ly: "
                    + result.ProcessedCount + "/" + result.CheckedCount;
                return;
            }

            if (result.CheckedCount == 0)
            {
                lblStatus.Text = "Chua chon chi tiet nao de CHECK ROUND.";
                return;
            }

            int previewCount = RoundHolePreviewForm.ShowPreview(result.Results);
            int exportedCount = ExcelRoundHoleExporter.Export(result.Results);
            RoundHolePreviewForm.BringLatestToFront();
            string message = "CHECK ROUND xong. Lo tron: " + result.RoundHoleCount
                + ", lo dai: " + result.SlotHoleCount
                + ", NG: " + result.NgCount
                + ", CHECK: " + result.CheckCount
                + ", bo qua: " + result.SkippedCount + ".";

            if (previewCount > 0)
                message += " Da mo preview 2D cho " + previewCount + " chi tiet.";
            else if (result.NgCount + result.CheckCount > 0)
                message += " Khong tao duoc preview 2D cho ket qua nay.";

            if (exportedCount > 0)
                message += " Da xuat Excel " + exportedCount + " dong can kiem tra.";
            else
                message += " Khong co lo bat thuong de xuat Excel.";

            lblStatus.Text = message;
            MessageBox.Show(
                message,
                "CHECK ROUND",
                MessageBoxButtons.OK,
                result.NgCount > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }

        private string GetActiveSolidWorksDocumentDirectory()
        {
            try
            {
                ModelDoc2 activeDocument = swApp == null ? null : swApp.ActiveDoc as ModelDoc2;
                string documentPath = activeDocument == null ? "" : activeDocument.GetPathName();
                if (!string.IsNullOrWhiteSpace(documentPath))
                    return Path.GetDirectoryName(documentPath);
            }
            catch
            {
            }

            return "";
        }

        private void ShowSamePartResult(SamePartCheckResult result, string excelOutputDirectory)
        {
            if (result == null)
            {
                lblStatus.Text = "Khong co ket qua CHECK SAME PART.";
                return;
            }

            if (result.Canceled)
            {
                lblStatus.Text = "Da huy CHECK SAME PART. Da xu ly: "
                    + result.ProcessedCount + "/" + result.CheckedCount;
                return;
            }

            if (result.CheckedCount == 0)
            {
                lblStatus.Text = "Chua chon chi tiet nao de CHECK SAME PART.";
                return;
            }

            string exportedPath;
            int exportedCount = ExcelSamePartExporter.Export(
                result,
                excelOutputDirectory,
                out exportedPath);
            int sameFull = result.Groups.Count(group => group.Status == "SAME FULL");
            int sameGeometry = result.Groups.Count(group => group.Status == "SAME GEOMETRY");
            int sameFlat = result.Groups.Count(group => group.Status == "SAME FLAT");
            int mirrorCheck = result.Groups.Count(group => group.Status == "CHECK MIRROR");
            string message = "CHECK SAME PART xong. Nhom trung hoan toan: " + sameFull
                + ", trung hinh hoc: " + sameGeometry
                + ", chi trung Flat-Pattern: " + sameFlat
                + ", mirror/can doi chieu: " + mirrorCheck
                + ", can kiem tra: " + result.Errors.Count + ".";

            if (exportedCount > 0)
            {
                message += " Da xuat Excel " + exportedCount + " dong.";
                if (!string.IsNullOrWhiteSpace(exportedPath))
                    message += " File: " + exportedPath;
            }
            else
                message += " Khong tim thay nhom chi tiet giong nhau.";
            if (!string.IsNullOrWhiteSpace(result.DebugLogPath))
                message += " Debug: " + result.DebugLogPath;

            lblStatus.Text = message;
            MessageBox.Show(
                message,
                "CHECK SAME PART",
                MessageBoxButtons.OK,
                result.Errors.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }

        private void WriteCheckDebugResult(KetQuaSoSanhDfTk result)
        {
            System.Diagnostics.Debug.WriteLine(BuildDebugResultText(result));
        }

        private string BuildDebugResultText(KetQuaSoSanhDfTk result)
        {
            if (result == null)
                return "Khong co ket qua CHECK DF/TK.";

            List<string> lines = new List<string>
            {
                "CHECK DF/TK DEBUG",
                "Da chon: " + result.CheckedCount,
                "Da xu ly: " + result.ProcessedCount,
                "Khac nhau: " + result.DiffResults.Count,
                "Bo qua: " + result.SkippedCount,
                "Log: " + result.CheckLogs.Count,
                "Da huy: " + (result.Canceled ? "Yes" : "No")
            };

            if (result.DiffResults.Count > 0)
            {
                lines.Add("");
                lines.Add("Ket qua khac nhau:");
                for (int i = 0; i < result.DiffResults.Count; i++)
                {
                    DfTkResult diff = result.DiffResults[i];
                    lines.Add("- #" + (i + 1) + " " + diff.Component + " | " + diff.BuhinNo + " | " + diff.DiffText);
                    lines.Add("  DF/TK ngoai: " + diff.OuterDf + " / " + diff.OuterTk);
                    lines.Add("  DF/TK trong: " + diff.InnerDf + " / " + diff.InnerTk);
                    lines.Add("  Dien tich DF/TK: " + diff.AreaDf + " / " + diff.AreaTk);
                }
            }

            if (result.CheckLogs.Count > 0)
            {
                lines.Add("");
                lines.Add("Log kiem tra:");
                for (int i = 0; i < result.CheckLogs.Count; i++)
                    lines.Add("- #" + (i + 1) + " " + result.CheckLogs[i]);
            }

            return string.Join(System.Environment.NewLine, lines);
        }

        private bool AreAllSelectedRowsChecked()
        {
            HashSet<int> rowIndexes = GetSelectedRowIndexes();
            if (rowIndexes.Count == 0)
                return false;

            foreach (int rowIndex in rowIndexes)
            {
                bool isChecked = Convert.ToBoolean(gridBom.Rows[rowIndex].Cells[0].Value ?? false);
                if (!isChecked)
                    return false;
            }

            return true;
        }

        private void SetSelectedRowsChecked(bool isChecked)
        {
            foreach (int rowIndex in GetSelectedRowIndexes())
            {
                DataGridViewRow row = gridBom.Rows[rowIndex];
                if (!row.IsNewRow)
                    row.Cells[0].Value = isChecked;
            }

            gridBom.EndEdit();
        }

        private HashSet<int> GetSelectedRowIndexes()
        {
            HashSet<int> rowIndexes = new HashSet<int>();

            foreach (DataGridViewCell cell in gridBom.SelectedCells)
            {
                if (cell.RowIndex >= 0 && !gridBom.Rows[cell.RowIndex].IsNewRow)
                    rowIndexes.Add(cell.RowIndex);
            }

            foreach (DataGridViewRow row in gridBom.SelectedRows)
            {
                if (!row.IsNewRow)
                    rowIndexes.Add(row.Index);
            }

            return rowIndexes;
        }

        private string NormalizeKey(string value)
        {
            return (value ?? "").Trim().ToUpperInvariant();
        }

        private void HighlightRowsForResults(List<DfTkResult> results)
        {
            HashSet<string> buhinNos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> componentNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (DfTkResult result in results)
            {
                if (!string.IsNullOrWhiteSpace(result.BuhinNo))
                    buhinNos.Add(NormalizeKey(result.BuhinNo));

                if (!string.IsNullOrWhiteSpace(result.Component))
                    componentNames.Add(NormalizeKey(result.Component));
            }

            foreach (DataGridViewRow row in gridBom.Rows)
            {
                if (row.IsNewRow)
                    continue;

                string buhinNo = NormalizeKey(Convert.ToString(row.Cells[1].Value ?? ""));
                string fileName = NormalizeKey(Convert.ToString(row.Cells[5].Value ?? ""));

                if (buhinNos.Contains(buhinNo) || componentNames.Contains(fileName))
                    row.DefaultCellStyle.BackColor = System.Drawing.Color.LightYellow;
            }
        }

        private void HighlightRows(HashSet<int> rowIndexes)
        {
            foreach (int rowIndex in rowIndexes)
            {
                if (rowIndex < 0 || rowIndex >= gridBom.Rows.Count)
                    continue;

                DataGridViewRow row = gridBom.Rows[rowIndex];
                if (!row.IsNewRow)
                    row.DefaultCellStyle.BackColor = System.Drawing.Color.LightYellow;
            }
        }


    }
}
