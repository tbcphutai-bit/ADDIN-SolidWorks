using System;
using System.Collections.Generic;
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
            ResetProgress();
            AutoFitBomGrid();
        }

        public void LoadBom(Func<bool> isCancellationRequested = null)
        {
            LoadedBomContext = BomCommandContext.None;
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
            bomLoader.LoadBOMTableToGrid(gridBom, swTable, isCancellationRequested);
            if (isCancellationRequested != null && isCancellationRequested())
            {
                lblStatus.Text = "Da huy CAP NHAT BOM.";
                return;
            }
            AutoFitBomGrid();
            chkSelectAll.Checked = true;
            SetAllChecked(true);
            SortGridByBuhinNoAscending();

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
            bomLoader?.ClearBomGrid(gridBom);
            LoadedBomContext = BomCommandContext.None;
            lblStatus.Text = "Da xoa BOM";
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
            gridBom.Columns[2].FillWeight = 80;
            gridBom.Columns[3].FillWeight = 60;
            gridBom.Columns[4].FillWeight = 60;
            gridBom.Columns[5].FillWeight = 180;
        }

        public void CheckDfTk()
        {
            if (swApp == null)
            {
                lblStatus.Text = "Chua ket noi SOLIDWORKS.";
                return;
            }

            cancelRequested = false;
            checkInProgress = true;
            KetQuaSoSanhDfTk result = null;
            try
            {
                ChaySoSanhDfTk runner = new ChaySoSanhDfTk(swApp, gridBom);
                result = runner.Run(BeginProgress, UpdateProgress, IsCancelRequested);
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

        public void RequestCancel()
        {
            if (!checkInProgress)
                return;

            cancelRequested = true;
            lblStatus.Text = "Dang huy CHECK DF/TK...";
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
