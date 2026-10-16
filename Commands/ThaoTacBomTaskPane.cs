using System;
using System.Collections.Generic;
using System.Drawing;
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
        private bool pendingDragCheckboxGroup;
        private bool dragEventsConfigured;
        private bool rowDragActive;
        private bool rowDragMoved;
        private bool dragStartsAtCheckbox;
        private bool sweepCheckedValue;
        private int dragAnchorRow = -1;
        private int lastSweepRow = -1;
        private int checkboxGestureVersion;
        private Point dragStartPoint;
        private Point dragMousePoint;
        private HashSet<DataGridViewRow> completedDragRows;
        private readonly HashSet<DataGridViewRow> sweptCheckboxRows = new HashSet<DataGridViewRow>();
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
            gridBom.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            ConfigureDragSelection();
            ConfigureGridForContext(BomCommandContext.None);
            ResetProgress();
        }

        public void LoadBom(Func<bool> isCancellationRequested = null)
        {
            ResetDragSelection();
            InvalidateDfTkCache();
            LoadedBomContext = BomCommandContext.None;
            ConfigureGridForContext(BomCommandContext.None);
            if (bomLoader == null)
            {
                lblStatus.Text = "Chưa kết nối SOLIDWORKS.";
                return;
            }

            ITableAnnotation swTable = bomLoader.GetCustomBomTable();

            if (swTable == null)
            {
                lblStatus.Text = "Không tìm thấy bảng BOM. Chọn bảng BOM trong bản vẽ rồi bấm Cập nhật.";
                return;
            }

            LoadedBomContext = bomLoader.GetBomCommandContext(swTable);
            ConfigureGridForContext(LoadedBomContext);

            // Avoid measuring and repainting every row while COM data is loaded.
            gridBom.SuspendLayout();
            try
            {
                bomLoader.LoadBOMTableToGrid(gridBom, swTable, isCancellationRequested);
                if (isCancellationRequested != null && isCancellationRequested())
                {
                    lblStatus.Text = "Đã dừng Cập nhật BOM.";
                    return;
                }

                chkSelectAll.Checked = true;
                SetAllChecked(true);
                SortGridByBuhinNoAscending();
            }
            finally
            {
                gridBom.ResumeLayout(true);
            }

            AutoFitBomGrid();
            gridBom.Invalidate(true);

            string bomLabel = LoadedBomContext == BomCommandContext.Unit
                ? "BOM UNIT"
                : "BOM chi tiết";
            lblStatus.Text = "Đã nạp " + bomLabel + ": " + gridBom.Rows.Count + " dòng";
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
            ResetDragSelection();
            InvalidateDfTkCache();
            bomLoader?.ClearBomGrid(gridBom);
            LoadedBomContext = BomCommandContext.None;
            ConfigureGridForContext(BomCommandContext.None);
            lblStatus.Text = "Đã xóa bảng BOM.";
        }

        private void ConfigureGridForContext(BomCommandContext context)
        {
            if (gridBom == null || gridBom.Columns.Count < 6)
                return;

            bool isUnit = context == BomCommandContext.Unit;
            gridBom.Columns[1].HeaderText = "部品\n番号";
            gridBom.Columns[2].HeaderText = isUnit ? "合番" : "材質";
            gridBom.Columns[2].Visible = true;
            gridBom.Columns[3].HeaderText = "板厚";
            gridBom.Columns[3].Visible = !isUnit;
            gridBom.Columns[4].HeaderText = "数量";
            gridBom.Columns[5].HeaderText = "部品ファイル名";

            UI.GridStyler.ConfigureColumns(gridBom);
        }

        public void SetAllChecked(bool isChecked)
        {
            ResetDragSelection();
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

        private void ConfigureDragSelection()
        {
            if (dragEventsConfigured) return;
            dragEventsConfigured = true;
            gridBom.MouseDown += GridBom_MouseDown;
            gridBom.MouseMove += GridBom_MouseMove;
            gridBom.MouseUp += GridBom_MouseUp;
            gridBom.MouseCaptureChanged += GridBom_MouseCaptureChanged;
            gridBom.Scroll += GridBom_Scroll;
            gridBom.RowsRemoved += (sender, args) => ResetDragSelection();
            gridBom.Sorted += (sender, args) => ResetDragSelection();
        }

        private void GridBom_MouseDown(object sender, MouseEventArgs e)
        {
            rowDragActive = false;
            rowDragMoved = false;
            sweptCheckboxRows.Clear();
            checkboxGestureVersion++;
            if (e.Button != MouseButtons.Left) return;
            DataGridView.HitTestInfo hit = gridBom.HitTest(e.X, e.Y);
            if (hit.Type != DataGridViewHitTestType.Cell || !IsBomRow(hit.RowIndex))
            {
                completedDragRows = null;
                return;
            }
            dragStartsAtCheckbox = hit.ColumnIndex == 0;
            if (!dragStartsAtCheckbox) completedDragRows = null;
            rowDragActive = true;
            dragAnchorRow = lastSweepRow = hit.RowIndex;
            dragStartPoint = dragMousePoint = e.Location;
            sweepCheckedValue = !Convert.ToBoolean(gridBom.Rows[hit.RowIndex].Cells[0].Value ?? false);
        }

        private void GridBom_MouseMove(object sender, MouseEventArgs e)
        {
            if (!rowDragActive) return;
            if ((e.Button & MouseButtons.Left) == MouseButtons.None)
            {
                FinishRowDrag();
                return;
            }
            dragMousePoint = e.Location;
            ContinueRowDrag();
        }

        private void GridBom_Scroll(object sender, ScrollEventArgs e)
        {
            if (rowDragActive) ContinueRowDrag();
        }

        private void ContinueRowDrag()
        {
            if (dragMousePoint.X < 0 || dragMousePoint.X >= gridBom.ClientSize.Width) return;
            Size threshold = SystemInformation.DragSize;
            Rectangle startArea = new Rectangle(dragStartPoint.X - threshold.Width / 2,
                dragStartPoint.Y - threshold.Height / 2, threshold.Width, threshold.Height);
            if (!rowDragMoved && startArea.Contains(dragMousePoint)) return;
            int rowIndex = gridBom.HitTest(dragMousePoint.X, dragMousePoint.Y).RowIndex;
            // DataGridView handles capture and edge scrolling. Follow the visible
            // boundary row when the pointer has moved above or below its cells.
            if (rowIndex < 0 && gridBom.FirstDisplayedScrollingRowIndex >= 0)
            {
                int first = gridBom.FirstDisplayedScrollingRowIndex;
                Rectangle bounds = gridBom.GetRowDisplayRectangle(first, false);
                if (dragMousePoint.Y < bounds.Top) rowIndex = first;
                else
                {
                    int last = first;
                    for (int i = first; i < gridBom.Rows.Count; i++)
                    {
                        if (gridBom.Rows[i].Displayed) last = i;
                    }
                    int bottom = Math.Min(gridBom.ClientSize.Height,
                        gridBom.GetRowDisplayRectangle(last, false).Bottom);
                    if (dragMousePoint.Y >= bottom) rowIndex = last;
                }
            }
            if (!IsBomRow(rowIndex) || !IsBomRow(dragAnchorRow)) return;
            if (!rowDragMoved && rowIndex == dragAnchorRow) return;
            rowDragMoved = true;
            if (!dragStartsAtCheckbox) return;
            pendingCheckboxRows = null;
            pendingDragCheckboxGroup = false;
            gridBom.CancelEdit();
            for (int i = Math.Min(lastSweepRow, rowIndex); i <= Math.Max(lastSweepRow, rowIndex); i++)
            {
                if (!IsBomRow(i) || !gridBom.Rows[i].Visible) continue;
                DataGridViewRow row = gridBom.Rows[i];
                sweptCheckboxRows.Add(row);
                row.Cells[0].Value = sweepCheckedValue;
            }
            lastSweepRow = rowIndex;
            gridBom.EndEdit();
        }

        private bool IsBomRow(int index)
        {
            return index >= 0 && index < gridBom.Rows.Count && !gridBom.Rows[index].IsNewRow;
        }

        private void GridBom_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) FinishRowDrag();
        }

        private void GridBom_MouseCaptureChanged(object sender, EventArgs e)
        {
            if (!gridBom.Capture) FinishRowDrag();
        }

        private void FinishRowDrag()
        {
            if (!rowDragActive) return;
            if (rowDragMoved)
            {
                // Painting checkboxes finishes its own operation. Only a drag
                // on the data cells prepares a later checkbox group click.
                completedDragRows = dragStartsAtCheckbox ? null : new HashSet<DataGridViewRow>(
                    GetSelectedRowIndexes().Select(index => gridBom.Rows[index]));
                if (completedDragRows != null && completedDragRows.Count < 2) completedDragRows = null;
            }
            rowDragActive = false;
            rowDragMoved = false;
        }

        private void ResetDragSelection()
        {
            rowDragActive = rowDragMoved = pendingDragCheckboxGroup = false;
            completedDragRows = null;
            pendingCheckboxRows = null;
            sweptCheckboxRows.Clear();
            checkboxGestureVersion++;
        }

        public void BeginCheckboxSelection(int rowIndex, int columnIndex)
        {
            pendingCheckboxRows = null;
            pendingDragCheckboxGroup = false;
            sweptCheckboxRows.Clear();
            checkboxGestureVersion++;
            if (!IsBomRow(rowIndex) || columnIndex != 0) return;
            HashSet<int> selectedIndexes = GetSelectedRowIndexes();
            pendingDragCheckboxGroup = completedDragRows != null &&
                completedDragRows.Contains(gridBom.Rows[rowIndex]) &&
                completedDragRows.SetEquals(selectedIndexes.Select(index => gridBom.Rows[index]));
            bool modifierSelectionRequested =
                (Control.ModifierKeys & (Keys.Control | Keys.Shift)) != Keys.None;
            // A deliberate row drag also requests a group change. An ordinary
            // click still changes one checkbox, regardless of stale highlights.
            pendingCheckboxRows = modifierSelectionRequested || pendingDragCheckboxGroup
                ? selectedIndexes : new HashSet<int>();
            pendingCheckboxRows.Add(rowIndex);
        }

        public void ApplyCheckboxSelection(int rowIndex, int columnIndex)
        {
            if (!IsBomRow(rowIndex) || columnIndex != 0) return;
            CommitCurrentCellIfDirty();
            DataGridViewRow target = gridBom.Rows[rowIndex];
            bool swept = sweptCheckboxRows.Count > 0;
            HashSet<int> indexes = pendingCheckboxRows;
            bool restoreDragSelection = pendingDragCheckboxGroup;
            int gestureVersion = checkboxGestureVersion;
            pendingCheckboxRows = null;
            pendingDragCheckboxGroup = false;
            if (indexes == null || indexes.Count == 0) indexes = new HashSet<int> { rowIndex };
            DataGridViewRow[] rows = swept ? sweptCheckboxRows.ToArray() :
                indexes.Where(IsBomRow).Select(index => gridBom.Rows[index]).ToArray();
            bool newValue = swept ? sweepCheckedValue : Convert.ToBoolean(target.Cells[0].Value ?? false);
            if (invoker.IsDisposed || invoker.Disposing || !invoker.IsHandleCreated) return;
            invoker.BeginInvoke(new Action(() =>
            {
                if (gridBom.IsDisposed || gridBom.Disposing || target.DataGridView != gridBom) return;
                foreach (DataGridViewRow row in rows)
                {
                    if (row.DataGridView == gridBom && !row.IsNewRow) row.Cells[0].Value = newValue;
                }
                gridBom.EndEdit();
                if (restoreDragSelection && gestureVersion == checkboxGestureVersion)
                {
                    gridBom.ClearSelection();
                    foreach (DataGridViewRow row in rows)
                    {
                        if (row.DataGridView == gridBom && !row.IsNewRow) row.Selected = true;
                    }
                }
            }));
        }

        public void ToggleSelectedRowsBySpace(KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Space || gridBom.SelectedCells.Count == 0)
                return;

            rowDragActive = rowDragMoved = pendingDragCheckboxGroup = false;
            pendingCheckboxRows = null;
            sweptCheckboxRows.Clear();
            checkboxGestureVersion++;
            bool check = !AreAllSelectedRowsChecked();
            SetSelectedRowsChecked(check);
            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        public void AutoFitBomGrid()
        {
            if (gridBom == null || gridBom.Columns.Count < 6)
                return;

            UI.GridStyler.ConfigureColumns(gridBom);
        }

        public bool CheckDfTk()
        {
            if (swApp == null)
            {
                lblStatus.Text = "Chưa kết nối SOLIDWORKS.";
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
                lblStatus.Text = "Chưa kết nối SOLIDWORKS.";
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
                lblStatus.Text = "Chưa kết nối SOLIDWORKS.";
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
                lblStatus.Text = "Chưa kết nối SOLIDWORKS.";
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
                lblStatus.Text = "Chưa kết nối SOLIDWORKS.";
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
                lblStatus.Text = "Chưa kết nối SOLIDWORKS.";
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
                        + ": đang kiểm tra \u30A6\u30E9\u8868...";
                    combined.UraOmote = new CheckUraOmoteRunner(swApp, gridBom)
                        .Run(BeginProgress, UpdateProgress, IsCancelRequested);
                }

                if (options.CheckKegaki && !combined.Canceled && !IsCancelRequested())
                {
                    currentStep++;
                    lblStatus.Text = "CHECK URA/KEGAKI " + currentStep + "/" + totalSteps
                        + ": đang kiểm tra KEGAKI...";
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
                lblStatus.Text = "Đã dừng CHECK URA/KEGAKI.";
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
                lblStatus.Text = "Chưa tick chi tiết nào để CHECK URA/KEGAKI.";
                return;
            }

            ExcelCombinedCheckExporter.Export(combined, gridBom);
            lblStatus.Text = "CHECK URA/KEGAKI xong. Đã xuất Excel gồm "
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
            lblStatus.Text = "Đang dừng lệnh kiểm tra…";
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
                lblStatus.Text = "Đã dừng CHECK DF/TK. Đã xử lý: " + result.ProcessedCount + "/" + result.CheckedCount;
                return;
            }

            if (result.CheckedCount == 0)
            {
                lblStatus.Text = "Chưa tick chi tiết nào để CHECK DF/TK.";
                return;
            }

            if (result.DiffResults.Count == 0)
            {
                string message = "Không có chi tiết nào khác nhau.";
                if (result.SkippedCount > 0)
                    message += " Bỏ qua " + result.SkippedCount + " dòng không có đường dẫn Part.";
                if (result.CheckLogs.Count > 0)
                    message += " Có " + result.CheckLogs.Count + " dòng log kiểm tra.";

                lblStatus.Text = message;
                MessageBox.Show(message, "Kết quả kiểm tra", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            ExcelDfTkExporter.Export(result.DiffResults, result.CheckLogs);
            lblStatus.Text = "CHECK DF/TK xong. Đã chọn: " + result.CheckedCount + ", khác nhau: " + result.DiffResults.Count;
        }

        private void ShowUraOmoteResult(UraOmoteCheckResult result)
        {
            if (result == null)
            {
                lblStatus.Text = "Không có kết quả Check ウラ 表.";
                return;
            }

            if (result.Canceled)
            {
                lblStatus.Text = "Đã dừng Check ウラ 表. Đã xử lý: " + result.ProcessedCount + "/" + result.CheckedCount;
                return;
            }

            if (result.CheckedCount == 0)
            {
                lblStatus.Text = "Chưa tick chi tiết nào để Check ウラ 表.";
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

            lblStatus.Text = "Check ウラ 表 xong. Đã chọn: " + result.CheckedCount
                + ", NG: " + ngCount
                + ", CHECK: " + checkCount
                + ", bỏ qua: " + result.SkippedCount;
        }

        private void ShowKegakiResult(KegakiCheckResult result)
        {
            if (result == null)
            {
                lblStatus.Text = "Không có kết quả CHECK KEGAKI.";
                return;
            }

            if (result.Canceled)
            {
                lblStatus.Text = "Đã dừng CHECK KEGAKI. Đã xử lý: "
                    + result.ProcessedCount + "/" + result.CheckedCount;
                return;
            }

            if (result.CheckedCount == 0)
            {
                lblStatus.Text = "Chưa tick chi tiết nào để CHECK KEGAKI.";
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

            lblStatus.Text = "CHECK KEGAKI xong. Đã chọn: " + result.CheckedCount
                + ", bend override: " + overrideCount
                + ", NG: " + ngCount
                + ", CHECK: " + checkCount
                + ", bỏ qua: " + result.SkippedCount;
        }

        private void ShowRoundResult(RoundHoleCheckResult result)
        {
            if (result == null)
            {
                lblStatus.Text = "Không có kết quả CHECK ROUND.";
                return;
            }

            if (result.Canceled)
            {
                lblStatus.Text = "Đã dừng CHECK ROUND. Đã xử lý: "
                    + result.ProcessedCount + "/" + result.CheckedCount;
                return;
            }

            if (result.CheckedCount == 0)
            {
                lblStatus.Text = "Chưa tick chi tiết nào để CHECK ROUND.";
                return;
            }

            int previewCount = RoundHolePreviewForm.ShowPreview(result.Results);
            int exportedCount = ExcelRoundHoleExporter.Export(result.Results);
            RoundHolePreviewForm.BringLatestToFront();
            string message = "CHECK ROUND xong. Lỗ tròn: " + result.RoundHoleCount
                + ", lỗ dài: " + result.SlotHoleCount
                + ", NG: " + result.NgCount
                + ", CHECK: " + result.CheckCount
                + ", bỏ qua: " + result.SkippedCount + ".";

            if (previewCount > 0)
                message += " Đã mở preview 2D cho " + previewCount + " chi tiết.";
            else if (result.NgCount + result.CheckCount > 0)
                message += " Không tạo được preview 2D cho kết quả này.";

            if (exportedCount > 0)
                message += " Đã xuất Excel " + exportedCount + " dòng cần kiểm tra.";
            else
                message += " Không có lỗ bất thường để xuất Excel.";

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
                lblStatus.Text = "Không có kết quả CHECK SAME PART.";
                return;
            }

            if (result.Canceled)
            {
                lblStatus.Text = "Đã dừng CHECK SAME PART. Đã xử lý: "
                    + result.ProcessedCount + "/" + result.CheckedCount;
                return;
            }

            if (result.CheckedCount == 0)
            {
                lblStatus.Text = "Chưa tick chi tiết nào để CHECK SAME PART.";
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
            string message = "CHECK SAME PART xong. Nhóm trùng hoàn toàn: " + sameFull
                + ", trùng hình học: " + sameGeometry
                + ", chỉ trùng Flat-Pattern: " + sameFlat
                + ", mirror/cần đối chiếu: " + mirrorCheck
                + ", cần kiểm tra: " + result.Errors.Count + ".";

            if (exportedCount > 0)
            {
                message += " Đã xuất Excel " + exportedCount + " dòng.";
                if (!string.IsNullOrWhiteSpace(exportedPath))
                    message += " File: " + exportedPath;
            }
            else
                message += " Không tìm thấy nhóm chi tiết giống nhau.";
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
                return "Không có kết quả CHECK DF/TK.";

            List<string> lines = new List<string>
            {
                "CHECK DF/TK DEBUG",
                "Da chon: " + result.CheckedCount,
                "Đã xử lý: " + result.ProcessedCount,
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
