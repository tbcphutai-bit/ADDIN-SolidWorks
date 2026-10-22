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
    /// No Part cut features, geometry edits, configuration switching or saves.
    /// Read-only analyzer and parametric KEEP / KEEP-CUT previews belong to backend.
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
        private const int ButtonFinish = 106; // Explicit exit; does NOT apply a cut.
        private const int ButtonCutP1 = 107;
        private const int ButtonCutP2 = 108;
        private const int GroupInformation = 110;
        private const int LabelInformation = 111;
        private const int GroupGeometry = 120;
        private const int LabelAngle = 121;
        private const int LabelPoint = 122;
        private const int LabelGap = 123;
        private const int LabelJointStatus = 124;
        private const int LabelRole = 125;

        // Distinct powers of two; do not use 0 or the same mark for both boxes.
        private const int MarkFrameA = 1;
        private const int MarkFrameB = 2;

        private readonly ISldWorks app;
        private readonly ModelDoc2 assembly;
        private readonly Func<Component2, Component2, string> onAnalyze;
        private readonly Func<Component2, Component2, string> onShowBodies;
        private readonly Func<Component2, Component2, int, string> onPreviewCut;
        private readonly Action onClearPreview;
        private readonly Action<AutoFrameJointSelectionPage> onClosed;

        private PropertyManagerPage2 page;
        private PropertyManagerPageSelectionbox selectionA;
        private PropertyManagerPageSelectionbox selectionB;
        private PropertyManagerPageControl analyzeButtonControl;
        private PropertyManagerPageControl showBodiesButtonControl;
        private PropertyManagerPageControl finishButtonControl;
        private PropertyManagerPageControl cutP1ButtonControl;
        private PropertyManagerPageControl cutP2ButtonControl;
        private PropertyManagerPageLabel statusLabel;
        private PropertyManagerPageLabel geometryAngleLabel;
        private PropertyManagerPageLabel geometryPointLabel;
        private PropertyManagerPageLabel geometryGapLabel;
        private PropertyManagerPageLabel geometryStatusLabel;
        private PropertyManagerPageLabel geometryRoleLabel;

        // R50: native UI work requires activation, the owner thread and a live epoch.
        private readonly int ownerThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
        private int pageEpoch;
        private volatile bool nativeActivated;
        private volatile bool controlsFaulted;
        private bool showInProgress;
        private bool readyRefreshQueued;
        private bool backendActionQueued;
        private bool initialFocusApplied;
        private bool closeNotified;
        private readonly System.Collections.Generic.Dictionary<int, string> cachedStatusText =
            new System.Collections.Generic.Dictionary<int, string>();

        private bool messageUpdatesUnavailable;
        private string lastPublishedMessage;

        private volatile bool shown;
        private volatile bool closing;
        private bool handlingAnalyze;
        private bool handlingShowBodies;
        private bool handlingCutPreview;
        private bool updatingReadyState;
        // A lightweight WinForms HWND schedules focus AFTER the native selection callback.
        // OnSelectionboxListChanged must never change SOLIDWORKS selection/focus inline.
        private System.Windows.Forms.Control deferredFocusWindow;
        private bool selectionRefreshQueued;
        private bool requestFocusB;
        private bool requestAutoAnalysis;
        private bool finishQueued, finishRequested, finishCloseIssued;
        private int selectionCallbackDepth, flowSequence, lastFocusBox;
        private int callbackCountA = -1, callbackCountB = -1, deferredCountA = -1, deferredCountB = -1;
        private Component2 committedA, committedB;
        private string committedNameA, committedNameB;

        // Keep analysis output while the same two component occurrences remain
        // selected. Do not use Name2 as an occurrence identity.
        private Component2 analyzedA;
        private Component2 analyzedB;
        private string lastAnalysisMessage;
        private string lastPreviewMessage;
        private string[] lastGeometryLines;

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
            Func<Component2, Component2, int, string> onPreviewCut,
            Action onClearPreview,
            Action<AutoFrameJointSelectionPage> onClosed)
        {
            this.app = app;
            this.onAnalyze = onAnalyze;
            this.onShowBodies = onShowBodies;
            this.onPreviewCut = onPreviewCut;
            this.onClearPreview = onClearPreview;
            this.onClosed = onClosed;
            this.assembly = app == null ? null : app.ActiveDoc as ModelDoc2;
        }

        public bool IsOpen { get { return shown && !closing; } }

        public void Show()
        {
            if (shown) return;
            if (closing)
                throw new InvalidOperationException("Trang đã đóng; hãy tạo phiên Auto Frame Joint mới.");
            if (System.Threading.Thread.CurrentThread.ManagedThreadId != ownerThreadId)
                throw new InvalidOperationException("Auto Frame Joint phải mở trên thread UI đã tạo trang.");
            if (assembly == null || assembly.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
                throw new InvalidOperationException("Hãy mở Assembly trước khi dùng Auto Frame Joint.");

            System.Threading.Interlocked.Increment(ref pageEpoch);
            nativeActivated = false;
            controlsFaulted = false;
            cachedStatusText.Clear();
            messageUpdatesUnavailable = false;
            lastPublishedMessage = null;
            int error = 0;
            const int options =
                (int)swPropertyManagerPageOptions_e.swPropertyManagerOptions_OkayButton |
                (int)swPropertyManagerPageOptions_e.swPropertyManagerOptions_CancelButton;
            try
            {
                page = app.CreatePropertyManagerPage("AUTO FRAME JOINT", options, this, ref error)
                    as PropertyManagerPage2;
                if (page == null || error != (int)swPropertyManagerPageStatus_e.swPropertyManagerPage_Okay)
                    throw new InvalidOperationException("Không tạo được PropertyManager Page. Mã lỗi: " + error);
                BuildControls();
                InitializeDeferredFocusWindow();
                if (deferredFocusWindow == null)
                    throw new InvalidOperationException("Không tạo được bộ điều phối UI. Hãy mở lại lệnh.");
                shown = true;
                showInProgress = true;
                int status;
                try { status = page.Show2(0); }
                finally { showInProgress = false; }
                if (status != (int)swPropertyManagerPageStatus_e.swPropertyManagerPage_Okay)
                    throw new InvalidOperationException("Không hiển thị được PropertyManager. Mã lỗi: " + status);
                Trace.WriteLine("[AFJ-R52-CAPTION] status_channel=PAGE_MESSAGE no_runtime_label_writes=True");
                TraceFlow("SHOW", "source=R52 MVID=" + typeof(AutoFrameJointSelectionPage).Assembly.ManifestModule.ModuleVersionId + " preview_callback=" + (onPreviewCut != null));
                Trace.WriteLine("[AFJ-R50] SHOW epoch=" + pageEpoch + " owner=" + ownerThreadId);
                UpdateReadyState();
                QueueInitialFocus();
            }
            catch
            {
                closing = true;
                shown = false;
                nativeActivated = false;
                showInProgress = false;
                System.Threading.Interlocked.Increment(ref pageEpoch);
                DisposeDeferredFocusWindow();
                throw;
            }
        }

        /// <summary>
        /// Creates a handle on the CURRENT SOLIDWORKS UI thread, so BeginInvoke
        /// posts work to the same UI thread. If setup fails, Show aborts
        /// instead of running backend work inside a native callback.
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
        private void QueueSelectionRefresh(bool advanceFromA, bool analyzeAfterB)
        {
            TraceFlow("SELECTION_REFRESH_REQUEST", "advance=" + advanceFromA + " analyze=" + analyzeAfterB);
            if (!IsUiLive(pageEpoch) || finishRequested) { TraceFlow("SELECTION_REFRESH_REJECT", "page state/owner/finish"); return; }
            requestFocusB |= advanceFromA;
            requestAutoAnalysis |= analyzeAfterB;
            if (selectionRefreshQueued) { TraceFlow("SELECTION_REFRESH_MERGED", "already queued"); return; }
            selectionRefreshQueued = true;
            if (!PostUi("selection refresh", delegate
            {
                selectionRefreshQueued = false;
                TraceFlow("SELECTION_REFRESH_RUN", "deferred");
                if (finishRequested) { TryQueueFinish(); return; }
                if (IsBackendBusy()) { TraceFlow("SELECTION_REFRESH_DEFER", "backend busy"); return; }
                UpdateReadyState();
                bool analyze = requestAutoAnalysis, advance = requestFocusB;
                requestAutoAnalysis = false; requestFocusB = false;
                if (analyze) QueueAutoAnalysis();
                if (!advance || selectionA == null || selectionB == null) { TraceFlow("AUTO_FOCUS_SKIP", "not requested or no controls"); return; }
                int epoch = pageEpoch;
                bool focusA = selectionA.GetSelectionFocus();
                Component2 a = IsUiLive(epoch) && IsSameDocumentActive() ? Selected(MarkFrameA) : null;
                Component2 b = IsUiLive(epoch) ? Selected(MarkFrameB) : null;
                if (focusA && a != null && b == null && IsUiLive(epoch))
                {
                    TraceFlow("AUTO_FOCUS_BEGIN", "A->B; this does not select Frame B");
                    selectionB.SetSelectionFocus();
                    if (IsUiLive(epoch)) TraceFlow("AUTO_FOCUS_END", "B_focus=" + selectionB.GetSelectionFocus() + "; awaiting B submit/list callback");
                }
                else TraceFlow("AUTO_FOCUS_SKIP", "focusA=" + focusA + " hasA=" + (a != null) + " hasB=" + (b != null));
            })) selectionRefreshQueued = false;
        }

        /// <summary>
        /// Automatic read-only analysis after both selections are committed.
        /// No background COM and no call inside OnSelectionboxListChanged.
        /// The user can still press Analyze manually to repeat the probe.
        /// </summary>
        private void QueueAutoAnalysis()
        {
            if (!IsUiLive(pageEpoch) || finishRequested) return;
            if (IsBackendBusy() || backendActionQueued)
            {
                requestAutoAnalysis = true;
                return;
            }
            QueueBackendAction(0, 0, true);
        }

        /// <summary>
        /// Finish from a PropertyManager button without closing the native page
        /// synchronously inside SOLIDWORKS OnButtonPress. The standard green
        /// OK and red Cancel corner buttons remain usable if posting fails.
        /// Both paths close READ ONLY; OnClose/AfterClose owns cleanup.
        /// </summary>
        private void QueueFinishPage()
        {
            TraceFlow("FINISH_REQUEST", "busy=" + IsBackendBusy() + " backendQueued=" + backendActionQueued);
            if (!IsUiLive(pageEpoch) || finishCloseIssued) return;
            finishRequested = true;
            requestAutoAnalysis = false;
            requestFocusB = false;
            TryQueueFinish();
        }

        private void TryQueueFinish()
        {
            if (!finishRequested || finishQueued || finishCloseIssued || !IsUiLive(pageEpoch)) return;
            if (IsBackendBusy() || backendActionQueued)
            {
                TraceFlow("FINISH_WAIT", "request retained until backend returns; no native close inline");
                return;
            }
            finishQueued = true;
            if (!PostUi("finish", delegate
            {
                finishQueued = false;
                if (!finishRequested || finishCloseIssued) return;
                if (IsBackendBusy() || backendActionQueued)
                {
                    TraceFlow("FINISH_WAIT", "backend became busy; request retained");
                    return;
                }
                if (page == null) return;
                finishCloseIssued = true;
                var timer = Stopwatch.StartNew();
                try
                {
                    TraceFlow("FINISH_CLOSE_BEGIN", "close only; no Part cut");
                    page.Close(true);
                    TraceFlow("FINISH_CLOSE_RETURN", "ms=" + timer.ElapsedMilliseconds + " closing=" + closing);
                }
                catch (Exception ex)
                {
                    finishCloseIssued = false;
                    finishRequested = false;
                    TraceUiFault("finish close", ex, false);
                }
            })) finishQueued = false;
        }

        private void DisposeDeferredFocusWindow()
        {
            System.Windows.Forms.Control dispatcher = deferredFocusWindow;
            deferredFocusWindow = null;
            readyRefreshQueued = false;
            backendActionQueued = false;
            selectionRefreshQueued = false;
            requestFocusB = false;
            requestAutoAnalysis = false;
            finishQueued = false; finishRequested = false; finishCloseIssued = false;
            if (dispatcher == null) return;
            try { dispatcher.Dispose(); }
            catch (Exception ex) { TraceUiFault("dispatcher dispose", ex, false); }
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

            cutP1ButtonControl = group.AddControl2(
                ButtonCutP1,
                (short)swPropertyManagerPageControlType_e.swControlType_Button,
                "Preview A+B — KEEP", align, controlOptions,
                "Frame ngắn che, khe hở 0,1 mm; ngoài phạm vi đã kiểm chứng sẽ dừng.")
                as PropertyManagerPageControl;
            cutP2ButtonControl = group.AddControl2(
                ButtonCutP2,
                (short)swPropertyManagerPageControlType_e.swControlType_Button,
                "Preview A+B — KEEP/CUT", align, controlOptions,
                "Cùng rule KEEP, thêm phần CUT màu hồng/tím; chỉ trên Body tạm.")
                as PropertyManagerPageControl;

            finishButtonControl = group.AddControl2(
                ButtonFinish,
                (short)swPropertyManagerPageControlType_e.swControlType_Button,
                "Kết thúc (không cắt Part)", align, controlOptions,
                "Đóng Auto Frame Joint và dọn Preview. Không lưu, không sửa Part.")
                as PropertyManagerPageControl;

            statusLabel = group.AddControl2(
                LabelStatus,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "Chọn Frame A/B; xem kết quả trong Thông báo.", align, controlOptions,
                "Trạng thái chọn Frame.")
                as PropertyManagerPageLabel;

            if (selectionA == null || selectionB == null ||
                analyzeButtonControl == null || showBodiesButtonControl == null ||
                finishButtonControl == null || cutP1ButtonControl == null ||
                cutP2ButtonControl == null || statusLabel == null)
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

            PropertyManagerPageGroup geometryGroup = page.AddGroupBox(
                GroupGeometry, "Tính giao góc 3D (ước lượng)", groupOptions)
                as PropertyManagerPageGroup;
            if (geometryGroup != null)
            {
                geometryAngleLabel = geometryGroup.AddControl2(
                    LabelAngle,
                    (short)swPropertyManagerPageControlType_e.swControlType_Label,
                    "Góc hai trục: xem Thông báo", align, controlOptions,
                    "Góc NHỌN giữa 2 trục vô hướng, không phải góc trong.")
                    as PropertyManagerPageLabel;
                geometryPointLabel = geometryGroup.AddControl2(
                    LabelPoint,
                    (short)swPropertyManagerPageControlType_e.swControlType_Label,
                    "XYZ gần nhất: xem Thông báo", align, controlOptions,
                    "Trung điểm giữa hai trục được ước lượng trong Assembly.")
                    as PropertyManagerPageLabel;
                geometryGapLabel = geometryGroup.AddControl2(
                    LabelGap,
                    (short)swPropertyManagerPageControlType_e.swControlType_Label,
                    "Khoảng cách trục: xem Thông báo", align, controlOptions,
                    "Không phải khe hở vật liệu; Web/Wall/Lip chưa được xử lý.")
                    as PropertyManagerPageLabel;
                geometryStatusLabel = geometryGroup.AddControl2(
                    LabelJointStatus,
                    (short)swPropertyManagerPageControlType_e.swControlType_Label,
                    "Trạng thái: xem Thông báo", align, controlOptions,
                    "Chỉ tính toán hình học tham chiếu.")
                    as PropertyManagerPageLabel;
                geometryRoleLabel = geometryGroup.AddControl2(
                    LabelRole,
                    (short)swPropertyManagerPageControlType_e.swControlType_Label,
                    "Dài/ngắn: xem Thông báo", align, controlOptions,
                    "KCC 26003 chỉ áp dụng khi được xác minh; bản vẽ riêng ưu tiên.")
                    as PropertyManagerPageLabel;
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

        /// <summary>
        /// SelectionBoxes prefer Component2, but an Assembly Face/Edge can
        /// also be present in SelectionMgr on some SOLIDWORKS versions.
        /// Always resolve its owning Component occurrence, never its Part
        /// document or an unmarked selection from another box.
        /// </summary>
        private Component2 Selected(int mark)
        {
            RequireDeferredSelectionRead();
            if (assembly == null) { TraceFlow("SELECTION_READ_REJECT", "no bound assembly"); return null; }
            SelectionMgr mgr = assembly.SelectionManager as SelectionMgr;
            if (mgr == null) { TraceFlow("SELECTION_READ_REJECT", "no SelectionMgr"); return null; }
            int count = mgr.GetSelectedObjectCount2(mark);
            Component2 component = null;
            string route = "COUNT_NOT_ONE";
            if (count == 1)
            {
                component = mgr.GetSelectedObjectsComponent4(1, mark) as Component2;
                route = "GET_SELECTED_COMPONENT";
                if (component == null)
                {
                    object selected = mgr.GetSelectedObject6(1, mark);
                    component = selected as Component2;
                    if (component == null)
                    {
                        IEntity entity = selected as IEntity;
                        if (entity != null) component = entity.GetComponent() as Component2;
                        route = "ENTITY_OWNER";
                    }
                    else route = "DIRECT_COMPONENT";
                }
            }
            string name = DeferredComponentName(component);
            if (mark == MarkFrameA) { committedA = component; committedNameA = name; deferredCountA = count; }
            else if (mark == MarkFrameB) { committedB = component; committedNameB = name; deferredCountB = count; }
            TraceFlow("SELECTION_READ", "mark=" + mark + " count=" + count + " resolved=" + (component != null) + " route=" + route + " component=" + name);
            return component;
        }

        private void TraceSelectionState(bool ready, string reason)
        {
            RequireDeferredSelectionRead();
            Trace.WriteLine("[AFJ-SEL] READY=" + ready + " A-count=" + deferredCountA + " B-count=" + deferredCountB + " reason=" + reason);
            TraceFlow("READY", "ready=" + ready + " deferredCounts=" + deferredCountA + "/" + deferredCountB +
                " A=" + committedNameA + " B=" + committedNameB + " reason=" + reason);
        }

        /// <summary>
        /// When deepCheck=false, only query selection marks and COM identities.
        /// Reading Part documents belongs to the Analyze button, never
        /// OnSelectionboxListChanged (which occurs during SOLIDWORKS selection).
        /// </summary>
        private bool TryReadSelection(out Component2 a, out Component2 b,
                                      out string reason, bool deepCheck)
        {
            RequireDeferredSelectionRead();
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
            lastPreviewMessage = null;
            lastGeometryLines = null;
        }

        private void StoreAnalysisResult(Component2 a, Component2 b, string message)
        {
            lastPreviewMessage = null;
            analyzedA = a;
            analyzedB = b;
            if (String.IsNullOrEmpty(message))
            {
                lastAnalysisMessage = message;
                return;
            }

            // ANALYZE callback supplies a short status + five geometric
            // information lines. Keep these details visible even after
            // an explicit Ghost preview updates the main status text.
            string[] lines = message.Replace("\r", "").Split('\n');
            lastAnalysisMessage = lines[0];
            if (lines.Length >= 6 &&
                lines[0].StartsWith("Đã tính giao góc", StringComparison.Ordinal))
                lastGeometryLines = lines;
        }

        private void UpdateGeometryLabels()
        {
            int epoch = pageEpoch;
            string[] lines = lastGeometryLines;
            if (lines != null && lines.Length < 6) lines = null;
            if (!CacheStatusText(LabelAngle, lines == null ? "Góc hai trục: chưa tính" : lines[1], epoch)) return;
            if (!CacheStatusText(LabelPoint, lines == null ? "XYZ gần nhất: chưa tính" : lines[2], epoch)) return;
            if (!CacheStatusText(LabelGap, lines == null ? "Khoảng cách trục: chưa tính" : lines[3], epoch)) return;
            if (!CacheStatusText(LabelJointStatus, lines == null ? "Trạng thái: chưa tính" : lines[4], epoch)) return;
            CacheStatusText(LabelRole, lines == null ? "Dài/ngắn: chưa tính" : lines[5], epoch);
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
            // Queue only. Never mutate native controls in a backend finally
            // or inside a SOLIDWORKS selection/button callback.
            if (!IsUiLive(pageEpoch) || readyRefreshQueued) return;
            readyRefreshQueued = true;
            if (!PostUi("ready refresh", delegate
            {
                readyRefreshQueued = false;
                if (!IsBackendBusy()) ApplyReadyState();
            })) readyRefreshQueued = false;
        }

        private void PerformAnalyze()
        {
            int epoch = pageEpoch;
            if (!IsUiLive(epoch) || IsBackendBusy()) return;
            Component2 a = null, b = null;
            handlingAnalyze = true;
            try
            {
                string reason;
                if (!TryReadSelection(out a, out b, out reason, true))
                {
                    if (IsUiLive(epoch)) { ClearAnalysisResult(); lastAnalysisMessage = reason; }
                    return;
                }
                if (!IsUiLive(epoch)) return;
                bool previousPair = IsPreviousPair(a, b);
                if (!IsUiLive(epoch)) return;
                if (!previousPair) ClearAnalysisResult();
                lastPreviewMessage = null;
                if (!CacheStatusText(LabelStatus, "Đang tính trục và giao góc 3D...", epoch)) return;
                if (!IsUiLive(epoch)) return;
                Trace.WriteLine("[AFJ-R50] BACKEND BEGIN Analyze epoch=" + epoch);
                string result = onAnalyze == null ? "Đã chọn 2 Frame. Chưa nối bộ phân tích." : onAnalyze(a, b);
                Trace.WriteLine("[AFJ-R50] BACKEND END Analyze epoch=" + epoch);
                if (!IsActionPairCurrent(a, b, epoch)) return;
                StoreAnalysisResult(a, b, String.IsNullOrWhiteSpace(result) ? "Không nhận được kết quả phân tích." : result);
            }
            catch (Exception ex)
            {
                // Logging and managed state only. Do not retry a native UI
                // object here, and do not opt into corrupted-state recovery.
                TraceUiFault("Analyze", ex, false);
                if (IsUiLive(epoch))
                {
                    if (a != null && b != null) StoreAnalysisResult(a, b, "Lỗi phân tích — xem Output.");
                    else ClearAnalysisResult();
                }
            }
            finally
            {
                handlingAnalyze = false;
                QueueAfterAction(epoch);
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
            int epoch = pageEpoch;
            if (!IsUiLive(epoch) || IsBackendBusy()) return;
            Component2 a = null, b = null;
            handlingShowBodies = true;
            try
            {
                string reason;
                if (!TryReadSelection(out a, out b, out reason, true))
                {
                    if (IsUiLive(epoch)) { ClearAnalysisResult(); lastAnalysisMessage = reason; }
                    return;
                }
                if (!IsUiLive(epoch)) return;
                bool previousPair = IsPreviousPair(a, b);
                if (!IsUiLive(epoch)) return;
                if (!previousPair) ClearAnalysisResult();
                lastPreviewMessage = null;
                if (!CacheStatusText(LabelStatus, "Đang tạo bản xem Frame A/B...", epoch)) return;
                if (!IsUiLive(epoch)) return;
                Trace.WriteLine("[AFJ-R50] BACKEND BEGIN Show A/B epoch=" + epoch);
                string result = onShowBodies == null ? "Chưa kết nối bộ hiển thị A/B." : onShowBodies(a, b);
                Trace.WriteLine("[AFJ-R50] BACKEND END Show A/B epoch=" + epoch);
                if (!IsActionPairCurrent(a, b, epoch)) return;
                StoreAnalysisResult(a, b, String.IsNullOrWhiteSpace(result) ? "Không nhận được trạng thái Preview." : result.Replace("\r", " ").Replace("\n", " "));
            }
            catch (Exception ex)
            {
                TraceUiFault("Show A/B", ex, false);
                if (IsUiLive(epoch))
                {
                    if (a != null && b != null) StoreAnalysisResult(a, b, "Lỗi hiển thị A/B — xem Output.");
                    else ClearAnalysisResult();
                }
            }
            finally
            {
                handlingShowBodies = false;
                QueueAfterAction(epoch);
            }
        }

        /// <summary>
        /// Post explicit PMP P1/P2 button work to the NEXT UI message.
        /// Never run potentially expensive B-rep COM Boolean INSIDE
        /// native SOLIDWORKS OnButtonPress. Same owner UI/STA thread.
        /// Logging precedes EVERY UI or backend gate so we can distinguish
        /// missing button callback, rejected selection, and Boolean errors.
        /// </summary>
        private void QueueCutCandidate(int candidate)
        {
            if (candidate != 1 && candidate != 2) return;
            QueueBackendAction(2, candidate, false);
        }

        /// <summary>
        /// Explicit P1/P2 temporary candidate on actual 3D B-rep.
        /// Does NOT imply production-approved Web/Wall/Lip trim.
        /// </summary>
        private void PerformCutCandidate(int candidate)
        {
            int epoch = pageEpoch;
            TraceFlow("PREVIEW_ACTION_ENTER", "candidate=" + candidate);
            if (!IsUiLive(epoch) || IsBackendBusy()) { TraceFlow("PREVIEW_ACTION_REJECT", "page/owner/busy"); return; }
            Component2 a = null, b = null;
            handlingCutPreview = true;
            try
            {
                string reason;
                if (!TryReadSelection(out a, out b, out reason, true))
                {
                    TraceFlow("PREVIEW_SELECTION_REJECT", reason);
                    if (IsUiLive(epoch)) { ClearAnalysisResult(); lastPreviewMessage = reason; }
                    return;
                }
                if (!IsUiLive(epoch)) return;
                bool previousPair = IsPreviousPair(a, b);
                if (!IsUiLive(epoch)) return;
                if (!previousPair) ClearAnalysisResult();
                if (!CacheStatusText(LabelStatus, "Đang tạo Preview cho hai Frame...", epoch)) return;
                if (!IsUiLive(epoch)) return;
                Trace.WriteLine("[AFJ-R50] BACKEND BEGIN Preview P" + candidate + " epoch=" + epoch);
                TraceFlow("PREVIEW_CALLBACK_BEGIN", "candidate=" + candidate + " bound=" + (onPreviewCut != null));
                string result = onPreviewCut == null ? "Chưa nối backend Preview." : onPreviewCut(a, b, candidate);
                TraceFlow("PREVIEW_CALLBACK_END", "candidate=" + candidate);
                Trace.WriteLine("[AFJ-R50] BACKEND END Preview P" + candidate + " epoch=" + epoch);
                if (!IsActionPairCurrent(a, b, epoch)) { TraceFlow("PREVIEW_RESULT_DROP", "page/pair changed"); return; }
                // Preserve the pair and geometric fields without flattening
                // a preview result into the analysis geometry cache.
                analyzedA = a;
                analyzedB = b;
                lastPreviewMessage = String.IsNullOrWhiteSpace(result) ? "Không nhận được kết quả Preview." : result;
            }
            catch (Exception ex)
            {
                TraceUiFault("Preview P" + candidate, ex, false);
                if (IsUiLive(epoch))
                {
                    analyzedA = a;
                    analyzedB = b;
                    lastPreviewMessage = "Lỗi Preview — xem Output.";
                }
            }
            finally
            {
                handlingCutPreview = false;
                QueueAfterAction(epoch);
            }
        }

        // ---------------- Native PMP callback methods ----------------
        public void AfterActivation()
        {
            if (!shown || closing || controlsFaulted) return;
            if (System.Threading.Thread.CurrentThread.ManagedThreadId != ownerThreadId)
            {
                controlsFaulted = true;
                Trace.WriteLine("[AFJ-R50] ACTIVATION rejected: owner thread mismatch");
                return;
            }
            nativeActivated = true;
            Trace.WriteLine("[AFJ-R50] ACTIVATED epoch=" + pageEpoch);
            UpdateReadyState();
            QueueInitialFocus();
        }

        public void OnClose(int reason)
        {
            // Invalidate queued work BEFORE any native controls are released.
            TraceFlow("CLOSE", "reason=" + reason);
            finishRequested = false; finishQueued = false;
            closing = true;
            nativeActivated = false;
            System.Threading.Interlocked.Increment(ref pageEpoch);
            Trace.WriteLine("[AFJ-R50] CLOSING reason=" + reason + " epoch=" + pageEpoch + "; no cut");
        }

        public void AfterClose()
        {
            if (closeNotified) return;
            closeNotified = true;
            TraceFlow("AFTER_CLOSE", "cleanup callback=" + (onClearPreview != null));
            var closeTimer = Stopwatch.StartNew();
            committedA = null; committedB = null; committedNameA = null; committedNameB = null;
            closing = true;
            shown = false;
            nativeActivated = false;
            System.Threading.Interlocked.Increment(ref pageEpoch);
            ClearAnalysisResult();
            cachedStatusText.Clear();
            DisposeDeferredFocusWindow();
            page = null;
            selectionA = null;
            selectionB = null;
            analyzeButtonControl = null;
            showBodiesButtonControl = null;
            finishButtonControl = null;
            cutP1ButtonControl = null;
            cutP2ButtonControl = null;
            statusLabel = null;
            geometryAngleLabel = null;
            geometryPointLabel = null;
            geometryGapLabel = null;
            geometryStatusLabel = null;
            geometryRoleLabel = null;
            try { if (onClearPreview != null) onClearPreview(); }
            catch (Exception ex) { TraceUiFault("preview cleanup", ex, false); }
            finally
            {
                try { if (onClosed != null) onClosed(this); }
                catch (Exception ex) { TraceUiFault("page closed callback", ex, false); }
                TraceFlow("AFTER_CLOSE_END", "cleanup/onClosed returned ms=" + closeTimer.ElapsedMilliseconds);
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
            TraceFlow("BUTTON", "id=" + id + " onPreviewCut_bound=" + (onPreviewCut != null));
            Trace.WriteLine("[AFJ-R50] BUTTON id=" + id + " epoch=" + pageEpoch);
            if (id == ButtonAnalyze) QueueBackendAction(0, 0, false);
            else if (id == ButtonShowBodies) QueueBackendAction(1, 0, false);
            else if (id == ButtonFinish) QueueFinishPage();
            else if (id == ButtonCutP1) QueueCutCandidate(1);
            else if (id == ButtonCutP2) QueueCutCandidate(2);
        }
        public void OnTextboxChanged(int id, string value) { }
        public void OnNumberboxChanged(int id, double value) { }
        public void OnComboboxEditChanged(int id, string value) { }
        public void OnComboboxSelectionChanged(int id, int item) { }
        public void OnListboxSelectionChanged(int id, int item) { }
        public void OnSelectionboxFocusChanged(int id)
        {
            selectionCallbackDepth++;
            try
            {
                TraceFlow("FOCUS_ENTER", "box=" + id);
                if (id == BoxFrameA || id == BoxFrameB) lastFocusBox = id;
                TraceFlow("FOCUS_EXIT", "box=" + id);
            }
            finally { selectionCallbackDepth--; }
        }

        public void OnSelectionboxListChanged(int id, int count)
        {
            selectionCallbackDepth++;
            try
            {
                TraceFlow("LIST_ENTER", "box=" + id + " count=" + count);
                if (id != BoxFrameA && id != BoxFrameB) { TraceFlow("LIST_REJECT", "unknown box"); return; }
                if (id == BoxFrameA) { callbackCountA = count; if (count != 1) { committedA = null; committedNameA = null; } }
                else { callbackCountB = count; if (count != 1) { committedB = null; committedNameB = null; } }
                QueueSelectionRefresh(id == BoxFrameA && count == 1, id == BoxFrameB && count == 1);
                TraceFlow("LIST_EXIT", "queued only; no COM selection read");
            }
            finally { selectionCallbackDepth--; }
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
            selectionCallbackDepth++;
            bool accepted = false;
            string reason = "REJECT_UNKNOWN";
            long started = Stopwatch.GetTimestamp();
            TraceFlow("SUBMIT_ENTER", "box=" + id + " type=" + type);
            try
            {
                if (id != BoxFrameA && id != BoxFrameB) { reason = "REJECT_BOX_ID"; return false; }
                if (!IsUiLive(pageEpoch)) { reason = "REJECT_PAGE_STATE_OR_OWNER"; return false; }
                if (selection == null) { reason = "REJECT_NULL_DISPATCH"; return false; }
                if (type != (int)swSelectType_e.swSelCOMPONENTS &&
                    type != (int)swSelectType_e.swSelFACES &&
                    type != (int)swSelectType_e.swSelEDGES)
                { reason = "REJECT_FILTER_TYPE"; return false; }

                // Only inspect the supplied dispatch interface. Do not read
                // SelectionMgr, Part documents, Name2 or native PMP controls.
                Component2 candidate = selection as Component2;
                if (candidate == null && !(selection is IEntity))
                { reason = "REJECT_NO_COMPONENT_OR_ENTITY_INTERFACE"; return false; }
                Component2 other = id == BoxFrameA ? committedB : committedA;
                if (candidate != null && Object.ReferenceEquals(candidate, other))
                { reason = "REJECT_DUPLICATE_CACHED_REFERENCE"; return false; }
                // Full occurrence/document identity is checked after commit,
                // then again before every action. Never clear/re-mark inline.
                if (String.IsNullOrWhiteSpace(itemText)) itemText = id == BoxFrameA ? "Frame A" : "Frame B";
                accepted = true;
                reason = "ACCEPT_FILTERED_PENDING_DEFERRED_IDENTITY";
                return true;
            }
            catch (Exception ex)
            {
                reason = "REJECT_EXCEPTION HRESULT=0x" + ex.HResult.ToString("X8");
                TraceFlow("SUBMIT_EXCEPTION", ex.GetType().FullName + " " + ex.Message);
                return false;
            }
            finally
            {
                double ms = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
                TraceFlow("SUBMIT_EXIT", "box=" + id + " type=" + type + " accepted=" + accepted +
                    " reason=" + reason + " ms=" + ms.ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
                selectionCallbackDepth--;
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

        // Managed-only callback diagnostics: no COM properties or native controls.
        private void TraceFlow(string stage, string detail)
        {
            Trace.WriteLine("[AFJ-R52] stage=" + stage + " seq=" + (++flowSequence) + " epoch=" + pageEpoch +
                " thread=" + System.Threading.Thread.CurrentThread.ManagedThreadId + " owner=" + ownerThreadId +
                " cbDepth=" + selectionCallbackDepth + " focusCache=" + lastFocusBox +
                " callbackCounts=" + callbackCountA + "/" + callbackCountB +
                " shown=" + shown + " closing=" + closing + " activated=" + nativeActivated +
                " faulted=" + controlsFaulted + " " + detail);
        }
        private void RequireDeferredSelectionRead()
        {
            if (selectionCallbackDepth != 0)
                throw new InvalidOperationException("R52: COM selection/control work attempted inside a selection callback");
        }
        private string DeferredComponentName(Component2 component)
        {
            RequireDeferredSelectionRead();
            if (component == null) return "NONE";
            try { return component.Name2; }
            catch (Exception ex) { TraceFlow("COMPONENT_NAME_UNAVAILABLE", "HRESULT=0x" + ex.HResult.ToString("X8")); return "UNAVAILABLE"; }
        }

        private bool IsBackendBusy()
        {
            return handlingAnalyze || handlingShowBodies || handlingCutPreview;
        }

        private bool IsUiLive(int epoch)
        {
            return epoch == System.Threading.Volatile.Read(ref pageEpoch) &&
                shown && !closing && nativeActivated && !showInProgress && !controlsFaulted &&
                System.Threading.Thread.CurrentThread.ManagedThreadId == ownerThreadId;
        }

        private bool PostUi(string operation, Action action)
        {
            int epoch = pageEpoch;
            if (!IsUiLive(epoch)) { TraceFlow("POST_REJECT", operation + " page/owner"); return false; }
            TraceFlow("POST_REQUEST", operation);
            System.Windows.Forms.Control dispatcher = deferredFocusWindow;
            if (dispatcher == null || dispatcher.IsDisposed || !dispatcher.IsHandleCreated)
            {
                Trace.WriteLine("[AFJ-R50] POST rejected: dispatcher unavailable " + operation);
                return false;
            }
            try
            {
                dispatcher.BeginInvoke((System.Windows.Forms.MethodInvoker)delegate
                {
                    if (!IsUiLive(epoch))
                    {
                        Trace.WriteLine("[AFJ-R50] DROP stale/inactive UI work " + operation + " epoch=" + epoch);
                        return;
                    }
                    try { RequireDeferredSelectionRead(); TraceFlow("POST_RUN", operation); action(); TraceFlow("POST_END", operation); }
                    catch (Exception ex) { TraceUiFault(operation, ex, true); }
                });
                return true;
            }
            catch (Exception ex)
            {
                TraceUiFault("post " + operation, ex, true);
                return false;
            }
        }

        private void TraceUiFault(string operation, Exception ex, bool blockControls)
        {
            // No COM calls, captions, Enabled writes or ReleaseComObject here.
            // This does not catch/recover native corrupted-state exceptions.
            if (blockControls)
            {
                controlsFaulted = true;
                readyRefreshQueued = false;
                requestAutoAnalysis = false;
                requestFocusB = false;
            }
            Trace.WriteLine("[AFJ-R50] FAULT " + operation + " epoch=" + pageEpoch +
                " owner=" + ownerThreadId + " thread=" + System.Threading.Thread.CurrentThread.ManagedThreadId +
                " HRESULT=0x" + ex.HResult.ToString("X8") + " " + ex.GetType().FullName + " " + ex.Message);
            Trace.WriteLine(ex.StackTrace);
        }

        private bool CacheStatusText(int id, string text, int epoch)
        {
            if (!IsUiLive(epoch) || selectionCallbackDepth != 0) return false;
            text = text ?? String.Empty;
            string previous;
            if (cachedStatusText.TryGetValue(id, out previous) && previous == text) return true;
            // Managed snapshot only. Never dereference a native label RCW.
            cachedStatusText[id] = text;
            Trace.WriteLine("[AFJ-R52-CAPTION] STATUS_CACHE id=" + id + " epoch=" + epoch + " text=" + text.Replace("\r", " ").Replace("\n", " | "));
            return true;
        }

        private string StatusMessageText()
        {
            string status;
            if (!cachedStatusText.TryGetValue(LabelStatus, out status)) return String.Empty;
            var text = new System.Text.StringBuilder(status);
            string[] geometry = lastGeometryLines;
            if (geometry != null && geometry.Length >= 6)
                for (int i = 1; i < 6; i++) text.AppendLine().Append(geometry[i]);
            return text.ToString();
        }

        private void PublishStatusMessage()
        {
            int epoch = pageEpoch;
            if (!IsUiLive(epoch) || selectionCallbackDepth != 0 || IsBackendBusy() ||
                page == null || messageUpdatesUnavailable) return;
            string text = StatusMessageText();
            if (String.IsNullOrEmpty(text) || text == lastPublishedMessage) return;
            try
            {
                Trace.WriteLine("[AFJ-R52-CAPTION] MESSAGE_BEGIN epoch=" + epoch + " chars=" + text.Length);
                bool accepted = page.SetMessage3(text,
                    (int)swPropertyManagerPageMessageVisibility.swMessageBoxVisible,
                    lastPublishedMessage == null
                        ? (int)swPropertyManagerPageMessageExpanded.swMessageBoxExpand
                        : (int)swPropertyManagerPageMessageExpanded.swMessageBoxMaintainExpandState,
                    "Auto Frame Joint — Trạng thái");
                Trace.WriteLine("[AFJ-R52-CAPTION] MESSAGE_END epoch=" + epoch + " accepted=" + accepted);
                if (!IsUiLive(epoch)) return;
                if (accepted) lastPublishedMessage = text;
                else messageUpdatesUnavailable = true;
            }
            catch (Exception ex)
            {
                // A normal COM failure disables only this optional channel.
                // No native-label fallback, retry or corrupted-state recovery.
                messageUpdatesUnavailable = true;
                Trace.WriteLine("[AFJ-R52-CAPTION] MESSAGE_UNAVAILABLE HRESULT=0x" + ex.HResult.ToString("X8") + " " + ex.GetType().FullName + " " + ex.Message);
            }
        }


        private void ApplyReadyState()
        {
            if (finishRequested) { TryQueueFinish(); return; }
            int epoch = pageEpoch;
            if (!IsUiLive(epoch) || IsBackendBusy() || updatingReadyState) return;
            updatingReadyState = true;
            try
            {
                // Buttons remain enabled from AddControl2. Do not call the
                // native IPropertyManagerPageControl.Enabled setter: it is the
                // crashing call in the supplied log/screenshot. Validate the
                // current selection again in every action instead.
                Component2 a, b;
                string reason;
                bool ready = TryReadSelection(out a, out b, out reason, false);
                if (!IsUiLive(epoch)) return;
                if (!IsSameDocumentActive()) return;
                if (!IsUiLive(epoch)) return;
                TraceSelectionState(ready, reason);
                if (!IsUiLive(epoch)) return;
                bool previousPair = ready && IsPreviousPair(a, b);
                if (!IsUiLive(epoch)) return;
                if (!previousPair) ClearAnalysisResult();
                string status = ready && !String.IsNullOrEmpty(lastPreviewMessage) ? lastPreviewMessage :
                    (ready && !String.IsNullOrEmpty(lastAnalysisMessage) ? lastAnalysisMessage : reason);
                if (!CacheStatusText(LabelStatus, status, epoch)) return;
                UpdateGeometryLabels();
                PublishStatusMessage();
            }
            catch (Exception ex) { TraceUiFault("ready refresh", ex, true); }
            finally { updatingReadyState = false; }
        }

        private bool IsActionPairCurrent(Component2 a, Component2 b, int epoch)
        {
            if (!IsUiLive(epoch)) return false;
            Component2 currentA, currentB;
            string reason;
            bool ready = TryReadSelection(out currentA, out currentB, out reason, false);
            if (!IsUiLive(epoch) || !ready) return false;
            bool sameA = ConclusivelySame(a, currentA);
            if (!IsUiLive(epoch) || !sameA) return false;
            bool sameB = ConclusivelySame(b, currentB);
            return IsUiLive(epoch) && sameB;
        }

        private void QueueAfterAction(int epoch)
        {
            if (!IsUiLive(epoch)) return;
            if (finishRequested) { TryQueueFinish(); return; }
            UpdateReadyState();
            if (requestAutoAnalysis || requestFocusB) QueueSelectionRefresh(false, false);
        }

        private void QueueBackendAction(int kind, int candidate, bool automatic)
        {
            TraceFlow("ACTION_REQUEST", "kind=" + kind + " candidate=" + candidate + " automatic=" + automatic);
            if (!IsUiLive(pageEpoch) || IsBackendBusy() || backendActionQueued || finishRequested || finishCloseIssued)
            { TraceFlow("ACTION_REJECT", "page/owner/busy/queued/finish gate"); return; }
            backendActionQueued = true;
            if (!PostUi("backend kind=" + kind, delegate
            {
                backendActionQueued = false;
                var timer = Stopwatch.StartNew();
                try
                {
                    TraceFlow("ACTION_RUN", "kind=" + kind + " candidate=" + candidate);
                    if (IsBackendBusy() || finishRequested || finishCloseIssued)
                    { TraceFlow("ACTION_REJECT", "busy/finish after posting"); return; }
                    int epoch = pageEpoch;
                    if (automatic)
                    {
                        Component2 a, b; string reason;
                        bool ready = TryReadSelection(out a, out b, out reason, false);
                        if (!IsUiLive(epoch) || !ready) { TraceFlow("AUTO_ANALYZE_SKIP", reason); return; }
                        bool previous = IsPreviousPair(a, b);
                        if (!IsUiLive(epoch)) return;
                        if (previous && !String.IsNullOrEmpty(lastAnalysisMessage)) { TraceFlow("AUTO_ANALYZE_SKIP", "same analyzed pair"); return; }
                    }
                    if (kind == 0) PerformAnalyze();
                    else if (kind == 1) PerformShowBodies();
                    else if (kind == 2) PerformCutCandidate(candidate);
                }
                finally
                {
                    TraceFlow("ACTION_END", "kind=" + kind + " candidate=" + candidate + " ms=" + timer.ElapsedMilliseconds);
                    TryQueueFinish();
                }
            })) { backendActionQueued = false; TraceFlow("ACTION_POST_FAILED", "kind=" + kind); TryQueueFinish(); }
        }

        private void QueueInitialFocus()
        {
            if (!IsUiLive(pageEpoch) || initialFocusApplied) return;
            PostUi("initial focus", delegate
            {
                if (finishRequested || initialFocusApplied || selectionA == null) return;
                initialFocusApplied = true;
                int epoch = pageEpoch;
                if (IsSameDocumentActive() && IsUiLive(epoch) &&
                    Selected(MarkFrameA) == null && IsUiLive(epoch) &&
                    Selected(MarkFrameB) == null && IsUiLive(epoch))
                    selectionA.SetSelectionFocus();
            });
        }

    }
}
