using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ADDIN.Commands;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.UI
{
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.AutoDispatch)]
    public sealed class VCutBendPropertyManagerPage : NativeCommandPropertyManagerPage
    {
        private readonly VCutPropertySetupCommand command;
        private readonly List<VCutPropertySetupCommand.BendRow> rows;
        private readonly IDictionary<string, string> properties;
        private readonly Action refresh;
        private readonly Action<List<VCutPropertySetupCommand.BendRow>> restoreDraft;
        private VCutBendAssignmentControl table;
        private PropertyManagerPageWindowFromHandle tableWindow;
        private bool refreshing, valid, confirmedClose, keepOpenAfterSave;
        internal Action<List<VCutPropertySetupCommand.BendRow>> ReopenSaved { get; set; }
        private string validationMessage;
        private int previousPanelWidth, expandedPanelWidth, highlightRequest;

        internal VCutBendPropertyManagerPage(ISldWorks app, VCutPropertySetupCommand command,
            List<VCutPropertySetupCommand.BendRow> rows, IDictionary<string, string> properties,
            Action refresh, Action<List<VCutPropertySetupCommand.BendRow>> restoreDraft,
            Action<NativeCommandPropertyManagerPage> closed)
            : base(app, "V CUT / 溝 — BEND SETUP / 曲げ設定", closed)
        {
            this.command = command;
            this.rows = rows.Select(CloneRow).ToList();
            this.properties = properties;
            this.refresh = refresh;
            this.restoreDraft = restoreDraft;
        }
        protected override bool Supports(int type) { return type == (int)swDocumentTypes_e.swDocPART; }
        protected override void OnPreparingPage()
        {
            table = new VCutBendAssignmentControl();
            table.ConfigureForNativeCommand();
            previousPanelWidth = Model.GetFeatureManagerWidth();
            int minimumWidth = (int)Math.Ceiling(420.0 * table.DeviceDpi / 96.0);
            if (previousPanelWidth > 0 && previousPanelWidth < minimumWidth && Model.SetFeatureManagerWidth(minimumWidth) == 0)
                expandedPanelWidth = Model.GetFeatureManagerWidth();
        }
        protected override void BuildControls()
        {
            // Reuse the custom grid, status logic and per-row Mark editor.
            // Native OK consumes this cached draft after the HWND closes.
            table.LoadRows(rows, properties, command.LoadedPartTitle, command.LoadedConfiguration, refreshed: true);
            table.DraftChanged += OnDraftChanged;
            table.RefreshRequested += OnRefreshRequested;
            table.SaveRequested += OnSaveRequested;
            table.CloseRequested += OnCloseRequested;
            table.BendSelected += OnBendSelected;
            table.MinimumSelected += OnMinimumSelected;
            var group = Group(1, "Bend setup / 曲げ・溝設定");
            tableWindow = (PropertyManagerPageWindowFromHandle)Control(group, 11,
                swPropertyManagerPageControlType_e.swControlType_WindowFromHandle, "Bend table / 曲げ一覧",
                "Assign ○ / ◎ / ● in Mark / 加工印.");
            // SOLIDWORKS scales native page units; the hosted control handles
            // its own DPI. Leave room for the summary, full detail and footer.
            tableWindow.Height = 590;
            if (!tableWindow.SetWindowHandlex64(table.Handle.ToInt64()))
                throw new InvalidOperationException("Không mở được bảng Bend line.");
            Revalidate();
        }
        protected override void OnShown() { table.HighlightCurrentRow(); }
        private void OnDraftChanged(object sender, EventArgs args) { Handle(Revalidate); }
        private void OnRefreshRequested(object sender, EventArgs args) { RequestRefresh(); }
        private void RequestRefresh()
        { QueuePageAction(() => { refreshing = true; CloseOkay(); }); }
        private void OnSaveRequested(object sender, EventArgs args)
        {
            QueuePageAction(() => {
                Revalidate();
                if (!valid) { Notify(validationMessage); return; }
                // Save touches feature definitions only in AfterClose.
                keepOpenAfterSave = true;
                CloseOkay();
            });
        }
        private void OnCloseRequested(object sender, EventArgs args)
        {
            // The custom control has already confirmed discarding its draft.
            QueuePageAction(() => { confirmedClose = true; Page.Close(false); });
        }
        private void QueuePageAction(Action work)
        {
            if (!Interactive || table == null || table.IsDisposed || table.Disposing) return;
            // Do not destroy the hosted HWND on its WinForms Click stack.
            table.BeginInvoke((Action)(() => {
                if (Interactive) Handle(work);
            }));
        }
        private void OnMinimumSelected(VCutPropertySetupCommand.BendRow row)
        {
            if (!Interactive || row == null) return;
            int request = ++highlightRequest;
            table.BeginInvoke((Action)(() => {
                if (Interactive && request == highlightRequest) Handle(() => command.SelectMinimum(row));
            }));
        }
        private void OnBendSelected(VCutPropertySetupCommand.BendRow row)
        {
            if (!Interactive || row == null) return;
            int request = ++highlightRequest;
            // Select the actual feature after the custom grid callback returns.
            // Pending selections are invalidated before disposing the HWND.
            table.BeginInvoke((Action)(() => {
                if (!Interactive || request != highlightRequest) return;
                Handle(() => command.SelectFeature(row));
            }));
        }
        private void Revalidate()
        {
            // Validate the loaded draft while the native page is open. Save
            // re-reads feature definitions after the page closes.
            var errors = new List<string>();
            for (int i = 0; i < rows.Count; i++)
            {
                var issue = VCutPropertySetupCommand.GetDraftIssue(rows[i], rows);
                if (issue != VCutBendIssue.None)
                    errors.Add("Bend " + (i + 1) + ": " +
                        VCutPropertySetupCommand.GetDraftIssueMessage(rows[i], issue));
            }
            valid = errors.Count == 0 && table.MinimumModelReady;
            if (!table.MinimumModelReady) errors.Add("Chờ model cập nhật hoặc giải quyết Mark nháp chưa khớp.");
            validationMessage = valid
                ? "Sẵn sàng. Save / ✓ ghi Mark và cập nhật Custom Property; Close / ✕ đóng bảng."
                : errors[0] + " (" + errors.Count + " dòng cần kiểm tra).";
        }
        protected override bool Validate(out string message)
        { message = validationMessage ?? "Chọn Mark trực tiếp trong bảng."; return refreshing || valid; }
        protected override Action CaptureAccepted()
        {
            if (refreshing) return refresh;
            Revalidate();
            if (!valid) throw new InvalidOperationException(validationMessage);
            // The grid commits each change immediately. Never read destroyed
            // native controls or an editing control while closing the page.
            var snapshot = rows.Select(CloneRow).ToList();
            return () => {
                bool ok;
                try
                {
                    string message;
                    ok = command.Save(snapshot, properties, out message);
                    Notify(message);
                }
                catch (Exception ex)
                {
                    ok = false;
                    Notify("Chưa hoàn tất lưu gán bào: " + ex.Message +
                        "\r\nBảng sẽ quét lại dữ liệu hiện tại và giữ bản nháp Mark trên các Feature còn khớp.");
                }
                if (!ok && restoreDraft != null) restoreDraft(snapshot);
                else if (ok && keepOpenAfterSave && ReopenSaved != null) ReopenSaved(snapshot);
            };
        }
        internal void ShowSavedState()
        {
            if (table != null) table.RefreshAfterSave();
            UpdateReady();
        }
        protected override Action CaptureCancelled()
        {
            if (confirmedClose || restoreDraft == null ||
                !rows.Any(row => row.Kind != row.OriginalKind)) return null;
            var snapshot = rows.Select(CloneRow).ToList();
            // Native X / Escape cannot be vetoed in OnClose. Ask after teardown
            // and reopen the matching document's draft when the user cancels.
            return () => {
                var answer = VCutNoticeDialog.Confirm(null, "Thông tin bào chưa được lưu",
                    "Bạn đã sửa Mark nhưng chưa bấm Save.\r\n\r\n" +
                    "Tiếp tục sẽ bỏ các thay đổi chưa lưu.\r\n" +
                    "Chọn Hủy để mở lại bảng và giữ các Mark đang sửa.");
                if (answer != DialogResult.Yes) restoreDraft(snapshot);
            };
        }
        protected override void OnReleased(bool accepted)
        {
            ++highlightRequest;
            if (table != null)
            {
                table.DraftChanged -= OnDraftChanged;
                table.RefreshRequested -= OnRefreshRequested;
                table.SaveRequested -= OnSaveRequested;
                table.CloseRequested -= OnCloseRequested;
                table.BendSelected -= OnBendSelected;
                table.MinimumSelected -= OnMinimumSelected;
                table.ReleaseNativeCommand();
                table.Dispose();
            }
            table = null; tableWindow = null;
            if (previousPanelWidth > 0 && expandedPanelWidth > 0 && Same(App.ActiveDoc, Model) && Model.GetFeatureManagerWidth() == expandedPanelWidth)
                Model.SetFeatureManagerWidth(previousPanelWidth);
        }
        internal static VCutPropertySetupCommand.BendRow CloneRow(VCutPropertySetupCommand.BendRow row)
        {
            return new VCutPropertySetupCommand.BendRow {
                Feature = row.Feature, FeatureType = row.FeatureType, BendAngleDeg = row.BendAngleDeg,
                FeatureId = row.FeatureId, FeatureName = row.FeatureName,
                Material = row.Material, ModelThicknessMm = row.ModelThicknessMm,
                BendRadiusMm = row.BendRadiusMm, Table = row.Table, TableSource = row.TableSource,
                TableRevision = row.TableRevision,
                TableThicknessMm = row.TableThicknessMm, TableValueMm = row.TableValueMm,
                TableError = row.TableError, WrongBendTable = row.WrongBendTable, IsGrooveTable = row.IsGrooveTable,
                ValueMm = row.ValueMm, OriginalKind = row.OriginalKind, Kind = row.Kind, MinimumContext = row.MinimumContext, PersistentId = row.PersistentId, DraftConflict = row.DraftConflict };
        }
    }

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.AutoDispatch)]
    public sealed class VCutSectionPropertyManagerPage : NativeCommandPropertyManagerPage
    {
        private readonly Action execute;
        internal VCutSectionPropertyManagerPage(ISldWorks app, Action execute, Action<NativeCommandPropertyManagerPage> closed)
            : base(app, "SECTION / 断面", closed) { this.execute = execute; }
        protected override bool Supports(int type) { return type == (int)swDocumentTypes_e.swDocDRAWING; }
        protected override bool UsesSelectionBoxes { get { return true; } }
        // One box takes either pick, and the pick decides what ✓ does: a sketch
        // line creates the section, a drawing view is inspected.
        protected override int SelectionMark(object item, int type)
        {
            if (type == (int)swSelectType_e.swSelDRAWINGVIEWS && item is SolidWorks.Interop.sldworks.View) return 1;
            return item is SketchLine && (type == (int)swSelectType_e.swSelSKETCHSEGS || type == (int)swSelectType_e.swSelEXTSKETCHSEGS) ? 1 : 0;
        }
        // A line selected together with its view means "create": keep the line.
        protected override IEnumerable<Pick> PrepareInitialSelections(List<Pick> picks)
        {
            var line = picks.FirstOrDefault(pick => pick.Item is SketchLine);
            return line != null ? new[] { line } : picks.Take(1);
        }
        protected override void BuildControls()
        {
            var select = Group(10, "Section line or view / 切断線・ビュー");
            Selection(select, 11, "Section line (create) or drawing view (inspect) / 切断線（作成）・ビュー（確認）", 1,
                swSelectType_e.swSelSKETCHSEGS, swSelectType_e.swSelEXTSKETCHSEGS, swSelectType_e.swSelDRAWINGVIEWS);
        }
        protected override string HelpText
        {
            get
            {
                return "Create: select a straight sketch line in a 3D view.\n作成：3Dビュー内の直線を選択。\n" +
                    "Inspect: select one drawing view / 確認：ビューを選択。\n" +
                    "Normal bends are supported / 通常曲げも対応。\n" +
                    "Crossed grooves require marks / 切断する溝は加工印を設定。\n✓ Run / 実行    ✕ Cancel / 取消";
            }
        }
        public override bool OnSubmitSelection(int id, object item, int type, ref string text)
        { return !Closing && id == 11 && SelectionMark(item, type) == 1; }
        protected override bool Validate(out string message)
        {
            object pick = Selected(1);
            if (pick is SketchLine)
            {
                var view = Manager.GetSelectedObjectsDrawingView2(1, 1);
                if (view == null || view.ReferencedDocument == null || view.IsFlatPatternView())
                { message = "Line phải thuộc Drawing View 3D có model tham chiếu; không chọn Flat Pattern."; return false; }
                message = "Sẵn sàng tạo Section từ đường đã chọn. Bấm ✓."; return true;
            }
            var inspected = pick as SolidWorks.Interop.sldworks.View;
            if (inspected != null && inspected.ReferencedDocument != null)
            { message = "Sẵn sàng kiểm tra View đã chọn. Bấm ✓."; return true; }
            message = "Chọn một đường Line để tạo Section, hoặc một Drawing View để kiểm tra.";
            return false;
        }
        protected override Action CaptureAccepted()
        { string message; if (!Validate(out message)) throw new InvalidOperationException(message); return execute; }
    }
}
