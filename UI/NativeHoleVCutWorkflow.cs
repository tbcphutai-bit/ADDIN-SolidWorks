using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ADDIN.Commands;
using ADDIN.UI;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN
{
    public partial class BomTaskPaneControl
    {
        private NativeCommandPropertyManagerPage nativeModelCommandPage;

        private void EnsureNativeModelCommandAvailable()
        {
            if (solidWorksClosing || swApp == null) throw new InvalidOperationException("Chưa kết nối SOLIDWORKS.");
            if (nativeModelCommandPage != null && nativeModelCommandPage.IsOpen)
                throw new InvalidOperationException("Hãy kết thúc lệnh native đang mở trước.");
            if ((nativeMakeHolePage != null && nativeMakeHolePage.IsOpen) || (autoSplinePage != null && autoSplinePage.IsOpen))
                throw new InvalidOperationException("Hãy kết thúc MAKE HOLE / AUTO SPLINE trước.");
            if (makeHoleCommand != null && makeHoleCommand.HasPendingHybridPattern)
                throw new InvalidOperationException("MAKE HOLE đang chờ Pattern. Hãy hoàn tất hoặc Cancel bước đang chờ trước.");
        }
        private void OpenNativeModelCommand(Func<Action<NativeCommandPropertyManagerPage>, NativeCommandPropertyManagerPage> create)
        {
            EnsureNativeModelCommandAvailable();
            bool wasEnabled = Enabled;
            nativeModelCommandPage = create(page => {
                if (ReferenceEquals(nativeModelCommandPage, page)) nativeModelCommandPage = null;
                if (!IsDisposed && !Disposing && !solidWorksClosing) Enabled = wasEnabled;
            });
            try
            {
                nativeModelCommandPage.Show();
                if (nativeModelCommandPage != null && nativeModelCommandPage.IsOpen) Enabled = false;
            }
            catch { DismissNativeModelCommand(); throw; }
        }
        private void NativeModelCommandError(string title, Exception ex)
        {
            try { if (swApp != null) swApp.SendMsgToUser2(title + ": " + ex.Message,
                (int)swMessageBoxIcon_e.swMbWarning, (int)swMessageBoxBtn_e.swMbOk); }
            catch (COMException) { }
            catch (InvalidComObjectException) { }
        }
        private void DismissNativeModelCommand()
        {
            var page = nativeModelCommandPage; nativeModelCommandPage = null;
            if (page != null) page.Dismiss();
        }
        private void ShowNativeRepairHolePage()
        {
            try
            {
                EnsureNativeModelCommandAvailable();
                if (makeHoleCommand == null) makeHoleCommand = new LenhMakeHole(swApp);
                BindRepairHoleViewModelToMakeHoleCommand();
                makeHoleCommand.ClearRepairHoleCache();
                OpenNativeModelCommand(closed => new RepairHolePropertyManagerPage(swApp,
                    () => {
                        Face2 face; List<LenhMakeHole.RepairHoleGroup> groups; string message;
                        if (!makeHoleCommand.ScanSelectedFaceForRepair(out face, out groups, out message))
                            throw new InvalidOperationException(message);
                        return new NativeRepairScan { Face = face, Groups = groups };
                    }, ExecuteNativeRepairHole, () => makeHoleCommand.ClearRepairHoleCache(), closed));
            }
            catch (Exception ex) { NativeModelCommandError("REPAIR HOLE", ex); }
        }
        private void ExecuteNativeRepairHole(Face2 face, List<LenhMakeHole.RepairHoleGroup> groups,
            List<LenhMakeHole.RepairHoleBatchItem> items)
        {
            if (solidWorksClosing || swApp == null || makeHoleCommand == null) return;
            try
            {
                // Keep the engine's explicit confirmation for topology-changing repair.
                List<LenhMakeHole.RepairHoleBatchItem> rebuild;
                if (makeHoleCommand.CheckRebuildRepairRequired(face, groups, items, out rebuild) && rebuild.Count > 0)
                {
                    DialogResult answer = MessageBox.Show(this, makeHoleCommand.GetRebuildRepairWarningMessage(rebuild),
                        "Repair Hole", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                    if (answer != DialogResult.Yes)
                    {
                        ShowNativeRepairHolePage();
                        var page = nativeModelCommandPage as RepairHolePropertyManagerPage;
                        if (page != null) page.RestoreDraft(face, groups, items);
                        return;
                    }
                }
                int count; string message;
                if (!makeHoleCommand.ExecuteRepairHoleBatch(face, items, GetRepairHoleOptionsForTaskPane(), out count, out message))
                    throw new InvalidOperationException(message);
                swApp.SendMsgToUser2("Đã Repair " + count + " lỗ.\n" + message,
                    (int)swMessageBoxIcon_e.swMbInformation, (int)swMessageBoxBtn_e.swMbOk);
            }
            finally { makeHoleCommand.ClearRepairHoleCache(); UpdateMakeHolePatternButton(); }
        }
        private void ShowNativeCountHolePage()
        {
            try
            {
                EnsureNativeModelCommandAvailable();
                if (paintHoleSummaryCommand == null) paintHoleSummaryCommand = new PaintHoleSummaryCommand(swApp);
                var model = swApp.ActiveDoc as ModelDoc2;
                if (model == null || (model.GetType() != (int)swDocumentTypes_e.swDocPART && model.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY))
                    throw new InvalidOperationException("Hãy mở Part hoặc Assembly để Đếm Hole.");
                string config = model.ConfigurationManager.ActiveConfiguration.Name;
                var result = paintHoleSummaryCommand.ScanForNativePage();
                var active = swApp.ActiveDoc as ModelDoc2;
                if (active == null || (!ReferenceEquals(model, active) && swApp.IsSame(model, active) != (int)swObjectEquality.swObjectSame) ||
                    config != active.ConfigurationManager.ActiveConfiguration.Name)
                    throw new InvalidOperationException("Model hoặc cấu hình thay đổi khi quét; hãy mở lại Đếm Hole.");
                OpenNativeModelCommand(closed => new CountHolePropertyManagerPage(swApp, result,
                    () => paintHoleSummaryCommand.ExportNativeResult(result), ShowNativeCountHolePage, closed));
            }
            catch (Exception ex) { NativeModelCommandError("ĐẾM HOLE", ex); }
        }
        private void ShowNativeVCutBendPage()
        { OpenNativeVCutBendPage(null, null, null, null); }
        private void OpenNativeVCutBendPage(List<VCutPropertySetupCommand.BendRow> draft,
            ModelDoc2 draftModel, string draftConfiguration, string draftPath, bool saved = false)
        {
            try
            {
                EnsureNativeModelCommandAvailable();
                var model = swApp.ActiveDoc as ModelDoc2;
                if (draft != null && !SameVCutDraftContext(model, draftModel, draftConfiguration, draftPath))
                    throw new InvalidOperationException("Part hoặc cấu hình đã thay đổi. Chưa khôi phục bản nháp Mark để tránh gán nhầm sang chi tiết khác.");
                if (vCutBendAssignmentControl.Visible && !vCutBendAssignmentControl.ConfirmDiscardChanges("mở PropertyManager V Cut")) return;
                var command = new VCutPropertySetupCommand(swApp);
                List<VCutPropertySetupCommand.BendRow> rows; Dictionary<string, string> properties; string message;
                if (!command.Load(out rows, out properties, out message)) throw new InvalidOperationException(message);
                int unmatched = 0;
                if (draft != null)
                {
                    // Load current feature/table data first. Restore only the Mark
                    // on the same native feature; names and list positions can change.
                    foreach (var previous in draft)
                    {
                        // Keep fresh external tags for untouched rows. A stale
                        // Save must not turn an old baseline into a new edit.
                        if (previous.Kind == previous.OriginalKind) continue;
                        var matches = rows.FindAll(row => SameVCutDraftFeature(row, previous));
                        if (matches.Count != 1) { unmatched++; continue; }
                        matches[0].Kind = previous.Kind;
                    }
                }
                string configuration = command.LoadedConfiguration;
                string path = model.GetPathName();
                vCutBendAssignmentControl.Visible = false;
                OpenNativeModelCommand(closed => {
                    var page = new VCutBendPropertyManagerPage(swApp, command, rows,
                        properties, () => QueueNativeVCutBendPage(null, null, null, null),
                        snapshot => QueueNativeVCutBendPage(snapshot, model, configuration, path), closed);
                    page.ReopenSaved = snapshot => QueueNativeVCutBendPage(snapshot, model, configuration, path, true);
                    return page;
                });
                if (saved) (nativeModelCommandPage as VCutBendPropertyManagerPage)?.ShowSavedState();
                if (unmatched > 0)
                    NativeModelCommandError("V CUT / 溝", new InvalidOperationException(
                        "Đã giữ Mark trên các Bend Feature còn khớp. " + unmatched +
                        " dòng đã bị thay thế hoặc không còn xác định được; hãy kiểm tra và gán lại trong bảng."));
            }
            catch (Exception ex) { NativeModelCommandError("V CUT / 溝 — BEND SETUP", ex); }
        }
        private void QueueNativeVCutBendPage(List<VCutPropertySetupCommand.BendRow> draft,
            ModelDoc2 model, string configuration, string path, bool saved = false)
        {
            if (IsDisposed || Disposing || solidWorksClosing || !IsHandleCreated) return;
            // Let SOLIDWORKS finish its native AfterClose callback before
            // creating the next page and its hosted HWND.
            BeginInvoke((Action)(() => {
                if (!IsDisposed && !Disposing && !solidWorksClosing)
                    OpenNativeVCutBendPage(draft, model, configuration, path, saved);
            }));
        }
        private bool SameVCutDraftContext(ModelDoc2 current, ModelDoc2 previous,
            string configuration, string path)
        {
            if (current == null || previous == null || current.GetType() != (int)swDocumentTypes_e.swDocPART) return false;
            try
            {
                return (ReferenceEquals(current, previous) || swApp.IsSame(current, previous) == (int)swObjectEquality.swObjectSame) &&
                    string.Equals(current.GetPathName(), path, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(current.ConfigurationManager.ActiveConfiguration.Name, configuration, StringComparison.OrdinalIgnoreCase);
            }
            catch (COMException) { return false; }
            catch (InvalidComObjectException) { return false; }
        }
        private bool SameVCutDraftFeature(VCutPropertySetupCommand.BendRow current,
            VCutPropertySetupCommand.BendRow previous)
        {
            if (current == null || previous == null || current.Feature == null || previous.Feature == null) return false;
            try
            {
                return ReferenceEquals(current.Feature, previous.Feature) ||
                    swApp.IsSame(current.Feature, previous.Feature) == (int)swObjectEquality.swObjectSame;
            }
            catch (COMException) { return false; }
            catch (InvalidComObjectException) { return false; }
        }
        private void RunSelectedSection()
        {
            try
            {
                EnsureNativeModelCommandAvailable();
                // The engine reads the selected cutting line and its owning View
                // directly. No parameter page or extra confirmation is needed.
                new AutoVCutSectionCommand(swApp).Run();
            }
            catch (Exception ex) { NativeModelCommandError("SECTION / 断面", ex); }
        }
    }
}
