using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.swpublished;

namespace ADDIN.UI.AutoFrameJoint
{
    /// <summary>
    /// Native SOLIDWORKS PropertyManager Page for selecting EXACTLY two distinct
    /// Part component occurrences. This class is intentionally UI-only.
    ///
    /// No cut features, geometry edits, configuration switching or saves.
    /// The read-only analyzer and 3D temporary-body preview belong to backend.
    /// </summary>
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.AutoDispatch)]
    public sealed class AutoFrameJointSelectionPage : PropertyManagerPage2Handler9
    {
        private const int GroupSelection = 100;
        private const int BoxFrameA = 101;
        private const int BoxFrameB = 102;
        private const int ButtonAnalyze = 103;
        private const int LabelStatus = 104;
        private const int GroupInformation = 110;
        private const int LabelInformation = 111;

        // Distinct powers of two; do not use 0 or the same mark for both boxes.
        private const int MarkFrameA = 1;
        private const int MarkFrameB = 2;

        private readonly ISldWorks app;
        private readonly ModelDoc2 assembly;
        private readonly Func<Component2, Component2, string> onAnalyze;
        private readonly Action onClearPreview;
        private readonly Action<AutoFrameJointSelectionPage> onClosed;

        private PropertyManagerPage2 page;
        private PropertyManagerPageSelectionbox selectionA;
        private PropertyManagerPageSelectionbox selectionB;
        private PropertyManagerPageControl analyzeButtonControl;
        private PropertyManagerPageLabel statusLabel;

        private bool shown;
        private bool closing;
        private bool handlingAnalyze;
        private bool updatingReadyState;

        // Keep analysis output while the same two component occurrences remain
        // selected. Do not use Name2 as an occurrence identity.
        private Component2 analyzedA;
        private Component2 analyzedB;
        private string lastAnalysisMessage;

        /// <summary>
        /// onAnalyze is an injected BACKEND callback. It receives A and B
        /// explicitly (not unmarked global selection order) and may optionally
        /// return a short read-only analysis summary.
        /// onClearPreview belongs to backend and is called AFTER page closes.
        /// Caller must retain this Page instance while SOLIDWORKS displays it.
        /// </summary>
        public AutoFrameJointSelectionPage(
            ISldWorks app,
            Func<Component2, Component2, string> onAnalyze,
            Action onClearPreview,
            Action<AutoFrameJointSelectionPage> onClosed)
        {
            this.app = app;
            this.onAnalyze = onAnalyze;
            this.onClearPreview = onClearPreview;
            this.onClosed = onClosed;
            this.assembly = app == null ? null : app.ActiveDoc as ModelDoc2;
        }

        public bool IsOpen { get { return shown && !closing; } }

        public void Show()
        {
            if (shown) return;
            if (assembly == null ||
                assembly.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
                throw new InvalidOperationException(
                    "Hãy mở Assembly trước khi dùng Auto Frame Joint.");

            int error = 0;
            const int options =
                (int)swPropertyManagerPageOptions_e.swPropertyManagerOptions_CancelButton;
            page = app.CreatePropertyManagerPage(
                "AUTO FRAME JOINT", options, this, ref error)
                as PropertyManagerPage2;

            if (page == null ||
                error != (int)swPropertyManagerPageStatus_e.swPropertyManagerPage_Okay)
                throw new InvalidOperationException(
                    "Không tạo được PropertyManager Page. Mã lỗi: " + error);

            BuildControls();
            shown = true;
            try
            {
                page.Show2(0);
                UpdateReadyState();
                selectionA.SetSelectionFocus();
            }
            catch
            {
                shown = false;
                throw;
            }
        }

        private void BuildControls()
        {
            int groupOptions =
                (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Visible |
                (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Expanded;
            int controlOptions =
                (int)swAddControlOptions_e.swControlOptions_Visible |
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            short align =
                (short)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;

            PropertyManagerPageGroup group = page.AddGroupBox(
                GroupSelection, "Chọn hai Frame", groupOptions)
                as PropertyManagerPageGroup;
            if (group == null)
                throw new InvalidOperationException("Không tạo được nhóm chọn Frame.");

            selectionA = group.AddControl2(
                BoxFrameA,
                (short)swPropertyManagerPageControlType_e.swControlType_Selectionbox,
                "Frame A", align, controlOptions, "Chọn Part thứ nhất.")
                as PropertyManagerPageSelectionbox;

            selectionB = group.AddControl2(
                BoxFrameB,
                (short)swPropertyManagerPageControlType_e.swControlType_Selectionbox,
                "Frame B", align, controlOptions, "Chọn Part thứ hai.")
                as PropertyManagerPageSelectionbox;

            analyzeButtonControl = group.AddControl2(
                ButtonAnalyze,
                (short)swPropertyManagerPageControlType_e.swControlType_Button,
                "Phân tích", align, controlOptions,
                "Đọc hai Frame. Không cắt hoặc thay đổi Part.")
                as PropertyManagerPageControl;

            statusLabel = group.AddControl2(
                LabelStatus,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "Chọn Frame A và Frame B", align, controlOptions,
                "Trạng thái chọn Frame.")
                as PropertyManagerPageLabel;

            if (selectionA == null || selectionB == null ||
                analyzeButtonControl == null || statusLabel == null)
                throw new InvalidOperationException(
                    "Thiếu một hoặc nhiều điều khiển của PropertyManager.");

            selectionA.Mark = MarkFrameA;
            selectionB.Mark = MarkFrameB;
            selectionA.SingleEntityOnly = true;
            selectionB.SingleEntityOnly = true;
            selectionA.Height = 42;
            selectionB.Height = 42;

            int[] filter = { (int)swSelectType_e.swSelCOMPONENTS };
            selectionA.SetSelectionFilters(filter);
            selectionB.SetSelectionFilters(filter);
            analyzeButtonControl.Enabled = false;

            var detailsGroup = page.AddGroupBox(
                GroupInformation, "Thông tin", groupOptions)
                as PropertyManagerPageGroup;
            if (detailsGroup != null)
            {
                detailsGroup.AddControl2(
                    LabelInformation,
                    (short)swPropertyManagerPageControlType_e.swControlType_Label,
                    "Preview cắt: chưa hỗ trợ. Chỉ đọc Assembly.",
                    align, controlOptions,
                    "Phần hiển thị body tạm do backend quản lý.");
            }
        }

        private bool IsSameDocumentActive()
        {
            try
            {
                ModelDoc2 current = app.ActiveDoc as ModelDoc2;
                return current != null && assembly != null &&
                    app.IsSame(current, assembly) ==
                        (int)swObjectEquality.swObjectSame;
            }
            catch (Exception ex)
            {
                Trace.WriteLine("[AFJ-PMP] Active document check: " + ex.Message);
                return false; // Unsupported/failed COM identity is NOT success.
            }
        }

        // Must be conclusively different. swObjectUnsupported (2) is not
        // a safe basis for permitting two component instances.
        private bool DefinitelyDifferent(Component2 x, Component2 y)
        {
            if (x == null || y == null) return false;
            try
            {
                return app.IsSame(x, y) ==
                    (int)swObjectEquality.swObjectNotSame;
            }
            catch (Exception ex)
            {
                Trace.WriteLine("[AFJ-PMP] Component identity check: " + ex.Message);
                return false;
            }
        }

        private Component2 Selected(int mark)
        {
            if (assembly == null) return null;
            SelectionMgr mgr = assembly.SelectionManager as SelectionMgr;
            if (mgr == null || mgr.GetSelectedObjectCount2(mark) != 1)
                return null;

            return mgr.GetSelectedObjectsComponent4(1, mark) as Component2
                ?? mgr.GetSelectedObject6(1, mark) as Component2;
        }

        /// <summary>
        /// When deepCheck=false, only query selection marks and COM identities.
        /// Reading Part documents belongs to the Analyze button, never
        /// OnSelectionboxListChanged (which occurs during SOLIDWORKS selection).
        /// </summary>
        private bool TryReadSelection(out Component2 a, out Component2 b,
                                      out string reason, bool deepCheck)
        {
            a = null;
            b = null;
            if (!IsSameDocumentActive())
            {
                reason = "Assembly đã đổi. Đóng lệnh và mở lại.";
                return false;
            }

            a = Selected(MarkFrameA);
            b = Selected(MarkFrameB);
            if (a == null || b == null)
            {
                reason = "Chọn đủ Frame A và Frame B.";
                return false;
            }

            if (!DefinitelyDifferent(a, b))
            {
                reason = "Hai Frame trùng nhau hoặc chưa xác minh được.";
                return false;
            }

            if (deepCheck)
            {
                // Slow/model-level checks only on explicit user action.
                ModelDoc2 docA = a.GetModelDoc2() as ModelDoc2;
                ModelDoc2 docB = b.GetModelDoc2() as ModelDoc2;
                if (docA == null || docB == null)
                {
                    reason = "Part chưa tải. Hãy Resolve thủ công.";
                    return false;
                }
                if (docA.GetType() != (int)swDocumentTypes_e.swDocPART ||
                    docB.GetType() != (int)swDocumentTypes_e.swDocPART)
                {
                    reason = "Chỉ hỗ trợ hai Part, không nhận Subassembly.";
                    return false;
                }
            }

            reason = deepCheck
                ? "Hai Part hợp lệ để phân tích sơ bộ."
                : "Đã chọn 2 Component — nhấn Phân tích.";
            return true;
        }

        private void ClearAnalysisResult()
        {
            analyzedA = null;
            analyzedB = null;
            lastAnalysisMessage = null;
        }

        private void StoreAnalysisResult(Component2 a, Component2 b, string message)
        {
            analyzedA = a;
            analyzedB = b;
            lastAnalysisMessage = message;
        }

        private bool IsPreviousPair(Component2 a, Component2 b)
        {
            // A/B must be the same occurrences in the same positions.
            // swObjectUnsupported or COM failure must invalidate old output.
            return analyzedA != null && analyzedB != null &&
                ConclusivelySame(a, analyzedA) &&
                ConclusivelySame(b, analyzedB);
        }

        private bool ConclusivelySame(Component2 a, Component2 b)
        {
            if (a == null || b == null) return false;
            try
            {
                return app.IsSame(a, b) ==
                    (int)swObjectEquality.swObjectSame;
            }
            catch (Exception ex)
            {
                Trace.WriteLine("[AFJ-PMP] Stored identity check: " + ex.Message);
                return false;
            }
        }

        private void UpdateReadyState()
        {
            if (!shown || closing || updatingReadyState) return;
            updatingReadyState = true;
            try
            {
                Component2 a, b;
                string reason;
                bool ready = TryReadSelection(out a, out b, out reason, false);

                if (!ready || !IsPreviousPair(a, b))
                    ClearAnalysisResult();

                if (analyzeButtonControl != null)
                    analyzeButtonControl.Enabled = ready && !handlingAnalyze;
                if (statusLabel != null)
                    statusLabel.Caption = ready &&
                        !String.IsNullOrEmpty(lastAnalysisMessage)
                        ? lastAnalysisMessage : reason;
            }
            catch (Exception ex)
            {
                ClearAnalysisResult();
                Trace.WriteLine("[AFJ-PMP] Ready state error: " + ex);
                if (analyzeButtonControl != null)
                    analyzeButtonControl.Enabled = false;
                if (statusLabel != null)
                    statusLabel.Caption = "Lỗi chọn Frame — xem Output.";
            }
            finally
            {
                updatingReadyState = false;
            }
        }

        private void PerformAnalyze()
        {
            if (closing || handlingAnalyze) return;

            Component2 a = null, b = null;
            string reason;
            handlingAnalyze = true;
            if (analyzeButtonControl != null)
                analyzeButtonControl.Enabled = false;

            try
            {
                // Read the selected pair with the same shallow constraints as
                // readiness, then validate component documents only here.
                if (!TryReadSelection(out a, out b, out reason, true))
                {
                    if (a != null && b != null && DefinitelyDifferent(a, b))
                        StoreAnalysisResult(a, b, reason);
                    else
                    {
                        ClearAnalysisResult();
                        if (statusLabel != null) statusLabel.Caption = reason;
                    }
                    return;
                }

                if (onAnalyze == null)
                {
                    StoreAnalysisResult(a, b,
                        "Đã chọn 2 Frame. Chưa nối bộ phân tích.");
                    return;
                }

                string result = onAnalyze(a, b);
                StoreAnalysisResult(a, b,
                    String.IsNullOrWhiteSpace(result)
                        ? "Không nhận được kết quả phân tích."
                        : result.Replace("\r", " ").Replace("\n", " "));
            }
            catch (Exception ex)
            {
                Trace.WriteLine("[AFJ-PMP] Analyze error: " + ex);
                if (a != null && b != null && DefinitelyDifferent(a, b))
                    StoreAnalysisResult(a, b, "Lỗi phân tích — xem Output.");
                else
                    ClearAnalysisResult();
            }
            finally
            {
                handlingAnalyze = false;
                UpdateReadyState(); // preserves message for same A/B pair
            }
        }

        // ---------------- Native PMP callback methods ----------------
        public void AfterActivation() { }

        public void OnClose(int reason)
        {
            closing = true;
            // Native page has Cancel only; there is no Apply Cut here.
        }

        public void AfterClose()
        {
            closing = true;
            shown = false;
            ClearAnalysisResult();
            page = null;
            selectionA = null;
            selectionB = null;
            analyzeButtonControl = null;
            statusLabel = null;
            try
            {
                if (onClearPreview != null) onClearPreview();
            }
            catch (Exception ex)
            {
                Trace.WriteLine("[AFJ-PMP] Preview cleanup failed: " + ex);
            }
            finally
            {
                try
                {
                    if (onClosed != null) onClosed(this);
                }
                catch (Exception ex)
                {
                    Trace.WriteLine("[AFJ-PMP] Page-close callback failed: " + ex);
                }
            }
        }

        public bool OnHelp() { return true; }
        public bool OnPreviousPage() { return false; }
        public bool OnNextPage() { return false; }
        public bool OnPreview() { return false; }
        public void OnWhatsNew() { }
        public void OnUndo() { }
        public void OnRedo() { }
        public bool OnTabClicked(int id) { return true; }
        public void OnGroupExpand(int id, bool expanded) { }
        public void OnGroupCheck(int id, bool isChecked) { }
        public void OnCheckboxCheck(int id, bool isChecked) { }
        public void OnOptionCheck(int id) { }
        public void OnButtonPress(int id)
        {
            if (id == ButtonAnalyze) PerformAnalyze();
        }
        public void OnTextboxChanged(int id, string value) { }
        public void OnNumberboxChanged(int id, double value) { }
        public void OnComboboxEditChanged(int id, string value) { }
        public void OnComboboxSelectionChanged(int id, int item) { }
        public void OnListboxSelectionChanged(int id, int item) { }
        public void OnSelectionboxFocusChanged(int id) { }
        public void OnSelectionboxListChanged(int id, int count)
        {
            // Allowed: query state and update PMP controls ONLY.
            // No ClearSelection2, Select4, boolean operation or Display3 here.
            if (id == BoxFrameA || id == BoxFrameB)
                UpdateReadyState();
        }
        public void OnSelectionboxCalloutCreated(int id) { }
        public void OnSelectionboxCalloutDestroyed(int id) { }

        public bool OnSubmitSelection(int id, object selection, int type,
                                      ref string itemText)
        {
            if (id != BoxFrameA && id != BoxFrameB)
                return false;
            if (type != (int)swSelectType_e.swSelCOMPONENTS)
                return false;
            if (!IsSameDocumentActive())
                return false;

            Component2 candidate = selection as Component2;
            if (candidate == null) return false;

            Component2 other = Selected(id == BoxFrameA ? MarkFrameB : MarkFrameA);
            if (other != null && !DefinitelyDifferent(candidate, other))
                return false;

            itemText = candidate.Name2;
            return true;
        }

        public int OnActiveXControlCreated(int id, bool status) { return 0; }
        public void OnSliderPositionChanged(int id, double value) { }
        public void OnSliderTrackingCompleted(int id, double value) { }
        public bool OnKeystroke(int wparam, int message, int lparam, int id)
        {
            return false;
        }
        public void OnPopupMenuItem(int id) { }
        public void OnPopupMenuItemUpdate(int id, ref int value) { }
        public void OnGainedFocus(int id) { }
        public void OnLostFocus(int id) { }
        public int OnWindowFromHandleControlCreated(int id, bool status)
        {
            return 0;
        }
        public void OnListboxRMBUp(int id, int x, int y) { }
        public void OnNumberBoxTrackingCompleted(int id, double value) { }
    }
}
