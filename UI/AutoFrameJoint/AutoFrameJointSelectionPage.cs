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
        private const int ButtonShowBodies = 105; // Explicit ghost preview; no real cut.
        private const int GroupInformation = 110;
        private const int LabelInformation = 111;

        // Distinct powers of two; do not use 0 or the same mark for both boxes.
        private const int MarkFrameA = 1;
        private const int MarkFrameB = 2;

        private readonly ISldWorks app;
        private readonly ModelDoc2 assembly;
        private readonly Func<Component2, Component2, string> onAnalyze;
        private readonly Func<Component2, Component2, string> onShowBodies;
        private readonly Action onClearPreview;
        private readonly Action<AutoFrameJointSelectionPage> onClosed;

        private PropertyManagerPage2 page;
        private PropertyManagerPageSelectionbox selectionA;
        private PropertyManagerPageSelectionbox selectionB;
        private PropertyManagerPageControl analyzeButtonControl;
        private PropertyManagerPageControl showBodiesButtonControl;
        private PropertyManagerPageLabel statusLabel;

        private bool shown;
        private bool closing;
        private bool handlingAnalyze;
        private bool handlingShowBodies;
        private bool updatingReadyState;
        // A lightweight WinForms HWND schedules focus AFTER the native selection callback.
        // OnSelectionboxListChanged must never change SOLIDWORKS selection/focus inline.
        private System.Windows.Forms.Control deferredFocusWindow;
        private bool selectionRefreshQueued;
        private bool requestFocusB;

        // Keep analysis output while the same two component occurrences remain
        // selected. Do not use Name2 as an occurrence identity.
        private Component2 analyzedA;
        private Component2 analyzedB;
        private string lastAnalysisMessage;

        /// <summary>
        /// onAnalyze is an injected BACKEND callback. It receives A and B
        /// explicitly (not unmarked global selection order) and may optionally
        /// return a short read-only analysis summary.
        /// onShowBodies is invoked only by the explicit View A/B button.
        /// It never changes geometry and does not render proposed cut results.
        /// onClearPreview belongs to backend and is called AFTER page closes.
        /// Caller must retain this Page instance while SOLIDWORKS displays it.
        /// </summary>
        public AutoFrameJointSelectionPage(
            ISldWorks app,
            Func<Component2, Component2, string> onAnalyze,
            Func<Component2, Component2, string> onShowBodies,
            Action onClearPreview,
            Action<AutoFrameJointSelectionPage> onClosed)
        {
            this.app = app;
            this.onAnalyze = onAnalyze;
            this.onShowBodies = onShowBodies;
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
            InitializeDeferredFocusWindow();
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
                DisposeDeferredFocusWindow();
                throw;
            }
        }

        /// <summary>
        /// Creates a handle on the CURRENT SOLIDWORKS UI thread, so BeginInvoke
        /// runs later on the same UI thread after SOLIDWORKS has finished the
        /// in-progress selection callback. If handle setup fails, manual selection
        /// remains fully usable.
        /// </summary>
        private void InitializeDeferredFocusWindow()
        {
            System.Windows.Forms.Control dispatcher = null;
            try
            {
                dispatcher = new System.Windows.Forms.Control();
                IntPtr handle = dispatcher.Handle;
                deferredFocusWindow = dispatcher;
            }
            catch (Exception ex)
            {
                Trace.WriteLine("[AFJ-SEL] Deferred focus unavailable: " + ex);
                if (dispatcher != null) dispatcher.Dispose();
                deferredFocusWindow = null;
            }
        }

        /// <summary>
        /// Coalesce native selection notifications, then refresh the PMP and
        /// optionally advance A to B AFTER SOLIDWORKS returns from selection.
        /// Only a WinForms UI-thread message is posted here. No background COM.
        /// </summary>
        private void QueueSelectionRefresh(bool advanceFromA)
        {
            if (!shown || closing) return;

            if (advanceFromA)
                requestFocusB = true;

            // If the WinForms dispatcher cannot initialize, preserve legacy
            // manual-selection behavior and avoid losing the ready-state UI.
            if (deferredFocusWindow == null)
            {
                requestFocusB = false;
                UpdateReadyState();
                return;
            }

            if (selectionRefreshQueued) return;
            selectionRefreshQueued = true;
            try
            {
                deferredFocusWindow.BeginInvoke(
                    (System.Windows.Forms.MethodInvoker)delegate
                    {
                        selectionRefreshQueued = false;
                        if (!shown || closing) return;

                        // Keep all selection/identity COM checks OUT of
                        // the in-progress OnSelectionboxListChanged callback.
                        UpdateReadyState();

                        bool tryAdvance = requestFocusB;
                        requestFocusB = false;
                        if (!tryAdvance || selectionA == null ||
                            selectionB == null)
                            return;

                        try
                        {
                            // Never steal focus if user manually chose B
                            // or changed the current Assembly in the meantime.
                            if (selectionA.GetSelectionFocus() &&
                                IsSameDocumentActive() &&
                                Selected(MarkFrameA) != null &&
                                Selected(MarkFrameB) == null)
                            {
                                selectionB.SetSelectionFocus();
                                Trace.WriteLine("[AFJ-SEL] A->B AUTO FOCUS");
                            }
                        }
                        catch (Exception ex)
                        {
                            Trace.WriteLine("[AFJ-SEL] Auto focus unavailable: " + ex);
                        }
                    });
            }
            catch (Exception ex)
            {
                selectionRefreshQueued = false;
                requestFocusB = false;
                Trace.WriteLine("[AFJ-SEL] Defer UI failed: " + ex);
                // Selection continues to work even if BeginInvoke is unavailable.
                UpdateReadyState();
            }
        }

        private void DisposeDeferredFocusWindow()
        {
            System.Windows.Forms.Control deferred = deferredFocusWindow;
            deferredFocusWindow = null;
            selectionRefreshQueued = false;
            requestFocusB = false;
            if (deferred == null) return;
            try { deferred.Dispose(); }
            catch (Exception ex)
            {
                Trace.WriteLine("[AFJ-SEL] Deferred focus dispose: " + ex);
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
                "1. Frame A", align, controlOptions,
                "Click vào mặt, cạnh hoặc tên Component của Frame A.")
                as PropertyManagerPageSelectionbox;

            selectionB = group.AddControl2(
                BoxFrameB,
                (short)swPropertyManagerPageControlType_e.swControlType_Selectionbox,
                "2. Frame B", align, controlOptions,
                "Click vào mặt, cạnh hoặc tên Component của Frame B.")
                as PropertyManagerPageSelectionbox;

            analyzeButtonControl = group.AddControl2(
                ButtonAnalyze,
                (short)swPropertyManagerPageControlType_e.swControlType_Button,
                "Phân tích", align, controlOptions,
                "Đọc hai Frame. Không cắt hoặc thay đổi Part.")
                as PropertyManagerPageControl;

            showBodiesButtonControl = group.AddControl2(
                ButtonShowBodies,
                (short)swPropertyManagerPageControlType_e.swControlType_Button,
                "Xem trạng thái A/B", align, controlOptions,
                "Xem bản sao hình học hiện tại của Frame A/B. Chưa mô phỏng vết cắt.")
                as PropertyManagerPageControl;

            statusLabel = group.AddControl2(
                LabelStatus,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "Chọn Frame A và Frame B", align, controlOptions,
                "Trạng thái chọn Frame.")
                as PropertyManagerPageLabel;

            if (selectionA == null || selectionB == null ||
                analyzeButtonControl == null || showBodiesButtonControl == null ||
                statusLabel == null)
                throw new InvalidOperationException(
                    "Thiếu một hoặc nhiều điều khiển của PropertyManager.");

            selectionA.Mark = MarkFrameA;
            selectionB.Mark = MarkFrameB;
            selectionA.SingleEntityOnly = true;
            selectionB.SingleEntityOnly = true;
            selectionA.Height = 42;
            selectionB.Height = 42;

            // SOLIDWORKS selection rules:
            // FACES + EDGES + COMPONENTS => prefer the owning COMPONENT,
            // so one click on a frame surface/edge selects its Part occurrence.
            // Do NOT add swSelCOMPSDONTOVERRIDE; that forces a Face/Edge item.
            int[] filter =
            {
                (int)swSelectType_e.swSelFACES,
                (int)swSelectType_e.swSelEDGES,
                (int)swSelectType_e.swSelCOMPONENTS
            };
            selectionA.SetSelectionFilters(filter);
            selectionB.SetSelectionFilters(filter);
            analyzeButtonControl.Enabled = false;
            showBodiesButtonControl.Enabled = false;

            var detailsGroup = page.AddGroupBox(
                GroupInformation, "Thông tin", groupOptions)
                as PropertyManagerPageGroup;
            if (detailsGroup != null)
            {
                detailsGroup.AddControl2(
                    LabelInformation,
                    (short)swPropertyManagerPageControlType_e.swControlType_Label,
                    "Xem trạng thái A/B: hình học hiện tại, chưa có vết cắt mới.",
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
                reason = a == null
                    ? "Bước 1: Chọn Frame A trong Assembly."
                    : "Đã chọn Frame A — tiếp tục chọn Frame B.";
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
                    analyzeButtonControl.Enabled = ready && !handlingAnalyze &&
                                                   !handlingShowBodies;
                if (showBodiesButtonControl != null)
                    showBodiesButtonControl.Enabled = ready && !handlingAnalyze &&
                                                    !handlingShowBodies;
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
                if (showBodiesButtonControl != null)
                    showBodiesButtonControl.Enabled = false;
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
            if (closing || handlingAnalyze || handlingShowBodies) return;

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

        /// <summary>
        /// Explicit user-triggered read-only ghost overlay. Never called by
        /// selection-list events because SOLIDWORKS is updating its selection.
        /// Uses the same pair validation as Analyze. The backend owns all COM
        /// body references and performs cleanup on AfterClose.
        /// </summary>
        private void PerformShowBodies()
        {
            if (closing || handlingAnalyze || handlingShowBodies) return;

            Component2 a = null, b = null;
            string reason;
            handlingShowBodies = true;
            if (showBodiesButtonControl != null)
                showBodiesButtonControl.Enabled = false;

            try
            {
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

                if (onShowBodies == null)
                {
                    StoreAnalysisResult(a, b, "Chưa kết nối bộ hiển thị A/B.");
                    return;
                }

                string result = onShowBodies(a, b);
                // Reuse pair-scoped status cache; old messages are invalidated
                // automatically if the user changes either selected occurrence.
                StoreAnalysisResult(a, b,
                    String.IsNullOrWhiteSpace(result)
                        ? "Không nhận được trạng thái Preview."
                        : result.Replace("\r", " ").Replace("\n", " "));
            }
            catch (Exception ex)
            {
                Trace.WriteLine("[AFJ-PMP] Show bodies error: " + ex);
                if (a != null && b != null && DefinitelyDifferent(a, b))
                    StoreAnalysisResult(a, b, "Lỗi hiển thị A/B — xem Output.");
                else
                    ClearAnalysisResult();
            }
            finally
            {
                handlingShowBodies = false;
                UpdateReadyState();
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
            // Cancel queued UI work before releasing native PMP controls.
            DisposeDeferredFocusWindow();

            page = null;
            selectionA = null;
            selectionB = null;
            analyzeButtonControl = null;
            showBodiesButtonControl = null;
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
            if (id == ButtonAnalyze)
                PerformAnalyze();
            else if (id == ButtonShowBodies)
                PerformShowBodies();
        }
        public void OnTextboxChanged(int id, string value) { }
        public void OnNumberboxChanged(int id, double value) { }
        public void OnComboboxEditChanged(int id, string value) { }
        public void OnComboboxSelectionChanged(int id, int item) { }
        public void OnListboxSelectionChanged(int id, int item) { }
        public void OnSelectionboxFocusChanged(int id)
        {
            if (id == BoxFrameA || id == BoxFrameB)
                Trace.WriteLine("[AFJ-SEL] Focus box=" + id);
        }

        public void OnSelectionboxListChanged(int id, int count)
        {
            if (id != BoxFrameA && id != BoxFrameB) return;

            // This callback runs DURING the SOLIDWORKS selection process.
            // Don't read selection COM state or change focus/selection here.
            // Multiple events are coalesced into one deferred UI refresh.
            Trace.WriteLine("[AFJ-SEL] List box=" + id + " count=" + count);
            QueueSelectionRefresh(id == BoxFrameA && count == 1);
        }
        public void OnSelectionboxCalloutCreated(int id) { }
        public void OnSelectionboxCalloutDestroyed(int id) { }

        /// <summary>
        /// Accept the owning Part occurrence from a face, edge, or direct
        /// component pick. SOLIDWORKS normally promotes combined filters to
        /// a Component; IEntity.GetComponent is a defensive fallback when it
        /// reports a Face/Edge in assembly context.
        /// </summary>
        public bool OnSubmitSelection(int id, object selection, int type,
                                      ref string itemText)
        {
            long started = Stopwatch.GetTimestamp();
            try
            {
                if (id != BoxFrameA && id != BoxFrameB)
                    return false;

                if (type != (int)swSelectType_e.swSelCOMPONENTS &&
                    type != (int)swSelectType_e.swSelFACES &&
                    type != (int)swSelectType_e.swSelEDGES)
                    return false;

                if (!IsSameDocumentActive())
                    return false;

                Component2 candidate = selection as Component2;
                if (candidate == null &&
                    (type == (int)swSelectType_e.swSelFACES ||
                     type == (int)swSelectType_e.swSelEDGES))
                {
                    IEntity entity = selection as IEntity;
                    if (entity != null)
                        candidate = entity.GetComponent() as Component2;
                }
                if (candidate == null)
                {
                    Trace.WriteLine("[AFJ-SEL] No owning Component for type=" + type);
                    return false;
                }

                Component2 other = Selected(
                    id == BoxFrameA ? MarkFrameB : MarkFrameA);
                if (other != null && !DefinitelyDifferent(candidate, other))
                {
                    Trace.WriteLine("[AFJ-SEL] Rejected duplicate A/B");
                    return false;
                }

                itemText = candidate.Name2;
                return true;
            }
            catch (Exception ex)
            {
                Trace.WriteLine("[AFJ-SEL] Selection rejected: " + ex.Message);
                return false;
            }
            finally
            {
                double elapsedMs = (Stopwatch.GetTimestamp() - started) *
                                   1000.0 / Stopwatch.Frequency;
                if (elapsedMs >= 50.0)
                    Trace.WriteLine("[AFJ-SEL] SLOW OnSubmitSelection " +
                                    elapsedMs.ToString("F1") + "ms type=" +
                                    type + " box=" + id);
            }
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
